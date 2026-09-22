using System.Net;
using System.Text;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class TestingToolsCampaignCommandTests
{
    [Fact]
    public async Task PreparationUsesGuardedCampaignEnrollmentInsteadOfSaveNameInference()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-campaign-command-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        File.Copy(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"),
            Path.Combine(temporary, "reign.testing.json"));
        var requests = new List<(string Path, string Body)>();
        var handler = new StubHandler(request =>
        {
            requests.Add((
                request.RequestUri!.AbsolutePath,
                request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? ""));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
            };
        });
        var options = TestOptions.Create(temporary) with { AllowVerificationControl = true };
        var redactor = new SensitiveDataRedactor();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, redactor);
        var campaignTests = new CampaignTestService(options,
            new ReignProcessRunner(options, redactor), api, new TestingCatalogService(options));
        campaignTests.Prepare("campaign-test", "main", "CommandTest", "guarded-run",
            "Campaign Command Launch");

        try
        {
            ApiEnvelope result = await TestingTools.PrepareCampaignCommandTest(
                api, campaignTests, "campaign-test", "guarded-run", "certification-test");

            Assert.True(result.Ok);
            Assert.Single(requests);
            Assert.Equal("/tests/campaign-command/prepare", requests[0].Path);
            Assert.Contains("\"campaignTestRunId\":\"guarded-run\"", requests[0].Body,
                StringComparison.Ordinal);
            Assert.Contains("\"protectedBaselineSaveName\":\"CommandTest\"", requests[0].Body,
                StringComparison.Ordinal);
            Assert.Contains("\"disposableSaveName\":\"ReignTest_CampaignCommandLaunch_",
                requests[0].Body, StringComparison.Ordinal);
            Assert.Contains("_Current\"", requests[0].Body, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response(request));
    }
}
