using System;
using ReignBeta.Integration;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    internal sealed class ReignPortraitRefreshPopup : ScreenBase
    {
        private GauntletLayer _layer;
        private readonly PortraitRefreshPopupVM _vm;
        private readonly Action<string> _decision;
        private bool _completed, _registered;
        internal static bool IsOpen { get; private set; }
        private static ReignPortraitRefreshPopup _calibration;

        internal ReignPortraitRefreshPopup(string name, bool canReflect, Action<string> decision)
        {
            _decision = decision;
            _vm = new PortraitRefreshPopupVM(name, canReflect, Choose);
        }
        internal static bool ShowCalibrationFixture()
        {
            if (IsOpen) return _calibration != null;
            _calibration = new ReignPortraitRefreshPopup("Rhagaea", true, _ => { });
            ScreenManager.PushScreen(_calibration);
            return IsOpen;
        }
        internal static void HideCalibrationFixture()
        {
            if (_calibration != null && ScreenManager.TopScreen == _calibration) ScreenManager.PopScreen();
        }
        protected override void OnInitialize()
        {
            base.OnInitialize();
            _layer = new GauntletLayer("ReignPortraitRefreshPopup", 10001, true);
            _layer.LoadMovie("ReignPortraitRefreshPopup", _vm);
            _layer.IsFocusLayer = true;
            _layer.ActiveCursor = CursorType.Default;
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            AddLayer(_layer); ScreenManager.TrySetFocus(_layer); MouseVisible = true;
            Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(this); _registered = true;
            IsOpen = true;
        }
        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_layer?.Input.IsKeyReleased(InputKey.Escape) == true) Choose("");
        }
        private void Choose(string mode)
        {
            if (_completed || ScreenManager.TopScreen != this) return;
            _completed = true;
            ScreenManager.PopScreen();
            if (mode.Length != 0) _decision?.Invoke(mode);
        }
        protected override void OnFinalize()
        {
            try
            {
                if (_registered) Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(this);
                if (_layer != null)
                {
                    _layer.IsFocusLayer = false; _layer.InputRestrictions.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer); if (HasLayer(_layer)) RemoveLayer(_layer);
                }
                _vm.OnFinalize();
            }
            finally { if (_calibration == this) _calibration = null; IsOpen = false; base.OnFinalize(); }
        }
    }

    internal sealed class PortraitRefreshPopupVM : ViewModel
    {
        private readonly Action<string> _choose;
        internal PortraitRefreshPopupVM(string name, bool canReflect, Action<string> choose)
        {
            Title = "LOOK AGAIN?";
            Detail = "You already have an image of " + name + ". Look Again replaces it using their current native appearance. Reflect on the Years changes only their apparent age.";
            CanReflect = canReflect; _choose = choose;
        }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public string LookAgainText => "Look Again";
        [DataSourceProperty] public string ReflectText => "Reflect on the Years";
        [DataSourceProperty] public string CancelText => "CANCEL";
        [DataSourceProperty] public bool CanReflect { get; }
        public void ExecuteLookAgain() => _choose("look_again");
        public void ExecuteReflect() { if (CanReflect) _choose("reflect_years"); }
        public void ExecuteCancel() => _choose("");
    }
}
