using System;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignTavernHouseScreenManager
    {
        private static readonly object StateLock = new object();
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static Settlement _pending;
        private static bool _stateLocked;
        public static bool IsCalibrationFixture { get; private set; }
        public static ReignTavernHouseScreenVM ActiveViewModel { get; private set; }
        public static bool IsOpen => _layer != null;

        public static void Open(Settlement town)
        {
            if (IsOpen || ReignPartyChatScreenManager.IsOpen || town?.IsTown != true || town != Settlement.CurrentSettlement
                || Hero.MainHero == null || Hero.MainHero.IsChild || Hero.MainHero.IsPrisoner) return;
            _pending = town;
        }

        public static void OpenForCalibration()
        {
            if (IsOpen || ReignPartyChatScreenManager.IsOpen) return;
            ScreenBase host = ScreenManager.TopScreen;
            if (host == null || host.GetType().Name.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) < 0) return;
            Attach(host, Settlement.CurrentSettlement, true);
        }

        public static void ApplicationTick(float dt)
        {
            try
            {
                if (_pending != null && !IsOpen)
                {
                    if (_pending != Settlement.CurrentSettlement) { _pending = null; return; }
                    ScreenBase candidate = ScreenManager.TopScreen;
                    string name = candidate?.GetType().Name ?? "";
                    if (candidate == null || name.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) < 0) return;
                    Settlement town = _pending; _pending = null;
                    Attach(candidate, town);
                }
                if (!IsOpen) return;
                if (_host == null || !_host.HasLayer(_layer) || ScreenManager.TopScreen != _host
                    || !IsCalibrationFixture && ActiveViewModel.Town != Settlement.CurrentSettlement)
                { Close(); return; }
                ApplyInputLock();
                ActiveViewModel.Tick(dt);
                if (_layer.Input.IsKeyReleased(InputKey.Escape))
                {
                    if (ActiveViewModel.IsPortraitPreviewVisible) ActiveViewModel.ExecuteClosePortraitPreview();
                    else ActiveViewModel.ExecuteClose();
                }
                else if (!ActiveViewModel.IsPortraitPreviewVisible && _layer.Input.IsKeyReleased(InputKey.Enter))
                    ActiveViewModel.ExecuteSend();
            }
            catch (Exception ex) { ReignLog.Exception("Tavern house interface", ex); Close(); }
        }

        private static void Attach(ScreenBase host, Settlement town, bool calibrationFixture = false)
        {
            try
            {
                _host = host;
                IsCalibrationFixture = calibrationFixture;
                ActiveViewModel = new ReignTavernHouseScreenVM(town, Close, calibrationFixture);
                _layer = new GauntletLayer("ReignTavernHouseScreen", 9999, true);
                _layer.LoadMovie("ReignTavernHouseScreen", ActiveViewModel);
                _host.AddLayer(_layer);
                _layer.ActiveCursor = CursorType.Default;
                _layer.IsFocusLayer = true;
                ScreenManager.TrySetFocus(_layer);
                Game.Current.GameStateManager.RegisterActiveStateDisableRequest(StateLock);
                _stateLocked = true;
                _host.MouseVisible = true;
                ApplyInputLock();
                ActiveViewModel.Begin();
            }
            catch { Close(); throw; }
        }

        private static void ApplyInputLock()
        {
            if (_layer == null) return;
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _layer.Input.IsKeysAllowed = true;
            _layer.Input.IsMouseButtonAllowed = true;
            _layer.Input.IsMouseWheelAllowed = true;
            if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
        }

        public static void Close()
        {
            _pending = null;
            var vm = ActiveViewModel;
            ActiveViewModel = null;
            try
            {
                vm?.OnFinalize();
                if (_layer != null)
                {
                    _layer.IsFocusLayer = false;
                    _layer.InputRestrictions.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer);
                    if (_host?.HasLayer(_layer) == true) _host.RemoveLayer(_layer);
                }
            }
            finally
            {
                _layer = null; _host = null; IsCalibrationFixture = false;
                if (_stateLocked)
                {
                    Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(StateLock);
                    _stateLocked = false;
                }
            }
        }
    }
}
