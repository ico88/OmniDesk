using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class TrayProtocolTests
{
    [Fact]
    public async Task Status_request_round_trip_is_correlated_and_contains_no_commands()
    {
        using var stream = new MemoryStream();
        var request = new TrayRequest(TrayProtocol.Version, Guid.NewGuid(), TrayProtocol.StatusMethod);
        await TrayProtocol.Write(stream, request, CancellationToken.None); stream.Position = 0;
        var result = await TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None);
        Assert.Equal(request, result); Assert.True(TrayProtocol.Valid(result));
        Assert.False(TrayProtocol.Valid(result with { Method = "StopService" }));
        Assert.False(TrayProtocol.Valid(result with { Version = 99 }));
        Assert.False(TrayProtocol.Valid(result with { RequestId = Guid.Empty }));
    }
    [Theory]
    [InlineData(-1)] [InlineData(0)] [InlineData(4097)] [InlineData(int.MaxValue)]
    public async Task Invalid_lengths_are_rejected_before_allocation(int size)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, size);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<InvalidDataException>(() => TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None));
    }
    [Theory]
    [InlineData("{\"version\":1,\"requestId\":\"11111111-1111-1111-1111-111111111111\",\"method\":\"GetConnectionStatus\",\"command\":\"StopService\"}", true)]
    [InlineData("{\"version\":1,\"Version\":2}", false)]
    public async Task Unknown_and_duplicate_fields_are_rejected(string json, bool unknown)
    {
        var body = Encoding.UTF8.GetBytes(json); var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        using var stream = new MemoryStream(); stream.Write(header); stream.Write(body); stream.Position = 0;
        if (unknown) await Assert.ThrowsAsync<JsonException>(() => TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None));
        else await Assert.ThrowsAsync<InvalidDataException>(() => TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None));
    }
    [Fact]
    public async Task Truncated_frames_are_not_accepted()
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, 100);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<EndOfStreamException>(() => TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None));
    }
}
