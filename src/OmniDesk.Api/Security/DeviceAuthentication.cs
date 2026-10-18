using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OmniDesk.Api.Data;

namespace OmniDesk.Api.Security;
// Explicit endpoint marker: only machine endpoints are exempt from browser CSRF.
public sealed record MachineRequest;
public sealed class DeviceAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, AppDbContext db) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DeviceJwt";
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return AuthenticateResult.NoResult();
        var token = header[7..];
        if (token.Length > 8192) return AuthenticateResult.Fail("Invalid device assertion");
        try
        {
            var handler = new JsonWebTokenHandler();
            var untrusted = handler.ReadJsonWebToken(token);
            if (!Guid.TryParse(untrusted.Subject, out var id)) return AuthenticateResult.Fail("Invalid device assertion");
            var credential = await db.DeviceCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.DeviceId == id && x.RevokedAt == null);
            if (credential is null) return AuthenticateResult.Fail("Invalid device assertion");
            using var rsa = RSA.Create(); rsa.ImportFromPem(credential.PublicKeyPem);
            var result = await handler.ValidateTokenAsync(token, new TokenValidationParameters {
                ValidateIssuerSigningKey = true, IssuerSigningKey = new RsaSecurityKey(rsa) { CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false } },
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ValidateIssuer = true, ValidIssuer = id.ToString(),
                ValidateAudience = true, ValidAudience = "omnidesk-agent", ValidateLifetime = true,
                RequireExpirationTime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.FromSeconds(10)
            });
            if (!result.IsValid) return AuthenticateResult.Fail($"Invalid device assertion ({result.Exception?.GetType().Name})");
            var jwt = (JsonWebToken)result.SecurityToken;
            var now = DateTimeOffset.UtcNow;
            if (!Guid.TryParse(jwt.Id, out var jti) || !jwt.TryGetPayloadValue<long>("iat", out var issued) ||
                issued > now.ToUnixTimeSeconds() + 10 || issued < now.ToUnixTimeSeconds() - 100 ||
                jwt.ValidTo > now.UtcDateTime.AddSeconds(100) || jwt.ValidTo <= jwt.IssuedAt || jwt.ValidTo - jwt.IssuedAt > TimeSpan.FromSeconds(90))
                return AuthenticateResult.Fail("Invalid device assertion");
            db.DeviceAssertions.Add(new DeviceAssertion { Id = jti, ExpiresAt = new DateTimeOffset(jwt.ValidTo) });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" })
            { return AuthenticateResult.Fail("Replayed device assertion"); }
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], SchemeName));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
        }
        catch (Exception e) when (e is ArgumentException or CryptographicException or SecurityTokenException or FormatException)
        { return AuthenticateResult.Fail("Invalid device assertion"); }
    }
}
public sealed class AssertionCleanup(IServiceScopeFactory scopes, ILogger<AssertionCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.DeviceAssertions.Where(x => x.ExpiresAt < DateTimeOffset.UtcNow.AddMinutes(-1)).ExecuteDeleteAsync(stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogWarning("Device assertion cleanup failed: {Type}", e.GetType().Name); }
        }
    }
}
