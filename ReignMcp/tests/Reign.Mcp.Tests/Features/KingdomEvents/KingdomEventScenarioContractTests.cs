using System.Net;
using System.Text;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class KingdomEventScenarioContractTests
{
    [Fact]
    public async Task McpKingdomEventToolRefusesAnUnarmedCampaignTestEnrollment()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-kingdom-event-test-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        File.Copy(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"),
            Path.Combine(temporary, "reign.testing.json"));
        var handler = new StubHandler(request =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
            };
        });
        ReignMcpOptions options = TestOptions.Create(temporary) with { AllowVerificationControl = true };
        var redactor = new SensitiveDataRedactor();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, redactor);
        var campaignTests = new CampaignTestService(options,
            new ReignProcessRunner(options, redactor), api, new TestingCatalogService(options));
        campaignTests.Prepare("campaign_test", "timeline_test", "BaselineSave", "campaign-run",
            "Kingdom Events Launch");

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TestingTools.StartKingdomEventTest(api, options, campaignTests,
                    campaignId: "campaign_test", campaignTestRunId: "campaign-run",
                    profile: "preflight", archetypeId: "famine", targetVariant: "first",
                    fixtureRunId: "fixture_test", kingdomId: "", accelerateOrganicTrigger: false,
                    confirmation: "start Reign kingdom event test on disposable save",
                    cancellationToken: CancellationToken.None));
            Assert.Contains("not armed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void RiskBasedKingdomEventMatrixCoversEveryArchetypeAndRepresentativeTargetOrdering()
    {
        string workspace = TestOptions.FindWorkspace();
        string scenarioRoot = Path.Combine(workspace, "ReignBetaServer", "ReignLiveTest", "scenarios");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(scenarioRoot, "kingdom-event-acceptance-manifest.json")));
        JsonElement root = manifest.RootElement;
        Assert.True(root.GetProperty("preparedOnly").GetBoolean());
        Assert.Equal(15, root.GetProperty("archetypes").GetArrayLength());
        Assert.Equal(3, root.GetProperty("targetVariants").GetArrayLength());
        Assert.Equal(15, root.GetProperty("baseArchetypeCaseCount").GetInt32());
        Assert.Equal(6, root.GetProperty("additionalTargetOrderingCaseCount").GetInt32());
        Assert.Equal(21, root.GetProperty("caseCount").GetInt32());
        Assert.Equal(3, root.GetProperty("representativeTargetOrdering").GetArrayLength());
        Assert.Equal(3, root.GetProperty("persistenceRepresentatives").GetArrayLength());
        Assert.Equal(1, root.GetProperty("organicProductionObservations").GetInt32());
        Assert.Equal(7, root.GetProperty("archetypes").EnumerateArray()
            .Count(item => item.GetProperty("polarity").GetString() == "harmful"));
        Assert.Equal(8, root.GetProperty("archetypes").EnumerateArray()
            .Count(item => item.GetProperty("polarity").GetString() == "beneficial"));

        string liveServer = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBetaServer"), "LiveInteractionTest.cs", "/src/"));
        string liveHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionKingdomEventHost.cs",
            "/src/Modules/KingdomEvents/"));
        string inGameTests = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignKingdomEventInGameTests.cs",
            "/src/Modules/KingdomEvents/"));
        string campaignBehavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignKingdomEventsCampaignBehavior.cs",
            "/src/Modules/KingdomEvents/"));
        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignMcp"), "TestingTools.KingdomEvents.cs"));
        Assert.Contains("\"kingdom_event_test\"", liveServer, StringComparison.Ordinal);
        Assert.Contains("RunKingdomEventTestProfile", liveHost, StringComparison.Ordinal);
        Assert.Contains("reign_start_kingdom_event_test", mcp, StringComparison.Ordinal);
        Assert.Contains("force Reign kingdom event on disposable save", mcp, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", mcp, StringComparison.Ordinal);
        Assert.Contains("expectedSaveName", mcp, StringComparison.Ordinal);
        Assert.Contains("mark_reload", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("save_checkpoint", mcp, StringComparison.Ordinal);
        Assert.Contains("TryRequireKingdomEventTestSave", inGameTests, StringComparison.Ordinal);
        Assert.Contains("organic_prepare", inGameTests, StringComparison.Ordinal);
        Assert.Contains("accelerateOrganicTrigger", mcp, StringComparison.Ordinal);
        Assert.Contains("kingdom_event_organic_acceleration_consumed", inGameTests, StringComparison.Ordinal);
        Assert.Contains("Organic production readiness is not yet available", inGameTests, StringComparison.Ordinal);
        Assert.Contains("ReignCampaignInitializationGate.IsPending", inGameTests, StringComparison.Ordinal);
        Assert.Contains("AreAutonomousWorldSystemsUnlocked(CurrentDay())", inGameTests, StringComparison.Ordinal);
        Assert.Contains("profile != \"organic_prepare\"", mcp, StringComparison.Ordinal);
        Assert.Contains("_testOrganicTriggerDay = -1", campaignBehavior, StringComparison.Ordinal);
        Assert.DoesNotContain("SyncData(\"_testOrganicTrigger", campaignBehavior, StringComparison.Ordinal);
        Assert.Contains("kingdom_event_no_target_no_record", inGameTests, StringComparison.Ordinal);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
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
