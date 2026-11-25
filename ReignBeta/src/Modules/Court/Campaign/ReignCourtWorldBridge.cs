using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Actions;
using ReignBeta.WorldSimulation.Domain;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court.Campaign
{
    // World Simulation reaches Court only through ICourtWorldBridge (ReignServices.CourtWorld). Every member
    // forwards to the existing Court member; HasExposedAgentEvidence moved here from ReignArrestCampaignBehavior.
    public sealed partial class ReignCourtCampaignBehavior : ICourtWorldBridge
    {
        bool ICourtWorldBridge.IsRuleModeActive => IsRuleModeActive;
        Hero ICourtWorldBridge.ActiveSpymaster => ActiveSpymaster;
        int ICourtWorldBridge.ActiveSpymasterMissionCount => ActiveSpymasterMissionCount;
        int ICourtWorldBridge.SpymasterMissionCapacity => SpymasterMissionCapacity;

        IReadOnlyList<ReignActionOption> ICourtWorldBridge.AgreementSocialChoices(ActionClause clause) => AgreementSocialChoices(clause);
        List<string> ICourtWorldBridge.CheckCourtAgreement(ActionClause clause) => CheckCourtAgreement(clause);
        IEnumerable<string> ICourtWorldBridge.CheckCourtAgreementPackage(ActionAgreement agreement) => CheckCourtAgreementPackage(agreement);
        JObject ICourtWorldBridge.CaptureCourtAgreement(ActionClause clause) => CaptureCourtAgreement(clause);
        void ICourtWorldBridge.ApplyCourtAgreement(ActionAgreement agreement, ActionClause clause, AgreementStep step) => ApplyCourtAgreement(agreement, clause, step);
        bool ICourtWorldBridge.ObserveCourtAgreement(ActionClause clause, AgreementStep step) => ObserveCourtAgreement(clause, step);

        bool ICourtWorldBridge.IsProtectedDocketCustody(Hero prisoner) => IsProtectedDocketCustody(prisoner);
        void ICourtWorldBridge.ProcessEconomyShipmentArrivals() => ProcessEconomyShipmentArrivals();
        float ICourtWorldBridge.GetDocketVillageProductionFactor(Village village) => GetDocketVillageProductionFactor(village);
        float ICourtWorldBridge.GetSpymasterFoodFactor(Town town) => GetSpymasterFoodFactor(town);
        float ICourtWorldBridge.GetSpymasterConstructionFactor(Town town) => GetSpymasterConstructionFactor(town);
        float ICourtWorldBridge.GetSpymasterSecurityDelta(Town town) => GetSpymasterSecurityDelta(town);
        float ICourtWorldBridge.GetSpymasterLoyaltyDelta(Town town) => GetSpymasterLoyaltyDelta(town);
        bool ICourtWorldBridge.IsNobleVisitorReserved(Hero hero) => IsNobleVisitorReserved(hero);

        bool ICourtWorldBridge.HasExposedAgentEvidence(Hero accused, out string evidence)
        {
            evidence = string.Empty;
            string accusedId = accused?.StringId;
            var history = ReignServices.WorldHistory;
            if (string.IsNullOrWhiteSpace(accusedId) || !ReignCampaignIdentity.HasActiveCampaign()
                || ReignSaveSyncCoordinator.IsAlignmentPending || history?.TimelineReady != true
                || !ReferenceEquals(ReignServices.CourtWorld, this))
                return false;

            string timeline = history.TimelineId;
            ReignSpymasterState state = SpymasterState;
            ReignForeignAgentRecord agent = state?.ForeignAgents?.FirstOrDefault(candidate => candidate?.Exposed == true
                && !candidate.ExposurePending
                && string.Equals(candidate.AgentHeroStringId, accusedId, StringComparison.Ordinal)
                // Older native records can lack assignment scope. A typed assignment must match this timeline.
                && (string.IsNullOrEmpty(candidate.AssignmentTimelineId)
                    ? string.IsNullOrEmpty(candidate.AssignmentId)
                    : string.Equals(candidate.AssignmentTimelineId, timeline, StringComparison.Ordinal)));
            if (agent == null) return false;
            // Detected actions only retain a hero ID, without the assignment or timeline.
            evidence = "foreign_agent:" + agent.AgentHeroStringId;
            return true;
        }

        bool ICourtWorldBridge.HasPublicMurderArrestEvidence(Hero accused, out string evidence)
        {
            evidence = string.Empty;
            var history = ReignServices.WorldHistory;
            if (string.IsNullOrWhiteSpace(accused?.StringId) || !ReignCampaignIdentity.HasActiveCampaign()
                || ReignSaveSyncCoordinator.IsAlignmentPending || history?.TimelineReady != true
                || !ReferenceEquals(ReignServices.CourtWorld, this) || _rulerDocketState?.NobleMatters == null)
                return false;

            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = history.TimelineId;
            foreach (var matter in _rulerDocketState.NobleMatters)
            {
                evidence = matter?.PublicMurderArrestEvidenceId(campaignId, timelineId, accused.StringId) ?? string.Empty;
                if (evidence.Length > 0)
                    return true;
            }
            return false;
        }
    }
}
