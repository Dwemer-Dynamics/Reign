using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Save;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;

namespace ReignBeta.Campaign
{
    public sealed class ReignCombatReputationCampaignBehavior : CampaignBehaviorBase
    {
        private sealed class SideCommandSnapshot
        {
            public string CommanderId = string.Empty;
            public bool HasArmy;
        }

        private sealed class BattleCommandSnapshot
        {
            public string SourceKey = string.Empty;
            public SideCommandSnapshot Attacker = new SideCommandSnapshot();
            public SideCommandSnapshot Defender = new SideCommandSnapshot();
        }

        private sealed class SiegeCommandSnapshot
        {
            public string SourceKey = string.Empty;
            public string SettlementId = string.Empty;
            public string AttackerHeroId = string.Empty;
            public string AttackerKingdomId = string.Empty;
            public string OriginalOwnerKingdomId = string.Empty;
            public List<string> DefenderHeroIds = new List<string>();
        }

        private sealed class PendingEscapeCheck
        {
            public string HeroId = string.Empty;
            public string SourceKey = string.Empty;
            public float DueDay;
        }

        private readonly Dictionary<int, BattleCommandSnapshot> _battles = new Dictionary<int, BattleCommandSnapshot>();
        private readonly Dictionary<string, SiegeCommandSnapshot> _sieges =
            new Dictionary<string, SiegeCommandSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly List<PendingEscapeCheck> _pendingEscapes = new List<PendingEscapeCheck>();
        private string _persistedState = string.Empty;
        private List<string> _persistedStateChunks = new List<string>();

        public override void RegisterEvents()
        {
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEnded);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                _persistedState = JsonConvert.SerializeObject(new
                {
                    sieges = _sieges.Values,
                    pendingEscapes = _pendingEscapes
                });
                _persistedStateChunks = ReignSavePayloadCodec.Encode(_persistedState);
                _persistedState = string.Empty;
            }
            dataStore.SyncData("_reign_combatReputationState", ref _persistedState);
            dataStore.SyncData("_reign_combatReputationStateChunks", ref _persistedStateChunks);
            if (dataStore.IsLoading)
            {
                try
                {
                    if (_persistedStateChunks != null && _persistedStateChunks.Count > 0)
                        _persistedState = ReignSavePayloadCodec.Decode(_persistedStateChunks);
                }
                catch
                {
                    _persistedState = string.Empty;
                }
                RestoreState();
            }
        }

        private void RestoreState()
        {
            _sieges.Clear();
            _pendingEscapes.Clear();
            if (string.IsNullOrWhiteSpace(_persistedState)) return;
            try
            {
                JObject root = JObject.Parse(_persistedState);
                foreach (SiegeCommandSnapshot item in root["sieges"]?.ToObject<List<SiegeCommandSnapshot>>() ?? new List<SiegeCommandSnapshot>())
                    if (!string.IsNullOrWhiteSpace(item.SettlementId)) _sieges[item.SettlementId] = item;
                _pendingEscapes.AddRange(root["pendingEscapes"]?.ToObject<List<PendingEscapeCheck>>() ?? new List<PendingEscapeCheck>());
            }
            catch
            {
                _persistedState = string.Empty;
            }
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attacker, PartyBase defender)
        {
            if (mapEvent == null || !mapEvent.IsFieldBattle) return;
            string source = "combat_" + CurrentDay().ToString("0.0000") + "_" + mapEvent.GetHashCode();
            _battles[mapEvent.GetHashCode()] = new BattleCommandSnapshot
            {
                SourceKey = source,
                Attacker = CaptureCommand(mapEvent.AttackerSide),
                Defender = CaptureCommand(mapEvent.DefenderSide)
            };
        }

        private static SideCommandSnapshot CaptureCommand(MapEventSide side)
        {
            SideCommandSnapshot result = new SideCommandSnapshot();
            if (side == null) return result;
            List<Army> armies = side.Parties.Select(x => x?.Party?.MobileParty?.Army)
                .Where(x => x != null).Distinct().ToList();
            result.HasArmy = armies.Count > 0;
            Hero commander = result.HasArmy
                ? armies.FirstOrDefault(x => x.LeaderParty?.Party == side.LeaderParty)?.ArmyOwner ?? armies[0].ArmyOwner
                : side.LeaderParty?.LeaderHero;
            result.CommanderId = commander?.StringId ?? string.Empty;
            return result;
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null || !mapEvent.IsFieldBattle) return;
            if (!_battles.TryGetValue(mapEvent.GetHashCode(), out BattleCommandSnapshot snapshot))
            {
                snapshot = new BattleCommandSnapshot
                {
                    SourceKey = "combat_" + CurrentDay().ToString("0.0000") + "_" + mapEvent.GetHashCode(),
                    Attacker = CaptureCommand(mapEvent.AttackerSide),
                    Defender = CaptureCommand(mapEvent.DefenderSide)
                };
            }
            _battles.Remove(mapEvent.GetHashCode());
            bool anyArmy = snapshot.Attacker.HasArmy || snapshot.Defender.HasArmy;
            ProcessBattleSide(snapshot.SourceKey, "attacker", snapshot.Attacker,
                mapEvent.WinningSide == mapEvent.AttackerSide.MissionSide, anyArmy, mapEvent.EndedByRetreat);
            ProcessBattleSide(snapshot.SourceKey, "defender", snapshot.Defender,
                mapEvent.WinningSide == mapEvent.DefenderSide.MissionSide, anyArmy, mapEvent.EndedByRetreat);
        }

        private void ProcessBattleSide(string source, string side, SideCommandSnapshot command,
            bool won, bool anyArmy, bool endedByRetreat)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.CommanderId)) return;
            Hero hero = FindHero(command.CommanderId);
            if (hero == null) return;
            if (anyArmy)
            {
                if (!command.HasArmy) return;
                Record(won ? "tactician" : "failed_tactician", hero, "commander",
                    source + "|" + side + "|army",
                    won ? hero.Name + " commanded an army to victory." : hero.Name + " commanded an army in defeat.");
            }
            else
            {
                Record(won ? "strong_captain" : "weak_captain", hero, "commander",
                    source + "|" + side + "|captain",
                    won ? hero.Name + " led a war party to victory." : hero.Name + " led a war party in defeat.");
            }
            if (won) return;
            if (endedByRetreat)
            {
                Record("coward", hero, "commander", source + "|" + side + "|retreat",
                    hero.Name + " was defeated and escaped the field by retreat.");
                return;
            }
            _pendingEscapes.Add(new PendingEscapeCheck
            {
                HeroId = hero.StringId,
                SourceKey = source + "|" + side + "|uncaptured",
                DueDay = CurrentDay() + 0.04f
            });
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            float now = CurrentDay();
            foreach (PendingEscapeCheck check in _pendingEscapes.Where(x => x.DueDay <= now).ToList())
            {
                _pendingEscapes.Remove(check);
                Hero hero = FindHero(check.HeroId);
                if (hero == null || hero.IsDead || hero.IsPrisoner) continue;
                Record("coward", hero, "commander", check.SourceKey,
                    hero.Name + " was defeated but escaped capture.");
            }
        }

        private void OnTournamentFinished(CharacterObject winner,
            TaleWorlds.Library.MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            Hero winnerHero = winner?.HeroObject;
            string source = "tournament_" + (town?.StringId ?? "unknown") + "_" + CurrentDay().ToString("0.0000");
            if (winnerHero != null)
                Record("champion_of_the_pit", winnerHero, "winner", source + "|" + winnerHero.StringId,
                    winnerHero.Name + " won the tournament at " + (town?.Name?.ToString() ?? "an arena") + ".");
            List<Hero> heroes = (participants ?? new TaleWorlds.Library.MBReadOnlyList<CharacterObject>(new List<CharacterObject>()))
                .Where(x => x?.HeroObject != null).Select(x => x.HeroObject).Distinct().ToList();
            if (!heroes.Contains(Hero.MainHero)) return;
            foreach (Hero loser in heroes.Where(x => x != winnerHero))
                CounterOnly(loser, source + "|" + loser.StringId + "|loss",
                    loser.Name + " failed to win a tournament in which the player also competed.",
                    "champion_of_the_pit");
        }

        private void OnSiegeStarted(SiegeEvent siege)
        {
            Settlement settlement = siege?.BesiegedSettlement;
            Hero attacker = siege?.BesiegerCamp?.LeaderParty?.LeaderHero;
            if (settlement == null || attacker == null) return;
            _sieges[settlement.StringId] = new SiegeCommandSnapshot
            {
                SourceKey = "siege_" + settlement.StringId + "_" + CurrentDay().ToString("0.0000"),
                SettlementId = settlement.StringId,
                AttackerHeroId = attacker.StringId,
                AttackerKingdomId = attacker.MapFaction?.StringId ?? string.Empty,
                OriginalOwnerKingdomId = settlement.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                DefenderHeroIds = CaptureDefenderIds(settlement, attacker.MapFaction?.StringId)
            };
        }

        private void OnSiegeEnded(SiegeEvent siege)
        {
            Settlement settlement = siege?.BesiegedSettlement;
            if (settlement == null) return;
            if (!_sieges.TryGetValue(settlement.StringId, out SiegeCommandSnapshot snapshot))
            {
                Hero currentAttacker = siege?.BesiegerCamp?.LeaderParty?.LeaderHero;
                if (currentAttacker == null) return;
                snapshot = new SiegeCommandSnapshot
                {
                    SourceKey = "siege_" + settlement.StringId + "_" + CurrentDay().ToString("0.0000"),
                    SettlementId = settlement.StringId,
                    AttackerHeroId = currentAttacker.StringId,
                    AttackerKingdomId = currentAttacker.MapFaction?.StringId ?? string.Empty,
                    OriginalOwnerKingdomId = settlement.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                    DefenderHeroIds = CaptureDefenderIds(settlement, currentAttacker.MapFaction?.StringId)
                };
            }
            _sieges.Remove(settlement.StringId);
            Hero attacker = FindHero(snapshot.AttackerHeroId);
            bool captured = !string.IsNullOrWhiteSpace(snapshot.AttackerKingdomId)
                && string.Equals(settlement.OwnerClan?.Kingdom?.StringId, snapshot.AttackerKingdomId, StringComparison.OrdinalIgnoreCase);
            if (attacker != null)
                Record(captured ? "siege_commander" : "inept_besieger", attacker, "commander",
                    snapshot.SourceKey + "|besieger",
                    captured
                        ? attacker.Name + " brought the siege of " + settlement.Name + " to victory."
                        : attacker.Name + " failed to take " + settlement.Name + " before the siege ended.");
            if (captured)
            {
                foreach (Hero defender in (snapshot.DefenderHeroIds ?? new List<string>())
                    .Select(FindHero).Where(x => x != null).Distinct())
                    CounterOnly(defender, snapshot.SourceKey + "|defender_loss|" + defender.StringId,
                        defender.Name + " failed to break the siege of " + settlement.Name + ".", "siege_breaker");
                return;
            }
            IEnumerable<Hero> defenders = settlement.Parties
                .Where(x => x?.LeaderHero != null && x.LeaderHero.IsLord
                    && !string.Equals(x.MapFaction?.StringId, snapshot.AttackerKingdomId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.LeaderHero).Distinct();
            foreach (Hero defender in defenders)
                Record("siege_breaker", defender, "defender", snapshot.SourceKey + "|defender|" + defender.StringId,
                    defender.Name + " stood among the commanders who broke the siege of " + settlement.Name + ".");
        }

        private static List<string> CaptureDefenderIds(Settlement settlement, string attackerKingdomId)
        {
            return settlement?.Parties
                .Where(x => x?.LeaderHero != null && x.LeaderHero.IsLord
                    && !string.Equals(x.MapFaction?.StringId, attackerKingdomId, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.LeaderHero.StringId).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null
                : Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static float CurrentDay()
        {
            return (float)CampaignTime.Now.ToDays;
        }

        private static void Record(string archetype, Hero subject, string role, string source, string summary)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(archetype, subject, role, source, summary);
        }

        private static void CounterOnly(Hero subject, string source, string summary, params string[] tagIds)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordSocialOutcome(string.Empty, subject, "subject", source,
                summary, null, new JArray(tagIds));
        }
    }
}
