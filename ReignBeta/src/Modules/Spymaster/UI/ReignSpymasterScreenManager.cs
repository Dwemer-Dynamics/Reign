using System;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignSpymasterScreenManager
    {
        private const int LayerOrder = 9998;
        private const float ReferenceWidth = 1672f;
        private const float ReferenceHeight = 941f;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignSpymasterScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        public static bool IsOpen { get; private set; }
        public static string AutomationPage => _vm?.AutomationPage ?? string.Empty;
        public static string AutomationTargetId => _vm?.AutomationTargetId ?? string.Empty;
        public static string AutomationActionId => _vm?.AutomationActionId ?? string.Empty;
        public static int AutomationTargetCount => _vm?.AutomationTargetCount ?? 0;
        public static int AutomationActionCount => _vm?.AutomationActionCount ?? 0;
        public static int AutomationReportCount => _vm?.AutomationReportCount ?? 0;
        public static bool AutomationCanBegin => _vm?.AutomationCanBegin == true;
        public static string AutomationStatus => _vm?.AutomationStatus ?? string.Empty;
        public static string AutomationOperationDetail => _vm?.AutomationOperationDetail ?? string.Empty;
        public static string AutomationPeopleGroup => _vm?.AutomationPeopleGroup ?? string.Empty;
        public static string AutomationSocialScope => _vm?.AutomationSocialScope ?? string.Empty;
        public static string AutomationSpymasterHeroId => _court?.ActiveSpymaster?.StringId ?? string.Empty;
        public static string AutomationSpymasterName => _court?.ActiveSpymaster?.Name?.ToString() ?? string.Empty;

        public static System.Threading.Tasks.Task<string> TryExecuteAutomationAppointmentAsync(string value)
        {
            if (!IsOpen || _vm == null)
                return System.Threading.Tasks.Task.FromResult("The Spymaster screen is not open.");
            return _vm.TryExecuteAutomationAppointmentAsync(value);
        }

        public static System.Threading.Tasks.Task<string> TryExecuteAutomationSocialSelectionAsync(string value)
        {
            if (!IsOpen || _vm == null)
                return System.Threading.Tasks.Task.FromResult("The Spymaster screen is not open.");
            return _vm.TryExecuteAutomationSocialSelectionAsync(value);
        }

        public static System.Threading.Tasks.Task<string> TryExecuteAutomationSocialTargetSelectionAsync(string value)
        {
            if (!IsOpen || _vm == null)
                return System.Threading.Tasks.Task.FromResult("The Spymaster screen is not open.");
            return _vm.TryExecuteAutomationSocialTargetSelectionAsync(value);
        }

        public static bool TryExecuteAutomationAction(string action, out string error)
        {
            return TryExecuteAutomationAction(action, string.Empty, out error);
        }

        public static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _vm == null) { error = "The Spymaster screen is not open."; return false; }
            switch ((action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-'))
            {
                case "intelligence": _vm.ExecuteIntelligence(); return true;
                case "subterfuge": _vm.ExecuteSubterfuge(); return true;
                case "reputation": _vm.ExecuteReputation(); return true;
                case "archive": _vm.ExecuteArchive(); return true;
                case "lands": _vm.ExecuteLands(); return true;
                case "people": _vm.ExecutePeople(); return true;
                case "next-people-group": _vm.ExecuteNextPeopleGroup(); return true;
                case "next-social-scope": _vm.ExecuteNextSocialScope(); return true;
                case "target-dropdown": _vm.ExecuteToggleTargetDropdown(); return true;
                case "action-dropdown": _vm.ExecuteToggleActionDropdown(); return true;
                case "social-dropdown": _vm.ExecuteToggleSocialDropdown(); return true;
                case "select-target": return _vm.TryExecuteAutomationSelection("target", value, out error);
                case "select-action": return _vm.TryExecuteAutomationSelection("action", value, out error);
                case "select-social-item": return _vm.TryExecuteAutomationSelection("social", value, out error);
                case "begin": return _vm.TryExecuteAutomationBegin(value, out error);
                default: error = "Unsupported Spymaster automation action '" + action + "'."; return false;
            }
        }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.IsRuleModeActive) return;
            court.EnsureServerSessionAligned();
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_spymaster"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Spymaster graphics could not be loaded. See the Reign log for details."));
                return;
            }
            ReignCourtScreenManager.Close();
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            try
            {
                _court = court;
                _vm = new ReignSpymasterScreenVM(court, () => Close(true));
                _layer = new GauntletLayer("ReignSpymasterScreen", LayerOrder, false);
                ApplyScale();
                _layer.LoadMovie("ReignSpymasterScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                _layer.Input.IsKeysAllowed = false;
                _layer.Input.IsMouseButtonAllowed = true;
                _layer.Input.IsMouseWheelAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Spymaster screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Spymaster screen could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer)) { Close(false); return; }
            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                _vm?.OnFrameTick(dt);
                ApplyScale();
                if (Input.IsKeyReleased(InputKey.Escape)) Close(true);
            }
            catch (Exception ex) { ReignLog.Warn("Spymaster screen tick failed: " + ex.Message); Close(true); }
        }

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null) return;
            float width = Screen.RealScreenResolutionWidth;
            float height = Screen.RealScreenResolutionHeight;
            if (width <= 0f || height <= 0f) return;
            float authoredScale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
            float nativeScale = height / 1080f;
            float modifier = authoredScale * _layer.Scale / Math.Max(0.01f, nativeScale);
            if (Math.Abs(_layer.UIContext.ScaleModifier - modifier) > 0.0001f) _layer.UIContext.ScaleModifier = modifier;
        }

        public static void Close(bool returnToCourt)
        {
            ReignCourtCampaignBehavior court = _court;
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
            catch (Exception ex) { ReignLog.Warn("Spymaster cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _vm = null; _layer = null; _host = null; _court = null; IsOpen = false;
            if (returnToCourt && court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court);
        }
    }
}
