using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Integration;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.EventArt;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignWarCouncilScreenVM : ViewModel
    {
        public const float ViewportWidth = 820f;
        public const float ViewportHeight = 820f;
        public const float BaseMapWidth = 820f;
        public const float BaseMapHeight = 820f;
        private readonly Action _close;
        private float _zoom;
        private float _mapOffsetX;
        private float _mapOffsetY;
        private int _markerRevision;
        private int _portraitRevision;
        private string _markerFingerprint = string.Empty;
        private string _settlementFingerprint = string.Empty;
        private string _councilorHeroId = string.Empty;
        private string _councilorName = "Unassigned";
        private string _councilorTitle = "No Marshal has been appointed";
        private string _councilorSkillText = "Tactics —\nForeign intelligence is limited.";
        private string _councilorPortraitId = string.Empty;
        private string _councilorPortraitArgs = string.Empty;
        private string _councilorPortraitProvider = string.Empty;
        private string _statusText = "Black numbered figures mark your realm's parties. Click one to select its lord.";
        private bool _pointerDown;
        private float _lastPointerX;
        private float _lastPointerY;
        private float _latestMapPointerX;
        private float _latestMapPointerY;
        private int _selectedLordIndex = -1;
        private int _lordCount;
        private Hero _messengerRecipient;
        private bool _messengerVisible;
        private bool _messengerBusy;
        private string _messengerInputText = string.Empty;
        private string _messengerRecipientName = string.Empty;
        private string _messengerStatus = string.Empty;
        private bool _finalized;

        public ReignWarCouncilScreenVM(Action close)
        {
            _close = close;
            Lords = new MBBindingList<ReignWarCouncilLordVM>();
            CouncilorCandidates = new MBBindingList<ReignWarCouncilLordVM>();
            Kingdoms = new MBBindingList<ReignWarCouncilKingdomVM>();
            Reports = new MBBindingList<ReignWarCouncilReportVM>();
            Markers = new List<ReignWarCouncilMarker>();
            Settlements = new MBBindingList<ReignWarCouncilSettlementMarker>();
            MapImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "map");
            OuterFrameImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "outer_frame");
            RavenImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "raven_scroll");
            PanelFrameImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "panel_frame");
            TallPanelFrameImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "panel_frame_tall");
            PanelFrameOverlayImageId = ReignEventArtTextureFactory.BuildImageId("war_council", "panel_frame_overlay");
            Zoom = (float)ReignWarCouncilRules.MinimumZoom;
            ReignWarCouncilCampaignBehavior.Instance?.RefreshForeignPartyIntelligence(true);
            RefreshAll();
            CenterMapOnWorld(
                MobileParty.MainParty?.Position.X
                    ?? (float)((ReignWarCouncilRules.PlayableMinimumX + ReignWarCouncilRules.PlayableMaximumX) * 0.5d),
                MobileParty.MainParty?.Position.Y
                    ?? (float)((ReignWarCouncilRules.PlayableMinimumY + ReignWarCouncilRules.PlayableMaximumY) * 0.5d));
        }

        public MBBindingList<ReignWarCouncilLordVM> Lords { get; }
        public MBBindingList<ReignWarCouncilKingdomVM> Kingdoms { get; }
        public MBBindingList<ReignWarCouncilReportVM> Reports { get; }
        public IReadOnlyList<ReignWarCouncilMarker> Markers { get; private set; }
        public MBBindingList<ReignWarCouncilSettlementMarker> Settlements { get; private set; }
        [DataSourceProperty] public string MapImageId { get; }
        [DataSourceProperty] public string OuterFrameImageId { get; }
        [DataSourceProperty] public string RavenImageId { get; }
        [DataSourceProperty] public float MapWidth => BaseMapWidth * Zoom;
        [DataSourceProperty] public float MapHeight => BaseMapHeight * Zoom;
        [DataSourceProperty] public float Zoom { get => _zoom; private set { if (Math.Abs(_zoom - value) > 0.0001f) { _zoom = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(MapWidth)); OnPropertyChanged(nameof(MapHeight)); } } }
        [DataSourceProperty] public float MapOffsetX { get => _mapOffsetX; private set { if (Math.Abs(_mapOffsetX - value) > 0.01f) { _mapOffsetX = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public float MapOffsetY { get => _mapOffsetY; private set { if (Math.Abs(_mapOffsetY - value) > 0.01f) { _mapOffsetY = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public int MarkerRevision { get => _markerRevision; private set { _markerRevision = value; OnPropertyChangedWithValue(value); } }
        [DataSourceProperty] public int SelectedLordIndex { get => _selectedLordIndex; private set { if (_selectedLordIndex != value) { _selectedLordIndex = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public int LordCount { get => _lordCount; private set { if (_lordCount != value) { _lordCount = value; OnPropertyChangedWithValue(value); } } }
        public int PortraitRevision => _portraitRevision;
        [DataSourceProperty] public string StatusText { get => _statusText; private set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string ZoomText => "CLOSE STRATEGIC VIEW • DRAG TO PAN";
        [DataSourceProperty] public string CouncilorName { get => _councilorName; private set { if (_councilorName != value) { _councilorName = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CouncilorTitle { get => _councilorTitle; private set { if (_councilorTitle != value) { _councilorTitle = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CouncilorSkillText { get => _councilorSkillText; private set { if (_councilorSkillText != value) { _councilorSkillText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CouncilorPortraitId { get => _councilorPortraitId; private set { if (_councilorPortraitId != value) { _councilorPortraitId = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CouncilorPortraitArgs { get => _councilorPortraitArgs; private set { if (_councilorPortraitArgs != value) { _councilorPortraitArgs = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CouncilorPortraitProvider { get => _councilorPortraitProvider; private set { if (_councilorPortraitProvider != value) { _councilorPortraitProvider = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool MessengerVisible { get => _messengerVisible; private set { if (_messengerVisible != value) { _messengerVisible = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool MessengerBusy { get => _messengerBusy; private set { if (_messengerBusy != value) { _messengerBusy = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string MessengerInputText { get => _messengerInputText; set { if (_messengerInputText != value) { _messengerInputText = value ?? string.Empty; OnPropertyChangedWithValue(_messengerInputText); } } }
        [DataSourceProperty] public string MessengerRecipientName { get => _messengerRecipientName; private set { if (_messengerRecipientName != value) { _messengerRecipientName = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(MessengerTitle)); } } }
        [DataSourceProperty] public string MessengerTitle => "RAVEN TO " + MessengerRecipientName;
        [DataSourceProperty] public string MessengerStatus { get => _messengerStatus; private set { if (_messengerStatus != value) { _messengerStatus = value; OnPropertyChangedWithValue(value); } } }
        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteZoomIn() => StatusText = "The War Council uses one close fixed scale. Drag the parchment to inspect another region.";
        public void ExecuteZoomOut() => StatusText = "The War Council uses one close fixed scale. Drag the parchment to inspect another region.";
        public void ExecuteCancelMessenger()
        {
            if (MessengerBusy) return;
            MessengerVisible = false;
            MessengerInputText = string.Empty;
            MessengerStatus = string.Empty;
            _messengerRecipient = null;
        }

        public async void ExecuteSendMessenger() => await SendMessengerAsync();

        public void ExecuteMapSurfaceClick()
        {
            SelectMarkerAt(_latestMapPointerX, _latestMapPointerY);
        }

        public void OnFrameTick(float dt)
        {
            // Campaign time is paused while this screen is open. Rebuilding all
            // portrait and roster VMs on a timer caused visible flicker and reset
            // panel scroll positions. Explicit successful mutations refresh once.
        }

        public override void OnFinalize()
        {
            _finalized = true;
            base.OnFinalize();
        }

        public void PointerPressed(float screenX, float screenY, float localX, float localY)
        {
            _pointerDown = true;
            _lastPointerX = screenX;
            _lastPointerY = screenY;
            UpdatePointerLocation(localX, localY);
            SelectMarkerAt(localX, localY);
        }

        public void UpdatePointerLocation(float localX, float localY)
        {
            _latestMapPointerX = localX;
            _latestMapPointerY = localY;
        }

        private void SelectMarkerAt(float localX, float localY)
        {
            ReignWarCouncilMarker marker = MarkerAt(localX, localY);
            bool selectable = marker?.IsPlayerRealm == true;
            if (selectable)
            {
                SelectLord(marker.HeroId);
                StatusText = marker.Roman + " • " + marker.HeroName + " selected in the lords roster.";
            }
            else if (marker != null)
            {
                StatusText = marker.HeroName + " — " + (marker.IsHostile ? "hostile" : "foreign") + " party sighting (intelligence only).";
            }
        }

        public void PointerMoved(float screenX, float screenY)
        {
            if (!_pointerDown) return;
            float deltaX = screenX - _lastPointerX;
            float deltaY = screenY - _lastPointerY;
            _lastPointerX = screenX;
            _lastPointerY = screenY;
            PanMap(deltaX, deltaY);
        }

        public void PointerReleased(float localX, float localY)
        {
            if (_pointerDown) SelectMarkerAt(localX, localY);
            _pointerDown = false;
        }

        public void PointerRightReleased(float localX, float localY)
        {
            ReignWarCouncilMarker marker = MarkerAt(localX, localY);
            if (marker?.IsPlayerRealm == true) SelectLord(marker.HeroId);
        }

        public void PointerScrolled(float delta, float localX, float localY)
        {
            StatusText = "Use click-and-drag to move across the close strategic map.";
        }

        public bool PanForAutomation(string value)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            if (parts.Length != 2
                || !float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float deltaX)
                || !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float deltaY))
                return false;
            float beforeX = MapOffsetX;
            float beforeY = MapOffsetY;
            PanMap(deltaX, deltaY);
            return Math.Abs(MapOffsetX - beforeX) > 0.01f || Math.Abs(MapOffsetY - beforeY) > 0.01f;
        }

        private void CenterMapOnWorld(float worldX, float worldY)
        {
            ReignWarCouncilPoint point = ReignWarCouncilRules.WorldToMap(worldX, worldY, MapWidth, MapHeight);
            MapOffsetX = ViewportWidth * 0.5f - (float)point.X;
            MapOffsetY = ViewportHeight * 0.5f - (float)point.Y;
            ClampMapOffsets();
        }

        private void PanMap(float deltaX, float deltaY)
        {
            MapOffsetX += deltaX;
            MapOffsetY += deltaY;
            ClampMapOffsets();
        }

        private void ClampMapOffsets()
        {
            MapOffsetX = Math.Max(ViewportWidth - MapWidth, Math.Min(0f, MapOffsetX));
            MapOffsetY = Math.Max(ViewportHeight - MapHeight, Math.Min(0f, MapOffsetY));
        }

        public bool SelectLord(string heroId)
        {
            ReignWarCouncilLordVM match = Lords.FirstOrDefault(x => string.Equals(x.HeroId, heroId, StringComparison.OrdinalIgnoreCase));
            if (match == null) return false;
            foreach (ReignWarCouncilLordVM row in Lords) row.IsSelected = row == match;
            SelectedLordIndex = Lords.IndexOf(match);
            StatusText = match.Roman + " • " + match.Name + " selected. Use the raven to send orders or other instructions.";
            return true;
        }

        public bool OpenMessengerForAutomation(string heroId)
        {
            ReignWarCouncilLordVM lord = Lords.FirstOrDefault(x => string.Equals(x.HeroId, heroId, StringComparison.OrdinalIgnoreCase));
            if (lord == null) return false;
            OpenMessenger(lord.Hero);
            return true;
        }

        public bool SetMessengerTextForAutomation(string text)
        {
            if (!MessengerVisible || MessengerBusy) return false;
            MessengerInputText = text;
            return !string.IsNullOrWhiteSpace(MessengerInputText);
        }

        public bool SendMessengerForAutomation()
        {
            if (!MessengerVisible || MessengerBusy || string.IsNullOrWhiteSpace(MessengerInputText)) return false;
            ExecuteSendMessenger();
            return true;
        }

        public bool MobilizeForAutomation(string heroId)
        {
            ReignWarCouncilLordVM lord = Lords.FirstOrDefault(x => string.Equals(x.HeroId, heroId, StringComparison.OrdinalIgnoreCase));
            return lord != null && Mobilize(lord.Hero);
        }

        internal bool CanOfferMobilization(Hero hero)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            bool authorizedRealm = hero?.Clan == Clan.PlayerClan
                || (playerKingdom != null && hero?.Clan?.Kingdom == playerKingdom);
            bool isInMainParty = hero?.PartyBelongedTo == MobileParty.MainParty;
            return authorizedRealm && hero.IsAlive && hero.IsActive && !hero.IsPrisoner
                && (hero.PartyBelongedTo == null || isInMainParty);
        }

        private void RefreshAll()
        {
            RefreshMarkersAndLords();
            RefreshCouncilor();
            RefreshKingdoms();
            RefreshReports();
        }

        private void RefreshMarkersAndLords()
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            ReignWarCouncilCampaignBehavior intelligence = ReignWarCouncilCampaignBehavior.Instance;
            intelligence?.RefreshForeignPartyIntelligence();
            List<MobileParty> parties = MobileParty.All.Where(x => x != null && x.IsActive
                    && x.LeaderHero != null && (x.IsMainParty || IsPlayerRealmParty(x, playerKingdom)
                        || (x.ActualClan?.Kingdom != null && x.ActualClan.Kingdom != playerKingdom
                            && intelligence?.IsForeignPartyDetected(x) == true)))
                .OrderByDescending(x => IsPlayerRealmParty(x, playerKingdom))
                .ThenBy(x => x.LeaderHero.Clan?.Name?.ToString()).ThenBy(x => x.LeaderHero.Name?.ToString())
                .ThenBy(x => x.StringId).ToList();
            List<ReignWarCouncilMarker> markers = new List<ReignWarCouncilMarker>();
            int playerNumber = 0;
            for (int i = 0; i < parties.Count; i++)
            {
                bool playerRealm = parties[i].IsMainParty || IsPlayerRealmParty(parties[i], playerKingdom);
                markers.Add(BuildMarker(parties[i], playerRealm ? ++playerNumber : 0, playerRealm));
            }
            string markerFingerprint = string.Join("|", markers.Select(x => x.PartyId + ":" + x.HeroId + ":"
                + x.WorldX.ToString("0.00", CultureInfo.InvariantCulture) + ":"
                + x.WorldY.ToString("0.00", CultureInfo.InvariantCulture) + ":" + x.ImageId + ":" + x.Roman));
            bool markerChanged = !string.Equals(_markerFingerprint, markerFingerprint, StringComparison.Ordinal);
            if (markerChanged)
            {
                Markers = markers;
                _markerFingerprint = markerFingerprint;
            }

            List<Hero> heroes = (playerKingdom?.Clans ?? Enumerable.Empty<Clan>())
                .SelectMany(x => x.Heroes).Where(IsAvailableKingdomLord)
                .OrderBy(x => x.Clan?.Name?.ToString()).ThenBy(x => x.Name?.ToString())
                .ThenBy(x => x.StringId).ToList();
            if (playerKingdom == null && Clan.PlayerClan != null)
                heroes = Clan.PlayerClan.Heroes.Where(IsAvailableKingdomLord).OrderBy(x => x.Name?.ToString())
                    .ThenBy(x => x.StringId).ToList();
            string selected = Lords.FirstOrDefault(x => x.IsSelected)?.HeroId;
            bool sameRoster = Lords.Count == heroes.Count && Lords.Select(x => x.HeroId)
                .SequenceEqual(heroes.Select(x => x.StringId), StringComparer.OrdinalIgnoreCase);
            if (sameRoster)
            {
                foreach (ReignWarCouncilLordVM row in Lords)
                {
                    ReignWarCouncilMarker marker = markers.FirstOrDefault(x => x.HeroId == row.HeroId);
                    row.Refresh(marker);
                }
            }
            else
            {
                Lords.Clear();
                foreach (Hero hero in heroes)
                {
                    ReignWarCouncilMarker marker = markers.FirstOrDefault(x => x.HeroId == hero.StringId);
                    ReignWarCouncilLordVM row = new ReignWarCouncilLordVM(this, hero, marker);
                    row.IsSelected = string.Equals(selected, hero.StringId, StringComparison.OrdinalIgnoreCase);
                    Lords.Add(row);
                }
                _portraitRevision++;
            }
            LordCount = Lords.Count;
            CouncilorCandidates.Clear();
            foreach (ReignWarCouncilLordVM lord in Lords)
                if (lord.CanServeAsCouncilor) CouncilorCandidates.Add(lord);
            List<ReignWarCouncilSettlementMarker> settlements = Settlement.All.Where(x => x != null
                    && (x.IsTown || x.IsCastle || x.IsVillage))
                .Select(x => new ReignWarCouncilSettlementMarker(this, x))
                .OrderBy(x => x.Name).ThenBy(x => x.TypeLabel).ThenBy(x => x.SettlementId).ToList();
            string settlementFingerprint = string.Join("|", settlements.Select(x => x.SettlementId + ":"
                + x.WorldX.ToString("0.00", CultureInfo.InvariantCulture) + ":"
                + x.WorldY.ToString("0.00", CultureInfo.InvariantCulture)));
            bool settlementsChanged = !string.Equals(_settlementFingerprint, settlementFingerprint, StringComparison.Ordinal);
            if (settlementsChanged)
            {
                Settlements.Clear();
                foreach (ReignWarCouncilSettlementMarker settlement in settlements) Settlements.Add(settlement);
                _settlementFingerprint = settlementFingerprint;
            }
            if (markerChanged) MarkerRevision++;
        }

        private void RefreshCouncilor()
        {
            ReignWarCouncilCampaignBehavior intelligence = ReignWarCouncilCampaignBehavior.Instance;
            string selectedId = intelligence?.SelectedCouncilorHeroId ?? string.Empty;
            Hero councilor = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == selectedId);
            Hero effective = councilor ?? Hero.MainHero;
            string heroId = effective?.StringId ?? string.Empty;
            if (!string.Equals(_councilorHeroId, heroId, StringComparison.OrdinalIgnoreCase))
            {
                _councilorHeroId = heroId;
                ImageIdentifierVM portrait = BuildPortrait(effective);
                CouncilorPortraitId = portrait?.Id ?? string.Empty;
                CouncilorPortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
                CouncilorPortraitProvider = portrait?.TextureProviderName ?? string.Empty;
                _portraitRevision++;
            }
            CouncilorName = councilor?.Name?.ToString() ?? "No Councilor Selected";
            CouncilorTitle = councilor == null
                ? "Using " + (Hero.MainHero?.Name?.ToString() ?? "the ruler") + " by default"
                : (councilor.Clan?.Name?.ToString() ?? "Independent War Council role");
            int tactics = effective?.GetSkillValue(DefaultSkills.Tactics) ?? 0;
            int leadership = effective?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
            string capitalName = "the capital";
            string capitalId = intelligence?.LastCapitalSettlementId ?? string.Empty;
            foreach (Settlement settlement in Settlement.All)
                if (string.Equals(settlement?.StringId, capitalId, StringComparison.OrdinalIgnoreCase))
                { capitalName = settlement.Name?.ToString() ?? capitalName; break; }
            CouncilorSkillText = "Tactics " + tactics + " • range "
                + ReignWarCouncilRules.ForeignPartyDetectionRange(tactics).ToString("0", CultureInfo.InvariantCulture)
                + "\nLeadership " + leadership + " • foreign chance "
                + (ReignWarCouncilRules.ForeignPartyDetectionChance(leadership) * 100d)
                    .ToString("0", CultureInfo.InvariantCulture) + "%\nFrom " + capitalName;
            foreach (ReignWarCouncilLordVM lord in Lords)
                lord.IsCouncilor = string.Equals(lord.HeroId, selectedId, StringComparison.OrdinalIgnoreCase);
        }

        private static ImageIdentifierVM BuildPortrait(Hero hero)
        {
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                return string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch { return null; }
        }

        private static bool IsAvailableKingdomLord(Hero hero)
        {
            return hero != null && hero != Hero.MainHero && hero.IsAlive && hero.IsActive
                && hero.CharacterObject?.Occupation == Occupation.Lord
                && TaleWorlds.CampaignSystem.Campaign.Current != null
                && hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge;
        }

        private static bool IsPlayerRealmParty(MobileParty party, Kingdom playerKingdom)
        {
            return party?.ActualClan == Clan.PlayerClan
                || (playerKingdom != null && party?.ActualClan?.Kingdom == playerKingdom);
        }

        private static ReignWarCouncilMarker BuildMarker(MobileParty party, int number, bool isPlayerRealm)
        {
            int infantry = 0, missile = 0, mounted = 0;
            foreach (TroopRosterElement troop in party.MemberRoster.GetTroopRoster())
            {
                if (troop.Character?.IsMounted == true) mounted += troop.Number;
                else if (troop.Character?.IsRanged == true) missile += troop.Number;
                else infantry += troop.Number;
            }
            ReignWarPartyComposition composition = ReignWarCouncilRules.ClassifyComposition(infantry, missile, mounted);
            bool isNaval = party.IsCurrentlyAtSea;
            string phase = isNaval ? "token_ship"
                : composition == ReignWarPartyComposition.Mounted ? "token_cavalry"
                : composition == ReignWarPartyComposition.Missile ? "token_archer" : "token_infantry";
            if (isPlayerRealm) phase += "_black";
            return new ReignWarCouncilMarker
            {
                PartyId = party.StringId, HeroId = party.LeaderHero.StringId,
                HeroName = party.LeaderHero.Name?.ToString() ?? party.StringId,
                Roman = isPlayerRealm ? ReignWarCouncilRules.ToRoman(number) : string.Empty, WorldX = party.Position.X, WorldY = party.Position.Y,
                Infantry = infantry, Missile = missile, Mounted = mounted,
                IsPlayerClan = party.ActualClan == Clan.PlayerClan, IsPlayerRealm = isPlayerRealm,
                IsHostile = !isPlayerRealm && MobileParty.MainParty?.MapFaction?.IsAtWarWith(party.MapFaction) == true,
                IsForeign = !isPlayerRealm, IsNaval = isNaval, Composition = composition,
                ImageId = ReignEventArtTextureFactory.BuildImageId("war_council", phase)
            };
        }

        private void RefreshKingdoms()
        {
            Kingdoms.Clear();
            foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated)
                .OrderByDescending(x => x.CurrentTotalStrength))
            {
                int parties = MobileParty.All.Count(x => x?.IsActive == true && x.ActualClan?.Kingdom == kingdom);
                Kingdoms.Add(new ReignWarCouncilKingdomVM(kingdom.Name?.ToString() ?? kingdom.StringId,
                    ((int)kingdom.CurrentTotalStrength).ToString("N0"), kingdom.Clans.Count,
                    parties, kingdom.Settlements.Count,
                    Clan.PlayerClan?.Kingdom == null ? "" : kingdom.IsAtWarWith(Clan.PlayerClan.Kingdom) ? "AT WAR" : "PEACE"));
            }
        }

        private void RefreshReports()
        {
            Reports.Clear();
            foreach (ReignWarCouncilBattleReport report in ReignWarCouncilCampaignBehavior.Instance?.RecentReports
                ?? Enumerable.Empty<ReignWarCouncilBattleReport>()) Reports.Add(new ReignWarCouncilReportVM(report));
        }

        internal void OpenMessenger(Hero hero)
        {
            if (hero == null) return;
            SelectLord(hero.StringId);
            _messengerRecipient = hero;
            MessengerRecipientName = hero.Name?.ToString() ?? hero.StringId;
            MessengerInputText = string.Empty;
            MessengerStatus = "Write a concise order or message. It will also appear in Correspondence.";
            MessengerVisible = true;
        }

        private async Task SendMessengerAsync()
        {
            Hero recipient = _messengerRecipient;
            string body = MessengerInputText?.Trim() ?? string.Empty;
            if (recipient == null || string.IsNullOrWhiteSpace(body) || MessengerBusy)
            {
                MessengerStatus = recipient == null ? "Select a lord first." : "Write a message before dispatching the raven.";
                return;
            }
            MessengerBusy = true;
            MessengerStatus = "Dispatching raven...";
            ReignLetterSendResult result = await ReignCorrespondenceScreenVM.SendProductionLetterAsync(
                recipient, body, "war-council-raven-" + Guid.NewGuid().ToString("N"));
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized) return;
                MessengerBusy = false;
                if (!result.Ok)
                {
                    MessengerStatus = "Could not send: " + result.Error;
                    return;
                }
                MessengerVisible = false;
                MessengerInputText = string.Empty;
                MessengerStatus = string.Empty;
                _messengerRecipient = null;
                StatusText = "Raven dispatched to " + (recipient.Name?.ToString() ?? recipient.StringId)
                    + ". The letter is recorded in Correspondence.";
            });
        }

        private ReignWarCouncilMarker MarkerAt(float localX, float localY)
        {
            return Markers.OrderBy(x => Math.Pow(MarkerMapX(x) - localX, 2) + Math.Pow(MarkerMapY(x) - localY, 2))
                .FirstOrDefault(x => Math.Pow(MarkerMapX(x) - localX, 2) + Math.Pow(MarkerMapY(x) - localY, 2) <= 60 * 60);
        }

        public float MarkerMapX(ReignWarCouncilMarker marker) => (float)ReignWarCouncilRules.WorldToMap(marker.WorldX, marker.WorldY, MapWidth, MapHeight).X;
        public float MarkerMapY(ReignWarCouncilMarker marker) => (float)ReignWarCouncilRules.WorldToMap(marker.WorldX, marker.WorldY, MapWidth, MapHeight).Y;

        internal bool Mobilize(Hero hero)
        {
            if (hero == null || !CanOfferMobilization(hero))
            { StatusText = "This kingdom lord is not currently eligible to lead a party."; return false; }
            bool isInMainParty = hero.PartyBelongedTo == MobileParty.MainParty;
            int stake = Math.Max(5000, TaleWorlds.CampaignSystem.Campaign.Current.Models.ClanFinanceModel.PartyGoldLowerThreshold);
            if (Hero.MainHero.Gold < stake)
            { StatusText = "Mobilization requires " + stake.ToString("N0") + " denars of recruiting funds."; return false; }
            Settlement spawn = hero.CurrentSettlement ?? Hero.MainHero.CurrentSettlement
                ?? hero.HomeSettlement ?? hero.Clan.Settlements.FirstOrDefault()
                ?? Clan.PlayerClan.Settlements.FirstOrDefault();
            if (spawn == null)
            { StatusText = "No safe clan settlement is available for mobilization."; return false; }
            if (isInMainParty) MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, -1);
            GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, hero, stake, true);
            MobileParty party = LordPartyComponent.CreateLordParty(hero.StringId + "_reign_warcouncil", hero,
                spawn.GatePosition, 2f, spawn, hero);
            party.SetMoveModeHold();
            Settlement recruit = Settlement.All.Where(x => x != null && !x.IsHideout && !x.IsUnderSiege
                    && x.MapFaction == party.MapFaction && (x.IsTown || x.IsCastle || x.IsVillage))
                .OrderBy(x => party.GetPosition2D.DistanceSquared(x.GetPosition2D)).FirstOrDefault();
            if (recruit != null)
            {
                ReignWorldActionRecord order = new ReignWorldActionRecord
                {
                    Type = ReignWorldActionType.RegularIssueCampaignOrder, Source = "war_council",
                    ActorHeroStringId = hero.StringId, TargetSettlementStringId = recruit.StringId,
                    AuthorizationMode = "direct_authority", RequiresAcceptance = false,
                    TermsJson = new JObject { ["objective"] = "recruit_resupply", ["source"] = "war_council",
                        ["minimumTroops"] = 40, ["minimumFoodDays"] = 3,
                        ["issuerHeroStringId"] = Hero.MainHero.StringId }.ToString()
                };
                ReignCampaignCommandBehavior.Instance?.ExecuteControlAction(order);
            }
            StatusText = hero.Name + " mobilized at " + spawn.Name + " with " + stake.ToString("N0") + " denars and began recruiting.";
            RefreshAll();
            return true;
        }

        private bool _councilorDropdownOpen;
        private string _lastCenteredSettlementId = string.Empty;

        public MBBindingList<ReignWarCouncilLordVM> CouncilorCandidates { get; }
        [DataSourceProperty] public string PanelFrameImageId { get; }
        [DataSourceProperty] public string TallPanelFrameImageId { get; }
        [DataSourceProperty] public string PanelFrameOverlayImageId { get; }
        [DataSourceProperty] public bool CouncilorDropdownOpen { get => _councilorDropdownOpen; private set { if (_councilorDropdownOpen != value) { _councilorDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        public string LastCenteredSettlementId => _lastCenteredSettlementId;

        public void ExecuteToggleCouncilorDropdown()
        {
            CouncilorDropdownOpen = !CouncilorDropdownOpen;
            StatusText = CouncilorDropdownOpen
                ? "Choose any available lord as War Councilor, or use the ruler's skills. This does not assign a court office."
                : "War Councilor selection closed.";
        }

        public void ExecuteUsePlayerAsCouncilor()
        {
            ReignWarCouncilCampaignBehavior.Instance?.UsePlayerSkills();
            CouncilorDropdownOpen = false;
            RefreshCouncilor();
            RefreshMarkersAndLords();
            StatusText = "No War Councilor is assigned. Intelligence now uses the ruler's Tactics and Leadership.";
        }

        public bool AssignCouncilorForAutomation(string heroId)
        {
            foreach (ReignWarCouncilLordVM lord in CouncilorCandidates)
                if (string.Equals(lord.HeroId, heroId, StringComparison.OrdinalIgnoreCase))
                    return AssignCouncilor(lord.Hero);
            return false;
        }

        public bool UsePlayerSkillsForAutomation()
        {
            ExecuteUsePlayerAsCouncilor();
            return string.IsNullOrWhiteSpace(ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId);
        }

        public bool CenterOnSettlementForAutomation(string settlementId)
        {
            foreach (ReignWarCouncilSettlementMarker settlement in Settlements)
                if (string.Equals(settlement.SettlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                    return CenterOnSettlement(settlement);
            return false;
        }

        internal bool AssignCouncilor(Hero hero)
        {
            ReignWarCouncilCampaignBehavior intelligence = ReignWarCouncilCampaignBehavior.Instance;
            if (hero == null || intelligence == null || !intelligence.SelectCouncilor(hero.StringId))
            {
                StatusText = "That lord is not currently available to serve as War Councilor.";
                return false;
            }
            CouncilorDropdownOpen = false;
            RefreshCouncilor();
            RefreshMarkersAndLords();
            StatusText = hero.Name + " now directs War Council intelligence without changing any other duty or office.";
            return true;
        }

        internal bool CenterOnSettlement(ReignWarCouncilSettlementMarker settlement)
        {
            if (settlement == null) return false;
            CenterMapOnWorld(settlement.WorldX, settlement.WorldY);
            _lastCenteredSettlementId = settlement.SettlementId;
            StatusText = settlement.TypeLabel + " • " + settlement.Name + " centered on the strategic map.";
            return true;
        }

    }

    public sealed class ReignWarCouncilLordVM : ViewModel
    {
        private readonly ReignWarCouncilScreenVM _owner;
        private bool _isSelected;
        private string _roman;
        private string _partyText;
        private bool _canMobilize;
        private bool _isFollowingOrders;
        private bool _hasWarCouncilOrder;
        private bool _isCouncilor;
        private string _statusText;
        public ReignWarCouncilLordVM(ReignWarCouncilScreenVM owner, Hero hero, ReignWarCouncilMarker marker)
        {
            _owner = owner; Hero = hero; HeroId = hero.StringId; Name = hero.Name?.ToString() ?? hero.StringId;
            ClanName = hero.Clan?.Name?.ToString() ?? "No clan";
            ImageIdentifierVM portrait = BuildPortrait(hero);
            PortraitId = portrait?.Id ?? string.Empty; PortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
            PortraitProvider = portrait?.TextureProviderName ?? string.Empty;
            RavenImageId = owner.RavenImageId;
            CouncilorDetail = "Tactics " + hero.GetSkillValue(DefaultSkills.Tactics)
                + " • Leadership " + hero.GetSkillValue(DefaultSkills.Leadership);
            Refresh(marker);
        }
        public void Refresh(ReignWarCouncilMarker marker)
        {
            Roman = marker?.Roman ?? "—";
            PartyText = marker == null ? "No active party" : (marker.Infantry + marker.Missile + marker.Mounted).ToString("N0")
                + " • I " + marker.Infantry + "  M " + marker.Missile + "  C " + marker.Mounted;
            CanMobilize = marker == null && _owner.CanOfferMobilization(Hero);
            ReignCampaignOrderRecord active = ReignCampaignCommandBehavior.Instance?.ActiveOrderFor(HeroId);
            JObject terms = Parse(active?.TermsJson);
            bool warCouncil = active != null && string.Equals(terms.Value<string>("source"), "war_council", StringComparison.OrdinalIgnoreCase);
            HasWarCouncilOrder = active != null && warCouncil;
            IsFollowingOrders = active != null && !warCouncil;
            string current = active == null ? "free-roam" : IsFollowingOrders ? "following-orders"
                : active.Objective == "hold_position" ? "hold" : active.Objective == "patrol" ? "patrol"
                : active.Objective == "raid" || active.Objective == "besiege_capture" ? "attack" : "following-orders";
            StatusText = Display(current);
        }
        public Hero Hero { get; }
        public string HeroId { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string ClanName { get; }
        [DataSourceProperty] public string Roman { get => _roman; private set { if (_roman != value) { _roman = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string PartyText { get => _partyText; private set { if (_partyText != value) { _partyText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string PortraitId { get; }
        [DataSourceProperty] public string PortraitArgs { get; }
        [DataSourceProperty] public string PortraitProvider { get; }
        [DataSourceProperty] public bool CanMobilize { get => _canMobilize; private set { if (_canMobilize != value) { _canMobilize = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string RavenImageId { get; }
        [DataSourceProperty] public string CouncilorDetail { get; }
        public bool CanServeAsCouncilor => Hero.IsAlive && Hero.IsActive && !Hero.IsPrisoner;
        [DataSourceProperty] public bool IsFollowingOrders { get => _isFollowingOrders; private set { if (_isFollowingOrders != value) { _isFollowingOrders = value; OnPropertyChangedWithValue(value); } } }
        public bool HasWarCouncilOrder { get => _hasWarCouncilOrder; private set => _hasWarCouncilOrder = value; }
        [DataSourceProperty] public bool IsCouncilor { get => _isCouncilor; set { if (_isCouncilor != value) { _isCouncilor = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; private set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }
        public void ExecuteSelect() => _owner.SelectLord(HeroId);
        public void ExecuteMobilize() => _owner.Mobilize(Hero);
        public void ExecuteOpenMessenger() => _owner.OpenMessenger(Hero);
        public void ExecuteAssignCouncilor() => _owner.AssignCouncilor(Hero);
        private static string Display(string value) => value == "following-orders" ? "Following Orders" : value == "free-roam" ? "Free Roam" : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value ?? string.Empty);
        private static JObject Parse(string json) { try { return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json); } catch { return new JObject(); } }
        private static ImageIdentifierVM BuildPortrait(Hero hero) { try { CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject); return string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code); } catch { return null; } }
    }

    public sealed class ReignWarCouncilMarker
    {
        public string PartyId, HeroId, HeroName, Roman, ImageId;
        public float WorldX, WorldY;
        public int Infantry, Missile, Mounted;
        public bool IsPlayerClan, IsPlayerRealm, IsForeign, IsHostile, IsNaval;
        public ReignWarPartyComposition Composition;
    }

    public sealed class ReignWarCouncilSettlementMarker : ViewModel
    {
        private readonly ReignWarCouncilScreenVM _owner;
        public ReignWarCouncilSettlementMarker(ReignWarCouncilScreenVM owner, Settlement settlement)
        {
            _owner = owner;
            SettlementId = settlement.StringId;
            Name = settlement.Name?.ToString() ?? settlement.StringId;
            WorldX = settlement.GetPosition2D.X;
            WorldY = settlement.GetPosition2D.Y;
            IsVillage = settlement.IsVillage;
            IsTown = settlement.IsTown;
            IsCastle = settlement.IsCastle;
            IsFortification = settlement.IsFortification;
            IsHostile = MobileParty.MainParty?.MapFaction?.IsAtWarWith(settlement.MapFaction) == true;
            IsFriendly = settlement.MapFaction == MobileParty.MainParty?.MapFaction;
            TypeLabel = IsTown ? "TOWN" : IsCastle ? "CASTLE" : IsVillage ? "VILLAGE" : "SETTLEMENT";
        }
        public string SettlementId { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string TypeLabel { get; }
        public float WorldX { get; }
        public float WorldY { get; }
        public bool IsVillage { get; }
        public bool IsTown { get; }
        public bool IsCastle { get; }
        public bool IsFortification { get; }
        public bool IsHostile { get; }
        public bool IsFriendly { get; }
        public void ExecuteSelect() => _owner.CenterOnSettlement(this);
    }

    public sealed class ReignWarCouncilKingdomVM : ViewModel
    {
        public ReignWarCouncilKingdomVM(string name, string strength, int clans, int parties, int settlements, string relation)
        { Name = name; Strength = strength; Detail = clans + " clans • " + parties + " parties • " + settlements + " settlements"; Relation = relation; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Strength { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public string Relation { get; }
    }

    public sealed class ReignWarCouncilReportVM : ViewModel
    {
        public ReignWarCouncilReportVM(ReignWarCouncilBattleReport report)
        {
            Title = (report.IsNaval ? "NAVAL • " : string.Empty) + report.Attacker + " VS " + report.Defender;
            Result = report.Winner + " won • " + report.AttackerInitial + "–" + report.DefenderInitial
                + " troops • losses " + report.AttackerLosses + "/" + report.DefenderLosses;
            Captures = string.IsNullOrWhiteSpace(report.CapturedLords) ? "No lord captured" : "Captured: " + report.CapturedLords;
            PlayerRealmInvolved = report.PlayerRealmInvolved;
        }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Result { get; }
        [DataSourceProperty] public string Captures { get; }
        [DataSourceProperty] public bool PlayerRealmInvolved { get; }
        [DataSourceProperty] public bool OrdinaryBattle { get => !PlayerRealmInvolved; }
    }
}
