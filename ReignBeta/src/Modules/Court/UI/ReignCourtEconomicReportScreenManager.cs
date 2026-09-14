using System;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignCourtEconomicReportScreenManager
    {
        private const int LayerOrder = 9998;
        private const float ReferenceWidth = 1678f;
        private const float ReferenceHeight = 937f;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignCourtEconomicReportVM _vm;
        private static ReignCourtCampaignBehavior _court;
        public static bool IsOpen { get; private set; }

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.IsRuleModeActive) return;
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_economic_report") || !ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_clan_accords"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Economic Report graphics could not be loaded. See the Reign log for details."));
                return;
            }
            ReignCourtScreenManager.Close();
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            try
            {
                _court = court;
                _vm = new ReignCourtEconomicReportVM(court, () => Close(true));
                _layer = new GauntletLayer("ReignCourtEconomicReportScreen", LayerOrder, false);
                ApplyScale();
                _layer.LoadMovie("ReignCourtEconomicReportScreen", _vm);
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
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Economic Report screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Economic Report could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer))
            {
                Close(false);
                return;
            }
            try
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _layer.Input.IsKeysAllowed = true;
                if (ScreenManager.FocusedLayer != _layer) ScreenManager.TrySetFocus(_layer);
                _vm?.OnFrameTick(dt);
                ApplyScale();
                if (Input.IsKeyReleased(InputKey.Escape)) Close(true);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Economic Report screen tick failed: " + ex.Message);
                Close(true);
            }
        }

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null) return;
            float width = Screen.RealScreenResolutionWidth;
            float height = Screen.RealScreenResolutionHeight;
            if (width <= 0f || height <= 0f) return;
            float authoredScale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
            float nativeScale = height / 1080f;
            float modifier = authoredScale * _layer.Scale / Math.Max(0.01f, nativeScale);
            if (Math.Abs(_layer.UIContext.ScaleModifier - modifier) > 0.0001f)
                _layer.UIContext.ScaleModifier = modifier;
        }

        public static string AutomationSourceName => _vm?.SourceName ?? string.Empty;
        public static string AutomationTargetName => _vm?.TargetName ?? string.Empty;
        public static string AutomationGoodsName => _vm?.GoodsName ?? string.Empty;
        public static string AutomationAvailableText => _vm?.AvailableText ?? string.Empty;
        public static string AutomationAmountText => _vm?.AmountText ?? string.Empty;
        public static string AutomationArrivalText => _vm?.ArrivalText ?? string.Empty;
        public static string AutomationSourceId => _vm?.SelectedSourceId ?? string.Empty;
        public static string AutomationTargetId => _vm?.SelectedTargetId ?? string.Empty;
        public static float AutomationTravelDays => _vm?.TravelDays ?? 0f;
        public static int AutomationSourceOptionCount => _vm?.SourceOptionCount ?? 0;
        public static int AutomationTargetOptionCount => _vm?.TargetOptionCount ?? 0;
        public static bool AutomationSourceDropdownOpen => _vm?.IsSourceDropdownOpen == true;
        public static bool AutomationTargetDropdownOpen => _vm?.IsTargetDropdownOpen == true;
        public static ReignEconomicShipment AutomationLatestShipment => _vm?.LatestShipment;
        public static bool AutomationCanSubmit => _vm?.CanSubmit == true;

        public static bool TryExecuteAutomationAction(string action, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _vm == null)
            {
                error = "The Economic Report is not open.";
                return false;
            }

            string raw = (action ?? string.Empty).Trim();
            int separator = raw.IndexOf(':');
            string verb = (separator >= 0 ? raw.Substring(0, separator) : raw)
                .Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
            string value = separator >= 0 ? raw.Substring(separator + 1).Trim() : string.Empty;
            if (string.Equals(verb, "select-source", StringComparison.Ordinal))
            {
                if (_vm.SelectSourceById(value)) return true;
                error = "The requested settlement is not a valid player-kingdom origin.";
                return false;
            }
            if (string.Equals(verb, "select-target", StringComparison.Ordinal))
            {
                if (_vm.SelectTargetById(value)) return true;
                error = "The requested settlement is not a valid fortification destination.";
                return false;
            }
            if (string.Equals(verb, "set-amount", StringComparison.Ordinal))
            {
                if (!int.TryParse(value, out int amount) || amount < 1)
                {
                    error = "set-amount requires a positive whole number.";
                    return false;
                }
                _vm.SetAmountForAutomation(amount);
                return true;
            }
            if (string.Equals(verb, "set-notes", StringComparison.Ordinal))
            {
                _vm.SetNotesForAutomation(value);
                return true;
            }

            switch (verb)
            {
                case "toggle-source": _vm.ExecuteToggleSourceDropdown(); break;
                case "toggle-target": _vm.ExecuteToggleTargetDropdown(); break;
                case "previous-source": _vm.ExecutePreviousSource(); break;
                case "next-source": _vm.ExecuteNextSource(); break;
                case "previous-target": _vm.ExecutePreviousTarget(); break;
                case "next-target": _vm.ExecuteNextTarget(); break;
                case "decrease-amount": _vm.ExecuteDecreaseAmount(); break;
                case "increase-amount": _vm.ExecuteIncreaseAmount(); break;
                case "submit": _vm.ExecuteSubmit(); break;
                default:
                    error = "Unsupported Economic Report automation action.";
                    return false;
            }
            return true;
        }

        public static void Close(bool returnToCourt)
        {
            ReignCourtCampaignBehavior court = _court;
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
            catch (Exception ex) { ReignLog.Warn("Economic Report cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            _court = null;
            IsOpen = false;
            if (returnToCourt && court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court);
        }
    }
}
