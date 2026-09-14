using System;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.UI.ViewModels;
using ReignBeta.UI.Widgets;
using ReignBeta.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignPartyChatScreenManager
    {
        private const int ModalInputLayerOrder = 9999;
        private static readonly object ActiveStateDisableRequester = new object();

        private static ScreenBase _hostScreen;
        private static GauntletLayer _gauntletLayer;
        private static ReignPartyChatScreenVM _dataSource;
        private static bool _pendingOpen;
        private static bool _loggedFocusIssue;
        private static bool _externalOverlayMode;
        private static bool _activeStateDisableRegistered;
        private static bool _ignoreNextHotkeyRelease;
        private static float _externalOverlayResumeDelay;
        private static CastleRoomSessionRecord _pendingCastleSession;
        private static Hero _pendingTargetedHero;
        private static string _pendingTargetedContext = string.Empty;
        private static Action<bool> _pendingTargetedClose;
        private static Settlement _pendingHomesSettlement;
        private static Hero _pendingHomeResident;

        public static bool IsOpen { get; private set; }
        public static ReignPartyChatScreenVM ActiveViewModel => _dataSource;

        public static void ApplicationTick(float dt)
        {
            if (ReignTavernHouseScreenManager.IsOpen) return;
            try
            {
                Tick(dt);
                TryOpenFromHotkey();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat tick failed: " + ex.Message);
            }
        }

        public static void Open()
        {
            if (IsOpen || _pendingOpen || ReignTavernHouseScreenManager.IsOpen)
            {
                return;
            }

            ScreenBase host = ScreenManager.TopScreen;
            if (!CanAttachToHost(host))
            {
                _pendingOpen = true;
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Party Chat will open when you return to the campaign map.", Color.FromUint(0xFFFFD36A)));
                return;
            }

            OpenOnHost(host);
        }

        public static void OpenCastle(CastleRoomSessionRecord session)
        {
            if (session == null || IsOpen || _pendingOpen) return;
            _pendingCastleSession = session;
            ScreenBase host = ScreenManager.TopScreen;
            if (!CanAttachToHost(host)) { _pendingOpen = true; return; }
            OpenOnHost(host);
        }

        public static void OpenHomes(Settlement settlement, Hero resident = null)
        {
            if (settlement == null || settlement != Settlement.CurrentSettlement || IsOpen || _pendingOpen) return;
            _pendingHomesSettlement = settlement;
            _pendingHomeResident = resident;
            ScreenBase host = ScreenManager.TopScreen;
            if (!CanAttachToHost(host)) { _pendingOpen = true; return; }
            OpenOnHost(host);
        }

        public static void OpenTargetedReview(Hero hero, string context, Action<bool> onClosed)
        {
            if (hero == null || IsOpen || _pendingOpen)
            {
                onClosed?.Invoke(true);
                return;
            }
            _pendingTargetedHero = hero;
            _pendingTargetedContext = context ?? string.Empty;
            _pendingTargetedClose = onClosed;
            ScreenBase host = ScreenManager.TopScreen;
            if (!CanAttachToHost(host))
            {
                _pendingOpen = true;
                return;
            }
            OpenOnHost(host);
        }

        public static void Tick(float dt)
        {
            if (_pendingOpen && !IsOpen && CanAttachToHost(ScreenManager.TopScreen))
            {
                _pendingOpen = false;
                OpenOnHost(ScreenManager.TopScreen);
            }

            if (!IsOpen)
            {
                return;
            }

            if (_hostScreen == null || _gauntletLayer == null || !_hostScreen.HasLayer(_gauntletLayer))
            {
                Close();
                return;
            }

            if (_externalOverlayMode)
            {
                _externalOverlayResumeDelay -= dt;
                if (_externalOverlayResumeDelay > 0f || ScreenManager.TopScreen != _hostScreen)
                {
                    return;
                }

                ExitExternalOverlayMode();
            }
            else if (ScreenManager.TopScreen != _hostScreen)
            {
                Close();
                return;
            }

            ApplyInputLock();
            _dataSource?.RefreshCastleArt();
            if (ScreenManager.FocusedLayer != _gauntletLayer)
            {
                if (!_loggedFocusIssue)
                {
                    ReignLog.Warn("Party chat focus was lost; reclaiming.");
                    _loggedFocusIssue = true;
                }

                ScreenManager.TrySetFocus(_gauntletLayer);
            }

            if (IsEscapePressedOrReleased())
            {
                if (_dataSource != null && _dataSource.IsPortraitPreviewVisible)
                {
                    _dataSource.ExecuteClosePortraitPreview();
                }
                else
                {
                    Close();
                }
            }
            else if (_dataSource != null
                && !_dataSource.IsPortraitPreviewVisible
                && IsSubmitReleased())
            {
                _dataSource.ExecuteSend();
            }
        }

        private static bool IsSubmitReleased()
        {
            return Input.IsKeyReleased(InputKey.Enter)
                || Input.IsKeyReleased(InputKey.NumpadEnter)
                || (_gauntletLayer?.Input != null
                    && (_gauntletLayer.Input.IsKeyReleased(InputKey.Enter)
                        || _gauntletLayer.Input.IsKeyReleased(InputKey.NumpadEnter)));
        }

        public static void Close()
        {
            Action<bool> targetedClose = _pendingTargetedClose;
            bool targetedProviderFailed = _dataSource?.TargetedReviewProviderFailed ?? true;
            _pendingTargetedClose = null;
            _pendingTargetedHero = null;
            _pendingTargetedContext = string.Empty;
            _pendingHomesSettlement = null;
            _pendingHomeResident = null;
            if (_gauntletLayer != null)
            {
                _gauntletLayer.IsFocusLayer = false;
                _gauntletLayer.InputRestrictions.ResetInputRestrictions();
                ScreenManager.TryLoseFocus(_gauntletLayer);
                try
                {
                    if (_hostScreen != null && _hostScreen.HasLayer(_gauntletLayer))
                    {
                        _hostScreen.RemoveLayer(_gauntletLayer);
                    }
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Party chat layer removal failed: " + ex.Message);
                }
            }

            _dataSource?.OnFinalize();
            _dataSource = null;
            _gauntletLayer = null;
            _hostScreen = null;
            _pendingOpen = false;
            _pendingCastleSession = null;
            _externalOverlayMode = false;
            _loggedFocusIssue = false;
            UnregisterActiveStateDisableRequest();
            IsOpen = false;
            ReignLog.Info("Party chat closed.");
            targetedClose?.Invoke(targetedProviderFailed);
        }

        private static void OpenOnHost(ScreenBase host)
        {
            if (host == null)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Party Chat could not find a screen to attach to.", Color.FromUint(0xFFFFAA00)));
                return;
            }

            try
            {
                _hostScreen = host;
                bool castle = _pendingCastleSession != null;
                bool targeted = !castle && _pendingTargetedHero != null;
                _dataSource = castle
                    ? new ReignPartyChatScreenVM(Close, _pendingCastleSession, OpenEncyclopediaOverlay, OpenPortraitRequestOverlay)
                    : targeted
                        ? new ReignPartyChatScreenVM(Close, _pendingTargetedHero, _pendingTargetedContext,
                            OpenEncyclopediaOverlay, OpenPortraitRequestOverlay)
                        : _pendingHomesSettlement != null
                            ? new ReignPartyChatScreenVM(Close, _pendingHomesSettlement, _pendingHomeResident,
                                OpenEncyclopediaOverlay, OpenPortraitRequestOverlay)
                            : new ReignPartyChatScreenVM(Close, OpenEncyclopediaOverlay, OpenPortraitRequestOverlay);
                _gauntletLayer = new GauntletLayer(castle ? "ReignCastleChatScreen" : "ReignPartyChatScreen", ModalInputLayerOrder, true);
                _gauntletLayer.LoadMovie(castle ? "ReignCastleChatScreen" : "ReignPartyChatScreen", _dataSource);
                _pendingCastleSession = null;
                _hostScreen.AddLayer(_gauntletLayer);
                _gauntletLayer.IsFocusLayer = true;
                _gauntletLayer.ActiveCursor = CursorType.Default;
                ScreenManager.TrySetFocus(_gauntletLayer);
                ApplyInputLock();
                RegisterActiveStateDisableRequest();
                _hostScreen.MouseVisible = true;
                _externalOverlayMode = false;
                IsOpen = true;
                ReignLog.Info("Party chat opened on host=" + _hostScreen.GetType().FullName);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat open failed: " + ex);
                Close();
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Party Chat could not open: " + ex.Message, Color.FromUint(0xFFFFAA00)));
            }
        }

        private static bool CanAttachToHost(ScreenBase screen)
        {
            if (screen == null || TaleWorlds.CampaignSystem.Campaign.Current == null || Hero.MainHero == null || MobileParty.MainParty == null)
            {
                return false;
            }

            string lower = (screen.GetType().FullName ?? string.Empty).ToLowerInvariant();
            string[] blocked =
            {
                "options", "encyclopedia", "conversation", "inventory", "partyscreen", "characterdeveloper",
                "kingdom", "clan", "quest", "barter", "trade", "save", "load", "menu", "loading",
                "mission", "initial"
            };

            foreach (string token in blocked)
            {
                if (lower.Contains(token))
                {
                    return false;
                }
            }

            return true;
        }

        private static void TryOpenFromHotkey()
        {
            if (ReignBetaSettings.Instance != null && !ReignBetaSettings.Instance.PartyChatEnabled)
            {
                return;
            }

            bool pressed = Input.IsKeyPressed(InputKey.BackSlash);
            bool released = Input.IsKeyReleased(InputKey.BackSlash);
            if (released && _ignoreNextHotkeyRelease)
            {
                _ignoreNextHotkeyRelease = false;
                return;
            }

            if (!pressed && !released)
            {
                return;
            }

            _ignoreNextHotkeyRelease = pressed;
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private static bool IsEscapePressedOrReleased()
        {
            return Input.IsKeyPressed(InputKey.Escape)
                || Input.IsKeyReleased(InputKey.Escape)
                || (_gauntletLayer != null
                    && (_gauntletLayer.Input.IsKeyPressed(InputKey.Escape)
                        || _gauntletLayer.Input.IsKeyReleased(InputKey.Escape)));
        }

        private static void OpenEncyclopediaOverlay(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            EnterExternalOverlayMode();
            ReignPortraitBridge.OpenHeroEncyclopedia(hero);
        }

        private static void OpenPortraitRequestOverlay(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            ReignPortraitBridge.RequestPortrait(hero);
        }

        private static void EnterExternalOverlayMode()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _externalOverlayMode = true;
            _externalOverlayResumeDelay = 0.75f;
            _loggedFocusIssue = false;
            UnregisterActiveStateDisableRequest();
            _dataSource?.SetOverlayVisible(false);
            ReleaseInputLock();
            ReignLog.Info("Party chat entered external overlay mode.");
        }

        private static void ExitExternalOverlayMode()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _externalOverlayMode = false;
            _loggedFocusIssue = false;
            _dataSource?.SetOverlayVisible(true);
            _gauntletLayer.IsFocusLayer = true;
            _gauntletLayer.ActiveCursor = CursorType.Default;
            ApplyInputLock();
            ScreenManager.TrySetFocus(_gauntletLayer);
            RegisterActiveStateDisableRequest();
            if (_hostScreen != null)
            {
                _hostScreen.MouseVisible = true;
            }
        }

        private static void ReleaseInputLock()
        {
            if (_gauntletLayer == null)
            {
                return;
            }

            _gauntletLayer.IsFocusLayer = false;
            _gauntletLayer.InputRestrictions.ResetInputRestrictions();
            ScreenManager.TryLoseFocus(_gauntletLayer);
        }

        private static void ApplyInputLock()
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

        private static void RegisterActiveStateDisableRequest()
        {
            if (_activeStateDisableRegistered)
            {
                return;
            }

            try
            {
                Game.Current?.GameStateManager?.RegisterActiveStateDisableRequest(ActiveStateDisableRequester);
                _activeStateDisableRegistered = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat active-state lock failed: " + ex.Message);
            }
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
            catch (Exception ex)
            {
                ReignLog.Warn("Party chat active-state unlock failed: " + ex.Message);
            }
            finally
            {
                _activeStateDisableRegistered = false;
            }
        }
    }
}
