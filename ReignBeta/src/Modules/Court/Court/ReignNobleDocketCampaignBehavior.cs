using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private void TryCreateNobleMatterForSlot(string campaignId, string timelineId, int day, int slot)
        {
            ReignNobleMatterSeverity severity = ReignRulerDocketRules.NobleMatterSeverity(campaignId, timelineId, day, slot);
            IReadOnlyList<ReignNobleMatterTemplate> pool = ReignNobleDocketCatalog.ForSeverity(severity);
            ReignNobleMatterTemplate selected = ReignRulerDocketRules.SelectNobleMatterTemplate(campaignId, timelineId, day, slot, severity);
            if (selected == null) return;
            int start = pool.ToList().FindIndex(x => x.Id == selected.Id);
            for (int offset = 0; offset < pool.Count; offset++)
            {
                ReignNobleDocketMatter matter = TryBuildNobleMatter(pool[(start + offset) % pool.Count], campaignId, timelineId, day, slot);
                if (matter == null) continue;
                EnsureRulerDocketState().NobleMatters.Add(matter);
                return;
            }
        }

        private ReignNobleDocketMatter TryBuildNobleMatter(ReignNobleMatterTemplate template,
            string campaignId, string timelineId, int day, int slot)
        {
            List<Hero> candidates = EligibleNobleMatterHeroes(day, slot);
            if (template.IsMurderInvestigation)
                return TryBuildMurderMatter(template, candidates, campaignId, timelineId, day, slot);
            List<Hero> principals = SelectNoblePrincipals(template, candidates, day, slot);
            if (principals.Count != template.MinimumPrincipals) return null;
            int truthSide = StableCourtOrder(campaignId + "|" + timelineId + "|" + day + "|" + slot + "|" + template.Id, day) % 3;
            string truth = truthSide == 0 ? principals[0].Name + " has the stronger underlying claim."
                : truthSide == 1 ? principals[1].Name + " has the stronger underlying claim."
                : "Neither principal is deliberately lying; incomplete or mistaken information caused the conflict.";
            ReignNobleDocketMatter matter = NewNobleMatter(template, campaignId, timelineId, day, truth);
            matter.CanonicalWinningRole = truthSide == 2 ? string.Empty
                : template.Category == ReignNobleMatterCategory.Marriage
                    && template.RequiresClanLeader ? "principal_a"
                    : truthSide == 0 ? "principal_a" : "principal_b";
            Hero marriageLeader = null;
            if (template.Category == ReignNobleMatterCategory.Marriage && template.RequiresClanLeader)
                marriageLeader = principals.Select(x => x.Clan?.Leader)
                    .FirstOrDefault(x => x != null && x.IsAlive && !x.IsPrisoner && !principals.Contains(x));
            if (template.Category == ReignNobleMatterCategory.Marriage && template.RequiresClanLeader
                && marriageLeader == null) return null;
            if (marriageLeader != null)
            {
                matter.Participants.Add(BuildNobleParticipant(principals[0], "principal_a",
                    template.UsesHiddenTruth && (truthSide == 0 || truthSide == 2)));
                matter.Participants.Add(BuildNobleParticipant(marriageLeader, "principal_b", false));
                matter.Participants.Add(BuildNobleParticipant(principals[1], "marriage_partner",
                    template.UsesHiddenTruth && (truthSide == 1 || truthSide == 2)));
            }
            else
                for (int index = 0; index < principals.Count; index++)
                    matter.Participants.Add(BuildNobleParticipant(principals[index], index == 0 ? "principal_a" : "principal_b",
                        template.UsesHiddenTruth && (truthSide == index || truthSide == 2)));
            if (template.UsesHiddenTruth)
            {
                Hero implicated = truthSide < 2 ? principals[truthSide] : null;
                matter.Evidence.Add(new ReignNobleEvidenceItem
                {
                    EvidenceId = "truth_chain", Label = "Corroborating testimony and records",
                    PublicDescription = truthSide < 2
                        ? "The surviving records and corroborating testimony support " + principals[truthSide].Name + "'s underlying claim."
                        : "The surviving records show that both principals relied on incomplete information rather than deliberately lying.",
                    PrivateMeaning = truth, SourceHeroId = principals[truthSide == 0 ? 1 : 0].StringId,
                    ImplicatesHeroId = implicated?.StringId ?? string.Empty, Reliable = true, CompleteChain = true
                });
            }
            return matter;
        }

        private ReignNobleDocketMatter TryBuildMurderMatter(ReignNobleMatterTemplate template,
            List<Hero> candidates, string campaignId, string timelineId, int day, int slot)
        {
            ReignChancellorOffice office = Chancellor;
            string chancellorHeroId = office != null && !office.IsVacant
                ? office.HeroId : string.Empty;
            Hero victim = candidates.Where(x => IsProtectedMurderVictim(x)
                    && !string.Equals(x.StringId, chancellorHeroId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => StableCourtOrder(x.StringId + "|victim|" + slot, day)).FirstOrDefault();
            if (victim == null) return null;
            List<Hero> suspects = candidates.Where(x => x != victim)
                .OrderBy(x => StableCourtOrder(x.StringId + "|suspect|" + slot, day)).Take(4).ToList();
            if (suspects.Count != 4) return null;
            Hero killer = suspects[StableCourtOrder(victim.StringId + "|killer|" + slot, day) % suspects.Count];
            string truth = killer.Name + " murdered " + victim.Name + ".";
            ReignNobleDocketMatter matter = NewNobleMatter(template, campaignId, timelineId, day, truth);
            matter.VictimHeroId = victim.StringId;
            matter.ActualCulpritHeroId = killer.StringId;
            matter.CanonicalWinningRole = "culprit:" + killer.StringId;
            string[] roles = { "accuser", "formally_accused", "strong_suspect", "strong_suspect" };
            for (int index = 0; index < suspects.Count; index++)
                matter.Participants.Add(BuildNobleParticipant(suspects[index], roles[index], suspects[index] == killer));
            Hero witness = suspects.First(x => x != killer);
            matter.Evidence.Add(new ReignNobleEvidenceItem
            {
                EvidenceId = "complete_chain", Label = "The complete evidentiary chain",
                PublicDescription = witness.Name + " can describe a witnessed movement, a matching physical trace, and a contradiction that together identify " + killer.Name + ".",
                PrivateMeaning = "The chain uniquely identifies " + killer.Name + " as the killer.",
                SourceHeroId = witness.StringId, ImplicatesHeroId = killer.StringId, Reliable = true, CompleteChain = true
            });
            int plausibleEvidenceIndex = 1;
            foreach (Hero suspect in suspects.Where(x => x != killer).Take(2))
            {
                matter.Evidence.Add(new ReignNobleEvidenceItem
                {
                    EvidenceId = "plausible_" + plausibleEvidenceIndex++, Label = "Plausible suspicion",
                    PublicDescription = "Circumstantial evidence gives the court a plausible but incomplete reason to suspect " + suspect.Name + ".",
                    PrivateMeaning = "This evidence is incomplete and does not overcome the reliable chain.",
                    ImplicatesHeroId = suspect.StringId, Reliable = false, CompleteChain = false
                });
            }
            return matter;
        }

        private ReignNobleDocketMatter NewNobleMatter(ReignNobleMatterTemplate template,
            string campaignId, string timelineId, int day, string truth)
        {
            string id = "noble_matter_" + Guid.NewGuid().ToString("N");
            return new ReignNobleDocketMatter
            {
                MatterId = id, CampaignId = campaignId, TimelineId = timelineId, ReignId = CurrentReignId(),
                ReceivedDay = day, TemplateId = template.Id, Title = template.Title, Severity = template.Severity,
                Category = template.Category, Premise = template.Premise, DemandA = template.DemandA, DemandB = template.DemandB,
                CanonicalTruth = truth, CanonicalTruthHash = ReignCourtTerms.Hash(id + "|" + truth)
            };
        }

        private List<Hero> EligibleNobleMatterHeroes(int day, int slot)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            HashSet<string> alreadyPending = new HashSet<string>(EnsureRulerDocketState().NobleMatters
                .Where(x => x?.IsPending == true).SelectMany(x => x.Participants ?? new List<ReignNobleMatterParticipant>())
                .Select(x => x.HeroId), StringComparer.OrdinalIgnoreCase);
            return Hero.AllAliveHeroes.Where(x => x != null && x != Hero.MainHero && x.IsLord && x.IsAlive
                    && x.IsActive && !x.IsChild && !x.IsPrisoner && x.Clan?.Kingdom == kingdom
                    && !alreadyPending.Contains(x.StringId) && !IsNobleVisitorReserved(x))
                .OrderBy(x => StableCourtOrder(x.StringId + "|noble|" + slot, day)).ToList();
        }

        private List<Hero> SelectNoblePrincipals(ReignNobleMatterTemplate template, List<Hero> candidates, int day, int slot)
        {
            if (template.RequiresSpouses)
            {
                foreach (Hero first in candidates.Where(x => x.Spouse != null && candidates.Contains(x.Spouse))
                    .OrderBy(x => StableCourtOrder(x.StringId + "|divorce|" + slot, day)))
                    if (first.GetRelation(first.Spouse) <= -15 || first.Spouse.GetRelation(first) <= -15)
                        return new List<Hero> { first, first.Spouse };
                return new List<Hero>();
            }
            if (template.RequiresUnmarriedPair)
            {
                for (int i = 0; i < candidates.Count; i++)
                for (int j = i + 1; j < candidates.Count; j++)
                {
                    Hero first = candidates[i], second = candidates[j];
                    if (first.Spouse != null || second.Spouse != null) continue;
                    if (TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel?.IsCoupleSuitableForMarriage(first, second) != true) continue;
                    if (template.RequiresLoverAffinity && (first.GetRelation(second) < 10 || second.GetRelation(first) < 10)) continue;
                    if (template.RequiresClanLeader && new[] { first.Clan?.Leader, second.Clan?.Leader }
                        .All(x => x == null || x == first || x == second || !x.IsAlive || x.IsPrisoner)) continue;
                    return new List<Hero> { first, second };
                }
                return new List<Hero>();
            }
            IEnumerable<Hero> ordered = candidates;
            if (template.RequiresClanLeader)
                ordered = ordered.OrderByDescending(x => x.Clan?.Leader == x)
                    .ThenBy(x => StableCourtOrder(x.StringId + "|leader|" + slot, day));
            List<Hero> result = ordered.Take(template.MinimumPrincipals).ToList();
            return result.Count == template.MinimumPrincipals ? result : new List<Hero>();
        }

        private static ReignNobleMatterParticipant BuildNobleParticipant(Hero hero, string role, bool knowsTruth)
        {
            return new ReignNobleMatterParticipant
            {
                HeroId = hero?.StringId ?? string.Empty, HeroName = hero?.Name?.ToString() ?? "Noble", Role = role,
                ClanId = hero?.Clan?.StringId ?? string.Empty, ClanLeaderHeroId = hero?.Clan?.Leader?.StringId ?? string.Empty,
                BirthClanId = ResolveBirthClanId(hero),
                IsClanLeader = hero?.Clan?.Leader == hero, KnowsCanonicalTruth = knowsTruth,
                PrivateKnowledge = knowsTruth ? "This participant consciously knows the canonical truth." : string.Empty,
                IsPrincipal = true
            };
        }

        private static string ResolveBirthClanId(Hero hero)
        {
            Clan parental = hero?.Father?.Clan ?? hero?.Mother?.Clan;
            if (parental != null && !parental.IsEliminated) return parental.StringId;
            return hero?.Clan?.StringId ?? string.Empty;
        }

        private static bool IsProtectedMurderVictim(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsLord || hero.IsChild
                || hero.IsPregnant || hero.Clan?.Leader == hero || hero.Clan == Clan.PlayerClan) return false;
            Hero player = Hero.MainHero;
            if (player != null && (hero == player.Spouse || hero == player.Father || hero == player.Mother
                || player.Children.Contains(hero))) return false;
            int viableAdults = hero.Clan?.Heroes.Count(x => x != null && x.IsAlive && !x.IsChild) ?? 0;
            return viableAdults > 1 && hero.CanDie(KillCharacterAction.KillCharacterActionDetail.Murdered);
        }

        public bool TryActivateNobleMatter(string matterId, out string error)
        {
            error = string.Empty;
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters.FirstOrDefault(x => x?.MatterId == matterId);
            if (matter == null || !matter.IsPending) { error = "The noble matter is no longer pending."; return false; }
            if (matter.ActivationCommitted) return true;
            if (matter.State == ReignNobleMatterState.FailedActivation) { error = matter.ActivationFailure; return false; }
            string murderVictimName = string.Empty;
            if (matter.IsMurder)
            {
                Hero victim = FindHero(matter.VictimHeroId);
                if (!IsProtectedMurderVictim(victim)) return FailNobleActivation(matter, "The reserved victim no longer satisfies the protected murder pool.", out error);
                murderVictimName = victim.Name?.ToString() ?? "a noble";
                Hero killer = FindHero(matter.ActualCulpritHeroId);
                if (killer == null || !matter.Participants.Any(x => x.HeroId == killer.StringId))
                    return FailNobleActivation(matter, "The reserved killer is no longer one of the four living suspects.", out error);
                KillCharacterAction.ApplyByMurder(victim, null, true);
                if (victim.IsAlive) return FailNobleActivation(matter, "The canonical murder action did not kill the reserved victim.", out error);
            }
            matter.ActivationCommitted = true;
            matter.ActivationReceiptId = "noble_activation_" + Guid.NewGuid().ToString("N");
            matter.ActivatedDay = CurrentDay();
            matter.State = ReignNobleMatterState.Active;
            matter.ActiveAudienceHeroIds = matter.Participants.Select(x => x.HeroId).Take(4).ToList();
            AddNobleHistory(matter, "activated", matter.IsMurder
                ? "News has just arrived that " + murderVictimName + " was murdered."
                : matter.Premise);
            StateChanged?.Invoke();
            return true;
        }

        private static bool FailNobleActivation(ReignNobleDocketMatter matter, string reason, out string error)
        {
            matter.State = ReignNobleMatterState.FailedActivation;
            matter.ActivationFailure = reason;
            error = reason;
            return false;
        }

        public void RevealNobleDemand(string matterId, string participantRole)
        {
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters.FirstOrDefault(x => x?.MatterId == matterId);
            if (matter == null || matter.State != ReignNobleMatterState.Active) return;
            if (string.Equals(participantRole, "principal_a", StringComparison.OrdinalIgnoreCase)
                || string.Equals(participantRole, "accuser", StringComparison.OrdinalIgnoreCase)) matter.DemandARevealed = true;
            if (string.Equals(participantRole, "principal_b", StringComparison.OrdinalIgnoreCase)
                || string.Equals(participantRole, "formally_accused", StringComparison.OrdinalIgnoreCase)) matter.DemandBRevealed = true;
            StateChanged?.Invoke();
        }

        public void RevealNobleEvidence(string matterId, string evidenceId)
        {
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters
                .FirstOrDefault(x => x?.MatterId == matterId);
            ReignNobleEvidenceItem evidence = matter?.Evidence.FirstOrDefault(x =>
                string.Equals(x.EvidenceId, evidenceId, StringComparison.OrdinalIgnoreCase));
            if (matter == null || matter.State != ReignNobleMatterState.Active
                || evidence == null || evidence.Revealed) return;
            evidence.Revealed = true;
            AddNobleHistory(matter, "evidence_revealed", evidence.PublicDescription);
            StateChanged?.Invoke();
        }

        public bool TryRuleNobleMatter(string matterId, ReignNobleRuling ruling, string selectedHeroId,
            int acceptanceTier, string immediateTermsJson, out string receipt)
        {
            receipt = string.Empty;
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters.FirstOrDefault(x => x?.MatterId == matterId);
            if (matter == null || matter.State != ReignNobleMatterState.Active) { receipt = "The noble matter is not active."; return false; }
            if (!HasRoyalCommandAccess) { receipt = "Noble matters may be ruled only while holding Court in the capital."; return false; }
            if (!matter.DemandsRevealed) { receipt = "Both principal demands must be stated before judgment."; return false; }
            if (matter.EffectsCommitted) { receipt = "This judgment has already been committed."; return false; }
            if (ruling == ReignNobleRuling.DeferToChancellor) return TryDeferNobleMatter(matter, out receipt);
            if (matter.IsMurder) return TryRuleMurderMatter(matter, ruling, selectedHeroId, out receipt);
            if (ruling != ReignNobleRuling.SideA && ruling != ReignNobleRuling.SideB)
            { receipt = "This matter requires a ruling for one principal or deferral."; return false; }
            ReignNobleMatterParticipant winner = matter.Participants.FirstOrDefault(x => x.Role == (ruling == ReignNobleRuling.SideA ? "principal_a" : "principal_b"));
            ReignNobleMatterParticipant loser = matter.Participants.FirstOrDefault(x => x.Role == (ruling == ReignNobleRuling.SideA ? "principal_b" : "principal_a"));
            if (winner == null || loser == null) { receipt = "The principal identities are incomplete."; return false; }
            string committedTerms = MergeRequiredImmediateTermsJson(matter, ruling,
                immediateTermsJson);
            if (!TryApplyImmediateNobleTerms(matter, committedTerms, out string termsError)) { receipt = termsError; return false; }
            matter.Ruling = ruling;
            matter.RuledForHeroId = winner.HeroId;
            matter.RuledAgainstHeroId = loser.HeroId;
            matter.AcceptanceTier = Math.Max(0, Math.Min(3, acceptanceTier));
            if (matter.Category == ReignNobleMatterCategory.Marriage
                && matter.Participants.Any(x => x.Role == "marriage_partner"))
                QueueMarriageRulingRelations(matter, ruling);
            else QueueNobleRulingRelations(matter);
            ApplyNobleReputationEffects(matter, loser);
            CompleteNobleJudgment(matter, ReignNobleMatterState.Ruled,
                "The ruler found for " + winner.HeroName + " against " + loser.HeroName + ".");
            receipt = "Judgment committed.";
            return true;
        }

        private bool TryApplyImmediateNobleTerms(ReignNobleDocketMatter matter, string termsJson, out string error)
        {
            error = string.Empty;
            JObject terms;
            try { terms = string.IsNullOrWhiteSpace(termsJson) ? new JObject() : JObject.Parse(termsJson); }
            catch (JsonException) { error = "Negotiated terms are not valid typed JSON."; return false; }
            HashSet<string> allowed = new HashSet<string>(new[] { "gold", "payerHeroId", "recipientHeroId",
                "marryHeroAId", "marryHeroBId", "divorceHeroAId", "divorceHeroBId", "apology",
                "admission", "censure", "withdrawClaim" }, StringComparer.OrdinalIgnoreCase);
            if (terms.Properties().Any(x => !allowed.Contains(x.Name)))
            { error = "Negotiated terms contain a future, unsupported, or untyped obligation."; return false; }
            int gold = Math.Max(0, terms.Value<int?>("gold") ?? 0);
            Hero payer = FindHero(terms.Value<string>("payerHeroId"));
            Hero recipient = FindHero(terms.Value<string>("recipientHeroId"));
            if (gold > 0)
            {
                if (payer == null || recipient == null || payer.Gold < gold)
                { error = "The immediate gold transfer is no longer valid or affordable."; return false; }
            }
            Hero marryA = FindHero(terms.Value<string>("marryHeroAId"));
            Hero marryB = FindHero(terms.Value<string>("marryHeroBId"));
            Hero divorceA = FindHero(terms.Value<string>("divorceHeroAId"));
            Hero divorceB = FindHero(terms.Value<string>("divorceHeroBId"));
            if ((marryA != null || marryB != null) && (divorceA != null || divorceB != null))
            { error = "One judgment cannot both marry and divorce a pair."; return false; }
            if (marryA != null || marryB != null)
            {
                if (marryA == null || marryB == null || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel?.IsCoupleSuitableForMarriage(marryA, marryB) != true)
                { error = "The marriage no longer passes the native marriage model."; return false; }
            }
            if (divorceA != null || divorceB != null)
            {
                if (divorceA == null || divorceB == null || divorceA.Spouse != divorceB || divorceB.Spouse != divorceA)
                { error = "The divorce no longer names a reciprocal married pair."; return false; }
            }
            // Commit native family state before fungible transfers. Every predicate
            // above is checked first, so a failed family action cannot spend gold.
            if (marryA != null)
            {
                Clan priorClanA = marryA.Clan, priorClanB = marryB.Clan;
                MarriageAction.Apply(marryA, marryB, true);
                if (marryA.Spouse != marryB || marryB.Spouse != marryA)
                {
                    marryA.Spouse = null; marryB.Spouse = null;
                    marryA.Clan = priorClanA; marryB.Clan = priorClanB;
                    error = "The native marriage action did not establish reciprocal spouse state.";
                    return false;
                }
            }
            if (divorceA != null)
            {
                divorceA.Spouse = null;
                divorceB.Spouse = null;
                if (Romance.RomanticStateList != null)
                    foreach (Romance.RomanticState romance in Romance.RomanticStateList.Where(x => x != null
                        && ((x.Person1 == divorceA && x.Person2 == divorceB) || (x.Person1 == divorceB && x.Person2 == divorceA))).ToList())
                        Romance.RomanticStateList.Remove(romance);
                RestoreDivorcedBirthClan(matter, divorceA);
                RestoreDivorcedBirthClan(matter, divorceB);
            }
            if (gold > 0) GiveGoldAction.ApplyBetweenCharacters(payer, recipient, gold, true);
            matter.ImmediateTermsJson = terms.ToString(Formatting.None);
            matter.AppliedEffectsHash = ReignCourtTerms.Hash(matter.MatterId + "|" + matter.ImmediateTermsJson);
            return true;
        }

        private static void RestoreDivorcedBirthClan(ReignNobleDocketMatter matter, Hero hero)
        {
            string birthClanId = matter.Participants.FirstOrDefault(x => x.HeroId == hero.StringId)?.BirthClanId;
            Clan birthClan = Clan.All.FirstOrDefault(x => x != null && !x.IsEliminated
                && (x.StringId == birthClanId || x.StringId == hero.Father?.Clan?.StringId
                    || x.StringId == hero.Mother?.Clan?.StringId));
            if (birthClan != null) hero.Clan = birthClan;
        }

        private static string BuildRequiredImmediateTermsJson(ReignNobleDocketMatter matter,
            ReignNobleRuling ruling)
        {
            if (ruling != ReignNobleRuling.SideA) return string.Empty;
            ReignNobleMatterParticipant first = matter.Participants.FirstOrDefault(x => x.Role == "principal_a");
            ReignNobleMatterParticipant second = matter.Category == ReignNobleMatterCategory.Marriage
                ? matter.Participants.FirstOrDefault(x => x.Role == "marriage_partner")
                    ?? matter.Participants.FirstOrDefault(x => x.Role == "principal_b")
                : matter.Participants.FirstOrDefault(x => x.Role == "principal_b");
            if (first == null || second == null) return string.Empty;
            JObject terms = new JObject();
            if (matter.Category == ReignNobleMatterCategory.Marriage)
            {
                terms["marryHeroAId"] = first.HeroId;
                terms["marryHeroBId"] = second.HeroId;
            }
            else if (matter.Category == ReignNobleMatterCategory.Divorce)
            {
                terms["divorceHeroAId"] = first.HeroId;
                terms["divorceHeroBId"] = second.HeroId;
            }
            return terms.ToString(Formatting.None);
        }

        private static string MergeRequiredImmediateTermsJson(ReignNobleDocketMatter matter,
            ReignNobleRuling ruling, string proposedTermsJson)
        {
            JObject proposed;
            try { proposed = string.IsNullOrWhiteSpace(proposedTermsJson)
                ? new JObject() : JObject.Parse(proposedTermsJson); }
            catch (JsonException) { return proposedTermsJson; }
            string requiredJson = BuildRequiredImmediateTermsJson(matter, ruling);
            if (!string.IsNullOrWhiteSpace(requiredJson))
                foreach (JProperty property in JObject.Parse(requiredJson).Properties())
                    proposed[property.Name] = property.Value;
            return proposed.ToString(Formatting.None);
        }

        private void QueueMarriageRulingRelations(ReignNobleDocketMatter matter,
            ReignNobleRuling ruling)
        {
            List<ReignNobleMatterParticipant> couple = matter.Participants
                .Where(x => x.Role == "principal_a" || x.Role == "marriage_partner").ToList();
            ReignNobleMatterParticipant leader = matter.Participants.FirstOrDefault(x => x.Role == "principal_b");
            int win = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            int loss = ReignRulerDocketRules.AcceptedLossRelation(
                ReignRulerDocketRules.NobleLoserRelation(matter.Severity), matter.AcceptanceTier);
            if (ruling == ReignNobleRuling.SideA)
            {
                foreach (ReignNobleMatterParticipant lover in couple)
                    QueueNobleRelation(matter, lover.HeroId, win, "The ruler permitted this noble's chosen marriage.");
                QueueNobleRelation(matter, leader?.HeroId, loss, "The ruler overruled this clan leader's marriage decision.");
                QueueClanSpillover(matter, leader?.ClanId, leader?.HeroId,
                    ReignRulerDocketRules.ClanSpilloverRelation(loss));
            }
            else
            {
                QueueNobleRelation(matter, leader?.HeroId, win, "The ruler upheld this clan leader's marriage decision.");
                foreach (ReignNobleMatterParticipant lover in couple)
                    QueueNobleRelation(matter, lover.HeroId, loss, "The ruler denied this noble's chosen marriage.");
            }
        }

        private static void ApplyNobleReputationEffects(ReignNobleDocketMatter matter,
            ReignNobleMatterParticipant loser)
        {
            ReignNobleMatterTemplate template = ReignNobleDocketCatalog.Find(matter.TemplateId);
            if (template == null) return;
            if (matter.Category == ReignNobleMatterCategory.Divorce)
            {
                if (matter.ImmediateTermsJson.IndexOf("divorceHeroAId",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    foreach (ReignNobleMatterParticipant spouse in matter.Participants.Take(2))
                        RecordNobleTag(matter, FindHero(spouse.HeroId), "divorcee",
                            "The ruler granted this noble a divorce.");
                // A denied divorce leaves the marriage intact and must not create
                // the divorcee reputation merely because the petitioner lost.
                return;
            }
            bool rulingEstablishesTags = matter.Ruling == ReignNobleRuling.SideA
                ? template.NegativeTagsApplyOnSideA
                : matter.Ruling == ReignNobleRuling.SideB && template.NegativeTagsApplyOnSideB;
            if (!rulingEstablishesTags) return;
            IEnumerable<ReignNobleMatterParticipant> subjects =
                template.ApplyNegativeTagsToAllRejectedParticipants
                    ? RejectedParticipants(matter)
                    : new[] { loser }.Where(x => x != null);
            foreach (ReignNobleMatterParticipant subject in subjects
                .GroupBy(x => x.HeroId, StringComparer.OrdinalIgnoreCase).Select(x => x.First()))
                foreach (string tag in template.ApplicableNegativeTags.Where(x => !string.IsNullOrWhiteSpace(x)))
                    RecordNobleTag(matter, FindHero(subject.HeroId), tag,
                        "The ruler's public judgment established this reputation.");
        }

        private static IEnumerable<ReignNobleMatterParticipant> RejectedParticipants(
            ReignNobleDocketMatter matter)
        {
            if (matter.Category == ReignNobleMatterCategory.Marriage
                && matter.Participants.Any(x => x.Role == "marriage_partner"))
                return matter.Ruling == ReignNobleRuling.SideA
                    ? matter.Participants.Where(x => x.Role == "principal_b")
                    : matter.Participants.Where(x => x.Role == "principal_a" || x.Role == "marriage_partner");
            string role = matter.Ruling == ReignNobleRuling.SideA ? "principal_b" : "principal_a";
            return matter.Participants.Where(x => x.Role == role);
        }

        private static void RecordNobleTag(ReignNobleDocketMatter matter, Hero subject,
            string tag, string summary)
        {
            if (subject == null || string.IsNullOrWhiteSpace(tag)) return;
            ReignWorldHistoryCampaignBehavior history = TaleWorlds.CampaignSystem.Campaign.Current
                ?.GetCampaignBehavior<ReignWorldHistoryCampaignBehavior>()
                ?? ReignWorldHistoryCampaignBehavior.Instance;
            history?.RecordDynamicSocialReputation(
                subject, tag, true, Math.Max(1, (int)matter.Severity + 1),
                matter.MatterId + "|judgment|" + subject.StringId + "|" + tag,
                summary, summary, new JObject
                {
                    ["matterId"] = matter.MatterId,
                    ["templateId"] = matter.TemplateId,
                    ["publicJudgment"] = matter.Ruling.ToString(),
                    ["canonicalTruthHash"] = matter.CanonicalTruthHash,
                    ["directReputationProducer"] = "noble_judgment",
                    ["sourceCorrelationId"] = matter.MatterId + "|judgment|"
                        + subject.StringId + "|" + tag
                });
        }

        private void QueueNobleRulingRelations(ReignNobleDocketMatter matter)
        {
            int win = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            int loss = ReignRulerDocketRules.AcceptedLossRelation(ReignRulerDocketRules.NobleLoserRelation(matter.Severity), matter.AcceptanceTier);
            List<ReignNobleMatterParticipant> winners = SupportedParticipants(matter).ToList();
            List<ReignNobleMatterParticipant> losers = RejectedParticipants(matter).ToList();
            foreach (ReignNobleMatterParticipant winner in winners)
                QueueNobleRelation(matter, winner.HeroId, win, "The ruler upheld this noble's position.");
            foreach (ReignNobleMatterParticipant loser in losers)
                QueueNobleRelation(matter, loser.HeroId, loss, "The ruler rejected this noble's position.");
            foreach (IGrouping<string, ReignNobleMatterParticipant> group in winners
                .Where(winner => losers.Any(loser => ReignRulerDocketRules.ShouldApplyOpposingClanSpillover(
                    matter.Category, winner.IsClanLeader, loser.IsClanLeader,
                    winner.ClanId, loser.ClanId)))
                .GroupBy(x => x.ClanId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                QueueClanSpillover(matter, group.Key, group.First().HeroId,
                    ReignRulerDocketRules.ClanSpilloverRelation(win));
            }
            foreach (IGrouping<string, ReignNobleMatterParticipant> group in losers
                .Where(loser => winners.Any(winner => ReignRulerDocketRules.ShouldApplyOpposingClanSpillover(
                    matter.Category, winner.IsClanLeader, loser.IsClanLeader,
                    winner.ClanId, loser.ClanId)))
                .GroupBy(x => x.ClanId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                QueueClanSpillover(matter, group.Key, group.First().HeroId,
                    ReignRulerDocketRules.ClanSpilloverRelation(loss));
        }

        private static IEnumerable<ReignNobleMatterParticipant> SupportedParticipants(
            ReignNobleDocketMatter matter)
        {
            string role = matter.Ruling == ReignNobleRuling.SideA ? "principal_a" : "principal_b";
            return matter.Participants.Where(x => x.Role == role);
        }

        private void QueueNobleRelation(ReignNobleDocketMatter matter, string observerId, int delta, string reason)
        {
            if (delta == 0 || string.IsNullOrWhiteSpace(observerId)) return;
            string reasonHash = ReignCourtTerms.Hash(reason ?? string.Empty);
            string id = matter.MatterId + "_relation_" + observerId + "_" + delta + "_"
                + reasonHash.Substring(0, Math.Min(12, reasonHash.Length));
            if (EnsureRulerDocketState().PendingRelationAdjustments.Any(x => x.AdjustmentId == id)) return;
            EnsureRulerDocketState().PendingRelationAdjustments.Add(new ReignDirectionalRelationAdjustment
            {
                AdjustmentId = id, ObserverHeroId = observerId, SubjectHeroId = Hero.MainHero?.StringId ?? string.Empty,
                Delta = delta, Reason = reason
            });
        }

        private void QueueClanSpillover(ReignNobleDocketMatter matter, string clanId, string principalId, int delta)
        {
            Clan clan = Clan.All.FirstOrDefault(x => x?.StringId == clanId);
            if (clan == null || delta == 0) return;
            foreach (Hero member in clan.Heroes.Where(x => x != null && x.IsAlive && x != Hero.MainHero && x.StringId != principalId))
                QueueNobleRelation(matter, member.StringId, delta, "The ruler's judgment materially affected this member's clan.");
        }

        private bool TryDeferNobleMatter(ReignNobleDocketMatter matter, out string receipt)
        {
            receipt = string.Empty;
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            Hero chancellor = FindHero(office?.HeroId);
            if (office == null || office.IsVacant || chancellor == null || !IsCurrentChancellorEligible(chancellor))
            { receipt = "No living, free, eligible Chancellor is available to investigate."; return false; }
            int charm = chancellor.GetSkillValue(DefaultSkills.Charm);
            int leadership = chancellor.GetSkillValue(DefaultSkills.Leadership);
            int steward = chancellor.GetSkillValue(DefaultSkills.Steward);
            string plausibleFailure = StableCourtOrder(matter.MatterId + "|chancellor_failure", CurrentDay()) % 2 == 0
                ? matter.Evidence.FirstOrDefault(x => !x.Reliable
                    && !string.IsNullOrWhiteSpace(x.ImplicatesHeroId))?.ImplicatesHeroId ?? string.Empty
                : string.Empty;
            matter.Investigation = new ReignChancellorInvestigation
            {
                Scheduled = true, DueDay = CurrentDay() + 1, ChancellorHeroId = chancellor.StringId,
                Charm = charm, Leadership = leadership, Steward = steward,
                SuccessChance = ReignRulerDocketRules.ChancellorInvestigationChance(charm, leadership, steward),
                SuccessRoll = ReignRulerDocketRules.ChancellorInvestigationSucceeds(matter.MatterId, charm, leadership, steward),
                FailureCandidateHeroId = plausibleFailure
            };
            matter.Ruling = ReignNobleRuling.DeferToChancellor;
            matter.State = ReignNobleMatterState.Deferred;
            matter.DecisionSummary = "The ruler deferred this matter to the Chancellor for further investigation.";
            foreach (ReignNobleMatterParticipant principal in matter.Participants.Where(x => x.IsPrincipal).Take(2))
                QueueNobleRelation(matter, principal.HeroId, -2, "The ruler deferred this dispute to the Chancellor.");
            AddNobleHistory(matter, "deferred", "The Chancellor will return an investigation result the next morning.");
            receipt = "Matter deferred to the Chancellor until next morning.";
            StateChanged?.Invoke();
            return true;
        }

        private void ProcessNobleInvestigations(int day)
        {
            foreach (ReignNobleDocketMatter matter in EnsureRulerDocketState().NobleMatters.Where(x => x != null
                && x.State == ReignNobleMatterState.Deferred && x.Investigation?.Scheduled == true
                && !x.Investigation.Resolved && x.Investigation.DueDay <= day).ToList())
            {
                string convicted = matter.Investigation.SuccessRoll ? matter.ActualCulpritHeroId : matter.Investigation.FailureCandidateHeroId;
                matter.Investigation.Resolved = true;
                if (matter.IsMurder && !string.IsNullOrWhiteSpace(convicted))
                {
                    matter.Investigation.Outcome = convicted == matter.ActualCulpritHeroId ? "correct_conviction" : "wrong_conviction";
                    TryCommitMurderConviction(matter, convicted, matter.Investigation.Outcome, out _);
                }
                else if (!matter.IsMurder)
                {
                    string role = matter.Investigation.SuccessRoll
                        ? matter.CanonicalWinningRole
                        : StableCourtOrder(matter.MatterId + "|ordinary_failure", day) % 2 == 0
                            ? OppositePrincipalRole(matter.CanonicalWinningRole) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(role)
                        && TryCommitChancellorOrdinaryFinding(matter, role, out string finding))
                        matter.Investigation.Outcome = finding;
                    else
                    {
                        matter.Investigation.Outcome = "insufficient_evidence";
                        CompleteNobleJudgment(matter, ReignNobleMatterState.Acquitted,
                            "The Chancellor returned insufficient evidence for a finding.");
                    }
                }
                else
                {
                    matter.Investigation.Outcome = "insufficient_evidence";
                    CompleteNobleJudgment(matter, ReignNobleMatterState.Acquitted, "The Chancellor returned insufficient evidence for a finding.");
                }
            }
        }

        private bool TryCommitChancellorOrdinaryFinding(ReignNobleDocketMatter matter,
            string winningRole, out string outcome)
        {
            outcome = string.Empty;
            ReignNobleRuling ruling = winningRole == "principal_a"
                ? ReignNobleRuling.SideA : ReignNobleRuling.SideB;
            ReignNobleMatterParticipant winner = matter.Participants.FirstOrDefault(x => x.Role == winningRole);
            ReignNobleMatterParticipant loser = matter.Participants.FirstOrDefault(x =>
                x.Role == (winningRole == "principal_a" ? "principal_b" : "principal_a"));
            if (winner == null || loser == null) return false;
            if (!TryApplyImmediateNobleTerms(matter,
                    MergeRequiredImmediateTermsJson(matter, ruling, string.Empty), out _)) return false;
            matter.Ruling = ruling;
            matter.RuledForHeroId = winner.HeroId;
            matter.RuledAgainstHeroId = loser.HeroId;
            matter.AcceptanceTier = 0;
            if (matter.Category == ReignNobleMatterCategory.Marriage
                && matter.Participants.Any(x => x.Role == "marriage_partner"))
                QueueMarriageRulingRelations(matter, ruling);
            else QueueNobleRulingRelations(matter);
            ApplyNobleReputationEffects(matter, loser);
            CompleteNobleJudgment(matter, ReignNobleMatterState.Ruled,
                "The Chancellor found for " + winner.HeroName + " against " + loser.HeroName + ".");
            outcome = matter.Investigation.SuccessRoll ? "correct_finding" : "wrong_finding";
            return true;
        }

        private static string OppositePrincipalRole(string role)
        {
            return role == "principal_a" ? "principal_b"
                : role == "principal_b" ? "principal_a" : string.Empty;
        }

        private bool TryRuleMurderMatter(ReignNobleDocketMatter matter, ReignNobleRuling ruling, string selectedHeroId, out string receipt)
        {
            if (ruling == ReignNobleRuling.AcquitAll)
            {
                matter.Ruling = ruling;
                CompleteNobleJudgment(matter, ReignNobleMatterState.Acquitted,
                    "The ruler acquitted all four suspects; the persisted truth remains unresolved publicly.");
                receipt = "All suspects acquitted. The case is closed unsolved.";
                return true;
            }
            if (ruling != ReignNobleRuling.ConvictParticipant || !matter.Participants.Any(x => x.HeroId == selectedHeroId))
            { receipt = "Select exactly one of the four living suspects to convict, acquit all, or defer."; return false; }
            return TryCommitMurderConviction(matter, selectedHeroId, "ruler_conviction", out receipt);
        }

        private bool TryCommitMurderConviction(ReignNobleDocketMatter matter, string convictedHeroId, string source, out string receipt)
        {
            receipt = string.Empty;
            Hero convicted = FindHero(convictedHeroId);
            Settlement capital = FindCurrentCapital();
            if (convicted == null || !convicted.IsAlive || capital?.Party == null || !convicted.CanBecomePrisoner())
            { receipt = "The selected suspect cannot be placed in the capital prison."; return false; }
            if (convicted.IsPrisoner) EndCaptivityAction.ApplyByReleasedByChoice(convicted, Hero.MainHero);
            TakePrisonerAction.Apply(capital.Party, convicted);
            if (!convicted.IsPrisoner) { receipt = "The canonical prisoner action did not establish custody."; return false; }
            matter.Ruling = ReignNobleRuling.ConvictParticipant;
            matter.ConvictedHeroId = convictedHeroId;
            matter.CustodyHold = new ReignCourtCustodyHold
            {
                Active = true, PrisonerHeroId = convictedHeroId, CapitalSettlementId = capital.StringId, StartedDay = CurrentDay()
            };
            RecordNobleTag(matter, convicted, "murderer",
                "The court publicly judged this noble responsible for murder.");
            RecordNobleTag(matter, convicted, "convicted_murderer",
                "The court convicted this noble of murder and committed them to capital custody.");
            CompleteNobleJudgment(matter, ReignNobleMatterState.Convicted,
                convicted.Name + " was convicted of murder and committed to the capital prison (" + source + ").");
            receipt = "Murder conviction committed; the prisoner is held in the capital.";
            return true;
        }

        private void CompleteNobleJudgment(ReignNobleDocketMatter matter, ReignNobleMatterState state, string summary)
        {
            matter.State = state;
            matter.DecidedDay = CurrentDay();
            matter.DecisionSummary = summary;
            matter.EffectsCommitted = true;
            if (string.IsNullOrWhiteSpace(matter.AppliedEffectsHash))
                matter.AppliedEffectsHash = ReignCourtTerms.Hash(matter.MatterId + "|" + matter.Ruling + "|" + summary);
            AddNobleHistory(matter, "judgment", summary);
            StateChanged?.Invoke();
            // Court is normally paused while a judgment is delivered. Start the
            // directional relationship delivery immediately instead of waiting
            // for the next campaign-hour tick.
            TryProcessPendingDocketRelationshipAdjustments();
        }

        private string AddNobleHistory(ReignNobleDocketMatter matter, string outcome, string detail)
        {
            string recordId = "noble_history_" + Guid.NewGuid().ToString("N");
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord
            {
                RecordId = recordId, ReignId = matter.ReignId,
                Type = "noble_matter", Outcome = outcome, PetitionId = matter.MatterId, Day = CurrentDay(),
                PetitionerHeroId = matter.Participants.FirstOrDefault()?.HeroId ?? string.Empty,
                PetitionerName = matter.Participants.FirstOrDefault()?.HeroName ?? string.Empty,
                Summary = matter.Title, Detail = detail, TranscriptId = matter.TranscriptId, SceneAssetPath = matter.SceneAssetPath
            });
            return recordId;
        }

        public bool TryExecuteMurderSentence(string matterId, out string receipt)
        {
            receipt = string.Empty;
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters
                .FirstOrDefault(x => x?.MatterId == matterId);
            Hero convicted = FindHero(matter?.ConvictedHeroId);
            if (matter == null || !matter.IsMurder || matter.State != ReignNobleMatterState.Convicted
                || matter.Irreversible || matter.CustodyHold?.Active != true || convicted == null
                || !convicted.IsAlive || !convicted.IsPrisoner)
            { receipt = "Only a living murderer held under this judgment may be executed."; return false; }
            Dictionary<string, int> rulerRelations = Hero.AllAliveHeroes.Where(x => x != null
                    && x != Hero.MainHero && !string.IsNullOrWhiteSpace(x.StringId))
                .ToDictionary(x => x.StringId, x => Hero.MainHero?.GetRelation(x) ?? 0,
                    StringComparer.OrdinalIgnoreCase);
            KillCharacterAction.ApplyByExecution(convicted, Hero.MainHero, true, true);
            if (convicted.IsAlive) { receipt = "The native execution action did not complete."; return false; }
            foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x != null && x != Hero.MainHero))
                if (rulerRelations.TryGetValue(hero.StringId, out int before)
                    && (Hero.MainHero?.GetRelation(hero) ?? before) < before)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, hero,
                        before - Hero.MainHero.GetRelation(hero), false);
            matter.CustodyHold.Active = false;
            matter.CustodyHold.Executed = true;
            matter.Irreversible = true;
            matter.DecisionSummary += " The convicted prisoner was executed; this consequence is irreversible.";
            AddNobleHistory(matter, "execution",
                convicted.Name + " was executed under the murder judgment. Native execution consequences remain, but automatic ruler relation losses were suppressed.");
            receipt = convicted.Name + " was executed. The death cannot be reversed.";
            StateChanged?.Invoke();
            return true;
        }

        public bool TryReverseNobleJudgment(string matterId, out string receipt)
        {
            receipt = string.Empty;
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters
                .FirstOrDefault(x => x?.MatterId == matterId);
            if (matter == null || (matter.State != ReignNobleMatterState.Ruled
                && matter.State != ReignNobleMatterState.Acquitted
                && matter.State != ReignNobleMatterState.Convicted))
            { receipt = "Only a completed noble judgment may be reversed."; return false; }
            if (matter.Irreversible || matter.CustodyHold?.Executed == true)
            { receipt = "This judgment includes an irreversible death and cannot be reversed."; return false; }

            if (matter.CustodyHold?.Active == true)
            {
                Hero prisoner = FindHero(matter.CustodyHold.PrisonerHeroId);
                if (prisoner != null && prisoner.IsPrisoner)
                    EndCaptivityAction.ApplyByReleasedByChoice(prisoner, Hero.MainHero);
                matter.CustodyHold.Active = false;
                matter.CustodyHold.ReleasedByReversal = true;
            }
            ReverseNobleTags(matter);
            ReverseNobleRelations(matter);
            foreach (ReignNobleMatterParticipant participant in matter.Participants)
                QueueNobleRelation(matter, participant.HeroId, -5,
                    "The ruler lost credibility by reversing a formal judgment.");
            matter.State = ReignNobleMatterState.Reversed;
            string recordId = AddNobleHistory(matter, "reversal",
                "The ruler reversed the public judgment. Safe social and relation effects were compensated; native family, gold, and death effects were not rewound.");
            matter.SupersededByRecordId = recordId;
            receipt = "Judgment reversed with a credibility cost. Irreversible native effects were left intact.";
            StateChanged?.Invoke();
            TryProcessPendingDocketRelationshipAdjustments();
            return true;
        }

        private void ReverseNobleRelations(ReignNobleDocketMatter matter)
        {
            int win = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            int loss = ReignRulerDocketRules.AcceptedLossRelation(
                ReignRulerDocketRules.NobleLoserRelation(matter.Severity), matter.AcceptanceTier);
            QueueNobleRelation(matter, matter.RuledForHeroId, -win,
                "The ruler reversed the judgment that had favored this noble.");
            QueueNobleRelation(matter, matter.RuledAgainstHeroId, -loss,
                "The ruler reversed the judgment that had rejected this noble.");
            if (matter.IsMurder && !string.IsNullOrWhiteSpace(matter.ConvictedHeroId))
                QueueNobleRelation(matter, matter.ConvictedHeroId,
                    ReignRulerDocketRules.NobleWinnerRelation(matter.Severity),
                    "The ruler vacated this noble's murder conviction while they still lived.");
        }

        private static void ReverseNobleTags(ReignNobleDocketMatter matter)
        {
            ReignNobleMatterTemplate template = ReignNobleDocketCatalog.Find(matter.TemplateId);
            IEnumerable<string> tags = (template?.ApplicableNegativeTags ?? Array.Empty<string>())
                .Concat(matter.IsMurder ? new[] { "murderer", "convicted_murderer" }
                    : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
            List<Hero> subjects = new List<Hero>();
            if (matter.Category == ReignNobleMatterCategory.Divorce)
                subjects.AddRange(matter.Participants.Take(2).Select(x => FindHero(x.HeroId)));
            else subjects.Add(FindHero(matter.IsMurder ? matter.ConvictedHeroId : matter.RuledAgainstHeroId));
            foreach (Hero subject in subjects.Where(x => x != null))
                foreach (string tag in tags)
                    ReignWorldHistoryCampaignBehavior.Instance?.RecordDynamicSocialReputation(
                        subject, tag, false, 0,
                        matter.MatterId + "|reversal|" + subject.StringId + "|" + tag,
                        "The ruler formally reversed the judgment supporting this reputation.",
                        "This negative court reputation was superseded by a formal reversal.",
                        new JObject { ["matterId"] = matter.MatterId, ["reversal"] = true });
        }

        public bool TryRequestCourtStay(string matterId, string heroId, out string receipt)
        {
            receipt = string.Empty;
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters.FirstOrDefault(x => x?.MatterId == matterId);
            Hero hero = FindHero(heroId);
            Settlement capital = FindCurrentCapital();
            if (matter == null || matter.IsPending || hero == null || capital == null
                || !matter.Participants.Any(x => x.HeroId == heroId) || hero.IsPrisoner)
            { receipt = "Only one free participant from a completed hearing may be asked to remain."; return false; }
            if (EnsureRulerDocketState().CourtStayLeases.Any(x => x.MatterId == matterId && !x.Released))
            { receipt = "A participant from this hearing is already staying at court."; return false; }
            if (!TryBringNobleToCapital(hero, capital, out string movementError))
            { receipt = movementError; return false; }
            EnsureRulerDocketState().CourtStayLeases.Add(new ReignCourtStayLease
            {
                LeaseId = "court_stay_" + Guid.NewGuid().ToString("N"), MatterId = matterId,
                HeroId = heroId, CapitalSettlementId = capital.StringId, StartDay = CurrentDay(), ReleaseDay = CurrentDay() + 1
            });
            receipt = hero.Name + " will remain at court until next morning.";
            StateChanged?.Invoke();
            return true;
        }

        private static bool TryBringNobleToCapital(Hero hero, Settlement capital,
            out string error)
        {
            error = string.Empty;
            try
            {
                if (hero.PartyBelongedTo != null)
                {
                    if (hero.PartyBelongedTo.CurrentSettlement != capital)
                        EnterSettlementAction.ApplyForParty(hero.PartyBelongedTo, capital);
                }
                else TeleportPartylessHero(hero, capital);
                bool arrived = hero.PartyBelongedTo?.CurrentSettlement == capital
                    || hero.CurrentSettlement == capital
                    || capital.HeroesWithoutParty.Contains(hero);
                if (!arrived)
                {
                    error = "The native movement action could not establish this noble at the capital.";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "The noble could not be brought to court: " + ex.Message;
                return false;
            }
        }

        private void ProcessCourtStayLeases(int day, int hour)
        {
            foreach (ReignCourtStayLease lease in EnsureRulerDocketState().CourtStayLeases
                         .Where(x => x != null && !x.Released).ToList())
            {
                if (day > lease.ReleaseDay || (day == lease.ReleaseDay && hour >= 8))
                {
                    lease.Released = true;
                    continue;
                }
                Hero hero = FindHero(lease.HeroId);
                Settlement capital = Settlement.Find(lease.CapitalSettlementId);
                if (hero != null && capital != null && !hero.IsPrisoner)
                    TryBringNobleToCapital(hero, capital, out _);
            }
        }

        public bool IsProtectedDocketCustody(Hero prisoner)
        {
            return prisoner != null && prisoner.IsPrisoner && EnsureRulerDocketState().NobleMatters.Any(x => x?.CustodyHold?.Active == true
                && !x.CustodyHold.Executed && !x.CustodyHold.ReleasedByReversal && x.CustodyHold.PrisonerHeroId == prisoner.StringId);
        }
    }
}
