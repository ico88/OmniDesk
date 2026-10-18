using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using OmniDesk.Api.Data;
namespace OmniDesk.Api.Security;
// Encrypt authenticator secrets and recovery-code values at the persistence boundary.
public sealed class ProtectedUserStore(AppDbContext db, IDataProtectionProvider provider) : UserStore<AppUser, IdentityRole, AppDbContext>(db)
{
    private readonly IDataProtector protector = provider.CreateProtector("OmniDesk.IdentityTokens.v1");
    public override Task SetTokenAsync(AppUser user, string loginProvider, string name, string? value, CancellationToken cancellationToken = default) =>
        base.SetTokenAsync(user, loginProvider, name, value is null ? null : protector.Protect(value), cancellationToken);
    public override async Task<string?> GetTokenAsync(AppUser user, string loginProvider, string name, CancellationToken cancellationToken = default)
    {
        var value = await base.GetTokenAsync(user, loginProvider, name, cancellationToken);
        return value is null ? null : protector.Unprotect(value);
    }
}
