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
                ReignRebellionMovementRecord movement = ReignServices.Rebellion?.Movements
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

            if (ReignServices.Rebellion?.IsCivilWarPair(actor, target) == true
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
            var wedding = ReignMarriageService.LinkedWedding(action);
            if (wedding != null)
            {
                bool exact = ReignMarriageService.MatchesLinkedTerms(wedding, action);
                reason = exact ? string.Empty : "The wedding no longer matches the signed diplomatic package.";
                return exact;
            }
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
