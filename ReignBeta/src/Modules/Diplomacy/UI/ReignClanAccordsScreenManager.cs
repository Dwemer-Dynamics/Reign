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
    public static class ReignClanAccordsScreenManager
    {
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignClanAccordsVM _vm;
        private static ReignCourtCampaignBehavior _court;
        private static float _refreshElapsed;
        public static bool IsOpen { get; private set; }

        public static void Open(ReignCourtCampaignBehavior court, Func<ReignClanAccordsScreenData> snapshot, Func<string, string> cancel)
        {
            if (court?.IsRuleModeActive != true || snapshot == null || cancel == null) return;
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_clan_accords"))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Clan Accords graphics could not be loaded. See the Reign log."));
                return;
            }
            // The Economy layer must release focus before the new modal owns it.
            ReignCourtEconomicReportScreenManager.Close(false);
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            _court = court;
            try
            {
                _vm = new ReignClanAccordsVM(snapshot, cancel, () => Close(true));
                _layer = new GauntletLayer("ReignClanAccordsScreen", 9998, false);
                ApplyScale();
                _layer.LoadMovie("ReignClanAccordsScreen", _vm);
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
                _refreshElapsed = 0f;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Clan Accords screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Clan Accords could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer) || _court?.IsRuleModeActive != true)
            { Close(false); return; }
            try
            {
                ApplyScale();
                _refreshElapsed += dt;
                if (_refreshElapsed >= 1f) { _refreshElapsed = 0f; _vm.RefreshIfChanged(); }
                if (Input.IsKeyReleased(InputKey.Escape)) _vm.ExecuteClose();
            }
            catch (Exception ex) { ReignLog.Warn("Clan Accords screen tick failed: " + ex); Close(true); }
        }

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null) return;
            float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
            if (width <= 0 || height <= 0) return;
            float modifier = Math.Min(width / 1672f, height / 941f) * _layer.Scale / Math.Max(.01f, height / 1080f);
            if (Math.Abs(_layer.UIContext.ScaleModifier - modifier) > .0001f) _layer.UIContext.ScaleModifier = modifier;
        }

        public static void Close(bool returnToEconomy)
        {
            var court = _court;
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
            catch (Exception ex) { ReignLog.Warn("Clan Accords cleanup failed: " + ex); }
            _vm?.OnFinalize();
            _vm = null; _layer = null; _host = null; _court = null; IsOpen = false;
            if (returnToEconomy && court?.IsRuleModeActive == true) ReignCourtEconomicReportScreenManager.Open(court);
        }
    }
}
