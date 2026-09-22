using System;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignDiplomacyAnnouncementScreenManager
    {
        private const int LayerOrder = 10002;
        private static readonly object ActiveStateDisableRequester = new object();
        private static ScreenBase _hostScreen;
        private static GauntletLayer _layer;
        private static ReignDiplomacyAnnouncementVM _vm;
        private static CampaignTimeControlMode _priorTimeMode;
        private static bool _restoreTime;
        private static bool _activeStateDisableRegistered;
        private static Action _onAcknowledged;
        private static Action _onClosedWithoutAcknowledgement;
        private static string _activeEventId = string.Empty;

        public static bool IsOpen { get; private set; }

        public static void Open(ReignDiplomacyAnnouncement announcement, Action onAcknowledged, Action onClosedWithoutAcknowledgement)
        {
            if (announcement == null || !announcement.IsValid || IsOpen || ScreenManager.TopScreen == null)
            {
                return;
            }

            try
            {
                _onAcknowledged = onAcknowledged;
                _onClosedWithoutAcknowledgement = onClosedWithoutAcknowledgement;
                _activeEventId = announcement.EventId ?? string.Empty;
                _hostScreen = ScreenManager.TopScreen;
                _vm = new ReignDiplomacyAnnouncementVM(announcement, Acknowledge);
                _layer = new GauntletLayer("ReignDiplomacyAnnouncementScreen", LayerOrder, true);
                _layer.LoadMovie("ReignDiplomacyAnnouncementScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                _layer.Input.IsMouseButtonAllowed = true;
                _layer.Input.IsMouseWheelAllowed = true;
                _hostScreen.AddLayer(_layer);
                _hostScreen.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                PauseCampaign();
                RegisterActiveStateDisableRequest();
                IsOpen = true;
                ReignLog.Info("Opened diplomacy announcement " + announcement.EventId + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Diplomacy announcement screen failed: " + ex.Message);
                Close(false);
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen)
            {
                return;
            }

            if (_hostScreen == null || _layer == null || ScreenManager.TopScreen != _hostScreen)
            {
                Close(false);
                return;
            }

            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                if (ScreenManager.FocusedLayer != _layer)
                {
                    ScreenManager.TrySetFocus(_layer);
                }

                if (Input.IsKeyReleased(InputKey.Enter)
                    || Input.IsKeyReleased(InputKey.NumpadEnter)
                    || _layer.Input.IsKeyReleased(InputKey.Enter)
                    || _layer.Input.IsKeyReleased(InputKey.NumpadEnter)
                    || Input.IsKeyPressed(InputKey.Escape)
                    || Input.IsKeyReleased(InputKey.Escape)
                    || _layer.Input.IsKeyPressed(InputKey.Escape)
                    || _layer.Input.IsKeyReleased(InputKey.Escape))
                {
                    Acknowledge();
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Diplomacy announcement focus failed: " + ex.Message);
                Close(false);
            }
        }

        private static void Acknowledge()
        {
            ReignLog.Info("Diplomacy announcement acknowledged.");
            Close(true);
        }

        public static bool TryAcknowledgeForLiveHarness(bool restoreInterruptedTime, out string eventId)
        {
            eventId = _activeEventId ?? string.Empty;
            if (!IsOpen || _layer == null || _vm == null)
            {
                return false;
            }

            if (!restoreInterruptedTime)
                _priorTimeMode = CampaignTimeControlMode.Stop;
            Acknowledge();
            return true;
        }

        public static bool TryCloseCalibrationFixture()
        {
            if (!IsOpen
                || !string.Equals(_activeEventId, "ui_calibration_diplomacy", StringComparison.Ordinal))
            {
                return false;
            }

            Close(false);
            return true;
        }

        private static void Close(bool acknowledged)
        {
            Action callback = acknowledged ? _onAcknowledged : null;
            Action dismissed = acknowledged ? null : _onClosedWithoutAcknowledgement;
            if (_layer != null)
            {
                try
                {
                    _layer.IsFocusLayer = false;
                    _layer.InputRestrictions?.ResetInputRestrictions();
                    ScreenManager.TryLoseFocus(_layer);
                    if (_hostScreen?.HasLayer(_layer) == true)
                    {
                        _hostScreen.RemoveLayer(_layer);
                    }
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Diplomacy announcement cleanup failed: " + ex.Message);
                }
            }

            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _hostScreen = null;
            _onAcknowledged = null;
            _onClosedWithoutAcknowledgement = null;
            _activeEventId = string.Empty;
            UnregisterActiveStateDisableRequest();
            RestoreCampaignTime();
            IsOpen = false;
            callback?.Invoke();
            dismissed?.Invoke();
        }

        private static void PauseCampaign()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlModeLock)
            {
                return;
            }

            _priorTimeMode = campaign.TimeControlMode;
            _restoreTime = true;
            campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            campaign.SetTimeControlModeLock(true);
        }

        private static void RestoreCampaignTime()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (!_restoreTime || campaign == null)
            {
                _restoreTime = false;
                return;
            }

            campaign.SetTimeControlModeLock(false);
            campaign.TimeControlMode = _priorTimeMode;
            _restoreTime = false;
        }

        private static void RegisterActiveStateDisableRequest()
        {
            if (_activeStateDisableRegistered)
            {
                return;
            }

            Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(ActiveStateDisableRequester);
            _activeStateDisableRegistered = true;
        }

        private static void UnregisterActiveStateDisableRequest()
        {
            if (!_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.UnregisterActiveStateDisableRequest(ActiveStateDisableRequester);
            }
            finally
            {
                _activeStateDisableRegistered = false;
            }
        }
    }
}
