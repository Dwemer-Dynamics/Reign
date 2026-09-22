using System;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI.ViewModels;
using ReignBeta.UI.Widgets;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignIndividualChatScreenManager
    {
        private const int ModalInputLayerOrder = 9999;
        private static readonly object ActiveStateDisableRequester = new object();
        private static ScreenBase _hostScreen;
        private static GauntletLayer _gauntletLayer;
        private static ReignIndividualChatScreenVM _dataSource;
        private static bool _loggedFocusIssue;
        private static bool _activeStateDisableRegistered;
        private static Action _returnCallback;
        private static string _officialPostingId = string.Empty;

        public static bool IsOpen { get; private set; }

        public static ReignIndividualChatScreenVM ActiveViewModel
        {
            get { return _dataSource; }
        }

        public static string AutomationInputText => _dataSource?.InputText ?? string.Empty;
        public static string AutomationBusyText => _dataSource?.BusyText ?? string.Empty;
        public static int AutomationChatLineCount => _dataSource?.ChatLines?.Count ?? 0;
        public static bool AutomationBusy => _dataSource?.IsBusy == true;
        public static bool AutomationCalibrationFixture => _dataSource?.IsCalibrationMode == true;
        public static bool AutomationModalFocusOwned => IsOpen
            && _gauntletLayer != null
            && _gauntletLayer.IsFocusLayer
            && ScreenManager.FocusedLayer == _gauntletLayer;
        public static bool AutomationActiveStateBlocked => IsOpen && _activeStateDisableRegistered;
        public static JObject AutomationPregnancyState => _dataSource?.PregnancyAutomationStateJson()
            ?? new JObject();
        public static bool AutomationPortraitZoomVisible => _dataSource?.IsPortraitZoomVisible == true;
        public static string AutomationPortraitZoomSide
        {
            get
            {
                if (_dataSource?.IsPortraitZoomVisible != true) return string.Empty;
                if (string.Equals(_dataSource.ZoomPortraitId, _dataSource.PlayerPortraitId, StringComparison.Ordinal))
                    return "player";
                if (string.Equals(_dataSource.ZoomPortraitId, _dataSource.NpcPortraitId, StringComparison.Ordinal))
                    return "npc";
                return "other";
            }
        }

        public static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _dataSource == null)
            {
                error = "The Individual Chat screen is not open.";
                return false;
            }
            if (!_dataSource.IsCalibrationMode)
            {
                error = "Individual Chat automation is restricted to the provider-free calibration fixture.";
                return false;
            }

            string normalized = (action ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant();
            switch (normalized)
            {
                case "set-input":
                    _dataSource.InputText = value ?? string.Empty;
                    return true;
                case "send":
                    _dataSource.ExecuteSend();
                    return true;
                case "toggle-player-portrait":
                    _dataSource.ExecuteTogglePlayerPortraitZoom();
                    return true;
                case "toggle-npc-portrait":
                    _dataSource.ExecuteToggleNpcPortraitZoom();
                    return true;
                case "close-portrait":
                    _dataSource.ExecuteClosePortraitZoom();
                    return true;
                case "show-pregnancy-warning":
                    return _dataSource.TryShowPregnancyWarningFixture(out error);
                default:
                    error = "Unsupported Individual Chat action '" + action
                        + "'. Use set-input, send, toggle-player-portrait, toggle-npc-portrait, close-portrait, or show-pregnancy-warning.";
                    return false;
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen)
            {
                return;
            }

            if (_hostScreen == null || _gauntletLayer == null || ScreenManager.TopScreen != _hostScreen)
            {
                Close();
                return;
            }

            if (!TryApplyInputLock())
            {
                return;
            }

            if (ScreenManager.FocusedLayer != _gauntletLayer)
            {
                if (!_loggedFocusIssue)
                {
                    ReignLog.Warn("Individual chat focus was lost. Attempting to reclaim focus.");
                    _loggedFocusIssue = true;
                }

                if (!TryReclaimFocus())
                {
                    return;
                }
            }

            if (IsEscapeReleased())
            {
                if (_dataSource == null || !_dataSource.IsPregnancyWarningVisible)
                {
                    Close();
                }
            }
            else if (_dataSource != null
                && !_dataSource.IsPortraitZoomVisible
                && !_dataSource.IsPregnancyWarningVisible
                && IsSubmitReleased())
            {
                _dataSource.ExecuteSend();
            }
        }

        private static bool IsSubmitReleased()
        {
            return Input.IsKeyReleased(InputKey.Enter)
                || Input.IsKeyReleased(InputKey.NumpadEnter)
                || (_gauntletLayer?.Input != null
                    && (_gauntletLayer.Input.IsKeyReleased(InputKey.Enter)
                        || _gauntletLayer.Input.IsKeyReleased(InputKey.NumpadEnter)));
        }

        public static void OpenForHero(Hero hero)
        {
            OpenForHero(hero, null, null);
        }

        public static void OpenForCalibration(Hero hero)
        {
            OpenForHeroInternal(hero, null, null, true);
        }

        public static void OpenForHero(Hero hero, JObject conversationContext, Action returnCallback)
        {
            OpenForHeroInternal(hero, conversationContext, returnCallback, false);
        }

        private static void OpenForHeroInternal(
            Hero hero,
            JObject conversationContext,
            Action returnCallback,
            bool calibrationMode)
        {
            if (!ReignConversationEligibility.TryValidateAdultConversationHero(hero, out string eligibilityError))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + eligibilityError, Color.FromUint(0xFFFFCC66)));
                return;
            }

            if (IsOpen)
            {
                Close(false);
            }

            ScreenBase host = ScreenManager.TopScreen;
            if (host == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Could not find a screen for chat.", Color.FromUint(0xFFFFCC66)));
                return;
            }

            try
            {
                _hostScreen = host;
                _returnCallback = returnCallback;
                _officialPostingId = conversationContext?.Value<string>("postingId") ?? string.Empty;
                _dataSource = new ReignIndividualChatScreenVM(
                    hero, Close, OpenPortraitRequestOverlay, true, conversationContext, calibrationMode);
                _gauntletLayer = new GauntletLayer("ReignIndividualChatScreen", ModalInputLayerOrder, true);
                _gauntletLayer.LoadMovie("ReignIndividualChatScreen", _dataSource);
                _gauntletLayer.IsFocusLayer = true;
                _gauntletLayer.ActiveCursor = CursorType.Default;
                ApplyInputLock();
                _hostScreen.AddLayer(_gauntletLayer);
                _hostScreen.MouseVisible = true;
                ScreenManager.TrySetFocus(_gauntletLayer);
                RegisterActiveStateDisableRequest();
                IsOpen = true;
                ReignLog.Info("Opened individual chat with " + hero.StringId + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat failed to open: " + ex.Message);
                Close();
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Chat could not open: " + ex.Message, Color.FromUint(0xFFFFCC66)));
            }
        }

        public static void Close()
        {
            Close(true);
        }

        public static bool TryCloseAutomationCalibrationFixture()
        {
            if (!IsOpen || !AutomationCalibrationFixture) return false;
            Close();
            return true;
        }

        public static void CloseIfOfficialAmbassador(string postingId)
        {
            if (!IsOpen || string.IsNullOrWhiteSpace(postingId)
                || !string.Equals(_officialPostingId, postingId, StringComparison.OrdinalIgnoreCase)) return;
            Close();
        }

        private static void Close(bool invokeReturnCallback)
        {
            Action returnCallback = invokeReturnCallback ? _returnCallback : null;
            _returnCallback = null;
            _officialPostingId = string.Empty;
            if (_gauntletLayer != null)
            {
                try
                {
                    _gauntletLayer.IsFocusLayer = false;
                    _gauntletLayer.InputRestrictions?.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_gauntletLayer);
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Individual chat focus cleanup failed: " + ex.Message);
                }
            }

            if (_hostScreen != null && _gauntletLayer != null)
            {
                try
                {
                    if (_hostScreen.HasLayer(_gauntletLayer))
                    {
                        _hostScreen.RemoveLayer(_gauntletLayer);
                    }
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Individual chat layer removal failed: " + ex.Message);
                }
            }

            _dataSource?.OnFinalize();
            _dataSource = null;
            _gauntletLayer = null;
            _hostScreen = null;
            _loggedFocusIssue = false;
            UnregisterActiveStateDisableRequest();
            IsOpen = false;
            returnCallback?.Invoke();
        }

        private static void ApplyInputLock()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _gauntletLayer.Input.IsKeysAllowed = true;
            _gauntletLayer.Input.IsMouseButtonAllowed = true;
            _gauntletLayer.Input.IsMouseWheelAllowed = true;
        }

        private static bool TryApplyInputLock()
        {
            try
            {
                ApplyInputLock();
                return true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat input lock failed; closing overlay: " + ex.Message);
                Close();
                return false;
            }
        }

        private static bool TryReclaimFocus()
        {
            try
            {
                ScreenManager.TrySetFocus(_gauntletLayer);
                return true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat focus reclaim failed; closing overlay: " + ex.Message);
                Close();
                return false;
            }
        }

        private static bool IsEscapeReleased()
        {
            try
            {
                return _gauntletLayer?.Input != null && _gauntletLayer.Input.IsKeyReleased(InputKey.Escape);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat input read failed; closing overlay: " + ex.Message);
                Close();
                return false;
            }
        }

        private static void OpenPortraitRequestOverlay(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            ReignPortraitBridge.RequestPortrait(hero);
        }

        private static void RegisterActiveStateDisableRequest()
        {
            if (_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(ActiveStateDisableRequester);
                _activeStateDisableRegistered = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat active-state lock failed: " + ex.Message);
            }
        }

        private static void UnregisterActiveStateDisableRequest()
        {
            if (!_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(ActiveStateDisableRequester);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Individual chat active-state unlock failed: " + ex.Message);
            }
            finally
            {
                _activeStateDisableRegistered = false;
            }
        }
    }
}
