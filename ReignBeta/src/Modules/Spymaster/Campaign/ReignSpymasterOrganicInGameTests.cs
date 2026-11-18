using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Actions;
using ReignBeta.Campaign;
using ReignBeta.World;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Court.ReignCourtCampaignBehavior;

namespace ReignBeta.Court
{
    internal static partial class ReignSpymasterTestControls
    {
        private const string OrganicPrepareConfirmation = "prepare organic Spymaster test on disposable save";
        private const string OrganicIrreversibleConfirmation = "run irreversible Reign Spymaster test on disposable save";
        private const string ControlledOutcomeConfirmation = "force Reign Spymaster outcomes on disposable save";
        private const string ControlledAgentActionConfirmation = "force one Reign Spymaster foreign-agent action on disposable save";

        internal static JObject RunSpymasterActionControlProfile(ReignCourtCampaignBehavior court, string runId, string phase, JObject options)
            => RunAcceptedSpymasterControlProfile(court, runId, phase, options, false);

        internal static JObject RunSpymasterDestructiveActionControlProfile(ReignCourtCampaignBehavior court, string runId, string phase, JObject options)
        {
            if (options.Value<bool?>("destructiveActionControl") != true
                || options.Value<string>("irreversibleConfirmation") != OrganicIrreversibleConfirmation
                || options.Value<string>("controlConfirmation") != "control Reign Spymaster action test on disposable save"
                || (options["enrollment"] as JObject)?.Value<string>("source") != "reign_mcp_spymaster_destructive_action_control")
                return OrganicFailure(runId, phase, "Accepted assassination controls require their separate irreversible authorization.");
            return RunAcceptedSpymasterControlProfile(court, runId, phase, options, true);
        }

        private static JObject RunAcceptedSpymasterControlProfile(ReignCourtCampaignBehavior court, string runId, string phase, JObject options, bool destructive)
        {
            try
            {
                if (!runId.StartsWith(destructive ? "destructive_action_control_" : "action_control_", StringComparison.Ordinal)
                    || !((string)options["batch"] ?? "").StartsWith(destructive ? "destructive_action_" : "action_", StringComparison.Ordinal))
                    throw new InvalidOperationException("Action controls require their own ledger and batch namespace.");
                if (phase == "prepare")
                    return PrepareOrganicLedger(court, runId, new JObject { ["confirmation"] = OrganicPrepareConfirmation });
                if (phase == "observe") return ObserveOrganicTest(court, runId, options);
                if (phase != "capture" && phase != "control" && phase != "verify")
                    throw new InvalidOperationException("Unsupported accepted-action control phase.");
                ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
                if (ledger == null || !ledger.DisposableSaveConfirmed
                    || ledger.CampaignId != ReignCampaignIdentity.CurrentCampaignId()
                    || ledger.SpymasterHeroStringId != court.ActiveSpymaster?.StringId)
                    throw new InvalidOperationException("Prepare a ledger for the current campaign and appointed Spymaster first.");
                List<ReignSpymasterMission> missions = AcceptedActionControlMissions(court, ledger, options, phase != "verify", destructive);
                if (phase == "capture")
                {
                    JObject captured = CaptureOrganicMissions(court, runId, options, missions);
                    if (destructive && captured.Value<bool?>("ok") == true)
                    {
                        var capturedBatch = ledger.Batches.Last(x => x.Label == (string)options["batch"]);
                        JArray targets = new JArray(missions.Select(m =>
                        {
                            Hero target = AcceptedAssassinationHero(court, m);
                            return new JObject { ["missionId"] = m.MissionId, ["targetId"] = m.TargetStringId,
                                ["heroId"] = target?.StringId, ["isAlive"] = target?.IsAlive == true };
                        }));
                        JObject before = JObject.Parse(capturedBatch.PreSnapshotJson);
                        before["acceptedAssassinationTargets"] = targets;
                        string retained = before.ToString(Formatting.None);
                        if (retained.Length > OrganicLedgerMaxStoredJsonChars)
                            throw new InvalidOperationException("The bounded assassination target receipt exceeds the retained snapshot limit.");
                        capturedBatch.PreSnapshotJson = retained;
                        captured["assassinationTargetsBefore"] = targets.DeepClone();
                        captured["batch"] = JObject.FromObject(capturedBatch);
                        court.RaiseStateChanged();
                    }
                    return captured;
                }
                var batch = ledger.Batches.LastOrDefault(x => x.Label == (string)options["batch"]);
                if (batch == null || batch.MissionIds.Count != missions.Count
                    || !new HashSet<string>(batch.MissionIds, StringComparer.Ordinal).SetEquals(missions.Select(x => x.MissionId)))
                    throw new InvalidOperationException("The exact accepted agreements must match the captured batch.");
                var bounded = new JObject { ["batch"] = options["batch"], ["minimum"] = missions.Count,
                    ["confirmation"] = ControlledOutcomeConfirmation, ["unitRoll"] = 0f };
                if (phase == "control") return ControlOrganicMissionOutcomes(court, runId, bounded);
                JObject verified = VerifyControlledMissionOutcomes(court, runId, bounded);
                if (destructive)
                {
                    JArray assertions = (JArray)verified["assertions"];
                    var targets = new JArray(missions.Select(m =>
                    {
                        Hero target = CapturedAssassinationHero(batch, m.MissionId);
                        return new JObject { ["missionId"] = m.MissionId, ["targetId"] = m.TargetStringId,
                            ["heroId"] = target?.StringId,
                            ["isDead"] = target?.IsDead == true, ["isAlive"] = target?.IsAlive == true };
                    }));
                    AddOrganicAssertion(assertions, "accepted_assassination_native_deaths",
                        targets.All(t => t.Value<bool>("isDead") && !t.Value<bool>("isAlive"))
                        && missions.All(m => (m.ResultSummary ?? "").StartsWith(
                            CapturedAssassinationHero(batch, m.MissionId)?.Name + " was assassinated.", StringComparison.Ordinal)),
                        "Every exact signed assassination target died through the production resolver.", targets);
                    verified = OrganicResult(runId, "verify_controlled_outcomes", assertions);
                    verified["state"] = OrganicStateSnapshot(court, ledger);
                }
                return verified;
            }
            catch (Exception ex) { return OrganicFailure(runId, phase, ex.Message); }
        }

        private static List<ReignSpymasterMission> AcceptedActionControlMissions(ReignCourtCampaignBehavior court, ReignSpymasterOrganicTestLedger ledger,
            JObject options, bool requireActive, bool destructive)
        {
            JArray ids = options["agreementIds"] as JArray;
            if (ids == null || ids.Count < 1 || ids.Count > 3 || ids.Any(x => x.Type != JTokenType.String)
                || ids.Values<string>().Distinct(StringComparer.Ordinal).Count() != ids.Count)
                throw new InvalidOperationException("Supply one to three distinct saved conversation agreement IDs.");
            var result = new List<ReignSpymasterMission>();
            foreach (string id in ids.Values<string>())
            {
                AgreementJournal journal = ReignAgreementBehavior.Instance?.Agreements.FirstOrDefault(x => x.Agreement.Id == id);
                ActionAgreement agreement = journal?.Agreement;
                string context = ReignCampaignIdentity.CurrentCampaignId() + ":"
                    + (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") + ":" + court.ActiveSpymaster?.StringId;
                if (agreement == null || journal.OperatorAuthorized || agreement.Origin != ActionOrigin.Conversation
                    || !agreement.HasBothConsents || journal.CommittedHash != agreement.TermsHash()
                    || agreement.PlayerId != Hero.MainHero?.StringId || agreement.NpcId != court.ActiveSpymaster?.StringId
                    || agreement.ConversationId != context || agreement.Failure.Length > 0
                    || (requireActive && agreement.State != AgreementState.Waiting && agreement.State != AgreementState.InTransit)
                    || agreement.Clauses.Count < 1 || agreement.Clauses.Count > 3)
                    throw new InvalidOperationException("Only exact consenting, current, non-operator Spymaster conversations can be controlled.");
                foreach (ActionClause clause in agreement.Clauses)
                {
                    if (!clause.ActionId.StartsWith("spymaster.", StringComparison.Ordinal)
                        || clause.ActionId.StartsWith("spymaster.assassinate_", StringComparison.Ordinal) != destructive
                        || !ReignActionCatalog.All.Any(x => x.Id == clause.ActionId)
                        || clause.ActorId != court.ActiveSpymaster?.StringId || clause.RecipientId != Hero.MainHero?.StringId)
                        throw new InvalidOperationException(destructive
                            ? "Only registered assassination Spymaster clauses are allowed in the separately authorized route."
                            : "Only registered non-assassination Spymaster clauses are allowed.");
                    AgreementStep step = journal.Steps.SingleOrDefault(x => x.ClauseId == clause.Id && x.Installment == 0);
                    JObject receipt = step == null || string.IsNullOrEmpty(step.Snapshot) ? null : JObject.Parse(step.Snapshot);
                    string operation = "sa_" + ReignCourtTerms.Hash(agreement.Id + ":" + clause.Id);
                    ReignSpymasterMission mission = court.EnsureSpymasterState().Missions.FirstOrDefault(x => x.MissionId == operation);
                    if (step == null || !step.Started || step.Returned || receipt?.Value<string>("missionId") != operation
                        || receipt.Value<string>("operationId") != operation || mission == null
                        || mission.SpymasterHeroStringId != court.ActiveSpymaster?.StringId || mission.MissionType != SpyType(clause)
                        || mission.TargetStringId != SpyTarget(clause) || mission.StartedDay + 0.001f < ledger.StartedDay
                        || ledger.InitialMissionIds.Contains(operation) || requireActive && (step.Completed || mission.State != ReignSpymasterMissionState.Active))
                        throw new InvalidOperationException("The accepted step must identify its exact production mission after preparation.");
                    if (destructive)
                    {
                        var capturedBatch = ledger.Batches.LastOrDefault(x => x.Label == (string)options["batch"]);
                        Hero capturedTarget = capturedBatch == null ? null : CapturedAssassinationHero(capturedBatch, mission.MissionId);
                        Hero target = requireActive ? AcceptedAssassinationHero(court, mission) : capturedTarget;
                        if (target == null || target == Hero.MainHero || target == court.ActiveSpymaster
                            || requireActive && !target.IsAlive || capturedBatch != null && target != capturedTarget)
                            throw new InvalidOperationException("Accepted assassination controls require the exact captured living target outside the player and appointed Spymaster.");
                    }
                    result.Add(mission);
                }
            }
            if (result.Count > 3 || result.Select(x => x.MissionId).Distinct(StringComparer.Ordinal).Count() != result.Count)
                throw new InvalidOperationException("At most three distinct accepted production missions can be controlled.");
            return result;
        }

        private static Hero AcceptedAssassinationHero(ReignCourtCampaignBehavior court, ReignSpymasterMission mission) =>
            mission.MissionType == "assassinate_governor"
                ? FindSettlement(mission.TargetStringId)?.Town?.Governor : FindHero(mission.TargetStringId);

        private static Hero CapturedAssassinationHero(ReignSpymasterOrganicBatchRecord batch, string missionId)
        {
            JArray targets = JObject.Parse(batch.PreSnapshotJson)["acceptedAssassinationTargets"] as JArray;
            JToken target = targets?.SingleOrDefault(x => x.Value<string>("missionId") == missionId);
            if (target?.Value<bool?>("isAlive") != true) return null;
            string heroId = target.Value<string>("heroId");
            return Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                .FirstOrDefault(x => x != null && x.StringId == heroId);
        }

        internal static JObject RunSpymasterOrganicProfile(ReignCourtCampaignBehavior court, string runId, string phase, JObject options)
        {
            string normalizedRunId = string.IsNullOrWhiteSpace(runId) ? Guid.NewGuid().ToString("N") : runId.Trim();
            string requestedPhase = (phase ?? "preflight").Trim().ToLowerInvariant();
            options = options ?? new JObject();
            switch (requestedPhase)
            {
                case "preflight": return OrganicPreflight(court, normalizedRunId);
                case "prepare": return PrepareOrganicLedger(court, normalizedRunId, options);
                case "capture": return CaptureOrganicMissions(court, normalizedRunId, options);
                case "control_outcomes": return ControlOrganicMissionOutcomes(court, normalizedRunId, options);
                case "verify_controlled_outcomes": return VerifyControlledMissionOutcomes(court, normalizedRunId, options);
                case "verify_controlled_assassination": return VerifyControlledAssassination(court, normalizedRunId, options);
                case "verify_controlled_capture": return VerifyControlledCapture(court, normalizedRunId, options);
                case "control_agent_action": return ControlForeignAgentAction(court, normalizedRunId, options);
                case "verify_controlled_agent_action": return VerifyControlledForeignAgentAction(court, normalizedRunId);
                case "observe": return ObserveOrganicTest(court, normalizedRunId, options);
                case "evaluate": return EvaluateOrganicTest(court, normalizedRunId, options);
                case "verify_reload": return VerifyOrganicReload(court, normalizedRunId);
                case "attempt_breakout": return AttemptOrganicBreakout(court, normalizedRunId, options);
                case "cleanup_marker": return CleanupOrganicLedger(court, normalizedRunId);
                default: return OrganicFailure(normalizedRunId, requestedPhase, "Unsupported organic Spymaster phase.");
            }
        }

        internal static JObject BuildSpymasterOrganicAgentRequest(ReignCourtCampaignBehavior court)
        {
            return court.BuildForeignAgentSnapshot(CurrentDayFloat());
        }

        private static JObject OrganicPreflight(ReignCourtCampaignBehavior court, string runId)
        {
            JArray assertions = new JArray();
            Kingdom player = Clan.PlayerClan?.Kingdom;
            Hero spymaster = court.ActiveSpymaster;
            Settlement playerSettlement = OrganicPlayerSettlement(court, player);
            Settlement foreignSettlement = OrganicForeignSettlement(court, player, 0);
            Hero foreignHero = OrganicForeignHero(court, player, 0);
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            List<Hero> candidates = Hero.AllAliveHeroes.Where(x => court.IsEligibleForeignAgentCandidate(x, player, player?.Leader, adultAge))
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
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest(court);
            result["recommendedTargets"] = new JObject
            {
                ["playerSettlementId"] = playerSettlement?.StringId ?? string.Empty,
                ["foreignSettlementId"] = foreignSettlement?.StringId ?? string.Empty,
                ["foreignHeroId"] = foreignHero?.StringId ?? string.Empty
            };
            return result;
        }

        private static JObject PrepareOrganicLedger(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], OrganicPrepareConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "prepare", "The exact disposable-save preparation confirmation is required.");
            ReignSpymasterState state = court.EnsureSpymasterState();
            ReignSpymasterOrganicTestLedger existing = FindOrganicLedger(court, runId);
            if (existing != null)
            {
                JObject idempotent = ObserveOrganicTest(court, runId, new JObject { ["stage"] = "prepare_replay" });
                idempotent["idempotent"] = true;
                return idempotent;
            }
            JObject preflight = OrganicPreflight(court, runId);
            if (preflight.Value<bool?>("ok") != true) return preflight;
            if (state.Missions.Any(x => x.State == ReignSpymasterMissionState.Active))
                return OrganicFailure(runId, "prepare", "Finish or cancel existing Spymaster missions before cloning the disposable acceptance save.");

            Kingdom player = Clan.PlayerClan?.Kingdom;
            ReignSpymasterOrganicTestLedger ledger = new ReignSpymasterOrganicTestLedger
            {
                RunId = runId,
                CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
                SpymasterHeroStringId = court.ActiveSpymaster?.StringId ?? string.Empty,
                StartedDay = CurrentDayFloat(),
                LastObservedDay = CurrentDayFloat(),
                PreviousAgentRecruitmentDay = state.LastAgentRecruitmentDay,
                StartingGold = Hero.MainHero?.Gold ?? 0,
                DisposableSaveConfirmed = true,
                PlayerKingdomStringId = player?.StringId ?? string.Empty,
                PlayerSettlementStringId = OrganicPlayerSettlement(court, player)?.StringId ?? string.Empty,
                ForeignSettlementStringId = OrganicForeignSettlement(court, player, 0)?.StringId ?? string.Empty,
                ForeignHeroStringId = OrganicForeignHero(court, player, 0)?.StringId ?? string.Empty,
                InitialMissionIds = state.Missions.Select(x => x.MissionId).ToList(),
                InitialAgentHeroIds = state.ForeignAgents.Select(x => x.AgentHeroStringId).ToList(),
                InitialActionIds = state.ForeignAgentActions.Select(x => x.ActionId).ToList(),
                InitialEffectIds = state.Effects.Select(x => x.EffectId).ToList()
            };
            state.OrganicTestLedgers.Add(ledger);
            // Establish a known observation boundary without creating an agent or
            // influencing any future production roll.
            state.LastAgentRecruitmentDay = ledger.StartedDay;
            court.RaiseStateChanged();
            JObject result = ObserveOrganicTest(court, runId, new JObject { ["stage"] = "prepared" });
            result["preflight"] = preflight;
            result["requiresDisposableSave"] = true;
            result["nextNaturalAgentTickDay"] = ledger.StartedDay + 5f;
            return result;
        }

        private static JObject CaptureOrganicMissions(ReignCourtCampaignBehavior court, string runId, JObject options, List<ReignSpymasterMission> accepted = null)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "capture", "Prepare the organic ledger on a disposable save first.");
            ReignSpymasterState state = court.EnsureSpymasterState();
            HashSet<string> known = new HashSet<string>(ledger.InitialMissionIds.Concat(ledger.MissionIds), StringComparer.OrdinalIgnoreCase);
            List<ReignSpymasterMission> added = (accepted ?? state.Missions).Where(x => x.StartedDay + 0.001f >= ledger.StartedDay && !known.Contains(x.MissionId))
                .OrderBy(x => x.StartedDay).ToList();
            if (accepted != null && (added.Count != accepted.Count || ledger.Batches.Any(x => x.Label == (string)options["batch"])))
                return OrganicFailure(runId, "capture", "Accepted mission capture is exactly once; use observe/control on the retained batch.");
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
                PreSnapshotJson = OrganicLedgerCheckpoint(court, ledger, "before_" + label).ToString(Formatting.None)
            };
            ledger.MissionIds.AddRange(batch.MissionIds);
            ledger.MissionIds = ledger.MissionIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            ledger.Batches.Add(batch);
            court.RaiseStateChanged();
            JArray assertions = new JArray();
            int minimum = Math.Max(0, (int?)options["minimumNewMissions"] ?? 1);
            AddOrganicAssertion(assertions, "organic_missions_captured", added.Count >= minimum,
                "UI-submitted production missions were attached to the persisted organic ledger.", new JObject
                {
                    ["batch"] = label, ["captured"] = added.Count, ["minimum"] = minimum,
                    ["missionIds"] = new JArray(batch.MissionIds)
                });
            AddOrganicAssertion(assertions, "organic_native_random_path", !ReignSpymasterTestRuntime.Active
                && (accepted != null || added.All(x => (x.MissionId ?? string.Empty).StartsWith("spy_", StringComparison.OrdinalIgnoreCase)
                    && !(x.MissionId ?? string.Empty).StartsWith("spytest_", StringComparison.OrdinalIgnoreCase))),
                "Captured missions use ordinary production IDs and native Bannerlord rolls.", null);
            JObject result = OrganicResult(runId, "capture", assertions);
            result["batch"] = JObject.FromObject(batch);
            result["state"] = OrganicStateSnapshot(court, ledger);
            return result;
        }

        private static JObject ControlOrganicMissionOutcomes(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], ControlledOutcomeConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "control_outcomes", "The exact controlled-outcome confirmation is required.");
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
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
            List<ReignSpymasterMission> missions = court.EnsureSpymasterState().Missions.Where(x =>
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

        private static JObject VerifyControlledMissionOutcomes(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
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
            List<ReignSpymasterMission> missions = court.EnsureSpymasterState().Missions.Where(x =>
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
            result["state"] = OrganicStateSnapshot(court, ledger);
            return result;
        }

        private static JObject VerifyControlledAssassination(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterMission mission = ControlledMissionFromBatch(court, runId, options);
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

        private static JObject VerifyControlledCapture(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterMission mission = ControlledMissionFromBatch(court, runId, options);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_spymaster_captured",
                mission != null && mission.AttributionPending && court.ActiveSpymaster?.IsPrisoner == true
                && court.PendingCapturedSpymasterMission?.MissionId == mission.MissionId,
                "The exact failed production assassination captured the Spymaster without exposing the sponsor.",
                mission == null ? null : OrganicMissionSnapshot(mission));
            AddOrganicAssertion(assertions, "controlled_capture_controls_consumed",
                ReignSpymasterTestRuntime.PendingMissionOutcomes(runId) == 0,
                "The exact failure and capture one-shots were consumed.", null);
            return OrganicResult(runId, "verify_controlled_capture", assertions);
        }

        private static ReignSpymasterMission ControlledMissionFromBatch(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            string batchLabel = (string)options["batch"] ?? string.Empty;
            ReignSpymasterOrganicBatchRecord batch = ledger?.Batches.LastOrDefault(x =>
                string.Equals(x.Label, batchLabel, StringComparison.OrdinalIgnoreCase));
            HashSet<string> controlled = new HashSet<string>(
                ReignSpymasterTestRuntime.ControlledMissionIds(runId), StringComparer.OrdinalIgnoreCase);
            return batch == null ? null : court.EnsureSpymasterState().Missions.FirstOrDefault(x =>
                batch.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)
                && controlled.Contains(x.MissionId));
        }

        private static JObject ControlForeignAgentAction(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            if (!string.Equals((string)options["confirmation"], ControlledAgentActionConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "control_agent_action", "The exact controlled foreign-agent confirmation is required.");
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "control_agent_action", "Prepare the organic ledger on a disposable save first.");
            ReignForeignAgentRecord agent = court.EnsureSpymasterState().ForeignAgents.FirstOrDefault(x => x.Activated && !x.Exposed);
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

        private static JObject VerifyControlledForeignAgentAction(ReignCourtCampaignBehavior court, string runId)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null) return OrganicFailure(runId, "verify_controlled_agent_action", "No organic Spymaster ledger exists for this run.");
            JObject receipt = ReignSpymasterTestRuntime.LastForeignAgentActionReceipt(runId);
            string actionId = (string)receipt?["actionId"] ?? string.Empty;
            ReignForeignAgentActionRecord action = court.EnsureSpymasterState().ForeignAgentActions.FirstOrDefault(x =>
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

        private static JObject ObserveOrganicTest(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null) return OrganicFailure(runId, "observe", "No organic Spymaster ledger exists for this run.");
            JObject snapshot = OrganicStateSnapshot(court, ledger);
            snapshot["stage"] = (string)options?["stage"] ?? "observation";
            snapshot["capturedUtc"] = DateTime.UtcNow.ToString("o");
            ledger.LastObservedDay = CurrentDayFloat();
            ledger.ObservationJson.Add(OrganicLedgerCheckpoint(court, ledger, (string)snapshot["stage"]).ToString(Formatting.None));
            if (ledger.ObservationJson.Count > OrganicLedgerMaxObservations)
                ledger.ObservationJson.RemoveRange(0, ledger.ObservationJson.Count - OrganicLedgerMaxObservations);
            court.RaiseStateChanged();
            JObject result = new JObject
            {
                ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "observe",
                ["snapshot"] = snapshot, ["agentRequest"] = BuildSpymasterOrganicAgentRequest(court),
                ["testRuntimeActive"] = ReignSpymasterTestRuntime.Active
            };
            return result;
        }

        private static JObject EvaluateOrganicTest(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null) return OrganicFailure(runId, "evaluate", "No organic Spymaster ledger exists for this run.");
            ReignSpymasterState state = court.EnsureSpymasterState();
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
                && OrganicSettlementBelongsToPlayer(court, x.SettlementStringId)),
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
            bool actionTargetsPlayer = newActions.All(x => OrganicSettlementBelongsToPlayer(court, x.SettlementStringId));
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
            result["snapshot"] = OrganicStateSnapshot(court, ledger);
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest(court);
            result["recommendedNext"] = OrganicRecommendedNext(assertions);
            return result;
        }

        private static JObject VerifyOrganicReload(ReignCourtCampaignBehavior court, string runId)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "organic_ledger_survived_reload", ledger != null && ledger.DisposableSaveConfirmed
                && string.Equals(ledger.CampaignId, ReignCampaignIdentity.CurrentCampaignId(), StringComparison.OrdinalIgnoreCase),
                "The organic acceptance ledger survived a native save/process restart/load boundary.", ledger == null ? null : JObject.FromObject(ledger));
            if (ledger != null)
            {
                ReignSpymasterState state = court.EnsureSpymasterState();
                AddOrganicAssertion(assertions, "organic_tracked_state_survived_reload", ledger.MissionIds.All(id => state.Missions.Any(x => x.MissionId == id))
                    && state.AppliedForeignAgentActionIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == state.AppliedForeignAgentActionIds.Count,
                    "Tracked missions and enemy-action idempotency keys survived reload without duplication.", null);
            }
            JObject result = OrganicResult(runId, "verify_reload", assertions);
            if (ledger != null) result["snapshot"] = OrganicStateSnapshot(court, ledger);
            result["agentRequest"] = BuildSpymasterOrganicAgentRequest(court);
            return result;
        }

        private static JObject AttemptOrganicBreakout(ReignCourtCampaignBehavior court, string runId, JObject options)
        {
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            if (ledger == null || !ledger.DisposableSaveConfirmed)
                return OrganicFailure(runId, "attempt_breakout", "Prepare the organic ledger on a disposable save first.");
            if ((bool?)options["allowIrreversible"] != true
                || !string.Equals((string)options["confirmation"], OrganicIrreversibleConfirmation, StringComparison.Ordinal))
                return OrganicFailure(runId, "attempt_breakout", "The exact irreversible disposable-save confirmation is required.");
            if (!court.CanAttemptSpymasterBreakout)
                return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "attempt_breakout",
                    ["status"] = "not_applicable", ["message"] = "No naturally captured Spymaster is currently eligible; repeat the isolated assassination attempt if this branch still needs observation." };
            float chance = court.GetSpymasterBreakoutChance();
            ReignSpymasterMission pendingMission = court.PendingCapturedSpymasterMission;
            bool controlled = options["unitRoll"] != null;
            if (controlled)
            {
                if (!string.Equals((string)options["controlConfirmation"], ControlledOutcomeConfirmation,
                    StringComparison.Ordinal))
                    return OrganicFailure(runId, "attempt_breakout", "The exact controlled-outcome confirmation is required.");
                float unitRoll = Math.Max(0f, Math.Min(0.999999f, (float)options["unitRoll"]));
                ReignSpymasterTestRuntime.ForceBreakoutOutcome(runId, pendingMission.MissionId, unitRoll);
            }
            string message = court.AttemptSpymasterBreakout();
            if (!controlled)
                return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "attempt_breakout",
                    ["status"] = "attempted", ["displayedChance"] = chance, ["message"] = message,
                    ["playerPrisoner"] = Hero.MainHero?.IsPrisoner == true, ["spymasterPrisoner"] = court.ActiveSpymaster?.IsPrisoner == true };
            JArray assertions = new JArray();
            AddOrganicAssertion(assertions, "controlled_breakout_succeeded",
                string.IsNullOrEmpty(message) && court.ActiveSpymaster?.IsPrisoner != true
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
            result["spymasterPrisoner"] = court.ActiveSpymaster?.IsPrisoner == true;
            return result;
        }

        private static JObject CleanupOrganicLedger(ReignCourtCampaignBehavior court, string runId)
        {
            ReignSpymasterState state = court.EnsureSpymasterState();
            ReignSpymasterOrganicTestLedger ledger = FindOrganicLedger(court, runId);
            int removed = state.OrganicTestLedgers.RemoveAll(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
            if (ledger != null && state.OrganicTestLedgers.Count == 0) state.LastAgentRecruitmentDay = ledger.PreviousAgentRecruitmentDay;
            ReignSpymasterTestRuntime.Reset();
            court.RaiseStateChanged();
            return new JObject { ["ok"] = true, ["runId"] = runId, ["profile"] = "organic", ["phase"] = "cleanup_marker",
                ["ledgersRemoved"] = removed, ["requiresSaveRollback"] = true,
                ["message"] = "The marker was removed. Restore the pre-test checkpoint and delete the disposable native save/Save Sync point to undo organic world effects." };
        }

        private static ReignSpymasterOrganicTestLedger FindOrganicLedger(ReignCourtCampaignBehavior court, string runId)
        {
            return court.EnsureSpymasterState().OrganicTestLedgers.FirstOrDefault(x => string.Equals(x.RunId, runId, StringComparison.OrdinalIgnoreCase));
        }

        private static JObject OrganicLedgerCheckpoint(ReignCourtCampaignBehavior court, ReignSpymasterOrganicTestLedger ledger, string stage)
        {
            ReignSpymasterState state = court.EnsureSpymasterState();
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

        private static JObject OrganicStateSnapshot(ReignCourtCampaignBehavior court, ReignSpymasterOrganicTestLedger ledger)
        {
            ReignSpymasterState state = court.EnsureSpymasterState();
            List<ReignSpymasterMission> missions = state.Missions.Where(x => ledger.MissionIds.Contains(x.MissionId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignForeignAgentRecord> agents = state.ForeignAgents.Where(x => !ledger.InitialAgentHeroIds.Contains(x.AgentHeroStringId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignForeignAgentActionRecord> actions = state.ForeignAgentActions.Where(x => !ledger.InitialActionIds.Contains(x.ActionId, StringComparer.OrdinalIgnoreCase)).ToList();
            List<ReignSpymasterSettlementEffect> effects = state.Effects.Where(x => !ledger.InitialEffectIds.Contains(x.EffectId, StringComparer.OrdinalIgnoreCase)).ToList();
            return new JObject
            {
                ["runId"] = ledger.RunId, ["campaignId"] = ledger.CampaignId, ["worldDay"] = CurrentDayFloat(),
                ["startedDay"] = ledger.StartedDay, ["elapsedDays"] = CurrentDayFloat() - ledger.StartedDay,
                ["startingGold"] = ledger.StartingGold, ["currentGold"] = Hero.MainHero?.Gold ?? 0,
                ["spymaster"] = SnapshotHero(court.ActiveSpymaster), ["missionCapacity"] = court.SpymasterMissionCapacity,
                ["activeMissionCount"] = court.ActiveSpymasterMissionCount, ["missions"] = new JArray(missions.Select(OrganicMissionSnapshot)),
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

        private static Settlement OrganicPlayerSettlement(ReignCourtCampaignBehavior court, Kingdom player)
        {
            return Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true && x.OwnerClan?.Kingdom == player)
                .OrderBy(x => x.Settlement.StringId).Select(x => x.Settlement).FirstOrDefault();
        }

        private static Settlement OrganicForeignSettlement(ReignCourtCampaignBehavior court, Kingdom player, int offset)
        {
            List<Settlement> rows = Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true && x.OwnerClan?.Kingdom != null
                && x.OwnerClan.Kingdom != player).OrderBy(x => x.Settlement.StringId).Select(x => x.Settlement).ToList();
            return rows.Count == 0 ? null : rows[Math.Abs(offset) % rows.Count];
        }

        private static Hero OrganicForeignHero(ReignCourtCampaignBehavior court, Kingdom player, int offset)
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

        private static bool OrganicSettlementBelongsToPlayer(ReignCourtCampaignBehavior court, string settlementId)
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
