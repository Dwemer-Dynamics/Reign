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
        // Native personal relation is the final gameplay authority for violence.
        // Ordinary hostility may escalate to a party attack at -40, while a
        // conversation-born fight to the death is reserved for near-total enmity.
        public const int NpcAttackPlayerRelationThreshold = -40;
        public const int LethalDuelPlayerRelationThreshold = -80;

        public static bool Validate(ReignWorldActionRecord action, out string reason)
            => ValidateCore(action, false, out reason);

        // This path checks native feasibility only. It never queues or executes anything.
        internal static bool ValidateProposal(ReignWorldActionRecord action, out string reason)
            => ValidateCore(action, true, out reason);

        private static bool ValidateCore(ReignWorldActionRecord action, bool proposal, out string reason)
        {
            reason = string.Empty;

            if (action == null)
            {
                reason = "Action is null.";
                return false;
            }

            string releasePrisonerId = RulerReleaseActionPolicy.PrisonerId(action.Type,
                ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty),
                ReadStringTerm(action.TermsJson, "prisonerHero", string.Empty), action.TargetHeroStringId);
            if (!string.IsNullOrWhiteSpace(releasePrisonerId)
                && ReignRulerCampaignBehavior.BlocksRelease(ReignObjectResolver.FindHero(releasePrisonerId)))
            { reason = "This ruler is sentenced to execution and cannot be ransomed, exchanged or released by agreement."; return false; }
            var rulerTarget = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "prisonerHeroStringId", action.TargetHeroStringId));
            if (action.Type == ReignWorldActionType.RegularKillCharacter
                && (ReignRulerCampaignBehavior.BlocksRelease(rulerTarget)
                    || ReignServices.Ruler?.AwaitsDecision(rulerTarget) == true))
            { reason = "This captured ruler awaits sovereign judgment. Use the captivity judgment to choose their fate; execution takes eight days."; return false; }
            if ((action.Type.ToString().IndexOf("Peace", StringComparison.OrdinalIgnoreCase) >= 0
                || action.Type == ReignWorldActionType.PoliticsResolveCivilWar || action.Type == ReignWorldActionType.PoliticsSurrenderRebellion)
                && ReignRulerCampaignBehavior.BlocksPeace(ReignObjectResolver.FindKingdom(action.ActorKingdomStringId), ReignObjectResolver.FindKingdom(action.TargetKingdomStringId)))
            { reason = "Peace must wait while the captured ruler is under sentence."; return false; }
            if (action.Type == ReignWorldActionType.DiplomacyRansomPackage || action.Type == ReignWorldActionType.DiplomacyExchangePrisoners)
            {
                Kingdom actorRealm = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId), targetRealm = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
                bool SelectedSentence(Kingdom prisoners, Kingdom holder, int limit) => prisoners != null && holder != null && Hero.AllAliveHeroes
                    .Where(h => h.IsPrisoner && h.Clan?.Kingdom == prisoners && h.PartyBelongedToAsPrisoner?.MapFaction == holder)
                    .Take(limit <= 0 ? int.MaxValue : limit).Any(ReignRulerCampaignBehavior.BlocksRelease);
                int limit = ReadIntTerm(action.TermsJson, action.Type == ReignWorldActionType.DiplomacyRansomPackage ? "maxPrisoners" : "maxEachSide", action.Type == ReignWorldActionType.DiplomacyRansomPackage ? 5 : 3);
                bool bulk = action.Type == ReignWorldActionType.DiplomacyExchangePrisoners || string.IsNullOrWhiteSpace(ReadStringTerm(action.TermsJson, "prisonerHeroStringId", string.Empty));
                if (bulk && (SelectedSentence(targetRealm, actorRealm, limit) || (action.Type == ReignWorldActionType.DiplomacyExchangePrisoners && SelectedSentence(actorRealm, targetRealm, limit))))
                { reason = "This prisoner package includes a sentenced ruler; no payment or release may proceed."; return false; }
            }
            Clan joiningClan = null; Kingdom joiningRealm = null;
            switch (action.Type)
            {
                case ReignWorldActionType.RegularHirePlayerAsMercenary:
                case ReignWorldActionType.RegularOfferPlayerVassalage:
                    joiningClan = Clan.PlayerClan; joiningRealm = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId); break;
                case ReignWorldActionType.RegularJoinKingdom:
                    joiningClan = ReignObjectResolver.FindClan(action.ActorClanStringId) ?? ReignObjectResolver.FindHero(action.ActorHeroStringId)?.Clan;
                    joiningRealm = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId); break;
                case ReignWorldActionType.RegularHireMercenaryClan:
                case ReignWorldActionType.PoliticsEncourageClanDefection:
                case ReignWorldActionType.PoliticsRestoreExiledClan:
                    joiningClan = ReignObjectResolver.FindClan(action.TargetClanStringId) ?? ReignObjectResolver.FindClan(action.ActorClanStringId);
                    joiningRealm = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId) ?? ReignObjectResolver.FindKingdom(action.ActorKingdomStringId); break;
            }
            if (ReignRulerCampaignBehavior.Restricted(joiningClan, joiningRealm))
            { reason = "This clan's exile prevents kingdom membership or mercenary service until its term ends."; return false; }

            if (!ValidateActionShape(action, out reason))
            {
                return false;
            }

            bool structured = IsStructuredAction(action);
            if (structured && !proposal && ReignServices.Agreement?.Authorizes(action) != true)
            {
                reason = "This action does not match an authorized saved agreement step.";
                return false;
            }
            if (structured && !string.Equals(action.Source, "reign_console", StringComparison.Ordinal)
                && !ValidateStructuredAuthority(action, out reason)) return false;

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
                return ValidatePolitics(action, out reason, proposal);
            }

            if (action.TypeValue >= 300)
            {
                return ValidateRegular(action, out reason, proposal);
            }

            reason = "Unsupported action family.";
            return false;
        }

        // Resolve current office holders at both preview and execution. The other party
        // is the agreement recipient, never an arbitrary selected target hero.
        private static bool ValidateStructuredAuthority(ReignWorldActionRecord action, out string reason)
        {
            Hero actor = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero counterpart = ReignObjectResolver.FindHero(ReadStringTerm(action.TermsJson, "toHeroStringId", ""));
            Kingdom realm = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom destination = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            Clan clan = ReignObjectResolver.FindClan(action.ActorClanStringId);
            Clan targetClan = ReignObjectResolver.FindClan(action.TargetClanStringId);
            reason = "Both agreement participants must be living adults.";
            if (actor == null || actor.IsDead || actor.IsChild || counterpart == null || counterpart.IsDead || counterpart.IsChild) return false;

            reason = Reign.Core.Contracts.Actions.ReignActionAuthority.Validate(action.Type.ToString(),
                new Reign.Core.Contracts.Actions.ActionAuthorityFacts
                {
                    ActorId = actor.StringId, CounterpartyId = counterpart.StringId,
                    SelectedTargetHeroId = action.TargetHeroStringId,
                    ActingRulerId = realm?.Leader?.StringId ?? "", DestinationRulerId = destination?.Leader?.StringId ?? "",
                    RebelRulerId = ReignObjectResolver.FindKingdom(ReadStringTerm(action.TermsJson, "rebelKingdomId", ""))?.Leader?.StringId ?? "",
                    AffectedClanLeaderId = clan?.Leader?.StringId ?? "", ReceivingClanLeaderId = targetClan?.Leader?.StringId ?? "",
                    ActorActualClanLeaderId = actor.Clan?.Leader?.StringId ?? "", CounterpartyActualClanLeaderId = counterpart.Clan?.Leader?.StringId ?? "",
                    PlayerClanLeaderId = Clan.PlayerClan?.Leader?.StringId ?? "", PlayerCurrentRulerId = Clan.PlayerClan?.Kingdom?.Leader?.StringId ?? ""
                });
            return reason.Length == 0;
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
            if (action.AuthorizationMode == ReignBeta.Shared.Dialogue.PlayerActionRules.Policy)
                return ValidatePlayerTransferAuthority(action, out reason);
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

        private static bool ValidatePlayerTransferAuthority(ReignWorldActionRecord action, out string reason)
        {
            reason = "Player handover authority does not match this campaign, turn, player, recipient, or exact terms.";
            try
            {
                var terms = JObject.Parse(action.TermsJson ?? "{}");
                var grant = terms["playerAction"] as JObject;
                if (grant == null || Hero.MainHero == null || action.RequiresAcceptance
                    || action.AcceptedByHeroStringId != Hero.MainHero.StringId
                    || action.ActorHeroStringId != Hero.MainHero.StringId
                    || terms.Value<string>("fromHeroStringId") != Hero.MainHero.StringId
                    || terms.Value<string>("toHeroStringId") != action.TargetHeroStringId
                    || grant.Value<string>("schema") != ReignBeta.Shared.Dialogue.PlayerActionRules.Schema
                    || grant.Value<string>("campaignId") != ReignBeta.Characters.Integration.ReignCampaignIdentity.CurrentCampaignId()
                    || grant.Value<string>("timelineId") != (ReignServices.WorldHistory?.TimelineId ?? "main")
                    || grant.Value<string>("turnId") != action.NegotiationId
                    || action.TermsHash != ReignNegotiationTermsHasher.Compute(action.NegotiatedCommand, action.TermsJson)) return false;
                int slot = grant.Value<int?>("slot") ?? -1;
                var intents = ReignBeta.Shared.Dialogue.PlayerActionRules.Extract(grant.Value<string>("playerText"), grant.Value<string>("recipientName"), grant.Value<bool?>("singleRecipient") ?? true);
                if (slot < 0 || slot >= intents.Count || intents[slot].Error.Length > 0
                    || intents[slot].Command != action.NegotiatedCommand || intents[slot].Quote != grant.Value<string>("quote")
                    || action.ActionId != ReignBeta.Shared.Dialogue.PlayerActionRules.ActionId(grant.Value<string>("campaignId"), grant.Value<string>("timelineId"), action.NegotiationId, slot)) return false;
                if (action.Type == ReignWorldActionType.RegularTransferGold)
                {
                    if (action.NegotiatedCommand != "transfer_gold" || terms.Value<int?>("gold") != intents[slot].Amount
                        || terms.Value<string>("paymentPurpose") != intents[slot].Purpose) return false;
                }
                else if (action.Type != ReignWorldActionType.RegularTransferItem || action.NegotiatedCommand != "transfer_item"
                    || terms.Value<string>("delivery") != "personal") return false;
                var recipient = ReignObjectResolver.FindHero(action.TargetHeroStringId);
                if (recipient == null || !recipient.IsAlive || recipient.IsPrisoner
                    || !((recipient.CurrentSettlement != null && recipient.CurrentSettlement == Hero.MainHero.CurrentSettlement)
                        || (recipient.PartyBelongedTo != null && recipient.PartyBelongedTo == Hero.MainHero.PartyBelongedTo)))
                { reason = "The recipient is no longer present for this handover."; return false; }
                reason = ""; return true;
            }
            catch { return false; }
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

        internal static bool IsStructuredAction(ReignWorldActionRecord action) => action != null
            && (action.Source == "structured_agreement" || action.Source == "reign_console"
                || action.AuthorizationMode == "explicit_agreement" || action.AuthorizationMode == "operator");

        // Operator execution is a saved, exact command. It never fabricates either participant's consent.
        internal static bool HasOperatorAuthorization(ReignWorldActionRecord action) => action?.Source == "reign_console"
            && action.AuthorizationMode == "operator" && ReignServices.Agreement?.Authorizes(action) == true;

        internal static bool IsDialogueActionSource(string source)
        {
            source = source ?? string.Empty;
            return source.IndexOf("dialogue", StringComparison.OrdinalIgnoreCase) >= 0
                || string.Equals(source, "hidden_action_planner",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(source, "test_lab_action_gate",
                    StringComparison.OrdinalIgnoreCase);
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

        private static bool ValidateRegular(ReignWorldActionRecord action, out string reason, bool proposal = false)
        {
            if (action.Type == ReignWorldActionType.RegularCreateClanAccord || action.Type == ReignWorldActionType.RegularCancelClanAccord)
            {
                if (ReignServices.ClanAccords == null)
                { reason = "The Clan Accords service is unavailable."; return false; }
                return ReignServices.ClanAccords.ValidateAction(action, out reason, proposal);
            }
            Hero actorHero = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero targetHero = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            MobileParty actorParty = actorHero?.PartyBelongedTo;
            Settlement targetSettlement = ReignObjectResolver.FindSettlement(action.TargetSettlementStringId);

            if (IsCampaignCommandControlAction(action.Type))
            {
                return ReignCampaignCommandBehavior.ValidateControlAction(action, out reason, proposal);
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
                if (ReignServices.TemporaryPartyGuest == null)
                {
                    reason = "The temporary noble guest service is unavailable.";
                    return false;
                }
                return ReignServices.TemporaryPartyGuest.ValidateAction(action, actorHero, out reason, proposal);
            }

            if (action.Type == ReignWorldActionType.RegularRecruitEncounteredResident
                || action.Type == ReignWorldActionType.RegularReleaseResidentFromDuty)
            {
                if (ReignServices.EncounteredResidents == null)
                { reason = "The encountered resident service is unavailable."; return false; }
                return ReignServices.EncounteredResidents.ValidateResidentAction(action, out reason, proposal);
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

            bool useSceneFollow = action.Type == ReignWorldActionType.RegularFollowOnMap
                && ReignSceneActionMissionBehavior.Current?.CanFollow(actorHero, targetHero) == true;
            if (RequiresMapParty(action.Type) && !useSceneFollow && !(action.Type == ReignWorldActionType.RegularStopFollowing
                && ReignSceneActionMissionBehavior.Current?.IsFollowing(actorHero) == true))
            {
                if (actorHero.IsPrisoner)
                {
                    reason = "Actor hero is a prisoner and cannot receive map orders.";
                    return false;
                }

                if (actorParty == null || !actorParty.IsActive || actorParty.LeaderHero != actorHero)
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

            if ((!useSceneFollow && action.Type == ReignWorldActionType.RegularFollowOnMap)
                || action.Type == ReignWorldActionType.RegularAttackParty)
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

                if (ReignServices.GoldTransferService == null)
                { reason = "The payment service is unavailable."; return false; }
                if (!ReignServices.GoldTransferService.Validate(action, actorHero, Hero.MainHero, amount, out reason))
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

                if (ReignServices.GoldTransferService == null)
                { reason = "The payment service is unavailable."; return false; }
                if (!ReignServices.GoldTransferService.Validate(action, from, to, amount, out reason)) return false;
            }

            if (action.Type == ReignWorldActionType.RegularTransferItem)
            {
                if (ReignServices.PersonalItemService == null)
                { reason = "The personal item service is unavailable."; return false; }
                if (!ReignServices.PersonalItemService.Validate(action, actorHero, targetHero, out reason)) return false;
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

                    if (hero.Clan?.Leader == hero && ReignServices.Ruler?.CanRecruitFugitiveMember(hero, targetClan) != true)
                    {
                        reason = "A clan leader cannot join another clan until native leadership succession is resolved.";
                        return false;
                    }

                    if (hero.Clan == targetClan)
                    {
                        reason = "Hero already belongs to the target clan.";
                        return false;
                    }

                    var residents = ReignServices.EncounteredResidents;
                    if (residents?.UsesCompanionMembership(hero, targetClan) == true
                        && !residents.ValidateCompanionMembership(hero, out reason)) return false;

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
            string mode = ReignDuelModePolicy.Resolve(
                ReadStringTerm(action?.TermsJson, "duelMode", ReadStringTerm(action?.TermsJson, "mode", string.Empty)),
                ReadBoolTerm(action?.TermsJson, "lethal", false));
            return ReignDuelModePolicy.IsLethal(mode);
        }
    }
}
