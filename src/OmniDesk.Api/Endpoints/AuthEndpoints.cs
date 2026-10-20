using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;
public sealed record LoginRequest(string Email, string Password, string? Code = null, string? RecoveryCode = null);
public sealed record EmailLoginCodeRequest(string ChallengeToken, string Code);
public sealed record EmailLoginResendRequest(string ChallengeToken);
public sealed record ProfileRequest(string? DisplayName);
public sealed record EmailNotificationsRequest(bool Enabled);
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
        app.MapPost("/api/auth/login", async (LoginRequest r, SignInManager<AppUser> signIn, UserManager<AppUser> users,
            IPasswordHasher<AppUser> hasher, IOutboundEmailSender emailSender, IOptions<OutboundEmailOptions> email, AppDbContext db, ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) => {
            if (string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || string.IsNullOrEmpty(r.Password) || r.Password.Length > 1024) return Results.BadRequest();
            var user = await users.FindByEmailAsync(r.Email);
            var success = false;
            if (user is { IsDisabled: false })
            {
                var result = await signIn.CheckPasswordSignInAsync(user, r.Password, lockoutOnFailure: true);
                success = result.Succeeded;
                if (success && app.Configuration.GetValue<bool>("Security:EmailTwoFactorEnabled"))
                {
                    if (string.IsNullOrWhiteSpace(user.Email) || !MailAddress.TryCreate(user.Email, out _))
                    {
                        db.AuditEvents.Add(LoginChallengeEvent(user, "delivery_failed"));
                        await db.SaveChangesAsync(cancellationToken);
                        return Results.Unauthorized();
                    }

                    var now = DateTimeOffset.UtcNow;
                    await db.EmailLoginChallenges.Where(x => x.ExpiresAt < now.AddDays(-1)).ExecuteDeleteAsync(cancellationToken);
                    var recentlyIssued = await db.EmailLoginChallenges.AnyAsync(x => x.UserId == user.Id && x.CreatedAt > now.AddMinutes(-1) && x.UsedAt == null, cancellationToken);
                    if (recentlyIssued)
                    {
                        db.AuditEvents.Add(LoginChallengeEvent(user, "rate_limited"));
                        await db.SaveChangesAsync(cancellationToken);
                        return Results.StatusCode(StatusCodes.Status429TooManyRequests);
                    }

                    await db.EmailLoginChallenges.Where(x => x.UserId == user.Id && x.UsedAt == null)
                        .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), cancellationToken);
                    var challengeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                    var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                    var challenge = new EmailLoginChallenge
                    {
                        UserId = user.Id,
                        TokenHash = HashChallengeToken(challengeToken),
                        CodeHash = hasher.HashPassword(user, code),
                        SecurityStamp = user.SecurityStamp ?? "",
                        CreatedAt = now,
                        LastSentAt = now,
                        ExpiresAt = now.AddMinutes(10),
                        SendCount = 1
                    };
                    db.EmailLoginChallenges.Add(challenge);
                    await db.SaveChangesAsync(cancellationToken);
                    try
                    {
                        await emailSender.SendAsync(new OutboundEmailMessage(user.Email, "Codice di accesso OmniDesk",
                            $"Il codice temporaneo per accedere a OmniDesk è: {code}\n\nScade tra 10 minuti. Se non hai richiesto l'accesso, ignora questo messaggio.",
                            $"login-{challenge.Id:N}@omnidesk.invalid"), cancellationToken);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        challenge.UsedAt = DateTimeOffset.UtcNow;
                        db.AuditEvents.Add(LoginChallengeEvent(user, "delivery_failed"));
                        await db.SaveChangesAsync(cancellationToken);
                        loggerFactory.CreateLogger("OmniDesk.Auth.EmailTwoFactor").LogWarning("Login code email delivery failed ({Type}).", exception.GetType().Name);
                        return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                    }
                    db.AuditEvents.Add(LoginChallengeEvent(user, "sent"));
                    await db.SaveChangesAsync(cancellationToken);
                    return Results.Accepted(value: new { requiresTwoFactor = true, challengeToken, maskedEmail = MaskEmail(user.Email), expiresInSeconds = 600 });
                }
                if (success && user.TwoFactorEnabled)
                {
                    success = false;
                    if (app.Configuration.GetValue<bool>("Security:TotpEnabled"))
                    {
                        if (r.Code is { Length: > 0 and <= 20 }) success = await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, AccountEndpoints.Normalize(r.Code));
                        else if (r.RecoveryCode is { Length: > 0 and <= 100 }) success = (await users.RedeemTwoFactorRecoveryCodeAsync(user, r.RecoveryCode.Trim())).Succeeded;
                    }
                    if (success) await users.ResetAccessFailedCountAsync(user); else await users.AccessFailedAsync(user);
                }
            }
            db.AuditEvents.Add(new AuditEvent { ActorId = user?.Id, Action = "auth.login", Target = user?.Id ?? "unknown", Outcome = success ? "success" : "denied" });
            await db.SaveChangesAsync();
            if (!success) return Results.Unauthorized();
            await signIn.SignInAsync(user!, isPersistent: false);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/login/email-code", async (EmailLoginCodeRequest request, HttpContext context, SignInManager<AppUser> signIn,
            UserManager<AppUser> users, IPasswordHasher<AppUser> hasher, AppDbContext db, CancellationToken cancellationToken) => {
            if (request.ChallengeToken is not { Length: 64 } || !request.ChallengeToken.All(Uri.IsHexDigit) || request.Code is not { Length: 6 } || !request.Code.All(char.IsAsciiDigit))
                return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var tokenHash = HashChallengeToken(request.ChallengeToken);
            var challenge = await db.EmailLoginChallenges.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
            if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt <= now || challenge.FailedAttempts >= 5)
                return Results.Unauthorized();
            var user = await users.FindByIdAsync(challenge.UserId);
            if (user is null || user.IsDisabled || await users.IsLockedOutAsync(user) || !string.Equals(user.SecurityStamp, challenge.SecurityStamp, StringComparison.Ordinal))
            {
                await db.EmailLoginChallenges.Where(x => x.Id == challenge.Id && x.UsedAt == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), cancellationToken);
                return Results.Unauthorized();
            }
            var verified = hasher.VerifyHashedPassword(user, challenge.CodeHash, request.Code) is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
            if (!verified)
            {
                await db.EmailLoginChallenges.Where(x => x.Id == challenge.Id && x.UsedAt == null && x.ExpiresAt > now && x.FailedAttempts < 5)
                    .ExecuteUpdateAsync(update => update
                        .SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1)
                        .SetProperty(x => x.UsedAt, x => x.FailedAttempts >= 4 ? now : x.UsedAt), cancellationToken);
                await users.AccessFailedAsync(user);
                db.AuditEvents.Add(LoginChallengeEvent(user, "code_denied"));
                await db.SaveChangesAsync(cancellationToken);
                return Results.Unauthorized();
            }
            var consumed = await db.EmailLoginChallenges.Where(x => x.Id == challenge.Id && x.UsedAt == null && x.ExpiresAt > now &&
                    x.FailedAttempts < 5 && x.SecurityStamp == user.SecurityStamp && x.CodeHash == challenge.CodeHash)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), cancellationToken);
            if (consumed != 1) return Results.Unauthorized();
            await users.ResetAccessFailedCountAsync(user);
            db.AuditEvents.Add(new AuditEvent { ActorId = user.Id, Action = "auth.login", Target = user.Id, Outcome = "success" });
            await db.SaveChangesAsync(cancellationToken);
            await signIn.SignInAsync(user, isPersistent: false);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/login/email-code/resend", async (EmailLoginResendRequest request, IPasswordHasher<AppUser> hasher,
            UserManager<AppUser> users, IOutboundEmailSender emailSender, AppDbContext db, ILoggerFactory loggerFactory, CancellationToken cancellationToken) => {
            if (request.ChallengeToken is not { Length: 64 } || !request.ChallengeToken.All(Uri.IsHexDigit)) return Results.Unauthorized();
            var now = DateTimeOffset.UtcNow;
            var tokenHash = HashChallengeToken(request.ChallengeToken);
            var challenge = await db.EmailLoginChallenges.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
            if (challenge is null || challenge.UsedAt is not null || challenge.ExpiresAt <= now || challenge.FailedAttempts >= 5 || challenge.SendCount >= 3 || challenge.LastSentAt > now.AddMinutes(-1))
                return Results.Unauthorized();
            var user = await users.FindByIdAsync(challenge.UserId);
            if (user is null || user.IsDisabled || await users.IsLockedOutAsync(user) || !string.Equals(user.SecurityStamp, challenge.SecurityStamp, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(user.Email) || !MailAddress.TryCreate(user.Email, out _)) return Results.Unauthorized();
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
            var oldSentAt = challenge.LastSentAt;
            var codeHash = hasher.HashPassword(user, code);
            var updated = await db.EmailLoginChallenges.Where(x => x.Id == challenge.Id && x.UsedAt == null && x.ExpiresAt > now &&
                    x.FailedAttempts < 5 && x.SendCount < 3 && x.LastSentAt == oldSentAt)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.CodeHash, codeHash)
                    .SetProperty(x => x.LastSentAt, now).SetProperty(x => x.ExpiresAt, now.AddMinutes(10))
                    .SetProperty(x => x.SendCount, x => x.SendCount + 1), cancellationToken);
            if (updated != 1) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            try
            {
                await emailSender.SendAsync(new OutboundEmailMessage(user.Email, "Nuovo codice di accesso OmniDesk",
                    $"Il nuovo codice temporaneo per accedere a OmniDesk è: {code}\n\nScade tra 10 minuti. Se non hai richiesto l'accesso, ignora questo messaggio.",
                    $"login-resend-{challenge.Id:N}-{challenge.SendCount + 1}@omnidesk.invalid"), cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await db.EmailLoginChallenges.Where(x => x.Id == challenge.Id && x.UsedAt == null)
                    .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, DateTimeOffset.UtcNow), cancellationToken);
                db.AuditEvents.Add(LoginChallengeEvent(user, "delivery_failed"));
                await db.SaveChangesAsync(cancellationToken);
                loggerFactory.CreateLogger("OmniDesk.Auth.EmailTwoFactor").LogWarning("Login code resend failed ({Type}).", exception.GetType().Name);
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }
            db.AuditEvents.Add(LoginChallengeEvent(user, "resent"));
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        }).RequireRateLimiting("login");
        app.MapPost("/api/auth/logout", async (HttpContext c, SignInManager<AppUser> signIn, AppDbContext db) => {
            db.AuditEvents.Add(new AuditEvent { ActorId = c.User.UserId(), Action = "auth.logout", Target = c.User.UserId() });
            await db.SaveChangesAsync();
            await signIn.SignOutAsync();
            return Results.NoContent();
        }).RequireAuthorization();
        app.MapGet("/api/auth/me", async (HttpContext c, UserManager<AppUser> users, PermissionService permissions) => {
            var user = await users.GetUserAsync(c.User);
            var outbound = app.Configuration.GetSection(OutboundEmailOptions.SectionName).Get<OutboundEmailOptions>() ?? new OutboundEmailOptions();
            return user is null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.Email, user.DisplayName, user.EmailNotificationsEnabled, emailNotificationsAvailable = outbound.Enabled && outbound.IsValid() && !string.IsNullOrWhiteSpace(user.Email), globalPermissions = await permissions.Global(user.Id), twoFactorAvailable = app.Configuration.GetValue<bool>("Security:TotpEnabled"), twoFactorEnabled = user.TwoFactorEnabled });
        }).RequireAuthorization();
        app.MapPut("/api/auth/email-notifications", async (EmailNotificationsRequest request, HttpContext c,
            UserManager<AppUser> users, AppDbContext db, IOptions<OutboundEmailOptions> email) => {
            var user = await users.GetUserAsync(c.User);
            if (user is null) return Results.Unauthorized();
            if (request.Enabled && (!email.Value.Enabled || string.IsNullOrWhiteSpace(user.Email)))
                return Results.BadRequest(new { error = "Email notifications require an account email address and server email configuration." });
            user.EmailNotificationsEnabled = request.Enabled;
            user.EmailNotificationsEnabledAt = request.Enabled ? DateTimeOffset.UtcNow : null;
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded) return Results.Problem("Email notification preference update failed", statusCode: 500);
            db.AuditEvents.Add(new AuditEvent { ActorId = user.Id, Action = request.Enabled ? "user.email_notifications.enabled" : "user.email_notifications.disabled", Target = user.Id });
            await db.SaveChangesAsync();
            return Results.Ok(new { user.EmailNotificationsEnabled });
        }).RequireAuthorization();
        app.MapPut("/api/auth/profile", async (ProfileRequest request, HttpContext c, UserManager<AppUser> users) => {
            var value = request.DisplayName?.Trim();
            if (value is { Length: > 80 } || value is not null && value.Any(char.IsControl)) return Results.BadRequest();
            var user = await users.GetUserAsync(c.User);
            if (user is null) return Results.Unauthorized();
            user.DisplayName = string.IsNullOrWhiteSpace(value) ? null : value;
            var result = await users.UpdateAsync(user);
            return result.Succeeded ? Results.Ok(new { user.DisplayName }) : Results.Problem("Profile update failed", statusCode: 500);
        }).RequireAuthorization();
    }
    private static string HashChallengeToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AuditEvent LoginChallengeEvent(AppUser user, string outcome) => new() { ActorId = user.Id, Action = "auth.login.email_2fa", Target = user.Id, Outcome = outcome };
    private static string MaskEmail(string email)
    {
        var separator = email.LastIndexOf('@');
        if (separator <= 0) return "***";
        var name = email[..separator];
        return (name.Length == 1 ? "*" : name[..1] + "***") + email[separator..];
    }
}
