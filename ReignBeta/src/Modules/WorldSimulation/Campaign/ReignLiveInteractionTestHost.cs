using System;
using System.Collections.Generic;
using System.Linq;
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
        private static readonly string[] CoreSupportedModes =
            { "individual_chat", "party_chat", "social_event", "wilderness_event", "social_balance", "passive_world", "campaign_command", "government", "party_agency", "structured_actions", "npc_lives", "personal_gossip" };
        private static TaleWorlds.CampaignSystem.Campaign _observedCampaign;
        private static string _gameInstanceId = string.Empty;
        private static DateTime _nextHeartbeatUtc = DateTime.MinValue;
        private static DateTime _nextPollUtc = DateTime.MinValue;
        private static DateTime _lastHeartbeatDiagnosticUtc = DateTime.MinValue;
        private static bool _heartbeatAcceptedForObservedCampaign;
        private static volatile bool _heartbeatInFlight;
        private static volatile bool _pollInFlight;
        private static volatile bool _commandInFlight;
        private static volatile bool _sessionCleanupInFlight;
        private static bool _autoAcknowledgeDiplomacyAnnouncements;
        private static bool _observeGovernmentNews;
        private static bool _armed;
        private static string _activeRunId = string.Empty;
        private static string _activeMode = string.Empty;
        private static string _presentation = "headless";
        private static ReignIndividualChatScreenVM _individualVm;
        private static ReignPartyChatScreenVM _partyVm;
        private static ReignSocialEventScreenVM _eventVm;
        private static ReignSocialEventSession _eventSession;
        private static string _lastNpcReplyText = string.Empty;
        private static bool _settingsCaptured;
        private static bool _oldDiplomacy;
        private static bool _oldStrategy;
        private static bool _oldPolitics;
        private static bool _oldConversationActions;
        private static string _lastAutomationSaveName = string.Empty;
        private static DateTime _lastAutomationSaveCompletedUtc = DateTime.MinValue;
        private static readonly TimeSpan AutomationSaveQuietPeriod = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan AutomationSaveCompletionReserve = TimeSpan.FromSeconds(75);
        private static string _wildernessOriginSettlementId = string.Empty;
        private static bool _manipulationFixtureCaptured;
        private static float _manipulationFixtureOriginalRenown;
        private static int _manipulationFixtureOriginalGold;
        private static bool _manipulationFixtureEconomicCapacityKnown;
        private static string _manipulationFixtureWealthEvidenceBasis = string.Empty;
        private static readonly List<string> TemporaryWildernessHeroIds = new List<string>();
        private static readonly Dictionary<string, LiveCommandResult> CompletedCommandResults = new Dictionary<string, LiveCommandResult>(StringComparer.OrdinalIgnoreCase);
        internal static string PartyAgencyClientProcessInstanceId { get; } = Guid.NewGuid().ToString("N");
        private static readonly PartyAgencyExecutionCache PartyAgencyCompletedCommands = new PartyAgencyExecutionCache();
        private static readonly Dictionary<string, JObject> PartyAgencyBaselines =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JObject> PartyAgencyNativeEvidence =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static string _partyAgencyFixtureTargetId = string.Empty;

        public static string GameInstanceId => _gameInstanceId;
        public static bool IsBusy => _commandInFlight || _sessionCleanupInFlight;
        internal static bool OwnsAutomationSession => IsBusy || (_armed && !string.IsNullOrWhiteSpace(_activeRunId));
        internal static bool OwnsPassiveWorldSession => _armed && _activeMode == "passive_world"
            && !string.IsNullOrWhiteSpace(_activeRunId);
        public static int CompletedCommandCacheCount =>
            CompletedCommandResults.Count + PartyAgencyCompletedCommands.Count;

        public static string[] GetSupportedModes()
        {
            List<string> modes = new List<string>(CoreSupportedModes);
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings?.CorrespondenceEnabled == true)
                modes.Add("correspondence");
            if (ReignBetaSettings.IsCourtSystemAvailable
                && ReignServices.Court?.IsRuleModeActive == true)
            {
                modes.Add("court_event");
                modes.Add("spymaster");
                if (ReignServices.Court.HasRoyalCommandAccess
                    && ReignServices.Court.ForeignAmbassadors.Any(posting => posting?.IsResident == true))
                    modes.Add("ambassador_official");
            }
            return modes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static void ApplicationTick(float dt)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            EnforcePassiveWorldAdvanceBoundary(campaign);
            ReignBeta.WorldSimulation.Economy.ReignWarEconomyCohortControl.Update(_gameInstanceId);
            bool playerReady = campaign != null
                && CharacterObject.PlayerCharacter != null;
            PauseNpcLivesEncounterCleanup(campaign);
            // MCM can expose its singleton after the native campaign has already
            // loaded. Reign's production endpoint uses the local-server defaults
            // while that singleton is unavailable; the recovery bridge must do
            // the same. An explicit false setting remains authoritative.
            if (settings?.UseLocalServer == false || campaign == null || !playerReady)
            {
                LogHeartbeatDiagnostic("Live interaction heartbeat waiting for campaign prerequisites. settings="
                    + (settings == null ? "missing" : "ready")
                    + " localServer=" + (settings?.UseLocalServer != false)
                    + " campaign=" + (campaign == null ? "missing" : "ready")
                    + " player=" + (playerReady ? "ready" : "missing") + ".");
                return;
            }
            if (!ReferenceEquals(_observedCampaign, campaign))
            {
                _observedCampaign = campaign;
                _gameInstanceId = "game-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(2);
                _nextPollUtc = DateTime.UtcNow.AddSeconds(3);
                _armed = false;
                _heartbeatAcceptedForObservedCampaign = false;
                CompletedCommandResults.Clear();
                PartyAgencyCompletedCommands.Clear();
                ResetLocalSession(false);
                ReignLog.Info("Live interaction heartbeat observed campaign instance="
                    + _gameInstanceId + " campaign=" + ReignCampaignIdentity.CurrentCampaignId() + ".");
                return;
            }
            if (_armed
                && _autoAcknowledgeDiplomacyAnnouncements
                && (string.Equals(_activeMode, "social_balance", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(_activeMode, "passive_world", StringComparison.OrdinalIgnoreCase))
                && ReignDiplomacyAnnouncementScreenManager.TryAcknowledgeForLiveHarness(
                    true,
                    out string eventId, _observeGovernmentNews
                        && string.Equals(_activeMode, "passive_world", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.Equals(_activeMode, "social_balance", StringComparison.OrdinalIgnoreCase))
                {
                    ReignServices.SocialBalanceHarness?.RecordOverride(
                        "diplomacy_announcement_acknowledged",
                        new JObject { ["eventId"] = eventId });
                }
            }
            // Heartbeat is the recovery/control plane that reports the actual
            // loaded campaign and save identity. Once Save Sync has finished its
            // bounded alignment, do not suppress that evidence behind a second
            // campaign-id comparison: the guarded server enrollment is the
            // authority that rejects a wrong campaign before commands are armed.
            if (ReignSaveSyncCoordinator.IsAlignmentPending)
            {
                LogHeartbeatDiagnostic("Live interaction heartbeat waiting for Save Sync alignment instance="
                    + _gameInstanceId + ".");
                return;
            }
            if (!_heartbeatInFlight && DateTime.UtcNow >= _nextHeartbeatUtc)
            {
                // The bridge does not need a high-frequency heartbeat while it is
                // idle. Rebuilding native campaign snapshots and performing a
                // loopback HTTP exchange every two seconds created substantial
                // allocator churn during multi-hour qualification runs. Keep the
                // active command cadence responsive, but let an idle armed bridge
                // settle between observations.
                _nextHeartbeatUtc = DateTime.UtcNow.AddSeconds(
                    _commandInFlight || !string.IsNullOrWhiteSpace(_activeRunId) ? 3 : 10);
                _heartbeatInFlight = true;
                LogHeartbeatDiagnostic("Live interaction heartbeat scheduling request instance="
                    + _gameInstanceId + ".", true);
                _ = HeartbeatAsync();
            }
            if (_armed && !_pollInFlight && !_commandInFlight && !_sessionCleanupInFlight && DateTime.UtcNow >= _nextPollUtc)
            {
                // Two-second idle pickup is sufficient for unattended control.
                // Once a run owns the game, use a shorter cadence for its next
                // command without continuously polling at frame-adjacent rates.
                _nextPollUtc = DateTime.UtcNow.AddMilliseconds(
                    string.IsNullOrWhiteSpace(_activeRunId) ? 2000 : 750);
                _pollInFlight = true;
                _ = PollAsync();
            }
        }

        private static async Task HeartbeatAsync()
        {
            try
            {
                JObject response = await ReignLiveTestServerClient.SubmitLiveTestHeartbeatAsync(_gameInstanceId, _activeRunId, _activeMode, _commandInFlight).ConfigureAwait(false);
                _armed = response.Value<bool?>("armed") == true;
                if (!_heartbeatAcceptedForObservedCampaign)
                {
                    _heartbeatAcceptedForObservedCampaign = true;
                    ReignLog.Info("Live interaction heartbeat accepted instance="
                        + _gameInstanceId + " armed=" + _armed + ".");
                }
                string serverRun = response.Value<string>("activeRunId") ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(_activeRunId)
                    && string.IsNullOrWhiteSpace(serverRun)
                    && !_commandInFlight
                    && !_sessionCleanupInFlight)
                {
                    _sessionCleanupInFlight = true;
                    _ = CloseOrphanedSessionAsync();
                }
            }
            catch (Exception ex)
            {
                _armed = false;
                ReignLog.Warn("Live interaction heartbeat failed: " + ex.Message);
            }
            finally { _heartbeatInFlight = false; }
        }

        private static void LogHeartbeatDiagnostic(string message, bool force = false)
        {
            DateTime now = DateTime.UtcNow;
            if (!force && now - _lastHeartbeatDiagnosticUtc < TimeSpan.FromSeconds(15)) return;
            _lastHeartbeatDiagnosticUtc = now;
            ReignLog.Info(message);
        }

        private static async Task CloseOrphanedSessionAsync()
        {
            try
            {
                await CloseSessionAsync(false).ConfigureAwait(false);
                _activeRunId = string.Empty;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Live interaction orphaned-session cleanup failed: " + ex.Message);
            }
            finally
            {
                _sessionCleanupInFlight = false;
            }
        }

        private static async Task PollAsync()
        {
            try
            {
                ReignMainThread.GameScope pollNativeScope = ReignMainThread.CurrentGameScope;
                string pollCampaignId = ReignServerClient.GetCampaignId();
                string pollGameInstanceId = _gameInstanceId;
                JObject response = await ReignLiveTestServerClient.PollLiveTestCommandAsync(pollGameInstanceId, pollCampaignId).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true || response.Value<bool?>("found") != true) return;
                JObject command = response["command"] as JObject;
                if (command == null) return;
                _commandInFlight = true;
                string runId = command.Value<string>("runId") ?? string.Empty;
                string commandId = command.Value<string>("commandId") ?? string.Empty;
                JObject run = response["run"] as JObject;
                PartyAgencyExecutionStamp execution = null;
                if (IsObservedPartyAgencyCommand(run, command))
                {
                    execution = new PartyAgencyExecutionStamp(pollNativeScope, pollCampaignId,
                        pollGameInstanceId, PartyAgencyClientProcessInstanceId, runId, commandId,
                        command.Value<string>("leaseId") ?? string.Empty,
                        command.Value<int?>("executionRetryGeneration") ?? 0);
                    if (!execution.MatchesIssuedScope(run.Value<string>("campaignId"),
                            run.Value<string>("gameInstanceId"), run.Value<string>("runId"))
                        || !IsCurrentPartyAgencyExecution(execution))
                        return;
                }

                LiveCommandResult completed;
                bool hasCompleted = execution == null
                    ? CompletedCommandResults.TryGetValue(commandId, out completed)
                        || TryGetDurableSocialBalanceResult(command, out completed)
                    : PartyAgencyCompletedCommands.TryGet(execution, out completed);
                if (hasCompleted)
                {
                    if (completed.Status == "completed")
                        ActivateHarnessRun(command);
                    await ReportCommandResultAsync(runId, commandId, completed, execution).ConfigureAwait(false);
                    return;
                }
                JObject acknowledgment = await ReignLiveTestServerClient.AcknowledgeLiveTestCommandAsync(
                    execution?.GameInstanceId ?? _gameInstanceId, runId, commandId,
                    "accepted", "Command accepted by the loaded campaign.", execution).ConfigureAwait(false);
                if (execution != null && (!HasAcceptedPartyAgencyAcknowledgment(acknowledgment, execution)
                    || !IsCurrentPartyAgencyExecution(execution)))
                    return;

                string operation = command.Value<string>("operation") ?? string.Empty;
                if (operation.Equals("world_advance", StringComparison.OrdinalIgnoreCase))
                {
                    // world_advance can run for hours. Publish its exact run id while
                    // it is executing so an interrupted external controller can
                    // cancel the authoritative run and let the command pause time.
                    ActivateHarnessRun(command);
                }
                LiveCommandResult result;
                try
                {
                    if (execution == null)
                        result = await ExecuteCommandAsync(command).ConfigureAwait(false);
                    else
                        result = await ReignMainThread.RunOwnedAsync(pollNativeScope,
                            () => IsCurrentPartyAgencyExecution(execution),
                            () => ExecuteObservedPartyAgencyCommandAsync(command, execution, operation)).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ReignLog.Exception("Live interaction command execution", ex);
                    result = LiveCommandResult.Failed(ex.Message);
                }
                if (execution == null)
                {
                    if (result.Status == "completed")
                        ActivateHarnessRun(command);
                    RememberCommandResult(commandId, result);
                    RememberDurableSocialBalanceResult(command, result);
                }
                else
                {
                    if (!IsCurrentPartyAgencyExecution(execution))
                        return;

                    if (result.Status == "completed")
                        ActivateHarnessRun(command);
                    result.PartyAgencyExecution = execution;
                    PartyAgencyCompletedCommands.Remember(result);
                }

                await ReportCommandResultAsync(runId, commandId, result, execution).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Live interaction command", ex);
            }
            finally
            {
                _commandInFlight = false;
                _pollInFlight = false;
            }
        }

        private static Task<JObject> ReportCommandResultAsync(string runId, string commandId,
            LiveCommandResult result, PartyAgencyExecutionStamp assignment)
        {
            PartyAgencyExecutionStamp execution = result.PartyAgencyExecution;
            if (assignment != null && (execution == null || !execution.CanReplayFor(assignment)
                || !IsCurrentPartyAgencyExecution(assignment)))
                return Task.FromResult<JObject>(null);

            return ReignLiveTestServerClient.ReportLiveTestCommandAsync(
                execution?.GameInstanceId ?? _gameInstanceId, runId, commandId, result.Status,
                result.Message, result.Error, result.Data, result.CorrelationIds, assignment, execution);
        }

        private static bool IsObservedPartyAgencyCommand(JObject run, JObject command)
        {
            JToken schemaVersion = command["executionObservationVersion"];
            return string.Equals(run?.Value<string>("mode"), "party_agency", StringComparison.OrdinalIgnoreCase)
                && schemaVersion?.Type == JTokenType.Integer
                && schemaVersion.Value<long>() == 1;
        }

        private static bool HasAcceptedPartyAgencyAcknowledgment(JObject response, PartyAgencyExecutionStamp execution)
        {
            if (response?["ok"]?.Type != JTokenType.Boolean || !response.Value<bool>("ok"))
                return false;
            if (response.TryGetValue("idempotent", out JToken idempotent)
                && (idempotent.Type != JTokenType.Boolean || idempotent.Value<bool>()))
                return false;

            JObject command = response["command"] as JObject;
            JObject run = response["run"] as JObject;
            return command != null && run != null
                && string.Equals(run.Value<string>("mode"), "party_agency", StringComparison.OrdinalIgnoreCase)
                && execution.MatchesIssuedScope(run.Value<string>("campaignId"),
                    run.Value<string>("gameInstanceId"), run.Value<string>("runId"))
                && string.Equals(execution.RunId, command.Value<string>("runId"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(execution.CommandId, command.Value<string>("commandId"), StringComparison.OrdinalIgnoreCase)
                && string.Equals(execution.ExecutionLeaseId, command.Value<string>("leaseId"), StringComparison.Ordinal)
                && execution.RetryGeneration == (command.Value<int?>("executionRetryGeneration") ?? 0)
                && string.Equals(command.Value<string>("status"), "accepted", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCurrentPartyAgencyExecution(PartyAgencyExecutionStamp execution)
        {
            return execution.IsCurrent(ReignMainThread.CurrentGameScope, ReignServerClient.GetCampaignId(),
                _gameInstanceId, PartyAgencyClientProcessInstanceId);
        }

        private static void ActivateHarnessRun(JObject command)
        {
            string operation = command?.Value<string>("operation") ?? string.Empty;
            bool socialBalance = operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase);
            bool passiveWorld = operation.StartsWith("world_", StringComparison.OrdinalIgnoreCase);
            if (!socialBalance && !passiveWorld)
                return;

            _activeRunId = command.Value<string>("runId") ?? string.Empty;
            _activeMode = passiveWorld ? "passive_world" : "social_balance";
            // Every command carries its own choice; never inherit observation from another run.
            _observeGovernmentNews = passiveWorld && command.Value<bool?>("observeGovernmentNews") == true;
            if ((operation.Equals("social_time_control", StringComparison.OrdinalIgnoreCase)
                    || operation.Equals("world_time_control", StringComparison.OrdinalIgnoreCase))
                && command.Property("autoAcknowledgeDiplomacyAnnouncements") != null)
            {
                _autoAcknowledgeDiplomacyAnnouncements =
                    command.Value<bool?>("autoAcknowledgeDiplomacyAnnouncements") == true;
            }
        }

        private static void RememberCommandResult(string commandId, LiveCommandResult result)
        {
            if (string.IsNullOrWhiteSpace(commandId) || result == null) return;
            CompletedCommandResults[commandId] = result;
            while (CompletedCommandResults.Count > 100)
            {
                string oldest = CompletedCommandResults.Keys.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(oldest)) break;
                CompletedCommandResults.Remove(oldest);
            }
        }

        private static async Task<LiveCommandResult> ExecuteCommandAsync(JObject command)
        {
            string operation = (command.Value<string>("operation") ?? string.Empty).ToLowerInvariant();
            if (operation.StartsWith("world_", StringComparison.OrdinalIgnoreCase))
                return await ExecutePassiveWorldCommandAsync(command).ConfigureAwait(false);
            if (operation.StartsWith("social_", StringComparison.OrdinalIgnoreCase))
                return await ExecuteSocialBalanceCommandAsync(command).ConfigureAwait(false);
            switch (operation)
            {
                case "conversation_clock_probe": return await ConversationClockProbeAsync(command).ConfigureAwait(false);
                case "resident_native_approach": return await ApproachResidentForTestAsync(command).ConfigureAwait(false);
                case "resident_native_leave": return await ApproachResidentForTestAsync(command, true).ConfigureAwait(false);
                case "search_targets": return await SearchTargetsAsync(command).ConfigureAwait(false);
                case "open": return await OpenAsync(command).ConfigureAwait(false);
                case "send": return await SendAsync(command).ConfigureAwait(false);
                case "select_participants": return await SelectParticipantsAsync(command).ConfigureAwait(false);
                case "next_phase": return await NextPhaseAsync().ConfigureAwait(false);
                case "wait_approach": return await WaitApproachAsync().ConfigureAwait(false);
                case "close": return await CloseCommandAsync(true).ConfigureAwait(false);
                case "scene_boundary": return await CloseCommandAsync(false).ConfigureAwait(false);
                case "wait_for_memory":
                    await Task.Delay(Math.Max(100, Math.Min(30000, command.Value<int?>("waitMilliseconds") ?? 1000))).ConfigureAwait(false);
                    return LiveCommandResult.Completed("Memory workers were given a bounded processing interval.");
                case "wait_for_correspondence": return await WaitForCorrespondenceAsync(command).ConfigureAwait(false);
                case "save_checkpoint": return await SaveCheckpointAsync(command).ConfigureAwait(false);
                case "shutdown_game": return await ShutdownGameAsync(command).ConfigureAwait(false);
                case "prepare_party_fixture": return await PreparePartyFixtureAsync(command).ConfigureAwait(false);
                case "prepare_authority_fixture": return await PrepareAuthorityFixtureAsync(command).ConfigureAwait(false);
                case "prepare_wilderness": return await PrepareWildernessAsync(command).ConfigureAwait(false);
                case "restore_settlement": return await RestoreSettlementAsync().ConfigureAwait(false);
                case "prepare_manipulation_fixture": return await PrepareManipulationFixtureAsync(command).ConfigureAwait(false);
                case "restore_manipulation_fixture": return await RestoreManipulationFixtureAsync().ConfigureAwait(false);
                case "gauntlet_snapshot": return await GauntletSnapshotAsync(command).ConfigureAwait(false);
                case "action_probe_wait": return await ActionProbeWaitAsync(command).ConfigureAwait(false);
                case "structured_action_test": return await StructuredActionTestAsync(command).ConfigureAwait(false);
                case "personal_gossip_test": return await PersonalGossipTestAsync(command).ConfigureAwait(false);
                case "npc_lives_test": return await NpcLivesTestAsync(command).ConfigureAwait(false);
                case "gauntlet_apply_fixture": return await GauntletApplyFixtureAsync(command).ConfigureAwait(false);
                case "gauntlet_restore_fixture": return await GauntletRestoreFixtureAsync(command).ConfigureAwait(false);
                case "gauntlet_advance_time": return await GauntletAdvanceTimeAsync(command).ConfigureAwait(false);
                case "gauntlet_arrive": return await GauntletRosterMutationAsync(command, true).ConfigureAwait(false);
                case "gauntlet_depart": return await GauntletRosterMutationAsync(command, false).ConfigureAwait(false);
                case "gauntlet_save": return await SaveCheckpointAsync(command).ConfigureAwait(false);
                case "gauntlet_reload": return LiveCommandResult.Failed("Gauntlet reload must be orchestrated through the lifecycle controller after Save Sync reconciliation.");
                case "ui_open": return await UiOpenAsync(command).ConfigureAwait(false);
                case "ui_status": return UiStatus();
                case "ui_snapshot": return await UiSnapshotAsync(command).ConfigureAwait(false);
                case "ui_action": return await UiActionAsync(command).ConfigureAwait(false);
                case "ui_back": return await UiBackAsync().ConfigureAwait(false);
                case "ui_close": return await UiCloseAsync().ConfigureAwait(false);
                case "spymaster_test": return await ExecuteSpymasterTestAsync(command).ConfigureAwait(false);
                case "spymaster_organic": return await ExecuteOrganicSpymasterTestAsync(command).ConfigureAwait(false);
                case "arrest_test": return await ExecuteArrestTestAsync(command).ConfigureAwait(false);
                case "rebellion_test": return await ExecuteRebellionPreparationTestAsync(command).ConfigureAwait(false);
                case "capital_ambassador_test": return await ExecuteCapitalAmbassadorTestAsync(command).ConfigureAwait(false);
                case "royal_council_test": return await ExecuteRoyalCouncilTestAsync(command).ConfigureAwait(false);
                case "ruler_docket_test": return await ExecuteRulerDocketTestAsync(command).ConfigureAwait(false);
                case "kingdom_event_test": return await ExecuteKingdomEventTestAsync(command).ConfigureAwait(false);
                case "government_test": return await ExecuteGovernmentTestAsync(command).ConfigureAwait(false);
                case "clan_accords_test": return await ExecuteClanAccordTestAsync(command).ConfigureAwait(false);
                case "campaign_command_test": return await ExecuteCampaignCommandTestAsync(command).ConfigureAwait(false);
                case "prepare_party_agency_fixture": return await PreparePartyAgencyFixtureAsync(command).ConfigureAwait(false);
                case "party_agency_advance_time": return await AdvancePartyAgencyTimeAsync(command).ConfigureAwait(false);
                case "party_agency_aggregate_fixtures": return await AggregatePartyAgencyFixturesAsync(command).ConfigureAwait(false);
                case "party_agency_review_provider_result": return await ExercisePartyAgencyReviewProviderResultAsync(command).ConfigureAwait(false);
                case "party_agency_recovery_fixture": return await ExercisePartyAgencyRecoveryFixtureAsync(command).ConfigureAwait(false);
                case "party_agency_verify_unrelated_state": return await VerifyPartyAgencyUnrelatedStateAsync(command).ConfigureAwait(false);
                case "party_agency_prepare_hostility": return await PreparePartyAgencyHostilityAsync(command).ConfigureAwait(false);
                case "party_agency_hostile_battle": return await ExercisePartyAgencyHostileBattleAsync(command).ConfigureAwait(false);
                case "party_agency_stage_save_phase": return await StagePartyAgencySavePhaseAsync(command).ConfigureAwait(false);
                case "party_agency_verify_save_phase": return await VerifyPartyAgencySavePhaseAsync(command).ConfigureAwait(false);
                case "party_agency_test": return PartyAgencyTest(command);
                case "confirm": return await ResolveConfirmationAsync(true).ConfigureAwait(false);
                case "deny": return await ResolveConfirmationAsync(false).ConfigureAwait(false);
                default: return LiveCommandResult.Failed("Unsupported operation '" + operation + "'.");
            }
        }

        private static async Task<LiveCommandResult> WaitForCorrespondenceAsync(JObject command)
        {
            int timeoutSeconds = Math.Max(30, Math.Min(600,
                command?.Value<int?>("timeoutSeconds") ?? 300));
            int stableMilliseconds = Math.Max(5000, Math.Min(60000,
                command?.Value<int?>("stableMilliseconds") ?? 25000));
            DateTime deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            DateTime? idleSince = null;
            int peakInFlight = 0;
            while (DateTime.UtcNow < deadline)
            {
                int inFlight = ReignCorrespondenceClient.CorrespondenceTickInFlight;
                peakInFlight = Math.Max(peakInFlight, inFlight);
                if (inFlight > 0)
                {
                    idleSince = null;
                }
                else if (!idleSince.HasValue)
                {
                    idleSince = DateTime.UtcNow;
                }
                else if ((DateTime.UtcNow - idleSince.Value).TotalMilliseconds >= stableMilliseconds)
                {
                    return LiveCommandResult.Completed(
                        "Correspondence generation and delivery remained idle for the required quiescence window.",
                        new JObject
                        {
                            ["stableMilliseconds"] = stableMilliseconds,
                            ["peakInFlight"] = peakInFlight,
                            ["inFlight"] = inFlight
                        });
                }
                await Task.Delay(500).ConfigureAwait(false);
            }
            return LiveCommandResult.Failed(
                "Correspondence generation did not become quiescent before the bounded timeout.",
                new JObject
                {
                    ["timeoutSeconds"] = timeoutSeconds,
                    ["stableMilliseconds"] = stableMilliseconds,
                    ["peakInFlight"] = peakInFlight,
                    ["inFlight"] = ReignCorrespondenceClient.CorrespondenceTickInFlight
                });
        }

        private static async Task<LiveCommandResult> CloseCommandAsync(bool terminal)
        {
            JObject result = await CloseSessionAsync(true).ConfigureAwait(false);
            if (terminal) _activeRunId = string.Empty;
            return LiveCommandResult.Completed(terminal ? "Production interaction session closed." : "Scene boundary closed and consolidated the production session.", result);
        }

        private static async Task<JObject> CloseSessionAsync(bool finish)
        {
            JObject result = new JObject { ["mode"] = _activeMode ?? string.Empty };
            try
            {
                if (_individualVm != null)
                {
                    ReignConversationFinishResult closed = finish ? await _individualVm.FinishAutomationSceneAsync("live_interaction_close").ConfigureAwait(false) : await _individualVm.FinishAutomationSceneAsync("live_interaction_scene_boundary").ConfigureAwait(false);
                    result["sessionId"] = closed.SessionId; result["sceneSummaryId"] = closed.SceneSummaryId; result["ok"] = closed.Ok; result["memory"] = closed.Raw;
                }
                else if (_partyVm != null)
                {
                    ReignConversationFinishResult closed = await _partyVm.FinishAutomationSceneAsync(finish ? "live_interaction_close" : "live_interaction_scene_boundary").ConfigureAwait(false);
                    result["sessionId"] = closed.SessionId; result["sceneSummaryId"] = closed.SceneSummaryId; result["ok"] = closed.Ok; result["memory"] = closed.Raw;
                }
                else if (_eventVm != null)
                {
                    JObject closed = await _eventVm.FinishAutomationAsync().ConfigureAwait(false);
                    JObject resolution = closed["resolution"] as JObject ?? new JObject();
                    JObject memory = resolution["conversationFinish"] as JObject ?? new JObject();
                    result["eventId"] = _eventSession?.Record?.EventId ?? string.Empty;
                    result["ok"] = closed.Value<bool?>("ok") == true;
                    result["phaseFinalized"] = closed.Value<bool?>("phaseFinalized") == true;
                    result["eventResolved"] = closed.Value<bool?>("eventResolved") == true;
                    result["sessionId"] = resolution.Value<string>("conversationSessionId") ?? string.Empty;
                    result["sceneSummaryId"] = memory.Value<string>("sceneSummaryId") ?? string.Empty;
                    result["memory"] = memory;
                    result["resolution"] = resolution;
                }
                else if (_correspondenceHero != null)
                {
                    result["ok"] = true;
                    result["recipientHeroId"] = _correspondenceHero.StringId;
                    result["threadId"] = _correspondenceThreadId ?? string.Empty;
                }
                else if (_courtMatter != null)
                {
                    EndCourtAudience();
                    result["ok"] = true;
                    result["matterId"] = _courtMatter.MatterId ?? string.Empty;
                    result["sessionId"] =
                        ReignServices.Court?.Session?.SessionId
                        ?? string.Empty;
                }
            }
            catch (Exception ex) { result["ok"] = false; result["error"] = ex.Message; }
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_presentation == "visible")
                {
                    ReignIndividualChatScreenManager.Close();
                    ReignPartyChatScreenManager.Close();
                    ReignSocialEventScreenManager.Close();
                    ReignCorrespondenceScreenManager.Close();
                    ReignCourtScreenManager.Close();
                }
                else
                {
                    _individualVm?.OnFinalize();
                    _partyVm?.OnFinalize();
                }
            }).ConfigureAwait(false);
            ResetLocalSession(true);
            return result;
        }

        private static async Task<ReignSocialEventSession> CreateSocialEventSessionAsync(JObject command, List<Hero> heroes)
        {
            ReignSocialEventsCampaignBehavior behavior = ReignServices.SocialEvents;
            if (behavior == null) return null;
            string eventId = command.Value<string>("eventId") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(eventId)) return await ReignMainThread.InvokeAsync(() => behavior.OpenLiveTestSocialEvent(eventId)).ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                string ignored;
                return behavior.CreateLiveTestSocialEvent(heroes, command.Value<string>("templateId") ?? string.Empty, out ignored);
            }).ConfigureAwait(false);
        }

        private static async Task InitializeEventVmAsync(List<Hero> heroes)
        {
            if (_presentation == "visible")
            {
                await ReignMainThread.InvokeAsync(() => ReignSocialEventScreenManager.Open(_eventSession, ReignServices.SocialEvents.ResolveLiveTestSession)).ConfigureAwait(false);
                _eventVm = await WaitForAsync(() => ReignSocialEventScreenManager.ActiveViewModel).ConfigureAwait(false);
            }
            else _eventVm = await ReignMainThread.InvokeAsync(() => new ReignSocialEventScreenVM(_eventSession, () => { }, ReignServices.SocialEvents.ResolveLiveTestSession)).ConfigureAwait(false);
            if (_eventVm != null && heroes.Count > 0) await ReignMainThread.InvokeAsync(() => _eventVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
        }

        private static void CaptureGuardedSettings(string effects)
        {
            if (string.Equals(effects, "full", StringComparison.OrdinalIgnoreCase)) return;
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings == null || _settingsCaptured) return;
            _oldDiplomacy = settings.ExecuteDiplomacyActions;
            _oldStrategy = settings.ExecuteStrategyPlans;
            _oldPolitics = settings.ExecuteInternalPoliticsActions;
            _oldConversationActions = settings.ExecuteConversationActions;
            settings.ExecuteDiplomacyActions = false;
            settings.ExecuteStrategyPlans = false;
            settings.ExecuteInternalPoliticsActions = false;
            settings.ExecuteConversationActions = false;
            _settingsCaptured = true;
        }

        private static void RestoreSettings()
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (!_settingsCaptured || settings == null) return;
            settings.ExecuteDiplomacyActions = _oldDiplomacy;
            settings.ExecuteStrategyPlans = _oldStrategy;
            settings.ExecuteInternalPoliticsActions = _oldPolitics;
            settings.ExecuteConversationActions = _oldConversationActions;
            _settingsCaptured = false;
        }

        private static void ResetLocalSession(bool preserveRun)
        {
            RestoreSettings();
            _individualVm = null; _partyVm = null; _eventVm = null; _eventSession = null;
            ResetCorrespondenceSession();
            ResetCourtSession();
            _autoAcknowledgeDiplomacyAnnouncements = false;
            _observeGovernmentNews = false;
            _activeMode = string.Empty; _presentation = "headless";
            if (!preserveRun) _activeRunId = string.Empty;
        }

        private static LiveCommandResult FailOpen(string error)
        {
            ResetLocalSession(false);
            return LiveCommandResult.Failed(error);
        }

        private static List<Hero> ResolveHeroesForMode(
            string mode,
            IEnumerable<string> searches,
            bool allowDisposableCourtMatter = false)
        {
            List<string> requested = (searches ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            IEnumerable<Hero> available = AvailableHeroesForMode(mode, allowDisposableCourtMatter);
            List<Hero> pool = available.Where(hero => hero != null).GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase).Select(group => group.First()).ToList();
            if (requested.Count == 0) return mode == "individual_chat" || mode == "ambassador_official"
                ? pool.Take(1).ToList()
                : pool.Take(mode == "party_chat" ? 3 : 5).ToList();
            List<Hero> result = new List<Hero>();
            foreach (string search in requested)
            {
                Hero fixture = string.Equals(search, "@party_agency_fixture",
                        StringComparison.OrdinalIgnoreCase)
                    ? FindPartyAgencyFixtureHero() : null;
                Hero match = fixture != null && pool.Any(hero => hero.StringId == fixture.StringId)
                    ? fixture
                    : RankHeroes(pool.Where(hero => result.All(existing => existing.StringId != hero.StringId)), search).FirstOrDefault();
                if (match != null) result.Add(match);
            }
            return result;
        }

        private static IEnumerable<Hero> AvailableHeroesForMode(
            string mode,
            bool allowDisposableCourtMatter = false)
        {
            if (mode == "party_chat" || mode == "wilderness_event")
                return ReignPartyChatSession.GetAvailableConversationHeroes();
            if (mode == "correspondence")
                return ReignCorrespondenceClient.GetKnownCorrespondenceContacts();
            if (mode == "ambassador_official")
            {
                HashSet<string> residentIds = new HashSet<string>(
                    (ReignServices.Court?.ForeignAmbassadors as IEnumerable<ForeignAmbassadorPosting>
                        ?? Enumerable.Empty<ForeignAmbassadorPosting>())
                        .Where(posting => posting?.IsResident == true)
                        .Select(posting => posting.HeroStringId),
                    StringComparer.OrdinalIgnoreCase);
                return Hero.AllAliveHeroes.Where(hero => IsLivingNpc(hero) && residentIds.Contains(hero.StringId));
            }
            if (mode == "court_event")
            {
                if (allowDisposableCourtMatter)
                    return Hero.AllAliveHeroes.Where(IsLivingNpc);
                IEnumerable<CourtMatter> matters =
                    ReignServices.Court?.Matters
                        as IEnumerable<CourtMatter>
                    ?? Enumerable.Empty<CourtMatter>();
                HashSet<string> participantIds = new HashSet<string>(
                    matters
                    .Where(matter => matter != null && !matter.IsTerminal)
                    .SelectMany(matter =>
                        (matter.ParticipantHeroIdsCsv ?? string.Empty).Split(
                            new[] { ',' },
                            StringSplitOptions.RemoveEmptyEntries))
                    .Select(value => value.Trim()),
                    StringComparer.OrdinalIgnoreCase);
                return Hero.AllAliveHeroes.Where(hero =>
                    IsLivingNpc(hero)
                    && participantIds.Contains(hero.StringId));
            }
            return Hero.AllAliveHeroes.Where(IsLivingNpc);
        }

        private static IEnumerable<Hero> RankHeroes(IEnumerable<Hero> heroes, string search)
        {
            string query = (search ?? string.Empty).Trim();
            return (heroes ?? Enumerable.Empty<Hero>()).Where(IsLivingNpc).Select(hero => new { Hero = hero, Score = SearchScore(hero, query) })
                .Where(row => row.Score < 100).OrderBy(row => row.Score).ThenBy(row => row.Hero.Name?.ToString() ?? row.Hero.StringId, StringComparer.OrdinalIgnoreCase).Select(row => row.Hero);
        }

        private static int SearchScore(Hero hero, string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return 10;
            string name = hero?.Name?.ToString() ?? string.Empty; string id = hero?.StringId ?? string.Empty;
            if (name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
            if (id.Equals(query, StringComparison.OrdinalIgnoreCase)) return 1;
            if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
            if (id.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 3;
            if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 4;
            if (id.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) return 5;
            return 100;
        }

        private static bool IsLivingNpc(Hero hero) { return ReignConversationEligibility.IsAdultLivingNpc(hero); }
        private static string NormalizeMode(string value) { return (value ?? string.Empty).Trim().ToLowerInvariant().Replace('-', '_'); }
        private static List<string> ReadSearches(JObject command)
        {
            List<string> searches = (command["targetSearches"] as JArray ?? new JArray()).Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            string single = command.Value<string>("targetSearch") ?? command.Value<string>("search") ?? string.Empty;
            if (searches.Count == 0 && !string.IsNullOrWhiteSpace(single)) searches.Add(single);
            return searches;
        }

        private static JObject HeroTargetJson(Hero hero)
        {
            JObject traits = new JObject
            {
                ["valor"] =
                    hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                ["generosity"] =
                    hero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0,
                ["honor"] =
                    hero?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                ["mercy"] =
                    hero?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                ["calculating"] =
                    hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0
            };
            JObject skills = new JObject
            {
                ["oneHanded"] =
                    hero?.GetSkillValue(DefaultSkills.OneHanded) ?? 0,
                ["twoHanded"] =
                    hero?.GetSkillValue(DefaultSkills.TwoHanded) ?? 0,
                ["polearm"] =
                    hero?.GetSkillValue(DefaultSkills.Polearm) ?? 0,
                ["bow"] =
                    hero?.GetSkillValue(DefaultSkills.Bow) ?? 0,
                ["crossbow"] =
                    hero?.GetSkillValue(DefaultSkills.Crossbow) ?? 0,
                ["throwing"] =
                    hero?.GetSkillValue(DefaultSkills.Throwing) ?? 0,
                ["riding"] =
                    hero?.GetSkillValue(DefaultSkills.Riding) ?? 0,
                ["athletics"] =
                    hero?.GetSkillValue(DefaultSkills.Athletics) ?? 0,
                ["smithing"] =
                    hero?.GetSkillValue(DefaultSkills.Crafting) ?? 0,
                ["scouting"] =
                    hero?.GetSkillValue(DefaultSkills.Scouting) ?? 0,
                ["tactics"] =
                    hero?.GetSkillValue(DefaultSkills.Tactics) ?? 0,
                ["roguery"] =
                    hero?.GetSkillValue(DefaultSkills.Roguery) ?? 0,
                ["charm"] =
                    hero?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                ["leadership"] =
                    hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0,
                ["trade"] =
                    hero?.GetSkillValue(DefaultSkills.Trade) ?? 0,
                ["steward"] =
                    hero?.GetSkillValue(DefaultSkills.Steward) ?? 0,
                ["medicine"] =
                    hero?.GetSkillValue(DefaultSkills.Medicine) ?? 0,
                ["engineering"] =
                    hero?.GetSkillValue(DefaultSkills.Engineering) ?? 0
            };
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? string.Empty, ["name"] = hero?.Name?.ToString() ?? string.Empty,
                ["characterObjectId"] = hero?.CharacterObject?.StringId ?? string.Empty,
                ["clanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["clanName"] = hero?.Clan?.Name?.ToString() ?? string.Empty,
                ["clanLeaderId"] = hero?.Clan?.Leader?.StringId ?? string.Empty,
                ["isClanLeader"] = hero != null
                    && hero.Clan?.Leader == hero,
                ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                ["isLord"] = hero?.IsLord == true, ["isWanderer"] = hero?.IsWanderer == true, ["isNotable"] = hero?.IsNotable == true,
                ["isAlive"] = hero?.IsAlive == true,
                ["isAdult"] = hero != null
                    && ReignConversationEligibility.IsAdult(hero),
                ["isRuler"] = hero != null
                    && hero.Clan?.Kingdom?.Leader == hero,
                ["occupation"] =
                    hero?.Occupation.ToString()
                    ?? string.Empty,
                ["governorOfSettlementId"] =
                    hero?.GovernorOf?.Settlement?.StringId
                    ?? string.Empty,
                ["governorOfSettlementName"] =
                    hero?.GovernorOf?.Settlement?.Name?.ToString()
                    ?? string.Empty,
                ["isFemale"] = hero?.IsFemale == true, ["age"] = hero?.Age ?? 0f,
                ["clanTier"] = hero?.Clan?.Tier ?? 0,
                ["valor"] = hero?.GetTraitLevel(DefaultTraits.Valor) ?? 0,
                ["generosity"] = hero?.GetTraitLevel(DefaultTraits.Generosity) ?? 0,
                ["honor"] = hero?.GetTraitLevel(DefaultTraits.Honor) ?? 0,
                ["mercy"] = hero?.GetTraitLevel(DefaultTraits.Mercy) ?? 0,
                ["calculating"] = hero?.GetTraitLevel(DefaultTraits.Calculating) ?? 0,
                ["traits"] = traits,
                ["skills"] = skills,
                ["currentSettlementId"] = hero?.CurrentSettlement?.StringId ?? string.Empty
            };
        }

        private static async Task<bool> WaitForIndividualPresentationAsync(
            string runId, ReignIndividualChatScreenVM viewModel, Hero hero)
        {
            // Await the existing history/opener operation, never a second provider request.
            Task<bool> opening = viewModel.PresentationInitialization;
            if (await Task.WhenAny(opening, Task.Delay(TimeSpan.FromSeconds(300))).ConfigureAwait(false) != opening
                || !await opening.ConfigureAwait(false)) return false;
            return await ReignMainThread.InvokeAsync(() =>
                _armed && _activeRunId == runId && ReferenceEquals(_individualVm, viewModel)
                && viewModel.ConversationHero == hero && !viewModel.ActionBusy && viewModel.InputEnabled
                && (_presentation != "visible" || ReferenceEquals(viewModel, ReignIndividualChatScreenManager.ActiveViewModel)))
                .ConfigureAwait(false);
        }

        private static async Task<T> WaitForAsync<T>(Func<T> getter) where T : class
        {
            for (int i = 0; i < 100; i++)
            {
                T value = await ReignMainThread.InvokeAsync(getter).ConfigureAwait(false);
                if (value != null) return value;
                await Task.Delay(100).ConfigureAwait(false);
            }
            return null;
        }


        private static async Task<LiveCommandResult> ExecuteObservedPartyAgencyCommandAsync(
            JObject command, PartyAgencyExecutionStamp execution, string operation)
        {
            var previous = ReignServerClient.PartyAgencySendOrigin.Value;
            ReignServerClient.PartyAgencySendOrigin.Value = string.Equals(operation, "send", StringComparison.Ordinal)
                ? payload => AttachPartyAgencySendOrigin(payload, execution) : (Action<JObject>)null;
            try { return await ExecuteCommandAsync(command).ConfigureAwait(false); }
            finally { ReignServerClient.PartyAgencySendOrigin.Value = previous; }
        }

        private static void AttachPartyAgencySendOrigin(JObject payload, PartyAgencyExecutionStamp execution)
        {
            if (execution == null || !IsCurrentPartyAgencyExecution(execution)) return;
            payload["partyAgencySendOrigin"] = new JObject
            {
                ["schemaVersion"] = 1,
                ["runId"] = execution.RunId,
                ["commandId"] = execution.CommandId,
                ["gameInstanceId"] = execution.GameInstanceId,
                ["clientProcessInstanceId"] = execution.ClientProcessInstanceId,
                ["executionLeaseId"] = execution.ExecutionLeaseId,
                ["retryGeneration"] = execution.RetryGeneration
            };
        }

        internal sealed class PartyAgencyExecutionStamp
        {
            private readonly ReignMainThread.GameScope _nativeScope;

            internal PartyAgencyExecutionStamp(ReignMainThread.GameScope nativeScope, string campaignId,
                string gameInstanceId, string clientProcessInstanceId, string runId, string commandId,
                string executionLeaseId, int retryGeneration = 0)
            {
                _nativeScope = nativeScope;
                CampaignId = campaignId;
                GameInstanceId = gameInstanceId;
                ClientProcessInstanceId = clientProcessInstanceId;
                RunId = runId;
                CommandId = commandId;
                ExecutionLeaseId = executionLeaseId;
                RetryGeneration = retryGeneration;
            }

            internal string CampaignId { get; }
            internal string GameInstanceId { get; }
            internal string ClientProcessInstanceId { get; }
            internal string RunId { get; }
            internal string CommandId { get; }
            internal string ExecutionLeaseId { get; }
            internal int RetryGeneration { get; }

            internal bool IsCurrent(ReignMainThread.GameScope nativeScope, string campaignId,
                string gameInstanceId, string clientProcessInstanceId)
            {
                return _nativeScope != null && ReferenceEquals(_nativeScope, nativeScope)
                    && string.Equals(CampaignId, campaignId, StringComparison.Ordinal)
                    && string.Equals(GameInstanceId, gameInstanceId, StringComparison.Ordinal)
                    && string.Equals(ClientProcessInstanceId, clientProcessInstanceId, StringComparison.Ordinal);
            }

            internal bool MatchesIssuedScope(string campaignId, string gameInstanceId, string runId)
            {
                return !string.IsNullOrWhiteSpace(CampaignId) && !string.IsNullOrWhiteSpace(GameInstanceId)
                    && !string.IsNullOrWhiteSpace(RunId) && !string.IsNullOrWhiteSpace(CommandId)
                    && !string.IsNullOrWhiteSpace(ExecutionLeaseId) && RetryGeneration >= 0
                    && string.Equals(CampaignId, campaignId, StringComparison.Ordinal)
                    && string.Equals(GameInstanceId, gameInstanceId, StringComparison.Ordinal)
                    && string.Equals(RunId, runId, StringComparison.OrdinalIgnoreCase);
            }

            internal bool CanReplayFor(PartyAgencyExecutionStamp assignment)
            {
                // A retry can use a different transport lease. Retain the original
                // lease on the cached result rather than restamping its execution.
                return IsCurrent(assignment._nativeScope, assignment.CampaignId,
                        assignment.GameInstanceId, assignment.ClientProcessInstanceId)
                    && MatchesIssuedScope(assignment.CampaignId, assignment.GameInstanceId, assignment.RunId)
                    && string.Equals(CommandId, assignment.CommandId, StringComparison.OrdinalIgnoreCase)
                    && RetryGeneration == assignment.RetryGeneration;
            }
        }

        private sealed class PartyAgencyExecutionCache
        {
            private const int Capacity = 100;
            private readonly List<LiveCommandResult> _completed = new List<LiveCommandResult>();

            internal int Count
            {
                get { lock (_completed) return _completed.Count; }
            }

            internal void Clear()
            {
                lock (_completed) _completed.Clear();
            }

            internal bool TryGet(PartyAgencyExecutionStamp assignment, out LiveCommandResult result)
            {
                lock (_completed)
                {
                    result = _completed.Find(entry => entry.PartyAgencyExecution.CanReplayFor(assignment));
                    return result != null;
                }
            }

            internal void Remember(LiveCommandResult result)
            {
                lock (_completed)
                {
                    if (_completed.Exists(entry => entry.PartyAgencyExecution.CanReplayFor(result.PartyAgencyExecution)))
                        return;
                    _completed.Add(result);
                    if (_completed.Count > Capacity)
                        _completed.RemoveAt(0);
                }
            }
        }

        private sealed class LiveCommandResult
        {
            internal PartyAgencyExecutionStamp PartyAgencyExecution;
            public string Status = "completed";
            public string Message = string.Empty;
            public string Error = string.Empty;
            public JObject Data = new JObject();
            public List<string> CorrelationIds = new List<string>();
            public static LiveCommandResult Completed(string message, JObject data = null) { return new LiveCommandResult { Status = "completed", Message = message ?? string.Empty, Data = data ?? new JObject() }; }
            public static LiveCommandResult Failed(string error, JObject data = null) { return new LiveCommandResult { Status = "failed", Error = error ?? "Command failed.", Message = error ?? "Command failed.", Data = data ?? new JObject() }; }
            public static LiveCommandResult Cancelled(string message, JObject data = null) { return new LiveCommandResult { Status = "cancelled", Message = message ?? "Command cancelled.", Data = data ?? new JObject() }; }
        }
    }
}
