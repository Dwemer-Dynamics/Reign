using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using TaleWorlds.Core;
using ReignBeta.Campaign;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using ReignBeta.Characters.World;
using ReignBeta.Government.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.World;
using ReignBeta.Shared.Dialogue;
using ReignBeta.WorldSimulation.Actions;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Diplomacy.World
{
    public static class ReignDiplomacyExecutor
    {
        private const string OrdinaryRansomMarker = "ordinaryRansomPackage";

        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            // An interrupted ransom can leave a formerly valid captive already released.
            // Keep its saved attempt authoritative ahead of ordinary validation.
            ReignActionResult priorRansom = ReadOrdinaryRansom(action, publish: true);
            if (priorRansom != null) return priorRansom;
            if (action?.Type == ReignWorldActionType.DiplomacyRansomPackage
                && (action.IsTerminal || !TryReadRansomExecution(action, out _)))
                return UnverifiedRansomPackage();

            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.DiplomacyValidationFailed(action, validationFailure);
            }
            if (!ReignGovernmentActionGate.TryAuthorize(action, out ReignActionResult governmentResult))
                return governmentResult;

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.DiplomacyDeclareWar:
                    if (ReignServices.AI != null)
                        ReignServices.AI.DeclareWarWithOrigin(actor, target, "direct",
                            action.Reason, action.ActionId, string.Empty, false);
                    else DeclareWarAction.ApplyByKingdomDecision(actor, target);
                    if (!actor.IsAtWarWith(target))
                        return ReignActionResult.FailTerminal("Native diplomacy did not establish war between the kingdoms.",
                            "declare_war_verification_failed", "state_verification");
                    return ReignActionResult.Done(actor.InformalName + " declared war on " + target.InformalName + ".")
                        .WithEffect("war_declared", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);

                case ReignWorldActionType.DiplomacyMakePeace:
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
                    if (actor.IsAtWarWith(target))
                        return ReignActionResult.FailTerminal("Native diplomacy did not establish peace between the kingdoms.",
                            "make_peace_verification_failed", "state_verification");
                    return ReignActionResult.Done(actor.InformalName + " made peace with " + target.InformalName + ".")
                        .WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);

                case ReignWorldActionType.DiplomacyOfferTributePeace:
                    int offeredDailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", 0);
                    int offeredTributeDays = ReadIntTerm(action.TermsJson, "durationDays", 30);
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, offeredDailyTribute, offeredTributeDays);
                    return ReignActionResult.Done(actor.InformalName + " made tribute peace with " + target.InformalName + ".")
                        .WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "dailyTribute=" + offeredDailyTribute + ";durationDays=" + offeredTributeDays);

                case ReignWorldActionType.DiplomacyDemandReparationsPeace:
                    string reparationsObservation = ApplyReparationsAndPeace(actor, target, action);
                    return WithPeaceAndReparations(
                        ReignActionResult.Done(target.InformalName + " accepted reparations demanded by " + actor.InformalName + "."),
                        actor,
                        target,
                        action, reparationsObservation);

                case ReignWorldActionType.DiplomacyDemandSettlementPeace:
                    List<Settlement> transferredSettlements = new List<Settlement>();
                    int transferred = TransferDemandedSettlements(actor, target, action, transferredSettlements);
                    string settlementPeaceObservation = ApplyReparationsAndPeace(actor, target, action);
                    RecordAgreement(action, "settlement_surrender_peace", 0f, true);
                    return WithTransferredSettlements(
                        WithPeaceAndReparations(
                            ReignActionResult.Done(target.InformalName + " surrendered " + transferred + " settlement(s) to " + actor.InformalName + " for peace."),
                            actor,
                            target,
                            action, settlementPeaceObservation)
                            .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=settlement_surrender_peace"),
                        transferredSettlements);

                case ReignWorldActionType.DiplomacyDemandSurrenderPeace:
                    List<Settlement> surrenderedSettlements = new List<Settlement>();
                    int surrendered = TransferDemandedSettlements(actor, target, action, surrenderedSettlements);
                    string surrenderObservation = ApplyReparationsAndPeace(actor, target, action);
                    bool destroyed = DestroyTargetKingdomIfLandless(target);
                    RecordAgreement(action, "full_surrender_peace", 0f, true);
                    return WithTransferredSettlements(
                        WithPeaceAndReparations(
                            ReignActionResult.Done(target.InformalName + " accepted surrender terms from " + actor.InformalName + ", including " + surrendered + " settlement(s)" + (destroyed ? ", and the kingdom was dissolved." : ".")),
                            actor,
                            target,
                            action, surrenderObservation)
                            .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=full_surrender_peace")
                            .WithEffect(destroyed ? "kingdom_destroyed" : "kingdom_not_destroyed", "kingdom", target.StringId, target.InformalName.ToString(), destroyed ? "target became landless" : "target still has land or was already eliminated"),
                        surrenderedSettlements);

                case ReignWorldActionType.DiplomacyRecordPromise:
                    RecordAgreement(action, "promise", ReadFloatTerm(action.TermsJson, "durationDays", 0f), ReadBoolTerm(action.TermsJson, "isPublic", false));
                    return ReignActionResult.Done("Diplomatic promise recorded: " + action.Reason)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=promise");

                case ReignWorldActionType.DiplomacySignTradeAgreement:
                    StartNativeTradeAgreement(actor, target, action);
                    RecordAgreement(action, "trade_agreement", ReadFloatTerm(action.TermsJson, "durationDays", 120f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a trade agreement with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=trade_agreement")
                        .WithEffect("native_trade_agreement_established", "kingdom", target.StringId, target.InformalName.ToString(), "durationDays=" + ReadFloatTerm(action.TermsJson, "durationDays", 120f).ToString(CultureInfo.InvariantCulture));

                case ReignWorldActionType.DiplomacySignNonAggressionPact:
                    RecordAgreement(action, "non_aggression_pact", ReadFloatTerm(action.TermsJson, "durationDays", 90f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a non-aggression pact with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=non_aggression_pact");

                case ReignWorldActionType.DiplomacySignAlliance:
                    StartNativeAlliance(actor, target, action);
                    RecordAgreement(action, "alliance", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed an alliance with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=alliance")
                        .WithEffect("native_alliance_established", "kingdom", target.StringId, target.InformalName.ToString(), "durationDays=" + ReadFloatTerm(action.TermsJson, "durationDays", 180f).ToString(CultureInfo.InvariantCulture));

                case ReignWorldActionType.DiplomacySignDefensivePact:
                    RecordAgreement(action, "defensive_pact", ReadFloatTerm(action.TermsJson, "durationDays", 120f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a defensive pact with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=defensive_pact");

                case ReignWorldActionType.DiplomacySignTemporaryTruce:
                    return ReignActionResult.FailTerminal("Temporary truce is retired from the beta action catalog.", "retired_action", "retired_action");

                case ReignWorldActionType.DiplomacyBreakTreaty:
                    return BreakTreaties(actor, target, action);

                case ReignWorldActionType.DiplomacyExchangePrisoners:
                    return ExchangePrisoners(actor, target, action);

                case ReignWorldActionType.DiplomacyRansomPackage:
                    return RansomPackage(actor, target, action);

                case ReignWorldActionType.DiplomacyHostageGuarantee:
                    RecordAgreement(action, "hostage_guarantee", ReadFloatTerm(action.TermsJson, "durationDays", 60f), ReadBoolTerm(action.TermsJson, "isPublic", false));
                    return ReignActionResult.Done(actor.InformalName + " recorded a hostage guarantee with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=hostage_guarantee");

                case ReignWorldActionType.DiplomacyWarIndemnity:
                    int indemnityGold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "indemnityGold", 0));
                    string indemnityGoldObservation = ApplyGoldTransfer(target.Leader, actor.Leader, indemnityGold);
                    RecordAgreement(action, "war_indemnity", ReadFloatTerm(action.TermsJson, "durationDays", 0f), true);
                    return ReignActionResult.Done(target.InformalName + " paid a war indemnity to " + actor.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", actor.Leader?.StringId ?? string.Empty, actor.Leader?.Name?.ToString() ?? string.Empty, indemnityGoldObservation)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=war_indemnity");

                case ReignWorldActionType.DiplomacyRecognizeConquest:
                    RecordAgreement(action, "recognize_conquest", ReadFloatTerm(action.TermsJson, "durationDays", 0f), true);
                    return ReignActionResult.Done(target.InformalName + " recognized " + actor.InformalName + "'s conquest claim.")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=recognize_conquest");

                case ReignWorldActionType.DiplomacyReturnOccupiedSettlement:
                    return ReturnOccupiedSettlement(actor, target, action);

                case ReignWorldActionType.DiplomacyDemilitarizedBorder:
                    RecordAgreement(action, "demilitarized_border", ReadFloatTerm(action.TermsJson, "durationDays", 60f), true);
                    return ReignActionResult.Done(actor.InformalName + " agreed to a demilitarized border with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=demilitarized_border");

                case ReignWorldActionType.DiplomacyTradeEmbargo:
                    return ReignActionResult.FailTerminal("Trade embargo is retired from the beta action catalog.", "retired_action", "retired_action");

                case ReignWorldActionType.DiplomacyCaravanProtectionAgreement:
                    RecordAgreement(action, "caravan_protection_agreement", ReadFloatTerm(action.TermsJson, "durationDays", 60f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a caravan protection agreement with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=caravan_protection_agreement");

                case ReignWorldActionType.DiplomacySupplyAgreement:
                    if (IsNavalSupplyAgreement(action))
                    {
                        return TransferNavalSupplyAgreement(actor, target, action);
                    }

                    int supplyGold = ReadIntTerm(action.TermsJson, "gold", 0);
                    string supplyGoldObservation = ApplyGoldTransfer(actor.Leader, target.Leader, supplyGold);
                    RecordAgreement(action, "supply_agreement", ReadFloatTerm(action.TermsJson, "durationDays", 60f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a supply agreement with " + target.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, supplyGoldObservation)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=supply_agreement");

                case ReignWorldActionType.DiplomacyLoanOrSubsidy:
                    int loanGold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "loanGold", 0));
                    string loanGoldObservation = ApplyGoldTransfer(actor.Leader, target.Leader, loanGold);
                    RecordAgreement(action, "loan_or_subsidy", ReadFloatTerm(action.TermsJson, "durationDays", 120f), ReadBoolTerm(action.TermsJson, "isPublic", true));
                    return ReignActionResult.Done(actor.InformalName + " sent a loan or subsidy to " + target.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, loanGoldObservation)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=loan_or_subsidy");

                case ReignWorldActionType.DiplomacyPayToStayNeutral:
                    int neutralGold = ReadIntTerm(action.TermsJson, "gold", 0);
                    string neutralGoldObservation = ApplyGoldTransfer(actor.Leader, target.Leader, neutralGold);
                    RecordAgreement(action, "paid_neutrality", ReadFloatTerm(action.TermsJson, "durationDays", 60f), ReadBoolTerm(action.TermsJson, "isPublic", false));
                    return ReignActionResult.Done(actor.InformalName + " paid " + target.InformalName + " to stay neutral.")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, neutralGoldObservation)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=paid_neutrality");

                case ReignWorldActionType.DiplomacyPayToJoinWar:
                    return PayToJoinWar(actor, target, action);

                case ReignWorldActionType.DiplomacyGuaranteeIndependence:
                    RecordAgreement(action, "guarantee_independence", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                    return ReignActionResult.Done(actor.InformalName + " guaranteed the independence of " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=guarantee_independence");

                case ReignWorldActionType.DiplomacyProtectorateOrVassalage:
                    RecordAgreement(action, "protectorate_or_vassalage", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                    return ReignActionResult.Done(actor.InformalName + " formalized a protectorate or vassalage agreement with " + target.InformalName + ".")
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=protectorate_or_vassalage");

                case ReignWorldActionType.DiplomacyPackage:
                    return DiplomaticPackage(actor, target, action);

                case ReignWorldActionType.DiplomacyBackRebellion:
                    return BackRebellion(actor, target, action);

                default:
                    return ReignActionResult.FailTerminal("Unsupported diplomacy action type: " + action.Type, "unsupported_action", "unsupported_action");
            }
        }

        private static ReignActionResult BackRebellion(Kingdom sponsor, Kingdom parent, ReignWorldActionRecord action)
        {
            string rebelKingdomId = ReadStringTerm(action.TermsJson, "rebelKingdomId", string.Empty);
            string movementId = ReadStringTerm(action.TermsJson, "movementId", string.Empty);
            Kingdom rebels = ReignObjectResolver.FindKingdom(rebelKingdomId);
            var rebellion = ReignServices.Rebellion;
            if (sponsor == null || rebellion?.FindMutableBackingMovement(movementId, parent, rebels) == null)
                return ReignActionResult.FailTerminal("Rebellion backing target is obsolete.", "backing_obsolete", "backing_obsolete");

            ReignWorldActionRecord agreementAction = new ReignWorldActionRecord
            {
                ActionId = action.ActionId,
                ActorKingdomStringId = sponsor.StringId,
                TargetKingdomStringId = rebels.StringId,
                ActorClanStringId = sponsor.RulingClan?.StringId ?? string.Empty,
                TargetClanStringId = rebels.RulingClan?.StringId ?? string.Empty,
                Reason = action.Reason,
                TermsJson = action.TermsJson
            };
            ReignServices.AI?.RecordAgreementFromAction(agreementAction,
                "rebellion_backing", 0f, true);
            if (!rebellion.RecordForeignBacking(movementId, sponsor.StringId, action.ActionId))
                return ReignActionResult.FailTerminal("Rebellion backing authority changed.", "backing_obsolete", "backing_obsolete");
            if (!sponsor.IsAtWarWith(parent))
            {
                if (ReignServices.AI != null)
                    ReignServices.AI.DeclareWarWithOrigin(sponsor, parent,
                        "rebellion_backing", action.Reason, action.ActionId, string.Empty, false);
                else DeclareWarAction.ApplyByKingdomDecision(sponsor, parent);
            }

            return ReignActionResult.Done(sponsor.InformalName + " agreed to back " + rebels.InformalName
                    + " against " + parent.InformalName + ".")
                .WithEffect("rebellion_backing", "kingdom", rebels.StringId, rebels.InformalName.ToString(),
                    "movementId=" + movementId + ";parentKingdomId=" + parent.StringId)
                .WithEffect("treaty_recorded", "kingdom", rebels.StringId, rebels.InformalName.ToString(),
                    "kind=rebellion_backing");
        }

        private static ReignActionResult WithPeaceAndReparations(ReignActionResult result, Kingdom actor, Kingdom target, ReignWorldActionRecord action, string goldObservation)
        {
            int lumpGold = ReadIntTerm(action.TermsJson, "reparationsGold", 0);
            int dailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", ReadIntTerm(action.TermsJson, "reparationsDailyTribute", 0));
            int durationDays = ReadIntTerm(action.TermsJson, "durationDays", 30);

            result.WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);
            if (lumpGold > 0)
            {
                result.WithEffect("gold_transfer", "hero", actor.Leader?.StringId ?? string.Empty, actor.Leader?.Name?.ToString() ?? string.Empty, goldObservation);
            }

            if (dailyTribute > 0)
            {
                result.WithEffect("tribute", "kingdom", target.StringId, target.InformalName.ToString(), "dailyTribute=" + dailyTribute + ";durationDays=" + durationDays);
            }

            return result;
        }

        private static ReignActionResult WithTransferredSettlements(ReignActionResult result, List<Settlement> settlements)
        {
            int count = settlements == null ? 0 : settlements.Count;
            result.WithDiagnostic("settlementsTransferred", count.ToString(CultureInfo.InvariantCulture));
            if (settlements == null)
            {
                return result;
            }

            foreach (Settlement settlement in settlements)
            {
                if (settlement == null)
                {
                    continue;
                }

                result.WithChangedEntity("settlement", settlement.StringId, settlement.Name.ToString(), "owner_changed");
                result.WithEffect("settlement_transferred", "settlement", settlement.StringId, settlement.Name.ToString(), "newOwnerKingdomId=" + (settlement.MapFaction?.StringId ?? string.Empty));
            }

            return result;
        }

        private static string ApplyReparationsAndPeace(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            int lumpGold = ReadIntTerm(action.TermsJson, "reparationsGold", 0);
            string goldObservation = "";
            if (lumpGold > 0 && target.Leader != null && actor.Leader != null)
            {
                goldObservation = ApplyGoldTransfer(target.Leader, actor.Leader, lumpGold);
            }

            int dailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", ReadIntTerm(action.TermsJson, "reparationsDailyTribute", 0));
            int durationDays = ReadIntTerm(action.TermsJson, "durationDays", 30);
            if (dailyTribute > 0)
            {
                MakePeaceAction.ApplyByKingdomDecision(target, actor, dailyTribute, durationDays);
                return goldObservation;
            }

            MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
            return goldObservation;
        }

        private static int TransferDemandedSettlements(Kingdom actor, Kingdom target, ReignWorldActionRecord action, List<Settlement> transferredSettlements)
        {
            List<string> settlementIds = ReadStringListTerm(action.TermsJson, "settlementIds");
            if (ReadBoolTerm(action.TermsJson, "allTargetFortifications", false))
            {
                foreach (Settlement settlement in Settlement.All.Where(x => x != null && x.IsFortification && x.MapFaction == target))
                {
                    if (!settlementIds.Contains(settlement.StringId))
                    {
                        settlementIds.Add(settlement.StringId);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            int transferred = 0;
            foreach (string settlementId in settlementIds.Distinct())
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null || !settlement.IsFortification || settlement.MapFaction != target)
                {
                    continue;
                }

                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, actor.Leader);
                transferredSettlements?.Add(settlement);
                transferred++;
            }

            return transferred;
        }

        private static bool DestroyTargetKingdomIfLandless(Kingdom target)
        {
            if (target == null || target.IsEliminated)
            {
                return false;
            }

            bool hasFortification = Settlement.All.Any(x => x != null && x.IsFortification && x.MapFaction == target);
            if (hasFortification)
            {
                return false;
            }

            DestroyKingdomAction.Apply(target);
            return true;
        }

        private static void StartNativeTradeAgreement(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            ITradeAgreementsCampaignBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
            if (behavior == null)
            {
                throw new System.InvalidOperationException("The native trade agreement system is unavailable; no agreement was recorded.");
            }

            float days = ReadFloatTerm(action.TermsJson, "durationDays", 120f);
            TradeAgreementsCampaignBehavior.TradeAgreement existing;
            if (behavior.HasTradeAgreement(actor, target, out existing))
            {
                double remainingDays = behavior.GetTradeAgreementEndDate(actor, target).ToDays - CampaignTime.Now.ToDays;
                if (System.Math.Abs(remainingDays - days) > 0.001d)
                    throw new System.InvalidOperationException("An existing native trade agreement has a different duration; the proposed renewal has not been executed.");
                return;
            }

            behavior.MakeTradeAgreement(actor, target, CampaignTime.Days(days));
            if (!behavior.HasTradeAgreement(actor, target, out existing))
                throw new System.InvalidOperationException("The native trade agreement did not establish the accepted agreement.");
        }

        private static void StartNativeAlliance(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            IAllianceCampaignBehavior behavior = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
            if (behavior == null)
            {
                throw new System.InvalidOperationException("The native alliance system is unavailable; no alliance was recorded.");
            }

            float days = ReadFloatTerm(action.TermsJson, "durationDays", 180f);
            if (float.IsNaN(days) || float.IsInfinity(days) || days <= 0)
                throw new System.InvalidOperationException("The accepted alliance duration must be finite and positive.");
            if (behavior.IsAllyWithKingdom(actor, target))
            {
                double remainingDays = behavior.GetAllianceEndDate(actor, target).ToDays - CampaignTime.Now.ToDays;
                if (System.Math.Abs(remainingDays - days) > 0.001d)
                    throw new System.InvalidOperationException("An existing native alliance has a different duration; the proposed renewal has not been executed.");
                return;
            }
            ReignBeta.Diplomacy.Runtime.ReignDiplomacyPatches.StartAcceptedAlliance(behavior, actor, target, days);
            if (!behavior.IsAllyWithKingdom(actor, target))
                throw new System.InvalidOperationException("The native alliance action did not establish the accepted alliance.");
            if (System.Math.Abs(behavior.GetAllianceEndDate(actor, target).ToDays - CampaignTime.Now.ToDays - days) > 0.001d)
                throw new System.InvalidOperationException("The native alliance duration does not match the accepted terms; no successful agreement was recorded.");
        }

        private static ReignActionResult BreakTreaties(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            string kind = ReadStringTerm(action.TermsJson, "agreementKind", ReadStringTerm(action.TermsJson, "kind", string.Empty));
            int ended = ReignServices.AI?.EndAgreementsBetween(actor.StringId, target.StringId,
                kind, "explicit_breach", actor.StringId, string.Empty) ?? 0;

            bool all = string.IsNullOrWhiteSpace(kind);
            if (all || kind == "trade_agreement")
            {
                ITradeAgreementsCampaignBehavior trade = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<ITradeAgreementsCampaignBehavior>();
                TradeAgreementsCampaignBehavior.TradeAgreement existing;
                if (trade != null && trade.HasTradeAgreement(actor, target, out existing))
                {
                    trade.EndTradeAgreement(actor, target);
                    ended++;
                }
            }

            if (all || kind == "alliance")
            {
                IAllianceCampaignBehavior alliance = TaleWorlds.CampaignSystem.Campaign.Current?.GetCampaignBehavior<IAllianceCampaignBehavior>();
                if (alliance != null && alliance.IsAllyWithKingdom(actor, target))
                {
                    alliance.EndAlliance(actor, target);
                    ended++;
                }
            }

            return ReignActionResult.Done(actor.InformalName + " broke " + ended + " treaty/agreement record(s) with " + target.InformalName + ".")
                .WithEffect("treaty_broken", "kingdom", target.StringId, target.InformalName.ToString(), "kind=" + (string.IsNullOrWhiteSpace(kind) ? "all" : kind) + ";ended=" + ended);
        }

        private static ReignActionResult ExchangePrisoners(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            JObject execution;
            try { execution = JObject.Parse(string.IsNullOrWhiteSpace(action.ExecutionSnapshotJson) ? "{}" : action.ExecutionSnapshotJson); }
            catch (Newtonsoft.Json.JsonException) { return UnverifiedPrisonerExchange(); }

            // The saved action and the native prisoners belong to the same campaign.
            // A save taken mid-exchange must not select a new batch on reload.
            const string exchangeMarker = "ordinaryPrisonerExchange";
            if (execution.Property(exchangeMarker) != null) return UnverifiedPrisonerExchange();

            var recorder = ReignServices.AI;
            Hero actorRuler = actor?.Leader, targetRuler = target?.Leader;
            bool AuthorityIsCurrent() => recorder != null && ReignServices.AI == recorder
                && actor != null && target != null && actor != target && !actor.IsEliminated && !target.IsEliminated
                && actorRuler?.IsAlive == true && targetRuler?.IsAlive == true
                && actor.Leader == actorRuler && target.Leader == targetRuler;
            if (!AuthorityIsCurrent())
                return ReignActionResult.ValidationFailed("The two prisoner exchange authorities are no longer available.");

            int maxEachSide = ReadIntTerm(action.TermsJson, "maxEachSide", 3);
            var actorPrisoners = CapturePrisonerExchange(actor, target, maxEachSide);
            var targetPrisoners = CapturePrisonerExchange(target, actor, maxEachSide);
            var prisoners = actorPrisoners.Concat(targetPrisoners).ToList();
            if (prisoners.Count == 0 || prisoners.Any(prisoner => !prisoner.IsInExpectedCustody()))
                return ReignActionResult.ValidationFailed("No complete, eligible prisoner exchange is available in the current custody rosters.");

            execution[exchangeMarker] = new JObject
            {
                ["actorKingdomId"] = actor.StringId, ["targetKingdomId"] = target.StringId,
                ["actorRulerId"] = actorRuler.StringId, ["targetRulerId"] = targetRuler.StringId,
                ["actorPrisonerIds"] = new JArray(actorPrisoners.Select(prisoner => prisoner.Hero.StringId)),
                ["targetPrisonerIds"] = new JArray(targetPrisoners.Select(prisoner => prisoner.Hero.StringId))
            };
            action.ExecutionSnapshotJson = execution.ToString(Newtonsoft.Json.Formatting.None);

            try
            {
                foreach (PrisonerExchangeRelease prisoner in prisoners)
                {
                    if (!AuthorityIsCurrent() || !prisoner.IsInExpectedCustody()) return UnverifiedPrisonerExchange();
                    EndCaptivityAction.ApplyByPeace(prisoner.Hero, prisoner.HolderKingdom == actor ? actorRuler : targetRuler);
                    if (!prisoner.WasReleased()) return UnverifiedPrisonerExchange();
                }
                if (!AuthorityIsCurrent() || prisoners.Any(prisoner => !prisoner.WasReleased()))
                    return UnverifiedPrisonerExchange();
                if (recorder.RecordAgreementFromAction(action, "prisoner_exchange", 0f, true) == null)
                    return UnverifiedPrisonerExchange();
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Prisoner exchange interrupted action=" + action.ActionId, ex);
                return UnverifiedPrisonerExchange();
            }
            return ReignActionResult.Done(actor.InformalName + " and " + target.InformalName + " exchanged prisoners. Released=" + prisoners.Count + ".")
                .WithEffect("prisoners_released", "kingdom", actor.StringId, actor.InformalName.ToString(), "released=" + actorPrisoners.Count)
                .WithEffect("prisoners_released", "kingdom", target.StringId, target.InformalName.ToString(), "released=" + targetPrisoners.Count)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=prisoner_exchange");
        }

        private static ReignActionResult UnverifiedPrisonerExchange()
        {
            return ReignActionResult.FailTerminal("The prisoner exchange could not be fully verified. Do not repeat this action automatically.",
                "prisoner_exchange_unverified", "state_verification");
        }

        private static List<PrisonerExchangeRelease> CapturePrisonerExchange(Kingdom prisonerKingdom,
            Kingdom holderKingdom, int maxCount)
        {
            return Hero.AllAliveHeroes
                .Where(hero => hero != null && hero.IsPrisoner && hero.Clan?.Kingdom == prisonerKingdom
                    && hero.PartyBelongedToAsPrisoner?.MapFaction == holderKingdom)
                .Take(maxCount <= 0 ? int.MaxValue : maxCount)
                .Select(hero => new PrisonerExchangeRelease(hero, prisonerKingdom, holderKingdom)).ToList();
        }

        private sealed class PrisonerExchangeRelease
        {
            internal Hero Hero { get; }
            internal Kingdom HolderKingdom { get; }
            private readonly Kingdom prisonerKingdom;
            private readonly PartyBase originalCaptor;
            private readonly TroopRoster originalRoster;
            private readonly CharacterObject character;

            internal PrisonerExchangeRelease(Hero hero, Kingdom prisonerKingdom, Kingdom holderKingdom)
            {
                Hero = hero;
                HolderKingdom = holderKingdom;
                this.prisonerKingdom = prisonerKingdom;
                originalCaptor = hero.PartyBelongedToAsPrisoner;
                originalRoster = originalCaptor?.PrisonRoster;
                character = hero.CharacterObject;
            }

            internal bool IsInExpectedCustody()
            {
                return Hero.IsAlive && Hero.IsPrisoner
                    && (prisonerKingdom == null || Hero.Clan?.Kingdom == prisonerKingdom)
                    && originalCaptor != null && Hero.PartyBelongedToAsPrisoner == originalCaptor
                    && originalCaptor.MapFaction == HolderKingdom && originalRoster != null
                    && originalCaptor.PrisonRoster == originalRoster && character != null
                    && originalRoster.GetTroopCount(character) > 0 && !ReignRulerCampaignBehavior.BlocksRelease(Hero);
            }

            internal bool WasReleased()
            {
                return Hero.IsAlive && !Hero.IsPrisoner && Hero.PartyBelongedToAsPrisoner == null
                    && originalRoster.GetTroopCount(character) == 0;
            }
        }

        private static ReignActionResult RansomPackage(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            var native = new ReignNativeRansom();
            var recorder = ReignServices.AI;
            Hero payee = actor?.Leader, payer = target?.Leader;
            var owner = new NativeRansomWorldAction
            {
                CampaignId = native.CampaignId, TimelineId = native.TimelineId, ActionId = action.ActionId,
                IdentityJson = OrdinaryRansomIdentity(action)
            };
            string binding = owner.Binding();
            bool AuthorityIsCurrent() => recorder != null && ReignServices.AI == recorder
                && actor != null && target != null && actor != target && !actor.IsEliminated && !target.IsEliminated
                && payee?.IsAlive == true && payer?.IsAlive == true && payee != payer
                && actor.Leader == payee && target.Leader == payer && OrdinaryRansomIdentity(action) == owner.IdentityJson;
            if (!native.OwnsLoadedWorld() || !AuthorityIsCurrent() || string.IsNullOrWhiteSpace(action.ActionId))
                return ReignActionResult.ValidationFailed("The two ransom authorities or the loaded campaign are no longer available.");

            string terms = action.TermsJson;
            string prisonerId = ReadStringTerm(terms, "prisonerHeroStringId", string.Empty);
            bool named = !string.IsNullOrWhiteSpace(prisonerId);
            Hero namedPrisoner = named ? ReignObjectResolver.FindHero(prisonerId) : null;
            if (named && namedPrisoner == null)
                return ReignActionResult.ValidationFailed("The named prisoner is unavailable for this ransom.");

            // These aliases separately control the native release reason and the explicit payment.
            bool byRansom = !named || ReadIntTerm(terms, "gold", ReadIntTerm(terms, "GoldAmount", 0)) > 0;
            List<Hero> selection = named ? new List<Hero> { namedPrisoner }
                : CapturePrisonerExchange(target, actor, ReadIntTerm(terms, "maxPrisoners", 5)).Select(leg => leg.Hero).ToList();
            if (selection.Count == 0)
                return ReignActionResult.ValidationFailed("The ransom requires prisoners held in the offering kingdom's current custody.");

            string method = byRansom ? "ransom" : "peace";
            var captives = selection.Select(hero => new ReignNativeRansom.Captive(hero, method,
                party => party.MapFaction == actor && (named || hero.Clan?.Kingdom == target))).ToList();
            int gold = Math.Max(0, ReadIntTerm(terms, "gold", ReadIntTerm(terms, "ransomGold", selection.Count * 10000)));
            string key = NativeRansomWorldAction.Key(owner.CampaignId, owner.TimelineId, owner.ActionId);
            var material = new JObject
            {
                ["actor"] = actor.StringId, ["actorName"] = actor.InformalName.ToString(),
                ["target"] = target.StringId, ["targetName"] = target.InformalName.ToString(),
                ["payer"] = payer.StringId, ["payee"] = payee.StringId, ["payeeName"] = payee.Name?.ToString() ?? "",
                ["namedPrisoner"] = named ? namedPrisoner.StringId : "",
                ["prisonerName"] = namedPrisoner?.Name?.ToString() ?? "",
                ["day"] = (float)CampaignTime.Now.ToDays
            };
            NativeRansomAttempt attempt = native.Execute(key, binding, material, captives, AuthorityIsCurrent,
                payee, payer, payee, gold, owner);
            if (attempt == null)
                return ReignActionResult.ValidationFailed("The exact rostered custody and ransom payment could not be verified.");
            if (!native.OwnsLoadedWorld() || !AuthorityIsCurrent()) return UnverifiedRansomPackage();
            return PresentOrdinaryRansom(action, key, attempt, recorder, publish: true, fresh: true);
        }

        private static string OrdinaryRansomIdentity(ReignWorldActionRecord action) => new JObject
        {
            ["id"] = action.ActionId, ["type"] = action.TypeValue, ["source"] = action.Source,
            ["actorKingdom"] = action.ActorKingdomStringId, ["targetKingdom"] = action.TargetKingdomStringId,
            ["actorHero"] = action.ActorHeroStringId, ["targetHero"] = action.TargetHeroStringId,
            ["actorClan"] = action.ActorClanStringId, ["targetClan"] = action.TargetClanStringId,
            ["targetSettlement"] = action.TargetSettlementStringId, ["termsHash"] = action.TermsHash,
            ["authorization"] = action.AuthorizationMode, ["negotiation"] = action.NegotiationId, ["terms"] = action.TermsJson
        }.ToString(Newtonsoft.Json.Formatting.None);

        internal static ReignActionResult ReadOrdinaryRansom(ReignWorldActionRecord action, bool publish, string requestedActionId = null)
        {
            if (action != null && action.Type != ReignWorldActionType.DiplomacyRansomPackage) return null;
            string id = action?.ActionId ?? requestedActionId;
            if (string.IsNullOrWhiteSpace(id)) return null;
            var native = new ReignNativeRansom();
            if (native.OwnsLoadedWorld())
            {
                string key = NativeRansomWorldAction.Key(native.CampaignId, native.TimelineId, id);
                if (native.Journal.IsRunning(key)) return ReignActionResult.Progress("The native ransom is still being verified.");
                if (native.Journal.TryFindWorldAction(native.CampaignId, native.TimelineId, id, out NativeRansomAttempt attempt))
                    return PresentOrdinaryRansom(action, key, attempt, ReignServices.AI, publish && action != null, fresh: false);
            }

            if (action == null || TryReadRansomExecution(action, out JObject execution)) return null;
            if (!string.IsNullOrEmpty((execution?[OrdinaryRansomMarker] as JObject)?.Value<string>("receiptId")))
                return ReignActionResult.Progress("The exact ransom receipt is unavailable in the loaded campaign scope.");
            // Old terminal actions remain historical. An older interrupted marker
            // cannot authorize an attempt or acquire a typed completion receipt.
            return action.Status != ReignWorldActionStatus.Completed ? UnverifiedRansomPackage() : null;
        }

        private static ReignActionResult PresentOrdinaryRansom(ReignWorldActionRecord action, string key,
            NativeRansomAttempt attempt, ReignAICampaignBehavior recorder, bool publish, bool fresh)
        {
            if (attempt?.WorldAction == null
                || (action != null && attempt.WorldAction.IdentityJson != OrdinaryRansomIdentity(action))) return UnverifiedRansomPackage();
            if (!attempt.IsVerified() || attempt.Payment == null)
                return UnverifiedRansomPackage().WithDiagnostic("ransomPhase", attempt.State.ToString())
                    .WithDiagnostic("holdReason", attempt.HoldReason);
            JObject material, identity;
            try
            {
                material = JObject.Parse(attempt.MaterialJson);
                identity = JObject.Parse(attempt.WorldAction.IdentityJson);
            }
            catch (Newtonsoft.Json.JsonException) { return UnverifiedRansomPackage(); }
            if (identity["type"]?.Type != JTokenType.Integer
                || identity.Value<int>("type") != (int)ReignWorldActionType.DiplomacyRansomPackage
                || identity.Value<string>("id") != attempt.WorldAction.ActionId
                || material.Value<string>("actor") != identity.Value<string>("actorKingdom")
                || material.Value<string>("target") != identity.Value<string>("targetKingdom")
                || material.Value<string>("payer") != attempt.Payment.PayerId
                || material.Value<string>("payee") != attempt.Payment.RecipientId
                || material["day"]?.Type != JTokenType.Float && material["day"]?.Type != JTokenType.Integer)
                return UnverifiedRansomPackage();

            string receiptId = "ransom_" + key;
            if (!string.IsNullOrEmpty(attempt.PublicationId) && attempt.PublicationId != receiptId)
                return UnverifiedRansomPackage();
            if (attempt.PublicationId != receiptId)
            {
                if (!publish) return ReignActionResult.Progress("The verified ransom receipt is awaiting publication.");
                try
                {
                    if (recorder?.RecordRansomAgreementFromAction(action, receiptId, material.Value<float>("day"), fresh) == null)
                        return UnverifiedRansomPackage();
                }
                catch (Exception ex)
                {
                    ReignLog.Exception("Verified ransom receipt awaits publication action=" + action.ActionId, ex);
                    return ReignActionResult.Progress("The verified ransom receipt is awaiting publication.");
                }
                attempt.PublicationId = receiptId;
            }

            NativeRansomPayment payment = attempt.Payment;
            string named = material.Value<string>("namedPrisoner") ?? "";
            if (publish)
            {
                JObject execution;
                try { execution = JObject.Parse(action.ExecutionSnapshotJson ?? "{}"); }
                catch (Newtonsoft.Json.JsonException) { execution = new JObject(); }
                execution[OrdinaryRansomMarker] = new JObject
                {
                    ["receiptId"] = receiptId, ["releases"] = JArray.FromObject(attempt.Releases),
                    ["payment"] = ReignNativeRansom.PaymentReceipt(payment)
                };
                action.ExecutionSnapshotJson = execution.ToString(Newtonsoft.Json.Formatting.None);
            }
            string summary = payment.Amount > 0
                ? material.Value<string>("targetName") + " paid ransom for " + attempt.Releases.Count + " noble prisoner(s)."
                : material.Value<string>("actorName") + " released " + attempt.Releases.Count + " noble prisoner(s).";
            ReignActionResult result = ReignActionResult.Done(summary);
            if (payment.Amount > 0)
            {
                string observation = ObserveGoldTransferBalances(payment.Amount, payment.Before.Payer, payment.After.Payer,
                    payment.Before.Recipient, payment.After.Recipient, payment.Invoked, true).ToString(Newtonsoft.Json.Formatting.None);
                result.WithEffect("gold_transfer", "hero", payment.RecipientId, material.Value<string>("payeeName"),
                    "from=" + payment.PayerId + ";amount=" + payment.Amount.ToString(CultureInfo.InvariantCulture) + ";observation=" + observation);
            }
            result.WithEffect("prisoners_released", "kingdom", material.Value<string>("target"),
                material.Value<string>("targetName"), "released=" + attempt.Releases.Count)
                .WithEffect("treaty_recorded", "kingdom", material.Value<string>("target"),
                    material.Value<string>("targetName"), "kind=ransom_package");
            NativeRansomRelease namedLeg = attempt.Releases.FirstOrDefault(leg => leg.HeroId == named);
            if (namedLeg != null)
                result.WithEffect("prisoner_release", "hero", named, material.Value<string>("prisonerName"), "method=" + namedLeg.Method)
                    .WithChangedEntity("hero", named, material.Value<string>("prisonerName"), "prisoner_released");
            return result;
        }


        private static bool TryReadRansomExecution(ReignWorldActionRecord action, out JObject execution)
        {
            execution = null;
            try { execution = JObject.Parse(string.IsNullOrWhiteSpace(action.ExecutionSnapshotJson) ? "{}" : action.ExecutionSnapshotJson); }
            catch (Newtonsoft.Json.JsonException) { return false; }
            return execution.Property(OrdinaryRansomMarker) == null;
        }

        internal static bool RetainLegacyRansomAttempt(ReignWorldActionRecord action)
            => action?.Type == ReignWorldActionType.DiplomacyRansomPackage
                && !TryReadRansomExecution(action, out JObject execution)
                && string.IsNullOrEmpty((execution?[OrdinaryRansomMarker] as JObject)?.Value<string>("receiptId"));

        private static ReignActionResult UnverifiedRansomPackage()
        {
            return ReignActionResult.FailTerminal("The ransom could not be fully verified. Review the prisoner and payment state before any new agreement.",
                "ordinary_ransom_unverified", "state_verification");
        }

        private static ReignActionResult ReturnOccupiedSettlement(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
            if (settlement == null || !settlement.IsFortification)
            {
                return ReignActionResult.ValidationFailed("A town or castle target is required for return occupied settlement.");
            }

            if (settlement.MapFaction != actor)
            {
                return ReignActionResult.ValidationFailed("Settlement is not currently held by the returning kingdom.");
            }

            if (target.Leader == null)
            {
                return ReignActionResult.ValidationFailed("Receiving kingdom has no leader.");
            }

            ChangeOwnerOfSettlementAction.ApplyByGift(settlement, target.Leader);
            RecordAgreement(action, "return_occupied_settlement", 0f, true);
            return ReignActionResult.Done(actor.InformalName + " returned " + settlement.Name + " to " + target.InformalName + ".")
                .WithChangedEntity("settlement", settlement.StringId, settlement.Name.ToString(), "owner_changed")
                .WithEffect("settlement_transferred", "settlement", settlement.StringId, settlement.Name.ToString(), "newOwnerKingdomId=" + target.StringId)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=return_occupied_settlement");
        }

        private static ReignActionResult PayToJoinWar(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            string enemyId = ReadStringTerm(action.TermsJson, "enemyKingdomId", ReadStringTerm(action.TermsJson, "thirdKingdomId", string.Empty));
            Kingdom enemy = ReignObjectResolver.FindKingdom(enemyId);
            if (enemy == null)
            {
                return ReignActionResult.ValidationFailed("terms.enemyKingdomId is required for pay to join war.");
            }

            int gold = ReadIntTerm(action.TermsJson, "gold", 0);
            string goldObservation = ApplyGoldTransfer(actor.Leader, target.Leader, gold);
            if (!target.IsAtWarWith(enemy))
            {
                DeclareWarAction.ApplyByKingdomDecision(target, enemy);
            }

            RecordAgreement(action, "paid_join_war", ReadFloatTerm(action.TermsJson, "durationDays", 30f), ReadBoolTerm(action.TermsJson, "isPublic", false));
            return ReignActionResult.Done(actor.InformalName + " paid " + target.InformalName + " to join war against " + enemy.InformalName + ".")
                .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, goldObservation)
                .WithEffect("war_declared", "kingdom", enemy.StringId, enemy.InformalName.ToString(), "actorKingdomId=" + target.StringId)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=paid_join_war");
        }

        private static ReignActionResult DiplomaticPackage(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            JObject terms = ReignValueService.ParseTerms(action);
            ReignActionResult result = ReignActionResult.Done(actor.InformalName + " recorded a compound diplomatic package with " + target.InformalName + ".");
            int shipsTransferred = 0;
            int goldApplied = 0;
            int settlementsTransferred = 0;
            int prisonersReleased = 0;
            var settlements = new List<Settlement>();
            ActionAgreement frozen = null;
            if (ReignMarriageService.LinkedWedding(action) == null)
            {
                try { frozen = CaptureWeddingPackage(actor, target, action, terms); }
                catch (InvalidOperationException ex) { return ReignActionResult.ValidationFailed(ex.Message); }
            }
            result = ReignMarriageService.SettleMarriage(action, result, frozen);
            if (!result.Success || !result.Completed) return result;
            var wedding = ReignMarriageService.LinkedWedding(action);
            foreach (var leg in wedding?.Consideration?.Clauses ?? new List<ActionClause>())
            {
                string id = ReignActionService.Arg(leg, "assetId");
                string amount = ReignActionService.Arg(leg, "quantity");
                string flow = "from=" + leg.ActorId + ";to=" + leg.RecipientId + ";amount=" + amount;
                switch (leg.ActionId)
                {
                    case "give.gold":
                        if (leg.Id == "arranged_fee") break;
                        goldApplied += int.Parse(amount, CultureInfo.InvariantCulture);
                        result.WithEffect("gold_transfer", "hero", leg.RecipientId, ReignActionService.HeroById(leg.RecipientId)?.Name.ToString(), flow);
                        break;
                    case "give.item":
                        result.WithEffect(ReignActionService.Arg(leg, "equipmentSet").Length == 0 ? "item_transfer" : "equipped_item_transfer",
                            "item", id, ReignObjectResolver.FindItem(id)?.Name.ToString(), flow);
                        break;
                    case "give.settlement":
                        var town = Settlement.Find(id);
                        if (town != null) settlements.Add(town);
                        settlementsTransferred++;
                        break;
                    case ReignWeddingBargain.ReleasePrisoner:
                        prisonersReleased++;
                        result.WithEffect("prisoner_release", "hero", id, ReignActionService.HeroById(id)?.Name.ToString(), flow);
                        break;
                    case "give.ship":
                        shipsTransferred++;
                        result.WithEffect("ship_transfer", "ship", id, id, flow);
                        break;
                }
            }

            bool alliancePackage = string.Equals(ReadStringTerm(action.TermsJson, "treatyKind", string.Empty), "alliance_package", System.StringComparison.OrdinalIgnoreCase);
            if (alliancePackage)
            {
                StartNativeAlliance(actor, target, action);
                RecordAgreement(action, "alliance", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                result.WithEffect("alliance_started", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);
                if (ReadBoolTerm(action.TermsJson, "guaranteeIndependence", false)) RecordAgreement(action, "guarantee_independence", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                if (ReadBoolTerm(action.TermsJson, "militaryCommitment", false) || ReadBoolTerm(action.TermsJson, "warSupport", false)) RecordAgreement(action, "alliance_war_support", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
                if (!string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "hostageHeroStringId", string.Empty))) RecordAgreement(action, "hostage_guarantee", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
            }

            RecordAgreement(action, "diplomatic_package", ReadFloatTerm(action.TermsJson, "durationDays", 0f), ReadBoolTerm(action.TermsJson, "isPublic", true));
            result.WithEffect("diplomatic_package", "kingdom", target.StringId, target.InformalName.ToString(), "gold=" + goldApplied + ";settlements=" + settlementsTransferred + ";prisoners=" + prisonersReleased + ";ships=" + shipsTransferred)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=diplomatic_package");
            WithTransferredSettlements(result, settlements);
            if (!ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string receiptFailure))
            {
                return ReignActionResult.FailTerminal("Diplomatic package verification failed: " + receiptFailure, "package_verification_failed", "state_verification");
            }

            string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
            result.WithMessage(string.IsNullOrWhiteSpace(receipt)
                ? actor.InformalName + " recorded a verified diplomatic package with " + target.InformalName + "."
                : "Verified agreement: " + receipt + ".");
            return result;
        }

        private static ActionAgreement CaptureWeddingPackage(Kingdom actor, Kingdom target, ReignWorldActionRecord action, JObject terms)
        {
            var package = new ActionAgreement { Id = ReignMarriageService.WeddingId(action) };
            string Read(JObject row, string name, string fallback = "") => ReignValueService.ReadStringTerm(row, name, fallback);
            Hero Party(string id) => ReignValueService.ResolveHero(id) ?? throw new InvalidOperationException("A named wedding asset holder is unavailable.");
            string fromId = Read(terms, "fromHeroStringId", action.ActorHeroStringId);
            string toId = Read(terms, "toHeroStringId", action.TargetHeroStringId);
            int gold = ReignValueService.ReadIntTerm(terms, "gold", ReignValueService.ReadIntTerm(terms, "GoldAmount",
                ReignValueService.ReadIntTerm(terms, "goldAmount", 0)));
            if (gold > 0)
                package.Clauses.Add(ReignWeddingBargain.Asset("give.gold", Party(Read(terms, "goldFromHeroStringId", fromId)),
                    Party(Read(terms, "goldToHeroStringId", toId)), gold));

            var items = ReignValueService.ReadItemTermObjects(terms);
            if (items.Count == 0 && Read(terms, "itemId", Read(terms, "item")).Length != 0) items.Add(terms);
            foreach (var row in items)
            {
                var item = ReignObjectResolver.FindItem(Read(row, "itemId", Read(row, "item")));
                if (item == null) throw new InvalidOperationException("The signed wedding item could not be resolved.");
                var from = Party(Read(row, "fromHeroStringId", Read(row, "itemFromHeroStringId",
                    Read(terms, "itemFromHeroStringId", Read(terms, "assetFromHeroStringId", fromId)))));
                var to = Party(Read(row, "toHeroStringId", Read(row, "itemToHeroStringId",
                    Read(terms, "itemToHeroStringId", Read(terms, "assetToHeroStringId", toId)))));
                int quantity = ReignValueService.ReadIntTerm(row, "amount", ReignValueService.ReadIntTerm(row, "Amount", 1));
                var leg = ReignWeddingBargain.Asset("give.item", from, to, quantity, item.StringId);
                string slot = Read(row, "sourceEquipmentSlot");
                string equipment = Read(row, "sourceEquipmentSet");
                if (quantity == 1 && (slot.Length != 0 || equipment.Length != 0
                    || (ReignValueService.PartyForInventory(from)?.ItemRoster.GetItemNumber(item) ?? 0) == 0)
                    && ReignObjectResolver.TryFindEquippedItem(from, item.StringId, slot, equipment, out _, out EquipmentIndex index,
                        out EquipmentElement element, out string set))
                {
                    leg = ReignWeddingBargain.Asset("give.item", from, to, 1, item.StringId, element.ItemModifier?.StringId ?? "");
                    leg.Arguments["equipmentSet"] = set;
                    leg.Arguments["equipmentSlot"] = ((int)index).ToString(CultureInfo.InvariantCulture);
                }
                package.Clauses.Add(leg);
            }

            var settlementIds = ReignValueService.ReadStringListTerm(terms, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId)) settlementIds.Add(action.TargetSettlementStringId);
            var fromKingdom = ReignObjectResolver.FindKingdom(Read(terms, "settlementFromKingdomId")) ?? target;
            var toKingdom = ReignObjectResolver.FindKingdom(Read(terms, "settlementToKingdomId")) ?? actor;
            foreach (string id in settlementIds.Distinct())
            {
                var settlement = Settlement.Find(id);
                if (settlement?.OwnerClan?.Leader == null || settlement.MapFaction != fromKingdom || toKingdom?.Leader == null)
                    throw new InvalidOperationException("The named wedding fief is outside the authorized parties.");
                package.Clauses.Add(ReignWeddingBargain.Asset("give.settlement", settlement.OwnerClan.Leader, toKingdom.Leader, 1, id));
            }

            string prisonerId = Read(terms, "prisonerHeroStringId", Read(terms, "prisonerHero"));
            if (prisonerId.Length != 0)
            {
                var prisoner = ReignObjectResolver.FindHero(prisonerId);
                var custody = prisoner?.PartyBelongedToAsPrisoner;
                var recipient = custody?.MapFaction == actor ? target?.Leader : custody?.MapFaction == target ? actor?.Leader : null;
                if (custody?.Owner == null || recipient == null)
                    throw new InvalidOperationException("The named wedding prisoner is outside the authorized parties.");
                var leg = ReignWeddingBargain.Asset(ReignWeddingBargain.ReleasePrisoner, custody.Owner, recipient, 1, prisoner.StringId);
                if (custody.Settlement != null) leg.Arguments["custodySettlementId"] = custody.Settlement.StringId;
                package.Clauses.Add(leg);
            }

            if (IsNavalPackage(action))
            {
                var from = Party(Read(terms, "assetFromHeroStringId", action.ActorHeroStringId));
                var to = Party(Read(terms, "assetToHeroStringId", action.TargetHeroStringId));
                var ships = from.PartyBelongedTo?.Ships.Where(ship => ship != null && ship.IsTradeable && !ship.IsUsedByQuest).ToList();
                int count = ReignValueService.ReadIntTerm(terms, "amount", ReignValueService.ReadIntTerm(terms, "shipCount", 0));
                if (ships == null || ships.Count == 0 || count > ships.Count)
                    throw new InvalidOperationException("The wedding package's ships are unavailable.");
                foreach (var ship in ships.Take(count <= 0 || ReadBoolTerm(action.TermsJson, "allMatchingAssets", false) ? ships.Count : count))
                    package.Clauses.Add(ReignWeddingBargain.Asset("give.ship", from, to, 1, ReignAssetTransfer.ShipId(ship)));
            }
            return package;
        }

        private static string ApplyGoldTransfer(Hero from, Hero to, int amount)
        {
            // Read on the same campaign-thread invocation, before any later native tick.
            int fromBefore = from?.Gold ?? 0, toBefore = to?.Gold ?? 0;
            bool invoked = amount > 0 && to != null;
            if (invoked)
            {
                GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
            }
            string observation = ObserveGoldTransferBalances(amount, fromBefore, from?.Gold ?? 0,
                toBefore, to?.Gold ?? 0, invoked, from != null && to != null && from != to)
                .ToString(Newtonsoft.Json.Formatting.None);
            return "from=" + (from?.StringId ?? string.Empty) + ";amount="
                + amount.ToString(CultureInfo.InvariantCulture) + ";observation=" + observation;
        }

        internal static JObject ObserveGoldTransferBalances(int requestedAmount, int fromBefore, int fromAfter,
            int toBefore, int toAfter, bool invoked, bool distinctHeroes)
        {
            // Long arithmetic prevents a wrapped balance difference from looking conserved.
            long debit = (long)fromBefore - fromAfter, credit = (long)toAfter - toBefore;
            bool conserved = distinctHeroes && debit >= 0 && debit == credit;
            return new JObject
            {
                ["schema"] = "reign-diplomacy-gold-observation-v1", ["requestedAmount"] = requestedAmount,
                ["fromBefore"] = fromBefore, ["fromAfter"] = fromAfter,
                ["toBefore"] = toBefore, ["toAfter"] = toAfter,
                ["debited"] = debit, ["credited"] = credit, ["invoked"] = invoked,
                ["conserved"] = conserved,
                ["exactPayment"] = invoked && requestedAmount > 0 && conserved && debit == requestedAmount
            };
        }

        private static bool IsNavalSupplyAgreement(ReignWorldActionRecord action)
        {
            string text = string.Join(" ", new[]
            {
                ReadStringTerm(action.TermsJson, "assetClass", string.Empty),
                ReadStringTerm(action.TermsJson, "asset", string.Empty),
                ReadStringTerm(action.TermsJson, "assetName", string.Empty),
                ReadStringTerm(action.TermsJson, "item", string.Empty),
                ReadStringTerm(action.TermsJson, "Item", string.Empty)
            }).ToLowerInvariant();

            return text.Contains("naval")
                || text.Contains("ship")
                || text.Contains("fleet")
                || text.Contains("vessel")
                || text.Contains("boat");
        }

        private static bool IsNavalPackage(ReignWorldActionRecord action)
        {
            string text = string.Join(" ", new[]
            {
                ReadStringTerm(action.TermsJson, "assetClass", string.Empty),
                ReadStringTerm(action.TermsJson, "asset", string.Empty),
                ReadStringTerm(action.TermsJson, "assetName", string.Empty),
                ReadStringTerm(action.TermsJson, "packageText", string.Empty)
            }).ToLowerInvariant();

            return text.Contains("naval")
                || text.Contains("ship")
                || text.Contains("fleet")
                || text.Contains("vessel")
                || text.Contains("boat");
        }

        private static ReignActionResult TransferNavalSupplyAgreement(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            Hero sourceHero = ReignObjectResolver.FindHero(action.ActorHeroStringId) ?? actor?.Leader;
            Hero destinationHero = ReignObjectResolver.FindHero(action.TargetHeroStringId) ?? target?.Leader ?? Hero.MainHero;
            MobileParty sourceParty = sourceHero?.PartyBelongedTo ?? FindFirstKingdomPartyWithShips(actor);
            MobileParty destinationParty = destinationHero == Hero.MainHero
                ? MobileParty.MainParty
                : destinationHero?.PartyBelongedTo ?? (target == Clan.PlayerClan?.Kingdom ? MobileParty.MainParty : target?.Leader?.PartyBelongedTo);

            if (sourceParty?.Party == null)
            {
                return ReignActionResult.FailTerminal("Naval supply agreement could not find a source party with ships.", "missing_ship_source", "world_state");
            }

            if (destinationParty?.Party == null)
            {
                return ReignActionResult.FailTerminal("Naval supply agreement could not find a destination party for ships.", "missing_ship_destination", "world_state");
            }

            List<Ship> availableShips = sourceParty.Ships?
                .Where(x => x != null && (!x.IsUsedByQuest || ReadBoolTerm(action.TermsJson, "includeQuestShips", false)))
                .ToList() ?? new List<Ship>();

            if (availableShips.Count == 0)
            {
                return ReignActionResult.FailTerminal(sourceParty.Name + " has no transferable ships.", "no_transferable_ships", "world_state")
                    .WithDiagnostic("sourcePartyId", sourceParty.StringId ?? string.Empty)
                    .WithDiagnostic("sourcePartyName", sourceParty.Name?.ToString() ?? string.Empty);
            }

            int requestedAmount = ReadIntTerm(action.TermsJson, "amount", ReadIntTerm(action.TermsJson, "Amount", ReadIntTerm(action.TermsJson, "shipCount", 0)));
            bool transferAll = ReadBoolTerm(action.TermsJson, "allMatchingAssets", requestedAmount <= 0);
            int transferCount = transferAll || requestedAmount <= 0 ? availableShips.Count : System.Math.Min(requestedAmount, availableShips.Count);
            List<Ship> selectedShips = availableShips.Take(transferCount).ToList();

            foreach (Ship ship in selectedShips)
            {
                ChangeShipOwnerAction.ApplyByTransferring(destinationParty.Party, ship);
            }

            RecordAgreement(action, "naval_supply_agreement", ReadFloatTerm(action.TermsJson, "durationDays", 60f), true);
            ReignActionResult result = ReignActionResult.Done(actor.InformalName + " transferred " + selectedShips.Count + " ship(s) to " + target.InformalName + ".")
                .WithDiagnostic("sourcePartyId", sourceParty.StringId ?? string.Empty)
                .WithDiagnostic("sourcePartyName", sourceParty.Name?.ToString() ?? string.Empty)
                .WithDiagnostic("destinationPartyId", destinationParty.StringId ?? string.Empty)
                .WithDiagnostic("destinationPartyName", destinationParty.Name?.ToString() ?? string.Empty)
                .WithDiagnostic("shipsTransferred", selectedShips.Count.ToString(CultureInfo.InvariantCulture))
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=naval_supply_agreement")
                .WithEffect("ship_transfer", "party", destinationParty.StringId ?? string.Empty, destinationParty.Name?.ToString() ?? string.Empty, "from=" + (sourceParty.StringId ?? string.Empty) + ";count=" + selectedShips.Count);

            foreach (Ship ship in selectedShips)
            {
                string hullId = ship.ShipHull?.StringId ?? string.Empty;
                string shipName = ship.Name?.ToString() ?? hullId;
                result.WithChangedEntity("ship", hullId, shipName, "owner_changed");
            }

            return result;
        }

        private static MobileParty FindFirstKingdomPartyWithShips(Kingdom kingdom)
        {
            if (kingdom == null)
            {
                return null;
            }

            return MobileParty.All
                .Where(x => x != null && x.MapFaction == kingdom && x.Ships != null && x.Ships.Count > 0)
                .OrderByDescending(x => x.LeaderHero == kingdom.Leader)
                .ThenByDescending(x => x.MemberRoster?.TotalManCount ?? 0)
                .FirstOrDefault();
        }

        private static void RecordAgreement(ReignWorldActionRecord action, string kind, float durationDays, bool isPublic)
        {
            ReignServices.AI?.RecordAgreementFromAction(action, kind, durationDays, isPublic);
        }

        private static int ReadIntTerm(string json, string key, int fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                if (token == null)
                {
                    return fallback;
                }

                string text = token.ToString();
                if (int.TryParse(text, NumberStyles.Integer | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                string digits = new string(text.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out parsed) ? parsed : fallback;
            }
            catch
            {
                ReignLog.Warn("Failed to parse diplomacy terms JSON: " + json);
                return fallback;
            }
        }

        private static float ReadFloatTerm(string json, string key, float fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                return token == null ? fallback : token.Value<float>();
            }
            catch
            {
                ReignLog.Warn("Failed to parse diplomacy float term JSON: " + json);
                return fallback;
            }
        }

        private static bool ReadBoolTerm(string json, string key, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                return token == null ? fallback : token.Value<bool>();
            }
            catch
            {
                ReignLog.Warn("Failed to parse diplomacy bool term JSON: " + json);
                return fallback;
            }
        }

        private static string ReadStringTerm(string json, string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return fallback;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                return token == null ? fallback : token.ToString();
            }
            catch
            {
                ReignLog.Warn("Failed to parse diplomacy string term JSON: " + json);
                return fallback;
            }
        }

        private static List<string> ReadStringListTerm(string json, string key)
        {
            List<string> result = new List<string>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return result;
            }

            try
            {
                JObject obj = JObject.Parse(json);
                JToken token = obj[key];
                if (token is JArray array)
                {
                    result.AddRange(array.Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                }
                else if (token != null)
                {
                    string value = token.ToString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        result.Add(value);
                    }
                }
            }
            catch
            {
                ReignLog.Warn("Failed to parse settlement terms JSON: " + json);
            }

            return result;
        }
    }
}
