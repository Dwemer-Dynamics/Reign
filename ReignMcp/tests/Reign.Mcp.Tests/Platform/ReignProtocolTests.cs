using System.Net;
using Reign.Core.Contracts.Platform;

namespace Reign.Mcp.Tests;

public sealed class ReignProtocolTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task MismatchPreventsTheApplicationRequest(int protocol)
    {
        var transport = new FixtureTransport(protocol);
        using var client = new HttpClient(new ReignProtocolHandler(transport));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.PostAsync("http://127.0.0.1:5101/events/ingest", new StringContent("{}")));
        Assert.Equal(0, transport.ApplicationRequests);
    }

    [Fact]
    public async Task MatchingServerIsProbedOnceAndEveryApplicationRequestCarriesItsProtocol()
    {
        var transport = new FixtureTransport(1);
        using var client = new HttpClient(new ReignProtocolHandler(transport));
        using var first = await client.PostAsync("http://127.0.0.1:5101/events/ingest", new StringContent("{}"));
        using var second = await client.GetAsync("http://127.0.0.1:5101/actions");
        Assert.Equal(1, transport.Probes);
        Assert.Equal(2, transport.ApplicationRequests);
    }

    private sealed class FixtureTransport(int protocol) : HttpMessageHandler
    {
        public int Probes;
        public int ApplicationRequests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            bool health = request.RequestUri!.AbsolutePath == "/health";
            if (health) Probes++;
            else
            {
                ApplicationRequests++;
                Assert.Equal("1", Assert.Single(request.Headers.GetValues(ReignProtocolHandler.HeaderName)));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(health ? "{\"service\":\"BannerlordReignServer\",\"protocolVersion\":" + protocol + "}" : "{}")
            });
        }
    }
}
