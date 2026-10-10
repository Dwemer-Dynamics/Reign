using System;
using ReignBeta.Integration;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    internal sealed class ReignPlayerApproachPopup : ScreenBase
    {
        private GauntletLayer _layer;
        private readonly ApproachPopupVM _vm;
        private readonly Action<bool> _decision;
        private readonly Action _abandoned;
        private bool _completed, _registered;
        internal static bool IsOpen { get; private set; }
        internal static bool CanPresentOnMap()
        {
            MapScreen map = ScreenManager.TopScreen as MapScreen;
            return ReignPlayerApproachPresentationPolicy.CanPresent(map?.IsActive == true, map?.IsReady == true, LoadingWindow.IsLoadingWindowActive);
        }
        internal static bool TryPresentOnMap(Action present)
        {
            MapScreen map = ScreenManager.TopScreen as MapScreen;
            return ReignPlayerApproachPresentationPolicy.TryPresent(map?.IsActive == true, map?.IsReady == true, LoadingWindow.IsLoadingWindowActive, present);
        }
        private static ReignPlayerApproachPopup _calibrationScreen;
        internal static bool ShowCalibrationFixture()
        {
            if (IsOpen) return _calibrationScreen != null;
            _calibrationScreen = new ReignPlayerApproachPopup("A REQUEST TO SPEAK", "Rhagaea is asking to speak with you.", "Talk to them", "Tell them another time", _ => { });
            ScreenManager.PushScreen(_calibrationScreen); return _calibrationScreen.Initialized;
        }
        internal static void HideCalibrationFixture()
        {
            if (_calibrationScreen != null && ScreenManager.TopScreen == _calibrationScreen) ScreenManager.PopScreen();
        }
        internal bool Initialized { get; private set; }
        internal void CancelIfTop()
        {
            if (!_completed && ScreenManager.TopScreen == this) ScreenManager.PopScreen();
        }

        internal ReignPlayerApproachPopup(string title, string detail, string accept, string decline, Action<bool> decision, Action abandoned = null)
        {
            _decision = decision;
            _abandoned = abandoned;
            _vm = new ApproachPopupVM(title, detail, accept, decline, Choose);
        }
        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                _layer = new GauntletLayer("ReignPlayerApproachPopup", 10001, true);
                _layer.LoadMovie("ReignPlayerApproachPopup", _vm);
                _layer.IsFocusLayer = true; _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                AddLayer(_layer); ScreenManager.TrySetFocus(_layer); MouseVisible = true;
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(this); _registered = true;
                IsOpen = Initialized = true;
            }
            catch (Exception ex) { ReignLog.Warn("Approach popup failed: " + ex.Message); }
        }
        private void Choose(bool accept)
        {
            if (_completed) return;
            _completed = true;
            if (ScreenManager.TopScreen == this) ScreenManager.PopScreen();
            _decision(accept);
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
            finally { if (_calibrationScreen == this) _calibrationScreen = null; IsOpen = false; base.OnFinalize(); if (!_completed) _abandoned?.Invoke(); }
        }
    }

    internal sealed class ApproachPopupVM : ViewModel
    {
        private readonly Action<bool> _choose;
        internal ApproachPopupVM(string title, string detail, string accept, string decline, Action<bool> choose)
        { Title = title; Detail = detail; AcceptText = accept; DeclineText = decline; _choose = choose; }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public string AcceptText { get; }
        [DataSourceProperty] public string DeclineText { get; }
        public void ExecuteAccept() => _choose(true);
        public void ExecuteDecline() => _choose(false);
    }
}
