using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.Runtime;
using ReignBeta.World;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignAICampaignBehavior : CampaignBehaviorBase
    {
        private readonly object _pendingServerActionsLock = new object();
        private List<ReignWorldActionRecord> _actions = new List<ReignWorldActionRecord>();
        private List<ReignDiplomaticAgreementRecord> _agreements = new List<ReignDiplomaticAgreementRecord>();
        private List<ReignWarOriginRecord> _warOrigins = new List<ReignWarOriginRecord>();
        private List<ReignTreatyObligationRecord> _treatyObligations = new List<ReignTreatyObligationRecord>();
        private List<ReignWorldActionRecord> _pendingServerActions = new List<ReignWorldActionRecord>();
        private readonly Dictionary<string, ReignActionResult> _lastActionResults = new Dictionary<string, ReignActionResult>();
        private float _lastServerSnapshotDay = -1000f;
        private float _lastServerActionPollDay = -1000f;
        private float _lastWorldTestHeartbeatDay = -1000f;
        private bool _serverActionPollInFlight;
        private bool _worldTestHeartbeatInFlight;
        private bool _conversationSceneTickInFlight;
        private bool _dailyWorldSnapshotInFlight;
        private bool _worldTestConfirmedCadenceInitialized;
        private bool _worldTestSaveSyncResumePending;
        private bool _governmentRefusalsReconciled;
        private long _lastRealtimeActionPollUtcTicks;
        private static readonly long RealtimeActionPollIntervalTicks =
            System.TimeSpan.FromSeconds(1d).Ticks;

        public static ReignAICampaignBehavior Instance { get; private set; }

        public IReadOnlyList<ReignWorldActionRecord> Actions
        {
            get { return _actions; }
        }

        public IReadOnlyList<ReignDiplomaticAgreementRecord> Agreements
        {
            get { return _agreements; }
        }

        public IReadOnlyList<ReignWarOriginRecord> WarOrigins { get { return _warOrigins; } }
        public IReadOnlyList<ReignTreatyObligationRecord> TreatyObligations { get { return _treatyObligations; } }

        public void ImportNativeDiplomaticAgreements()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                return;
            }

            IAllianceCampaignBehavior alliances = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<IAllianceCampaignBehavior>();
            ITradeAgreementsCampaignBehavior trades = TaleWorlds.CampaignSystem.Campaign.Current.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            List<Kingdom> live = Kingdom.All.Where(x => x != null && !x.IsEliminated).ToList();
            for (int i = 0; i < live.Count; i++)
            {
                for (int j = i + 1; j < live.Count; j++)
                {
                    Kingdom first = live[i];
                    Kingdom second = live[j];
                    if (alliances != null && alliances.IsAllyWithKingdom(first, second))
                    {
                        ImportNativeAgreement(first, second, "alliance", (float)alliances.GetAllianceEndDate(first, second).ToDays);
                    }

                    if (trades != null && trades.HasTradeAgreement(first, second, out TradeAgreementsCampaignBehavior.TradeAgreement _))
                    {
                        ImportNativeAgreement(first, second, "trade_agreement", (float)trades.GetTradeAgreementEndDate(first, second).ToDays);
                    }
                }
            }
        }

        private void ImportNativeAgreement(Kingdom first, Kingdom second, string kind, float expireDay)
        {
            if (_agreements.Any(x => x != null && x.IsActive && x.Kind == kind && SameKingdomPair(x.ActorKingdomStringId, x.TargetKingdomStringId, first.StringId, second.StringId)))
            {
                return;
            }

            _agreements.Add(new ReignDiplomaticAgreementRecord
            {
                Kind = kind,
                ActorKingdomStringId = first.StringId,
                TargetKingdomStringId = second.StringId,
                ActorClanStringId = first.RulingClan?.StringId ?? string.Empty,
                TargetClanStringId = second.RulingClan?.StringId ?? string.Empty,
                Reason = "Imported from active native campaign diplomacy.",
                CreatedDay = CurrentDay(),
                ExpireDay = expireDay,
                IsPublic = true,
                IsActive = true
            });
        }

        public ReignActionResult GetLastActionResult(string actionId)
        {
            return !string.IsNullOrWhiteSpace(actionId) && _lastActionResults.TryGetValue(actionId, out ReignActionResult result)
                ? result
                : null;
        }

        public override void RegisterEvents()
        {
            Instance = this;
            ReignCalendarPatches.Apply();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            RegisterWarOriginEvents();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            // Campaign readiness coordinator initializes the native calendar
            // before it permits recurring systems to run.
        }

        public void ApplicationTick(float deltaTime)
        {
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                || TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null)
                return;

            if (!_governmentRefusalsReconciled && ReignGovernmentCampaignBehavior.Instance != null)
            {
                _governmentRefusalsReconciled = true;
                foreach (var action in _actions.Where(x => x != null
                    && (x.Status == ReignWorldActionStatus.Failed || x.Status == ReignWorldActionStatus.Rejected)
                    && x.FailureReason == "The government process did not authorize this action.").ToList())
                {
                    if (ReignGovernmentCampaignBehavior.Instance.TryGetRecordedAuthorizationRefusal(action, out var refusal))
                        RecordRejectedAction(action, refusal);
                }
            }

            long nowTicks = System.DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(
                ref _lastRealtimeActionPollUtcTicks);
            if (nowTicks - lastTicks >= RealtimeActionPollIntervalTicks
                && !_serverActionPollInFlight)
            {
                Interlocked.Exchange(ref _lastRealtimeActionPollUtcTicks,
                    nowTicks);
                _serverActionPollInFlight = true;
                _ = PollServerActionsAsync();
            }

            int added = DrainPendingServerActions();
            if (added <= 0) return;
            float now = CurrentDay();
            foreach (ReignWorldActionRecord action in _actions.ToList())
            {
                if (action == null || action.IsTerminal
                    || action.ExecuteAfterDay > now) continue;
                ExecuteAction(action, now);
            }
        }

        internal void PrepareInitialState()
        {
            ReignCalendarService.InitializeForCurrentCampaign();
            InitializeWarOrigins();
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving) PruneSaveBackedHistory();
            dataStore.SyncData("_reignBeta_worldActions", ref _actions);
            dataStore.SyncData("_reignBeta_diplomaticAgreements", ref _agreements);
            dataStore.SyncData("_reignBeta_warOrigins", ref _warOrigins);
            dataStore.SyncData("_reignBeta_treatyObligations", ref _treatyObligations);
            dataStore.SyncData("_reignBeta_lastServerSnapshotDay", ref _lastServerSnapshotDay);
            dataStore.SyncData("_reignBeta_lastServerActionPollDay", ref _lastServerActionPollDay);
            dataStore.SyncData("_reignBeta_lastWorldTestHeartbeatDay", ref _lastWorldTestHeartbeatDay);
            dataStore.SyncData("_reignBeta_worldTestConfirmedCadenceInitialized", ref _worldTestConfirmedCadenceInitialized);

            if (_actions == null)
            {
                _actions = new List<ReignWorldActionRecord>();
            }

            if (_agreements == null)
            {
                _agreements = new List<ReignDiplomaticAgreementRecord>();
            }

            if (_warOrigins == null) _warOrigins = new List<ReignWarOriginRecord>();
            if (_treatyObligations == null) _treatyObligations = new List<ReignTreatyObligationRecord>();

            _actions.RemoveAll(x => x == null);
            _agreements.RemoveAll(x => x == null);
            _warOrigins.RemoveAll(x => x == null);
            _treatyObligations.RemoveAll(x => x == null);
            if (dataStore.IsLoading) PruneSaveBackedHistory();
        }

        private void PruneSaveBackedHistory()
        {
            _actions = (_actions ?? new List<ReignWorldActionRecord>())
                .Where(x => x != null && !x.IsTerminal)
                .Concat((_actions ?? new List<ReignWorldActionRecord>())
                    .Where(x => x != null && x.IsTerminal)
                    .OrderByDescending(x => x.LastAttemptDay)
                    .ThenByDescending(x => x.CreatedDay)
                    .Take(128))
                .Distinct()
                .OrderBy(x => x.CreatedDay)
                .ToList();
            _agreements = (_agreements ?? new List<ReignDiplomaticAgreementRecord>())
                .Where(x => x != null && x.IsActive)
                .Concat((_agreements ?? new List<ReignDiplomaticAgreementRecord>())
                    .Where(x => x != null && !x.IsActive)
                    .OrderByDescending(x => x.ExpireDay)
                    .ThenByDescending(x => x.CreatedDay)
                    .Take(64))
                .Distinct()
                .OrderBy(x => x.CreatedDay)
                .ToList();
            _warOrigins = (_warOrigins ?? new List<ReignWarOriginRecord>())
                .OrderByDescending(x => x.DeclarationDay).Take(256).OrderBy(x => x.DeclarationDay).ToList();
            _treatyObligations = (_treatyObligations ?? new List<ReignTreatyObligationRecord>())
                .OrderByDescending(x => x.TriggeredDay).Take(256).OrderBy(x => x.TriggeredDay).ToList();
        }

        public ReignWorldActionRecord EnqueueAction(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(action.ActionId))
            {
                action.ActionId = System.Guid.NewGuid().ToString("N");
            }

            action.CreatedDay = CurrentDay();
            action.ExecuteAfterDay = action.ExecuteAfterDay <= 0f ? action.CreatedDay : action.ExecuteAfterDay;
            action.Status = action.RequiresAcceptance ? ReignWorldActionStatus.Proposed : ReignWorldActionStatus.Accepted;

            _actions.Add(action);
            ReignLog.Info("Queued action " + action.ActionId + " type=" + action.Type + " source=" + action.Source);
            ShowDebug("Queued " + action.Type + ".");
            return action;
        }

        public int EnqueueAndExecuteServerActions(IEnumerable<ReignWorldActionRecord> actions)
        {
            if (actions == null)
            {
                return 0;
            }

            int queuedCount = 0;
            float now = CurrentDay();
            foreach (ReignWorldActionRecord action in actions)
            {
                if (action == null || _actions.Any(x => x != null && x.ActionId == action.ActionId))
                {
                    continue;
                }

                ReignWorldActionRecord queued = EnqueueAction(action);
                if (queued == null)
                {
                    continue;
                }

                queuedCount++;
                _ = ReignServerClient.ReportActionAsync(queued, "enqueued_immediate", "Queued immediately from dialogue response.");
                if (!queued.RequiresAcceptance && queued.ExecuteAfterDay <= now)
                {
                    ExecuteAction(queued, now);
                }
            }

            return queuedCount;
        }

        public ReignWorldActionRecord QueueDeclareWar(Kingdom actor, Kingdom target, string reason)
        {
            return EnqueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                Source = "debug_or_direct",
                ActorKingdomStringId = actor?.StringId ?? string.Empty,
                TargetKingdomStringId = target?.StringId ?? string.Empty,
                Reason = reason ?? string.Empty
            });
        }

        public ReignWorldActionRecord QueueMakePeace(Kingdom actor, Kingdom target, string reason)
        {
            return EnqueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.DiplomacyMakePeace,
                Source = "debug_or_direct",
                ActorKingdomStringId = actor?.StringId ?? string.Empty,
                TargetKingdomStringId = target?.StringId ?? string.Empty,
                Reason = reason ?? string.Empty
            });
        }

        public ReignWorldActionRecord QueueCaptureSettlement(Hero actor, Settlement target, string reason)
        {
            MobileParty party = actor?.PartyBelongedTo;
            Kingdom actorKingdom = party?.MapFaction as Kingdom;
            return EnqueueAction(new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.StrategyCaptureSettlement,
                Source = "debug_or_direct",
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                ActorKingdomStringId = actorKingdom?.StringId ?? string.Empty,
                TargetSettlementStringId = target?.StringId ?? string.Empty,
                Reason = reason ?? string.Empty,
                MinimumTroops = 40,
                DesiredStrength = 350,
                MaxAttempts = 24
            });
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress) return;
            if (!IsEnabled())
            {
                return;
            }

            float now = CurrentDay();
            MaybeTickConversationSceneState();
            ReignActionGauntlet.TickAllSuites();
            MaybeStartServerActionPoll(now);
            MaybeSubmitWorldTestHeartbeat(now);
            DrainPendingServerActions();
            foreach (ReignWorldActionRecord action in _actions.ToList())
            {
                if (action == null || action.IsTerminal || action.ExecuteAfterDay > now)
                {
                    continue;
                }

                ExecuteAction(action, now);
            }
        }

        private void MaybeTickConversationSceneState()
        {
            if (_conversationSceneTickInFlight) return;
            _conversationSceneTickInFlight = true;
            _ = TickConversationSceneStateAsync();
        }

        private async Task TickConversationSceneStateAsync()
        {
            try
            {
                await ReignServerClient.TickConversationSceneStateAsync().ConfigureAwait(false);
            }
            finally
            {
                _conversationSceneTickInFlight = false;
            }
        }

        private void MaybeSubmitWorldTestHeartbeat(float now)
        {
            if (!_worldTestConfirmedCadenceInitialized)
            {
                _lastWorldTestHeartbeatDay = -1000f;
                _worldTestConfirmedCadenceInitialized = true;
                ReignLog.Info("World Test heartbeat migrated to confirmed-success cadence.");
            }
            if (ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                ScheduleWorldTestHeartbeatAfterSaveSync();
                return;
            }
            int day = (int)now;
            if (_worldTestHeartbeatInFlight || day <= (int)_lastWorldTestHeartbeatDay)
            {
                return;
            }

            _worldTestHeartbeatInFlight = true;
            _ = SubmitWorldTestHeartbeatAsync(now);
        }

        private void ScheduleWorldTestHeartbeatAfterSaveSync()
        {
            if (_worldTestSaveSyncResumePending) return;
            _worldTestSaveSyncResumePending = true;
            ReignSaveSyncCoordinator.RunAfterCurrentAlignmentOnMainThread(() =>
            {
                if (!ReferenceEquals(Instance, this) || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
                _worldTestSaveSyncResumePending = false;
                ReignLog.Info("Save Sync alignment completed; resuming deferred World Test heartbeat.");
                MaybeSubmitWorldTestHeartbeat(CurrentDay());
            });
        }

        private async Task SubmitWorldTestHeartbeatAsync(float scheduledDay)
        {
            bool accepted = false;
            try
            {
                double? acceptedDay = await ReignServerClient
                    .SubmitWorldTestHeartbeatAsync(_lastWorldTestHeartbeatDay)
                    .ConfigureAwait(false);
                accepted = acceptedDay.HasValue;
                if (accepted) _lastWorldTestHeartbeatDay = (float)acceptedDay.Value;
                else ReignLog.Warn("World Test daily heartbeat was not accepted.");
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _worldTestHeartbeatInFlight = false;
                    if (!accepted || !ReferenceEquals(Instance, this)
                        || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
                    float currentDay = CurrentDay();
                    if ((int)currentDay > (int)_lastWorldTestHeartbeatDay)
                    {
                        ReignLog.Info("World Test heartbeat completed behind campaign time; submitting compact catch-up heartbeat.");
                        MaybeSubmitWorldTestHeartbeat(currentDay);
                    }
                }).ConfigureAwait(false);
            }
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            ReignCalendarService.Flush();
            if (_actions == null)
            {
                return;
            }

            int active = _actions.Count(x => x != null && !x.IsTerminal);
            int completed = _actions.Count(x => x != null && x.Status == ReignWorldActionStatus.Completed);
            ReignLog.Info("Ledger daily status active=" + active + " completed=" + completed + " total=" + _actions.Count);
            PurgeExpiredAgreements();

            float now = CurrentDay();
            if (!_dailyWorldSnapshotInFlight && now - _lastServerSnapshotDay >= 0.9f)
            {
                _dailyWorldSnapshotInFlight = true;
                _ = SubmitDailyWorldSnapshotAsync(now);
            }
        }

        private async Task SubmitDailyWorldSnapshotAsync(float scheduledDay)
        {
            try
            {
                if (await ReignServerClient.IngestDailyWorldSnapshotAsync().ConfigureAwait(false))
                    _lastServerSnapshotDay = scheduledDay;
            }
            finally
            {
                _dailyWorldSnapshotInFlight = false;
            }
        }

        private void MaybeStartServerActionPoll(float now)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && (!settings.UseLocalServer || !settings.ReceiveServerActionCommands))
            {
                return;
            }

            if (_serverActionPollInFlight || now - _lastServerActionPollDay < 0.25f)
            {
                return;
            }

            _lastServerActionPollDay = now;
            _serverActionPollInFlight = true;
            _ = PollServerActionsAsync();
        }

        private async Task PollServerActionsAsync()
        {
            try
            {
                List<ReignWorldActionRecord> actions = await ReignServerClient.FetchActionCommandsAsync().ConfigureAwait(false);
                if (actions == null || actions.Count == 0)
                {
                    return;
                }

                lock (_pendingServerActionsLock)
                {
                    _pendingServerActions.AddRange(actions);
                }
            }
            finally
            {
                _serverActionPollInFlight = false;
            }
        }

        private int DrainPendingServerActions()
        {
            List<ReignWorldActionRecord> pending;
            lock (_pendingServerActionsLock)
            {
                if (_pendingServerActions.Count == 0)
                {
                    return 0;
                }

                pending = _pendingServerActions.ToList();
                _pendingServerActions.Clear();
            }

            int added = 0;
            foreach (ReignWorldActionRecord action in pending)
            {
                if (action == null || _actions.Any(x => x != null && x.ActionId == action.ActionId))
                {
                    continue;
                }

                ReignWorldActionRecord queued = EnqueueAction(action);
                if (queued != null)
                {
                    added++;
                    _ = ReignServerClient.ReportActionAsync(queued, "enqueued", "Queued in Bannerlord action ledger.");
                }
            }
            return added;
        }
        public ReignActionResult ExecuteActionForTest(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return ReignActionResult.ValidationFailed("Action is null.");
            }

            ReignWorldActionRecord queued = EnqueueAction(action);
            if (queued == null)
            {
                return ReignActionResult.ValidationFailed("Action could not be queued.");
            }

            queued.RequiresAcceptance = false;
            queued.Status = ReignWorldActionStatus.Accepted;
            queued.ExecuteAfterDay = CurrentDay();
            return ExecuteAction(queued, CurrentDay());
        }

        public bool CompleteActionFromMissionHook(string actionId, ReignActionResult result)
        {
            if (string.IsNullOrWhiteSpace(actionId) || result == null)
            {
                return false;
            }

            ReignWorldActionRecord action = _actions.FirstOrDefault(x => x != null && x.ActionId == actionId);
            if (action == null || action.IsTerminal)
            {
                return false;
            }

            if (RecordRejectedAction(action, result)) return true;

            if (result.Success && result.Completed)
            {
                ReignGovernmentCampaignBehavior.Instance?.RecordCompletedWorldAction(action);
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Completed;
                action.FailureReason = string.Empty;
                action.ExecuteAfterDay = CurrentDay();
                ReignLog.Info("Completed mission-hook action " + action.ActionId + ": " + result.Message);
                ShowDebug("Completed " + action.Type + ".");
                _ = ReignServerClient.ReportActionAsync(action, "completed", result.Message, result);
                return true;
            }

            action.FailureReason = result.Message;
            if (!result.Success && !result.Retryable)
            {
                action.Status = ReignWorldActionStatus.Failed;
                ReignLog.Warn("Failed mission-hook action " + action.ActionId + ": " + result.Message);
                ShowDebug("Failed " + action.Type + ": " + result.Message);
                _ = ReignServerClient.ReportActionAsync(action, "failed", result.Message, result);
                return true;
            }

            action.Status = ReignWorldActionStatus.Executing;
            action.ExecuteAfterDay = CurrentDay() + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.25f);
            ReignLog.Info("Mission-hook action progress " + action.ActionId + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "executing", result.Message, result);
            return true;
        }

        private ReignActionResult ExecuteAction(ReignWorldActionRecord action, float now)
        {
            if (ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress)
            {
                return ReignActionResult.Progress("Action deferred until the native save completes.")
                    .WithResultCode("native_save_in_progress");
            }

            if (action.RequiresAcceptance && action.Status == ReignWorldActionStatus.Proposed)
            {
                return ReignActionResult.Progress("Action is waiting for acceptance.").WithResultCode("waiting_for_acceptance");
            }

            if (!CanExecuteFamily(action))
            {
                ReignActionResult blocked = ReignActionResult.BlockedBySettings("Action family is disabled in Bannerlord Reign settings.");
                action.Status = ReignWorldActionStatus.Failed;
                action.FailureReason = blocked.Message;
                ReignLog.Warn("Blocked action " + action.ActionId + ": " + blocked.Message);
                _ = ReignServerClient.ReportActionAsync(action, "blocked", blocked.Message, blocked);
                return blocked;
            }

            action.Status = ReignWorldActionStatus.Executing;
            action.LastAttemptDay = now;
            if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(action.ExecutionPhase) || action.ExecutionPhase == "idle")
                {
                    Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
                    Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
                    action.ExecutionSnapshotJson = new JObject
                    {
                        ["negotiationId"] = action.NegotiationId ?? string.Empty, ["termsHash"] = action.TermsHash ?? string.Empty,
                        ["actorKingdomId"] = action.ActorKingdomStringId ?? string.Empty, ["targetKingdomId"] = action.TargetKingdomStringId ?? string.Empty,
                        ["actorRulerGold"] = actorKingdom?.Leader?.Gold ?? 0, ["targetRulerGold"] = targetKingdom?.Leader?.Gold ?? 0,
                        ["termsJson"] = action.TermsJson ?? "{}", ["preflightDay"] = now
                    }.ToString(Newtonsoft.Json.Formatting.None);
                    action.ExecutionPhase = "preflight_complete";
                }
                else if (action.ExecutionPhase == "verified")
                {
                    return ReignActionResult.Done("Negotiated action was already verified.");
                }
                action.ExecutionPhase = "material_terms";
            }

            ReignActionResult result;
            if (action.TypeValue < 100)
            {
                result = ReignDiplomacyExecutor.Execute(action);
            }
            else if (action.TypeValue >= 100 && action.TypeValue < 200)
            {
                result = ReignStrategyExecutor.Execute(action);
            }
            else if (action.TypeValue >= 200 && action.TypeValue < 300)
            {
                result = ReignPoliticsExecutor.Execute(action);
            }
            else if (action.TypeValue >= 300)
            {
                result = ReignRegularActionExecutor.Execute(action);
            }
            else
            {
                result = ReignActionResult.FailTerminal("Unsupported action family.", "unsupported_action_family", "unsupported_action");
            }

            if (RecordRejectedAction(action, result)) return result;

            if (string.Equals(result.Outcome, "obsolete", System.StringComparison.OrdinalIgnoreCase))
            {
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Cancelled;
                action.FailureReason = string.Empty;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
                    action.ExecutionPhase = "obsolete";
                ReignLog.Info("Obsoleted action " + action.ActionId + ": " + result.Message);
                _ = ReignServerClient.ReportActionAsync(action, "obsolete", result.Message, result);
                return result;
            }

            if (result.Success && result.Completed)
            {
                ReignGovernmentCampaignBehavior.Instance?.RecordCompletedWorldAction(action);
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Completed;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase)) action.ExecutionPhase = "verified";
                action.FailureReason = string.Empty;
                ReignLog.Info("Completed action " + action.ActionId + ": " + result.Message);
                string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
                if (string.IsNullOrWhiteSpace(receipt))
                {
                    ShowDebug(result.Outcome == "noop" ? "No change for " + action.Type + "." : "Completed " + action.Type + ".");
                }
                else
                {
                    ShowVerifiedReceipt(receipt);
                }
                _ = ReignServerClient.ReportActionAsync(action, "completed", result.Message, result);
                return result;
            }

            if (result.Success)
            {
                RememberActionResult(action, result);
                action.Status = ReignWorldActionStatus.Executing;
                action.FailureReason = string.Empty;
                action.ExecuteAfterDay = now + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.25f);
                ReignLog.Info("Action progress " + action.ActionId + ": " + result.Message);
                _ = ReignServerClient.ReportActionAsync(action, "executing", result.Message, result);
                return result;
            }

            action.AttemptCount++;
            RememberActionResult(action, result);
            action.FailureReason = result.Message;
            action.ExecuteAfterDay = now + (result.NextAttemptDelayDays > 0f ? result.NextAttemptDelayDays : 0.5f);

            if (!result.Retryable || action.AttemptCount >= action.MaxAttempts)
            {
                action.Status = ReignWorldActionStatus.Failed;
                if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase)) action.ExecutionPhase = "failed";
                ReignLog.Warn("Failed action " + action.ActionId + ": " + result.Message);
                ShowDebug("Failed " + action.Type + ": " + result.Message);
                _ = ReignServerClient.ReportActionAsync(action, "failed", result.Message, result);
                return result;
            }

            action.Status = ReignWorldActionStatus.Accepted;
            ReignLog.Warn("Action retry " + action.ActionId + " attempt=" + action.AttemptCount + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "retry", result.Message, result);
            return result;
        }

        private bool RecordRejectedAction(ReignWorldActionRecord action, ReignActionResult result)
        {
            if (result == null || result.Success || !result.Completed || result.Retryable
                || !string.Equals(result.Outcome, "rejected", System.StringComparison.OrdinalIgnoreCase))
                return false;
            RememberActionResult(action, result);
            action.Status = ReignWorldActionStatus.Rejected;
            action.FailureReason = result.Message;
            if (string.Equals(action.AuthorizationMode, "negotiated", System.StringComparison.OrdinalIgnoreCase))
                action.ExecutionPhase = "rejected";
            ReignLog.Info("Rejected action " + action.ActionId + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "rejected", result.Message, result);
            return true;
        }

        private static bool CanExecuteFamily(ReignWorldActionRecord action)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null)
            {
                return true;
            }

            if (action.TypeValue < 100)
            {
                return settings.ExecuteDiplomacyActions;
            }

            if (action.TypeValue >= 100 && action.TypeValue < 200)
            {
                return settings.ExecuteStrategyPlans;
            }

            if (action.TypeValue >= 200 && action.TypeValue < 300)
            {
                return settings.ExecuteInternalPoliticsActions;
            }

            if (action.TypeValue >= 300)
            {
                return settings.ExecuteConversationActions;
            }

            return true;
        }

        public ReignDiplomaticAgreementRecord RecordAgreementFromAction(ReignWorldActionRecord action, string kind, float durationDays, bool isPublic)
        {
            if (action == null)
            {
                return null;
            }

            string canonicalKind = (kind ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(canonicalKind))
            {
                canonicalKind = action.Type.ToString();
            }

            float now = CurrentDay();
            float expire = durationDays > 0f ? now + durationDays : 0f;
            foreach (ReignDiplomaticAgreementRecord existing in _agreements.Where(x => x != null && x.IsActive && SameAgreementPair(x, action) && x.Kind == canonicalKind))
            {
                EndAgreement(existing, "superseded", string.Empty, string.Empty, now);
            }

            ReignDiplomaticAgreementRecord record = new ReignDiplomaticAgreementRecord
            {
                Kind = canonicalKind,
                ActorKingdomStringId = action.ActorKingdomStringId ?? string.Empty,
                TargetKingdomStringId = action.TargetKingdomStringId ?? string.Empty,
                ActorClanStringId = action.ActorClanStringId ?? string.Empty,
                TargetClanStringId = action.TargetClanStringId ?? string.Empty,
                TargetHeroStringId = action.TargetHeroStringId ?? string.Empty,
                TargetSettlementStringId = action.TargetSettlementStringId ?? string.Empty,
                Reason = action.Reason ?? string.Empty,
                TermsJson = action.TermsJson ?? string.Empty,
                CreatedDay = now,
                ExpireDay = expire,
                IsPublic = isPublic,
                IsActive = true
            };

            _agreements.Add(record);
            ReignLog.Info("Recorded diplomatic agreement " + record.AgreementId + " kind=" + record.Kind);
            return record;
        }

        public int EndAgreementsBetween(string actorKingdomId, string targetKingdomId, string kind)
        {
            return EndAgreementsBetween(actorKingdomId, targetKingdomId, kind, "ended", string.Empty, string.Empty);
        }

        public int EndAgreementsBetween(string actorKingdomId, string targetKingdomId, string kind,
            string endReason, string breakerKingdomId, string triggeringWarId)
        {
            int ended = 0;
            foreach (ReignDiplomaticAgreementRecord agreement in _agreements.Where(x => x != null && x.IsActive).ToList())
            {
                bool pairMatches = SameKingdomPair(agreement.ActorKingdomStringId, agreement.TargetKingdomStringId, actorKingdomId, targetKingdomId);
                bool kindMatches = string.IsNullOrWhiteSpace(kind) || string.Equals(agreement.Kind, kind, System.StringComparison.OrdinalIgnoreCase);
                if (pairMatches && kindMatches)
                {
                    EndAgreement(agreement, endReason, breakerKingdomId, triggeringWarId, CurrentDay());
                    ended++;
                }
            }

            if (ended > 0)
            {
                ReignLog.Info("Ended " + ended + " diplomatic agreement(s) between " + actorKingdomId + " and " + targetKingdomId + ".");
            }

            return ended;
        }

        private void PurgeExpiredAgreements()
        {
            float now = CurrentDay();
            int expired = 0;
            foreach (ReignDiplomaticAgreementRecord agreement in _agreements.Where(x => x != null && x.IsActive && x.IsExpired(now)))
            {
                EndAgreement(agreement, "expired", string.Empty, string.Empty, now);
                expired++;
            }

            if (expired > 0)
            {
                ReignLog.Info("Expired " + expired + " diplomatic agreement(s).");
            }
        }

        private static void EndAgreement(ReignDiplomaticAgreementRecord agreement, string reason,
            string breakerKingdomId, string triggeringWarId, float endedDay)
        {
            if (agreement == null || !agreement.IsActive) return;
            agreement.IsActive = false;
            agreement.EndedDay = endedDay;
            agreement.EndReason = reason ?? string.Empty;
            agreement.BreakerKingdomStringId = breakerKingdomId ?? string.Empty;
            agreement.TriggeringWarId = triggeringWarId ?? string.Empty;
        }

        private static bool SameAgreementPair(ReignDiplomaticAgreementRecord agreement, ReignWorldActionRecord action)
        {
            return SameKingdomPair(agreement.ActorKingdomStringId, agreement.TargetKingdomStringId, action.ActorKingdomStringId, action.TargetKingdomStringId)
                && SameNullableId(agreement.ActorClanStringId, action.ActorClanStringId)
                && SameNullableId(agreement.TargetClanStringId, action.TargetClanStringId)
                && SameNullableId(agreement.TargetHeroStringId, action.TargetHeroStringId)
                && SameNullableId(agreement.TargetSettlementStringId, action.TargetSettlementStringId);
        }

        private static bool SameKingdomPair(string a1, string a2, string b1, string b2)
        {
            return (string.Equals(a1 ?? string.Empty, b1 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b2 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase))
                || (string.Equals(a1 ?? string.Empty, b2 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(a2 ?? string.Empty, b1 ?? string.Empty, System.StringComparison.OrdinalIgnoreCase));
        }

        private static bool SameNullableId(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) && string.IsNullOrWhiteSpace(right))
            {
                return true;
            }

            return string.Equals(left ?? string.Empty, right ?? string.Empty, System.StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsEnabled()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            return settings == null || settings.Enabled;
        }

        private static float CurrentDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
        }

        private static void ShowDebug(string message)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings != null && !settings.DebugMessagesEnabled)
            {
                return;
            }

            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(0xFF66CCFF)));
        }

        private void RememberActionResult(ReignWorldActionRecord action, ReignActionResult result)
        {
            if (action == null || result == null || string.IsNullOrWhiteSpace(action.ActionId))
            {
                return;
            }

            _lastActionResults[action.ActionId] = result;
            if (_lastActionResults.Count > 500)
            {
                HashSet<string> liveIds = new HashSet<string>(_actions.Skip(System.Math.Max(0, _actions.Count - 300)).Where(x => x != null).Select(x => x.ActionId));
                foreach (string staleId in _lastActionResults.Keys.Where(x => !liveIds.Contains(x)).ToList())
                {
                    _lastActionResults.Remove(staleId);
                }
            }
        }

        private static void ShowVerifiedReceipt(string receipt)
        {
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] VERIFIED: " + receipt, Color.FromUint(0xFF66FF99)));
        }
    }
}
