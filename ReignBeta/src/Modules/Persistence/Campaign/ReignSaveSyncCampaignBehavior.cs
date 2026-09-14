using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed class ReignSaveSyncCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly object NativeSaveObservationGate = new object();
        private static long _nativeSaveSequence;
        private static long _nativeSaveCompletedSequence;
        private static string _nativeSaveStage = "idle";
        private static string _nativeSaveName = string.Empty;
        private static string _expectedNativeSaveName = string.Empty;
        private static DateTime _nativeSaveStartedUtc = DateTime.MinValue;
        private static DateTime _nativeSaveCompletedUtc = DateTime.MinValue;
        private static bool _nativeSaveSucceeded;
        private static volatile bool _nativeSaveInProgress;

        private string _savePointId = string.Empty;
        private string _savePointKind = string.Empty;
        private string _capturedUtc = string.Empty;
        private double _campaignTimeDays;
        private double _campaignTimeMilliseconds;
        private string _timelineId = string.Empty;
        private long _worldHistorySequence;
        private string _worldHistoryHeadEventId = string.Empty;
        private bool _registrationConfirmed;

        private string _previousSavePointId;
        private string _previousSavePointKind;
        private string _previousCapturedUtc;
        private double _previousCampaignTimeDays;
        private double _previousCampaignTimeMilliseconds;
        private string _previousTimelineId;
        private long _previousWorldHistorySequence;
        private string _previousWorldHistoryHeadEventId;
        private bool _previousRegistrationConfirmed;
        private bool _loadedFromNativeSave;
        private bool _registrationFailedForCurrentSave;
        private string _registrationFailureDetail = string.Empty;

        public static ReignSaveSyncCampaignBehavior Instance { get; private set; }

        internal static bool IsNativeSaveInProgress => _nativeSaveInProgress;

        internal static JObject NativeSaveObservation()
        {
            lock (NativeSaveObservationGate)
            {
                return new JObject
                {
                    ["sequence"] = _nativeSaveSequence,
                    ["completedSequence"] = _nativeSaveCompletedSequence,
                    ["stage"] = _nativeSaveStage,
                    ["saveName"] = _nativeSaveName,
                    ["startedUtc"] = UtcText(_nativeSaveStartedUtc),
                    ["completedUtc"] = UtcText(_nativeSaveCompletedUtc),
                    ["succeeded"] = _nativeSaveSucceeded
                };
            }
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnSaveStartedEvent.AddNonSerializedListener(this, OnSaveStarted);
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);
            CampaignEvents.OnSaveOverEvent.AddNonSerializedListener(this, OnSaveOver);
            CampaignEvents.CollectMetadataEntriesEvent.AddNonSerializedListener(this, CollectMetadataEntries);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignSaveSync_pointId", ref _savePointId);
            dataStore.SyncData("_reignSaveSync_pointKind", ref _savePointKind);
            dataStore.SyncData("_reignSaveSync_capturedUtc", ref _capturedUtc);
            dataStore.SyncData("_reignSaveSync_campaignTimeDays", ref _campaignTimeDays);
            dataStore.SyncData("_reignSaveSync_campaignTimeMilliseconds", ref _campaignTimeMilliseconds);
            dataStore.SyncData("_reignSaveSync_timelineId", ref _timelineId);
            dataStore.SyncData("_reignSaveSync_worldHistorySequence", ref _worldHistorySequence);
            dataStore.SyncData("_reignSaveSync_worldHistoryHeadEventId", ref _worldHistoryHeadEventId);
            dataStore.SyncData("_reignSaveSync_registrationConfirmed", ref _registrationConfirmed);
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            ResetNativeSaveObservation();
            _loadedFromNativeSave = false;
            ReignSaveSyncCoordinator.MarkReadyForNewCampaign();
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            ResetNativeSaveObservation();
            _loadedFromNativeSave = true;
            ReignSaveSyncCoordinator.MarkLoadedSavePending();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (!_loadedFromNativeSave)
            {
                ReignSaveSyncCoordinator.MarkReadyForNewCampaign();
                return;
            }

            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            if (!IsDurableCampaignId(campaignId))
            {
                ReignLog.Warn("Save Sync skipped because Bannerlord did not expose a durable campaign ID.");
                ReignSaveSyncCoordinator.MarkReadyWithoutSync();
                return;
            }

            ReignWorldHistoryCampaignBehavior history = ReignWorldHistoryCampaignBehavior.Instance;
            string timeline = string.IsNullOrWhiteSpace(_timelineId) ? history?.TimelineId ?? "main" : _timelineId;
            long sequence = _worldHistorySequence > 0 ? _worldHistorySequence : history?.Sequence ?? 0L;
            string head = string.IsNullOrWhiteSpace(_worldHistoryHeadEventId) ? history?.HeadEventId ?? string.Empty : _worldHistoryHeadEventId;
            if (string.IsNullOrWhiteSpace(_savePointId))
            {
                _savePointKind = "legacy_baseline";
                _campaignTimeDays = CampaignTime.Now.ToDays;
                _campaignTimeMilliseconds = CampaignTime.Now.ToMilliseconds;
                _capturedUtc = NativeSaveCreationUtc();
                if (string.IsNullOrWhiteSpace(_capturedUtc)) _capturedUtc = "legacy_unknown_utc";
                _timelineId = timeline;
                _worldHistorySequence = sequence;
                _worldHistoryHeadEventId = head;
                _savePointId = LegacyPointId(campaignId, _campaignTimeMilliseconds, timeline, sequence, head);
                _registrationConfirmed = false;
            }

            // Calendar state is campaign-scoped on the server. Do not let a previous
            // campaign's cached calendar survive while the load barrier is closed.
            ReignCalendarService.Reset();
            ReignWorldHistoryTransport.ResetToSavePoint(campaignId, timeline, sequence);
            JObject payload = BuildPayload(campaignId);
            payload["nativeSaveName"] = ActiveNativeSaveName();
            payload["nativeCreationUtc"] = NativeSaveCreationUtc();
            payload["registrationConfirmed"] = _registrationConfirmed;
            ReignSaveSyncCoordinator.BeginLoadedSave(payload);
        }

        private void OnSaveStarted()
        {
            _nativeSaveInProgress = true;
            lock (NativeSaveObservationGate)
            {
                _nativeSaveSequence++;
                _nativeSaveStage = "native_save_started";
                _nativeSaveName = string.Empty;
                _nativeSaveStartedUtc = DateTime.UtcNow;
                _nativeSaveCompletedUtc = DateTime.MinValue;
                _nativeSaveSucceeded = false;
            }
            PreservePreviousPoint();
            _registrationFailedForCurrentSave = false;
            _registrationFailureDetail = string.Empty;
            if (!ReignCampaignPreparationCampaignBehavior.IsCurrentCampaignSealed())
            {
                _registrationFailedForCurrentSave = true;
                _registrationFailureDetail =
                    "Campaign preparation has not received its durable readiness seal.";
            }
            ReignWorldHistoryCampaignBehavior history = ReignWorldHistoryCampaignBehavior.Instance;
            _savePointId = Guid.NewGuid().ToString("N");
            _savePointKind = "native_save";
            _capturedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            _campaignTimeDays = CampaignTime.Now.ToDays;
            _campaignTimeMilliseconds = CampaignTime.Now.ToMilliseconds;
            _timelineId = history?.TimelineId ?? string.Empty;
            _worldHistorySequence = history?.Sequence ?? 0L;
            _worldHistoryHeadEventId = history?.HeadEventId ?? string.Empty;
            _registrationConfirmed = false;
        }

        private void OnBeforeSave()
        {
            if (string.IsNullOrWhiteSpace(_savePointId)) return;
            Stopwatch totalTimer = Stopwatch.StartNew();
            if (!ReignCampaignPreparationCampaignBehavior.IsCurrentCampaignSealed())
            {
                _registrationConfirmed = false;
                _registrationFailedForCurrentSave = true;
                _registrationFailureDetail =
                    "Campaign preparation is still running. Wait for the preparation window to close, then save again.";
                SetNativeSaveStage("serializing_native_save_before_readiness_seal");
                ReignLog.Warn("Save Sync rejected snapshot registration before the campaign readiness seal.");
                ReignLog.Info("Save boundary timing stage=readiness_rejected totalMs=" + totalTimer.ElapsedMilliseconds);
                return;
            }
            SetNativeSaveStage("registering_save_sync_snapshot");
            long saveBoundarySequence = Math.Max(0L, _worldHistorySequence);
            ReignWorldHistoryTransport.BeginSaveBoundary(saveBoundarySequence);
            try
            {
                Stopwatch historyTimer = Stopwatch.StartNew();
                bool historyDrained = ReignWorldHistoryTransport.FlushAndWaitThroughSequence(
                    saveBoundarySequence,
                    ReignWorldHistoryFlushPolicy.CriticalSaveBoundaryBudget);
                historyTimer.Stop();
                if (!historyDrained)
                {
                    _registrationConfirmed = false;
                    _registrationFailedForCurrentSave = true;
                    _registrationFailureDetail =
                        "World-history events were still pending after the critical-save consistency budget.";
                    ReignLog.Warn(
                        "Save Sync skipped this snapshot rather than delaying the native save while world-history events were still pending.");
                    SetNativeSaveStage("serializing_native_save_without_registered_snapshot");
                    ReignLog.Info("Save boundary timing stage=history_timeout historyMs="
                        + historyTimer.ElapsedMilliseconds + " totalMs=" + totalTimer.ElapsedMilliseconds);
                    return;
                }
                JObject payload = BuildPayload(ReignCampaignIdentity.CurrentCampaignId());
                lock (NativeSaveObservationGate)
                    payload["nativeSaveName"] = _expectedNativeSaveName ?? string.Empty;
                payload["worldHistoryOutboxDrained"] = historyDrained;
                Stopwatch registrationTimer = Stopwatch.StartNew();
                JObject response = ReignSaveSyncCoordinator.RegisterBeforeSaveAsync(payload).GetAwaiter().GetResult();
                registrationTimer.Stop();
                string result = response?.Value<string>("result") ?? string.Empty;
                _registrationConfirmed = response?.Value<bool?>("ok") == true
                    && !result.Equals("disabled", StringComparison.OrdinalIgnoreCase);
                if (!_registrationConfirmed && !result.Equals("disabled", StringComparison.OrdinalIgnoreCase))
                {
                    _registrationFailedForCurrentSave = true;
                    _registrationFailureDetail = response?.Value<string>("error") ?? result;
                    ReignLog.Warn("Save Sync could not register this native save point; loading it later will leave server data untouched unless a snapshot exists.");
                }
                SetNativeSaveStage("serializing_native_save");
                ReignLog.Info("Save boundary timing stage=registered historyMs="
                    + historyTimer.ElapsedMilliseconds + " registrationMs="
                    + registrationTimer.ElapsedMilliseconds + " totalMs=" + totalTimer.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _registrationConfirmed = false;
                _registrationFailedForCurrentSave = true;
                _registrationFailureDetail = ex.Message;
                ReignLog.Warn("Save Sync registration deferred safely: " + ex.Message);
                SetNativeSaveStage("serializing_native_save_without_registered_snapshot");
                ReignLog.Info("Save boundary timing stage=registration_failed totalMs=" + totalTimer.ElapsedMilliseconds);
            }
            finally
            {
                ReignWorldHistoryTransport.EndSaveBoundary(
                    saveBoundarySequence);
            }
        }

        private void OnSaveOver(bool successful, string saveName)
        {
            DateTime completedUtc = DateTime.UtcNow;
            DateTime startedUtc;
            lock (NativeSaveObservationGate)
            {
                startedUtc = _nativeSaveStartedUtc;
                _nativeSaveCompletedSequence = _nativeSaveSequence;
                _nativeSaveStage = successful ? "native_save_completed" : "native_save_failed";
                _nativeSaveName = saveName ?? string.Empty;
                _nativeSaveCompletedUtc = completedUtc;
                _nativeSaveSucceeded = successful;
                _expectedNativeSaveName = string.Empty;
            }
            _nativeSaveInProgress = false;
            long totalMs = startedUtc == DateTime.MinValue
                ? -1L
                : Math.Max(0L, (long)(completedUtc - startedUtc).TotalMilliseconds);
            ReignLog.Info("Save boundary timing stage=completed successful=" + successful
                + " totalMs=" + totalMs + " saveName=" + (saveName ?? string.Empty));
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string pointId = _savePointId;
            if (!successful) RestorePreviousPoint();
            if (_registrationConfirmed
                && !string.IsNullOrWhiteSpace(pointId)
                && IsDurableCampaignId(campaignId))
            {
                ReignSaveSyncCoordinator.FinalizeSavePoint(campaignId, pointId, successful, saveName ?? string.Empty);
            }

            if (successful && _registrationFailedForCurrentSave)
            {
                ShowSaveSyncRegistrationFailure(saveName);
            }

            _registrationFailedForCurrentSave = false;
            _registrationFailureDetail = string.Empty;
        }

        private static void ResetNativeSaveObservation()
        {
            _nativeSaveInProgress = false;
            lock (NativeSaveObservationGate)
            {
                _nativeSaveSequence = 0;
                _nativeSaveCompletedSequence = 0;
                _nativeSaveStage = "idle";
                _nativeSaveName = string.Empty;
                _nativeSaveStartedUtc = DateTime.MinValue;
                _nativeSaveCompletedUtc = DateTime.MinValue;
                _nativeSaveSucceeded = false;
                _expectedNativeSaveName = string.Empty;
            }
        }

        internal static void ExpectNativeSaveName(string saveName)
        {
            lock (NativeSaveObservationGate)
                _expectedNativeSaveName = saveName ?? string.Empty;
        }

        private static void SetNativeSaveStage(string stage)
        {
            lock (NativeSaveObservationGate)
                _nativeSaveStage = stage ?? string.Empty;
        }

        private static string UtcText(DateTime value)
        {
            return value == DateTime.MinValue
                ? string.Empty
                : value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        private void ShowSaveSyncRegistrationFailure(string saveName)
        {
            string slot = string.IsNullOrWhiteSpace(saveName) ? "This Bannerlord save" : "The Bannerlord save '" + saveName + "'";
            if ((_registrationFailureDetail ?? string.Empty).IndexOf("15 unique states", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Bannerlord Reign Save Sync Storage Full",
                    slot + " was created, but Reign could not retain another unique server snapshot."
                        + "\n\nSave Sync already has 15 unique states. Delete an old save game from Bannerlord's native Load/Save screen, then save again. Deleting the native save will also delete its matching Reign snapshot."
                        + "\n\nYour Bannerlord save file itself is not damaged, but this newest save is not protected by Save Sync.",
                    true,
                    false,
                    "Acknowledge",
                    string.Empty,
                    null,
                    null),
                    true);
                return;
            }
            if ((_registrationFailureDetail ?? string.Empty).IndexOf("World-history events were still pending", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Bannerlord Reign Save Sync Deferred",
                    slot + " was created before Reign finished storing its pending World History."
                        + "\n\nThe local server was online, but this save is not yet protected by a matching Reign rollback snapshot. Let the local server finish catching up, then save again."
                        + "\n\nYour Bannerlord save file itself is not damaged.",
                    true,
                    false,
                    "Acknowledge",
                    string.Empty,
                    null,
                    null),
                    true);
                return;
            }
            if ((_registrationFailureDetail ?? string.Empty).IndexOf("Campaign preparation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                InformationManager.ShowInquiry(new InquiryData(
                    "Bannerlord Reign Save Sync Deferred",
                    slot + " was created before Bannerlord Reign preparation received its durable readiness seal."
                        + "\n\nWait for the preparation window to close, then save again. From that point onward, normal saves will be protected by matching Reign rollback snapshots."
                        + "\n\nYour Bannerlord save file itself is not damaged.",
                    true,
                    false,
                    "Acknowledge",
                    string.Empty,
                    null,
                    null),
                    true);
                return;
            }
            string message = slot + " was created, but the local Reign server was offline or could not register its Save Sync snapshot."
                + "\n\nLoading this save later with Save Sync enabled will not be able to restore Reign to this exact point. Start the local Reign server, then save again to create a protected rollback point."
                + "\n\nYour Bannerlord save file itself is not damaged.";

            ReignLog.Warn("Showing Save Sync registration failure warning for native save '" + (saveName ?? string.Empty) + "': " + (_registrationFailureDetail ?? string.Empty));
            InformationManager.ShowInquiry(new InquiryData(
                "Bannerlord Reign Save Sync Failed",
                message,
                true,
                false,
                "Acknowledge",
                string.Empty,
                null,
                null),
                true);
        }

        private void CollectMetadataEntries(List<KeyValuePair<string, string>> pairs)
        {
            if (pairs == null || string.IsNullOrWhiteSpace(_savePointId)) return;
            AddMetadata(pairs, "ReignSaveSyncVersion", "1");
            AddMetadata(pairs, "ReignCampaignId", ReignCampaignIdentity.CurrentCampaignId());
            AddMetadata(pairs, "ReignSavePointId", _savePointId);
            AddMetadata(pairs, "ReignSavePointKind", _savePointKind);
            AddMetadata(pairs, "ReignSavePointCapturedUtc", _capturedUtc);
            AddMetadata(pairs, "ReignSavePointCampaignTimeMs", _campaignTimeMilliseconds.ToString("R", CultureInfo.InvariantCulture));
            AddMetadata(pairs, "ReignSavePointTimelineId", _timelineId);
            AddMetadata(pairs, "ReignSavePointHistorySequence", _worldHistorySequence.ToString(CultureInfo.InvariantCulture));
            AddMetadata(pairs, "ReignSavePointHistoryHead", _worldHistoryHeadEventId);
            AddMetadata(pairs, "ReignSavePointRegistered", _registrationConfirmed ? "true" : "false");
        }

        private JObject BuildPayload(string campaignId)
        {
            return new JObject
            {
                ["campaignId"] = campaignId ?? string.Empty,
                ["campaignLabel"] = ReignCampaignIdentity.CurrentCampaignLabel(),
                ["savePointId"] = _savePointId ?? string.Empty,
                ["savePointKind"] = _savePointKind ?? "native_save",
                ["capturedUtc"] = _capturedUtc ?? string.Empty,
                ["campaignTimeDays"] = _campaignTimeDays,
                ["campaignTimeMilliseconds"] = _campaignTimeMilliseconds,
                ["timelineId"] = _timelineId ?? string.Empty,
                ["worldHistorySequence"] = _worldHistorySequence,
                ["worldHistoryHeadEventId"] = _worldHistoryHeadEventId ?? string.Empty,
                ["clientVersion"] = typeof(ReignSaveSyncCampaignBehavior).Assembly.GetName().Version?.ToString() ?? string.Empty
            };
        }

        private void PreservePreviousPoint()
        {
            _previousSavePointId = _savePointId;
            _previousSavePointKind = _savePointKind;
            _previousCapturedUtc = _capturedUtc;
            _previousCampaignTimeDays = _campaignTimeDays;
            _previousCampaignTimeMilliseconds = _campaignTimeMilliseconds;
            _previousTimelineId = _timelineId;
            _previousWorldHistorySequence = _worldHistorySequence;
            _previousWorldHistoryHeadEventId = _worldHistoryHeadEventId;
            _previousRegistrationConfirmed = _registrationConfirmed;
        }

        private void RestorePreviousPoint()
        {
            _savePointId = _previousSavePointId ?? string.Empty;
            _savePointKind = _previousSavePointKind ?? string.Empty;
            _capturedUtc = _previousCapturedUtc ?? string.Empty;
            _campaignTimeDays = _previousCampaignTimeDays;
            _campaignTimeMilliseconds = _previousCampaignTimeMilliseconds;
            _timelineId = _previousTimelineId ?? string.Empty;
            _worldHistorySequence = _previousWorldHistorySequence;
            _worldHistoryHeadEventId = _previousWorldHistoryHeadEventId ?? string.Empty;
            _registrationConfirmed = _previousRegistrationConfirmed;
        }

        private static void AddMetadata(List<KeyValuePair<string, string>> pairs, string key, string value)
        {
            pairs.RemoveAll(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
            pairs.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        }

        private static string LegacyPointId(string campaignId, double milliseconds, string timelineId, long sequence, string head)
        {
            string canonical = campaignId + "|" + milliseconds.ToString("R", CultureInfo.InvariantCulture) + "|" + (timelineId ?? "") + "|" + sequence.ToString(CultureInfo.InvariantCulture) + "|" + (head ?? "") + "|" + (Hero.MainHero?.StringId ?? "");
            using (SHA256 sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical))).Replace("-", "").ToLowerInvariant();
                return "legacy_" + hash.Substring(0, 40);
            }
        }

        private static bool IsDurableCampaignId(string campaignId)
        {
            return !string.IsNullOrWhiteSpace(campaignId)
                && !campaignId.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                && !campaignId.StartsWith("unsaved_", StringComparison.OrdinalIgnoreCase)
                && campaignId.All(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-');
        }

        private static string ActiveNativeSaveName()
        {
            try
            {
                object value = typeof(MBSaveLoad).GetProperty("ActiveSaveSlotName", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null, null);
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        private static string NativeSaveCreationUtc()
        {
            // The serialized Reign UTC is authoritative for new saves. Native creation metadata is optional corroboration.
            return string.Empty;
        }
    }
}
