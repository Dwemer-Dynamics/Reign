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
    public static class ReignCourtScreenManager
    {
        private const int LayerOrder = 9997;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignCourtScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        public static bool IsOpen { get; private set; }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.IsRuleModeActive) return;
            court.EnsureServerSessionAligned();
            if (IsOpen) Close();
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_court"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Royal Court graphics could not be loaded. See the Reign log for details."));
                return;
            }
            ScreenBase host = ScreenManager.TopScreen;
            if (host == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Rule Mode requires the campaign map screen."));
                return;
            }
            try
            {
                _host = host;
                _court = court;
                _vm = new ReignCourtScreenVM(court, Close);
                // This layer must preserve the MapScreen framebuffer. Passing
                // shouldClear=true paints the exposed campaign area black.
                _layer = new GauntletLayer("ReignCourtScreen", LayerOrder, false);
                _vm.SetLargeCourtLayout(false);
                ApplyScale();
                _layer.LoadMovie("ReignCourtScreen", _vm);
                // The Court needs focus for its mouse controls, but keyboard input
                // stays with the game/OS so native shortcuts such as Win+Shift+S
                // are not swallowed. Escape is read from the global input service.
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.ResetInputRestrictions();
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
                ReignLog.Warn("Rule Mode screen failed to open: " + ex);
                Close();
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Rule Mode screen could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            ReignCourtCampaignBehavior.Instance?.ProcessCourtLifeBackgroundWork(dt);
            ReignCourtPetitionScreenManager.ApplicationTick(dt);
            if (!IsOpen) return;
            // Authority can be incomplete for a frame or two while a restored
            // campaign settles. Keep the workspace mounted during that bounded
            // observation window; command methods still require IsRuleModeActive.
            if (_court?.ShouldKeepCourtScreenOpen != true) { Close(); return; }
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer)) { Close(); return; }
            try
            {
                ApplyScale();
                _vm?.OnFrameTick(dt);
                if (IsPointerInsideCourt())
                {
                    _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                }
                else
                {
                    _layer.InputRestrictions.ResetInputRestrictions();
                }
                if (Input.IsKeyReleased(InputKey.Escape)) Close();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Rule Mode screen tick failed: " + ex.Message);
                Close();
            }
        }

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null || _vm == null) return;
            float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
            if (width <= 0f || height <= 0f) return;
            var layout = ReignCourtLayout.ComputeNativeLayout(width, height, _layer.UIContext.Scale);
            if (Math.Abs(_layer.UIContext.ScaleModifier - layout.ScaleModifier) > 0.0001f)
                _layer.UIContext.ScaleModifier = layout.ScaleModifier;
            _vm.CourtWorkspaceTopInset = layout.TopInset;
        }

        // Called synchronously by the existing game-thread heartbeat. Observation
        // only: no UI opening, input, calibration ownership or campaign mutation.
        internal static object AutomationLayoutSnapshot
        {
            get
            {
                const string schema = "reign-court-layout-observation-v1";
                var context = _layer?.UIContext;
                if (!IsOpen || context?.Root == null) return new { schema, open = false };
                var home = context.Root.FindChild("CompactCourtHome", true);
                var office = context.Root.FindChild("CompactCourtOffice", true);
                var panel = home?.IsVisible == true ? home : office?.IsVisible == true ? office : null;
                float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
                var expected = ReignCourtLayout.Compute(width, height);
                float expectedTop = expected.TopInset * expected.Scale;
                return new
                {
                    schema, open = true, panelVisible = panel != null,
                    physicalWidth = width, physicalHeight = height,
                    nativeBaseScale = context.Scale, layerScale = _layer.Scale,
                    scaleModifier = context.ScaleModifier, customScale = context.CustomScale,
                    expectedScale = expected.Scale,
                    scaleError = context.CustomScale - expected.Scale,
                    expectedTop, expectedBottom = expectedTop + ReignCourtLayout.Height * expected.Scale,
                    x = panel?.GlobalPosition.X ?? 0f, y = panel?.GlobalPosition.Y ?? 0f,
                    width = panel?.Size.X ?? 0f, height = panel?.Size.Y ?? 0f,
                    bottom = (panel?.GlobalPosition.Y ?? 0f) + (panel?.Size.Y ?? 0f)
                };
            }
        }

        private static bool IsPointerInsideCourt()
        {
            if (_layer?.UIContext?.Root == null) return false;

            var home = _layer.UIContext.Root.FindChild("CompactCourtHome", true);
            var office = _layer.UIContext.Root.FindChild("CompactCourtOffice", true);
            var panel = home?.IsVisible == true ? home : office?.IsVisible == true ? office : null;
            if (panel == null) return false;

            var pointer = _layer.Input.GetMousePositionPixel();
            var position = panel.GlobalPosition;
            var size = panel.Size;
            return pointer.X >= position.X
                && pointer.Y >= position.Y
                && pointer.X < position.X + size.X
                && pointer.Y < position.Y + size.Y;
        }

        public static void Close()
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
            catch (Exception ex) { ReignLog.Warn("Rule Mode screen cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _vm = null;
            _court = null;
            _layer = null;
            _host = null;
            IsOpen = false;
        }
    }
}
