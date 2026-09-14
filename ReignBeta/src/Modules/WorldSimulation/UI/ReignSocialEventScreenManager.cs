using System;
using ReignBeta.Runtime;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignSocialEventScreenManager
    {
        private static ReignSocialEventScreen _activeScreen;

        public static bool IsOpen => _activeScreen != null;
        public static ReignSocialEventScreenVM ActiveViewModel => _activeScreen?.ActiveViewModel;

        // Kept for the shared application-tick call site. The event is now a real
        // ScreenBase and owns its own frame lifecycle, matching the proven legacy UI.
        public static void ApplicationTick(float dt)
        {
        }

        public static void Open(ReignSocialEventSession session, Action resolveEvent)
        {
            OpenInternal(session, resolveEvent, false);
        }

        public static void OpenForCalibration(ReignSocialEventSession session)
        {
            OpenInternal(session, null, true);
        }

        private static void OpenInternal(
            ReignSocialEventSession session,
            Action resolveEvent,
            bool calibrationMode)
        {
            if (session == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] No social event session found.", Color.FromUint(0xFFFFCC66)));
                return;
            }

            if (_activeScreen != null)
            {
                Integration.ReignLog.Warn("Ignored duplicate social-event screen open request.");
                return;
            }

            try
            {
                _activeScreen = new ReignSocialEventScreen(session, resolveEvent, calibrationMode);
                ScreenManager.PushScreen(_activeScreen);
            }
            catch (Exception ex)
            {
                _activeScreen = null;
                Integration.ReignLog.Warn("Social event screen failed to open: " + ex.Message);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Event screen could not open: " + ex.Message, Color.FromUint(0xFFFFCC66)));
            }
        }

        public static void Close()
        {
            _activeScreen?.RequestClose();
        }

        internal static void NotifyClosed(ReignSocialEventScreen screen)
        {
            if (ReferenceEquals(_activeScreen, screen))
            {
                _activeScreen = null;
            }
        }
    }

    internal sealed class ReignSocialEventScreen : ScreenBase
    {
        private const int EventLayerOrder = 250;
        private const float ExternalOverlayOpenGraceSeconds = 3f;
        private const float GauntletReferenceHeight = 1080f;
        private const float GauntletReferenceAspectRatio = 1920f / GauntletReferenceHeight;
        private const float NarrowAspectRatioTolerance = 0.98f;

        private readonly ReignSocialEventSession _session;
        private readonly Action _resolveEvent;
        private readonly bool _calibrationMode;
        private GauntletLayer _gauntletLayer;
        private ReignSocialEventScreenVM _dataSource;
        private bool _activeStateDisableRegistered;
        private bool _loggedFocusIssue;
        private bool _externalOverlayMode;
        private bool _externalOverlayObserved;
        private float _externalOverlayElapsedSeconds;
        private float _externalOverlayResumeDelay;
        private float _escapeSuppressionSeconds;
        private bool _closeRequested;

        internal ReignSocialEventScreenVM ActiveViewModel => _dataSource;

        public ReignSocialEventScreen(
            ReignSocialEventSession session,
            Action resolveEvent,
            bool calibrationMode)
        {
            _session = session;
            _resolveEvent = resolveEvent;
            _calibrationMode = calibrationMode;
        }

        protected override void OnInitialize()
        {
            base.OnInitialize();
            try
            {
                _dataSource = new ReignSocialEventScreenVM(
                    _session, RequestClose, _resolveEvent,
                    OpenEncyclopediaOverlay, OpenPortraitRequestOverlay,
                    _calibrationMode);
                _gauntletLayer = new GauntletLayer("ReignSocialEventScreen", EventLayerOrder, true);
                ApplyReferenceLockedScale();
                _gauntletLayer.LoadMovie("ReignSocialEventScreen", _dataSource);
                _gauntletLayer.IsFocusLayer = true;
                _gauntletLayer.ActiveCursor = CursorType.Default;
                ApplyInputLock();
                AddLayer(_gauntletLayer);
                ScreenManager.TrySetFocus(_gauntletLayer);
                RegisterActiveStateDisableRequest();
                MouseVisible = true;
                Integration.ReignLog.Info("Opened social event screen eventId=" + _session.Record.EventId + ".");
            }
            catch (Exception ex)
            {
                Integration.ReignLog.Warn("Social event screen initialization failed: " + ex.Message);
                _closeRequested = true;
            }
        }

        protected override void OnFrameTick(float dt)
        {
            base.OnFrameTick(dt);
            ApplyReferenceLockedScale();

            if (_closeRequested)
            {
                if (ScreenManager.TopScreen == this)
                {
                    ScreenManager.PopScreen();
                }
                return;
            }

            if (_externalOverlayMode)
            {
                _externalOverlayElapsedSeconds += dt;
                _externalOverlayResumeDelay -= dt;

                if (Integration.ReignPortraitBridge.IsEncyclopediaOpenQueued)
                {
                    return;
                }

                if (ScreenManager.TopScreen != this)
                {
                    _externalOverlayObserved = true;
                    return;
                }

                if (ScreenManager.FocusedLayer != null && ScreenManager.FocusedLayer != _gauntletLayer)
                {
                    _externalOverlayObserved = true;
                    return;
                }

                if (_externalOverlayResumeDelay > 0f
                    || (!_externalOverlayObserved && _externalOverlayElapsedSeconds < ExternalOverlayOpenGraceSeconds))
                {
                    return;
                }

                ExitExternalOverlayMode();
            }

            ApplyInputLock();
            if (ScreenManager.FocusedLayer != _gauntletLayer)
            {
                if (!_loggedFocusIssue)
                {
                    Integration.ReignLog.Warn("Social event focus was lost. Attempting to reclaim focus.");
                    _loggedFocusIssue = true;
                }

                ScreenManager.TrySetFocus(_gauntletLayer);
            }

            _dataSource?.OnFrameTick(dt);
            _escapeSuppressionSeconds = Math.Max(0f, _escapeSuppressionSeconds - dt);
            if (_escapeSuppressionSeconds <= 0f && _gauntletLayer?.Input != null && _gauntletLayer.Input.IsKeyReleased(InputKey.Escape))
            {
                if (_dataSource != null && _dataSource.IsPortraitPreviewVisible)
                {
                    _dataSource.ExecuteClosePortraitPreview();
                }
                else
                {
                    RequestClose();
                }
            }
            else if (_dataSource != null
                && !_dataSource.IsPortraitPreviewVisible
                && IsSubmitReleased())
            {
                _dataSource.ExecuteSend();
            }
        }

        private bool IsSubmitReleased()
        {
            return Input.IsKeyReleased(InputKey.Enter)
                || Input.IsKeyReleased(InputKey.NumpadEnter)
                || (_gauntletLayer?.Input != null
                    && (_gauntletLayer.Input.IsKeyReleased(InputKey.Enter)
                        || _gauntletLayer.Input.IsKeyReleased(InputKey.NumpadEnter)));
        }

        private void ApplyReferenceLockedScale()
        {
            if (_gauntletLayer?.UIContext == null)
            {
                return;
            }

            float width = Screen.RealScreenResolutionWidth;
            float height = Screen.RealScreenResolutionHeight;
            if (width <= 0f || height <= 0f)
            {
                return;
            }

            // Gauntlet normally enlarges a 1920x1080-authored prefab with the
            // physical display. This screen was approved at fixed reference-pixel
            // dimensions, so cancel only that native resolution multiplier while
            // preserving the player's global UI-scale setting.
            float nativeScale = height / GauntletReferenceHeight;
            float aspectRatio = width / height;
            float narrowAspectRatioThreshold = GauntletReferenceAspectRatio * NarrowAspectRatioTolerance;
            if (aspectRatio < narrowAspectRatioThreshold)
            {
                nativeScale *= aspectRatio / narrowAspectRatioThreshold;
            }

            float scaleModifier = _gauntletLayer.Scale / Math.Max(nativeScale, 0.01f);
            if (Math.Abs(_gauntletLayer.UIContext.ScaleModifier - scaleModifier) > 0.0001f)
            {
                _gauntletLayer.UIContext.ScaleModifier = scaleModifier;
            }

            float logicalHeight = height / Math.Max(_gauntletLayer.Scale, 0.01f);
            _dataSource?.UpdateViewportLayout(logicalHeight);
        }

        protected override void OnFinalize()
        {
            UnregisterActiveStateDisableRequest();
            Integration.ReignPortraitBridge.CancelQueuedEncyclopediaOpen();

            if (_gauntletLayer != null)
            {
                try
                {
                    _gauntletLayer.IsFocusLayer = false;
                    _gauntletLayer.InputRestrictions?.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_gauntletLayer);
                    if (HasLayer(_gauntletLayer))
                    {
                        RemoveLayer(_gauntletLayer);
                    }
                }
                catch (Exception ex)
                {
                    Integration.ReignLog.Warn("Social event focus cleanup failed: " + ex.Message);
                }
            }

            _dataSource?.OnFinalize();
            _dataSource = null;
            _gauntletLayer = null;
            ReignSocialEventScreenManager.NotifyClosed(this);
            Integration.ReignLog.Info("Finalized social event screen.");
            base.OnFinalize();
        }

        internal void RequestClose()
        {
            _closeRequested = true;
        }

        private void OpenEncyclopediaOverlay(TaleWorlds.CampaignSystem.Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            EnterExternalOverlayMode();
            Integration.ReignPortraitBridge.QueueOpenHeroEncyclopedia(hero);
        }

        private void OpenPortraitRequestOverlay(TaleWorlds.CampaignSystem.Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            Integration.ReignPortraitBridge.RequestPortrait(hero);
        }

        private void EnterExternalOverlayMode()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _externalOverlayMode = true;
            _externalOverlayObserved = false;
            _externalOverlayElapsedSeconds = 0f;
            _externalOverlayResumeDelay = 0.75f;
            _loggedFocusIssue = false;
            UnregisterActiveStateDisableRequest();
            ReleaseInputLock();
            Integration.ReignLog.Info("Social event released focus for an encyclopedia overlay while remaining visible beneath it.");
        }

        private void ExitExternalOverlayMode()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _externalOverlayMode = false;
            _externalOverlayObserved = false;
            _externalOverlayElapsedSeconds = 0f;
            _externalOverlayResumeDelay = 0f;
            _escapeSuppressionSeconds = 0.35f;
            _loggedFocusIssue = false;
            _gauntletLayer.IsFocusLayer = true;
            _gauntletLayer.ActiveCursor = CursorType.Default;
            ApplyInputLock();
            ScreenManager.TrySetFocus(_gauntletLayer);
            RegisterActiveStateDisableRequest();
            MouseVisible = true;
            Integration.ReignLog.Info("Social event restored focus after the encyclopedia overlay closed.");
        }

        private void ReleaseInputLock()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _gauntletLayer.IsFocusLayer = false;
            _gauntletLayer.InputRestrictions?.ResetInputRestrictions();
            ScreenManager.TryLoseFocus(_gauntletLayer);
        }

        private void ApplyInputLock()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _gauntletLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
            _gauntletLayer.Input.IsKeysAllowed = true;
            _gauntletLayer.Input.IsMouseButtonAllowed = true;
            _gauntletLayer.Input.IsMouseWheelAllowed = true;
        }

        private void RegisterActiveStateDisableRequest()
        {
            if (_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(this);
                _activeStateDisableRegistered = true;
            }
            catch (Exception ex)
            {
                Integration.ReignLog.Warn("Social event active-state lock failed: " + ex.Message);
            }
        }

        private void UnregisterActiveStateDisableRequest()
        {
            if (!_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(this);
            }
            catch (Exception ex)
            {
                Integration.ReignLog.Warn("Social event active-state unlock failed: " + ex.Message);
            }
            finally
            {
                _activeStateDisableRegistered = false;
            }
        }
    }
}
