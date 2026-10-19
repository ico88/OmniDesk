using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using OmniDesk.Api.Attachments;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class ClamAvAttachmentScannerTests
{
    [Theory]
    [InlineData("stream: OK\0", AttachmentScanVerdict.Clean)]
    [InlineData("stream: Eicar-Test FOUND\0", AttachmentScanVerdict.Infected)]
    public async Task Sends_bounded_instream_chunks_and_parses_terminated_response(string response, AttachmentScanVerdict expected)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var content = Enumerable.Range(0, 100_000).Select(i => (byte)(i % 251)).ToArray();
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var command = new List<byte>();
            while (command.Count == 0 || command[^1] != 0)
            {
                var b = new byte[1];
                await stream.ReadExactlyAsync(b);
                command.Add(b[0]);
            }
            Assert.Equal("zINSTREAM\0", System.Text.Encoding.ASCII.GetString(command.ToArray()));
            using var received = new MemoryStream();
            var length = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(length);
                var count = BinaryPrimitives.ReadUInt32BigEndian(length);
                if (count == 0) break;
                Assert.InRange(count, 1u, 64u * 1024u);
                var chunk = new byte[checked((int)count)];
                await stream.ReadExactlyAsync(chunk);
                received.Write(chunk);
            }
            Assert.Equal(content, received.ToArray());
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response));
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Attachments:ClamAvHost"] = "127.0.0.1",
            ["Attachments:ClamAvPort"] = port.ToString()
        }).Build();
        var scanner = new ClamAvAttachmentScanner(config);
        Assert.Equal(expected, await scanner.ScanAsync(new MemoryStream(content), CancellationToken.None));
        await server;
    }

    [Fact]
    public async Task Rejects_unterminated_or_unknown_scanner_response()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var command = new byte[10];
            await stream.ReadExactlyAsync(command);
            var length = new byte[4];
            await stream.ReadExactlyAsync(length);
            var count = BinaryPrimitives.ReadUInt32BigEndian(length);
            var chunk = new byte[checked((int)count)];
            await stream.ReadExactlyAsync(chunk);
            await stream.ReadExactlyAsync(length);
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("stream: ERROR\0"));
        });
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Attachments:ClamAvHost"] = "127.0.0.1",
            ["Attachments:ClamAvPort"] = port.ToString()
        }).Build();
        await Assert.ThrowsAsync<AttachmentScanProtocolException>(() =>
            new ClamAvAttachmentScanner(config).ScanAsync(new MemoryStream([1, 2, 3]), CancellationToken.None));
        await server;
    }
}
