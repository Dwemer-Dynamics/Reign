using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using ReignBeta.Integration;
using ReignBeta.Save;
using ReignBeta.Shared.WarCouncil;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court.WarCouncil
{
    public sealed class ReignWarCouncilBattleReport
    {
        public string ReportId = Guid.NewGuid().ToString("N");
        public double Day;
        public string BattleType = string.Empty;
        public bool IsNaval;
        public string Winner = string.Empty;
        public string Attacker = string.Empty;
        public string Defender = string.Empty;
        public int AttackerInitial;
        public int DefenderInitial;
        public int AttackerLosses;
        public int DefenderLosses;
        public string CapturedLords = string.Empty;
        public string Settlement = string.Empty;
        public bool PlayerRealmInvolved;
    }

    public sealed class ReignWarCouncilIntelligenceState
    {
        public int ContractVersion = 2;
        public double NextSweepDay;
        public int SweepIndex;
        public string CouncilorHeroId = string.Empty;
        public string EffectiveCouncilorHeroId = string.Empty;
        public int CouncilorTactics;
        public int CouncilorLeadership;
        public double DetectionRange;
        public string CapitalSettlementId = string.Empty;
        public List<string> DetectedPartyIds = new List<string>();
    }

    /// <summary>Save-backed, bounded worldwide battle ledger used by the War Council.</summary>
    public sealed class ReignWarCouncilCampaignBehavior : CampaignBehaviorBase
    {
        private List<ReignWarCouncilBattleReport> _reports = new List<ReignWarCouncilBattleReport>();
        private List<string> _chunks = new List<string>();
        private ReignWarCouncilIntelligenceState _intelligence = new ReignWarCouncilIntelligenceState();
        private List<string> _intelligenceChunks = new List<string>();
        public static ReignWarCouncilCampaignBehavior Instance { get; private set; }

        public IReadOnlyList<ReignWarCouncilBattleReport> RecentReports
        {
            get
            {
                Prune();
                return _reports.OrderByDescending(x => x.Day).ToList();
            }
        }

        public int DetectedForeignPartyCount => _intelligence?.DetectedPartyIds?.Count ?? 0;
        public int LastCouncilorTactics => _intelligence?.CouncilorTactics ?? 0;
        public double NextIntelligenceSweepDay => _intelligence?.NextSweepDay ?? 0d;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                Prune();
                _chunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_reports));
            }
            dataStore.SyncData("_reign_warCouncilBattleReports", ref _chunks);
            if (dataStore.IsSaving)
                _intelligenceChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(_intelligence));
            dataStore.SyncData("_reign_warCouncilIntelligence", ref _intelligenceChunks);
            if (dataStore.IsLoading)
            {
                try
                {
                    string json = _chunks == null || _chunks.Count == 0
                        ? string.Empty : ReignSavePayloadCodec.Decode(_chunks);
                    _reports = string.IsNullOrWhiteSpace(json)
                        ? new List<ReignWarCouncilBattleReport>()
                        : JsonConvert.DeserializeObject<List<ReignWarCouncilBattleReport>>(json)
                            ?? new List<ReignWarCouncilBattleReport>();
                    Prune();
                    string intelligenceJson = _intelligenceChunks == null || _intelligenceChunks.Count == 0
                        ? string.Empty : ReignSavePayloadCodec.Decode(_intelligenceChunks);
                    _intelligence = string.IsNullOrWhiteSpace(intelligenceJson)
                        ? new ReignWarCouncilIntelligenceState()
                        : JsonConvert.DeserializeObject<ReignWarCouncilIntelligenceState>(intelligenceJson)
                            ?? new ReignWarCouncilIntelligenceState();
                    // Contract v1 mirrored the active Marshal into CouncilorHeroId on every
                    // sweep. The independent War Councilor role begins at v2, so old values
                    // must not silently become a player choice after upgrade.
                    if (_intelligence.ContractVersion < 2)
                    {
                        _intelligence.CouncilorHeroId = string.Empty;
                        _intelligence.ContractVersion = 2;
                    }
                    _intelligence.DetectedPartyIds = (_intelligence.DetectedPartyIds ?? new List<string>())
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch (Exception ex)
                {
                    _reports = new List<ReignWarCouncilBattleReport>();
                    _intelligence = new ReignWarCouncilIntelligenceState();
                    ReignLog.Warn("War Council battle ledger could not be restored: " + ex.Message);
                }
            }
        }

        public bool IsForeignPartyDetected(MobileParty party)
        {
            RefreshForeignPartyIntelligence();
            return party != null && (_intelligence?.DetectedPartyIds?.Contains(
                party.StringId, StringComparer.OrdinalIgnoreCase) ?? false);
        }

        public void RefreshForeignPartyIntelligence(bool force = false)
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            EnsureIntelligenceState();
            double now = CampaignTime.Now.ToDays;
            if (!force && _intelligence != null && _intelligence.SweepIndex > 0 && now < _intelligence.NextSweepDay)
                return;

            Hero selected = FindHero(_intelligence.CouncilorHeroId);
            if (!IsEligibleCouncilor(selected))
            {
                selected = null;
                _intelligence.CouncilorHeroId = string.Empty;
            }
            Hero effective = selected ?? Hero.MainHero;
            int tactics = effective?.GetSkillValue(DefaultSkills.Tactics) ?? 0;
            int leadership = effective?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
            int sweep = Math.Max(0, _intelligence?.SweepIndex ?? 0) + 1;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            Settlement capital = ResolveCapital();
            float capitalX = capital?.GetPosition2D.X ?? MobileParty.MainParty?.Position.X ?? 0f;
            float capitalY = capital?.GetPosition2D.Y ?? MobileParty.MainParty?.Position.Y ?? 0f;
            List<string> detected = new List<string>();
            foreach (MobileParty party in MobileParty.All.Where(x => IsForeignKingdomParty(x, playerKingdom)))
            {
                if (ReignWarCouncilRules.DetectForeignParty(party.StringId, sweep, tactics, leadership,
                    capitalX, capitalY, party.Position.X, party.Position.Y)) detected.Add(party.StringId);
            }
            _intelligence = new ReignWarCouncilIntelligenceState
            {
                ContractVersion = 2,
                NextSweepDay = now + ReignWarCouncilRules.IntelligenceSweepIntervalDays,
                SweepIndex = sweep,
                CouncilorHeroId = selected?.StringId ?? string.Empty,
                EffectiveCouncilorHeroId = effective?.StringId ?? string.Empty,
                CouncilorTactics = tactics,
                CouncilorLeadership = leadership,
                DetectionRange = ReignWarCouncilRules.ForeignPartyDetectionRange(tactics),
                CapitalSettlementId = capital?.StringId ?? string.Empty,
                DetectedPartyIds = detected.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            };
        }

        private void OnDailyTick() => RefreshForeignPartyIntelligence();

        private static bool IsForeignKingdomParty(MobileParty party, Kingdom playerKingdom)
        {
            Kingdom kingdom = party?.ActualClan?.Kingdom;
            return party != null && party.IsActive && !party.IsMainParty && party.LeaderHero != null
                && kingdom != null && kingdom != playerKingdom;
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || mapEvent.AttackerSide == null || mapEvent.DefenderSide == null) return;
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            bool playerRealmInvolved = SideContainsPlayerRealm(mapEvent.AttackerSide, playerKingdom)
                || SideContainsPlayerRealm(mapEvent.DefenderSide, playerKingdom);
            string captured = string.Join(", ", mapEvent.AttackerSide.Parties
                .Concat(mapEvent.DefenderSide.Parties)
                .Where(x => x != null)
                .SelectMany(x => x.RosterToReceiveLootPrisoners.GetTroopRoster())
                .Where(x => x.Character?.IsHero == true)
                .Select(x => x.Character.HeroObject?.Name?.ToString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct());
            _reports.Add(new ReignWarCouncilBattleReport
            {
                Day = CampaignTime.Now.ToDays,
                BattleType = mapEvent.EventType.ToString(),
                IsNaval = mapEvent.IsNavalMapEvent,
                Winner = mapEvent.Winner?.LeaderParty?.Name?.ToString() ?? mapEvent.WinningSide.ToString(),
                Attacker = mapEvent.AttackerSide.LeaderParty?.Name?.ToString() ?? "Unknown attackers",
                Defender = mapEvent.DefenderSide.LeaderParty?.Name?.ToString() ?? "Unknown defenders",
                AttackerInitial = mapEvent.AttackerSide.HealthyTroopCountAtMapEventStart,
                DefenderInitial = mapEvent.DefenderSide.HealthyTroopCountAtMapEventStart,
                AttackerLosses = mapEvent.AttackerSide.TroopCasualties,
                DefenderLosses = mapEvent.DefenderSide.TroopCasualties,
                CapturedLords = captured,
                Settlement = mapEvent.MapEventSettlement?.Name?.ToString() ?? string.Empty,
                PlayerRealmInvolved = playerRealmInvolved
            });
            Prune();
        }

        private void Prune()
        {
            double now = TaleWorlds.CampaignSystem.Campaign.Current == null ? double.MaxValue : CampaignTime.Now.ToDays;
            _reports = _reports.Where(x => x != null)
                .OrderByDescending(x => x.Day)
                .Where((x, index) => ReignWarCouncilRules.ShouldRetainReport(x.Day, now, index))
                .ToList();
        }

        public string SelectedCouncilorHeroId => _intelligence?.CouncilorHeroId ?? string.Empty;
        public string EffectiveCouncilorHeroId => _intelligence?.EffectiveCouncilorHeroId ?? string.Empty;
        public int LastCouncilorLeadership => _intelligence?.CouncilorLeadership ?? 0;
        public double LastDetectionRange => _intelligence?.DetectionRange ?? 0d;
        public string LastCapitalSettlementId => _intelligence?.CapitalSettlementId ?? string.Empty;

        public bool SelectCouncilor(string heroId)
        {
            Hero hero = FindHero(heroId);
            if (!IsEligibleCouncilor(hero)) return false;
            EnsureIntelligenceState();
            _intelligence.CouncilorHeroId = hero.StringId;
            RefreshForeignPartyIntelligence(true);
            return true;
        }

        public void UsePlayerSkills()
        {
            EnsureIntelligenceState();
            _intelligence.CouncilorHeroId = string.Empty;
            RefreshForeignPartyIntelligence(true);
        }

        private static bool IsEligibleCouncilor(Hero hero)
        {
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            bool playerRealm = hero?.Clan == Clan.PlayerClan
                || (playerKingdom != null && hero?.Clan?.Kingdom == playerKingdom);
            bool holdsCouncilOffice = hero != null && (ReignCourtCampaignBehavior.Instance?.Offices?.Any(assignment => assignment.IsActive
                && string.Equals(assignment.HeroStringId, hero.StringId, StringComparison.OrdinalIgnoreCase)) ?? false);
            return hero != null && hero != Hero.MainHero && !holdsCouncilOffice && playerRealm && hero.IsAlive && hero.IsActive
                && !hero.IsPrisoner && hero.CharacterObject?.Occupation == Occupation.Lord
                && TaleWorlds.CampaignSystem.Campaign.Current != null
                && hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge;
        }

        private static Hero FindHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId)) return null;
            foreach (Hero hero in Hero.AllAliveHeroes)
                if (string.Equals(hero?.StringId, heroId, StringComparison.OrdinalIgnoreCase)) return hero;
            return null;
        }

        private static Settlement ResolveCapital()
        {
            Settlement capital = ReignCourtCampaignBehavior.Instance?.CurrentCapital;
            if (capital != null) return capital;
            if (Clan.PlayerClan != null)
            {
                foreach (Settlement settlement in Clan.PlayerClan.Settlements)
                    if (settlement?.IsTown == true) return settlement;
                foreach (Settlement settlement in Clan.PlayerClan.Settlements)
                    if (settlement?.IsCastle == true) return settlement;
            }
            return Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
        }

        private void EnsureIntelligenceState()
        {
            if (_intelligence == null) _intelligence = new ReignWarCouncilIntelligenceState();
            if (_intelligence.DetectedPartyIds == null) _intelligence.DetectedPartyIds = new List<string>();
            _intelligence.ContractVersion = 2;
        }

        private static bool SideContainsPlayerRealm(MapEventSide side, Kingdom playerKingdom)
        {
            if (side == null) return false;
            foreach (MapEventParty battleParty in side.Parties)
            {
                PartyBase party = battleParty?.Party;
                if (party == PartyBase.MainParty) return true;
                Clan partyClan = party?.MobileParty?.ActualClan ?? party?.LeaderHero?.Clan;
                if (partyClan == Clan.PlayerClan || (playerKingdom != null && partyClan?.Kingdom == playerKingdom)) return true;
                if (party?.MemberRoster == null) continue;
                foreach (TroopRosterElement troop in party.MemberRoster.GetTroopRoster())
                {
                    Hero hero = troop.Character?.HeroObject;
                    Clan heroClan = hero?.Clan;
                    if (heroClan == Clan.PlayerClan || (playerKingdom != null && heroClan?.Kingdom == playerKingdom)) return true;
                }
            }
            return false;
        }
    }
}
