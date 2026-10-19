using System.Text.Json;
using OmniDesk.Remote.MeshCentral;
using Xunit;

namespace OmniDesk.Api.Tests;

public class MeshNodeResponseTests
{
    [Theory]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//expected\",\"conn\":1}]}}",true)]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//expected\",\"conn\":3}]}}",true)]
    [InlineData("{\"nodes\":{\"mesh//other\":[{\"_id\":\"node//expected\",\"conn\":1}]}}",false)]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//other\",\"conn\":1}]}}",false)]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//expected\",\"conn\":0}]}}",false)]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//expected\",\"conn\":1,\"meshid\":\"mesh//other\"}]}}",false)]
    [InlineData("{\"nodes\":{\"mesh//group\":[{\"_id\":\"node//expected\",\"conn\":1},{\"_id\":\"node//other\",\"conn\":1}]}}",false)]
    [InlineData("{\"nodes\":{\"mesh//group\":[]}}",false)]
    public void RequiresUniqueOnlineNodeInExactGroup(string json,bool expected)
    {
        using var doc=JsonDocument.Parse(json);
        Assert.Equal(expected,MeshCentralClient.MatchesOnlineNode(doc.RootElement,"mesh//group","node//expected"));
    }
}
