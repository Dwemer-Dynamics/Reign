#if !REIGN_EXCLUDE_COURT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.CastleChat;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI;
using ReignBeta.UI.Calibration;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private const string UiCalibrationAutomationLeaseOwner = "reign-live-interaction-ui";
        private static string _uiCalibrationTarget = string.Empty;
        private static string _uiCalibrationMovie = string.Empty;

        private static async Task<LiveCommandResult> UiOpenAsync(JObject command)
        {
            string target = NormalizeUiTarget(
                command.Value<string>("targetSearch")
                ?? command.Value<string>("text"));
            if (string.IsNullOrWhiteSpace(target))
                return LiveCommandResult.Failed("ui_open requires a supported UI target.");

            ReignUiCalibrationService.AcquireAutomationLease(UiCalibrationAutomationLeaseOwner);
            bool keepAutomationLease = false;
            try
            {
                if (IsNativeUiTarget(target))
                {
                    LiveCommandResult nativeResult = await NativeUiOpenAsync(target).ConfigureAwait(false);
                    keepAutomationLease = string.Equals(nativeResult.Status, "completed", StringComparison.Ordinal);
                    return nativeResult;
                }

                ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
                if (court == null)
                    return LiveCommandResult.Failed("The production court controller is unavailable.");

            JObject uiEnrollment = command["enrollment"] as JObject;
            bool docketClockIsolation = (target == "court" || target == "family-chambers")
                && ((!string.IsNullOrWhiteSpace(command.Value<string>("campaignTestRunId"))
                    && !string.IsNullOrWhiteSpace(command.Value<string>("expectedSaveName")))
                    || (!string.IsNullOrWhiteSpace(uiEnrollment?.Value<string>("campaignTestRunId"))
                        && !string.IsNullOrWhiteSpace(uiEnrollment?.Value<string>("disposableSaveName"))));
            double docketWorldDayBefore = -1;

            string sessionError = await ReignMainThread.InvokeAsync(() =>
            {
                if (docketClockIsolation)
                {
                    if (TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                        return "Pause and drain the enrolled campaign before opening a docket test.";
                    docketWorldDayBefore = CampaignTime.Now.ToDays;
                }
                if (!court.IsRuleModeActive)
                {
                    Settlement settlement = Settlement.CurrentSettlement;
                    if (!court.TryOpenSession(settlement, out string error)) return error;
                }
                MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
                if (mapState?.AtMenu == true) mapState.ExitMenuMode();
                // Exiting a native settlement wait can restore its running mode.
                // Reassert pause in the same native dispatch, before another tick.
                if (docketClockIsolation) court.PauseTime();
                return string.Empty;
            }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(sessionError))
                return LiveCommandResult.Failed("Rule Mode could not start: " + sessionError);

            string openError = await ReignMainThread.InvokeAsync(() =>
            {
                CloseCalibrationScreens();
                // An outgoing audience may restore its prior time mode on close.
                if (docketClockIsolation) court.PauseTime();
                _uiCalibrationTarget = string.Empty;
                _uiCalibrationMovie = string.Empty;
                switch (target)
                {
                    case "court":
                        ReignCourtScreenManager.Open(court);
                        break;
                    case "court-petition":
                        ReignCourtPetitionScreenManager.OpenForCalibration(court,
                            BuildCourtPetitionCalibrationFixture());
                        break;
                    case "castle-layout":
                        ReignCastleLayoutScreenManager.Open(court);
                        break;
                    case "castle-chat":
                        Settlement castleSettlement = Settlement.CurrentSettlement
                            ?? MobileParty.MainParty?.CurrentSettlement;
                        ReignPartyChatScreenManager.OpenCastle(new CastleRoomSessionRecord
                        {
                            SessionKey = "ui_calibration_castle_chat",
                            CampaignId = "ui_calibration",
                            TimelineId = "ui_calibration",
                            SettlementStringId = castleSettlement?.StringId ?? string.Empty,
                            CultureId = castleSettlement?.Culture?.StringId ?? "generic",
                            Room = (int)CastleRoom.MainHall,
                            ImageStatus = "disabled",
                            OpeningStatus = "complete",
                            OccupantHeroIdsCsv = string.Empty,
                            TranscriptJson = "[]"
                        });
                        break;
                    case "ambassador":
                        ReignAmbassadorScreenManager.Open(court);
                        break;
                    case "economic-report":
                        ReignCourtEconomicReportScreenManager.Open(court);
                        break;
                    case "clan-accords":
                        var accords = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance;
                        if (accords == null || !ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.IsPlayerRuler)
                            return "Clan Accords requires the player to rule a kingdom.";
                        ReignClanAccordsScreenManager.Open(court,
                            ReignClanAccordsPresentation.Capture, accords.CancelFromUi);
                        break;
                    case "government":
                        Kingdom governmentKingdom = Clan.PlayerClan?.Kingdom;
                        if (governmentKingdom != null)
                            ReignGovernmentScreenManager.Open(governmentKingdom,
                                governmentKingdom.Leader == Hero.MainHero, null);
                        break;
                    case "spymaster":
                        ReignSpymasterScreenManager.Open(court);
                        break;
                    case "war-council":
                        ReignWarCouncilScreenManager.Open(court);
                        break;
                    case "family-chambers":
                        ReignFamilyChambersScreenManager.Open(court);
                        break;
                    case "training-yard":
                    {
                        Settlement yardSettlement = Settlement.CurrentSettlement
                            ?? MobileParty.MainParty?.CurrentSettlement;
                        if (yardSettlement?.IsFortification != true)
                            return "The Training Yard calibration target requires the player party at a town or castle.";
                        ReignTrainingYardScreenManager.Open(yardSettlement);
                        break;
                    }
                    case "royal-council":
                        ReignRoyalCouncilScreenManager.OpenForCalibration(court);
                        break;
                    case "correspondence":
                    {
                        Hero hero = FindCalibrationHero();
                        if (hero == null) return "No eligible adult character is available for the correspondence calibration fixture.";
                        ReignCorrespondenceScreenManager.OpenForCalibration(hero);
                        break;
                    }
                    case "individual-chat":
                    {
                        Hero hero = FindCalibrationHero();
                        if (hero == null) return "No eligible adult character is available for the individual-chat calibration fixture.";
                        ReignIndividualChatScreenManager.OpenForCalibration(hero);
                        break;
                    }
                    case "party-chat":
                        ReignPartyChatScreenManager.Open();
                        break;
                    case "tavern-house":
                        ReignTavernHouseScreenManager.OpenForCalibration();
                        break;
                    case "social-event":
                    case "wilderness-event":
                    {
                        ReignSocialEventSession session = CreateCalibrationSocialEventSession(target == "wilderness-event");
                        if (session == null) return "No eligible adult characters are available for the social-event calibration fixture.";
                        ReignSocialEventScreenManager.OpenForCalibration(session);
                        break;
                    }
                    case "diplomacy-announcement":
                        ReignDiplomacyAnnouncementScreenManager.Open(
                            CreateCalibrationDiplomacyAnnouncement(), null, null);
                        break;
                    case "notable-generation":
                        if (!ReignNotableGenerationPopupManager.ShowCalibrationFixture())
                            return "The notable-generation calibration popup could not be opened.";
                        break;
                    case "memories-book":
                        MemoriesBookOverlay.OpenForCalibration();
                        break;
                }
                return string.Empty;
            }).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(openError))
            {
                await ReignMainThread.InvokeAsync(CloseCalibrationScreens).ConfigureAwait(false);
                return LiveCommandResult.Failed(openError, UiStateJson());
            }

            // War Council presents a lossless 16K tiled map.  On a cold native
            // texture load its first two Gauntlet late-update frames can take
            // substantially longer than ordinary Reign movies, so retain the
            // same frame-based readiness proof with a target-specific bound.
            int openDeadlineSeconds = target == "war-council" ? 120 : 15;
            DateTime deadline = DateTime.UtcNow.AddSeconds(openDeadlineSeconds);
            while ((!IsUiTargetOpen(target) || !IsUiTargetReadyForCapture(target)) && DateTime.UtcNow < deadline)
                await Task.Delay(100).ConfigureAwait(false);
            if (!IsUiTargetOpen(target))
            {
                JObject state = UiStateJson();
                await ReignMainThread.InvokeAsync(CloseCalibrationScreens).ConfigureAwait(false);
                return LiveCommandResult.Failed("The production UI did not become visible before the bounded deadline.", state);
            }
            if (!IsUiTargetReadyForCapture(target))
            {
                JObject state = UiStateJson();
                await ReignMainThread.InvokeAsync(CloseCalibrationScreens).ConfigureAwait(false);
                return LiveCommandResult.Failed("The production UI opened but did not finish its first native layout frames before the bounded deadline.", state);
            }

            _uiCalibrationTarget = target;
            _uiCalibrationMovie = MovieForUiTarget(target);
            keepAutomationLease = true;
            JObject openedState = UiStateJson();
            if (docketClockIsolation)
            {
                JObject clock = await ReignMainThread.InvokeAsync(() => new JObject
                {
                    ["worldDayBefore"] = docketWorldDayBefore,
                    ["worldDayAfter"] = CampaignTime.Now.ToDays,
                    ["timePaused"] = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop
                }).ConfigureAwait(false);
                openedState["docketClock"] = clock;
                if (clock.Value<bool>("timePaused") != true || clock.Value<double>("worldDayAfter") != docketWorldDayBefore)
                {
                    await ReignMainThread.InvokeAsync(court.PauseTime).ConfigureAwait(false);
                    return LiveCommandResult.Failed("Docket opening did not preserve the paused campaign day.", openedState);
                }
            }
            return LiveCommandResult.Completed("Production Reign UI opened for calibration.", openedState);
            }
            finally
            {
                if (!keepAutomationLease)
                {
                    if (IsNativeUiTarget(target))
                    {
                        try
                        {
                            await ReignMainThread.InvokeAsync(CloseNativeUiCalibrationTarget).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            ReignLog.Warn("Native UI calibration failed-open cleanup failed: " + ex.Message);
                        }
                    }
                    ReignUiCalibrationService.ReleaseAutomationLease(UiCalibrationAutomationLeaseOwner);
                }
            }
        }

        private static bool IsUiTargetReadyForCapture(string target)
        {
            // The high-definition strategic map and its input surface are created
            // after the War Council screen manager reports open. Waiting for two
            // native late-update frames prevents a successful ui-open receipt from
            // racing a screenshot that still contains only the campaign map.
            return target != "war-council"
                || ReignWarCouncilMapInputWidget.AutomationLateUpdateCount >= 2;
        }

        private static LiveCommandResult UiStatus()
        {
            return LiveCommandResult.Completed("Reign UI calibration state captured.", UiStateJson());
        }

        private static async Task<LiveCommandResult> UiSnapshotAsync(JObject command)
        {
            string requested = NormalizeUiTarget(command.Value<string>("targetSearch") ?? command.Value<string>("text"));
            if (string.IsNullOrWhiteSpace(_uiCalibrationTarget))
                return LiveCommandResult.Failed("No Reign UI calibration target is active for snapshot export.");
            if (string.IsNullOrWhiteSpace(requested)
                || !string.Equals(requested, _uiCalibrationTarget, StringComparison.OrdinalIgnoreCase))
            {
                return LiveCommandResult.Failed(
                    "ui_snapshot requires the exact calibration target opened by this LiveTest session.",
                    UiStateJson());
            }

            string activeTarget = _uiCalibrationTarget;
            bool nativeTarget = IsNativeUiTarget(activeTarget);
            if (nativeTarget
                && !string.Equals(activeTarget, _nativeUiCalibrationTarget, StringComparison.OrdinalIgnoreCase))
            {
                return LiveCommandResult.Failed(
                    "The requested native snapshot target is not the exact active native calibration fixture.",
                    UiStateJson());
            }

            string movie = nativeTarget
                ? NativeUiMovieForTarget(activeTarget)
                : _uiCalibrationMovie;
            if (string.IsNullOrWhiteSpace(movie))
                return LiveCommandResult.Failed("No active Reign movie is available for snapshot export.");

            Tuple<bool, string, string> saved = await ReignMainThread.InvokeAsync(() =>
            {
                bool ok = ReignUiCalibrationService.TrySaveSnapshot(movie, out string path, out string error);
                return Tuple.Create(ok, path, error);
            }).ConfigureAwait(false);
            JObject data = UiStateJson();
            data["movieName"] = movie;
            data["snapshotPath"] = saved.Item2 ?? string.Empty;
            return saved.Item1
                ? LiveCommandResult.Completed("Native Reign UI geometry snapshot exported.", data)
                : LiveCommandResult.Failed(saved.Item3, data);
        }

        private static async Task<LiveCommandResult> UiActionAsync(JObject command)
        {
            string target = NormalizeUiTarget(command.Value<string>("targetSearch"));
            string action = command.Value<string>("text") ?? string.Empty;
            string value = command.Value<string>("value") ?? command.Value<string>("fabricatedText") ?? string.Empty;
            if (target != "economic-report" && target != "spymaster" && target != "ambassador"
                && target != "war-council" && target != "family-chambers" && target != "royal-council"
                && target != "training-yard" && target != "individual-chat" && target != "native-conversation")
            {
                return LiveCommandResult.Failed(
                    "ui_action requires the ambassador, economic-report, spymaster, war-council, family-chambers, training-yard, royal-council, individual-chat, or native-conversation target.",
                    UiStateJson());
            }
            if (!string.Equals(_uiCalibrationTarget, target, StringComparison.OrdinalIgnoreCase))
            {
                return LiveCommandResult.Failed(
                    "ui_action is restricted to the exact calibration target opened by this LiveTest session.",
                    UiStateJson());
            }

            if (target == "spymaster" && string.Equals(action.Trim().Replace('_', '-'), "appoint", StringComparison.OrdinalIgnoreCase))
            {
                Task<string> appointment = await ReignMainThread.InvokeAsync(() =>
                    ReignSpymasterScreenManager.TryExecuteAutomationAppointmentAsync(value)).ConfigureAwait(false);
                string appointmentError = await appointment.ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(appointmentError)
                    ? LiveCommandResult.Completed("Spymaster appointment completed through the production court office path.", UiStateJson())
                    : LiveCommandResult.Failed(appointmentError, UiStateJson());
            }

            if (target == "spymaster" && string.Equals(action.Trim().Replace('_', '-'), "select-social-item", StringComparison.OrdinalIgnoreCase))
            {
                Task<string> selection = await ReignMainThread.InvokeAsync(() =>
                    ReignSpymasterScreenManager.TryExecuteAutomationSocialSelectionAsync(value)).ConfigureAwait(false);
                string selectionError = await selection.ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(selectionError)
                    ? LiveCommandResult.Completed("Spymaster social records refreshed and selected through the production UI path.", UiStateJson())
                    : LiveCommandResult.Failed(selectionError, UiStateJson());
            }

            if (target == "spymaster"
                && string.Equals(action.Trim().Replace('_', '-'), "select-target", StringComparison.OrdinalIgnoreCase)
                && value.Trim().Replace('_', '-').StartsWith("foreign-noble-with-", StringComparison.OrdinalIgnoreCase))
            {
                Task<string> selection = await ReignMainThread.InvokeAsync(() =>
                    ReignSpymasterScreenManager.TryExecuteAutomationSocialTargetSelectionAsync(value)).ConfigureAwait(false);
                string selectionError = await selection.ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(selectionError)
                    ? LiveCommandResult.Completed("Spymaster target with the required production social record selected through the production UI path.", UiStateJson())
                    : LiveCommandResult.Failed(selectionError, UiStateJson());
            }

            Tuple<bool, string> result = await ReignMainThread.InvokeAsync(() =>
            {
                string error;
                bool ok = target == "war-council"
                    ? ReignWarCouncilScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "royal-council"
                    ? ReignRoyalCouncilScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "family-chambers"
                    ? ReignFamilyChambersScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "training-yard"
                    ? ReignTrainingYardScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "spymaster"
                    ? ReignSpymasterScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "ambassador"
                        ? ReignAmbassadorScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "individual-chat"
                        ? ReignIndividualChatScreenManager.TryExecuteAutomationAction(action, value, out error)
                    : target == "native-conversation"
                        ? TryExecuteNativeUiAutomationAction(target, action, out error)
                        : ReignCourtEconomicReportScreenManager.TryExecuteAutomationAction(action, out error);
                return Tuple.Create(ok, error);
            }).ConfigureAwait(false);
            return result.Item1
                ? LiveCommandResult.Completed((target == "royal-council" ? "Royal Council"
                    : target == "war-council" ? "War Council"
                    : target == "spymaster" ? "Spymaster"
                    : target == "training-yard" ? "Training Yard"
                    : target == "ambassador" ? "Ambassador"
                    : target == "individual-chat" ? "Individual Chat"
                    : target == "native-conversation" ? "Native Conversation"
                    : "Economic Report") + " control exercised on the campaign thread.", UiStateJson())
                : LiveCommandResult.Failed(result.Item2, UiStateJson());
        }

        private static async Task<LiveCommandResult> UiCloseAsync()
        {
            try
            {
                await ReignMainThread.InvokeAsync(CloseCalibrationScreens).ConfigureAwait(false);
                _uiCalibrationTarget = string.Empty;
                _uiCalibrationMovie = string.Empty;
                return await RestoreSettlementMenuAfterCalibrationAsync(
                    "Calibration UI closed.").ConfigureAwait(false);
            }
            finally
            {
                ReignUiCalibrationService.ReleaseAutomationLease(UiCalibrationAutomationLeaseOwner);
            }
        }

        private static async Task<LiveCommandResult> UiBackAsync()
        {
            if (IsNativeUiTarget(_uiCalibrationTarget))
                return await UiCloseAsync().ConfigureAwait(false);

            bool returningToCourt = await ReignMainThread.InvokeAsync(() =>
            {
                if (ReignPartyChatScreenManager.IsOpen
                    && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode == true)
                {
                    ReignPartyChatScreenManager.Close();
                    return false;
                }
                if (ReignCastleLayoutScreenManager.IsOpen)
                {
                    ReignCastleLayoutScreenManager.Close(true);
                    return true;
                }
                if (ReignAmbassadorScreenManager.IsOpen)
                {
                    ReignAmbassadorScreenManager.Close(true);
                    return true;
                }
                if (ReignCourtEconomicReportScreenManager.IsOpen)
                {
                    ReignCourtEconomicReportScreenManager.Close(true);
                    return true;
                }
                if (ReignGovernmentScreenManager.IsOpen)
                {
                    ReignGovernmentScreenManager.Close(false);
                    ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
                    if (court != null)
                        ReignCourtScreenManager.Open(court);
                    return true;
                }
                if (ReignSpymasterScreenManager.IsOpen)
                {
                    ReignSpymasterScreenManager.Close(true);
                    return true;
                }
                if (ReignWarCouncilScreenManager.IsOpen)
                {
                    ReignWarCouncilScreenManager.Close(true);
                    return true;
                }
                if (ReignFamilyChambersScreenManager.IsOpen)
                {
                    ReignFamilyChambersScreenManager.Close(true);
                    return true;
                }
                if (ReignRoyalCouncilScreenManager.IsOpen)
                {
                    ReignRoyalCouncilScreenManager.Close(true);
                    return true;
                }
                if (ReignCorrespondenceScreenManager.IsOpen)
                {
                    ReignCorrespondenceScreenManager.Close();
                    return false;
                }
                if (ReignIndividualChatScreenManager.TryCloseAutomationCalibrationFixture())
                {
                    return false;
                }
                if (ReignPartyChatScreenManager.IsOpen)
                {
                    ReignPartyChatScreenManager.Close();
                    return false;
                }
                if (ReignSocialEventScreenManager.IsOpen)
                {
                    ReignSocialEventScreenManager.Close();
                    return false;
                }
                if (ReignDiplomacyAnnouncementScreenManager.TryCloseCalibrationFixture())
                    return false;
                if (ReignNotableGenerationPopupManager.HideCalibrationFixture())
                    return false;
                if (MemoriesBookOverlay.CloseCalibrationFixture())
                    return false;
                if (ReignCourtPetitionScreenManager.TryCloseAutomationAudience())
                    return ReignCourtScreenManager.IsOpen;
                if (ReignCourtScreenManager.IsOpen)
                {
                    ReignCourtScreenManager.Close();
                    return false;
                }
                return false;
            }).ConfigureAwait(false);

            if (returningToCourt)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (!ReignCourtScreenManager.IsOpen && DateTime.UtcNow < deadline)
                    await Task.Delay(100).ConfigureAwait(false);
                if (!ReignCourtScreenManager.IsOpen)
                    return LiveCommandResult.Failed("The production sub-screen did not return to Court before the bounded deadline.", UiStateJson());
                _uiCalibrationTarget = "court";
                _uiCalibrationMovie = MovieForUiTarget("court");
                return LiveCommandResult.Completed("Calibration sub-screen returned to Court.", UiStateJson());
            }

            _uiCalibrationTarget = string.Empty;
            _uiCalibrationMovie = string.Empty;
            try
            {
                return await RestoreSettlementMenuAfterCalibrationAsync(
                    "Calibration Court screen closed.").ConfigureAwait(false);
            }
            finally
            {
                ReignUiCalibrationService.ReleaseAutomationLease(UiCalibrationAutomationLeaseOwner);
            }
        }

        private static async Task<LiveCommandResult> RestoreSettlementMenuAfterCalibrationAsync(
            string successMessage)
        {
            string restoreError = await ReignMainThread.InvokeAsync(
                RestoreSettlementMenuAfterCalibration).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(restoreError))
                return LiveCommandResult.Failed(restoreError, UiStateJson());

            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            JObject state = null;
            while (DateTime.UtcNow < deadline)
            {
                state = await ReignMainThread.InvokeAsync(UiStateJson).ConfigureAwait(false);
                if (state.Value<bool?>("settlementMenuReady") == true)
                    return LiveCommandResult.Completed(successMessage, state);
                await Task.Delay(100).ConfigureAwait(false);
            }
            return LiveCommandResult.Failed(
                "The Reign UI closed, but Bannerlord did not restore the active settlement menu before the bounded deadline.",
                state ?? UiStateJson());
        }

        private static string RestoreSettlementMenuAfterCalibration()
        {
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (campaign == null || mapState == null)
                return "The active Bannerlord campaign map is unavailable after closing the Reign UI.";
            if (campaign.CurrentMenuContext?.GameMenu != null && mapState.AtMenu)
                return string.Empty;

            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            if (settlement == null || (!settlement.IsTown && !settlement.IsCastle && !settlement.IsVillage))
                return "The player is not inside a settlement whose native menu can be restored.";

            string menuId = settlement.IsVillage
                ? "village"
                : settlement.IsCastle ? "castle" : "town";
            GameMenu.ActivateGameMenu(menuId);
            return string.Empty;
        }

        private static void CloseCalibrationScreens()
        {
            CloseNativeUiCalibrationTarget();
            if (ReignPartyChatScreenManager.IsOpen
                && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode == true)
                ReignPartyChatScreenManager.Close();
            if (_uiCalibrationTarget == "party-chat" && ReignPartyChatScreenManager.IsOpen)
                ReignPartyChatScreenManager.Close();
            if (ReignTavernHouseScreenManager.IsOpen && ReignTavernHouseScreenManager.IsCalibrationFixture)
                ReignTavernHouseScreenManager.Close();
            if (_uiCalibrationTarget == "correspondence" && ReignCorrespondenceScreenManager.IsOpen)
                ReignCorrespondenceScreenManager.Close();
            ReignIndividualChatScreenManager.TryCloseAutomationCalibrationFixture();
            if ((_uiCalibrationTarget == "social-event" || _uiCalibrationTarget == "wilderness-event")
                && ReignSocialEventScreenManager.IsOpen)
                ReignSocialEventScreenManager.Close();
            ReignDiplomacyAnnouncementScreenManager.TryCloseCalibrationFixture();
            ReignNotableGenerationPopupManager.HideCalibrationFixture();
            MemoriesBookOverlay.CloseCalibrationFixture();
            ReignCourtPetitionScreenManager.CloseCalibrationFixture();
            ReignCourtPetitionScreenManager.TryCloseAutomationAudience();
            if (ReignCastleLayoutScreenManager.IsOpen) ReignCastleLayoutScreenManager.Close(false);
            if (ReignAmbassadorScreenManager.IsOpen) ReignAmbassadorScreenManager.Close(false);
            if (ReignCourtEconomicReportScreenManager.IsOpen) ReignCourtEconomicReportScreenManager.Close(false);
            if (ReignClanAccordsScreenManager.IsOpen) ReignClanAccordsScreenManager.Close(false);
            if (ReignGovernmentScreenManager.IsOpen) ReignGovernmentScreenManager.Close(false);
            if (ReignSpymasterScreenManager.IsOpen) ReignSpymasterScreenManager.Close(false);
            if (ReignWarCouncilScreenManager.IsOpen) ReignWarCouncilScreenManager.Close(false);
            if (ReignFamilyChambersScreenManager.IsOpen) ReignFamilyChambersScreenManager.Close(false);
            if (ReignTrainingYardScreenManager.IsOpen) ReignTrainingYardScreenManager.Close(false);
            if (ReignRoyalCouncilScreenManager.IsOpen) ReignRoyalCouncilScreenManager.Close(false);
            if (ReignCourtScreenManager.IsOpen) ReignCourtScreenManager.Close();
        }

        private static bool IsUiTargetOpen(string target)
        {
            switch (target)
            {
                case "court": return ReignCourtScreenManager.IsOpen;
                case "court-petition": return ReignCourtPetitionScreenManager.IsOpen;
                case "castle-layout": return ReignCastleLayoutScreenManager.IsOpen;
                case "castle-chat": return ReignPartyChatScreenManager.IsOpen
                    && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode == true;
                case "ambassador": return ReignAmbassadorScreenManager.IsOpen;
                case "economic-report": return ReignCourtEconomicReportScreenManager.IsOpen;
                case "clan-accords": return ReignClanAccordsScreenManager.IsOpen;
                case "government": return ReignGovernmentScreenManager.IsOpen;
                case "spymaster": return ReignSpymasterScreenManager.IsOpen;
                case "war-council": return ReignWarCouncilScreenManager.IsOpen;
                case "family-chambers": return ReignFamilyChambersScreenManager.IsOpen;
                case "training-yard": return ReignTrainingYardScreenManager.IsOpen;
                case "royal-council": return ReignRoyalCouncilScreenManager.IsOpen;
                case "correspondence": return ReignCorrespondenceScreenManager.IsOpen;
                case "individual-chat": return ReignIndividualChatScreenManager.IsOpen;
                case "party-chat": return ReignPartyChatScreenManager.IsOpen
                    && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode != true;
                case "tavern-house": return ReignTavernHouseScreenManager.IsOpen
                    && ReignTavernHouseScreenManager.IsCalibrationFixture;
                case "social-event":
                case "wilderness-event": return ReignSocialEventScreenManager.IsOpen;
                case "diplomacy-announcement": return ReignDiplomacyAnnouncementScreenManager.IsOpen;
                case "notable-generation": return ReignNotableGenerationPopupManager.IsOpen;
                case "memories-book": return MemoriesBookOverlay.IsCalibrationOpen;
                case "native-initial-screen":
                case "native-game-menu":
                case "native-encyclopedia-hero":
                case "native-clan-members":
                case "native-marriage-offer":
                case "native-heir-selection":
                case "native-skill-grid-item":
                case "native-character-developer":
                case "native-crafting-hero":
                case "native-crafting":
                case "native-education":
                case "native-recruit-volunteer":
                case "native-game-menu-party":
                case "native-conversation":
                case "native-quests": return IsNativeUiTargetOpen(target);
                default: return false;
            }
        }

        private static string NormalizeUiTarget(string value)
        {
            string target = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            switch (target)
            {
                case "keep":
                case "castle":
                case "castlelayout": return "castle-layout";
                case "castlechat": return "castle-chat";
                case "ambassadors": return "ambassador";
                case "economy":
                case "economic":
                case "economicreport": return "economic-report";
                case "clanaccords": return "clan-accords";
                case "government-screen":
                case "governmentscreen": return "government";
                case "warcouncil": return "war-council";
                case "courtpetition": return "court-petition";
                case "family":
                case "familychambers": return "family-chambers";
                case "trainingyard": return "training-yard";
                case "royalcouncil": return "royal-council";
                case "individualchat": return "individual-chat";
                case "partychat": return "party-chat";
                case "tavernhouse":
                case "visit-the-madam": return "tavern-house";
                case "socialevent": return "social-event";
                case "wildernessevent": return "wilderness-event";
                case "diplomacy":
                case "diplomacyannouncement": return "diplomacy-announcement";
                case "notable":
                case "notablegeneration": return "notable-generation";
                case "memories":
                case "memorybook":
                case "memoriesbook": return "memories-book";
                case "nativeinitialscreen": return "native-initial-screen";
                case "nativegamemenu": return "native-game-menu";
                case "nativeencyclopediahero": return "native-encyclopedia-hero";
                case "nativeclanmembers": return "native-clan-members";
                case "nativemarriageoffer": return "native-marriage-offer";
                case "nativeheirselection": return "native-heir-selection";
                case "nativeskillgriditem": return "native-skill-grid-item";
                case "nativecharacterdeveloper": return "native-character-developer";
                case "nativecraftinghero": return "native-crafting-hero";
                case "nativecrafting": return "native-crafting";
                case "nativeeducation": return "native-education";
                case "nativerecruitvolunteer": return "native-recruit-volunteer";
                case "nativegamemenuparty": return "native-game-menu-party";
                case "nativeconversation": return "native-conversation";
                case "nativequests": return "native-quests";
                case "court":
                case "court-petition":
                case "castle-layout":
                case "castle-chat":
                case "ambassador":
                case "economic-report":
                case "clan-accords":
                case "government":
                case "spymaster":
                case "war-council": return target;
                case "family-chambers": return target;
                case "training-yard": return target;
                case "royal-council": return target;
                case "correspondence":
                case "individual-chat":
                case "party-chat":
                case "tavern-house":
                case "social-event":
                case "wilderness-event":
                case "diplomacy-announcement":
                case "notable-generation":
                case "memories-book": return target;
                case "native-initial-screen":
                case "native-game-menu":
                case "native-encyclopedia-hero":
                case "native-clan-members":
                case "native-marriage-offer":
                case "native-heir-selection":
                case "native-skill-grid-item":
                case "native-character-developer":
                case "native-crafting-hero":
                case "native-crafting":
                case "native-education":
                case "native-recruit-volunteer":
                case "native-game-menu-party":
                case "native-conversation":
                case "native-quests": return target;
                default: return string.Empty;
            }
        }

        private static string MovieForUiTarget(string target)
        {
            switch (target)
            {
                case "court": return "ReignCourtScreen";
                case "court-petition": return "ReignCourtPetitionScreen";
                case "castle-layout": return "ReignCastleLayoutScreen";
                case "castle-chat": return "ReignCastleChatScreen";
                case "ambassador": return "ReignAmbassadorScreen";
                case "economic-report": return "ReignCourtEconomicReportScreen";
                case "clan-accords": return "ReignClanAccordsScreen";
                case "government": return "ReignGovernmentScreen";
                case "spymaster": return "ReignSpymasterScreen";
                case "war-council": return "ReignWarCouncilScreen";
                case "family-chambers": return "ReignFamilyChambersScreen";
                case "training-yard": return "ReignTrainingYardScreen";
                case "royal-council": return "ReignRoyalCouncilScreen";
                case "correspondence": return "ReignCorrespondenceScreen";
                case "individual-chat": return "ReignIndividualChatScreen";
                case "party-chat": return "ReignPartyChatScreen";
                case "tavern-house": return "ReignTavernHouseScreen";
                case "social-event":
                case "wilderness-event": return "ReignSocialEventScreen";
                case "diplomacy-announcement": return "ReignDiplomacyAnnouncementScreen";
                case "notable-generation": return "ReignNotableGenerationPopup";
                case "memories-book": return "AIPortraitsMemoriesBook";
                default: return string.Empty;
            }
        }

        private static ReignDocketPetition BuildCourtPetitionCalibrationFixture()
        {
            Settlement target = Settlement.CurrentSettlement
                ?? Clan.PlayerClan?.Fiefs.Select(x => x.Settlement).FirstOrDefault(x => x?.Town != null);
            Hero petitioner = target?.Notables.FirstOrDefault(x => x != null && x.IsAlive && x.IsActive)
                ?? Hero.AllAliveHeroes.FirstOrDefault(x => x != Hero.MainHero && x.IsNotable && x.IsActive && !x.IsPrisoner);
            return new ReignDocketPetition
            {
                PetitionId = "ui_calibration_court_petition",
                CampaignId = "ui_calibration",
                TimelineId = "ui_calibration",
                ReignId = "ui_calibration",
                ReceivedDay = (int)Math.Floor(CampaignTime.Now.ToDays),
                Kind = ReignPetitionKind.Food,
                Severity = ReignPetitionSeverity.Serious,
                State = ReignDocketPetitionState.Pending,
                PetitionerHeroId = petitioner?.StringId ?? string.Empty,
                PetitionerName = petitioner?.Name?.ToString() ?? "Guildmaster Eronys",
                TargetSettlementId = target?.StringId ?? string.Empty,
                TargetSettlementName = target?.Name?.ToString() ?? "Ortysia",
                ParentTownId = target?.StringId ?? string.Empty,
                ParentTownName = target?.Name?.ToString() ?? "Ortysia",
                NeedValue = 18.4d,
                HealthyExpectedValue = 42d,
                NormalizedNeed = 0.56d,
                FoodStockCost = 30,
                GoldSubstituteCost = 1240,
                DurationDays = 7,
                RequesterRelationDelta = 4,
                AssociatedRelationDelta = 1,
                DailyEffect = 0.35d,
                DangerLabel = "Low",
                ProblemSummary = "The dependent villages cannot replace seed grain lost during the last raids.",
                TermsHash = "ui_calibration_no_mutation"
            };
        }

        private static JObject UiStateJson()
        {
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            MapState mapState = Game.Current?.GameStateManager?.ActiveState as MapState;
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            JObject state = new JObject
            {
                ["target"] = _uiCalibrationTarget,
                ["movieName"] = _uiCalibrationMovie,
                ["courtSessionActive"] = ReignCourtCampaignBehavior.Instance?.IsRuleModeActive == true,
                ["courtOpen"] = ReignCourtScreenManager.IsOpen,
                ["castleLayoutOpen"] = ReignCastleLayoutScreenManager.IsOpen,
                ["castleChatOpen"] = ReignPartyChatScreenManager.IsOpen
                    && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode == true,
                ["ambassadorOpen"] = ReignAmbassadorScreenManager.IsOpen,
                ["economicReportOpen"] = ReignCourtEconomicReportScreenManager.IsOpen,
                ["clanAccordsOpen"] = ReignClanAccordsScreenManager.IsOpen,
                ["governmentOpen"] = ReignGovernmentScreenManager.IsOpen,
                ["governmentInput"] = JObject.FromObject(ReignGovernmentScreenManager.AutomationInputSnapshot),
                ["courtPetitionOpen"] = ReignCourtPetitionScreenManager.IsOpen,
                ["spymasterOpen"] = ReignSpymasterScreenManager.IsOpen,
                ["warCouncilOpen"] = ReignWarCouncilScreenManager.IsOpen,
                ["familyChambersOpen"] = ReignFamilyChambersScreenManager.IsOpen,
                ["trainingYardOpen"] = ReignTrainingYardScreenManager.IsOpen,
                ["royalCouncilOpen"] = ReignRoyalCouncilScreenManager.IsOpen,
                ["correspondenceOpen"] = ReignCorrespondenceScreenManager.IsOpen,
                ["individualChatOpen"] = ReignIndividualChatScreenManager.IsOpen,
                ["partyChatOpen"] = ReignPartyChatScreenManager.IsOpen
                    && ReignPartyChatScreenManager.ActiveViewModel?.IsCastleMode != true,
                ["tavernHouseOpen"] = ReignTavernHouseScreenManager.IsOpen,
                ["tavernHouseCalibrationFixture"] = ReignTavernHouseScreenManager.IsCalibrationFixture,
                ["tavernHouseMadamCount"] = ReignTavernHouseScreenManager.ActiveViewModel?.Madam.Count ?? 0,
                ["tavernHouseWorkerCount"] = ReignTavernHouseScreenManager.ActiveViewModel?.Workers.Count ?? 0,
                ["tavernHouseInputEnabled"] = ReignTavernHouseScreenManager.ActiveViewModel?.InputEnabled == true,
                ["socialEventOpen"] = ReignSocialEventScreenManager.IsOpen,
                ["diplomacyAnnouncementOpen"] = ReignDiplomacyAnnouncementScreenManager.IsOpen,
                ["notableGenerationOpen"] = ReignNotableGenerationPopupManager.IsOpen,
                ["memoriesBookOpen"] = MemoriesBookOverlay.IsCalibrationOpen,
                ["mapAtMenu"] = mapState?.AtMenu == true,
                ["currentMenuId"] = campaign?.CurrentMenuContext?.GameMenu?.StringId ?? string.Empty,
                ["currentSettlementId"] = settlement?.StringId ?? string.Empty,
                ["settlementMenuReady"] = mapState?.AtMenu == true
                    && campaign?.CurrentMenuContext?.GameMenu != null
                    && settlement != null
            };
            AppendNativeUiCalibrationState(state);
            if (ReignRoyalCouncilScreenManager.IsOpen)
            {
                state["royalCouncilBusy"] = ReignRoyalCouncilScreenManager.AutomationBusy;
                state["royalCouncilCalibrationFixture"] = ReignRoyalCouncilScreenManager.AutomationCalibrationFixture;
                state["royalCouncilProviderCallCount"] = ReignRoyalCouncilScreenManager.AutomationProviderCallCount;
                state["royalCouncilTranscript"] = ReignRoyalCouncilScreenManager.AutomationTranscript;
            }
            if (ReignIndividualChatScreenManager.IsOpen)
            {
                state["individualChatCalibrationFixture"] = ReignIndividualChatScreenManager.AutomationCalibrationFixture;
                state["individualChatInputText"] = ReignIndividualChatScreenManager.AutomationInputText;
                state["individualChatBusyText"] = ReignIndividualChatScreenManager.AutomationBusyText;
                state["individualChatChatLineCount"] = ReignIndividualChatScreenManager.AutomationChatLineCount;
                state["individualChatBusy"] = ReignIndividualChatScreenManager.AutomationBusy;
                state["individualChatModalFocusOwned"] = ReignIndividualChatScreenManager.AutomationModalFocusOwned;
                state["individualChatActiveStateBlocked"] = ReignIndividualChatScreenManager.AutomationActiveStateBlocked;
                state["individualChatPregnancy"] = ReignIndividualChatScreenManager.AutomationPregnancyState;
                state["individualChatPortraitZoomVisible"] = ReignIndividualChatScreenManager.AutomationPortraitZoomVisible;
                state["individualChatPortraitZoomSide"] = ReignIndividualChatScreenManager.AutomationPortraitZoomSide;
            }
            if (ReignFamilyChambersScreenManager.IsOpen)
            {
                state["familyChambersAdultCount"] = ReignFamilyChambersScreenManager.AutomationAdultCount;
                state["familyChambersChildCount"] = ReignFamilyChambersScreenManager.AutomationChildCount;
                state["familyChambersSelectedCount"] = ReignFamilyChambersScreenManager.AutomationSelectedCount;
                state["familyChambersCanEnter"] = ReignFamilyChambersScreenManager.AutomationCanEnter;
                state["familyChambersFirstAdultId"] = ReignFamilyChambersScreenManager.AutomationFirstAdultId;
                state["familyChambersFirstChildId"] = ReignFamilyChambersScreenManager.AutomationFirstChildId;
                state["familyChambersStatus"] = ReignFamilyChambersScreenManager.AutomationStatus;
            }
            if (ReignTrainingYardScreenManager.IsOpen)
            {
                state["trainingYardTrainerCount"] = ReignTrainingYardScreenManager.AutomationTrainerCount;
                state["trainingYardTroopCount"] = ReignTrainingYardScreenManager.AutomationTroopCount;
                state["trainingYardIsTraining"] = ReignTrainingYardScreenManager.AutomationIsTraining;
                state["trainingYardTotalXpDelivered"] = ReignTrainingYardScreenManager.AutomationTotalXpDelivered;
                state["trainingYardStatus"] = ReignTrainingYardScreenManager.AutomationStatus;
            }
            if (ReignCourtEconomicReportScreenManager.IsOpen)
            {
                state["economicSource"] = ReignCourtEconomicReportScreenManager.AutomationSourceName;
                state["economicTarget"] = ReignCourtEconomicReportScreenManager.AutomationTargetName;
                state["economicGoods"] = ReignCourtEconomicReportScreenManager.AutomationGoodsName;
                state["economicAvailable"] = ReignCourtEconomicReportScreenManager.AutomationAvailableText;
                state["economicAmount"] = ReignCourtEconomicReportScreenManager.AutomationAmountText;
                state["economicArrival"] = ReignCourtEconomicReportScreenManager.AutomationArrivalText;
                state["economicSourceId"] = ReignCourtEconomicReportScreenManager.AutomationSourceId;
                state["economicTargetId"] = ReignCourtEconomicReportScreenManager.AutomationTargetId;
                state["economicTravelDays"] = ReignCourtEconomicReportScreenManager.AutomationTravelDays;
                state["economicSourceOptionCount"] = ReignCourtEconomicReportScreenManager.AutomationSourceOptionCount;
                state["economicTargetOptionCount"] = ReignCourtEconomicReportScreenManager.AutomationTargetOptionCount;
                state["economicSourceDropdownOpen"] = ReignCourtEconomicReportScreenManager.AutomationSourceDropdownOpen;
                state["economicTargetDropdownOpen"] = ReignCourtEconomicReportScreenManager.AutomationTargetDropdownOpen;
                state["economicCanSubmit"] = ReignCourtEconomicReportScreenManager.AutomationCanSubmit;
                ReignEconomicShipment shipment = ReignCourtEconomicReportScreenManager.AutomationLatestShipment;
                if (shipment != null)
                {
                    state["economicShipmentId"] = shipment.ShipmentId;
                    state["economicShipmentStatus"] = shipment.Status;
                    state["economicRequestedFood"] = shipment.RequestedFood;
                    state["economicSpoilagePercent"] = shipment.SpoilagePercent;
                    state["economicRoadSpoilage"] = shipment.RoadSpoilage;
                    state["economicSurvivingFood"] = shipment.SurvivingFood;
                    state["economicCapacityOverflow"] = shipment.CapacityOverflow;
                    state["economicDeliveredFood"] = shipment.DeliveredFood;
                    state["economicDispatchDay"] = shipment.RequestedDay;
                    state["economicArrivalDay"] = shipment.ArrivalDay;
                    state["economicSourceFoodBefore"] = shipment.SourceFoodBefore;
                    state["economicSourceFoodAfterDispatch"] = shipment.SourceFoodAfterDispatch;
                    state["economicTargetFoodBeforeDelivery"] = shipment.TargetFoodBeforeDelivery;
                    state["economicTargetFoodAfterDelivery"] = shipment.TargetFoodAfterDelivery;
                    state["economicSourceKnowledgeCount"] = shipment.SourceClanKnownHeroIds?.Count ?? 0;
                    state["economicTargetKnowledgeCount"] = shipment.TargetClanKnownHeroIds?.Count ?? 0;
                    state["economicSourceLetterStatus"] = shipment.SourceLetterStatus;
                    state["economicTargetLetterStatus"] = shipment.TargetLetterStatus;
                    state["economicSourceLetterId"] = shipment.SourceLetterId;
                    state["economicTargetLetterId"] = shipment.TargetLetterId;
                    state["economicShipmentError"] = shipment.Error;
                }
            }
            if (ReignAmbassadorScreenManager.IsOpen)
            {
                state["ambassadorCount"] = ReignAmbassadorScreenManager.AutomationCount;
                state["ambassadorPostingId"] = ReignAmbassadorScreenManager.AutomationPostingId;
                state["ambassadorOriginKingdomId"] = ReignAmbassadorScreenManager.AutomationOriginKingdomId;
                state["ambassadorHeroId"] = ReignAmbassadorScreenManager.AutomationHeroId;
                state["ambassadorStatus"] = ReignAmbassadorScreenManager.AutomationStatus;
                state["ambassadorCanSpeak"] = ReignAmbassadorScreenManager.AutomationCanSpeak;
                state["ambassadorCanRemove"] = ReignAmbassadorScreenManager.AutomationCanRemove;
            }
            if (ReignSpymasterScreenManager.IsOpen)
            {
                state["spymasterPage"] = ReignSpymasterScreenManager.AutomationPage;
                state["spymasterTargetId"] = ReignSpymasterScreenManager.AutomationTargetId;
                state["spymasterActionId"] = ReignSpymasterScreenManager.AutomationActionId;
                state["spymasterTargetCount"] = ReignSpymasterScreenManager.AutomationTargetCount;
                state["spymasterActionCount"] = ReignSpymasterScreenManager.AutomationActionCount;
                state["spymasterReportCount"] = ReignSpymasterScreenManager.AutomationReportCount;
                state["spymasterCanBegin"] = ReignSpymasterScreenManager.AutomationCanBegin;
                state["spymasterStatus"] = ReignSpymasterScreenManager.AutomationStatus;
                state["spymasterOperationDetail"] = ReignSpymasterScreenManager.AutomationOperationDetail;
                state["spymasterPeopleGroup"] = ReignSpymasterScreenManager.AutomationPeopleGroup;
                state["spymasterSocialScope"] = ReignSpymasterScreenManager.AutomationSocialScope;
                state["spymasterHeroId"] = ReignSpymasterScreenManager.AutomationSpymasterHeroId;
                state["spymasterName"] = ReignSpymasterScreenManager.AutomationSpymasterName;
            }
            if (ReignWarCouncilScreenManager.IsOpen)
            {
                state["warCouncilLordCount"] = ReignWarCouncilScreenManager.AutomationLordCount;
                state["warCouncilPartyCount"] = ReignWarCouncilScreenManager.AutomationPartyCount;
                state["warCouncilKingdomCount"] = ReignWarCouncilScreenManager.AutomationKingdomCount;
                state["warCouncilReportCount"] = ReignWarCouncilScreenManager.AutomationReportCount;
                state["warCouncilRealmBattleReportCount"] = ReignWarCouncilScreenManager.AutomationRealmBattleReportCount;
                state["warCouncilZoom"] = ReignWarCouncilScreenManager.AutomationZoom;
                state["warCouncilMapOffsetX"] = ReignWarCouncilScreenManager.AutomationMapOffsetX;
                state["warCouncilMapOffsetY"] = ReignWarCouncilScreenManager.AutomationMapOffsetY;
                state["warCouncilMapInputLateUpdateCount"] = ReignWarCouncilMapInputWidget.AutomationLateUpdateCount;
                state["warCouncilMapInputHeldFrameCount"] = ReignWarCouncilMapInputWidget.AutomationHeldFrameCount;
                state["warCouncilMapInputPressCount"] = ReignWarCouncilMapInputWidget.AutomationPressCount;
                state["warCouncilMapInputMoveCount"] = ReignWarCouncilMapInputWidget.AutomationMoveCount;
                state["warCouncilMapInputReleaseCount"] = ReignWarCouncilMapInputWidget.AutomationReleaseCount;
                state["warCouncilMapInputScrollCount"] = ReignWarCouncilMapInputWidget.AutomationScrollCount;
                state["warCouncilMapInputPointerInside"] = ReignWarCouncilMapInputWidget.AutomationLastPointerInside;
                state["warCouncilMapInputMouseX"] = ReignWarCouncilMapInputWidget.AutomationLastMouseX;
                state["warCouncilMapInputMouseY"] = ReignWarCouncilMapInputWidget.AutomationLastMouseY;
                state["warCouncilMapInputLogicalX"] = ReignWarCouncilMapInputWidget.AutomationLogicalX;
                state["warCouncilMapInputLogicalY"] = ReignWarCouncilMapInputWidget.AutomationLogicalY;
                state["warCouncilMapInputLogicalWidth"] = ReignWarCouncilMapInputWidget.AutomationLogicalWidth;
                state["warCouncilMapInputLogicalHeight"] = ReignWarCouncilMapInputWidget.AutomationLogicalHeight;
                state["warCouncilMapImageAvailable"] = ReignWarCouncilScreenManager.AutomationMapImageAvailable;
                state["warCouncilHighDefinitionMapTileCount"] = ReignWarCouncilScreenManager.AutomationHighDefinitionMapTileCount;
                state["warCouncilScrollFrameImageAvailable"] = ReignWarCouncilScreenManager.AutomationScrollFrameImageAvailable;
                state["warCouncilOuterFrameImageAvailable"] = ReignWarCouncilScreenManager.AutomationOuterFrameImageAvailable;
                state["warCouncilSettlementImagesAvailable"] = ReignWarCouncilScreenManager.AutomationSettlementImagesAvailable;
                state["warCouncilTimePaused"] = ReignWarCouncilScreenManager.AutomationTimePaused;
                state["warCouncilFixedOverview"] = ReignWarCouncilScreenManager.AutomationFixedOverview;
                state["warCouncilMapPanningEnabled"] = ReignWarCouncilScreenManager.AutomationMapPanningEnabled;
                state["warCouncilClanPartyCount"] = ReignWarCouncilScreenManager.AutomationClanPartyCount;
                state["warCouncilKingdomPartyCount"] = ReignWarCouncilScreenManager.AutomationKingdomPartyCount;
                state["warCouncilPlayerRealmPartyCount"] = ReignWarCouncilScreenManager.AutomationPlayerRealmPartyCount;
                state["warCouncilHostilePartyCount"] = ReignWarCouncilScreenManager.AutomationHostilePartyCount;
                state["warCouncilForeignPartyCount"] = ReignWarCouncilScreenManager.AutomationForeignPartyCount;
                state["warCouncilDetectedForeignPartyCount"] = ReignWarCouncilScreenManager.AutomationDetectedForeignPartyCount;
                state["warCouncilCouncilorTactics"] = ReignWarCouncilScreenManager.AutomationCouncilorTactics;
                state["warCouncilCouncilorLeadership"] = ReignWarCouncilScreenManager.AutomationCouncilorLeadership;
                state["warCouncilDetectionRange"] = ReignWarCouncilScreenManager.AutomationDetectionRange;
                state["warCouncilDetectionChance"] = ReignWarCouncilScreenManager.AutomationDetectionChance;
                state["warCouncilCapitalSettlementId"] = ReignWarCouncilScreenManager.AutomationCapitalSettlementId;
                state["warCouncilSelectedCouncilorHeroId"] = ReignWarCouncilScreenManager.AutomationSelectedCouncilorHeroId;
                state["warCouncilEffectiveCouncilorHeroId"] = ReignWarCouncilScreenManager.AutomationEffectiveCouncilorHeroId;
                state["warCouncilCouncilorCandidateCount"] = ReignWarCouncilScreenManager.AutomationCouncilorCandidateCount;
                state["warCouncilCouncilorDropdownOpen"] = ReignWarCouncilScreenManager.AutomationCouncilorDropdownOpen;
                state["warCouncilCouncilorName"] = ReignWarCouncilScreenManager.AutomationCouncilorName;
                state["warCouncilCouncilorSkillText"] = ReignWarCouncilScreenManager.AutomationCouncilorSkillText;
                state["warCouncilPortraitRevision"] = ReignWarCouncilScreenManager.AutomationPortraitRevision;
                state["warCouncilNavalPartyCount"] = ReignWarCouncilScreenManager.AutomationNavalPartyCount;
                state["warCouncilPlayerRealmNavalPartyCount"] = ReignWarCouncilScreenManager.AutomationPlayerRealmNavalPartyCount;
                state["warCouncilHostileNavalPartyCount"] = ReignWarCouncilScreenManager.AutomationHostileNavalPartyCount;
                state["warCouncilInfantryPartyCount"] = ReignWarCouncilScreenManager.AutomationInfantryPartyCount;
                state["warCouncilMissilePartyCount"] = ReignWarCouncilScreenManager.AutomationMissilePartyCount;
                state["warCouncilMountedPartyCount"] = ReignWarCouncilScreenManager.AutomationMountedPartyCount;
                state["warCouncilPartylessLordCount"] = ReignWarCouncilScreenManager.AutomationPartylessLordCount;
                state["warCouncilMobilizableLordCount"] = ReignWarCouncilScreenManager.AutomationMobilizableLordCount;
                state["warCouncilFollowingOrderCount"] = ReignWarCouncilScreenManager.AutomationFollowingOrderCount;
                state["warCouncilOrderCount"] = ReignWarCouncilScreenManager.AutomationWarCouncilOrderCount;
                state["warCouncilFirstPartyHeroId"] = ReignWarCouncilScreenManager.AutomationFirstPartyHeroId;
                state["warCouncilFirstPartyWorldX"] = ReignWarCouncilScreenManager.AutomationFirstPartyWorldX;
                state["warCouncilFirstPartyWorldY"] = ReignWarCouncilScreenManager.AutomationFirstPartyWorldY;
                state["warCouncilFirstMobilizableHeroId"] = ReignWarCouncilScreenManager.AutomationFirstMobilizableHeroId;
                state["warCouncilFirstHostileSettlementId"] = ReignWarCouncilScreenManager.AutomationFirstHostileSettlementId;
                state["warCouncilFirstHostileSettlementWorldX"] = ReignWarCouncilScreenManager.AutomationFirstHostileSettlementWorldX;
                state["warCouncilFirstHostileSettlementWorldY"] = ReignWarCouncilScreenManager.AutomationFirstHostileSettlementWorldY;
                state["warCouncilTownSettlementCount"] = ReignWarCouncilScreenManager.AutomationTownSettlementCount;
                state["warCouncilCastleSettlementCount"] = ReignWarCouncilScreenManager.AutomationCastleSettlementCount;
                state["warCouncilVillageSettlementCount"] = ReignWarCouncilScreenManager.AutomationVillageSettlementCount;
                state["warCouncilSettlementListCount"] = ReignWarCouncilScreenManager.AutomationSettlementListCount;
                state["warCouncilLastCenteredSettlementId"] = ReignWarCouncilScreenManager.AutomationLastCenteredSettlementId;
                state["warCouncilStatus"] = ReignWarCouncilScreenManager.AutomationStatus;
                state["warCouncilRavenImageAvailable"] = ReignWarCouncilScreenManager.AutomationRavenImageAvailable;
                state["warCouncilPanelFrameOverlayImageAvailable"] = ReignWarCouncilScreenManager.AutomationPanelFrameOverlayImageAvailable;
                state["warCouncilPanelFrameImageAvailable"] = ReignWarCouncilScreenManager.AutomationPanelFrameImageAvailable;
                state["warCouncilTallPanelFrameImageAvailable"] = ReignWarCouncilScreenManager.AutomationTallPanelFrameImageAvailable;
                state["warCouncilMessengerVisible"] = ReignWarCouncilScreenManager.AutomationMessengerVisible;
                state["warCouncilMessengerBusy"] = ReignWarCouncilScreenManager.AutomationMessengerBusy;
                state["warCouncilMessengerRecipient"] = ReignWarCouncilScreenManager.AutomationMessengerRecipient;
                state["warCouncilMessengerStatus"] = ReignWarCouncilScreenManager.AutomationMessengerStatus;
            }
            return state;
        }

        private static Hero FindCalibrationHero()
        {
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero == Hero.MainHero) continue;
                if (ReignConversationEligibility.TryValidateAdultConversationHero(hero, out string error))
                    return hero;
            }
            return null;
        }

        private static List<Hero> FindCalibrationHeroes(int maximum)
        {
            List<Hero> heroes = new List<Hero>();
            foreach (Hero hero in Hero.AllAliveHeroes)
            {
                if (hero == null || hero == Hero.MainHero) continue;
                if (!ReignConversationEligibility.TryValidateAdultConversationHero(hero, out string error)) continue;
                heroes.Add(hero);
                if (heroes.Count >= maximum) break;
            }
            return heroes;
        }

        private static ReignSocialEventSession CreateCalibrationSocialEventSession(bool wilderness)
        {
            List<Hero> heroes = FindCalibrationHeroes(3);
            if (heroes.Count == 0) return null;
            Settlement settlement = Settlement.CurrentSettlement
                ?? MobileParty.MainParty?.CurrentSettlement;
            float now = TaleWorlds.CampaignSystem.Campaign.Current == null
                ? 0f
                : (float)CampaignTime.Now.ToDays;
            SocialEventRecord record = new SocialEventRecord
            {
                EventId = wilderness ? "ui_calibration_wilderness_event" : "ui_calibration_social_event",
                TemplateId = wilderness ? "generated_wilderness" : "feast_empire",
                DisplayName = wilderness ? "A Quiet Discovery" : "Council Supper",
                SettlementStringId = wilderness ? string.Empty : settlement?.StringId ?? string.Empty,
                HostHeroStringId = heroes[0].StringId,
                AttendeeHeroStringIds = new List<string>(),
                MovedHeroStringIds = new List<string>(),
                AnnouncementDay = now,
                ExpiresDay = now + 1f,
                RequiredPlayerClanTier = 0,
                Status = SocialEventStatus.Attended,
                AnnouncementSent = true,
                IsGeneratedWildernessEvent = wilderness,
                GeneratedTerrainKey = wilderness ? "woodland" : string.Empty,
                GeneratedLocationText = wilderness ? "beside a quiet woodland road" : string.Empty,
                GeneratedTimeOfDayText = wilderness ? "late afternoon" : string.Empty,
                GeneratedTitle = wilderness ? "A Quiet Discovery" : string.Empty,
                GeneratedApproachText = wilderness ? "A weathered marker catches the party's attention beside the road." : string.Empty,
                GeneratedOpeningText = wilderness ? "The company gathers while the wind moves through the trees." : string.Empty,
                GeneratedPlayerHook = wilderness ? "You may ask what the marker means to them." : string.Empty
            };
            foreach (Hero hero in heroes) record.AttendeeHeroStringIds.Add(hero.StringId);
            ReignSocialEventSession session = new ReignSocialEventSession(record);
            foreach (Hero hero in heroes) session.ActivateHero(hero, true);
            return session;
        }

        private static ReignDiplomacyAnnouncement CreateCalibrationDiplomacyAnnouncement()
        {
            Hero actor = FindCalibrationHero();
            Hero target = Hero.MainHero;
            Kingdom actorKingdom = actor?.Clan?.Kingdom;
            Kingdom targetKingdom = target?.Clan?.Kingdom;
            return new ReignDiplomacyAnnouncement
            {
                EventId = "ui_calibration_diplomacy",
                ActionId = "ui_calibration",
                Title = "A FORMAL DECLARATION",
                Outcome = "Envoys exchange sealed terms before the assembled court.",
                Summary = "This temporary announcement verifies native portrait, banner, and text geometry without changing diplomacy.",
                Terms = "No campaign action, relation, pressure, or reputation value is changed.",
                ActorHeroStringId = actor?.StringId ?? string.Empty,
                ActorName = actor?.Name?.ToString() ?? "Foreign Envoy",
                ActorKingdomName = actorKingdom?.Name?.ToString() ?? "A Foreign Realm",
                ActorKingdomStringId = actorKingdom?.StringId ?? string.Empty,
                ActorPublicReason = "To place the realm's position before the council.",
                TargetHeroStringId = target?.StringId ?? string.Empty,
                TargetName = target?.Name?.ToString() ?? "Your Majesty",
                TargetKingdomName = targetKingdom?.Name?.ToString() ?? "Your Realm",
                TargetKingdomStringId = targetKingdom?.StringId ?? string.Empty,
                TargetPublicReason = "To receive and consider the declaration.",
                WorldDay = TaleWorlds.CampaignSystem.Campaign.Current == null
                    ? 0f
                    : (float)CampaignTime.Now.ToDays,
                Accepted = false
            };
        }
    }
}
#else
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static Task<LiveCommandResult> UiOpenAsync(JObject command) => Task.FromResult(LiveCommandResult.Failed("Court UI calibration is excluded from this build."));
        private static LiveCommandResult UiStatus() => LiveCommandResult.Failed("Court UI calibration is excluded from this build.");
        private static Task<LiveCommandResult> UiSnapshotAsync(JObject command) => Task.FromResult(LiveCommandResult.Failed("Court UI calibration is excluded from this build."));
        private static Task<LiveCommandResult> UiActionAsync(JObject command) => Task.FromResult(LiveCommandResult.Failed("Court UI calibration is excluded from this build."));
        private static Task<LiveCommandResult> UiBackAsync() => Task.FromResult(LiveCommandResult.Failed("Court UI calibration is excluded from this build."));
        private static Task<LiveCommandResult> UiCloseAsync() => Task.FromResult(LiveCommandResult.Failed("Court UI calibration is excluded from this build."));
    }
}
#endif
