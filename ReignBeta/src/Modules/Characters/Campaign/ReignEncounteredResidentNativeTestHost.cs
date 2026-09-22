using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        // A native approach seam, not a character/action fixture. The target must
        // already exist in the live scene and ordinary dialogue remains in charge.
        private static async Task<LiveCommandResult> ApproachResidentForTestAsync(JObject command, bool leave = false)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                var behavior = ReignEncounteredResidentsCampaignBehavior.Instance;
                JObject enrollment = command["enrollment"] as JObject;
                string actualCampaign = ReignCampaignIdentity.CurrentCampaignId();
                string actualTimeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                string save = ReignServerClient.ActiveNativeSaveName();
                string prefix = enrollment?.Value<string>("savePrefix") ?? "";
                string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
                bool isolated = enrollment != null && prefix.StartsWith("ReignTest_", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(enrollment.Value<string>("campaignTestRunId"))
                    && !string.IsNullOrWhiteSpace(baseline) && save != baseline
                    && save == prefix + "_Current" && save == enrollment.Value<string>("disposableSaveName")
                    && actualCampaign == enrollment.Value<string>("campaignId")
                    && actualTimeline == enrollment.Value<string>("timelineId")
                    && Hero.MainHero?.StringId == enrollment.Value<string>("mainHeroId");
                string confirmation = leave ? "leave resident scene on enrolled disposable save" : "approach resident on enrolled disposable save";
                if (!_armed || !isolated || command.Value<string>("confirmation") != confirmation)
                    return LiveCommandResult.Failed("An armed exact campaign-test Current enrollment and matching scene-operation confirmation are required.");
                if (command.Value<string>("expectedGameInstanceId") != GameInstanceId
                    || !ReignSaveSyncCoordinator.IsReadyForCampaign(actualCampaign)
                    || ReignSaveSyncCoordinator.IsAlignmentPending)
                    return LiveCommandResult.Failed("The game instance or Save Sync alignment changed; re-observe before approaching.");
                if (campaign == null || campaign.TimeControlMode != CampaignTimeControlMode.Stop
                    || Mission.Current == null || Agent.Main == null || !Agent.Main.IsActive()
                    || Settlement.CurrentSettlement == null || Settlement.CurrentSettlement.IsUnderSiege
                    || campaign.ConversationManager.IsConversationInProgress || behavior == null
                    || Mission.Current.IsFieldBattle || Mission.Current.IsSiegeBattle || Mission.Current.IsSallyOutBattle
                    || Mission.Current.IsNavalBattle || Mission.Current.IsNavalRaidBattle || !Mission.Current.IsFriendlyMission
                    || Hero.MainHero?.PartyBelongedTo?.MapEvent != null
                    || ReignBeta.UI.ReignIndividualChatScreenManager.IsOpen || ReignBeta.UI.ReignPartyChatScreenManager.IsOpen)
                    return LiveCommandResult.Failed("Approach requires a paused peaceful settlement mission without an active native conversation.");
                var handler = Mission.Current.GetMissionBehavior<SandBox.Missions.MissionLogics.MissionAgentHandler>();
                if (handler == null) return LiveCommandResult.Failed("The native settlement agent handler is unavailable.");
                if (leave)
                {
                    // Honor the same native mission-logic refusals/inquiries as leaving by Tab.
                    // No inquiry is dismissed and no battle, quest or dialogue exit is forced.
                    foreach (var logic in Mission.Current.MissionLogics)
                    {
                        var inquiry = logic.OnEndMissionRequest(out bool canLeave);
                        if (!canLeave || inquiry != null)
                            return LiveCommandResult.Failed("Native mission logic requires attention before leaving; no exit was forced.");
                    }
                    string settlementId = Settlement.CurrentSettlement.StringId;
                    Mission.Current.EndMission();
                    return LiveCommandResult.Completed("Requested ordinary peaceful mission exit; observe the settlement menu before checkpointing.", new JObject {
                        ["schema"] = "reign-resident-native-leave-v1", ["campaignId"] = actualCampaign,
                        ["timelineId"] = actualTimeline, ["saveIdentifier"] = save,
                        ["campaignTestRunId"] = enrollment.Value<string>("campaignTestRunId"),
                        ["gameInstanceId"] = GameInstanceId, ["settlementId"] = settlementId,
                        ["nativeExitRequested"] = true, ["providerCalls"] = 0, ["directRecruitmentActions"] = 0 });
                }
                Agent target = Mission.Current.Agents.FirstOrDefault(a => a.Index == command.Value<int?>("agentIndex"));
                string body;
                using (var hash = SHA256.Create())
                    body = target == null ? "" : BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(target.BodyPropertiesValue.ToString())))
                        .Replace("-", "").ToLowerInvariant();
                if (!behavior.CanMeet(target) || target.Character.StringId != command.Value<string>("sourceTemplateId")
                    || body != command.Value<string>("bodySha256"))
                    return LiveCommandResult.Failed("The eligible adult native agent no longer matches the observed index, template and body.");
                string beforePlayer = Agent.Main.Position.ToString();
                string beforeTarget = target.Position.ToString();
                handler.TeleportTargetAgentNearReferenceAgent(target, Agent.Main, false, true);
                campaign.ConversationManager.SetupAndStartMissionConversation(target, Agent.Main, true);
                return LiveCommandResult.Completed("Approached the existing NPC and opened ordinary native dialogue.", new JObject {
                    ["schema"] = "reign-resident-native-approach-v1", ["campaignId"] = actualCampaign,
                    ["timelineId"] = actualTimeline, ["saveIdentifier"] = save,
                    ["campaignTestRunId"] = enrollment.Value<string>("campaignTestRunId"),
                    ["gameInstanceId"] = GameInstanceId, ["agentIndex"] = target.Index,
                    ["sourceTemplateId"] = target.Character.StringId, ["bodySha256"] = body,
                    ["playerBefore"] = beforePlayer, ["playerAfter"] = Agent.Main.Position.ToString(),
                    ["targetBefore"] = beforeTarget, ["targetAfter"] = target.Position.ToString(),
                    ["nativeConversationStarted"] = campaign.ConversationManager.IsConversationInProgress,
                    ["providerCalls"] = 0, ["directRecruitmentActions"] = 0 });
            }).ConfigureAwait(false);
        }
    }
}
