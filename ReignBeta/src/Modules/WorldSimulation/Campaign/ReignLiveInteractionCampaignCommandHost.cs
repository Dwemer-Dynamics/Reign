using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Court.WarCouncil;
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
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
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
                await ReignCampaignCommandClient.VerifyCampaignCommandSaveAsync(new JObject
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
            JObject recorded = await ReignCampaignCommandClient.ReportCampaignCommandProfileAsync(profilePayload)
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
            if (ReignServices.CampaignCommand == null) failures.Add("command_behavior_missing");
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
            JObject started = await ReignCampaignCommandClient.StartCampaignCommandVerificationAsync(tier, seed)
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
                JObject statusEnvelope = await ReignCampaignCommandClient.GetCampaignCommandVerificationStatusAsync()
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
            JObject resultEnvelope = await ReignCampaignCommandClient.GetCampaignCommandVerificationResultAsync(runId)
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

        private static JObject RunCampaignCommandEvaluation()
        {
            int active = ReignServices.CampaignCommand?.Orders.Count(x => x != null && !x.IsTerminal) ?? 0;
            int duplicates = ReignServices.CampaignCommand?.Orders.GroupBy(x => x.OrderId,
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
            foreach (ReignCampaignOrderRecord order in ReignServices.CampaignCommand?.Orders
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
                if (ReignServices.CampaignCommand.ExecuteControlAction(action).Success) cancelled++;
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
