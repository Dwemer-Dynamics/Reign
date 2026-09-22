#if !REIGN_EXCLUDE_COURT
using System;
using ReignBeta.Integration;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignTrainingYardScreenManager
    {
        private const int LayerOrder = 9998;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignTrainingYardScreenVM _vm;
        private static Settlement _settlement;
        private static Settlement _pendingSettlement;
        private static bool _returnToKeepPending;

        public static bool IsOpen { get; private set; }
        public static bool IsTraining => _vm?.IsTraining == true;
        internal static string AutomationStatus => _vm?.StatusText ?? string.Empty;
        internal static int AutomationTrainerCount => _vm?.Trainers?.Count ?? 0;
        internal static int AutomationTroopCount => _vm?.Troops?.Count ?? 0;
        internal static bool AutomationIsTraining => _vm?.IsTraining == true;
        internal static long AutomationTotalXpDelivered => _vm?.TotalXpDelivered ?? 0;

        public static void Open(Settlement settlement)
        {
            if (settlement?.IsFortification != true || settlement != Settlement.CurrentSettlement) return;
            _pendingSettlement = settlement;
        }

        private static void OpenNow(Settlement settlement)
        {
            if (IsOpen) Close(false);
            if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_training_yard")
                || !ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_family_chambers"))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Training Yard graphics could not be loaded."));
                ReturnToKeep();
                return;
            }
            _host = ScreenManager.TopScreen;
            if (_host == null) { ReturnToKeep(); return; }
            try
            {
                _settlement = settlement;
                PauseCampaign();
                _vm = new ReignTrainingYardScreenVM(settlement, () => Close(true), StartTraining, PauseCampaign);
                _layer = new GauntletLayer("ReignTrainingYardScreen", LayerOrder, false);
                _layer.LoadMovie("ReignTrainingYardScreen", _vm);
                _layer.IsFocusLayer = true;
                _layer.ActiveCursor = CursorType.Default;
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _host.AddLayer(_layer);
                _host.MouseVisible = true;
                ScreenManager.TrySetFocus(_layer);
                IsOpen = true;
                ReignLog.Info("Training Yard opened at " + settlement.StringId + ".");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Training Yard failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] Training Yard could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (_returnToKeepPending && !IsOpen)
            {
                _returnToKeepPending = false;
                ReturnToKeep();
                return;
            }
            if (_pendingSettlement != null && !IsOpen)
            {
                Settlement pending = _pendingSettlement;
                _pendingSettlement = null;
                OpenNow(pending);
                return;
            }
            if (!IsOpen) return;
            if (!HasValidHost())
            {
                StopForSafety("Training stopped because the keep is no longer available.", false);
                return;
            }
            if (IsTraining && !CanContinueTraining(out string reason))
            {
                StopForSafety(reason, true);
                return;
            }
            _vm?.OnFrameTick(dt);
            if (Input.IsKeyReleased(InputKey.Escape)) Close(true);
        }

        public static void OnCampaignHourlyTick()
        {
            if (!IsOpen || !IsTraining) return;
            if (!CanContinueTraining(out string reason))
            {
                StopForSafety(reason, true);
                return;
            }
            if (_vm?.ApplyHourlyTraining(out reason) != true)
                StopForSafety(reason, true);
        }

        internal static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _vm == null)
            {
                error = "The Training Yard is not open.";
                return false;
            }
            string normalized = (action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            switch (normalized)
            {
                case "select-trainer":
                    return _vm.TrySelectTrainer(value, out error);
                case "train":
                    _vm.ExecuteTrain();
                    if (_vm.IsTraining) return true;
                    error = _vm.StatusText;
                    return false;
                case "stop":
                    _vm.ExecuteStop();
                    return !_vm.IsTraining;
                default:
                    error = "Unsupported Training Yard action. Use select-trainer, train, or stop.";
                    return false;
            }
        }

        private static bool StartTraining()
        {
            if (!CanContinueTraining(out _)) return false;
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null) return false;
            try
            {
                // Parties inside a settlement do not advance under ordinary
                // StoppableFastForward. Bannerlord's native settlement wait menu
                // owns the clock and establishes UnstoppableFastForward itself.
                TaleWorlds.CampaignSystem.GameMenus.GameMenu waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu?.IsWaitMenu != true)
                {
                    TaleWorlds.CampaignSystem.GameMenus.GameMenu.SwitchToMenu("town_wait_menus");
                    waitMenu = campaign.CurrentMenuContext?.GameMenu;
                }
                if (waitMenu?.IsWaitMenu != true) return false;

                // StartWait is deliberately called even when IsWaitActive was
                // retained by an earlier pause; it reasserts the native mode.
                waitMenu.StartWait();
                return waitMenu.IsWaitActive
                    && campaign.TimeControlMode == CampaignTimeControlMode.UnstoppableFastForward;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Training Yard could not start native settlement waiting: " + ex.Message);
                return false;
            }
        }

        private static bool CanContinueTraining(out string reason)
        {
            reason = string.Empty;
            MobileParty party = MobileParty.MainParty;
            if (_settlement == null || Settlement.CurrentSettlement != _settlement || _settlement.IsFortification != true)
            { reason = "Training stopped because the party left the keep."; return false; }
            if (party == null || !party.IsActive)
            { reason = "Training stopped because the player party is unavailable."; return false; }
            if (party.MapEvent != null || party.BesiegerCamp != null || _settlement.SiegeEvent != null)
            { reason = "Training stopped because combat or a siege interrupted the yard."; return false; }
            return true;
        }

        private static bool HasValidHost()
        {
            try
            {
                return _host != null && _layer != null && ScreenManager.TopScreen == _host && _host.HasLayer(_layer);
            }
            catch { return false; }
        }

        private static void StopForSafety(string reason, bool keepOpen)
        {
            PauseCampaign();
            _vm?.SetStopped(string.IsNullOrWhiteSpace(reason) ? "Training stopped safely." : reason);
            if (!keepOpen) Close(false);
        }

        private static void PauseCampaign()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null) return;
            try
            {
                TaleWorlds.CampaignSystem.GameMenus.GameMenu waitMenu = campaign.CurrentMenuContext?.GameMenu;
                if (waitMenu?.IsWaitMenu == true)
                {
                    waitMenu.EndWait();
                    return;
                }
                if (!campaign.TimeControlModeLock)
                    campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Training Yard could not pause native settlement waiting: " + ex.Message);
            }
        }

        public static void Close(bool returnToKeep)
        {
            _pendingSettlement = null;
            if (returnToKeep) _returnToKeepPending = true;
            PauseCampaign();
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
            catch (Exception ex) { ReignLog.Warn("Training Yard cleanup failed: " + ex.Message); }
            _vm?.OnFinalize();
            _vm = null;
            _layer = null;
            _host = null;
            _settlement = null;
            IsOpen = false;
        }

        private static void ReturnToKeep()
        {
            try { TaleWorlds.CampaignSystem.GameMenus.GameMenu.ActivateGameMenu("town_keep"); }
            catch (Exception ex) { ReignLog.Warn("Could not return to the native keep menu: " + ex.Message); }
        }
    }
}
#endif
