using Newtonsoft.Json.Linq;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Spymaster.Campaign
{
    // Test-harness side of IReignSpymasterTestControls: forwards to ReignSpymasterTestRuntime.
    // Lives with the spymaster module so ExcludeCourt builds compile it out together.
    internal sealed class ReignSpymasterInProcessTestControls : IReignSpymasterTestControls
    {
        public bool Active => ReignSpymasterTestRuntime.Active;
        public bool SuppressIrreversibleNativeConsequences => ReignSpymasterTestRuntime.SuppressIrreversibleNativeConsequences;
        public string RunId => ReignSpymasterTestRuntime.RunId;
        public bool TryNext(string purpose, out float unitRoll) => ReignSpymasterTestRuntime.TryNext(purpose, out unitRoll);
        public bool TryTakeMissionOutcome(string missionId, out float unitRoll) =>
            ReignSpymasterTestRuntime.TryTakeMissionOutcome(missionId, out unitRoll);
        public void DiscardMissionOutcome(string missionId) => ReignSpymasterTestRuntime.DiscardMissionOutcome(missionId);
        public void DiscardMissionConsequence(string missionId) => ReignSpymasterTestRuntime.DiscardMissionConsequence(missionId);
        public bool TryTakeMissionConsequence(string missionId, out float unitRoll) =>
            ReignSpymasterTestRuntime.TryTakeMissionConsequence(missionId, out unitRoll);
        public bool TryTakeBreakoutOutcome(string missionId, out float unitRoll) =>
            ReignSpymasterTestRuntime.TryTakeBreakoutOutcome(missionId, out unitRoll);
        public JObject PendingForeignAgentActionControl() => ReignSpymasterTestRuntime.PendingForeignAgentActionControl();
        public void ExerciseForeignAgentDelivery(JObject response, float requestDay) =>
            ReignSpymasterTestRuntime.ExerciseForeignAgentDelivery(response, requestDay);
        public void RecordForeignAgentActionReceipt(JObject receipt) => ReignSpymasterTestRuntime.RecordForeignAgentActionReceipt(receipt);
        public void RecordNativeIntent(string type, JObject data = null) => ReignSpymasterTestRuntime.RecordNativeIntent(type, data);
    }
}
