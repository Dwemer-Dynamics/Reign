using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Reign.Mcp.Server;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class McpHostIntegrationTests
{
    [Fact]
    public async Task PoliticalPressureObservationRoutesOneReadOnlyDirectionalRequest()
    {
        int requests = 0;
        var handler = new PressureObservationHandler(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/world-test/details", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Content);
            string query = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains("subsystem=political pressures", query);
            Assert.Contains("pair=foreign|player", query);
            Assert.Contains("campaignId=observed_campaign", query);
            Assert.Contains("timelineId=observed_timeline", query);
            Assert.Contains("page=2", query);
            Assert.Contains("pageSize=10", query);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true,"rows":[{"actor_kingdom_id":"foreign","target_kingdom_id":"player","before_value":10,"after_value":4,"incident":{"origin_pressure_before":10,"origin_pressure_after":4,"target_stance":"awaiting_player"}}]}""")
            };
        });
        var options = TestOptions.Create();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, new SensitiveDataRedactor());
        var result = await WorldTools.GetWorldDetails(api, campaignId: "observed_campaign",
            timelineId: "observed_timeline", subsystem: "political pressures",
            pair: "foreign|player", page: 2, pageSize: 10);
        Assert.True(result.Ok);
        Assert.Equal(1, requests);
        Assert.Equal(4, result.Data!.Value.GetProperty("rows")[0].GetProperty("after_value").GetInt32());
        Assert.Equal("awaiting_player", result.Data.Value.GetProperty("rows")[0].GetProperty("incident").GetProperty("target_stance").GetString());
    }

    private sealed class PressureObservationHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a|a")]
    [InlineData("a|b|c")]
    public void ExactRelationshipPairRequiresTwoDistinctIdentifiers(string pair)
    {
        Assert.Throws<ArgumentException>(() =>
        {
            _ = WorldTools.GetWorldDetails(null!, campaignId: "observed_campaign",
                subsystem: "relationship pair", pair: pair);
        });
    }

    [Fact]
    public void ExactRelationshipObservationHasCatalogAndRunbookCoverage()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = catalog.RootElement.GetProperty("relationshipPairObservation");
        Assert.Equal("reign_get_world_details", entry.GetProperty("tool").GetString());
        Assert.Equal("relationship pair", entry.GetProperty("subsystem").GetString());
        Assert.Equal("reign-relationship-pair-observation-v1", entry.GetProperty("schema").GetString());
        Assert.Contains("reign-relationship-pair-observation-v1",
            TestingDocumentation.Read(Path.Combine(root, "docs", "agent", "TESTING_TOOL_GUIDE.md")));
    }

    [Fact]
    public async Task StdioHostAdvertisesExpectedSurface()
    {
        var packagedServer = Environment.GetEnvironmentVariable("REIGN_MCP_TEST_SERVER_DLL");
        var serverAssembly = string.IsNullOrWhiteSpace(packagedServer)
            ? typeof(RuntimeTools).Assembly.Location
            : Path.GetFullPath(packagedServer);
        Assert.True(File.Exists(serverAssembly), $"MCP test server is missing: {serverAssembly}");

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "Reign MCP integration test",
            Command = "dotnet",
            Arguments = new[] { serverAssembly }
        });

        await using var client = await McpClient.CreateAsync(transport);
        var tools = await client.ListToolsAsync();
        var names = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("political pressures", tools.Single(tool => tool.Name == "reign_get_world_details").JsonSchema.GetRawText());

        string[] expectedTools =
        [
            "reign_get_capabilities",
            "reign_get_status",
            "reign_shutdown_server",
            "reign_list_campaigns",
            "reign_get_campaign_storage_audit",
            "reign_cleanup_campaigns_without_saves",
            "reign_get_world_overview",
            "reign_get_world_details",
            "reign_query_world_history",
            "reign_get_action_health",
            "reign_get_verification_status",
            "reign_get_verification_results",
            "reign_get_live_test_status",
            "reign_get_workspace_status",
            "reign_get_validation_plan",
            "reign_get_validation_status",
            "reign_search_source",
            "reign_read_source",
            "reign_build",
            "reign_validate",
            "reign_run_offline_verification"
        ];
        Assert.All(expectedTools, name => Assert.Contains(name, names));

        var statusTool = tools.Single(tool => tool.Name == "reign_get_validation_status");
        Assert.True(statusTool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(statusTool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.Contains("requestingTaskId",
            tools.Single(tool => tool.Name == "reign_validate").JsonSchema.GetRawText());
        var laneResult = await AssertSuccessfulCallAsync(client, "reign_get_validation_status", []);
        using var lane = JsonDocument.Parse(laneResult.StructuredContent?.ToString()
            ?? throw new InvalidOperationException("Validation status structured content was empty."));
        Assert.Equal("reign-validation-lane-v1", lane.RootElement.GetProperty("schema").GetString());
        Assert.Contains(lane.RootElement.GetProperty("status").GetString(),
            new[] { "available", "busy", "unavailable" });

        var resources = await client.ListResourcesAsync();
        Assert.Contains(resources, resource =>
            resource.Uri == "reign://workspace/roadmap");
        Assert.Contains(resources, resource =>
            resource.Uri == "reign://runtime/status");

        var templates = await client.ListResourceTemplatesAsync();
        Assert.Contains(templates, template =>
            template.UriTemplate == "reign://campaign/{campaignId}/world/{timelineId}");

        var prompts = await client.ListPromptsAsync();
        var promptNames = prompts.Select(prompt => prompt.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("diagnose_reign_world", promptNames);
        Assert.Contains("review_reign_verification_failure", promptNames);
        Assert.Contains("plan_reign_feature", promptNames);

        var capabilities = await client.CallToolAsync(
            "reign_get_capabilities",
            new Dictionary<string, object?>());
        Assert.NotEqual(true, capabilities.IsError);

        var roadmap = await client.ReadResourceAsync("reign://workspace/roadmap");
        Assert.Contains(roadmap.Contents, content =>
            content is ModelContextProtocol.Protocol.TextResourceContents text
            && text.Text.Contains("# Bannerlord Reign Roadmap", StringComparison.Ordinal));

        var diagnosticPrompt = await client.GetPromptAsync(
            "diagnose_reign_world",
            new Dictionary<string, object?>
            {
                ["campaignId"] = "latest",
                ["timelineId"] = "main",
                ["symptom"] = "No arranged marriage has appeared."
            });
        Assert.NotEmpty(diagnosticPrompt.Messages);

        if (Environment.GetEnvironmentVariable("REIGN_MCP_LIVE_ACCEPTANCE") == "1")
        {
            Assert.Equal(29, tools.Count);
            Assert.Equal(6, resources.Count + templates.Count);
            Assert.Equal(3, prompts.Count);

            using (var document = JsonDocument.Parse(
                       capabilities.StructuredContent?.ToString()
                       ?? throw new InvalidOperationException("Capability structured content was empty.")))
            {
                var manifest = document.RootElement;
                Assert.True(manifest.GetProperty("buildEnabled").GetBoolean());
                Assert.Equal(
                    Environment.GetEnvironmentVariable("REIGN_MCP_ALLOW_RESTORE") == "true",
                    manifest.GetProperty("restoreEnabled").GetBoolean());
                Assert.Equal(
                    Environment.GetEnvironmentVariable("REIGN_MCP_ALLOW_VERIFICATION_CONTROL") == "true",
                    manifest.GetProperty("verificationControlEnabled").GetBoolean());
                Assert.Equal(
                    Environment.GetEnvironmentVariable("REIGN_MCP_ALLOW_OFFLINE_VERIFICATION") == "true",
                    manifest.GetProperty("offlineVerificationEnabled").GetBoolean());
            }

            var status = await AssertSuccessfulCallAsync(
                client,
                "reign_get_status",
                new Dictionary<string, object?>());
            using (var document = JsonDocument.Parse(
                       status.StructuredContent?.ToString()
                       ?? throw new InvalidOperationException("Status structured content was empty.")))
            {
                var health = document.RootElement.GetProperty("health");
                Assert.True(health.GetProperty("ok").GetBoolean());
                Assert.Equal(200, health.GetProperty("statusCode").GetInt32());
            }

            var campaigns = await AssertSuccessfulCallAsync(
                client,
                "reign_list_campaigns",
                new Dictionary<string, object?>());
            AssertEnvelopeOk(campaigns);

            using (var document = JsonDocument.Parse(
                       campaigns.StructuredContent?.ToString()
                       ?? throw new InvalidOperationException("Campaign structured content was empty.")))
            {
                var observedCampaigns = document.RootElement
                    .GetProperty("data")
                    .GetProperty("campaigns");
                if (observedCampaigns.GetArrayLength() > 0)
                {
                    var campaign = observedCampaigns[0];
                    var campaignId = campaign.GetProperty("campaignId").GetString()!;
                    var timelines = campaign.GetProperty("timelines");
                    var timelineId = timelines.GetArrayLength() > 0
                        ? timelines[0].GetProperty("timeline_id").GetString() ?? "main"
                        : "main";
                    var overview = await AssertSuccessfulCallAsync(
                        client,
                        "reign_get_world_overview",
                        new Dictionary<string, object?>
                        {
                            ["campaignId"] = campaignId,
                            ["timelineId"] = timelineId,
                            ["limit"] = 20
                        });
                    AssertEnvelopeOk(overview);
                }
            }

            var verification = await AssertSuccessfulCallAsync(
                client,
                "reign_get_verification_status",
                new Dictionary<string, object?>());
            AssertEnvelopeOk(verification);

            var actionHealth = await AssertSuccessfulCallAsync(
                client,
                "reign_get_action_health",
                new Dictionary<string, object?>
                {
                    ["campaignId"] = "",
                    ["failureLimit"] = 20
                });
            Assert.NotNull(actionHealth.StructuredContent);

            var gatedVerification = await client.CallToolAsync(
                "reign_start_verification",
                new Dictionary<string, object?>());
            Assert.Equal(true, gatedVerification.IsError);

            var gatedOfflineRun = await client.CallToolAsync(
                "reign_run_offline_verification",
                new Dictionary<string, object?>());
            Assert.Equal(true, gatedOfflineRun.IsError);

            if (Environment.GetEnvironmentVariable("REIGN_MCP_LIVE_BUILD_ACCEPTANCE") == "1")
            {
                var build = await AssertSuccessfulCallAsync(
                    client,
                    "reign_build",
                    new Dictionary<string, object?>
                    {
                        ["component"] = "mcp",
                        ["configuration"] = "Release",
                        ["restore"] = false
                    });
                using var document = JsonDocument.Parse(
                    build.StructuredContent?.ToString()
                    ?? throw new InvalidOperationException("Build structured content was empty."));
                Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            }
        }
    }

    private static async Task<CallToolResult> AssertSuccessfulCallAsync(
        McpClient client,
        string name,
        Dictionary<string, object?> arguments)
    {
        var result = await client.CallToolAsync(name, arguments);
        Assert.NotEqual(true, result.IsError);
        Assert.NotNull(result.StructuredContent);
        return result;
    }

    private static void AssertEnvelopeOk(CallToolResult result)
    {
        using var document = JsonDocument.Parse(
            result.StructuredContent?.ToString()
            ?? throw new InvalidOperationException("Envelope structured content was empty."));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(200, document.RootElement.GetProperty("statusCode").GetInt32());
    }
}
