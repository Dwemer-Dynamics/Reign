using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Court.Campaign;

namespace ReignBeta.Spymaster.Campaign
{
    /// <summary>
    /// Non-serialized, command-scoped deterministic hooks used only by the armed
    /// live-test bridge. Normal campaigns never enable this runtime.
    /// </summary>
    internal static class ReignSpymasterTestRuntime
    {
        private static readonly Dictionary<string, Queue<float>> ForcedRolls =
            new Dictionary<string, Queue<float>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ForcedMissionOutcome> ForcedMissionOutcomes =
            new Dictionary<string, ForcedMissionOutcome>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ForcedMissionOutcome> ForcedMissionConsequences =
            new Dictionary<string, ForcedMissionOutcome>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, ForcedMissionOutcome> ForcedBreakoutOutcomes =
            new Dictionary<string, ForcedMissionOutcome>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> ControlledMissionIdsByRun =
            new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static Random _random;
        private static ForcedForeignAgentAction _forcedForeignAgentAction;
        private static JObject _lastForeignAgentActionReceipt;
        private static ForeignAgentDeliveryProbe _deliveryProbe;

        private sealed class ForeignAgentDeliveryProbe
        {
            internal string OperationId, RunId, SavePointId, GameInstanceId, RestoreGeneration;
            internal string AgentId, AssignmentId, SettlementId;
            internal Func<bool> OwnsLoad;
            internal readonly Stopwatch Elapsed = Stopwatch.StartNew();
            internal JObject DroppedAction;
            internal int OtherActions;
            internal float? ReplayDay;
            internal bool Expired => Elapsed.Elapsed >= TimeSpan.FromMinutes(20);

            internal bool Matches(JObject row) =>
                (string)row["agentHeroStringId"] == AgentId
                && (string)row["assignmentId"] == AssignmentId
                && (string)row["settlementStringId"] == SettlementId;

            internal JObject Evidence() => new JObject
            {
                ["schema"] = "reign-spymaster-disruption-delivery-v1",
                ["operationId"] = OperationId, ["campaignTestRunId"] = RunId,
                ["savePointId"] = SavePointId, ["gameInstanceId"] = GameInstanceId,
                ["restoreGeneration"] = RestoreGeneration, ["agentHeroId"] = AgentId,
                ["assignmentId"] = AssignmentId, ["settlementId"] = SettlementId,
                ["state"] = ReplayDay.HasValue ? "replay_observed" : DroppedAction != null ? "dropped"
                    : Expired ? "expired" : "armed",
                ["actionId"] = DroppedAction?["actionId"]?.DeepClone(),
                ["effectType"] = DroppedAction?["effectType"]?.DeepClone(),
                ["issuedDay"] = DroppedAction?["issuedDay"]?.DeepClone(),
                ["expiresDay"] = DroppedAction?["expiresDay"]?.DeepClone(),
                ["otherActionCount"] = OtherActions, ["replayDay"] = ReplayDay
            };
        }

        internal static JObject ControlForeignAgentDelivery(JObject args, string operationId, string runId,
            string point, string gameInstanceId, string restoreGeneration, bool canArm, Func<bool> ownsLoad)
        {
            string agentId = (string)args["agentHeroId"];
            string assignmentId = (string)args["assignmentId"];
            string settlementId = (string)args["settlementId"];
            var probe = _deliveryProbe;
            bool sameLoad = probe != null && probe.OwnsLoad();
            if ((string)args["control"] == "arm" && !sameLoad)
            {
                if (!canArm || !ownsLoad())
                    throw new InvalidOperationException("Select one current active hidden assignment while the tick is idle.");
                _deliveryProbe = probe = new ForeignAgentDeliveryProbe
                {
                    OperationId = operationId, RunId = runId, SavePointId = point, GameInstanceId = gameInstanceId,
                    RestoreGeneration = restoreGeneration, AgentId = agentId, AssignmentId = assignmentId,
                    SettlementId = settlementId, OwnsLoad = ownsLoad
                };
                sameLoad = true;
            }
            if (!sameLoad || probe.OperationId != operationId || probe.RunId != runId
                || probe.AgentId != agentId || probe.AssignmentId != assignmentId || probe.SettlementId != settlementId)
                throw new InvalidOperationException("The operation does not own this assignment's loaded checkpoint.");
            return probe.Evidence();
        }

        internal static void ExerciseForeignAgentDelivery(JObject response, float requestDay)
        {
            var probe = _deliveryProbe;
            if (probe == null) return;
            if (!probe.OwnsLoad())
            {
                _deliveryProbe = null;
                return;
            }
            if (probe.Expired
                || (string)response["restoreGeneration"] != probe.RestoreGeneration
                || (string)response["disruptionReplay"] != ReignCourtCampaignBehavior.ForeignAgentDisruptionReplaySchema
                || !(response["actions"] is JArray actions)) return;

            var matching = actions.OfType<JObject>().Where(probe.Matches);
            if (probe.DroppedAction != null)
            {
                if (!probe.ReplayDay.HasValue && requestDay > (float)probe.DroppedAction["issuedDay"]
                    && matching.Any(row => JToken.DeepEquals(row, probe.DroppedAction)))
                    probe.ReplayDay = requestDay;
                return;
            }
            var newActions = matching.Where(row =>
                ReignCourtCampaignBehavior.TryReadForeignAgentDisruptionWindow(row, requestDay, out float issued, out _)
                && issued == requestDay && row["exposureRevision"]?.Type == JTokenType.Integer
                && (int)row["exposureRevision"] == 0 && !string.IsNullOrWhiteSpace((string)row["actionId"])).ToArray();
            if (newActions.Length != 1) return;

            // Consume the opt-in before skipping one real action. Keep every assignment and other action in the response.
            var dropped = newActions[0];
            probe.DroppedAction = (JObject)dropped.DeepClone();
            probe.OtherActions = actions.Count - 1;
            dropped.Remove();
        }

        private sealed class ForcedMissionOutcome
        {
            public string RunId;
            public float UnitRoll;
        }

        private sealed class ForcedForeignAgentAction
        {
            public string RunId;
            public string AgentHeroId;
            public float UnitRoll;
        }

        public static bool Active { get; private set; }
        public static bool SuppressIrreversibleNativeConsequences { get; private set; }
        public static string RunId { get; private set; } = string.Empty;
        public static JArray NativeIntents { get; } = new JArray();

        public static void Begin(string runId, int seed, bool suppressIrreversibleNativeConsequences)
        {
            Reset();
            if (string.IsNullOrWhiteSpace(runId)) throw new ArgumentException("A live-test run id is required.", nameof(runId));
            RunId = runId;
            _random = new Random(seed);
            SuppressIrreversibleNativeConsequences = suppressIrreversibleNativeConsequences;
            Active = true;
        }

        public static void Force(string purpose, params float[] unitRolls)
        {
            if (!Active) throw new InvalidOperationException("The Spymaster test runtime is not active.");
            Queue<float> queue = new Queue<float>();
            foreach (float roll in unitRolls ?? Array.Empty<float>())
                queue.Enqueue(Math.Max(0f, Math.Min(0.999999f, roll)));
            ForcedRolls[purpose ?? string.Empty] = queue;
        }

        public static bool TryNext(string purpose, out float unitRoll)
        {
            unitRoll = 0f;
            if (!Active) return false;
            if (ForcedRolls.TryGetValue(purpose ?? string.Empty, out Queue<float> queue) && queue.Count > 0)
                unitRoll = queue.Dequeue();
            else
                unitRoll = (float)(_random?.NextDouble() ?? 0d);
            return true;
        }

        public static void ForceMissionOutcome(string runId, string missionId, float unitRoll,
            float? consequenceUnitRoll = null)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("A controlled live-test run id is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(missionId))
                throw new ArgumentException("An exact captured mission id is required.", nameof(missionId));
            string normalizedRunId = runId.Trim();
            string normalizedMissionId = missionId.Trim();
            ForcedMissionOutcomes[normalizedMissionId] = new ForcedMissionOutcome
            {
                RunId = normalizedRunId,
                UnitRoll = Math.Max(0f, Math.Min(0.999999f, unitRoll))
            };
            if (consequenceUnitRoll.HasValue)
                ForcedMissionConsequences[normalizedMissionId] = new ForcedMissionOutcome
                {
                    RunId = normalizedRunId,
                    UnitRoll = Math.Max(0f, Math.Min(0.999999f, consequenceUnitRoll.Value))
                };
            if (!ControlledMissionIdsByRun.TryGetValue(normalizedRunId, out HashSet<string> controlledIds))
            {
                controlledIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                ControlledMissionIdsByRun[normalizedRunId] = controlledIds;
            }
            controlledIds.Add(normalizedMissionId);
        }

        public static bool TryTakeMissionOutcome(string missionId, out float unitRoll)
        {
            unitRoll = 0f;
            if (string.IsNullOrWhiteSpace(missionId)
                || !ForcedMissionOutcomes.TryGetValue(missionId.Trim(), out ForcedMissionOutcome forced))
                return false;
            ForcedMissionOutcomes.Remove(missionId.Trim());
            unitRoll = forced.UnitRoll;
            return true;
        }

        public static void DiscardMissionOutcome(string missionId)
        {
            if (string.IsNullOrWhiteSpace(missionId)) return;
            string normalizedMissionId = missionId.Trim();
            ForcedMissionOutcomes.Remove(normalizedMissionId);
            ForcedMissionConsequences.Remove(normalizedMissionId);
            ForcedBreakoutOutcomes.Remove(normalizedMissionId);
        }

        public static void DiscardMissionConsequence(string missionId)
        {
            if (!string.IsNullOrWhiteSpace(missionId)) ForcedMissionConsequences.Remove(missionId.Trim());
        }

        public static bool TryTakeMissionConsequence(string missionId, out float unitRoll)
        {
            return TryTakeExactRoll(ForcedMissionConsequences, missionId, out unitRoll);
        }

        public static void ForceBreakoutOutcome(string runId, string missionId, float unitRoll)
        {
            ForceExactRoll(ForcedBreakoutOutcomes, runId, missionId, unitRoll);
        }

        public static bool TryTakeBreakoutOutcome(string missionId, out float unitRoll)
        {
            return TryTakeExactRoll(ForcedBreakoutOutcomes, missionId, out unitRoll);
        }

        public static int PendingMissionOutcomes(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)) return 0;
            int count = 0;
            foreach (Dictionary<string, ForcedMissionOutcome> controls in new[]
            {
                ForcedMissionOutcomes, ForcedMissionConsequences, ForcedBreakoutOutcomes
            })
                foreach (ForcedMissionOutcome forced in controls.Values)
                    if (string.Equals(forced.RunId, runId.Trim(), StringComparison.OrdinalIgnoreCase)) count++;
            return count;
        }

        private static void ForceExactRoll(Dictionary<string, ForcedMissionOutcome> controls,
            string runId, string missionId, float unitRoll)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("A controlled live-test run id is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(missionId))
                throw new ArgumentException("An exact captured mission id is required.", nameof(missionId));
            controls[missionId.Trim()] = new ForcedMissionOutcome
            {
                RunId = runId.Trim(),
                UnitRoll = Math.Max(0f, Math.Min(0.999999f, unitRoll))
            };
        }

        private static bool TryTakeExactRoll(Dictionary<string, ForcedMissionOutcome> controls,
            string missionId, out float unitRoll)
        {
            unitRoll = 0f;
            if (string.IsNullOrWhiteSpace(missionId)
                || !controls.TryGetValue(missionId.Trim(), out ForcedMissionOutcome forced))
                return false;
            controls.Remove(missionId.Trim());
            unitRoll = forced.UnitRoll;
            return true;
        }

        public static string[] ControlledMissionIds(string runId)
        {
            if (string.IsNullOrWhiteSpace(runId)
                || !ControlledMissionIdsByRun.TryGetValue(runId.Trim(), out HashSet<string> controlledIds))
                return Array.Empty<string>();
            return new List<string>(controlledIds).ToArray();
        }

        public static void ForceForeignAgentAction(string runId, string agentHeroId, float unitRoll)
        {
            if (string.IsNullOrWhiteSpace(runId))
                throw new ArgumentException("A controlled live-test run id is required.", nameof(runId));
            if (string.IsNullOrWhiteSpace(agentHeroId))
                throw new ArgumentException("An exact active foreign-agent id is required.", nameof(agentHeroId));
            _forcedForeignAgentAction = new ForcedForeignAgentAction
            {
                RunId = runId.Trim(),
                AgentHeroId = agentHeroId.Trim(),
                UnitRoll = Math.Max(0f, Math.Min(0.999999f, unitRoll))
            };
            _lastForeignAgentActionReceipt = null;
        }

        public static JObject PendingForeignAgentActionControl()
        {
            ForcedForeignAgentAction forced = _forcedForeignAgentAction;
            return forced == null ? null : new JObject
            {
                ["confirmation"] = "force one Reign Spymaster foreign-agent action on disposable save",
                ["runId"] = forced.RunId,
                ["agentHeroId"] = forced.AgentHeroId,
                ["unitRoll"] = forced.UnitRoll
            };
        }

        public static void RecordForeignAgentActionReceipt(JObject receipt)
        {
            if (receipt == null || receipt.Value<bool?>("consumed") != true) return;
            string agentHeroId = (string)receipt["agentHeroId"] ?? string.Empty;
            if (_forcedForeignAgentAction == null
                || !string.Equals(_forcedForeignAgentAction.AgentHeroId, agentHeroId, StringComparison.OrdinalIgnoreCase)) return;
            _lastForeignAgentActionReceipt = new JObject(receipt);
            _lastForeignAgentActionReceipt["runId"] = _forcedForeignAgentAction.RunId;
            _forcedForeignAgentAction = null;
        }

        public static JObject LastForeignAgentActionReceipt(string runId)
        {
            return _lastForeignAgentActionReceipt != null
                && string.Equals((string)_lastForeignAgentActionReceipt["runId"], runId, StringComparison.OrdinalIgnoreCase)
                ? new JObject(_lastForeignAgentActionReceipt) : null;
        }

        public static bool HasPendingForeignAgentAction(string runId)
        {
            return _forcedForeignAgentAction != null
                && string.Equals(_forcedForeignAgentAction.RunId, runId, StringComparison.OrdinalIgnoreCase);
        }

        public static void RecordNativeIntent(string type, JObject data = null)
        {
            if (!Active) return;
            JObject row = data == null ? new JObject() : new JObject(data);
            row["type"] = type ?? string.Empty;
            row["runId"] = RunId;
            row["recordedUtc"] = DateTime.UtcNow.ToString("o");
            NativeIntents.Add(row);
        }

        public static void Reset()
        {
            Active = false;
            SuppressIrreversibleNativeConsequences = false;
            RunId = string.Empty;
            _random = null;
            ForcedRolls.Clear();
            ForcedMissionOutcomes.Clear();
            ForcedMissionConsequences.Clear();
            ForcedBreakoutOutcomes.Clear();
            ControlledMissionIdsByRun.Clear();
            _forcedForeignAgentAction = null;
            _lastForeignAgentActionReceipt = null;
            _deliveryProbe = null;
            NativeIntents.Clear();
        }
    }
}
