using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.PartyAgency;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.UI;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> SaveCheckpointAsync(JObject command)
        {
            string requested = command.Value<string>("saveName") ?? string.Empty;
            string saveName = SanitizeSaveName(string.IsNullOrWhiteSpace(requested)
                ? "Reign_Conversation_Qualification_A" : requested);
            if (string.IsNullOrWhiteSpace(saveName))
                return LiveCommandResult.Failed("A valid save name is required.");

            JObject ownership = command["checkpointOwnership"] as JObject;
            JToken partyAgencyStage = ownership?["partyAgencyStage"];
            bool hasPartyAgencyStage = partyAgencyStage != null && partyAgencyStage.Type != JTokenType.Null;
            if (saveName.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase) || ownership != null)
            {
                if (ownership == null || requested != saveName
                    || !Guid.TryParseExact(ownership.Value<string>("id"), "N", out _)
                    || string.IsNullOrWhiteSpace(ownership.Value<string>("runId"))
                    || ownership.Value<string>("saveName") != saveName)
                    return LiveCommandResult.Failed("An exact campaign-test checkpoint intent is required for this native save.");
                if (!await ReignMainThread.InvokeAsync(() => ValidCheckpointDestination(ownership, saveName)).ConfigureAwait(false))
                    return CheckpointNotQueued(ownership, "The campaign-test source or checkpoint destination does not match its intent.");
            }
            if (await ReignMainThread.InvokeAsync(() => ConversationClockRequiresRestore).ConfigureAwait(false))
                return LiveCommandResult.Failed("A conversation clock diagnostic requires restoring Current without saving; no checkpoint was queued.",
                    CheckpointNotQueuedEvidence(ownership, new JObject { ["requiresCheckpointRestore"] = true }));
            if (await ReignMainThread.InvokeAsync(() => TaleWorlds.MountAndBlade.Mission.Current != null).ConfigureAwait(false))
                return LiveCommandResult.Failed("Leave the native mission before checkpointing; no save was queued.",
                    CheckpointNotQueuedEvidence(ownership, new JObject { ["nativeMissionActive"] = true }));
            JObject visiblePartyConversation = await FinishVisiblePartyConversationForCheckpointAsync().ConfigureAwait(false);
            if (visiblePartyConversation.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(visiblePartyConversation.Value<string>("error"),
                    CheckpointNotQueuedEvidence(ownership, visiblePartyConversation));
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            string acknowledgedDiplomacyEventId = string.Empty;
            bool acknowledgedDiplomacyAnnouncement = await ReignMainThread.InvokeAsync(() =>
                ReignDiplomacyAnnouncementScreenManager.TryAcknowledgeForLiveHarness(
                    false,
                    out acknowledgedDiplomacyEventId)).ConfigureAwait(false);

            ReignSaveSyncCoordinator.SetAutomationSaveNotificationSuppressed(true);
            try
            {
            DateTime commandDeadline = DateTime.UtcNow.AddSeconds(Math.Max(
                45,
                Math.Min(300, command.Value<int?>("timeoutSeconds") ?? 180)));
            bool campaignPaused = await ReignMainThread.InvokeAsync(() =>
            {
                TaleWorlds.CampaignSystem.Campaign campaign =
                    TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign == null) return false;
                campaign.TimeControlMode = CampaignTimeControlMode.Stop;
                return true;
            }).ConfigureAwait(false);
            if (!campaignPaused)
            {
                return LiveCommandResult.Failed(
                    "No active campaign was available to pause before the checkpoint.",
                    CheckpointNotQueuedEvidence(ownership, SaveDiagnostics(saveName, "campaign_pause_failed")));
            }

            DateTime diplomacyDeadline = commandDeadline
                - AutomationSaveCompletionReserve;
            while (ReignServices.WorldDiplomacy?.HasInFlightRequest == true
                && DateTime.UtcNow < diplomacyDeadline)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            if (ReignServices.WorldDiplomacy?.HasInFlightRequest == true)
            {
                return LiveCommandResult.Failed(
                    "Diplomacy did not become idle before the checkpoint deadline; Bannerlord's native save was not started.",
                    CheckpointNotQueuedEvidence(ownership, SaveDiagnostics(saveName, "diplomacy_preflight_timeout")));
            }

            // Bannerlord accepts SaveAs while its native MapScreen Escape menu is
            // open, but does not begin the queued save until Return to the Game is
            // invoked. Close only that ordinary native pause menu before observing
            // the save sequence so an attached campaign cannot enter a false
            // IsSaving/native-start-timeout state. The shared helper uses the
            // native MapScreen API and never dismisses another inquiry or screen.
            bool escapeMenuRecovered = await ReignMainThread.InvokeAsync(
                CloseNativeMapEscapeMenuIfOpen).ConfigureAwait(false);
            if (escapeMenuRecovered)
            {
                ReignLog.Info("Guarded checkpoint closed Bannerlord's native MapScreen Escape menu before SaveAs.");
                await Task.Delay(500).ConfigureAwait(false);
            }

            JObject nativeBefore = ReignSaveSyncCampaignBehavior.NativeSaveObservation();
            DateTime priorCompletedUtc = ParseUtc(nativeBefore.Value<string>("completedUtc"));
            DateTime quietSince = _lastAutomationSaveCompletedUtc > priorCompletedUtc
                ? _lastAutomationSaveCompletedUtc
                : priorCompletedUtc;
            while (quietSince != DateTime.MinValue
                && DateTime.UtcNow - quietSince < AutomationSaveQuietPeriod
                && DateTime.UtcNow < commandDeadline)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }

            long historySequence = ReignServices.WorldHistory?.Sequence ?? 0L;
            TimeSpan historyDrainBudget = commandDeadline - DateTime.UtcNow
                - AutomationSaveCompletionReserve;
            if (historyDrainBudget <= TimeSpan.Zero
                || !ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                    historySequence, historyDrainBudget))
            {
                JObject diagnostics = SaveDiagnostics(saveName,
                    "history_preflight_timeout");
                diagnostics["worldHistorySequence"] = historySequence;
                diagnostics["worldHistoryPending"] =
                    ReignWorldHistoryTransport.PendingCount();
                diagnostics["worldHistoryUploadInFlight"] =
                    ReignWorldHistoryTransport.UploadInFlight;
                return LiveCommandResult.Failed(
                    "World History did not drain before the checkpoint deadline; Bannerlord's native save was not started.",
                    CheckpointNotQueuedEvidence(ownership, diagnostics));
            }

            long expectedSequence = (nativeBefore.Value<long?>("sequence") ?? 0L) + 1L;
            bool queued = await ReignMainThread.InvokeAsync(() =>
            {
                // A periodic scheduling hold never grants permission to save over real work.
                // Recheck at the native SaveAs boundary after the external quiet window.
                if (ReignPersonalGossipClient.PersonalGossipRuntimeStatus().Value<bool?>("inFlight") != false)
                    return false;
                if (ConversationClockRequiresRestore
                    || TaleWorlds.CampaignSystem.Campaign.Current?.SaveHandler == null
                    || TaleWorlds.CampaignSystem.Campaign.Current.SaveHandler.IsSaving)
                    return false;
                if (ownership != null && !ValidCheckpointDestination(ownership, saveName))
                    return false;
                if (hasPartyAgencyStage && !PartyAgencyCheckpointMatchesStage(partyAgencyStage as JObject, saveName))
                    return false;
                if (ownership != null)
                    expectedSequence = ReignSaveSyncCampaignBehavior.NativeSaveObservation().Value<long>("sequence") + 1L;
                // Save Sync registers before Bannerlord's completion callback
                // reveals the SaveAs name. Supplying the intended name here lets
                // the server treat an overwrite as a rotation and reserve the
                // superseded slot without weakening rollback safety.
                ReignSaveSyncCampaignBehavior.ExpectNativeSaveName(saveName);
                try
                {
                    TaleWorlds.CampaignSystem.Campaign.Current.SaveHandler.SaveAs(saveName);
                }
                catch
                {
                    ReignSaveSyncCampaignBehavior.ExpectNativeSaveName(string.Empty);
                    throw;
                }
                return true;
            }).ConfigureAwait(false);
            if (!queued)
            {
                if (ownership != null)
                    return CheckpointNotQueued(ownership, "Native saving, in-flight work, or a changed checkpoint identity prevented SaveAs.");
                return LiveCommandResult.Failed(
                    "Communication work is in flight, Bannerlord is already saving, or no campaign save handler is available.",
                    SaveDiagnostics(saveName, "queue_rejected"));
            }

            DateTime nativeStartDeadline = DateTime.UtcNow.AddSeconds(15);
            bool observedSaving = false;
            bool observedNativeStart = false;
            while (DateTime.UtcNow < commandDeadline)
            {
                bool saving = await ReignMainThread.InvokeAsync(() =>
                    TaleWorlds.CampaignSystem.Campaign.Current?.SaveHandler?.IsSaving == true).ConfigureAwait(false);
                observedSaving |= saving;
                JObject native = ReignSaveSyncCampaignBehavior.NativeSaveObservation();
                long sequence = native.Value<long?>("sequence") ?? 0L;
                long completedSequence = native.Value<long?>("completedSequence") ?? 0L;
                observedNativeStart |= sequence >= expectedSequence;
                bool nativeCompleted = completedSequence >= expectedSequence
                    && string.Equals(native.Value<string>("saveName"), saveName, StringComparison.OrdinalIgnoreCase);
                JObject finalization = ReignSaveSyncCoordinator.SaveFinalizationSnapshot();
                bool syncCompleted = string.Equals(
                        finalization.Value<string>("completedSaveName"),
                        saveName,
                        StringComparison.OrdinalIgnoreCase)
                    && finalization.Value<bool?>("succeeded") == true
                    && (finalization.Value<int?>("inFlight") ?? 0) == 0;
                if (nativeCompleted && native.Value<bool?>("succeeded") == true && !saving && syncCompleted)
                {
                    JObject checkpointReceipt = null;
                    if (ownership != null)
                    {
                        string point = await ReignMainThread.InvokeAsync(() =>
                        {
                            string actual = ReignServices.SaveSync?.CurrentSavePointId;
                            return completedSequence == expectedSequence
                                && CheckpointScopeMatches(ownership)
                                && CheckpointNamesEqual(ActiveSaveName(), saveName)
                                && !string.IsNullOrWhiteSpace(actual)
                                && NativeSaveHasPoint(saveName, ownership.Value<string>("campaignId"), actual)
                                ? actual : null;
                        }).ConfigureAwait(false);
                        string hash = point == null ? null : NativeCheckpointHash(saveName);
                        if (hash == null)
                            return LiveCommandResult.Failed("SaveAs completed without exact native campaign-test checkpoint proof.",
                                SaveDiagnostics(saveName, "checkpoint_evidence_unavailable"));
                        checkpointReceipt = new JObject
                        {
                            ["id"] = ownership.Value<string>("id"),
                            ["runId"] = ownership.Value<string>("runId"),
                            ["gameInstanceId"] = ownership.Value<string>("gameInstanceId"),
                            ["saveName"] = saveName, ["savePointId"] = point, ["sha256"] = hash
                        };
                        if (hasPartyAgencyStage)
                            checkpointReceipt["partyAgencyStage"] = partyAgencyStage.DeepClone();
                    }
                    _lastAutomationSaveName = saveName;
                    _lastAutomationSaveCompletedUtc = DateTime.UtcNow;
                    return LiveCommandResult.Completed("Bannerlord save and Save Sync checkpoint completed.", new JObject
                    {
                        ["saveName"] = saveName,
                        ["worldDay"] = CampaignTime.Now.ToDays,
                        ["gameInstanceId"] = _gameInstanceId,
                        ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                        ["saveSyncAligned"] = true,
                        ["escapeMenuRecovered"] = escapeMenuRecovered,
                        ["visiblePartyConversation"] = visiblePartyConversation,
                        ["acknowledgedDiplomacyAnnouncement"] = acknowledgedDiplomacyAnnouncement,
                        ["acknowledgedDiplomacyEventId"] = acknowledgedDiplomacyEventId,
                        ["nativeSave"] = native,
                        ["saveFinalization"] = finalization,
                        ["checkpointOwnership"] = checkpointReceipt
                    });
                }
                if (!observedNativeStart && DateTime.UtcNow >= nativeStartDeadline)
                {
                    return LiveCommandResult.Failed(
                        "Bannerlord accepted the checkpoint request, but its native save loop did not start it within 15 seconds.",
                        SaveDiagnostics(saveName, "native_start_timeout"));
                }
                if (nativeCompleted && native.Value<bool?>("succeeded") != true)
                {
                    return LiveCommandResult.Failed(
                        "Bannerlord completed the native save callback with a failure.",
                        SaveDiagnostics(saveName, "native_save_failed"));
                }
                if (nativeCompleted
                    && (finalization.Value<int?>("inFlight") ?? 0) == 0
                    && string.Equals(finalization.Value<string>("completedSaveName"), saveName, StringComparison.OrdinalIgnoreCase)
                    && finalization.Value<bool?>("succeeded") != true)
                {
                    return LiveCommandResult.Failed(
                        "Bannerlord wrote the save, but Save Sync finalization failed.",
                        SaveDiagnostics(saveName, "save_sync_finalize_failed"));
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            JObject timeout = SaveDiagnostics(saveName, "checkpoint_timeout");
            timeout["observedSaving"] = observedSaving;
            timeout["observedNativeStart"] = observedNativeStart;
            return LiveCommandResult.Failed("Timed out waiting for Bannerlord and Save Sync to complete the checkpoint.", timeout);
            }
            finally
            {
                ReignSaveSyncCoordinator.SetAutomationSaveNotificationSuppressed(false);
            }
        }

        private static bool CheckpointNamesEqual(string left, string right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static bool CheckpointScopeMatches(JObject ownership)
        {
            string campaign = ReignCampaignIdentity.CurrentCampaignId();
            return !string.IsNullOrWhiteSpace(campaign)
                && CheckpointNamesEqual(campaign, ownership.Value<string>("campaignId"))
                && !string.IsNullOrWhiteSpace(_gameInstanceId)
                && _gameInstanceId == ownership.Value<string>("gameInstanceId")
                && CheckpointNamesEqual(ReignServices.WorldHistory?.TimelineId ?? "main", ownership.Value<string>("timelineId"))
                && ReignSaveSyncCoordinator.IsReadyForCampaign(campaign)
                && !ReignSaveSyncCoordinator.IsAlignmentPending;
        }

        private static bool CheckpointSourceMatches(JObject ownership)
        {
            string source = ownership.Value<string>("sourceSaveName");
            return !string.IsNullOrWhiteSpace(source) && CheckpointScopeMatches(ownership)
                && CheckpointNamesEqual(ActiveSaveName(), source)
                && (ReignServices.SaveSync?.CurrentSavePointId ?? string.Empty) == ownership.Value<string>("sourcePointId");
        }

        private static bool ValidCheckpointDestination(JObject ownership, string saveName)
        {
            string baseline = ownership.Value<string>("baselineSaveName");
            if (!saveName.StartsWith("ReignTest_", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(baseline) || CheckpointNamesEqual(baseline, saveName)
                || !CheckpointSourceMatches(ownership))
                return false;

            string point = ownership.Value<string>("previousDestinationPointId");
            string hash = NativeCheckpointHash(saveName);
            if (string.IsNullOrEmpty(point))
                return hash == null && MBSaveLoad.GetSaveFileWithName(saveName) == null;
            return hash != null && CheckpointNamesEqual(hash, ownership.Value<string>("previousDestinationSha256"))
                && NativeSaveHasPoint(saveName, ownership.Value<string>("campaignId"), point);
        }

        private static bool NativeSaveHasPoint(string saveName, string campaignId, string point)
        {
            var metadata = MBSaveLoad.GetSaveFileWithName(saveName)?.MetaData;
            return metadata != null && CheckpointNamesEqual(metadata["ReignCampaignId"], campaignId)
                && metadata["ReignSavePointId"] == point;
        }

        private static string NativeCheckpointHash(string name)
        {
            if (SanitizeSaveName(name) != name || string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Invalid native checkpoint name.");
            string path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Mount and Blade II Bannerlord", "Game Saves", name + ".sav");
            try
            {
                using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var sha = SHA256.Create())
                {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException("A linked native checkpoint cannot prove ownership.");
                    return BitConverter.ToString(sha.ComputeHash(input)).Replace("-", string.Empty);
                }
            }
            catch (FileNotFoundException) { return null; }
        }

        private static JObject CheckpointNotQueuedEvidence(JObject ownership, JObject evidence)
        {
            evidence["nativeSaveQueued"] = false;
            if (ownership != null) evidence["checkpointNotSubmitted"] = ownership.Value<string>("id");
            return evidence;
        }

        private static LiveCommandResult CheckpointNotQueued(JObject ownership, string message) =>
            LiveCommandResult.Failed(message, CheckpointNotQueuedEvidence(ownership, new JObject()));

        private static async Task<JObject> FinishVisiblePartyConversationForCheckpointAsync()
        {
            // Family Chambers and other player-opened party conversations are not
            // necessarily owned by this harness. Their active-state disable request
            // prevents the native save loop from starting, so finish them before SaveAs.
            var visible = await ReignMainThread.InvokeAsync(() => ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
            JObject result = new JObject { ["ok"] = true, ["closed"] = false, ["nativeSaveQueued"] = false };
            if (visible == null || ReferenceEquals(visible, _partyVm)) return result;
            bool ready = await ReignMainThread.InvokeAsync(() =>
                ReferenceEquals(visible, ReignPartyChatScreenManager.ActiveViewModel) && !visible.IsBusy).ConfigureAwait(false);
            if (!ready)
            {
                result["ok"] = false;
                result["error"] = "The visible party or family conversation is busy or changed; no save was queued. Wait for its response before checkpointing.";
                return result;
            }
            ReignConversationFinishResult finished;
            try
            {
                finished = await visible.FinishAutomationSceneAsync("campaign_checkpoint").ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result["ok"] = false;
                result["error"] = "The visible conversation could not finish before checkpointing: " + ex.Message;
                return result;
            }
            result["sessionId"] = finished.SessionId;
            result["sceneSummaryId"] = finished.SceneSummaryId;
            if (!finished.Ok)
            {
                result["ok"] = false;
                result["error"] = "The visible conversation did not finish successfully; no save was queued.";
                return result;
            }
            bool closed = await ReignMainThread.InvokeAsync(() =>
            {
                if (!ReferenceEquals(visible, ReignPartyChatScreenManager.ActiveViewModel) || visible.IsBusy) return false;
                ReignPartyChatScreenManager.Close();
                return true;
            }).ConfigureAwait(false);
            result["ok"] = closed;
            result["closed"] = closed;
            if (!closed) result["error"] = "The visible conversation changed during checkpoint preparation; no save was queued.";
            else await Task.Delay(500).ConfigureAwait(false);
            return result;
        }

        private static JObject SaveDiagnostics(string saveName, string phase)
        {
            return new JObject
            {
                ["saveName"] = saveName ?? string.Empty,
                ["phase"] = phase ?? string.Empty,
                ["nativeSave"] = ReignSaveSyncCampaignBehavior.NativeSaveObservation(),
                ["saveFinalization"] = ReignSaveSyncCoordinator.SaveFinalizationSnapshot(),
                ["alignmentPending"] = ReignSaveSyncCoordinator.IsAlignmentPending,
                ["diplomacy"] = new JObject
                {
                    ["evaluationInFlight"] =
                        ReignServices.WorldDiplomacy?.EvaluationInFlight == true,
                    ["announcementPollInFlight"] =
                        ReignServices.WorldDiplomacy?.AnnouncementPollInFlight == true,
                    ["pressurePollInFlight"] =
                        ReignServices.WorldDiplomacy?.PressurePollInFlight == true,
                    ["inFlight"] =
                        ReignServices.WorldDiplomacy?.HasInFlightRequest == true
                },
                ["campaignPreparation"] =
                    ReignServices.CampaignPreparation?.Snapshot()
                    ?? new JObject(),
                ["mainThreadDispatch"] = new JObject
                {
                    ["pendingCount"] = ReignMainThread.PendingCount,
                    ["executedCount"] = ReignMainThread.ExecutedCount,
                    ["lastDrainUtc"] = UtcText(ReignMainThread.LastDrainUtc),
                    ["lastExecutedUtc"] = UtcText(ReignMainThread.LastExecutedUtc)
                }
            };
        }

        private static DateTime ParseUtc(string value)
        {
            return DateTime.TryParse(value, out DateTime parsed)
                ? parsed.ToUniversalTime()
                : DateTime.MinValue;
        }

        private static string UtcText(DateTime value)
        {
            return value == DateTime.MinValue ? string.Empty : value.ToUniversalTime().ToString("o");
        }

        private static async Task<LiveCommandResult> ShutdownGameAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            bool saving = await ReignMainThread.InvokeAsync(() =>
                TaleWorlds.CampaignSystem.Campaign.Current?.SaveHandler?.IsSaving == true).ConfigureAwait(false);
            if (saving || ReignSaveSyncCoordinator.IsAlignmentPending)
                return LiveCommandResult.Failed("Bannerlord cannot shut down while a save or Save Sync alignment is active.");

            int graceMilliseconds = Math.Max(500, Math.Min(
                10000, command.Value<int?>("delayMilliseconds") ?? 1500));
            await Task.Delay(graceMilliseconds).ConfigureAwait(false);
            // TaleWorlds.Engine.Utilities.DoDelayedexit accepts an application
            // return code, not a delay duration. Passing the requested delay as
            // that return code can leave BLSE shutdown in an unreconciled state.
            await ReignMainThread.InvokeAsync(() => Utilities.DoDelayedexit(0)).ConfigureAwait(false);
            return LiveCommandResult.Completed("Bannerlord accepted a graceful delayed shutdown.", new JObject
            {
                ["graceMilliseconds"] = graceMilliseconds,
                ["returnCode"] = 0,
                ["lastSaveName"] = _lastAutomationSaveName,
                ["gameInstanceId"] = _gameInstanceId
            });
        }

        private static string SanitizeSaveName(string value)
        {
            return new string((value ?? string.Empty)
                .Where(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == ' ')
                .Take(64).ToArray()).Trim();
        }
    }
}
