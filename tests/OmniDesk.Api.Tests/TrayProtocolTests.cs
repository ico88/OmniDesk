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
    public async Task Support_methods_require_their_payload_and_reject_status_payloads()
    {
        var ticket = new SupportCreate(Guid.NewGuid(), "Subject", "Description", new string('A',5000), true);
        var request = new TrayRequest(TrayProtocol.Version,Guid.NewGuid(),TrayProtocol.CreateTicketMethod,ticket);
        Assert.True(TrayProtocol.Valid(request));
        Assert.False(TrayProtocol.Valid(request with { Ticket = null }));
        Assert.False(TrayProtocol.Valid(request with { Method = TrayProtocol.StatusMethod }));
        Assert.False(TrayProtocol.Valid(request with { Method = TrayProtocol.ListTicketsMethod }));
        using var stream = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => TrayProtocol.Write(stream,request,CancellationToken.None));
        await TrayProtocol.Write(stream,request,CancellationToken.None,TrayProtocol.MaxSupportPayload); stream.Position=0;
        Assert.Equal(request,await TrayProtocol.Read<TrayRequest>(stream,CancellationToken.None,TrayProtocol.MaxSupportPayload));
        var conversation = new TrayRequest(TrayProtocol.Version, Guid.NewGuid(), TrayProtocol.TicketConversationMethod, TicketId: Guid.NewGuid());
        Assert.True(TrayProtocol.Valid(conversation));
        Assert.False(TrayProtocol.Valid(conversation with { TicketId = null }));
        Assert.False(TrayProtocol.Valid(conversation with { Body = "extra" }));
        var reply = new TrayRequest(TrayProtocol.Version, Guid.NewGuid(), TrayProtocol.ReplyToTicketMethod,
            TicketId: Guid.NewGuid(), ExpectedVersion: 1, Body: "Serve ancora aiuto");
        Assert.True(TrayProtocol.Valid(reply));
        Assert.False(TrayProtocol.Valid(reply with { ExpectedVersion = 0 }));
        Assert.False(TrayProtocol.Valid(reply with { Body = " " }));
    }
    [Fact]
    public async Task Nested_duplicate_fields_are_rejected()
    {
        var body = Encoding.UTF8.GetBytes("{\"ticket\":{\"subject\":\"one\",\"Subject\":\"two\"}}");
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,body.Length);
        using var stream = new MemoryStream();stream.Write(header);stream.Write(body);stream.Position=0;
        await Assert.ThrowsAsync<InvalidDataException>(()=>TrayProtocol.Read<TrayRequest>(stream,CancellationToken.None));
    }
    [Fact]
    public async Task Truncated_frames_are_not_accepted()
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header, 100);
        using var stream = new MemoryStream(header);
        await Assert.ThrowsAsync<EndOfStreamException>(() => TrayProtocol.Read<TrayRequest>(stream, CancellationToken.None));
    }
}
