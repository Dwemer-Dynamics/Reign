using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private const string OrganicPrepareConfirmation = "prepare organic Spymaster test on disposable save";
        private const string OrganicIrreversibleConfirmation = "run irreversible Reign Spymaster test on disposable save";
        private const string ControlledOutcomeConfirmation = "force Reign Spymaster outcomes on disposable save";
        private const string ControlledAgentActionConfirmation = "force one Reign Spymaster foreign-agent action on disposable save";

        internal JObject RunSpymasterOrganicProfile(string runId, string phase, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string requestedPhase = (phase ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            switch (requestedPhase)
            {
                case "preflight": return OrganicPreflight(normalizedRunId);
                case "prepare": return PrepareOrganicLedger(normalizedRunId, options);
                case "capture": return CaptureOrganicMissions(normalizedRunId, options);
                case "control_outcomes": return ControlOrganicMissionOutcomes(normalizedRunId, options);
                case "verify_controlled_outcomes": return VerifyControlledMissionOutcomes(normalizedRunId, options);
                case "verify_controlled_assassination": return VerifyControlledAssassination(normalizedRunId, options);
                case "verify_controlled_capture": return VerifyControlledCapture(normalizedRunId, options);
                case "control_agent_action": return ControlForeignAgentAction(normalizedRunId, options);
                case "verify_controlled_agent_action": return VerifyControlledForeignAgentAction(normalizedRunId);
                case "observe": return ObserveOrganicTest(normalizedRunId, options);
                case "evaluate": return EvaluateOrganicTest(normalizedRunId, options);
                case "verify_reload": return VerifyOrganicReload(normalizedRunId);
                case "attempt_breakout": return AttemptOrganicBreakout(normalizedRunId, options);
                case "cleanup_marker": return CleanupOrganicLedger(normalizedRunId);
                default: return OrganicFailure(normalizedRunId, requestedPhase, "Unsupported organic Spymaster phase.");
            }
        }

        internal JObject BuildSpymasterOrganicAgentRequest()
        {
            return BuildForeignAgentSnapshot(CurrentDayFloat());
        }

        private JObject OrganicPreflight(string runId)
        {
            JArray assertions = new JArray();
            Kingdom player = Clan.PlayerClan?.Kingdom;
            Hero spymaster = ActiveSpymaster;
            Settlement playerSettlement = OrganicPlayerSettlement(player);
            Settlement foreignSettlement = OrganicForeignSettlement(player, 0);
            Hero foreignHero = OrganicForeignHero(player, 0);
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            List<Hero> candidates = Hero.AllAliveHeroes.Where(x => IsEligibleForeignAgentCandidate(x, player, player?.Leader, adultAge))
                .Where(x => x.IsNotable ? ResolveNotableFortification(x)?.IsFortification == true
                    : ResolveNobleFortification(x, player)?.IsFortification == true).ToList();
            AddOrganicAssertion(assertions, "organic_campaign_ready", TaleWorlds.CampaignSystem.Campaign.Current != null && Hero.MainHero != null
                && player != null && player.Leader == Hero.MainHero, "A loaded player-ruled kingdom is available.", null);
            AddOrganicAssertion(assertions, "organic_spymaster_ready", spymaster?.IsAlive == true && spymaster.IsActive && !spymaster.IsPrisoner,
                "A living, active, free Spymaster is appointed.", SnapshotHero(spymaster));
            AddOrganicAssertion(assertions, "organic_player_holding", playerSettlement?.IsFortification == true,
                "At least one player town or castle can receive a genuine foreign-agent operation.", OrganicSettlementSnapshot(playerSettlement));
            AddOrganicAssertion(assertions, "organic_foreign_targets", foreignSettlement?.IsFortification == true && foreignHero != null,
                "Foreign settlement and noble targets exist for genuine player operations.", new JObject
                {
                    ["settlement"] = OrganicSettlementSnapshot(foreignSettlement), ["hero"] = SnapshotHero(foreignHero)
                });
            AddOrganicAssertion(assertions, "organic_agent_candidates", candidates.Count > 0,
                "The player realm contains local nobles or notables that the production recruitment tick can evaluate.", new JObject
                {
                    ["total"] = candidates.Count, ["nobles"] = candidates.Count(x => x.IsLord), ["notables"] = candidates.Count(x => x.IsNotable)
                });
            AddOrganicAssertion(assertions, "organic_bridge_isolation", !ReignSpymasterTestRuntime.Active,
                "No forced-roll or consequence-suppression runtime is active.", null);
            JObject result = OrganicResult(runId, "preflight", assertions);
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest();
            result["recommendedTargets"] = new JObject
            {
                ["playerSettlementId"] = playerSettlement?.StringId ?? string.Empty,
                ["foreignSettlementId"] = foreignSettlement?.StringId ?? string.Empty,
                ["foreignHeroId"] = foreignHero?.StringId ?? string.Empty
            };
            return result;
        }

        private JObject PrepareOrganicLedger(string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], OrganicPrepareConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "prepare", "The exact disposable-save preparation confirmation is required.");
            ReignSpymasterState state = EnsureSpymasterState();
            ReignSpymasterOrganicTestLedger existing = FindOrganicLedger(runId);
            if (existing != null)
            {
                JObject idempotent = ObserveOrganicTest(runId, new JObject { ["stage"] = "prepare_replay" });
                idempotent["idempotent"] = true;
                return idempotent;
            }
            JObject preflight = OrganicPreflight(runId);
            if (preflight.Value<bool?>("ok") != true) return preflight;
            if (state.Missions.Any(x => x.State == ReignSpymasterMissionState.Active))
                return OrganicFailure(runId, "prepare", "Finish or cancel existing Spymaster missions before cloning the disposable acceptance save.");

            Kingdom player = Clan.PlayerClan?.Kingdom;
            ReignSpymasterOrganicTestLedger ledger = new ReignSpymasterOrganicTestLedger
            {
                RunId = runId,
                CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                SpymasterHeroStringId = ActiveSpymaster?.StringId ?? string.Empty,
                StartedDay = CurrentDayFloat(),
                LastObservedDay = CurrentDayFloat(),
                PreviousAgentRecruitmentDay = state.LastAgentRecruitmentDay,
                StartingGold = Hero.MainHero?.Gold ?? 0,
                DisposableSaveConfirmed = true,
                PlayerKingdomStringId = player?.StringId ?? string.Empty,
                PlayerSettlementStringId = OrganicPlayerSettlement(player)?.StringId ?? string.Empty,
                ForeignSettlementStringId = OrganicForeignSettlement(player, 0)?.StringId ?? string.Empty,
                ForeignHeroStringId = OrganicForeignHero(player, 0)?.StringId ?? string.Empty,
                InitialMissionIds = state.Missions.Select(x => x.MissionId).ToList(),
                InitialAgentHeroIds = state.ForeignAgents.Select(x => x.AgentHeroStringId).ToList(),
                InitialActionIds = state.ForeignAgentActions.Select(x => x.ActionId).ToList(),
                InitialEffectIds = state.Effects.Select(x => x.EffectId).ToList()
            };
            state.OrganicTestLedgers.Add(ledger);
            // Establish a known observation boundary without creating an agent or
            // influencing any future production roll.
            state.LastAgentRecruitmentDay = ledger.StartedDay;
            StateChanged?.Invoke();
            JObject result = ObserveOrganicTest(runId, new JObject { ["stage"] = "prepared" });
            result["preflight"] = preflight;
            result["requiresDisposableSave"] = true;
            result["nextNaturalAgentTickDay"] = ledger.StartedDay + 5f;
            return result;
        }

        private JObject CaptureOrganicMissions(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "capture", "Prepare the organic ledger on a disposable save first.");
            ReignSpymasterState state = EnsureSpymasterState();
            HashSet<string> known = new HashSet<string>(ledger.InitialMissionIds.Concat(ledger.MissionIds), StringComparer.OrdinalIgnoreCase);
            List<ReignSpymasterMission> added = state.Missions.Where(x => x.StartedDay + 0.001f >= ledger.StartedDay && !known.Contains(x.MissionId))
                .OrderBy(x => x.StartedDay).ToList();
            string label = (string)options["batch"] ?? "organic_batch";
            ReignSpymasterOrganicBatchRecord batch = new ReignSpymasterOrganicBatchRecord
            {
                BatchId = "organic_batch_" + Guid.NewGuid().ToString("N"),
                Label = label,
                CapturedDay = CurrentDayFloat(),
                AgentsBefore = state.ForeignAgents.Count,
                ActionsBefore = state.ForeignAgentActions.Count,
                EffectsBefore = state.Effects.Count,
                MissionIds = added.Select(x => x.MissionId).ToList(),
                PreSnapshotJson = OrganicLedgerCheckpoint(ledger, "before_" + label).ToString(Formatting.None)
            };
            ledger.MissionIds.AddRange(batch.MissionIds);
            ledger.MissionIds = ledger.MissionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ledger.Batches.Add(batch);
            StateChanged?.Invoke();
            JArray assertions = new JArray();
            int minimum = Math.Max(0, (int?)options["minimumNewMissions"] ?? 1);
            AddOrganicAssertion(assertions, "organic_missions_captured", added.Count >= minimum,
                "UI-submitted production missions were attached to the persisted organic ledger.", new JObject
                {
                    ["batch"] = label, ["captured"] = added.Count, ["minimum"] = minimum,
                    ["missionIds"] = new JArray(batch.MissionIds)
                });
            AddOrganicAssertion(assertions, "organic_native_random_path", !ReignSpymasterTestRuntime.Active
                && added.All(x => (x.MissionId ?? string.Empty).StartsWith("spy_", StringComparison.OrdinalIgnoreCase)
                    && !(x.MissionId ?? string.Empty).StartsWith("spytest_", StringComparison.OrdinalIgnoreCase)),
                "Captured missions use ordinary production IDs and native Bannerlord rolls.", null);
            JObject result = OrganicResult(runId, "capture", assertions);
            result["batch"] = JObject.FromObject(batch);
            result["state"] = OrganicStateSnapshot(ledger);
            return result;
        }

        private JObject ControlOrganicMissionOutcomes(string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], ControlledOutcomeConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "control_outcomes", "The exact controlled-outcome confirmation is required.");
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "control_outcomes", "Prepare the organic ledger on a disposable save first.");
            string batchLabel = (string)options["batch"] ?? string.Empty;
            ReignSpymasterOrganicBatchRecord batch = ledger.Batches.LastOrDefault(x =>
                string.Equals(x.Label, batchLabel, StringComparison.OrdinalIgnoreCase));
            if (batch == null)
                return OrganicFailure(runId, "control_outcomes", "The requested captured mission batch does not exist.");
            HashSet<string> requestedTypes = new HashSet<string>(
                (options["missionTypes"] as JArray ?? new JArray()).Values<string>(),
                StringComparer.OrdinalIgnoreCase);
            float unitRoll = Math.Max(0f, Math.Min(0.999999f, (float?)options["unitRoll"] ?? 0f));
            float? consequenceUnitRoll = options["consequenceUnitRoll"] == null
                ? (float?)null : Math.Max(0f, Math.Min(0.999999f, (float)options["consequenceUnitRoll"]));
            List<ReignSpymasterMission> missions = EnsureSpymasterState().Missions.Where(x =>
                batch.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)
                && x.State == ReignSpymasterMissionState.Active
                && (requestedTypes.Count == 0 || requestedTypes.Contains(x.MissionType))).ToList();
            int minimum = Math.Max(1, (int?)options["minimum"] ?? 1);
            if (missions.Count < minimum)
                return OrganicFailure(runId, "control_outcomes", "The captured batch did not contain enough active requested missions.");
            foreach (ReignSpymasterMission mission in missions)
                ReignSpymasterTestRuntime.ForceMissionOutcome(runId, mission.MissionId, unitRoll,
                    consequenceUnitRoll);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_outcomes_exact_missions", missions.Count >= minimum,
                "Controlled rolls were bound only to exact captured production mission ids.", new JObject
                {
                    ["batch"] = batchLabel, ["minimum"] = minimum, ["unitRoll"] = unitRoll,
                    ["consequenceUnitRoll"] = consequenceUnitRoll,
                    ["missionIds"] = new JArray(missions.Select(x => x.MissionId)),
                    ["missionTypes"] = new JArray(missions.Select(x => x.MissionType))
                });
            JObject result = OrganicResult(runId, "control_outcomes", assertions);
            result["pendingExactMissionOverrides"] = ReignSpymasterTestRuntime.PendingMissionOutcomes(runId);
            result["normalCampaignIsolation"] = "Only listed mission GUIDs can consume these non-serialized one-shot rolls.";
            return result;
        }

        private JObject VerifyControlledMissionOutcomes(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null) return OrganicFailure(runId, "verify_controlled_outcomes", "No organic Spymaster ledger exists for this run.");
            string batchLabel = (string)options["batch"] ?? string.Empty;
            ReignSpymasterOrganicBatchRecord batch = ledger.Batches.LastOrDefault(x =>
                string.Equals(x.Label, batchLabel, StringComparison.OrdinalIgnoreCase));
            if (batch == null)
                return OrganicFailure(runId, "verify_controlled_outcomes", "The requested controlled batch does not exist.");
            HashSet<string> requestedTypes = new HashSet<string>(
                (options["missionTypes"] as JArray ?? new JArray()).Values<string>(),
                StringComparer.OrdinalIgnoreCase);
            HashSet<string> controlledMissionIds = new HashSet<string>(
                ReignSpymasterTestRuntime.ControlledMissionIds(runId),
                StringComparer.OrdinalIgnoreCase);
            List<ReignSpymasterMission> missions = EnsureSpymasterState().Missions.Where(x =>
                batch.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)
                && controlledMissionIds.Contains(x.MissionId)
                && (requestedTypes.Count == 0 || requestedTypes.Contains(x.MissionType))).ToList();
            int minimum = Math.Max(1, (int?)options["minimum"] ?? 1);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_outcomes_succeeded", missions.Count >= minimum
                && missions.All(x => x.State == ReignSpymasterMissionState.Succeeded && x.OutcomeRoll >= 0f),
                "Each exact controlled mission resolved successfully through the production resolver.",
                new JArray(missions.Select(OrganicMissionSnapshot)));
            AddOrganicAssertion(assertions, "controlled_outcomes_consumed", ReignSpymasterTestRuntime.PendingMissionOutcomes(runId) == 0,
                "Every one-shot mission override was consumed or safely discarded.", new JObject
                {
                    ["pending"] = ReignSpymasterTestRuntime.PendingMissionOutcomes(runId)
                });
            JObject result = OrganicResult(runId, "verify_controlled_outcomes", assertions);
            result["controlledMissionIds"] = new JArray(controlledMissionIds);
            result["state"] = OrganicStateSnapshot(ledger);
            return result;
        }

        private JObject VerifyControlledAssassination(string runId, JObject options)
        {
            ReignSpymasterMission mission = ControlledMissionFromBatch(runId, options);
            Hero target = mission == null ? null : Hero.AllAliveHeroes
                .Concat(Hero.DeadOrDisabledHeroes)
                .FirstOrDefault(x => x != null && string.Equals(x.StringId, mission.TargetStringId, StringComparison.Ordinal));
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_assassination_succeeded",
                mission?.State == ReignSpymasterMissionState.Succeeded && target?.IsAlive == false
                && (mission.ResultSummary ?? string.Empty).IndexOf("was assassinated", StringComparison.OrdinalIgnoreCase) >= 0,
                "The exact UI-submitted production assassination succeeded and the native target is dead.",
                mission == null ? null : OrganicMissionSnapshot(mission));
            AddOrganicAssertion(assertions, "controlled_assassination_controls_consumed",
                ReignSpymasterTestRuntime.PendingMissionOutcomes(runId) == 0,
                "Every exact assassination one-shot was consumed or discarded.", null);
            return OrganicResult(runId, "verify_controlled_assassination", assertions);
        }

        private JObject VerifyControlledCapture(string runId, JObject options)
        {
            ReignSpymasterMission mission = ControlledMissionFromBatch(runId, options);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_spymaster_captured",
                mission != null && mission.AttributionPending && ActiveSpymaster?.IsPrisoner == true
                && PendingCapturedSpymasterMission?.MissionId == mission.MissionId,
                "The exact failed production assassination captured the Spymaster without exposing the sponsor.",
                mission == null ? null : OrganicMissionSnapshot(mission));
            AddOrganicAssertion(assertions, "controlled_capture_controls_consumed",
                ReignSpymasterTestRuntime.PendingMissionOutcomes(runId) == 0,
                "The exact failure and capture one-shots were consumed.", null);
            return OrganicResult(runId, "verify_controlled_capture", assertions);
        }

        private ReignSpymasterMission ControlledMissionFromBatch(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            string batchLabel = (string)options["batch"] ?? string.Empty;
            ReignSpymasterOrganicBatchRecord batch = ledger?.Batches.LastOrDefault(x =>
                string.Equals(x.Label, batchLabel, StringComparison.OrdinalIgnoreCase));
            HashSet<string> controlled = new HashSet<string>(
                ReignSpymasterTestRuntime.ControlledMissionIds(runId), StringComparer.OrdinalIgnoreCase);
            return batch == null ? null : EnsureSpymasterState().Missions.FirstOrDefault(x =>
                batch.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)
                && controlled.Contains(x.MissionId));
        }

        private JObject ControlForeignAgentAction(string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], ControlledAgentActionConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "control_agent_action", "The exact controlled foreign-agent confirmation is required.");
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "control_agent_action", "Prepare the organic ledger on a disposable save first.");
            ReignForeignAgentRecord agent = EnsureSpymasterState().ForeignAgents.FirstOrDefault(x => x.Activated && !x.Exposed);
            if (agent == null)
                return OrganicFailure(runId, "control_agent_action", "No active hidden foreign agent is available for the exact one-shot action control.");
            float unitRoll = Math.Max(0f, Math.Min(0.999999f, (float?)options["unitRoll"] ?? 0f));
            ReignSpymasterTestRuntime.ForceForeignAgentAction(runId, agent.AgentHeroStringId, unitRoll);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_agent_action_exact_agent", true,
                "The one-shot action roll was bound to one exact active hidden foreign agent.", new JObject
                {
                    ["agentHeroId"] = agent.AgentHeroStringId,
                    ["sponsorKingdomId"] = agent.SponsorKingdomStringId,
                    ["settlementId"] = agent.SettlementStringId,
                    ["unitRoll"] = unitRoll,
                    ["productionActionChance"] = 0.58
                });
            JObject result = OrganicResult(runId, "control_agent_action", assertions);
            result["pending"] = ReignSpymasterTestRuntime.HasPendingForeignAgentAction(runId);
            return result;
        }

        private JObject VerifyControlledForeignAgentAction(string runId)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null) return OrganicFailure(runId, "verify_controlled_agent_action", "No organic Spymaster ledger exists for this run.");
            JObject receipt = ReignSpymasterTestRuntime.LastForeignAgentActionReceipt(runId);
            string actionId = (string)receipt?["actionId"] ?? string.Empty;
            ReignForeignAgentActionRecord action = EnsureSpymasterState().ForeignAgentActions.FirstOrDefault(x =>
                string.Equals(x.ActionId, actionId, StringComparison.OrdinalIgnoreCase));
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_agent_action_production_path", receipt?.Value<bool?>("consumed") == true
                && receipt.Value<bool?>("actionCreated") == true && action != null,
                "The exact one-shot roll crossed the real 58% production branch and created a normal foreign-agent action.", receipt);
            AddOrganicAssertion(assertions, "controlled_agent_action_consumed", !ReignSpymasterTestRuntime.HasPendingForeignAgentAction(runId),
                "The foreign-agent control was consumed exactly once and is no longer armed.", null);
            JObject result = OrganicResult(runId, "verify_controlled_agent_action", assertions);
            result["action"] = action == null ? null : JObject.FromObject(action);
            return result;
        }

        private JObject ObserveOrganicTest(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null) return OrganicFailure(runId, "observe", "No organic Spymaster ledger exists for this run.");
            JObject snapshot = OrganicStateSnapshot(ledger);
            snapshot["stage"] = (string)options?["stage"] ?? "observation";
            snapshot["capturedUtc"] = DateTime.UtcNow.ToString("o");
            ledger.LastObservedDay = CurrentDayFloat();
            ledger.ObservationJson.Add(OrganicLedgerCheckpoint(ledger, (string)snapshot["stage"]).ToString(Formatting.None));
            if (ledger.ObservationJson.Count > OrganicLedgerMaxObservations)
                ledger.ObservationJson.RemoveRange(0, ledger.ObservationJson.Count - OrganicLedgerMaxObservations);
            StateChanged?.Invoke();
            JObject result = new JObject
            {
                ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "observe",
                ["snapshot"] = snapshot, ["agentRequest"] = BuildSpymasterOrganicAgentRequest(),
                ["testRuntimeActive"] = ReignSpymasterTestRuntime.Active
            };
            return result;
        }

        private JObject EvaluateOrganicTest(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null) return OrganicFailure(runId, "evaluate", "No organic Spymaster ledger exists for this run.");
            ReignSpymasterState state = EnsureSpymasterState();
            List<ReignSpymasterMission> missions = state.Missions.Where(x => ledger.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignSpymasterMission> resolved = missions.Where(x => x.State != ReignSpymasterMissionState.Active).ToList();
            List<ReignForeignAgentRecord> newAgents = state.ForeignAgents.Where(x => !ledger.InitialAgentHeroIds.Contains(x.AgentHeroStringId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignForeignAgentActionRecord> newActions = state.ForeignAgentActions.Where(x => !ledger.InitialActionIds.Contains(x.ActionId, StringComparer.OrdinalIgnoreCase)).ToList();
            JArray assertions = new JArray();
            int minimumResolved = Math.Max(0, (int?)options["minimumResolved"] ?? 1);
            AddOrganicAssertion(assertions, "organic_minimum_resolved", resolved.Count >= minimumResolved,
                "The requested number of production missions resolved through ordinary campaign ticks.", new JObject
                { ["resolved"] = resolved.Count, ["active"] = missions.Count - resolved.Count, ["minimum"] = minimumResolved });
            AddOrganicAssertion(assertions, "organic_outcome_roll_consistency", resolved.All(OrganicOutcomeMatchesDisplayedChance),
                "Every resolved mission agrees with its stored displayed success probability and native outcome roll.", new JArray(resolved.Select(OrganicMissionSnapshot)));
            AddOrganicAssertion(assertions, "organic_detection_roll_consistency", resolved.All(OrganicDetectionMatchesDisplayedChance),
                "Failure detection and attribution fields agree with the stored probability snapshots and native rolls.", null);
            AddOrganicAssertion(assertions, "organic_no_duplicate_outcomes", missions.Select(x => x.MissionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == missions.Count
                && newActions.Select(x => x.ActionId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == newActions.Count,
                "Mission and enemy-agent outcomes remain exactly-once.", null);
            AddOrganicAssertion(assertions, "organic_runtime_never_forced", !ReignSpymasterTestRuntime.Active
                && missions.All(x => !(x.MissionId ?? string.Empty).StartsWith("spytest_", StringComparison.OrdinalIgnoreCase)),
                "The acceptance run used production IDs and no forced-roll runtime.", null);

            string[] sabotageTypes = { "disrupt_food", "disrupt_construction", "disrupt_security", "disrupt_loyalty" };
            List<ReignSpymasterMission> successfulSabotage = resolved.Where(x => x.State == ReignSpymasterMissionState.Succeeded
                && sabotageTypes.Contains(x.MissionType, StringComparer.OrdinalIgnoreCase)).ToList();
            bool sabotageEffects = successfulSabotage.All(m => state.Effects.Any(e => e.SettlementStringId == m.TargetStringId
                && e.AgentHeroStringId == m.SpymasterHeroStringId && e.EffectType == m.MissionType.Replace("disrupt_", string.Empty)));
            AddOrganicAssertion(assertions, "organic_player_sabotage_effects", sabotageEffects,
                "Every successful player sabotage created the matching production settlement effect.", new JArray(successfulSabotage.Select(OrganicMissionSnapshot)));
            if ((bool?)options["requireAllSabotageSuccess"] == true)
                AddOrganicAssertion(assertions, "organic_all_sabotage_types_succeeded", sabotageTypes.All(t => successfulSabotage.Any(m => m.MissionType == t)),
                    "Food, construction, security, and loyalty sabotage each succeeded naturally at least once.", null);

            List<ReignSpymasterMission> intelligence = resolved.Where(x => x.MissionType == "land_intelligence"
                || x.MissionType.StartsWith("person_", StringComparison.OrdinalIgnoreCase)).ToList();
            bool reportsValid = intelligence.Where(x => x.State == ReignSpymasterMissionState.Succeeded)
                .All(x => !string.IsNullOrWhiteSpace(x.ReportJson) && x.ReportJson != "{}");
            AddOrganicAssertion(assertions, "organic_intelligence_reports", reportsValid,
                "Every naturally successful intelligence operation retained a report payload.", null);
            if ((bool?)options["requireIntelligenceSuccess"] == true)
                AddOrganicAssertion(assertions, "organic_intelligence_success_observed", intelligence.Any(x => x.State == ReignSpymasterMissionState.Succeeded),
                    "At least one player intelligence operation against the configured targets succeeded naturally.", null);
            if ((bool?)options["requirePeopleScopes"] == true)
            {
                Kingdom player = Clan.PlayerClan?.Kingdom;
                bool ownNoble = intelligence.Any(x => x.MissionType == "person_skills" && FindHero(x.TargetStringId)?.IsLord == true
                    && FindHero(x.TargetStringId)?.Clan?.Kingdom == player);
                bool ownNotable = intelligence.Any(x => x.MissionType == "person_relationships" && FindHero(x.TargetStringId)?.IsNotable == true
                    && FindHero(x.TargetStringId)?.HomeSettlement?.MapFaction == player);
                bool foreignNoble = intelligence.Any(x => x.MissionType == "person_rumors" && FindHero(x.TargetStringId)?.IsLord == true
                    && FindHero(x.TargetStringId)?.Clan?.Kingdom != null && FindHero(x.TargetStringId)?.Clan?.Kingdom != player);
                AddOrganicAssertion(assertions, "organic_people_scopes", ownNoble && ownNotable && foreignNoble,
                    "Own-noble, own-notable, and foreign-noble target filters each submitted their intended production investigation.", null);
            }

            if ((bool?)options["requireSocialSuccess"] == true)
                AddOrganicAssertion(assertions, "organic_social_success_observed", resolved.Any(x => (x.MissionType.StartsWith("fabricate_", StringComparison.OrdinalIgnoreCase)
                    || x.MissionType.StartsWith("mitigate_", StringComparison.OrdinalIgnoreCase)) && x.State == ReignSpymasterMissionState.Succeeded
                    && !string.IsNullOrWhiteSpace(x.ReportJson) && x.ReportJson != "{}"),
                    "At least one genuine social operation succeeded and synchronized a server result.", null);
            if ((bool?)options["requireAssassinationAttempt"] == true)
                AddOrganicAssertion(assertions, "organic_assassination_branches_observed", resolved.Any(x => x.MissionType == "assassinate_person")
                    && resolved.Any(x => x.MissionType == "assassinate_governor"),
                    "Both foreign-person and governed-settlement assassination attempts resolved through the production path.", null);

            bool uniqueAffiliation = state.ForeignAgents.GroupBy(x => x.AgentHeroStringId, StringComparer.OrdinalIgnoreCase)
                .All(g => g.Select(x => x.SponsorKingdomStringId).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1);
            AddOrganicAssertion(assertions, "organic_agent_affiliation_unique", uniqueAffiliation,
                "Each hidden NPC retains at most one foreign kingdom affiliation.", null);
            AddOrganicAssertion(assertions, "organic_one_agent_per_enemy_kingdom", state.ForeignAgents
                .Where(x => !x.Exposed).GroupBy(x => x.SponsorKingdomStringId, StringComparer.OrdinalIgnoreCase).All(g => g.Count() <= 1),
                "No enemy kingdom retains more than one hidden active or dormant agent; exposed agents remain historical records.", null);
            AddOrganicAssertion(assertions, "organic_agent_locality", newAgents.All(x => !string.IsNullOrWhiteSpace(x.SettlementStringId)
                && OrganicSettlementBelongsToPlayer(x.SettlementStringId)),
                "Naturally recruited noble and notable agents map to a valid player town or castle.", null);
            AddOrganicAssertion(assertions, "organic_agent_tick_timing", newAgents.All(x => x.RecruitedDay + 0.001f >= ledger.StartedDay + 5f),
                "No organic foreign agent appears before the first genuine five-day recruitment boundary.", null);
            if ((bool?)options["requireNobleAndNotableAgents"] == true)
                AddOrganicAssertion(assertions, "organic_noble_and_notable_agents", newAgents.Any(x => x.IsNotable)
                    && newAgents.Any(x => !x.IsNotable),
                    "The long-horizon run observed both notable-locality and noble-locality recruitment.", null);
            if ((bool?)options["requireAgentRecruitment"] == true)
                AddOrganicAssertion(assertions, "organic_agent_recruited", newAgents.Count > 0,
                    "At least one foreign agent was recruited by the genuine five-day server tick.", new JArray(newAgents.Select(x => JObject.FromObject(x))));
            bool actionTargetsPlayer = newActions.All(x => OrganicSettlementBelongsToPlayer(x.SettlementStringId));
            bool actionEffectsApplied = newActions.All(a => state.Effects.Any(e => e.SettlementStringId == a.SettlementStringId
                && e.AgentHeroStringId == a.AgentHeroStringId && e.EffectType == a.EffectType));
            AddOrganicAssertion(assertions, "organic_enemy_actions_valid", actionTargetsPlayer && actionEffectsApplied
                && newActions.All(x => new[] { "food", "construction", "security", "loyalty" }.Contains(x.EffectType)),
                "Every observed foreign-agent action targeted a player fortification and applied its supported production effect.", new JArray(newActions.Select(x => JObject.FromObject(x))));
            if ((bool?)options["requireAgentAction"] == true)
                AddOrganicAssertion(assertions, "organic_enemy_action_observed", newActions.Count > 0,
                    "At least one foreign agent naturally conducted an operation against a player holding.", null);

            List<ReignSpymasterMission> counter = resolved.Where(x => x.MissionType == "counterintelligence"
                && x.State == ReignSpymasterMissionState.Succeeded).ToList();
            bool exposed = counter.Any(x => !(x.ResultSummary ?? string.Empty).StartsWith("No foreign network", StringComparison.OrdinalIgnoreCase));
            if ((bool?)options["requireCounterintelligence"] == true)
                AddOrganicAssertion(assertions, "organic_counterintelligence_exposed_agent", exposed
                    && newAgents.Any(x => x.Exposed && x.ExposedDay >= ledger.StartedDay)
                    && newActions.Where(x => counter.Any(c => c.ResolvedDay + 0.001f >= x.WorldDay)).All(x => x.Detected && x.SponsorAttributed),
                    "A naturally successful player counterintelligence mission durably identified an existing agent and attributed its recorded operations.", null);

            double expected = resolved.Sum(x => Math.Max(0d, Math.Min(1d, x.SuccessChance / 100d)));
            double variance = resolved.Sum(x => { double p = Math.Max(0d, Math.Min(1d, x.SuccessChance / 100d)); return p * (1d - p); });
            int observedSuccess = resolved.Count(x => x.State == ReignSpymasterMissionState.Succeeded);
            double tolerance = Math.Max(2d, 3d * Math.Sqrt(Math.Max(0d, variance)));
            AddOrganicAssertion(assertions, "organic_probability_aggregate", Math.Abs(observedSuccess - expected) <= tolerance,
                "Aggregate natural outcomes remain within a three-sigma envelope of their displayed probabilities.", new JObject
                { ["trials"] = resolved.Count, ["observedSuccesses"] = observedSuccess, ["expectedSuccesses"] = expected, ["tolerance"] = tolerance });

            JObject result = OrganicResult(runId, "evaluate", assertions);
            result["snapshot"] = OrganicStateSnapshot(ledger);
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest();
            result["recommendedNext"] = OrganicRecommendedNext(assertions);
            return result;
        }

        private JObject VerifyOrganicReload(string runId)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "organic_ledger_survived_reload", ledger != null && ledger.DisposableSaveConfirmed
                && string.Equals(ledger.CampaignId, ReignCampaignIdentity.CurrentCampaignId(), StringComparison.OrdinalIgnoreCase),
                "The organic acceptance ledger survived a native save/process restart/load boundary.", ledger == null ? null : JObject.FromObject(ledger));
            if (ledger != null)
            {
                ReignSpymasterState state = EnsureSpymasterState();
                AddOrganicAssertion(assertions, "organic_tracked_state_survived_reload", ledger.MissionIds.All(id => state.Missions.Any(x => x.MissionId == id))
                    && state.AppliedForeignAgentActionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == state.AppliedForeignAgentActionIds.Count,
                    "Tracked missions and enemy-action idempotency keys survived reload without duplication.", null);
            }
            JObject result = OrganicResult(runId, "verify_reload", assertions);
            if (ledger != null) result["snapshot"] = OrganicStateSnapshot(ledger);
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest();
            return result;
        }

        private JObject AttemptOrganicBreakout(string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "attempt_breakout", "Prepare the organic ledger on a disposable save first.");
            if ((bool?)options["allowIrreversible"] != true
                || !string.Equals((string)options["confirmation"], OrganicIrreversibleConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "attempt_breakout", "The exact irreversible disposable-save confirmation is required.");
            if (!CanAttemptSpymasterBreakout)
                return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "attempt_breakout",
                    ["status"] = "not_applicable", ["message"] = "No naturally captured Spymaster is currently eligible; repeat the isolated assassination attempt if this branch still needs observation." };
            float chance = GetSpymasterBreakoutChance();
            ReignSpymasterMission pendingMission = PendingCapturedSpymasterMission;
            bool controlled = options["unitRoll"] != null;
            if (controlled)
            {
                if (!string.Equals((string)options["controlConfirmation"], ControlledOutcomeConfirmation,
                    StringComparison.Ordinal))
                    return OrganicFailure(runId, "attempt_breakout", "The exact controlled-outcome confirmation is required.");
                float unitRoll = Math.Max(0f, Math.Min(0.999999f, (float)options["unitRoll"]));
                ReignSpymasterTestRuntime.ForceBreakoutOutcome(runId, pendingMission.MissionId, unitRoll);
            }
            string message = AttemptSpymasterBreakout();
            if (!controlled)
                return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "attempt_breakout",
                    ["status"] = "attempted", ["displayedChance"] = chance, ["message"] = message,
                    ["playerPrisoner"] = Hero.MainHero?.IsPrisoner == true, ["spymasterPrisoner"] = ActiveSpymaster?.IsPrisoner == true };
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_breakout_succeeded",
                string.IsNullOrEmpty(message) && ActiveSpymaster?.IsPrisoner != true
                && pendingMission.AttributionPending == false
                && (pendingMission.ResultSummary ?? string.Empty).IndexOf("personally broke", StringComparison.OrdinalIgnoreCase) >= 0,
                "The production breakout consumed the exact one-shot, freed the Spymaster, and preserved sponsor concealment.",
                OrganicMissionSnapshot(pendingMission));
            AddOrganicAssertion(assertions, "controlled_breakout_control_consumed",
                ReignSpymasterTestRuntime.PendingMissionOutcomes(runId) == 0,
                "The exact breakout one-shot was consumed.", null);
            JObject result = OrganicResult(runId, "attempt_breakout", assertions);
            result["displayedChance"] = chance;
            result["playerPrisoner"] = Hero.MainHero?.IsPrisoner == true;
            result["spymasterPrisoner"] = ActiveSpymaster?.IsPrisoner == true;
            return result;
        }

        private JObject CleanupOrganicLedger(string runId)
        {
            ReignSpymasterState state = EnsureSpymasterState();
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(runId);
            int removed = state.OrganicTestLedgers.RemoveAll(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
            if (ledger != null && state.OrganicTestLedgers.Count == 0) state.LastAgentRecruitmentDay = ledger.PreviousAgentRecruitmentDay;
            ReignSpymasterTestRuntime.Reset();
            StateChanged?.Invoke();
            return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "cleanup_marker",
                ["ledgersRemoved"] = removed, ["requiresSaveRollback"] = true,
                ["message"] = "The marker was removed. Restore the pre-test checkpoint and delete the disposable native save/Save Sync point to undo organic world effects." };
        }

        private ReignSpymasterOrganicTestLedger FindOrganicLedger(string runId)
        {
            return EnsureSpymasterState().OrganicTestLedgers.FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        private JObject OrganicLedgerCheckpoint(ReignSpymasterOrganicTestLedger ledger, string stage)
        {
            ReignSpymasterState state = EnsureSpymasterState();
            return new JObject
            {
                ["schemaVersion"] = 1,
                ["stage"] = stage ?? "checkpoint",
                ["worldDay"] = CurrentDayFloat(),
                ["gold"] = Hero.MainHero?.Gold ?? 0,
                ["trackedMissionCount"] = ledger?.MissionIds?.Count ?? 0,
                ["activeMissionCount"] = state.Missions.Count(x => x.State == ReignSpymasterMissionState.Active),
                ["foreignAgentCount"] = state.ForeignAgents.Count,
                ["foreignAgentActionCount"] = state.ForeignAgentActions.Count,
                ["settlementEffectCount"] = state.Effects.Count,
                ["testRuntimeActive"] = ReignSpymasterTestRuntime.Active
            };
        }

        private JObject OrganicStateSnapshot(ReignSpymasterOrganicTestLedger ledger)
        {
            ReignSpymasterState state = EnsureSpymasterState();
            List<ReignSpymasterMission> missions = state.Missions.Where(x => ledger.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignForeignAgentRecord> agents = state.ForeignAgents.Where(x => !ledger.InitialAgentHeroIds.Contains(x.AgentHeroStringId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignForeignAgentActionRecord> actions = state.ForeignAgentActions.Where(x => !ledger.InitialActionIds.Contains(x.ActionId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignSpymasterSettlementEffect> effects = state.Effects.Where(x => !ledger.InitialEffectIds.Contains(x.EffectId, StringComparer.OrdinalIgnoreCase)).ToList();
            return new JObject
            {
                ["runId"] = ledger.RunId, ["campaignId"] = ledger.CampaignId, ["worldDay"] = CurrentDayFloat(),
                ["startedDay"] = ledger.StartedDay, ["elapsedDays"] = CurrentDayFloat() - ledger.StartedDay,
                ["startingGold"] = ledger.StartingGold, ["currentGold"] = Hero.MainHero?.Gold ?? 0,
                ["spymaster"] = SnapshotHero(ActiveSpymaster), ["missionCapacity"] = SpymasterMissionCapacity,
                ["activeMissionCount"] = ActiveSpymasterMissionCount, ["missions"] = new JArray(missions.Select(OrganicMissionSnapshot)),
                ["newAgents"] = new JArray(agents.Select(x => JObject.FromObject(x))), ["newAgentActions"] = new JArray(actions.Select(x => JObject.FromObject(x))),
                ["newEffects"] = new JArray(effects.Select(x => JObject.FromObject(x))), ["batches"] = JArray.FromObject(ledger.Batches),
                ["playerSettlement"] = OrganicSettlementSnapshot(FindSettlement(ledger.PlayerSettlementStringId)),
                ["foreignSettlement"] = OrganicSettlementSnapshot(FindSettlement(ledger.ForeignSettlementStringId)),
                ["testRuntimeActive"] = ReignSpymasterTestRuntime.Active
            };
        }

        private static JObject OrganicMissionSnapshot(ReignSpymasterMission mission)
        {
            return mission == null ? new JObject() : new JObject
            {
                ["missionId"] = mission.MissionId, ["type"] = mission.MissionType, ["targetId"] = mission.TargetStringId,
                ["targetName"] = mission.TargetName, ["state"] = mission.State.ToString(), ["cost"] = mission.GoldCost,
                ["startedDay"] = mission.StartedDay, ["dueDay"] = mission.DueDay, ["resolvedDay"] = mission.ResolvedDay,
                ["successChance"] = mission.SuccessChance, ["detectionChance"] = mission.DetectionChance,
                ["outcomeRoll"] = mission.OutcomeRoll, ["detectionRoll"] = mission.DetectionRoll,
                ["attributionRoll"] = mission.AttributionRoll, ["targetNoticed"] = mission.TargetNoticed,
                ["playerIdentified"] = mission.PlayerIdentified, ["attributionPending"] = mission.AttributionPending,
                ["summary"] = mission.ResultSummary, ["reportJson"] = mission.ReportJson,
                ["history"] = new JArray(mission.History ?? new List<string>())
            };
        }

        private static bool OrganicOutcomeMatchesDisplayedChance(ReignSpymasterMission mission)
        {
            if (mission == null || mission.State == ReignSpymasterMissionState.Cancelled) return true;
            if (mission.OutcomeRoll < 0f || mission.OutcomeRoll >= 100f) return false;
            bool expectedSuccess = mission.OutcomeRoll < mission.SuccessChance;
            return expectedSuccess == (mission.State == ReignSpymasterMissionState.Succeeded);
        }

        private static bool OrganicDetectionMatchesDisplayedChance(ReignSpymasterMission mission)
        {
            if (mission == null || mission.State == ReignSpymasterMissionState.Succeeded || mission.State == ReignSpymasterMissionState.Cancelled) return true;
            if (mission.DetectionRoll < 0f || mission.DetectionRoll >= 100f) return false;
            bool assassination = (mission.MissionType ?? string.Empty).StartsWith("assassinate", StringComparison.OrdinalIgnoreCase);
            bool noticed = assassination || mission.DetectionRoll < mission.DetectionChance;
            if (noticed != mission.TargetNoticed) return false;
            if (!mission.PlayerIdentified) return true;
            return !assassination && mission.TargetNoticed && mission.AttributionRoll >= 0f
                && mission.AttributionRoll < Math.Max(5f, Math.Min(95f, mission.DetectionChance * 0.7f));
        }

        private Settlement OrganicPlayerSettlement(Kingdom player)
        {
            return Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true && x.OwnerClan?.Kingdom == player)
                .OrderBy(x => x.Settlement.StringId).Select(x => x.Settlement).FirstOrDefault();
        }

        private Settlement OrganicForeignSettlement(Kingdom player, int offset)
        {
            List<Settlement> rows = Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true && x.OwnerClan?.Kingdom != null
                && x.OwnerClan.Kingdom != player).OrderBy(x => x.Settlement.StringId).Select(x => x.Settlement).ToList();
            return rows.Count == 0 ? null : rows[Math.Abs(offset) % rows.Count];
        }

        private Hero OrganicForeignHero(Kingdom player, int offset)
        {
            List<Hero> rows = Hero.AllAliveHeroes.Where(x => x?.IsLord == true && x.Clan?.Kingdom != null && x.Clan.Kingdom != player)
                .OrderBy(x => x.StringId).ToList();
            return rows.Count == 0 ? null : rows[Math.Abs(offset) % rows.Count];
        }

        private static JObject OrganicSettlementSnapshot(Settlement settlement)
        {
            Town town = settlement?.Town;
            return settlement == null ? new JObject() : new JObject
            {
                ["settlementId"] = settlement.StringId, ["name"] = settlement.Name?.ToString() ?? string.Empty,
                ["isTown"] = settlement.IsTown, ["isCastle"] = settlement.IsCastle,
                ["ownerKingdomId"] = town?.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                ["governorId"] = town?.Governor?.StringId ?? string.Empty, ["food"] = town?.FoodStocks ?? 0f,
                ["loyalty"] = town?.Loyalty ?? 0f, ["security"] = town?.Security ?? 0f,
                ["prosperity"] = town?.Prosperity ?? 0f, ["garrison"] = town?.GarrisonParty?.MemberRoster?.TotalManCount ?? 0
            };
        }

        private bool OrganicSettlementBelongsToPlayer(string settlementId)
        {
            Settlement settlement = FindSettlement(settlementId);
            return settlement?.Town?.OwnerClan?.Kingdom != null && settlement.Town.OwnerClan.Kingdom == Clan.PlayerClan?.Kingdom;
        }

        private static void AddOrganicAssertion(JArray assertions, string id, bool passed, string summary, JToken data)
        {
            assertions.Add(new JObject { ["caseId"] = id, ["passed"] = passed, ["summary"] = summary ?? string.Empty,
                ["data"] = data ?? JValue.CreateNull() });
        }

        private static JObject OrganicResult(string runId, string phase, JArray assertions)
        {
            int passed = assertions.Count(x => (bool?)x["passed"] == true);
            return new JObject { ["ok"] = passed == assertions.Count, ["runId"] = runId, ["profile"] = "organic", ["phase"] = phase,
                ["passedCount"] = passed, ["failedCount"] = assertions.Count - passed, ["totalCount"] = assertions.Count,
                ["assertions"] = assertions };
        }

        private static JObject OrganicFailure(string runId, string phase, string error)
        {
            return new JObject { ["ok"] = false, ["runId"] = runId, ["profile"] = "organic", ["phase"] = phase,
                ["error"] = error ?? "Organic Spymaster phase failed." };
        }

        private static JArray OrganicRecommendedNext(JArray assertions)
        {
            JArray next = new JArray();
            HashSet<string> failed = new HashSet<string>(assertions.Where(x => (bool?)x["passed"] != true)
                .Select(x => (string)x["caseId"] ?? string.Empty), StringComparer.OrdinalIgnoreCase);
            if (failed.Contains("organic_minimum_resolved") || failed.Contains("organic_intelligence_success_observed")) next.Add("Run another intelligence wave and advance through its due day.");
            if (failed.Contains("organic_all_sabotage_types_succeeded")) next.Add("Repeat rotating sabotage waves until every type has a natural success.");
            if (failed.Contains("organic_agent_recruited") || failed.Contains("organic_enemy_action_observed")) next.Add("Advance another five-day natural agent cycle and observe again.");
            if (failed.Contains("organic_counterintelligence_exposed_agent")) next.Add("With an active hidden agent present, repeat a natural counterintelligence mission.");
            if (failed.Contains("organic_social_success_observed")) next.Add("Repeat fabrication/mitigation waves after confirming an applicable social item exists.");
            return next;
        }
    }
}
