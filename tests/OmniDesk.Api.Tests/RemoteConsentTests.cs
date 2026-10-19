using OmniDesk.Remote.MeshCentral;
using Xunit;
namespace OmniDesk.Api.Tests;
public class RemoteConsentTests
{
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
