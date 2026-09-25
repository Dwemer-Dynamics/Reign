using System.Net;
using System.Text;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ConversationDiagnosticsTests
{
    [Fact]
    public async Task ToolsOnlyReadBoundedRetainedEvidence()
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler(request => {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Null(request.Content);
            requests.Add(request.RequestUri!);
            return Json("{\"ok\":true}");
        }));
        var api = new ReignApiClient(http, TestOptions.Create(), new SensitiveDataRedactor());
        await RuntimeTools.QueryConversationDiagnostics(api, campaignId: "campaign", search: "Corein & tournaments", limit: 10);
        await RuntimeTools.GetConversationDiagnosticTrace(api, "legacy-abc", "campaign");
        await RuntimeTools.GetConversationDiagnosticPayload(api, "abc", "def", offset: 12000, length: 500);
        Assert.Equal(new[] { "/api/diagnostics/conversations", "/api/diagnostics/conversations/trace", "/api/diagnostics/conversations/payload" }, requests.Select(x => x.AbsolutePath));
        Assert.Contains("search=Corein%20%26%20tournaments", requests[0].Query);
        Assert.Contains("offset=12000", requests[2].Query);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RuntimeTools.QueryConversationDiagnostics(api, limit: 101));
        await Assert.ThrowsAsync<ArgumentException>(() => RuntimeTools.GetConversationDiagnosticTrace(api, "../../campaigns"));
        await Assert.ThrowsAsync<ArgumentException>(() => RuntimeTools.GetConversationDiagnosticTrace(api, ""));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => RuntimeTools.GetConversationDiagnosticPayload(api, "a", "b", length: 12001));
        Assert.Equal(3, requests.Count);
    }

    [Fact]
    public async Task OnlyDedicatedPayloadContentPreservesImmutableRedactedPages()
    {
        const string retained = "A fictional secret=castle-plan and returned reasoning. Credentials: [REDACTED].";
        using var http = new HttpClient(new Handler(_ => Json(JsonSerializer.Serialize(new {
            ok = true, content = retained, apiKey = string.Concat("fixture-", "auth-value"), metadata = new { password = string.Concat("fixture-", "password-value") }
        }))));
        var api = new ReignApiClient(http, TestOptions.Create(), new SensitiveDataRedactor());
        var page = await api.GetAsync("/api/diagnostics/conversations/payload");
        Assert.Equal(retained, page.Data!.Value.GetProperty("content").GetString());
        Assert.Equal("[REDACTED]", page.Data.Value.GetProperty("apiKey").GetString());
        Assert.Equal("[REDACTED]", page.Data.Value.GetProperty("metadata").GetProperty("password").GetString());
        var ordinary = await api.GetAsync("/api/logs");
        Assert.DoesNotContain("castle-plan", ordinary.Data!.Value.GetProperty("content").GetString());
    }

    [Fact]
    public void CatalogAndHelpDescribeCaptureBoundaryAndTests()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var diagnostics = catalog.RootElement.GetProperty("conversationDiagnostics");
        Assert.Equal("reign-conversation-diagnostics-v1", diagnostics.GetProperty("schema").GetString());
        Assert.Contains("until manually cleared", diagnostics.GetProperty("retention").GetString());
        Assert.True(File.Exists(Path.Combine(workspace, diagnostics.GetProperty("browserContract").GetString()!)));
        foreach (string file in new[] { "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "docs/agent/testing/DIALOGUE_AND_PROVIDERS.md" })
        {
            string text = File.ReadAllText(Path.Combine(workspace, file));
            Assert.Contains("reign_query_conversation_diagnostics", text);
            Assert.Contains("reign_get_conversation_diagnostic_trace", text);
            Assert.Contains("reign_get_conversation_diagnostic_payload", text);
        }
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
