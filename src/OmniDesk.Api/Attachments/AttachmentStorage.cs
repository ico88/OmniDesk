using System.Security.Cryptography;

namespace OmniDesk.Api.Attachments;

public sealed class AttachmentStorage(IConfiguration configuration)
{
    public const long MaxBytes = 10 * 1024 * 1024;
    private readonly string? root = NormalizeRoot(configuration["Attachments:RootPath"]);

    public async Task<(string StorageKey, long SizeBytes, string Sha256)> SaveQuarantinedAsync(Stream source, CancellationToken ct)
    {
        var directory = RequireRoot();
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var key = Guid.NewGuid().ToString("N");
        var path = GetPath(key);
        long size = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        try
        {
            await using var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                buffer.Length, FileOptions.Asynchronous | FileOptions.WriteThrough);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            while (true)
            {
                var read = await source.ReadAsync(buffer, ct);
                if (read == 0) break;
                size += read;
                if (size > MaxBytes) throw new AttachmentTooLargeException();
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            if (size == 0) throw new InvalidDataException("Empty attachment.");
            await destination.FlushAsync(ct);
            destination.Flush(flushToDisk: true);
            return (key, size, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public FileStream OpenRead(string storageKey) => new(GetPath(storageKey), FileMode.Open, FileAccess.Read,
        FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void Delete(string storageKey) => TryDelete(GetPath(storageKey));

    public void DeleteForRetention(string storageKey)
    {
        var directory = RequireRoot();
        if (!Directory.Exists(directory)) throw new AttachmentStorageUnavailableException();
        var path = GetPath(storageKey);
        File.Delete(path);
        if (File.Exists(path)) throw new IOException("Attachment could not be removed.");
    }

    private string GetPath(string storageKey)
    {
        if (storageKey.Length != 32 || storageKey.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid attachment storage key.");
        return Path.Combine(RequireRoot(), storageKey.ToLowerInvariant());
    }

    private string RequireRoot() => root ?? throw new AttachmentStorageUnavailableException();

    private static string? NormalizeRoot(string? value) => string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}

public sealed class AttachmentTooLargeException : Exception { }
public sealed class AttachmentStorageUnavailableException : Exception { }
