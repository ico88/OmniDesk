using OmniDesk.Contracts;
using Xunit;
namespace OmniDesk.Api.Tests;
public sealed class RemoteIdentityTests
{
    [Fact]
    public void Native_hex_key_hash_maps_to_server_identifier()
    {
        Assert.Equal("node//"+new string('A',64),RemoteAgentPackage.NormalizeNodeId(new string('0',96)));
        var bytes=Enumerable.Range(0,48).Select(i=>(byte)i).ToArray();
        var expected="node//"+Convert.ToBase64String(bytes).Replace('+','@').Replace('/','$');
        Assert.Equal(expected,RemoteAgentPackage.NormalizeNodeId(Convert.ToHexString(bytes)));
        Assert.Equal(expected,RemoteAgentPackage.NormalizeNodeId(expected[6..]));
    }
    [Theory]
    [InlineData("")][InlineData("node//other")][InlineData("../invalid")][InlineData("STOP")]
    public void Invalid_native_output_is_not_a_mapping(string value)=>Assert.Throws<InvalidDataException>(()=>RemoteAgentPackage.NormalizeNodeId(value));
}
