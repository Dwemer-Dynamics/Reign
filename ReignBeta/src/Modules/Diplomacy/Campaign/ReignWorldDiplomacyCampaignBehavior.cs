using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.Campaign
{
    public sealed class ReignWorldDiplomacyCampaignBehavior : CampaignBehaviorBase
    {
        private const int OutageNotificationFailureThreshold = 3;
        public static ReignWorldDiplomacyCampaignBehavior Instance { get; private set; }
        private readonly object _announcementLock = new object();
        private readonly object _evaluationCatchUpLock = new object();
        private readonly object _pressureGenerationLock = new object();
        private readonly object _pressurePollLock = new object();
        private readonly Queue<ReignDiplomacyAnnouncement> _pendingAnnouncements = new Queue<ReignDiplomacyAnnouncement>();
        private readonly HashSet<string> _knownAnnouncementIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float _lastEvaluationDay = -1000f;
        private float _lastAnnouncementPollDay = -1000f;
        private float _lastPressurePollDay = -1000f;
        private float _lastPressureGenerationDay = -1000f;
        private float _lastPressureGenerationAttemptDay = -1000f;
        private float _lastRetryDay = -1000f;
        private float _lastSuccessfulEvaluationDay = -1000f;
        private volatile bool _evaluationInFlight;
        private volatile bool _announcementPollInFlight;
        private volatile bool _pressurePollInFlight;
        private volatile bool _pressureGenerationInFlight;
        private float _pendingPressureGenerationDay = -1000f;
        private float _pendingEvaluationDay = -1000f;
        private bool _outageShown;
        private int _consecutiveEvaluationFailures;
        private string _lastError = string.Empty;
        private List<string> _processedPressureNoticeIds = new List<string>();
        private List<string> _processedPressureEffectIds = new List<string>();
        private List<string> _processedClanConflictNoticeIds = new List<string>();

        public bool EvaluationInFlight => _evaluationInFlight;
        public bool AnnouncementPollInFlight => _announcementPollInFlight;
        public bool PressurePollInFlight => _pressurePollInFlight;
        public bool HasInFlightRequest =>
            _evaluationInFlight || _announcementPollInFlight
            || _pressurePollInFlight || _pressureGenerationInFlight;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignDiplomacy_lastEvaluationDay", ref _lastEvaluationDay);
            dataStore.SyncData("_reignDiplomacy_lastAnnouncementPollDay", ref _lastAnnouncementPollDay);
            dataStore.SyncData("_reignDiplomacy_lastPressurePollDay", ref _lastPressurePollDay);
            dataStore.SyncData("_reignDiplomacy_lastPressureGenerationDay",
                ref _lastPressureGenerationDay);
            dataStore.SyncData("_reignDiplomacy_lastPressureGenerationAttemptDay",
                ref _lastPressureGenerationAttemptDay);
            dataStore.SyncData("_reignDiplomacy_lastRetryDay", ref _lastRetryDay);
            dataStore.SyncData("_reignDiplomacy_lastSuccessfulEvaluationDay", ref _lastSuccessfulEvaluationDay);
            dataStore.SyncData("_reignDiplomacy_processedPressureNotices", ref _processedPressureNoticeIds);
            dataStore.SyncData("_reignDiplomacy_processedPressureEffects", ref _processedPressureEffectIds);
            dataStore.SyncData("_reignDiplomacy_processedClanConflictNotices",
                ref _processedClanConflictNoticeIds);
            if (dataStore.IsLoading)
            {
                _processedPressureNoticeIds = _processedPressureNoticeIds ?? new List<string>();
                _processedPressureEffectIds = _processedPressureEffectIds ?? new List<string>();
                _processedClanConflictNoticeIds = _processedClanConflictNoticeIds
                    ?? new List<string>();
            }
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Prepared by ReignCampaignPreparationCampaignBehavior.
        }

        internal void PrepareInitialState()
        {
            ReignDiplomacyPatches.CancelUnresolvedNpcDiplomacy();
            ReignAICampaignBehavior.Instance?.ImportNativeDiplomaticAgreements();
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop
                || !IsEnabled() || ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                return;
            }

            float now = CurrentDay();
            if (!ReignCampaignPreparationCampaignBehavior
                .AreAutonomousWorldSystemsUnlocked(now))
            {
                return;
            }
            TrySchedulePoliticalPressureGeneration(now);
            if (_evaluationInFlight)
            {
                RememberPendingEvaluationDay(now);
                return;
            }
            if (now - _lastEvaluationDay < 0.9f)
            {
                return;
            }

            _lastEvaluationDay = now;
            _evaluationInFlight = true;
            _ = EvaluateAsync();
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop
                || !IsEnabled() || ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                return;
            }

            float now = CurrentDay();
            if (!ReignCampaignPreparationCampaignBehavior
                .AreAutonomousWorldSystemsUnlocked(now))
            {
                return;
            }
            TrySchedulePoliticalPressureGeneration(now);
            if (_evaluationInFlight)
                RememberPendingEvaluationDay(now);
            if (!string.IsNullOrWhiteSpace(_lastError) && !_evaluationInFlight && now - _lastRetryDay >= 0.25f)
            {
                _lastRetryDay = now;
                _evaluationInFlight = true;
                _ = EvaluateAsync(true);
            }
            else if (string.IsNullOrWhiteSpace(_lastError) && !_evaluationInFlight
                && now - _lastEvaluationDay >= 0.9f)
            {
                // A model request can span several campaign days at maximum map
                // speed. DailyTick events that occur while it is in flight are
                // intentionally skipped, so the first following hourly tick must
                // catch the evaluation producer up instead of waiting another day.
                _lastEvaluationDay = now;
                _evaluationInFlight = true;
                _ = EvaluateAsync();
            }

            if (!_announcementPollInFlight && now - _lastAnnouncementPollDay >= 0.04f)
            {
                _lastAnnouncementPollDay = now;
                _announcementPollInFlight = true;
                _ = PollAnnouncementsAsync();
            }
            if (now - _lastPressurePollDay >= 0.04f
                && TryBeginPoliticalPressurePoll())
            {
                _lastPressurePollDay = now;
                _ = PollPoliticalPressureAsync();
            }

            TryShowNextAnnouncement();
        }

        private void TrySchedulePoliticalPressureGeneration(float now)
        {
            if (_pressureGenerationInFlight)
            {
                lock (_pressureGenerationLock)
                    _pendingPressureGenerationDay = Math.Max(
                        _pendingPressureGenerationDay, now);
                return;
            }
            if (now - _lastPressureGenerationDay < 0.9f
                || now - _lastPressureGenerationAttemptDay < 0.25f)
                return;
            _lastPressureGenerationAttemptDay = now;
            _pressureGenerationInFlight = true;
            _ = EvaluatePoliticalPressureAsync(now);
        }

        private async Task EvaluatePoliticalPressureAsync(float requestedDay)
        {
            try
            {
                ReignDiplomacyEvaluationResult result =
                    await ReignServerClient.EvaluatePoliticalPressureAsync()
                        .ConfigureAwait(false);
                if (!result.Ok)
                {
                    ReignLog.Warn("Political pressure producer deferred: "
                        + (string.IsNullOrWhiteSpace(result.Error)
                            ? result.Status : result.Error));
                    return;
                }
                _lastPressureGenerationDay = requestedDay;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Political pressure producer failed: "
                    + ex.Message);
            }
            finally
            {
                _pressureGenerationInFlight = false;
                float pendingDay;
                lock (_pressureGenerationLock)
                {
                    pendingDay = _pendingPressureGenerationDay;
                    _pendingPressureGenerationDay = -1000f;
                }
                if (pendingDay - _lastPressureGenerationDay >= 0.9f)
                {
                    await ReignMainThread.InvokeAsync(() =>
                        TrySchedulePoliticalPressureGeneration(pendingDay))
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task EvaluateAsync(bool retry = false)
        {
            try
            {
                ReignDiplomacyEvaluationResult result = await ReignServerClient.EvaluateWorldDiplomacyAsync(retry).ConfigureAwait(false);
                if (!result.Ok)
                {
                    RecordEvaluationFailure(result.Error);
                    return;
                }

                _lastError = string.Empty;
                _consecutiveEvaluationFailures = 0;
                _outageShown = false;
                _lastSuccessfulEvaluationDay = _lastEvaluationDay;
                if (!_announcementPollInFlight)
                {
                    _lastAnnouncementPollDay = _lastEvaluationDay;
                    _announcementPollInFlight = true;
                    await PollAnnouncementsAsync().ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(TryShowNextAnnouncement)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (ReignSaveSyncCoordinator.IsAlignmentPending)
                {
                    // Save Sync owns the campaign while it aligns an older native save. This is
                    // an expected deferral, not a diplomacy outage.
                    return;
                }
                RecordEvaluationFailure(ex.Message);
            }
            finally
            {
                _evaluationInFlight = false;
                float pendingDay;
                lock (_evaluationCatchUpLock)
                {
                    pendingDay = _pendingEvaluationDay;
                    _pendingEvaluationDay = -1000f;
                }
                if (string.IsNullOrWhiteSpace(_lastError)
                    && pendingDay - _lastEvaluationDay >= 0.9f)
                {
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (_evaluationInFlight)
                        {
                            RememberPendingEvaluationDay(pendingDay);
                            return;
                        }
                        _lastEvaluationDay = pendingDay;
                        _evaluationInFlight = true;
                        _ = EvaluateAsync();
                    }).ConfigureAwait(false);
                }
            }
        }

        private void RememberPendingEvaluationDay(float day)
        {
            lock (_evaluationCatchUpLock)
                _pendingEvaluationDay = Math.Max(_pendingEvaluationDay, day);
        }

        private void RecordEvaluationFailure(string error)
        {
            _lastError = string.IsNullOrWhiteSpace(error)
                ? "The Reign diplomacy service did not return a usable response."
                : error;
            _consecutiveEvaluationFailures++;
            // A reused loopback connection can occasionally be closed between
            // requests even though the server is healthy. Hourly retry already
            // recovers that condition. Reserve the campaign-pausing inquiry for a
            // sustained outage so normal play and unattended World Tests are not
            // interrupted by one transient transport failure.
            if (_consecutiveEvaluationFailures >= OutageNotificationFailureThreshold)
                _ = ReignMainThread.InvokeAsync(ShowOutageOnce);
        }

        private async Task PollAnnouncementsAsync()
        {
            try
            {
                List<ReignDiplomacyAnnouncement> announcements = await ReignServerClient.FetchDiplomacyAnnouncementsAsync().ConfigureAwait(false);
                lock (_announcementLock)
                {
                    foreach (ReignDiplomacyAnnouncement announcement in announcements.Where(x => x != null && x.IsValid))
                    {
                        if (_knownAnnouncementIds.Add(announcement.EventId))
                        {
                            _pendingAnnouncements.Enqueue(announcement);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Diplomacy announcement poll failed: " + ex.Message);
            }
            finally
            {
                _announcementPollInFlight = false;
            }
        }

        private async Task PollPoliticalPressureAsync()
        {
            try
            {
                List<ReignPoliticalPressureNotice> notices =
                    await ReignServerClient.FetchPoliticalPressureNoticesAsync().ConfigureAwait(false);
                foreach (ReignPoliticalPressureNotice notice in notices.Where(x => x != null))
                {
                    if (_processedPressureNoticeIds.Contains(notice.IncidentId,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        JArray replayReceipts = await ReignMainThread
                            .InvokeAsync(() => ApplyPoliticalPressureNotice(
                                notice, displayNotice: false))
                            .ConfigureAwait(false);
                        await ReignServerClient.AcknowledgePoliticalPressureNoticeAsync(
                            notice.IncidentId, notice.TimelineId, CurrentDay(),
                            replayReceipts)
                            .ConfigureAwait(false);
                        continue;
                    }
                    JArray receipts = await ReignMainThread.InvokeAsync(() =>
                        ApplyPoliticalPressureNotice(notice)).ConfigureAwait(false);
                    await ReignServerClient.AcknowledgePoliticalPressureNoticeAsync(
                        notice.IncidentId, notice.TimelineId, CurrentDay(), receipts)
                        .ConfigureAwait(false);
                }
                List<ReignClanConflictNotice> clanNotices =
                    await ReignServerClient.FetchClanConflictNoticesAsync().ConfigureAwait(false);
                foreach (ReignClanConflictNotice notice in clanNotices.Where(x => x != null))
                {
                    bool display = !_processedClanConflictNoticeIds.Contains(
                        notice.IncidentId, StringComparer.OrdinalIgnoreCase);
                    if (display)
                    {
                        await ReignMainThread.InvokeAsync(() =>
                            ApplyClanConflictNotice(notice)).ConfigureAwait(false);
                    }
                    await ReignServerClient.AcknowledgeClanConflictNoticeAsync(
                        notice.IncidentId, notice.TimelineId, CurrentDay())
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Political pressure notice poll failed: " + ex.Message);
            }
            finally
            {
                lock (_pressurePollLock)
                    _pressurePollInFlight = false;
            }
        }

        internal async Task PollPoliticalPressureForLiveHarnessAsync()
        {
            // Campaign time is stopped at a World Test checkpoint, so the
            // ordinary hourly poll cannot drain effects produced at the final
            // boundary. Join any poll already in flight, then perform one full
            // idempotent fetch/apply/receipt cycle without advancing time.
            while (!TryBeginPoliticalPressurePoll())
                await Task.Delay(50).ConfigureAwait(false);
            await PollPoliticalPressureAsync().ConfigureAwait(false);
        }

        private bool TryBeginPoliticalPressurePoll()
        {
            lock (_pressurePollLock)
            {
                if (_pressurePollInFlight) return false;
                _pressurePollInFlight = true;
                return true;
            }
        }

        private JArray ApplyPoliticalPressureNotice(
            ReignPoliticalPressureNotice notice, bool displayNotice = true)
        {
            JArray receipts = new JArray();
            foreach (ReignPoliticalPressureNativeEffect effect in notice.NativeEffects)
            {
                if (effect == null || string.IsNullOrWhiteSpace(effect.EffectId)) continue;
                if (_processedPressureEffectIds.Contains(effect.EffectId,
                    StringComparer.OrdinalIgnoreCase))
                {
                    receipts.Add(new JObject { ["effectId"] = effect.EffectId,
                        ["ok"] = true, ["before"] = -1, ["after"] = -1 });
                    continue;
                }
                bool ok = false;
                float before = -1f;
                float after = -1f;
                string error = string.Empty;
                try
                {
                    if (string.Equals(effect.EffectType, "settlement_loyalty",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        // Town.AllTowns excludes castle administration objects in
                        // Bannerlord. Political pressure intentionally targets both
                        // towns and castles, so resolve through the complete native
                        // settlement registry before reading its Town component.
                        Settlement settlement = Settlement.All.FirstOrDefault(x =>
                            x != null && string.Equals(x.StringId, effect.TargetId,
                                StringComparison.OrdinalIgnoreCase));
                        Town town = settlement?.Town;
                        if (town == null) throw new InvalidOperationException(
                            "Settlement " + effect.TargetId + " was not found.");
                        before = town.Loyalty;
                        town.Loyalty = MBMath.ClampFloat(before + effect.Amount, 0f, 100f);
                        after = town.Loyalty;
                        ok = true;
                    }
                    else throw new InvalidOperationException(
                        "Unsupported political pressure effect " + effect.EffectType + ".");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    ReignLog.Warn("Political pressure native effect failed: " + error);
                }
                if (ok) AddBoundedProcessedId(_processedPressureEffectIds, effect.EffectId);
                receipts.Add(new JObject { ["effectId"] = effect.EffectId,
                    ["ok"] = ok, ["before"] = before, ["after"] = after,
                    ["error"] = error });
            }

            if (!displayNotice) return receipts;

            string originChoice = string.Equals(notice.OriginStance, "support_lords",
                StringComparison.OrdinalIgnoreCase) ? "sided publicly with the involved lords"
                : "refused the court and preserved foreign relations";
            string targetChoice = string.Equals(notice.TargetStance, "support_lords",
                StringComparison.OrdinalIgnoreCase) ? "sided publicly with the involved lords"
                : "refused the court and preserved foreign relations";
            string text = "[Bannerlord Reign — Political Pressure] " + notice.Headline
                + "\n" + notice.Narrative + "\n"
                + notice.OriginRulerName + " of " + notice.OriginKingdomName + " " + originChoice
                + "; " + DescribePoliticalPressureShift(notice.OriginPressureBefore,
                    notice.OriginPressureAfter) + ". "
                + notice.TargetRulerName + " of " + notice.TargetKingdomName + " " + targetChoice
                + "; " + DescribePoliticalPressureShift(notice.TargetPressureBefore,
                    notice.TargetPressureAfter) + ".";
            InformationManager.DisplayMessage(new InformationMessage(text,
                string.Equals(notice.Polarity, "hostile", StringComparison.OrdinalIgnoreCase)
                    ? Colors.Red : Colors.Green));
            AddBoundedProcessedId(_processedPressureNoticeIds, notice.IncidentId);
            return receipts;
        }

        private void ApplyClanConflictNotice(ReignClanConflictNotice notice)
        {
            string winner = string.Equals(notice.WinningSide, "a",
                StringComparison.OrdinalIgnoreCase) ? notice.ClanAName : notice.ClanBName;
            string resolution = notice.MediationSucceeded
                ? "The ruler's mediation brought the clans to an accord."
                : "The ruler found for " + winner
                    + "; the losing clan departed embittered by the judgment.";
            string participants = "Petitioners for " + notice.ClanAName + ": "
                + string.Join(", ", notice.InvolvedA) + ". Petitioners for "
                + notice.ClanBName + ": " + string.Join(", ", notice.InvolvedB) + ".";
            string text = "[Bannerlord Reign — Clan Conflict] " + notice.Headline
                + "\n" + notice.Narrative + "\n"
                + notice.RulerName + " of " + notice.KingdomName + " heard "
                + notice.ClanAName + " against " + notice.ClanBName + ". "
                + participants + " "
                + resolution;
            InformationManager.DisplayMessage(new InformationMessage(text,
                notice.MediationSucceeded ? Colors.Green : Colors.Red));
            AddBoundedProcessedId(_processedClanConflictNoticeIds, notice.IncidentId);
        }

        private static string DescribePoliticalPressureShift(int before, int after)
        {
            if (after > before) return "political pressure mounted";
            if (after < before) return "political pressure eased";
            return "political pressure held steady";
        }

        private static void AddBoundedProcessedId(List<string> values, string id)
        {
            if (values == null || string.IsNullOrWhiteSpace(id)
                || values.Contains(id, StringComparer.OrdinalIgnoreCase)) return;
            values.Add(id);
            if (values.Count > 2000) values.RemoveRange(0, values.Count - 2000);
        }

        public async Task<bool> WaitForAnnouncementAndShowForTestAsync(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
            {
                return false;
            }

            for (int attempt = 0; attempt < 24; attempt++)
            {
                try
                {
                    List<ReignDiplomacyAnnouncement> announcements = await ReignServerClient.FetchDiplomacyAnnouncementsAsync().ConfigureAwait(false);
                    bool matched = false;
                    lock (_announcementLock)
                    {
                        foreach (ReignDiplomacyAnnouncement announcement in announcements.Where(x => x != null && x.IsValid))
                        {
                            if (_knownAnnouncementIds.Add(announcement.EventId))
                            {
                                _pendingAnnouncements.Enqueue(announcement);
                            }
                            if (string.Equals(announcement.EventId, eventId, StringComparison.OrdinalIgnoreCase))
                            {
                                matched = true;
                            }
                        }
                    }
                    if (matched)
                    {
                        await ReignMainThread.InvokeAsync(TryShowNextAnnouncement).ConfigureAwait(false);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Diplomacy debug announcement wait failed: " + ex.Message);
                }
                await Task.Delay(250).ConfigureAwait(false);
            }
            return false;
        }

        private void TryShowNextAnnouncement()
        {
            if (ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || ReignDiplomacyAnnouncementScreenManager.IsOpen || !CanOpenOnCampaignMap())
            {
                return;
            }

            ReignDiplomacyAnnouncement next = null;
            lock (_announcementLock)
            {
                if (_pendingAnnouncements.Count > 0)
                {
                    next = _pendingAnnouncements.Dequeue();
                }
            }

            if (next != null)
            {
                ReignDiplomacyAnnouncementScreenManager.Open(
                    next,
                    () => _ = AcknowledgeDiplomacyAnnouncementAndContinueAsync(
                        next.EventId),
                    () =>
                    {
                        lock (_announcementLock)
                        {
                            _knownAnnouncementIds.Remove(next.EventId);
                        }
                        TryShowNextAnnouncement();
                    });
            }
        }

        internal async Task PollAndShowNextAnnouncementForLiveHarnessAsync()
        {
            if (!_announcementPollInFlight)
            {
                _announcementPollInFlight = true;
                await PollAnnouncementsAsync().ConfigureAwait(false);
            }
            await ReignMainThread.InvokeAsync(TryShowNextAnnouncement)
                .ConfigureAwait(false);
        }

        private async Task AcknowledgeDiplomacyAnnouncementAndContinueAsync(
            string eventId)
        {
            try
            {
                await ReignServerClient.AcknowledgeDiplomacyAnnouncementAsync(
                    eventId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Diplomacy announcement acknowledgement failed: "
                    + ex.Message);
                lock (_announcementLock)
                    _knownAnnouncementIds.Remove(eventId);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(TryShowNextAnnouncement)
                    .ConfigureAwait(false);
            }
        }

        private void ShowOutageOnce()
        {
            if (_outageShown || string.IsNullOrWhiteSpace(_lastError)
                || _consecutiveEvaluationFailures < OutageNotificationFailureThreshold)
            {
                return;
            }

            _outageShown = true;
            InformationManager.ShowInquiry(new InquiryData(
                "Bannerlord Reign Diplomacy Paused",
                "Ruler-driven diplomacy could not reach the Reign AI service. No new NPC diplomatic decisions will occur until it recovers.\n\n" + _lastError + "\n\nLast successful evaluation: " + (_lastSuccessfulEvaluationDay < 0f ? "none in this save" : "campaign day " + _lastSuccessfulEvaluationDay.ToString("0.0")) + ".\n\nCheck the local server window and configured diplomacy model.",
                true,
                false,
                "Acknowledge",
                string.Empty,
                null,
                null), true);
        }

        private static bool CanOpenOnCampaignMap()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Mission.Current != null || CharacterObject.OneToOneConversationCharacter != null)
            {
                return false;
            }

            if (ReignIndividualChatScreenManager.IsOpen || ReignPartyChatScreenManager.IsOpen || ReignSocialEventScreenManager.IsOpen || ReignCorrespondenceScreenManager.IsOpen)
            {
                return false;
            }

            string topName = ScreenManager.TopScreen?.GetType().Name ?? string.Empty;
            return topName.IndexOf("Map", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsEnabled()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            return (settings == null || settings.Enabled)
                && (settings == null || settings.AllowAutonomousWorldTicks)
                && (settings == null || settings.UseLocalServer)
                && (settings == null || settings.ExecuteDiplomacyActions);
        }

        private static float CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
        }
    }
}
