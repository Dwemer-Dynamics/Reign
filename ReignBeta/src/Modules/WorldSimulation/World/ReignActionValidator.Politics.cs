using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Campaign;
using ReignBeta.Family;
using ReignBeta.Government;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using static ReignBeta.WorldSimulation.World.ReignWorldParsing;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Family;
using ReignBeta.Characters.World;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Government.Campaign;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.World;
using ReignBeta.WorldSimulation.CampaignCommands;
using ReignBeta.WorldSimulation.Domain;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Rulers;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.World
{
    public static partial class ReignActionValidator
    {
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

            if (party == null || !party.IsActive || party.LeaderHero != actorHero)
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

            bool observingOwnCapture = action.Type == ReignWorldActionType.StrategyCaptureSettlement
                && action.PlanStage == ReignStrategicPlanStage.SiegeOrAssault
                && target != null && (party.BesiegedSettlement == target || party.MapEvent?.MapEventSettlement == target);
            if ((party.MapEvent != null || party.BesiegedSettlement != null) && !observingOwnCapture)
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
                if (target.MapFaction == null || party.MapFaction == null
                    || (target.MapFaction != party.MapFaction && !target.MapFaction.IsAtWarWith(party.MapFaction)))
                {
                    reason = "Target settlement is not hostile to the actor party.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        private static bool ValidatePolitics(ReignWorldActionRecord action, out string reason, bool proposal = false)
        {
            if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance
                && (IsStructuredAction(action) || IsDialogueActionSource(action.Source)
                    || string.Equals(action.AuthorizationMode, "dialogue_acceptance", StringComparison.OrdinalIgnoreCase)))
                return ReignMarriageService.TryResolveCouple(action, out Hero _, out Hero _, out reason, proposal);
            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Clan claimant = ReignObjectResolver.FindClan(action.ActorClanStringId);

            if (action.NegotiatedCommand == "pledge_rebellion_support")
            {
                reason = ReignServices.Rebellion?.StructuredPledgePreflightFailure(
                    ReignObjectResolver.FindHero(action.ActorHeroStringId), ReignObjectResolver.FindHero(action.TargetHeroStringId),
                    action.ActorKingdomStringId, ReadStringTerm(action.TermsJson, "challengedRulerId", ""))
                    ?? "Rebellion campaign behavior is unavailable.";
                if (reason.Length > 0) return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsConsentGovernmentReduction)
            {
                Hero member = ReignObjectResolver.FindHero(action.ActorHeroStringId);
                Hero ruler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                ReignGovernmentCampaignBehavior government = ReignServices.Government;
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
                if (!proposal && !HasOperatorAuthorization(action) && (action.RequiresAcceptance || !ReadBoolTerm(action.TermsJson, "consentConfirmed", false)))
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
                if (!IsLiveKingdom(kingdom) || !IsLiveKingdom(counterpart) || ReignServices.Rebellion?.IsCivilWarPair(kingdom, counterpart) != true)
                {
                    reason = "Negotiated resolution requires both active sides of a Reign civil war.";
                    return false;
                }
                if (!IsStructuredAction(action) && (!string.Equals(action.AuthorizationMode, "negotiated", StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrWhiteSpace(action.NegotiationId) || string.IsNullOrWhiteSpace(action.TermsHash) || action.RequiresAcceptance))
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
                // Structured execution was bound to its complete saved agreement at
                // ValidateCore's entry. Legacy negotiations retain their own hash format.
                if (!IsStructuredAction(action) && (string.IsNullOrWhiteSpace(calculatedHash) || !string.Equals(calculatedHash, action.TermsHash, StringComparison.OrdinalIgnoreCase)))
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
                    bool recognitionContextValid = ReignServices.Rebellion?.ValidateRecognizedIndependenceContext(
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
                reason = ReignServices.Rebellion?.DeclarationPreflightFailure(kingdom, claimant, addressedRuler)
                    ?? "Rebellion campaign behavior is unavailable.";
                if (reason.Length > 0) return false;
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
    }
}
