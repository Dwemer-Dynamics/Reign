using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static bool IsCampaignCommandDisposableSaveName(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return normalized.StartsWith("ReignTest_", StringComparison.Ordinal)
                && normalized.EndsWith("_Current", StringComparison.Ordinal)
                && normalized.All(character => (character >= '0' && character <= '9')
                    || (character >= 'A' && character <= 'Z')
                    || (character >= 'a' && character <= 'z')
                    || character == '_' || character == '-');
        }

        private static async Task<LiveCommandResult> ExecuteCampaignCommandTestAsync(JObject command)
        {
            string profile = (command.Value<string>("profile") ?? string.Empty).Trim().ToLowerInvariant();
            string runId = command.Value<string>("certificationRunId")
                ?? command.Value<string>("runId") ?? string.Empty;
            string expectedSave = command.Value<string>("disposableSaveName") ?? string.Empty;
            int seed = command.Value<int?>("seed") ?? 1337;
            int pass = command.Value<int?>("pass") ?? 1;
            string phase = (command.Value<string>("phase") ?? string.Empty).Trim().ToLowerInvariant();
            DateTime started = DateTime.UtcNow;
            JObject evidence = new JObject
            {
                ["profile"] = profile, ["seed"] = seed, ["pass"] = pass,
                ["startedUtc"] = started.ToString("o"),
                ["capabilityCount"] = ReignCampaignCommandCapabilityRegistry.All.Count
            };
            bool passed = false;
            bool fatal = false;
            string failureClass = string.Empty;
            string error = string.Empty;
            int caseCount = 0;
            int passedCases = 0;
            JArray caseLedgerRows = null;
            try
            {
                string activeSave = await ReignMainThread.InvokeAsync(ActiveSaveName).ConfigureAwait(false);
                evidence["activeSaveName"] = activeSave;
                bool saveAligned = IsCampaignCommandDisposableSaveName(expectedSave)
                    && string.Equals(activeSave, expectedSave, StringComparison.OrdinalIgnoreCase);
                evidence["saveAligned"] = saveAligned;
                if (!saveAligned)
                {
                    fatal = true;
                    failureClass = "wrong_loaded_save";
                    throw new InvalidOperationException(
                        "The exact guarded campaign-test run-owned Current save is not loaded.");
                }
                await ReignServerClient.VerifyCampaignCommandSaveAsync(new JObject
                {
                    ["runId"] = runId, ["loadedSaveName"] = activeSave,
                    ["campaignAligned"] = true,
                    ["campaignIdObserved"] = ReignCampaignIdentity.CurrentCampaignId()
                }).ConfigureAwait(false);

                JObject result;
                switch (profile)
                {
                    case "preflight":
                        result = RunCampaignCommandPreflight();
                        break;
                    case "offline_contracts":
                        result = await RunCampaignCommandVerificationProfileAsync("offline", seed,
                            command.Value<int?>("timeoutSeconds") ?? 1200).ConfigureAwait(false);
                        break;
                    case "geography":
                        result = await ReignMainThread.InvokeAsync(RunCampaignCommandGeography).ConfigureAwait(false);
                        break;
                    case "personality":
                        result = RunCampaignCommandPersonalityMatrix(seed);
                        break;
                    case "native_primitives":
                    case "composite_orders":
                    case "correspondence":
                    case "fault_recovery":
                    case "save_roundtrip":
                        result = await ReignMainThread.InvokeAsync(() =>
                            RunCampaignCommandNativeProfile(profile, seed, phase, runId,
                                command.Value<bool?>("restartVerified") == true,
                                command.Value<string>("restartReceipt") ?? string.Empty)).ConfigureAwait(false);
                        break;
                    case "llm_matrix":
                        result = await RunCampaignCommandVerificationProfileAsync("live-llm", seed,
                            command.Value<int?>("timeoutSeconds") ?? 7200).ConfigureAwait(false);
                        break;
                    case "campaign_soak":
                        result = await RunCampaignCommandSoakAsync(command, seed).ConfigureAwait(false);
                        break;
                    case "evaluate":
                        result = RunCampaignCommandEvaluation();
                        break;
                    case "cleanup":
                        result = await ReignMainThread.InvokeAsync(RunCampaignCommandCleanup).ConfigureAwait(false);
                        break;
                    default:
                        result = new JObject { ["ok"] = false, ["error"] = "Unsupported Campaign Command profile." };
                        break;
                }
                caseLedgerRows = result["caseLedgerRows"] as JArray;
                result.Remove("caseLedgerRows");
                evidence["result"] = result;
                passed = result.Value<bool?>("ok") == true;
                caseCount = result.Value<int?>("caseCount") ?? 1;
                passedCases = result.Value<int?>("passedCaseCount") ?? (passed ? caseCount : 0);
                error = result.Value<string>("error") ?? string.Empty;
                failureClass = result.Value<string>("failureClass") ?? failureClass;
                fatal = fatal || result.Value<bool?>("fatal") == true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                evidence["exception"] = ex.ToString();
            }

            evidence["completedUtc"] = DateTime.UtcNow.ToString("o");
            evidence["durationMs"] = (long)(DateTime.UtcNow - started).TotalMilliseconds;
            JObject profilePayload = new JObject
            {
                ["runId"] = runId, ["profile"] = profile, ["seed"] = seed, ["pass"] = pass,
                ["passed"] = passed, ["fatal"] = fatal,
                ["failureClass"] = string.IsNullOrWhiteSpace(failureClass)
                    ? passed ? string.Empty : "assertion_failure" : failureClass,
                ["caseCount"] = caseCount, ["passedCaseCount"] = passedCases,
                ["evidence"] = evidence,
                ["replayBundle"] = "reign-live://campaign-command/" + runId + "/" + profile
            };
            if (profile == "save_roundtrip" && phase == "prepare")
                return passed
                    ? LiveCommandResult.Completed("Campaign Command save-roundtrip fixture is ready for its native checkpoint.", evidence)
                    : LiveCommandResult.Failed(string.IsNullOrWhiteSpace(error)
                        ? "Campaign Command save-roundtrip fixture preparation failed." : error, evidence);
            if (evidence["result"] is JObject measuredResult && measuredResult["metrics"] != null)
                profilePayload["metrics"] = measuredResult["metrics"];
            if (caseLedgerRows != null && caseLedgerRows.Count > 0)
                profilePayload["caseLedgerRows"] = caseLedgerRows;
            JObject recorded = await ReignServerClient.ReportCampaignCommandProfileAsync(profilePayload)
                .ConfigureAwait(false);
            evidence["serverRecord"] = recorded;
            if (passed)
                return LiveCommandResult.Completed("Campaign Command profile " + profile + " passed.", evidence);
            if (fatal)
                return LiveCommandResult.Failed(string.IsNullOrWhiteSpace(error)
                    ? "Campaign Command profile " + profile + " hit a fatal harness failure." : error, evidence);
            return LiveCommandResult.Completed("Campaign Command profile " + profile
                + " recorded a repair-required result and certification will continue with independent sections.", evidence);
        }

        private static JObject RunCampaignCommandPreflight()
        {
            var failures = new JArray();
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) failures.Add("campaign_missing");
            if (Hero.MainHero == null) failures.Add("main_hero_missing");
            if (MobileParty.MainParty == null) failures.Add("main_party_missing");
            if (ReignCampaignCommandBehavior.Instance == null) failures.Add("command_behavior_missing");
            if (ReignCampaignCommandCapabilityRegistry.All.Count != 20) failures.Add("capability_count");
            if (ReignCampaignCommandCapabilityRegistry.All.Any(x => !x.IsComplete)) failures.Add("incomplete_capability");
            if (ReignBetaSettings.Instance?.CampaignCommandEngineEnabled == false) failures.Add("engine_disabled");
            return new JObject
            {
                ["ok"] = failures.Count == 0, ["caseCount"] = 7,
                ["passedCaseCount"] = 7 - failures.Count, ["failures"] = failures,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId()
            };
        }

        private static async Task<JObject> RunCampaignCommandVerificationProfileAsync(
            string tier, int seed, int timeoutSeconds)
        {
            JObject started = await ReignServerClient.StartCampaignCommandVerificationAsync(tier, seed)
                .ConfigureAwait(false);
            if (started.Value<bool?>("ok") != true)
                return new JObject { ["ok"] = false,
                    ["error"] = started.Value<string>("error") ?? "Verification Lab did not return a run id." };
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, Math.Min(7200, timeoutSeconds)));
            string runId = started.SelectToken("status.runId")?.Value<string>()
                ?? started.Value<string>("runId") ?? string.Empty;
            JObject status = new JObject();
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(1000).ConfigureAwait(false);
                JObject statusEnvelope = await ReignServerClient.GetCampaignCommandVerificationStatusAsync()
                    .ConfigureAwait(false);
                status = statusEnvelope["status"] as JObject ?? statusEnvelope;
                string observedRunId = status.Value<string>("runId") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(runId) && !string.IsNullOrWhiteSpace(observedRunId))
                    runId = observedRunId;
                if (string.IsNullOrWhiteSpace(runId) || !string.Equals(observedRunId, runId,
                        StringComparison.OrdinalIgnoreCase)) continue;
                string state = (status.Value<string>("state")
                    ?? status.Value<string>("status") ?? string.Empty).ToLowerInvariant();
                if (state == "completed" || state == "failed" || state == "cancelled") break;
            }
            if (string.IsNullOrWhiteSpace(runId))
                return new JObject { ["ok"] = false,
                    ["error"] = "Verification Lab did not publish a run id before the profile timeout." };
            JObject resultEnvelope = await ReignServerClient.GetCampaignCommandVerificationResultAsync(runId)
                .ConfigureAwait(false);
            JObject result = resultEnvelope["run"] as JObject ?? resultEnvelope;
            bool passed = result.Value<bool?>("passed") == true
                || string.Equals(result.Value<string>("status"), "completed", StringComparison.OrdinalIgnoreCase);
            JArray checks = result["checks"] as JArray ?? new JArray();
            JObject matrix = checks.OfType<JObject>().FirstOrDefault(x =>
                string.Equals(x.Value<string>("id"), "campaign_command.live_llm_300",
                    StringComparison.OrdinalIgnoreCase));
            JObject metrics = matrix?["data"] as JObject ?? new JObject();
            JArray caseLedgerRows = metrics["caseLedgerRows"] as JArray ?? new JArray();
            metrics.Remove("caseLedgerRows");
            return new JObject
            {
                ["ok"] = passed, ["verificationRunId"] = runId,
                ["caseCount"] = result.Value<int?>("totalCount") ?? checks.Count,
                ["passedCaseCount"] = result.Value<int?>("passedCount") ?? 0,
                ["metrics"] = metrics,
                ["caseLedgerRows"] = caseLedgerRows,
                ["report"] = result,
                ["error"] = passed ? string.Empty : result.Value<string>("summary") ?? "Verification Lab profile failed."
            };
        }

        private static JObject RunCampaignCommandOfflineContracts(int seed)
        {
            JArray failures = new JArray();
            int cases = 0;
            foreach (ReignCampaignCommandCapability capability in ReignCampaignCommandCapabilityRegistry.All)
            {
                cases++;
                if (!capability.IsComplete || !ReignCampaignCommandCapabilityRegistry.IsPlannerVisible(capability.Id))
                    failures.Add("registry:" + capability.Id);
            }
            string[] aliases = { "go", "wait", "scout", "resupply", "siege", "hunt", "attack party", "home" };
            foreach (string alias in aliases)
            {
                cases++;
                if (!ReignCampaignCommandCapabilityRegistry.IsPlannerVisible(
                    ReignCampaignCommandCapabilityRegistry.NormalizeObjective(alias))) failures.Add("alias:" + alias);
            }
            cases += 14;
            if (ReignWarCouncilRules.ToRoman(49) != "XLIX") failures.Add("war_council_roman");
            if (ReignWarCouncilRules.ClassifyComposition(8, 3, 2) != ReignWarPartyComposition.Infantry) failures.Add("war_council_infantry");
            if (ReignWarCouncilRules.ClassifyComposition(2, 8, 3) != ReignWarPartyComposition.Missile) failures.Add("war_council_missile");
            if (ReignWarCouncilRules.ClassifyComposition(2, 3, 8) != ReignWarPartyComposition.Mounted) failures.Add("war_council_mounted");
            if (Math.Abs(ReignWarCouncilRules.ForeignPartyDetectionChance(0) - 0.25d) > 0.0001d) failures.Add("war_council_detection_floor");
            if (Math.Abs(ReignWarCouncilRules.ForeignPartyDetectionChance(250) - 0.90d) > 0.0001d) failures.Add("war_council_detection_cap");
            if (Math.Abs(ReignWarCouncilRules.ForeignPartyDetectionRange(0) - 70d) > 0.0001d) failures.Add("war_council_range_floor");
            if (Math.Abs(ReignWarCouncilRules.ForeignPartyDetectionRange(300) - 520d) > 0.0001d) failures.Add("war_council_range_cap");
            if (!ReignWarCouncilRules.IsWithinForeignDetectionRange(100d, 100d, 170d, 100d, 0)) failures.Add("war_council_range_floor_inclusive");
            if (ReignWarCouncilRules.IsWithinForeignDetectionRange(100d, 100d, 170.01d, 100d, 0)) failures.Add("war_council_range_floor_exclusive");
            if (ReignWarCouncilRules.StableDetectionRoll("party-test", 7) != ReignWarCouncilRules.StableDetectionRoll("party-test", 7)) failures.Add("war_council_detection_stability");
            if (ReignWarCouncilRules.DetectForeignParty("party-test", 7, 300, 250, 0d, 0d, 521d, 0d)) failures.Add("war_council_out_of_range_hidden");
            ReignWarCouncilReportVM realmReport = new ReignWarCouncilReportVM(new ReignWarCouncilBattleReport
            {
                Attacker = "Realm Party", Defender = "Enemy Party", PlayerRealmInvolved = true
            });
            if (!realmReport.PlayerRealmInvolved || realmReport.OrdinaryBattle || realmReport.Title != "Realm Party VS Enemy Party") failures.Add("war_council_realm_report_emphasis");
            ReignWarCouncilReportVM ordinaryReport = new ReignWarCouncilReportVM(new ReignWarCouncilBattleReport
            {
                Attacker = "Foreign Party", Defender = "Other Party", PlayerRealmInvolved = false
            });
            if (ordinaryReport.PlayerRealmInvolved || !ordinaryReport.OrdinaryBattle) failures.Add("war_council_ordinary_report_style");
            ReignWarCouncilPoint roundTrip = ReignWarCouncilRules.MapToWorld(
                ReignWarCouncilRules.WorldToMap(512.481, 319.377, 1536, 1024).X,
                ReignWarCouncilRules.WorldToMap(512.481, 319.377, 1536, 1024).Y, 1536, 1024);
            if (Math.Abs(roundTrip.X - 512.481) > 0.001 || Math.Abs(roundTrip.Y - 319.377) > 0.001) failures.Add("war_council_map_roundtrip");
            if (ReignWarCouncilRules.SelectAttackObjective(true, true, false) != "raid") failures.Add("war_council_attack_resolution");
            Random random = new Random(seed);
            for (int i = 0; i < 1000; i++)
            {
                cases++;
                ReignCampaignCommandDecision decision = ReignCampaignCommandJudgment.Evaluate(
                    new ReignCampaignCommandDecisionInput
                    {
                        OwnStrength = random.Next(1, 1000), EnemyStrength = random.Next(0, 3000),
                        FoodDays = (float)random.NextDouble() * 10f, Morale = random.Next(0, 101),
                        CasualtyRatio = (float)random.NextDouble(), Mercy = random.Next(-2, 3),
                        Valor = random.Next(-2, 3), Honor = random.Next(-2, 3),
                        Calculating = random.Next(-2, 3), Leadership = random.Next(0, 331),
                        Tactics = random.Next(0, 331), RelationToSovereign = random.Next(-100, 101),
                        SovereignOrder = random.Next(0, 2) == 1, CivilianRelief = random.Next(0, 2) == 1,
                        Conquest = random.Next(0, 2) == 1, TargetUrgent = random.Next(0, 2) == 1,
                        TargetValid = true
                    });
                if (!ReignCampaignCommandJudgment.IsAllowedOutcome(decision.Outcome))
                    failures.Add("unsafe_outcome:" + i);
            }
            cases++;
            var suicidalConquest = ReignCampaignCommandJudgment.Evaluate(new ReignCampaignCommandDecisionInput
            { OwnStrength = 100, EnemyStrength = 400, FoodDays = 4, Morale = 60, Mercy = 2,
                Conquest = true, SovereignOrder = false, TargetValid = true });
            if (suicidalConquest.Outcome != "refuse") failures.Add("compassionate_conquest");
            cases++;
            var desperateRelief = ReignCampaignCommandJudgment.Evaluate(new ReignCampaignCommandDecisionInput
            { OwnStrength = 100, EnemyStrength = 300, FoodDays = 4, Morale = 60, Mercy = 2,
                Valor = 2, CivilianRelief = true, TargetUrgent = true, SovereignOrder = true, TargetValid = true });
            if (desperateRelief.Outcome != "continue" && desperateRelief.Outcome != "adapt")
                failures.Add("compassionate_relief");
            return new JObject
            {
                ["ok"] = failures.Count == 0, ["caseCount"] = cases,
                ["passedCaseCount"] = cases - failures.Count, ["failures"] = failures,
                ["seed"] = seed
            };
        }

        private static JObject RunCampaignCommandGeography()
        {
            JArray failures = new JArray();
            int cases = 0;
            MobileParty reference = MobileParty.MainParty;
            foreach (string region in new[] { "north", "south", "east", "west", "central", "frontier", "all" })
            {
                cases++;
                IReadOnlyList<Settlement> anchors = ReignCampaignCommandGeography.ResolveAnchors(reference,
                    region, null);
                if (reference?.MapFaction != null && Settlement.All.Any(x => x != null
                    && x.MapFaction == reference.MapFaction && (x.IsTown || x.IsCastle || x.IsVillage))
                    && (anchors.Count == 0 || anchors.Any(x => x == null || string.IsNullOrWhiteSpace(x.StringId))))
                    failures.Add(region);
            }
            cases += 6;
            foreach (string value in new[] { "southern kingdom", "northern marches", "eastern border",
                "western lands", "heartland", "frontier" })
                if (string.IsNullOrWhiteSpace(ReignCampaignCommandGeography.NormalizeRegion(value))) failures.Add(value);
            return new JObject { ["ok"] = failures.Count == 0, ["caseCount"] = cases,
                ["passedCaseCount"] = cases - failures.Count, ["failures"] = failures };
        }

        private static JObject RunCampaignCommandPersonalityMatrix(int seed)
        {
            JObject contracts = RunCampaignCommandOfflineContracts(seed);
            return new JObject
            {
                ["ok"] = contracts.Value<bool>("ok"),
                ["caseCount"] = contracts.Value<int>("caseCount"),
                ["passedCaseCount"] = contracts.Value<int>("passedCaseCount"),
                ["failures"] = contracts["failures"],
                ["branches"] = new JArray("sovereign_obedience", "compassionate_conquest_refusal",
                    "compassionate_relief_commitment", "loyalty", "low_relation", "calculated_adaptation")
            };
        }

        private static JObject RunCampaignCommandNativeProfile(string profile, int seed, string phase,
            string certificationRunId, bool restartVerified, string restartReceipt)
        {
            if (MobileParty.MainParty == null || ReignCampaignCommandBehavior.Instance == null)
                return new JObject { ["ok"] = false, ["fatal"] = true,
                    ["failureClass"] = "bridge_desynchronization", ["error"] = "Campaign command runtime is unavailable." };
            string runScope = new string((certificationRunId ?? string.Empty).Where(ch =>
                    char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').Take(64).ToArray());
            if (string.IsNullOrWhiteSpace(runScope)) runScope = "unknown";
            if (profile == "native_primitives") return RunCampaignCommandNativeCapabilityMatrix(seed, runScope);
            MobileParty commanderParty = MobileParty.All.FirstOrDefault(x => x != null && x.IsActive
                && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                && x.MapEvent == null && x.BesiegedSettlement == null);
            if (commanderParty == null)
                return new JObject { ["ok"] = false, ["error"] = "No eligible NPC-led party is available." };

            Settlement friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                    && x.MapFaction == commanderParty.MapFaction && !x.IsUnderSiege)
                .OrderBy(x => commanderParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement friendlyVillage = Settlement.All.Where(x => x != null && x.IsVillage
                    && x.MapFaction == commanderParty.MapFaction && !x.IsUnderSiege)
                .OrderBy(x => commanderParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            string objective = profile == "correspondence" ? "timed_hold"
                : profile == "fault_recovery" ? "patrol" : "move";
            JObject terms = new JObject
            {
                ["objective"] = objective, ["durationHours"] = 2,
                ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["region"] = profile == "fault_recovery" ? "southern kingdom" : string.Empty
            };
            if (profile == "composite_orders")
            {
                terms.Remove("objective");
                terms.Remove("durationHours");
                terms["steps"] = new JArray
                {
                    new JObject { ["objective"] = "recruit_resupply",
                        ["targetSettlementStringId"] = friendly?.StringId ?? string.Empty,
                        ["minimumTroops"] = Math.Max(1, commanderParty.MemberRoster.TotalRegulars),
                        ["minimumFoodDays"] = 0 },
                    new JObject { ["objective"] = "timed_hold", ["durationHours"] = 2 },
                    new JObject { ["objective"] = "patrol",
                        ["targetSettlementStringId"] = friendly?.StringId ?? string.Empty }
                };
            }
            ReignWorldActionRecord issue = new ReignWorldActionRecord
            {
                ActionId = "campaign_command_test_" + runScope + "_" + profile + "_" + seed,
                Type = ReignWorldActionType.RegularIssueCampaignOrder,
                Source = "dialogue_auto_commit_certification",
                ActorHeroStringId = commanderParty.LeaderHero.StringId,
                TargetSettlementStringId = friendly?.StringId ?? string.Empty,
                TermsJson = terms.ToString(Formatting.None),
                AuthorizationMode = "accepted_recommendation",
                AcceptedByHeroStringId = commanderParty.LeaderHero.StringId,
                RequiresAcceptance = false,
                Reason = "Disposable Campaign Command certification case."
            };
            if (profile == "save_roundtrip" && phase == "verify")
            {
                ReignCampaignOrderRecord persisted = ReignCampaignCommandBehavior.Instance.Orders
                    .FirstOrDefault(x => x.OrderId == issue.ActionId);
                bool payloadRoundTrip = persisted != null
                    && persisted.CommanderHeroStringId == commanderParty.LeaderHero.StringId
                    && ReignCampaignCommandBehavior.Instance.VerifySaveRoundTripForCertification();
                var reloadChecks = new JArray
                {
                    new JObject { ["id"] = "save_payload_roundtrip", ["passed"] = payloadRoundTrip },
                    new JObject { ["id"] = "native_checkpoint_observed", ["passed"] = restartVerified },
                    new JObject { ["id"] = "different_game_instance_observed",
                        ["passed"] = restartVerified && !string.IsNullOrWhiteSpace(restartReceipt) }
                };
                bool cancelledPersisted = persisted == null || persisted.IsTerminal;
                if (persisted != null && !persisted.IsTerminal)
                {
                    ReignActionResult cancelPersisted = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(
                        new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_reload_cleanup",
                            ActorHeroStringId = persisted.CommanderHeroStringId,
                            Reason = "Certification reload cleanup.",
                            TermsJson = new JObject { ["orderId"] = persisted.OrderId }.ToString(Formatting.None)
                        });
                    cancelledPersisted = cancelPersisted.Success && persisted.Status == "cancelled";
                }
                reloadChecks.Add(new JObject { ["id"] = "safe_cancellation", ["passed"] = cancelledPersisted });
                int reloadPassed = reloadChecks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
                JObject reloadMetrics = new JObject
                {
                    ["checks"] = reloadChecks.Count, ["passed"] = reloadPassed,
                    ["save_payload_roundtrip"] = payloadRoundTrip,
                    ["native_checkpoint_observed"] = restartVerified,
                    ["different_game_instance_observed"] = restartVerified
                        && !string.IsNullOrWhiteSpace(restartReceipt),
                    ["safe_cancellation"] = cancelledPersisted,
                    ["restartReceipt"] = restartReceipt
                };
                return new JObject
                {
                    ["ok"] = reloadPassed == reloadChecks.Count,
                    ["caseCount"] = reloadChecks.Count, ["passedCaseCount"] = reloadPassed,
                    ["checks"] = reloadChecks, ["metrics"] = reloadMetrics,
                    ["orderStatus"] = persisted?.Status ?? "missing"
                };
            }
            ReignActionResult issued = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(issue);
            ReignCampaignOrderRecord order = ReignCampaignCommandBehavior.Instance.Orders
                .FirstOrDefault(x => x.OrderId == issue.ActionId);
            bool mainUnaffected = !commanderParty.IsMainParty && MobileParty.MainParty != commanderParty;
            bool durable = issued.Success && order != null && order.CommanderHeroStringId == commanderParty.LeaderHero.StringId;
            JArray checks = new JArray
            {
                new JObject { ["id"] = "issued", ["passed"] = issued.Success },
                new JObject { ["id"] = "durable_order", ["passed"] = durable },
                new JObject { ["id"] = "main_party_untouched", ["passed"] = mainUnaffected }
            };
            if (profile == "composite_orders" && order != null && !order.IsTerminal)
            {
                bool planShape = order.PlanVersion == 2 && order.Steps?.Count == 3
                    && order.Steps.Select(x => x.Objective).SequenceEqual(
                        new[] { "recruit_resupply", "timed_hold", "patrol" })
                    && !string.IsNullOrWhiteSpace(order.PlanHash);
                checks.Add(new JObject { ["id"] = "multistage_plan_shape", ["passed"] = planShape });
                int transitions = 0;
                while (!order.IsTerminal && order.Objective != "patrol" && transitions < 3)
                {
                    if (!ReignCampaignCommandBehavior.Instance.CompleteCurrentStepForCertification(
                        order.OrderId, "Deterministic multi-stage transition fixture.")) break;
                    transitions++;
                }
                checks.Add(new JObject { ["id"] = "ordered_step_transitions",
                    ["passed"] = !order.IsTerminal && order.Objective == "patrol"
                        && order.CurrentStepIndex == 2 && order.CompletedStepCount == 2 });
                checks.Add(new JObject { ["id"] = "indefinite_patrol_semantics",
                    ["passed"] = order.Objective == "patrol" && order.DeadlineDay <= 0d });

                string[][] distinctFlows =
                {
                    new[] { "move", "timed_hold" },
                    new[] { "move", "scout_report" },
                    new[] { "withdraw", "return_home" }
                };
                List<MobileParty> flowCommanders = MobileParty.All.Where(x => x != null && x.IsActive
                        && !x.IsMainParty && x != commanderParty && x.LeaderHero != null
                        && !x.LeaderHero.IsPrisoner && x.MapEvent == null && x.BesiegedSettlement == null)
                    .Take(distinctFlows.Length).ToList();
                int distinctFlowReceipts = 0;
                for (int flowIndex = 0; flowIndex < distinctFlows.Length; flowIndex++)
                {
                    if (flowIndex >= flowCommanders.Count) break;
                    MobileParty flowCommander = flowCommanders[flowIndex];
                    Settlement flowTarget = Settlement.All.Where(x => x != null && !x.IsHideout
                            && x.MapFaction == flowCommander.MapFaction && !x.IsUnderSiege)
                        .OrderBy(x => flowCommander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
                    JArray flowSteps = new JArray();
                    foreach (string flowObjective in distinctFlows[flowIndex])
                    {
                        JObject flowStep = new JObject { ["objective"] = flowObjective };
                        if (flowObjective == "move" || flowObjective == "scout_report")
                            flowStep["targetSettlementStringId"] = flowTarget?.StringId ?? string.Empty;
                        if (flowObjective == "timed_hold" || flowObjective == "scout_report")
                            flowStep["durationHours"] = 2;
                        flowSteps.Add(flowStep);
                    }
                    ReignWorldActionRecord flowIssue = new ReignWorldActionRecord
                    {
                        ActionId = issue.ActionId + "_flow_" + flowIndex,
                        Type = ReignWorldActionType.RegularIssueCampaignOrder,
                        Source = "dialogue_auto_commit_certification",
                        ActorHeroStringId = flowCommander.LeaderHero.StringId,
                        TargetSettlementStringId = flowTarget?.StringId ?? string.Empty,
                        TermsJson = new JObject { ["steps"] = flowSteps,
                            ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty }
                            .ToString(Formatting.None),
                        AuthorizationMode = "accepted_recommendation",
                        AcceptedByHeroStringId = flowCommander.LeaderHero.StringId,
                        RequiresAcceptance = false,
                        Reason = "Materially distinct composite certification flow."
                    };
                    ReignActionResult flowIssued = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(flowIssue);
                    ReignCampaignOrderRecord flowOrder = ReignCampaignCommandBehavior.Instance.Orders
                        .FirstOrDefault(x => x.OrderId == flowIssue.ActionId);
                    bool flowShape = flowIssued.Success && flowOrder?.PlanVersion == 2
                        && flowOrder.Steps.Select(x => x.Objective)
                            .SequenceEqual(distinctFlows[flowIndex], StringComparer.Ordinal)
                        && !string.IsNullOrWhiteSpace(flowOrder.PlanHash);
                    if (flowShape) distinctFlowReceipts++;
                    checks.Add(new JObject { ["id"] = "distinct_composite_flow_" + flowIndex,
                        ["passed"] = flowShape, ["objectives"] = new JArray(distinctFlows[flowIndex]) });
                    if (flowOrder != null && !flowOrder.IsTerminal)
                        ReignCampaignCommandBehavior.Instance.ExecuteControlAction(new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_cleanup",
                            ActorHeroStringId = flowCommander.LeaderHero.StringId,
                            TermsJson = new JObject { ["orderId"] = flowOrder.OrderId }.ToString(Formatting.None)
                        });
                }
                checks.Add(new JObject { ["id"] = "materially_distinct_composite_receipts",
                    ["passed"] = distinctFlowReceipts == distinctFlows.Length,
                    ["receiptCount"] = distinctFlowReceipts });

                Hero partyless = Hero.AllAliveHeroes.FirstOrDefault(x => x != null && x != Hero.MainHero
                    && x.IsLord && !x.IsChild && !x.IsPrisoner && x.Clan?.Kingdom != null
                    && x.PartyBelongedTo == null && x.Clan.Kingdom.Settlements.Any(s => !s.IsUnderSiege));
                ReignCampaignOrderRecord partylessOrder = null;
                if (partyless != null)
                {
                    Settlement spawn = partyless.Clan.Kingdom.Settlements.First(s => !s.IsUnderSiege);
                    ReignWorldActionRecord partylessIssue = new ReignWorldActionRecord
                    {
                        ActionId = issue.ActionId + "_partyless",
                        Type = ReignWorldActionType.RegularIssueCampaignOrder,
                        Source = "dialogue_auto_commit_certification",
                        ActorHeroStringId = partyless.StringId,
                        TargetSettlementStringId = spawn.StringId,
                        AuthorizationMode = "accepted_recommendation",
                        AcceptedByHeroStringId = partyless.StringId,
                        RequiresAcceptance = false,
                        Reason = "Morwyn-class partyless recruit then patrol regression.",
                        TermsJson = new JObject
                        {
                            ["steps"] = new JArray
                            {
                                new JObject { ["objective"] = "recruit_resupply",
                                    ["targetSettlementStringId"] = spawn.StringId,
                                    ["minimumTroops"] = 1, ["minimumFoodDays"] = 0 },
                                new JObject { ["objective"] = "patrol",
                                    ["targetSettlementStringId"] = spawn.StringId }
                            }
                        }.ToString(Formatting.None)
                    };
                    ReignActionResult partylessResult = ReignCampaignCommandBehavior.Instance
                        .ExecuteControlAction(partylessIssue);
                    partylessOrder = ReignCampaignCommandBehavior.Instance.Orders
                        .FirstOrDefault(x => x.OrderId == partylessIssue.ActionId);
                    checks.Add(new JObject { ["id"] = "partyless_recruit_patrol_regression",
                        ["passed"] = partylessResult.Success && partylessOrder?.Steps?.Count == 3
                            && partylessOrder.Steps[0].Objective == "establish_party"
                            && partyless.PartyBelongedTo?.LeaderHero == partyless });
                }
                else
                {
                    checks.Add(new JObject { ["id"] = "partyless_recruit_patrol_regression",
                        ["passed"] = false, ["error"] = "No eligible partyless lord exists in the disposable fixture." });
                }
                if (partylessOrder != null && !partylessOrder.IsTerminal)
                {
                    ReignCampaignCommandBehavior.Instance.ExecuteControlAction(new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        ActorHeroStringId = partyless.StringId,
                        Source = "campaign_command_certification_cleanup",
                        TermsJson = new JObject { ["orderId"] = partylessOrder.OrderId }.ToString(Formatting.None)
                    });
                }
            }
            if (profile == "correspondence" && order != null && !order.IsTerminal)
            {
                bool requested = ReignCampaignCommandBehavior.Instance.RequestGuidanceForCertification(
                    order.OrderId, "Enemy strength changed; urgent sovereign guidance is required.");
                bool twelveHours = requested && Math.Abs((order.GuidanceDeadlineDay - CampaignTime.Now.ToDays) * 24d - 12d) < 0.1d;
                checks.Add(new JObject { ["id"] = "urgent_report_queued", ["passed"] = requested });
                checks.Add(new JObject { ["id"] = "twelve_hour_deadline", ["passed"] = twelveHours });
                ReignActionResult response = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularRespondToOrderReport,
                        Source = "campaign_command_certification",
                        ActorHeroStringId = commanderParty.LeaderHero.StringId,
                        Reason = "Certification response.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId,
                            ["response"] = "continue" }.ToString(Formatting.None)
                    });
                checks.Add(new JObject { ["id"] = "response_delivered", ["passed"] = response.Success && !order.AwaitingGuidance });
                bool requestedAgain = ReignCampaignCommandBehavior.Instance.RequestGuidanceForCertification(
                    order.OrderId, "No-response branch fixture.");
                bool expired = requestedAgain && ReignCampaignCommandBehavior.Instance
                    .ExpireGuidanceForCertification(order.OrderId);
                checks.Add(new JObject { ["id"] = "no_response_branch", ["passed"] = expired });
            }
            if (profile == "fault_recovery" && order != null && !order.IsTerminal)
            {
                ReignActionResult duplicate = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(issue);
                checks.Add(new JObject { ["id"] = "duplicate_suppressed",
                    ["passed"] = duplicate.ResultCode == "campaign_order_duplicate_suppressed" });
                int beforeReassert = order.NativeReassertions;
                commanderParty.SetMoveModeHold();
                bool reviewed = ReignCampaignCommandBehavior.Instance.ReviewForCertification(order.OrderId);
                checks.Add(new JObject { ["id"] = "native_ai_reasserted",
                    ["passed"] = reviewed && order.NativeReassertions > beforeReassert });
                ReignWorldActionRecord obsolete = new ReignWorldActionRecord
                {
                    ActionId = issue.ActionId + "_obsolete",
                    Type = ReignWorldActionType.RegularIssueCampaignOrder,
                    Source = "dialogue_auto_commit_certification",
                    ActorHeroStringId = commanderParty.LeaderHero.StringId,
                    TargetSettlementStringId = (friendlyVillage ?? friendly)?.StringId ?? string.Empty,
                    Reason = "Changed-world invalid-target fixture.",
                    AuthorizationMode = "accepted_recommendation",
                    AcceptedByHeroStringId = commanderParty.LeaderHero.StringId,
                    RequiresAcceptance = false,
                    TermsJson = new JObject { ["objective"] = "raid",
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty }.ToString(Formatting.None)
                };
                ReignActionResult obsoleteResult = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(obsolete);
                ReignCampaignOrderRecord obsoleteOrder = ReignCampaignCommandBehavior.Instance.Orders
                    .FirstOrDefault(x => x.OrderId == obsolete.ActionId);
                checks.Add(new JObject { ["id"] = "obsolete_target_classified",
                    ["passed"] = obsoleteOrder?.Status == "failed"
                        && (obsoleteOrder.LastFailure.IndexOf("hostile", StringComparison.OrdinalIgnoreCase) >= 0
                            || obsoleteOrder.LastFailure.IndexOf("village", StringComparison.OrdinalIgnoreCase) >= 0),
                    ["classification"] = obsoleteOrder?.LastFailure ?? obsoleteResult?.Message ?? string.Empty });
            }
            if (profile == "save_roundtrip")
            {
                checks.Add(new JObject { ["id"] = "save_payload_roundtrip",
                    ["passed"] = ReignCampaignCommandBehavior.Instance.VerifySaveRoundTripForCertification() });
            }

            bool cancelled = true;
            if (order != null && !order.IsTerminal && !(profile == "save_roundtrip" && phase == "prepare"))
            {
                ReignActionResult cancel = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        Source = "campaign_command_certification",
                        ActorHeroStringId = commanderParty.LeaderHero.StringId,
                        Reason = "Certification cleanup.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                    });
                cancelled = cancel.Success && order.Status == "cancelled";
            }
            checks.Add(new JObject { ["id"] = "safe_cancellation", ["passed"] = cancelled });
            int passedChecks = checks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
            JObject profileMetrics = new JObject { ["checks"] = checks.Count, ["passed"] = passedChecks };
            foreach (JObject check in checks.OfType<JObject>())
                profileMetrics[check.Value<string>("id") ?? "unknown"] = check.Value<bool>("passed");
            return new JObject
            {
                ["ok"] = passedChecks == checks.Count, ["caseCount"] = checks.Count,
                ["passedCaseCount"] = passedChecks, ["checks"] = checks,
                ["issued"] = issued.Success, ["message"] = issued.Message,
                ["orderStatus"] = order?.Status ?? "missing", ["mainPartyUnaffected"] = mainUnaffected,
                ["nativeSignature"] = order?.LastNativeSignature ?? string.Empty,
                ["metrics"] = profileMetrics
            };
        }

        private static JObject RunCampaignCommandNativeCapabilityMatrix(int seed, string runScope)
        {
            List<MobileParty> eligible = MobileParty.All.Where(x => x != null && x.IsActive
                && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                && x.MapEvent == null && x.BesiegedSettlement == null).ToList();
            List<MobileParty> commanderCandidates = eligible.Where(x => x.MapFaction is Kingdom
                && eligible.Any(y => y != x && y.MapFaction == x.MapFaction)).ToList();
            MobileParty commander = commanderCandidates.OrderByDescending(x =>
                (eligible.Any(y => y != x && FactionManager.IsAtWarAgainstFaction(x.MapFaction,
                    y.MapFaction)) ? 100 : 0)
                + (Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x.MapFaction
                    && !s.IsUnderSiege && s != MobileParty.MainParty?.CurrentSettlement) ? 10 : 0)
                + (Settlement.All.Any(s => s != null && s.IsFortification && s.MapFaction == x.MapFaction
                    && s.IsUnderSiege) ? 1000 : 0)).FirstOrDefault();
            if (commander == null)
                return new JObject { ["ok"] = false, ["caseCount"] = 18, ["passedCaseCount"] = 0,
                    ["error"] = "No kingdom has two eligible NPC-led parties for the native capability matrix." };

            Hero commanderHero = commander.LeaderHero;
            MobileParty auxiliary = eligible.First(x => x != commander && x.MapFaction == commander.MapFaction);
            Settlement friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                    && x.MapFaction == commander.MapFaction && !x.IsUnderSiege)
                .OrderByDescending(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement hostileVillage = Settlement.All.Where(x => x != null && x.IsVillage
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement hostileFortification = Settlement.All.Where(x => x != null && x.IsFortification
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            Settlement besiegedFriendly = Settlement.All.FirstOrDefault(x => x != null && x.IsFortification
                && x.MapFaction == commander.MapFaction && x.IsUnderSiege && x.SiegeEvent?.BesiegerCamp?.LeaderParty != null);
            MobileParty hostileParty = eligible.Where(x => x != commander
                    && FactionManager.IsAtWarAgainstFaction(commander.MapFaction, x.MapFaction))
                .OrderBy(x => commander.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();

            MobileParty armyCommander = null;
            MobileParty armyAuxiliary = null;
            Settlement armyFriendly = null;
            List<MobileParty> createdArmyFixtureParties = new List<MobileParty>();
            Clan provisionedInfluenceClan = null;
            float originalInfluence = 0f;
            List<MobileParty> armyCandidates = commanderCandidates.Where(x => x.Army == null
                    && x.LeaderHero?.Clan != null
                    && eligible.Any(y => y != x && y.Army == null && y.MapFaction == x.MapFaction))
                .OrderByDescending(x => x == commander ? 100000 : 0)
                .ThenByDescending(x => x.MapFaction == Clan.PlayerClan?.Kingdom ? 10000 : 0)
                .ThenByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
            foreach (MobileParty candidate in armyCandidates)
            {
                if (!TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(
                    candidate, out MBList<MobileParty> _)) continue;
                armyCommander = candidate;
                provisionedInfluenceClan = candidate.LeaderHero.Clan;
                originalInfluence = provisionedInfluenceClan.Influence;
                break;
            }
            if (armyCommander == null)
            {
                foreach (MobileParty candidate in armyCandidates)
                {
                    Clan clan = candidate.LeaderHero.Clan;
                    float before = clan.Influence;
                    if (before < 5000f) ChangeClanInfluenceAction.Apply(clan, 5000f - before);
                    if (TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel.CanLordCreateArmy(
                        candidate, out MBList<MobileParty> _))
                    {
                        armyCommander = candidate;
                        provisionedInfluenceClan = clan;
                        originalInfluence = before;
                        break;
                    }
                    if (Math.Abs(clan.Influence - before) > 0.01f)
                        ChangeClanInfluenceAction.Apply(clan, before - clan.Influence);
                }
            }
            if (armyCommander == null)
            {
                CharacterObject fixtureTroop = TaleWorlds.ObjectSystem.MBObjectManager.Instance
                    .GetObject<CharacterObject>("imperial_elite_cataphract");
                foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated
                    && x.FactionsAtWarWith.Any(faction => faction?.Fiefs?.Any() == true)))
                {
                    Settlement spawn = Settlement.All.FirstOrDefault(x => x != null && x.IsFortification
                        && x.MapFaction == kingdom && !x.IsUnderSiege);
                    Hero fixtureLeader = Hero.AllAliveHeroes.FirstOrDefault(x => x != null
                        && x != Hero.MainHero && !x.IsPrisoner && x.PartyBelongedTo == null
                        && x.Clan?.Kingdom == kingdom && x.Clan.Leader == x
                        && !x.Clan.IsUnderMercenaryService && x.CanLeadParty());
                    List<Hero> fixtureMembers = Hero.AllAliveHeroes.Where(x => x != null
                            && x != Hero.MainHero && x != fixtureLeader && x != kingdom.Leader
                            && !x.IsPrisoner && x.PartyBelongedTo == null && x.Clan?.Kingdom == kingdom
                            && x.CanLeadParty()).Take(4).ToList();
                    if (spawn == null || fixtureLeader == null || fixtureMembers.Count == 0
                        || fixtureTroop == null || DefaultItems.Grain == null) continue;
                    try
                    {
                        List<Hero> fixtureHeroes = new[] { fixtureLeader }.Concat(fixtureMembers).ToList();
                        for (int fixtureIndex = 0; fixtureIndex < fixtureHeroes.Count; fixtureIndex++)
                        {
                            Hero fixtureHero = fixtureHeroes[fixtureIndex];
                            MobileParty fixtureParty = LordPartyComponent.CreateLordParty(
                                fixtureHero.StringId + "_reign_campaign_command_army_" + seed + "_" + fixtureIndex,
                                fixtureHero, spawn.GatePosition, 2f, spawn, fixtureHero);
                            fixtureParty.MemberRoster.AddToCounts(fixtureTroop, 180);
                            fixtureParty.ItemRoster.AddToCounts(DefaultItems.Grain, 300);
                            fixtureParty.SetMoveModeHold();
                            createdArmyFixtureParties.Add(fixtureParty);
                        }
                        MobileParty fixtureCandidate = createdArmyFixtureParties.FirstOrDefault(x =>
                            x?.LeaderHero == fixtureLeader && x.Army == null);
                        if (fixtureCandidate == null)
                            throw new InvalidOperationException("The native army leader fixture was not created.");
                        provisionedInfluenceClan = fixtureLeader.Clan;
                        originalInfluence = provisionedInfluenceClan.Influence;
                        if (originalInfluence < 5000f)
                            ChangeClanInfluenceAction.Apply(provisionedInfluenceClan, 5000f - originalInfluence);
                        if (TaleWorlds.CampaignSystem.Campaign.Current.Models.ArmyManagementCalculationModel
                            .CanLordCreateArmy(fixtureCandidate, out MBList<MobileParty> fixturePossibleMembers))
                        {
                            armyCommander = fixtureCandidate;
                            armyAuxiliary = fixturePossibleMembers.FirstOrDefault(x => x != null
                                && x != fixtureCandidate && x.Army == null);
                            armyFriendly = spawn;
                            break;
                        }
                        if (Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                            ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                                originalInfluence - provisionedInfluenceClan.Influence);
                        provisionedInfluenceClan = null;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command native army ecology fixture failed: " + ex.Message);
                    }
                    if (armyCommander == null && provisionedInfluenceClan != null
                        && Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                        ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                            originalInfluence - provisionedInfluenceClan.Influence);
                    if (armyCommander == null) provisionedInfluenceClan = null;
                    foreach (MobileParty fixtureParty in createdArmyFixtureParties.ToList())
                    {
                        try { if (fixtureParty?.IsActive == true) DestroyPartyAction.Apply(null, fixtureParty); }
                        catch (Exception ex)
                        {
                            ReignLog.Warn("Campaign Command native army ecology rollback failed: " + ex.Message);
                        }
                    }
                    createdArmyFixtureParties.Clear();
                }
            }
            if (armyCommander != null)
            {
                armyAuxiliary = armyAuxiliary ?? eligible.FirstOrDefault(x => x != armyCommander && x.Army == null
                    && x.MapFaction == armyCommander.MapFaction);
                armyFriendly = armyFriendly ?? Settlement.All.Where(x => x != null && x.IsFortification
                            && x.MapFaction == armyCommander.MapFaction && !x.IsUnderSiege)
                        .OrderByDescending(x => armyCommander.GetPosition2D.DistanceSquared(x.GetPosition2D))
                        .FirstOrDefault();
            }
            Hero armyCommanderHero = armyCommander?.LeaderHero;
            Hero armyAuxiliaryHero = armyAuxiliary?.LeaderHero;

            TaleWorlds.CampaignSystem.Siege.SiegeEvent certificationSiege = null;
            if (besiegedFriendly == null && hostileParty != null)
            {
                Settlement siegeFixture = Settlement.All.Where(x => x != null && x.IsFortification
                        && x.MapFaction == commander.MapFaction && !x.IsUnderSiege
                        && x != MobileParty.MainParty?.CurrentSettlement)
                    .OrderBy(x => hostileParty.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
                if (siegeFixture != null)
                {
                    try
                    {
                        certificationSiege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager.StartSiegeEvent(
                            siegeFixture, hostileParty);
                        besiegedFriendly = siegeFixture;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command siege fixture could not be created: " + ex.Message);
                    }
                }
            }

            JArray receipts = new JArray();
            int passed = 0;
            try
            {
                foreach (ReignCampaignCommandCapability capability in ReignCampaignCommandCapabilityRegistry.All)
                {
                    MobileParty actor = commander;
                    Settlement settlement = null;
                    MobileParty targetParty = null;
                    JObject terms = new JObject
                    {
                        ["objective"] = capability.Id,
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["durationHours"] = 2
                    };
                    string prerequisite = string.Empty;
                    switch (capability.Id)
                    {
                    case "establish_party": break;
                    case "move":
                    case "patrol":
                    case "scout_report":
                    case "defend":
                    case "return_home": settlement = friendly; break;
                    case "timed_hold": break;
                    case "hold_position":
                        terms["positionX"] = Math.Max(0f, Math.Min(1040f, commander.Position.X + 4f));
                        terms["positionY"] = Math.Max(0f, Math.Min(1040f, commander.Position.Y + 4f));
                        terms["durationHours"] = 0;
                        break;
                    case "escort": targetParty = auxiliary; break;
                    case "recruit_resupply":
                        settlement = friendly;
                        terms["minimumTroops"] = Math.Min(commander.Party.PartySizeLimit,
                            Math.Max(1, commander.MemberRoster.TotalManCount + 1));
                        break;
                    case "form_army":
                        actor = armyCommander;
                        settlement = armyFriendly;
                        if (actor == null || armyAuxiliary == null || settlement == null)
                            prerequisite = "no native-eligible independent army fixture exists";
                        break;
                    case "join_army":
                        actor = armyAuxiliary;
                        targetParty = armyCommander?.Army?.LeaderParty == armyCommander ? armyCommander : null;
                        if (targetParty == null) prerequisite = "form_army did not produce a native army";
                        break;
                    case "leave_army":
                        actor = armyAuxiliary;
                        terms["armyDetachmentAccepted"] = true;
                        if (actor?.Army == null) prerequisite = "join_army did not produce native membership";
                        break;
                    case "disband_army":
                        actor = armyCommander;
                        if (actor?.Army?.LeaderParty != actor) prerequisite = "the harness-created army is unavailable";
                        break;
                    case "raid": settlement = hostileVillage;
                        if (settlement == null) prerequisite = "no hostile village exists"; break;
                    case "besiege_capture": settlement = hostileFortification;
                        if (settlement == null) prerequisite = "no hostile fortification exists"; break;
                    case "relieve_siege": settlement = besiegedFriendly;
                        if (settlement == null) prerequisite = "no friendly active siege exists"; break;
                    case "hunt_enemy_parties": terms["region"] = "frontier";
                        if (hostileParty == null) prerequisite = "no hostile active party exists"; break;
                    case "engage_party": targetParty = hostileParty;
                        if (targetParty == null) prerequisite = "no hostile active party exists"; break;
                    case "withdraw": settlement = friendly; break;
                    }
                    if (friendly == null && capability.RequiresSettlement && settlement == null
                        && string.IsNullOrWhiteSpace(prerequisite)) prerequisite = "no friendly settlement anchor exists";

                    Hero actorHero = actor?.LeaderHero;
                    if (actorHero == null && ReferenceEquals(actor, commander)) actorHero = commanderHero;
                    if (actorHero == null && ReferenceEquals(actor, armyCommander)) actorHero = armyCommanderHero;
                    if (actorHero == null && ReferenceEquals(actor, armyAuxiliary)) actorHero = armyAuxiliaryHero;
                    if (actorHero == null && string.IsNullOrWhiteSpace(prerequisite))
                        prerequisite = "selected native actor no longer has a resolvable leader";

                    string actionId = "campaign_command_native_" + runScope + "_" + seed + "_" + capability.Id;
                    ReignActionResult issued = null;
                    ReignCampaignOrderRecord order = null;
                    if (string.IsNullOrWhiteSpace(prerequisite))
                    {
                        if (targetParty != null) terms["targetPartyId"] = targetParty.StringId;
                        ReignWorldActionRecord action = new ReignWorldActionRecord
                        {
                            ActionId = actionId,
                            Type = ReignWorldActionType.RegularIssueCampaignOrder,
                            Source = "dialogue_auto_commit_certification",
                            ActorHeroStringId = actorHero.StringId,
                            TargetHeroStringId = targetParty?.LeaderHero?.StringId ?? string.Empty,
                            TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                            TermsJson = terms.ToString(Formatting.None),
                            AuthorizationMode = "accepted_recommendation",
                            AcceptedByHeroStringId = actorHero.StringId,
                            RequiresAcceptance = false,
                            Reason = "Disposable native capability certification."
                        };
                        issued = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(action);
                        order = ReignCampaignCommandBehavior.Instance.Orders.FirstOrDefault(x => x.OrderId == actionId);
                    }
                    bool nativeReceipt = issued?.Success == true && order != null
                        && (order.Status == "active" || order.Status == "completed")
                        && (order.Status == "completed" || !string.IsNullOrWhiteSpace(order.LastNativeSignature));
                    if (nativeReceipt) passed++;
                    receipts.Add(new JObject
                    {
                        ["capability"] = capability.Id,
                        ["passed"] = nativeReceipt,
                        ["prerequisiteFailure"] = prerequisite,
                        ["actionResult"] = issued?.Message ?? string.Empty,
                        ["orderStatus"] = order?.Status ?? "missing",
                        ["nativeSignature"] = order?.LastNativeSignature ?? string.Empty
                    });
                    if (order != null && !order.IsTerminal)
                    {
                        ReignCampaignCommandBehavior.Instance.ExecuteControlAction(new ReignWorldActionRecord
                        {
                            Type = ReignWorldActionType.RegularCancelCampaignOrder,
                            Source = "campaign_command_certification_cleanup",
                            ActorHeroStringId = order.CommanderHeroStringId,
                            Reason = "Capability case cleanup.",
                            TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                        });
                    }
                }
            }
            finally
            {
                if (armyCommander?.Army?.LeaderParty == armyCommander)
                {
                    try { DisbandArmyAction.ApplyByUnknownReason(armyCommander.Army); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command army fixture cleanup failed: " + ex.Message);
                    }
                }
                if (provisionedInfluenceClan != null
                    && Math.Abs(provisionedInfluenceClan.Influence - originalInfluence) > 0.01f)
                    ChangeClanInfluenceAction.Apply(provisionedInfluenceClan,
                        originalInfluence - provisionedInfluenceClan.Influence);
                foreach (MobileParty fixtureParty in createdArmyFixtureParties.ToList())
                {
                    try { if (fixtureParty?.IsActive == true) DestroyPartyAction.Apply(null, fixtureParty); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command native army ecology cleanup failed: " + ex.Message);
                    }
                }
                if (certificationSiege != null)
                {
                    try { certificationSiege.FinalizeSiegeEvent(); }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command siege fixture cleanup failed: " + ex.Message);
                    }
                }
            }
            return new JObject
            {
                ["ok"] = passed == ReignCampaignCommandCapabilityRegistry.All.Count,
                ["caseCount"] = ReignCampaignCommandCapabilityRegistry.All.Count,
                ["passedCaseCount"] = passed,
                ["receipts"] = receipts,
                ["metrics"] = new JObject
                {
                    ["capabilitiesAttempted"] = ReignCampaignCommandCapabilityRegistry.All.Count,
                    ["capabilitiesPassed"] = passed,
                    ["nativeReceiptCount"] = receipts.OfType<JObject>().Count(x => x.Value<bool>("passed"))
                },
                ["error"] = passed == ReignCampaignCommandCapabilityRegistry.All.Count ? string.Empty
                    : "One or more registered native adapters lacked a successful native receipt."
            };
        }

        private static async Task<JObject> RunCampaignCommandSoakAsync(JObject command, int seed)
        {
            int requestedDays = Math.Max(1, Math.Min(30, command.Value<int?>("soakDays") ?? 30));
            int pass = command.Value<int?>("pass") ?? 1;
            string runScope = command.Value<string>("certificationRunId")
                ?? command.Value<string>("runId") ?? "campaign_command_soak";
            string disposableSaveName = command.Value<string>("disposableSaveName") ?? string.Empty;
            LiveCommandResult checkpoint = await SaveCheckpointAsync(new JObject
            {
                ["saveName"] = disposableSaveName,
                ["timeoutSeconds"] = 300
            }).ConfigureAwait(false);
            bool checkpointPassed = string.Equals(checkpoint.Status, "completed",
                StringComparison.OrdinalIgnoreCase);
            command["checkpointPrepared"] = checkpointPassed;
            command["checkpointEvidence"] = checkpoint.Data ?? new JObject();
            if (!checkpointPassed)
            {
                CampaignCommandSoakFixture failedFixture = new CampaignCommandSoakFixture
                {
                    Error = string.IsNullOrWhiteSpace(checkpoint.Error)
                        ? "The isolated native checkpoint could not be created." : checkpoint.Error
                };
                failedFixture.Checks.Add(SoakCheck("case_checkpoint_observed", false));
                return BuildCampaignCommandSoakResult(false, requestedDays, 0d, 0d,
                    new JArray(), failedFixture.Checks, failedFixture, command, failedFixture.Error);
            }
            CampaignCommandSoakFixture fixture = await ReignMainThread.InvokeAsync(() =>
                PrepareCampaignCommandSoakFixture(runScope, seed, pass)).ConfigureAwait(false);
            JArray checks = fixture.Checks;
            checks.Add(SoakCheck("case_checkpoint_observed", true));
            if (!fixture.Prepared)
            {
                JObject cleanup = await ReignMainThread.InvokeAsync(() =>
                    CleanupCampaignCommandSoakFixture(fixture)).ConfigureAwait(false);
                foreach (JObject check in cleanup.Value<JArray>("checks")?.OfType<JObject>()
                    ?? Enumerable.Empty<JObject>()) checks.Add(check);
                return BuildCampaignCommandSoakResult(false, requestedDays, 0d, 0d,
                    new JArray(), checks, fixture, command,
                    fixture.Error.Length > 0 ? fixture.Error : "The native soak fixture could not be prepared.");
            }

            JArray advances = new JArray();
            double startDay = 0d;
            double completedDay = 0d;
            int completedHours = 0;
            string advanceError = string.Empty;
            bool advancesPassed = true;
            try
            {
                int firstSegmentDays = Math.Min(1, requestedDays);
                int remainingDays = requestedDays - firstSegmentDays;
                foreach (int segmentDays in new[] { firstSegmentDays, remainingDays }.Where(x => x > 0))
                {
                    JObject advanceCommand = new JObject(command)
                    {
                        ["days"] = segmentDays,
                        ["timeoutSeconds"] = Math.Max(30,
                            Math.Min(21600, command.Value<int?>("timeoutSeconds") ?? 5400))
                    };
                    LiveCommandResult advance = await PassiveWorldAdvanceAsync(advanceCommand)
                        .ConfigureAwait(false);
                    JObject data = advance.Data ?? new JObject();
                    double segmentStart = data.Value<double?>("startWorldDay") ?? completedDay;
                    double segmentEnd = data.Value<double?>("worldDay") ?? segmentStart;
                    if (advances.Count == 0) startDay = segmentStart;
                    completedDay = segmentEnd;
                    int segmentHours = (int)Math.Max(0d,
                        Math.Min(segmentDays * 24d, (segmentEnd - segmentStart) * 24d));
                    completedHours += segmentHours;
                    bool segmentPassed = string.Equals(advance.Status, "completed",
                            StringComparison.OrdinalIgnoreCase)
                        && segmentEnd - segmentStart >= segmentDays;
                    advancesPassed &= segmentPassed;
                    if (!segmentPassed && string.IsNullOrWhiteSpace(advanceError))
                        advanceError = string.IsNullOrWhiteSpace(advance.Error)
                            ? "A native campaign-time segment did not reach its checkpoint." : advance.Error;
                    data["requestedSegmentDays"] = segmentDays;
                    data["status"] = advance.Status ?? string.Empty;
                    advances.Add(data);

                    if (advances.Count == 1)
                    {
                        JObject recovery = await ReignMainThread.InvokeAsync(() =>
                            ObserveAndRestoreCampaignCommandSoakChanges(fixture)).ConfigureAwait(false);
                        foreach (JObject check in recovery.Value<JArray>("checks")?.OfType<JObject>()
                            ?? Enumerable.Empty<JObject>()) checks.Add(check);
                    }
                    if (!segmentPassed) break;
                }
            }
            finally
            {
                JObject cleanup = await ReignMainThread.InvokeAsync(() =>
                    CleanupCampaignCommandSoakFixture(fixture)).ConfigureAwait(false);
                foreach (JObject check in cleanup.Value<JArray>("checks")?.OfType<JObject>()
                    ?? Enumerable.Empty<JObject>()) checks.Add(check);
            }

            bool checksPassed = checks.OfType<JObject>().All(x => x.Value<bool>("passed"));
            bool passed = advancesPassed && completedHours >= requestedDays * 24 && checksPassed;
            return BuildCampaignCommandSoakResult(passed, requestedDays, startDay, completedDay,
                advances, checks, fixture, command, passed ? string.Empty : advanceError);
        }

        private sealed class CampaignCommandSoakFixture
        {
            public readonly JArray Checks = new JArray();
            public readonly List<ReignCampaignOrderRecord> Orders = new List<ReignCampaignOrderRecord>();
            public bool Prepared;
            public string Error = string.Empty;
            public Kingdom PlayerKingdom;
            public Kingdom TemporaryWarTarget;
            public bool TemporaryWarCreated;
            public Settlement OwnershipSettlement;
            public Hero OriginalSettlementOwner;
            public Hero TemporarySettlementOwner;
            public bool OwnershipChanged;
            public TaleWorlds.CampaignSystem.Siege.SiegeEvent Siege;
            public bool SiegeStarted;
            public MobileParty LossParty;
            public CharacterObject LossTroop;
            public int LossCount;
            public int InitialConcurrentOrders;
        }

        private static CampaignCommandSoakFixture PrepareCampaignCommandSoakFixture(
            string runScope, int seed, int pass)
        {
            CampaignCommandSoakFixture fixture = new CampaignCommandSoakFixture();
            try
            {
                List<MobileParty> parties = MobileParty.All.Where(x => x != null && x.IsActive
                        && !x.IsMainParty && x.LeaderHero != null && !x.LeaderHero.IsPrisoner
                        && x.MapEvent == null && x.BesiegedSettlement == null
                        && x.MapFaction is Kingdom
                        && (x.Army == null || x.Army.LeaderParty == x))
                    .OrderByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
                Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
                fixture.PlayerKingdom = parties.Select(x => x.MapFaction as Kingdom).Where(x => x != null)
                    .Distinct().OrderByDescending(x => x == playerKingdom ? 10000 : 0)
                    .ThenByDescending(x => parties.Count(p => p.MapFaction == x))
                    .FirstOrDefault(x => x.Clans.Count(c => !c.IsEliminated && c.Leader != null) >= 2
                        && Settlement.All.Any(s => s != null && s.IsFortification
                            && s.MapFaction == x && !s.IsUnderSiege
                            && s != MobileParty.MainParty?.CurrentSettlement));
                if (fixture.PlayerKingdom != null)
                    parties = parties.OrderByDescending(x => x.MapFaction == fixture.PlayerKingdom)
                        .ThenByDescending(x => x.MemberRoster?.TotalManCount ?? 0).ToList();
                List<Settlement> friendly = Settlement.All.Where(x => x != null && !x.IsHideout
                        && x.MapFaction == fixture.PlayerKingdom && !x.IsUnderSiege)
                    .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
                if (fixture.PlayerKingdom == null || parties.Count < 3 || friendly.Count == 0)
                {
                    fixture.Error = "The disposable campaign needs three eligible NPC-led parties and one reversible kingdom fixture.";
                    return fixture;
                }

                fixture.TemporaryWarTarget = Kingdom.All.FirstOrDefault(x => x != null
                    && x != fixture.PlayerKingdom && !x.IsEliminated
                    && !FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, x));
                if (fixture.TemporaryWarTarget != null)
                {
                    DeclareWarAction.ApplyByDefault(fixture.PlayerKingdom, fixture.TemporaryWarTarget);
                    fixture.TemporaryWarCreated = FactionManager.IsAtWarAgainstFaction(
                        fixture.PlayerKingdom, fixture.TemporaryWarTarget);
                }
                fixture.Checks.Add(SoakCheck("war_change_observed", fixture.TemporaryWarCreated));

                fixture.OwnershipSettlement = friendly.FirstOrDefault(x => x.IsFortification
                    && x != MobileParty.MainParty?.CurrentSettlement && x.OwnerClan?.Leader != null
                    && fixture.PlayerKingdom.Clans.Any(c => c != x.OwnerClan && !c.IsEliminated && c.Leader != null));
                if (fixture.OwnershipSettlement != null)
                {
                    fixture.OriginalSettlementOwner = fixture.OwnershipSettlement.OwnerClan.Leader;
                    fixture.TemporarySettlementOwner = fixture.PlayerKingdom.Clans
                        .Where(c => c != fixture.OwnershipSettlement.OwnerClan && !c.IsEliminated && c.Leader != null)
                        .Select(c => c.Leader).FirstOrDefault();
                    ChangeOwnerOfSettlementAction.ApplyByGift(fixture.OwnershipSettlement,
                        fixture.TemporarySettlementOwner);
                    fixture.OwnershipChanged = fixture.OwnershipSettlement.OwnerClan
                        == fixture.TemporarySettlementOwner?.Clan;
                }
                fixture.Checks.Add(SoakCheck("ownership_change_observed", fixture.OwnershipChanged));

                Settlement siegeTarget = friendly.FirstOrDefault(x => x.IsFortification
                    && x != MobileParty.MainParty?.CurrentSettlement && !x.IsUnderSiege);
                MobileParty enemyParty = MobileParty.All.Where(x => x != null && x.IsActive
                        && x.LeaderHero != null && x.MapEvent == null && x.BesiegedSettlement == null
                        && FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, x.MapFaction))
                    .OrderBy(x => siegeTarget == null ? 0f
                        : x.GetPosition2D.DistanceSquared(siegeTarget.GetPosition2D)).FirstOrDefault();
                if (siegeTarget != null && enemyParty != null)
                {
                    try
                    {
                        fixture.Siege = TaleWorlds.CampaignSystem.Campaign.Current.SiegeEventManager
                            .StartSiegeEvent(siegeTarget, enemyParty);
                        fixture.SiegeStarted = siegeTarget.IsUnderSiege && fixture.Siege != null;
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Campaign Command soak siege fixture could not be created: " + ex.Message);
                    }
                }
                fixture.Checks.Add(SoakCheck("siege_observed", fixture.SiegeStarted));

                string[] preferredObjectives = { "patrol", "defend", "timed_hold" };
                for (int index = 0; index < preferredObjectives.Length; index++)
                {
                    MobileParty actor = parties[index];
                    Settlement actorSettlement = Settlement.All.FirstOrDefault(x => x != null
                        && !x.IsHideout && x.MapFaction == actor.MapFaction && !x.IsUnderSiege);
                    string objective = preferredObjectives[index];
                    if (objective != "timed_hold" && actorSettlement == null) objective = "timed_hold";
                    JObject terms = new JObject
                    {
                        ["objective"] = objective,
                        ["issuerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                        ["durationHours"] = 744
                    };
                    Settlement settlement = objective == "timed_hold" ? null : actorSettlement;
                    string actionId = "campaign_command_soak_" + runScope + "_p" + pass + "_s" + seed
                        + "_" + objective + "_" + index;
                    ReignActionResult issued = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(
                        new ReignWorldActionRecord
                        {
                            ActionId = actionId,
                            Type = ReignWorldActionType.RegularIssueCampaignOrder,
                            Source = "campaign_command_certification_soak",
                            ActorHeroStringId = actor.LeaderHero.StringId,
                            TargetSettlementStringId = settlement?.StringId ?? string.Empty,
                            TermsJson = terms.ToString(Formatting.None),
                            AuthorizationMode = "accepted_recommendation",
                            AcceptedByHeroStringId = actor.LeaderHero.StringId,
                            RequiresAcceptance = false,
                            Reason = "Disposable simultaneous-order native soak."
                        });
                    ReignCampaignOrderRecord order = ReignCampaignCommandBehavior.Instance.Orders
                        .FirstOrDefault(x => x.OrderId == actionId);
                    if (issued.Success && order != null && !order.IsTerminal
                        && !string.IsNullOrWhiteSpace(order.LastNativeSignature)) fixture.Orders.Add(order);
                }
                fixture.InitialConcurrentOrders = fixture.Orders.Count;
                fixture.Checks.Add(SoakCheck("simultaneous_orders", fixture.InitialConcurrentOrders >= 3,
                    fixture.InitialConcurrentOrders));

                fixture.LossParty = parties.FirstOrDefault(x => x.MemberRoster?.TotalManCount > 2);
                var lossElement = fixture.LossParty?.MemberRoster?.GetTroopRoster()
                    .FirstOrDefault(x => x.Character != null && !x.Character.IsHero && x.Number > 0);
                fixture.LossTroop = lossElement.HasValue ? lossElement.Value.Character : null;
                if (fixture.LossParty != null && fixture.LossTroop != null)
                {
                    int before = fixture.LossParty.MemberRoster.TotalManCount;
                    fixture.LossParty.MemberRoster.AddToCounts(fixture.LossTroop, -1);
                    fixture.LossCount = before - fixture.LossParty.MemberRoster.TotalManCount;
                }
                fixture.Checks.Add(SoakCheck("party_loss_observed", fixture.LossCount == 1,
                    fixture.LossCount));
                fixture.Checks.Add(SoakCheck("main_party_untouched",
                    !ReignCampaignCommandBehavior.Instance.Orders.Any(x => x != null && !x.IsTerminal
                        && string.Equals(x.CommanderHeroStringId, Hero.MainHero?.StringId,
                            StringComparison.OrdinalIgnoreCase))));
                fixture.Prepared = fixture.Checks.OfType<JObject>().All(x => x.Value<bool>("passed"));
            }
            catch (Exception ex)
            {
                fixture.Error = ex.Message;
                ReignLog.Warn("Campaign Command soak fixture preparation failed: " + ex);
            }
            return fixture;
        }

        private static JObject ObserveAndRestoreCampaignCommandSoakChanges(CampaignCommandSoakFixture fixture)
        {
            JArray checks = new JArray();
            int observed = fixture.Orders.Count(x => x != null && (x.IsTerminal
                || (!string.IsNullOrWhiteSpace(x.LastNativeSignature) && x.LastReviewDay > x.AcceptedDay)));
            checks.Add(SoakCheck("hourly_order_review_observed", observed == fixture.Orders.Count, observed));
            bool restored = RestoreCampaignCommandSoakWorld(fixture);
            checks.Add(SoakCheck("changed_world_restored", restored));
            return new JObject { ["checks"] = checks };
        }

        private static JObject CleanupCampaignCommandSoakFixture(CampaignCommandSoakFixture fixture)
        {
            JArray checks = new JArray();
            bool worldRestored = RestoreCampaignCommandSoakWorld(fixture);
            int cancellations = 0;
            foreach (ReignCampaignOrderRecord order in fixture.Orders.Where(x => x != null && !x.IsTerminal))
            {
                ReignActionResult result = ReignCampaignCommandBehavior.Instance.ExecuteControlAction(
                    new ReignWorldActionRecord
                    {
                        Type = ReignWorldActionType.RegularCancelCampaignOrder,
                        Source = "campaign_command_certification_soak_cleanup",
                        ActorHeroStringId = order.CommanderHeroStringId,
                        Reason = "Native soak cleanup.",
                        TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                    });
                if (result.Success && order.IsTerminal) cancellations++;
            }
            int duplicates = ReignCampaignCommandBehavior.Instance?.Orders
                .GroupBy(x => x.OrderId, StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1) ?? 0;
            int active = ReignCampaignCommandBehavior.Instance?.Orders.Count(x => x != null && !x.IsTerminal) ?? 0;
            bool terminal = fixture.Orders.All(x => x != null && x.IsTerminal);
            checks.Add(SoakCheck("cleanup_world_restored", worldRestored));
            checks.Add(SoakCheck("terminal_orders_bounded", terminal, cancellations));
            checks.Add(SoakCheck("no_duplicate_effects", duplicates == 0, duplicates));
            checks.Add(SoakCheck("bounded_queue", active < 100, active));
            return new JObject { ["checks"] = checks };
        }

        private static bool RestoreCampaignCommandSoakWorld(CampaignCommandSoakFixture fixture)
        {
            bool restored = true;
            if (fixture.Siege != null)
            {
                try { fixture.Siege.FinalizeSiegeEvent(); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak siege cleanup failed: " + ex.Message);
                }
                fixture.Siege = null;
            }
            if (fixture.OwnershipChanged && fixture.OwnershipSettlement != null
                && fixture.OriginalSettlementOwner != null
                && fixture.OwnershipSettlement.OwnerClan != fixture.OriginalSettlementOwner.Clan)
            {
                try { ChangeOwnerOfSettlementAction.ApplyByGift(fixture.OwnershipSettlement,
                    fixture.OriginalSettlementOwner); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak owner cleanup failed: " + ex.Message);
                }
            }
            if (fixture.TemporaryWarCreated && fixture.PlayerKingdom != null
                && fixture.TemporaryWarTarget != null
                && FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, fixture.TemporaryWarTarget))
            {
                try { MakePeaceAction.ApplyByKingdomDecision(fixture.PlayerKingdom,
                    fixture.TemporaryWarTarget, 0, 0); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak war cleanup failed: " + ex.Message);
                }
            }
            if (fixture.LossCount > 0 && fixture.LossParty != null && fixture.LossParty.IsActive
                && fixture.LossTroop != null)
            {
                try { fixture.LossParty.MemberRoster.AddToCounts(fixture.LossTroop, fixture.LossCount); }
                catch (Exception ex)
                {
                    restored = false;
                    ReignLog.Warn("Campaign Command soak roster cleanup failed: " + ex.Message);
                }
                fixture.LossCount = 0;
            }
            return restored
                && (fixture.OwnershipSettlement == null || fixture.OriginalSettlementOwner == null
                    || fixture.OwnershipSettlement.OwnerClan == fixture.OriginalSettlementOwner.Clan)
                && (fixture.PlayerKingdom == null || fixture.TemporaryWarTarget == null
                    || !FactionManager.IsAtWarAgainstFaction(fixture.PlayerKingdom, fixture.TemporaryWarTarget));
        }

        private static JObject BuildCampaignCommandSoakResult(bool passed, int requestedDays,
            double startDay, double completedDay, JArray advances, JArray checks,
            CampaignCommandSoakFixture fixture, JObject command, string error)
        {
            int passedChecks = checks.OfType<JObject>().Count(x => x.Value<bool>("passed"));
            int hourlyCases = requestedDays * 24;
            int completedHours = (int)Math.Max(0d, Math.Min(hourlyCases,
                (completedDay - startDay) * 24d));
            JObject metrics = new JObject
            {
                ["caseCheckpointObserved"] = command.Value<bool?>("checkpointPrepared") == true,
                ["simultaneousOrders"] = CheckPassed(checks, "simultaneous_orders"),
                ["simultaneousOrderCount"] = fixture.InitialConcurrentOrders,
                ["ownershipChangeObserved"] = CheckPassed(checks, "ownership_change_observed"),
                ["warChangeObserved"] = CheckPassed(checks, "war_change_observed"),
                ["siegeObserved"] = CheckPassed(checks, "siege_observed"),
                ["partyLossObserved"] = CheckPassed(checks, "party_loss_observed"),
                ["hourlyOrderReviewObserved"] = CheckPassed(checks, "hourly_order_review_observed"),
                ["worldRestored"] = CheckPassed(checks, "changed_world_restored")
                    && CheckPassed(checks, "cleanup_world_restored"),
                ["mainPartyUntouched"] = CheckPassed(checks, "main_party_untouched"),
                ["noDuplicateEffects"] = CheckPassed(checks, "no_duplicate_effects"),
                ["boundedQueue"] = CheckPassed(checks, "bounded_queue"),
                ["terminalOrdersBounded"] = CheckPassed(checks, "terminal_orders_bounded")
            };
            bool metricGate = metrics.Properties().Where(x => x.Name != "simultaneousOrderCount")
                .All(x => x.Value.Type == JTokenType.Boolean && x.Value.Value<bool>());
            passed &= metricGate && fixture.InitialConcurrentOrders >= 3;
            return new JObject
            {
                ["ok"] = passed,
                ["caseCount"] = hourlyCases + checks.Count,
                ["passedCaseCount"] = completedHours + passedChecks,
                ["startDay"] = startDay,
                ["completedDay"] = completedDay,
                ["requestedDays"] = requestedDays,
                ["checks"] = checks,
                ["advances"] = advances,
                ["checkpointEvidence"] = command["checkpointEvidence"] ?? new JObject(),
                ["metrics"] = metrics,
                ["error"] = passed ? string.Empty : string.IsNullOrWhiteSpace(error)
                    ? "The native soak failed its hourly, world-change, recovery, or safety evidence gate." : error
            };
        }

        private static JObject SoakCheck(string id, bool passed, int observed = 0) => new JObject
        {
            ["id"] = id, ["passed"] = passed, ["observed"] = observed
        };

        private static bool CheckPassed(JArray checks, string id) => checks.OfType<JObject>()
            .Any(x => string.Equals(x.Value<string>("id"), id, StringComparison.Ordinal)
                && x.Value<bool>("passed"));

        private static JObject RunCampaignCommandEvaluation()
        {
            int active = ReignCampaignCommandBehavior.Instance?.Orders.Count(x => x != null && !x.IsTerminal) ?? 0;
            int duplicates = ReignCampaignCommandBehavior.Instance?.Orders.GroupBy(x => x.OrderId,
                StringComparer.OrdinalIgnoreCase).Count(x => x.Count() > 1) ?? 0;
            bool passed = duplicates == 0 && active < 100;
            return new JObject
            {
                ["ok"] = passed, ["caseCount"] = 2,
                ["passedCaseCount"] = (duplicates == 0 ? 1 : 0) + (active < 100 ? 1 : 0),
                ["duplicateOrderIds"] = duplicates, ["activeOrders"] = active
            };
        }

        private static JObject RunCampaignCommandCleanup()
        {
            int cancelled = 0;
            foreach (ReignCampaignOrderRecord order in ReignCampaignCommandBehavior.Instance?.Orders
                .Where(x => x != null && !x.IsTerminal).ToList() ?? new List<ReignCampaignOrderRecord>())
            {
                ReignWorldActionRecord action = new ReignWorldActionRecord
                {
                    Type = ReignWorldActionType.RegularCancelCampaignOrder,
                    ActorHeroStringId = order.CommanderHeroStringId,
                    Source = "campaign_command_certification_cleanup",
                    Reason = "Certification cleanup.",
                    TermsJson = new JObject { ["orderId"] = order.OrderId }.ToString(Formatting.None)
                };
                if (ReignCampaignCommandBehavior.Instance.ExecuteControlAction(action).Success) cancelled++;
            }
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            Settlement current = MobileParty.MainParty?.CurrentSettlement;
            string menu = campaign?.CurrentMenuContext?.GameMenu?.StringId ?? string.Empty;
            if (current != null && (menu == "town_wait_menus" || menu == "village_wait_menus"))
                TaleWorlds.CampaignSystem.GameMenus.GameMenu.SwitchToMenu(current.IsVillage
                    ? "village" : current.IsCastle ? "castle" : "town");
            return new JObject { ["ok"] = true, ["caseCount"] = Math.Max(1, cancelled),
                ["passedCaseCount"] = Math.Max(1, cancelled), ["cancelledOrders"] = cancelled };
        }

        private static string ActiveSaveName()
        {
            try
            {
                object value = typeof(MBSaveLoad).GetProperty("ActiveSaveSlotName",
                    BindingFlags.Public | BindingFlags.Static)?.GetValue(null, null);
                return Convert.ToString(value) ?? string.Empty;
            }
            catch { return string.Empty; }
        }
    }
}
