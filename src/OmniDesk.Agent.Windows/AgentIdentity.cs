using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace OmniDesk.Agent.Windows;
public sealed record AgentIdentity(string ServerUrl, Guid DeviceId, string EncryptedPrivateKey)
{
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OmniDesk");
    public static string ConfigPath => Path.Combine(Folder, "agent.json");
    public static Uri ValidateServer(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("Server must be an HTTPS origin without credentials, path, query or fragment.");
        return uri;
    }
    public static AgentIdentity? Load() => File.Exists(ConfigPath) ? JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllText(ConfigPath)) : null;
    public void Save()
    {
        var directory = Directory.CreateDirectory(Folder);
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this)); File.Move(temporary, ConfigPath, true);
    }
    public static AgentIdentity Create(string server, Guid id, RSA rsa) => new(ValidateServer(server).AbsoluteUri, id,
        Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(rsa.ExportPkcs8PrivateKeyPem()), null, DataProtectionScope.LocalMachine)));
    public string Assertion()
    {
        using var rsa = RSA.Create();
        var plaintext = ProtectedData.Unprotect(Convert.FromBase64String(EncryptedPrivateKey), null, DataProtectionScope.LocalMachine);
        try { rsa.ImportFromPem(Encoding.UTF8.GetString(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        var now = DateTime.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor {
            Issuer = DeviceId.ToString(), Audience = "omnidesk-agent", IssuedAt = now, NotBefore = now.AddSeconds(-5), Expires = now.AddSeconds(60),
            Claims = new Dictionary<string, object> { ["sub"] = DeviceId.ToString(), ["jti"] = Guid.NewGuid().ToString() },
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } }, SecurityAlgorithms.RsaSha256)
        });
    }
}
