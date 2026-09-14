using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.World
{
    public static class ReignDiplomacyExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
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
                    if (ReignAICampaignBehavior.Instance != null)
                        ReignAICampaignBehavior.Instance.DeclareWarWithOrigin(actor, target, "direct",
                            action.Reason, action.ActionId, string.Empty, false);
                    else DeclareWarAction.ApplyByKingdomDecision(actor, target);
                    return ReignActionResult.Done(actor.InformalName + " declared war on " + target.InformalName + ".")
                        .WithEffect("war_declared", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);

                case ReignWorldActionType.DiplomacyMakePeace:
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
                    return ReignActionResult.Done(actor.InformalName + " made peace with " + target.InformalName + ".")
                        .WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);

                case ReignWorldActionType.DiplomacyOfferTributePeace:
                    int offeredDailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", 0);
                    int offeredTributeDays = ReadIntTerm(action.TermsJson, "durationDays", 30);
                    MakePeaceAction.ApplyByKingdomDecision(actor, target, offeredDailyTribute, offeredTributeDays);
                    return ReignActionResult.Done(actor.InformalName + " made tribute peace with " + target.InformalName + ".")
                        .WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "dailyTribute=" + offeredDailyTribute + ";durationDays=" + offeredTributeDays);

                case ReignWorldActionType.DiplomacyDemandReparationsPeace:
                    ApplyReparationsAndPeace(actor, target, action);
                    return WithPeaceAndReparations(
                        ReignActionResult.Done(target.InformalName + " accepted reparations demanded by " + actor.InformalName + "."),
                        actor,
                        target,
                        action);

                case ReignWorldActionType.DiplomacyDemandSettlementPeace:
                    List<Settlement> transferredSettlements = new List<Settlement>();
                    int transferred = TransferDemandedSettlements(actor, target, action, transferredSettlements);
                    ApplyReparationsAndPeace(actor, target, action);
                    RecordAgreement(action, "settlement_surrender_peace", 0f, true);
                    return WithTransferredSettlements(
                        WithPeaceAndReparations(
                            ReignActionResult.Done(target.InformalName + " surrendered " + transferred + " settlement(s) to " + actor.InformalName + " for peace."),
                            actor,
                            target,
                            action)
                            .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=settlement_surrender_peace"),
                        transferredSettlements);

                case ReignWorldActionType.DiplomacyDemandSurrenderPeace:
                    List<Settlement> surrenderedSettlements = new List<Settlement>();
                    int surrendered = TransferDemandedSettlements(actor, target, action, surrenderedSettlements);
                    ApplyReparationsAndPeace(actor, target, action);
                    bool destroyed = DestroyTargetKingdomIfLandless(target);
                    RecordAgreement(action, "full_surrender_peace", 0f, true);
                    return WithTransferredSettlements(
                        WithPeaceAndReparations(
                            ReignActionResult.Done(target.InformalName + " accepted surrender terms from " + actor.InformalName + ", including " + surrendered + " settlement(s)" + (destroyed ? ", and the kingdom was dissolved." : ".")),
                            actor,
                            target,
                            action)
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
                    ApplyGoldTransfer(target.Leader, actor.Leader, indemnityGold);
                    RecordAgreement(action, "war_indemnity", ReadFloatTerm(action.TermsJson, "durationDays", 0f), true);
                    return ReignActionResult.Done(target.InformalName + " paid a war indemnity to " + actor.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", actor.Leader?.StringId ?? string.Empty, actor.Leader?.Name?.ToString() ?? string.Empty, "from=" + (target.Leader?.StringId ?? string.Empty) + ";amount=" + indemnityGold)
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
                    ApplyGoldTransfer(actor.Leader, target.Leader, supplyGold);
                    RecordAgreement(action, "supply_agreement", ReadFloatTerm(action.TermsJson, "durationDays", 60f), true);
                    return ReignActionResult.Done(actor.InformalName + " signed a supply agreement with " + target.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, "from=" + (actor.Leader?.StringId ?? string.Empty) + ";amount=" + supplyGold)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=supply_agreement");

                case ReignWorldActionType.DiplomacyLoanOrSubsidy:
                    int loanGold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "loanGold", 0));
                    ApplyGoldTransfer(actor.Leader, target.Leader, loanGold);
                    RecordAgreement(action, "loan_or_subsidy", ReadFloatTerm(action.TermsJson, "durationDays", 120f), ReadBoolTerm(action.TermsJson, "isPublic", true));
                    return ReignActionResult.Done(actor.InformalName + " sent a loan or subsidy to " + target.InformalName + ".")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, "from=" + (actor.Leader?.StringId ?? string.Empty) + ";amount=" + loanGold)
                        .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=loan_or_subsidy");

                case ReignWorldActionType.DiplomacyPayToStayNeutral:
                    int neutralGold = ReadIntTerm(action.TermsJson, "gold", 0);
                    ApplyGoldTransfer(actor.Leader, target.Leader, neutralGold);
                    RecordAgreement(action, "paid_neutrality", ReadFloatTerm(action.TermsJson, "durationDays", 60f), ReadBoolTerm(action.TermsJson, "isPublic", false));
                    return ReignActionResult.Done(actor.InformalName + " paid " + target.InformalName + " to stay neutral.")
                        .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, "from=" + (actor.Leader?.StringId ?? string.Empty) + ";amount=" + neutralGold)
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
            if (sponsor == null || parent == null || rebels == null || rebels.IsEliminated)
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
            ReignAICampaignBehavior.Instance?.RecordAgreementFromAction(agreementAction,
                "rebellion_backing", 0f, true);
            ReignRebellionCampaignBehavior.Instance?.RecordForeignBacking(movementId, sponsor.StringId, action.ActionId);
            if (!sponsor.IsAtWarWith(parent))
            {
                if (ReignAICampaignBehavior.Instance != null)
                    ReignAICampaignBehavior.Instance.DeclareWarWithOrigin(sponsor, parent,
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

        private static ReignActionResult WithPeaceAndReparations(ReignActionResult result, Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            int lumpGold = ReadIntTerm(action.TermsJson, "reparationsGold", 0);
            int dailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", ReadIntTerm(action.TermsJson, "reparationsDailyTribute", 0));
            int durationDays = ReadIntTerm(action.TermsJson, "durationDays", 30);

            result.WithEffect("peace_made", "kingdom", target.StringId, target.InformalName.ToString(), "actorKingdomId=" + actor.StringId);
            if (lumpGold > 0)
            {
                result.WithEffect("gold_transfer", "hero", actor.Leader?.StringId ?? string.Empty, actor.Leader?.Name?.ToString() ?? string.Empty, "from=" + (target.Leader?.StringId ?? string.Empty) + ";amount=" + lumpGold);
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

        private static void ApplyReparationsAndPeace(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            int lumpGold = ReadIntTerm(action.TermsJson, "reparationsGold", 0);
            if (lumpGold > 0 && target.Leader != null && actor.Leader != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(target.Leader, actor.Leader, lumpGold, true);
            }

            int dailyTribute = ReadIntTerm(action.TermsJson, "dailyTribute", ReadIntTerm(action.TermsJson, "reparationsDailyTribute", 0));
            int durationDays = ReadIntTerm(action.TermsJson, "durationDays", 30);
            if (dailyTribute > 0)
            {
                MakePeaceAction.ApplyByKingdomDecision(target, actor, dailyTribute, durationDays);
                return;
            }

            MakePeaceAction.ApplyByKingdomDecision(actor, target, 0, 0);
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

            if (!behavior.IsAllyWithKingdom(actor, target))
            {
                behavior.StartAlliance(actor, target);
            }
            if (!behavior.IsAllyWithKingdom(actor, target))
                throw new System.InvalidOperationException("The native alliance action did not establish the accepted alliance.");
        }

        private static ReignActionResult BreakTreaties(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            string kind = ReadStringTerm(action.TermsJson, "agreementKind", ReadStringTerm(action.TermsJson, "kind", string.Empty));
            int ended = ReignAICampaignBehavior.Instance?.EndAgreementsBetween(actor.StringId, target.StringId,
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
            int maxEachSide = ReadIntTerm(action.TermsJson, "maxEachSide", 3);
            int releasedActor = ReleasePrisoners(actor, target, maxEachSide, false);
            int releasedTarget = ReleasePrisoners(target, actor, maxEachSide, false);
            RecordAgreement(action, "prisoner_exchange", 0f, true);
            return ReignActionResult.Done(actor.InformalName + " and " + target.InformalName + " exchanged prisoners. Released=" + (releasedActor + releasedTarget) + ".")
                .WithEffect("prisoners_released", "kingdom", actor.StringId, actor.InformalName.ToString(), "released=" + releasedActor)
                .WithEffect("prisoners_released", "kingdom", target.StringId, target.InformalName.ToString(), "released=" + releasedTarget)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=prisoner_exchange");
        }

        private static ReignActionResult RansomPackage(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            string promisedPrisonerId = ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty);
            Hero releasedPrisoner = null;
            int released;
            if (!string.IsNullOrWhiteSpace(promisedPrisonerId))
            {
                released = ReleasePackagePrisoner(actor, target, action, out releasedPrisoner);
            }
            else
            {
                int maxPrisoners = ReadIntTerm(action.TermsJson, "maxPrisoners", 5);
                released = ReleasePrisoners(target, actor, maxPrisoners, true);
            }
            int gold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "ransomGold", released * 10000));
            ApplyGoldTransfer(target.Leader, actor.Leader, gold);
            RecordAgreement(action, "ransom_package", 0f, true);
            ReignActionResult result = ReignActionResult.Done(target.InformalName + " paid ransom for " + released + " noble prisoner(s).")
                .WithEffect("gold_transfer", "hero", actor.Leader?.StringId ?? string.Empty, actor.Leader?.Name?.ToString() ?? string.Empty, "from=" + (target.Leader?.StringId ?? string.Empty) + ";amount=" + gold)
                .WithEffect("prisoners_released", "kingdom", target.StringId, target.InformalName.ToString(), "released=" + released)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=ransom_package");
            if (releasedPrisoner != null)
            {
                result.WithEffect("prisoner_release", "hero", releasedPrisoner.StringId, releasedPrisoner.Name.ToString(), "method=ransom")
                    .WithChangedEntity("hero", releasedPrisoner.StringId, releasedPrisoner.Name.ToString(), "prisoner_released");
            }

            return result;
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
            ApplyGoldTransfer(actor.Leader, target.Leader, gold);
            if (!target.IsAtWarWith(enemy))
            {
                DeclareWarAction.ApplyByKingdomDecision(target, enemy);
            }

            RecordAgreement(action, "paid_join_war", ReadFloatTerm(action.TermsJson, "durationDays", 30f), ReadBoolTerm(action.TermsJson, "isPublic", false));
            return ReignActionResult.Done(actor.InformalName + " paid " + target.InformalName + " to join war against " + enemy.InformalName + ".")
                .WithEffect("gold_transfer", "hero", target.Leader?.StringId ?? string.Empty, target.Leader?.Name?.ToString() ?? string.Empty, "from=" + (actor.Leader?.StringId ?? string.Empty) + ";amount=" + gold)
                .WithEffect("war_declared", "kingdom", enemy.StringId, enemy.InformalName.ToString(), "actorKingdomId=" + target.StringId)
                .WithEffect("treaty_recorded", "kingdom", target.StringId, target.InformalName.ToString(), "kind=paid_join_war");
        }

        private static ReignActionResult DiplomaticPackage(Kingdom actor, Kingdom target, ReignWorldActionRecord action)
        {
            JObject terms = ReignValueService.ParseTerms(action);
            int shipsTransferred = 0;
            ReignActionResult packageAssetResult = null;
            if (IsNavalPackage(action))
            {
                packageAssetResult = TransferPackageNavalAssets(actor, target, action, out shipsTransferred);
                if (!packageAssetResult.Success)
                {
                    return packageAssetResult;
                }
            }

            ReignActionResult result = ReignActionResult.Done(actor.InformalName + " recorded a compound diplomatic package with " + target.InformalName + ".");
            ReignTradePackageExecutor.ApplyMaterialTerms(action, terms, result);
            int goldApplied = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", 0));
            List<Settlement> settlements = new List<Settlement>();
            int settlementsTransferred = TransferPackageSettlements(actor, target, action, settlements);
            int prisonersReleased = ReleasePackagePrisoner(actor, target, action, out Hero releasedPrisoner);
            if (releasedPrisoner != null)
            {
                result.WithEffect("prisoner_release", "hero", releasedPrisoner.StringId, releasedPrisoner.Name.ToString(), "facilitator=package")
                    .WithChangedEntity("hero", releasedPrisoner.StringId, releasedPrisoner.Name.ToString(), "released_from_captivity");
            }
            if (ReignMarriageService.HasMarriageIntent(action)
                && !ReignMarriageService.TryApplyMarriage(action, result, out string marriageFailure))
            {
                return ReignActionResult.FailTerminal(marriageFailure, "marriage_verification_failed", "state_verification");
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
            if (packageAssetResult != null)
            {
                CopyResultDetails(packageAssetResult, result);
            }

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

        private static int TransferPackageSettlements(Kingdom actor, Kingdom target, ReignWorldActionRecord action, List<Settlement> transferredSettlements)
        {
            List<string> settlementIds = ReadStringListTerm(action.TermsJson, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            Kingdom fromKingdom = ReignObjectResolver.FindKingdom(ReadStringTerm(action.TermsJson, "settlementFromKingdomId", target?.StringId ?? string.Empty)) ?? target;
            Kingdom toKingdom = ReignObjectResolver.FindKingdom(ReadStringTerm(action.TermsJson, "settlementToKingdomId", actor?.StringId ?? string.Empty)) ?? actor;
            if (toKingdom?.Leader == null)
            {
                return 0;
            }

            int transferred = 0;
            foreach (string settlementId in settlementIds.Distinct())
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null || !settlement.IsFortification || settlement.MapFaction != fromKingdom)
                {
                    continue;
                }

                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, toKingdom.Leader);
                if (settlement.MapFaction != toKingdom)
                {
                    continue;
                }

                transferredSettlements?.Add(settlement);
                transferred++;
            }

            return transferred;
        }

        private static int ReleasePackagePrisoner(Kingdom actor, Kingdom target, ReignWorldActionRecord action, out Hero releasedPrisoner)
        {
            releasedPrisoner = null;
            string prisonerId = ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty);
            Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
            if (prisoner == null || !prisoner.IsPrisoner)
            {
                return 0;
            }

            Hero holderLeader = (prisoner.PartyBelongedToAsPrisoner?.MapFaction == actor ? actor?.Leader : target?.Leader)
                ?? target?.Leader
                ?? actor?.Leader;
            if (holderLeader == null)
            {
                return 0;
            }

            int gold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", 0));
            if (gold > 0)
            {
                EndCaptivityAction.ApplyByRansom(prisoner, holderLeader);
            }
            else
            {
                EndCaptivityAction.ApplyByPeace(prisoner, holderLeader);
            }

            if (prisoner.IsPrisoner)
            {
                return 0;
            }

            releasedPrisoner = prisoner;
            return 1;
        }

        private static int ReleasePrisoners(Kingdom prisonerKingdom, Kingdom holderKingdom, int maxCount, bool byRansom)
        {
            int released = 0;
            foreach (Hero hero in Hero.AllAliveHeroes
                         .Where(x => x != null
                             && x.IsPrisoner
                             && x.Clan?.Kingdom == prisonerKingdom
                             && x.PartyBelongedToAsPrisoner?.MapFaction == holderKingdom)
                         .Take(maxCount <= 0 ? int.MaxValue : maxCount)
                         .ToList())
            {
                if (byRansom)
                {
                    EndCaptivityAction.ApplyByRansom(hero, holderKingdom.Leader);
                }
                else
                {
                    EndCaptivityAction.ApplyByPeace(hero, holderKingdom.Leader);
                }

                released++;
            }

            return released;
        }

        private static void ApplyGoldTransfer(Hero from, Hero to, int amount)
        {
            if (amount > 0 && to != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
            }
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

        private static ReignActionResult TransferPackageNavalAssets(Kingdom actor, Kingdom target, ReignWorldActionRecord action, out int transferred)
        {
            transferred = 0;
            Hero sourceHero = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "assetFromHeroStringId", string.Empty))
                ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)
                ?? actor?.Leader;
            Hero destinationHero = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "assetToHeroStringId", string.Empty))
                ?? ReignObjectResolver.FindHero(action.TargetHeroStringId)
                ?? target?.Leader
                ?? Hero.MainHero;
            MobileParty sourceParty = sourceHero == Hero.MainHero
                ? MobileParty.MainParty
                : sourceHero?.PartyBelongedTo ?? FindFirstKingdomPartyWithShips(actor);
            MobileParty destinationParty = destinationHero == Hero.MainHero
                ? MobileParty.MainParty
                : destinationHero?.PartyBelongedTo ?? (target == Clan.PlayerClan?.Kingdom ? MobileParty.MainParty : target?.Leader?.PartyBelongedTo);

            if (sourceParty?.Party == null)
            {
                return ReignActionResult.FailTerminal("Diplomatic package could not find a source party with ships.", "missing_ship_source", "world_state");
            }

            if (destinationParty?.Party == null)
            {
                return ReignActionResult.FailTerminal("Diplomatic package could not find a destination party for ships.", "missing_ship_destination", "world_state");
            }

            List<Ship> availableShips = sourceParty.Ships?
                .Where(x => x != null && (!x.IsUsedByQuest || ReadBoolTerm(action.TermsJson, "includeQuestShips", false)))
                .ToList() ?? new List<Ship>();
            if (availableShips.Count == 0)
            {
                return ReignActionResult.FailTerminal(sourceParty.Name + " has no transferable ships for the diplomatic package.", "no_transferable_ships", "world_state")
                    .WithDiagnostic("sourcePartyId", sourceParty.StringId ?? string.Empty)
                    .WithDiagnostic("sourcePartyName", sourceParty.Name?.ToString() ?? string.Empty);
            }

            int requestedAmount = ReadIntTerm(action.TermsJson, "amount", ReadIntTerm(action.TermsJson, "Amount", ReadIntTerm(action.TermsJson, "shipCount", 0)));
            bool transferAll = ReadBoolTerm(action.TermsJson, "allMatchingAssets", requestedAmount <= 0);
            int transferCount = transferAll || requestedAmount <= 0 ? availableShips.Count : System.Math.Min(requestedAmount, availableShips.Count);
            List<Ship> selectedShips = availableShips.Take(transferCount).ToList();

            List<Ship> verifiedShips = new List<Ship>();
            foreach (Ship ship in selectedShips)
            {
                ChangeShipOwnerAction.ApplyByTransferring(destinationParty.Party, ship);
                if ((destinationParty.Ships?.Contains(ship) ?? false) && !(sourceParty.Ships?.Contains(ship) ?? false))
                {
                    verifiedShips.Add(ship);
                }
            }

            transferred = verifiedShips.Count;
            if (transferred != selectedShips.Count)
            {
                return ReignActionResult.FailTerminal("Diplomatic package ship transfer did not match the promised count.", "ship_verification_failed", "state_verification");
            }

            ReignActionResult result = ReignActionResult.Done("Diplomatic package transferred " + verifiedShips.Count + " ship(s).")
                .WithDiagnostic("sourcePartyId", sourceParty.StringId ?? string.Empty)
                .WithDiagnostic("sourcePartyName", sourceParty.Name?.ToString() ?? string.Empty)
                .WithDiagnostic("destinationPartyId", destinationParty.StringId ?? string.Empty)
                .WithDiagnostic("destinationPartyName", destinationParty.Name?.ToString() ?? string.Empty)
                .WithDiagnostic("shipsTransferred", verifiedShips.Count.ToString(CultureInfo.InvariantCulture))
                .WithEffect("ship_transfer", "party", destinationParty.StringId ?? string.Empty, destinationParty.Name?.ToString() ?? string.Empty, "from=" + (sourceParty.StringId ?? string.Empty) + ";amount=" + verifiedShips.Count);

            foreach (Ship ship in verifiedShips)
            {
                string hullId = ship.ShipHull?.StringId ?? string.Empty;
                string shipName = ship.Name?.ToString() ?? hullId;
                result.WithChangedEntity("ship", hullId, shipName, "owner_changed");
            }

            return result;
        }

        private static void CopyResultDetails(ReignActionResult source, ReignActionResult target)
        {
            if (source == null || target == null)
            {
                return;
            }

            foreach (Dictionary<string, string> effect in source.Effects)
            {
                target.Effects.Add(new Dictionary<string, string>(effect));
            }

            foreach (Dictionary<string, string> entity in source.ChangedEntities)
            {
                target.ChangedEntities.Add(new Dictionary<string, string>(entity));
            }

            foreach (Dictionary<string, string> diagnostic in source.Diagnostics)
            {
                target.Diagnostics.Add(new Dictionary<string, string>(diagnostic));
            }
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
            ReignAICampaignBehavior.Instance?.RecordAgreementFromAction(action, kind, durationDays, isPublic);
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
