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
    public static class ReignRoyalCouncilScreenManager
    {
        private const int LayerOrder = 9998;
        private const float ReferenceWidth = 1672f;
        private const float ReferenceHeight = 941f;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignRoyalCouncilScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        public static bool IsOpen { get; private set; }
        public static int AutomationProviderCallCount => _vm?.AutomationProviderCallCount ?? 0;
        public static string AutomationTranscript => _vm?.AutomationTranscript ?? string.Empty;
        public static bool AutomationBusy => _vm?.IsBusy == true;
        public static bool AutomationCalibrationFixture => _vm?.AutomationCalibrationFixture == true;

        public static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            if (!IsOpen || _vm == null) { error = "The Royal Council screen is not open."; return false; }
            return _vm.TryAutomation(action, value, out error);
        }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            Open(court, false);
        }

        public static void OpenForCalibration(ReignCourtCampaignBehavior court)
        {
            Open(court, true);
        }

        private static void Open(ReignCourtCampaignBehavior court, bool calibrationFixture)
        {
            if (court == null || !court.HasRoyalCommandAccess)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] The Royal Council may sit only in the capital."));
                return;
            }
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_royal_council"))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Royal Council graphics could not be loaded."));
                return;
            }
            ReignCourtScreenManager.Close();
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            try
            {
                _court = court;
                _vm = new ReignRoyalCouncilScreenVM(court, () => Close(true), calibrationFixture);
                _layer = new GauntletLayer("ReignRoyalCouncilScreen", LayerOrder, false);
                ApplyScale();
                _layer.LoadMovie("ReignRoyalCouncilScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                _layer.Input.IsMouseButtonAllowed = true;
                _layer.Input.IsMouseWheelAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Royal Council screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Royal Council could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer)) { Close(false); return; }
            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                ApplyScale();
                if (Input.IsKeyReleased(InputKey.Escape)) Close(true);
            }
            catch (Exception ex) { ReignLog.Warn("Royal Council screen tick failed: " + ex.Message); Close(true); }
        }

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null) return;
            float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
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
                if (_layer != null) { _layer.IsFocusLayer = false; _layer.InputRestrictions?.ResetInputRestrictions(); ScreenManager.TryLoseFocus(_layer); }
                if (_host != null && _layer != null && _host.HasLayer(_layer)) _host.RemoveLayer(_layer);
            }
            catch (Exception ex) { ReignLog.Warn("Royal Council cleanup failed: " + ex.Message); }
            _vm?.OnFinalize(); _vm = null; _layer = null; _host = null; _court = null; IsOpen = false;
            if (returnToCourt && court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court);
        }
    }
}
#endif
