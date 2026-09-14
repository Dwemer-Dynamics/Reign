using System;
using System.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignFamilyChambersScreenManager
    {
        private const int LayerOrder = 9998;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignFamilyChambersScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        private static bool _returnToCourt;
        private static ReignCourtCampaignBehavior _pendingCourt;
        private static bool _pendingAllowOutsideRuleMode;
        private static Action _pendingOriginReturn;
        private static bool _allowOutsideRuleMode;
        private static Action _originReturn;
        private static Action _queuedOriginReturn;

        public static bool IsOpen { get; private set; }
        internal static int AutomationAdultCount => _vm?.Adults?.Count ?? 0;
        internal static int AutomationChildCount => _vm?.Children?.Count ?? 0;
        internal static int AutomationSelectedCount => _vm?.SelectedCount ?? 0;
        internal static bool AutomationCanEnter => _vm?.CanEnterScene == true;
        internal static string AutomationFirstAdultId => _vm?.Adults?.FirstOrDefault()?.Hero?.StringId ?? string.Empty;
        internal static string AutomationFirstChildId => _vm?.Children?.FirstOrDefault()?.Hero?.StringId ?? string.Empty;
        internal static string AutomationStatus => _vm?.StatusText ?? string.Empty;

        internal static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            if (_vm == null) { error = "Family Chambers is not open."; return false; }
            return _vm.TryExecuteAutomationAction(action, value, out error);
        }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court?.IsRuleModeActive != true)
            {
                ReignLog.Warn("Family Chambers open ignored because Rule Mode is inactive.");
                return;
            }
            if (ReignFamilyChambersCampaignBehavior.Instance == null)
            {
                ReignLog.Warn("Family Chambers open ignored because its campaign behavior is unavailable.");
                return;
            }
            // Court button commands run while Gauntlet is dispatching the old
            // movie's input event. Queue the transition so the new layer is
            // attached from ApplicationTick after Court teardown completes.
            _pendingCourt = court;
            _pendingAllowOutsideRuleMode = false;
            _pendingOriginReturn = null;
        }

        public static void OpenFromKeep(ReignCourtCampaignBehavior court, Action returnToKeep)
        {
            if (court == null || Settlement.CurrentSettlement?.IsTown != true)
            {
                ReignLog.Warn("Family Chambers keep entry ignored because no native town keep is active.");
                return;
            }
            if (ReignFamilyChambersCampaignBehavior.Instance == null) return;
            _pendingCourt = court;
            _pendingAllowOutsideRuleMode = true;
            _pendingOriginReturn = returnToKeep;
        }

        private static void OpenNow(ReignCourtCampaignBehavior court, bool allowOutsideRuleMode, Action originReturn)
        {
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_party_chat"))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Family Chambers graphics could not be loaded."));
                return;
            }
            _host = ScreenManager.TopScreen;
            if (_host == null)
            {
                ReignLog.Warn("Family Chambers could not open because the campaign map screen is unavailable.");
                return;
            }
            try
            {
                _court = court;
                _allowOutsideRuleMode = allowOutsideRuleMode;
                _originReturn = originReturn;
                _vm = new ReignFamilyChambersScreenVM(ReturnToOrigin, EnterConversation);
                _layer = new GauntletLayer("ReignFamilyChambersScreen", LayerOrder, false);
                _layer.LoadMovie("ReignFamilyChambersScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
                ReignLog.Info("Family Chambers opened on host=" + (_host.GetType().FullName ?? _host.GetType().Name) + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Family Chambers failed to open: " + ex);
                Close(false);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Family Chambers could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (_queuedOriginReturn != null && !IsOpen)
            {
                Action callback = _queuedOriginReturn;
                _queuedOriginReturn = null;
                callback();
                return;
            }
            if (_pendingCourt != null && !IsOpen)
            {
                ReignCourtCampaignBehavior court = _pendingCourt;
                bool allowOutside = _pendingAllowOutsideRuleMode;
                Action originReturn = _pendingOriginReturn;
                _pendingCourt = null;
                _pendingAllowOutsideRuleMode = false;
                _pendingOriginReturn = null;
                OpenNow(court, allowOutside, originReturn);
                // AddLayer is committed at frame end on some map screens. Do
                // not validate HasLayer during the same tick that attached it.
                return;
            }
            if (_returnToCourt && !IsOpen)
            {
                _returnToCourt = false;
                if (_court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(_court);
            }
            if (!IsOpen) return;
            bool originValid = _allowOutsideRuleMode ? Settlement.CurrentSettlement?.IsTown == true
                : _court?.ShouldKeepCourtScreenOpen == true;
            if (!originValid || _host == null || _layer == null
                || ScreenManager.TopScreen != _host || !SafeHasLayer(_host, _layer))
            {
                ReignLog.Warn("Family Chambers lost its host layer: host="
                    + (_host?.GetType().FullName ?? "<null>")
                    + " top=" + (ScreenManager.TopScreen?.GetType().FullName ?? "<null>")
                    + " layer=" + (_layer == null ? "<null>" : "present")
                    + " attached=" + (_host != null && _layer != null && SafeHasLayer(_host, _layer)) + ".");
                Close(false);
                return;
            }
            _vm?.OnFrameTick(dt);
            if (Input.IsKeyReleased(InputKey.Escape)) ReturnToOrigin();
        }

        private static void ReturnToOrigin()
        {
            if (_allowOutsideRuleMode) _queuedOriginReturn = _originReturn;
            else _returnToCourt = true;
            Close(false);
        }

        private static void EnterConversation(FamilyChambersSessionRecord session)
        {
            Close(false);
            ReignFamilyChambersPreparation.Begin(session);
        }

        public static void Close(bool returnToCourt)
        {
            if (returnToCourt) _returnToCourt = true;
            _pendingCourt = null;
            _pendingAllowOutsideRuleMode = false;
            _pendingOriginReturn = null;
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
            catch (Exception ex) { ReignLog.Warn("Family Chambers cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            _originReturn = null;
            _allowOutsideRuleMode = false;
            IsOpen = false;
        }

        private static bool SafeHasLayer(ScreenBase host, GauntletLayer layer)
        {
            try { return host?.HasLayer(layer) == true; }
            catch { return false; }
        }
    }
}
