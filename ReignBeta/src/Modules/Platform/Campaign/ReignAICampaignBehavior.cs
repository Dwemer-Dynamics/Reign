using System;
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
using TerminalActionReportProtocol = Reign.Core.Contracts.Actions.TerminalActionReportProtocol;

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
        private bool _completedCampaignOrdersReconciled;
        private long _lastRealtimeActionPollUtcTicks;
        // Terminal action report v1 drain: one in-flight report per loaded owner fence,
        // elapsed-time backoff scoped to that same fence (cleared when the owner changes).
        private object _terminalReportInFlightToken;
        private ReignServerClient.RequestFence _terminalReportInFlightFence;
        private ReignServerClient.RequestFence _terminalReportFence;
        private long _nextTerminalReportScanUtcTicks;
        // Bounded scan: a rotating cursor examines at most TerminalReportScanBatch retained
        // records per scan; captures/reoffers are hinted (record references only, the durable
        // state stays on the record) so they are found promptly without a full scan.
        private const int TerminalReportScanBatch = 64;
        private int _terminalReportScanCursor;
        private readonly List<ReignWorldActionRecord> _terminalReportHints = new List<ReignWorldActionRecord>();
        private readonly HashSet<string> _terminalReportDeferralLogged = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, KeyValuePair<long, int>> _terminalReportBackoff = new Dictionary<string, KeyValuePair<long, int>>(StringComparer.Ordinal);
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
            if (!string.IsNullOrWhiteSpace(actionId) && _lastActionResults.TryGetValue(actionId, out ReignActionResult result)) return result;
            var saved = _actions.FirstOrDefault(a => a != null && a.ActionId == actionId);
            if (saved == null) return null;
            if (saved.Status == ReignWorldActionStatus.Completed) return ReignActionResult.Done("Already completed; the saved action receipt prevents a second execution.");
            if (saved.Status == ReignWorldActionStatus.Cancelled) return ReignActionResult.NoOp("This action was cancelled; its saved receipt prevents another execution.")
                .WithResultCode("action_cancelled").WithOutcome("cancelled");
            if (saved.IsTerminal) return ReignActionResult.ValidationFailed(saved.FailureReason ?? "This action was not completed.");
            return ReignActionResult.Progress("Awaiting native completion.");
        }

        internal JArray ConversationActionReceipts(Hero hero) => JArray.FromObject(_actions
            .Where(a => a != null && hero != null && (a.ActorHeroStringId == hero.StringId || a.TargetHeroStringId == hero.StringId))
            .OrderByDescending(a => a.CreatedDay).Take(12).Select(a => new {
                actionId = a.ActionId, command = a.NegotiatedCommand,
                status = a.Status == ReignWorldActionStatus.Completed ? "applied"
                    : a.Status == ReignWorldActionStatus.Cancelled ? "cancelled" : a.IsTerminal ? "failed" : "pending",
                message = GetLastActionResult(a.ActionId)?.Message ?? "Awaiting native completion." }));

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

            ReignSurrenderService.Tick();
            if (IsEnabled() && !ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                ReignServerClient.PulsePersonalGossip();
                ReignServerClient.PulseCommunications();
            }

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

            if (!_completedCampaignOrdersReconciled && ReignBeta.Actions.ReignAgreementBehavior.Instance != null
                && ReignCampaignCommandBehavior.Instance != null)
            {
                _completedCampaignOrdersReconciled = true;
                foreach (var action in _actions.ToList())
                {
                    if (TryRetireImpossibleTreaty(action)) continue;
                    if (ReignBeta.Actions.ReignAgreementBehavior.Instance?.ReconcileObservedCampaignCancellation(action) == true) continue;
                    TryRestoreObservedPendingCampaignOrder(action);
                    TryCompleteObservedCampaignOrder(action);
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

            if (IsEnabled() && !ReignSaveSyncCoordinator.IsAlignmentPending) TickTerminalReports();

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
                .Where(x => x != null && (!x.IsTerminal || DurableDialogueReceipt(x)))
                .Concat((_actions ?? new List<ReignWorldActionRecord>())
                    .Where(x => x != null && x.IsTerminal && !DurableDialogueReceipt(x))
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
                if (action == null) continue;
                var existing = _actions.FirstOrDefault(x => x != null && x.ActionId == action.ActionId);
                if (existing != null)
                {
                    if (existing.TermsHash != action.TermsHash || existing.Type != action.Type
                        || existing.ActorHeroStringId != action.ActorHeroStringId || existing.TargetHeroStringId != action.TargetHeroStringId)
                        throw new InvalidOperationException("A replayed action has different terms: " + action.ActionId);
                    RefreshTerminalReportOffer(existing, action);
                    if (HandleTerminalReportReoffer(existing)) continue;
                    var prior = GetLastActionResult(existing.ActionId);
                    _ = ReignServerClient.ReportActionAsync(existing, existing.Status.ToString().ToLowerInvariant(), prior?.Message ?? "Saved action result.", prior);
                    continue;
                }

                if (!OfferTerminalReport(action)) continue;
                ReignWorldActionRecord queued = EnqueueAction(action);
                if (queued == null)
                {
                    continue;
                }

                queuedCount++;
                _ = ReignServerClient.ReportActionAsync(queued, "enqueued_immediate", "Queued immediately from dialogue response.");
                if (!queued.RequiresAcceptance && queued.ExecuteAfterDay <= now)
                {
                    try
                    {
                        ExecuteAction(queued, now);
                    }
                    catch (System.Exception ex)
                    {
                        ReignLog.Exception("Immediate dialogue action execution failed action=" + queued.ActionId
                            + " type=" + queued.Type, ex);
                        throw;
                    }
                }
            }

            return queuedCount;
        }

        private static bool DurableDialogueReceipt(ReignWorldActionRecord action) => action.ActionId != null
            && (action.ActionId.StartsWith("pa_", StringComparison.Ordinal) || action.ActionId.StartsWith("da_", StringComparison.Ordinal)
                || action.ActionId.StartsWith("sa_", StringComparison.Ordinal));

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
            if (!ReignSaveSyncCoordinator.IsAlignmentPending)
                _ = ReignServerClient.TickPersonalGossipAsync();
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
                // Started on the game thread: this fence is the loaded owner of the poll.
                var importFence = ReignServerClient.RequestFence.Capture();
                List<ReignWorldActionRecord> actions = await ReignServerClient.FetchActionCommandsAsync().ConfigureAwait(false);
                if (actions == null || actions.Count == 0)
                {
                    return;
                }

                foreach (ReignWorldActionRecord action in actions)
                    if (action != null) action.ImportFence = importFence;

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
                if (action == null) continue;
                var existing = _actions.FirstOrDefault(x => x != null && x.ActionId == action.ActionId);
                if (existing != null)
                {
                    if (existing.TermsHash != action.TermsHash || existing.Type != action.Type
                        || existing.ActorHeroStringId != action.ActorHeroStringId || existing.TargetHeroStringId != action.TargetHeroStringId)
                    { ReignLog.Warn("Rejected conflicting action replay " + action.ActionId); continue; }
                    RefreshTerminalReportOffer(existing, action);
                    if (HandleTerminalReportReoffer(existing)) continue;
                    var prior = GetLastActionResult(existing.ActionId);
                    _ = ReignServerClient.ReportActionAsync(existing, existing.Status.ToString().ToLowerInvariant(), prior?.Message ?? "Saved action result.", prior);
                    continue;
                }

                if (!OfferTerminalReport(action)) continue;
                ReignWorldActionRecord queued = EnqueueAction(action);
                if (queued != null)
                {
                    added++;
                    _ = ReignServerClient.ReportActionAsync(queued, "enqueued", "Queued in Bannerlord action ledger.");
                }
            }
            return added;
        }

        // ---- Opt-in terminal action report v1 (independent player gold/item handovers). ----
        // Phases: offered/unproven (scope captured before execution; native execution is
        // deferred while it differs from the loaded scope), captured (exact report, no protocol
        // POST from this save yet), posted/native_pending (a POST may have run: exact replay on
        // the pinned origin only), then settled: acknowledged, legacy (one-shot sent), stale,
        // conflict, refused. Unproven and settled evidence is retained, never resent or rebound.
        private const string TerminalReportStateSchema = "reign-client-terminal-report-v1";
        private static readonly long TerminalReportScanTicks = TimeSpan.FromSeconds(2d).Ticks;
        private static readonly HashSet<string> TerminalReportStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "completed", "failed", "rejected", "cancelled", "blocked", "obsolete" };
        private static readonly string[] TerminalReportScopeKeys =
            { "campaignId", "timelineId", "playerHeroStringId", "generation", "actionId", "termsHash" };

        private static JObject TerminalReportState(ReignWorldActionRecord action)
        {
            if (string.IsNullOrEmpty(action?.TerminalReportJson)) return null;
            try
            {
                JObject state = JObject.Parse(action.TerminalReportJson);
                if (state.Value<string>("schema") == TerminalReportStateSchema) return state;
            }
            catch (Newtonsoft.Json.JsonException) { }
            // Unreadable state is inert: it never reopens the legacy writer.
            return new JObject { ["schema"] = TerminalReportStateSchema, ["phase"] = "refused", ["reason"] = "unreadable_state" };
        }

        // Exact JSON string value only; other token types never coerce into identity.
        private static string TerminalReportString(JObject source, string key) =>
            source?[key] is JValue value && value.Type == JTokenType.String ? (string)value : null;

        // Classified from the imported record alone (never the loaded hero): an original
        // pa_ independent player gold/item transfer. Everything else keeps legacy reporting.
        private static bool IsKnownTerminalReportCandidate(ReignWorldActionRecord action) =>
            action?.ActionId != null && action.ActionId.StartsWith("pa_", StringComparison.Ordinal)
            && ((action.Type == ReignWorldActionType.RegularTransferGold && action.NegotiatedCommand == "transfer_gold")
                || (action.Type == ReignWorldActionType.RegularTransferItem && action.NegotiatedCommand == "transfer_item"))
            && !string.Equals(action.AuthorizationMode, "negotiated", StringComparison.OrdinalIgnoreCase);

        // Game thread, before ledger admission. Returns false only for a known candidate
        // whose original import owner (fence and its captured shared activity generation)
        // is no longer loaded; the server outbox redelivers it. A known candidate without
        // proof is admitted as retained unproven evidence: it never falls back to legacy.
        private bool OfferTerminalReport(ReignWorldActionRecord action)
        {
            action.TerminalReportJson = string.Empty;
            if (!IsKnownTerminalReportCandidate(action)) return true;
            var fence = action.ImportFence as ReignServerClient.RequestFence;
            string liveGeneration = ReignWorldHistoryCampaignBehavior.Instance?.SharedActivityGeneration ?? string.Empty;
            if (fence != null && (!fence.IsCurrent() || fence.SharedActivityGeneration != liveGeneration))
            {
                ReignLog.Warn("Terminal report candidate not admitted: its import owner is no longer loaded action=" + action.ActionId);
                return false;
            }
            string origin = action.ImportOrigin;
            var state = new JObject
            {
                ["schema"] = TerminalReportStateSchema, ["origin"] = origin ?? string.Empty,
                ["campaignId"] = ReignServerClient.GetCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["generation"] = fence != null ? fence.SharedActivityGeneration : liveGeneration,
                ["actionId"] = action.ActionId, ["termsHash"] = action.TermsHash ?? string.Empty
            };
            string reason = fence == null || origin == null ? "missing_import_proof"
                : origin.Length == 0 ? "origin_unproven"
                : Hero.MainHero == null || action.ActorHeroStringId != Hero.MainHero.StringId ? "actor_not_loaded_player"
                : !TerminalReportScopeKeys.All(key => TerminalActionReportProtocol.IsTrustedIdentifier(TerminalReportString(state, key))) ? "scope_unproven"
                : null;
            state["phase"] = reason == null ? "offered" : "unproven";
            if (reason != null)
            {
                state["reason"] = reason;
                ReignLog.Warn("Terminal report candidate retained without delivery action=" + action.ActionId + " reason=" + reason);
            }
            action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            return true;
        }

        // Game thread. Null when a nonterminal record has no terminal report state, or when its
        // persisted offer still matches the loaded campaign, timeline, player, action/terms and
        // shared activity generation (and an offered report keeps its recognized origin).
        // Otherwise the mismatch reason: native execution is deferred, nothing is mutated.
        // An unproven offer recorded without any generation keeps its earlier legacy execution.
        private static string TerminalReportExecutionMismatch(ReignWorldActionRecord action)
        {
            if (action == null || action.IsTerminal || string.IsNullOrEmpty(action.TerminalReportJson)) return null;
            JObject state = TerminalReportState(action);
            string phase = TerminalReportString(state, "phase");
            if (phase != "offered" && phase != "unproven") return "phase_" + (phase ?? "missing");
            string generation = TerminalReportString(state, "generation") ?? string.Empty;
            string live = ReignWorldHistoryCampaignBehavior.Instance?.SharedActivityGeneration ?? string.Empty;
            if (TerminalReportString(state, "campaignId") != ReignServerClient.GetCampaignId()) return "campaign";
            if (TerminalReportString(state, "timelineId") != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")) return "timeline";
            if (Hero.MainHero == null || TerminalReportString(state, "playerHeroStringId") != Hero.MainHero.StringId) return "player";
            if (TerminalReportString(state, "actionId") != action.ActionId
                || TerminalReportString(state, "termsHash") != (action.TermsHash ?? string.Empty)) return "action";
            if ((phase == "offered" || generation.Length > 0) && (generation.Length == 0 || generation != live)) return "generation";
            if (phase == "offered" && string.IsNullOrEmpty(TerminalReportString(state, "origin"))) return "origin";
            return null;
        }

        // Game thread, dedup path. Rebinds only an untouched (never attempted, no result)
        // nonterminal offered candidate whose persisted offer is stale solely by shared
        // activity generation, when the same original origin re-offers the identical action
        // (ids, terms, type, command, actor/target) under a current loaded fence and the
        // current generation and scope. The prior generation is kept as evidence. Anything
        // else stays deferred by TerminalReportExecutionMismatch for reconciliation.
        private bool RefreshTerminalReportOffer(ReignWorldActionRecord existing, ReignWorldActionRecord incoming)
        {
            if (existing == null || incoming == null || existing.IsTerminal
                || TerminalReportExecutionMismatch(existing) != "generation"
                || !IsKnownTerminalReportCandidate(existing) || !IsKnownTerminalReportCandidate(incoming)) return false;
            JObject state = TerminalReportState(existing);
            if (TerminalReportString(state, "phase") != "offered") return false;
            bool untouched = existing.AttemptCount == 0 && existing.LastAttemptDay <= 0f
                && existing.Status != ReignWorldActionStatus.Executing
                && !_lastActionResults.ContainsKey(existing.ActionId);
            bool identical = incoming.ActionId == existing.ActionId && incoming.TermsHash == existing.TermsHash
                && incoming.Type == existing.Type && incoming.NegotiatedCommand == existing.NegotiatedCommand
                && incoming.ActorHeroStringId == existing.ActorHeroStringId && incoming.TargetHeroStringId == existing.TargetHeroStringId
                && string.Equals(incoming.AuthorizationMode, existing.AuthorizationMode, StringComparison.Ordinal);
            var fence = incoming.ImportFence as ReignServerClient.RequestFence;
            string live = ReignWorldHistoryCampaignBehavior.Instance?.SharedActivityGeneration ?? string.Empty;
            string origin = TerminalReportString(state, "origin") ?? string.Empty;
            if (!untouched || !identical || fence == null || !fence.IsCurrent() || live.Length == 0
                || fence.SharedActivityGeneration != live || origin.Length == 0 || incoming.ImportOrigin != origin
                || existing.ActorHeroStringId != Hero.MainHero?.StringId
                || !TerminalActionReportProtocol.IsTrustedIdentifier(live)) return false;
            state["priorGeneration"] = TerminalReportString(state, "generation");
            state["generation"] = live;
            state["reason"] = "reoffered_current_generation";
            existing.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            ReignLog.Info("Terminal report candidate rebound to the current generation on an exact re-offer action=" + existing.ActionId);
            return true;
        }

        // A re-offer never synthesizes a protocol candidate's legacy report: a pending exact
        // report is retried now, and settled evidence suppresses the synthesized report.
        private bool HandleTerminalReportReoffer(ReignWorldActionRecord existing)
        {
            JObject state = TerminalReportState(existing);
            string phase = state?.Value<string>("phase");
            if (state == null || ((phase == "offered" || phase == "unproven") && !existing.IsTerminal)) return false;
            if (phase == "captured" || phase == "posted" || phase == "native_pending")
            {
                _terminalReportBackoff.Remove(existing.ActionId);
                HintTerminalReport(existing);
            }
            return true;
        }

        // The persisted exact report restates the persisted scope and status it is filed
        // under, within the protocol size bound; sha is its recomputed digest.
        private static bool TerminalReportPayloadExact(JObject state, string json, string status, out string sha)
        {
            sha = string.Empty;
            if (state == null || string.IsNullOrEmpty(json) || !TerminalReportStatuses.Contains(status ?? string.Empty)
                || !TerminalReportScopeKeys.All(key => TerminalActionReportProtocol.IsTrustedIdentifier(TerminalReportString(state, key)))
                || !TerminalActionReportProtocol.TryReportSha256(json, out sha, out _)) return false;
            JObject report;
            try { report = JObject.Parse(json); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string player = TerminalReportString(state, "playerHeroStringId"), actionId = TerminalReportString(state, "actionId");
            return TerminalReportString(report, "status") == status
                && TerminalReportString(report, "campaignId") == TerminalReportString(state, "campaignId")
                && TerminalReportString(report, "timelineId") == TerminalReportString(state, "timelineId")
                && TerminalReportString(report, "playerHeroStringId") == player
                && TerminalReportString(report, "actorHeroStringId") == player
                && TerminalReportString(report, "sharedActivityGeneration") == TerminalReportString(state, "generation")
                && TerminalReportString(report, "termsHash") == TerminalReportString(state, "termsHash")
                && TerminalReportString(report, "serverActionId") == actionId
                && (report["actionId"] == null || TerminalReportString(report, "actionId") == actionId);
        }

        // Terminal game-thread result. A candidate captures its one exact report and SHA;
        // delivery belongs to the drain. Everything else keeps the existing one-shot report.
        private void ReportActionOutcome(ReignWorldActionRecord action, string status, string message, ReignActionResult result)
        {
            JObject state = TerminalReportState(action);
            if (state == null || !TerminalReportStatuses.Contains(status ?? string.Empty))
            {
                _ = ReignServerClient.ReportActionAsync(action, status, message, result);
                return;
            }
            string phase = state.Value<string>("phase");
            if (phase != "offered" && phase != "unproven")
            {
                ReignLog.Warn("Terminal report already captured; a later terminal result was not reported action=" + action.ActionId);
                return;
            }
            string json = ReignServerClient.BuildTerminalReportJson(action, status, message, result);
            bool exact = TerminalReportPayloadExact(state, json, status, out string sha);
            if (exact) { state["reportJson"] = json; state["reportSha256"] = sha; }
            state["status"] = status;
            state["phase"] = !exact ? "refused" : phase == "offered" ? "captured" : "unproven";
            if (!exact) state["reason"] = "report_scope_or_size";
            action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            _terminalReportBackoff.Remove(action.ActionId);
            if (exact && phase == "offered") HintTerminalReport(action);
            ReignLog.Info("Terminal action report captured action=" + action.ActionId + " phase=" + state.Value<string>("phase"));
        }

        private void TickTerminalReports()
        {
            long now = DateTime.UtcNow.Ticks;
            if (now < _nextTerminalReportScanUtcTicks) return;
            _nextTerminalReportScanUtcTicks = now + TerminalReportScanTicks;
            string generation = ReignWorldHistoryCampaignBehavior.Instance?.SharedActivityGeneration ?? string.Empty;
            if (ReignBetaSettings.Instance?.UseLocalServer == false || generation.Length == 0) return;
            // Backoff and the in-flight slot belong to one loaded owner; a new owner starts clean.
            if (_terminalReportFence == null || !_terminalReportFence.IsCurrent() || _terminalReportFence.SharedActivityGeneration != generation)
            {
                _terminalReportFence = ReignServerClient.RequestFence.Capture();
                _terminalReportBackoff.Clear();
            }
            if (_terminalReportInFlightToken != null && ReferenceEquals(_terminalReportInFlightFence, _terminalReportFence)) return;
            // Hinted records first; each hint is consumed once and must still be retained.
            while (_terminalReportHints.Count > 0)
            {
                ReignWorldActionRecord hinted = _terminalReportHints[0];
                _terminalReportHints.RemoveAt(0);
                if (_actions.IndexOf(hinted) >= 0 && TryStartTerminalReport(hinted, now)) return;
            }
            int count = _actions.Count;
            if (count == 0) { _terminalReportScanCursor = 0; return; }
            if (_terminalReportScanCursor >= count) _terminalReportScanCursor = 0;
            for (int examined = 0; examined < Math.Min(count, TerminalReportScanBatch); examined++)
            {
                ReignWorldActionRecord action = _actions[_terminalReportScanCursor];
                _terminalReportScanCursor = (_terminalReportScanCursor + 1) % count;
                if (TryStartTerminalReport(action, now)) return;
            }
        }

        private void HintTerminalReport(ReignWorldActionRecord action)
        {
            // Overflow falls back to the rotating cursor, which still reaches every record.
            if (action != null && _terminalReportHints.Count < TerminalReportScanBatch && !_terminalReportHints.Contains(action))
                _terminalReportHints.Add(action);
        }

        private bool TryStartTerminalReport(ReignWorldActionRecord action, long now)
        {
            if (string.IsNullOrEmpty(action?.TerminalReportJson)
                || (_terminalReportBackoff.TryGetValue(action.ActionId, out var wait) && wait.Key > now)) return false;
            JObject state = TerminalReportState(action);
            string phase = state?.Value<string>("phase");
            if (phase != "captured" && phase != "posted" && phase != "native_pending") return false;
            var token = new object();
            _terminalReportInFlightToken = token;
            _terminalReportInFlightFence = _terminalReportFence;
            _ = AdvanceTerminalReportAsync(action, state, _terminalReportFence, token);
            return true;
        }

        private async Task AdvanceTerminalReportAsync(ReignWorldActionRecord action, JObject state, ReignServerClient.RequestFence fence, object token)
        {
            string actionId = action.ActionId, origin = TerminalReportString(state, "origin") ?? string.Empty;
            string sha = TerminalReportString(state, "reportSha256") ?? string.Empty, phase = state.Value<string>("phase");
            string reason = "pending";
            bool progressed = false;
            // Game-thread guard for every pinned request: same loaded owner, scope, phase,
            // origin and an exact persisted report (rechecked immediately before dispatch).
            Func<bool> owner = () => TerminalReportRequestOwner(action, fence, sha, phase, origin);
            try
            {
                if (phase == "captured")
                {
                    string preflight = await PreflightTerminalReportAsync(state, owner).ConfigureAwait(false);
                    reason = "preflight_" + preflight;
                    if (preflight == "legacy")
                    {
                        // Verified missing capability or known current ineligibility before any
                        // protocol POST: the existing one-shot report, same origin, marked first.
                        if (!await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "legacy", reason).ConfigureAwait(false)) return;
                        progressed = true;
                        phase = "legacy";
                        JObject legacy = await ReignServerClient.SendPinnedJsonAsync(origin, "/actions/report",
                            JObject.Parse(TerminalReportString(state, "reportJson")), owner).ConfigureAwait(false);
                        await ReignServerClient.ApplyLegacyReportRelationChangesAsync(legacy, fence, actionId, owner).ConfigureAwait(false);
                        return;
                    }
                    if (preflight == "stale" || preflight == "conflict")
                    {
                        progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, preflight, reason).ConfigureAwait(false);
                        return;
                    }
                    // An exact authoritative eligible reply starts the first POST. So does an
                    // exact protocol_owned reply (same scope and report type): a save may hold
                    // this captured report from before its first POST while another live
                    // instance already posted it, so a same-generation reload restores
                    // "captured". The pinned replay carries only this exact body; the server
                    // revalidates hash, type and generation and refuses any different report.
                    // Posted is persisted before dispatch, so no legacy fallback follows.
                    if (preflight != "eligible" && preflight != "protocol_owned") return;
                    if (!await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "posted", reason).ConfigureAwait(false)) return;
                    phase = "posted";
                }

                JObject ack = await ReignServerClient.SendPinnedJsonAsync(origin, TerminalActionReportProtocol.ProposedRoute,
                    TerminalReportEnvelope(state), owner).ConfigureAwait(false);
                string disposition = TerminalReportAckDisposition(ack, state);
                reason = "ack_" + disposition + ":" + (TerminalReportString(ack, "reason") ?? string.Empty);
                if (disposition != TerminalActionReportProtocol.Accepted)
                {
                    // After a POST nothing falls back. Only exact stale/conflict settles;
                    // invalid, mismatched, ineligible or unknown replies stay pending.
                    string settled = disposition == TerminalActionReportProtocol.Stale ? "stale"
                        : disposition == TerminalActionReportProtocol.Conflict ? "conflict" : null;
                    if (settled != null) progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, settled, reason).ConfigureAwait(false);
                    return;
                }
                string authoritative = TerminalReportString(ack, "authoritativeStatus") ?? string.Empty;
                if (!TerminalReportStatuses.Contains(authoritative)) { reason = "ack_authoritative_status_invalid"; return; }
                bool memoryDone = TerminalReportString(ack, "memoryProjectionStatus") == "completed";
                string effectStatus = TerminalReportString(ack, "nativeEffectStatus") ?? string.Empty;
                JToken effectToken = ack["nativeEffect"];
                bool noEffect = effectToken == null || effectToken.Type == JTokenType.Null;
                bool nativeDone = (effectStatus == "none" && noEffect) || effectStatus == "applied";
                if (effectStatus == "pending")
                {
                    JObject effect = effectToken as JObject;
                    JObject observation = null;
                    string effectReason = "owner_changed";
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        observation = ApplyTerminalReportEffect(action, fence, sha, phase, origin, effect, out effectReason);
                    }).ConfigureAwait(false);
                    if (observation == null)
                    {
                        // Invalid or unverifiable effect evidence: no local mutation, stays pending.
                        reason = "effect_" + effectReason;
                        return;
                    }
                    phase = "native_pending";
                    string receiptId = TerminalReportString(effect, "receiptId");
                    JObject receiptAck = await ReignServerClient.SendPinnedJsonAsync(origin, "/relationships/conversation/native-receipt", new JObject
                    {
                        ["campaignId"] = TerminalReportString(state, "campaignId"),
                        ["receiptIds"] = new JArray(receiptId),
                        ["nativeReceipt"] = observation
                    }, owner).ConfigureAwait(false);
                    nativeDone = receiptAck?.Value<bool?>("ok") == true
                        && TerminalReportString(receiptAck, "campaignId") == TerminalReportString(state, "campaignId")
                        && receiptAck["receiptIds"] is JArray acked
                        && acked.Any(id => id.Type == JTokenType.String && (string)id == receiptId);
                    if (!nativeDone) reason = "native_receipt_unacknowledged";
                }
                else if (!nativeDone) reason = "native_effect_status_unrecognized";
                if (nativeDone && memoryDone)
                {
                    progressed = await TransitionTerminalReportAsync(action, fence, sha, phase, origin, "acknowledged", reason,
                        s => s["authoritativeStatus"] = authoritative).ConfigureAwait(false);
                }
                else if (nativeDone) reason = "memory_projection_pending";
            }
            catch (Exception ex)
            {
                reason = "error_" + ex.GetType().Name;
            }
            finally
            {
                if (!progressed || reason.StartsWith("error_", StringComparison.Ordinal))
                    ReignLog.Info("Terminal action report step unsettled action=" + actionId + " reason=" + BoundedReason(reason));
                await ReignMainThread.InvokeAsync(() =>
                {
                    // Release only this step's own slot. A step whose owner was replaced
                    // touches nothing else: no backoff, no state.
                    if (ReferenceEquals(_terminalReportInFlightToken, token))
                    {
                        _terminalReportInFlightToken = null;
                        _terminalReportInFlightFence = null;
                    }
                    if (!ReferenceEquals(Instance, this) || !ReferenceEquals(_terminalReportFence, fence) || !fence.IsCurrent()) return;
                    long now = DateTime.UtcNow.Ticks;
                    if (progressed) { _terminalReportBackoff.Remove(actionId); return; }
                    int failures = _terminalReportBackoff.TryGetValue(actionId, out var prior) ? prior.Value + 1 : 1;
                    long delay = TimeSpan.FromSeconds(Math.Min(300d, 5d * Math.Pow(2d, Math.Min(failures, 6)))).Ticks;
                    _terminalReportBackoff[actionId] = new KeyValuePair<long, int>(now + delay, failures);
                }).ConfigureAwait(false);
            }
        }

        // "eligible", "protocol_owned" (exact scope/type already owned by the protocol; the
        // caller may replay only the pinned exact body), "legacy" (verified missing capability
        // or current ineligibility), "stale", "conflict" or "unknown". Only the pinned origin is read.
        private static async Task<string> PreflightTerminalReportAsync(JObject state, Func<bool> owner)
        {
            string origin = TerminalReportString(state, "origin") ?? string.Empty;
            JObject health = await ReignServerClient.SendPinnedJsonAsync(origin, "/health", null, owner).ConfigureAwait(false);
            if (health.Value<bool?>("ok") != true || TerminalReportString(health, "service") != "BannerlordReignServer") return "unknown";
            JToken capability = health[TerminalActionReportProtocol.HealthCapabilityField];
            if (capability == null) return "legacy";
            if (capability.Type != JTokenType.String || (string)capability != TerminalActionReportProtocol.RequestSchema) return "unknown";
            JObject reply = await ReignServerClient.SendPinnedJsonAsync(origin, TerminalActionReportProtocol.ProposedRoute + "?"
                + string.Join("&", TerminalReportScopeKeys.Select(key => key + "=" + Uri.EscapeDataString(TerminalReportString(state, key) ?? string.Empty))),
                null, owner).ConfigureAwait(false);
            if (reply.Value<bool?>("ok") != true || TerminalReportString(reply, "schema") != TerminalActionReportProtocol.PreflightSchema
                || TerminalReportString(reply, "service") != "BannerlordReignServer" || TerminalReportString(reply, "protocol") != TerminalActionReportProtocol.RequestSchema
                || TerminalReportScopeKeys.Any(key => TerminalReportString(reply, key) != TerminalReportString(state, key))) return "unknown";
            string disposition = TerminalReportString(reply, "disposition"), reason = TerminalReportString(reply, "reason");
            string reportType = TerminalReportString(JObject.Parse(TerminalReportString(state, "reportJson") ?? "{}"), "type") ?? string.Empty;
            bool sameType = reportType.Length > 0 && TerminalReportString(reply, "reportType") == reportType;
            if (disposition == TerminalActionReportProtocol.Eligible && sameType) return "eligible";
            if (disposition == TerminalActionReportProtocol.Accepted && reason == "protocol_owned" && sameType) return "protocol_owned";
            if (disposition == TerminalActionReportProtocol.Ineligible) return "legacy";
            if (disposition == TerminalActionReportProtocol.Stale) return "stale";
            return disposition == TerminalActionReportProtocol.Conflict ? "conflict" : "unknown";
        }

        private static JObject TerminalReportEnvelope(JObject state)
        {
            var envelope = new JObject { ["schema"] = TerminalActionReportProtocol.RequestSchema };
            foreach (string key in TerminalReportScopeKeys) envelope[key] = TerminalReportString(state, key);
            envelope["status"] = TerminalReportString(state, "status");
            envelope["reportJson"] = TerminalReportString(state, "reportJson");
            envelope["reportSha256"] = TerminalReportString(state, "reportSha256");
            return envelope;
        }

        // The disposition of an ack that echoes this exact report identity, else "invalid".
        private static string TerminalReportAckDisposition(JObject ack, JObject state) =>
            ack?.Value<bool?>("ok") != true || TerminalReportString(ack, "schema") != TerminalActionReportProtocol.ResponseSchema
            || TerminalReportString(ack, "campaignId") != TerminalReportString(state, "campaignId")
            || TerminalReportString(ack, "actionId") != TerminalReportString(state, "actionId")
            || TerminalReportString(ack, "reportSha256") != TerminalReportString(state, "reportSha256")
            || TerminalReportString(ack, "generation") != TerminalReportString(state, "generation")
                ? "invalid" : TerminalReportString(ack, "disposition") ?? "invalid";

        // Game thread: the same loaded owner and ledger record, the exact live campaign,
        // timeline, player and shared activity generation, and the same report identity,
        // phase, action, terms and original pinned origin.
        private JObject CurrentTerminalReport(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase, string origin)
        {
            if (!ReferenceEquals(Instance, this) || fence == null || !fence.IsCurrent() || !_actions.Contains(action)
                || Hero.MainHero == null || string.IsNullOrEmpty(sha) || string.IsNullOrEmpty(origin)) return null;
            JObject state = TerminalReportState(action);
            string generation = TerminalReportString(state, "generation");
            return state != null && TerminalReportString(state, "reportSha256") == sha && TerminalReportString(state, "phase") == phase
                && TerminalReportString(state, "origin") == origin
                && TerminalReportString(state, "campaignId") == ReignServerClient.GetCampaignId()
                && TerminalReportString(state, "timelineId") == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main")
                && TerminalReportString(state, "playerHeroStringId") == Hero.MainHero.StringId
                && !string.IsNullOrEmpty(generation) && generation == fence.SharedActivityGeneration
                && generation == (ReignWorldHistoryCampaignBehavior.Instance?.SharedActivityGeneration ?? string.Empty)
                && TerminalReportString(state, "actionId") == action.ActionId
                && TerminalReportString(state, "termsHash") == (action.TermsHash ?? string.Empty) ? state : null;
        }

        // Game thread: CurrentTerminalReport plus the persisted report still hashing to its
        // SHA and restating its exact encoded scope. Every pinned request requires it.
        private bool TerminalReportRequestOwner(ReignWorldActionRecord action, ReignServerClient.RequestFence fence, string sha, string phase, string origin)
        {
            JObject state = CurrentTerminalReport(action, fence, sha, phase, origin);
            return state != null && TerminalReportPayloadExact(state, TerminalReportString(state, "reportJson"),
                TerminalReportString(state, "status"), out string recomputed) && recomputed == sha;
        }

        private Task<bool> TransitionTerminalReportAsync(ReignWorldActionRecord action, ReignServerClient.RequestFence fence,
            string sha, string fromPhase, string origin, string toPhase, string reason, Action<JObject> update = null) =>
            ReignMainThread.InvokeAsync(() =>
            {
                if (!TerminalReportRequestOwner(action, fence, sha, fromPhase, origin)) return false;
                JObject state = TerminalReportState(action);
                state["phase"] = toPhase;
                state["reason"] = BoundedReason(reason);
                update?.Invoke(state);
                // The accepted marker (identity, SHA, status) stays; the pending payload is cleared.
                if (toPhase == "acknowledged") state.Remove("reportJson");
                action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
                ReignLog.Info("Terminal action report action=" + action.ActionId + " phase=" + toPhase + " reason=" + BoundedReason(reason));
                return true;
            });

        // Game thread. Validates the staged receipt against the original action and exact
        // report before any native mutation, then applies it once through the save-backed
        // native relation ledger (a replay returns the saved observation).
        private JObject ApplyTerminalReportEffect(ReignWorldActionRecord action, ReignServerClient.RequestFence fence,
            string sha, string phase, string origin, JObject effect, out string reason)
        {
            reason = "owner_changed";
            if (!TerminalReportRequestOwner(action, fence, sha, phase, origin)) return null;
            JObject state = TerminalReportState(action);
            reason = "invalid_effect";
            string timeline = TerminalReportString(state, "timelineId"), generation = TerminalReportString(state, "generation");
            string expected;
            try { expected = TerminalActionReportProtocol.ReceiptId(action.ActionId, timeline, generation, sha); }
            catch (ArgumentException) { return null; }
            string subjectId = TerminalReportString(effect, "subjectId") ?? string.Empty, targetId = TerminalReportString(effect, "targetId") ?? string.Empty;
            JToken deltaToken = effect?["delta"];
            long delta = deltaToken != null && deltaToken.Type == JTokenType.Integer ? (long)deltaToken : 0L;
            bool pair = subjectId.Length > 0 && targetId.Length > 0 && subjectId != targetId
                && ((subjectId == action.ActorHeroStringId && targetId == action.TargetHeroStringId)
                    || (subjectId == action.TargetHeroStringId && targetId == action.ActorHeroStringId));
            // Server action relation deltas are bounded to -25..15 and staged as pending.
            if (effect == null || TerminalReportString(effect, "receiptId") != expected || TerminalReportString(effect, "timelineId") != timeline
                || TerminalReportString(effect, "reportGeneration") != generation || TerminalReportString(effect, "reportSha256") != sha
                || TerminalReportString(effect, "nativeApplicationStatus") != "pending"
                || !pair || delta == 0 || delta < -25 || delta > 15) return null;
            reason = "participant_unavailable";
            Hero subject = ReignObjectResolver.FindHero(subjectId), target = ReignObjectResolver.FindHero(targetId);
            ReignRelationshipCampaignBehavior relations = ReignRelationshipCampaignBehavior.Instance;
            if (subject == null || target == null || subject == target || relations == null) return null;
            if (phase != "native_pending")
            {
                state["phase"] = "native_pending";
                action.TerminalReportJson = state.ToString(Newtonsoft.Json.Formatting.None);
            }
            reason = "measurement_incomplete";
            JObject observation;
            try { observation = relations.ApplyConversationRelationOnce(new[] { expected }, subject, target, (int)delta, true); }
            catch (InvalidOperationException) { return null; }
            observation["timelineId"] = timeline;
            observation["reportGeneration"] = generation;
            observation["reportSha256"] = sha;
            reason = string.Empty;
            return observation;
        }

        private static string BoundedReason(string reason) => new string((reason ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c == '_' || c == ':').Take(96).ToArray());

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

        private bool TryRestoreObservedPendingCampaignOrder(ReignWorldActionRecord action)
        {
            if (action == null || action.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || action.Status != ReignWorldActionStatus.Completed) return false;
            JObject terms;
            try { terms = JObject.Parse(action.TermsJson ?? "{}"); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string namedOrder = terms.Value<string>("orderId");
            string orderId = string.IsNullOrWhiteSpace(namedOrder) ? action.ActionId : namedOrder.Trim();
            var order = ReignCampaignCommandBehavior.Instance?.Orders.FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            string correlation = string.IsNullOrWhiteSpace(action.NegotiationId) ? action.ActionId : action.NegotiationId;
            if (order == null || order.IsTerminal || order.CommanderHeroStringId != action.ActorHeroStringId
                || order.CorrelationId != correlation || order.Steps == null || order.Steps.Count == 0
                || order.CompletedStepCount >= order.Steps.Count
                || ReignBeta.Actions.ReignAgreementBehavior.Instance?.MatchesObservedPendingOrder(action, order.Status) != true)
                return false;
            // A duplicate issue receipt used to claim completion while the exact saved
            // native order was still active. Correct only that receipt; do not issue it.
            var progress = ReignActionResult.Progress("The accepted native campaign order remains in progress.")
                .WithResultCode("campaign_order_accepted")
                .WithEffect("campaign_order", "order", order.OrderId, order.Objective, "nativeProgressObserved=true");
            action.Status = ReignWorldActionStatus.Executing;
            action.FailureReason = string.Empty;
            action.ExecuteAfterDay = CurrentDay() + 1f / 24f;
            RememberActionResult(action, progress);
            _ = ReignServerClient.ReportActionAsync(action, "executing", progress.Message, progress);
            return true;
        }

        private bool TryRetireImpossibleTreaty(ReignWorldActionRecord action)
        {
            string kind = action?.Type == ReignWorldActionType.DiplomacyDemilitarizedBorder ? "demilitarized_border"
                : action?.Type == ReignWorldActionType.DiplomacyCaravanProtectionAgreement ? "caravan_protection_agreement"
                : action?.Type == ReignWorldActionType.DiplomacySignDefensivePact ? "defensive_pact" : "";
            if (kind.Length == 0 || (action.ExecutionPhase != "idle" && action.ExecutionPhase != "obsolete")
                || (action.ExecutionSnapshotJson != "{}" && !string.IsNullOrEmpty(action.ExecutionSnapshotJson))) return false;
            bool recorded = _agreements.Any(t => t != null && t.Kind == kind && SameAgreementPair(t, action)
                && t.Reason == action.Reason && t.TermsJson == action.TermsJson);
            if (ReignBeta.Actions.ReignAgreementBehavior.Instance?.ReconcileImpossibleTreaty(action, recorded) != true) return false;
            var result = ReignActionResult.Obsolete(ReignBeta.Actions.ReignUnfulfilledTreatyPolicy.Message,
                ReignBeta.Actions.ReignUnfulfilledTreatyPolicy.ResultCode);
            RememberActionResult(action, result);
            if (action.Status == ReignWorldActionStatus.Cancelled) return true;
            action.Status = ReignWorldActionStatus.Cancelled;
            action.FailureReason = string.Empty;
            action.ExecutionPhase = "obsolete";
            ReignLog.Info("Obsoleted unfulfilled treaty " + action.ActionId + ": " + result.Message);
            _ = ReignServerClient.ReportActionAsync(action, "obsolete", result.Message, result);
            return true;
        }

        internal bool RecordObservedCampaignCancellation(ReignWorldActionRecord action, string orderId, string cancellationId)
        {
            if (action == null || !_actions.Contains(action) || action.Type != ReignWorldActionType.RegularIssueCampaignOrder) return false;
            bool alreadyCancelled = action.Status == ReignWorldActionStatus.Cancelled;
            if (action.IsTerminal && !alreadyCancelled) return false;
            var result = ReignActionResult.NoOp("The supervised campaign order was cancelled by mutual agreement.")
                .WithResultCode("campaign_order_cancelled").WithOutcome("cancelled")
                .WithEffect("campaign_order_cancelled", "order", orderId, "", "cancellationAgreement=" + cancellationId);
            RememberActionResult(action, result);
            // The receipt cache is transient; restore it on reload without reporting or applying cancellation twice.
            if (alreadyCancelled) return true;
            action.Status = ReignWorldActionStatus.Cancelled;
            action.FailureReason = string.Empty;
            action.ExecuteAfterDay = CurrentDay();
            _ = ReignServerClient.ReportActionAsync(action, "cancelled", result.Message, result);
            return true;
        }

        internal bool TryCompleteObservedCampaignOrder(ReignWorldActionRecord action)
        {
            if (action == null || action.Type != ReignWorldActionType.RegularIssueCampaignOrder
                || (action.IsTerminal && !(action.Status == ReignWorldActionStatus.Failed
                    && action.FailureReason == ReignBeta.Actions.ReignCompletedOrderPolicy.StaleAuthorizationFailure))) return false;
            JObject terms;
            try { terms = JObject.Parse(action.TermsJson ?? "{}"); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            string namedOrder = terms.Value<string>("orderId");
            string orderId = string.IsNullOrWhiteSpace(namedOrder) ? action.ActionId : namedOrder.Trim();
            var order = ReignCampaignCommandBehavior.Instance?.Orders.FirstOrDefault(o =>
                string.Equals(o.OrderId, orderId, StringComparison.OrdinalIgnoreCase));
            string correlation = string.IsNullOrWhiteSpace(action.NegotiationId) ? action.ActionId : action.NegotiationId;
            if (order == null || order.Status != "completed" || order.CommanderHeroStringId != action.ActorHeroStringId
                || order.CorrelationId != correlation || order.Steps == null || order.Steps.Count == 0
                || order.CompletedStepCount != order.Steps.Count || order.Steps.Any(s => s.Status != "completed")
                || ReignBeta.Actions.ReignAgreementBehavior.Instance?.MatchesObservedCompletedOrder(action, order.Status) != true)
                return false;
            // Recover only the known stale authorization error after exact native and
            // saved-consent proof. No native executor runs on this receipt-only path.
            if (action.Status == ReignWorldActionStatus.Failed) action.Status = ReignWorldActionStatus.Executing;
            return CompleteActionFromMissionHook(action.ActionId, ReignActionResult.Done("The supervised campaign order completed.")
                .WithResultCode("campaign_order_completed")
                .WithEffect("campaign_order_completed", "order", order.OrderId, order.Objective,
                    "completedSteps=" + order.CompletedStepCount + ";nativeCompletionObserved=true"));
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
                ReportActionOutcome(action, "completed", result.Message, result);
                return true;
            }

            action.FailureReason = result.Message;
            if (!result.Success && !result.Retryable)
            {
                action.Status = ReignWorldActionStatus.Failed;
                ReignLog.Warn("Failed mission-hook action " + action.ActionId + ": " + result.Message);
                ShowDebug("Failed " + action.Type + ": " + result.Message);
                ReportActionOutcome(action, "failed", result.Message, result);
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

            // Every native world-action execution passes here (both tick loops, immediate
            // dialogue execution, and records restored by the dedup path without re-import).
            // A candidate whose persisted offer no longer matches the loaded scope is deferred
            // untouched; only an exact current-generation re-offer can rebind it.
            string offerMismatch = TerminalReportExecutionMismatch(action);
            if (offerMismatch != null)
            {
                if (_terminalReportDeferralLogged.Add(action.ActionId + ":" + offerMismatch))
                    ReignLog.Warn("Terminal report candidate execution deferred action=" + action.ActionId + " reason=" + offerMismatch
                        + "; reconciliation limit: awaits an exact current-generation re-offer from its original origin");
                return ReignActionResult.Progress("Action deferred until its saved offer matches the loaded campaign scope.")
                    .WithResultCode("terminal_report_offer_scope_mismatch");
            }

            if (TryRetireImpossibleTreaty(action)
                || ReignBeta.Actions.ReignAgreementBehavior.Instance?.ReconcileObservedCampaignCancellation(action) == true
                || TryCompleteObservedCampaignOrder(action)) return GetLastActionResult(action.ActionId);

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
                ReportActionOutcome(action, "blocked", blocked.Message, blocked);
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
                ReportActionOutcome(action, "obsolete", result.Message, result);
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
                ReportActionOutcome(action, "completed", result.Message, result);
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
                ReportActionOutcome(action, "failed", result.Message, result);
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
            ReportActionOutcome(action, "rejected", result.Message, result);
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
