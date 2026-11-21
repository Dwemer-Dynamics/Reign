using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Government;
using ReignBeta.Platform;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TerminalActionReportProtocol = Reign.Core.Contracts.Actions.TerminalActionReportProtocol;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Diplomacy.World;
using ReignBeta.Government.Campaign;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Domain;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Platform.Settings;
using ReignBeta.Relationships.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Platform.Campaign
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
        // Not persisted. Records whose report already existed (captured or later) when this
        // save was loaded, keyed by record reference; filled once in SyncData, never added to.
        // A report captured after the load is new by absence.
        private readonly Dictionary<ReignWorldActionRecord, string> _inheritedTerminalReportPhase = new Dictionary<ReignWorldActionRecord, string>();
        // Per behavior instance (one load): reports held without any request, settled reports
        // whose one exact-redelivery reconciliation is requested or done, and the load session
        // whose proof the server refused.
        private readonly Dictionary<string, string> _terminalReportHolds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _terminalReportReconcileRequested = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _terminalReportReconciled = new HashSet<string>(StringComparer.Ordinal);
        private string _terminalReportStaleProofSession;
        private static readonly long RealtimeActionPollIntervalTicks =
            System.TimeSpan.FromSeconds(1d).Ticks;

        public static ReignAICampaignBehavior Instance => ReignServices.AI;

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

        // The saved uncertain-execution hold outranks any transient cached result, before and after reload.
        private static bool IsUncertainExecutionHeld(ReignWorldActionRecord action) => action != null
            && ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.IsUncertainExecutionHeld(action.Status.ToString(), action.FailureReason);

        private static ReignActionResult UncertainExecutionResult() =>
            ReignActionResult.Progress(ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionMarker)
                .WithResultCode(ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionResultCode).WithOutcome("unknown");

        public ReignActionResult GetLastActionResult(string actionId)
        {
            var saved = _actions.FirstOrDefault(a => a != null && a.ActionId == actionId);
            if (IsUncertainExecutionHeld(saved)) return UncertainExecutionResult();
            if (!string.IsNullOrWhiteSpace(actionId) && _lastActionResults.TryGetValue(actionId, out ReignActionResult result)) return result;
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
            ReignServices.AI = this;
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
                ReignPersonalGossipClient.PulsePersonalGossip();
                ReignCommunicationClient.PulseCommunications();
            }

            if (!_governmentRefusalsReconciled && ReignServices.Government != null)
            {
                _governmentRefusalsReconciled = true;
                foreach (var action in _actions.Where(x => x != null
                    && (x.Status == ReignWorldActionStatus.Failed || x.Status == ReignWorldActionStatus.Rejected)
                    && x.FailureReason == "The government process did not authorize this action.").ToList())
                {
                    if (ReignServices.Government.TryGetRecordedAuthorizationRefusal(action, out var refusal))
                        RecordRejectedAction(action, refusal);
                }
            }

            if (!_completedCampaignOrdersReconciled && ReignServices.Agreement != null
                && ReignServices.CampaignCommand != null)
            {
                _completedCampaignOrdersReconciled = true;
                foreach (var action in _actions.ToList())
                {
                    if (TryRetireImpossibleTreaty(action)) continue;
                    if (ReignServices.Agreement?.ReconcileObservedCampaignCancellation(action) == true) continue;
                    TryRestoreObservedPendingCampaignOrder(action);
                    TryCompleteObservedCampaignOrder(action);
                }
            }

            long nowTicks = System.DateTime.UtcNow.Ticks;
            long lastTicks = Interlocked.Read(
                ref _lastRealtimeActionPollUtcTicks);
            if (nowTicks - lastTicks >= RealtimeActionPollIntervalTicks
                && !_serverActionPollInFlight && !ReignSaveSyncCoordinator.IsAlignmentPending)
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
            if (dataStore.IsLoading)
            {
                PruneSaveBackedHistory();
                CaptureInheritedTerminalReports();
            }
        }

        // After the load prune (which keeps every pa_ receipt): a report captured or later, or
        // unreadable, existed in the loaded save. Offered/unproven records have no report.
        private void CaptureInheritedTerminalReports()
        {
            _inheritedTerminalReportPhase.Clear();
            foreach (ReignWorldActionRecord action in _actions)
            {
                if (string.IsNullOrEmpty(action?.TerminalReportJson)) continue;
                string phase = TerminalReportString(TerminalReportState(action), "phase");
                if (string.IsNullOrEmpty(phase) || phase == "offered" || phase == "unproven") continue;
                _inheritedTerminalReportPhase[action] = phase;
            }
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
                    // A held uncertain execution is never re-offered, re-reported or restarted.
                    if (IsUncertainExecutionHeld(existing)) continue;
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
                    _lastActionResults.TryGetValue(queued.ActionId, out ReignActionResult resultBefore);
                    try
                    {
                        ExecuteAction(queued, now);
                    }
                    catch (System.Exception ex)
                    {
                        ReignLog.Exception("Immediate dialogue action execution failed action=" + queued.ActionId
                            + " type=" + queued.Type, ex);
                        // Only this exact admitted record, and only when it entered Executing
                        // without a recorded result, becomes a durable non-runnable hold. No
                        // status, phase, attempt, report or terminal result is written; a
                        // terminal record or a result recorded by this attempt is kept as is.
                        bool recorded = _lastActionResults.TryGetValue(queued.ActionId, out ReignActionResult resultAfter)
                            && !ReferenceEquals(resultAfter, resultBefore);
                        if (ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.ShouldHoldAfterThrow(queued.Status.ToString(), recorded))
                        {
                            queued.FailureReason = ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionMarker;
                            queued.ExecuteAfterDay = float.MaxValue;
                            ReignLog.Warn("Immediate action held with an unknown native outcome action=" + queued.ActionId);
                        }
                        // Existing court, docket and agreement callers keep their exception semantics.
                        throw;
                    }
                }
            }

            return queuedCount;
        }

        // Presentation entry points only (accepted conversation replies and letters). The
        // import keeps its normal order; a throw is contained so the accepted reply survives.
        // heldActionId names only the exact row that became held during this call: a
        // conflicting replay, an unattempted row, a row with a recorded result and any
        // later unimported neighbor are never labelled, and earlier holds are never
        // attributed to this reply. Existing non-presentation callers keep the throw.
        internal bool TryImportForPresentation(IEnumerable<ReignWorldActionRecord> rows, out int queued, out string heldActionId)
        {
            queued = 0;
            heldActionId = null;
            List<ReignWorldActionRecord> list = rows?.Where(row => row != null).ToList() ?? new List<ReignWorldActionRecord>();
            if (list.Count == 0) return true;
            var heldBefore = new HashSet<string>(list.Where(row => IsUncertainExecutionHeld(_actions.FirstOrDefault(
                a => a != null && a.ActionId == row.ActionId))).Select(row => row.ActionId), StringComparer.Ordinal);
            try
            {
                queued = EnqueueAndExecuteServerActions(list);
                return true;
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Presented action import interrupted", ex);
                heldActionId = list.Select(row => row.ActionId).FirstOrDefault(id => !heldBefore.Contains(id)
                    && IsUncertainExecutionHeld(_actions.FirstOrDefault(a => a != null && a.ActionId == id)));
                return false;
            }
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
                _ = ReignPersonalGossipClient.TickPersonalGossipAsync();
            ReignTestHarness.Current.TickActionGauntletSuites();
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

            if (_serverActionPollInFlight || now - _lastServerActionPollDay < 0.25f
                || ReignSaveSyncCoordinator.IsAlignmentPending)
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
                List<ReignWorldActionRecord> actions = await ReignServerClient.FetchActionCommandsAsync(importFence).ConfigureAwait(false);
                if (actions == null || actions.Count == 0)
                {
                    return;
                }

                lock (_pendingServerActionsLock)
                {
                    _pendingServerActions.AddRange(actions);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Local server action poll failed: " + ex.Message);
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
                if (!(action?.ImportFence is ReignServerClient.RequestFence fence)
                    || !ReferenceEquals(Instance, this) || !fence.IsCurrentActionPoll())
                {
                    ReignLog.Warn("Discarded pending server action from an earlier loaded scope action=" + (action?.ActionId ?? ""));
                    continue;
                }
                var existing = _actions.FirstOrDefault(x => x != null && x.ActionId == action.ActionId);
                if (existing != null)
                {
                    if (existing.TermsHash != action.TermsHash || existing.Type != action.Type
                        || existing.ActorHeroStringId != action.ActorHeroStringId || existing.TargetHeroStringId != action.TargetHeroStringId)
                    { ReignLog.Warn("Rejected conflicting action replay " + action.ActionId); continue; }
                    if (IsUncertainExecutionHeld(existing)) continue;
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
