using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ToolSafetyTests
{
    [Fact]
    public async Task BuildIsDisabledByDefault()
    {
        var options = TestOptions.Create();
        var redactor = new SensitiveDataRedactor();
        var runner = new ReignProcessRunner(options, redactor);
        using var http = new HttpClient(new OfflineHandler())
        {
            BaseAddress = options.ServerBaseUri
        };
        var api = new ReignApiClient(http, options, redactor);
        var catalog = new ReignProjectCatalog(options);
        var service = new ReignBuildService(options, runner, api, catalog);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.BuildAsync("mcp", "Release", false, CancellationToken.None));
    }

    [Fact]
    public void ExactConfirmationIsCaseSensitive()
    {
        Assert.Throws<InvalidOperationException>(() =>
            InputGuard.RequireConfirmation(
                "Start Reign Verification",
                "start Reign verification"));
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline");
        }
    }
}
