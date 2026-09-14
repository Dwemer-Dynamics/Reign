using System;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.UI.ViewModels;
using ReignBeta.UI.Widgets;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignCorrespondenceScreenManager
    {
        private const int LayerOrder = 9999;
        private static readonly object DisableRequester = new object();
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignCorrespondenceScreenVM _vm;
        private static bool _pending;
        private static Hero _pendingHero;
        private static bool _pendingCalibration;
        private static bool _disableRegistered;
        private static bool _closeRequested;

        public static bool IsOpen { get; private set; }

        public static void Open(Hero preselected = null)
        {
            OpenInternal(preselected, false);
        }

        public static void OpenForCalibration(Hero preselected)
        {
            OpenInternal(preselected, true);
        }

        private static void OpenInternal(Hero preselected, bool calibrationMode)
        {
            if (IsOpen) Close();
            ScreenBase host = ScreenManager.TopScreen;
            if (!CanAttach(host))
            {
                _pending = true;
                _pendingHero = preselected;
                _pendingCalibration = calibrationMode;
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Correspondence will open on the campaign map."));
                return;
            }

            try
            {
                _host = host;
                _vm = new ReignCorrespondenceScreenVM(RequestClose, preselected, calibrationMode);
                _layer = new GauntletLayer("ReignCorrespondenceScreen", LayerOrder, true);
                _layer.LoadMovie("ReignCorrespondenceScreen", _vm);
                _host.AddLayer(_layer);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                ApplyInputLock();
                ScreenManager.TrySetFocus(_layer);
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(DisableRequester);
                _disableRegistered = true;
                _host.MouseVisible = true;
                _pending = false;
                _pendingHero = null;
                _pendingCalibration = false;
                _closeRequested = false;
                IsOpen = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Correspondence screen failed to open: " + ex.Message);
                Close();
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (_pending && !IsOpen && CanAttach(ScreenManager.TopScreen))
                OpenInternal(_pendingHero, _pendingCalibration);
            if (!IsOpen)
            {
                TryHotkey();
                return;
            }

            if (_closeRequested)
            {
                Close();
                return;
            }

            if (_host == null || _layer == null || !_host.HasLayer(_layer) || ScreenManager.TopScreen != _host)
            {
                Close();
                return;
            }

            ApplyInputLock();
            if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
            if (Input.IsKeyReleased(InputKey.Escape) || _layer.Input.IsKeyReleased(InputKey.Escape)) Close();
            else if (_vm != null && IsSubmitReleased()) _vm.ExecuteSend();
        }

        private static bool IsSubmitReleased()
        {
            return Input.IsKeyReleased(InputKey.Enter)
                || Input.IsKeyReleased(InputKey.NumpadEnter)
                || (_layer?.Input != null
                    && (_layer.Input.IsKeyReleased(InputKey.Enter)
                        || _layer.Input.IsKeyReleased(InputKey.NumpadEnter)));
        }

        public static void Close()
        {
            IsOpen = false;
            _closeRequested = false;
            _pending = false;
            _pendingHero = null;
            _pendingCalibration = false;
            try
            {
                if (_layer != null)
                {
                    _layer.IsFocusLayer = false;
                    _layer.InputRestrictions.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer);
                    if (_host != null && _host.HasLayer(_layer)) _host.RemoveLayer(_layer);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Correspondence close failed: " + ex.Message);
            }

            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            if (_disableRegistered)
            {
                try { Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(DisableRequester); } catch { }
                _disableRegistered = false;
            }
        }

        private static void RequestClose()
        {
            _closeRequested = true;
        }

        private static void TryHotkey()
        {
            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.CorrespondenceEnabled) return;
            bool control = Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl);
            if (control && Input.IsKeyPressed(InputKey.M)) Open();
        }

        private static bool CanAttach(ScreenBase screen)
        {
            if (screen == null || TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null) return false;
            string name = (screen.GetType().FullName ?? string.Empty).ToLowerInvariant();
            string[] blocked = { "options", "encyclopedia", "conversation", "inventory", "party", "character", "kingdom", "clan", "quest", "barter", "trade", "save", "load", "menu", "mission", "initial" };
            foreach (string token in blocked) if (name.Contains(token)) return false;
            return true;
        }

        private static void ApplyInputLock()
        {
            if (_layer == null) return;
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _layer.Input.IsKeysAllowed = true;
            _layer.Input.IsMouseButtonAllowed = true;
            _layer.Input.IsMouseWheelAllowed = true;
        }
    }
}
