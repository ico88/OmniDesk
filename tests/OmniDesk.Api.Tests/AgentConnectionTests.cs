using System.Net;
using OmniDesk.Agent.Windows;
using Xunit;

namespace OmniDesk.Api.Tests;

public sealed class AgentConnectionTests
{
    [Fact]
    public async Task Lan_origin_preserves_IP_port_and_checks_readiness_without_credentials()
    {
        using var handler = new FixtureHandler(HttpStatusCode.OK, "{\"status\":\"ready\"}");
        using var client = new HttpClient(handler);
        var server = AgentConnection.ValidateServer("https://192.168.1.233:8085");
        await AgentConnection.CheckReady(client, server);
        Assert.Equal("https://192.168.1.233:8085/api/health/ready", handler.Target);
        Assert.False(handler.HasAuthorization);
    }

    [Theory]
    [InlineData("http://192.168.1.233:8085")]
    [InlineData("https://user:password@192.168.1.233:8085")]
    [InlineData("https://192.168.1.233:8085/api")]
    [InlineData("https://192.168.1.233:8085?token=secret")]
    [InlineData("https://192.168.1.233:8085#fragment")]
    public void Unsafe_server_origins_are_rejected(string server) => Assert.Throws<ArgumentException>(() => AgentConnection.ValidateServer(server));

    [Theory]
    [InlineData(302, "")]
    [InlineData(503, "{\"status\":\"ready\"}")]
    [InlineData(200, "{\"status\":\"live\"}")]
    [InlineData(200, "{\"status\":1}")]
    [InlineData(200, "[]")]
    public async Task Redirect_unavailable_and_wrong_service_responses_are_rejected(int status, string body)
    {
        using var client = new HttpClient(new FixtureHandler((HttpStatusCode)status, body));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AgentConnection.CheckReady(client, new Uri("https://192.168.1.233:8085")));
    }

    [Fact]
    public async Task Readiness_response_has_a_size_limit()
    {
        using var client = new HttpClient(new FixtureHandler(HttpStatusCode.OK, new string(' ', 16385)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => AgentConnection.CheckReady(client, new Uri("https://192.168.1.233:8085")));
    }

    private sealed class FixtureHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string? Target { get; private set; }
        public bool HasAuthorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Target = request.RequestUri!.AbsoluteUri;
            HasAuthorization = request.Headers.Authorization is not null;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
