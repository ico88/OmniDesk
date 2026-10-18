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
    public static AgentIdentity? Load()
    {
        if (!File.Exists(ConfigPath)) return null;
        AssertTrusted(new DirectoryInfo(Folder)); AssertTrusted(new FileInfo(ConfigPath));
        return JsonSerializer.Deserialize<AgentIdentity>(File.ReadAllText(ConfigPath));
    }
    public void Save()
    {
        SecureDirectory(Folder);
        WriteTrusted(ConfigPath, JsonSerializer.Serialize(this));
    }
    private static readonly SecurityIdentifier SystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier AdminSid = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    internal static void SecureDirectory(string path)
    {
        var directory = Directory.CreateDirectory(path);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Agent directory cannot be a reparse point");
        var acl = new DirectorySecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(AdminSid);
        foreach (var sid in new[] { SystemSid, AdminSid }) acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        directory.SetAccessControl(acl);
    }
    internal static void AssertTrusted(FileSystemInfo info)
    {
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException("Untrusted agent reparse point");
        FileSystemSecurity acl = info is DirectoryInfo directory ? directory.GetAccessControl() : ((FileInfo)info).GetAccessControl();
        var owner = acl.GetOwner(typeof(SecurityIdentifier));
        if (!SystemSid.Equals(owner) && !AdminSid.Equals(owner)) throw new UnauthorizedAccessException("Untrusted agent file owner");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !SystemSid.Equals(rule.IdentityReference) && !AdminSid.Equals(rule.IdentityReference))
                throw new UnauthorizedAccessException("Untrusted agent file permissions");
    }
    internal static void WriteTrusted(string path, string content)
    {
        AssertTrusted(new DirectoryInfo(Path.GetDirectoryName(path)!));
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream)) writer.Write(content);
            var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(AdminSid);
            foreach (var sid in new[] { SystemSid, AdminSid }) acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(temporary).SetAccessControl(acl);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
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
