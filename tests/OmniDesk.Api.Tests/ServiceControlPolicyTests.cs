using OmniDesk.Contracts;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class ServiceControlPolicyTests
{
    [Theory]
    [InlineData("Spooler")]
    [InlineData("BITS")]
    [InlineData("Vendor.Agent-2")]
    public void Allows_plain_service_names(string name) => Assert.True(ServiceControlPolicy.IsControllable(name));

    [Theory]
    [InlineData("")]
    [InlineData("../../cmd")]
    [InlineData("service name")]
    [InlineData("OmniDeskAgent")]
    [InlineData("RpcSs")]
    [InlineData("WinDefend")]
    public void Rejects_invalid_or_protected_services(string name) => Assert.False(ServiceControlPolicy.IsControllable(name));

    [Theory]
    [InlineData("start")]
    [InlineData("stop")]
    [InlineData("restart")]
    public void Allows_only_explicit_actions(string action) => Assert.True(ServiceControlPolicy.IsValidAction(action));

    [Theory]
    [InlineData("delete")]
    [InlineData("pause")]
    [InlineData("Start")]
    public void Rejects_unsupported_actions(string action) => Assert.False(ServiceControlPolicy.IsValidAction(action));
}
