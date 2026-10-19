using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OmniDesk.Api.Data;
using OmniDesk.Api.Security;
namespace OmniDesk.Api.Endpoints;
public sealed record ReauthenticateRequest(string Password, string? Code = null);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword, string? Code = null);
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this WebApplication app)
    {
        var account = app.MapGroup("/api/auth").RequireAuthorization();
        account.MapPost("/password", async (ChangePasswordRequest r, HttpContext c, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) => {
            if (string.IsNullOrEmpty(r.NewPassword) || r.NewPassword.Length > 1024) return Results.BadRequest();
            var user = await users.GetUserAsync(c.User);
            if (user is null || !await Reauthenticate(users, user, r.CurrentPassword, r.Code, user.TwoFactorEnabled)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            var result = await users.ChangePasswordAsync(user, r.CurrentPassword, r.NewPassword);
            if (!result.Succeeded) return Results.BadRequest(new { errors = result.Errors.Select(x => x.Code) });
            db.AuditEvents.Add(Event(user, "auth.password.change")); await db.SaveChangesAsync(); await tx.CommitAsync();
            await signIn.RefreshSignInAsync(user); return Results.NoContent();
        });
        var totp = account.MapGroup("/totp").AddEndpointFilter(async (ctx, next) =>
            ctx.HttpContext.RequestServices.GetRequiredService<IConfiguration>().GetValue<bool>("Security:TotpEnabled") ? await next(ctx) : Results.StatusCode(503));
        totp.MapPost("/setup", async (ReauthenticateRequest r, HttpContext c, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) => {
            var user = await users.GetUserAsync(c.User); if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Results.Conflict();
            if (!await Reauthenticate(users, user, r.Password, null, false)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            Check(await users.ResetAuthenticatorKeyAsync(user));
            var key = await users.GetAuthenticatorKeyAsync(user) ?? throw new InvalidOperationException("Authenticator key unavailable");
            db.AuditEvents.Add(Event(user, "auth.totp.setup")); await db.SaveChangesAsync(); await tx.CommitAsync();
            await signIn.RefreshSignInAsync(user);
            var label = Uri.EscapeDataString("OmniDesk:" + user.Email);
            return Results.Ok(new { sharedKey = key, authenticatorUri = $"otpauth://totp/{label}?secret={Uri.EscapeDataString(key)}&issuer=OmniDesk&digits=6&period=30" });
        });
        totp.MapPost("/enable", async (ReauthenticateRequest r, HttpContext c, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) => {
            var user = await users.GetUserAsync(c.User); if (user is null) return Results.Unauthorized();
            if (user.TwoFactorEnabled) return Results.Conflict();
            if (!await Reauthenticate(users, user, r.Password, r.Code, true)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            Check(await users.SetTwoFactorEnabledAsync(user, true)); Check(await users.UpdateSecurityStampAsync(user));
            var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            db.AuditEvents.Add(Event(user, "auth.totp.enable")); await db.SaveChangesAsync(); await tx.CommitAsync();
            await signIn.RefreshSignInAsync(user); return Results.Ok(new { recoveryCodes });
        });
        totp.MapPost("/disable", async (ReauthenticateRequest r, HttpContext c, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) => {
            var user = await users.GetUserAsync(c.User); if (user is null) return Results.Unauthorized();
            if (!user.TwoFactorEnabled || !await Reauthenticate(users, user, r.Password, r.Code, true)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            Check(await users.SetTwoFactorEnabledAsync(user, false)); Check(await users.ResetAuthenticatorKeyAsync(user));
            await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0); Check(await users.UpdateSecurityStampAsync(user));
            db.AuditEvents.Add(Event(user, "auth.totp.disable")); await db.SaveChangesAsync(); await tx.CommitAsync();
            await signIn.RefreshSignInAsync(user); return Results.NoContent();
        });
        totp.MapPost("/recovery-codes", async (ReauthenticateRequest r, HttpContext c, UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db) => {
            var user = await users.GetUserAsync(c.User); if (user is null || !user.TwoFactorEnabled || !await Reauthenticate(users, user, r.Password, r.Code, true)) return Results.Unauthorized();
            await using var tx = await db.Database.BeginTransactionAsync();
            var recoveryCodes = await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            Check(await users.UpdateSecurityStampAsync(user));
            db.AuditEvents.Add(Event(user, "auth.totp.recovery.rotate")); await db.SaveChangesAsync(); await tx.CommitAsync();
            await signIn.RefreshSignInAsync(user); return Results.Ok(new { recoveryCodes });
        });
    }
    private static async Task<bool> Reauthenticate(UserManager<AppUser> users, AppUser user, string password, string? code, bool requireTotp)
    {
        if (user.IsDisabled || await users.IsLockedOutAsync(user) || string.IsNullOrEmpty(password) || password.Length > 1024) return false;
        var valid = await users.CheckPasswordAsync(user, password);
        if (valid && requireTotp) valid = code is { Length: <= 20 } && await users.VerifyTwoFactorTokenAsync(user, TokenOptions.DefaultAuthenticatorProvider, Normalize(code));
        if (!valid) { await users.AccessFailedAsync(user); return false; }
        await users.ResetAccessFailedCountAsync(user); return true;
    }
    public static string Normalize(string code) => code.Replace(" ", "").Replace("-", "");
    private static void Check(IdentityResult result) { if (!result.Succeeded) throw new InvalidOperationException("Identity update failed"); }
    private static AuditEvent Event(AppUser user, string action) => new() { ActorId = user.Id, Action = action, Target = user.Id };
}
