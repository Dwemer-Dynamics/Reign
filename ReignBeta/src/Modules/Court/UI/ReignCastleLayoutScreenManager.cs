#if !REIGN_EXCLUDE_COURT
using System;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignCastleLayoutScreenManager
    {
        private const int LayerOrder = 9998;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignCastleLayoutScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        private static ReignCourtCampaignBehavior _pendingCourt;
        private static ReignCourtCampaignBehavior _pendingReturnToCourt;
        private static bool _pendingFromKeep;
        private static bool _fromKeep;

        public static bool IsOpen { get; private set; }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.IsRuleModeActive) return;
            // Court button commands run while Gauntlet is dispatching the old
            // movie's input event. Adding the Castle layer from inside that
            // callback lets the old layer's finalization discard the new layer
            // before the next frame. Queue the transition and attach only from
            // ApplicationTick, after Court teardown has completed.
            _pendingCourt = court;
            _pendingFromKeep = false;
        }

        public static void OpenFromKeep(ReignCourtCampaignBehavior court)
        {
            if (court == null || Settlement.CurrentSettlement?.IsTown != true) return;
            _pendingCourt = court;
            _pendingFromKeep = true;
        }

        private static void OpenNow(ReignCourtCampaignBehavior court, bool fromKeep)
        {
            if (court == null || (!fromKeep && !court.IsRuleModeActive)) return;
            if (IsOpen) Close(false);

            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_castle_layout"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Castle Layout graphics could not be loaded. See the Reign log for details."));
                return;
            }

            ScreenBase host = ScreenManager.TopScreen;
            if (host == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Castle Layout requires the campaign map screen."));
                return;
            }

            try
            {
                _court = court;
                _fromKeep = fromKeep;
                _host = host;
                _vm = new ReignCastleLayoutScreenVM(ReturnToOrigin, LeaveForCastleChat, OpenFamilyChambers,
                    OpenGovernment, court, fromKeep);
                _layer = new GauntletLayer("ReignCastleLayoutScreen", LayerOrder, false);
                _layer.LoadMovie("ReignCastleLayoutScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                _layer.Input.IsKeysAllowed = false;
                _layer.Input.IsMouseButtonAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
                ReignLog.Info("Castle Layout opened on host=" + (_host.GetType().FullName ?? _host.GetType().Name) + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Castle Layout screen failed to open: " + ex);
                Close(false);
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Castle Layout could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (_pendingReturnToCourt != null && !IsOpen)
            {
                ReignCourtCampaignBehavior court = _pendingReturnToCourt;
                _pendingReturnToCourt = null;
                if (court.IsRuleModeActive) ReignCourtScreenManager.Open(court);
                return;
            }

            if (_pendingCourt != null && !IsOpen)
            {
                ReignCourtCampaignBehavior court = _pendingCourt;
                bool fromKeep = _pendingFromKeep;
                _pendingCourt = null;
                _pendingFromKeep = false;
                OpenNow(court, fromKeep);
                // Do not validate HasLayer during the same tick that AddLayer
                // runs; some map-screen implementations commit it at frame end.
                return;
            }

            if (!IsOpen) return;
            bool originValid = _fromKeep ? Settlement.CurrentSettlement?.IsTown == true : _court?.IsRuleModeActive == true;
            if (!originValid || _host == null || _layer == null || ScreenManager.TopScreen != _host || !SafeHasLayer(_host, _layer))
            {
                ReignLog.Warn("Castle Layout lost its host layer: host="
                    + (_host?.GetType().FullName ?? "<null>")
                    + " top=" + (ScreenManager.TopScreen?.GetType().FullName ?? "<null>")
                    + " layer=" + (_layer == null ? "<null>" : "present")
                    + " attached=" + (_host != null && _layer != null && SafeHasLayer(_host, _layer)) + ".");
                Close(false);
                return;
            }

            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                if (Input.IsKeyReleased(InputKey.Escape)) ReturnToOrigin();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Castle Layout screen tick failed: " + ex.Message);
                Close(false);
            }
        }

        private static void ReturnToOrigin()
        {
            ReignCourtCampaignBehavior court = _court;
            bool fromKeep = _fromKeep;
            Close(false);
            if (fromKeep)
            {
                try { TaleWorlds.CampaignSystem.GameMenus.GameMenu.ActivateGameMenu("town_keep"); }
                catch (Exception ex) { ReignLog.Warn("Could not return to the native keep menu: " + ex.Message); }
            }
            else if (court?.IsRuleModeActive == true) _pendingReturnToCourt = court;
        }

        private static void LeaveForCastleChat()
        {
            // Room entry deliberately closes only the layout. Returning to Court
            // here would race the asynchronously prepared Castle Chat layer.
            Close(false);
        }

        private static void OpenFamilyChambers()
        {
            ReignCourtCampaignBehavior court = _court;
            bool fromKeep = _fromKeep;
            Close(false);
            if (fromKeep) ReignFamilyChambersScreenManager.OpenFromKeep(court, () => OpenFromKeep(court));
            else ReignFamilyChambersScreenManager.Open(court);
        }

        private static void OpenGovernment()
        {
            ReignCourtCampaignBehavior court = _court;
            bool fromKeep = _fromKeep;
            Kingdom kingdom = Settlement.CurrentSettlement?.MapFaction as Kingdom ?? Clan.PlayerClan?.Kingdom;
            Close(false);
            if (kingdom == null) return;
            ReignGovernmentScreenManager.Open(kingdom, kingdom.Leader == Hero.MainHero,
                () => { if (fromKeep) OpenFromKeep(court); else if (court?.IsRuleModeActive == true) Open(court); });
        }

        public static void Close(bool returnToCourt = false)
        {
            ReignCourtCampaignBehavior court = _court;
            _pendingCourt = null;
            _pendingFromKeep = false;
            _pendingReturnToCourt = returnToCourt && court?.IsRuleModeActive == true ? court : null;

            if (_layer != null)
            {
                try { _layer.IsFocusLayer = false; }
                catch (Exception ex) { ReignLog.Warn("Castle Layout focus cleanup failed: " + ex.Message); }
                try { _layer.InputRestrictions?.ResetInputRestrictions(); }
                catch (Exception ex) { ReignLog.Warn("Castle Layout input cleanup failed: " + ex.Message); }
                try { ScreenManager.TryLoseFocus(_layer); }
                catch (Exception ex) { ReignLog.Warn("Castle Layout focus release failed: " + ex.Message); }
            }

            if (_host != null && _layer != null)
            {
                try
                {
                    if (_host.HasLayer(_layer)) _host.RemoveLayer(_layer);
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Castle Layout layer removal failed: " + ex.Message);
                }
            }

            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            _court = null;
            _fromKeep = false;
            IsOpen = false;
        }

        private static bool SafeHasLayer(ScreenBase host, GauntletLayer layer)
        {
            try { return host?.HasLayer(layer) == true; }
            catch { return false; }
        }
    }
}
#endif
