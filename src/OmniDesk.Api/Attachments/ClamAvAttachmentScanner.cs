using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace OmniDesk.Api.Attachments;

public enum AttachmentScanVerdict { Clean, Infected }

public interface IAttachmentScanner
{
    Task<AttachmentScanVerdict> ScanAsync(Stream content, CancellationToken ct);
}

public sealed class ClamAvAttachmentScanner(IConfiguration configuration) : IAttachmentScanner
{
    public async Task<AttachmentScanVerdict> ScanAsync(Stream content, CancellationToken ct)
    {
        var host = configuration["Attachments:ClamAvHost"];
        var port = configuration.GetValue("Attachments:ClamAvPort", 3310);
        if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
            throw new AttachmentScannerUnavailableException();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, deadline.Token);
        await using var socket = client.GetStream();
        await socket.WriteAsync("zINSTREAM\0"u8.ToArray(), deadline.Token);

        var buffer = new byte[64 * 1024];
        var length = new byte[4];
        while (true)
        {
            var read = await content.ReadAsync(buffer, deadline.Token);
            if (read == 0) break;
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)read);
            await socket.WriteAsync(length, deadline.Token);
            await socket.WriteAsync(buffer.AsMemory(0, read), deadline.Token);
        }
        BinaryPrimitives.WriteUInt32BigEndian(length, 0);
        await socket.WriteAsync(length, deadline.Token);
        await socket.FlushAsync(deadline.Token);

        using var response = new MemoryStream();
        var reply = new byte[256];
        var terminated = false;
        while (!terminated)
        {
            var read = await socket.ReadAsync(reply, deadline.Token);
            if (read == 0) break;
            var terminator = Array.IndexOf(reply, (byte)0, 0, read);
            var count = terminator >= 0 ? terminator : read;
            if (response.Length + count > 4096) throw new AttachmentScanProtocolException();
            response.Write(reply, 0, count);
            terminated = terminator >= 0;
        }
        if (!terminated) throw new AttachmentScanProtocolException();
        var text = Encoding.UTF8.GetString(response.ToArray()).Trim('\0', '\r', '\n', ' ');
        if (text.EndsWith(": OK", StringComparison.Ordinal)) return AttachmentScanVerdict.Clean;
        if (text.EndsWith("FOUND", StringComparison.Ordinal)) return AttachmentScanVerdict.Infected;
        throw new AttachmentScanProtocolException();
    }
}

public sealed class AttachmentScannerUnavailableException : Exception { }
public sealed class AttachmentScanProtocolException : Exception { }
