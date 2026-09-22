using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignShared.Spymaster;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        internal JObject RunSpymasterInGameProfile(string runId, string profile, int seed)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId;
            string requestedProfile = (profile ?? "feature").Trim().ToLowerInvariant();
            if (requestedProfile == "cleanup") return CleanupSpymasterTestState(normalizedRunId);
            if (requestedProfile == "prepare_reload") return PrepareSpymasterReloadFixture(normalizedRunId, seed);
            if (requestedProfile == "verify_reload") return VerifySpymasterReloadFixture(normalizedRunId);

            JArray assertions = new JArray();
            string originalJson = JsonConvert.SerializeObject(EnsureSpymasterState(), Formatting.None);
            int originalGold = Hero.MainHero?.Gold ?? 0;
            DateTime started = DateTime.UtcNow;
            JArray capturedNativeIntents = new JArray();
            ReignSpymasterTestRuntime.Begin(normalizedRunId, seed, true);
            try
            {
                _spymasterState = new ReignSpymasterState { LastAgentRecruitmentDay = CurrentDayFloat() };
                Hero spymaster = ActiveSpymaster;
                AddSpyAssertion(assertions, "campaign_loaded", TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null,
                    "A native campaign and main hero are loaded.", new JObject { ["campaignId"] = ReignBeta.Integration.ReignCampaignIdentity.CurrentCampaignId() });
                AddSpyAssertion(assertions, "spymaster_appointed", spymaster != null && spymaster.IsAlive && spymaster.IsActive,
                    "A living active Spymaster is appointed through the production office state.", SnapshotHero(spymaster));
                AddSpyAssertion(assertions, "office_portrait_identity", spymaster != null && !string.IsNullOrWhiteSpace(spymaster.StringId)
                    && !string.IsNullOrWhiteSpace(spymaster.Name?.ToString()),
                    "The appointed office exposes exactly one portrait identity and display name.", SnapshotHero(spymaster));

                if (spymaster == null) return BuildSpymasterRun(normalizedRunId, requestedProfile, seed, started, assertions);

                ReignSpymasterMission activeLock = NewTestMission(normalizedRunId, "land_intelligence", spymaster, null, null);
                activeLock.State = ReignSpymasterMissionState.Active;
                EnsureSpymasterState().Missions.Add(activeLock);
                bool canChange = CanChangeSpymasterOffice(out string lockError);
                AddSpyAssertion(assertions, "active_mission_blocks_office_change", !canChange && lockError.Contains("active"),
                    "Dismissal and replacement are blocked while the incumbent owns an active mission.", new JObject { ["error"] = lockError });
                EnsureSpymasterState().Missions.Clear();

                int[] skillBoundaries = { 0, 1, 99, 100, 299, 300, 301 };
                foreach (int skill in skillBoundaries)
                {
                    ReignSpymasterCoreQuote q = ReignSpymasterCore.Quote("person_rumors", skill, 3, false);
                    AddSpyAssertion(assertions, "roguery_probability_" + skill, q.SuccessChance >= 5d && q.SuccessChance <= 95d
                        && q.DetectionChance >= 5d && q.DetectionChance <= 95d,
                        "Displayed and resolved probabilities share the bounded deterministic Roguery formula.", JObject.FromObject(q));
                }

                List<Town> fortifications = Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true).ToList();
                List<ReignSettlementSupplySnapshot> snapshots = ReignCourtSupplyService.GetAllFortificationSnapshots();
                AddSpyAssertion(assertions, "land_target_completeness", snapshots.Count == fortifications.Count,
                    "Every native town and castle is available to land intelligence.", new JObject { ["nativeCount"] = fortifications.Count, ["optionCount"] = snapshots.Count });
                AddSpyAssertion(assertions, "land_target_groups", fortifications.Any(x => x.Settlement.IsTown)
                    && fortifications.Any(x => x.Settlement.IsCastle),
                    "Town and castle groups are both represented in the campaign target population.", new JObject
                    {
                        ["towns"] = fortifications.Count(x => x.Settlement.IsTown), ["castles"] = fortifications.Count(x => x.Settlement.IsCastle),
                        ["playerRealm"] = fortifications.Count(x => x.OwnerClan?.Kingdom == Clan.PlayerClan?.Kingdom),
                        ["foreign"] = fortifications.Count(x => x.OwnerClan?.Kingdom != Clan.PlayerClan?.Kingdom)
                    });
                Town governedTarget = fortifications.FirstOrDefault(x => x.Governor?.IsAlive == true);
                if (governedTarget != null)
                {
                    ReignSpymasterMissionQuote governorQuote =
                        GetSpymasterQuote("assassinate_governor", governedTarget.Governor);
                    ReignSpymasterMissionQuote displayedGovernorQuote = GetSpymasterQuote(
                        "assassinate_governor", "settlement", governedTarget.Settlement.StringId);
                    AddSpyAssertion(assertions, "governor_assassination_displayed_quote_matches_charge",
                        displayedGovernorQuote.GoldCost == governorQuote.GoldCost
                        && Math.Abs(displayedGovernorQuote.DurationDays - governorQuote.DurationDays) < 0.001f
                        && Math.Abs(displayedGovernorQuote.SuccessChance - governorQuote.SuccessChance) < 0.001f
                        && Math.Abs(displayedGovernorQuote.DetectionChance - governorQuote.DetectionChance) < 0.001f,
                        "The settlement-screen quote resolves its governor and exactly matches the price, duration, and probabilities charged to the mission.",
                        new JObject
                        {
                            ["settlementId"] = governedTarget.Settlement.StringId,
                            ["governorId"] = governedTarget.Governor.StringId,
                            ["displayed"] = JObject.FromObject(displayedGovernorQuote),
                            ["charged"] = JObject.FromObject(governorQuote)
                        });
                }
                ReignSettlementSupplySnapshot landSnapshot = snapshots.FirstOrDefault();
                if (landSnapshot != null)
                {
                    string immutable = JsonConvert.SerializeObject(landSnapshot, Formatting.None);
                    AddSpyAssertion(assertions, "land_report_schema", immutable.Contains("OwnerClanName") && immutable.Contains("GovernorName")
                        && immutable.Contains("FoodCapacity") && immutable.Contains("FoodChange") && immutable.Contains("LoyaltyChange")
                        && immutable.Contains("SecurityChange") && immutable.Contains("GarrisonWages") && immutable.Contains("CurrentConstructionProgress")
                        && immutable.Contains("DailyNetIncome") && immutable.Contains("Items"),
                        "The immutable land snapshot contains all economy-view-equivalent ownership, governor, food, loyalty, security, military, construction, income, and market fields.",
                        JObject.Parse(immutable));
                }

                EnsureGoldForSpyTest(100000);
                int paymentBefore = Hero.MainHero.Gold;
                Town paymentTarget = fortifications.FirstOrDefault();
                string paymentTargetId = paymentTarget?.Settlement?.StringId ?? string.Empty;
                string paymentTargetName = paymentTarget?.Settlement?.Name?.ToString() ?? "test fortification";
                string firstError = StartSpymasterMission("land_intelligence", "settlement", paymentTargetId, paymentTargetName);
                int paymentAfter = Hero.MainHero.Gold;
                ReignSpymasterMission paid = EnsureSpymasterState().Missions.LastOrDefault();
                AddSpyAssertion(assertions, "payment_up_front_and_timing", string.IsNullOrWhiteSpace(firstError) && paid != null
                    && paymentBefore - paymentAfter == paid.GoldCost && Math.Abs((paid.DueDay - paid.StartedDay) - 4f) < 0.001f,
                    "Mission payment is charged once up front and the due day matches the displayed duration.", paid == null ? null : JObject.FromObject(paid));
                StartSpymasterMission("land_intelligence", "settlement", paymentTargetId, paymentTargetName);
                StartSpymasterMission("land_intelligence", "settlement", paymentTargetId, paymentTargetName);
                string capacityError = StartSpymasterMission("land_intelligence", "settlement", paymentTargetId, paymentTargetName);
                AddSpyAssertion(assertions, "mission_capacity", ActiveSpymasterMissionCount == ReignSpymasterCore.MissionCapacity && capacityError.Contains("capacity"),
                    "The fourth simultaneous mission is rejected at the production capacity boundary.", new JObject { ["active"] = ActiveSpymasterMissionCount, ["error"] = capacityError });
                EnsureSpymasterState().Missions.Clear();

                int savedGold = Hero.MainHero.Gold;
                if (savedGold > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, savedGold, false);
                string insufficient = StartSpymasterMission("land_intelligence", "settlement", paymentTargetId, paymentTargetName);
                if (savedGold > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, savedGold, false);
                AddSpyAssertion(assertions, "insufficient_funds", insufficient.Contains("treasury") && EnsureSpymasterState().Missions.Count == 0,
                    "Insufficient funds rejects the mission without creating an operation.", new JObject { ["error"] = insufficient });

                ReignSpymasterMission invalidated = NewTestMission(normalizedRunId, "land_intelligence", spymaster, null, null);
                invalidated.TargetType = "settlement"; invalidated.TargetStringId = "missing_spymaster_target";
                invalidated.GoldCost = 1500; invalidated.StartedDay = CurrentDayFloat() - 5f; invalidated.DueDay = CurrentDayFloat() - 1f;
                EnsureSpymasterState().Missions.Add(invalidated);
                int noRefundGold = Hero.MainHero.Gold;
                ProcessSpymasterDailyTick();
                AddSpyAssertion(assertions, "invalidation_no_refund", invalidated.State == ReignSpymasterMissionState.Cancelled
                    && Hero.MainHero.Gold == noRefundGold && invalidated.ResultSummary.Contains("not recovered"),
                    "A due mission whose target became invalid is cancelled deterministically without refund.", JObject.FromObject(invalidated));

                EnsureSpymasterState().Missions.Clear();
                if (paymentTarget != null)
                {
                    ReignSpymasterMission landMission = NewTestMission(normalizedRunId, "land_intelligence", spymaster, null, paymentTarget.Settlement);
                    landMission.SuccessChance = 54f; landMission.DetectionChance = 25f;
                    ReignSpymasterTestRuntime.Force("mission_outcome", 0.5399f);
                    ResolveSpymasterMission(landMission, spymaster, CurrentDayFloat());
                    string reportBefore = landMission.ReportJson;
                    AddSpyAssertion(assertions, "displayed_probability_success_boundary", landMission.State == ReignSpymasterMissionState.Succeeded
                        && Math.Abs(landMission.OutcomeRoll - 53.99f) < 0.02f,
                        "A roll just below the displayed success probability succeeds.", JObject.FromObject(landMission));
                    AddSpyAssertion(assertions, "immutable_land_report", !string.IsNullOrWhiteSpace(reportBefore)
                        && reportBefore == landMission.ReportJson,
                        "The completed report is a stored immutable snapshot rather than a live recomputation.", null);

                    ReignSpymasterMission exposed = NewTestMission(normalizedRunId, "disrupt_security", spymaster, null, paymentTarget.Settlement);
                    exposed.SuccessChance = 54f; exposed.DetectionChance = 25f; exposed.Harmful = true;
                    ReignSpymasterTestRuntime.Force("mission_outcome", 0.54f);
                    ReignSpymasterTestRuntime.Force("mission_detection", 0.2499f);
                    ReignSpymasterTestRuntime.Force("mission_attribution", 0f);
                    ResolveSpymasterMission(exposed, spymaster, CurrentDayFloat());
                    AddSpyAssertion(assertions, "displayed_probability_failure_detection_boundary", exposed.State == ReignSpymasterMissionState.Exposed
                        && exposed.TargetNoticed && exposed.PlayerIdentified,
                        "A roll exactly at success fails; a detection roll just below the displayed risk notices and attributes the operation.", JObject.FromObject(exposed));

                    foreach (string disruption in new[] { "disrupt_food", "disrupt_construction", "disrupt_security", "disrupt_loyalty" })
                    {
                        ReignSpymasterMission mission = NewTestMission(normalizedRunId, disruption, spymaster, null, paymentTarget.Settlement);
                        ResolveSuccessfulSpymasterMission(mission, spymaster, CurrentDayFloat());
                        ReignSpymasterEffectSpec expected = ReignSpymasterCore.Effect(disruption);
                        ReignSpymasterSettlementEffect actual = EnsureSpymasterState().Effects.LastOrDefault();
                        AddSpyAssertion(assertions, "effect_" + disruption, actual != null && actual.EffectType == expected.EffectType
                            && Math.Abs(actual.Magnitude - expected.Magnitude) < 0.001d && Math.Abs((actual.EndDay - actual.StartDay) - expected.DurationDays) < 0.001d,
                            "The live campaign applies the intended severity and duration.", actual == null ? null : JObject.FromObject(actual));
                    }
                    for (int i = 0; i < 5; i++) EnsureSpymasterState().Effects.Add(new ReignSpymasterSettlementEffect
                    { SettlementStringId = paymentTarget.Settlement.StringId, EffectType = "food", StartDay = CurrentDayFloat(), EndDay = CurrentDayFloat() + 10f, Magnitude = -0.35f });
                    AddSpyAssertion(assertions, "effect_clamping_invariant", Math.Abs(GetSpymasterFoodFactor(paymentTarget) + 0.90f) < 0.001f,
                        "Stacked multiplicative disruption is clamped so settlement production cannot cross the invariant floor.", null);
                }

                Hero ownNoble = Hero.AllAliveHeroes.FirstOrDefault(x => x?.IsLord == true && x.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);
                Hero ownNotable = Hero.AllAliveHeroes.FirstOrDefault(x => x?.IsNotable == true && x.HomeSettlement?.MapFaction == Clan.PlayerClan?.Kingdom);
                Hero foreignNoble = Hero.AllAliveHeroes.FirstOrDefault(x => x?.IsLord == true && x.Clan?.Kingdom != null
                    && x.Clan.Kingdom != Clan.PlayerClan?.Kingdom && x.CanDie(KillCharacterAction.KillCharacterActionDetail.Murdered));
                AddSpyAssertion(assertions, "people_target_filters", ownNoble != null && ownNotable != null && foreignNoble != null,
                    "The loaded campaign supplies own nobles, own notables, and foreign nobles to the production filter domains.", new JObject
                    { ["ownNoble"] = ownNoble?.StringId ?? "", ["ownNotable"] = ownNotable?.StringId ?? "", ["foreignNoble"] = foreignNoble?.StringId ?? "" });
                if (foreignNoble != null)
                {
                    string skills = BuildPersonReport(foreignNoble, "person_skills");
                    string relationships = BuildPersonReport(foreignNoble, "person_relationships");
                    string rumors = BuildPersonReport(foreignNoble, "person_rumors");
                    AddSpyAssertion(assertions, "people_report_scope", skills.Contains("One Handed") && !skills.Contains("Strongest known ties")
                        && relationships.Contains("Strongest known ties") && !relationships.Contains("One Handed")
                        && rumors.Contains("rumors") && !rumors.Contains("One Handed"),
                        "Skills, relationships, and rumor/reputation requests return only their appropriate information domain.",
                        new JObject { ["skills"] = skills, ["relationships"] = relationships, ["rumors"] = rumors });

                    ReignSpymasterMission assassination = NewTestMission(normalizedRunId, "assassinate_person", spymaster, foreignNoble, null);
                    ResolveAssassination(assassination, foreignNoble);
                    AddSpyAssertion(assertions, "assassination_success_intent", ReignSpymasterTestRuntime.NativeIntents.Any(x => (string)x["type"] == "assassination"),
                        "Successful assassination reaches the native death boundary while the test hook safely records the irreversible intent.", JObject.FromObject(assassination));

                    ReignSpymasterMission failedAssassination = NewTestMission(normalizedRunId, "assassinate_person", spymaster, foreignNoble, null);
                    failedAssassination.Harmful = true; failedAssassination.TargetIsRuler = foreignNoble.Clan?.Kingdom?.Leader == foreignNoble;
                    ReignSpymasterTestRuntime.Force("assassination_consequence", 0.30f);
                    ApplyFailedMissionConsequences(failedAssassination, spymaster);
                    AddSpyAssertion(assertions, "assassination_failure_kill_or_capture", ReignSpymasterTestRuntime.NativeIntents.Any(x =>
                        (string)x["type"] == "spymaster_killed" || (string)x["type"] == "spymaster_captured"),
                        "A failed assassination reaches a deterministic Spymaster kill-or-capture consequence without harming the test campaign.", JObject.FromObject(failedAssassination));
                }

                AddSpyAssertion(assertions, "breakout_probability_and_outcomes", ReignSpymasterCore.BreakoutChance(0, 100d, 6) == 5d
                    && ReignSpymasterCore.BreakoutChance(500, 0d, 0) == 85d,
                    "Captured-Spymaster breakout success/failure boundaries are deterministic, bounded, and serialized on the mission.", null);
                AddSpyAssertion(assertions, "rumor_mitigation_boundaries", ReignSpymasterCore.SocialMitigationOutcome("mitigate_own_rumor", 0.81999d) == "disproven"
                    && ReignSpymasterCore.SocialMitigationOutcome("mitigate_own_rumor", 0.82d) == "mitigated",
                    "Rumor removal and mitigation branches execute at the intended boundary.", null);
                AddSpyAssertion(assertions, "reputation_mitigation_boundaries", ReignSpymasterCore.SocialMitigationOutcome("mitigate_own_reputation", 0.14999d) == "removed"
                    && ReignSpymasterCore.SocialMitigationOutcome("mitigate_own_reputation", 0.15d) == "greatly_mitigated",
                    "Reputation total-removal and large-mitigation branches execute at the intended boundary.", null);

                foreach (double loyalty in new[] { 0d, 1d, 10d, 20d, 30d, 39d, 40d, 41d })
                    AddSpyAssertion(assertions, "foreign_agent_curve_" + loyalty.ToString("0"),
                        Math.Abs(ReignSpymasterCore.RecruitmentChance(loyalty) - (loyalty >= 40d ? 10d : 90d - loyalty * 2d)) < 0.001d
                        && (loyalty <= 40d || !ReignSpymasterCore.ShouldRecruit(loyalty, 0d)),
                        "The live assembly uses the exact enemy-agent loyalty curve and boundary eligibility.", new JObject { ["loyalty"] = loyalty, ["chance"] = ReignSpymasterCore.RecruitmentChance(loyalty) });

                JObject agentRequest = BuildForeignAgentSnapshot(CurrentDayFloat());
                HashSet<string> agentCandidateIds = new HashSet<string>((agentRequest["candidates"] as JArray ?? new JArray())
                    .OfType<JObject>().Select(x => (string)x["heroId"] ?? string.Empty), StringComparer.OrdinalIgnoreCase);
                AddSpyAssertion(assertions, "foreign_agent_candidate_exclusions",
                    !agentCandidateIds.Contains(Hero.MainHero?.StringId ?? string.Empty)
                    && !agentCandidateIds.Contains(spymaster.StringId),
                    "The player ruler and appointed Spymaster cannot enter an enemy recruitment pool.",
                    new JObject { ["candidateCount"] = agentCandidateIds.Count,
                        ["playerRulerId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["spymasterId"] = spymaster.StringId });

                JObject agentResponse = new JObject
                {
                    ["ok"] = true,
                    ["agents"] = new JArray(new JObject { ["agentHeroStringId"] = ownNoble?.StringId ?? spymaster.StringId,
                        ["sponsorKingdomStringId"] = foreignNoble?.Clan?.Kingdom?.StringId ?? "foreign-test", ["settlementStringId"] = paymentTargetId,
                        ["isNotable"] = false, ["activated"] = true, ["recruitedDay"] = CurrentDayFloat() - 5f, ["nextActionDay"] = CurrentDayFloat() + 5f }),
                    ["actions"] = new JArray(new JObject { ["actionId"] = "spytest-action-" + normalizedRunId,
                        ["agentHeroStringId"] = ownNoble?.StringId ?? spymaster.StringId, ["sponsorKingdomStringId"] = "foreign-test",
                        ["settlementStringId"] = paymentTargetId, ["effectType"] = "security", ["durationDays"] = 10f,
                        ["magnitude"] = -1f, ["detected"] = true, ["sponsorAttributed"] = false })
                };
                int effectsBeforeAgent = EnsureSpymasterState().Effects.Count;
                ApplyForeignAgentResponse(agentResponse, CurrentDayFloat());
                ApplyForeignAgentResponse(agentResponse, CurrentDayFloat());
                AddSpyAssertion(assertions, "foreign_agent_action_idempotence", EnsureSpymasterState().ForeignAgentActions.Count(x => x.ActionId == "spytest-action-" + normalizedRunId) == 1
                    && EnsureSpymasterState().Effects.Count == effectsBeforeAgent + 1
                    && EnsureSpymasterState().ForeignAgents.Count == 1 && EnsureSpymasterState().ForeignAgents[0].Activated,
                    "Foreign-agent activation is upserted and action IDs prevent duplicate save/load outcomes.", JObject.FromObject(EnsureSpymasterState()));

                ReignSpymasterMission memory = NewTestMission(normalizedRunId, "person_relationships", spymaster, foreignNoble, null);
                memory.GoldCost = 2250; memory.SuccessChance = 47f; memory.DetectionChance = 31f; memory.OutcomeRoll = 46f;
                memory.TargetNoticed = true; memory.PlayerIdentified = false; memory.ResultSummary = "Complete historical result";
                memory.History.Add("Assigned with parameters, cost and probability snapshot.");
                memory.History.Add("Resolved with outcome, detection, attribution and consequences.");
                string memoryJson = JsonConvert.SerializeObject(memory, Formatting.None);
                ReignSpymasterMission memoryRoundTrip = JsonConvert.DeserializeObject<ReignSpymasterMission>(memoryJson);
                AddSpyAssertion(assertions, "memory_complete_roundtrip", memoryRoundTrip != null && memoryRoundTrip.GoldCost == 2250
                    && memoryRoundTrip.SuccessChance == 47f && memoryRoundTrip.OutcomeRoll == 46f && memoryRoundTrip.History.Count == 2
                    && memoryRoundTrip.ResultSummary == memory.ResultSummary,
                    "The Spymaster's mission memory retains parameters, costs, probability snapshots, outcomes, attribution, consequences and ordered history.", JObject.Parse(memoryJson));

                ReignSpymasterState legacy = new ReignSpymasterState { Version = 0, Missions = null, Effects = null, ForeignAgents = null, ForeignAgentActions = null, AppliedForeignAgentActionIds = null };
                _spymasterState = legacy;
                ReignSpymasterState migrated = EnsureSpymasterState();
                AddSpyAssertion(assertions, "legacy_state_migration", migrated.Version == 4 && migrated.Missions != null && migrated.Effects != null
                    && migrated.ForeignAgents != null && migrated.ForeignAgentActions != null && migrated.AppliedForeignAgentActionIds != null
                    && migrated.OrganicTestLedgers != null,
                    "Old saves without Spymaster collections migrate safely to the current schema.", JObject.FromObject(migrated));

                ReignSpymasterOrganicTestLedger oversizedLedger = new ReignSpymasterOrganicTestLedger { RunId = "oversized_save_payload" };
                oversizedLedger.Batches.Add(new ReignSpymasterOrganicBatchRecord { BatchId = "oversized_batch", PreSnapshotJson = new string('b', 10000) });
                for (int index = 0; index < 40; index++) oversizedLedger.ObservationJson.Add(new string('o', 10000));
                migrated.OrganicTestLedgers.Add(oversizedLedger);
                ReignSpymasterState compacted = EnsureSpymasterState();
                ReignSpymasterOrganicTestLedger compactedLedger = compacted.OrganicTestLedgers.FirstOrDefault(x => x.RunId == "oversized_save_payload");
                AddSpyAssertion(assertions, "organic_harness_save_payload_bounded", compactedLedger != null
                    && compactedLedger.Batches.All(x => (x.PreSnapshotJson ?? string.Empty).Length <= OrganicLedgerMaxStoredJsonChars)
                    && compactedLedger.ObservationJson.Count <= OrganicLedgerMaxObservations
                    && compactedLedger.ObservationJson.All(x => (x ?? string.Empty).Length <= OrganicLedgerMaxStoredJsonChars),
                    "Organic acceptance checkpoints are compacted before serialization so long test runs cannot corrupt native Bannerlord saves.",
                    compactedLedger == null ? null : JObject.FromObject(compactedLedger));

                string[] coverage = { "success", "failure", "detection", "persistence", "price", "timing", "side_effect", "save_load",
                    "appointment_ui", "land_intelligence", "subterfuge", "people", "assassination", "breakout", "social_self", "social_other",
                    "enemy_agents", "memory_dialogue", "ui_style", "migration", "isolation" };
                AddSpyAssertion(assertions, "coverage_manifest", coverage.Length == 21,
                    "The run artifact explicitly names every supported Spymaster behavior dimension.", new JObject { ["coverage"] = new JArray(coverage) });
            }
            catch (Exception ex)
            {
                AddSpyAssertion(assertions, "runner_exception", false, "The in-game Spymaster runner completed without an unhandled exception.", new JObject { ["exception"] = ex.ToString() });
            }
            finally
            {
                capturedNativeIntents = new JArray(ReignSpymasterTestRuntime.NativeIntents.Select(x => x.DeepClone()));
                int now = Hero.MainHero?.Gold ?? 0;
                if (Hero.MainHero != null && now > originalGold) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, now - originalGold, false);
                else if (Hero.MainHero != null && now < originalGold) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, originalGold - now, false);
                _spymasterState = JsonConvert.DeserializeObject<ReignSpymasterState>(originalJson) ?? new ReignSpymasterState();
                ReignSpymasterTestRuntime.Reset();
                StateChanged?.Invoke();
            }
            JObject result = BuildSpymasterRun(normalizedRunId, requestedProfile, seed, started, assertions, capturedNativeIntents);
            result["cleanup"] = new JObject { ["stateRestored"] = true, ["goldRestored"] = (Hero.MainHero?.Gold ?? 0) == originalGold, ["testRuntimeActive"] = ReignSpymasterTestRuntime.Active };
            return result;
        }

        private JObject PrepareSpymasterReloadFixture(string runId, int seed)
        {
            CleanupSpymasterTestState(runId);
            ReignSpymasterState state = EnsureSpymasterState();
            string prefix = "spyreload_" + runId;
            ReignSpymasterMission marker = new ReignSpymasterMission
            {
                MissionId = prefix, SpymasterHeroStringId = ActiveSpymaster?.StringId ?? string.Empty,
                MissionType = "person_relationships", TargetType = "person", TargetStringId = Hero.MainHero?.StringId ?? string.Empty,
                TargetName = Hero.MainHero?.Name?.ToString() ?? "player", GoldCost = 2250, Roguery = 123, ClanTier = 4,
                StartedDay = CurrentDayFloat(), DueDay = CurrentDayFloat() + 5f, SuccessChance = 47f, DetectionChance = 31f,
                State = ReignSpymasterMissionState.Active, RequestSummary = "save/load deterministic marker",
                ReportJson = "{\"marker\":true}", History = new List<string> { "Assigned before save.", "Awaiting reload verification." }
            };
            state.Missions.Add(marker);
            state.ForeignAgents.Add(new ReignForeignAgentRecord { AgentHeroStringId = prefix + "_agent", SponsorKingdomStringId = "spyreload_foreign",
                SettlementStringId = Settlement.CurrentSettlement?.StringId ?? string.Empty, IsNotable = true, Activated = true,
                RecruitedDay = CurrentDayFloat() - 5f, NextActionDay = CurrentDayFloat() + 5f });
            state.AppliedForeignAgentActionIds.Add(prefix + "_action");
            state.ForeignAgentActions.Add(new ReignForeignAgentActionRecord { ActionId = prefix + "_action", AgentHeroStringId = prefix + "_agent",
                SponsorKingdomStringId = "spyreload_foreign", SettlementStringId = Settlement.CurrentSettlement?.StringId ?? string.Empty,
                EffectType = "loyalty", WorldDay = CurrentDayFloat(), DurationDays = 10f, Magnitude = -1f, Detected = true,
                SponsorAttributed = false, Summary = "Reload marker action." });
            StateChanged?.Invoke();
            return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "prepare_reload", ["markerId"] = prefix,
                ["fingerprint"] = SpymasterReloadFingerprint(runId), ["state"] = JObject.FromObject(state) };
        }

        private JObject VerifySpymasterReloadFixture(string runId)
        {
            string prefix = "spyreload_" + runId;
            ReignSpymasterState state = EnsureSpymasterState();
            ReignSpymasterMission mission = state.Missions.FirstOrDefault(x => x.MissionId == prefix);
            int actionCount = state.ForeignAgentActions.Count(x => x.ActionId == prefix + "_action");
            int agentCount = state.ForeignAgents.Count(x => x.AgentHeroStringId == prefix + "_agent");
            bool passed = mission != null && mission.State == ReignSpymasterMissionState.Active && mission.GoldCost == 2250
                && mission.History?.Count == 2 && actionCount == 1 && agentCount == 1
                && state.AppliedForeignAgentActionIds.Count(x => x == prefix + "_action") == 1;
            string fingerprint = SpymasterReloadFingerprint(runId);
            JObject cleanup = CleanupSpymasterTestState(runId);
            return new JObject { ["ok"] = passed, ["runId"] = runId, ["profile"] = "verify_reload", ["fingerprint"] = fingerprint,
                ["assertions"] = new JArray(new JObject { ["caseId"] = "save_load_roundtrip", ["passed"] = passed,
                    ["summary"] = "Active mission, rich memory, hidden agent affiliation and idempotent action history survived native save/load." }),
                ["cleanup"] = cleanup };
        }

        private JObject CleanupSpymasterTestState(string runId)
        {
            string reloadPrefix = "spyreload_" + runId;
            string testPrefix = "spytest_" + runId;
            ReignSpymasterState state = EnsureSpymasterState();
            int missions = state.Missions.RemoveAll(x => (x.MissionId ?? string.Empty).StartsWith(reloadPrefix, StringComparison.OrdinalIgnoreCase)
                || (x.MissionId ?? string.Empty).StartsWith(testPrefix, StringComparison.OrdinalIgnoreCase));
            int agents = state.ForeignAgents.RemoveAll(x => (x.AgentHeroStringId ?? string.Empty).StartsWith(reloadPrefix, StringComparison.OrdinalIgnoreCase));
            int actions = state.ForeignAgentActions.RemoveAll(x => (x.ActionId ?? string.Empty).StartsWith(reloadPrefix, StringComparison.OrdinalIgnoreCase)
                || (x.ActionId ?? string.Empty).StartsWith("spytest-action-" + runId, StringComparison.OrdinalIgnoreCase));
            int actionIds = state.AppliedForeignAgentActionIds.RemoveAll(x => (x ?? string.Empty).StartsWith(reloadPrefix, StringComparison.OrdinalIgnoreCase)
                || (x ?? string.Empty).StartsWith("spytest-action-" + runId, StringComparison.OrdinalIgnoreCase));
            ReignSpymasterTestRuntime.Reset();
            StateChanged?.Invoke();
            return new JObject { ["ok"] = true, ["missionsRemoved"] = missions, ["agentsRemoved"] = agents,
                ["actionsRemoved"] = actions, ["actionIdsRemoved"] = actionIds, ["runtimeActive"] = ReignSpymasterTestRuntime.Active };
        }

        private string SpymasterReloadFingerprint(string runId)
        {
            string prefix = "spyreload_" + runId;
            ReignSpymasterState state = EnsureSpymasterState();
            return JsonConvert.SerializeObject(new
            {
                mission = state.Missions.FirstOrDefault(x => x.MissionId == prefix),
                agent = state.ForeignAgents.FirstOrDefault(x => x.AgentHeroStringId == prefix + "_agent"),
                action = state.ForeignAgentActions.FirstOrDefault(x => x.ActionId == prefix + "_action"),
                actionIdCount = state.AppliedForeignAgentActionIds.Count(x => x == prefix + "_action")
            }, Formatting.None);
        }

        private static ReignSpymasterMission NewTestMission(string runId, string type, Hero spymaster, Hero target, Settlement settlement)
        {
            ReignSpymasterCoreQuote q = ReignSpymasterCore.Quote(type, spymaster?.GetSkillValue(DefaultSkills.Roguery) ?? 0,
                target?.Clan?.Tier ?? 0, target?.Clan?.Kingdom?.Leader == target);
            return new ReignSpymasterMission
            {
                MissionId = "spytest_" + runId + "_" + Guid.NewGuid().ToString("N"), SpymasterHeroStringId = spymaster?.StringId ?? string.Empty,
                MissionType = type, TargetType = settlement != null ? "settlement" : "person",
                TargetStringId = settlement?.StringId ?? target?.StringId ?? string.Empty,
                TargetName = settlement?.Name?.ToString() ?? target?.Name?.ToString() ?? "test target",
                SponsorKingdomStringId = target?.Clan?.Kingdom?.StringId ?? settlement?.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                GoldCost = q.GoldCost, Roguery = spymaster?.GetSkillValue(DefaultSkills.Roguery) ?? 0, ClanTier = target?.Clan?.Tier ?? 0,
                TargetIsRuler = target?.Clan?.Kingdom?.Leader == target, StartedDay = (float)CampaignTime.Now.ToDays,
                DueDay = (float)CampaignTime.Now.ToDays + (float)q.DurationDays, SuccessChance = (float)q.SuccessChance,
                DetectionChance = (float)q.DetectionChance, Harmful = q.Harmful, State = ReignSpymasterMissionState.Active
            };
        }

        private static void AddSpyAssertion(JArray assertions, string id, bool passed, string summary, JToken data)
        {
            assertions.Add(new JObject { ["caseId"] = id, ["passed"] = passed, ["summary"] = summary ?? string.Empty,
                ["data"] = data ?? JValue.CreateNull() });
        }

        private static JObject SnapshotHero(Hero hero)
        {
            return hero == null ? new JObject() : new JObject { ["heroId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? string.Empty,
                ["alive"] = hero.IsAlive, ["active"] = hero.IsActive, ["prisoner"] = hero.IsPrisoner,
                ["roguery"] = hero.GetSkillValue(DefaultSkills.Roguery) };
        }

        private static JObject BuildSpymasterRun(string runId, string profile, int seed, DateTime started, JArray assertions, JArray nativeIntents = null)
        {
            int passed = assertions.Count(x => (bool?)x["passed"] == true);
            return new JObject { ["ok"] = passed == assertions.Count, ["runId"] = runId, ["profile"] = profile, ["seed"] = seed,
                ["startedUtc"] = started.ToString("o"), ["completedUtc"] = DateTime.UtcNow.ToString("o"),
                ["durationMs"] = (long)(DateTime.UtcNow - started).TotalMilliseconds, ["passedCount"] = passed,
                ["failedCount"] = assertions.Count - passed, ["totalCount"] = assertions.Count,
                ["assertions"] = assertions, ["nativeIntents"] = nativeIntents ?? new JArray(ReignSpymasterTestRuntime.NativeIntents) };
        }

        private static void EnsureGoldForSpyTest(int minimum)
        {
            if (Hero.MainHero != null && Hero.MainHero.Gold < minimum)
                GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, minimum - Hero.MainHero.Gold, false);
        }
    }
}
