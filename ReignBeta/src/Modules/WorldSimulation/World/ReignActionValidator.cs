using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Campaign;
using ReignBeta.Family;
using ReignBeta.Government;
using ReignBeta.PartyAgency;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace ReignBeta.World
{
    public static class ReignActionValidator
    {
        // Native personal relation is the final gameplay authority for violence.
        // Ordinary hostility may escalate to a party attack at -40, while a
        // conversation-born fight to the death is reserved for near-total enmity.
        public const int NpcAttackPlayerRelationThreshold = -40;
        public const int LethalDuelPlayerRelationThreshold = -80;

        public static bool Validate(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;

            if (action == null)
            {
                reason = "Action is null.";
                return false;
            }

            if (!ValidateActionShape(action, out reason))
            {
                return false;
            }

            if (string.Equals(action.AuthorizationMode, "negotiated", StringComparison.OrdinalIgnoreCase)
                && !ValidateNegotiatedAuthorization(action, out reason))
            {
                return false;
            }

            if (IsDialogueActionSource(action.Source)
                && !ValidateUniversalDialogueAuthorization(action, out reason))
            {
                return false;
            }

            if (RequiresDialogueSettlementAuthorization(action)
                && !ValidateDialogueSettlementAuthorization(action, out reason))
            {
                return false;
            }

            if (action.Type == ReignWorldActionType.Unknown)
            {
                reason = "Action type is unknown.";
                return false;
            }

            Hero personalActor = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            if (ReignPregnancyRestrictionPolicy.IsRestrictedNpc(personalActor)
                && ReignPregnancyRestrictionPolicy.IsRestrictedAction(action.Type))
            {
                reason = personalActor.Name + " is pregnant and unavailable for party or combat duty until childbirth.";
                return false;
            }

            if (action.TypeValue >= 1 && action.TypeValue < 100)
            {
                return ValidateDiplomacy(action, out reason);
            }

            if (action.TypeValue >= 100 && action.TypeValue < 200)
            {
                return ValidateStrategy(action, out reason);
            }

            if (action.TypeValue >= 200 && action.TypeValue < 300)
            {
                return ValidatePolitics(action, out reason);
            }

            if (action.TypeValue >= 300)
            {
                return ValidateRegular(action, out reason);
            }

            reason = "Unsupported action family.";
            return false;
        }

        private static bool ValidateDiplomacy(ReignWorldActionRecord action, out string reason)
        {
            if (IsRetiredDiplomacyAction(action.Type))
            {
                reason = action.Type + " has been retired from Bannerlord Reign beta because it is unreliable or has no meaningful game effect.";
                return false;
            }

            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);

            if (!IsLiveKingdom(actor))
            {
                reason = "Actor kingdom is missing or eliminated.";
                return false;
            }

            if (!IsLiveKingdom(target))
            {
                reason = "Target kingdom is missing or eliminated.";
                return false;
            }

            if (actor == target)
            {
                reason = "Actor and target kingdom are the same.";
                return false;
            }

            if (RequiresDialogueSettlementAuthorization(action)
                && !ValidateDialogueKingdomSettlementAuthority(action, actor, target,
                    out reason))
            {
                return false;
            }

            if (action.Type == ReignWorldActionType.DiplomacyBackRebellion)
            {
                string movementId = ReadStringTerm(action.TermsJson, "movementId", string.Empty);
                string rebelKingdomId = ReadStringTerm(action.TermsJson, "rebelKingdomId", string.Empty);
                ReignRebellionMovementRecord movement = ReignRebellionCampaignBehavior.Instance?.Movements
                    .FirstOrDefault(x => x != null && string.Equals(x.MovementId, movementId, StringComparison.OrdinalIgnoreCase));
                if (movement == null || movement.ResolutionApplied
                    || !string.Equals(movement.Stage, "civil_war", StringComparison.OrdinalIgnoreCase))
                {
                    reason = "The rebellion backing request is missing, resolved, or obsolete.";
                    return false;
                }
                if (!string.Equals(movement.ParentKingdomStringId, target.StringId, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(movement.RebelKingdomStringId, rebelKingdomId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "The backing action does not match the authoritative rebellion sides.";
                    return false;
                }
                Kingdom rebelKingdom = ReignObjectResolver.FindKingdom(rebelKingdomId);
                if (!IsLiveKingdom(rebelKingdom) || actor == rebelKingdom
                    || actor == target || actor.IsAtWarWith(rebelKingdom))
                {
                    reason = "The selected sponsor is not eligible to back this rebellion.";
                    return false;
                }
                if (actor.Leader == null || !string.Equals(actor.Leader.StringId,
                    action.ActorHeroStringId, StringComparison.OrdinalIgnoreCase)
                    || rebelKingdom.Leader == null || !string.Equals(rebelKingdom.Leader.StringId,
                        movement.LeaderHeroStringId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "A ruler named in the backing request has been replaced.";
                    return false;
                }
            }

            if (ReignRebellionCampaignBehavior.Instance?.IsCivilWarPair(actor, target) == true
                && (IsPeaceDemand(action.Type) || action.Type == ReignWorldActionType.DiplomacyMakePeace
                    || (action.Type == ReignWorldActionType.DiplomacyPackage && ReadBoolTerm(action.TermsJson, "makePeace", false))))
            {
                reason = "Parent-rebel peace requires a negotiated civil-war action with an explicit political result.";
                return false;
            }

            if (string.Equals(action.Source, "world_diplomacy_director", StringComparison.OrdinalIgnoreCase))
            {
                Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
                if (actor == playerKingdom)
                {
                    reason = "Autonomous world diplomacy cannot decide for the player's kingdom.";
                    return false;
                }
                if (target == playerKingdom && action.Type != ReignWorldActionType.DiplomacyDeclareWar
                    && action.Type != ReignWorldActionType.DiplomacyBreakTreaty)
                {
                    reason = "A bilateral proposal to the player's kingdom requires an accepted court decision.";
                    return false;
                }
                if ((!string.IsNullOrWhiteSpace(action.ActorHeroStringId)
                        && (actor.Leader == null || !string.Equals(actor.Leader.StringId,
                            action.ActorHeroStringId, StringComparison.OrdinalIgnoreCase)))
                    || (!string.IsNullOrWhiteSpace(action.TargetHeroStringId)
                        && (target.Leader == null || !string.Equals(target.Leader.StringId,
                            action.TargetHeroStringId, StringComparison.OrdinalIgnoreCase))))
                {
                    reason = "A ruler named by the diplomatic proposal has been replaced.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.DiplomacyDeclareWar && actor.IsAtWarWith(target))
            {
                reason = "Kingdoms are already at war.";
                return false;
            }

            if (RequiresPeace(action.Type) && actor.IsAtWarWith(target))
            {
                reason = "This agreement cannot be signed while the kingdoms are at war.";
                return false;
            }

            if (IsPeaceDemand(action.Type) && !actor.IsAtWarWith(target))
            {
                reason = "Kingdoms are not at war.";
                return false;
            }

            if (action.Type == ReignWorldActionType.DiplomacyDemandSettlementPeace)
            {
                return ValidateSettlementTransferTerms(action, actor, target, allowAllTargetFortifications: false, requireAny: true, out reason);
            }

            if (action.Type == ReignWorldActionType.DiplomacyDemandSurrenderPeace)
            {
                return ValidateSettlementTransferTerms(action, actor, target, allowAllTargetFortifications: true, requireAny: true, out reason);
            }

            if (action.Type == ReignWorldActionType.DiplomacyReturnOccupiedSettlement)
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);
                if (settlement == null || !settlement.IsFortification)
                {
                    reason = "A town or castle target is required for return occupied settlement.";
                    return false;
                }

                if (settlement.MapFaction != actor)
                {
                    reason = "The returning kingdom does not currently own the settlement.";
                    return false;
                }

                if (target.Leader == null)
                {
                    reason = "Receiving kingdom has no leader.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.DiplomacyPayToJoinWar)
            {
                string enemyId = ReadStringTerm(action.TermsJson, "enemyKingdomId", ReadStringTerm(action.TermsJson, "thirdKingdomId", string.Empty));
                Kingdom enemy = ReignObjectResolver.FindKingdom(enemyId);
                if (!IsLiveKingdom(enemy))
                {
                    reason = "terms.enemyKingdomId is required and must be a live kingdom.";
                    return false;
                }

                if (enemy == actor || enemy == target)
                {
                    reason = "Paid war target must be a third kingdom.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.DiplomacyRansomPackage)
            {
                string prisonerId = ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty);
                if (!string.IsNullOrWhiteSpace(prisonerId))
                {
                    Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
                    if (prisoner == null || !prisoner.IsPrisoner)
                    {
                        reason = "Named ransom target is missing or is no longer a prisoner: " + prisonerId;
                        return false;
                    }

                    IFaction holder = prisoner.PartyBelongedToAsPrisoner?.MapFaction;
                    if (holder != actor && holder != target)
                    {
                        reason = "Named ransom target is not held by either involved kingdom.";
                        return false;
                    }
                }
            }

            if (action.Type == ReignWorldActionType.DiplomacyPackage)
            {
                return ValidateDiplomaticPackage(action, actor, target, out reason);
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateNegotiatedAuthorization(ReignWorldActionRecord action, out string reason)
        {
            if (string.IsNullOrWhiteSpace(action.NegotiationId) || string.IsNullOrWhiteSpace(action.TermsHash)
                || string.IsNullOrWhiteSpace(action.NegotiatedCommand) || action.RequiresAcceptance)
            {
                reason = "Negotiated execution is missing its exact approval metadata.";
                return false;
            }
            string calculated = ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand, action.TermsJson);
            if (!string.Equals(calculated, action.TermsHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Negotiated terms no longer match the package approved by both rulers.";
                return false;
            }
            Kingdom actor = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom target = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Hero actorRuler = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero targetRuler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            if (actor?.Leader != actorRuler || target?.Leader != targetRuler)
            {
                reason = "Negotiated approval is stale because one of the current rulers has changed.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private static bool ValidateDialogueSettlementAuthorization(
            ReignWorldActionRecord action, out string reason)
        {
            if (!string.Equals(action.AuthorizationMode, "dialogue_acceptance",
                    StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(action.AcceptedByHeroStringId)
                || string.IsNullOrWhiteSpace(action.NegotiationId)
                || string.IsNullOrWhiteSpace(action.TermsHash)
                || string.IsNullOrWhiteSpace(action.NegotiatedCommand)
                || action.RequiresAcceptance)
            {
                reason = "Dialogue settlement transfer is missing its exact NPC consent metadata.";
                return false;
            }

            Hero acceptedBy = ReignObjectResolver.FindHero(action.AcceptedByHeroStringId);
            if (acceptedBy == null || acceptedBy.IsDead)
            {
                reason = "The NPC who agreed to the settlement transfer is missing or dead.";
                return false;
            }

            string calculated = ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand,
                action.TermsJson);
            if (string.IsNullOrWhiteSpace(calculated)
                || !string.Equals(calculated, action.TermsHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Dialogue settlement terms no longer match the exact package the NPC accepted.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateUniversalDialogueAuthorization(
            ReignWorldActionRecord action, out string reason)
        {
            if (!string.Equals(action.AuthorizationMode, "dialogue_acceptance", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(action.AcceptedByHeroStringId)
                || string.IsNullOrWhiteSpace(action.NegotiationId)
                || string.IsNullOrWhiteSpace(action.TermsHash)
                || string.IsNullOrWhiteSpace(action.NegotiatedCommand)
                || action.RequiresAcceptance)
            {
                reason = "Dialogue action is missing its universal authority receipt or exact consent metadata.";
                return false;
            }
            string calculated = ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand, action.TermsJson);
            if (!string.Equals(calculated, action.TermsHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Dialogue action terms changed after authority was granted.";
                return false;
            }
            JObject terms;
            try { terms = JObject.Parse(string.IsNullOrWhiteSpace(action.TermsJson) ? "{}" : action.TermsJson); }
            catch
            {
                reason = "Dialogue action authority receipt is not valid JSON.";
                return false;
            }
            JObject receipt = terms["authorityReceipt"] as JObject;
            string policy = receipt?.Value<string>("policy") ?? string.Empty;
            string acceptedBy = receipt?.Value<string>("acceptedByHeroStringId") ?? string.Empty;
            if (receipt == null || string.IsNullOrWhiteSpace(policy)
                || !string.Equals(acceptedBy, action.AcceptedByHeroStringId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Dialogue action authority receipt does not identify the agreeing NPC and policy.";
                return false;
            }
            Hero consentingHero = ReignObjectResolver.FindHero(action.AcceptedByHeroStringId);
            if (consentingHero == null || consentingHero.IsDead)
            {
                reason = "The NPC who authorized this dialogue action is unavailable.";
                return false;
            }
            if (action.TypeValue >= 1 && action.TypeValue < 100)
            {
                Kingdom represented = ReignObjectResolver.FindKingdom(receipt.Value<string>("representedKingdomId") ?? string.Empty);
                string representedRulerId = receipt.Value<string>("representedRulerHeroStringId") ?? string.Empty;
                bool representedSide = represented != null
                    && (string.Equals(represented.StringId, action.ActorKingdomStringId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(represented.StringId, action.TargetKingdomStringId, StringComparison.OrdinalIgnoreCase));
                if (!representedSide || represented.Leader == null
                    || !string.Equals(represented.Leader.StringId, representedRulerId, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "The represented realm or ruler in the dialogue authority receipt is stale.";
                    return false;
                }
                if (string.Equals(policy, "ambassador_charter_direct", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(receipt.Value<string>("postingId"))
                        || (receipt.Value<long?>("authorityRevision") ?? 0L) < 1L)
                    {
                        reason = "Ambassador authority receipt is missing its posting or charter revision.";
                        return false;
                    }
                }
                else if (!string.Equals(policy, "kingdom_ruler", StringComparison.OrdinalIgnoreCase)
                    || represented.Leader != consentingHero)
                {
                    reason = "National dialogue action was not authorized by a current ruler or chartered ambassador.";
                    return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        private static bool ValidateDialogueKingdomSettlementAuthority(
            ReignWorldActionRecord action, Kingdom actor, Kingdom target, out string reason)
        {
            Hero acceptedBy = ReignObjectResolver.FindHero(action.AcceptedByHeroStringId);
            Hero player = Hero.MainHero;
            bool exactRulerPair = actor?.Leader != null && target?.Leader != null
                && player != null && acceptedBy != null
                && ((actor.Leader == player && target.Leader == acceptedBy)
                    || (actor.Leader == acceptedBy && target.Leader == player));
            if (!exactRulerPair)
            {
                reason = "Kingdom settlement transfers require the player and agreeing NPC to remain the current rulers of the two realms.";
                return false;
            }

            Kingdom source;
            if (action.Type == ReignWorldActionType.DiplomacyReturnOccupiedSettlement)
            {
                source = actor;
            }
            else if (action.Type == ReignWorldActionType.DiplomacyPackage)
            {
                source = ReignObjectResolver.FindKingdom(ReadStringTerm(action.TermsJson,
                    "settlementFromKingdomId", target.StringId));
            }
            else
            {
                source = target;
            }

            string boundSourceId = ReadStringTerm(action.TermsJson,
                "settlementFromKingdomId", string.Empty);
            string boundAuthorizerId = ReadStringTerm(action.TermsJson,
                "settlementAuthorizingHeroStringId", string.Empty);
            string authorityKind = ReadStringTerm(action.TermsJson,
                "settlementAuthorityKind", string.Empty);
            if (source?.Leader == null
                || (source.Leader != acceptedBy && source.Leader != player)
                || !string.Equals(boundSourceId, source.StringId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(boundAuthorizerId, source.Leader.StringId,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(authorityKind, "kingdom_ruler",
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Settlement peace terms are not bound to the current source-kingdom ruler.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool RequiresDialogueSettlementAuthorization(
            ReignWorldActionRecord action)
        {
            if (action == null || !HasSettlementTransfer(action))
            {
                return false;
            }
            return string.Equals(action.AuthorizationMode, "dialogue_acceptance",
                    StringComparison.OrdinalIgnoreCase)
                || IsDialogueActionSource(action.Source);
        }

        private static bool HasSettlementTransfer(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return false;
            }
            if (action.Type == ReignWorldActionType.DiplomacyDemandSettlementPeace
                || action.Type == ReignWorldActionType.DiplomacyDemandSurrenderPeace
                || action.Type == ReignWorldActionType.DiplomacyReturnOccupiedSettlement)
            {
                return true;
            }
            if (action.Type != ReignWorldActionType.RegularTradePackage
                && action.Type != ReignWorldActionType.DiplomacyPackage)
            {
                return false;
            }
            return !string.IsNullOrWhiteSpace(action.TargetSettlementStringId)
                || ReadStringListTerm(action.TermsJson, "settlementIds").Count > 0
                || !string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson,
                    "targetSettlementStringId", string.Empty));
        }

        internal static bool IsDialogueActionSource(string source)
        {
            source = source ?? string.Empty;
            return source.IndexOf("dialogue", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(source, "hidden_action_planner",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "test_lab_action_gate",
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasWarBlockingAgreement(Kingdom actor, Kingdom target)
        {
            float now = TaleWorlds.CampaignSystem.Campaign.Current == null ? 0f : (float)CampaignTime.Now.ToDays;
            return ReignAICampaignBehavior.Instance?.Agreements.Any(x =>
                x != null
                && x.IsActive
                && !x.IsExpired(now)
                && BlocksWar(x.Kind)
                && ((string.Equals(x.ActorKingdomStringId, actor.StringId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.TargetKingdomStringId, target.StringId, StringComparison.OrdinalIgnoreCase))
                    || (string.Equals(x.ActorKingdomStringId, target.StringId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.TargetKingdomStringId, actor.StringId, StringComparison.OrdinalIgnoreCase)))) == true;
        }

        private static bool BlocksWar(string kind)
        {
            string value = (kind ?? string.Empty).Trim().ToLowerInvariant();
            return value == "alliance"
                || value == "defensive_pact"
                || value == "non_aggression_pact"
                || value == "demilitarized_border"
                || value == "guarantee_independence"
                || value == "trade_agreement"
                || value == "recognized_independence";
        }

        private static bool ValidateStrategy(ReignWorldActionRecord action, out string reason)
        {
            Hero actorHero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            MobileParty party = ReignObjectResolver.FindHeroParty(action.ActorHeroStringId);
            Settlement target = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            if (actorHero == null)
            {
                reason = "Actor hero is missing.";
                return false;
            }

            if (actorHero.IsPrisoner)
            {
                reason = "Actor hero is a prisoner and cannot execute strategy actions.";
                return false;
            }

            if (party == null || !party.IsActive || party.LeaderHero == null)
            {
                reason = "Actor hero does not lead an active party.";
                return false;
            }

            if (party.IsMainParty)
            {
                reason = "Strategy executor will not override the main player party.";
                return false;
            }

            if (!party.IsLordParty)
            {
                reason = "Actor party is not a lord party.";
                return false;
            }

            if (party.MapEvent != null || party.BesiegedSettlement != null)
            {
                reason = "Actor party is already in a battle or siege.";
                return false;
            }

            if (target == null)
            {
                reason = "Target settlement is missing.";
                return false;
            }

            if (target.IsHideout)
            {
                reason = "Hideouts are not valid strategic settlement targets.";
                return false;
            }

            if (action.Type == ReignWorldActionType.StrategyCaptureSettlement && !target.IsFortification)
            {
                reason = "Capture plans require a town or castle target.";
                return false;
            }

            if (action.Type == ReignWorldActionType.StrategyAttackSettlement || action.Type == ReignWorldActionType.StrategyCaptureSettlement)
            {
                if (target.MapFaction == null || party.MapFaction == null || !target.MapFaction.IsAtWarWith(party.MapFaction))
                {
                    reason = "Target settlement is not hostile to the actor party.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidatePolitics(ReignWorldActionRecord action, out string reason)
        {
            if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance
                && (IsDialogueActionSource(action.Source)
                    || string.Equals(action.AuthorizationMode, "dialogue_acceptance", StringComparison.OrdinalIgnoreCase)))
                return ReignMarriageService.TryResolveCouple(action, out Hero _, out Hero _, out reason);
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Clan claimant = ReignObjectResolver.FindClan(action.ActorClanStringId);

            if (action.Type == ReignWorldActionType.PoliticsConsentGovernmentReduction)
            {
                Hero member = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero ruler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                ReignGovernmentCampaignBehavior government = ReignGovernmentCampaignBehavior.Instance;
                ReignGovernmentStateRecord state = government?.GetGovernment(kingdom);
                bool belongsToRealm = member?.Clan?.Kingdom == kingdom
                    || government?.GetSeats(kingdom?.StringId).Any(x => string.Equals(
                        x.HeroStringId, member?.StringId, StringComparison.OrdinalIgnoreCase)) == true;
                if (!IsLiveKingdom(kingdom) || ruler == null || ruler != Hero.MainHero
                    || kingdom.Leader != ruler)
                {
                    reason = "Only the living player ruler can secure consent for a government reduction.";
                    return false;
                }
                if (member == null || member.IsDead || member.IsChild || member.IsPrisoner
                    || member == ruler || !belongsToRealm)
                {
                    reason = "Government-reduction consent requires an eligible NPC member of the ruler's realm.";
                    return false;
                }
                if (action.RequiresAcceptance || !ReadBoolTerm(action.TermsJson, "consentConfirmed", false))
                {
                    reason = "The NPC has not unambiguously consented to the government reduction.";
                    return false;
                }
                if (state == null || state.Level <= ReignGovernmentRules.MinimumLevel)
                {
                    reason = "The realm has no reducible government authority level.";
                    return false;
                }
                reason = string.Empty;
                return true;
            }

            if (action.Type == ReignWorldActionType.PoliticsRecruitLordToRebellion
                || action.Type == ReignWorldActionType.PoliticsJoinRebellion
                || action.Type == ReignWorldActionType.PoliticsSurrenderRebellion
                || action.Type == ReignWorldActionType.PoliticsResolveRebellionPledge
                || action.Type == ReignWorldActionType.PoliticsResolveRebellionSummons)
            {
                Hero actor = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero target = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                if (actor == null || actor.IsDead)
                {
                    reason = "A living actor hero is required for this rebellion action.";
                    return false;
                }
                if (action.RequiresAcceptance)
                {
                    reason = "The rebellion action has not yet been explicitly accepted or declared.";
                    return false;
                }
                if (action.Type != ReignWorldActionType.PoliticsSurrenderRebellion
                    && (target == null || target.IsDead))
                {
                    reason = "A living target lord is required for this rebellion action.";
                    return false;
                }
                if ((action.Type == ReignWorldActionType.PoliticsRecruitLordToRebellion
                        || action.Type == ReignWorldActionType.PoliticsJoinRebellion
                        || action.Type == ReignWorldActionType.PoliticsResolveRebellionPledge
                        || action.Type == ReignWorldActionType.PoliticsResolveRebellionSummons)
                    && actor != Hero.MainHero)
                {
                    reason = "Only the player clan leader can use this rebellion recruitment or allegiance action.";
                    return false;
                }
                reason = string.Empty;
                return true;
            }

            if (action.Type == ReignWorldActionType.PoliticsResolveCivilWar)
            {
                Kingdom counterpart = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
                string politicalResult = ReadStringTerm(action.TermsJson, "politicalResult", string.Empty);
                if (!IsLiveKingdom(kingdom) || !IsLiveKingdom(counterpart) || ReignRebellionCampaignBehavior.Instance?.IsCivilWarPair(kingdom, counterpart) != true)
                {
                    reason = "Negotiated resolution requires both active sides of a Reign civil war.";
                    return false;
                }
                if (!string.Equals(action.AuthorizationMode, "negotiated", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(action.NegotiationId) || string.IsNullOrWhiteSpace(action.TermsHash) || action.RequiresAcceptance)
                {
                    reason = "Civil-war settlement requires verified two-ruler negotiation authorization.";
                    return false;
                }
                Hero actorRuler = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero targetRuler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                if (actorRuler != kingdom.Leader || targetRuler != counterpart.Leader)
                {
                    reason = "Negotiated approval is stale because one of the named rulers has changed.";
                    return false;
                }
                string calculatedHash = ReignNegotiationTermsHasher.Compute("resolve_civil_war", action.TermsJson);
                if (string.IsNullOrWhiteSpace(calculatedHash) || !string.Equals(calculatedHash, action.TermsHash, StringComparison.OrdinalIgnoreCase))
                {
                    reason = "Negotiated terms no longer match the exact package approved by both rulers.";
                    return false;
                }
                if (politicalResult != "recognized_independence")
                {
                    reason = "Ordinary civil-war peace is disabled; only safeguarded recognition of rebel independence may resolve the war by agreement.";
                    return false;
                }
                if (politicalResult == "recognized_independence")
                {
                    string movementId = ReadStringTerm(action.TermsJson,
                        "movementId", string.Empty);
                    string rebelKingdomId = ReadStringTerm(action.TermsJson,
                        "rebelKingdomId", string.Empty);
                    string parentKingdomId = ReadStringTerm(action.TermsJson,
                        "parentKingdomId", string.Empty);
                    string recognitionReason = string.Empty;
                    bool recognitionContextValid = ReignRebellionCampaignBehavior
                        .Instance?.ValidateRecognizedIndependenceContext(
                            movementId, kingdom, counterpart, actorRuler,
                            targetRuler, out recognitionReason) == true;
                    if (string.IsNullOrWhiteSpace(movementId)
                        || !string.Equals(rebelKingdomId,
                            action.ActorKingdomStringId,
                            StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(parentKingdomId,
                            action.TargetKingdomStringId,
                            StringComparison.OrdinalIgnoreCase)
                        || !recognitionContextValid)
                    {
                        reason = string.IsNullOrWhiteSpace(recognitionReason)
                            ? "Recognition terms must identify the exact active rebel movement, rebel realm, and parent realm."
                            : recognitionReason;
                        return false;
                    }
                }
                reason = string.Empty;
                return true;
            }

            if (!IsLiveKingdom(kingdom))
            {
                reason = "Target kingdom is missing or eliminated.";
                return false;
            }

            if (claimant == null || claimant.IsEliminated)
            {
                reason = "Claimant clan is missing or eliminated.";
                return false;
            }

            if (action.Type != ReignWorldActionType.PoliticsMarriageAlliance && claimant == kingdom.RulingClan)
            {
                reason = "Claimant clan already rules the kingdom.";
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsExileClan)
            {
                if (claimant.Kingdom != kingdom)
                {
                    reason = "Clan must belong to the kingdom before exile.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (action.Type == ReignWorldActionType.PoliticsRestoreExiledClan)
            {
                if (claimant.Kingdom != null)
                {
                    reason = "Clan is not currently exiled or independent.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (action.Type == ReignWorldActionType.PoliticsEncourageClanDefection)
            {
                if (claimant.Kingdom == kingdom)
                {
                    reason = "Clan already belongs to the destination kingdom.";
                    return false;
                }

                if (claimant.Kingdom == null)
                {
                    reason = "Independent or exiled clans should use restore exiled clan.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (action.Type == ReignWorldActionType.PoliticsInstallRulingClan && claimant.Kingdom != kingdom)
            {
                reason = "Claimant clan must still belong to the kingdom to take the throne directly.";
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsStartRulingClanRebellion && claimant.Kingdom != kingdom)
            {
                reason = "Claimant clan must belong to the kingdom before starting a rebellion.";
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsStartRulingClanRebellion)
            {
                Hero declaringHero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero addressedRuler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                if (claimant != Clan.PlayerClan || declaringHero != Hero.MainHero || claimant?.Leader != declaringHero)
                {
                    reason = "Explicit rebellion declarations are reserved for the player clan leader; NPC outbreaks use weekly relationship rolls.";
                    return false;
                }
                if (addressedRuler == null || addressedRuler != kingdom.Leader)
                {
                    reason = "The declaration must be addressed to the current ruler being challenged.";
                    return false;
                }
                if (action.RequiresAcceptance)
                {
                    reason = "A declaration of rebellion is unilateral and must be queued as an immediate action.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.PoliticsStartRulingClanRebellion && !ValidateSupporterClans(action, kingdom, claimant, out reason))
            {
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance || action.Type == ReignWorldActionType.PoliticsMediateClanDispute)
            {
                Clan targetClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
                if (targetClan == null || targetClan.IsEliminated)
                {
                    reason = "Target clan is required and must be active for this politics action.";
                    return false;
                }

                if (targetClan == claimant)
                {
                    reason = "Actor clan and target clan must be different.";
                    return false;
                }

                if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance)
                {
                    return ReignMarriageService.TryResolveCouple(action, out Hero _, out Hero _, out reason);
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateRegular(ReignWorldActionRecord action, out string reason)
        {
            if (action.Type == ReignWorldActionType.RegularCreateClanAccord || action.Type == ReignWorldActionType.RegularCancelClanAccord)
            {
                if (ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance == null)
                { reason = "The Clan Accords service is unavailable."; return false; }
                return ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance.ValidateAction(action, out reason);
            }
            Hero actorHero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero targetHero = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            MobileParty actorParty = actorHero?.PartyBelongedTo;
            Settlement targetSettlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            if (IsCampaignCommandControlAction(action.Type))
            {
                return ReignCampaignCommandBehavior.ValidateControlAction(action, out reason);
            }

            if (action.Type == ReignWorldActionType.RegularTradePackage)
            {
                return ReignValueService.ValidateTradePackage(action, out reason, out ReignTradeAppraisal _);
            }

            if (RequiresActorHero(action.Type) && actorHero == null)
            {
                reason = "Actor hero is required for this regular action.";
                return false;
            }

            if (actorHero != null && actorHero.IsDead)
            {
                reason = "Actor hero is dead.";
                return false;
            }

            if (IsTemporaryPartyGuestAction(action.Type))
            {
                if (ReignTemporaryPartyGuestCampaignBehavior.Instance == null)
                {
                    reason = "The temporary noble guest service is unavailable.";
                    return false;
                }
                return ReignTemporaryPartyGuestCampaignBehavior.Instance.ValidateAction(action, actorHero, out reason);
            }

            if (action.Type == ReignWorldActionType.RegularRecruitEncounteredResident
                || action.Type == ReignWorldActionType.RegularReleaseResidentFromDuty)
            {
                if (ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance == null)
                { reason = "The encountered resident service is unavailable."; return false; }
                return ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance.ValidateResidentAction(action, out reason);
            }

            if (IsArrestAction(action.Type))
            {
                if (actorHero != Hero.MainHero)
                {
                    reason = "Conversation arrest actions must be performed by the player character.";
                    return false;
                }
                if (!ReignConversationEligibility.IsAdultLivingNpc(targetHero))
                {
                    reason = "Conversation arrest actions require a living adult non-player target.";
                    return false;
                }
                if (action.Type == ReignWorldActionType.RegularPrepareArrest
                    && string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson,
                        "accusation", action.Reason)))
                {
                    reason = "PrepareArrest requires a stated accusation.";
                    return false;
                }
                if (action.Type == ReignWorldActionType.RegularPlayerAttackParty)
                {
                    MobileParty targetParty = targetHero.PartyBelongedTo;
                    if (targetParty == null || !targetParty.IsActive
                        || targetParty.LeaderHero != targetHero
                        || PlayerEncounter.EncounteredParty != targetParty.Party)
                    {
                        reason = "PlayerAttackParty requires the accused to lead the active encountered party.";
                        return false;
                    }
                }
            }

            if (RequiresMapParty(action.Type))
            {
                if (actorHero.IsPrisoner)
                {
                    reason = "Actor hero is a prisoner and cannot receive map orders.";
                    return false;
                }

                if (actorParty == null || !actorParty.IsActive || actorParty.LeaderHero == null)
                {
                    reason = "Actor hero does not lead an active party.";
                    return false;
                }

                if (actorParty.IsMainParty)
                {
                    reason = "Regular AI orders will not override the main player party.";
                    return false;
                }

                if (action.Type == ReignWorldActionType.RegularSurrenderToPlayer)
                {
                    if (actorParty.MapEvent == null || !actorParty.MapEvent.IsPlayerMapEvent || PartyBase.MainParty?.MapEvent != actorParty.MapEvent)
                    {
                        reason = "SurrenderToPlayer requires an active player encounter with the actor party.";
                        return false;
                    }
                }
                else if (actorParty.MapEvent != null || actorParty.BesiegedSettlement != null)
                {
                    reason = "Actor party is already in a battle or siege.";
                    return false;
                }
            }

            if (RequiresSettlement(action.Type))
            {
                if (targetSettlement == null)
                {
                    reason = "Target settlement is required for this action.";
                    return false;
                }

                if (action.Type == ReignWorldActionType.RegularRaidVillage && !targetSettlement.IsVillage)
                {
                    reason = "RaidVillage requires a village target.";
                    return false;
                }

                if (action.Type == ReignWorldActionType.RegularBesiegeSettlement && !targetSettlement.IsFortification)
                {
                    reason = "BesiegeSettlement requires a town or castle target.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularFollowOnMap || action.Type == ReignWorldActionType.RegularAttackParty)
            {
                MobileParty targetParty = ReignObjectResolver.FindParty(ReadStringTerm(action.TermsJson, "targetPartyId", string.Empty)) ?? targetHero?.PartyBelongedTo;
                if (targetParty == null || !targetParty.IsActive)
                {
                    reason = action.Type + " requires a target party or target hero with an active party.";
                    return false;
                }

                if (targetParty == actorParty)
                {
                    reason = "Actor party and target party are the same.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularKillCharacter)
            {
                if (targetHero == null)
                {
                    reason = "KillCharacter requires a target hero.";
                    return false;
                }

                if (targetHero == Hero.MainHero)
                {
                    reason = "KillCharacter will not execute against the player character.";
                    return false;
                }

                if (targetHero.IsDead)
                {
                    reason = "Target hero is already dead.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularAttackPlayerParty)
            {
                if (!ReignConversationEligibility.IsAdultLivingNpc(actorHero))
                {
                    reason = "AttackPlayerParty requires a living adult non-player actor.";
                    return false;
                }

                int relation = actorHero.GetRelation(Hero.MainHero);
                if (relation > NpcAttackPlayerRelationThreshold)
                {
                    reason = "AttackPlayerParty requires personal relation "
                        + NpcAttackPlayerRelationThreshold + " or lower; current relation is " + relation + ".";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularDuelPlayer)
            {
                if (!ReignConversationEligibility.IsAdultLivingNpc(actorHero))
                {
                    reason = "DuelPlayer requires a living adult non-player actor.";
                    return false;
                }

                if (targetHero != null && targetHero != Hero.MainHero)
                {
                    reason = "DuelPlayer must target the player; its NPC opponent belongs in actorHeroStringId.";
                    return false;
                }

                if (IsLethalDuel(action))
                {
                    int relation = actorHero.GetRelation(Hero.MainHero);
                    if (relation > LethalDuelPlayerRelationThreshold)
                    {
                        reason = "A duel to the death requires personal relation "
                            + LethalDuelPlayerRelationThreshold + " or lower; current relation is " + relation + ".";
                        return false;
                    }
                }
            }

            if (action.Type == ReignWorldActionType.RegularGiveGoldToPlayer)
            {
                int amount = ReadRegularGoldAmount(action);
                if (amount <= 0)
                {
                    reason = "GoldAmount must be positive.";
                    return false;
                }

                if (actorHero == null || actorHero == Hero.MainHero || actorHero.Gold < amount)
                {
                    reason = "Actor hero cannot afford the gold transfer.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularTransferGold)
            {
                int amount = ReadRegularGoldAmount(action);
                Hero from = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "fromHeroStringId", string.Empty)) ?? actorHero;
                Hero to = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? targetHero ?? Hero.MainHero;
                if (amount <= 0 || from == null || to == null || from == to)
                {
                    reason = "TransferGold requires a positive GoldAmount and two different valid heroes.";
                    return false;
                }

                if (from.Gold < amount)
                {
                    reason = from.Name + " cannot afford the gold transfer.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularTransferItem)
            {
                Hero from = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "fromHeroStringId", string.Empty)) ?? actorHero;
                Hero to = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? targetHero ?? Hero.MainHero;
                MobileParty fromParty = from == Hero.MainHero ? MobileParty.MainParty : from?.PartyBelongedTo;
                MobileParty toParty = to == Hero.MainHero ? MobileParty.MainParty : to?.PartyBelongedTo;
                string itemId = ReadStringTerm(action.TermsJson, "itemId", ReadStringTerm(action.TermsJson, "item", string.Empty));
                ItemObject item = ReignObjectResolver.FindItem(itemId);
                int amount = ReadIntTerm(action.TermsJson, "amount", ReadIntTerm(action.TermsJson, "Amount", 1));
                if (from == null || to == null || fromParty == null || toParty == null || fromParty == toParty || item == null || amount <= 0)
                {
                    reason = "TransferItem requires different source/destination parties plus valid heroes, item, and amount.";
                    return false;
                }

                bool inventoryHasItem = fromParty != null && fromParty.ItemRoster.GetItemNumber(item) >= amount;
                bool equippedHasItem = amount == 1 && ReignObjectResolver.TryFindEquippedItem(
                    from,
                    itemId,
                    ReadStringTerm(action.TermsJson, "sourceEquipmentSlot", string.Empty),
                    ReadStringTerm(action.TermsJson, "sourceEquipmentSet", string.Empty),
                    out Equipment _,
                    out EquipmentIndex _,
                    out EquipmentElement _,
                    out string _);
                if (equippedHasItem
                    && ReignTemporaryPartyGuestCampaignBehavior.Instance
                        ?.IsTemporaryGuestEquipmentLocked(from) == true)
                {
                    reason = from.Name + " is a temporary party guest, so their equipped items cannot be changed or transferred.";
                    return false;
                }
                if (!inventoryHasItem && !equippedHasItem)
                {
                    reason = from.Name + " does not have enough " + item.Name + " in inventory or equipped slots.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularTransferWorkshop)
            {
                Workshop workshop = ReignObjectResolver.FindWorkshop(ReadStringTerm(action.TermsJson, "workshopId", ReadStringTerm(action.TermsJson, "workshop", string.Empty)));
                Hero to = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", string.Empty)) ?? targetHero ?? Hero.MainHero;
                if (workshop == null || workshop.Owner == null || workshop.WorkshopType == null || to == null)
                {
                    reason = "TransferWorkshop requires a valid workshop, current owner, workshop type, and receiving hero.";
                    return false;
                }
            }

            if (action.Type == ReignWorldActionType.RegularTransferPrisoner)
            {
                Hero prisoner = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "prisonerHeroStringId", action.TargetHeroStringId));
                if (prisoner == null || !prisoner.IsPrisoner)
                {
                    reason = "TransferPrisoner requires a target prisoner hero who is currently captive.";
                    return false;
                }
            }

            if (!ValidateRegularClanKingdom(action, out reason))
            {
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateRegularClanKingdom(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;
            switch (action.Type)
            {
                case ReignWorldActionType.RegularHirePlayerAsMercenary:
                case ReignWorldActionType.RegularOfferPlayerVassalage:
                {
                    Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
                    if (!IsLiveKingdom(kingdom))
                    {
                        reason = "A live target kingdom is required.";
                        return false;
                    }

                    if (Clan.PlayerClan == null)
                    {
                        reason = "Player clan is missing.";
                        return false;
                    }

                    return true;
                }
                case ReignWorldActionType.RegularDismissPlayerMercenary:
                    if (Clan.PlayerClan == null || !Clan.PlayerClan.IsUnderMercenaryService)
                    {
                        reason = "Player clan is not currently a mercenary clan.";
                        return false;
                    }

                    return true;
                case ReignWorldActionType.RegularDismissPlayerVassal:
                    if (Clan.PlayerClan == null || Clan.PlayerClan.Kingdom == null || Clan.PlayerClan.IsUnderMercenaryService)
                    {
                        reason = "Player clan is not currently a regular vassal.";
                        return false;
                    }

                    return true;
                case ReignWorldActionType.RegularJoinKingdom:
                {
                    Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId) ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan;
                    Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
                    if (clan == null || clan.IsEliminated || !IsLiveKingdom(kingdom))
                    {
                        reason = "JoinKingdom requires a live clan and target kingdom.";
                        return false;
                    }

                    if (clan.Kingdom == kingdom)
                    {
                        reason = "Clan already belongs to the target kingdom.";
                        return false;
                    }

                    return true;
                }
                case ReignWorldActionType.RegularLeaveKingdom:
                {
                    Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId) ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan;
                    if (clan == null || clan.Kingdom == null)
                    {
                        reason = "LeaveKingdom requires a clan currently in a kingdom.";
                        return false;
                    }

                    return true;
                }
                case ReignWorldActionType.RegularHireMercenaryClan:
                {
                    Clan clan = ReignObjectResolver.FindClan(action.TargetClanStringId) ?? ReignObjectResolver.FindClan(action.ActorClanStringId);
                    Kingdom kingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
                    if (clan == null || clan.IsEliminated || !IsLiveKingdom(kingdom))
                    {
                        reason = "HireMercenaryClan requires a live clan and hiring kingdom.";
                        return false;
                    }

                    return true;
                }
                case ReignWorldActionType.RegularJoinClan:
                {
                    Hero hero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                    Clan targetClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
                    if (hero == null || hero.IsDead || hero.IsPrisoner || targetClan == null || targetClan.IsEliminated)
                    {
                        reason = "JoinClan requires a living free hero and a live target clan.";
                        return false;
                    }

                    if (hero.CompanionOf != null)
                    {
                        reason = "Companion clan membership must be changed through companion actions.";
                        return false;
                    }

                    if (hero.Clan?.Leader == hero)
                    {
                        reason = "A clan leader cannot join another clan until native leadership succession is resolved.";
                        return false;
                    }

                    if (hero.Clan == targetClan)
                    {
                        reason = "Hero already belongs to the target clan.";
                        return false;
                    }

                    return true;
                }
                case ReignWorldActionType.RegularLeaveClan:
                {
                    Hero hero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                    if (hero == null || hero.IsDead || hero.IsPrisoner || hero.Clan == null)
                    {
                        reason = "LeaveClan requires a living free hero who currently belongs to a clan.";
                        return false;
                    }

                    if (hero.CompanionOf != null)
                    {
                        reason = "Companion clan membership must be changed through companion actions.";
                        return false;
                    }

                    if (hero.Clan.Leader == hero)
                    {
                        reason = "A clan leader cannot leave until native leadership succession is resolved.";
                        return false;
                    }

                    return true;
                }
                default:
                    return true;
            }
        }

        private static bool ValidateActionShape(ReignWorldActionRecord action, out string reason)
        {
            if (string.IsNullOrWhiteSpace(action.ActionId))
            {
                reason = "ActionId is required.";
                return false;
            }

            if (action.ActionId.Length > 80)
            {
                reason = "ActionId is too long.";
                return false;
            }

            if (action.MaxAttempts < 1 || action.MaxAttempts > 72)
            {
                reason = "MaxAttempts must be between 1 and 72.";
                return false;
            }

            if (action.MinimumTroops < 0 || action.MinimumTroops > 10000)
            {
                reason = "MinimumTroops must be between 0 and 10000.";
                return false;
            }

            if (action.DesiredStrength < 0 || action.DesiredStrength > 50000)
            {
                reason = "DesiredStrength must be between 0 and 50000.";
                return false;
            }

            if (action.Reason != null && action.Reason.Length > 2000)
            {
                reason = "Action reason is too long.";
                return false;
            }

            foreach (string id in new[] { action.ActorHeroStringId, action.TargetHeroStringId, action.ActorKingdomStringId, action.TargetKingdomStringId, action.ActorClanStringId, action.TargetClanStringId, action.TargetSettlementStringId })
            {
                if (id != null && id.Length > 160)
                {
                    reason = "A resolved object id is too long to be a Bannerlord StringId.";
                    return false;
                }
            }

            if (!string.IsNullOrWhiteSpace(action.TermsJson))
            {
                if (action.TermsJson.Length > 12000)
                {
                    reason = "TermsJson is too large for a compact world action.";
                    return false;
                }

                try
                {
                    JObject.Parse(action.TermsJson);
                }
                catch
                {
                    reason = "TermsJson is not valid JSON.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool RequiresActorHero(ReignWorldActionType type)
        {
            switch (type)
            {
                case ReignWorldActionType.RegularHirePlayerAsMercenary:
                case ReignWorldActionType.RegularDismissPlayerMercenary:
                case ReignWorldActionType.RegularOfferPlayerVassalage:
                case ReignWorldActionType.RegularDismissPlayerVassal:
                case ReignWorldActionType.RegularJoinKingdom:
                case ReignWorldActionType.RegularLeaveKingdom:
                case ReignWorldActionType.RegularHireMercenaryClan:
                case ReignWorldActionType.RegularTradePackage:
                    return false;
                default:
                    return true;
            }
        }

        private static bool IsArrestAction(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.RegularPrepareArrest
                || type == ReignWorldActionType.RegularConfirmArrest
                || type == ReignWorldActionType.RegularReleaseArrestedCharacter
                || type == ReignWorldActionType.RegularRescindArrestAccusation
                || type == ReignWorldActionType.RegularPlayerAttackParty;
        }

        private static bool IsCampaignCommandControlAction(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.RegularIssueCampaignOrder
                || type == ReignWorldActionType.RegularReviseCampaignOrder
                || type == ReignWorldActionType.RegularRespondToOrderReport
                || type == ReignWorldActionType.RegularCancelCampaignOrder;
        }

        private static bool IsTemporaryPartyGuestAction(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.RegularAcceptTemporaryPartyGuest
                || type == ReignWorldActionType.RegularRenewTemporaryPartyGuest
                || type == ReignWorldActionType.RegularEndTemporaryPartyGuest
                || type == ReignWorldActionType.RegularAcknowledgeOwnFactionCombatRisk;
        }

        private static bool RequiresMapParty(ReignWorldActionType type)
        {
            switch (type)
            {
                case ReignWorldActionType.RegularFollowOnMap:
                case ReignWorldActionType.RegularStopFollowing:
                case ReignWorldActionType.RegularGoToSettlement:
                case ReignWorldActionType.RegularPatrolAroundSettlement:
                case ReignWorldActionType.RegularWaitNearSettlement:
                case ReignWorldActionType.RegularRaidVillage:
                case ReignWorldActionType.RegularBesiegeSettlement:
                case ReignWorldActionType.RegularAttackParty:
                case ReignWorldActionType.RegularAttackPlayerParty:
                case ReignWorldActionType.RegularSurrenderToPlayer:
                case ReignWorldActionType.RegularLeavePlayerAlone:
                    return true;
                default:
                    return false;
            }
        }

        private static bool RequiresSettlement(ReignWorldActionType type)
        {
            switch (type)
            {
                case ReignWorldActionType.RegularGoToSettlement:
                case ReignWorldActionType.RegularPatrolAroundSettlement:
                case ReignWorldActionType.RegularWaitNearSettlement:
                case ReignWorldActionType.RegularRaidVillage:
                case ReignWorldActionType.RegularBesiegeSettlement:
                    return true;
                default:
                    return false;
            }
        }

        private static int ReadRegularGoldAmount(ReignWorldActionRecord action)
        {
            return ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", ReadIntTerm(action.TermsJson, "goldAmount", 0)));
        }

        private static bool IsLethalDuel(ReignWorldActionRecord action)
        {
            string mode = ReadStringTerm(action?.TermsJson, "duelMode",
                ReadStringTerm(action?.TermsJson, "mode", "training"))
                .Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');
            return ReadBoolTerm(action?.TermsJson, "lethal", false)
                || mode == "lethal"
                || mode == "death"
                || mode == "to_the_death"
                || mode == "honor"
                || mode == "honour";
        }

        private static bool IsRetiredDiplomacyAction(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.DiplomacySignTemporaryTruce
                || type == ReignWorldActionType.DiplomacyTradeEmbargo;
        }

        private static bool RequiresPeace(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.DiplomacySignTradeAgreement
                || type == ReignWorldActionType.DiplomacySignNonAggressionPact
                || type == ReignWorldActionType.DiplomacySignAlliance
                || type == ReignWorldActionType.DiplomacySignDefensivePact
                || type == ReignWorldActionType.DiplomacyCaravanProtectionAgreement
                || type == ReignWorldActionType.DiplomacySupplyAgreement
                || type == ReignWorldActionType.DiplomacyLoanOrSubsidy
                || type == ReignWorldActionType.DiplomacyGuaranteeIndependence
                || type == ReignWorldActionType.DiplomacyProtectorateOrVassalage;
        }

        private static bool IsPeaceDemand(ReignWorldActionType type)
        {
            return type == ReignWorldActionType.DiplomacyMakePeace
                || type == ReignWorldActionType.DiplomacyOfferTributePeace
                || type == ReignWorldActionType.DiplomacyDemandReparationsPeace
                || type == ReignWorldActionType.DiplomacyDemandSettlementPeace
                || type == ReignWorldActionType.DiplomacyDemandSurrenderPeace;
        }

        private static bool HasDiplomaticPackageTerm(ReignWorldActionRecord action)
        {
            JObject terms = string.IsNullOrWhiteSpace(action.TermsJson) ? new JObject() : JObject.Parse(action.TermsJson);
            string assetText = string.Join(" ", new[]
            {
                ReadStringTerm(action.TermsJson, "asset", string.Empty),
                ReadStringTerm(action.TermsJson, "assetClass", string.Empty),
                ReadStringTerm(action.TermsJson, "assetName", string.Empty)
            }).ToLowerInvariant();
            return ReadIntTerm(action.TermsJson, "gold", 0) > 0
                || ReadIntTerm(action.TermsJson, "GoldAmount", 0) > 0
                || !string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "item", string.Empty))
                || !string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "itemId", string.Empty))
                || ReignValueService.ReadItemTermObjects(terms).Count > 0
                || !string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "prisonerHero", string.Empty))
                || !string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty))
                || ReadStringListTerm(action.TermsJson, "settlementIds").Count > 0
                || !string.IsNullOrWhiteSpace(action.TargetSettlementStringId)
                || assetText.Contains("ship")
                || assetText.Contains("fleet")
                || assetText.Contains("naval")
                || ReignMarriageService.HasMarriageIntent(action)
                || string.Equals(ReadStringTerm(action.TermsJson, "treatyKind", string.Empty), "alliance_package", StringComparison.OrdinalIgnoreCase)
                || ReadBoolTerm(action.TermsJson, "guaranteeIndependence", false)
                || ReadBoolTerm(action.TermsJson, "militaryCommitment", false)
                || ReadBoolTerm(action.TermsJson, "warSupport", false);
        }

        private static bool ValidateDiplomaticPackage(ReignWorldActionRecord action, Kingdom actor, Kingdom target, out string reason)
        {
            bool alliancePackage = string.Equals(ReadStringTerm(action.TermsJson, "treatyKind", string.Empty), "alliance_package", StringComparison.OrdinalIgnoreCase);
            if (alliancePackage && actor.IsAtWarWith(target))
            {
                reason = "An alliance package cannot be executed while the kingdoms are at war.";
                return false;
            }
            if (!HasDiplomaticPackageTerm(action))
            {
                reason = "Diplomatic package requires a concrete executable term: gold, item, prisoner, settlement, ship, or real marriage.";
                return false;
            }

            if (!ReignValueService.ValidateMaterialTransfers(action, out reason))
            {
                return false;
            }

            if (ReignMarriageService.HasMarriageIntent(action)
                && !ReignMarriageService.TryResolveCouple(action, out Hero _, out Hero _, out reason))
            {
                return false;
            }
            if (alliancePackage && string.Equals(action.Source, "world_diplomacy_director", StringComparison.OrdinalIgnoreCase) && ReignMarriageService.HasMarriageIntent(action)
                && (string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "marriageHero1StringId", string.Empty)) || string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "marriageHero2StringId", string.Empty))))
            {
                reason = "NPC world alliance marriages require two explicit eligible hero IDs.";
                return false;
            }

            System.Collections.Generic.List<string> settlementIds = ReadStringListTerm(action.TermsJson, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Add(action.TargetSettlementStringId);
            }

            foreach (string settlementId in settlementIds)
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null)
                {
                    reason = "Package settlement could not be found: " + settlementId;
                    return false;
                }

                if (!settlement.IsFortification)
                {
                    reason = "Package settlement transfers only support towns and castles right now.";
                    return false;
                }

                string fromKingdomId = ReadStringTerm(action.TermsJson, "settlementFromKingdomId", target.StringId);
                Kingdom fromKingdom = ReignObjectResolver.FindKingdom(fromKingdomId);
                if (fromKingdom == null || settlement.MapFaction != fromKingdom)
                {
                    reason = "Package settlement is not owned by the declared source kingdom: " + settlement.Name;
                    return false;
                }

                string toKingdomId = ReadStringTerm(action.TermsJson, "settlementToKingdomId", actor.StringId);
                Kingdom toKingdom = ReignObjectResolver.FindKingdom(toKingdomId);
                if (toKingdom?.Leader == null)
                {
                    reason = "Package settlement destination has no ruler to receive the transfer.";
                    return false;
                }
            }

            string prisonerId = ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty);
            if (!string.IsNullOrWhiteSpace(prisonerId))
            {
                Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
                if (prisoner == null)
                {
                    reason = "Package prisoner could not be found: " + prisonerId;
                    return false;
                }

                if (!prisoner.IsPrisoner)
                {
                    reason = "Package prisoner target is not currently a prisoner: " + prisoner.Name;
                    return false;
                }

                if (prisoner.PartyBelongedToAsPrisoner?.MapFaction != actor && prisoner.PartyBelongedToAsPrisoner?.MapFaction != target)
                {
                    reason = "Package prisoner is not held by either involved kingdom.";
                    return false;
                }
            }

            int gold = ReadIntTerm(action.TermsJson, "gold", ReadIntTerm(action.TermsJson, "GoldAmount", 0));
            if (gold < 0)
            {
                reason = "Package gold cannot be negative.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool IsLiveKingdom(Kingdom kingdom)
        {
            return kingdom != null && !kingdom.IsEliminated;
        }

        private static bool ValidateSettlementTransferTerms(ReignWorldActionRecord action, Kingdom actor, Kingdom target, bool allowAllTargetFortifications, bool requireAny, out string reason)
        {
            List<string> settlementIds = ReadStringListTerm(action.TermsJson, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            bool allTargetFortifications = allowAllTargetFortifications && ReadBoolTerm(action.TermsJson, "allTargetFortifications", false);
            if (allTargetFortifications && settlementIds.Count == 0)
            {
                if (actor.Leader == null)
                {
                    reason = "Receiving kingdom has no leader to receive surrendered settlements.";
                    return false;
                }

                if (!Settlement.All.Any(x => x != null && x.IsFortification && x.MapFaction == target))
                {
                    reason = "Target kingdom has no towns or castles to transfer.";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            if (settlementIds.Count == 0)
            {
                reason = requireAny ? "At least one town or castle target is required." : string.Empty;
                return !requireAny;
            }

            foreach (string settlementId in settlementIds.Distinct())
            {
                if (!ValidateSurrenderedSettlement(settlementId, actor, target, out reason))
                {
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateSurrenderedSettlement(string settlementId, Kingdom actor, Kingdom target, out string reason)
        {
            Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
            if (settlement == null)
            {
                reason = "A settlement target is required for settlement surrender.";
                return false;
            }

            if (!settlement.IsFortification)
            {
                reason = "Only towns and castles can be demanded in peace terms.";
                return false;
            }

            if (settlement.MapFaction != target)
            {
                reason = "Demanded settlement is not owned by the surrendering kingdom.";
                return false;
            }

            if (actor.Leader == null)
            {
                reason = "Demanding kingdom has no leader to receive surrendered settlements.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidateSupporterClans(ReignWorldActionRecord action, Kingdom kingdom, Clan claimant, out string reason)
        {
            foreach (string clanId in ReadSupporterClanIds(action))
            {
                Clan supporter = ReignObjectResolver.FindClan(clanId);
                if (supporter == null || supporter.IsEliminated)
                {
                    reason = "Supporter clan could not be found: " + clanId;
                    return false;
                }

                if (supporter == kingdom.RulingClan)
                {
                    reason = "The ruling clan cannot be listed as a rebellion supporter.";
                    return false;
                }

                if (supporter.Kingdom != kingdom)
                {
                    reason = "Supporter clan must belong to the kingdom before the rebellion.";
                    return false;
                }

                if (supporter == claimant)
                {
                    continue;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static IEnumerable<string> ReadSupporterClanIds(ReignWorldActionRecord action)
        {
            foreach (string id in SplitCsv(action.SupporterClanIdsCsv))
            {
                yield return id;
            }

            if (string.IsNullOrWhiteSpace(action.TermsJson))
            {
                yield break;
            }

            JObject obj;
            try
            {
                obj = JObject.Parse(action.TermsJson);
            }
            catch
            {
                yield break;
            }

            JToken token = obj["supporterClanIds"];
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string id = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        yield return id;
                    }
                }
            }
        }

        private static IEnumerable<string> SplitCsv(string value)
        {
            return (value ?? string.Empty)
                .Split(',')
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
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
                return fallback;
            }
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
                if (int.TryParse(text, System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out int parsed))
                {
                    return parsed;
                }

                string digits = new string(text.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static System.Collections.Generic.List<string> ReadStringListTerm(string json, string key)
        {
            System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>();
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
                    foreach (JToken item in array)
                    {
                        string value = item?.ToString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            result.Add(value);
                        }
                    }
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
                return result;
            }

            return result;
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
                if (token == null)
                {
                    return fallback;
                }

                return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
