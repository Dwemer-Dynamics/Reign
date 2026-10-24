using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ArenaOverhaul.TeamTournament;
using ReignBeta.Integration;
using ReignBeta.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using SandBox.Missions.MissionLogics.Arena;

namespace ReignBeta.Campaign
{
    public sealed class ReignWorldHistoryCampaignBehavior : CampaignBehaviorBase
    {
        private sealed class BattleSnapshot
        {
            public string CorrelationId;
            public JArray StartEntities = new JArray();
            public readonly HashSet<string> MissionHeroes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, List<Hero>> PrisonerHeroesByParty = new Dictionary<string, List<Hero>>(StringComparer.OrdinalIgnoreCase);
        }

        private string _timelineId = "main";
        private string _headEventId = string.Empty;
        private long _sequence;
        private float _historyCompleteFromDay;
        private bool _timelineReady;
        private readonly List<JObject> _deferred = new List<JObject>();
        private readonly Dictionary<int, BattleSnapshot> _battleSnapshots = new Dictionary<int, BattleSnapshot>();
        private readonly Dictionary<string, string> _reconciliationState = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _observedEntities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _recentEventKeys = new Dictionary<string, float>(StringComparer.Ordinal);
        private bool _reconciliationInitialized;
        private double _contactSampleDay;
        private string _contactPartyId = string.Empty;
        private string _contactRoster = string.Empty;
        private double _seaTravelStartedDay;
        private double _waitingStartedDay;
        private string _waitingRoster = string.Empty;

        public static ReignWorldHistoryCampaignBehavior Instance { get; private set; }
        public string TimelineId => _timelineId;
        public long Sequence => _sequence;
        public string HeadEventId => _headEventId;
        public float HistoryCompleteFromDay => _historyCompleteFromDay;
        internal bool TimelineReady => _timelineReady;
        internal string SharedActivityGeneration { get; private set; } = string.Empty;

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.OnHeroJoinedPartyEvent.AddNonSerializedListener(this, OnSharedHeroJoinedParty);
            CampaignEvents.OnPartySizeChangedEvent.AddNonSerializedListener(this, OnSharedPartySizeChanged);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnSharedCompanionAdded);
            CampaignEvents.CompanionRemoved.AddNonSerializedListener(this, OnSharedCompanionRemoved);
            CampaignEvents.OnNewItemCraftedEvent.AddNonSerializedListener(this, OnSharedItemCrafted);
            CampaignEvents.HeroOrPartyGaveItem.AddNonSerializedListener(this, OnSharedItemGiven);
            CampaignEvents.OnMobilePartyNavigationStateChangedEvent.AddNonSerializedListener(this, OnSharedNavigationChanged);
            CampaignEvents.OnBeforeSaveEvent.AddNonSerializedListener(this, OnBeforeSave);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
            CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompleted);
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaided);
            CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
            CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnWarDeclared);
            CampaignEvents.MakePeace.AddNonSerializedListener(this, OnMakePeace);
            CampaignEvents.OnAllianceStartedEvent.AddNonSerializedListener(this, OnAllianceStarted);
            CampaignEvents.OnAllianceEndedEvent.AddNonSerializedListener(this, OnAllianceEnded);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
            CampaignEvents.OnGovernorChangedEvent.AddNonSerializedListener(this, OnGovernorChanged);
            CampaignEvents.HeroPrisonerTaken.AddNonSerializedListener(this, OnHeroPrisonerTaken);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleased);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.HeroWounded.AddNonSerializedListener(this, OnHeroWounded);
            CampaignEvents.BeforeHeroesMarried.AddNonSerializedListener(this, OnBeforeHeroesMarried);
            CampaignEvents.OnChildConceivedEvent.AddNonSerializedListener(this, OnChildConceived);
            CampaignEvents.OnGivenBirthEvent.AddNonSerializedListener(this, OnGivenBirth);
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this, OnClanChangedKingdom);
            CampaignEvents.OnClanDefectedEvent.AddNonSerializedListener(this, OnClanDefected);
            CampaignEvents.OnClanCreatedEvent.AddNonSerializedListener(this, OnClanCreated);
            CampaignEvents.OnClanDestroyedEvent.AddNonSerializedListener(this, OnClanDestroyed);
            CampaignEvents.ArmyCreated.AddNonSerializedListener(this, OnArmyCreated);
            CampaignEvents.ArmyDispersed.AddNonSerializedListener(this, OnArmyDispersed);
            CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.OnPartyLeaderChangedEvent.AddNonSerializedListener(this, OnPartyLeaderChanged);
            CampaignEvents.TournamentStarted.AddNonSerializedListener(this, OnTournamentStarted);
            CampaignEvents.TournamentFinished.AddNonSerializedListener(this, OnTournamentFinished);
            CampaignEvents.OnSiegeEventStartedEvent.AddNonSerializedListener(this, OnSiegeStarted);
            CampaignEvents.OnSiegeEventEndedEvent.AddNonSerializedListener(this, OnSiegeEnded);
            CampaignEvents.SiegeCompletedEvent.AddNonSerializedListener(this, OnSiegeCompleted);
            CampaignEvents.WorkshopOwnerChangedEvent.AddNonSerializedListener(this, OnWorkshopOwnerChanged);
            CampaignEvents.OnTroopRecruitedEvent.AddNonSerializedListener(this, OnTroopRecruited);
            CampaignEvents.OnItemSoldEvent.AddNonSerializedListener(this, OnItemSold);
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
            CampaignEvents.OnSettlementLeftEvent.AddNonSerializedListener(this, OnSettlementLeft);
            CampaignEvents.OnShipDestroyedEvent.AddNonSerializedListener(this, OnShipDestroyed);
            CampaignEvents.OnShipOwnerChangedEvent.AddNonSerializedListener(this, OnShipOwnerChanged);
            CampaignEvents.OnShipCreatedEvent.AddNonSerializedListener(this, OnShipCreated);
            CampaignEvents.OnShipRepairedEvent.AddNonSerializedListener(this, OnShipRepaired);
            CampaignEvents.OnBarterAcceptedEvent.AddNonSerializedListener(this, OnBarterAccepted);
            CampaignEvents.HeroRelationChanged.AddNonSerializedListener(this, OnHeroRelationChanged);
            CampaignEvents.CharacterDefeated.AddNonSerializedListener(this, OnCharacterDefeated);
            CampaignEvents.RulingClanChanged.AddNonSerializedListener(this, OnRulingClanChanged);
            CampaignEvents.KingdomCreatedEvent.AddNonSerializedListener(this, OnKingdomCreated);
            CampaignEvents.KingdomDestroyedEvent.AddNonSerializedListener(this, OnKingdomDestroyed);
            CampaignEvents.KingdomDecisionAdded.AddNonSerializedListener(this, OnKingdomDecisionAdded);
            CampaignEvents.KingdomDecisionConcluded.AddNonSerializedListener(this, OnKingdomDecisionConcluded);
            CampaignEvents.RebellionFinished.AddNonSerializedListener(this, OnRebellionFinished);
            CampaignEvents.TownRebelliosStateChanged.AddNonSerializedListener(this, OnTownRebelliousStateChanged);
            CampaignEvents.ArmyGathered.AddNonSerializedListener(this, OnArmyGathered);
            CampaignEvents.OnPartyJoinedArmyEvent.AddNonSerializedListener(this, OnPartyJoinedArmy);
            CampaignEvents.PartyRemovedFromArmyEvent.AddNonSerializedListener(this, OnPartyRemovedFromArmy);
            CampaignEvents.OnMercenaryServiceStartedEvent.AddNonSerializedListener(this, OnMercenaryServiceStarted);
            CampaignEvents.OnMercenaryServiceEndedEvent.AddNonSerializedListener(this, OnMercenaryServiceEnded);
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);
            CampaignEvents.ItemsLooted.AddNonSerializedListener(this, OnItemsLooted);
            CampaignEvents.OnLootDistributedToPartyEvent.AddNonSerializedListener(this, OnLootDistributed);
            CampaignEvents.OnCaravanTransactionCompletedEvent.AddNonSerializedListener(this, OnCaravanTransaction);
            CampaignEvents.OnPrisonerSoldEvent.AddNonSerializedListener(this, OnPrisonerSold);
            CampaignEvents.OnTroopsDesertedEvent.AddNonSerializedListener(this, OnTroopsDeserted);
            CampaignEvents.OnTroopGivenToSettlementEvent.AddNonSerializedListener(this, OnTroopGivenToSettlement);
            CampaignEvents.OnBuildingLevelChangedEvent.AddNonSerializedListener(this, OnBuildingLevelChanged);
            CampaignEvents.OnQuestStartedEvent.AddNonSerializedListener(this, OnQuestStarted);
            CampaignEvents.OnQuestCompletedEvent.AddNonSerializedListener(this, OnQuestCompleted);
            CampaignEvents.OnNewIssueCreatedEvent.AddNonSerializedListener(this, OnNewIssueCreated);
            CampaignEvents.OnIssueUpdatedEvent.AddNonSerializedListener(this, OnIssueUpdated);
            CampaignEvents.WorkshopTypeChangedEvent.AddNonSerializedListener(this, OnWorkshopTypeChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_reign_worldHistoryTimelineId", ref _timelineId);
            dataStore.SyncData("_reign_worldHistoryHeadEventId", ref _headEventId);
            dataStore.SyncData("_reign_worldHistorySequence", ref _sequence);
            dataStore.SyncData("_reign_worldHistoryCompleteFromDay", ref _historyCompleteFromDay);
            dataStore.SyncData("_reign_contactSampleDay", ref _contactSampleDay);
            dataStore.SyncData("_reign_contactPartyId", ref _contactPartyId);
            dataStore.SyncData("_reign_contactRoster", ref _contactRoster);
            dataStore.SyncData("_reign_seaTravelStartedDay", ref _seaTravelStartedDay);
            dataStore.SyncData("_reign_waitingStartedDay", ref _waitingStartedDay);
            dataStore.SyncData("_reign_waitingRoster", ref _waitingRoster);
            if (string.IsNullOrWhiteSpace(_timelineId)) _timelineId = "main_" + Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            if (_historyCompleteFromDay <= 0f) _historyCompleteFromDay = CurrentDay();
            if (string.IsNullOrWhiteSpace(_timelineId) || _timelineId == "main") _timelineId = "main_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            _timelineReady = false;
            ReignWorldHistoryTransport.Initialize(TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId ?? "default", _timelineId, _historyCompleteFromDay);
            ReignWorldHistoryCoverage.ValidateCurrentGameSurface();
        }

        internal async Task<JObject> EnsureTimelineReadyAsync(string generationId)
        {
            if (_timelineReady)
            {
                return new JObject
                {
                    ["ok"] = true,
                    ["timelineId"] = _timelineId,
                    ["historyCompleteFromWorldDay"] = _historyCompleteFromDay
                };
            }

            JObject response = await ReignServerClient.OpenWorldHistoryTimelineAsync(
                _timelineId,
                _sequence,
                _headEventId,
                _historyCompleteFromDay).ConfigureAwait(false);
            if (!ReignCampaignInitializationGate.IsActiveGeneration(generationId))
                throw new InvalidOperationException(
                    "The world-history response belongs to a stale campaign generation.");
            if (response?.Value<bool?>("ok") != true)
                throw new InvalidOperationException(
                    response?.Value<string>("error") ?? "World-history timeline handshake was rejected.");

            await ReignMainThread.InvokeAsync(() =>
            {
                if (!ReignCampaignInitializationGate.IsActiveGeneration(generationId))
                    throw new InvalidOperationException(
                        "The world-history response belongs to a stale campaign generation.");
                string opened = response.Value<string>("timelineId");
                if (string.IsNullOrWhiteSpace(opened))
                    throw new InvalidOperationException("World-history timeline response omitted its timeline id.");
                _timelineId = opened;
                SharedActivityGeneration = response.Value<string>("sharedActivityGeneration") ?? string.Empty;
                float serverCompleteFrom =
                    response.Value<float?>("historyCompleteFromWorldDay") ?? _historyCompleteFromDay;
                if (serverCompleteFrom > 0f) _historyCompleteFromDay = serverCompleteFrom;
                _timelineReady = true;
                ReignWorldHistoryTransport.Initialize(
                    TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId ?? "default",
                    _timelineId,
                    _historyCompleteFromDay);
                foreach (JObject pending in _deferred.ToList())
                {
                    ReignWorldHistoryTransport.Enqueue(pending);
                }
                _deferred.Clear();
                CaptureReconciliationBaseline();
                CaptureSharedPartyContact();
            }).ConfigureAwait(false);
            return response;
        }

        private void OnBeforeSave()
        {
            ReignWorldHistoryTransport.FlushAll();
        }

        private void OnMapEventStarted(MapEvent mapEvent, PartyBase attacker, PartyBase defender)
        {
            if (mapEvent == null) return;
            CaptureSharedPartyContact();
            string correlation = BattleCorrelation(mapEvent);
            BattleSnapshot snapshot = new BattleSnapshot { CorrelationId = correlation };
            foreach (PartyBase party in mapEvent.InvolvedParties ?? Enumerable.Empty<PartyBase>())
            {
                snapshot.PrisonerHeroesByParty[PartyId(party)] = HeroPrisoners(party).ToList();
            }
            _battleSnapshots[mapEvent.GetHashCode()] = snapshot;
            JArray entities = BuildMapEventEntities(mapEvent, false);
            snapshot.StartEntities = (JArray)entities.DeepClone();
            Emit("battle_started", "started", DescribeMapEvent(mapEvent, "started"), correlation, mapEvent.MapEventSettlement, entities, true, "ordinary",
                new JObject { ["battleType"] = mapEvent.EventType.ToString(), ["positionX"] = mapEvent.Position.X, ["positionY"] = mapEvent.Position.Y });
        }

        private void OnMapEventEnded(MapEvent mapEvent)
        {
            if (mapEvent == null) return;
            _battleSnapshots.TryGetValue(mapEvent.GetHashCode(), out BattleSnapshot snapshot);
            string correlation = snapshot?.CorrelationId ?? BattleCorrelation(mapEvent);
            JArray entities = BuildMapEventEntities(mapEvent, true);
            if (snapshot != null)
            {
                foreach (JObject original in snapshot.StartEntities.OfType<JObject>())
                {
                    string id = original.Value<string>("entityId");
                    if (!entities.OfType<JObject>().Any(e => e.Value<string>("entityId") == id))
                        entities.Add(original.DeepClone());
                }
                foreach (JObject entity in entities.OfType<JObject>())
                    if (snapshot.MissionHeroes.Contains(entity.Value<string>("entityId") ?? string.Empty))
                        entity["missionParticipant"] = true;
            }
            AddRescueEvidence(mapEvent, snapshot, entities);
            Emit("battle_completed", "completed", DescribeMapEvent(mapEvent, "completed"), correlation, mapEvent.MapEventSettlement, entities, true, "ordinary",
                new JObject
                {
                    ["battleType"] = mapEvent.EventType.ToString(), ["winningSide"] = mapEvent.WinningSide.ToString(),
                    ["endedByRetreat"] = mapEvent.EndedByRetreat, ["isNaval"] = mapEvent.IsNavalMapEvent,
                    ["attackerInitial"] = mapEvent.AttackerSide?.HealthyTroopCountAtMapEventStart ?? 0,
                    ["defenderInitial"] = mapEvent.DefenderSide?.HealthyTroopCountAtMapEventStart ?? 0,
                    ["attackerCasualties"] = mapEvent.AttackerSide?.TroopCasualties ?? 0,
                    ["defenderCasualties"] = mapEvent.DefenderSide?.TroopCasualties ?? 0
                });
            _battleSnapshots.Remove(mapEvent.GetHashCode());
        }

        private JArray BuildMapEventEntities(MapEvent mapEvent, bool outcome)
        {
            JArray result = new JArray();
            AddSideEntities(result, mapEvent, mapEvent.AttackerSide, "attacker", outcome);
            AddSideEntities(result, mapEvent, mapEvent.DefenderSide, "defender", outcome);
            if (mapEvent.MapEventSettlement != null) result.Add(Entity(mapEvent.MapEventSettlement, "battle_location"));
            return result;
        }

        private void AddSideEntities(JArray result, MapEvent mapEvent, MapEventSide side, string sideName, bool outcome)
        {
            if (side == null) return;
            bool winner = outcome && mapEvent.WinningSide == side.MissionSide;
            foreach (MapEventParty battleParty in side.Parties)
            {
                PartyBase party = battleParty?.Party;
                if (party == null) continue;
                string partyRole = sideName + " party participant" + (winner ? " winner" : outcome ? " loser" : "");
                if (party == side.LeaderParty) partyRole += " side_leader";
                JObject partyEntity = Entity(party, partyRole, sideName);
                partyEntity["quantity"] = battleParty.HealthyManCountAtStart;
                partyEntity["payload"] = new JObject
                {
                    ["healthyAtStart"] = battleParty.HealthyManCountAtStart, ["participatingTroops"] = battleParty.ParticipatingTroopCount,
                    ["contribution"] = battleParty.ContributionToBattle, ["plunderedGold"] = battleParty.PlunderedGold, ["goldLost"] = battleParty.GoldLost,
                    ["gainedRenown"] = battleParty.GainedRenown, ["gainedInfluence"] = battleParty.GainedInfluence, ["gainedMorale"] = battleParty.GainedMorale
                };
                result.Add(partyEntity);
                foreach (Hero hero in PartyHeroes(party))
                {
                    string role = "participant " + sideName + (winner ? " winner" : outcome ? " loser" : "");
                    if (hero == party.LeaderHero) role += " leader commander";
                    if (party == side.LeaderParty && hero == party.LeaderHero) role += " side_leader";
                    result.Add(Entity(hero, role, sideName, party.Id));
                }
                if (outcome)
                {
                    AddRosterEntities(result, battleParty.DiedInBattle, "killed casualty", sideName, party.Id);
                    AddRosterEntities(result, battleParty.WoundedInBattle, "wounded casualty", sideName, party.Id);
                    AddRosterEntities(result, battleParty.RoutedInBattle, "routed casualty", sideName, party.Id);
                    AddRosterEntities(result, battleParty.RosterToReceiveLootPrisoners, "captured prisoner", sideName, party.Id);
                    AddItemRosterEntities(result, battleParty.RosterToReceiveLootItems, "loot received", party.Id);
                }
            }
        }

        private void AddRescueEvidence(MapEvent mapEvent, BattleSnapshot snapshot, JArray entities)
        {
            if (snapshot == null || mapEvent.Winner == null) return;
            BattleSideEnum losingSide = mapEvent.DefeatedSide;
            List<Hero> released = new List<Hero>();
            foreach (MapEventParty losingParty in mapEvent.PartiesOnSide(losingSide))
            {
                if (!snapshot.PrisonerHeroesByParty.TryGetValue(PartyId(losingParty.Party), out List<Hero> before)) continue;
                HashSet<string> stillHeld = new HashSet<string>(HeroPrisoners(losingParty.Party).Select(x => x.StringId), StringComparer.OrdinalIgnoreCase);
                released.AddRange(before.Where(x => x != null && !stillHeld.Contains(x.StringId) && !x.IsPrisoner));
            }
            if (released.Count == 0) return;
            foreach (Hero hero in released.Distinct()) entities.Add(Entity(hero, "rescued_prisoner beneficiary"));
            bool playerOnWinningSide = mapEvent.Winner.Parties.Any(x => x.Party == PartyBase.MainParty);
            if (playerOnWinningSide && Hero.MainHero != null)
            {
                bool direct = mapEvent.Winner.LeaderParty == PartyBase.MainParty || mapEvent.Winner.Parties.Count == 1;
                entities.Add(Entity(Hero.MainHero, direct ? "direct_rescuer" : "assisting_rescuer rescuing_side_participant", mapEvent.WinningSide.ToString(), PartyBase.MainParty.Id));
            }
        }

        private void OnRaidCompleted(BattleSideEnum winner, RaidEventComponent raid)
        {
            Settlement settlement = raid?.MapEventSettlement;
            JArray entities = raid?.MapEvent == null ? new JArray() : BuildMapEventEntities(raid.MapEvent, true);
            Emit("raid_completed", "completed", "A raid at " + Name(settlement) + " ended with " + winner + " victorious.", raid?.MapEvent == null ? string.Empty : BattleCorrelation(raid.MapEvent), settlement, entities, true, "ordinary",
                new JObject { ["winnerSide"] = winner.ToString(), ["raidDamage"] = raid?.RaidDamage ?? 0f });
        }

        private void OnVillageBeingRaided(Village village) => EmitVillage("village_raid_started", "started", village, village?.VillageState.ToString());
        private void OnVillageLooted(Village village) => EmitVillage("village_looted", "completed", village, village?.VillageState.ToString());
        private void OnVillageStateChanged(Village village, Village.VillageStates oldState, Village.VillageStates newState, MobileParty party)
        {
            JArray entities = new JArray(Entity(village?.Settlement, "affected village"), Entity(party, "responsible party actor"));
            Emit("village_state_changed", "completed", Name(village?.Settlement) + " changed from " + oldState + " to " + newState + ".", string.Empty, village?.Settlement, entities, true, "ordinary",
                new JObject { ["oldState"] = oldState.ToString(), ["newState"] = newState.ToString() });
        }

        private void OnWarDeclared(IFaction first, IFaction second, DeclareWarAction.DeclareWarDetail detail) => EmitFactionPair("war_declared", first, second, "declared war on", detail.ToString(), "major_world");
        private void OnMakePeace(IFaction first, IFaction second, MakePeaceAction.MakePeaceDetail detail) => EmitFactionPair("peace_made", first, second, "made peace with", detail.ToString(), "major_world");
        private void OnAllianceStarted(Kingdom first, Kingdom second) => EmitFactionPair("alliance_started", first, second, "formed an alliance with", string.Empty, "major_world");
        private void OnAllianceEnded(Kingdom first, Kingdom second) => EmitFactionPair("alliance_ended", first, second, "ended its alliance with", string.Empty, "major_world");

        private void OnSettlementOwnerChanged(Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner, Hero capturer, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            JArray entities = new JArray(Entity(settlement, "transferred settlement target"), Entity(newOwner, "new owner"), Entity(oldOwner, "former owner"), Entity(capturer, "conqueror capturer actor"));
            Emit("settlement_owner_changed", "completed", Name(settlement) + " passed from " + Name(oldOwner) + " to " + Name(newOwner) + ".", string.Empty, settlement, entities, true, "major_world",
                new JObject { ["detail"] = detail.ToString(), ["openToClaim"] = openToClaim });
        }

        private void OnGovernorChanged(Town town, Hero newGovernor, Hero oldGovernor)
        {
            Emit("governor_changed", "completed", Name(newGovernor) + " became governor of " + Name(town?.Settlement) + ".", string.Empty, town?.Settlement,
                new JArray(Entity(newGovernor, "new governor actor"), Entity(oldGovernor, "former governor"), Entity(town?.Settlement, "governed settlement")), true, "ordinary");
        }

        private void OnHeroPrisonerTaken(PartyBase captor, Hero prisoner)
        {
            Emit("hero_prisoner_taken", "completed", Name(prisoner) + " was captured by " + Name(captor) + ".", string.Empty, captor?.MobileParty?.CurrentSettlement,
                new JArray(Entity(captor, "captor actor"), Entity(captor?.LeaderHero, "captor leader"), Entity(prisoner, "captured prisoner target")), true, "ordinary");
        }

        private void OnHeroPrisonerReleased(Hero prisoner, PartyBase captor, IFaction faction, EndCaptivityDetail detail, bool showNotification)
        {
            string correlation = FindRecentBattleCorrelation(captor);
            JArray entities = new JArray(Entity(prisoner, "released_prisoner beneficiary"), Entity(captor, "former captor"), Entity(captor?.LeaderHero, "releaser former_captor"), Entity(faction, "release faction"));
            Emit("hero_prisoner_released", "completed", Name(prisoner) + " was released from " + Name(captor) + ".", correlation, captor?.MobileParty?.CurrentSettlement, entities, true, "ordinary",
                new JObject { ["endCaptivityDetail"] = detail.ToString(), ["showNotification"] = showNotification });
        }

        private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            Emit("hero_killed", "completed", Name(victim) + " was killed" + (killer == null ? "." : " by " + Name(killer) + "."), string.Empty, victim?.CurrentSettlement,
                new JArray(Entity(victim, "killed victim target"), Entity(killer, "killer actor")), true, "major_world", new JObject { ["detail"] = detail.ToString() });
        }

        private void OnHeroWounded(Hero hero) => EmitSimpleHero("hero_wounded", hero, "wounded hero target", Name(hero) + " was wounded.", "ordinary");
        private void OnBeforeHeroesMarried(Hero first, Hero second, bool showNotification) => Emit("heroes_married", "completed", Name(first) + " married " + Name(second) + ".", string.Empty, first?.CurrentSettlement,
            new JArray(Entity(first, "spouse participant"), Entity(second, "spouse participant")), true, "major_world");
        private void OnChildConceived(Hero mother) => EmitSimpleHero("child_conceived", mother, "pregnant mother participant", Name(mother) + " conceived a child.", "participants");
        private void OnGivenBirth(Hero mother, List<Hero> children, int stillborn)
        {
            JArray entities = new JArray(Entity(mother, "mother participant"));
            foreach (Hero child in children ?? new List<Hero>()) entities.Add(Entity(child, "newborn child participant"));
            Emit("birth", "completed", Name(mother) + " gave birth to " + (children?.Count ?? 0) + " child or children.", string.Empty, mother?.CurrentSettlement, entities, true, "major_world", new JObject { ["stillbornCount"] = stillborn });
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom oldKingdom, Kingdom newKingdom, ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotification) =>
            EmitClanKingdomChange("clan_changed_kingdom", clan, oldKingdom, newKingdom, detail.ToString());
        private void OnClanDefected(Clan clan, Kingdom oldKingdom, Kingdom newKingdom) => EmitClanKingdomChange("clan_defected", clan, oldKingdom, newKingdom, "defection");
        private void OnClanCreated(Clan clan, bool showNotification) => Emit("clan_created", "completed", Name(clan) + " was founded.", string.Empty, null, new JArray(Entity(clan, "created clan target"), Entity(clan?.Leader, "founder leader actor")), true, "major_world");
        private void OnClanDestroyed(Clan clan) => Emit("clan_destroyed", "completed", Name(clan) + " was destroyed.", string.Empty, null, new JArray(Entity(clan, "destroyed clan target"), Entity(clan?.Leader, "last clan leader")), true, "major_world");

        private void OnArmyCreated(Army army) => EmitArmy("army_created", "started", army, "An army was formed under " + Name(army?.ArmyOwner) + ".");
        private void OnArmyDispersed(Army army, Army.ArmyDispersionReason reason, bool isPlayersArmy) => EmitArmy("army_dispersed", "completed", army, "The army under " + Name(army?.ArmyOwner) + " dispersed: " + reason + ".");
        private void OnMobilePartyCreated(MobileParty party)
        {
            if (party?.LeaderHero == null && party?.Party != PartyBase.MainParty) return;
            EmitSimpleParty("mobile_party_created", party, "created party target", Name(party) + " was formed.");
        }
        private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyer) => Emit("mobile_party_destroyed", "completed", Name(party) + " was destroyed by " + Name(destroyer) + ".", string.Empty, party?.CurrentSettlement,
            new JArray(Entity(party, "destroyed party target"), Entity(destroyer, "destroyer actor"), Entity(destroyer?.LeaderHero, "destroyer leader")), true, "ordinary");
        private void OnPartyLeaderChanged(MobileParty party, Hero oldLeader) => Emit("party_leader_changed", "completed", Name(party?.LeaderHero) + " took command of " + Name(party) + ".", string.Empty, party?.CurrentSettlement,
            new JArray(Entity(party, "party target"), Entity(oldLeader, "former leader"), Entity(party?.LeaderHero, "new leader commander actor")), true, "ordinary");

        private void OnTournamentStarted(Town town) => Emit("tournament_started", "started", "A tournament began at " + Name(town?.Settlement) + ".", "tournament_" + town?.StringId + "_" + CurrentDay().ToString("0.000"), town?.Settlement, new JArray(Entity(town?.Settlement, "tournament location")), true, "ordinary");
        private void OnTournamentFinished(CharacterObject winner, TaleWorlds.Library.MBReadOnlyList<CharacterObject> participants, Town town, ItemObject prize)
        {
            TeamTournamentOutcome teamOutcome = TeamTournamentOutcome.ForFinishedEvent(town, winner);
            JArray entities = new JArray(Entity(winner?.HeroObject, "tournament winner actor"), Entity(town?.Settlement, "tournament location"));
            foreach (CharacterObject participant in participants ?? new TaleWorlds.Library.MBReadOnlyList<CharacterObject>(new List<CharacterObject>()))
                if (participant?.HeroObject != null && participant != winner)
                    entities.Add(Entity(participant.HeroObject, teamOutcome?.WinningTeam.Contains(participant) == true
                        ? "tournament winner teammate actor" : "tournament participant"));
            if (prize != null) entities.Add(new JObject { ["entityId"] = prize.StringId, ["entityType"] = "item", ["name"] = prize.Name?.ToString() ?? prize.StringId, ["role"] = "tournament prize" });
            string summary = Name(winner?.HeroObject) + (teamOutcome == null ? " won the tournament at " : " and their team won the tournament at ") + Name(town?.Settlement) + ".";
            Emit("tournament_finished", "completed", summary, string.Empty, town?.Settlement, entities, true, "ordinary",
                new JObject { ["format"] = teamOutcome == null ? "individual" : "team" }, teamOutcome == null ? "bannerlord_native" : "arena_overhaul");
        }

        private void OnSiegeStarted(SiegeEvent siege) => EmitSiege("siege_started", "started", siege);
        private void OnSiegeEnded(SiegeEvent siege) => EmitSiege("siege_ended", "completed", siege);
        private void OnSiegeCompleted(Settlement settlement, MobileParty attacker, bool won, MapEvent.BattleTypes battleType) => Emit("siege_completed", "completed", Name(attacker?.LeaderHero) + (won ? " captured " : " failed to capture ") + Name(settlement) + ".", "siege_" + settlement?.StringId,
            settlement, new JArray(Entity(attacker, won ? "conqueror attacker winner" : "attacker loser"), Entity(attacker?.LeaderHero, won ? "conqueror commander winner" : "attacking commander loser"), Entity(settlement, "besieged settlement target")), true, "major_world",
            new JObject { ["isWin"] = won, ["battleType"] = battleType.ToString() });

        private void OnWorkshopOwnerChanged(Workshop workshop, Hero oldOwner) => Emit("workshop_owner_changed", "completed", Name(workshop?.Owner) + " acquired a workshop at " + Name(workshop?.Settlement) + ".", string.Empty, workshop?.Settlement,
            new JArray(Entity(workshop?.Owner, "new owner actor"), Entity(oldOwner, "former owner"), Entity(workshop?.Settlement, "workshop settlement")), true, "ordinary");
        private void OnTroopRecruited(Hero recruiter, Settlement settlement, Hero source, CharacterObject troop, int amount) => Emit("troops_recruited", "completed", Name(recruiter) + " recruited " + amount + " " + (troop?.Name?.ToString() ?? "troops") + ".", string.Empty, settlement,
            new JArray(Entity(recruiter, "recruiter actor"), Entity(source, "recruitment source"), Entity(settlement, "recruitment location"), new JObject { ["entityId"] = troop?.StringId ?? string.Empty, ["entityType"] = "troop", ["name"] = troop?.Name?.ToString() ?? string.Empty, ["role"] = "recruited troop", ["quantity"] = amount }), true, "ordinary");
        private void OnItemSold(PartyBase receiver, PartyBase payer, ItemRosterElement item, int number, Settlement settlement) => Emit("item_sold", "completed", Name(payer) + " bought " + number + " " + (item.EquipmentElement.Item?.Name?.ToString() ?? "items") + " from " + Name(receiver) + ".", string.Empty, settlement,
            new JArray(Entity(receiver, "seller receiver"), Entity(payer, "buyer payer"), Entity(receiver?.LeaderHero, "seller participant"), Entity(payer?.LeaderHero, "buyer participant"), new JObject { ["entityId"] = item.EquipmentElement.Item?.StringId ?? string.Empty, ["entityType"] = "item", ["name"] = item.EquipmentElement.Item?.Name?.ToString() ?? string.Empty, ["role"] = "traded item", ["quantity"] = number }), true, "ordinary");
        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            Hero traveler = hero ?? party?.LeaderHero;
            if (traveler == null && party?.Party != PartyBase.MainParty) return;
            Emit("settlement_entered", "completed", Name(traveler) + " entered " + Name(settlement) + ".", string.Empty, settlement,
                new JArray(Entity(party, "arriving party"), Entity(traveler, "arriving traveler actor"), Entity(settlement, "arrival destination")), true, "ordinary");
        }
        private void OnSettlementLeft(MobileParty party, Settlement settlement)
        {
            if (party?.LeaderHero == null && party?.Party != PartyBase.MainParty) return;
            Emit("settlement_left", "completed", Name(party?.LeaderHero) + " left " + Name(settlement) + ".", string.Empty, settlement,
                new JArray(Entity(party, "departing party"), Entity(party?.LeaderHero, "departing traveler actor"), Entity(settlement, "departure origin")), true, "ordinary");
        }

        private void OnShipDestroyed(PartyBase owner, Ship ship, DestroyShipAction.ShipDestroyDetail detail)
        {
            if (ShouldRecordShip(owner, ship)) EmitShip("ship_destroyed", owner, ship, null, detail.ToString());
        }
        private void OnShipOwnerChanged(Ship ship, PartyBase oldOwner, ChangeShipOwnerAction.ShipOwnerChangeDetail detail)
        {
            PartyBase owner = ship?.Owner ?? oldOwner;
            if (ShouldRecordShip(owner, ship)) EmitShip("ship_owner_changed", owner, ship, null, detail.ToString());
        }
        private void OnShipCreated(Ship ship, Settlement settlement)
        {
            if (ShouldRecordShip(ship?.Owner, ship)) EmitShip("ship_created", ship?.Owner, ship, settlement, string.Empty);
        }
        private void OnShipRepaired(Ship ship, Settlement settlement)
        {
            if (ShouldRecordShip(ship?.Owner, ship)) EmitShip("ship_repaired", ship?.Owner, ship, settlement, string.Empty);
        }

        private void OnBarterAccepted(Hero first, Hero second, List<Barterable> barterables)
        {
            Emit("barter_accepted", "completed", Name(first) + " concluded a barter with " + Name(second) + ".", string.Empty, first?.CurrentSettlement,
                new JArray(Entity(first, "barter participant actor"), Entity(second, "barter participant target")), true, "ordinary",
                new JObject { ["barterableCount"] = barterables?.Count ?? 0 });
        }

        private void OnHeroRelationChanged(Hero first, Hero second, int change, bool showNotification, ChangeRelationAction.ChangeRelationDetail detail, Hero originalFirst, Hero originalSecond)
        {
            Emit("hero_relation_changed", "completed", "Relations between " + Name(first) + " and " + Name(second) + " changed by " + change + ".", string.Empty, first?.CurrentSettlement,
                new JArray(Entity(first, "relationship participant"), Entity(second, "relationship participant"), Entity(originalFirst, "relationship action source"), Entity(originalSecond, "relationship action source")), true, "ordinary",
                new JObject { ["change"] = change, ["detail"] = detail.ToString() });
        }

        private void OnCharacterDefeated(Hero winner, Hero loser) => Emit("character_defeated", "completed", Name(winner) + " defeated " + Name(loser) + ".", string.Empty, winner?.CurrentSettlement,
            new JArray(Entity(winner, "winner actor"), Entity(loser, "defeated target")), true, "ordinary");
        private void OnRulingClanChanged(Kingdom kingdom, Clan oldRulingClan)
        {
            Clan newRulingClan = kingdom?.RulingClan;
            Emit("ruling_clan_changed", "completed", Name(newRulingClan) + " became the ruling clan of " + Name(kingdom) + ".", string.Empty, null,
                new JArray(Entity(kingdom, "kingdom target"), Entity(oldRulingClan, "former ruling clan"),
                    Entity(newRulingClan, "new ruling clan actor"), Entity(newRulingClan?.Leader, "new ruler actor")), true, "major_world");
        }
        private void OnKingdomCreated(Kingdom kingdom) => Emit("kingdom_created", "completed", Name(kingdom) + " was established.", string.Empty, null, new JArray(Entity(kingdom, "created kingdom target"), Entity(kingdom?.Leader, "founding ruler actor")), true, "major_world");
        private void OnKingdomDestroyed(Kingdom kingdom) => Emit("kingdom_destroyed", "completed", Name(kingdom) + " was destroyed.", string.Empty, null, new JArray(Entity(kingdom, "destroyed kingdom target"), Entity(kingdom?.Leader, "last ruler")), true, "major_world");
        private void OnKingdomDecisionAdded(KingdomDecision decision, bool isPlayerDecision) => Emit("kingdom_decision_added", "started", "A kingdom decision was opened: " + decision?.GetType().Name + ".", string.Empty, null,
            new JArray(Entity(decision?.Kingdom, "deciding kingdom")), true, "ordinary", new JObject { ["decisionType"] = decision?.GetType().FullName ?? string.Empty, ["isPlayerDecision"] = isPlayerDecision });
        private void OnKingdomDecisionConcluded(KingdomDecision decision, DecisionOutcome outcome, bool isPlayerDecision) => Emit("kingdom_decision_concluded", "completed", "A kingdom decision concluded: " + decision?.GetType().Name + ".", string.Empty, null,
            new JArray(Entity(decision?.Kingdom, "deciding kingdom")), true, "ordinary", new JObject { ["decisionType"] = decision?.GetType().FullName ?? string.Empty, ["outcome"] = outcome?.GetType().Name ?? string.Empty, ["isPlayerDecision"] = isPlayerDecision });
        private void OnRebellionFinished(Settlement settlement, Clan rebelClan) => Emit("rebellion_finished", "completed", "The rebellion at " + Name(settlement) + " ended.", string.Empty, settlement,
            new JArray(Entity(settlement, "rebellious settlement target"), Entity(rebelClan, "rebel clan actor"), Entity(rebelClan?.Leader, "rebel leader actor")), true, "major_world");
        private void OnTownRebelliousStateChanged(Town town, bool rebellious) => Emit("town_rebellious_state_changed", "completed", Name(town?.Settlement) + (rebellious ? " entered rebellion." : " left rebellion."), string.Empty, town?.Settlement,
            new JArray(Entity(town?.Settlement, "rebellious town target"), Entity(town?.OwnerClan, "town owner clan")), true, "major_world", new JObject { ["rebellious"] = rebellious });

        private void OnArmyGathered(Army army, IMapPoint gatheringPoint) => EmitArmy("army_gathered", "completed", army, "The army under " + Name(army?.ArmyOwner) + " finished gathering.");
        private void OnPartyJoinedArmy(MobileParty party) => Emit("party_joined_army", "completed", Name(party) + " joined the army under " + Name(party?.Army?.ArmyOwner) + ".", string.Empty, party?.CurrentSettlement,
            new JArray(Entity(party, "joining army party"), Entity(party?.LeaderHero, "joining commander actor"), Entity(party?.Army?.ArmyOwner, "army leader")), true, "ordinary");
        private void OnPartyRemovedFromArmy(MobileParty party) => EmitSimpleParty("party_removed_from_army", party, "departing army party", Name(party) + " left an army.");
        private void OnMercenaryServiceStarted(Clan clan, StartMercenaryServiceAction.StartMercenaryServiceActionDetails detail) => Emit("mercenary_service_started", "completed", Name(clan) + " entered mercenary service.", string.Empty, null,
            new JArray(Entity(clan, "mercenary clan actor"), Entity(clan?.Leader, "mercenary leader actor"), Entity(clan?.Kingdom, "employing kingdom")), true, "major_world", new JObject { ["detail"] = detail.ToString() });
        private void OnMercenaryServiceEnded(Clan clan, EndMercenaryServiceAction.EndMercenaryServiceActionDetails detail) => Emit("mercenary_service_ended", "completed", Name(clan) + " ended mercenary service.", string.Empty, null,
            new JArray(Entity(clan, "mercenary clan actor"), Entity(clan?.Leader, "mercenary leader actor"), Entity(clan?.Kingdom, "former employing kingdom")), true, "major_world", new JObject { ["detail"] = detail.ToString() });
        private void OnHeroChangedClan(Hero hero, Clan oldClan)
        {
            // Clearing a companion's stale family field leaves the effective
            // clan unchanged. It is cache repair, not a new migration event.
            if (hero?.Clan == oldClan) return;
#if !REIGN_EXCLUDE_COURT
            if (ReignCourtNobleCampaignBehavior.IsHouseholdClanMigrationInProgress) return;
#endif
            Emit("hero_changed_clan", "completed", Name(hero) + " moved from " + Name(oldClan) + " to " + Name(hero?.Clan) + ".", string.Empty, hero?.CurrentSettlement,
                new JArray(Entity(hero, "moving hero actor"), Entity(oldClan, "former clan"), Entity(hero?.Clan, "new clan")), true, "major_world");
        }

        private void OnItemsLooted(MobileParty party, ItemRoster items)
        {
            if (!IsMainPlayerParty(party?.Party)) return;
            JArray entities = new JArray(Entity(party, "looting party actor"), Entity(party?.LeaderHero, "looter leader actor")); AddItemRosterEntities(entities, items, "looted item", party?.StringId ?? string.Empty);
            Emit("items_looted", "completed", Name(party) + " collected loot.", string.Empty, party?.CurrentSettlement, entities, true, "ordinary");
        }
        private void OnLootDistributed(PartyBase winner, PartyBase defeated, ItemRoster items)
        {
            if (!IsMainPlayerParty(winner) && !IsMainPlayerParty(defeated)) return;
            JArray entities = new JArray(Entity(winner, "loot receiver winner actor"), Entity(defeated, "defeated loot source"), Entity(winner?.LeaderHero, "winning leader")); AddItemRosterEntities(entities, items, "distributed loot item", winner?.Id ?? string.Empty);
            Emit("loot_distributed", "completed", Name(winner) + " received loot from " + Name(defeated) + ".", string.Empty, winner?.MobileParty?.CurrentSettlement, entities, true, "ordinary");
        }
        private void OnCaravanTransaction(MobileParty caravan, Town town, List<(EquipmentElement, int)> items) => Emit("caravan_transaction_completed", "completed", Name(caravan) + " completed a transaction at " + Name(town?.Settlement) + ".", string.Empty, town?.Settlement,
            new JArray(Entity(caravan, "trading caravan actor"), Entity(caravan?.LeaderHero, "caravan leader actor"), Entity(town?.Settlement, "market location")), true, "ordinary", new JObject { ["itemStackCount"] = items?.Count ?? 0 });
        private void OnPrisonerSold(PartyBase seller, PartyBase buyer, TroopRoster prisoners)
        {
            JArray entities = new JArray(Entity(seller, "prisoner seller actor"), Entity(buyer, "prisoner buyer target"), Entity(seller?.LeaderHero, "seller leader"), Entity(buyer?.LeaderHero, "buyer leader")); AddRosterEntities(entities, prisoners, "sold prisoner", string.Empty, buyer?.Id ?? string.Empty);
            Emit("prisoners_sold", "completed", Name(seller) + " sold prisoners to " + Name(buyer) + ".", string.Empty, seller?.MobileParty?.CurrentSettlement, entities, true, "ordinary");
        }
        private void OnTroopsDeserted(MobileParty party, TroopRoster troops)
        {
            JArray entities = new JArray(Entity(party, "deserted party target"), Entity(party?.LeaderHero, "party leader")); AddRosterEntities(entities, troops, "deserting troop actor", string.Empty, party?.StringId ?? string.Empty);
            Emit("troops_deserted", "completed", "Troops deserted " + Name(party) + ".", string.Empty, party?.CurrentSettlement, entities, true, "ordinary");
        }
        private void OnTroopGivenToSettlement(Hero giver, Settlement settlement, TroopRoster troops)
        {
            JArray entities = new JArray(Entity(giver, "troop giver actor"), Entity(settlement, "troop recipient settlement")); AddRosterEntities(entities, troops, "transferred troop", string.Empty, settlement?.StringId ?? string.Empty);
            Emit("troops_given_to_settlement", "completed", Name(giver) + " transferred troops to " + Name(settlement) + ".", string.Empty, settlement, entities, true, "ordinary");
        }
        private void OnBuildingLevelChanged(Town town, Building building, int levelChange) => Emit("building_level_changed", "completed", (building?.Name?.ToString() ?? "A building") + " changed level at " + Name(town?.Settlement) + ".", string.Empty, town?.Settlement,
            new JArray(Entity(town?.Settlement, "building settlement"), Entity(town?.Governor, "governor participant")), true, "ordinary", new JObject { ["buildingType"] = building?.BuildingType?.StringId ?? string.Empty, ["levelChange"] = levelChange });
        private void OnQuestStarted(QuestBase quest) => Emit("quest_started", "started", "Quest started: " + (quest?.Title?.ToString() ?? quest?.StringId ?? "unknown") + ".", string.Empty, null, new JArray(Entity(quest?.QuestGiver, "quest giver actor")), true, "ordinary", new JObject { ["questId"] = quest?.StringId ?? string.Empty });
        private void OnQuestCompleted(QuestBase quest, QuestBase.QuestCompleteDetails detail) => Emit("quest_completed", "completed", "Quest completed: " + (quest?.Title?.ToString() ?? quest?.StringId ?? "unknown") + ".", string.Empty, null, new JArray(Entity(quest?.QuestGiver, "quest giver participant"), Entity(Hero.MainHero, "quest completer actor")), true, "ordinary", new JObject { ["questId"] = quest?.StringId ?? string.Empty, ["detail"] = detail.ToString() });
        private void OnNewIssueCreated(IssueBase issue) => Emit("issue_created", "started", "Issue opened by " + Name(issue?.IssueOwner) + ".", string.Empty, issue?.IssueOwner?.CurrentSettlement, new JArray(Entity(issue?.IssueOwner, "issue owner actor")), true, "ordinary", new JObject { ["issueType"] = issue?.GetType().FullName ?? string.Empty });
        private void OnIssueUpdated(IssueBase issue, IssueBase.IssueUpdateDetails detail, Hero solver) => Emit("issue_updated", "completed", "Issue for " + Name(issue?.IssueOwner) + " was updated.", string.Empty, issue?.IssueOwner?.CurrentSettlement, new JArray(Entity(issue?.IssueOwner, "issue owner"), Entity(solver, "issue solver actor")), true, "ordinary", new JObject { ["issueType"] = issue?.GetType().FullName ?? string.Empty, ["detail"] = detail.ToString() });
        private void OnWorkshopTypeChanged(Workshop workshop) => Emit("workshop_type_changed", "completed", "A workshop changed type at " + Name(workshop?.Settlement) + ".", string.Empty, workshop?.Settlement,
            new JArray(Entity(workshop?.Owner, "workshop owner actor"), Entity(workshop?.Settlement, "workshop settlement")), true, "ordinary", new JObject { ["workshopType"] = workshop?.WorkshopType?.StringId ?? string.Empty });

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            ReconcileWorldState();
            CaptureSharedPartyContact();
            ReignWorldHistoryTransport.Flush();
        }

        internal void RecordMissionParticipant(Hero hero)
        {
            MapEvent battle = PartyBase.MainParty?.MapEvent;
            if (hero != null && battle != null && _battleSnapshots.TryGetValue(battle.GetHashCode(), out BattleSnapshot snapshot))
                snapshot.MissionHeroes.Add(hero.StringId);
        }

        private void OnSharedPartySizeChanged(PartyBase party)
        {
            if (party == PartyBase.MainParty) CaptureSharedPartyContact();
        }

        private void OnSharedHeroJoinedParty(Hero hero, MobileParty party)
        {
            if (party != MobileParty.MainParty) return;
            CaptureSharedPartyContact();
            Emit("hero_joined_party", "completed", Name(hero) + " joined the player's traveling party.", string.Empty,
                party.CurrentSettlement, new JArray(Entity(hero, "joining traveler"), Entity(Hero.MainHero, "party leader")), true, "participant_only");
        }

        private void OnSharedCompanionAdded(Hero hero) => Emit("companion_added", "completed", Name(hero) + " became a companion.",
            string.Empty, hero?.CurrentSettlement, new JArray(Entity(hero, "companion"), Entity(Hero.MainHero, "recruiter")), true, "participant_only");

        private void OnSharedCompanionRemoved(Hero hero, RemoveCompanionAction.RemoveCompanionDetail detail)
        {
            CaptureSharedPartyContact();
            Emit("companion_removed", "completed", Name(hero) + " left companion service.", string.Empty, hero?.CurrentSettlement,
                new JArray(Entity(hero, "departing companion"), Entity(Hero.MainHero, "former clan leader")), true, "participant_only", new JObject { ["detail"] = detail.ToString() });
        }

        private void OnSharedItemCrafted(ItemObject item, ItemModifier modifier, bool orderItem)
        {
            if (item == null || Hero.MainHero == null) return;
            Emit("crafting_completed", "completed", "The player's smithing session produced " + item.Name + ".", string.Empty,
                Hero.MainHero.CurrentSettlement, new JArray(Entity(Hero.MainHero, "crafting session observer")), true, "participant_only",
                new JObject { ["itemId"] = item.StringId, ["itemName"] = item.Name.ToString(), ["quantity"] = 1, ["isOrderItem"] = orderItem });
        }

        private void OnSharedItemGiven((Hero, PartyBase) giver, (Hero, PartyBase) receiver, ItemRosterElement item, bool notification)
        {
            if (giver.Item1 != Hero.MainHero && receiver.Item1 != Hero.MainHero && giver.Item2 != PartyBase.MainParty && receiver.Item2 != PartyBase.MainParty) return;
            Emit("item_given", "completed", "An item transfer was completed: " + item.Amount + " " + item.EquipmentElement.Item?.Name + ".",
                string.Empty, Hero.MainHero?.CurrentSettlement,
                new JArray(Entity(giver.Item1, "giver"), Entity(receiver.Item1, "recipient"), Entity(giver.Item2, "giving party"), Entity(receiver.Item2, "receiving party")),
                true, "participant_only", new JObject { ["itemId"] = item.EquipmentElement.Item?.StringId ?? string.Empty, ["quantity"] = item.Amount });
        }

        private void OnSharedNavigationChanged(MobileParty party)
        {
            if (party != MobileParty.MainParty) return;
            CaptureSharedPartyContact();
            bool sea = party.IsCurrentlyAtSea;
            if (sea) _seaTravelStartedDay = CampaignTime.Now.ToDays;
            if (!sea && _seaTravelStartedDay <= 0d) return;
            Emit(sea ? "sea_travel_started" : "sea_travel_ended", sea ? "started" : "completed",
                sea ? "The player's party began sea travel." : "The player's party ended its sea travel; destination and losses follow the separate native records.",
                string.Empty, party.CurrentSettlement, new JArray(PartyHeroes(party.Party).Select(h => Entity(h, "traveling participant"))),
                true, "participant_only", new JObject { ["startedDay"] = _seaTravelStartedDay });
            if (!sea) _seaTravelStartedDay = 0d;
        }

        internal void CaptureSharedPartyContact()
        {
            if (!_timelineReady || ReignCampaignInitializationGate.IsPending) return;
            double now = CampaignTime.Now.ToDays;
            string partyId = PartyBase.MainParty?.Id ?? string.Empty;
            string[] present = PartyHeroes(PartyBase.MainParty).Where(h => h.IsAlive && !h.IsPrisoner && h.HitPoints > 0)
                .Select(h => h.StringId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            bool available = Hero.MainHero != null && Hero.MainHero.HitPoints > 0 && !Hero.MainHero.IsPrisoner && PartyBase.MainParty?.MapEvent == null;
            bool stationary = available && MobileParty.MainParty != null && !MobileParty.MainParty.IsMoving;
            if (_waitingStartedDay > 0d)
                _waitingRoster = string.Join("|", (_waitingRoster ?? string.Empty).Split('|').Intersect(present, StringComparer.Ordinal));
            if (stationary && _waitingStartedDay <= 0d)
            {
                _waitingStartedDay = now;
                _waitingRoster = string.Join("|", present);
            }
            else if (!stationary && _waitingStartedDay > 0d)
            {
                if (available && now - _waitingStartedDay >= 0.125d)
                    Emit("party_wait_ended", "completed", "The player's party resumed movement after waiting. This does not establish sleep, camping, lodging or a meal.",
                        string.Empty, MobileParty.MainParty?.CurrentSettlement,
                        new JArray(PartyHeroes(PartyBase.MainParty).Where(h => (_waitingRoster ?? string.Empty).Split('|').Contains(h.StringId)).Select(h => Entity(h, "waiting traveler"))),
                        true, "participant_only", new JObject { ["fromDay"] = _waitingStartedDay, ["untilDay"] = now });
                _waitingStartedDay = 0d;
                _waitingRoster = string.Empty;
            }
            if (available && _contactSampleDay > 0d && now > _contactSampleDay && now - _contactSampleDay <= 0.09d && _contactPartyId == partyId)
            {
                string[] together = present.Intersect((_contactRoster ?? string.Empty).Split('|'), StringComparer.Ordinal).ToArray();
                if (together.Contains(Hero.MainHero.StringId) && together.Length > 1)
                    Emit("party_contact_interval", "completed", "The player and accompanying heroes shared ordinary travel contact.",
                        string.Empty, null, new JArray(Entity(Hero.MainHero, "traveler actor")), true, "participant_only",
                        new JObject { ["fromDay"] = _contactSampleDay, ["partyId"] = partyId,
                            ["playerHeroId"] = Hero.MainHero.StringId, ["eligibleHeroIds"] = new JArray(together) });
            }
            _contactSampleDay = now;
            _contactPartyId = partyId;
            _contactRoster = available ? string.Join("|", present) : string.Empty;
        }

        internal void RecordArenaPracticeEnd(IEnumerable<string> participants, int opponentsBeaten, bool survived, bool interrupted)
        {
            var ids = new HashSet<string>(participants ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            var entities = new JArray(Hero.AllAliveHeroes.Where(h => ids.Contains(h.StringId)).Select(h => Entity(h, "arena practice participant")));
            Emit("arena_practice_ended", "completed", interrupted ? "The player's arena practice was interrupted." : "The player's arena practice ended.",
                string.Empty, Hero.MainHero?.CurrentSettlement, entities, true, "participant_only",
                new JObject { ["opponentsBeaten"] = opponentsBeaten, ["survived"] = survived, ["interrupted"] = interrupted });
        }

        private void ReconcileWorldState()
        {
            Dictionary<string, string> current = BuildReconciliationState();
            if (!_reconciliationInitialized)
            {
                foreach (KeyValuePair<string, string> pair in current) _reconciliationState[pair.Key] = pair.Value;
                _reconciliationInitialized = true;
                return;
            }
            int emitted = 0;
            foreach (KeyValuePair<string, string> pair in current)
            {
                if (!_reconciliationState.TryGetValue(pair.Key, out string before) || before == pair.Value) continue;
                string entityId = pair.Key.Substring(pair.Key.IndexOf(':') + 1);
                if (!_observedEntities.Contains(entityId) && emitted++ < 100)
                {
                    Emit("unattributed_state_change", "completed", pair.Key + " changed without a correlated native actor.", string.Empty, null,
                        new JArray(new JObject { ["entityId"] = entityId, ["entityType"] = pair.Key.Split(':')[0], ["role"] = "changed entity unknown actor", ["before"] = before, ["after"] = pair.Value }), false, "ordinary");
                }
            }
            _reconciliationState.Clear();
            foreach (KeyValuePair<string, string> pair in current) _reconciliationState[pair.Key] = pair.Value;
            _observedEntities.Clear();
        }

        private void CaptureReconciliationBaseline()
        {
            _reconciliationState.Clear();
            foreach (KeyValuePair<string, string> pair in BuildReconciliationState()) _reconciliationState[pair.Key] = pair.Value;
            _reconciliationInitialized = true;
        }

        private Dictionary<string, string> BuildReconciliationState()
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Settlement settlement in Settlement.All.Where(x => x != null))
                result["settlement:" + settlement.StringId] = (settlement.OwnerClan?.StringId ?? "") + "|" + (settlement.Town?.Governor?.StringId ?? "") + "|" + (settlement.Village?.VillageState.ToString() ?? "");
            foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x != null))
                result["hero:" + hero.StringId] = (hero.Clan?.StringId ?? "") + "|" + hero.IsPrisoner;
            foreach (MobileParty party in MobileParty.All.Where(x => x != null))
                result["party:" + party.StringId] = (party.LeaderHero?.StringId ?? "") + "|" + (party.Army?.LeaderParty?.StringId ?? "");
            List<Kingdom> kingdoms = Kingdom.All.Where(x => x != null && !x.IsEliminated).ToList();
            for (int i = 0; i < kingdoms.Count; i++)
                for (int j = i + 1; j < kingdoms.Count; j++)
                    result["diplomacy:" + kingdoms[i].StringId + "~" + kingdoms[j].StringId] = kingdoms[i].IsAtWarWith(kingdoms[j]).ToString();
            return result;
        }

        private void EmitVillage(string type, string phase, Village village, string state)
        {
            Settlement settlement = village?.Settlement;
            Emit(type, phase, Name(settlement) + " is now " + state + ".", string.Empty, settlement,
                new JArray(Entity(settlement, "affected village target"), Entity(settlement?.OwnerClan, "village owner clan")), true, "ordinary");
        }

        private void EmitFactionPair(string type, IFaction first, IFaction second, string verb, string detail, string dissemination)
        {
            Emit(type, "completed", Name(first) + " " + verb + " " + Name(second) + ".", string.Empty, null,
                new JArray(Entity(first, "initiating faction actor"), Entity(second, "target faction")), true, dissemination, new JObject { ["detail"] = detail });
        }

        private void EmitClanKingdomChange(string type, Clan clan, Kingdom oldKingdom, Kingdom newKingdom, string detail)
        {
            Emit(type, "completed", Name(clan) + " left " + Name(oldKingdom) + " for " + Name(newKingdom) + ".", string.Empty, null,
                new JArray(Entity(clan, "moving clan actor"), Entity(clan?.Leader, "clan leader actor"), Entity(oldKingdom, "former kingdom"), Entity(newKingdom, "new kingdom")), true, "major_world", new JObject { ["detail"] = detail });
        }

        private void EmitArmy(string type, string phase, Army army, string summary)
        {
            JArray entities = new JArray(Entity(army?.ArmyOwner, "army leader commander actor"), Entity(army?.LeaderParty, "army leader party"));
            foreach (MobileParty party in army?.Parties ?? Enumerable.Empty<MobileParty>()) entities.Add(Entity(party, "army member party"));
            Emit(type, phase, summary, "army_" + (army?.LeaderParty?.StringId ?? "unknown"), army?.LeaderParty?.CurrentSettlement, entities, true, "ordinary");
        }

        private void EmitSiege(string type, string phase, SiegeEvent siege)
        {
            Settlement settlement = siege?.BesiegedSettlement;
            MobileParty attacker = siege?.BesiegerCamp?.LeaderParty;
            Emit(type, phase, Name(attacker?.LeaderHero) + " " + (phase == "started" ? "began" : "ended") + " a siege of " + Name(settlement) + ".", "siege_" + settlement?.StringId,
                settlement, new JArray(Entity(attacker, "besieger party actor"), Entity(attacker?.LeaderHero, "besieger commander actor"), Entity(settlement, "besieged settlement target"), Entity(settlement?.OwnerClan, "defending owner clan")), true, "ordinary");
        }

        private void EmitShip(string type, PartyBase owner, Ship ship, Settlement settlement, string detail)
        {
            Emit(type, "completed", "Ship " + (ship?.Name?.ToString() ?? "unknown") + " changed state: " + type + ".", string.Empty, settlement,
                new JArray(Entity(owner, "ship owner party"), Entity(owner?.LeaderHero, "ship owner actor"), new JObject { ["entityId"] = ship?.Name?.ToString() ?? string.Empty, ["entityType"] = "ship", ["name"] = ship?.Name?.ToString() ?? string.Empty, ["role"] = type.Replace('_', ' ') }, Entity(settlement, "ship location")), true, "ordinary", new JObject { ["detail"] = detail });
        }

        private void EmitSimpleHero(string type, Hero hero, string role, string summary, string dissemination) => Emit(type, "completed", summary, string.Empty, hero?.CurrentSettlement, new JArray(Entity(hero, role)), true, dissemination);
        private void EmitSimpleParty(string type, MobileParty party, string role, string summary) => Emit(type, "completed", summary, string.Empty, party?.CurrentSettlement, new JArray(Entity(party, role), Entity(party?.LeaderHero, "party leader actor")), true, "ordinary");

        public void RecordReignSystemEvent(string eventType, string phase, string category, string correlationId,
            string summary, string dissemination, string actorHeroId, string kingdomId, JObject payload, JArray participantHeroIds = null)
        {
            Hero actor = string.IsNullOrWhiteSpace(actorHeroId) ? null : Hero.FindFirst(x => string.Equals(x.StringId, actorHeroId, StringComparison.OrdinalIgnoreCase));
            Kingdom kingdom = string.IsNullOrWhiteSpace(kingdomId) ? null : Kingdom.All.FirstOrDefault(x => string.Equals(x.StringId, kingdomId, StringComparison.OrdinalIgnoreCase));
            JArray entities = new JArray(Entity(actor, "political actor"), Entity(kingdom, "affected kingdom"));
            foreach (string participantId in (participantHeroIds ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                Hero participant = Hero.FindFirst(x => string.Equals(x.StringId, participantId, StringComparison.OrdinalIgnoreCase));
                if (participant != null && participant != actor) entities.Add(Entity(participant, "political participant"));
            }
            Emit(eventType, phase, summary, correlationId, actor?.CurrentSettlement,
                entities, true,
                dissemination, payload, "reign_system", category);
        }

        public void RecordSocialOutcome(string archetypeId, Hero subject, string role, string sourceKey,
            string summary, JObject evidence = null, JArray counterOnlyTagIds = null,
            bool forceExposure = false, bool forcePromotion = false)
        {
            if (subject == null || subject.IsDead) return;
            float adultAge = TaleWorlds.CampaignSystem.Campaign.Current?.Models?.AgeModel?.HeroComesOfAge ?? 18f;
            if (subject.Age < adultAge) return;
            JObject payload = evidence == null ? new JObject() : new JObject(evidence);
            payload["archetypeId"] = archetypeId ?? string.Empty;
            payload["subjectId"] = subject.StringId ?? string.Empty;
            payload["role"] = role ?? "subject";
            payload["clanTier"] = subject.Clan?.Tier ?? 1;
            payload["isAlive"] = subject.IsAlive;
            payload["isAdult"] = subject.Age >= adultAge;
            payload["isPlayer"] = subject == Hero.MainHero;
            payload["sex"] = subject.IsFemale ? "female" : "male";
            payload["sourceEventId"] = sourceKey ?? string.Empty;
            payload["provenanceSummary"] = summary ?? string.Empty;
            payload["forceExposure"] = forceExposure;
            payload["forcePromotion"] = forcePromotion;
            if (counterOnlyTagIds != null) payload["counterOnlyTagIds"] = counterOnlyTagIds;
            Emit("social_outcome", "completed", summary ?? "A social outcome was recorded.",
                sourceKey ?? string.Empty, subject.CurrentSettlement,
                new JArray(Entity(subject, "social reputation subject")), true, "ordinary",
                payload, "reign_social", "social", retainUntilTimelineReady: true);
        }

        public void RecordDynamicSocialReputation(
            Hero subject,
            string tagId,
            bool active,
            int value,
            string sourceKey,
            string summary,
            string description,
            JObject evidence = null)
        {
            JObject payload = evidence == null ? new JObject() : new JObject(evidence);
            payload["dynamicReputationTagId"] = tagId ?? string.Empty;
            payload["dynamicActive"] = active;
            payload["dynamicValue"] = value;
            payload["dynamicDescription"] = description ?? string.Empty;
            payload["dynamicEvidence"] = evidence == null ? new JObject() : new JObject(evidence);
            RecordSocialOutcome(string.Empty, subject, "derived", sourceKey, summary, payload);
        }

        public void RecordSocialRumorCorrection(
            Hero subject,
            string tagId,
            string sourceKey,
            string summary,
            JObject evidence = null)
        {
            JObject payload = evidence == null ? new JObject() : new JObject(evidence);
            payload["counterRumorOnly"] = true;
            RecordSocialOutcome(string.Empty, subject, "subject", sourceKey, summary, payload,
                new JArray(tagId ?? string.Empty));
        }

        private void Emit(string eventType, string phase, string summary, string correlationId, Settlement location, JArray entities, bool complete, string dissemination, JObject payload = null, string source = "bannerlord_native", string categoryOverride = "", bool retainUntilTimelineReady = false)
        {
            // Bannerlord and War Sails emit thousands of synthetic creation/ownership callbacks
            // while reconstructing a save. They are initialization state, not events that happened
            // during play, and previously dominated young campaign databases.
            if (!ReignWorldHistoryFlushPolicy.ShouldPersistEvent(eventType))
                return;
            double worldDay = CampaignTime.Now.ToDays;
            entities = entities ?? new JArray();
            payload = payload ?? new JObject();
            bool playerActivity = entities.OfType<JObject>().Any(e => e.Value<bool?>("isPlayer") == true);
            if (playerActivity && Hero.MainHero != null)
            {
                payload["playerHeroId"] = Hero.MainHero.StringId;
                // Companion awareness of an outcome is distinct from witnessing
                // every action. Restricted battle squads use mission evidence.
                if (eventType == "settlement_entered" || eventType == "settlement_left"
                    || eventType == "quest_completed" || eventType == "troops_recruited"
                    || eventType == "item_sold" || eventType == "barter_accepted" || eventType == "loot_distributed"
                    || eventType == "items_looted" || eventType == "prisoners_sold" || eventType == "troops_given_to_settlement"
                    || eventType == "troops_deserted" || eventType == "party_joined_army" || eventType == "party_removed_from_army"
                    || eventType == "army_gathered" || eventType == "army_dispersed" || eventType == "ship_repaired"
                    || eventType == "ship_created" || eventType == "ship_destroyed" || eventType == "ship_owner_changed"
                    || eventType == "hero_joined_party" || eventType == "companion_added" || eventType == "companion_removed")
                {
                    var observers = new JArray();
                    foreach (Hero companion in PartyHeroes(PartyBase.MainParty).Where(h => h.IsAlive && !h.IsPrisoner))
                    {
                        JObject observer = Entity(companion, "accompanying party member");
                        observer["activityAccompanying"] = true;
                        observers.Add(observer);
                    }
                    payload["activityObservers"] = observers;
                }
            }
            if (IsDuplicateHighFrequencyEvent(eventType, summary, location, entities, (float)worldDay)) return;
            long sequence = ++_sequence;
            string eventId = _timelineId + "_" + sequence.ToString("D12") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            _headEventId = eventId;
            foreach (JObject entity in (entities ?? new JArray()).OfType<JObject>())
            {
                string id = entity.Value<string>("entityId");
                if (!string.IsNullOrWhiteSpace(id)) _observedEntities.Add(id);
            }
            JObject evt = new JObject
            {
                ["eventId"] = eventId, ["sequence"] = sequence, ["worldDay"] = worldDay, ["eventType"] = eventType,
                ["phase"] = phase ?? "completed", ["category"] = string.IsNullOrWhiteSpace(categoryOverride) ? Category(eventType) : categoryOverride, ["correlationId"] = correlationId ?? string.Empty,
                ["locationId"] = location?.StringId ?? string.Empty, ["locationName"] = Name(location), ["disseminationClass"] = dissemination ?? "ordinary",
                ["summary"] = summary ?? eventType, ["source"] = source ?? "bannerlord_native", ["isComplete"] = complete,
                ["entities"] = entities ?? new JArray(), ["payload"] = payload ?? new JObject()
            };
            if (!_timelineReady)
            {
                // Most callbacks observed during save reconstruction are initialization
                // noise and remain suppressed. Explicit social outcomes are player-facing
                // facts, however, so retain them until the timeline handshake completes.
                if (retainUntilTimelineReady) _deferred.Add(evt);
                return;
            }
            ReignWorldHistoryTransport.Enqueue(evt);
        }

        private bool IsDuplicateHighFrequencyEvent(
            string eventType,
            string summary,
            Settlement location,
            JArray entities,
            float worldDay)
        {
            switch (eventType)
            {
                case "settlement_entered":
                case "settlement_left":
                case "troops_recruited":
                case "caravan_transaction_completed":
                    break;
                default:
                    return false;
            }
            string entityIds = string.Join(",", (entities ?? new JArray()).OfType<JObject>()
                .Select(x => x.Value<string>("entityId") ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            string key = eventType + "|" + (summary ?? "") + "|" + (location?.StringId ?? "") + "|" + entityIds;
            if (_recentEventKeys.TryGetValue(key, out float previousDay)
                && worldDay - previousDay >= 0f && worldDay - previousDay < 0.001f)
                return true;
            _recentEventKeys[key] = worldDay;
            if (_recentEventKeys.Count > 4096)
            {
                foreach (string expired in _recentEventKeys
                    .Where(x => worldDay - x.Value > 0.1f)
                    .Select(x => x.Key)
                    .Take(2048)
                    .ToList())
                    _recentEventKeys.Remove(expired);
            }
            return false;
        }

        private static bool ShouldRecordShip(PartyBase owner, Ship ship)
        {
            PartyBase effectiveOwner = ship?.Owner ?? owner;
            return effectiveOwner == PartyBase.MainParty || effectiveOwner?.LeaderHero != null;
        }

        private static bool IsMainPlayerParty(PartyBase party)
        {
            return party == PartyBase.MainParty || party?.LeaderHero == Hero.MainHero;
        }

        private JObject Entity(Hero hero, string role, string side = "", string partyId = "")
        {
            if (hero == null) return new JObject();
            return new JObject { ["entityId"] = hero.StringId, ["entityType"] = "hero", ["name"] = Name(hero), ["role"] = role ?? "participant", ["side"] = side ?? "", ["partyId"] = partyId ?? hero.PartyBelongedTo?.StringId ?? "", ["clanId"] = hero.Clan?.StringId ?? "", ["kingdomId"] = hero.Clan?.Kingdom?.StringId ?? "", ["isPlayer"] = hero == Hero.MainHero };
        }

        private JObject Entity(PartyBase party, string role, string side = "")
        {
            if (party == null) return new JObject();
            return new JObject { ["entityId"] = party.Id, ["entityType"] = "party", ["name"] = Name(party), ["role"] = role ?? "party", ["side"] = side ?? "", ["partyId"] = party.Id, ["clanId"] = party.LeaderHero?.Clan?.StringId ?? "", ["kingdomId"] = (party.MapFaction as Kingdom)?.StringId ?? party.LeaderHero?.Clan?.Kingdom?.StringId ?? "", ["isPlayer"] = IsMainPlayerParty(party) };
        }

        private JObject Entity(MobileParty party, string role) => Entity(party?.Party, role);
        private JObject Entity(Settlement settlement, string role)
        {
            if (settlement == null) return new JObject();
            return new JObject { ["entityId"] = settlement.StringId, ["entityType"] = "settlement", ["name"] = Name(settlement), ["role"] = role ?? "settlement", ["clanId"] = settlement.OwnerClan?.StringId ?? "", ["kingdomId"] = settlement.OwnerClan?.Kingdom?.StringId ?? "" };
        }
        private JObject Entity(Clan clan, string role)
        {
            if (clan == null) return new JObject();
            return new JObject { ["entityId"] = clan.StringId, ["entityType"] = "clan", ["name"] = Name(clan), ["role"] = role ?? "clan", ["clanId"] = clan.StringId, ["kingdomId"] = clan.Kingdom?.StringId ?? "" };
        }
        private JObject Entity(IFaction faction, string role)
        {
            if (faction == null) return new JObject();
            Kingdom kingdom = faction as Kingdom;
            Clan clan = faction as Clan;
            return new JObject { ["entityId"] = kingdom?.StringId ?? clan?.StringId ?? faction.Name?.ToString() ?? "", ["entityType"] = kingdom != null ? "kingdom" : clan != null ? "clan" : "faction", ["name"] = Name(faction), ["role"] = role ?? "faction", ["clanId"] = clan?.StringId ?? "", ["kingdomId"] = kingdom?.StringId ?? clan?.Kingdom?.StringId ?? "" };
        }

        private void AddRosterEntities(JArray result, TroopRoster roster, string role, string side, string partyId)
        {
            if (roster == null) return;
            int ordinaryQuantity = 0;
            int ordinaryKinds = 0;
            foreach (TroopRosterElement element in roster.GetTroopRoster())
            {
                CharacterObject character = element.Character;
                if (character == null || element.Number <= 0) continue;
                if (character.HeroObject == null)
                {
                    ordinaryQuantity += element.Number;
                    ordinaryKinds++;
                    continue;
                }
                JObject entity = Entity(character.HeroObject, role, side,
                    partyId);
                entity["quantity"] = element.Number;
                result.Add(entity);
            }
            if (ordinaryQuantity > 0)
                result.Add(new JObject
                {
                    ["entityId"] = "aggregate:troop:" + role + ":" + partyId,
                    ["entityType"] = "aggregate",
                    ["name"] = role + " aggregate",
                    ["role"] = role,
                    ["side"] = side,
                    ["partyId"] = partyId,
                    ["quantity"] = ordinaryQuantity,
                    ["payload"] = new JObject
                    {
                        ["aggregatedEntityType"] = "troop",
                        ["distinctEntryCount"] = ordinaryKinds
                    }
                });
        }

        private void AddItemRosterEntities(JArray result, ItemRoster roster, string role, string partyId)
        {
            if (roster == null) return;
            int quantity = 0;
            int kinds = 0;
            foreach (ItemRosterElement element in roster)
            {
                ItemObject item = element.EquipmentElement.Item;
                if (item == null || element.Amount <= 0) continue;
                quantity += element.Amount;
                kinds++;
            }
            if (quantity > 0)
                result.Add(new JObject
                {
                    ["entityId"] = "aggregate:item:" + role + ":" + partyId,
                    ["entityType"] = "aggregate",
                    ["name"] = role + " aggregate",
                    ["role"] = role,
                    ["partyId"] = partyId,
                    ["quantity"] = quantity,
                    ["payload"] = new JObject
                    {
                        ["aggregatedEntityType"] = "item",
                        ["distinctEntryCount"] = kinds
                    }
                });
        }

        private IEnumerable<Hero> PartyHeroes(PartyBase party) => party?.MemberRoster?.GetTroopRoster().Where(x => x.Character?.HeroObject != null).Select(x => x.Character.HeroObject).Distinct() ?? Enumerable.Empty<Hero>();
        private IEnumerable<Hero> HeroPrisoners(PartyBase party) => party?.PrisonRoster?.GetTroopRoster().Where(x => x.Character?.HeroObject != null).Select(x => x.Character.HeroObject).Distinct() ?? Enumerable.Empty<Hero>();
        private string PartyId(PartyBase party) => party?.Id ?? string.Empty;
        private string BattleCorrelation(MapEvent mapEvent) => "map_event_" + mapEvent.GetHashCode().ToString("x") + "_" + mapEvent.BattleStartTime.ToHours.ToString("0.000");
        private string FindRecentBattleCorrelation(PartyBase party) => _battleSnapshots.Values.LastOrDefault(x => x.PrisonerHeroesByParty.ContainsKey(PartyId(party)))?.CorrelationId ?? string.Empty;
        private string DescribeMapEvent(MapEvent mapEvent, string phase) => mapEvent.EventType + " " + phase + " near " + (Name(mapEvent.MapEventSettlement) == "unknown" ? "the campaign position" : Name(mapEvent.MapEventSettlement)) + ".";
        private float CurrentDay() => TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
        private string Category(string type) => type.Contains("battle") || type.Contains("raid") || type.Contains("siege") || type.Contains("war") || type.Contains("prisoner") ? "warfare" : type.Contains("marri") || type.Contains("birth") || type.Contains("clan") ? "personal_and_clan" : type.Contains("item") || type.Contains("workshop") || type.Contains("trade") ? "economy" : type.Contains("settlement") || type.Contains("village") ? "settlement" : "campaign";
        private string Name(object value)
        {
            if (value == null) return "unknown";
            if (value is Hero hero) return hero.Name?.ToString() ?? hero.StringId;
            if (value is MobileParty mobile) return mobile.Name?.ToString() ?? mobile.StringId;
            if (value is PartyBase party) return party.Name?.ToString() ?? party.Id;
            if (value is Settlement settlement) return settlement.Name?.ToString() ?? settlement.StringId;
            if (value is Clan clan) return clan.Name?.ToString() ?? clan.StringId;
            if (value is IFaction faction) return faction.Name?.ToString() ?? "faction";
            return value.ToString();
        }
    }

    internal sealed class ReignSharedActivityMissionBehavior : MissionLogic
    {
        private readonly HashSet<string> _seenHeroes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private float _nextSample;
        private bool _practiceActive;
        private readonly HashSet<string> _practiceHeroes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            _nextSample -= dt;
            if (_nextSample > 0f) return;
            _nextSample = 0.25f;
            Mission mission = Mission;
            if (mission == null) return;
            ArenaPracticeFightMissionController practice = mission.GetMissionBehavior<ArenaPracticeFightMissionController>();
            bool practicing = practice?.IsPlayerPracticing == true;
            if (_practiceActive && !practicing)
            {
                ReignWorldHistoryCampaignBehavior.Instance?.RecordArenaPracticeEnd(_practiceHeroes, practice?.OpponentCountBeatenByPlayer ?? 0, practice?.IsPlayerSurvived ?? false, false);
                _practiceHeroes.Clear();
            }
            _practiceActive = practicing;
            if (PartyBase.MainParty?.MapEvent == null && !practicing) return;
            Team playerTeam = mission.PlayerTeam;
            foreach (Agent agent in mission.Agents)
            {
                if (agent == null) continue;
                Hero hero = (agent.Character as CharacterObject)?.HeroObject;
                if (hero == null) continue;
                bool combatant = ReignSceneTeamSafety.IsPlayerTeamOrEnemy(agent.Team, playerTeam);
                if (combatant && _seenHeroes.Add(hero.StringId))
                    ReignWorldHistoryCampaignBehavior.Instance?.RecordMissionParticipant(hero);
                if (practicing && combatant) _practiceHeroes.Add(hero.StringId);
            }
        }

        protected override void OnEndMission()
        {
            if (_practiceActive)
                ReignWorldHistoryCampaignBehavior.Instance?.RecordArenaPracticeEnd(_practiceHeroes, 0, false, true);
            base.OnEndMission();
        }
    }
}
