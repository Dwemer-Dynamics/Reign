using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        // One bounded diagnostic pulse per loaded campaign object. Latch BEFORE
        // mutation, including failures. No eviction, retry delta or exit-time debt.
        private static readonly ConditionalWeakTable<TaleWorlds.CampaignSystem.Campaign, ReignConversationClockProbeSession>
            ConversationClockAttempts = new ConditionalWeakTable<TaleWorlds.CampaignSystem.Campaign, ReignConversationClockProbeSession>();
        private const BindingFlags ClockInstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool ConversationClockRequiresRestore => TaleWorlds.CampaignSystem.Campaign.Current != null
            && ConversationClockAttempts.TryGetValue(TaleWorlds.CampaignSystem.Campaign.Current, out var state) && state.Attempted;

        private static async Task<LiveCommandResult> ConversationClockProbeAsync(JObject command)
        {
            if (command.Value<string>("profile") != "observe")
                return LiveCommandResult.Failed("Conversation time advancement is retired; only clock observation is available.", null);
            TaleWorlds.CampaignSystem.Campaign originalCampaign = null;
            Mission originalMission = null;
            JObject result = ReignConversationClockProbeSession.NewReceipt(command.Value<string>("probeId") ?? "");
            try
            {
                result = await ReignMainThread.InvokeAsync(() =>
                {
                    originalCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
                    originalMission = Mission.Current;
                    ValidateConversationClockProbe(command);
                    string probeId = command.Value<string>("probeId");
                    JObject before = CaptureConversationClockProbe();
                    ReignConversationClockProbeSession state = ConversationClockAttempts.GetValue(originalCampaign,
                        ignored => new ReignConversationClockProbeSession());
                    // WaitAsyncTasks has no bounded wait API. Do not block the UI or
                    // mutate while native AI work may exist; observe and investigate it.
                    if (command.Value<string>("profile") == "pulse" && !state.Attempted && originalCampaign.CampaignLateAITickTask != null)
                        throw new InvalidOperationException("Native AI task is present; clock mutation is blocked pending quiescence proof.");
                    return state.Run(command.Value<string>("profile"), probeId, before,
                        () => ReignConversationClockProbe.PulseFiveMinutes(ConversationClockTracker(originalCampaign)),
                        CaptureConversationClockProbe);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result["requiresCheckpointRestore"] = originalCampaign != null
                    && ConversationClockAttempts.TryGetValue(originalCampaign, out var state) && state.Attempted;
                return LiveCommandResult.Failed(ex.GetBaseException().Message, result);
            }
            // Observe a later dispatcher drain without closing or unlocking chat.
            // The whole asynchronous phase retains the receipt even if dispatch
            // itself fails during campaign/scene teardown.
            try
            {
                await Task.Delay(250).ConfigureAwait(false);
                return await ReignMainThread.InvokeAsync(() =>
                {
                    bool observed = ReignConversationClockProbeSession.ObserveLater(result, () =>
                    {
                        // Preserve the mutation receipt even if the campaign, save,
                        // focus or any guard changed after the first application tick.
                        if (!ReferenceEquals(originalCampaign, TaleWorlds.CampaignSystem.Campaign.Current)
                            || !ReferenceEquals(originalMission, Mission.Current))
                            throw new InvalidOperationException("Campaign or mission changed during observation.");
                        ValidateConversationClockProbe(command);
                        return CaptureConversationClockProbe();
                    });
                    if (!observed) return LiveCommandResult.Failed("Clock observation failed; retain the mutation receipt and restore Current without saving if required.", result);
                    JObject later = (JObject)result["later"];
                    JObject expected = result["after"] as JObject ?? result["before"] as JObject;
                    bool stable = JToken.DeepEquals(expected["clock"], later["clock"])
                        && JToken.DeepEquals(result["before"]["frozenWorld"], later["frozenWorld"])
                        && JToken.DeepEquals(result["before"]["timers"], later["timers"])
                        && JToken.DeepEquals(result["before"]["absoluteTimers"], later["absoluteTimers"])
                        && result["failure"] == null;
                    result["unchangedAfterObservation"] = stable;
                    result["distinctDispatcherDrains"] = result.Value<bool?>("duplicate") == true ? (JToken)JValue.CreateNull()
                        : new JValue(later.Value<long>("applicationDispatchTicks") > result["before"].Value<long>("applicationDispatchTicks"));
                    return stable
                        ? LiveCommandResult.Completed("Clock diagnostic captured. Timer accounting and native save/exit acceptance remain unqualified; restore the retained Current without saving after a pulse.", result)
                        : LiveCommandResult.Failed("Native clock, world or timers changed unexpectedly across paused frames. Stop and restore the retained Current without saving.", result);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result["observationFailure"] = ex.GetBaseException().Message;
                return LiveCommandResult.Failed("Clock observation dispatch failed; restore Current without saving if required by the retained receipt.", result);
            }
        }

        private static void ValidateConversationClockProbe(JObject command)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var enrollment = command["enrollment"] as JObject;
            string expected = enrollment?.Value<string>("disposableSaveName") ?? "";
            string baseline = enrollment?.Value<string>("protectedBaselineSaveName") ?? "";
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            if (!ReignMainThread.IsMainThread || !_armed || campaign == null
                || !ReignConversationClockProbeSession.LeaseIsCurrent(command.Value<string>("leaseExpiresUtc"), DateTimeOffset.UtcNow)
                || command.Value<string>("gameInstanceId") != _gameInstanceId
                || command.Value<string>("campaignId") != campaignId
                || command.Value<string>("mode") != "individual_chat"
                || command.Value<string>("confirmation") != "probe Reign conversation clock on disposable save"
                || !new[] { "observe", "pulse" }.Contains(command.Value<string>("profile"))
                || string.IsNullOrWhiteSpace(command.Value<string>("probeId"))
                || enrollment?.Value<string>("source") != "reign_mcp_conversation_clock"
                || enrollment?.Value<bool?>("recoveryCheckpointVerified") != true
                || string.IsNullOrWhiteSpace(enrollment?.Value<string>("campaignTestRunId"))
                || enrollment?.Value<string>("campaignId") != campaignId
                || enrollment?.Value<string>("timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                || expected.Length == 0 || baseline.Length == 0
                || string.Equals(expected, baseline, StringComparison.OrdinalIgnoreCase)
                || expected != ActiveSaveName() || expected != command.Value<string>("expectedSaveName")
                || !ReignSaveSyncCoordinator.IsReadyForCampaign(campaignId)
                || ReignSaveSyncCoordinator.SaveFinalizationSnapshot().Value<int>("inFlight") != 0
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || campaign.TimeControlMode != CampaignTimeControlMode.Stop
                || Mission.Current == null || Mission.Current.Mode != MissionMode.Conversation
                || Game.Current?.GameStateManager?.ActiveStateDisabledByUser != true
                || !ReignIndividualChatScreenManager.AutomationActiveStateBlocked
                || !ReignIndividualChatScreenManager.AutomationModalFocusOwned
                || ReignIndividualChatScreenManager.AutomationBusy)
                throw new InvalidOperationException("Clock probe requires the exact armed, aligned disposable Current with retained recovery and an idle, focused, locked Individual Chat inside a stopped native conversation mission.");
        }

        private static object ConversationClockTracker(TaleWorlds.CampaignSystem.Campaign campaign)
        {
            PropertyInfo property = typeof(TaleWorlds.CampaignSystem.Campaign).GetProperty("MapTimeTracker", ClockInstanceMembers);
            return property?.GetValue(campaign, null) ?? throw new InvalidOperationException("Native MapTimeTracker is unavailable.");
        }

        private static JObject CaptureConversationClockProbe()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var parties = new JArray(MobileParty.All.OrderBy(p => p.StringId, StringComparer.Ordinal).Select(p => new JObject
            {
                ["id"] = p.StringId, ["x"] = p.GetPosition2D.x, ["y"] = p.GetPosition2D.y,
                ["members"] = p.MemberRoster.TotalManCount, ["wounded"] = p.MemberRoster.TotalWounded,
                ["prisoners"] = p.PrisonRoster.TotalManCount
            }));
            object manager = typeof(TaleWorlds.CampaignSystem.Campaign).GetField("_campaignPeriodicEventManager", ClockInstanceMembers)?.GetValue(campaign)
                ?? throw new InvalidOperationException("Native periodic manager is unavailable.");
            var timers = new JObject();
            foreach (FieldInfo field in manager.GetType().GetFields(ClockInstanceMembers).OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (!field.FieldType.Name.StartsWith("PeriodicTicker", StringComparison.Ordinal)) continue;
                object ticker = field.GetValue(manager) ?? throw new InvalidOperationException("Native ticker is uninitialized: " + field.Name);
                timers[field.Name] = new JObject
                {
                    ["debt"] = Convert.ToDouble(ticker.GetType().GetProperty("TickDebt", ClockInstanceMembers).GetValue(ticker, null)),
                    ["index"] = Convert.ToInt32(ticker.GetType().GetProperty("Index", ClockInstanceMembers).GetValue(ticker, null))
                };
            }
            if (timers.Count != 20) throw new InvalidOperationException("Native periodic ticker inventory changed: " + timers.Count);
            var absoluteTimers = new JArray();
            var events = typeof(TaleWorlds.CampaignSystem.Campaign).GetProperty("CustomPeriodicCampaignEvents", ClockInstanceMembers)?.GetValue(campaign, null) as IEnumerable
                ?? throw new InvalidOperationException("Native absolute timer inventory is unavailable.");
            FieldInfo nextTrigger = typeof(MBCampaignEvent).GetField("NextTriggerTime", ClockInstanceMembers);
            if (nextTrigger == null) throw new InvalidOperationException("Native absolute timer trigger is unavailable.");
            foreach (MBCampaignEvent timer in events)
                absoluteTimers.Add(new JObject
                {
                    ["nextTriggerHours"] = ((CampaignTime)nextTrigger.GetValue(timer)).ToHours,
                    ["periodHours"] = timer.TriggerPeriod.ToHours, ["deleted"] = timer.isEventDeleted
                });
            return new JObject
            {
                ["clock"] = ReignConversationClockProbe.Read(ConversationClockTracker(campaign)),
                ["gameInstanceId"] = _gameInstanceId,
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["saveName"] = ActiveSaveName(),
                ["applicationDispatchTicks"] = ReignMainThread.LastDrainUtc.Ticks,
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["hour"] = CampaignTime.Now.CurrentHourInDay,
                ["sceneHour"] = Mission.Current.Scene.TimeOfDay,
                ["overlayLocked"] = ReignIndividualChatScreenManager.AutomationActiveStateBlocked,
                ["modalFocusOwned"] = ReignIndividualChatScreenManager.AutomationModalFocusOwned,
                ["asyncTaskPresent"] = campaign.CampaignLateAITickTask != null,
                ["timers"] = timers,
                ["absoluteTimers"] = absoluteTimers,
                ["frozenWorld"] = new JObject
                {
                    ["partyCount"] = parties.Count, ["partyStateSha256"] = ClockProbeHash(parties.ToString(Formatting.None)),
                    ["mapEventCount"] = campaign.MapEventManager.MapEvents.Count()
                }
            };
        }

        private static string ClockProbeHash(string text)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}
