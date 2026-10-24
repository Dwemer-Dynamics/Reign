using Reign.Mcp.Server;
using ReignBeta.Shared;

namespace Reign.Mcp.Tests;

public sealed class PersonalGossipToolsTests
{
    [Fact]
    public void CompositionProbeRequiresExactFrozenJobAndAcceptsNoOverrides()
    {
        Assert.Single(PersonalGossipTestTools.ParseCases("""
            [{"caseId":"probe","phase":"composition_probe","actorHeroId":"source","messageId":"gossip_root_out"}]
            """));
        foreach (string value in new[] {
            "{\"caseId\":\"probe\",\"phase\":\"composition_probe\",\"actorHeroId\":\"source\"}",
            "{\"caseId\":\"probe\",\"phase\":\"composition_probe\",\"messageId\":\"gossip_root_out\"}",
            "{\"caseId\":\"probe\",\"phase\":\"composition_probe\",\"actorHeroId\":\"source\",\"messageId\":\"../private\"}",
            "{\"caseId\":\"probe\",\"phase\":\"composition_probe\",\"actorHeroId\":\"source\",\"messageId\":\"gossip_root_out\",\"sourceText\":\"replacement\"}",
            "{\"caseId\":\"probe\",\"phase\":\"composition_probe\",\"actorHeroId\":\"source\",\"messageId\":\"gossip_root_out\",\"draws\":{\"share\":1}}"
        }) Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases("[" + value + "]"));
    }

    [Fact]
    public async Task CompositionProbeCannotReachNativeOrProviderWithoutExplicitPermission()
    {
        var options = TestOptions.Create() with { AllowVerificationControl = true };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => PersonalGossipTestTools.TestPersonalGossip(
            null!, options, null!, "campaign", "owned", "probe", """
            [{"caseId":"probe","phase":"composition_probe","actorHeroId":"source","messageId":"gossip_root_out"}]
            """, allowProviderCalls: false, confirmation: PersonalGossipTestTools.Confirmation));
        Assert.Contains("provider authorization", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompositionProbeCatalogRequiresShadowEvidenceAndSeparateNativeAcceptance()
    {
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var probe = catalog.RootElement.GetProperty("personalGossip").GetProperty("nativeCompositionProbe");
        Assert.Equal("reign-gossip-composition-probe-v1", probe.GetProperty("schema").GetString());
        Assert.False(probe.GetProperty("nativeDeliveryAcceptance").GetBoolean());
        Assert.Equal(0, probe.GetProperty("liveAttemptsConsumed").GetInt32());
        foreach (string path in new[] { "ReignMcp/README.md", "ReignMcp/docs/security-model.md", "docs/agent/testing/DIALOGUE_AND_PROVIDERS.md" })
            Assert.Contains("composition_probe", File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), path)));
    }

    [Fact]
    public void EmptyReadinessDoesNotLoseALaterAppliedRelationshipReceipt()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Request(now);
        watch.Observe(watch.Revision, true, false, now.AddSeconds(1));
        Assert.True(watch.ShouldPoll(now.AddSeconds(119)));
        Assert.False(watch.ShouldPoll(now.AddSeconds(121)));
    }

    [Fact]
    public void CoalescedWakeSurvivesAnOlderDisabledOrEmptyHttpResponse()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Request(now);
        long observed = watch.Revision;
        watch.Request(now.AddSeconds(30));
        watch.Observe(observed, false, false, now.AddSeconds(31));
        Assert.True(watch.ShouldPoll(now.AddSeconds(149)));
        Assert.False(watch.ShouldPoll(now.AddSeconds(151)));
    }

    [Fact]
    public void PendingPollsCannotExtendTheFastWatchIndefinitely()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Observe(watch.Revision, true, true, now);
        watch.Observe(watch.Revision, true, true, now.AddSeconds(119));
        Assert.False(watch.ShouldPoll(now.AddSeconds(121)));
        watch.Request(now.AddSeconds(121));
        Assert.True(watch.ShouldPoll(now.AddSeconds(240)));
        watch.Observe(watch.Revision, false, false, now.AddSeconds(122));
        Assert.False(watch.ShouldPoll(now.AddSeconds(123)));
    }

    [Fact]
    public void KnownWorkContinuesAtSlowCadenceUntilAnEmptyOrDisabledResult()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Request(now);
        watch.Observe(watch.Revision, true, true, now);
        watch.Observe(watch.Revision, true, true, now.AddSeconds(119));
        Assert.False(watch.ShouldPoll(now.AddSeconds(121)));
        Assert.True(watch.ShouldPoll(now.AddSeconds(149)));
        watch.Observe(watch.Revision, true, true, now.AddSeconds(150));
        Assert.False(watch.ShouldPoll(now.AddSeconds(151)));
        Assert.True(watch.ShouldPoll(now.AddSeconds(180)));
        watch.Observe(watch.Revision, true, false, now.AddSeconds(180));
        Assert.False(watch.HasKnownWork);
        Assert.False(watch.ShouldPoll(now.AddSeconds(181)));
        watch.Observe(watch.Revision, true, true, now.AddSeconds(200));
        watch.Observe(watch.Revision, false, true, now.AddSeconds(201));
        Assert.True(watch.ShouldPoll(now.AddSeconds(400)));
        Assert.True(watch.IsDisabled);
        Assert.False(watch.HasKnownWork);
    }

    [Fact]
    public void DisabledReadinessChecksSettingsAtThirtySecondsWithoutRenewingFastWatch()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Request(now);
        watch.Observe(watch.Revision, false, true, now);
        Assert.False(watch.HasKnownWork);
        Assert.Equal(DateTime.MinValue, watch.UntilUtc);
        Assert.False(watch.ShouldPoll(now.AddSeconds(29)));
        Assert.True(watch.ShouldPoll(now.AddSeconds(30)));
        watch.Observe(watch.Revision, false, false, now.AddSeconds(30));
        Assert.False(watch.ShouldPoll(now.AddSeconds(59)));
        Assert.True(watch.ShouldPoll(now.AddSeconds(60)));
        watch.Observe(watch.Revision, true, false, now.AddSeconds(60));
        Assert.False(watch.IsDisabled);
        Assert.False(watch.ShouldPoll(now.AddSeconds(600)));
    }

    [Fact]
    public void PresenceContractAcceptsNoAdditionalAuthority()
    {
        Assert.Single(PersonalGossipTestTools.ParseCases("""
            [{"caseId":"wire","phase":"presence_contract"}]
            """));
        Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases("""
            [{"caseId":"wire","phase":"presence_contract","heroIds":["ira"]}]
            """));
    }

    [Fact]
    public void NewWorkAfterAnExpiredEmptyWindowGetsANewBoundedWatch()
    {
        var watch = new PersonalGossipWakeState();
        var now = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc);
        watch.Request(now);
        watch.Observe(watch.Revision, true, false, now.AddSeconds(1));
        Assert.False(watch.ShouldPoll(now.AddSeconds(121)));
        watch.Observe(watch.Revision, true, true, now.AddSeconds(121));
        Assert.True(watch.ShouldPoll(now.AddSeconds(240)));
        long oldRevision = watch.Revision;
        watch.Request(now.AddSeconds(200));
        watch.Observe(oldRevision, false, false, now.AddSeconds(201));
        Assert.True(watch.HasKnownWork);
        Assert.True(watch.ShouldPoll(now.AddSeconds(319)));
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"snapshot\",\"draws\":{\"share\":1}}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"tick\",\"worldDay\":100}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"sourceMarker\":\"family visit\",\"draws\":{\"share\":0}}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"sourceMarker\":\"family visit\",\"draws\":{\"friendly_recipient\":1}}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"playerTurns\":[]}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"catalog\"},{\"caseId\":\"one\",\"phase\":\"catalog\"}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"sourceMarker\":\"family visit\",\"compositionMode\":\"return_fake_success\"}]")]
    [InlineData("[{\"caseId\":\"one\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"placements\":[{\"heroId\":\"ira\",\"party\":\"main\",\"settlementId\":\"town\"}]}]")]
    public void RejectsUnboundedOrWrongPhaseAuthority(string json) =>
        Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases(json));

    [Fact]
    public void AcceptsBoundedPreconditionsWithoutGrantingConversationAuthority()
    {
        var cases = PersonalGossipTestTools.ParseCases("""
            [{"caseId":"family_setup","phase":"prepare","actorHeroId":"ira",
              "sourceMarker":"your mother's visit","draws":{"share":1,"friendly_recipient":0.8,"reply_disclosure":100},
              "relationships":[{"observerId":"ira","subjectId":"rhagaea","affinity":0}],
              "traitProfiles":[{"heroId":"ira","profile":"excitable"}],
              "placements":[{"heroId":"rhagaea","settlementId":"town_EW2"}]}]
            """);
        Assert.Single(cases);
        Assert.False(cases[0].TryGetProperty("playerTurns", out _));
    }

    [Fact]
    public void GroupUsesExactParticipantsAndUnchangedNaturalWhisperText()
    {
        var cases = PersonalGossipTestTools.ParseCases("""
            [{"caseId":"group","phase":"conversation","actorHeroId":"ira","conversationMode":"party_chat",
              "participantHeroIds":["ira","rhagaea"],"playerTurns":["*I whisper to Ira that we leave at dawn.* The road is clear."]}]
            """);
        Assert.Equal(2, cases[0].GetProperty("participantHeroIds").GetArrayLength());
        Assert.Equal("*I whisper to Ira that we leave at dawn.* The road is clear.", cases[0].GetProperty("playerTurns")[0].GetString());
        Assert.False(cases[0].TryGetProperty("promptOverride", out _));
    }

    [Fact]
    public void ActivationIsOnlyAnExplicitPreparationPrerequisite()
    {
        var cases = PersonalGossipTestTools.ParseCases("""
            [{"caseId":"family","phase":"prepare","actorHeroId":"ira","activateResidents":["ira","rhagaea"],
              "placements":[{"heroId":"ira","settlementId":"town_EW2"},{"heroId":"rhagaea","settlementId":"town_EW2"}]}]
            """);
        Assert.Equal(2, cases[0].GetProperty("activateResidents").GetArrayLength());
        Assert.False(cases[0].TryGetProperty("gossipSignals", out _));
        Assert.False(cases[0].TryGetProperty("playerTurns", out _));
    }

    [Theory]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"snapshot\",\"participantHeroIds\":[\"ira\",\"rhagaea\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"party_chat\",\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"party_chat\",\"participantHeroIds\":[\"ira\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"party_chat\",\"participantHeroIds\":[\"ira\",\"ira\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"party_chat\",\"participantHeroIds\":[\"rhagaea\",\"other\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"individual_chat\",\"participantHeroIds\":[\"ira\",\"rhagaea\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"participantHeroIds\":[\"ira\",\"rhagaea\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"conversationMode\":\"party_chat\",\"participantHeroIds\":[\"ira\",\"b\",\"c\",\"d\",\"e\",\"f\",\"g\",\"h\",\"i\"],\"playerTurns\":[\"Hello.\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"activateResidents\":[\"ira\"]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"activateResidents\":[\"ira\",\"ira\"],\"placements\":[{\"heroId\":\"ira\",\"party\":\"main\"}]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"activateResidents\":[\"ira\"],\"placements\":[{\"heroId\":\"rhagaea\",\"party\":\"main\"}]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"activateResidents\":[\"ira\"],\"placements\":[{\"heroId\":\"ira\",\"party\":\"main\"},{\"heroId\":\"ira\",\"settlementId\":\"town_EW2\"}]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"prepare\",\"actorHeroId\":\"ira\",\"activateResidents\":[]}")]
    [InlineData("{\"caseId\":\"bad\",\"phase\":\"conversation\",\"actorHeroId\":\"ira\",\"activateResidents\":[\"ira\"],\"playerTurns\":[\"Hello.\"]}")]
    public void GroupAndActivationRejectAmbiguousOrExpandedAuthority(string item) =>
        Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases("[" + item + "]"));

    [Fact]
    public void AcceptsOnlyTheFixedPostWatchDelayWithoutArbitraryDuration()
    {
        Assert.Single(PersonalGossipTestTools.ParseCases("""
            [{"caseId":"late_ready","phase":"prepare","actorHeroId":"ira",
              "sourceMarker":"a meaningful source","compositionMode":"delay_past_watch_once"}]
            """));
        Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases("""
            [{"caseId":"late_ready","phase":"prepare","actorHeroId":"ira",
              "sourceMarker":"a meaningful source","compositionMode":"delay_past_watch_once","delaySeconds":500}]
            """));
    }

    [Fact]
    public void DistinctParticipantCapAppliesAcrossFields()
    {
        string ids = string.Join(",", Enumerable.Range(0, 16).Select(i => "\"hero" + i + "\""));
        Assert.Throws<ArgumentException>(() => PersonalGossipTestTools.ParseCases(
            "[{\"caseId\":\"many\",\"phase\":\"policy_probe\",\"actorHeroId\":\"extra\",\"heroIds\":[" + ids + "]}]"));
    }

    [Fact]
    public void FixedLatencyFaultRequiresExistingPrepareAuthority()
    {
        Assert.Single(PersonalGossipTestTools.ParseCases("""
            [{"caseId":"late_answer","phase":"prepare","actorHeroId":"ira","sourceMarker":"your mother's visit","compositionMode":"delay_once"}]
            """));
        Assert.ThrowsAny<ArgumentException>(() => PersonalGossipTestTools.ParseCases("""
            [{"caseId":"late_answer","phase":"tick","compositionMode":"delay_once"}]
            """));
    }

    [Fact]
    public void ManifestIsPendingAndBalancedWithoutDuplicateCartesianExecution()
    {
        var manifest = PersonalGossipTestTools.GetManifest(TestOptions.Create(TestOptions.FindWorkspace()));
        // Structural case assertions live here; completion is always a separate evidence receipt.
        Assert.Equal("reign-personal-gossip-launch-manifest-v1", manifest.GetProperty("schema").GetString());
        string text = manifest.GetRawText();
        Assert.DoesNotContain("\"status\":\"passed\"", text, StringComparison.Ordinal);
        Assert.Contains("G72", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VerificationGatePrecedesRuntimeAccess()
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace()) with { AllowVerificationControl = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => PersonalGossipTestTools.TestPersonalGossip(
            null!, options, null!, "campaign", "owned", "batch", "[{\"caseId\":\"one\",\"phase\":\"catalog\"}]",
            confirmation: PersonalGossipTestTools.Confirmation));
    }
}
