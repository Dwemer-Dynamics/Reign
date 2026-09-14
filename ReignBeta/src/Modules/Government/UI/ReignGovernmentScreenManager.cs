using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Government;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignGovernmentScreenManager
    {
        private const int LayerOrder = 9998;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignGovernmentScreenVM _vm;
        private static Kingdom _kingdom;
        private static bool _interactive;
        private static Action _returnAction;
        private static OpenRequest _pendingOpen;
        private static Action _pendingReturn;
        private static readonly Dictionary<string, string> PresentedStages = new Dictionary<string, string>(StringComparer.Ordinal);
        private static ReignGovernmentCampaignBehavior _presentationBehavior;
        private static float _scanDelay;
        private static bool _ownsTimeLock;
        private static long _inputTicks, _globalMousePresses, _globalMouseReleases;
        private static long _layerMousePresses, _layerMouseReleases, _globalEscapes, _layerEscapes;

        public static bool IsOpen { get; private set; }
        internal static string AutomationKingdomId => _kingdom?.StringId ?? string.Empty;
        internal static int AutomationPartyCount => _vm?.Parties?.Count ?? 0;
        internal static int AutomationResolutionCount => _vm?.Resolutions?.Count ?? 0;
        internal static bool AutomationInteractive => _vm?.IsInteractive == true;
        internal static bool TryComposeHearing(string businessId, string message)
            => IsOpen && _interactive && _vm != null && _vm.TryComposeHearing(businessId, message);
        // Observation only: counters belong to this opening, never to campaign saves.
        internal static object AutomationInputSnapshot => new
        {
            schema = "reign-government-input-observation-v1",
            open = IsOpen,
            activeBusinessId = _vm?.ActiveBusinessId ?? string.Empty,
            focusedLayerType = ScreenManager.FocusedLayer?.GetType().FullName ?? string.Empty,
            focusedLayerName = ScreenManager.FocusedLayer?.GetType().GetProperty("Name")?.GetValue(ScreenManager.FocusedLayer, null)?.ToString() ?? string.Empty,
            ownsFocus = _layer != null && ScreenManager.FocusedLayer == _layer,
            hostIsTopScreen = _host != null && ScreenManager.TopScreen == _host,
            layerAttached = _host != null && _layer != null && SafeHasLayer(_host, _layer),
            inquiryActive = InformationManager.IsAnyInquiryActive(),
            keysAllowed = _layer?.Input.IsKeysAllowed == true,
            mouseButtonsAllowed = _layer?.Input.IsMouseButtonAllowed == true,
            mouseWheelAllowed = _layer?.Input.IsMouseWheelAllowed == true,
            inputTicks = _inputTicks,
            globalMousePresses = _globalMousePresses,
            globalMouseReleases = _globalMouseReleases,
            layerMousePresses = _layerMousePresses,
            layerMouseReleases = _layerMouseReleases,
            globalEscapes = _globalEscapes,
            layerEscapes = _layerEscapes
        };

        public static void Open(Kingdom kingdom, bool interactive, Action returnAction)
        {
            if (kingdom == null || kingdom.IsEliminated || ReignGovernmentCampaignBehavior.Instance == null) return;
            _pendingOpen = new OpenRequest { Kingdom = kingdom, Interactive = interactive, ReturnAction = returnAction };
        }

        public static void OpenBusiness(Kingdom kingdom, string businessId, bool interactive = true, Action returnAction = null)
        {
            if (kingdom == null || kingdom.IsEliminated || ReignGovernmentCampaignBehavior.Instance == null) return;
            _pendingOpen = new OpenRequest { Kingdom = kingdom, Interactive = interactive, ReturnAction = returnAction, BusinessId = businessId };
        }

        private static void OpenNow(OpenRequest request)
        {
            if (request?.Kingdom == null || request.Kingdom.IsEliminated) return;
            if (IsOpen) Close(false);
            ScreenBase host = ScreenManager.TopScreen;
            if (host == null)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Government requires the campaign map screen."));
                return;
            }
            try
            {
                _kingdom = request.Kingdom;
                _interactive = request.Interactive && request.Kingdom.Leader == Hero.MainHero;
                _returnAction = request.ReturnAction;
                _host = host;
                _inputTicks = _globalMousePresses = _globalMouseReleases = 0;
                _layerMousePresses = _layerMouseReleases = _globalEscapes = _layerEscapes = 0;
                _vm = new ReignGovernmentScreenVM(ReignGovernmentCampaignBehavior.Instance, _kingdom, _interactive, ReturnToOrigin);
                if (!string.IsNullOrWhiteSpace(request.BusinessId)) _vm.SelectBusinessById(request.BusinessId);
                _layer = new GauntletLayer("ReignGovernmentScreen", LayerOrder, false);
                _layer.LoadMovie("ReignGovernmentScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                _layer.Input.IsMouseButtonAllowed = true;
                _layer.Input.IsMouseWheelAllowed = true;
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
                PauseCampaign();
                RememberPresentedStage();
                ReignLog.Info("Government screen opened for " + _kingdom.StringId
                    + " mode=" + (_interactive ? "ruler" : "read-only") + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Government screen failed to open: " + ex);
                Close(false);
                _scanDelay = 5f;
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Government could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (_pendingReturn != null && !IsOpen)
            {
                Action callback = _pendingReturn;
                _pendingReturn = null;
                callback();
                return;
            }
            if (_pendingOpen != null && !IsOpen)
            {
                OpenRequest request = _pendingOpen;
                _pendingOpen = null;
                OpenNow(request);
                return;
            }
            if (!IsOpen) { TryPresentPendingBusiness(dt); return; }
            if (_kingdom == null || _kingdom.IsEliminated || _host == null || _layer == null
                || ScreenManager.TopScreen != _host || !SafeHasLayer(_host, _layer))
            {
                Close(false);
                return;
            }
            try
            {
                _inputTicks++;
                if (Input.IsKeyPressed(InputKey.LeftMouseButton)) _globalMousePresses++;
                if (Input.IsKeyReleased(InputKey.LeftMouseButton)) _globalMouseReleases++;
                if (_layer.Input.IsKeyPressed(InputKey.LeftMouseButton)) _layerMousePresses++;
                if (_layer.Input.IsKeyReleased(InputKey.LeftMouseButton)) _layerMouseReleases++;
                if (Input.IsKeyReleased(InputKey.Escape)) _globalEscapes++;
                if (_layer.Input.IsKeyReleased(InputKey.Escape)) _layerEscapes++;
                _vm?.Tick();
                RememberPresentedStage();
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                if (Input.IsKeyReleased(InputKey.Escape) || _layer.Input.IsKeyReleased(InputKey.Escape)) ReturnToOrigin();
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Government screen tick failed: " + ex.Message);
                string retryId = _vm?.ActiveBusinessId;
                Close(false);
                if (!string.IsNullOrWhiteSpace(retryId)) PresentedStages.Remove(retryId);
                _scanDelay = 5f;
            }
        }

        private static void RememberPresentedStage()
        {
            if (IsOpen && !string.IsNullOrWhiteSpace(_vm?.ActiveBusinessId)) PresentedStages[_vm.ActiveBusinessId] = _vm.ActivePresentationKey;
        }

        private static void TryPresentPendingBusiness(float dt)
        {
            _scanDelay -= Math.Max(0f, dt);
            if (_scanDelay > 0f) return;
            _scanDelay = 0.5f;
            var behavior = ReignGovernmentCampaignBehavior.Instance;
            if (!ReferenceEquals(behavior, _presentationBehavior)) { PresentedStages.Clear(); _presentationBehavior = behavior; }
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (behavior == null || kingdom?.Leader != Hero.MainHero || !CanPresentOnMap()) return;
            var next = behavior.GetBusiness(kingdom.StringId).FirstOrDefault(x =>
                (x.Status == "hearing" || x.Status == "reconsideration" || x.Status == "awaiting_ruler")
                && (!PresentedStages.TryGetValue(x.BusinessId, out string shown) || shown != x.Status + "|" + x.Postponed + "|" + x.RecessUntilDay));
            if (next == null) return;
            OpenNow(new OpenRequest { Kingdom = kingdom, Interactive = true, BusinessId = next.BusinessId });
        }

        internal static bool CanPresentOnMap()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlModeLock || MobileParty.MainParty == null || Mission.Current != null
                || CharacterObject.OneToOneConversationCharacter != null || InformationManager.IsAnyInquiryActive()
                || MobileParty.MainParty.MapEvent != null || MapEvent.PlayerMapEvent != null || MobileParty.MainParty.SiegeEvent != null
                || campaign.CurrentMenuContext != null) return false;
            MapState state = Game.Current?.GameStateManager?.ActiveState as MapState;
            if (state == null || state.AtMenu || state.MapConversationActive || state.IsSimulationActive) return false;
            ScreenBase screen = ScreenManager.TopScreen;
            if (screen == null || (screen.GetType().FullName ?? "").IndexOf("MapScreen", StringComparison.OrdinalIgnoreCase) < 0) return false;
            // Native escape menus and independent Reign layers can share the map host. Do not steal their focus.
            var escape = screen.GetType().GetProperty("IsEscapeMenuOpened");
            if (escape?.GetValue(screen, null) is bool open && open) return false;
            var focused = ScreenManager.FocusedLayer;
            if (focused is GauntletLayer)
            {
                string name = focused.GetType().GetProperty("Name")?.GetValue(focused, null)?.ToString() ?? string.Empty;
                if (name.IndexOf("Map", StringComparison.OrdinalIgnoreCase) < 0 || new[] { "Reign", "Escape", "Conversation", "Kingdom" }.Any(x => name.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
            }
            return true;
        }

        private static void PauseCampaign()
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlModeLock) return;
            campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            campaign.SetTimeControlModeLock(true); _ownsTimeLock = true;
        }

        private static void ReturnToOrigin()
        {
            Action callback = _returnAction;
            Close(false);
            _pendingReturn = callback;
        }

        public static void Close(bool returnToOrigin)
        {
            RememberPresentedStage();
            Action callback = returnToOrigin ? _returnAction : null;
            _pendingOpen = null;
            if (_layer != null)
            {
                try { _layer.IsFocusLayer = false; } catch { }
                try { _layer.InputRestrictions?.ResetInputRestrictions(); } catch { }
                try { ScreenManager.TryLoseFocus(_layer); } catch { }
            }
            if (_host != null && _layer != null)
            {
                try { if (_host.HasLayer(_layer)) _host.RemoveLayer(_layer); }
                catch (Exception ex) { ReignLog.Warn("Government layer removal failed: " + ex.Message); }
            }
            _vm?.OnFinalize();
            if (_ownsTimeLock)
            {
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (campaign != null) { campaign.SetTimeControlModeLock(false); campaign.TimeControlMode = CampaignTimeControlMode.Stop; }
                _ownsTimeLock = false;
            }
            _vm = null;
            _layer = null;
            _host = null;
            _kingdom = null;
            _returnAction = null;
            _interactive = false;
            IsOpen = false;
            if (callback != null) _pendingReturn = callback;
        }

        private static bool SafeHasLayer(ScreenBase host, GauntletLayer layer)
        {
            try { return host?.HasLayer(layer) == true; }
            catch { return false; }
        }

        private sealed class OpenRequest
        {
            internal Kingdom Kingdom;
            internal bool Interactive;
            internal Action ReturnAction;
            internal string BusinessId;
        }
    }
}
