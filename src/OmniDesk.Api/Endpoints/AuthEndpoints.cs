using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
using OmniDesk.Api.ServiceDesk;

namespace OmniDesk.Api.Endpoints;
public sealed record LoginRequest(string Email, string Password, string? Code = null, string? RecoveryCode = null);
public sealed record ProfileRequest(string? DisplayName);
public sealed record EmailNotificationsRequest(bool Enabled);
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
        app.MapPost("/api/auth/login", async (LoginRequest r, SignInManager<AppUser> signIn, UserManager<AppUser> users, AppDbContext db) => {
            if (string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || string.IsNullOrEmpty(r.Password) || r.Password.Length > 1024) return Results.BadRequest();
            var user = await users.FindByEmailAsync(r.Email);
            var success = false;
            if (user is { IsDisabled: false })
            {
                var result = await signIn.CheckPasswordSignInAsync(user, r.Password, lockoutOnFailure: true);
                success = result.Succeeded;
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
}
