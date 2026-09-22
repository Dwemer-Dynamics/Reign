using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Shared;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using ReignBeta.Settings;
using ReignBeta.UI;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed class ReignRelationshipCampaignBehavior : CampaignBehaviorBase
    {
        public static ReignRelationshipCampaignBehavior Instance { get; private set; }

        private bool _tickInFlight;
        private float _lastTickDay = -1000f;
        private float _lastDirectorSnapshotDay = -1000f;
        private bool _directorSnapshotInFlight;
        private float _lastAmbientSnapshotDay = -1000f;
        private int _lastAmbientCapturedDay = -1000;
        private string _lastAmbientCapturedTimelineId = "";
        private bool _ambientSnapshotInFlight;
        private bool _ambientConfirmedCadenceInitialized;
        private bool _saveSyncResumePending;
        private bool _initialRelationshipBaselinesApplied;
        private readonly Queue<ReignLetter> _pendingMail = new Queue<ReignLetter>();
        private bool _mailInquiryOpen;
        private bool _standardMailInquiryOpen;
        private const int NativeSyncNormalMaxPerFrame = 256;
        private const int NativeSyncBacklogMaxPerFrame = 1024;
        private const int NativeSyncBacklogThreshold = 256;
        private const double NativeSyncNormalFrameBudgetMilliseconds = 5d;
        private const double NativeSyncBacklogFrameBudgetMilliseconds = 8d;
        private readonly object _nativeSyncLock = new object();
        private readonly Dictionary<string, ReignNativeRelationSyncTarget> _nativeSyncTargets =
            new Dictionary<string, ReignNativeRelationSyncTarget>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<string> _nativeSyncOrder = new Queue<string>();
        private string _nativeSyncPlanId = string.Empty;
        private string _nativeSyncTimelineId = "main";
        private int _nativeSyncApplied;
        private int _nativeSyncAlreadyAligned;
        private int _nativeSyncObsolete;
        private int _nativeSyncFailed;
        private long _nativeSyncDurationMs;
        private string _nativeSyncLastError = string.Empty;
        private readonly List<string> _nativeSyncObsoletePairKeys = new List<string>();
        private readonly List<ReignNativeRelationSyncFailure> _nativeSyncFailedPairs =
            new List<ReignNativeRelationSyncFailure>();
        private string _nativeSyncHeroCachePlanId = string.Empty;
        private Dictionary<string, Hero> _nativeSyncHeroCache =
            new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
        private readonly Queue<JObject> _nativeSyncReceipts = new Queue<JObject>();
        private bool _initializationProjectionAllowed;
        private int _directorActionPollInFlight;
        private long _lastDirectorActionPollUtcTicks;
        private static readonly long DirectorActionPollIntervalTicks =
            TimeSpan.FromSeconds(1d).Ticks;

        public void ApplicationTick(float deltaTime)
        {
            TryScheduleRealtimeDirectorActionPoll();
            OnTick(deltaTime);
        }

        private void TryScheduleRealtimeDirectorActionPoll()
        {
            if (ReignCampaignInitializationGate.IsPending
                || TaleWorlds.CampaignSystem.Campaign.Current == null
                || Hero.MainHero == null
                || ReignBetaSettings.Instance?.PassiveRelationshipDirectorEnabled == false)
                return;
            long now = DateTime.UtcNow.Ticks;
            long last = Interlocked.Read(ref _lastDirectorActionPollUtcTicks);
            if (now - last < DirectorActionPollIntervalTicks) return;
            if (Interlocked.CompareExchange(ref _directorActionPollInFlight, 1, 0) != 0)
                return;
            Interlocked.Exchange(ref _lastDirectorActionPollUtcTicks, now);
            _ = DrainRelationshipDirectorActionsAsync();
        }

        private async Task DrainRelationshipDirectorActionsAsync()
        {
            try
            {
                List<ReignDirectorAction> actions =
                    await ReignServerClient.PollRelationshipDirectorActionsAsync()
                        .ConfigureAwait(false);
                if (actions.Count == 0) return;
                JArray receipts = new JArray();
                await ReignMainThread.InvokeAsync(() =>
                {
                    foreach (ReignDirectorAction action in actions)
                        receipts.Add(ApplyDirectorAction(action));
                }).ConfigureAwait(false);
                await ReignServerClient.ReportRelationshipDirectorActionsAsync(receipts)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Real-time relationship director action poll failed: "
                    + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _directorActionPollInFlight, 0);
            }
        }

        public void MergeNativeSyncTargets(string timelineId, IEnumerable<ReignNativeRelationSyncTarget> targets)
        {
            if (targets == null) return;
            lock (_nativeSyncLock)
            {
                _nativeSyncTimelineId = string.IsNullOrWhiteSpace(timelineId) ? "main" : timelineId;
                bool added = false;
                foreach (ReignNativeRelationSyncTarget target in targets)
                {
                    if (target == null || string.IsNullOrWhiteSpace(target.PairKey)
                        || string.IsNullOrWhiteSpace(target.HeroAId)
                        || string.IsNullOrWhiteSpace(target.HeroBId))
                        continue;
                    if (_nativeSyncTargets.TryGetValue(target.PairKey, out ReignNativeRelationSyncTarget existing)
                        && existing.Revision >= target.Revision)
                        continue;
                    if (!_nativeSyncTargets.ContainsKey(target.PairKey))
                        _nativeSyncOrder.Enqueue(target.PairKey);
                    _nativeSyncTargets[target.PairKey] = target;
                    added = true;
                }
                if (added)
                {
                    _nativeSyncPlanId = "continuous_native_targets";
                    if (_nativeSyncHeroCache.Count == 0) _nativeSyncHeroCachePlanId = string.Empty;
                }
            }
        }

        public JArray DrainNativeSyncReceipts()
        {
            JArray receipts = new JArray();
            lock (_nativeSyncLock)
            {
                while (_nativeSyncReceipts.Count > 0 && receipts.Count < NativeSyncBacklogMaxPerFrame)
                    receipts.Add(_nativeSyncReceipts.Dequeue());
            }
            return receipts;
        }

        internal void RequeueNativeSyncReceipts(JArray receipts)
        {
            if (receipts == null || receipts.Count == 0) return;
            lock (_nativeSyncLock)
            {
                foreach (JObject receipt in receipts.OfType<JObject>())
                    _nativeSyncReceipts.Enqueue(receipt);
            }
        }

        internal async Task<ReignAmbientRelationshipRunResult> StartInitialRelationshipRunAsync(
            string generationId)
        {
            ReignAmbientRelationshipRunResult result =
                await ReignServerClient.SubmitCampaignOpeningRelationshipSeedAsync(
                    generationId).ConfigureAwait(false);
            if (!result.Ok)
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? "The initial relationship snapshot was rejected."
                        : result.Error);
            await ReignMainThread.InvokeAsync(() =>
            {
                float day = (float)CampaignTime.Now.ToDays;
                _lastAmbientSnapshotDay = day;
                _lastAmbientCapturedDay =
                    (int)Math.Floor(day + 0.000001f);
                _lastAmbientCapturedTimelineId =
                    ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                _lastDirectorSnapshotDay = day;
                _ambientConfirmedCadenceInitialized = true;
                ReplaceNativeSyncPlan(result.NativeSyncPlan);
            }).ConfigureAwait(false);
            return result;
        }

        internal void ProcessInitializationProjectionFrame()
        {
            _initializationProjectionAllowed = true;
            try
            {
                OnTick(0f);
            }
            finally
            {
                _initializationProjectionAllowed = false;
            }
        }

        internal ReignNativeRelationSyncProgress InitializationProgress()
        {
            return NativeSyncProgressSnapshot();
        }

        internal int PendingNativeReceiptCount
        {
            get
            {
                lock (_nativeSyncLock) return _nativeSyncReceipts.Count;
            }
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.OnNewGameCreatedPartialFollowUpEndEvent.AddNonSerializedListener(this, OnNewGameCreatedPartialFollowUpEnd);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignRelationship_lastTickDay", ref _lastTickDay);
            dataStore.SyncData("_reignRelationship_lastDirectorSnapshotDay", ref _lastDirectorSnapshotDay);
            dataStore.SyncData("_reignRelationship_lastAmbientSnapshotDay", ref _lastAmbientSnapshotDay);
            dataStore.SyncData("_reignRelationship_lastAmbientCapturedDay", ref _lastAmbientCapturedDay);
            dataStore.SyncData("_reignRelationship_lastAmbientCapturedTimelineId", ref _lastAmbientCapturedTimelineId);
            dataStore.SyncData("_reignRelationship_ambientConfirmedCadenceInitialized", ref _ambientConfirmedCadenceInitialized);
            dataStore.SyncData("_reignRelationship_initialBaselinesApplied", ref _initialRelationshipBaselinesApplied);
        }

        private void OnNewGameCreatedPartialFollowUpEnd(CampaignGameStarter starter)
        {
            // The readiness coordinator applies this idempotent baseline before
            // timeline draining and before gameplay is released.
        }

        internal void PrepareInitialBaselines()
        {
            // The server owns the opening baseline plus deterministic MBTI seed.
            // Keeping this save flag preserves old save compatibility without
            // writing an intermediate native relation before the final projection.
            _initialRelationshipBaselinesApplied = true;
        }

        private void ApplyInitialRelationshipBaselines()
        {
            if (_initialRelationshipBaselinesApplied) return;

            List<Hero> heroes = Hero.AllAliveHeroes
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.First())
                .ToList();
            Dictionary<string, StartingRelationshipPair> pairs =
                new Dictionary<string, StartingRelationshipPair>(StringComparer.OrdinalIgnoreCase);

            foreach (Hero hero in heroes)
            {
                AddStartingRelationshipPair(pairs, hero, hero.Spouse, true, false, false);
                AddStartingRelationshipPair(pairs, hero, hero.Father, false, true, false);
                AddStartingRelationshipPair(pairs, hero, hero.Mother, false, true, false);
            }

            foreach (IGrouping<string, Hero> siblings in heroes
                .SelectMany(hero => new[]
                {
                    new { ParentId = hero.Father?.StringId ?? string.Empty, Hero = hero },
                    new { ParentId = hero.Mother?.StringId ?? string.Empty, Hero = hero }
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.ParentId))
                .GroupBy(x => x.ParentId, x => x.Hero, StringComparer.OrdinalIgnoreCase))
            {
                List<Hero> children = siblings
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(x => x.First())
                    .ToList();
                for (int i = 0; i < children.Count; i++)
                    for (int j = i + 1; j < children.Count; j++)
                        AddStartingRelationshipPair(pairs, children[i], children[j], false, true, false);
            }

            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated))
            {
                Hero ruler = kingdom.Leader;
                if (ruler == null || !ruler.IsAlive) continue;
                foreach (Hero lord in heroes.Where(x => x != ruler && x.IsLord && x.Clan?.Kingdom == kingdom))
                    AddStartingRelationshipPair(pairs, lord, ruler, false, false, true);
            }

            int changed = 0;
            foreach (StartingRelationshipPair pair in pairs.Values)
            {
                int current = pair.First.GetRelation(pair.Second);
                if (current == pair.Target) continue;
                SetRelationWithoutGameplayRewards(pair.First, pair.Second, pair.Target);
                changed++;
            }

            _initialRelationshipBaselinesApplied = true;
            ReignLog.Info("Initial relationship baselines applied pairs=" + pairs.Count
                + " changed=" + changed
                + " spouses=" + pairs.Values.Count(x => x.Target == ReignRelationshipBaselinePolicy.SpouseBaseline)
                + " familyOrRuler=" + pairs.Values.Count(x => x.Target == ReignRelationshipBaselinePolicy.ImmediateFamilyBaseline)
                + ".");
        }

        private static void AddStartingRelationshipPair(
            Dictionary<string, StartingRelationshipPair> pairs,
            Hero first,
            Hero second,
            bool areSpouses,
            bool areImmediateFamily,
            bool isLordRulerPair)
        {
            if (first == null || second == null || first == second
                || !first.IsAlive || !second.IsAlive
                || string.IsNullOrWhiteSpace(first.StringId) || string.IsNullOrWhiteSpace(second.StringId)) return;

            int target = ReignRelationshipBaselinePolicy.ResolveStartingBaseline(
                areSpouses, areImmediateFamily, isLordRulerPair);
            if (target == ReignRelationshipBaselinePolicy.NoBaseline) return;

            bool firstComesFirst = string.Compare(first.StringId, second.StringId, StringComparison.OrdinalIgnoreCase) <= 0;
            Hero heroA = firstComesFirst ? first : second;
            Hero heroB = firstComesFirst ? second : first;
            string key = heroA.StringId + "|" + heroB.StringId;
            if (!pairs.TryGetValue(key, out StartingRelationshipPair existing) || target > existing.Target)
                pairs[key] = new StartingRelationshipPair(heroA, heroB, target);
        }

        private sealed class StartingRelationshipPair
        {
            public StartingRelationshipPair(Hero first, Hero second, int target)
            {
                First = first;
                Second = second;
                Target = target;
            }

            public Hero First { get; }
            public Hero Second { get; }
            public int Target { get; }
        }

        private void OnTick(float deltaTime)
        {
            if (ReignCampaignInitializationGate.IsPending && !_initializationProjectionAllowed) return;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            string currentPlanId;
            int queuedTargetCount;
            lock (_nativeSyncLock)
            {
                if (_nativeSyncTargets.Count == 0) return;
                currentPlanId = _nativeSyncPlanId;
                queuedTargetCount = _nativeSyncTargets.Count;
            }
            if (!string.Equals(_nativeSyncHeroCachePlanId, currentPlanId, StringComparison.OrdinalIgnoreCase))
            {
                _nativeSyncHeroCache = Hero.AllAliveHeroes
                    .Where(x => x != null && !string.IsNullOrWhiteSpace(x.StringId))
                    .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
                _nativeSyncHeroCachePlanId = currentPlanId;
            }
            Stopwatch timer = Stopwatch.StartNew();
            int processed = 0;
            bool backlogMode = queuedTargetCount >= NativeSyncBacklogThreshold;
            int maxPerFrame = backlogMode
                ? NativeSyncBacklogMaxPerFrame
                : NativeSyncNormalMaxPerFrame;
            double frameBudgetMilliseconds = backlogMode
                ? NativeSyncBacklogFrameBudgetMilliseconds
                : NativeSyncNormalFrameBudgetMilliseconds;
            while (processed < maxPerFrame
                && timer.Elapsed.TotalMilliseconds < frameBudgetMilliseconds)
            {
                ReignNativeRelationSyncTarget target;
                string planId;
                lock (_nativeSyncLock)
                {
                    target = null;
                    while (_nativeSyncOrder.Count > 0 && target == null)
                    {
                        string pairKey = _nativeSyncOrder.Dequeue();
                        if (_nativeSyncTargets.TryGetValue(pairKey, out target))
                            _nativeSyncTargets.Remove(pairKey);
                    }
                    if (target == null) break;
                    planId = _nativeSyncPlanId;
                }

                bool applied = false, alreadyAligned = false, obsolete = false;
                int observedRelation = 0;
                string error = string.Empty;
                try
                {
                    _nativeSyncHeroCache.TryGetValue(target.HeroAId, out Hero actor);
                    _nativeSyncHeroCache.TryGetValue(target.HeroBId, out Hero other);
                    if (actor == null || other == null)
                    {
                        obsolete = true;
                    }
                    else
                    {
                        observedRelation = actor.GetRelation(other);
                        if (observedRelation == target.TargetRelation)
                        {
                            alreadyAligned = true;
                        }
                        else
                        {
                            SetRelationWithoutGameplayRewards(actor, other, target.TargetRelation);
                            observedRelation = actor.GetRelation(other);
                            if (observedRelation != target.TargetRelation)
                                throw new InvalidOperationException("Bannerlord did not retain the projected native relation.");
                            applied = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                lock (_nativeSyncLock)
                {
                    // A newer daily plan replaces the entire old generation. If
                    // it arrived while this one target was being applied, its
                    // queue contains the authoritative replacement and these
                    // stale counters must not be attributed to the new plan.
                    if (!string.Equals(planId, _nativeSyncPlanId, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (applied) _nativeSyncApplied++;
                    else if (alreadyAligned) _nativeSyncAlreadyAligned++;
                    else if (obsolete)
                    {
                        _nativeSyncObsolete++;
                        if (_nativeSyncObsoletePairKeys.Count < 1000)
                            _nativeSyncObsoletePairKeys.Add(target.PairKey);
                    }
                    else
                    {
                        _nativeSyncFailed++;
                        _nativeSyncLastError = error;
                        if (_nativeSyncFailedPairs.Count < 250)
                            _nativeSyncFailedPairs.Add(new ReignNativeRelationSyncFailure
                            {
                                PairKey = target.PairKey,
                                Error = error
                            });
                    }
                    _nativeSyncReceipts.Enqueue(new JObject
                    {
                        ["pairKey"] = target.PairKey,
                        ["revision"] = target.Revision,
                        ["status"] = applied ? "applied" : alreadyAligned ? "already_aligned" : obsolete ? "obsolete" : "failed",
                        ["observedRelation"] = applied || alreadyAligned ? target.TargetRelation : observedRelation,
                        ["error"] = error ?? string.Empty
                    });
                }
                processed++;
            }
            timer.Stop();
            if (processed > 0)
            {
                lock (_nativeSyncLock)
                    _nativeSyncDurationMs += timer.ElapsedMilliseconds;
            }
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (Hero.MainHero == null) return;
            float day = (float)CampaignTime.Now.ToDays;
            TrySchedulePassiveRelationshipSnapshots(day);
            if (_tickInFlight) return;
            if (day - _lastTickDay < 0.2f) return;
            _lastTickDay = day;
            _tickInFlight = true;
            _ = PollAsync();
        }

        private async Task PollAsync()
        {
            try
            {
                ReignCorrespondenceTickResult result = await ReignServerClient.TickCorrespondenceAsync().ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    foreach (ReignNativeRelationChange change in result.NativeRelationChanges ?? new List<ReignNativeRelationChange>())
                    {
                        Hero subject = FindHero(change.SubjectId);
                        Hero target = FindHero(change.TargetId);
                        if (subject != null && target != null && change.Delta != 0)
                        {
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(subject, target, change.Delta, true);
                        }
                    }
                    foreach (ReignLetter letter in result.DeliveredLetters ?? new List<ReignLetter>()) _pendingMail.Enqueue(letter);
                    if (result.QueuedActions != null && result.QueuedActions.Count > 0)
                    {
                        int queued = ReignAICampaignBehavior.Instance?.EnqueueAndExecuteServerActions(result.QueuedActions) ?? 0;
                        ReignLog.Info("Immediate correspondence action import actions=" + result.QueuedActions.Count + " queued=" + queued);
                    }
                    TryShowMailInquiry();
                }).ConfigureAwait(false);
                ReignNativeRelationSyncProgress nativeProgress = NativeSyncProgressSnapshot();
                if (nativeProgress != null)
                    await ReignServerClient.ReportNativeRelationSyncProgressAsync(nativeProgress).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Relationship/correspondence campaign poll failed: " + ex.Message);
            }
            finally
            {
                _tickInFlight = false;
            }
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (Hero.MainHero == null) return;
            float day = (float)CampaignTime.Now.ToDays;
            TrySchedulePassiveRelationshipSnapshots(day);
        }

        private void TrySchedulePassiveRelationshipSnapshots(float day)
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (ReignBetaSettings.Instance?.PassiveRelationshipDirectorEnabled == false) return;
            if (!_ambientConfirmedCadenceInitialized)
            {
                // Older builds persisted the attempt day before the server accepted
                // the snapshot. That makes a failed upload look current after loading
                // the save and suppresses recovery. Invalidate it once; all subsequent
                // cadence timestamps represent confirmed server success only.
                _lastAmbientSnapshotDay = -1000f;
                _ambientConfirmedCadenceInitialized = true;
                ReignLog.Info("Relationship producer migrated to confirmed-success cadence; an immediate ambient snapshot will be scheduled.");
            }
            if (ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                ScheduleAfterSaveSyncAlignment();
                return;
            }
            int dayKey = (int)Math.Floor(day + 0.000001f);
            string currentTimelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            if (!currentTimelineId.Equals(_lastAmbientCapturedTimelineId, StringComparison.OrdinalIgnoreCase))
            {
                _lastAmbientCapturedDay = -1000;
                _lastAmbientCapturedTimelineId = currentTimelineId;
            }
            if (ReignBetaSettings.Instance?.AmbientRelationshipDriftEnabled != false
                && dayKey > _lastAmbientCapturedDay)
            {
                try
                {
                    ReignServerClient.CaptureAmbientRelationshipDailyInput();
                    _lastAmbientCapturedDay = dayKey;
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Ambient relationship daily input capture failed: " + ex.Message);
                }
            }
            bool ambientEnabled = ReignBetaSettings.Instance?.AmbientRelationshipDriftEnabled != false;
            bool ambientHasConfirmedRun = _lastAmbientSnapshotDay > -900f;
            bool ambientDue = ambientEnabled && day - _lastAmbientSnapshotDay >= 0.9f;
            bool directorDue = day - _lastDirectorSnapshotDay >= 6.9f;

            // The initial ambient snapshot establishes authoritative Reign
            // affinity before the political-marriage director evaluates the
            // world. After that first confirmed run, a due weekly director gets
            // priority. At high campaign speed the daily request can span more
            // than one game day; starting it first on every completion used to
            // starve arranged-marriage rolls forever.
            if (!_ambientSnapshotInFlight && !_directorSnapshotInFlight
                && directorDue && (!ambientEnabled || ambientHasConfirmedRun))
            {
                _directorSnapshotInFlight = true;
                _ = SubmitDirectorSnapshotAsync(day);
                return;
            }

            if (!_ambientSnapshotInFlight && !_directorSnapshotInFlight && ambientDue)
            {
                _ambientSnapshotInFlight = true;
                _ = SubmitAmbientSnapshotAsync(day);
                return;
            }

            if (!_ambientSnapshotInFlight && !_directorSnapshotInFlight && directorDue)
            {
                _directorSnapshotInFlight = true;
                _ = SubmitDirectorSnapshotAsync(day);
            }
        }

        private void ScheduleAfterSaveSyncAlignment()
        {
            if (_saveSyncResumePending) return;
            _saveSyncResumePending = true;
            ReignSaveSyncCoordinator.RunAfterCurrentAlignmentOnMainThread(() =>
            {
                if (!ReferenceEquals(Instance, this) || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
                _saveSyncResumePending = false;
                ReignLog.Info("Save Sync alignment completed; resuming deferred passive relationship producers.");
                TrySchedulePassiveRelationshipSnapshots((float)CampaignTime.Now.ToDays);
            });
        }

        private async Task SubmitAmbientSnapshotAsync(float scheduledDay)
        {
            try
            {
                ReignAmbientRelationshipRunResult result = await ReignServerClient.SubmitAmbientRelationshipSnapshotAsync().ConfigureAwait(false);
                if (!result.Ok)
                {
                    ReignLog.Warn("Ambient relationship drift failed: "
                        + (!string.IsNullOrWhiteSpace(result.Error) ? result.Error : "The server did not accept the snapshot."));
                }
                else
                {
                    _lastAmbientSnapshotDay = scheduledDay;
                    ReplaceNativeSyncPlan(result.NativeSyncPlan);
                    ReignLog.Info("Ambient relationship drift run=" + result.RunId + " pairs=" + result.ProcessedPairs + " directions=" + result.ChangedDirections + " nativeQueued=" + result.NativeChangesQueued + " ms=" + result.DurationMs + ".");
                }
            }
            catch (Exception ex) { ReignLog.Warn("Ambient relationship drift failed: " + ex.Message); }
            finally { _ambientSnapshotInFlight = false; }
        }

        private void ReplaceNativeSyncPlan(ReignNativeRelationSyncPlan plan)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.PlanId)) return;
            lock (_nativeSyncLock)
            {
                if (string.Equals(_nativeSyncPlanId, plan.PlanId, StringComparison.OrdinalIgnoreCase))
                    return;
                _nativeSyncTargets.Clear();
                _nativeSyncOrder.Clear();
                _nativeSyncPlanId = plan.PlanId;
                _nativeSyncTimelineId = string.IsNullOrWhiteSpace(plan.TimelineId) ? "main" : plan.TimelineId;
                _nativeSyncApplied = 0;
                _nativeSyncAlreadyAligned = 0;
                _nativeSyncObsolete = 0;
                _nativeSyncFailed = 0;
                _nativeSyncDurationMs = 0;
                _nativeSyncLastError = string.Empty;
                _nativeSyncHeroCachePlanId = string.Empty;
                _nativeSyncObsoletePairKeys.Clear();
                _nativeSyncFailedPairs.Clear();
                foreach (ReignNativeRelationSyncTarget target in plan.Targets)
                {
                    if (target == null || string.IsNullOrWhiteSpace(target.PairKey)
                        || string.IsNullOrWhiteSpace(target.HeroAId)
                        || string.IsNullOrWhiteSpace(target.HeroBId))
                        continue;
                    if (!_nativeSyncTargets.ContainsKey(target.PairKey))
                        _nativeSyncOrder.Enqueue(target.PairKey);
                    _nativeSyncTargets[target.PairKey] = target;
                }
                ReignLog.Info("Native relationship bulk-sync plan=" + _nativeSyncPlanId
                    + " targets=" + _nativeSyncTargets.Count + ".");
            }
        }

        private ReignNativeRelationSyncProgress NativeSyncProgressSnapshot()
        {
            lock (_nativeSyncLock)
            {
                if (string.IsNullOrWhiteSpace(_nativeSyncPlanId)) return null;
                ReignNativeRelationSyncProgress progress = new ReignNativeRelationSyncProgress
                {
                    PlanId = _nativeSyncPlanId,
                    TimelineId = _nativeSyncTimelineId,
                    AppliedCount = _nativeSyncApplied,
                    AlreadyAlignedCount = _nativeSyncAlreadyAligned,
                    ObsoleteCount = _nativeSyncObsolete,
                    FailedCount = _nativeSyncFailed,
                    RemainingCount = _nativeSyncTargets.Count,
                    DurationMs = _nativeSyncDurationMs,
                    LastError = _nativeSyncLastError
                };
                progress.ObsoletePairKeys.AddRange(_nativeSyncObsoletePairKeys);
                foreach (ReignNativeRelationSyncFailure failure in _nativeSyncFailedPairs)
                    progress.FailedPairs.Add(new ReignNativeRelationSyncFailure
                    {
                        PairKey = failure.PairKey,
                        Error = failure.Error
                    });
                return progress;
            }
        }

        private async Task SubmitDirectorSnapshotAsync(float scheduledDay)
        {
            try
            {
                ReignDirectorRunResult result = await ReignServerClient.SubmitNobleRelationshipSnapshotAsync().ConfigureAwait(false);
                if (!result.Ok)
                {
                    ReignLog.Warn("Passive relationship director failed: "
                        + (!string.IsNullOrWhiteSpace(result.Error) ? result.Error : "The server did not accept the snapshot."));
                    return;
                }
                _lastDirectorSnapshotDay = scheduledDay;
                ReignLog.Info("Passive relationship director run=" + result.RunId + " candidates=" + result.CandidateCount + " events=" + result.EventCount + " skipped=" + result.Skipped + " reason=" + result.Reason + ".");
            }
            catch (Exception ex) { ReignLog.Warn("Passive relationship director failed: " + ex.Message); }
            finally { _directorSnapshotInFlight = false; }
        }

        private static bool IsObsoleteManagedConceptionError(string error,
            Hero mother, Hero biologicalFather)
        {
            return string.Equals(error, "Mother is already pregnant.",
                    StringComparison.OrdinalIgnoreCase)
                || mother == null
                || biologicalFather == null
                || !mother.IsAlive
                || !biologicalFather.IsAlive
                || mother.Age > 45f;
        }

        private JObject ApplyDirectorAction(ReignDirectorAction action)
        {
            if (action == null) return new JObject();
            string status = "completed", error = string.Empty;
            try
            {
                if (action.ActionType == "native_relation")
                {
                    Hero actor = FindHero(action.ActorId), target = FindHero(action.TargetId);
                    int delta = action.Payload?.Value<int?>("delta") ?? 0;
                    int? targetRelation = action.Payload?.Value<int?>("targetNativeRelation");
                    if (actor == null || target == null)
                    {
                        // A pair can disappear between a server snapshot and the
                        // next native poll because of death, save branching, or a
                        // mod removing a hero. It is no longer an actionable error.
                        status = "obsolete";
                        error = "One or both heroes are no longer present in the loaded campaign.";
                    }
                    else if (targetRelation.HasValue)
                    {
                        SetRelationWithoutGameplayRewards(actor, target,
                            Math.Max(-100, Math.Min(100, targetRelation.Value)));
                    }
                    else if (delta != 0)
                    {
                        SetRelationWithoutGameplayRewards(actor, target,
                            actor.GetRelation(target) + Math.Max(-100, Math.Min(100, delta)));
                    }
                }
                else if (action.ActionType == "start_conception")
                {
                    Hero mother = FindHero(action.Payload?.Value<string>("motherId"));
                    Hero bio = FindHero(action.Payload?.Value<string>("biologicalFatherId"));
                    Hero legal = FindHero(action.Payload?.Value<string>("legalFatherId")) ?? bio;
                    if (ReignFamilyCampaignBehavior.Instance == null)
                        throw new InvalidOperationException("Reign family campaign behavior is unavailable.");
                    if (!ReignFamilyCampaignBehavior.Instance.TryStartManagedConception(
                            action.Payload?.Value<string>("conceptionId"), mother, bio, legal,
                            action.Payload?.Value<float?>("conceptionDay") ?? (float)CampaignTime.Now.ToDays,
                            action.Payload?.Value<float?>("dueDay") ?? (float)CampaignTime.Now.ToDays + 36f,
                            action.Payload?.Value<float?>("secrecy") ?? 0f,
                            action.Payload?.Value<bool?>("playerInvolved") == true, out error))
                    {
                        if (IsObsoleteManagedConceptionError(error, mother, bio))
                            status = "obsolete";
                        else throw new InvalidOperationException(error);
                    }
                }
                else if (action.ActionType == "marriage")
                {
                    Hero first = FindHero(action.ActorId), second = FindHero(action.TargetId);
                    if (first == null || second == null)
                    {
                        status = "obsolete";
                        error = "One or both marriage candidates are no longer present in the loaded campaign.";
                    }
                    else if (first == Hero.MainHero || second == Hero.MainHero)
                    {
                        status = "invalid";
                        error = "Automatic NPC marriage actions cannot include the player.";
                    }
                    else
                    {
                        if (first.Spouse != null || second.Spouse != null)
                        {
                            status = "obsolete";
                            error = "A marriage candidate married before this action was applied.";
                        }
                        else if (TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel == null
                            || !TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel
                                .IsCoupleSuitableForMarriage(first, second))
                        {
                            status = "invalid";
                            error = "Native marriage model rejected the couple.";
                        }
                        else
                        {
                            MarriageAction.Apply(first, second, true);
                            if (first.Spouse != second || second.Spouse != first)
                            {
                                // Bannerlord can reject an otherwise non-throwing
                                // MarriageAction. Never report that silent no-op as
                                // completed, and roll back a possible one-sided link.
                                if (first.Spouse == second) first.Spouse = null;
                                if (second.Spouse == first) second.Spouse = null;
                                status = "invalid";
                                error = "Native marriage action did not establish reciprocal spouse state.";
                            }
                        }
                    }
                }
                else if (action.ActionType == "divorce")
                {
                    Hero first = FindHero(action.ActorId), second = FindHero(action.TargetId);
                    if (first == null || second == null || first == Hero.MainHero || second == Hero.MainHero || first.Spouse != second) throw new InvalidOperationException("Divorce action could not resolve the staged NPC marriage.");
                    first.Spouse = null;
                    second.Spouse = null;
					// MarriageAction leaves its MatchMadeByFamily/Marriage romance
					// record behind when Reign performs a direct NPC divorce. That
					// stale record made both former spouses appear to remain in an
					// active courtship and distorted later marriage selection.
					if (Romance.RomanticStateList != null)
					{
						foreach (Romance.RomanticState romance in Romance.RomanticStateList
							.Where(x => x != null
								&& ((x.Person1 == first && x.Person2 == second)
									|| (x.Person1 == second && x.Person2 == first))).ToList())
							Romance.RomanticStateList.Remove(romance);
					}
                }
                else if (action.ActionType == "reveal_parentage")
                {
                    if (ReignFamilyCampaignBehavior.Instance == null || !ReignFamilyCampaignBehavior.Instance.RevealBiologicalFather(action.Payload?.Value<string>("childId"), out error)) throw new InvalidOperationException(error);
                }
                else throw new InvalidOperationException("Unknown relationship director action: " + action.ActionType);
            }
            catch (Exception ex) { status = "failed"; error = ex.Message; ReignLog.Warn("Relationship director action failed: " + ex.Message); }
            return new JObject
            {
                ["directorActionId"] = action.DirectorActionId ?? string.Empty,
                ["status"] = status,
                ["error"] = error,
				["worldDay"] = CampaignTime.Now.ToDays,
				["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"
            };
        }

        private void TryShowMailInquiry()
        {
            if (_mailInquiryOpen || _pendingMail.Count == 0) return;
            ReignLetter letter = _pendingMail.Dequeue();
            Hero sender = FindHero(letter.SenderId);
            _mailInquiryOpen = true;
#if !REIGN_EXCLUDE_COURT
            if (ReignCourtCampaignBehavior.Instance?.TryHandleRegentLetter(letter, () =>
            {
                _mailInquiryOpen = false;
                _standardMailInquiryOpen = false;
                TryShowMailInquiry();
            }) == true) return;
#endif
            _standardMailInquiryOpen = true;
            InformationManager.ShowInquiry(new InquiryData(
                "A Letter Has Arrived",
                "A letter has arrived from " + (!string.IsNullOrWhiteSpace(letter.SenderName) ? letter.SenderName : sender?.Name?.ToString() ?? "an unknown sender") + ".",
                true,
                true,
                "Read Letter",
                "Not Now",
                () =>
                {
                    _mailInquiryOpen = false;
                    _standardMailInquiryOpen = false;
                    ReignCorrespondenceScreenManager.Open(sender);
                    TryShowMailInquiry();
                },
                () =>
                {
                    _mailInquiryOpen = false;
                    _standardMailInquiryOpen = false;
                    TryShowMailInquiry();
                },
                string.Empty,
                0f,
                null,
                null), true);
        }

        public static bool TryAcknowledgeMailInquiryForLiveHarness()
        {
            ReignRelationshipCampaignBehavior behavior = Instance;
            if (behavior == null || !behavior._mailInquiryOpen
                || !behavior._standardMailInquiryOpen)
                return false;
            InformationManager.HideInquiry();
            behavior._mailInquiryOpen = false;
            behavior._standardMailInquiryOpen = false;
            behavior.TryShowMailInquiry();
            return true;
        }

        private static void SetRelationWithoutGameplayRewards(Hero first, Hero second, int target)
        {
            if (first == null || second == null || first == second || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            TaleWorlds.CampaignSystem.Campaign.Current.Models.DiplomacyModel.GetHeroesForEffectiveRelation(first, second, out Hero effectiveFirst, out Hero effectiveSecond);
            if (effectiveFirst == null || effectiveSecond == null || effectiveFirst == effectiveSecond) return;
            effectiveFirst.SetPersonalRelation(effectiveSecond, Math.Max(-100, Math.Min(100, target)));
        }

        private static Hero FindHero(string id)
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null && string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
