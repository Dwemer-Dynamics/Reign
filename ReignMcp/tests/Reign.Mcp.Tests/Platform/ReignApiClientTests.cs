using System.Net;
using System.Text;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignApiClientTests
{
    [Fact]
    public async Task EncodesQueryAndRedactsResponse()
    {
        Uri? observed = null;
        var handler = new StubHandler(request =>
        {
            observed = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"ok":true,"apiKey":"do-not-return","value":"safe"}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var client = new ReignApiClient(http, options, new SensitiveDataRedactor());

        var result = await client.GetAsync("/world-test/details", new Dictionary<string, string?>
        {
            ["search"] = "A B&C"
        });

        Assert.True(result.Ok);
        Assert.Equal("?search=A%20B%26C", observed!.Query);
        Assert.DoesNotContain("do-not-return", result.Data!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result.Data.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectsOversizedResponse()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('x', 4096))
        });
        var options = TestOptions.Create(maxApiBytes: 1024);
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var client = new ReignApiClient(http, options, new SensitiveDataRedactor());

        var result = await client.GetAsync("/health");

        Assert.False(result.Ok);
        Assert.Contains("response limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostAsyncSerializesAnonymousBodyUsingItsRuntimeType()
    {
        string? observedBody = null;
        long? observedContentLength = null;
        bool? observedChunked = null;
        var handler = new StubHandler(request =>
        {
            observedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            observedContentLength = request.Content.Headers.ContentLength;
            observedChunked = request.Headers.TransferEncodingChunked;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
            };
        });
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var client = new ReignApiClient(http, options, new SensitiveDataRedactor());

        var result = await client.PostAsync("/tests/live/run/start", new
        {
            campaignId = "campaign-test",
            mode = "spymaster"
        });

        Assert.True(result.Ok);
        Assert.Contains("\"campaignId\":\"campaign-test\"", observedBody, StringComparison.Ordinal);
        Assert.Contains("\"mode\":\"spymaster\"", observedBody, StringComparison.Ordinal);
        Assert.Equal(Encoding.UTF8.GetByteCount(observedBody!), observedContentLength);
        Assert.NotEqual(true, observedChunked);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(response(request));
        }
    }
}
