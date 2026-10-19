using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public static class ActiveAccountCookie
{
    public static async Task Validate(CookieValidatePrincipalContext context)
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated != true) return;
        var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
        var options = context.HttpContext.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Value;
        var user = await users.GetUserAsync(context.Principal);
        var stamp = context.Principal.FindFirst(options.ClaimsIdentity.SecurityStampClaimType)?.Value;
        // Validate on every request, including before the periodic Identity validator runs.
        if (user is null || user.IsDisabled || string.IsNullOrEmpty(stamp) || stamp != user.SecurityStamp)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}
