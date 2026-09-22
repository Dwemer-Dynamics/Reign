using System;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignNotableGenerationPopupManager
    {
        private static ReignNotableGenerationPopupScreen _activeScreen;
        private static bool _keepOpenRequested;
        private static bool _calibrationFixtureOpen;
        private static string _title = "Preparing Bannerlord Reign";
        private static string _detail = "Preparing the campaign for safe play and saving.";
        private static string _progress = string.Empty;
        private static bool _hasError;
        private static Action _retry;

        public static bool IsOpen => _activeScreen != null;

        public static void RequireUntilReady()
        {
            _keepOpenRequested = true;
        }

        public static bool Show()
        {
            if (IsOpen)
            {
                _keepOpenRequested = true;
                return true;
            }
            if (ScreenManager.TopScreen == null) return false;

            try
            {
                _activeScreen = new ReignNotableGenerationPopupScreen();
                ScreenManager.PushScreen(_activeScreen);
                if (!_activeScreen.InitializedSuccessfully)
                {
                    _activeScreen.RequestClose();
                    _activeScreen = null;
                    return false;
                }
                _activeScreen.UpdateStatus(_title, _detail, _progress, _hasError, _retry);
                _keepOpenRequested = true;
                return true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Notable generation popup failed to open: " + ex.Message);
                _activeScreen = null;
                return false;
            }
        }

        public static void UpdateStatus(
            string title,
            string detail,
            string progress,
            bool hasError,
            Action retry)
        {
            _title = title ?? string.Empty;
            _detail = detail ?? string.Empty;
            _progress = progress ?? string.Empty;
            _hasError = hasError;
            _retry = retry;
            _activeScreen?.UpdateStatus(
                _title, _detail, _progress, _hasError, _retry);
        }

        public static void ApplicationTick(float dt)
        {
            if (!_keepOpenRequested
                || IsOpen
                || TaleWorlds.CampaignSystem.Campaign.Current == null)
                return;

            string screenName = ScreenManager.TopScreen?.GetType().FullName ?? string.Empty;
            if (screenName.IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) < 0) return;

            // Bannerlord can externally finalize a pushed screen while completing
            // its own campaign-screen transition. The initialization gate remains
            // authoritative, so restore the blocking popup until Hide explicitly
            // releases it after successful generation (or the retry inquiry).
            ReignLog.Warn("Notable generation popup was closed while preparation remained active; restoring it.");
            Show();
        }

        public static void Hide()
        {
            _keepOpenRequested = false;
            _calibrationFixtureOpen = false;
            _activeScreen?.RequestClose();
        }

        public static bool ShowCalibrationFixture()
        {
            if (IsOpen && !_calibrationFixtureOpen) return false;

            _calibrationFixtureOpen = true;
            UpdateStatus(
                "PREPARING CALRADIA",
                "Generating the notable characters and relationships needed for this campaign.",
                "Building notable 18 of 42",
                false,
                null);
            if (Show()) return true;

            _calibrationFixtureOpen = false;
            return false;
        }

        public static bool HideCalibrationFixture()
        {
            if (!IsOpen || !_calibrationFixtureOpen)
            {
                return false;
            }

            Hide();
            return true;
        }

        internal static void NotifyClosed(ReignNotableGenerationPopupScreen screen)
        {
            if (ReferenceEquals(_activeScreen, screen)) _activeScreen = null;
        }
    }

    internal sealed class ReignNotableGenerationPopupScreen : ScreenBase
    {
        private const int LayerOrder = 10001;
        private GauntletLayer _layer;
        private NotableGenerationPopupViewModel _viewModel;
        private bool _activeStateDisableRegistered;
        private bool _closeRequested;

        internal bool InitializedSuccessfully { get; private set; }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                _viewModel = new NotableGenerationPopupViewModel();
                _layer = new GauntletLayer("ReignNotableGenerationPopup", LayerOrder, true);
                _layer.LoadMovie("ReignNotableGenerationPopup", _viewModel);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                ApplyInputLock();
                AddLayer(_layer);
                ScreenManager.TrySetFocus(_layer);
                RegisterActiveStateDisableRequest();
                MouseVisible = true;
                InitializedSuccessfully = true;
                ReignLog.Info("Opened notable generation popup.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Notable generation popup initialization failed: " + ex.Message);
                _closeRequested = true;
            }
        }

        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            if (_closeRequested)
            {
                if (ScreenManager.TopScreen == this) ScreenManager.PopScreen();
                return;
            }

            ApplyInputLock();
            if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
        }

        protected override void OnFinalize()
        {
            UnregisterActiveStateDisableRequest();
            if (_layer != null)
            {
                try
                {
                    _layer.IsFocusLayer = false;
                    _layer.InputRestrictions?.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer);
                    if (HasLayer(_layer)) RemoveLayer(_layer);
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Notable generation popup cleanup failed: " + ex.Message);
                }
            }

            _viewModel?.OnFinalize();
            _viewModel = null;
            _layer = null;
            ReignNotableGenerationPopupManager.NotifyClosed(this);
            ReignLog.Info("Finalized notable generation popup.");
            base.OnFinalize();
        }

        internal void RequestClose()
        {
            _closeRequested = true;
            if (ScreenManager.TopScreen == this) ScreenManager.PopScreen();
        }

        internal void UpdateStatus(
            string title,
            string detail,
            string progress,
            bool hasError,
            Action retry)
        {
            _viewModel?.Update(title, detail, progress, hasError, retry);
        }

        private void ApplyInputLock()
        {
            if (_layer == null) return;
            _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            bool allowRetry = _viewModel?.HasError == true;
            _layer.Input.IsKeysAllowed = allowRetry;
            _layer.Input.IsMouseButtonAllowed = allowRetry;
            _layer.Input.IsMouseWheelAllowed = false;
        }

        private void RegisterActiveStateDisableRequest()
        {
            if (_activeStateDisableRegistered) return;
            try
            {
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(this);
                _activeStateDisableRegistered = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Notable generation popup active-state lock failed: " + ex.Message);
            }
        }

        private void UnregisterActiveStateDisableRequest()
        {
            if (!_activeStateDisableRegistered) return;
            try
            {
                Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(this);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Notable generation popup active-state unlock failed: " + ex.Message);
            }
            finally
            {
                _activeStateDisableRegistered = false;
            }
        }

        private sealed class NotableGenerationPopupViewModel : ViewModel
        {
            private string _title = "Preparing Bannerlord Reign";
            private string _detail = "Preparing the campaign for safe play and saving.";
            private string _progress = string.Empty;
            private bool _hasError;
            private Action _retry;

            [DataSourceProperty]
            public string Title
            {
                get => _title;
                private set
                {
                    if (value == _title) return;
                    _title = value;
                    OnPropertyChangedWithValue(value, nameof(Title));
                }
            }

            [DataSourceProperty]
            public string Detail
            {
                get => _detail;
                private set
                {
                    if (value == _detail) return;
                    _detail = value;
                    OnPropertyChangedWithValue(value, nameof(Detail));
                }
            }

            [DataSourceProperty]
            public string Progress
            {
                get => _progress;
                private set
                {
                    if (value == _progress) return;
                    _progress = value;
                    OnPropertyChangedWithValue(value, nameof(Progress));
                }
            }

            [DataSourceProperty]
            public bool HasError
            {
                get => _hasError;
                private set
                {
                    if (value == _hasError) return;
                    _hasError = value;
                    OnPropertyChangedWithValue(value, nameof(HasError));
                }
            }

            public void ExecuteRetry()
            {
                if (!HasError) return;
                _retry?.Invoke();
            }

            internal void Update(
                string title,
                string detail,
                string progress,
                bool hasError,
                Action retry)
            {
                Title = title ?? string.Empty;
                Detail = detail ?? string.Empty;
                Progress = progress ?? string.Empty;
                HasError = hasError;
                _retry = retry;
            }
        }
    }
}
