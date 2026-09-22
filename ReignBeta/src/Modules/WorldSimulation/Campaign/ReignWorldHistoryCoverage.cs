using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    internal static class ReignWorldHistoryCoverage
    {
        internal const int ExpectedEventEndpointCount = 276;
        internal const string ExpectedSurfaceHash = "e12b364a929ac5e4d4f4412d8f918038ae10f4897de1a92158b029775834cd34";
        private static readonly string[] ExpectedEventEndpoints = @"AfterGameMenuInitializedEvent
AfterMissionStarted
AfterSettlementEntered
AfterSiegeCompletedEvent
AiHourlyTickEvent
AlleyClearedByPlayer
AlleyOccupiedByPlayer
AlleyOwnerChanged
ArmyCreated
ArmyDispersed
ArmyGathered
ArmyOverlaySetDirtyEvent
BanditPartyRecruited
BarterablesRequested
BattleStarted
BeforeGameMenuOpenedEvent
BeforeHeroesMarried
BeforeHeroKilledEvent
BeforeMissionOpenedEvent
BeforePlayerAgentSpawnEvent
BeforeSettlementEnteredEvent
CanBeGovernorOrHavePartyRoleEvent
CanHaveCampaignIssuesEvent
CanHeroBecomePrisonerEvent
CanHeroDieEvent
CanHeroEquipmentBeChangedEvent
CanHeroLeadPartyEvent
CanHeroMarryEvent
CanKingdomBeDiscontinuedEvent
CanMoveToSettlementEvent
CanPlayerMeetWithHeroAfterConversationEvent
CharacterBecameFugitiveEvent
CharacterDefeated
CharacterPortraitPopUpClosedEvent
CharacterPortraitPopUpOpenedEvent
ChildEducationCompletedEvent
ClanTierIncrease
CollectAvailableTutorialsEvent
CollectMetadataEntriesEvent
CompanionRemoved
ConversationEnded
CraftingPartUnlockedEvent
CrimeRatingChanged
DailyTickClanEvent
DailyTickEvent
DailyTickHeroEvent
DailyTickPartyEvent
DailyTickSettlementEvent
DailyTickTownEvent
ForceSuppliesCompletedEvent
ForceVolunteersCompletedEvent
GameMenuOpened
GameMenuOptionSelectedEvent
HeroComesOfAgeEvent
HeroCreated
HeroGainedSkill
HeroGrowsOutOfInfancyEvent
HeroKilledEvent
HeroLevelledUp
HeroOccupationChangedEvent
HeroOrPartyGaveItem
HeroOrPartyTradedGold
HeroPrisonerReleased
HeroPrisonerTaken
HeroReachesTeenAgeEvent
HeroRelationChanged
HeroWounded
HourlyTickClanEvent
HourlyTickEvent
HourlyTickPartyEvent
HourlyTickSettlementEvent
IsSettlementBusyEvent
IssueLogAddedEvent
ItemsLooted
KingdomCreatedEvent
KingdomDecisionAdded
KingdomDecisionCancelled
KingdomDecisionConcluded
KingdomDestroyedEvent
LocationCharactersAreReadyToSpawnEvent
LocationCharactersSimulatedEvent
MakePeace
MapEventEnded
MapEventStarted
MapInteractableCreated
MapInteractableDestroyed
MercenaryNumberChangedInTown
MercenaryTroopChangedInTown
MissionTickEvent
MobilePartyCreated
MobilePartyDestroyed
MobilePartyQuestStatusChanged
NearbyPartyAddedToPlayerMapEvent
NewCompanionAdded
OnAfterSessionLaunchedEvent
OnAgentJoinedConversationEvent
OnAllianceEndedEvent
OnAllianceStartedEvent
OnBarterAcceptedEvent
OnBarterCanceledEvent
OnBeforeMainCharacterDiedEvent
OnBeforePlayerCharacterChangedEvent
OnBeforeSaveEvent
OnBlockadeActivatedEvent
OnBlockadeDeactivatedEvent
OnBuildingLevelChangedEvent
OnCallToWarAgreementEndedEvent
OnCallToWarAgreementStartedEvent
OnCaravanTransactionCompletedEvent
OnCharacterCreationInitializedEvent
OnCharacterCreationIsOverEvent
OnCheckForIssueEvent
OnChildConceivedEvent
OnClanChangedKingdomEvent
OnClanCreatedEvent
OnClanDefectedEvent
OnClanDestroyedEvent
OnClanEarnedGoldFromTributeEvent
OnClanInfluenceChangedEvent
OnClanLeaderChangedEvent
OnCollectLootsItemsEvent
OnConfigChangedEvent
OnCraftingOrderCompletedEvent
OnEquipmentSmeltedByHeroEvent
OnFigureheadUnlockedEvent
OnGameEarlyLoadedEvent
OnGameLoadedEvent
OnGameLoadFinishedEvent
OnGameOverEvent
OnGivenBirthEvent
OnGovernorChangedEvent
OnHeirSelectionOverEvent
OnHeirSelectionRequestedEvent
OnHeroActivatedEvent
OnHeroChangedClanEvent
OnHeroCombatHitEvent
OnHeroGetsBusyEvent
OnHeroJoinedPartyEvent
OnHeroSharedFoodWithAnotherHeroEvent
OnHeroTeleportationRequestedEvent
OnHeroUnregisteredEvent
OnHideoutBattleCompletedEvent
OnHideoutDeactivatedEvent
OnHideoutSpottedEvent
OnHomeHideoutChangedEvent
OnIncidentResolvedEvent
OnIssueOwnerChangedEvent
OnIssueUpdatedEvent
OnItemConsumedEvent
OnItemProducedEvent
OnItemsDiscardedByPlayerEvent
OnItemSoldEvent
OnItemsRefinedEvent
OnLootDistributedToPartyEvent
OnMainPartyPrisonerRecruitedEvent
OnMainPartyStarvingEvent
OnMapEventContinuityNeedsUpdateEvent
OnMapMarkerCreatedEvent
OnMapMarkerRemovedEvent
OnMarriageOfferCanceledEvent
OnMarriageOfferedToPlayerEvent
OnMercenaryServiceEndedEvent
OnMercenaryServiceStartedEvent
OnMissionEndedEvent
OnMissionStartedEvent
OnMobilePartyJoinedToSiegeEventEvent
OnMobilePartyLeftSiegeEventEvent
OnMobilePartyNavigationStateChangedEvent
OnMobilePartyRaftStateChangedEvent
OnNewGameCreatedEvent
OnNewGameCreatedPartialFollowUpEndEvent
OnNewGameCreatedPartialFollowUpEvent
OnNewIssueCreatedEvent
OnNewItemCraftedEvent
OnPartyAddedToMapEventEvent
OnPartyConsumedFoodEvent
OnPartyDisbandCanceledEvent
OnPartyDisbandedEvent
OnPartyDisbandStartedEvent
OnPartyJoinedArmyEvent
OnPartyLeaderChangedEvent
OnPartyLeaderChangeOfferCanceledEvent
OnPartyLeftArmyEvent
OnPartyRemovedEvent
OnPartySizeChangedEvent
OnPeaceOfferedToPlayerEvent
OnPeaceOfferResolvedEvent
OnPlayerArmyLeaderChangedBehaviorEvent
OnPlayerBattleEndEvent
OnPlayerBoardGameOverEvent
OnPlayerBodyPropertiesChangedEvent
OnPlayerCharacterChangedEvent
OnPlayerEarnedGoldFromAssetEvent
OnPlayerJoinedTournamentEvent
OnPlayerLearnsAboutHeroEvent
OnPlayerMetHeroEvent
OnPlayerPartyKnockedOrKilledTroopEvent
OnPlayerSiegeStartedEvent
OnPlayerTradeProfitEvent
OnPrisonerDonatedToSettlementEvent
OnPrisonerReleasedEvent
OnPrisonerSoldEvent
OnPrisonerTakenEvent
OnQuarterDailyPartyTick
OnQuestCompletedEvent
OnQuestStartedEvent
OnRansomOfferCancelledEvent
OnRansomOfferedToPlayerEvent
OnSaveOverEvent
OnSaveStartedEvent
OnSessionLaunchedEvent
OnSettlementLeftEvent
OnSettlementOwnerChangedEvent
OnShipCreatedEvent
OnShipDestroyedEvent
OnShipOwnerChangedEvent
OnShipRepairedEvent
OnSiegeAftermathAppliedEvent
OnSiegeBombardmentHitEvent
OnSiegeBombardmentWallHitEvent
OnSiegeEngineDestroyedEvent
OnSiegeEventEndedEvent
OnSiegeEventStartedEvent
OnTradeAgreementSignedEvent
OnTradeRumorIsTakenEvent
OnTroopGivenToSettlementEvent
OnTroopRecruitedEvent
OnTroopsDesertedEvent
OnTutorialCompletedEvent
OnUnitRecruitedEvent
OnVassalOrMercenaryServiceOfferCanceledEvent
OnVassalOrMercenaryServiceOfferedToPlayerEvent
PartyAttachedAnotherParty
PartyRemovedFromArmyEvent
PartyVisibilityChangedEvent
PerkOpenedEvent
PerkResetEvent
PersuasionProgressCommittedEvent
PlayerAgentSpawned
PlayerDesertedBattleEvent
PlayerEliminatedFromTournament
PlayerInventoryExchangeEvent
PlayerStartedTournamentMatch
PlayerStartRecruitmentEvent
PlayerStartTalkFromMenu
PlayerTraitChangedEvent
PlayerUpgradedTroopsEvent
PrisonersChangeInSettlement
QuarterHourlyTickEvent
QuestLogAddedEvent
RaidCompletedEvent
RebellionFinished
RebelliousClanDisbandedAtSettlement
RenownGained
RomanticStateChanged
RulingClanChanged
SettlementEntered
SiegeCompletedEvent
SiegeEngineBuiltEvent
TickEvent
TickPartialHourlyAiEvent
TournamentCancelled
TournamentFinished
TournamentStarted
TownRebelliosStateChanged
TrackDetectedEvent
TrackLostEvent
VillageBecomeNormal
VillageBeingRaided
VillageLooted
VillageStateChanged
WarDeclared
WeeklyTickEvent
WorkshopInitializedEvent
WorkshopOwnerChangedEvent
WorkshopTypeChangedEvent"
            .Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

        private static readonly HashSet<string> Captured = new HashSet<string>(StringComparer.Ordinal)
        {
            "MapEventStarted","MapEventEnded","RaidCompletedEvent","VillageBeingRaided","VillageLooted","VillageStateChanged",
            "WarDeclared","MakePeace","OnAllianceStartedEvent","OnAllianceEndedEvent","OnSettlementOwnerChangedEvent","OnGovernorChangedEvent",
            "HeroPrisonerTaken","HeroPrisonerReleased","HeroKilledEvent","HeroWounded","BeforeHeroesMarried","OnChildConceivedEvent","OnGivenBirthEvent",
            "OnClanChangedKingdomEvent","OnClanDefectedEvent","OnClanCreatedEvent","OnClanDestroyedEvent","ArmyCreated","ArmyDispersed",
            "MobilePartyCreated","MobilePartyDestroyed","OnPartyLeaderChangedEvent","TournamentStarted","TournamentFinished",
            "OnSiegeEventStartedEvent","OnSiegeEventEndedEvent","SiegeCompletedEvent","WorkshopOwnerChangedEvent","OnTroopRecruitedEvent",
            "OnItemSoldEvent","SettlementEntered","OnSettlementLeftEvent","OnShipDestroyedEvent","OnShipOwnerChangedEvent","OnShipCreatedEvent","OnShipRepairedEvent"
            ,"OnBarterAcceptedEvent","HeroRelationChanged","CharacterDefeated","RulingClanChanged","KingdomCreatedEvent","KingdomDestroyedEvent",
            "KingdomDecisionAdded","KingdomDecisionConcluded","RebellionFinished","TownRebelliosStateChanged","ArmyGathered","OnPartyJoinedArmyEvent",
            "PartyRemovedFromArmyEvent","OnMercenaryServiceStartedEvent","OnMercenaryServiceEndedEvent","OnHeroChangedClanEvent","ItemsLooted",
            "OnLootDistributedToPartyEvent","OnCaravanTransactionCompletedEvent","OnPrisonerSoldEvent","OnTroopsDesertedEvent","OnTroopGivenToSettlementEvent",
            "OnBuildingLevelChangedEvent","OnQuestStartedEvent","OnQuestCompletedEvent","OnNewIssueCreatedEvent","OnIssueUpdatedEvent","WorkshopTypeChangedEvent"
        };

        internal static void ValidateCurrentGameSurface()
        {
            try
            {
                List<string> names = CampaignEventNames();
                string hash = Hash(names);
                Assembly campaignAssembly = typeof(CampaignEvents).Assembly;
                string assemblyIdentity = "assembly="
                    + campaignAssembly.GetName().Name + " version="
                    + (campaignAssembly.GetName().Version?.ToString() ?? "unknown")
                    + " mvid=" + campaignAssembly.ManifestModule.ModuleVersionId
                    + " location=" + (campaignAssembly.Location ?? string.Empty);
                if (names.Count != ExpectedEventEndpointCount || !string.Equals(hash, ExpectedSurfaceHash, StringComparison.OrdinalIgnoreCase))
                {
                    HashSet<string> actual = new HashSet<string>(names,
                        StringComparer.Ordinal);
                    HashSet<string> expected = new HashSet<string>(
                        ExpectedEventEndpoints, StringComparer.Ordinal);
                    string missing = string.Join(",", expected
                        .Where(x => !actual.Contains(x))
                        .OrderBy(x => x, StringComparer.Ordinal));
                    string unexpected = string.Join(",", actual
                        .Where(x => !expected.Contains(x))
                        .OrderBy(x => x, StringComparer.Ordinal));
                    ReignLog.Warn("World-history coverage manifest no longer matches Bannerlord CampaignEvents. expectedCount=" + ExpectedEventEndpointCount + " actualCount=" + names.Count + " expectedHash=" + ExpectedSurfaceHash + " actualHash=" + hash + " missing=[" + missing + "] unexpected=[" + unexpected + "] " + assemblyIdentity);
                }
                else
                {
                    int captured = names.Count(x => Classify(x).Item1 == "capture");
                    int excluded = names.Count(x => Classify(x).Item1 == "exclude");
                    ReignLog.Info("World-history CampaignEvents coverage verified endpoints=" + names.Count + " capture=" + captured + " correlate=" + (names.Count - captured - excluded) + " exclude=" + excluded + " " + assemblyIdentity + ".");
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("World-history coverage validation failed: " + ex.Message);
            }
        }

        internal static IReadOnlyList<Tuple<string, string, string>> Manifest()
        {
            return CampaignEventNames().Select(name =>
            {
                Tuple<string, string> classification = Classify(name);
                return Tuple.Create(name, classification.Item1, classification.Item2);
            }).ToList();
        }

        private static Tuple<string, string> Classify(string name)
        {
            if (Captured.Contains(name)) return Tuple.Create("capture", "Dedicated structured native adapter.");
            if (name.StartsWith("Can", StringComparison.Ordinal) || name.StartsWith("IsSettlementBusy", StringComparison.Ordinal))
                return Tuple.Create("exclude", "Eligibility query; it does not prove that an action occurred.");
            if (name.IndexOf("Tick", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Menu", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Portrait", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Spawn", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Track", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Config", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Metadata", StringComparison.OrdinalIgnoreCase) >= 0)
                return Tuple.Create("exclude", "Engine, UI, query, or telemetry callback outside campaign-world action history.");
            if (name.IndexOf("Mission", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("CombatHit", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Agent", StringComparison.OrdinalIgnoreCase) >= 0)
                return Tuple.Create("exclude", "Mission telemetry is excluded; campaign outcomes are captured separately.");
            return Tuple.Create("correlate", "Covered through a canonical outcome adapter or hourly state reconciliation until a dedicated lossless adapter is required.");
        }

        private static List<string> CampaignEventNames()
        {
            return typeof(CampaignEvents).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(x => x.Name).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        }

        private static string Hash(List<string> names)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", names)));
                return string.Concat(bytes.Select(x => x.ToString("x2")));
            }
        }
    }
}
