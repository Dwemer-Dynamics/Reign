using System;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignCourtPetitionScreenManager
    {
        private const int LayerOrder = 9998;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignCourtPetitionScreenVM _vm;
        private static ReignCourtNobleMatterScreenVM _nobleVm;
        private static ReignCourtLifeScreenVM _lifeVm;
        private static ReignCourtCampaignBehavior _court;
        private static Action _returnToCourt;
        private static CampaignTimeControlMode _priorTimeMode;
        private static bool _restoreTime;

        public static bool IsOpen { get; private set; }
        internal static string AutomationCourtLifeMatterId => _lifeVm?.MatterId ?? "";
        internal static bool AutomationCanDecide => IsOpen && ((_vm != null
            && !_vm.CalibrationMode && !_vm.Busy && !_vm.DecisionComplete)
            || (_nobleVm != null && !_nobleVm.Busy && !_nobleVm.DecisionComplete)
            || (_lifeVm != null && !_lifeVm.Busy && !_lifeVm.DecisionComplete));
        internal static bool AutomationCanContinue => IsOpen && (_vm?.CanContinue == true || _nobleVm?.CanContinue == true || _lifeVm?.CanContinue == true);
        internal static bool AutomationCanTechnicalReturn => IsOpen && (_vm?.CanPostpone == true || _nobleVm?.CanPostpone == true || _lifeVm?.CanPostpone == true);
        internal static bool AutomationCanPostpone => AutomationCanTechnicalReturn;
        internal static bool AutomationScenePreparationComplete => IsOpen
            && (_vm?.ScenePreparationComplete == true || _nobleVm?.ScenePreparationComplete == true || _lifeVm?.ScenePreparationComplete == true);
        internal static bool AutomationSceneReady => IsOpen && (_vm?.SceneReady == true || _nobleVm?.SceneReady == true || _lifeVm?.SceneReady == true);
        internal static Newtonsoft.Json.Linq.JObject AutomationSceneEvidence => _lifeVm?.ScenePreparationEvidence
            ?? _nobleVm?.ScenePreparationEvidence ?? _vm?.ScenePreparationEvidence
            ?? new Newtonsoft.Json.Linq.JObject { ["status"] = "closed", ["ready"] = false };
        internal static string AutomationScenePreparationError => _lifeVm?.ScenePreparationError ?? _nobleVm?.ScenePreparationError
            ?? _vm?.ScenePreparationError ?? string.Empty;
        internal static string AutomationTranscript => _lifeVm?.AutomationTranscript ?? _nobleVm?.AutomationTranscript
            ?? _vm?.AutomationTranscript ?? string.Empty;
        internal static int AutomationExpectedReplyCount => _lifeVm?.AutomationExpectedReplyCount ?? _nobleVm?.AutomationExpectedReplyCount
            ?? (_vm != null ? 1 : 0);
        internal static bool AutomationNaturalConversationComplete => IsOpen
            && (_nobleVm?.AutomationNaturalConversationComplete == true || _lifeVm?.AutomationNaturalConversationComplete == true);
        internal static int AutomationPersistedConversationLineCount =>
            _lifeVm?.AutomationPersistedConversationLineCount ?? _nobleVm?.AutomationPersistedConversationLineCount ?? 0;
        internal static bool AutomationHasPersistedPlayerLine(string rulerText) => IsOpen
            && (_nobleVm?.AutomationHasPersistedPlayerLine(rulerText) == true || _lifeVm?.AutomationHasPersistedPlayerLine(rulerText) == true);
        internal static bool AutomationProviderBackedPhaseComplete(string phase) => IsOpen
            && (_nobleVm?.AutomationProviderBackedPhaseComplete(phase) == true || _lifeVm?.AutomationProviderBackedPhaseComplete(phase) == true);
        internal static string AutomationProviderBackedStatus =>
            _lifeVm?.AutomationProviderBackedStatus ?? _nobleVm?.AutomationProviderBackedStatus ?? "no-active-noble-audience";
        internal static string AutomationNaturalConversationStatus => _lifeVm?.AutomationNaturalConversationStatus ?? _nobleVm?.AutomationNaturalConversationStatus
            ?? "no-active-noble-audience";

        public static void Open(ReignCourtCampaignBehavior court, ReignDocketPetition petition, Action returnToCourt)
        {
            OpenInternal(court, petition, returnToCourt, false);
        }

        public static void Open(ReignCourtCampaignBehavior court, ReignNobleDocketMatter matter,
            Action returnToCourt)
        {
            if (court == null || matter == null || !matter.IsPending || IsOpen) return;
            if (!court.TryActivateNobleMatter(matter.MatterId, out string activationError))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] The noble audience could not open: " + activationError));
                returnToCourt?.Invoke();
                return;
            }
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_court"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Court graphics could not be loaded. The matter remains pending."));
                returnToCourt?.Invoke();
                return;
            }
            ScreenBase host = ScreenManager.TopScreen;
            if (host == null) { returnToCourt?.Invoke(); return; }
            try
            {
                PauseCampaign();
                _host = host;
                _court = court;
                _returnToCourt = returnToCourt;
                _nobleVm = new ReignCourtNobleMatterScreenVM(court, matter, () => Close(true));
                _layer = new GauntletLayer("ReignCourtNobleMatterScreen", LayerOrder, false);
                _layer.LoadMovie("ReignCourtPetitionScreen", _nobleVm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Noble matter screen failed to open: " + ex);
                Cleanup(true);
                returnToCourt?.Invoke();
            }
        }

        public static void OpenForCalibration(ReignCourtCampaignBehavior court, ReignDocketPetition petition)
        {
            OpenInternal(court, petition, null, true);
        }

        public static void Open(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter, Action returnToCourt)
        {
            if (court == null || matter == null || IsOpen) return;
            if (!court.TryActivateCourtLifeMatter(matter, out string error))
            { InformationManager.DisplayMessage(new InformationMessage(error)); returnToCourt?.Invoke(); return; }
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_court") || ScreenManager.TopScreen == null)
            { matter.TechnicalFailure = true; returnToCourt?.Invoke(); return; }
            try
            {
                PauseCampaign(); _host = ScreenManager.TopScreen; _court = court; _returnToCourt = returnToCourt;
                _lifeVm = new ReignCourtLifeScreenVM(court, matter, () => Close(true));
                _layer = new GauntletLayer("ReignCourtLifeScreen", LayerOrder, false);
                _layer.LoadMovie("ReignCourtPetitionScreen", _lifeVm);
                _layer.IsFocusLayer = true; _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true; _host.AddLayer(_layer); _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer); IsOpen = true;
            }
            catch (Exception ex)
            { matter.TechnicalFailure = true; ReignLog.Warn("Court life audience failed: " + ex); Cleanup(true); returnToCourt?.Invoke(); }
        }

        internal static bool TryOpenForAutomation(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter, out string error)
        {
            Open(court, matter, null);
            error = IsOpen && _lifeVm != null && _lifeVm.MatterId == matter?.MatterId ? "" : "The exact production court-life audience could not open.";
            return error.Length == 0;
        }

        internal static bool TryOpenForAutomation(ReignCourtCampaignBehavior court,
            ReignDocketPetition petition, out string error)
        {
            error = string.Empty;
            if (court == null || petition == null || !petition.IsPending)
            {
                error = "The exact pending production petition is required.";
                return false;
            }
            OpenInternal(court, petition, null, false);
            if (IsOpen && _vm != null && !_vm.CalibrationMode) return true;
            error = "The production Court Petition screen did not open.";
            return false;
        }

        internal static bool TryOpenForAutomation(ReignCourtCampaignBehavior court,
            ReignNobleDocketMatter matter, out string error)
        {
            error = string.Empty;
            if (court == null || matter == null || !matter.IsPending)
            {
                error = "The exact pending production noble matter is required.";
                return false;
            }
            Open(court, matter, null);
            if (IsOpen && _nobleVm != null) return true;
            error = string.IsNullOrWhiteSpace(matter.ActivationFailure)
                ? "The production noble-matter audience did not open."
                : matter.ActivationFailure;
            return false;
        }

        internal static bool TryExecuteAutomationAction(string action, out string error)
        {
            return TryExecuteAutomationAction(action, string.Empty, out error);
        }

        internal static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen)
            {
                error = "No production Court audience is open.";
                return false;
            }
            if (_lifeVm != null) return _lifeVm.TryAutomationAction(action, value, out error);
            if (_nobleVm != null) return _nobleVm.TryAutomationAction(action, value, out error);
            if (_vm == null || _vm.CalibrationMode)
            {
                error = "No production Court audience is open.";
                return false;
            }
            switch ((action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-'))
            {
                case "send": return _vm.TryAutomationSend(value, out error);
                case "grant-direct": _vm.ExecuteGrantDirect(); break;
                case "fund-instead": _vm.ExecuteGrantWithGold(); break;
                case "refuse": _vm.ExecuteRefuse(); break;
                case "continue": _vm.ExecuteContinue(); break;
                case "postpone":
                case "technical-return": _vm.ExecutePostpone(); break;
                default:
                    error = "Supported petition actions are send, grant-direct, fund-instead, refuse, continue, and postpone.";
                    return false;
            }
            return _vm?.DecisionComplete == true || !IsOpen;
        }

        private static void OpenInternal(ReignCourtCampaignBehavior court, ReignDocketPetition petition,
            Action returnToCourt, bool calibrationMode)
        {
            if (court == null || petition == null || !petition.IsPending) return;
            if (IsOpen) return;
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_court"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Petition graphics could not be loaded. The petition remains pending."));
                returnToCourt?.Invoke();
                return;
            }

            ScreenBase host = ScreenManager.TopScreen;
            if (host == null) { returnToCourt?.Invoke(); return; }
            try
            {
                PauseCampaign();
                _host = host;
                _court = court;
                _returnToCourt = returnToCourt;
                _vm = new ReignCourtPetitionScreenVM(court, petition, () => Close(true), calibrationMode);
                _layer = new GauntletLayer("ReignCourtPetitionScreen", LayerOrder, false);
                _layer.LoadMovie("ReignCourtPetitionScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Court Petition screen failed to open: " + ex);
                Cleanup(true);
                returnToCourt?.Invoke();
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer))
            {
                Cleanup(true);
                return;
            }
            try
            {
                _vm?.OnFrameTick(dt);
                _nobleVm?.OnFrameTick(dt);
                _lifeVm?.OnFrameTick(dt);
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                if (Input.IsKeyReleased(InputKey.Escape))
                {
                    if (_vm?.CanPostpone == true) _vm.ExecutePostpone();
                    else if (_nobleVm?.CanPostpone == true) _nobleVm.ExecutePostpone();
                    else if (_lifeVm?.CanPostpone == true) _lifeVm.ExecutePostpone();
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Court Petition screen tick failed: " + ex.Message);
                _vm?.RegisterTechnicalFailure("The petition interface encountered a technical failure. No decision was applied.");
                _nobleVm?.RegisterTechnicalFailure("The noble hearing encountered a technical failure. No judgment was applied.");
                _lifeVm?.RegisterTechnicalFailure("The court audience encountered a technical failure. You may return and retry.");
            }
        }

        public static void CloseCalibrationFixture()
        {
            if (_vm?.CalibrationMode == true) Close(false);
        }

        internal static bool TryCloseAutomationAudience()
        {
            if (!IsOpen || _vm?.CalibrationMode == true) return false;
            if (_vm == null && _nobleVm == null && _lifeVm == null) return false;
            Close(false);
            return true;
        }

        private static void Close(bool returnToCourt)
        {
            Action callback = returnToCourt ? _returnToCourt : null;
            Cleanup(true);
            callback?.Invoke();
        }

        private static void Cleanup(bool restoreTime)
        {
            try
            {
                if (_layer != null)
                {
                    _layer.IsFocusLayer = false;
                    _layer.InputRestrictions?.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer);
                }
                if (_host != null && _layer != null && _host.HasLayer(_layer)) _host.RemoveLayer(_layer);
            }
            catch (Exception ex) { ReignLog.Warn("Court Petition cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _nobleVm?.OnFinalize();
            _lifeVm?.OnFinalize();
            _vm = null; _nobleVm = null; _lifeVm = null; _court = null; _layer = null; _host = null; _returnToCourt = null; IsOpen = false;
            if (restoreTime) RestoreCampaignTime();
        }

        private static void PauseCampaign()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlModeLock) return;
            _priorTimeMode = campaign.TimeControlMode;
            _restoreTime = true;
            campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            campaign.SetTimeControlModeLock(true);
        }

        private static void RestoreCampaignTime()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (!_restoreTime || campaign == null) { _restoreTime = false; return; }
            campaign.SetTimeControlModeLock(false);
            campaign.TimeControlMode = _priorTimeMode;
            _restoreTime = false;
        }
    }
}
