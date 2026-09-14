#if !REIGN_EXCLUDE_COURT
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
    public static class ReignAmbassadorScreenManager
    {
        private const int LayerOrder = 9998;
        private const float ReferenceWidth = 1672f;
        private const float ReferenceHeight = 941f;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignAmbassadorScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;

        public static bool IsOpen { get; private set; }
        public static int AutomationCount => _vm?.AutomationCount ?? 0;
        public static string AutomationPostingId => _vm?.AutomationPostingId ?? string.Empty;
        public static string AutomationOriginKingdomId => _vm?.AutomationOriginKingdomId ?? string.Empty;
        public static string AutomationHeroId => _vm?.AutomationHeroId ?? string.Empty;
        public static string AutomationStatus => _vm?.AutomationStatus ?? string.Empty;
        public static bool AutomationCanSpeak => _vm?.CanSpeak == true;
        public static bool AutomationCanRemove => _vm?.CanRemove == true;

        public static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _vm == null)
            {
                error = "The Ambassador screen is not open.";
                return false;
            }
            switch ((action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-'))
            {
                case "select":
                case "select-ambassador": return _vm.TrySelectAutomation(value, out error);
                case "speak":
                    if (!_vm.CanSpeak) { error = "The selected ambassador is not resident or the screen is busy."; return false; }
                    _vm.ExecuteSpeak();
                    return true;
                case "dismiss":
                    if (!_vm.CanRemove) { error = "The selected ambassador cannot be dismissed right now."; return false; }
                    _vm.ExecuteRemoveAmbassador();
                    return true;
                default:
                    error = "Unsupported Ambassador automation action '" + (action ?? string.Empty) + "'.";
                    return false;
            }
        }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.HasRoyalCommandAccess)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Ambassador commands are available only while ruling from the capital."));
                return;
            }
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_ambassador"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Ambassador graphics could not be loaded. See the Reign log for details."));
                return;
            }

            ReignCourtScreenManager.Close();
            _host = ScreenManager.TopScreen;
            if (_host == null) return;

            try
            {
                _court = court;
                _vm = new ReignAmbassadorScreenVM(court, () => Close(true));
                _layer = new GauntletLayer("ReignAmbassadorScreen", LayerOrder, false);
                ApplyScale();
                _layer.LoadMovie("ReignAmbassadorScreen", _vm);
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
                ReignLog.Warn("Ambassador screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Ambassador screen could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer))
            {
                Close(false);
                return;
            }

            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                ApplyScale();
                if (Input.IsKeyReleased(InputKey.Escape)) Close(true);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Ambassador screen tick failed: " + ex.Message);
                Close(true);
            }
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
            if (Math.Abs(_layer.UIContext.ScaleModifier - modifier) > 0.0001f)
                _layer.UIContext.ScaleModifier = modifier;
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
            catch (Exception ex)
            {
                ReignLog.Warn("Ambassador screen cleanup failed: " + ex.Message);
            }

            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            _court = null;
            IsOpen = false;
            if (returnToCourt && court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court);
        }
    }
}
#endif
