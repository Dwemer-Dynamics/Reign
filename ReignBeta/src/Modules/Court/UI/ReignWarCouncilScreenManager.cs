using System;
using System.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.EventArt;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    public static class ReignWarCouncilScreenManager
    {
        private const int LayerOrder = 9998;
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static ReignWarCouncilScreenVM _vm;
        private static ReignCourtCampaignBehavior _court;
        private static CampaignTimeControlMode _priorTimeMode;
        private static bool _restoreTime;
        public static bool IsOpen { get; private set; }
        public static ReignWarCouncilScreenVM ActiveViewModel => _vm;

        public static void Open(ReignCourtCampaignBehavior court)
        {
            if (court == null || !court.IsRuleModeActive) return;
            if (IsOpen) Close(false);
            ReignCourtScreenManager.Close();
            _host = ScreenManager.TopScreen;
            if (_host == null) return;
            try
            {
                _court = court;
                if (!PauseCampaign())
                    throw new InvalidOperationException("Campaign time could not be paused for the War Council.");
                _vm = new ReignWarCouncilScreenVM(() => Close(true));
                _layer = new GauntletLayer("ReignWarCouncilScreen", LayerOrder, false);
                ApplyScale();
                _layer.LoadMovie("ReignWarCouncilScreen", _vm);
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
                ReignLog.Warn("War Council screen failed to open: " + ex);
                Close(true);
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] War Council could not open: " + ex.Message));
            }
        }

        public static void ApplicationTick(float dt)
        {
            if (!IsOpen) return;
            if (_host == null || _layer == null || ScreenManager.TopScreen != _host || !_host.HasLayer(_layer))
            { Close(false); return; }
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
                ReignLog.Warn("War Council screen tick failed: " + ex.Message);
                Close(true);
            }
        }

        public static bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            if (!IsOpen || _vm == null) { error = "The War Council is not open."; return false; }
            string raw = (action ?? string.Empty).Trim();
            int separator = raw.IndexOf(':');
            string verb = (separator >= 0 ? raw.Substring(0, separator) : raw).Trim().ToLowerInvariant().Replace('_', '-');
            string arg = separator >= 0 ? raw.Substring(separator + 1).Trim() : (value ?? string.Empty).Trim();
            switch (verb)
            {
                case "select-lord": return FailUnless(_vm.SelectLord(arg), "Unknown War Council lord.", out error);
                case "open-raven": return FailUnless(_vm.OpenMessengerForAutomation(arg), "Unknown War Council lord.", out error);
                case "set-raven-text": return FailUnless(_vm.SetMessengerTextForAutomation(arg), "Open a raven message and provide non-empty text.", out error);
                case "send-raven": return FailUnless(_vm.SendMessengerForAutomation(), "The raven message is not ready to send.", out error);
                case "cancel-raven": _vm.ExecuteCancelMessenger(); return true;
                case "mobilize": return FailUnless(_vm.MobilizeForAutomation(arg), "Mobilization was refused by native safeguards.", out error);
                case "pan-map": return FailUnless(_vm.PanForAutomation(arg), "Map pan was invalid or reached the parchment boundary.", out error);
                case "center-settlement": return FailUnless(_vm.CenterOnSettlementForAutomation(arg), "Unknown War Council settlement.", out error);
                case "toggle-councilor": _vm.ExecuteToggleCouncilorDropdown(); return true;
                case "select-councilor": return FailUnless(_vm.AssignCouncilorForAutomation(arg), "That lord is not available as War Councilor.", out error);
                case "use-player-skills": return FailUnless(_vm.UsePlayerSkillsForAutomation(), "The ruler-skill fallback could not be selected.", out error);
                default: error = "Unsupported War Council automation action."; return false;
            }
        }

        private static bool FailUnless(bool ok, string message, out string error) { error = ok ? string.Empty : message; return ok; }

        public static int AutomationLordCount => _vm?.Lords.Count ?? 0;
        public static int AutomationPartyCount => _vm?.Markers.Count ?? 0;
        public static int AutomationKingdomCount => _vm?.Kingdoms.Count ?? 0;
        public static int AutomationReportCount => _vm?.Reports.Count ?? 0;
        public static int AutomationRealmBattleReportCount => _vm?.Reports.Count(x => x.PlayerRealmInvolved) ?? 0;
        public static float AutomationZoom => _vm?.Zoom ?? 0f;
        public static float AutomationMapOffsetX => _vm?.MapOffsetX ?? 0f;
        public static float AutomationMapOffsetY => _vm?.MapOffsetY ?? 0f;
        public static string AutomationStatus => _vm?.StatusText ?? string.Empty;
        public static int AutomationClanPartyCount => _vm?.Markers.Count(x => x.IsPlayerClan) ?? 0;
        public static int AutomationKingdomPartyCount => _vm?.Markers.Count(x => x.IsPlayerRealm && !x.IsPlayerClan) ?? 0;
        public static int AutomationPlayerRealmPartyCount => _vm?.Markers.Count(x => x.IsPlayerRealm) ?? 0;
        public static int AutomationHostilePartyCount => _vm?.Markers.Count(x => x.IsHostile) ?? 0;
        public static int AutomationForeignPartyCount => _vm?.Markers.Count(x => x.IsForeign) ?? 0;
        public static int AutomationDetectedForeignPartyCount => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.DetectedForeignPartyCount ?? 0;
        public static int AutomationCouncilorTactics => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.LastCouncilorTactics ?? 0;
        public static int AutomationCouncilorLeadership => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.LastCouncilorLeadership ?? 0;
        public static double AutomationDetectionRange => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.LastDetectionRange ?? 0d;
        public static double AutomationDetectionChance => ReignWarCouncilRules.ForeignPartyDetectionChance(AutomationCouncilorLeadership);
        public static string AutomationCapitalSettlementId => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.LastCapitalSettlementId ?? string.Empty;
        public static string AutomationSelectedCouncilorHeroId => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId ?? string.Empty;
        public static string AutomationEffectiveCouncilorHeroId => ReignBeta.Court.WarCouncil.ReignWarCouncilCampaignBehavior.Instance?.EffectiveCouncilorHeroId ?? string.Empty;
        public static int AutomationCouncilorCandidateCount => _vm?.CouncilorCandidates.Count ?? 0;
        public static bool AutomationCouncilorDropdownOpen => _vm?.CouncilorDropdownOpen == true;
        public static int AutomationNavalPartyCount => _vm?.Markers.Count(x => x.IsNaval) ?? 0;
        public static int AutomationPlayerRealmNavalPartyCount => _vm?.Markers.Count(x => x.IsPlayerRealm && x.IsNaval) ?? 0;
        public static int AutomationHostileNavalPartyCount => _vm?.Markers.Count(x => x.IsHostile && x.IsNaval) ?? 0;
        public static int AutomationInfantryPartyCount => _vm?.Markers.Count(x => x.Composition == ReignWarPartyComposition.Infantry) ?? 0;
        public static int AutomationMissilePartyCount => _vm?.Markers.Count(x => x.Composition == ReignWarPartyComposition.Missile) ?? 0;
        public static int AutomationMountedPartyCount => _vm?.Markers.Count(x => x.Composition == ReignWarPartyComposition.Mounted) ?? 0;
        public static int AutomationPartylessLordCount => _vm?.Lords.Count(x => x.PartyText == "No active party") ?? 0;
        public static int AutomationMobilizableLordCount => _vm?.Lords.Count(x => x.CanMobilize) ?? 0;
        public static int AutomationFollowingOrderCount => _vm?.Lords.Count(x => x.IsFollowingOrders) ?? 0;
        public static int AutomationWarCouncilOrderCount => _vm?.Lords.Count(x => x.HasWarCouncilOrder) ?? 0;
        public static string AutomationFirstPartyHeroId => _vm?.Markers.FirstOrDefault(x => x.IsPlayerRealm)?.HeroId ?? string.Empty;
        public static float AutomationFirstPartyWorldX => _vm?.Markers.FirstOrDefault(x => x.IsPlayerRealm)?.WorldX ?? 0f;
        public static float AutomationFirstPartyWorldY => _vm?.Markers.FirstOrDefault(x => x.IsPlayerRealm)?.WorldY ?? 0f;
        public static string AutomationFirstMobilizableHeroId => _vm?.Lords.FirstOrDefault(x => x.CanMobilize)?.HeroId ?? string.Empty;
        public static string AutomationFirstHostileSettlementId => _vm?.Settlements.FirstOrDefault(x => x.IsHostile)?.SettlementId ?? string.Empty;
        public static float AutomationFirstHostileSettlementWorldX => _vm?.Settlements.FirstOrDefault(x => x.IsHostile)?.WorldX ?? 0f;
        public static float AutomationFirstHostileSettlementWorldY => _vm?.Settlements.FirstOrDefault(x => x.IsHostile)?.WorldY ?? 0f;
        public static int AutomationTownSettlementCount => _vm?.Settlements.Count(x => x.IsTown) ?? 0;
        public static int AutomationCastleSettlementCount => _vm?.Settlements.Count(x => x.IsCastle) ?? 0;
        public static int AutomationVillageSettlementCount => _vm?.Settlements.Count(x => x.IsVillage) ?? 0;
        public static int AutomationSettlementListCount => _vm?.Settlements.Count ?? 0;
        public static string AutomationLastCenteredSettlementId => _vm?.LastCenteredSettlementId ?? string.Empty;
        public static bool AutomationFixedOverview => _vm != null
            && Math.Abs(_vm.Zoom - (float)ReignWarCouncilRules.MinimumZoom) < 0.001f
            && Math.Abs(_vm.MapWidth - ReignWarCouncilScreenVM.BaseMapWidth * _vm.Zoom) < 0.001f
            && Math.Abs(_vm.MapHeight - ReignWarCouncilScreenVM.BaseMapHeight * _vm.Zoom) < 0.001f;
        public static bool AutomationMapPanningEnabled => _vm != null
            && _vm.MapWidth > ReignWarCouncilScreenVM.ViewportWidth
            && _vm.MapHeight > ReignWarCouncilScreenVM.ViewportHeight
            && _vm.MapOffsetX <= 0f && _vm.MapOffsetX >= ReignWarCouncilScreenVM.ViewportWidth - _vm.MapWidth
            && _vm.MapOffsetY <= 0f && _vm.MapOffsetY >= ReignWarCouncilScreenVM.ViewportHeight - _vm.MapHeight;
        public static string AutomationCouncilorName => _vm?.CouncilorName ?? string.Empty;
        public static string AutomationCouncilorSkillText => _vm?.CouncilorSkillText ?? string.Empty;
        public static int AutomationPortraitRevision => _vm?.PortraitRevision ?? 0;
        public static bool AutomationMapImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "map"));
        public static int AutomationHighDefinitionMapTileCount
        {
            get
            {
                int count = 0;
                for (int y = 0; y < ReignWarCouncilMapWidget.HighDefinitionTileGridSize; y++)
                    for (int x = 0; x < ReignWarCouncilMapWidget.HighDefinitionTileGridSize; x++)
                        if (!string.IsNullOrWhiteSpace(ReignEventArtTextureFactory.ResolveImagePath(
                            "war_council", "map_tile_" + x + "_" + y))) count++;
                return count;
            }
        }
        public static bool AutomationScrollFrameImageAvailable => false;
        public static bool AutomationOuterFrameImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "outer_frame"));
        public static bool AutomationPanelFrameOverlayImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "panel_frame_overlay"));
        public static bool AutomationPanelFrameImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "panel_frame"));
        public static bool AutomationTallPanelFrameImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "panel_frame_tall"));
        public static bool AutomationRavenImageAvailable => !string.IsNullOrWhiteSpace(
            ReignEventArtTextureFactory.ResolveImagePath("war_council", "raven_scroll"));
        public static bool AutomationSettlementImagesAvailable => false;
        public static bool AutomationMessengerVisible => _vm?.MessengerVisible == true;
        public static bool AutomationMessengerBusy => _vm?.MessengerBusy == true;
        public static string AutomationMessengerRecipient => _vm?.MessengerRecipientName ?? string.Empty;
        public static string AutomationMessengerStatus => _vm?.MessengerStatus ?? string.Empty;
        public static bool AutomationTimePaused => TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode == CampaignTimeControlMode.Stop;

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
            catch (Exception ex) { ReignLog.Warn("War Council cleanup failed: " + ex.Message); }
            _vm?.OnFinalize(); _vm = null; _layer = null; _host = null; _court = null; IsOpen = false;
            RestoreCampaignTime();
            if (returnToCourt && court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court);
        }

        private static bool PauseCampaign()
        {
            TaleWorlds.CampaignSystem.Campaign campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null) return false;
            if (campaign.TimeControlModeLock)
                return campaign.TimeControlMode == CampaignTimeControlMode.Stop;

            _priorTimeMode = campaign.TimeControlMode;
            _restoreTime = true;
            campaign.TimeControlMode = CampaignTimeControlMode.Stop;
            campaign.SetTimeControlModeLock(true);
            return campaign.TimeControlMode == CampaignTimeControlMode.Stop;
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

        private static void ApplyScale()
        {
            if (_layer?.UIContext == null) return;
            float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
            if (width <= 0f || height <= 0f) return;
            float authoredScale = Math.Min(width / ReferenceWidth, height / ReferenceHeight);
            float nativeScale = height / 1080f;
            float modifier = authoredScale * _layer.Scale / Math.Max(0.01f, nativeScale);
            if (Math.Abs(_layer.UIContext.ScaleModifier - modifier) > 0.0001f) _layer.UIContext.ScaleModifier = modifier;
        }
    }
}
