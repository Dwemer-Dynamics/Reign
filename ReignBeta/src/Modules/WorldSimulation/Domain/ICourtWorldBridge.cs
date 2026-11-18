using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.WorldSimulation.Actions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.WorldSimulation.Domain
{
    /// <summary>
    /// What World Simulation needs from the Court module, so that World Simulation never names a Court type.
    /// Court implements it and publishes itself as <c>ReignServices.CourtWorld</c>; the slot is null when Court
    /// is absent or not yet registered. Members forward to the Court behaviour's own members unchanged.
    /// </summary>
    internal interface ICourtWorldBridge
    {
        // Spymaster eligibility for court-family actions.
        bool IsRuleModeActive { get; }
        Hero ActiveSpymaster { get; }
        int ActiveSpymasterMissionCount { get; }
        int SpymasterMissionCapacity { get; }

        // Court agreement clauses.
        IReadOnlyList<ReignActionOption> AgreementSocialChoices(ActionClause clause);
        List<string> CheckCourtAgreement(ActionClause clause);
        IEnumerable<string> CheckCourtAgreementPackage(ActionAgreement agreement);
        JObject CaptureCourtAgreement(ActionClause clause);
        void ApplyCourtAgreement(ActionAgreement agreement, ActionClause clause, AgreementStep step);
        bool ObserveCourtAgreement(ActionClause clause, AgreementStep step);

        // Arrests and custody.
        bool HasExposedAgentEvidence(Hero accused, out string evidence);
        bool IsProtectedDocketCustody(Hero prisoner);

        // Economy and settlement effects.
        void ProcessEconomyShipmentArrivals();
        float GetDocketVillageProductionFactor(Village village);
        float GetSpymasterFoodFactor(Town town);
        float GetSpymasterConstructionFactor(Town town);
        float GetSpymasterSecurityDelta(Town town);
        float GetSpymasterLoyaltyDelta(Town town);

        // Rulers.
        bool IsNobleVisitorReserved(Hero hero);
    }
}
