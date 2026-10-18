using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;

namespace OmniDesk.Api.Endpoints;
public sealed record LoginRequest(string Email, string Password, string? Code = null, string? RecoveryCode = null);
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext c, IAntiforgery a) => Results.Ok(new { token = a.GetAndStoreTokens(c).RequestToken }));
        app.MapPost("/api/auth/login", async (LoginRequest r, SignInManager<AppUser> signIn, UserManager<AppUser> users, AppDbContext db) => {
            if (string.IsNullOrWhiteSpace(r.Email) || r.Email.Length > 254 || string.IsNullOrEmpty(r.Password) || r.Password.Length > 1024) return Results.BadRequest();
            var user = await users.FindByEmailAsync(r.Email);
            var success = false;
            if (user is not null)
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
            return user is null ? Results.Unauthorized() : Results.Ok(new { user.Id, user.Email, globalPermissions = await permissions.Global(user.Id), twoFactorAvailable = app.Configuration.GetValue<bool>("Security:TotpEnabled"), twoFactorEnabled = user.TwoFactorEnabled });
        }).RequireAuthorization();
    }
}
