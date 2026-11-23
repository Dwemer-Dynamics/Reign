using ReignBeta.Characters.Campaign;
using ReignBeta.Dialogue.Campaign;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.ClanAccords;
using ReignBeta.Government.Campaign;
using ReignBeta.KingdomEvents.Campaign;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Platform.Campaign;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Actions;
using ReignBeta.WorldSimulation.Arrests;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Domain;
using ReignBeta.WorldSimulation.Economy;
using ReignBeta.WorldSimulation.News;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
using ReignBeta.Court.Campaign;
using ReignBeta.Court.WarCouncil;
#endif

namespace ReignBeta.Platform.Runtime
{
    /// <summary>
    /// Process-wide registry of the live campaign behaviours. Each slot is written by its behaviour at exactly the
    /// point that used to set its static <c>Instance</c> (constructor, RegisterEvents or OnLoaded), and cleared only
    /// where that behaviour cleared it before (PartyChatHistory in EndCampaign). Slots are never filled or cleared on
    /// game start or end. Each behaviour keeps an <c>Instance</c> property that forwards to its slot.
    /// </summary>
    internal static class ReignServices
    {
        // Characters
        internal static ReignCharacterEditorCampaignBehavior CharacterEditor { get; set; }
        internal static ReignCharacterKnowledgeCampaignBehavior CharacterKnowledge { get; set; }
        internal static ReignEncounteredResidentsCampaignBehavior EncounteredResidents { get; set; }
        internal static ReignFamilyCampaignBehavior Family { get; set; }
        internal static ReignNpcLivesBehavior NpcLives { get; set; }
        internal static ReignStartingChildrenCampaignBehavior StartingChildren { get; set; }
        internal static ReignTavernHouseCampaignBehavior TavernHouse { get; set; }
        internal static ReignWandererPopulationCampaignBehavior WandererPopulation { get; set; }

#if !REIGN_EXCLUDE_COURT
        // Court (compiled out with ExcludeCourt)
        internal static ReignCourtCampaignBehavior Court { get; set; }
        internal static ReignCourtNobleCampaignBehavior CourtNoble { get; set; }
        internal static ReignFamilyChambersCampaignBehavior FamilyChambers { get; set; }
        internal static ReignWarCouncilCampaignBehavior WarCouncil { get; set; }
#endif

        /// <summary>
        /// The Court behaviour seen through the World Simulation bridge. Set next to <c>Court</c>; null when
        /// Court is absent, so <c>CourtWorld?.X</c> yields what <c>ReignCourtCampaignBehavior.Instance?.X</c> did.
        /// </summary>
        internal static ICourtWorldBridge CourtWorld { get; set; }

        // Dialogue
        internal static ReignBanterCampaignBehavior Banter { get; set; }
        internal static ReignIndividualConversationBehavior IndividualConversation { get; set; }
        internal static ReignPartyChatHistoryBehavior PartyChatHistory { get; set; }
        internal static ReignRescueConversationBehavior RescueConversation { get; set; }

        // Diplomacy
        internal static ReignWorldDiplomacyCampaignBehavior WorldDiplomacy { get; set; }
        internal static ReignClanAccordsCampaignBehavior ClanAccords { get; set; }

        // Government, KingdomEvents, Persistence, Platform, Rebellion
        internal static ReignGovernmentCampaignBehavior Government { get; set; }
        internal static ReignKingdomEventsCampaignBehavior KingdomEvents { get; set; }
        internal static ReignSaveSyncCampaignBehavior SaveSync { get; set; }
        internal static ReignAICampaignBehavior AI { get; set; }
        internal static ReignCampaignPreparationCampaignBehavior CampaignPreparation { get; set; }
        internal static ReignRebellionCampaignBehavior Rebellion { get; set; }

        // Relationships and Reputation
        internal static ReignPlayerApproachCampaignBehavior PlayerApproach { get; set; }
        internal static ReignRelationshipCampaignBehavior Relationship { get; set; }
        internal static ReignCourtPersonalityReputationCampaignBehavior CourtPersonalityReputation { get; set; }
        internal static ReignRulerReputationCampaignBehavior RulerReputation { get; set; }
        internal static ReignSocialBalanceHarnessCampaignBehavior SocialBalanceHarness { get; set; }

        // WorldSimulation
        internal static ReignAgreementBehavior Agreement { get; set; }
        internal static ReignArrestCampaignBehavior Arrest { get; set; }
        internal static ReignSocialEventsCampaignBehavior SocialEvents { get; set; }
        internal static ReignWorldHistoryCampaignBehavior WorldHistory { get; set; }
        internal static ReignCampaignCommandBehavior CampaignCommand { get; set; }
        internal static ReignEconomyCampaignBehavior Economy { get; set; }
        internal static ReignNewsCampaignBehavior News { get; set; }
        internal static ReignTemporaryPartyGuestCampaignBehavior TemporaryPartyGuest { get; set; }
        internal static ReignRulerCampaignBehavior Ruler { get; set; }
        internal static ReignFollowCampaignBehavior Follow { get; set; }
        internal static ReignGoldTransferService GoldTransferService { get; set; }
        internal static ReignPersonalItemService PersonalItemService { get; set; }
    }
}
