using System.Xml.Linq;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class StructuredActionHostTests
{
    [Fact] public void RebellionObservationKeepsExactParticipantLinksBoundsAndTheReadOnlyBoundary()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        var start = source.IndexOf("private static JObject BuildStructuredRebellionObservation", StringComparison.Ordinal);
        var observer = source.Substring(start, source.IndexOf("private static JObject BuildStructuredMissionObservation", start, StringComparison.Ordinal) - start);
        Assert.Contains("snapshot[\"rebellionState\"] = BuildStructuredRebellionObservation(ids)", source);
        Assert.Contains("reign-structured-rebellion-observation-v1", observer);
        foreach (var participant in new[] { "ids.Contains(p.PlayerHeroStringId)", "ids.Contains(p.RulerHeroStringId)",
            "plotIds.Contains(p.PlotId) && ids.Contains(p.LordHeroStringId)", "ids.Contains(m.LeaderHeroStringId)",
            "ids.Contains(m.OriginalRulerHeroStringId)", "plotIds.Contains(s.PlotId)" })
            Assert.Contains(participant, observer);
        foreach (var collection in new[] { "plots", "pledges", "memberships", "movements", "summonses" })
        {
            Assert.Contains(collection + ".Take(64)", observer);
            Assert.Contains(collection + ".Count > 64", observer);
        }
        Assert.Contains("behavior != null", observer);
        Assert.Contains("actualClanAvailable", observer);
        Assert.Contains("actualParentAvailable", observer);
        Assert.Contains("actualRebelAvailable", observer);
        Assert.Contains("FactionManager.IsAtWarAgainstFaction(parent, rebel)", observer);
        foreach (var forbidden in new[] { "JObject.FromObject(", ".DecisionReason", ".LastReason", ".DemandJson",
            ".ExecutionSnapshotJson", ".OriginalClanKingdomsJson", ".BackingTerminalReason", "GetField(",
            "TryRecord", "TryDeclare", "TryRecruit", "TryJoin", "TryResolve", "ResolveNegotiated",
            "RunPreparationHarnessProfile", "ChangeKingdomAction", "ChangeRulingClanAction", "Commit(",
            "RunOperator", "OnHourlyTick", "ChatWith", "SaveNativeOperation", "KillCharacterAction" })
            Assert.DoesNotContain(forbidden, observer);
    }

    [Fact] public void NativeMissionObservationIsBoundedReadOnlyAndKeepsDuplicateAgentsVisible()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        var observer = source.Substring(source.IndexOf("private static JObject BuildStructuredMissionObservation", StringComparison.Ordinal));
        Assert.Contains("snapshot[\"nativeMission\"] = BuildStructuredMissionObservation(ids)", source);
        Assert.Contains("ids.Contains(a.Character.StringId)", observer);
        Assert.Contains("agents.Take(32)", observer);
        Assert.Contains("agents.Count > 32", observer);
        Assert.Contains("slot < 4", observer);
        Assert.Contains("reign-structured-mission-observation-v1", observer);
        Assert.Contains("agent.Team.IsEnemyOf(mission.PlayerTeam)", observer);
        Assert.Contains("agent.CurrentWatchState", observer);
        Assert.Contains("agent.CurrentMortalityState", observer);
        Assert.Contains("ArenaDuelMissionController", observer);
        Assert.Contains("removals.Where(r => ids.Contains(r.HeroId)).Take(2)", observer);
        Assert.Contains("ids.Contains(removal.AffectorHeroId)", observer);
        Assert.Contains("[\"duelAgentRemovals\"] = removalRows", observer);
        Assert.DoesNotContain("Distinct(", observer);
        foreach (var mutation in new[] { "SetMissionMode(", "SetWatchState(", "SetTeam(", "SpawnAgent(",
            "EndMission(", "RegisterBlow(", "SetTarget", "CompleteDuel", "RunOperator", "ChatWith", "GetField(", "SetValue(" })
            Assert.DoesNotContain(mutation, observer);
    }

    [Fact] public void CourtAndMissionObserversKeepParticipantBoundsAndDoNotExecuteOrExposePrivateProse()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        var observer = source.Substring(source.IndexOf("private static JObject BuildStructuredActionNativeSnapshot", StringComparison.Ordinal));
        foreach (var filter in new[] { "ids.Contains(o.HeroStringId)", "ids.Contains(r.HeroStringId)",
            "ids.Contains(a.HeroStringId)", "ids.Contains(chancellor.HeroId)", "ids.Contains(h.ChancellorHeroId)",
            "ids.Contains(m.SpymasterHeroStringId)", "ids.Contains(e.AgentHeroStringId)" })
            Assert.Contains(filter, observer);
        foreach (var collection in new[] { "offices", "regents", "ambassadors", "serviceHistory", "missions", "effects" })
        {
            Assert.Contains(collection + ".Take(64)", observer);
            Assert.Contains(collection + ".Count > 64", observer);
        }
        foreach (var schema in new[] { "reign-structured-court-observation-v1", "reign-structured-spymaster-observation-v1", "reign-structured-spymaster-effects-v1" })
            Assert.Contains(schema, observer);
        foreach (var forbidden in new[] { "TrySchedule", "TryAppoint", "TryDismiss", "ResolveMission", "RunOperator",
            "Commit(", "ChatWith", ".ReportJson", ".Summary", ".Detail", ".FabricatedText", ".MissionTermsJson", ".ResultSummary" })
            Assert.DoesNotContain(forbidden, observer);
        Assert.Contains("#if !REIGN_EXCLUDE_COURT", observer);
        Assert.Contains("chancellor.DesiredActiveEffectiveDay", observer);
        Assert.Contains("chancellor.TotalPaidGold", observer);
        Assert.Contains("m.AgreementEffectState", observer);
    }

    [Fact] public void OrderAndTreatyObservationsAreScopedBoundedAndDoNotExposeProse()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        var observer = source.Substring(source.IndexOf("private static JObject BuildStructuredActionNativeSnapshot", StringComparison.Ordinal));
        Assert.Contains("ids.Contains(o.CommanderHeroStringId) || ids.Contains(o.IssuerHeroStringId)", observer);
        Assert.Contains("kingdoms.Contains(a.ActorKingdomStringId) && kingdoms.Contains(a.TargetKingdomStringId)", observer);
        Assert.Contains("orders.Take(64)", observer);
        Assert.Contains("treaties.Take(64)", observer);
        Assert.Contains("orders.Count > 64", observer);
        Assert.Contains("treaties.Count > 64", observer);
        Assert.Contains("kingdoms.Contains(k.StringId)", observer);
        Assert.Contains("nativePairs.Count < 64", observer);
        Assert.Contains("nativePairTotal > 64", observer);
        Assert.Contains("nativeAlliance.GetAllianceEndDate(first, second).ToDays", observer);
        Assert.Contains("nativeTrade.GetTradeAgreementEndDate(first, second).ToDays", observer);
        foreach (var schema in new[] { "reign-structured-order-observation-v1", "reign-structured-treaty-observation-v1",
            "reign-structured-native-diplomacy-observation-v1" })
            Assert.Contains(schema, observer);
        foreach (var privateField in new[] { ".TermsJson", ".Reason", ".HistoryJson", ".PendingReportText", ".LlmReason" })
            Assert.DoesNotContain(privateField, observer);
    }
    [Fact] public void ShipFixtureRechecksNativeSafetyAndRetainsReceiptBeforeMutation()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        var start = source.IndexOf("private static JObject ApplyStructuredShipDamageFixture", StringComparison.Ordinal);
        var fixture = source.Substring(start, source.IndexOf("private static JObject BuildStructuredActionNativeSnapshot", start, StringComparison.Ordinal) - start);
        Assert.Contains("owners.Count != 1", fixture);
        Assert.Contains("before != expected", fixture);
        Assert.Contains("damaged >= before", fixture);
        foreach (var safety in new[] { "party.MapEvent != null", "party.SiegeEvent != null", "party.IsTransitionInProgress", "ship.IsUsedByQuest" })
            Assert.Contains(safety, fixture);
        Assert.True(fixture.IndexOf("behavior.NativeOperation(id)", StringComparison.Ordinal) < fixture.IndexOf("var owners", StringComparison.Ordinal));
        Assert.True(fixture.IndexOf("behavior.SaveNativeOperation(id, receipt)", StringComparison.Ordinal) < fixture.IndexOf("ship.HitPoints = damaged", StringComparison.Ordinal));
        Assert.Contains("[\"status\"] = \"uncertain\"", fixture);
        Assert.DoesNotContain("GetProperty", fixture);
        Assert.DoesNotContain("GetMethod", fixture);
        Assert.DoesNotContain("SetValue", fixture);
    }
    [Fact] public void GuestObservationRemainsBoundedReadOnlyAndInsideNativeEnrollmentGate()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionStructuredActionHost.cs"));
        Assert.Contains("expected != ActiveSaveName()", source);
        Assert.Contains("TimeControlMode != CampaignTimeControlMode.Stop", source);
        Assert.Contains("[\"before\"] = BuildStructuredActionNativeSnapshot(c)", source);
        Assert.Contains("evidence[\"after\"] = BuildStructuredActionNativeSnapshot(c)", source);
        var observer = source.Substring(source.IndexOf("private static JObject BuildStructuredActionNativeSnapshot", StringComparison.Ordinal));
        Assert.Contains("testCase[\"heroIds\"]", observer);
        Assert.Contains("Distinct(StringComparer.Ordinal).Take(16)", observer);
        Assert.Contains("BuildConversationContext", observer);
        Assert.Contains("reign-structured-guest-observation-v1", observer);
        foreach (var forbidden in new[] { "RunDisposableLiveTestHourlyTick", "OnHourlyTick", "RunOperator", "Commit(", "ChatWith", "[\"purpose\"]", "[\"pendingTerms\"]" })
            Assert.DoesNotContain(forbidden, observer);
    }
    [Theory]
    [InlineData("ReignIndividualChatScreen", "InputText")]
    [InlineData("ReignPartyChatScreen", "InputText")]
    [InlineData("ReignCastleChatScreen", "InputText")]
    [InlineData("ReignCorrespondenceScreen", "InputText")]
    [InlineData("ReignSocialEventScreen", "InputText")]
    [InlineData("ReignCourtPetitionScreen", "InputText")]
    [InlineData("ReignRoyalCouncilScreen", "InputText")]
    [InlineData("ReignWarCouncilScreen", "MessengerInputText")]
    [InlineData("ReignTavernHouseScreen", "InputText")]
    public void EveryRenderedConversationComposerHasProtectedRangeAndSemanticStyles(string prefab, string field)
    {
        string workspace = TestOptions.FindWorkspace();
        var xml = XDocument.Load(Path.Combine(workspace, "ReignBeta", "GUI", "Prefabs", prefab + ".xml"));
        var input = Assert.Single(xml.Descendants(), e => (string?)e.Attribute("Text") == "@" + field);
        Assert.Equal("ReignActionEditableTextWidget", input.Name.LocalName);
        Assert.Equal("@ActionComposerId", (string?)input.Attribute("ComposerId"));
        Assert.Equal("ReignSerifDynamic", (string?)input.Attribute("Brush.Font"));
        var brushes = XDocument.Load(Path.Combine(workspace, "ReignBeta", "GUI", "Brushes", "ReignChatActions.xml"));
        var brush = Assert.Single(brushes.Descendants("Brush"), b => (string?)b.Attribute("Name") == (string?)input.Attribute("Brush"));
        var normal = Assert.Single(brush.Descendants("Style"), s => (string?)s.Attribute("Name") == "Default");
        var action = Assert.Single(brush.Descendants("Style"), s => (string?)s.Attribute("Name") == "Action");
        Assert.Equal("#C5BDAFFF", (string?)normal.Attribute("FontColor"));
        Assert.Equal("#FFFFFFFF", (string?)action.Attribute("FontColor"));
        Assert.Equal((string?)input.Attribute("Brush.FontSize"), (string?)action.Attribute("FontSize"));
    }
}
