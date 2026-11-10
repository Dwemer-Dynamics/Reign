using Reign.Mcp.Server;
using ReignBeta.Shared.Characters;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NpcLivesHarnessTests
{
    [Fact]
    public void ShipTradeGovernorCompatibilityUsesThePortAndKeepsItsOptionalNativeBoundary()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignShipTradeGovernorPatch.cs"));
        Assert.Contains("AccessTools.TypeByName(\"NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior\")", source);
        Assert.Contains("if (type == null) return;", source);
        Assert.Contains("=> oldOwnerPort ?? newOwnerPort;", source);
        Assert.Contains("=> SelectTradePort(oldOwner?.Settlement, ship?.Owner?.Settlement);", source);
        Assert.Contains("sites.Length != 1", source);
        Assert.Contains("source[sites[0] + 1].opcode != OpCodes.Ldfld", source);
        Assert.Contains("!Equals(source[sites[0] + 1].operand, town)", source);
        foreach (string forbidden in new[] { "GiveGoldAction.", "ChangeShipOwnerAction.Apply", "ChangeState(", "GovernorOf =", "CurrentSettlement =" })
            Assert.DoesNotContain(forbidden, source);
        string startup = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        Assert.Contains("ReignShipTradeGovernorPatch.Apply();", startup);
        Assert.Contains("ReignShipTradeGovernorPatch.Unapply();", startup);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("npcLives").GetProperty("shipTradeGovernorContext").GetString()!;
        foreach (string required in new[] { "OnShipOwnerChanged", "CurrentSettlement.Town", "optional", "four", "actual native campaign regression", "no provider" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Away-governor ship-trade context (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void NewMeetingValidationHonorsTheSwitchWithoutCancellingSignedCommitments()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLifeActions.cs"));
        Assert.Contains("=> enabled || agreement != null && agreement.HasBothConsents;", source);
        Assert.Contains("if (!MeetingFeatureAllows(ReignServerClient.NpcLivesOptions.Allows(\"meeting\"), a))", source);
        Assert.Contains("errors.Add(\"New summoned meetings are disabled.\");", source);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("npcLives").GetProperty("meetingFeatureGate").GetString()!;
        foreach (string required in new[] { "unsigned", "both", "18", "actual native", "no consent" }) Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("New meeting feature gate (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void AnnouncementDeliveryControlAcceptsOnlyBoundedLifecycleAndPartyIdentities()
    {
        var entry = new Dictionary<string, string> { ["caseId"] = "hold_1", ["phase"] = "announcement_transport",
            ["heroId"] = "npc_a", ["otherHeroId"] = "npc_b", ["operationId"] = "hold_1", ["control"] = "arm" };
        string Encode() => System.Text.Json.JsonSerializer.Serialize(new[] { entry });
        foreach (string control in new[] { "arm", "observe", "release" })
        {
            entry["control"] = control;
            Assert.Single(NpcLivesTestTools.ParseCases(Encode()));
        }
        foreach (string required in entry.Keys.ToArray())
        {
            string saved = entry[required]; entry.Remove(required);
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
            foreach (string invalid in new[] { "", " ", "a/b", "a\n", new string('x', 129) })
            {
                entry[required] = invalid;
                Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
            }
            entry[required] = saved;
        }
        foreach (string invalid in new[] { "Arm", "send", "ack", "release_later", "expired" })
        {
            entry["control"] = invalid;
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
        }
        entry["control"] = "arm"; entry["otherHeroId"] = entry["heroId"];
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry["otherHeroId"] = "npc_b";
        foreach (string forbidden in new[] { "text", "recipients", "members", "route", "payload", "consent", "arrivalDays",
            "saveName", "durationSeconds", "worldDay", "storyId", "outcome", "termsHash", "enrollment", "gameInstanceId" })
        {
            entry[forbidden] = "forged";
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry.Remove(forbidden);
        }
        foreach (string phase in new[] { "snapshot", "prepare", "decline", "composer", "place", "observe", "encounter" })
        {
            entry["phase"] = phase;
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
        }
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(
            "[{\"caseId\":\"x\",\"phase\":\"announcement_transport\",\"heroId\":\"a\",\"otherHeroId\":\"b\",\"operationId\":\"x\",\"control\":1}]"));
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(
            "[{\"caseId\":\"x\",\"phase\":\"announcement_transport\",\"heroId\":\"a\",\"otherHeroId\":\"b\",\"operationId\":\"x\",\"control\":\"arm\",\"control\":\"release\"}]"));
    }

    [Fact]
    public void AnnouncementControlBindsOriginalQueueAndRetiresWithoutSendingOrRearming()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesAnnouncementFixture.cs"));
        foreach (string required in new[] { "Stopwatch.StartNew()", "NativeAnnouncementHoldMaximumSeconds = 180d",
            "AnnouncementTransportScopeAllows", "!double.IsNaN(elapsedSeconds)", "!double.IsInfinity(elapsedSeconds)",
            "elapsedSeconds >= 0", "elapsedSeconds < NativeAnnouncementHoldMaximumSeconds", "AnnouncementHoldOwnsCurrent(hold)",
            "RetireAnnouncementHoldIfInvalid()", "PartyComposerDigest(payload)", "[\"sourceMemberIds\"] = AnnouncementMemberIds(payload)",
            "control == \"arm\" || hold == null || hold.Operation != operation", "[\"releaseCount\"] = 1",
            "OwnsNpcLivesSession", "IsNativeSaveInProgress", "_outbox.Count != 0", "_failedOperations.Count != 0",
            "hero.PartyBelongedTo != party", "party.MemberRoster.GetTroopCount", "members.Count > 64" })
            Assert.Contains(required, source);
        foreach (string forbidden in new[] { "QueueLifeOperation(", "Request(", "_outbox.Remove", "_outbox.Clear", "NpcAcceptedHash =",
            "PlayerAcceptedHash =", "CampaignTime.Now.Add", "Task.Delay", "Thread.Sleep" }) Assert.DoesNotContain(forbidden, source);
        string dispatch = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesBehavior.cs"));
        int boundary = dispatch.IndexOf("if (HoldAnnouncementDeliveryForNativeTest(row)) return;", StringComparison.Ordinal);
        Assert.True(boundary > 0 && boundary < dispatch.IndexOf("var p = (JObject)row[\"payload\"].DeepClone();", boundary, StringComparison.Ordinal));
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("npcLives").GetProperty("announcementTransportControl").GetString()!;
        foreach (string required in new[] { "180", "monotonic", "original", "retained recovery", "never rearms", "actual UI", "outbox drained" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
        {
            string document = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("Announcement delivery control (2026-10-25)", document);
            Assert.Contains("Unsigned meeting estimate observations (2026-10-25)", document);
        }
    }

    [Fact]
    public void MeetingEstimateEvidenceRetainsUnsignedPreviewAndReadOnlyNativeInputs()
    {
        string root = TestOptions.FindWorkspace();
        string preview = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionStructuredActionHost.cs"));
        Assert.Contains("a.PlayerAcceptedHash = \"\"; a.NpcAcceptedHash = \"\";", preview);
        Assert.Contains("[\"agreement\"] = JObject.FromObject(a)", preview);
        string snapshot = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesTestHost.cs"));
        foreach (string field in new[] { "partySpeed", "settlementGatePosition", "homeSettlementId", "homeSettlementGatePosition" })
            Assert.Contains("[\"" + field + "\"]", snapshot);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Contains("unsigned", catalog.RootElement.GetProperty("npcLives").GetProperty("nativeMeetingEstimateEvidence").GetString()!);
    }

    [Fact]
    public void PartyComposerRequiresTwoDistinctIdentitiesAndCannotCarryAnActionOrConsent()
    {
        var entry = new Dictionary<string, string> { ["caseId"] = "composer_1", ["phase"] = "composer",
            ["heroId"] = "npc_a", ["otherHeroId"] = "npc_b", ["operationId"] = "composer_1" };
        string Encode() => System.Text.Json.JsonSerializer.Serialize(new[] { entry });
        Assert.Single(NpcLivesTestTools.ParseCases(Encode()));
        foreach (string required in new[] { "heroId", "otherHeroId", "operationId" })
        {
            string saved = entry[required]; entry.Remove(required);
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry[required] = saved;
            foreach (string invalid in new[] { "", " ", "a/b", "a\n", new string('x', 129) })
            {
                entry[required] = invalid;
                Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
            }
            entry[required] = saved;
        }
        entry["otherHeroId"] = entry["heroId"];
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry["otherHeroId"] = "npc_b";
        foreach (string forbidden in new[] { "actionId", "action", "storyId", "outcome", "termsHash", "consent",
            "playerAcceptedHash", "announcement", "text", "locationId", "enrollment", "gameInstanceId", "campaignId" })
        {
            entry[forbidden] = "forged";
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry.Remove(forbidden);
        }
        foreach (string phase in new[] { "snapshot", "prepare", "decline", "place", "observe", "encounter" })
        {
            entry["phase"] = phase;
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
        }
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(
            "[{\"caseId\":\"x\",\"phase\":\"composer\",\"heroId\":\"a\",\"otherHeroId\":\"b\",\"operationId\":1}]"));
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(
            "[{\"caseId\":\"x\",\"phase\":\"composer\",\"heroId\":\"a\",\"otherHeroId\":\"b\",\"operationId\":\"x\",\"operationId\":\"y\"}]"));
    }

    [Fact]
    public void PartyComposerCatalogAndProcedureRetainTheNativeBoundaryAndNoSendAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("partyActionComposer").GetString()!;
        foreach (string required in new[] { "otherHeroId", "operationId", "actual main-party", "uncertain",
            "replayed", "currentComposer", "Conservation", "no action or consent", "retained recovery" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Party action composer fixture (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void ReplacedLeaveDraftIsDiscardedBeforeBothRoutesCachePublicationEffects()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignServer/src/Modules/Platform/Program.cs"));
        const string guard = "ConstrainNpcLeaveReply(payload, heroId, parsed, ref reply);";
        var guards = System.Text.RegularExpressions.Regex.Matches(source,
            System.Text.RegularExpressions.Regex.Escape(guard)).Select(m => m.Index).ToArray();
        Assert.Equal(2, guards.Length);
        foreach (int index in guards)
        {
            int parsed = source.LastIndexOf("Dictionary<string, object> parsed = TryParseJsonObject(content);", index, StringComparison.Ordinal);
            int memories = source.IndexOf("List<Dictionary<string, object>> memoryWrites =", parsed, StringComparison.Ordinal);
            int state = source.IndexOf("Dictionary<string, object> stateUpdates =", parsed, StringComparison.Ordinal);
            int actionGate = source.IndexOf("Dictionary<string, object> actionGate =", parsed, StringComparison.Ordinal);
            Assert.True(parsed >= 0 && parsed < index && index < memories && index < state && index < actionGate);
        }
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Contains("before deriving", catalog.RootElement.GetProperty("npcLives").GetProperty("personalLeavePublication").GetString()!);
        Assert.Contains("omitted", catalog.RootElement.GetProperty("npcLives").GetProperty("personalLeavePublication").GetString()!);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Personal leave reply publication (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void PersonalLeaveDiagnosticsAreBoundedObservationsUnderTheExistingNativeGate()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesTestHost.cs"));
        Assert.Contains("_personalTravel.Values.Where(j => j.HeroId == heroId).Take(16)", source);
        foreach (string field in new[] { "personalJourneys", "personalJourneyCount", "personalJourneysTruncated",
            "leaveDepartureReadiness", "leaveDepartureReadinessCount", "leaveDepartureReadinessTruncated",
            "destinationHostileToNpc", "destinationSafeForNpc", "blockedReason" })
            Assert.Contains("[\"" + field + "\"]", source);
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("npcLives").GetProperty("personalLeaveSafety").GetString()!;
        foreach (string required in new[] { "unsafeLeaveLocationIds", "16", "personalJourneys", "leaveDepartureReadiness",
            "no new operator", "capture", "ended service", "actual departure" }) Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Personal leave destination safety (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void ProductionRescueReleaseRequiresEveryNativeBoundaryAndNeverRepeatsAnAttempt()
    {
        // Cover all ownership, native-result, identity, custody and one-use combinations.
        for (int mask = 0; mask < 512; mask++)
        {
            bool Bit(int bit) => (mask & (1 << bit)) != 0;
            Assert.Equal(mask == 255, NpcLivesPolicy.CanReleaseBoundRescueCaptive(
                Bit(0), Bit(1), Bit(2), Bit(3), Bit(4), Bit(5), Bit(6), Bit(7), Bit(8)));
        }
    }

    [Fact]
    public void BoundRescueUsesNativePrisonerResolutionAndRetainsNormalFinalizationAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesEncounters.cs"));
        int start = source.IndexOf("internal void ReleaseBoundRescueCaptives", StringComparison.Ordinal);
        int end = source.IndexOf("private void OnStoryHideoutCompleted", start, StringComparison.Ordinal);
        string handoff = source[start..end];
        foreach (string required in new[] { "ReferenceEquals(encounter, PlayerEncounter.Current)", "Mission.Current != null",
            "ReferenceEquals(e.Value, battle)", "battle.HasWinner && battle.WinningSide == battle.PlayerSide",
            "rescueCaptureWitnessed", "rescueCaptorPartyId", "story.NativeEncounterId == entry.Key",
            "story.Bindings.TryGetValue", "ReferenceEquals(holder, capturedHolder)", "nativeRescueHandoffInspection",
            "nativeRescueHandoff", "EndCaptivityAction.ApplyByReleasedByChoice" })
            Assert.Contains(required, handoff);
        foreach (string forbidden in new[] { "ResolveStoryEncounter(", "QueueLifeOperation(", "testOutcome", "OwnsNpcLivesSession" })
            Assert.DoesNotContain(forbidden, handoff);
        Assert.Contains("[HarmonyPatch(typeof(PlayerEncounter), \"DoFreeOrCapturePrisonerHeroes\")]", source);
        string travel = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesTravel.cs"));
        Assert.Contains("Install(typeof(PlayerEncounter), \"DoFreeOrCapturePrisonerHeroes\", typeof(ReignNpcStoryRescuePrisonerPatch), \"Prefix\", true);", travel);
        Assert.Contains("RequiredHookCount = 13", travel);
        Assert.Contains("CompleteStoryNativeBinding(row)", source[end..]);
        string bindings = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignNpcStoryBindings.cs"));
        Assert.Contains("row[\"rescueCaptureWitnessed\"] = contact.IsAlive && contact.IsPrisoner", bindings);
        Assert.Contains("row[\"rescueCaptorPartyId\"] = holder.MobileParty?.StringId", bindings);
        Assert.True(bindings.IndexOf("if (!ReignNpcLivesPatches.VerifyReady())", StringComparison.Ordinal)
            < bindings.IndexOf("TakePrisonerAction.Apply(holder, contact)", StringComparison.Ordinal));
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("npcLives").GetProperty("nativeRescueHandoff").GetString()!;
        foreach (string required in new[] { "DoFreeOrCapturePrisonerHeroes", "nativeRescueHandoff", "rescueCaptureWitnessed",
            "native player victory", "normal hideout callback", "no new operator" }) Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Native rescue prisoner handoff (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void ControlledRescueCustodyRetainsItsWitnessAcrossNativeTransferWithoutClaimingForeignCustody()
    {
        bool Owns(bool witnessed = true, bool sameHero = true, bool prisoner = true,
            bool matchingHolder = false, bool missingHolder = false, bool sameBattle = true)
            => NpcLivesPolicy.ControlledRescueCustodyMatches(witnessed, sameHero, prisoner,
                matchingHolder, missingHolder, sameBattle);
        Assert.True(Owns(matchingHolder: true)); // Captured PartyBase can outlive its mobile-party component.
        Assert.True(Owns(missingHolder: true)); // Only the witnessed battle's native handoff gap.
        Assert.False(Owns()); // Another holder never becomes ours through a controlled win.
        Assert.False(Owns(missingHolder: true, sameBattle: false));
        Assert.False(Owns(missingHolder: true, witnessed: false));
        Assert.False(Owns(missingHolder: true, sameHero: false));
        Assert.False(Owns(missingHolder: true, prisoner: false));
        Assert.False(Owns(matchingHolder: true, sameHero: false));
        Assert.False(Owns(matchingHolder: true, prisoner: false));
        Assert.False(Owns(matchingHolder: true, witnessed: false));
    }

    [Fact]
    public void ControlledRescueAcknowledgementRejectsForeignCaptivesAndUnownedOrUnfinishedCombat()
    {
        bool Can(bool owned = true, bool mission = false, bool conversation = true, bool witnessed = true,
            bool captive = true, string required = "rescued", string outcome = "won", string bound = "friend", string speaking = "friend")
            => NpcLivesPolicy.CanAcknowledgeControlledRescueConversation(owned, mission, conversation, witnessed,
                captive, required, outcome, bound, speaking);
        Assert.True(Can());
        Assert.False(Can(owned: false)); // Includes retired campaign, timeline, generation or story revision.
        Assert.False(Can(mission: true));
        Assert.False(Can(conversation: false));
        Assert.False(Can(witnessed: false));
        Assert.False(Can(captive: false));
        foreach (string outcome in new[] { "", "lost", "withdrawn", "won_later" }) Assert.False(Can(outcome: outcome));
        foreach (string required in new[] { "", "won", "recovered" }) Assert.False(Can(required: required));
        foreach (string other in new[] { "", "other", "Friend", " friend" }) Assert.False(Can(speaking: other));
        Assert.False(Can(bound: "", speaking: ""));
        Assert.False(Can(bound: null!, speaking: null!));
    }

    [Fact]
    public void DeclineRequiresTheObservedOfferBindingAndCannotCarryExecutionAuthority()
    {
        var entry = new Dictionary<string, string> { ["caseId"] = "decline_1", ["phase"] = "decline",
            ["heroId"] = "npc", ["matterId"] = "leave_1", ["proposalId"] = "proposal_1",
            ["revision"] = "2", ["termsHash"] = new string('a', 64) };
        string Encode() => System.Text.Json.JsonSerializer.Serialize(new[] { entry });
        Assert.Single(NpcLivesTestTools.ParseCases(Encode()));
        foreach (string required in new[] { "heroId", "matterId", "proposalId", "revision", "termsHash" })
        {
            string value = entry[required]; entry.Remove(required);
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry[required] = value;
        }
        foreach (string revision in new[] { "0", "-1", "01", "+1", "1.5", " 1", "2147483648" })
        {
            entry["revision"] = revision;
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
        }
        entry["revision"] = "2";
        foreach (string hash in new[] { "", new string('a', 63), new string('g', 64), new string('A', 64) })
        {
            entry["termsHash"] = hash;
            Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
        }
        entry["termsHash"] = new string('a', 64);
        foreach (string forbidden in new[] { "storyId", "outcome", "waitDays", "consent", "playerAcceptedHash", "enrollment" })
        {
            entry[forbidden] = "forged";
            Assert.ThrowsAny<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode())); entry.Remove(forbidden);
        }
        entry["phase"] = "snapshot";
        Assert.Throws<ArgumentException>(() => NpcLivesTestTools.ParseCases(Encode()));
    }

    [Fact]
    public void NativeDeclineProcedureCatalogAndSecurityDescribeTheSameRestrictedChoice()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("declineSavedOffer").GetString()!;
        foreach (string field in new[] { "termsHash", "DeclineOffer", "NPC-signed", "never manufactures an offer/signature or grants consent", "60 seconds" }) Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Saved-offer refusal (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void RelocationCommerceScopeIsExactNestedAndExceptionSafe()
    {
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor(null!));
        Assert.Throws<ArgumentException>(() => NpcPartyRelocationScope.Enter(" "));
        var outer = NpcPartyRelocationScope.Enter("party");
        var inner = NpcPartyRelocationScope.Enter("party");
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("Party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        outer.Dispose(); outer.Dispose();
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        using (NpcPartyRelocationScope.Enter("other"))
        {
            inner.Dispose();
            Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
            Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        }
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using (NpcPartyRelocationScope.Enter("party")) throw new InvalidOperationException("native entry failed");
        }));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
    }

    [Fact]
    public void RelocationCommerceScopeCannotSuppressOrBeReleasedFromAnotherThread()
    {
        using var scope = NpcPartyRelocationScope.Enter("party");
        bool foreignSuppressed = true;
        Exception? releaseError = null;
        Exception? threadError = null;
        var thread = new Thread(() => threadError = Record.Exception(() =>
        {
            foreignSuppressed = NpcPartyRelocationScope.SuppressesCommerceFor("party");
            releaseError = Record.Exception(scope.Dispose);
            using (NpcPartyRelocationScope.Enter("other")) Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        }));
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(threadError);
        Assert.False(foreignSuppressed);
        Assert.IsType<InvalidOperationException>(releaseError);
        Assert.True(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("other"));
        scope.Dispose();
        Assert.False(NpcPartyRelocationScope.SuppressesCommerceFor("party"));
    }

    [Fact]
    public void MeetingInterceptionIsRegisteredAndItsReadOnlyReadinessIsCataloged()
    {
        string root = TestOptions.FindWorkspace();
        string startup = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/SubModule.cs"));
        Assert.Contains("ReignNpcLivesPatches.Apply();", startup);
        Assert.Contains("ReignNpcLivesPatches.Unapply();", startup);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("relocationEntrySafety").GetString()!;
        foreach (string field in new[] { "meetingInterception", "13 owned Harmony hooks", "relocationCommerceSuppressed", "Synchronous", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
        {
            string help = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("Summons entry safety (2026-10-12)", help);
            Assert.Contains("13 owned Harmony hooks", help);
        }
    }

    [Fact]
    public void SoloCampObservationPreservesTheGuardedSnapshotBoundary()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("soloSummonsCamps").GetString()!;
        foreach (string field in new[] { "summonsCamp", "attendanceMode", "SoloAttendance", "20", "original position", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Solo summons camps (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }

    [Fact]
    public void PartyContentPreviewsAreBoundedButDigestsCoverEveryRowRegardlessOfOrder()
    {
        var members = Enumerable.Range(0, 80).Select(i => new NpcPartyRosterEntry { CharacterId = $"troop_{i:D3}", Number = i + 1, Xp = i }).ToList();
        var prisoners = Enumerable.Range(0, 70).Select(i => new NpcPartyRosterEntry { CharacterId = $"prisoner_{i:D3}", Number = 1 }).ToList();
        var items = Enumerable.Range(0, 90).Select(i => new NpcPartyItemEntry { ItemId = $"item_{i:D3}", ModifierId = "fine", Amount = 2 }).ToList();
        var original = NpcPartyContentObservation.Capture("native_party", members, prisoners, items);
        var reordered = NpcPartyContentObservation.Capture("native_party", members.AsEnumerable().Reverse(), prisoners.AsEnumerable().Reverse(), items.AsEnumerable().Reverse());
        Assert.True(original.Truncated);
        Assert.Equal(64, original.Members.Count); Assert.Equal(64, original.Prisoners.Count); Assert.Equal(64, original.Inventory.Count);
        Assert.Equal(80, original.Digest.MemberRows); Assert.Equal(70, original.Digest.PrisonerRows); Assert.Equal(90, original.Digest.InventoryRows);
        Assert.Equal(3240, original.Digest.MemberCount); Assert.Equal(70, original.Digest.PrisonerCount); Assert.Equal(180, original.Digest.ItemCount);
        Assert.Equal(original.Digest.MembersSha256, reordered.Digest.MembersSha256);
        Assert.Equal(original.Digest.PrisonersSha256, reordered.Digest.PrisonersSha256);
        Assert.Equal(original.Digest.InventorySha256, reordered.Digest.InventorySha256);
        members[79].Wounded++; prisoners[69].Xp++; items[89].ModifierId = "rusty";
        var changed = NpcPartyContentObservation.Capture("native_party", members, prisoners, items);
        Assert.NotEqual(original.Digest.MembersSha256, changed.Digest.MembersSha256);
        Assert.NotEqual(original.Digest.PrisonersSha256, changed.Digest.PrisonersSha256);
        Assert.NotEqual(original.Digest.InventorySha256, changed.Digest.InventorySha256);
    }
    [Fact]
    public void PartyContentDigestsDistinguishIdentityQuantityInjuryExperienceAndItemVariants()
    {
        NpcPartyContentObservation Observe(string troop = "hero", int count = 2, int wounded = 1, int xp = 10,
            string item = "a|b", string modifier = "c", int amount = 3) => NpcPartyContentObservation.Capture("party",
                new[] { new NpcPartyRosterEntry { CharacterId = troop, Number = count, Wounded = wounded, Xp = xp } },
                Array.Empty<NpcPartyRosterEntry>(), new[] { new NpcPartyItemEntry { ItemId = item, ModifierId = modifier, Amount = amount } });
        var baseline = Observe();
        foreach (var changed in new[] { Observe(troop: "other"), Observe(count: 3), Observe(wounded: 0), Observe(xp: 11) })
            Assert.NotEqual(baseline.Digest.MembersSha256, changed.Digest.MembersSha256);
        foreach (var changed in new[] { Observe(item: "a", modifier: "b|c"), Observe(modifier: "fine"), Observe(amount: 4) })
            Assert.NotEqual(baseline.Digest.InventorySha256, changed.Digest.InventorySha256);
        Assert.Equal(64, baseline.Digest.MembersSha256.Length);
        Assert.NotEqual(baseline.Digest.MembersSha256, baseline.Digest.PrisonersSha256);
        Assert.False(baseline.Truncated);
        Assert.Equal("party", baseline.Digest.PartyId);
    }
    [Fact]
    public void PartyContentObservationAndRelocationDigestsAreCatalogedWithoutNewAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("partyContentObservation").GetString()!;
        foreach (string field in new[] { "partyContents", "64", "SHA-256", "all rows", "TargetRelocationBefore", "TargetRelocationAfter", "TargetRelocationDay", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Party contents observation (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact]
    public void ScheduledMeetingObservationsRetainTheExistingNativeGates()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("scheduledSummons").GetString()!;
        foreach (string field in new[] { "meetingJourneys (64)", "meetingDuty", "meetingHeld", "meetingSettlementHeld",
            "partyDecisionLock", "partyLeaderId", "partyPosition", "partyTargetSettlementId", "no new mutation or consent authority" })
            Assert.Contains(field, contract);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Scheduled summons observation (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact]
    public void ControlledCleanupEvidenceAndRecoveryAreCataloged()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = document.RootElement.GetProperty("npcLives").GetProperty("controlledEncounter").GetString()!;
        Assert.Contains("120 seconds", contract);
        Assert.Contains("settled and cleanup", contract);
        Assert.Contains("exact armed session/campaign", contract);
        string rescue = document.RootElement.GetProperty("npcLives").GetProperty("controlledRescueConversation").GetString()!;
        foreach (string field in new[] { "testRescueCapture", "testConversationAcknowledgement", "testConversationInspection",
            "prisonerHolderPresent", "same owned native battle", "Foreign holders", "lastObserved", "no consent", "at most once" })
            Assert.Contains(field, rescue);
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Controlled outcome cleanup (2026-10-12)", File.ReadAllText(Path.Combine(root, path)));
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Controlled rescue conversation cleanup (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignMcp/README.md" })
            Assert.Contains("Custody handoff (2026-10-25)", File.ReadAllText(Path.Combine(root, path)));
    }
    [Theory]
    [InlineData("observe", "")]
    [InlineData("place", "")]
    [InlineData("start", ",\"objectiveId\":\"duel\"")]
    [InlineData("finish", ",\"encounterId\":\"encounter\",\"outcome\":\"won\"")]
    [InlineData("encounter", ",\"objectiveId\":\"duel\",\"outcome\":\"won\"")]
    [InlineData("encounter", ",\"objectiveId\":\"duel\",\"outcome\":\"lost\"")]
    [InlineData("encounter", ",\"objectiveId\":\"hideout\",\"outcome\":\"withdrawn\"")]
    public void AcceptsOnlyBoundedMechanicCases(string phase, string fields) => Assert.Single(
        NpcLivesTestTools.ParseCases("[{\"caseId\":\"case_1\",\"storyId\":\"story_1\",\"phase\":\"" + phase + "\"" + fields + "}]"));
    [Theory]
    [InlineData("[{\"caseId\":\"snapshot\",\"heroId\":\"companion\",\"phase\":\"snapshot\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"authored\",\"chapterId\":\"duel\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"authored\",\"chapterId\":\"duel\",\"locationId\":\"town\"}]")]
    [InlineData("[{\"caseId\":\"prepare\",\"heroId\":\"companion\",\"phase\":\"prepare\",\"templateId\":\"outing\",\"chapterId\":\"visit\",\"invitationKind\":\"leave\"}]")]
    public void AcceptsExactCharacterSnapshotAndUnacceptedAuthoredInvitation(string input) => Assert.Single(NpcLivesTestTools.ParseCases(input));
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"place\",\"locationId\":\"other_town\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"place\",\"outcome\":\"won\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"place\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"invitationKind\":\"leave\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"outing\",\"chapterId\":\"visit\",\"invitationKind\":\"forced_departure\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"duel\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"hideout\",\"outcome\":\"rescued\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"encounter\",\"objectiveId\":\"duel\",\"outcome\":\"won\",\"encounterId\":\"forged\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\",\"chapterId\":\"duel\",\"locationId\":\"\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"locationId\":\"town\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"prepare\",\"templateId\":\"story\",\"chapterId\":\"duel\",\"consent\":\"true\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"snapshot\",\"storyId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"heroId\":\"npc\",\"phase\":\"observe\",\"storyId\":\"story\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"finish\",\"encounterId\":\"e\",\"outcome\":\"rescued\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"outcome\":\"won\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"enrollment\":\"forged\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\",\"phase\":\"finish\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\"},{\"caseId\":\"a\",\"storyId\":\"s\",\"phase\":\"observe\"}]")]
    public void RejectsUnboundedOrFabricatedAuthority(string input) => Assert.ThrowsAny<ArgumentException>(() => NpcLivesTestTools.ParseCases(input));
}
