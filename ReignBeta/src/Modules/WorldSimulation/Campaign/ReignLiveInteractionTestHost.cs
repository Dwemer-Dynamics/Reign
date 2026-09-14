using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.PartyAgency;
using ReignBeta.Runtime;
using ReignBeta.Settings;
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

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static readonly string[] CoreSupportedModes =
            { "individual_chat", "party_chat", "social_event", "wilderness_event", "social_balance", "passive_world", "campaign_command", "government", "party_agency" };
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
        private static readonly Dictionary<string, JObject> PartyAgencyBaselines =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, JObject> PartyAgencyNativeEvidence =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private static string _partyAgencyFixtureTargetId = string.Empty;

        public static string GameInstanceId => _gameInstanceId;
        public static bool IsBusy => _commandInFlight || _sessionCleanupInFlight;
        public static int CompletedCommandCacheCount => CompletedCommandResults.Count;

        public static string[] GetSupportedModes()
        {
            List<string> modes = new List<string>(CoreSupportedModes);
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            if (settings?.CorrespondenceEnabled == true)
                modes.Add("correspondence");
            if (ReignBetaSettings.IsCourtSystemAvailable
                && ReignCourtCampaignBehavior.Instance?.IsRuleModeActive == true)
            {
                modes.Add("court_event");
                modes.Add("spymaster");
                if (ReignCourtCampaignBehavior.Instance.HasRoyalCommandAccess
                    && ReignCourtCampaignBehavior.Instance.ForeignAmbassadors.Any(posting => posting?.IsResident == true))
                    modes.Add("ambassador_official");
            }
            return modes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static void ApplicationTick(float dt)
        {
            ReignBetaSettings settings = ReignBetaSettings.Instance;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            bool playerReady = campaign != null
                && CharacterObject.PlayerCharacter != null;
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
                    out string eventId))
            {
                if (string.Equals(_activeMode, "social_balance", StringComparison.OrdinalIgnoreCase))
                {
                    ReignSocialBalanceHarnessCampaignBehavior.Instance?.RecordOverride(
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
                JObject response = await ReignServerClient.SubmitLiveTestHeartbeatAsync(_gameInstanceId, _activeRunId, _activeMode, _commandInFlight).ConfigureAwait(false);
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
                JObject response = await ReignServerClient.PollLiveTestCommandAsync(_gameInstanceId).ConfigureAwait(false);
                if (response.Value<bool?>("ok") != true || response.Value<bool?>("found") != true) return;
                JObject command = response["command"] as JObject;
                if (command == null) return;
                _commandInFlight = true;
                string runId = command.Value<string>("runId") ?? string.Empty;
                string commandId = command.Value<string>("commandId") ?? string.Empty;
                if (CompletedCommandResults.TryGetValue(commandId, out LiveCommandResult completed)
                    || TryGetDurableSocialBalanceResult(command, out completed))
                {
                    if (completed.Status == "completed")
                        ActivateHarnessRun(command);
                    await ReportCommandResultAsync(runId, commandId, completed).ConfigureAwait(false);
                    return;
                }
                await ReignServerClient.AcknowledgeLiveTestCommandAsync(_gameInstanceId, runId, commandId, "accepted", "Command accepted by the loaded campaign.").ConfigureAwait(false);
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
                    result = await ExecuteCommandAsync(command).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ReignLog.Exception("Live interaction command execution", ex);
                    result = LiveCommandResult.Failed(ex.Message);
                }
                if (result.Status == "completed")
                    ActivateHarnessRun(command);
                RememberCommandResult(commandId, result);
                RememberDurableSocialBalanceResult(command, result);
                await ReportCommandResultAsync(runId, commandId, result).ConfigureAwait(false);
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

        private static Task<JObject> ReportCommandResultAsync(string runId, string commandId, LiveCommandResult result)
        {
            return ReignServerClient.ReportLiveTestCommandAsync(
                _gameInstanceId, runId, commandId, result.Status, result.Message, result.Error, result.Data, result.CorrelationIds);
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
                int inFlight = ReignServerClient.CorrespondenceTickInFlight;
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
                    ["inFlight"] = ReignServerClient.CorrespondenceTickInFlight
                });
        }

        private static LiveCommandResult PartyAgencyTest(JObject command)
        {
            ReignTemporaryPartyGuestCampaignBehavior behavior = ReignTemporaryPartyGuestCampaignBehavior.Instance;
            if (behavior == null)
                return LiveCommandResult.Failed("The temporary noble guest campaign behavior is unavailable.");
            JObject snapshot = behavior.BuildHarnessSnapshot();
            JObject patches = ReignTemporaryPartyGuestPatches.BuildDiagnostics();
            snapshot["patches"] = patches;
            string profile = command?.Value<string>("profile") ?? "contracts";
            string caseId = command?.Value<string>("caseId") ?? string.Empty;
            string variant = command?.Value<string>("variant") ?? string.Empty;
            string phase = command?.Value<string>("phase") ?? "observe";
            string targetSearch = command?.Value<string>("targetSearch") ?? string.Empty;
            string runId = command?.Value<string>("runId") ?? _activeRunId ?? string.Empty;
            snapshot["profile"] = profile;
            snapshot["caseId"] = caseId;
            snapshot["variant"] = variant;
            snapshot["observationPhase"] = phase;
            snapshot["naturalLanguageOnly"] = true;
            snapshot["directDialogueActionInvocation"] = false;
            snapshot["contractAssertions"] = new JObject
            {
                ["openEndedReviewIsFiveDays"] = ReignTemporaryPartyGuestCampaignBehavior.OpenEndedReviewDays == 5f,
                ["fixedTermRangeIsOneToThirtyDays"] = ReignTemporaryPartyGuestCampaignBehavior.MinimumFixedTermDays == 1f
                    && ReignTemporaryPartyGuestCampaignBehavior.MaximumFixedTermDays == 30f,
                ["returnRangeIsOneToTwelveHours"] = ReignTemporaryPartyGuestCampaignBehavior.MinimumReturnHours == 1f
                    && ReignTemporaryPartyGuestCampaignBehavior.MaximumReturnHours == 12f,
                ["allLifecyclePhasesPresent"] = Enum.GetValues(typeof(ReignTemporaryGuestPhase)).Length == 6,
                ["campHooksActive"] = patches.Value<bool?>("active") == true
                    && (patches.Value<int?>("patchedHookCount") ?? 0) >= 10
            };
            if (profile == "preflight" || profile == "contracts")
                snapshot["candidateMatrix"] = BuildPartyAgencyCandidateMatrix(behavior);

            Hero target = ResolvePartyAgencyTarget(targetSearch);
            ReignTemporaryPartyGuestRecord targetRecord = behavior.Records.FirstOrDefault(record =>
                record != null && (string.Equals(record.HeroStringId, target?.StringId,
                    StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(targetSearch)
                        && string.Equals(record.HeroStringId, targetSearch,
                            StringComparison.OrdinalIgnoreCase))));
            JObject targetEvidence = BuildPartyAgencyTargetEvidence(behavior, target, targetRecord);
            snapshot["target"] = targetEvidence;

            string baselineKey = runId + "|" + caseId + "|" + variant;
            JObject nativeEvidence = PartyAgencyNativeEvidence.TryGetValue(
                baselineKey, out JObject capturedNativeEvidence)
                ? (JObject)capturedNativeEvidence.DeepClone() : null;
            if (nativeEvidence != null) snapshot["nativeEvidence"] = nativeEvidence;
            if (phase.Equals("before", StringComparison.OrdinalIgnoreCase))
                PartyAgencyBaselines[baselineKey] = (JObject)snapshot.DeepClone();
            JObject baseline = PartyAgencyBaselines.TryGetValue(baselineKey, out JObject captured)
                ? captured : null;
            JObject expected = command?["expected"] as JObject ?? new JObject();
            JObject caseAssertions = phase.Equals("before", StringComparison.OrdinalIgnoreCase)
                ? new JObject()
                : EvaluatePartyAgencyExpected(expected, snapshot, targetEvidence, baseline,
                    nativeEvidence);
            snapshot["caseAssertions"] = caseAssertions;

            bool ok = snapshot["contractAssertions"].Children<JProperty>()
                .All(property => property.Value.Value<bool>())
                && caseAssertions.Children<JProperty>().All(property => property.Value.Value<bool>());
            return ok
                ? LiveCommandResult.Completed("Temporary noble guest contracts and runtime state were captured.", snapshot)
                : LiveCommandResult.Failed("One or more temporary noble guest harness contracts failed.", snapshot);
        }

        private static JObject EvaluatePartyAgencyExpected(JObject expected, JObject snapshot,
            JObject target, JObject baseline, JObject nativeEvidence)
        {
            JObject assertions = new JObject();
            if (expected == null || !expected.Properties().Any()) return assertions;
            int recordCount = snapshot.Value<int?>("recordCount")
                ?? snapshot.Value<int?>("activeCount") ?? 0;
            int baselineCount = baseline?.Value<int?>("recordCount")
                ?? baseline?.Value<int?>("activeCount") ?? recordCount;
            if (expected.Property("recordDelta") != null)
                assertions["recordDelta"] = recordCount - baselineCount
                    == expected.Value<int>("recordDelta");
            string recordExpectation = expected.Value<string>("record") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(recordExpectation))
                assertions["recordPresence"] = recordExpectation.Equals("present", StringComparison.OrdinalIgnoreCase)
                    ? target.Value<bool?>("recordPresent") == true
                    : recordExpectation.Equals("absent", StringComparison.OrdinalIgnoreCase)
                        && target.Value<bool?>("recordPresent") != true;
            string expectedPhase = expected.Value<string>("phase") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedPhase))
                assertions["phase"] = string.Equals(target.Value<string>("phase"), expectedPhase,
                    StringComparison.OrdinalIgnoreCase);
            if (expected["phaseOneOf"] is JArray phases)
                assertions["phaseOneOf"] = phases.Values<string>().Any(value =>
                    string.Equals(value, target.Value<string>("phase"), StringComparison.OrdinalIgnoreCase));
            string expectedTermKind = expected.Value<string>("termKind") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedTermKind))
                assertions["termKind"] = string.Equals(target.Value<string>("termKind"),
                    expectedTermKind, StringComparison.OrdinalIgnoreCase);
            if (expected.Property("fixedTermDays") != null)
                assertions["fixedTermDays"] = Math.Abs(
                    (target.Value<double?>("fixedTermDays") ?? double.NaN)
                    - expected.Value<double>("fixedTermDays")) < 0.01d;
            if (expected.Property("reviewIntervalDays") != null)
                assertions["reviewIntervalDays"] = Math.Abs(
                    (target.Value<double?>("reviewIntervalDays") ?? double.NaN)
                    - expected.Value<double>("reviewIntervalDays")) < 0.01d;
            foreach (string flag in new[]
            {
                "identityPreserved", "campProtected", "hostilityWarningAcknowledged",
                "temporarilyExcludedFromBattle", "banishedToPlayerClan", "nativeRecoveryReleased",
                "noDuplicateHeroOrParty", "stateEligible"
            })
                if (expected.Property(flag) != null)
                {
                    bool actual = target.Value<bool?>(flag) == true;
                    if (flag == "temporarilyExcludedFromBattle"
                        && nativeEvidence?.Value<bool?>(flag) == true)
                        actual = true;
                    assertions[flag] = actual == expected.Value<bool>(flag);
                }
            if (expected.Property("minimumReviewDueCount") != null)
                assertions["minimumReviewDueCount"] = snapshot.Value<int?>("reviewDueCount")
                    >= expected.Value<int>("minimumReviewDueCount");
            if (expected.Property("timePaused") != null)
            {
                bool paused = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
                assertions["timePaused"] = paused == expected.Value<bool>("timePaused");
            }
            if (expected.Value<bool?>("externalEvidenceRequired") == true
                || expected.Value<bool?>("unrelatedStateUnchanged") == true
                || expected.Value<bool?>("allPriorRequiredInstancesPassed") == true)
            {
                assertions["nativeEvidenceReceipt"] = nativeEvidence?.Value<bool?>("ok") == true
                    && !string.IsNullOrWhiteSpace(nativeEvidence.Value<string>("receiptId"));
            }
            return assertions;
        }

        private static JObject BuildPartyAgencyTargetEvidence(
            ReignTemporaryPartyGuestCampaignBehavior behavior, Hero hero,
            ReignTemporaryPartyGuestRecord record)
        {
            MobileParty source = record == null ? null : ReignObjectResolver.FindParty(record.SourcePartyStringId);
            bool recordPresent = record != null;
            bool stateEligible = hero != null && hero != Hero.MainHero && hero.IsLord
                && hero.CompanionOf == null && ReignConversationEligibility.IsAdultLivingNpc(hero)
                && !hero.IsPrisoner && hero.Clan != null && hero.Clan != Clan.PlayerClan
                && hero.GovernorOf == null
                && hero.PartyBelongedTo?.IsCurrentlyUsedByAQuest != true
                && hero.PartyBelongedTo?.MapEvent == null
                && hero.PartyBelongedTo?.SiegeEvent == null
                && hero.PartyBelongedTo?.BesiegerCamp == null;
            int heroMatches = string.IsNullOrWhiteSpace(record?.HeroStringId) ? 0
                : Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                    .Count(candidate => candidate != null && string.Equals(candidate.StringId,
                        record.HeroStringId, StringComparison.OrdinalIgnoreCase));
            int partyMatches = string.IsNullOrWhiteSpace(record?.SourcePartyStringId) ? 0
                : MobileParty.All.Count(party => party != null && string.Equals(party.StringId,
                    record.SourcePartyStringId, StringComparison.OrdinalIgnoreCase));
            bool completed = record?.Phase == ReignTemporaryGuestPhase.Completed;
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? record?.HeroStringId ?? string.Empty,
                ["heroName"] = hero?.Name?.ToString() ?? string.Empty,
                ["recordPresent"] = recordPresent,
                ["phase"] = record?.Phase.ToString() ?? string.Empty,
                ["termKind"] = record?.TermKind.ToString() ?? string.Empty,
                ["startedDay"] = record?.StartedDay ?? 0f,
                ["reviewDueDay"] = record?.ReviewDueDay ?? 0f,
                ["fixedTermDays"] = record?.FixedTermDays ?? 0f,
                ["reviewIntervalDays"] = recordPresent
                    ? record.ReviewDueDay - record.StartedDay : 0f,
                ["stateEligible"] = stateEligible,
                ["identityPreserved"] = !recordPresent || (hero != null
                    && string.Equals(record.OriginalClanStringId, hero.Clan?.StringId ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalKingdomStringId,
                        hero.Clan?.Kingdom?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalCompanionClanStringId,
                        hero.CompanionOf?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)),
                ["campProtected"] = !recordPresent || string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                    || behavior.IsProtectedCamp(source),
                ["hostilityWarningAcknowledged"] = record?.HostilityWarningAcknowledged == true,
                ["temporarilyExcludedFromBattle"] = record?.TemporarilyExcludedFromBattle == true,
                ["banishedToPlayerClan"] = recordPresent && hero?.Clan == Clan.PlayerClan
                    && !string.Equals(record.OriginalClanStringId, Clan.PlayerClan?.StringId,
                        StringComparison.OrdinalIgnoreCase),
                ["nativeRecoveryReleased"] = completed
                    && (source == null || !behavior.IsProtectedCamp(source)),
                ["noDuplicateHeroOrParty"] = (!recordPresent || heroMatches == 1)
                    && (string.IsNullOrWhiteSpace(record?.SourcePartyStringId) || partyMatches <= 1),
                ["sourcePartyId"] = record?.SourcePartyStringId ?? string.Empty,
                ["sourcePartyExists"] = source != null,
                ["currentClanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["currentKingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["inPlayerParty"] = hero?.PartyBelongedTo == MobileParty.MainParty,
                ["isDead"] = hero?.IsDead == true,
                ["isPrisoner"] = hero?.IsPrisoner == true
            };
        }

        private static JObject BuildPartyAgencyCandidateMatrix(
            ReignTemporaryPartyGuestCampaignBehavior behavior)
        {
            List<Hero> nobles = Hero.AllAliveHeroes.Where(hero => hero != null
                && hero != Hero.MainHero && hero.IsLord).ToList();
            Func<Hero, bool> eligible = hero => BuildPartyAgencyTargetEvidence(behavior, hero,
                behavior.Records.FirstOrDefault(record => record?.HeroStringId == hero.StringId))
                .Value<bool>("stateEligible");
            return new JObject
            {
                ["livingNobleCount"] = nobles.Count,
                ["stateEligibleCount"] = nobles.Count(eligible),
                ["partylessCount"] = nobles.Count(hero => eligible(hero) && hero.PartyBelongedTo == null),
                ["ordinaryPartyMemberCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo != null && hero.PartyBelongedTo.LeaderHero != hero),
                ["partyLeaderCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.LeaderHero == hero),
                ["armyMemberCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.Army != null
                    && hero.PartyBelongedTo.Army.LeaderParty != hero.PartyBelongedTo),
                ["armyLeaderCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.Army?.LeaderParty == hero.PartyBelongedTo),
                ["clanLeaderCount"] = nobles.Count(hero => eligible(hero) && hero.Clan?.Leader == hero),
                ["kingdomRulerCount"] = nobles.Count(hero => eligible(hero)
                    && hero.Clan?.Kingdom?.Leader == hero),
                ["governorCount"] = nobles.Count(hero => hero.GovernorOf != null),
                ["prisonerCount"] = nobles.Count(hero => hero.IsPrisoner),
                ["questBoundCount"] = nobles.Count(hero => hero.PartyBelongedTo?.IsCurrentlyUsedByAQuest == true)
            };
        }

        private static Hero ResolvePartyAgencyTarget(string search)
        {
            if (string.Equals(search, "@party_agency_fixture", StringComparison.OrdinalIgnoreCase))
                return FindPartyAgencyFixtureHero();
            if (string.IsNullOrWhiteSpace(search)) return null;
            Hero exact = ReignObjectResolver.FindHero(search.Trim());
            if (exact != null) return exact;
            return RankHeroes(Hero.AllAliveHeroes.Where(hero => hero != null), search).FirstOrDefault();
        }

        private static async Task<LiveCommandResult> SaveCheckpointAsync(JObject command)
        {
            if (await ReignMainThread.InvokeAsync(() => TaleWorlds.MountAndBlade.Mission.Current != null).ConfigureAwait(false))
                return LiveCommandResult.Failed("Leave the native mission before checkpointing; no save was queued.",
                    new JObject { ["nativeMissionActive"] = true, ["nativeSaveQueued"] = false });
            JObject visiblePartyConversation = await FinishVisiblePartyConversationForCheckpointAsync().ConfigureAwait(false);
            if (visiblePartyConversation.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(visiblePartyConversation.Value<string>("error"), visiblePartyConversation);
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            string acknowledgedDiplomacyEventId = string.Empty;
            bool acknowledgedDiplomacyAnnouncement = await ReignMainThread.InvokeAsync(() =>
                ReignDiplomacyAnnouncementScreenManager.TryAcknowledgeForLiveHarness(
                    false,
                    out acknowledgedDiplomacyEventId)).ConfigureAwait(false);

            string requested = command.Value<string>("saveName") ?? string.Empty;
            string saveName = SanitizeSaveName(string.IsNullOrWhiteSpace(requested)
                ? "Reign_Conversation_Qualification_A"
                : requested);
            if (string.IsNullOrWhiteSpace(saveName))
                return LiveCommandResult.Failed("A valid save name is required.");

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
                    SaveDiagnostics(saveName, "campaign_pause_failed"));
            }

            DateTime diplomacyDeadline = commandDeadline
                - AutomationSaveCompletionReserve;
            while (ReignWorldDiplomacyCampaignBehavior.Instance?.HasInFlightRequest == true
                && DateTime.UtcNow < diplomacyDeadline)
            {
                await Task.Delay(250).ConfigureAwait(false);
            }
            if (ReignWorldDiplomacyCampaignBehavior.Instance?.HasInFlightRequest == true)
            {
                return LiveCommandResult.Failed(
                    "Diplomacy did not become idle before the checkpoint deadline; Bannerlord's native save was not started.",
                    SaveDiagnostics(saveName, "diplomacy_preflight_timeout"));
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

            long historySequence = ReignWorldHistoryCampaignBehavior.Instance?.Sequence ?? 0L;
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
                    diagnostics);
            }

            bool queued = await ReignMainThread.InvokeAsync(() =>
            {
                if (TaleWorlds.CampaignSystem.Campaign.Current?.SaveHandler == null
                    || TaleWorlds.CampaignSystem.Campaign.Current.SaveHandler.IsSaving)
                    return false;
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
                return LiveCommandResult.Failed(
                    "Bannerlord is already saving or no campaign save handler is available.",
                    SaveDiagnostics(saveName, "queue_rejected"));
            }

            long expectedSequence = (nativeBefore.Value<long?>("sequence") ?? 0L) + 1L;
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
                    _lastAutomationSaveName = saveName;
                    _lastAutomationSaveCompletedUtc = DateTime.UtcNow;
                    return LiveCommandResult.Completed("Bannerlord save and Save Sync checkpoint completed.", new JObject
                    {
                        ["saveName"] = saveName,
                        ["worldDay"] = CampaignTime.Now.ToDays,
                        ["gameInstanceId"] = _gameInstanceId,
                        ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                        ["saveSyncAligned"] = true,
                        ["escapeMenuRecovered"] = escapeMenuRecovered,
                        ["visiblePartyConversation"] = visiblePartyConversation,
                        ["acknowledgedDiplomacyAnnouncement"] = acknowledgedDiplomacyAnnouncement,
                        ["acknowledgedDiplomacyEventId"] = acknowledgedDiplomacyEventId,
                        ["nativeSave"] = native,
                        ["saveFinalization"] = finalization
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
                        ReignWorldDiplomacyCampaignBehavior.Instance?.EvaluationInFlight == true,
                    ["announcementPollInFlight"] =
                        ReignWorldDiplomacyCampaignBehavior.Instance?.AnnouncementPollInFlight == true,
                    ["pressurePollInFlight"] =
                        ReignWorldDiplomacyCampaignBehavior.Instance?.PressurePollInFlight == true,
                    ["inFlight"] =
                        ReignWorldDiplomacyCampaignBehavior.Instance?.HasInFlightRequest == true
                },
                ["campaignPreparation"] =
                    ReignCampaignPreparationCampaignBehavior.Instance?.Snapshot()
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

        private static async Task<LiveCommandResult> PreparePartyFixtureAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            int desiredCount = Math.Max(2, Math.Min(
                5, command.Value<int?>("desiredCount") ?? 5));
            List<string> searches = ReadSearches(command);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                MobileParty party = MobileParty.MainParty;
                if (party == null)
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = "The main party is unavailable."
                    };

                List<Hero> globalPool = Hero.AllAliveHeroes
                    .Where(hero => IsLivingNpc(hero)
                        && !hero.IsPrisoner
                        && !hero.IsWounded)
                    .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
                List<Hero> requested = new List<Hero>();
                foreach (string search in searches)
                {
                    Hero match = RankHeroes(
                        globalPool.Where(hero => requested.All(existing =>
                            !string.Equals(existing.StringId, hero.StringId,
                                StringComparison.OrdinalIgnoreCase))),
                        search).FirstOrDefault();
                    if (match != null) requested.Add(match);
                }
                if (requested.Count == 0)
                {
                    requested = globalPool
                        .OrderBy(hero => hero.IsLord ? 1 : 0)
                        .ThenBy(hero => hero.IsWanderer ? 0 : 1)
                        .ThenBy(hero => hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Take(12)
                        .ToList();
                }

                JArray added = new JArray();
                JArray rejected = new JArray();
                foreach (Hero hero in requested)
                {
                    if (ReignPartyChatSession.GetAvailableConversationHeroes().Count
                        >= desiredCount)
                        break;
                    if (hero.PartyBelongedTo == party)
                    {
                        added.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["status"] = "already_present"
                        });
                        continue;
                    }

                    try
                    {
                        string originSettlementId =
                            hero.CurrentSettlement?.StringId ?? string.Empty;
                        AddHeroToPartyAction.Apply(hero, party, false);
                        bool present = hero.PartyBelongedTo == party
                            || party.MemberRoster.GetTroopCount(
                                hero.CharacterObject) > 0;
                        if (!present)
                            throw new InvalidOperationException(
                                "Bannerlord did not place the hero in the main party.");
                        added.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["originSettlementId"] = originSettlementId,
                            ["status"] = "added"
                        });
                    }
                    catch (Exception ex)
                    {
                        rejected.Add(new JObject
                        {
                            ["heroId"] = hero.StringId,
                            ["name"] = hero.Name?.ToString() ?? hero.StringId,
                            ["error"] = ex.Message
                        });
                    }
                }

                List<Hero> eligible =
                    ReignPartyChatSession.GetAvailableConversationHeroes();
                return new JObject
                {
                    ["ok"] = eligible.Count >= desiredCount,
                    ["desiredCount"] = desiredCount,
                    ["eligibleCount"] = eligible.Count,
                    ["eligibleHeroIds"] = new JArray(
                        eligible.Select(hero => hero.StringId).Take(desiredCount)),
                    ["added"] = added,
                    ["rejected"] = rejected,
                    ["disposableSaveFixture"] = true
                };
            }).ConfigureAwait(false);

            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed(
                    "The disposable qualification party is ready through Bannerlord's native party roster.",
                    result)
                : LiveCommandResult.Failed(
                    "Five party-chat-eligible heroes could not be prepared from the supplied fixture candidates.",
                    result);
        }

        private static async Task<LiveCommandResult> PrepareAuthorityFixtureAsync(
            JObject command)
        {
            if (command?.Value<bool?>("confirmDisposableCampaign") != true)
                return LiveCommandResult.Failed(
                    "prepare_authority_fixture requires confirmDisposableCampaign=true because native kingdom, clan, family, settlement, and party state are changed until the baseline save is reloaded.");
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);

            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                try
                {
                    ReignBetaDebugActions.SetupPlayerKingdomTestScenario();
                    Hero player = Hero.MainHero;
                    Clan playerClan = Clan.PlayerClan;
                    Kingdom kingdom = playerClan?.Kingdom;
                    MobileParty party = MobileParty.MainParty;
                    Settlement seat = Settlement.CurrentSettlement
                        ?? party?.CurrentSettlement;
                    if (player == null || playerClan == null
                        || kingdom == null || party == null || seat == null
                        || seat.Town == null)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "The native royal-court setup did not produce a player kingdom, main party, and occupied fortification."
                        };
                    if (kingdom.RulingClan != playerClan
                        || kingdom.Leader != player
                        || seat.OwnerClan != playerClan)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "The native royal-court setup did not make the player the ruler and personal owner of the occupied fortification."
                        };

                    List<Hero> independentWanderers = Hero.AllAliveHeroes
                        .Where(hero => IsLivingNpc(hero)
                            && hero.IsWanderer
                            && !hero.IsPrisoner
                            && !hero.IsWounded
                            && hero.Clan != playerClan
                            && hero.CompanionOf != playerClan)
                        .OrderBy(hero =>
                            hero.PartyBelongedTo == null ? 0 : 1)
                        .ThenBy(hero =>
                            hero.CurrentSettlement == seat ? 0 : 1)
                        .ThenBy(hero =>
                            hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (independentWanderers.Count < 3)
                        return new JObject
                        {
                            ["ok"] = false,
                            ["error"] =
                                "Three adult independent wanderers are required for separate same-clan, immediate-family, and governor witnesses."
                        };

                    Hero clanWitness = independentWanderers[0];
                    Hero familyWitness = independentWanderers[1];
                    Hero governorWitness = independentWanderers[2];
                    clanWitness.Clan = playerClan;
                    AdoptHeroAction.Apply(familyWitness);
                    governorWitness.Clan = playerClan;

                    EnsureAuthorityFixturePartyMember(clanWitness, party);
                    EnsureAuthorityFixturePartyMember(familyWitness, party);

                    if (governorWitness.PartyBelongedTo != null)
                    {
                        governorWitness.PartyBelongedTo.MemberRoster
                            .AddToCounts(
                                governorWitness.CharacterObject, -1);
                    }
                    if (governorWitness.CurrentSettlement != seat)
                        EnterSettlementAction.ApplyForCharacterOnly(
                            governorWitness, seat);
                    ChangeGovernorAction.Apply(
                        seat.Town, governorWitness);

                    List<Hero> vassalWitnesses = kingdom.Clans
                        .Where(clan => clan != null
                            && clan != playerClan
                            && !clan.IsEliminated
                            && !clan.IsClanTypeMercenary
                            && !clan.IsUnderMercenaryService
                            && clan.Leader != null
                            && IsLivingNpc(clan.Leader)
                            && !clan.Leader.IsPrisoner)
                        .Select(clan => clan.Leader)
                        .Distinct()
                        .OrderBy(hero =>
                            hero.Name?.ToString() ?? hero.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Take(2)
                        .ToList();
                    foreach (Hero witness in vassalWitnesses)
                        EnsureAuthorityFixturePartyMember(witness, party);

                    List<Kingdom> enemyKingdoms =
                        (kingdom.FactionsAtWarWith
                            ?? Enumerable.Empty<IFaction>())
                        .Where(faction =>
                            faction != null
                            && faction.IsKingdomFaction)
                        .Select(faction => faction as Kingdom)
                        .Where(enemy =>
                            enemy != null && enemy != kingdom
                            && !enemy.IsEliminated)
                        .GroupBy(enemy => enemy.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .Select(group => group.First())
                        .OrderBy(enemy => enemy.StringId,
                            StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    List<Hero> partyWitnesses = new[]
                        {
                            familyWitness, clanWitness
                        }
                        .Concat(vassalWitnesses)
                        .Where(hero =>
                            hero?.PartyBelongedTo == party)
                        .Distinct()
                        .Take(5)
                        .ToList();

                    bool ready = familyWitness.Clan == playerClan
                        && (familyWitness.Father == player
                            || familyWitness.Mother == player)
                        && clanWitness.Clan == playerClan
                        && clanWitness.Father != player
                        && clanWitness.Mother != player
                        && seat.Town.Governor == governorWitness
                        && governorWitness.GovernorOf == seat.Town
                        && vassalWitnesses.Count >= 2
                        && partyWitnesses.Count >= 4;
                    return new JObject
                    {
                        ["ok"] = ready,
                        ["error"] = ready
                            ? string.Empty
                            : "One or more native authority-fixture invariants did not hold after setup.",
                        ["disposableSaveFixture"] = true,
                        ["requiresBaselineReload"] = true,
                        ["playerHeroId"] = player.StringId,
                        ["playerName"] =
                            player.Name?.ToString() ?? string.Empty,
                        ["playerClanId"] = playerClan.StringId,
                        ["playerClanTier"] = playerClan.Tier,
                        ["playerKingdomId"] = kingdom.StringId,
                        ["playerKingdomName"] =
                            kingdom.InformalName?.ToString()
                            ?? kingdom.Name?.ToString()
                            ?? string.Empty,
                        ["playerIsRuler"] =
                            kingdom.RulingClan == playerClan
                            && kingdom.Leader == player,
                        ["settlementId"] = seat.StringId,
                        ["settlementName"] =
                            seat.Name?.ToString() ?? string.Empty,
                        ["settlementOwnerClanId"] =
                            seat.OwnerClan?.StringId ?? string.Empty,
                        ["settlementOwnerClanName"] =
                            seat.OwnerClan?.Name?.ToString()
                            ?? string.Empty,
                        ["settlementOwnerHeroId"] =
                            seat.OwnerClan?.Leader?.StringId
                            ?? string.Empty,
                        ["settlementGovernorHeroId"] =
                            seat.Town.Governor?.StringId
                            ?? string.Empty,
                        ["settlementGovernorName"] =
                            seat.Town.Governor?.Name?.ToString()
                            ?? string.Empty,
                        ["familyObserver"] =
                            HeroTargetJson(familyWitness),
                        ["clanObserver"] =
                            HeroTargetJson(clanWitness),
                        ["governorObserver"] =
                            HeroTargetJson(governorWitness),
                        ["vassalObservers"] = new JArray(
                            vassalWitnesses.Select(
                                HeroTargetJson)),
                        ["partyObserverIds"] = new JArray(
                            partyWitnesses.Select(
                                hero => hero.StringId)),
                        ["currentEnemyKingdomCount"] =
                            enemyKingdoms.Count,
                        ["enemyKingdomIds"] = new JArray(
                            enemyKingdoms.Select(
                                enemy => enemy.StringId)),
                        ["nativeInvariant"] = new JObject
                        {
                            ["sameClanWitness"] =
                                clanWitness.Clan == playerClan,
                            ["immediateFamilyWitness"] =
                                familyWitness.Clan == playerClan
                                && (familyWitness.Father == player
                                    || familyWitness.Mother == player),
                            ["localGovernorWitness"] =
                                seat.Town.Governor
                                    == governorWitness,
                            ["subjectVassalWitnessCount"] =
                                vassalWitnesses.Count,
                            ["playerOwnsSettlement"] =
                                seat.OwnerClan == playerClan,
                            ["playerGovernsSettlement"] =
                                seat.Town.Governor == player,
                            ["playerRulesKingdom"] =
                                kingdom.RulingClan == playerClan
                                && kingdom.Leader == player
                        }
                    };
                }
                catch (Exception ex)
                {
                    ReignLog.Exception(
                        "Live authority fixture setup", ex);
                    return new JObject
                    {
                        ["ok"] = false,
                        ["error"] = ex.Message,
                        ["requiresBaselineReload"] = true
                    };
                }
            }).ConfigureAwait(false);

            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed(
                    "The disposable sovereign, settlement, diplomacy, same-clan, and immediate-family authority fixture is ready.",
                    result)
                : LiveCommandResult.Failed(
                    result.Value<string>("error")
                        ?? "Authority fixture setup failed.",
                    result);
        }

        private static void EnsureAuthorityFixturePartyMember(
            Hero hero,
            MobileParty party)
        {
            if (hero == null || party == null
                || hero.PartyBelongedTo == party)
                return;
            AddHeroToPartyAction.Apply(
                hero, party, false);
            if (hero.PartyBelongedTo != party
                && party.MemberRoster.GetTroopCount(
                    hero.CharacterObject) <= 0)
                throw new InvalidOperationException(
                    "Bannerlord did not place authority witness "
                    + (hero.Name?.ToString()
                        ?? hero.StringId)
                    + " in the main party.");
        }

        private static async Task<LiveCommandResult> PrepareWildernessAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                MobileParty party = MobileParty.MainParty;
                Settlement origin = Settlement.CurrentSettlement ?? party?.CurrentSettlement;
                if (party == null || origin == null)
                    return new JObject { ["ok"] = false, ["error"] = "The main party must be inside a settlement before wilderness setup." };
                List<Hero> requested = ResolveHeroesForMode("party_chat", ReadSearches(command))
                    .Where(hero => hero != null && hero != Hero.MainHero && hero.IsAlive && !hero.IsPrisoner && !hero.IsWounded)
                    .Take(5).ToList();
                if (requested.Count < 2)
                    return new JObject { ["ok"] = false, ["error"] = "At least two eligible temporary wilderness participants are required." };

                _wildernessOriginSettlementId = origin.StringId;
                TemporaryWildernessHeroIds.Clear();
                foreach (Hero hero in requested)
                {
                    if (hero.PartyBelongedTo != party)
                    {
                        AddHeroToPartyAction.Apply(hero, party, false);
                        TemporaryWildernessHeroIds.Add(hero.StringId);
                    }
                }
                LeaveSettlementAction.ApplyForParty(party);
                MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
                if (mapState?.AtMenu == true) mapState.ExitMenuMode();
                return new JObject
                {
                    ["ok"] = true,
                    ["originSettlementId"] = _wildernessOriginSettlementId,
                    ["temporaryHeroIds"] = new JArray(TemporaryWildernessHeroIds),
                    ["mainPartyHeroCount"] = ReignPartyChatSession.GetMainPartyHeroes().Count
                };
            }).ConfigureAwait(false);
            if (result.Value<bool?>("ok") != true)
                return LiveCommandResult.Failed(result.Value<string>("error") ?? "Wilderness setup failed.", result);

            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                bool ready = await ReignMainThread.InvokeAsync(() =>
                {
                    MapState state = Game.Current?.GameStateManager?.ActiveState as MapState;
                    return MobileParty.MainParty?.CurrentSettlement == null
                        && Settlement.CurrentSettlement == null
                        && state != null && !state.AtMenu
                        && TaleWorlds.CampaignSystem.Campaign.Current?.CurrentMenuContext == null;
                }).ConfigureAwait(false);
                if (ready) return LiveCommandResult.Completed("The disposable party is ready on the open map for production wilderness events.", result);
                await Task.Delay(250).ConfigureAwait(false);
            }
            return LiveCommandResult.Failed("The party left the settlement but did not reach an eligible open-map state.", result);
        }

        private static async Task<LiveCommandResult> RestoreSettlementAsync()
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Settlement origin = Settlement.Find(_wildernessOriginSettlementId);
                MobileParty party = MobileParty.MainParty;
                if (origin == null || party == null)
                    return new JObject { ["ok"] = false, ["error"] = "The recorded wilderness origin settlement is unavailable." };
                if (party.CurrentSettlement == null) EnterSettlementAction.ApplyForParty(party, origin);
                foreach (string heroId in TemporaryWildernessHeroIds.ToList())
                {
                    Hero hero = Hero.FindFirst(value => value != null && value.StringId == heroId);
                    if (hero == null) continue;
                    if (hero.PartyBelongedTo == party) party.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                    EnterSettlementAction.ApplyForCharacterOnly(hero, origin);
                }
                JArray restored = new JArray(TemporaryWildernessHeroIds);
                TemporaryWildernessHeroIds.Clear();
                return new JObject
                {
                    ["ok"] = true,
                    ["settlementId"] = origin.StringId,
                    ["restoredHeroIds"] = restored,
                    ["partyCurrentSettlementId"] = party.CurrentSettlement?.StringId ?? string.Empty
                };
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The disposable wilderness party and origin settlement were restored.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Settlement restoration failed.", result);
        }

        private static async Task<LiveCommandResult> PrepareManipulationFixtureAsync(JObject command)
        {
            if (_individualVm != null || _partyVm != null || _eventVm != null)
                await CloseSessionAsync(false).ConfigureAwait(false);
            int desiredTier = Math.Max(0, Math.Min(6,
                command?.Value<int?>("playerClanTier") ?? 6));
            int desiredGold = Math.Max(0, Math.Min(100000000,
                command?.Value<int?>("playerGold") ?? (Hero.MainHero?.Gold ?? 0)));
            string wealthEvidence = (command?.Value<string>(
                "wealthEvidence") ?? "demonstrated")
                .Trim().ToLowerInvariant();
            if (wealthEvidence != "demonstrated"
                && wealthEvidence != "hidden"
                && wealthEvidence != "visible_presentation")
                return LiveCommandResult.Failed(
                    "wealthEvidence must be demonstrated, hidden, or visible_presentation.");
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Hero player = Hero.MainHero;
                Clan clan = Clan.PlayerClan;
                if (player == null || clan == null
                    || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.ClanTierModel == null)
                    return new JObject { ["ok"] = false, ["error"] = "The loaded campaign has no available player clan." };
                if (!_manipulationFixtureCaptured)
                {
                    _manipulationFixtureOriginalRenown = clan.Renown;
                    _manipulationFixtureOriginalGold = player.Gold;
                    _manipulationFixtureCaptured = true;
                }
                int required = desiredTier <= 0
                    ? 0
                    : TaleWorlds.CampaignSystem.Campaign.Current.Models
                        .ClanTierModel.GetRequiredRenownForTier(desiredTier);
                SetManipulationFixtureRenown(
                    clan,
                    desiredTier <= 0 ? 0f : required + 1f);
                player.Gold = desiredGold;
                _manipulationFixtureEconomicCapacityKnown =
                    wealthEvidence == "demonstrated";
                _manipulationFixtureWealthEvidenceBasis =
                    wealthEvidence == "demonstrated"
                        ? "demonstrated_financial_capacity"
                        : wealthEvidence == "hidden"
                            ? "hidden_wallet_not_observable"
                            : "visible_presentation_only";
                return new JObject
                {
                    ["ok"] = clan.Tier == desiredTier,
                    ["error"] = clan.Tier == desiredTier
                        ? string.Empty
                        : "Bannerlord did not refresh the requested clan tier after the reversible renown change.",
                    ["originalRenown"] = _manipulationFixtureOriginalRenown,
                    ["originalGold"] = _manipulationFixtureOriginalGold,
                    ["requestedClanTier"] = desiredTier,
                    ["currentRenown"] = clan.Renown,
                    ["clanTier"] = clan.Tier,
                    ["tierVerified"] = clan.Tier == desiredTier,
                    ["playerGold"] = player.Gold,
                    ["wealthEvidenceBasis"] =
                        _manipulationFixtureWealthEvidenceBasis,
                    ["economicCapacityKnown"] =
                        _manipulationFixtureEconomicCapacityKnown,
                    ["playerHeroId"] = player.StringId,
                    ["playerName"] = player.Name?.ToString() ?? string.Empty
                };
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The reversible court-intrigue opportunity fixture is active.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Manipulation fixture setup failed.", result);
        }

        private static async Task<LiveCommandResult> RestoreManipulationFixtureAsync()
        {
            JObject result = await ReignMainThread.InvokeAsync(() =>
            {
                Clan clan = Clan.PlayerClan;
                if (clan == null)
                    return new JObject { ["ok"] = false, ["error"] = "The loaded campaign has no available player clan." };
                if (_manipulationFixtureCaptured)
                {
                    SetManipulationFixtureRenown(
                        clan,
                        _manipulationFixtureOriginalRenown);
                    if (Hero.MainHero != null)
                        Hero.MainHero.Gold =
                            _manipulationFixtureOriginalGold;
                }
                JObject restored = new JObject
                {
                    ["ok"] = true,
                    ["restored"] = _manipulationFixtureCaptured,
                    ["renown"] = clan.Renown,
                    ["clanTier"] = clan.Tier,
                    ["gold"] = Hero.MainHero?.Gold ?? 0
                };
                _manipulationFixtureCaptured = false;
                _manipulationFixtureOriginalRenown = 0f;
                _manipulationFixtureOriginalGold = 0;
                _manipulationFixtureEconomicCapacityKnown = false;
                _manipulationFixtureWealthEvidenceBasis = string.Empty;
                return restored;
            }).ConfigureAwait(false);
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The court-intrigue opportunity fixture was restored.", result)
                : LiveCommandResult.Failed(result.Value<string>("error") ?? "Manipulation fixture restoration failed.", result);
        }

        private static void SetManipulationFixtureRenown(
            Clan clan,
            float renown)
        {
            if (clan == null) return;
            // Clan.Renown is a stored number, while Clan.Tier is a cached
            // private value. Directly assigning Renown therefore leaves the
            // production context on the old tier. ResetClanRenown and
            // AddRenown are the native paths that recalculate that cache and
            // dispatch the ordinary tier-change event without notifications.
            clan.ResetClanRenown();
            if (renown > 0f)
                clan.AddRenown(renown, shouldNotify: false);
        }

        public static void ApplyManipulationFixtureOpportunityEvidence(
            JObject opportunitySnapshot)
        {
            if (!_manipulationFixtureCaptured
                || opportunitySnapshot == null)
                return;
            JObject target = opportunitySnapshot["target"] as JObject;
            JObject wealth = target?["wealth"] as JObject;
            if (wealth == null) return;
            wealth["economicCapacityKnown"] =
                _manipulationFixtureEconomicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureWealthEvidenceBasis;
        }

        public static void ApplyManipulationFixtureWealthEvidence(
            Hero subject,
            JObject wealth)
        {
            if (!_manipulationFixtureCaptured
                || subject != Hero.MainHero
                || wealth == null)
                return;
            wealth["economicCapacityKnown"] =
                _manipulationFixtureEconomicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureWealthEvidenceBasis;
            if (_manipulationFixtureEconomicCapacityKnown)
                return;
            wealth["gold"] = -1;
            wealth["clanGold"] = -1;
            wealth["partyInventoryValue"] = -1;
            wealth["personalWealthTier"] =
                "not directly observed";
            wealth["clanWealthTier"] =
                "not directly observed";
            wealth["visibleWealthTier"] =
                "not directly observed";
            wealth["notes"] =
                "Wallet and treasury amounts are not observable evidence. "
                + "Use visible presentation, known rank, holdings, and "
                + "witnessed transactions only.";
        }

        public static void ApplyNpcObservableWealthEvidence(
            Hero subject,
            JObject wealth)
        {
            if (subject != Hero.MainHero || wealth == null)
                return;
            bool economicCapacityKnown =
                _manipulationFixtureCaptured
                && _manipulationFixtureEconomicCapacityKnown;
            wealth["economicCapacityKnown"] =
                economicCapacityKnown;
            wealth["wealthEvidenceBasis"] =
                _manipulationFixtureCaptured
                    ? _manipulationFixtureWealthEvidenceBasis
                    : "private_wallet_not_observable";
            if (economicCapacityKnown)
                return;
            wealth["gold"] = -1;
            wealth["clanGold"] = -1;
            wealth["partyInventoryValue"] = -1;
            wealth["personalWealthTier"] =
                "not directly observed";
            wealth["clanWealthTier"] =
                "not directly observed";
            wealth["visibleWealthTier"] =
                "not directly observed";
            wealth["notes"] =
                "Private wallet, inventory value, and treasury amounts are "
                + "not observable evidence. Use visible presentation, known "
                + "rank, public holdings, and witnessed transactions only.";
        }

        public static void ApplyNpcObservableClanEvidence(
            Hero subject,
            JObject clan)
        {
            if (subject != Hero.MainHero || clan == null)
                return;
            bool economicCapacityKnown =
                _manipulationFixtureCaptured
                && _manipulationFixtureEconomicCapacityKnown;
            clan["economicCapacityKnown"] =
                economicCapacityKnown;
            clan["wealthEvidenceBasis"] =
                _manipulationFixtureCaptured
                    ? _manipulationFixtureWealthEvidenceBasis
                    : "private_treasury_not_observable";
            if (economicCapacityKnown)
                return;
            clan["gold"] = -1;
            clan["wealthTier"] = "not directly observed";
            clan["notes"] =
                "Exact clan treasury is private. Public tier, renown, "
                + "influence, kingdom affiliation, and holdings remain "
                + "available when the observer knows the clan.";
        }

        private static async Task<LiveCommandResult> SearchTargetsAsync(JObject command)
        {
            string mode = NormalizeMode(command.Value<string>("mode"));
            string search = command.Value<string>("search") ?? string.Empty;
            int limit = Math.Max(1, Math.Min(2000, command.Value<int?>("limit") ?? 25));
            JObject data = await ReignMainThread.InvokeAsync(() =>
            {
                IEnumerable<Hero> heroes = AvailableHeroesForMode(mode);
                if (command.Property("isLord") != null)
                {
                    bool required = command.Value<bool>("isLord");
                    heroes = heroes.Where(hero => hero.IsLord == required);
                }
                if (command.Property("minimumAge") != null)
                {
                    double minimumAge = Math.Max(0d, command.Value<double>("minimumAge"));
                    heroes = heroes.Where(hero => hero.Age >= minimumAge);
                }
                if (command.Property("minClanTier") != null)
                {
                    int minimumTier = Math.Max(0, command.Value<int>("minClanTier"));
                    heroes = heroes.Where(hero => (hero.Clan?.Tier ?? 0) >= minimumTier);
                }
                if (command.Property("maxClanTier") != null)
                {
                    int maximumTier = Math.Max(0, command.Value<int>("maxClanTier"));
                    heroes = heroes.Where(hero => (hero.Clan?.Tier ?? 0) <= maximumTier);
                }
                if (command.Property("maxHonor") != null)
                {
                    int maximumHonor = Math.Max(
                        -2, Math.Min(
                            2, command.Value<int>("maxHonor")));
                    heroes = heroes.Where(hero =>
                        hero.GetTraitLevel(DefaultTraits.Honor)
                            <= maximumHonor);
                }
                List<Hero> ranked = RankHeroes(heroes, search).Take(limit).ToList();
                JArray targets = new JArray(ranked.Select(HeroTargetJson));
                JArray events = new JArray();
                if (mode == "social_event")
                {
                    events = new JArray((ReignSocialEventsCampaignBehavior.Instance?.GetLiveTestEventRecords() ?? new List<SocialEventRecord>())
                        .Where(record => record != null && (string.IsNullOrWhiteSpace(search)
                            || (record.DisplayName ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0
                            || (record.EventId ?? string.Empty).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                        .Take(limit).Select(record => new JObject
                        {
                             ["eventId"] = record.EventId ?? string.Empty, ["name"] = record.DisplayName ?? string.Empty,
                             ["templateId"] = record.TemplateId ?? string.Empty, ["status"] = record.Status.ToString(),
                             ["settlementId"] = record.SettlementStringId ?? string.Empty, ["wilderness"] = record.IsGeneratedWildernessEvent
                             ,["isOpen"] = record.IsOpen((float)CampaignTime.Now.ToDays)
                             ,["attendeeHeroIds"] = new JArray(
                                 record.AttendeeHeroStringIds
                                     ?? new List<string>())
                         }));
                }
                return new JObject
                {
                    ["mode"] = mode,
                    ["search"] = search,
                    ["isLord"] = command.Property("isLord") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<bool>("isLord")),
                    ["minimumAge"] = command.Property("minimumAge") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<double>("minimumAge")),
                    ["minClanTier"] = command.Property("minClanTier") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("minClanTier")),
                    ["maxClanTier"] = command.Property("maxClanTier") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("maxClanTier")),
                    ["maxHonor"] = command.Property("maxHonor") == null
                        ? JValue.CreateNull()
                        : new JValue(command.Value<int>("maxHonor")),
                    ["targets"] = targets,
                    ["events"] = events
                };
            }).ConfigureAwait(false);
            return LiveCommandResult.Completed("Target search completed.", data);
        }

        private static async Task<LiveCommandResult> OpenAsync(JObject command)
        {
            string runId = command.Value<string>("runId") ?? string.Empty;
            string mode = NormalizeMode(command.Value<string>("mode"));
            if (!GetSupportedModes().Contains(mode)) return LiveCommandResult.Failed("Unsupported or currently unavailable interaction mode.");
            if (!string.IsNullOrWhiteSpace(_activeRunId)) await CloseSessionAsync(false).ConfigureAwait(false);
            _activeRunId = runId;
            _activeMode = mode;
            _lastNpcReplyText = string.Empty;
            _presentation = string.Equals(command.Value<string>("presentation"), "visible", StringComparison.OrdinalIgnoreCase) ? "visible" : "headless";
            CaptureGuardedSettings(command.Value<string>("effects"));
            try
            {
                List<string> searches = ReadSearches(command);
                bool allowDisposableCourtMatter =
                    mode == "court_event"
                    && command.Value<bool?>("createTestEvent") == true;
                List<Hero> heroes = await ReignMainThread.InvokeAsync(() =>
                    ResolveHeroesForMode(mode, searches, allowDisposableCourtMatter)).ConfigureAwait(false);
                switch (mode)
                {
                    case "individual_chat":
                        if (heroes.Count == 0) return FailOpen("No living non-player hero matched the requested target.");
                        if (_presentation == "visible")
                        {
                            await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.OpenForHero(heroes[0])).ConfigureAwait(false);
                            _individualVm = await WaitForAsync(() => ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else _individualVm = await ReignMainThread.InvokeAsync(() => new ReignIndividualChatScreenVM(heroes[0], () => { }, null, false)).ConfigureAwait(false);
                        if (_individualVm == null) return FailOpen("The individual conversation controller did not initialize.");
                        break;
                    case "ambassador_official":
                        {
                            if (heroes.Count == 0) return FailOpen("No resident foreign ambassador matched the requested target.");
                            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
                            ForeignAmbassadorPosting posting = court?.ForeignAmbassadors.FirstOrDefault(candidate => candidate?.IsResident == true
                                && string.Equals(candidate.HeroStringId, heroes[0].StringId, StringComparison.OrdinalIgnoreCase));
                            if (court == null) return FailOpen("The production court controller is unavailable.");
                            if (!court.TryBuildOfficialAmbassadorContext(posting, out Hero envoy, out JObject context, out string error))
                                return FailOpen(string.IsNullOrWhiteSpace(error) ? "The official ambassador context was unavailable." : error);
                            heroes = new List<Hero> { envoy };
                            if (_presentation == "visible")
                            {
                                await ReignMainThread.InvokeAsync(() => ReignIndividualChatScreenManager.OpenForHero(envoy, context, null)).ConfigureAwait(false);
                                _individualVm = await WaitForAsync(() => ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                            }
                            else _individualVm = await ReignMainThread.InvokeAsync(() => new ReignIndividualChatScreenVM(envoy, () => { }, null, false, context)).ConfigureAwait(false);
                            if (_individualVm == null) return FailOpen("The official ambassador conversation controller did not initialize.");
                            break;
                        }
                    case "party_chat":
                        bool targetedReview = command.Value<bool?>("targetedReview") == true;
                        if (targetedReview && heroes.Count != 1)
                            return FailOpen("A targeted temporary-guest review requires exactly one available hero.");
                        if (!targetedReview && heroes.Count < 2)
                            return FailOpen("Party chat requires two or more currently available heroes.");
                        string reviewContext = command.Value<string>("reviewContext") ?? string.Empty;
                        if (_presentation == "visible" && targetedReview)
                        {
                            await ReignMainThread.InvokeAsync(() =>
                                ReignPartyChatScreenManager.OpenTargetedReview(
                                    heroes[0], reviewContext, _ => { })).ConfigureAwait(false);
                            _partyVm = await WaitForAsync(() =>
                                ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else if (_presentation == "visible")
                        {
                            await ReignMainThread.InvokeAsync(ReignPartyChatScreenManager.Open).ConfigureAwait(false);
                            _partyVm = await WaitForAsync(() => ReignPartyChatScreenManager.ActiveViewModel).ConfigureAwait(false);
                        }
                        else if (targetedReview)
                            _partyVm = await ReignMainThread.InvokeAsync(() =>
                                new ReignPartyChatScreenVM(() => { }, heroes[0], reviewContext)).ConfigureAwait(false);
                        else _partyVm = await ReignMainThread.InvokeAsync(() => new ReignPartyChatScreenVM(() => { })).ConfigureAwait(false);
                        if (_partyVm == null || (!targetedReview
                            && !await ReignMainThread.InvokeAsync(() =>
                                _partyVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false)))
                            return FailOpen("The party conversation controller could not select every requested hero.");
                        break;
                    case "social_event":
                        _eventSession = await CreateSocialEventSessionAsync(command, heroes).ConfigureAwait(false);
                        if (_eventSession == null) return FailOpen("No eligible scheduled social event could be opened. Enter a town or specify an existing event.");
                        await InitializeEventVmAsync(heroes.Count > 0 ? heroes : _eventSession.Record.GetAttendees().Take(3).ToList()).ConfigureAwait(false);
                        if (_eventVm == null) return FailOpen("The social-event conversation controller did not initialize.");
                        break;
                    case "wilderness_event":
                        int minimumWildernessParticipants = Math.Max(
                            1,
                            Math.Min(
                                5,
                                command.Value<int?>(
                                    "minimumParticipants") ?? 1));
                        _eventSession = await (ReignSocialEventsCampaignBehavior.Instance?.StartLiveTestWildernessEventAsync(
                                heroes,
                                minimumWildernessParticipants)
                            ?? Task.FromResult<ReignSocialEventSession>(null)).ConfigureAwait(false);
                        if (_eventSession == null) return FailOpen("The production wilderness-event eligibility or generation step rejected this game state.");
                        await InitializeEventVmAsync(_eventSession.Record.GetAttendees().Take(5).ToList()).ConfigureAwait(false);
                        if (_eventVm == null) return FailOpen("The wilderness conversation controller did not initialize.");
                        heroes = _eventSession.Record.GetAttendees().Take(5).ToList();
                        break;
                    case "correspondence":
                        {
                            string error = await OpenCorrespondenceAsync(
                                command,
                                heroes).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(error))
                                return FailOpen(error);
                            heroes = new List<Hero> { _correspondenceHero };
                            break;
                        }
                    case "court_event":
                        {
                            string error = await OpenCourtAsync(
                                command,
                                heroes).ConfigureAwait(false);
                            if (!string.IsNullOrWhiteSpace(error))
                                return FailOpen(error);
                            heroes = new List<Hero> { _courtSpeaker };
                            break;
                        }
                }
                return LiveCommandResult.Completed("Production interaction session opened.", new JObject
                {
                    ["runId"] = runId, ["mode"] = mode, ["presentation"] = _presentation,
                    ["sessionHandle"] = runId + ":" + mode, ["targets"] = new JArray(heroes.Select(HeroTargetJson)),
                    ["eventId"] = _eventSession?.Record?.EventId ?? string.Empty
                });
            }
            catch (Exception ex) { return FailOpen(ex.Message); }
        }

        private static async Task<LiveCommandResult> SendAsync(JObject command)
        {
            string requestedText = (command.Value<string>("text") ?? string.Empty).Trim();
            bool contextualPartyAgencyResponse =
                command.Value<bool?>("contextualPartyAgencyResponse") == true;
            string text = contextualPartyAgencyResponse
                ? BuildPartyAgencyContextualPlayerResponse(requestedText, _lastNpcReplyText)
                : requestedText;
            if (string.IsNullOrWhiteSpace(text)) return LiveCommandResult.Failed("No player text was supplied.");
            string commandId = command.Value<string>("commandId") ?? Guid.NewGuid().ToString("N");
            string correlation = "live-" + commandId;
            int scene = command.Value<int?>("sceneIndex") ?? 0;
            int turn = command.Value<int?>("turnIndex") ?? 0;
            bool guardedPromptOverride = command.Value<bool?>("guardedPromptOverride") == true;
            string guardedPromptOverrideDirective =
                (command.Value<string>("guardedPromptOverrideDirective") ?? string.Empty).Trim();
            string guardedPromptOverrideOwnerCommandId =
                (command.Value<string>("guardedPromptOverrideOwnerCommandId") ?? string.Empty).Trim();
            bool governmentCertificationMemoryIsolation =
                command.Value<bool?>("governmentCertificationMemoryIsolation") == true;
            if (governmentCertificationMemoryIsolation && guardedPromptOverride
                && string.IsNullOrWhiteSpace(guardedPromptOverrideOwnerCommandId))
            {
                // The server independently verifies this exact live command, run,
                // game instance, and disposable save before honoring the scope.
                guardedPromptOverrideOwnerCommandId = commandId;
            }
            if ((_activeMode == "individual_chat" || _activeMode == "ambassador_official") && _individualVm != null)
            {
                ReignDialogueReply reply = await _individualVm.SendAutomationLineAsync(
                    text, correlation, _activeRunId, scene, turn,
                    guardedPromptOverride, guardedPromptOverrideDirective,
                    guardedPromptOverrideOwnerCommandId).ConfigureAwait(false);
                _lastNpcReplyText = reply.Text ?? string.Empty;
                JObject data = new JObject
                {
                    ["ok"] = reply.Ok, ["text"] = reply.Text ?? string.Empty, ["emotion"] = reply.Emotion ?? string.Empty,
                    ["playerText"] = text,
                    ["contextualPartyAgencyResponse"] = contextualPartyAgencyResponse,
                    ["guardedPromptOverrideRequested"] = guardedPromptOverride,
                    ["governmentCertificationMemoryIsolationRequested"] =
                        governmentCertificationMemoryIsolation,
                    ["intent"] = reply.Intent ?? string.Empty, ["error"] = reply.Error ?? string.Empty,
                    ["correlationId"] = reply.CorrelationId ?? correlation, ["sessionId"] = reply.ConversationSessionId ?? string.Empty,
                    ["exchangeId"] = reply.ExchangeId ?? string.Empty, ["turnIds"] = reply.TurnIds,
                    ["selectedContextPulls"] = reply.SelectedContextPulls, ["contextBundles"] = reply.ContextBundles,
                    ["memoryWrites"] = reply.MemoryWrites, ["rawResponse"] = reply.RawResponse,
                    ["conceptionGateNeeded"] = reply.ConceptionGateNeeded, ["queuedActionCount"] = reply.QueuedActions?.Count ?? 0
                };
                LiveCommandResult result = reply.Ok ? LiveCommandResult.Completed("Individual dialogue turn completed.", data) : LiveCommandResult.Failed(reply.Error, data);
                result.CorrelationIds.Add(reply.CorrelationId ?? correlation);
                if (reply.ConceptionGateNeeded) result.Status = "needs_input";
                return result;
            }
            if (_activeMode == "party_chat" && _partyVm != null)
            {
                ReignPartyChatBatchResult batch = await _partyVm
                    .SendAutomationLineAsync(
                        text,
                        _activeRunId,
                        scene,
                        turn,
                        correlation)
                    .ConfigureAwait(false);
                _lastNpcReplyText = string.Join("\n", batch.Replies
                    .Where(reply => reply != null && !string.IsNullOrWhiteSpace(reply.Text))
                    .Select(reply => reply.Text));
                JObject data = new JObject
                {
                    ["ok"] = batch.Ok, ["error"] = batch.Error ?? string.Empty, ["sessionId"] = batch.SessionId ?? string.Empty,
                    ["playerText"] = text,
                    ["contextualPartyAgencyResponse"] = contextualPartyAgencyResponse,
                    ["exchangeId"] = batch.ExchangeId ?? string.Empty,
                    ["replies"] = new JArray(batch.Replies.Select(reply => new JObject
                    {
                        ["heroId"] = reply.SpeakerHeroStringId ?? string.Empty, ["heroName"] = reply.SpeakerName ?? string.Empty,
                        ["text"] = reply.Text ?? string.Empty, ["emotion"] = reply.Emotion ?? string.Empty,
                        ["intent"] = reply.Intent ?? string.Empty, ["relationshipSignal"] = reply.RelationshipSignal ?? string.Empty,
                        ["relationshipAssessments"] = reply.RelationshipAssessments,
                        ["participation"] = reply.Participation ?? string.Empty, ["reactionTargetHeroStringId"] = reply.ReactionTargetHeroStringId ?? string.Empty,
                        ["correlationId"] = reply.CorrelationId ?? string.Empty, ["sessionId"] = reply.ConversationSessionId ?? string.Empty,
                        ["exchangeId"] = reply.ExchangeId ?? string.Empty, ["selectedContextPulls"] = reply.SelectedContextPulls,
                        ["queuedActionCount"] = reply.QueuedActions?.Count ?? 0, ["ok"] = reply.Ok, ["error"] = reply.Error ?? string.Empty,
                        ["clientTotalMs"] = reply.ClientTotalMs, ["timingSummary"] = reply.TimingSummary ?? string.Empty,
                        ["rawResponse"] = reply.RawResponse
                    }))
                };
                LiveCommandResult result = batch.Ok ? LiveCommandResult.Completed("Party dialogue turn completed.", data) : LiveCommandResult.Failed(batch.Error, data);
                result.CorrelationIds.AddRange(batch.Replies.Select(reply => reply.CorrelationId).Where(value => !string.IsNullOrWhiteSpace(value)));
                return result;
            }
            if ((_activeMode == "social_event" || _activeMode == "wilderness_event") && _eventVm != null)
            {
                ReignSocialEventTurnReply turnReply = await _eventVm.SendAutomationLineAsync(text, correlation).ConfigureAwait(false);
                JObject data = new JObject
                {
                    ["ok"] = turnReply.Ok, ["idempotent"] = turnReply.Idempotent, ["turnId"] = turnReply.TurnId ?? string.Empty,
                    ["error"] = turnReply.Error ?? string.Empty,
                    ["replies"] = new JArray(turnReply.ParticipantResults.Select(reply => new JObject
                    {
                        ["heroId"] = reply.HeroStringId ?? string.Empty, ["text"] = reply.Text ?? string.Empty,
                        ["correlationId"] = reply.CorrelationId ?? string.Empty,
                        ["participation"] = reply.Participation ?? string.Empty, ["reactionTargetHeroStringId"] = reply.ReactionTargetHeroStringId ?? string.Empty,
                        ["emotion"] = reply.Emotion ?? string.Empty, ["intent"] = reply.Intent ?? string.Empty,
                        ["relationshipSignal"] = reply.RelationshipSignal ?? string.Empty, ["ok"] = reply.Ok, ["error"] = reply.Error ?? string.Empty
                        ,["relationshipAssessments"] = reply.RelationshipAssessments
                        ,["selectedContextPulls"] = reply.SelectedContextPulls
                        ,["contextBundles"] = reply.ContextBundles
                        ,["queuedActionCount"] = reply.QueuedActions?.Count ?? 0
                        ,["clientTotalMs"] = reply.ClientTotalMs
                        ,["timingSummary"] = reply.TimingSummary ?? string.Empty
                        ,["rawResponse"] = reply.RawResponse
                    })),
                    ["addressedHeroIds"] = new JArray(turnReply.AddressedHeroStringIds), ["joinedHeroIds"] = new JArray(turnReply.JoinedHeroStringIds),
                    ["wanderedHeroIds"] = new JArray(turnReply.WanderedHeroStringIds), ["correlationId"] = correlation
                    ,["rawResponse"] = turnReply.RawResponse
                };
                LiveCommandResult result = turnReply.Ok ? LiveCommandResult.Completed("Social-event group turn completed.", data) : LiveCommandResult.Failed(turnReply.Error, data);
                result.CorrelationIds.Add(correlation);
                result.CorrelationIds.AddRange(turnReply.ParticipantResults.Select(reply => reply.CorrelationId).Where(value => !string.IsNullOrWhiteSpace(value)));
                return result;
            }
            if (_activeMode == "correspondence" && _correspondenceHero != null)
                return await SendCorrespondenceAsync(text, correlation).ConfigureAwait(false);
            if (_activeMode == "court_event" && _courtMatter != null && _courtSpeaker != null)
                return await SendCourtAsync(text, correlation).ConfigureAwait(false);
            return LiveCommandResult.Failed("No matching production interaction session is open for this run.");
        }

        private static string BuildPartyAgencyContextualPlayerResponse(
            string finalConsentPrompt, string npcReply)
        {
            string reply = npcReply ?? string.Empty;
            var clauses = new List<string>();
            Hero player = Hero.MainHero;
            string playerName = player?.Name?.ToString() ?? "Egan";
            string realmName = player?.Clan?.Kingdom?.Name?.ToString()
                ?? player?.Clan?.Name?.ToString() ?? "my realm";
            Settlement seat = Settlement.CurrentSettlement
                ?? player?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            string seatName = seat?.Name?.ToString() ?? "this settlement";

            if (PartyAgencyReplyContains(reply, "who are you", "your name", "what authority",
                    "authority backs", "whose authority", "identify yourself", "give me yours",
                    "give me your name", "hear a name", "hear your name", "name from your mouth",
                    "a name. that's"))
                clauses.Add("My name is " + playerName + ", sovereign of " + realmName
                    + " and lord of " + seatName
                    + "; this undertaking proceeds under my own authority.");
            if (PartyAgencyReplyContains(reply, "family", "husband", "wife", "children",
                    "household", "home while", "left exposed"))
                clauses.Add("While you are away, I will place a trusted " + seatName
                    + " garrison detail over your household and cover their provisions and safety.");
            if (PartyAgencyReplyContains(reply, "spoils", "share", "cut", "payment",
                    "reward", "coin"))
                clauses.Add("You will receive the fair share or payment you named, recorded before we depart.");
            if (PartyAgencyReplyContains(reply, "independent", "retainer", "subject",
                    "partner", "battlefield orders", "take orders", "my own command"))
                clauses.Add("You travel as an independent ally, not my retainer or subject, and we will discuss our course before acting.");
            if (PartyAgencyReplyContains(reply, "scout", "report", "route", "plan",
                    "where we", "what happens", "practical detail"))
                clauses.Add("You may inspect the scouts' reports, route, provisions, and security arrangements before we ride.");
            if (PartyAgencyReplyContains(reply, "written record", "document", "addendum",
                    "papers", "written proof", "in writing"))
                clauses.Add("I have prepared the written record, addendum, or other papers you requested, and I place them in your hands now for inspection before we continue.");
            clauses.Add("I accept every condition you have named and will hold myself to it.");
            clauses.Add(finalConsentPrompt);
            return string.Join(" ", clauses.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static bool PartyAgencyReplyContains(string text, params string[] phrases)
        {
            return phrases.Any(phrase => (text ?? string.Empty).IndexOf(
                phrase, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static async Task<LiveCommandResult> SelectParticipantsAsync(JObject command)
        {
            List<Hero> heroes = await ReignMainThread.InvokeAsync(() => ResolveHeroesForMode(_activeMode, ReadSearches(command))).ConfigureAwait(false);
            bool selected = false;
            if (_partyVm != null) selected = await ReignMainThread.InvokeAsync(() => _partyVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
            else if (_eventVm != null) selected = await ReignMainThread.InvokeAsync(() => _eventVm.SelectAutomationHeroes(heroes)).ConfigureAwait(false);
            return selected ? LiveCommandResult.Completed("Participants selected.", new JObject { ["targets"] = new JArray(heroes.Select(HeroTargetJson)) }) : LiveCommandResult.Failed("The requested participants could not be selected.");
        }

        private static async Task<LiveCommandResult> NextPhaseAsync()
        {
            if (_eventVm == null) return LiveCommandResult.Failed("Next phase is available only for social events.");
            bool advanced = await _eventVm.AdvanceAutomationPhaseAsync().ConfigureAwait(false);
            return advanced ? LiveCommandResult.Completed("The production event advanced to its next phase.") : LiveCommandResult.Failed("The event has no next phase or is busy.");
        }

        private static async Task<LiveCommandResult> WaitApproachAsync()
        {
            if (_eventVm == null) return LiveCommandResult.Failed("Wait Approach is available only for social events.");
            await ReignMainThread.InvokeAsync(_eventVm.ExecuteWaitApproach).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
            return LiveCommandResult.Completed("The production Wait Approach control was invoked; subsequent participants are recorded by the event turn audit.");
        }

        private static async Task<LiveCommandResult> ResolveConfirmationAsync(bool confirm)
        {
            ReignIndividualChatScreenVM viewModel = await ReignMainThread.InvokeAsync(() =>
                _individualVm ?? ReignIndividualChatScreenManager.ActiveViewModel).ConfigureAwait(false);
            if (viewModel == null || !viewModel.IsPregnancyWarningVisible)
                return LiveCommandResult.Failed("No individual-chat confirmation is pending.");

            Task<ReignIndividualChatScreenVM.PregnancyDecisionReceipt> completion =
                await ReignMainThread.InvokeAsync(
                    viewModel.ObservePregnancyDecisionCompletionAsync).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (confirm) viewModel.ExecuteProceedPregnancy();
                else viewModel.ExecutePullOut();
            }).ConfigureAwait(false);

            Task boundedDeadline = Task.Delay(TimeSpan.FromSeconds(30));
            Task finished = await Task.WhenAny(completion, boundedDeadline).ConfigureAwait(false);
            if (finished != completion)
            {
                JObject timeoutState = await ReignMainThread.InvokeAsync(
                    viewModel.PregnancyAutomationStateJson).ConfigureAwait(false);
                return LiveCommandResult.Failed(
                    "The pregnancy decision did not reach a terminal view-model state before the bounded deadline.",
                    timeoutState);
            }

            ReignIndividualChatScreenVM.PregnancyDecisionReceipt receipt =
                await completion.ConfigureAwait(false);
            JObject evidence = receipt.ToJson();
            return receipt.Ok
                ? LiveCommandResult.Completed(
                    confirm ? "Pending confirmation was accepted." : "Pending confirmation was denied.",
                    evidence)
                : LiveCommandResult.Failed(
                    string.IsNullOrWhiteSpace(receipt.Error)
                        ? "The pregnancy decision did not complete successfully."
                        : receipt.Error,
                    evidence);
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
                        ReignCourtCampaignBehavior.Instance?.Session?.SessionId
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
            ReignSocialEventsCampaignBehavior behavior = ReignSocialEventsCampaignBehavior.Instance;
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
                await ReignMainThread.InvokeAsync(() => ReignSocialEventScreenManager.Open(_eventSession, ReignSocialEventsCampaignBehavior.Instance.ResolveLiveTestSession)).ConfigureAwait(false);
                _eventVm = await WaitForAsync(() => ReignSocialEventScreenManager.ActiveViewModel).ConfigureAwait(false);
            }
            else _eventVm = await ReignMainThread.InvokeAsync(() => new ReignSocialEventScreenVM(_eventSession, () => { }, ReignSocialEventsCampaignBehavior.Instance.ResolveLiveTestSession)).ConfigureAwait(false);
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
                return ReignServerClient.GetKnownCorrespondenceContacts();
            if (mode == "ambassador_official")
            {
                HashSet<string> residentIds = new HashSet<string>(
                    (ReignCourtCampaignBehavior.Instance?.ForeignAmbassadors as IEnumerable<ForeignAmbassadorPosting>
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
                    ReignCourtCampaignBehavior.Instance?.Matters
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

        private sealed class LiveCommandResult
        {
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
