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
        Assert.Contains("team.IsEnemyOf(playerTeam)", observer);
        Assert.Contains("ReadStructuredMissionField(null, \"player-team-valid\", () => playerTeam?.IsValid == true)", observer);
        Assert.Contains("ReadStructuredMissionField(agent, \"team-valid\", () => team?.IsValid == true)", observer);
        Assert.Contains("[\"enemyOfPlayer\"] = teamValid && playerTeamValid", observer);
        Assert.Contains("[\"teamValid\"] = teamValid", observer);
        Assert.Contains("[\"playerTeamValid\"] = playerTeamValid", observer);
        Assert.Contains("agent.CurrentWatchState", observer);
        Assert.Contains("agent.CurrentMortalityState", observer);
        Assert.Contains("agent.Mission == mission && agent.HasBeenBuilt", observer);
        Assert.Contains("[\"nativeStateAvailable\"] = nativeStateAvailable", observer);
        Assert.Contains("[\"active\"] = nativeStateAvailable ? JToken.FromObject(ReadStructuredMissionField(agent, \"active\", () => agent.IsActive())) : JValue.CreateNull()", observer);
        Assert.Contains("[\"controller\"] = nativeStateAvailable ?", observer);
        Assert.Contains("[\"position\"] = nativeStateAvailable ?", observer);
        Assert.Contains("nativeStateAvailable ? ReadStructuredMissionField(agent, \"equipment\", () => agent.Equipment) : null", observer);
        Assert.Contains("[\"equipmentAvailable\"] = equipment != null", observer);
        Assert.Contains("bool? hasMeleeWeapon = equipment == null ? (bool?)null : false", observer);
        Assert.Contains("equipment != null && slot < 5", observer);
        Assert.Contains("item?.Weapons?.Any(usage => usage != null && usage.IsMeleeWeapon)", observer);
        Assert.Contains("[\"hasMeleeWeapon\"] = hasMeleeWeapon.HasValue", observer);
        Assert.DoesNotContain("HasMeleeWeaponCached", observer);
        Assert.DoesNotContain("ContainsMeleeWeapon(", observer);
        Assert.Contains("ReadStructuredMissionField(null, \"agent-selection\"", observer);
        Assert.Contains("ReadStructuredMissionField(agent, \"watch-state\"", observer);
        Assert.Contains("ReadStructuredMissionField(agent, \"team-side\"", observer);
        Assert.Contains("ReadStructuredMissionField(agent, \"position\"", observer);
        Assert.Contains("throw new InvalidOperationException(\"Native mission observation field '\"", observer);
        Assert.Contains("catch (Exception ex)", observer);
        Assert.Contains("ArenaDuelMissionController", observer);
        Assert.Contains("removals.Where(r => ids.Contains(r.HeroId)).Take(2)", observer);
        Assert.Contains("ids.Contains(removal.AffectorHeroId)", observer);
        Assert.Contains("[\"duelAgentRemovals\"] = removalRows", observer);
        Assert.DoesNotContain("Distinct(", observer);
        foreach (var mutation in new[] { "SetMissionMode(", "SetWatchState(", "SetTeam(", "SpawnAgent(",
            "EndMission(", "RegisterBlow(", "SetTarget", "CompleteDuel", "RunOperator", "ChatWith", "GetField(", "SetValue(" })
            Assert.DoesNotContain(mutation, observer);
    }

    [Fact] public void GuideFixtureRequiresRealConsentAndCannotMoveNpcOrCompleteTheOrder()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        int start = source.IndexOf("private static JObject ApplyStructuredAcceptedGuideStepFixture", StringComparison.Ordinal);
        string fixture = source.Substring(start, source.IndexOf("private static JObject ApplyStructuredPlayerSettlementEncounterFixture", start, StringComparison.Ordinal) - start);
        foreach (string gate in new[] { "journal.OperatorAuthorized", "agreement.Origin != ActionOrigin.Conversation",
            "!agreement.HasBothConsents", "AgreementState.Waiting", "journal.CommittedHash != agreement.TermsHash()",
            "clause?.ActionId != \"show_the_way\"", "!step.Started", "step.Completed", "step.FulfilledInstallments != 0",
            "world.Value<string>(\"NegotiationId\") != agreement.Id", "retained.Value<string>(\"requestHash\") != hash" })
            Assert.Contains(gate, fixture);
        foreach (string forbidden in new[] { "RunOperator(", "Commit(", ".Completed =", ".State =", "Complete(" })
            Assert.DoesNotContain(forbidden, fixture);
        string mission = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/World/ReignSceneActionMissionBehavior.cs"));
        start = mission.IndexOf("internal JObject StepPlayerToAcceptedGuideForTest", StringComparison.Ordinal);
        string step = mission.Substring(start, mission.IndexOf("private void CacheDestinations", start, StringComparison.Ordinal) - start);
        foreach (string gate in new[] { "mission != TaleWorlds.MountAndBlade.Mission.Current", "ConversationOpen(mission)",
            "settlement.IsUnderSiege", "!order.Guide || order.Reported", "!order.Bound", "order.TargetHero != Hero.MainHero",
            "distance > 20f", "distance > 1.5f", "player.TeleportToPosition(destination)", "distanceAfter > 1.5f" })
            Assert.Contains(gate, step);
        foreach (string forbidden in new[] { "guide.TeleportToPosition", "order.Destination =", "Complete(", "SetScriptedPosition", "order.Reported =" })
            Assert.DoesNotContain(forbidden, step);
    }

    [Fact] public void PregnancyDiscoveryRequiresItsRealCounterpartAndFaultsRemainBoundedFailures()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        int start = source.IndexOf("case \"entities\":", StringComparison.Ordinal);
        string discovery = source.Substring(start, source.IndexOf("case \"status\":", start, StringComparison.Ordinal) - start);
        Assert.Contains("counterpartHeroId.Length == 0 || counterpartHeroId.Length > 128", discovery);
        Assert.Contains("ReignActionService.HeroById(counterpartHeroId) == null", discovery);
        Assert.Contains("new ActionClause { RecipientId = counterpartHeroId }", discovery);
        Assert.Contains("clause: entityClause", discovery);
        Assert.Contains("supported only for pregnancy discovery", discovery);
        foreach (string forbidden in new[] { "ExposePregnancy(", "RunOperator(", "SaveNativeOperation(", "KnownPregnancy(" })
            Assert.DoesNotContain(forbidden, discovery);
        Assert.Contains("string detail = ex.ToString()", source);
        Assert.Contains("detail.Substring(0, Math.Min(detail.Length, 4096))", source);
        Assert.Contains("LiveCommandResult.Failed(\"Structured action case failed: \"", source);
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
    [Fact] public void PlayerEncounterFixtureRequiresNativeContextAndPreservesUncertainReplay()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        int start = source.IndexOf("private static JObject ApplyStructuredPlayerSettlementEncounterFixture", StringComparison.Ordinal);
        string fixture = source.Substring(start, source.IndexOf("private static JObject ApplyStructuredSiegeFixture", start, StringComparison.Ordinal) - start);
        foreach (string safety in new[] { "party.LeaderHero != Hero.MainHero", "party.Army != null", "party.MapEvent != null",
            "party.SiegeEvent != null", "party.IsCurrentlyAtSea", "party.IsTransitionInProgress", "Mission.Current != null",
            "!settlement.IsTown", "settlement.OwnerClan?.StringId", "besieger.StringId", "besieger.MapEvent != null",
            "ReferenceEquals(besieger.SiegeEvent, siege)", "FactionManager.IsAtWarAgainstFaction" }) Assert.Contains(safety, fixture);
        Assert.True(fixture.IndexOf("behavior.NativeOperation(id)", StringComparison.Ordinal) < fixture.IndexOf("var party", StringComparison.Ordinal));
        Assert.True(fixture.IndexOf("behavior.SaveNativeOperation(id, receipt)", StringComparison.Ordinal) < fixture.IndexOf("PlayerEncounter.Finish()", StringComparison.Ordinal));
        Assert.Contains("EncounterManager.StartSettlementEncounter(party, settlement)", fixture);
        Assert.Contains("PlayerEncounter.EnterSettlement()", fixture);
        Assert.Contains("PlayerEncounter.LocationEncounter != null", fixture);
        Assert.Contains("GameMenu.ActivateGameMenu(\"town\")", fixture);
        Assert.Contains("[\"status\"] = \"uncertain\"", fixture);
        foreach (string forbidden in new[] { "GetProperty(", "GetMethod(", "SetValue(", "KillCharacter", "SetWinner", "ApplyBreakIn", "ApplyBreakOut" }) Assert.DoesNotContain(forbidden, fixture);
    }
    [Fact] public void SiegeFixtureRechecksNativeSafetyAndRetainsReceiptBeforeMutation()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        int start = source.IndexOf("private static JObject ApplyStructuredSiegeFixture", StringComparison.Ordinal);
        string fixture = source.Substring(start, source.IndexOf("private static JObject ApplyStructuredShipDamageFixture", start, StringComparison.Ordinal) - start);
        Assert.Contains("recoveryCheckpointVerified", source);
        foreach (string safety in new[] { "attacker == MobileParty.MainParty", "attacker.Army != null", "attacker.MapEvent != null",
            "attacker.SiegeEvent != null", "attacker.IsCurrentlyAtSea", "!settlement.IsFortification", "settlement.OwnerClan?.StringId",
            "FactionManager.IsAtWarAgainstFaction", "MobileParty.MainParty?.CurrentSettlement == settlement" }) Assert.Contains(safety, fixture);
        Assert.True(fixture.IndexOf("behavior.NativeOperation(id)", StringComparison.Ordinal) < fixture.IndexOf("var attacker", StringComparison.Ordinal));
        Assert.True(fixture.IndexOf("behavior.SaveNativeOperation(id, receipt)", StringComparison.Ordinal) < fixture.IndexOf("attacker.Position =", StringComparison.Ordinal));
        Assert.Contains("StartSiegeEvent(settlement, attacker)", fixture);
        Assert.Contains("ReferenceEquals(settlement.SiegeEvent, siege)", fixture);
        Assert.Contains("[\"status\"] = \"uncertain\"", fixture);
        foreach (string forbidden in new[] { "GetProperty", "GetMethod", "SetValue", "AdvanceTime", "KillCharacter", "SetWinner" }) Assert.DoesNotContain(forbidden, fixture);
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
