using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;

public static class RemoteOperatorName
{
    public static string Resolve(AppUser? user)
    {
        if (!string.IsNullOrWhiteSpace(user?.DisplayName)) return user.DisplayName;
        if (!string.IsNullOrWhiteSpace(user?.Email)) return user.Email;
        return "Operatore OmniDesk";
    }
}
