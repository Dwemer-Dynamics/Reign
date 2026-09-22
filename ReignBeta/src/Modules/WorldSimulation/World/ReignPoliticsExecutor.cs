using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.World
{
    public static class ReignPoliticsExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            if (!ReignActionValidator.Validate(action, out string validationFailure))
            {
                return ReignActionResult.ValidationFailed(validationFailure);
            }
            if (!ReignGovernmentActionGate.TryAuthorize(action, out ReignActionResult governmentResult))
                return governmentResult;

            Kingdom kingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Clan claimant = ReignObjectResolver.FindClan(action.ActorClanStringId);

            switch (action.Type)
            {
                case ReignWorldActionType.PoliticsStartRulingClanRebellion:
                    return StartRulingClanRebellion(action, kingdom, claimant);

                case ReignWorldActionType.PoliticsResolveCivilWar:
                    return ReignRebellionCampaignBehavior.Instance?.ResolveNegotiatedSettlement(action)
                        ?? ReignActionResult.FailTerminal("Rebellion campaign behavior is unavailable.", "rebellion_behavior_unavailable", "state_verification");

                case ReignWorldActionType.PoliticsRecruitLordToRebellion:
                    return RecruitLordToRebellion(action);

                case ReignWorldActionType.PoliticsJoinRebellion:
                    return JoinRebellion(action);

                case ReignWorldActionType.PoliticsSurrenderRebellion:
                    return SurrenderRebellion(action);

                case ReignWorldActionType.PoliticsResolveRebellionPledge:
                    return ResolveRebellionPledge(action);

                case ReignWorldActionType.PoliticsResolveRebellionSummons:
                    return ResolveRebellionSummons(action);

                case ReignWorldActionType.PoliticsConsentGovernmentReduction:
                    return RecordGovernmentReductionConsent(action);

                case ReignWorldActionType.PoliticsInstallRulingClan:
                    ChangeRulingClanAction.Apply(kingdom, claimant);
                    return ReignActionResult.Done(claimant.Name + " took control of " + kingdom.InformalName + ".")
                        .WithChangedEntity("kingdom", kingdom.StringId, kingdom.InformalName.ToString(), "ruling_clan_changed")
                        .WithChangedEntity("clan", claimant.StringId, claimant.Name.ToString(), "installed_as_ruling_clan")
                        .WithEffect("ruling_clan_changed", "kingdom", kingdom.StringId, kingdom.InformalName.ToString(), "newRulingClanId=" + claimant.StringId);

                case ReignWorldActionType.PoliticsMarriageAlliance:
                    ReignActionResult marriageResult = ReignActionResult.Done("Marriage alliance completed.");
                    if (!ReignMarriageService.TryApplyMarriage(action, marriageResult, out string marriageFailure))
                    {
                        return ReignActionResult.FailTerminal(marriageFailure, "marriage_verification_failed", "state_verification");
                    }

                    RecordAgreement(action, "marriage_alliance", ReadFloatTerm(action.TermsJson, "durationDays", 365f), true);
                    marriageResult.WithEffect("treaty_recorded", "clan", claimant.StringId, claimant.Name.ToString(), "kind=marriage_alliance");
                    string marriageReceipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(marriageResult);
                    return marriageResult.WithMessage("Verified alliance: " + marriageReceipt + ".");

                case ReignWorldActionType.PoliticsSupportClaimant:
                    RecordAgreement(action, "support_claimant", ReadFloatTerm(action.TermsJson, "durationDays", 120f), ReadBoolTerm(action.TermsJson, "isPublic", false));
                    return ReignActionResult.Done(claimant.Name + " received claimant support in " + kingdom.InformalName + ".")
                        .WithEffect("treaty_recorded", "clan", claimant.StringId, claimant.Name.ToString(), "kind=support_claimant");

                case ReignWorldActionType.PoliticsEncourageClanDefection:
                    return EncourageClanDefection(action, kingdom, claimant);

                case ReignWorldActionType.PoliticsExileClan:
                    return ExileClan(action, kingdom, claimant);

                case ReignWorldActionType.PoliticsRestoreExiledClan:
                    return RestoreExiledClan(action, kingdom, claimant);

                case ReignWorldActionType.PoliticsMediateClanDispute:
                    RecordAgreement(action, "mediate_clan_dispute", ReadFloatTerm(action.TermsJson, "durationDays", 30f), true);
                    return ReignActionResult.Done("A clan dispute was mediated around " + claimant.Name + " in " + kingdom.InformalName + ".")
                        .WithEffect("treaty_recorded", "clan", claimant.StringId, claimant.Name.ToString(), "kind=mediate_clan_dispute");

                default:
                    return ReignActionResult.FailTerminal("Unsupported politics action type: " + action.Type, "unsupported_action", "unsupported_action");
            }
        }

        private static ReignActionResult StartRulingClanRebellion(ReignWorldActionRecord action, Kingdom kingdom, Clan claimant)
        {
            List<Clan> rebelClans = new List<Clan> { claimant };
            foreach (string clanId in ReadSupporterClanIds(action))
            {
                Clan clan = ReignObjectResolver.FindClan(clanId);
                if (clan != null && clan.Kingdom == kingdom && clan != kingdom.RulingClan && !rebelClans.Contains(clan))
                {
                    rebelClans.Add(clan);
                }
            }

            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TryStartDirectedMovement(kingdom, claimant, rebelClans.Skip(1), out outcome))
            {
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome) ? "Rebellion campaign behavior is unavailable." : outcome);
            }

            return ReignActionResult.Done(outcome)
                .WithEffect("rebellion_started", "kingdom", kingdom.StringId, kingdom.InformalName.ToString(), "claimantClanId=" + claimant.StringId + ";changedClans=" + rebelClans.Count)
                .WithChangedEntity("clan", claimant.StringId, claimant.Name.ToString(), "rebellion_claimant");
        }

        private static ReignActionResult RecruitLordToRebellion(ReignWorldActionRecord action)
        {
            Hero recruiter = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero target = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TryRecruitLord(recruiter, target, out outcome))
            {
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Rebellion campaign behavior is unavailable." : outcome);
            }
            return ReignActionResult.Done(outcome)
                .WithEffect("rebellion_lord_recruited", "hero", target.StringId, target.Name.ToString(),
                    "clanId=" + (target.Clan?.StringId ?? string.Empty))
                .WithChangedEntity("clan", target.Clan?.StringId ?? string.Empty,
                    target.Clan?.Name?.ToString() ?? string.Empty, "joined_player_rebellion");
        }

        private static ReignActionResult JoinRebellion(ReignWorldActionRecord action)
        {
            Hero player = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero rebelLeader = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TryJoinNpcRebellion(player, rebelLeader, out outcome))
            {
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Rebellion campaign behavior is unavailable." : outcome);
            }
            return ReignActionResult.Done(outcome)
                .WithEffect("player_joined_rebellion", "hero", player.StringId, player.Name.ToString(),
                    "rebelLeaderId=" + rebelLeader.StringId)
                .WithChangedEntity("clan", player.Clan?.StringId ?? string.Empty,
                    player.Clan?.Name?.ToString() ?? string.Empty, "joined_rebellion");
        }

        private static ReignActionResult SurrenderRebellion(ReignWorldActionRecord action)
        {
            Hero surrendering = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            string movementId = ReadStringTerm(action.TermsJson, "movementId", string.Empty);
            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TrySurrender(surrendering, movementId, out outcome))
            {
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Rebellion campaign behavior is unavailable." : outcome);
            }
            return ReignActionResult.Done(outcome)
                .WithEffect("rebellion_surrendered", "hero", surrendering.StringId,
                    surrendering.Name.ToString(), "movementId=" + movementId);
        }

        private static ReignActionResult ResolveRebellionPledge(ReignWorldActionRecord action)
        {
            Hero player = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero lord = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            JObject terms = ParseRebellionTerms(action.TermsJson);
            string decision = terms.Value<string>("decision") ?? string.Empty;
            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TryRecordPreparationDecision(
                    player, lord, decision, terms.Value<string>("decisionReason") ?? action.Reason,
                    terms.Value<string>("requestChannel") ?? "conversation", action.ActionId, out outcome))
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Rebellion campaign behavior is unavailable." : outcome);
            return ReignActionResult.Done(outcome)
                .WithEffect("rebellion_preparation_decision", "hero", lord.StringId,
                    lord.Name.ToString(), "decision=" + decision);
        }

        private static ReignActionResult ResolveRebellionSummons(ReignWorldActionRecord action)
        {
            Hero player = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero ruler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            JObject terms = ParseRebellionTerms(action.TermsJson);
            string response = terms.Value<string>("playerResponse") ?? string.Empty;
            string verdict = terms.Value<string>("rulerVerdict") ?? string.Empty;
            string outcome = string.Empty;
            if (ReignRebellionCampaignBehavior.Instance == null
                || !ReignRebellionCampaignBehavior.Instance.TryResolvePreparationSummons(
                    player, ruler, response, verdict, out outcome))
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Rebellion campaign behavior is unavailable." : outcome);
            return ReignActionResult.Done(outcome)
                .WithEffect("rebellion_summons_resolved", "hero", player.StringId,
                    player.Name.ToString(), "response=" + response + ";verdict=" + verdict);
        }

        private static ReignActionResult RecordGovernmentReductionConsent(ReignWorldActionRecord action)
        {
            Hero member = ReignObjectResolver.FindHero(action.ActorHeroStringId);
            Hero ruler = ReignObjectResolver.FindHero(action.TargetHeroStringId);
            string outcome = string.Empty;
            if (ReignGovernmentCampaignBehavior.Instance == null
                || !ReignGovernmentCampaignBehavior.Instance.TryRecordReductionConsent(
                    ruler, member, out outcome))
            {
                return ReignActionResult.ValidationFailed(string.IsNullOrWhiteSpace(outcome)
                    ? "Government campaign behavior is unavailable." : outcome);
            }
            bool clanWide = member?.Clan?.Leader == member;
            return ReignActionResult.Done(outcome)
                .WithEffect("government_reduction_consent", clanWide ? "clan" : "hero",
                    clanWide ? member.Clan.StringId : member.StringId,
                    clanWide ? member.Clan.Name.ToString() : member.Name.ToString(),
                    "scope=" + (clanWide ? "clan" : "individual"));
        }

        private static ReignActionResult EncourageClanDefection(ReignWorldActionRecord action, Kingdom destination, Clan clan)
        {
            Kingdom oldKingdom = clan.Kingdom;
            if (oldKingdom == null)
            {
                return ReignActionResult.ValidationFailed("Defecting clan has no current kingdom.");
            }

            CampaignTime stayUntil = CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f));
            ChangeKingdomAction.ApplyByJoinToKingdomByDefection(clan, oldKingdom, destination, stayUntil);
            RecordAgreement(action, "clan_defection", ReadFloatTerm(action.TermsJson, "durationDays", 120f), ReadBoolTerm(action.TermsJson, "isPublic", true));
            return ReignActionResult.Done(clan.Name + " defected from " + oldKingdom.InformalName + " to " + destination.InformalName + ".")
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "kingdom_changed")
                .WithEffect("clan_defected", "clan", clan.StringId, clan.Name.ToString(), "from=" + oldKingdom.StringId + ";to=" + destination.StringId)
                .WithEffect("treaty_recorded", "clan", clan.StringId, clan.Name.ToString(), "kind=clan_defection");
        }

        private static JObject ParseRebellionTerms(string json)
        {
            try { return string.IsNullOrWhiteSpace(json) ? new JObject() : JObject.Parse(json); }
            catch { return new JObject(); }
        }

        private static ReignActionResult ExileClan(ReignWorldActionRecord action, Kingdom kingdom, Clan clan)
        {
            ChangeKingdomAction.ApplyByLeaveKingdom(clan, true);
            RecordAgreement(action, "exile_clan", ReadFloatTerm(action.TermsJson, "durationDays", 180f), true);
            return ReignActionResult.Done(clan.Name + " was exiled from " + kingdom.InformalName + ".")
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "exiled")
                .WithEffect("clan_exiled", "clan", clan.StringId, clan.Name.ToString(), "from=" + kingdom.StringId)
                .WithEffect("treaty_recorded", "clan", clan.StringId, clan.Name.ToString(), "kind=exile_clan");
        }

        private static ReignActionResult RestoreExiledClan(ReignWorldActionRecord action, Kingdom kingdom, Clan clan)
        {
            ChangeKingdomAction.ApplyByJoinToKingdom(clan, kingdom, CampaignTime.Now + CampaignTime.Days(ReadFloatTerm(action.TermsJson, "minimumStayDays", 30f)));
            RecordAgreement(action, "restore_exiled_clan", ReadFloatTerm(action.TermsJson, "durationDays", 120f), true);
            return ReignActionResult.Done(clan.Name + " was restored to " + kingdom.InformalName + ".")
                .WithChangedEntity("clan", clan.StringId, clan.Name.ToString(), "restored")
                .WithEffect("clan_restored", "clan", clan.StringId, clan.Name.ToString(), "to=" + kingdom.StringId)
                .WithEffect("treaty_recorded", "clan", clan.StringId, clan.Name.ToString(), "kind=restore_exiled_clan");
        }

        private static void RecordAgreement(ReignWorldActionRecord action, string kind, float durationDays, bool isPublic)
        {
            ReignAICampaignBehavior.Instance?.RecordAgreementFromAction(action, kind, durationDays, isPublic);
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
                    string id = item.ToString();
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
                ReignLog.Warn("Failed to parse politics float term JSON: " + json);
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
                ReignLog.Warn("Failed to parse politics bool term JSON: " + json);
                return fallback;
            }
        }

        private static string ReadStringTerm(string json, string key, string fallback)
        {
            if (string.IsNullOrWhiteSpace(json)) return fallback;
            try
            {
                JToken token = JObject.Parse(json)[key];
                return token == null ? fallback : token.ToString();
            }
            catch
            {
                return fallback;
            }
        }
    }
}
