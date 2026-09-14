using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace ReignBeta.World
{
    internal static class ReignMarriageService
    {
        public static bool HasMarriageIntent(ReignWorldActionRecord action)
        {
            if (action == null)
            {
                return false;
            }

            if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance)
            {
                return true;
            }

            JObject terms = ReignValueService.ParseTerms(action);
            if (!string.IsNullOrWhiteSpace(ReignValueService.ReadStringTerm(terms, "marriageHero1StringId", string.Empty))
                || !string.IsNullOrWhiteSpace(ReignValueService.ReadStringTerm(terms, "marriageHero2StringId", string.Empty)))
            {
                return true;
            }
            string text = string.Join(" ", new[]
            {
                ReignValueService.ReadStringTerm(terms, "treatyKind", string.Empty),
                ReignValueService.ReadStringTerm(terms, "promiseText", string.Empty),
                ReignValueService.ReadStringTerm(terms, "packageText", string.Empty)
            });
            return text.IndexOf("marriage", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("marry", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("dowry", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool TryResolveCouple(ReignWorldActionRecord action, out Hero first, out Hero second, out string reason)
        {
            first = null;
            second = null;
            reason = string.Empty;
            if (global::TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel == null)
            {
                reason = "Bannerlord marriage model is unavailable.";
                return false;
            }

            JObject terms = ReignValueService.ParseTerms(action);
            if (action.Type == ReignWorldActionType.PoliticsMarriageAlliance
                && (ReignActionValidator.IsDialogueActionSource(action.Source)
                    || string.Equals(action.AuthorizationMode, "dialogue_acceptance", StringComparison.OrdinalIgnoreCase)))
            {
                return TryResolveConsentingCouple(action, terms, out first, out second, out reason);
            }
            Hero explicitFirst = ResolveFirstHero(terms,
                "marriageHero1StringId", "playerMarriageHeroStringId", "playerFamilyHeroStringId", "groomHeroStringId", "proposingHeroStringId");
            Hero explicitSecond = ResolveFirstHero(terms,
                "marriageHero2StringId", "npcMarriageHeroStringId", "otherFamilyHeroStringId", "brideHeroStringId", "spouseHeroStringId", "proposedSpouseHeroStringId");
            Hero requestedHero = ResolveFirstHero(terms,
                "requestedMarriageHeroStringId", "requestedMarriageHero", "marriageHeroStringId", "marriageHero");

            Clan actorClan = ReignObjectResolver.FindClan(action?.ActorClanStringId)
                ?? ReignObjectResolver.FindHero(action?.ActorHeroStringId)?.Clan;
            Clan targetClan = ReignObjectResolver.FindClan(action?.TargetClanStringId)
                ?? ReignObjectResolver.FindHero(action?.TargetHeroStringId)?.Clan;
            Hero fromHero = ResolveFirstHero(terms, "fromHeroStringId", "goldFromHeroStringId", "itemFromHeroStringId");
            Hero toHero = ResolveFirstHero(terms, "toHeroStringId", "goldToHeroStringId", "itemToHeroStringId");

            if (actorClan == null)
            {
                actorClan = fromHero?.Clan ?? ReignObjectResolver.FindKingdom(action?.ActorKingdomStringId)?.RulingClan;
            }

            if (targetClan == null)
            {
                targetClan = toHero?.Clan ?? ReignObjectResolver.FindKingdom(action?.TargetKingdomStringId)?.RulingClan;
            }

            if (actorClan == targetClan || actorClan == null || targetClan == null)
            {
                Clan otherClan = FirstDifferentClan(Clan.PlayerClan, actorClan, targetClan, fromHero?.Clan, toHero?.Clan,
                    ReignObjectResolver.FindKingdom(action?.ActorKingdomStringId)?.RulingClan,
                    ReignObjectResolver.FindKingdom(action?.TargetKingdomStringId)?.RulingClan);
                if (Clan.PlayerClan != null && otherClan != null)
                {
                    actorClan = Clan.PlayerClan;
                    targetClan = otherClan;
                }
            }

            if (actorClan == null || targetClan == null || actorClan == targetClan)
            {
                reason = "Marriage alliance requires two different active clans.";
                return false;
            }

            if (explicitFirst?.Clan == targetClan && explicitSecond?.Clan == actorClan)
            {
                Hero swap = explicitFirst;
                explicitFirst = explicitSecond;
                explicitSecond = swap;
            }
            else
            {
                if (explicitFirst?.Clan == targetClan && explicitSecond == null)
                {
                    explicitSecond = explicitFirst;
                    explicitFirst = null;
                }

                if (explicitSecond?.Clan == actorClan && explicitFirst == null)
                {
                    explicitFirst = explicitSecond;
                    explicitSecond = null;
                }
            }

            List<Hero> namedHeroes = FindNamedMarriageHeroes(terms, actorClan, targetClan);
            if (requestedHero != null && !namedHeroes.Contains(requestedHero))
            {
                namedHeroes.Insert(0, requestedHero);
            }

            foreach (Hero namedHero in namedHeroes)
            {
                if (namedHero.Clan == actorClan)
                {
                    if (explicitFirst != null && explicitFirst != namedHero)
                    {
                        reason = "Marriage request names more than one hero from " + actorClan.Name + ".";
                        return false;
                    }

                    explicitFirst = namedHero;
                }
                else if (namedHero.Clan == targetClan)
                {
                    if (explicitSecond != null && explicitSecond != namedHero)
                    {
                        reason = "Marriage request names more than one hero from " + targetClan.Name + ".";
                        return false;
                    }

                    explicitSecond = namedHero;
                }
                else
                {
                    reason = "Requested marriage hero " + namedHero.Name + " does not belong to either clan in this agreement.";
                    return false;
                }
            }

            if (explicitFirst != null && explicitFirst.Clan != actorClan)
            {
                reason = "Requested marriage hero " + explicitFirst.Name + " does not belong to " + actorClan.Name + ".";
                return false;
            }

            if (explicitSecond != null && explicitSecond.Clan != targetClan)
            {
                reason = "Requested marriage hero " + explicitSecond.Name + " does not belong to " + targetClan.Name + ".";
                return false;
            }

            bool firstIsRequired = explicitFirst != null;
            bool secondIsRequired = explicitSecond != null;
            bool protectRoyalHeirs = IsNpcWorldAlliance(action);
            if (protectRoyalHeirs && ((explicitFirst != null && IsProtectedRoyalHeir(explicitFirst)) || (explicitSecond != null && IsProtectedRoyalHeir(explicitSecond))))
            {
                reason = "NPC world diplomacy cannot use a ruler's oldest living child in a marriage package.";
                return false;
            }
            IEnumerable<Hero> firstCandidates = firstIsRequired
                ? new[] { explicitFirst }
                : OrderedCandidates(actorClan, fromHero, Hero.MainHero);
            IEnumerable<Hero> secondCandidates = secondIsRequired
                ? new[] { explicitSecond }
                : OrderedCandidates(targetClan, toHero);
            if (protectRoyalHeirs)
            {
                firstCandidates = firstCandidates.Where(x => !IsProtectedRoyalHeir(x));
                secondCandidates = secondCandidates.Where(x => !IsProtectedRoyalHeir(x));
            }
            foreach (Hero candidateFirst in firstCandidates)
            {
                foreach (Hero candidateSecond in secondCandidates)
                {
                    if (global::TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(candidateFirst, candidateSecond))
                    {
                        first = candidateFirst;
                        second = candidateSecond;
                        return true;
                    }
                }
            }

            if (firstIsRequired || secondIsRequired)
            {
                Hero requested = explicitFirst ?? explicitSecond;
                reason = "Requested marriage hero " + requested.Name + " is not currently eligible for a suitable marriage across these clans.";
                return false;
            }

            reason = "No unmarried, eligible hero pair exists between " + actorClan.Name + " and " + targetClan.Name + ".";
            return false;
        }

        private static bool TryResolveConsentingCouple(ReignWorldActionRecord action, JObject terms,
            out Hero first, out Hero second, out string reason)
        {
            first = ReignObjectResolver.FindHero(terms.Value<string>("marriageHero1StringId") ?? string.Empty);
            second = ReignObjectResolver.FindHero(terms.Value<string>("marriageHero2StringId") ?? string.Empty);
            JObject receipt = terms["authorityReceipt"] as JObject;
            if (first == null || second == null || first == second || first != Hero.MainHero
                || first.IsDead || second.IsDead
                || !string.Equals(receipt?.Value<string>("policy"), "personal_marriage_consent", StringComparison.Ordinal)
                || !string.Equals(action.AcceptedByHeroStringId, second.StringId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(action.ActorHeroStringId, first.StringId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(action.TargetHeroStringId, second.StringId, StringComparison.OrdinalIgnoreCase))
            {
                reason = "Marriage requires the exact living player and consenting spouse in the authorized agreement.";
                return false;
            }
            // Retries may observe the couple after native marriage moved one spouse
            // into the other's clan. Never select a replacement spouse or repeat Apply.
            if (first.Spouse == second && second.Spouse == first)
            {
                reason = string.Empty;
                return true;
            }
            if (first.Spouse != null || second.Spouse != null
                || !global::TaleWorlds.CampaignSystem.Campaign.Current.Models.MarriageModel.IsCoupleSuitableForMarriage(first, second))
            {
                reason = "The exact consenting couple is not currently eligible for marriage.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        public static bool TryApplyMarriage(ReignWorldActionRecord action, ReignActionResult result, out string reason)
        {
            if (!TryResolveCouple(action, out Hero first, out Hero second, out reason))
            {
                return false;
            }

            string firstClanId = first.Clan?.StringId ?? string.Empty;
            string secondClanId = second.Clan?.StringId ?? string.Empty;
            string firstName = first.Name?.ToString() ?? first.StringId;
            string secondName = second.Name?.ToString() ?? second.StringId;
            bool alreadyMarried = first.Spouse == second && second.Spouse == first;
            if (!alreadyMarried) MarriageAction.Apply(first, second, true);
            if (first.Spouse != second || second.Spouse != first)
            {
                reason = "Bannerlord did not complete the promised marriage between " + firstName + " and " + secondName + ".";
                return false;
            }

            // A dialogue receipt hashes the agreed terms. Keep that signed payload
            // stable across retries; the result effect carries resolved display names.
            if (!string.Equals(action.AuthorizationMode, "dialogue_acceptance", StringComparison.OrdinalIgnoreCase))
                StampResolvedCouple(action, first, second);
            result.WithEffect("marriage_completed", "hero", second.StringId, secondName,
                    "alreadyMarried=" + alreadyMarried + ";hero1=" + first.StringId + ";hero1Name=" + firstName + ";hero1Clan=" + firstClanId
                    + ";hero2=" + second.StringId + ";hero2Name=" + secondName + ";hero2Clan=" + secondClanId)
                .WithChangedEntity("hero", first.StringId, firstName, "spouse_changed")
                .WithChangedEntity("hero", second.StringId, secondName, "spouse_changed");
            reason = string.Empty;
            return true;
        }

        private static void StampResolvedCouple(ReignWorldActionRecord action, Hero first, Hero second)
        {
            JObject terms = ReignValueService.ParseTerms(action);
            terms["marriageHero1StringId"] = first.StringId;
            terms["marriageHero1Name"] = first.Name?.ToString() ?? first.StringId;
            terms["marriageHero2StringId"] = second.StringId;
            terms["marriageHero2Name"] = second.Name?.ToString() ?? second.StringId;
            action.TermsJson = terms.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static Hero ResolveFirstHero(JObject terms, params string[] keys)
        {
            foreach (string key in keys)
            {
                string id = ReignValueService.ReadStringTerm(terms, key, string.Empty);
                Hero hero = ReignValueService.ResolveHero(id) ?? ReignObjectResolver.FindHero(id);
                if (hero != null)
                {
                    return hero;
                }
            }

            return null;
        }

        private static Clan FirstDifferentClan(Clan baseline, params Clan[] clans)
        {
            return clans.FirstOrDefault(x => x != null && x != baseline && !x.IsEliminated);
        }

        private static List<Hero> FindNamedMarriageHeroes(JObject terms, Clan actorClan, Clan targetClan)
        {
            string text = string.Join(" ", new[]
            {
                ReignValueService.ReadStringTerm(terms, "packageText", string.Empty),
                ReignValueService.ReadStringTerm(terms, "marriageRequestText", string.Empty)
            });
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<Hero>();
            }

            IEnumerable<Hero> heroes = (actorClan?.Heroes ?? Enumerable.Empty<Hero>())
                .Concat(targetClan?.Heroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && x.IsAlive)
                .Distinct();
            return heroes
                .Where(x => ContainsWholeName(text, x.Name?.ToString()))
                .OrderByDescending(x => x.Name?.ToString()?.Length ?? 0)
                .ToList();
        }

        private static bool ContainsWholeName(string text, string name)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            int start = 0;
            while ((start = text.IndexOf(name, start, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = start + name.Length;
                bool leftBoundary = start == 0 || !char.IsLetterOrDigit(text[start - 1]);
                bool rightBoundary = end >= text.Length || !char.IsLetterOrDigit(text[end]);
                if (leftBoundary && rightBoundary)
                {
                    return true;
                }

                start++;
            }

            return false;
        }

        private static IEnumerable<Hero> OrderedCandidates(Clan clan, params Hero[] preferred)
        {
            HashSet<Hero> seen = new HashSet<Hero>();
            foreach (Hero hero in preferred.Concat(clan?.Heroes ?? Enumerable.Empty<Hero>()))
            {
                if (hero != null && hero.Clan == clan && hero.IsAlive && seen.Add(hero))
                {
                    yield return hero;
                }
            }
        }

        private static bool IsNpcWorldAlliance(ReignWorldActionRecord action)
        {
            if (!string.Equals(action?.Source, "world_diplomacy_director", StringComparison.OrdinalIgnoreCase)) return false;
            JObject terms = ReignValueService.ParseTerms(action);
            return string.Equals(ReignValueService.ReadStringTerm(terms, "treatyKind", string.Empty), "alliance_package", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsProtectedRoyalHeir(Hero hero)
        {
            Kingdom kingdom = hero?.Clan?.Kingdom;
            Hero ruler = kingdom?.Leader;
            if (ruler == null) return false;
            Hero protectedHeir = ruler.Children?
                .Where(x => x != null && x.IsAlive)
                .OrderByDescending(x => x.Age)
                .ThenBy(x => x.StringId)
                .FirstOrDefault();
            return protectedHeir == hero;
        }
    }
}
