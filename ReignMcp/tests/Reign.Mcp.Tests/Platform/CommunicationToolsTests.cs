using System.Net;
using System.Text;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class CommunicationToolsTests
{
    [Theory]
    [InlineData("", "main", 0, 10)]
    [InlineData("campaign", "", 0, 10)]
    [InlineData("../campaign", "main", 0, 10)]
    [InlineData("campaign", "../main", 0, 10)]
    [InlineData("campaign", "main", -1, 10)]
    [InlineData("campaign", "main", 0, 101)]
    public async Task InvalidScopeCannotReachTheTransport(string campaign, string timeline, int offset, int limit) =>
        await Assert.ThrowsAnyAsync<ArgumentException>(() => CommunicationTools.GetStatus(null!, campaign, timeline, offset: offset, limit: limit));

    [Fact]
    public async Task StatusUsesOnlyTheReadOnlyBoundedRoute()
    {
        Uri? endpoint = null; HttpMethod? method = null;
        using var handler = new Handler(request => { endpoint = request.RequestUri; method = request.Method;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true,\"messages\":[]}", Encoding.UTF8, "application/json") }; });
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, new SensitiveDataRedactor());
        var result = await CommunicationTools.GetStatus(api, "campaign", "main", "hero", 25, 10);
        Assert.True(result.Ok); Assert.Equal(HttpMethod.Get, method);
        Assert.Equal("/communications/status", endpoint!.AbsolutePath);
        Assert.Contains("timelineId=main", endpoint.Query, StringComparison.Ordinal);
        Assert.Contains("offset=25", endpoint.Query, StringComparison.Ordinal);
        Assert.Contains("limit=10", endpoint.Query, StringComparison.Ordinal);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
