using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Court
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
            NativeIntents.Clear();
        }
    }
}
