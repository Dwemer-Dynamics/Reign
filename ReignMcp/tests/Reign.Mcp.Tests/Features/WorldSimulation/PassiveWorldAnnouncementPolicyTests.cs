using System.Net;
using System.Text.Json;
using Reign.Core.Contracts.World;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class PassiveWorldAnnouncementPolicyTests
{
    [Theory]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, true)]
    public void ObservationPreservesOnlyGovernmentNewsAndHoldsItsClock(
        bool observe, bool governmentNewsOpen, bool acknowledge, bool hold)
    {
        Assert.Equal(acknowledge, PassiveWorldAnnouncementPolicy.ShouldAcknowledge(observe, governmentNewsOpen));
        Assert.Equal(hold, PassiveWorldAnnouncementPolicy.ShouldHoldClock(observe, governmentNewsOpen));
    }

    [Theory]
    [InlineData(false, "none", true, "control")]
    [InlineData(true, "rolling", true, "checkpointMode none")]
    [InlineData(true, "none", true, "not armed")]
    [InlineData(true, "none", false, "confirmation")]
    public async Task ObservationCannotBypassControlConfirmationEnrollmentOrNoSaveGates(
        bool allowControl, string checkpointMode, bool confirmationMatches, string expectedError)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-news-observation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"), Path.Combine(temporary, "reign.testing.json"));
            var options = TestOptions.Create(temporary) with { AllowVerificationControl = allowControl };
            var redactor = new SensitiveDataRedactor();
            using var handler = new NoRequestsAllowedHandler();
            using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
            var service = new CampaignTestService(options, new ReignProcessRunner(options, redactor),
                new ReignApiClient(http, options, redactor), new TestingCatalogService(options));
            service.Prepare("news-campaign", "main", "Baseline", "news-run", "Observe Government News");
            Exception? error = await Record.ExceptionAsync(() => service.AdvanceAsync("news-run",
                confirmationMatches ? "advance Reign campaign test on disposable save" : "incorrect",
                1, null, checkpointMode, 120, 60, CancellationToken.None, observeGovernmentNews: true));
            Assert.NotNull(error);
            Assert.Contains(expectedError, error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, handler.RequestCount);
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    [Fact]
    public void EveryAcknowledgmentPathAndRecoveryCarryTheSameOptInChoice()
    {
        string root = TestOptions.FindWorkspace();
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        string advance = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionPassiveWorldHost.cs"));
        string controller = File.ReadAllText(Path.Combine(root, "ReignServer/tests/ReignLiveTest/Features/WorldSimulation/PassiveWorldControl.cs"));
        string mcp = File.ReadAllText(Path.Combine(root, "ReignMcp/src/Reign.Mcp.Server/Modules/Platform/CampaignTestTools.cs"));
        Assert.Contains("out string eventId, _observeGovernmentNews", host);
        Assert.Contains("_observeGovernmentNews = passiveWorld && command.Value<bool?>(\"observeGovernmentNews\") == true", host);
        Assert.Contains("_observeGovernmentNews = false;", host);
        Assert.Contains("TryAcknowledgeForLiveHarness(true, out string eventId, _observeGovernmentNews)", advance);
        Assert.Contains("PassiveWorldAcknowledgeDiplomacyAnnouncementAsync(command)", advance);
        Assert.Contains("!holdForGovernmentNews && waitMenu?.IsWaitMenu == true", advance);
        Assert.Contains("!holdForGovernmentNews && waitMenu?.IsWaitMenu != true", advance);
        Assert.Contains("step[\"observeGovernmentNews\"] = _observeGovernmentNewsForPassiveWorldControl", controller);
        Assert.Contains("_checkpointPeriodicPollToken = null; _checkpointPeriodicPollRuntime = null;\n                    _observeGovernmentNewsForPassiveWorldControl = previous;", controller.Replace("\r\n", "\n"));
        Assert.Contains("if (observeGovernmentNews) drainArgs.Add(\"--observe-government-news\")", mcp);
        Assert.Equal(false, typeof(CampaignTestTools).GetMethod("AdvanceCampaignTest")!
            .GetParameters().Single(p => p.Name == "observeGovernmentNews").DefaultValue);
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var contract = catalog.RootElement.GetProperty("campaignTest").GetProperty("passiveWorldGovernmentNewsObservation");
        Assert.False(contract.GetProperty("default").GetBoolean());
        Assert.True(contract.GetProperty("requiresUnsavedBoundary").GetBoolean());
        Assert.True(contract.GetProperty("continuesThroughCatchUpAndRecovery").GetBoolean());
    }

    [Fact]
    public void AnUnacknowledgedBulletinCannotRestoreFastTimeAfterAStoppedBoundary()
    {
        string root = TestOptions.FindWorkspace();
        string screen = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Diplomacy/UI/ReignDiplomacyAnnouncementScreenManager.cs"));
        string host = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionPassiveWorldHost.cs"));
        int acknowledge = screen.IndexOf("public static bool TryAcknowledgeForLiveHarness", StringComparison.Ordinal);
        int preservePause = screen.IndexOf("KeepCampaignPausedForLiveHarness();", acknowledge, StringComparison.Ordinal);
        int preserveNews = screen.IndexOf("PassiveWorldAnnouncementPolicy.ShouldAcknowledge", acknowledge, StringComparison.Ordinal);
        Assert.True(preservePause > acknowledge && preservePause < preserveNews);
        int latch = screen.IndexOf("public static void KeepCampaignPausedForLiveHarness()", StringComparison.Ordinal);
        int nextMethod = screen.IndexOf("public static bool TryCloseCalibrationFixture()", latch, StringComparison.Ordinal);
        string latchBody = screen[latch..nextMethod];
        Assert.Contains("if (IsOpen && _restoreTime)", latchBody);
        Assert.Contains("_priorTimeMode = CampaignTimeControlMode.Stop;", latchBody);
        Assert.DoesNotContain("Acknowledge();", latchBody);
        Assert.Contains("ReignDiplomacyAnnouncementScreenManager.KeepCampaignPausedForLiveHarness();", host);
        // Boundary, explicit stop, cancellation, timeout, success and exceptional cleanup
        // all use the same latch, independent of the server's diplomacy queue counts.
        Assert.Equal(6, host.Split("PausePassiveWorldCampaign(campaign);", StringSplitOptions.None).Length - 1);
        int cleanup = host.IndexOf("finally", StringComparison.Ordinal);
        int clear = host.IndexOf("_passiveWorldAdvanceBoundary = null;", cleanup, StringComparison.Ordinal);
        Assert.Contains("boundary.Owns(campaign, _activeRunId)", host[cleanup..clear]);
        Assert.Contains("PausePassiveWorldCampaign(campaign);", host[cleanup..clear]);
    }

    [Fact]
    public void GovernmentInspectionClosePreservesMapOriginWithoutInventingATown()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionUiCalibrationHost.cs"));
        int map = source.IndexOf("if (_uiCalibrationOpenedFromMap)", StringComparison.Ordinal);
        int settlement = source.IndexOf("PreserveCalibrationClock(RestoreSettlementMenuAfterCalibration)", map, StringComparison.Ordinal);
        Assert.True(map >= 0 && settlement > map);
        string mapReturn = source[map..settlement];
        Assert.Contains("ReferenceEquals(_uiCalibrationClockCampaign", mapReturn);
        Assert.Contains("ActiveState is MapState", mapReturn);
        Assert.Contains("CalibrationClockResult(successMessage, UiStateJson())", mapReturn);
        Assert.DoesNotContain("GameMenu.ActivateGameMenu", mapReturn);
        Assert.Contains("_uiCalibrationOpenedFromMap = false;", source);
    }

    private sealed class NoRequestsAllowedHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
