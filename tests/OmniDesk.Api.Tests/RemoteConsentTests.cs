using OmniDesk.Remote.MeshCentral;
using Xunit;
namespace OmniDesk.Api.Tests;
public class RemoteConsentTests
{
    [Fact] public void Session_lifetime_defaults_to_the_shared_thirty_minute_setting()=>Assert.Equal(TimeSpan.FromMinutes(30),new MeshCentralClient(new MeshCentralOptions(null,null,null)).SessionLifetime);
    [Fact] public void Session_lifetime_uses_the_shared_runtime_setting()=>Assert.Equal(TimeSpan.FromMinutes(20),new MeshCentralClient(new MeshCentralOptions(null,null,null,20)).SessionLifetime);
    [Fact] public void Expired_mesh_share_falls_back_to_server_service_identity_and_saved_guest_name()
    {
        using var json=System.Text.Json.JsonDocument.Parse("[]");
        Assert.Equal(new MeshDisconnectIdentity("user//omnidesk-service","Federico Operatore"),
            MeshDisconnectIdentity.Resolve(json.RootElement,"expired-share","omnidesk-service","Federico Operatore"));
    }
    [Fact] public void Existing_mesh_share_uses_its_authoritative_identity()
    {
        using var json=System.Text.Json.JsonDocument.Parse("[{\"publicid\":\"share-1\",\"userid\":\"user//remote-owner\",\"guestName\":\"Recorded Operator\"}]");
        Assert.Equal(new MeshDisconnectIdentity("user//remote-owner","Recorded Operator"),
            MeshDisconnectIdentity.Resolve(json.RootElement,"share-1","omnidesk-service","Current Display Name"));
    }
    [Fact] public void Duplicate_mesh_share_ids_fail_closed()
    {
        using var json=System.Text.Json.JsonDocument.Parse("[{\"publicid\":\"share-1\"},{\"publicid\":\"share-1\"}]");
        Assert.Throws<InvalidOperationException>(()=>MeshDisconnectIdentity.Resolve(json.RootElement,"share-1","omnidesk-service","Operator"));
    }
    [Theory]
    [InlineData("remote.control",true,73)]
    [InlineData("terminal",true,18)]
    [InlineData("files",true,36)]
    [InlineData("remote.control",false,65)]
    [InlineData("terminal",false,2)]
    [InlineData("files",false,4)]
    public void KeepsNotificationAndSelectsOnlyRequiredPrompt(string capability,bool required,int expected)
        =>Assert.Equal(expected,MeshCentralClient.SessionConsent(capability,required));
    [Fact] public void UnknownCapabilityFailsClosed()=>Assert.Throws<ArgumentException>(()=>MeshCentralClient.SessionConsent("administration",false));
}
