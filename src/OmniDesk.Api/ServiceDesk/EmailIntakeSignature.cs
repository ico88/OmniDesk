using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OmniDesk.Api.ServiceDesk;

public static class EmailIntakeSignature
{
    public static bool Verify(ReadOnlySpan<byte> secret, string? timestampHeader, string? signatureHeader,
        ReadOnlySpan<byte> body, DateTimeOffset now, out long timestamp)
    {
        timestamp = 0;
        if (secret.Length < 32 || body.Length > 64 * 1024 ||
            !long.TryParse(timestampHeader, NumberStyles.None, CultureInfo.InvariantCulture, out timestamp) ||
            timestamp < now.ToUnixTimeSeconds() - 300 || timestamp > now.ToUnixTimeSeconds() + 300 ||
            signatureHeader is null || !signatureHeader.StartsWith("sha256=", StringComparison.Ordinal) ||
            signatureHeader.Length != 7 + 64)
            return false;

        byte[] supplied;
        try { supplied = Convert.FromHexString(signatureHeader.AsSpan(7)); }
        catch (FormatException) { return false; }

        var prefix = Encoding.ASCII.GetBytes(timestampHeader + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        var expected = HMACSHA256.HashData(secret, signed);
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    public static string Sign(ReadOnlySpan<byte> secret, long timestamp, ReadOnlySpan<byte> body)
    {
        var prefix = Encoding.ASCII.GetBytes(timestamp.ToString(CultureInfo.InvariantCulture) + ".");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed.AsSpan(prefix.Length));
        return "sha256=" + Convert.ToHexString(HMACSHA256.HashData(secret, signed)).ToLowerInvariant();
    }
}
