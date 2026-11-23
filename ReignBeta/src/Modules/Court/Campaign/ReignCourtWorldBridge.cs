using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
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
            ReignSpymasterState state = SpymasterState;
            ReignForeignAgentRecord agent = state?.ForeignAgents?.FirstOrDefault(candidate => candidate.Exposed
                && string.Equals(candidate.AgentHeroStringId, accused?.StringId,
                    StringComparison.OrdinalIgnoreCase));
            if (agent == null) return false;
            List<string> ids = new List<string> { "foreign_agent:" + agent.AgentHeroStringId };
            ids.AddRange((state.ForeignAgentActions ?? new List<ReignForeignAgentActionRecord>())
                .Where(action => action.Detected && string.Equals(action.AgentHeroStringId,
                    agent.AgentHeroStringId, StringComparison.OrdinalIgnoreCase))
                .Select(action => "foreign_agent_action:" + action.ActionId));
            evidence = string.Join(",", ids.Distinct(StringComparer.OrdinalIgnoreCase));
            return true;
        }
    }
}
