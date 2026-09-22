using System.Collections.Generic;
using ReignBeta.Campaign;
using ReignBeta.Events;
using ReignBeta.Family;
using ReignBeta.Government;
using ReignBeta.Knowledge;
using ReignBeta.PartyAgency;
using ReignBeta.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.SaveSystem;

namespace ReignBeta.Save
{
    public sealed class ReignBetaSaveDefiner : SaveableTypeDefiner
    {
        public ReignBetaSaveDefiner() : base(9876600)
        {
        }

        protected override void DefineClassTypes()
        {
            AddClassDefinition(typeof(ReignWorldActionRecord), 1);
            AddClassDefinition(typeof(ReignDiplomaticAgreementRecord), 2);
            AddClassDefinition(typeof(ReignWarOriginRecord), 3);
            AddClassDefinition(typeof(ReignTreatyObligationRecord), 4);
            AddClassDefinition(typeof(SocialEventRecord), 20);
            AddClassDefinition(typeof(ReignConceptionRecord), 30);
            AddClassDefinition(typeof(ReignParentageRecord), 31);
            AddClassDefinition(typeof(ReignRebellionMovementRecord), 40);
            AddClassDefinition(typeof(ReignRebellionMembershipRecord), 41);
            AddClassDefinition(typeof(ReignNegotiatedActionRecord), 42);
            AddClassDefinition(typeof(ReignRebellionWeeklyRollRecord), 43);
            AddClassDefinition(typeof(ReignKingdomEventRecord), 44);
            AddClassDefinition(typeof(ReignKingdomEventTestLedger), 45);
            AddClassDefinition(typeof(ReignRebellionPlotRecord), 46);
            AddClassDefinition(typeof(ReignRebellionPledgeRecord), 47);
            AddClassDefinition(typeof(ReignRebellionSummonsRecord), 48);
            AddClassDefinition(typeof(ReignCharacterKnowledgeRecord), 61);
            AddClassDefinition(typeof(ReignArrestCase), 67);
            AddClassDefinition(typeof(ReignTemporaryPartyGuestRecord), 68);
            AddClassDefinition(typeof(ReignGovernmentStateRecord), 70);
            AddClassDefinition(typeof(ReignGovernmentPartyRecord), 71);
            AddClassDefinition(typeof(ReignGovernmentSeatRecord), 72);
            AddClassDefinition(typeof(ReignGovernmentResolutionRecord), 73);
            AddClassDefinition(typeof(ReignGovernmentPressureRecord), 74);
            AddClassDefinition(typeof(ReignGovernmentLobbyRecord), 75);
            AddClassDefinition(typeof(ReignGovernmentMeetingRecord), 76);
#if !REIGN_EXCLUDE_COURT
            AddClassDefinition(typeof(CourtSession), 50);
            AddClassDefinition(typeof(CourtMatter), 51);
            AddClassDefinition(typeof(CourtAgendaItem), 52);
            AddClassDefinition(typeof(CourtDecisionOption), 53);
            AddClassDefinition(typeof(CourtOfficeAssignment), 54);
            AddClassDefinition(typeof(AmbassadorPosting), 55);
            AddClassDefinition(typeof(IntelligenceOperation), 56);
            AddClassDefinition(typeof(CourtObligation), 57);
            AddClassDefinition(typeof(CourtPlot), 58);
            AddClassDefinition(typeof(CourtCounterSample), 59);
            AddClassDefinition(typeof(CourtRegentAssignment), 60);
            AddClassDefinition(typeof(KingdomCapitalDesignation), 62);
            AddClassDefinition(typeof(ForeignAmbassadorPosting), 63);
            AddClassDefinition(typeof(CapitalAmbassadorTestLedger), 64);
            AddClassDefinition(typeof(CastleRoomSessionRecord), 65);
            AddClassDefinition(typeof(CastleBathHistoryRecord), 66);
            AddClassDefinition(typeof(FamilyChambersSessionRecord), 69);
#endif
        }

        protected override void DefineContainerDefinitions()
        {
            ConstructContainerDefinition(typeof(List<ReignWorldActionRecord>));
            ConstructContainerDefinition(typeof(List<ReignDiplomaticAgreementRecord>));
            ConstructContainerDefinition(typeof(List<ReignWarOriginRecord>));
            ConstructContainerDefinition(typeof(List<ReignTreatyObligationRecord>));
            ConstructContainerDefinition(typeof(List<SocialEventRecord>));
            ConstructContainerDefinition(typeof(List<ReignConceptionRecord>));
            ConstructContainerDefinition(typeof(List<ReignParentageRecord>));
            ConstructContainerDefinition(typeof(List<ReignRebellionMovementRecord>));
            ConstructContainerDefinition(typeof(List<ReignRebellionMembershipRecord>));
            ConstructContainerDefinition(typeof(List<ReignNegotiatedActionRecord>));
            ConstructContainerDefinition(typeof(List<ReignRebellionWeeklyRollRecord>));
            ConstructContainerDefinition(typeof(List<ReignKingdomEventRecord>));
            ConstructContainerDefinition(typeof(List<ReignKingdomEventTestLedger>));
            ConstructContainerDefinition(typeof(List<ReignRebellionPlotRecord>));
            ConstructContainerDefinition(typeof(List<ReignRebellionPledgeRecord>));
            ConstructContainerDefinition(typeof(List<ReignRebellionSummonsRecord>));
            ConstructContainerDefinition(typeof(List<ReignCharacterKnowledgeRecord>));
            ConstructContainerDefinition(typeof(List<ReignArrestCase>));
            ConstructContainerDefinition(typeof(List<ReignTemporaryPartyGuestRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentStateRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentPartyRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentSeatRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentResolutionRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentPressureRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentLobbyRecord>));
            ConstructContainerDefinition(typeof(List<ReignGovernmentMeetingRecord>));
#if !REIGN_EXCLUDE_COURT
            ConstructContainerDefinition(typeof(List<CourtMatter>));
            ConstructContainerDefinition(typeof(List<CourtAgendaItem>));
            ConstructContainerDefinition(typeof(List<CourtDecisionOption>));
            ConstructContainerDefinition(typeof(List<CourtOfficeAssignment>));
            ConstructContainerDefinition(typeof(List<AmbassadorPosting>));
            ConstructContainerDefinition(typeof(List<IntelligenceOperation>));
            ConstructContainerDefinition(typeof(List<CourtObligation>));
            ConstructContainerDefinition(typeof(List<CourtPlot>));
            ConstructContainerDefinition(typeof(List<CourtCounterSample>));
            ConstructContainerDefinition(typeof(List<CourtRegentAssignment>));
            ConstructContainerDefinition(typeof(List<KingdomCapitalDesignation>));
            ConstructContainerDefinition(typeof(List<ForeignAmbassadorPosting>));
            ConstructContainerDefinition(typeof(List<CapitalAmbassadorTestLedger>));
            ConstructContainerDefinition(typeof(List<CastleRoomSessionRecord>));
            ConstructContainerDefinition(typeof(List<CastleBathHistoryRecord>));
            ConstructContainerDefinition(typeof(List<FamilyChambersSessionRecord>));
#endif
            ConstructContainerDefinition(typeof(List<string>));
            ConstructContainerDefinition(typeof(List<int>));
        }
    }
}
