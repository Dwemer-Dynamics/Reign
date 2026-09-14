#nullable disable
using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.UI;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.Campaign
{
    /// <summary>
    /// Owns the one blocking preparation pass for a campaign generation. Only
    /// authoritative native/server state lives here; derivative work remains
    /// behind the gate and may continue after gameplay is released.
    /// </summary>
    public sealed class ReignCampaignPreparationCampaignBehavior : CampaignBehaviorBase
    {
        internal const float AutonomousWorldStartupGraceDays = 5f;
        private readonly object _stateLock = new object();
        private string _generationId;
        private ReignCampaignReadinessState _state;
        private Task _pipelineTask;
        private bool _sessionLaunched;
        private bool _loadedCampaign;
        private bool _sealedForCurrentCampaign;
        private float _mapScreenStableSeconds;
        private string _relationshipPlanId = string.Empty;
        private string _statusTitle = "Preparing Bannerlord Reign";
        private string _statusDetail = "Waiting for the campaign map to finish opening.";
        private string _statusProgress = string.Empty;
        private string _lastError = string.Empty;

        // This is the complete persisted readiness journal. Do not add large
        // snapshots or response bodies here.
        private int _sealVersion;
        private int _narrativeVersion;
        internal int NarrativeVersion => _narrativeVersion;
        private string _sealedCampaignId = string.Empty;
        private string _sealedTimelineId = string.Empty;
        private int _completedStageMask;
        private long _sealedHistorySequence;
        private float _autonomousWorldOriginDay = -1f;

        public static ReignCampaignPreparationCampaignBehavior Instance { get; private set; }
        internal bool IsSealedForCurrentCampaign => _sealedForCurrentCampaign;

        public ReignCampaignPreparationCampaignBehavior()
        {
            Instance = this;
            BeginGeneration();
        }

        public override void RegisterEvents()
        {
            CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, OnGameLoaded);
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reignInitialization_sealVersion", ref _sealVersion);
            dataStore.SyncData("_reignInitialization_narrativeVersion", ref _narrativeVersion);
            dataStore.SyncData("_reignInitialization_campaignId", ref _sealedCampaignId);
            dataStore.SyncData("_reignInitialization_timelineId", ref _sealedTimelineId);
            dataStore.SyncData("_reignInitialization_completedStageMask", ref _completedStageMask);
            dataStore.SyncData("_reignInitialization_historySequence", ref _sealedHistorySequence);
            dataStore.SyncData("_reignInitialization_autonomousWorldOriginDay",
                ref _autonomousWorldOriginDay);
        }

        internal void ApplicationTick(float deltaSeconds)
        {
            if (!_sessionLaunched || _sealedForCurrentCampaign) return;
            bool campaignAvailable =
                TaleWorlds.CampaignSystem.Campaign.Current != null;
            bool playerAvailable = CharacterObject.PlayerCharacter != null;
            if (!campaignAvailable || !playerAvailable) return;

            ReignNotableGenerationPopupManager.RequireUntilReady();
            string screenName = ScreenManager.TopScreen?.GetType().FullName ?? string.Empty;
            bool campaignMapVisible =
                screenName.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) >= 0;
            if (campaignMapVisible || ReignNotableGenerationPopupManager.IsOpen)
            {
                _mapScreenStableSeconds += Math.Max(0f, deltaSeconds);
                ReignNotableGenerationPopupManager.Show();
            }
            ReignNotableGenerationPopupManager.UpdateStatus(
                _statusTitle,
                _statusDetail,
                _statusProgress,
                !string.IsNullOrWhiteSpace(_lastError),
                Retry);

            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            bool saveSyncReady =
                ReignSaveSyncCoordinator.IsReadyForCampaign(campaignId);
            if (!saveSyncReady)
            {
                SetStatus(
                    "Aligning Save Sync",
                    "Restoring the matching server state for this Bannerlord save.",
                    string.Empty);
                return;
            }

            if (!ReignCampaignPreparationStartPolicy.CanStart(
                _sessionLaunched,
                campaignAvailable,
                playerAvailable,
                saveSyncReady,
                _pipelineTask != null))
                return;

            InitializeReadinessState(campaignId);
            _pipelineTask = ReignCampaignInitializationGate.RunInitializationRequestAsync(
                _generationId,
                RunPipelineAsync);
        }

        internal JObject Snapshot()
        {
            lock (_stateLock)
            {
                JObject snapshot = _state?.Snapshot() ?? new JObject
                {
                    ["generationId"] = _generationId,
                    ["requiredStage"] = "CampaignStart"
                };
                snapshot["sealedForCurrentCampaign"] = _sealedForCurrentCampaign;
                snapshot["relationshipPlanId"] = _relationshipPlanId;
                snapshot["statusTitle"] = _statusTitle;
                snapshot["statusDetail"] = _statusDetail;
                snapshot["statusProgress"] = _statusProgress;
                snapshot["error"] = _lastError;
                return snapshot;
            }
        }

        internal static bool IsCurrentCampaignSealed()
        {
            return Instance?.IsSealedForCurrentCampaign == true
                && !ReignCampaignInitializationGate.IsPending;
        }

        internal static float AutonomousWorldOriginDay
            => Instance?._autonomousWorldOriginDay ?? -1f;

        internal static bool AreAutonomousWorldSystemsUnlocked(float worldDay)
        {
            float origin = AutonomousWorldOriginDay;
            // Saves made before this policy remain compatible and unlocked.
            return origin < 0f
                || worldDay + 0.0001f >= origin + AutonomousWorldStartupGraceDays;
        }

        internal static JObject AutonomousWorldStartupGraceSnapshot(float worldDay)
        {
            float origin = AutonomousWorldOriginDay;
            bool hasOrigin = origin >= 0f;
            float unlockDay = hasOrigin
                ? origin + AutonomousWorldStartupGraceDays : worldDay;
            return new JObject
            {
                ["policy"] = "reign_autonomous_world_day_5",
                ["originWorldDay"] = hasOrigin ? origin : -1f,
                ["unlockWorldDay"] = unlockDay,
                ["elapsedCampaignDays"] = hasOrigin
                    ? Math.Max(0f, worldDay - origin)
                    : AutonomousWorldStartupGraceDays,
                ["graceDays"] = AutonomousWorldStartupGraceDays,
                ["locked"] = !AreAutonomousWorldSystemsUnlocked(worldDay),
                ["legacyCompatible"] = !hasOrigin
            };
        }

        private void OnNewGameCreated(CampaignGameStarter starter)
        {
            _narrativeVersion = 5;
            _loadedCampaign = false;
            _autonomousWorldOriginDay = CurrentCampaignDay();
            BeginGeneration();
        }

        private void OnGameLoaded(CampaignGameStarter starter)
        {
            _loadedCampaign = true;
            if (_autonomousWorldOriginDay < 0f)
            {
                // Do not impose a new five-day freeze on saves created before
                // the campaign-origin field existed.
                _autonomousWorldOriginDay = CurrentCampaignDay()
                    - AutonomousWorldStartupGraceDays;
            }
            BeginGeneration();
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (!_loadedCampaign && _autonomousWorldOriginDay < 0f)
                _autonomousWorldOriginDay = CurrentCampaignDay();
            _sessionLaunched = true;
            SetStatus(
                "Preparing Bannerlord Reign",
                "Waiting for the campaign map to finish opening.",
                string.Empty);
        }

        private static float CurrentCampaignDay()
        {
            return TaleWorlds.CampaignSystem.Campaign.Current == null
                ? 0f : (float)CampaignTime.Now.ToDays;
        }

        private void BeginGeneration()
        {
            _generationId = Guid.NewGuid().ToString("N");
            _state = null;
            _pipelineTask = null;
            _sessionLaunched = false;
            _sealedForCurrentCampaign = false;
            _mapScreenStableSeconds = 0f;
            _relationshipPlanId = string.Empty;
            _lastError = string.Empty;
            ReignCampaignInitializationGate.BeginGeneration(_generationId);
        }

        private void InitializeReadinessState(string campaignId)
        {
            if (_state != null) return;
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? string.Empty;
            bool matchingSeal =
                _sealVersion >= ReignCampaignReadinessState.CurrentSealVersion
                && string.Equals(_sealedCampaignId, campaignId, StringComparison.Ordinal)
                && string.Equals(_sealedTimelineId, timelineId, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(timelineId);
            bool priorCompatibleSeal = !matchingSeal
                && _sealVersion == 2
                && string.Equals(_sealedCampaignId, campaignId,
                    StringComparison.Ordinal)
                && string.Equals(_sealedTimelineId, timelineId,
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(timelineId);
            ReignInitializationStageMask mask;
            if (matchingSeal)
            {
                mask = (ReignInitializationStageMask)_completedStageMask;
            }
            else if (priorCompatibleSeal)
            {
                // Seal v2 used bit 7 for acknowledgement. In v3 that bit is
                // InitialSocialWorld, so never cast the old mask directly.
                // Preserve every expensive completed preparation stage and run
                // only the new social-world compatibility audit.
                mask = ReignInitializationStageMask.Personalities
                    | ReignInitializationStageMask.NativeFoundations
                    | ReignInitializationStageMask.Timeline
                    | ReignInitializationStageMask.Identity
                    | ReignInitializationStageMask.AuthoritativeSnapshots
                    | ReignInitializationStageMask.Relationships
                    | ReignInitializationStageMask.CriticalDrain;
            }
            else
            {
                mask = ReignInitializationStageMask.None;
            }
            if (!matchingSeal
                && ReignCharacterEditorCampaignBehavior.Instance?.NotableBackgroundInitializationComplete == true)
            {
                mask |= ReignInitializationStageMask.Personalities;
            }

            ReignInitializationJournal journal = new ReignInitializationJournal
            {
                SealVersion = matchingSeal ? _sealVersion : 0,
                CampaignId = matchingSeal || priorCompatibleSeal
                    ? _sealedCampaignId : campaignId,
                TimelineId = matchingSeal || priorCompatibleSeal
                    ? _sealedTimelineId : timelineId,
                CompletedStageMask = mask,
                SealedHistorySequence = matchingSeal ? _sealedHistorySequence : 0L
            };
            lock (_stateLock)
            {
                _state = matchingSeal || mask != ReignInitializationStageMask.None || _loadedCampaign
                    ? ReignCampaignReadinessState.Resume(journal, campaignId, _generationId)
                    : ReignCampaignReadinessState.Start(campaignId, _generationId);
            }
            ReignLog.Info("Campaign preparation generation=" + _generationId
                + " campaign=" + campaignId
                + " mode=" + (matchingSeal ? "sealed"
                    : priorCompatibleSeal || _loadedCampaign
                        ? "compatibility_audit" : "fresh")
                + " stage=" + _state.RequiredStage + ".");
        }

        private async Task RunPipelineAsync()
        {
            try
            {
#if !REIGN_EXCLUDE_COURT
                SetStatus("Restoring Noble Houses",
                    "Moving the existing court families into their independent landless clans.",
                    string.Empty);
                await ReignMainThread.InvokeAsync(() =>
                    Require(
                        ReignCourtNobleCampaignBehavior.Instance,
                        "court-house migration")
                    .EnsureHouseholdClansMigrated()).ConfigureAwait(false);
#endif
                if (_state.RequiresTimelineHandshake)
                {
                    SetStatus("Reopening Campaign Timeline",
                        "Restoring durable World History and pending social processing.",
                        string.Empty);
                    ReignWorldHistoryCampaignBehavior history =
                        Require(ReignWorldHistoryCampaignBehavior.Instance,
                            "world-history timeline");
                    string previousTimeline = _state.TimelineId;
                    JObject opened = await history
                        .EnsureTimelineReadyAsync(_generationId)
                        .ConfigureAwait(false);
                    string timeline =
                        opened?.Value<string>("timelineId") ?? history.TimelineId;
                    if (!_state.AcceptTimelineHandshake(_generationId, timeline))
                    {
                        throw new InvalidOperationException(
                            "The world-history timeline handshake was stale or empty.");
                    }
                    PersistJournal();
                    if (!string.Equals(
                        previousTimeline,
                        timeline,
                        StringComparison.Ordinal))
                    {
                        ReignLog.Info(
                            "Campaign timeline changed during load from "
                            + previousTimeline + " to " + timeline
                            + "; downstream readiness will be rebuilt.");
                    }
                }

                if (_state.CanRelease)
                {
                    await ReleaseCampaignAsync().ConfigureAwait(false);
                    return;
                }

                if (_state.RequiredStage == ReignInitializationStage.Personalities)
                {
                    SetStatus("Preparing Personalities",
                        "Generating and applying permanent notable personalities.", string.Empty);
                    ReignPersonalityPreparationResult personalities =
                        await Require(ReignCharacterEditorCampaignBehavior.Instance, "character preparation")
                            .EnsureInitialPersonalitiesAsync(
                                _generationId,
                                (completed, total, stage) =>
                                {
                                    ReignCampaignInitializationGate.ReportProgress(stage, completed, total);
                                    SetStatus("Preparing Personalities", stage,
                                        total <= 0 ? string.Empty : completed + " / " + total);
                                }).ConfigureAwait(false);
                    Advance(ReignInitializationStage.Personalities, personalities?.Ok == true);
                }

                if (_state.RequiredStage == ReignInitializationStage.NativeFoundations)
                {
                    SetStatus("Preparing Native Foundations",
                        "Applying calendar, relationship, court, family, and diplomacy foundations.", string.Empty);
                    await ReignMainThread.InvokeAsync(PrepareNativeFoundations).ConfigureAwait(false);
                    Advance(ReignInitializationStage.NativeFoundations, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.Timeline)
                {
                    SetStatus("Opening Campaign Timeline",
                        "Connecting this campaign to its durable world-history timeline.", string.Empty);
                    ReignWorldHistoryCampaignBehavior history =
                        Require(ReignWorldHistoryCampaignBehavior.Instance, "world-history timeline");
                    JObject opened = await history.EnsureTimelineReadyAsync(_generationId).ConfigureAwait(false);
                    string timeline = opened?.Value<string>("timelineId") ?? history.TimelineId;
                    if (string.IsNullOrWhiteSpace(timeline))
                        throw new InvalidOperationException("The world-history timeline id was empty.");
                    _state.SetTimeline(timeline);
                    Advance(ReignInitializationStage.Timeline, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.Identity)
                {
                    SetStatus("Synchronizing Campaign Identity",
                        "Registering heroes and the authoritative portrait roster.", string.Empty);
                    JObject identity = await ReignServerClient.SynchronizeIdentityNetworkAsync(
                            _generationId)
                        .ConfigureAwait(false);
                    if (identity?.Value<bool?>("ok") != true
                        || identity.Value<bool?>("portraitRosterOk") != true)
                    {
                        throw new InvalidOperationException(
                            identity?.Value<string>("error")
                            ?? identity?.Value<string>("portraitRosterError")
                            ?? "Campaign identity synchronization was incomplete.");
                    }
                    await ReignMainThread.InvokeAsync(() =>
                        ReignCharacterEditorCampaignBehavior.Instance
                            ?.MarkIdentitySynchronizedForCurrentDay())
                        .ConfigureAwait(false);
                    Advance(ReignInitializationStage.Identity, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.AuthoritativeSnapshots)
                {
                    SetStatus("Capturing Initial World State",
                        "Recording the authoritative day-one world snapshot.", string.Empty);
                    if (!await ReignServerClient.IngestDailyWorldSnapshotAsync().ConfigureAwait(false))
                        throw new InvalidOperationException("The initial world snapshot was rejected.");
                    Advance(ReignInitializationStage.AuthoritativeSnapshots, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.Relationships)
                {
                    await PrepareRelationshipsAsync().ConfigureAwait(false);
                    Advance(ReignInitializationStage.Relationships, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.CriticalDrain)
                {
                    SetStatus("Sealing Critical State",
                        "Finishing the authoritative history and relationship queues.", string.Empty);
                    bool drained = await Task.Run(() =>
                        ReignWorldHistoryTransport.FlushAndWait(TimeSpan.FromMinutes(10)))
                        .ConfigureAwait(false);
                    if (!drained)
                        throw new InvalidOperationException(
                            "World-history upload did not become idle within ten minutes.");
                    Advance(ReignInitializationStage.CriticalDrain, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.InitialSocialWorld)
                {
                    await PrepareInitialSocialWorldAsync().ConfigureAwait(false);
                    Advance(ReignInitializationStage.InitialSocialWorld, true);
                }

                if (_state.RequiredStage == ReignInitializationStage.ServerAcknowledgement)
                {
                    await SealWithServerAsync().ConfigureAwait(false);
                }

                if (!_state.CanRelease)
                    throw new InvalidOperationException(
                        "Campaign preparation reached the end without a valid readiness seal.");
                await ReleaseCampaignAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_stateLock)
                {
                    if (_state != null && _state.RequiredStage != ReignInitializationStage.Failed)
                        _state.Fail(ex.Message);
                    _lastError = ex.Message;
                    _statusTitle = "Bannerlord Reign Preparation Needs Attention";
                    _statusDetail = ex.Message;
                    _statusProgress = "Select Retry to continue from the last completed stage.";
                }
                ReignCampaignInitializationGate.ReportProgress("failed_waiting_for_retry", 0, 0);
                ReignLog.Exception("Campaign preparation failed.", ex);
            }
        }

        private async Task PrepareRelationshipsAsync()
        {
            SetStatus("Generating Opening Relationships",
                "Seeding co-located, family, and political relationships from permanent MBTI compatibility.",
                string.Empty);
            ReignRelationshipCampaignBehavior relationships =
                Require(ReignRelationshipCampaignBehavior.Instance, "relationship preparation");
            ReignAmbientRelationshipRunResult run =
                await relationships.StartInitialRelationshipRunAsync(_generationId).ConfigureAwait(false);
            _relationshipPlanId = run.NativeSyncPlan?.PlanId ?? string.Empty;
            DateTime deadline = DateTime.UtcNow.AddMinutes(10);
            while (DateTime.UtcNow < deadline)
            {
                await ReignMainThread.InvokeAsync(
                    relationships.ProcessInitializationProjectionFrame).ConfigureAwait(false);
                await ReignServerClient.PumpInitializationNativeReceiptsAsync().ConfigureAwait(false);
                ReignNativeRelationSyncProgress progress = relationships.InitializationProgress();
                if (progress == null)
                {
                    if (run.NativeChangesQueued > 0)
                        throw new InvalidOperationException(
                            "The server queued relationship changes without returning a native projection plan.");
                    return;
                }
                SetStatus("Generating Opening Relationships",
                    "Applying final seeded relationships to Bannerlord before gameplay begins.",
                    (progress.AppliedCount + progress.AlreadyAlignedCount + progress.ObsoleteCount)
                        + " applied, " + progress.RemainingCount + " remaining");
                if (progress.FailedCount > 0)
                    throw new InvalidOperationException(
                        "Native relationship projection failed for " + progress.FailedCount
                        + " pair(s): " + progress.LastError);
                if (progress.RemainingCount == 0
                    && relationships.PendingNativeReceiptCount == 0)
                {
                    if (!await ReignServerClient.ReportNativeRelationSyncProgressAsync(progress)
                        .ConfigureAwait(false))
                    {
                        throw new InvalidOperationException(
                            "The server did not confirm native relationship reconciliation.");
                    }
                    return;
                }
                await Task.Delay(16).ConfigureAwait(false);
            }
            throw new InvalidOperationException(
                "Native relationship reconciliation did not finish within ten minutes.");
        }

        private async Task SealWithServerAsync()
        {
            SetStatus("Verifying Readiness",
                "Confirming that every critical queue is durably idle.", string.Empty);
            ReignRelationshipCampaignBehavior relationships =
                Require(ReignRelationshipCampaignBehavior.Instance, "relationship readiness");
            for (int observationNumber = 0; observationNumber < 2; observationNumber++)
            {
                ReignNativeRelationSyncProgress progress = relationships.InitializationProgress();
                ReignInitializationQueueObservation observation =
                    new ReignInitializationQueueObservation
                    {
                        WorldHistoryCount = ReignWorldHistoryTransport.PendingCount(),
                        RelationshipInputCount = ReignServerClient.PendingAmbientInputCount(),
                        NativeTargetCount = progress?.RemainingCount ?? 0,
                        NativeReceiptCount = relationships.PendingNativeReceiptCount,
                        InFlightCount = ReignWorldHistoryTransport.UploadInFlight ? 1 : 0,
                        NativeProjectionFailureCount = progress?.FailedCount ?? 0,
                        HistorySequence =
                            ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L
                    };
                if (!_state.ObserveQuiescence(observation) && observationNumber == 1)
                    throw new InvalidOperationException(
                        "Critical initialization queues changed during readiness verification.");
                await ReignMainThread.InvokeAsync(() => { }).ConfigureAwait(false);
                await Task.Delay(50).ConfigureAwait(false);
            }

            long expectedSequence = _state.SealedHistorySequence;
            JObject acknowledged = await ReignInitializationServerClient.AcknowledgeSealAsync(
                _state.CampaignId,
                _state.TimelineId,
                _generationId,
                expectedSequence,
                _relationshipPlanId).ConfigureAwait(false);
            if (!string.Equals(
                    acknowledged.Value<string>("campaignId"),
                    _state.CampaignId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    acknowledged.Value<string>("timelineId"),
                    _state.TimelineId,
                    StringComparison.Ordinal)
                || (acknowledged.Value<long?>("acknowledgedHistorySequence") ?? -1L)
                    < expectedSequence
                || acknowledged.Value<bool?>("relationshipPlanComplete") != true
                || (acknowledged.Value<int?>("pendingNativeTargets") ?? -1) != 0)
            {
                throw new InvalidOperationException(
                    "The server readiness acknowledgement did not match the prepared campaign state.");
            }
            _state.AcceptServerAcknowledgement(
                _state.CampaignId,
                _state.TimelineId,
                expectedSequence,
                acknowledged.Value<int?>("sealVersion") ?? 0);
            PersistJournal();
        }

        private async Task PrepareInitialSocialWorldAsync()
        {
            SetStatus("Generating Background Rumors",
                "Processing initial rumors, reputations, Public Standing, and their native relationship projections.",
                string.Empty);
            ReignRelationshipCampaignBehavior relationships =
                Require(ReignRelationshipCampaignBehavior.Instance,
                    "initial social-world native projection");
            DateTime deadline = DateTime.UtcNow.AddMinutes(10);
            JObject previous = null;
            int stableReadyObservations = 0;
            while (DateTime.UtcNow < deadline)
            {
                JObject status = await ReignInitializationServerClient.ReadStatusAsync(
                    _state.CampaignId, _state.TimelineId, _generationId)
                    .ConfigureAwait(false);
                if (status?.Value<bool?>("ok") != true)
                    throw new InvalidOperationException(status?.Value<string>("error")
                        ?? "Initial social-world status was unavailable.");

                await ReignServerClient.PullRelationshipNativeTargetsAsync()
                    .ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(
                    relationships.ProcessInitializationProjectionFrame)
                    .ConfigureAwait(false);
                await ReignServerClient.PumpInitializationNativeReceiptsAsync()
                    .ConfigureAwait(false);

                int pending = status.Value<int?>("pendingSocialOutcomes") ?? 0;
                int completed = status.Value<int?>("completedSocialOutcomes") ?? 0;
                int lanes = status.Value<int?>("activeSubjectLanes") ?? 0;
                int workers = status.Value<int?>("activeWorkers") ?? 0;
                int invalidations = status.Value<int?>("standingInvalidations") ?? 0;
                int native = status.Value<int?>("nativeTargets") ?? 0;
                int failed = (status.Value<int?>("failedSocialOutcomes") ?? 0)
                    + (status.Value<int?>("standingFailures") ?? 0)
                    + (status.Value<int?>("nativeFailures") ?? 0);
                SetStatus("Generating Background Rumors",
                    "Processing initial rumors, reputations, Public Standing, and native relationship projections.",
                    completed + " outcomes complete, " + pending + " pending; "
                    + lanes + " subject lanes on " + workers + " workers; "
                    + invalidations + " standing and " + native + " native remaining");
                if (failed > 0)
                    throw new InvalidOperationException(
                        "Initial social-world processing reported " + failed
                        + " failed item(s). Select Retry after reviewing the server error.");

                bool ready = status.Value<bool?>("ready") == true
                    && relationships.PendingNativeReceiptCount == 0;
                string fingerprint = pending + "|" + lanes + "|" + invalidations
                    + "|" + native + "|" + completed;
                string previousFingerprint = previous == null ? string.Empty
                    : previous.Value<string>("fingerprint") ?? string.Empty;
                if (ready && fingerprint == previousFingerprint)
                    stableReadyObservations++;
                else
                    stableReadyObservations = ready ? 1 : 0;
                previous = new JObject { ["fingerprint"] = fingerprint };
                if (stableReadyObservations >= 2) return;
                await Task.Delay(50).ConfigureAwait(false);
            }
            throw new InvalidOperationException(
                "Initial rumor and reputation preparation did not finish within ten minutes.");
        }

        private async Task ReleaseCampaignAsync()
        {
            ReignInitializationReleaseToken token;
            if (!ReignCampaignInitializationGate.TryCreateReleaseToken(
                _generationId, _state, out token)
                || !ReignCampaignInitializationGate.TrySealAndMarkReady(
                    _generationId, token))
            {
                throw new InvalidOperationException(
                    "The campaign readiness seal could not release the active generation.");
            }
            _sealedForCurrentCampaign = true;
            await ReignMainThread.InvokeAsync(() =>
            {
                ReignNotableGenerationPopupManager.Hide();
                InformationManager.DisplayMessage(new InformationMessage(
                    "Bannerlord Reign preparation is complete. Saving is now protected.",
                    Color.FromUint(0xFFFFD36A)));
            }).ConfigureAwait(false);
            ReignLog.Info("Campaign readiness seal released generation=" + _generationId
                + " campaign=" + _state.CampaignId
                + " timeline=" + _state.TimelineId
                + " historySequence=" + _state.SealedHistorySequence + ".");
        }

        private void PrepareNativeFoundations()
        {
            Require(ReignAICampaignBehavior.Instance, "calendar").PrepareInitialState();
            Require(ReignRelationshipCampaignBehavior.Instance, "relationship baselines")
                .PrepareInitialBaselines();
            Require(ReignRulerReputationCampaignBehavior.Instance, "ruler reputation")
                .PrepareInitialState();
            Require(ReignRebellionCampaignBehavior.Instance, "rebellion state")
                .PrepareInitialState();
            Require(ReignWorldDiplomacyCampaignBehavior.Instance, "world diplomacy")
                .PrepareInitialState();
            Require(ReignSocialEventsCampaignBehavior.Instance, "social events")
                .PrepareInitialState();
            Require(ReignFamilyCampaignBehavior.Instance, "family state")
                .PrepareInitialState();
            Require(ReignCourtPersonalityReputationCampaignBehavior.Instance, "court reputation")
                .PrepareInitialState();
#if !REIGN_EXCLUDE_COURT
            Require(ReignCourtCampaignBehavior.Instance, "court state").PrepareInitialState();
#endif
        }

        private void Advance(ReignInitializationStage stage, bool success)
        {
            if (!_state.AcceptStageResult(_generationId, stage, success))
                throw new InvalidOperationException(stage + " returned for a stale campaign generation.");
            PersistJournal();
        }

        private void PersistJournal()
        {
            _sealVersion = _state.CanRelease
                ? ReignCampaignReadinessState.CurrentSealVersion
                : 0;
            _sealedCampaignId = _state.CampaignId;
            _sealedTimelineId = _state.TimelineId;
            _completedStageMask = (int)_state.CompletedStageMask;
            _sealedHistorySequence = _state.SealedHistorySequence;
        }

        private void Retry()
        {
            if (_state == null || _state.RequiredStage != ReignInitializationStage.Failed) return;
            _state.Retry();
            _pipelineTask = null;
            _lastError = string.Empty;
            _ = ReignInitializationServerClient.RetryAsync(_state.CampaignId,
                _state.TimelineId, _generationId);
            SetStatus("Retrying Bannerlord Reign Preparation",
                "Continuing from the last completed stage.", string.Empty);
            ReignLog.Info("Retrying campaign preparation generation=" + _generationId
                + " stage=" + _state.RequiredStage + ".");
        }

        private void SetStatus(string title, string detail, string progress)
        {
            lock (_stateLock)
            {
                _statusTitle = title ?? string.Empty;
                _statusDetail = detail ?? string.Empty;
                _statusProgress = progress ?? string.Empty;
            }
        }

        private static T Require<T>(T instance, string name) where T : class
        {
            if (instance == null)
                throw new InvalidOperationException(
                    "The " + name + " campaign behavior is unavailable.");
            return instance;
        }
    }
}
