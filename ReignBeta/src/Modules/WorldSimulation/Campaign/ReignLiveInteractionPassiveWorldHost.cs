using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapNotificationTypes;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private const int RestoreWindowCommand = 9;
        private const string NativeDiplomacyNoticeConfirmation =
            "acknowledge Bannerlord diplomacy notices on disposable save";
        private static readonly FieldInfo NativeMapNoticesField =
            typeof(CampaignInformationManager).GetField("_mapNotices",
                BindingFlags.Instance | BindingFlags.NonPublic);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr windowHandle);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr windowHandle,
            out uint processId);

        [DllImport("user32.dll")]
        private static extern bool AttachThreadInput(
            uint sourceThreadId,
            uint targetThreadId,
            bool attach);

        private static async Task<LiveCommandResult> ExecutePassiveWorldCommandAsync(JObject command)
        {
            string operation = (command?.Value<string>("operation") ?? string.Empty).Trim().ToLowerInvariant();
            if (operation == "world_advance")
                return await PassiveWorldAdvanceAsync(command).ConfigureAwait(false);
            if (operation == "world_acknowledge_diplomacy_announcement")
                return await PassiveWorldAcknowledgeDiplomacyAnnouncementAsync()
                    .ConfigureAwait(false);
            if (operation == "world_acknowledge_native_diplomacy_notices")
                return await PassiveWorldAcknowledgeNativeDiplomacyNoticesAsync(command)
                    .ConfigureAwait(false);
            if (operation == "world_drain_political_pressure")
                return await PassiveWorldDrainPoliticalPressureAsync()
                    .ConfigureAwait(false);
            if (operation == "world_delete_checkpoint")
                return await PassiveWorldDeleteCheckpointAsync(command)
                    .ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    switch (operation)
                    {
                        case "world_time_control":
                            return SocialBalanceTimeControl(command, "passive-world test campaign");
                        case "world_snapshot":
                            return PassiveWorldSnapshot();
                        default:
                            return LiveCommandResult.Failed(
                                "Unsupported passive-world operation '" + operation + "'.");
                    }
                }
                catch (Exception ex)
                {
                    return LiveCommandResult.Failed(
                        "Passive-world native command failed: " + ex.Message);
                }
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> PassiveWorldAdvanceAsync(JObject command)
        {
            double startDay = await ReignMainThread.InvokeAsync(
                () => CampaignTime.Now.ToDays).ConfigureAwait(false);
            double requestedTarget = command.Value<double?>("targetWorldDay")
                ?? (startDay + Math.Max(0d, command.Value<double?>("days") ?? 1d));
            double targetDay = Math.Max(startDay, requestedTarget);
            int timeoutSeconds = Math.Max(30,
                Math.Min(21600, command.Value<int?>("timeoutSeconds") ?? 7200));
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            int focusAttempts = 1;
            int focusSuccesses = TryFocusBannerlordWindowForLiveAdvance() ? 1 : 0;
            int escapeMenuRecoveryCount = 0;

            _activeMode = "passive_world";
            _autoAcknowledgeDiplomacyAnnouncements = true;
            JObject startCommand = new JObject(command)
            {
                ["timeMode"] = "fast",
                ["settlementWait"] = true,
                ["autoAcknowledgeDiplomacyAnnouncements"] = true
            };
            LiveCommandResult started = await ReignMainThread.InvokeAsync(() =>
            {
                if (CloseNativeMapEscapeMenuIfOpen()) escapeMenuRecoveryCount++;
                return SocialBalanceTimeControl(startCommand,
                    "passive-world test campaign");
            }).ConfigureAwait(false);
            if (started.Status != "completed") return started;

            double currentDay = startDay;
            int announcementsAcknowledged = 0;
            int kingdomEventAnnouncementsAcknowledged = 0;
            int nativeDiplomacyNoticesAcknowledged = 0;
            int mailInquiriesAcknowledged = 0;
            int timeModeReassertions = 0;
            double lastProgressDay = startDay;
            DateTime lastProgressUtc = DateTime.UtcNow;
            DateTime nextFocusRecoveryUtc = DateTime.UtcNow.AddSeconds(2);
            DateTime nextCancellationCheckUtc = DateTime.MinValue;
            while (currentDay + 0.000001d < targetDay)
            {
                if (DateTime.UtcNow >= nextCancellationCheckUtc)
                {
                    nextCancellationCheckUtc = DateTime.UtcNow.AddMilliseconds(500);
                    string runId = command.Value<string>("runId") ?? string.Empty;
                    try
                    {
                        JObject status = await ReignServerClient
                            .GetLiveTestRunStatusAsync(runId).ConfigureAwait(false);
                        if (status.Value<bool?>("cancelRequested") == true
                            || string.Equals(status.Value<string>("status"),
                                "cancelled", StringComparison.OrdinalIgnoreCase))
                        {
                            await ReignMainThread.InvokeAsync(() =>
                            {
                                TaleWorlds.CampaignSystem.Campaign campaign =
                                    TaleWorlds.CampaignSystem.Campaign.Current;
                                if (campaign != null)
                                    campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                                return true;
                            }).ConfigureAwait(false);
                            return LiveCommandResult.Cancelled(
                                "Passive-world advancement was cancelled and campaign time was paused.",
                                new JObject
                                {
                                    ["startWorldDay"] = startDay,
                                    ["targetWorldDay"] = targetDay,
                                    ["worldDay"] = currentDay
                                });
                        }
                    }
                    catch (Exception ex)
                    {
                        ReignLog.Warn("Passive-world cancellation check deferred: "
                            + ex.Message);
                    }
                }

                if (DateTime.UtcNow >= deadline)
                {
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        TaleWorlds.CampaignSystem.Campaign campaign =
                            TaleWorlds.CampaignSystem.Campaign.Current;
                        if (campaign != null)
                            campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                        return true;
                    }).ConfigureAwait(false);
                    return LiveCommandResult.Failed(
                        "Timed out while advancing the passive-world campaign at day "
                        + currentDay.ToString("0.###") + ".");
                }

                JObject observation = await ReignMainThread.InvokeAsync(() =>
                {
                    bool escapeMenuRecovered = CloseNativeMapEscapeMenuIfOpen();
                    bool acknowledged = ReignDiplomacyAnnouncementScreenManager
                        .TryAcknowledgeForLiveHarness(true, out string eventId);
                    bool kingdomEventAcknowledged = ReignKingdomEventsCampaignBehavior
                        .TryAcknowledgeAnnouncementForLiveHarness(out string kingdomEventId);
                    bool mailInquiryAcknowledged = ReignRelationshipCampaignBehavior
                        .TryAcknowledgeMailInquiryForLiveHarness();
                    int nativeDiplomacyNotices =
                        AcknowledgeNativeDiplomacyMapNotices();
                    TaleWorlds.CampaignSystem.Campaign campaign =
                        TaleWorlds.CampaignSystem.Campaign.Current;
                    bool timeModeReasserted = false;
                    GameMenu waitMenu = campaign?.CurrentMenuContext?.GameMenu;
                    if (campaign != null && waitMenu?.IsWaitMenu == true
                        && campaign.TimeControlMode != CampaignTimeControlMode.UnstoppableFastForward)
                    {
                        // Native settlement waiting owns UnstoppableFastForward.
                        // Production announcements and menus can pause it while
                        // leaving IsWaitActive set, so restart the native wait
                        // instead of replacing its mode with a stoppable mode.
                        waitMenu.StartWait();
                        timeModeReasserted = true;
                    }
                    else if (campaign != null && waitMenu?.IsWaitMenu != true
                        && campaign.TimeControlMode != CampaignTimeControlMode.StoppableFastForward)
                    {
                        campaign.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
                        timeModeReasserted = true;
                    }
                    return new JObject
                    {
                        ["worldDay"] = CampaignTime.Now.ToDays,
                        ["acknowledged"] = acknowledged,
                        ["kingdomEventAcknowledged"] = kingdomEventAcknowledged,
                        ["kingdomEventId"] = kingdomEventId,
                        ["mailInquiryAcknowledged"] = mailInquiryAcknowledged,
                        ["nativeDiplomacyNoticesAcknowledged"] =
                            nativeDiplomacyNotices,
                        ["eventId"] = eventId,
                        ["escapeMenuRecovered"] = escapeMenuRecovered,
                        ["timeModeReasserted"] = timeModeReasserted
                    };
                }).ConfigureAwait(false);
                currentDay = observation.Value<double?>("worldDay") ?? currentDay;
                if (currentDay > lastProgressDay + 0.000001d)
                {
                    lastProgressDay = currentDay;
                    lastProgressUtc = DateTime.UtcNow;
                }
                if (observation.Value<bool?>("acknowledged") == true)
                    announcementsAcknowledged++;
                if (observation.Value<bool?>("kingdomEventAcknowledged") == true)
                    kingdomEventAnnouncementsAcknowledged++;
                if (observation.Value<bool?>("mailInquiryAcknowledged") == true)
                    mailInquiriesAcknowledged++;
                nativeDiplomacyNoticesAcknowledged += Math.Max(0,
                    observation.Value<int?>("nativeDiplomacyNoticesAcknowledged") ?? 0);
                if (observation.Value<bool?>("timeModeReasserted") == true)
                    timeModeReassertions++;
                if (observation.Value<bool?>("escapeMenuRecovered") == true)
                    escapeMenuRecoveryCount++;
                if (DateTime.UtcNow >= nextFocusRecoveryUtc
                    && DateTime.UtcNow - lastProgressUtc >= TimeSpan.FromSeconds(2))
                {
                    nextFocusRecoveryUtc = DateTime.UtcNow.AddSeconds(2);
                    focusAttempts++;
                    if (TryFocusBannerlordWindowForLiveAdvance()) focusSuccesses++;
                }
                await Task.Delay(50).ConfigureAwait(false);
            }

            LiveCommandResult stopped = await ReignMainThread.InvokeAsync(() =>
            {
                TaleWorlds.CampaignSystem.Campaign campaign =
                    TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign == null)
                    return LiveCommandResult.Failed("The campaign closed during passive-world advancement.");
                campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                return LiveCommandResult.Completed(
                    "Reached and paused at the passive-world checkpoint.",
                    new JObject
                    {
                        ["startWorldDay"] = startDay,
                        ["targetWorldDay"] = targetDay,
                        ["worldDay"] = CampaignTime.Now.ToDays,
                        ["announcementsAcknowledged"] = announcementsAcknowledged,
                        ["kingdomEventAnnouncementsAcknowledged"] =
                            kingdomEventAnnouncementsAcknowledged,
                        ["nativeDiplomacyNoticesAcknowledged"] =
                            nativeDiplomacyNoticesAcknowledged,
                        ["mailInquiriesAcknowledged"] = mailInquiriesAcknowledged,
                        ["timeModeReassertions"] = timeModeReassertions,
                        ["focusAttempts"] = focusAttempts,
                        ["focusSuccesses"] = focusSuccesses,
                        ["escapeMenuRecoveryCount"] = escapeMenuRecoveryCount,
                        ["timeMode"] = campaign.TimeControlMode.ToString(),
                        ["settlementId"] = Hero.MainHero?.CurrentSettlement?.StringId ?? string.Empty
                    });
            }).ConfigureAwait(false);
            return stopped;
        }

        private static bool CloseNativeMapEscapeMenuIfOpen()
        {
            Type mapScreenType = Type.GetType(
                "SandBox.View.Map.MapScreen, SandBox.View", false);
            object mapScreen = mapScreenType?
                .GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null, null);
            PropertyInfo openProperty = mapScreenType?
                .GetProperty("IsEscapeMenuOpened",
                    BindingFlags.Public | BindingFlags.Instance);
            if (mapScreen == null || openProperty == null
                || !(openProperty.GetValue(mapScreen, null) is bool isOpen)
                || !isOpen)
                return false;

            MethodInfo closeMethod = mapScreenType.GetMethod("CloseEscapeMenu",
                BindingFlags.Public | BindingFlags.Instance);
            if (closeMethod == null)
                throw new InvalidOperationException(
                    "Bannerlord's native map Escape menu could not be closed for guarded advancement.");
            closeMethod.Invoke(mapScreen, null);
            return true;
        }

        private static bool TryFocusBannerlordWindowForLiveAdvance()
        {
            try
            {
                using (Process process = Process.GetCurrentProcess())
                {
                    process.Refresh();
                    IntPtr windowHandle = process.MainWindowHandle;
                    if (windowHandle == IntPtr.Zero) return false;

                    IntPtr foregroundWindow = GetForegroundWindow();
                    if (foregroundWindow == windowHandle) return true;

                    uint bannerlordProcessId;
                    uint bannerlordThreadId = GetWindowThreadProcessId(
                        windowHandle,
                        out bannerlordProcessId);
                    uint foregroundProcessId;
                    uint foregroundThreadId = foregroundWindow == IntPtr.Zero
                        ? 0u
                        : GetWindowThreadProcessId(foregroundWindow,
                            out foregroundProcessId);
                    bool inputAttached = foregroundThreadId != 0u
                        && bannerlordThreadId != 0u
                        && foregroundThreadId != bannerlordThreadId
                        && AttachThreadInput(foregroundThreadId,
                            bannerlordThreadId, true);
                    try
                    {
                        ShowWindowAsync(windowHandle, RestoreWindowCommand);
                        BringWindowToTop(windowHandle);
                        SetForegroundWindow(windowHandle);
                        return GetForegroundWindow() == windowHandle;
                    }
                    finally
                    {
                        if (inputAttached)
                            AttachThreadInput(foregroundThreadId,
                                bannerlordThreadId, false);
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Passive-world focus recovery deferred: " + ex.Message);
                return false;
            }
        }

        private static async Task<LiveCommandResult>
            PassiveWorldAcknowledgeDiplomacyAnnouncementAsync()
        {
            ReignWorldDiplomacyCampaignBehavior behavior =
                ReignWorldDiplomacyCampaignBehavior.Instance;
            if (behavior != null)
                await behavior.PollAndShowNextAnnouncementForLiveHarnessAsync()
                    .ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                bool acknowledged = ReignDiplomacyAnnouncementScreenManager
                    .TryAcknowledgeForLiveHarness(false, out string eventId);
                return LiveCommandResult.Completed(
                    acknowledged
                        ? "Acknowledged the active Reign diplomacy announcement."
                        : "No Reign diplomacy announcement was open.",
                    new JObject
                    {
                        ["acknowledged"] = acknowledged,
                        ["eventId"] = eventId
                    });
            }).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult>
            PassiveWorldAcknowledgeNativeDiplomacyNoticesAsync(JObject command)
        {
            string confirmation = command?.Value<string>("confirmation") ?? string.Empty;
            if (!string.Equals(confirmation, NativeDiplomacyNoticeConfirmation,
                StringComparison.Ordinal))
            {
                return LiveCommandResult.Failed(
                    "Native Bannerlord diplomacy notices require the exact disposable-save confirmation.",
                    new JObject
                    {
                        ["requiredConfirmation"] = NativeDiplomacyNoticeConfirmation
                    });
            }

            return await ReignMainThread.InvokeAsync(() =>
            {
                int acknowledged = AcknowledgeNativeDiplomacyMapNotices();
                return LiveCommandResult.Completed(
                    acknowledged > 0
                        ? "Acknowledged Bannerlord's pending war/peace map notice(s)."
                        : "No Bannerlord war/peace map notice was pending.",
                    new JObject
                    {
                        ["acknowledged"] = acknowledged,
                        ["remaining"] = CountNativeDiplomacyMapNotices(),
                        ["worldDay"] = CampaignTime.Now.ToDays
                    });
            }).ConfigureAwait(false);
        }

        private static int AcknowledgeNativeDiplomacyMapNotices()
        {
            List<InformationData> notices = NativeMapNotices();
            if (notices == null || notices.Count == 0) return 0;

            InformationData[] diplomacyNotices = notices
                .Where(IsNativeDiplomacyMapNotice)
                .ToArray();
            foreach (InformationData notice in diplomacyNotices)
                MBInformationManager.MapNoticeRemoved(notice);
            return diplomacyNotices.Length;
        }

        private static int CountNativeDiplomacyMapNotices()
        {
            List<InformationData> notices = NativeMapNotices();
            return notices?.Count(IsNativeDiplomacyMapNotice) ?? 0;
        }

        private static List<InformationData> NativeMapNotices()
        {
            CampaignInformationManager manager =
                TaleWorlds.CampaignSystem.Campaign.Current?.CampaignInformationManager;
            return manager == null || NativeMapNoticesField == null
                ? null
                : NativeMapNoticesField.GetValue(manager) as List<InformationData>;
        }

        private static bool IsNativeDiplomacyMapNotice(InformationData notice)
        {
            return notice is WarMapNotification || notice is PeaceMapNotification;
        }

        private static async Task<LiveCommandResult>
            PassiveWorldDrainPoliticalPressureAsync()
        {
            ReignWorldDiplomacyCampaignBehavior behavior =
                ReignWorldDiplomacyCampaignBehavior.Instance;
            if (behavior == null)
                return LiveCommandResult.Failed(
                    "The Reign diplomacy campaign behavior is not available.");

            await behavior.PollPoliticalPressureForLiveHarnessAsync()
                .ConfigureAwait(false);
            double worldDay = await ReignMainThread.InvokeAsync(
                () => CampaignTime.Now.ToDays).ConfigureAwait(false);
            return LiveCommandResult.Completed(
                "Drained pending Reign political-pressure effects while campaign time remained paused.",
                new JObject { ["worldDay"] = worldDay });
        }

        private static LiveCommandResult PassiveWorldSnapshot()
        {
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null)
                return LiveCommandResult.Failed("No campaign is loaded.");

            Settlement settlement = Hero.MainHero?.CurrentSettlement;
            GameMenu menu = campaign.CurrentMenuContext?.GameMenu;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            return LiveCommandResult.Completed(
                "Captured the passive-world native checkpoint state.",
                new JObject
                {
                    ["campaignId"] = campaignId,
                    ["activeSaveName"] = ActiveSaveName(),
                    ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["worldDay"] = CampaignTime.Now.ToDays,
                    ["timeMode"] = campaign.TimeControlMode.ToString(),
                    ["settlementId"] = settlement?.StringId ?? string.Empty,
                    ["settlementName"] = settlement?.Name?.ToString() ?? string.Empty,
                    ["safeSettlement"] = settlement != null && (settlement.IsTown || settlement.IsCastle),
                    ["waitMenu"] = menu?.IsWaitMenu == true,
                    ["waitActive"] = menu?.IsWaitActive == true,
                    ["diplomacyAnnouncementOpen"] =
                        ReignDiplomacyAnnouncementScreenManager.IsOpen,
                    ["nativeDiplomacyNoticesPending"] =
                        CountNativeDiplomacyMapNotices(),
                    ["saveSyncReady"] = ReignSaveSyncCoordinator.IsReadyForCampaign(campaignId),
                    ["saveSyncAlignmentPending"] = ReignSaveSyncCoordinator.IsAlignmentPending
                });
        }

        private static async Task<LiveCommandResult> PassiveWorldDeleteCheckpointAsync(
            JObject command)
        {
            string saveName = (command?.Value<string>("saveName") ?? string.Empty).Trim();
            string prefix = (command?.Value<string>("expectedPrefix") ?? string.Empty).Trim();
            string confirmation = command?.Value<string>("confirmation") ?? string.Empty;
            if (!string.Equals(confirmation, "delete Reign campaign-test checkpoint",
                    StringComparison.Ordinal))
                return LiveCommandResult.Failed("Exact campaign-test checkpoint deletion confirmation is required.");
            bool runOwnedCheckpoint = !string.IsNullOrWhiteSpace(prefix)
                && prefix.StartsWith("ReignTest_", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(saveName)
                && saveName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            bool exactLegacySpymasterCheckpoint = string.Equals(
                    prefix,
                    "Reign_Spymaster_Organic_",
                    StringComparison.Ordinal)
                && string.Equals(
                    saveName,
                    "Reign_Spymaster_Organic_Midrun_A",
                    StringComparison.Ordinal);
            if (!runOwnedCheckpoint && !exactLegacySpymasterCheckpoint)
                return LiveCommandResult.Failed("Checkpoint deletion is outside the enrolled ReignTest_ run namespace.");

            return await ReignMainThread.InvokeAsync(() =>
            {
                string active = ActiveSaveName();
                if (string.Equals(active, saveName, StringComparison.OrdinalIgnoreCase))
                    return LiveCommandResult.Failed("The currently loaded Bannerlord save cannot be deleted.");
                bool deleted = MBSaveLoad.DeleteSaveGame(saveName);
                return deleted
                    ? LiveCommandResult.Completed("Deleted the exact run-owned campaign-test checkpoint.",
                        new JObject { ["saveName"] = saveName, ["expectedPrefix"] = prefix,
                            ["activeSaveName"] = active })
                    : LiveCommandResult.Failed("Bannerlord did not delete the requested campaign-test checkpoint.",
                        new JObject { ["saveName"] = saveName, ["expectedPrefix"] = prefix,
                            ["activeSaveName"] = active });
            }).ConfigureAwait(false);
        }
    }
}
