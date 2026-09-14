using System.Net;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class PromptEvidenceToolTests
{
    [Theory]
    [InlineData("", 0, 0, 4000)]
    [InlineData("correlation", -1, 0, 4000)]
    [InlineData("correlation", 0, -1, 4000)]
    [InlineData("correlation", 0, 0, 12001)]
    public async Task RejectsInvalidPagesBeforeAnyRequest(string correlation, int message, int offset, int length)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => RuntimeTools.GetPromptEvidence(null!, correlation,
            messageIndex: message, offset: offset, length: length));
    }

    [Fact]
    public async Task EvidenceUsesReadOnlyRouteAndPinsExactAttempt()
    {
        using var handler = new EvidenceHandler();
        using var http = new HttpClient(handler);
        var api = new ReignApiClient(http, TestOptions.Create(), new SensitiveDataRedactor());
        await RuntimeTools.GetPromptEvidence(api, "correlation", "campaign", "attempt-id", 2, 16000, 4000);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Contains("/audit/prompt?", handler.Uri);
        Assert.Contains("evidenceId=attempt-id", handler.Uri);
        Assert.Contains("offset=16000", handler.Uri);
        Assert.Contains("messageIndex=2", handler.Uri);
    }

    [Fact]
    public void FocusedPromptSuiteExecutesProductionChecksInsteadOfOnlyCoverageMetadata()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBetaServer/src/Modules/Platform/VerificationLab.cs"));
        int start = source.IndexOf("private static void RunQuickVerificationChecks", StringComparison.Ordinal);
        int end = source.IndexOf("private static void RunOfflineVerificationChecks", start, StringComparison.Ordinal);
        string quick = source[start..end];
        Assert.Contains("requestedSuite, \"prompt_efficiency\"", quick);
        Assert.Contains("RunPromptSizeContract(checks)", quick);
        Assert.Contains("RunPromptCachingSelfTests()", quick);
    }

    [Fact]
    public void CatalogAndGuidesDescribeEvidenceBoundsAndRetention()
    {
        string workspace = TestOptions.FindWorkspace();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var contract = doc.RootElement.GetProperty("promptComposition");
        Assert.Equal("reign_get_prompt_evidence", contract.GetProperty("tool").GetString());
        Assert.Equal(12000, contract.GetProperty("maximumPageCharacters").GetInt32());
        Assert.Equal("reign-prompt-evidence-v1", contract.GetProperty("evidenceSchema").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
        {
            string text = TestingDocumentation.Read(Path.Combine(workspace, path));
            Assert.Contains("reign_get_prompt_evidence", text);
            Assert.Contains("evidenceId", text);
            Assert.Contains("12000", text);
        }
    }

    private sealed class EvidenceHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string Uri { get; private set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method; Uri = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") });
        }
    }
}
