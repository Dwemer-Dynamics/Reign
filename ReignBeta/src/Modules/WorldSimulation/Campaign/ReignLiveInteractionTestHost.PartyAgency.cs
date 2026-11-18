using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Events;
using ReignBeta.PartyAgency;
using ReignBeta.UI;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Dialogue.UI;
using ReignBeta.Diplomacy.Campaign;
using ReignBeta.Diplomacy.UI;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Reputation.Campaign;
using ReignBeta.WorldSimulation.Integration;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.Runtime;
using ReignBeta.WorldSimulation.UI;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static LiveCommandResult PartyAgencyTest(JObject command)
        {
            ReignTemporaryPartyGuestCampaignBehavior behavior = ReignServices.TemporaryPartyGuest;
            if (behavior == null)
                return LiveCommandResult.Failed("The temporary noble guest campaign behavior is unavailable.");
            JObject snapshot = behavior.BuildHarnessSnapshot();
            JObject patches = ReignTemporaryPartyGuestPatches.BuildDiagnostics();
            snapshot["patches"] = patches;
            string profile = command?.Value<string>("profile") ?? "contracts";
            string caseId = command?.Value<string>("caseId") ?? string.Empty;
            string variant = command?.Value<string>("variant") ?? string.Empty;
            string phase = command?.Value<string>("phase") ?? "observe";
            string targetSearch = command?.Value<string>("targetSearch") ?? string.Empty;
            string runId = command?.Value<string>("runId") ?? _activeRunId ?? string.Empty;
            snapshot["profile"] = profile;
            snapshot["caseId"] = caseId;
            snapshot["variant"] = variant;
            snapshot["observationPhase"] = phase;
            snapshot["naturalLanguageOnly"] = true;
            snapshot["directDialogueActionInvocation"] = false;
            snapshot["contractAssertions"] = new JObject
            {
                ["openEndedReviewIsThirtyDays"] = ReignTemporaryPartyGuestCampaignBehavior.OpenEndedReviewDays == 30f,
                ["fixedTermRangeIsOneToOneHundredTwentySixDays"] = ReignTemporaryPartyGuestCampaignBehavior.MinimumFixedTermDays == 1f
                    && ReignTemporaryPartyGuestCampaignBehavior.MaximumFixedTermDays == 126f,
                ["returnRangeIsOneToTwelveHours"] = ReignTemporaryPartyGuestCampaignBehavior.MinimumReturnHours == 1f
                    && ReignTemporaryPartyGuestCampaignBehavior.MaximumReturnHours == 12f,
                ["allLifecyclePhasesPresent"] = Enum.GetValues(typeof(ReignTemporaryGuestPhase)).Length == 7,
                ["campHooksActive"] = patches.Value<bool?>("active") == true
                    && (patches.Value<int?>("patchedHookCount") ?? 0) >= 10
            };
            if (profile == "preflight" || profile == "contracts")
                snapshot["candidateMatrix"] = BuildPartyAgencyCandidateMatrix(behavior);

            Hero target = ResolvePartyAgencyTarget(targetSearch);
            ReignTemporaryPartyGuestRecord targetRecord = behavior.Records.FirstOrDefault(record =>
                record != null && (string.Equals(record.HeroStringId, target?.StringId,
                    StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(targetSearch)
                        && string.Equals(record.HeroStringId, targetSearch,
                            StringComparison.OrdinalIgnoreCase))));
            JObject targetEvidence = BuildPartyAgencyTargetEvidence(behavior, target, targetRecord);
            snapshot["target"] = targetEvidence;

            string baselineKey = runId + "|" + caseId + "|" + variant;
            JObject nativeEvidence = PartyAgencyNativeEvidence.TryGetValue(
                baselineKey, out JObject capturedNativeEvidence)
                ? (JObject)capturedNativeEvidence.DeepClone() : null;
            if (nativeEvidence != null) snapshot["nativeEvidence"] = nativeEvidence;
            if (phase.Equals("before", StringComparison.OrdinalIgnoreCase))
                PartyAgencyBaselines[baselineKey] = (JObject)snapshot.DeepClone();
            JObject baseline = PartyAgencyBaselines.TryGetValue(baselineKey, out JObject captured)
                ? captured : null;
            JObject expected = command?["expected"] as JObject ?? new JObject();
            JObject caseAssertions = phase.Equals("before", StringComparison.OrdinalIgnoreCase)
                ? new JObject()
                : EvaluatePartyAgencyExpected(expected, snapshot, targetEvidence, baseline,
                    nativeEvidence);
            snapshot["caseAssertions"] = caseAssertions;

            bool ok = snapshot["contractAssertions"].Children<JProperty>()
                .All(property => property.Value.Value<bool>())
                && caseAssertions.Children<JProperty>().All(property => property.Value.Value<bool>());
            return ok
                ? LiveCommandResult.Completed("Temporary noble guest contracts and runtime state were captured.", snapshot)
                : LiveCommandResult.Failed("One or more temporary noble guest harness contracts failed.", snapshot);
        }

        private static JObject EvaluatePartyAgencyExpected(JObject expected, JObject snapshot,
            JObject target, JObject baseline, JObject nativeEvidence)
        {
            JObject assertions = new JObject();
            if (expected == null || !expected.Properties().Any()) return assertions;
            int recordCount = snapshot.Value<int?>("recordCount")
                ?? snapshot.Value<int?>("activeCount") ?? 0;
            int baselineCount = baseline?.Value<int?>("recordCount")
                ?? baseline?.Value<int?>("activeCount") ?? recordCount;
            if (expected.Property("recordDelta") != null)
                assertions["recordDelta"] = recordCount - baselineCount
                    == expected.Value<int>("recordDelta");
            string recordExpectation = expected.Value<string>("record") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(recordExpectation))
                assertions["recordPresence"] = recordExpectation.Equals("present", StringComparison.OrdinalIgnoreCase)
                    ? target.Value<bool?>("recordPresent") == true
                    : recordExpectation.Equals("absent", StringComparison.OrdinalIgnoreCase)
                        && target.Value<bool?>("recordPresent") != true;
            string expectedPhase = expected.Value<string>("phase") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedPhase))
                assertions["phase"] = string.Equals(target.Value<string>("phase"), expectedPhase,
                    StringComparison.OrdinalIgnoreCase);
            if (expected["phaseOneOf"] is JArray phases)
                assertions["phaseOneOf"] = phases.Values<string>().Any(value =>
                    string.Equals(value, target.Value<string>("phase"), StringComparison.OrdinalIgnoreCase));
            string expectedTermKind = expected.Value<string>("termKind") ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedTermKind))
                assertions["termKind"] = string.Equals(target.Value<string>("termKind"),
                    expectedTermKind, StringComparison.OrdinalIgnoreCase);
            if (expected.Property("fixedTermDays") != null)
                assertions["fixedTermDays"] = Math.Abs(
                    (target.Value<double?>("fixedTermDays") ?? double.NaN)
                    - expected.Value<double>("fixedTermDays")) < 0.01d;
            if (expected.Property("reviewIntervalDays") != null)
                assertions["reviewIntervalDays"] = Math.Abs(
                    (target.Value<double?>("reviewIntervalDays") ?? double.NaN)
                    - expected.Value<double>("reviewIntervalDays")) < 0.01d;
            foreach (string flag in new[]
            {
                "identityPreserved", "campProtected", "hostilityWarningAcknowledged",
                "temporarilyExcludedFromBattle", "banishedToPlayerClan", "nativeRecoveryReleased",
                "noDuplicateHeroOrParty", "stateEligible"
            })
                if (expected.Property(flag) != null)
                {
                    bool actual = target.Value<bool?>(flag) == true;
                    if (flag == "temporarilyExcludedFromBattle"
                        && nativeEvidence?.Value<bool?>(flag) == true)
                        actual = true;
                    assertions[flag] = actual == expected.Value<bool>(flag);
                }
            if (expected.Property("minimumReviewDueCount") != null)
                assertions["minimumReviewDueCount"] = snapshot.Value<int?>("reviewDueCount")
                    >= expected.Value<int>("minimumReviewDueCount");
            if (expected.Property("timePaused") != null)
            {
                bool paused = TaleWorlds.CampaignSystem.Campaign.Current?.TimeControlMode
                    == CampaignTimeControlMode.Stop;
                assertions["timePaused"] = paused == expected.Value<bool>("timePaused");
            }
            if (expected.Value<bool?>("externalEvidenceRequired") == true
                || expected.Value<bool?>("unrelatedStateUnchanged") == true
                || expected.Value<bool?>("allPriorRequiredInstancesPassed") == true)
            {
                assertions["nativeEvidenceReceipt"] = nativeEvidence?.Value<bool?>("ok") == true
                    && !string.IsNullOrWhiteSpace(nativeEvidence.Value<string>("receiptId"));
            }
            return assertions;
        }

        private static JObject BuildPartyAgencyTargetEvidence(
            ReignTemporaryPartyGuestCampaignBehavior behavior, Hero hero,
            ReignTemporaryPartyGuestRecord record)
        {
            MobileParty source = record == null ? null : ReignObjectResolver.FindParty(record.SourcePartyStringId);
            bool recordPresent = record != null;
            bool stateEligible = hero != null && hero != Hero.MainHero && hero.IsLord
                && hero.CompanionOf == null && ReignConversationEligibility.IsAdultLivingNpc(hero)
                && !hero.IsPrisoner && hero.Clan != null && hero.Clan != Clan.PlayerClan
                && hero.GovernorOf == null
                && hero.PartyBelongedTo?.IsCurrentlyUsedByAQuest != true
                && hero.PartyBelongedTo?.MapEvent == null
                && hero.PartyBelongedTo?.SiegeEvent == null
                && hero.PartyBelongedTo?.BesiegerCamp == null;
            int heroMatches = string.IsNullOrWhiteSpace(record?.HeroStringId) ? 0
                : Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                    .Count(candidate => candidate != null && string.Equals(candidate.StringId,
                        record.HeroStringId, StringComparison.OrdinalIgnoreCase));
            int partyMatches = string.IsNullOrWhiteSpace(record?.SourcePartyStringId) ? 0
                : MobileParty.All.Count(party => party != null && string.Equals(party.StringId,
                    record.SourcePartyStringId, StringComparison.OrdinalIgnoreCase));
            bool completed = record?.Phase == ReignTemporaryGuestPhase.Completed;
            return new JObject
            {
                ["heroId"] = hero?.StringId ?? record?.HeroStringId ?? string.Empty,
                ["heroName"] = hero?.Name?.ToString() ?? string.Empty,
                ["recordPresent"] = recordPresent,
                ["phase"] = record?.Phase.ToString() ?? string.Empty,
                ["termKind"] = record?.TermKind.ToString() ?? string.Empty,
                ["startedDay"] = record?.StartedDay ?? 0f,
                ["reviewDueDay"] = record?.ReviewDueDay ?? 0f,
                ["fixedTermDays"] = record?.FixedTermDays ?? 0f,
                ["reviewIntervalDays"] = recordPresent
                    ? record.ReviewDueDay - record.StartedDay : 0f,
                ["stateEligible"] = stateEligible,
                ["identityPreserved"] = !recordPresent || (hero != null
                    && string.Equals(record.OriginalClanStringId, hero.Clan?.StringId ?? string.Empty,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalKingdomStringId,
                        hero.Clan?.Kingdom?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.OriginalCompanionClanStringId,
                        hero.CompanionOf?.StringId ?? string.Empty, StringComparison.OrdinalIgnoreCase)),
                ["campProtected"] = !recordPresent || string.IsNullOrWhiteSpace(record.SourcePartyStringId)
                    || behavior.IsProtectedCamp(source),
                ["hostilityWarningAcknowledged"] = record?.HostilityWarningAcknowledged == true,
                ["temporarilyExcludedFromBattle"] = record?.TemporarilyExcludedFromBattle == true,
                ["banishedToPlayerClan"] = recordPresent && hero?.Clan == Clan.PlayerClan
                    && !string.Equals(record.OriginalClanStringId, Clan.PlayerClan?.StringId,
                        StringComparison.OrdinalIgnoreCase),
                ["nativeRecoveryReleased"] = completed
                    && (source == null || !behavior.IsProtectedCamp(source)),
                ["noDuplicateHeroOrParty"] = (!recordPresent || heroMatches == 1)
                    && (string.IsNullOrWhiteSpace(record?.SourcePartyStringId) || partyMatches <= 1),
                ["sourcePartyId"] = record?.SourcePartyStringId ?? string.Empty,
                ["sourcePartyExists"] = source != null,
                ["currentClanId"] = hero?.Clan?.StringId ?? string.Empty,
                ["currentKingdomId"] = hero?.Clan?.Kingdom?.StringId ?? string.Empty,
                ["inPlayerParty"] = hero?.PartyBelongedTo == MobileParty.MainParty,
                ["isDead"] = hero?.IsDead == true,
                ["isPrisoner"] = hero?.IsPrisoner == true
            };
        }

        private static JObject BuildPartyAgencyCandidateMatrix(
            ReignTemporaryPartyGuestCampaignBehavior behavior)
        {
            List<Hero> nobles = Hero.AllAliveHeroes.Where(hero => hero != null
                && hero != Hero.MainHero && hero.IsLord).ToList();
            Func<Hero, bool> eligible = hero => BuildPartyAgencyTargetEvidence(behavior, hero,
                behavior.Records.FirstOrDefault(record => record?.HeroStringId == hero.StringId))
                .Value<bool>("stateEligible");
            return new JObject
            {
                ["livingNobleCount"] = nobles.Count,
                ["stateEligibleCount"] = nobles.Count(eligible),
                ["partylessCount"] = nobles.Count(hero => eligible(hero) && hero.PartyBelongedTo == null),
                ["ordinaryPartyMemberCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo != null && hero.PartyBelongedTo.LeaderHero != hero),
                ["partyLeaderCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.LeaderHero == hero),
                ["armyMemberCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.Army != null
                    && hero.PartyBelongedTo.Army.LeaderParty != hero.PartyBelongedTo),
                ["armyLeaderCount"] = nobles.Count(hero => eligible(hero)
                    && hero.PartyBelongedTo?.Army?.LeaderParty == hero.PartyBelongedTo),
                ["clanLeaderCount"] = nobles.Count(hero => eligible(hero) && hero.Clan?.Leader == hero),
                ["kingdomRulerCount"] = nobles.Count(hero => eligible(hero)
                    && hero.Clan?.Kingdom?.Leader == hero),
                ["governorCount"] = nobles.Count(hero => hero.GovernorOf != null),
                ["prisonerCount"] = nobles.Count(hero => hero.IsPrisoner),
                ["questBoundCount"] = nobles.Count(hero => hero.PartyBelongedTo?.IsCurrentlyUsedByAQuest == true)
            };
        }

        private static Hero ResolvePartyAgencyTarget(string search)
        {
            if (string.Equals(search, "@party_agency_fixture", StringComparison.OrdinalIgnoreCase))
                return FindPartyAgencyFixtureHero();
            if (string.IsNullOrWhiteSpace(search)) return null;
            Hero exact = ReignObjectResolver.FindHero(search.Trim());
            if (exact != null) return exact;
            return RankHeroes(Hero.AllAliveHeroes.Where(hero => hero != null), search).FirstOrDefault();
        }

        private static string BuildPartyAgencyContextualPlayerResponse(
            string finalConsentPrompt, string npcReply)
        {
            string reply = npcReply ?? string.Empty;
            var clauses = new List<string>();
            Hero player = Hero.MainHero;
            string playerName = player?.Name?.ToString() ?? "Egan";
            string realmName = player?.Clan?.Kingdom?.Name?.ToString()
                ?? player?.Clan?.Name?.ToString() ?? "my realm";
            Settlement seat = Settlement.CurrentSettlement
                ?? player?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            string seatName = seat?.Name?.ToString() ?? "this settlement";

            if (PartyAgencyReplyContains(reply, "who are you", "your name", "what authority",
                    "authority backs", "whose authority", "identify yourself", "give me yours",
                    "give me your name", "hear a name", "hear your name", "name from your mouth",
                    "a name. that's"))
                clauses.Add("My name is " + playerName + ", sovereign of " + realmName
                    + " and lord of " + seatName
                    + "; this undertaking proceeds under my own authority.");
            if (PartyAgencyReplyContains(reply, "family", "husband", "wife", "children",
                    "household", "home while", "left exposed"))
                clauses.Add("While you are away, I will place a trusted " + seatName
                    + " garrison detail over your household and cover their provisions and safety.");
            if (PartyAgencyReplyContains(reply, "spoils", "share", "cut", "payment",
                    "reward", "coin"))
                clauses.Add("You will receive the fair share or payment you named, recorded before we depart.");
            if (PartyAgencyReplyContains(reply, "independent", "retainer", "subject",
                    "partner", "battlefield orders", "take orders", "my own command"))
                clauses.Add("You travel as an independent ally, not my retainer or subject, and we will discuss our course before acting.");
            if (PartyAgencyReplyContains(reply, "scout", "report", "route", "plan",
                    "where we", "what happens", "practical detail"))
                clauses.Add("You may inspect the scouts' reports, route, provisions, and security arrangements before we ride.");
            if (PartyAgencyReplyContains(reply, "written record", "document", "addendum",
                    "papers", "written proof", "in writing"))
                clauses.Add("I have prepared the written record, addendum, or other papers you requested, and I place them in your hands now for inspection before we continue.");
            clauses.Add("I accept every condition you have named and will hold myself to it.");
            clauses.Add(finalConsentPrompt);
            return string.Join(" ", clauses.Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        private static bool PartyAgencyReplyContains(string text, params string[] phrases)
        {
            return phrases.Any(phrase => (text ?? string.Empty).IndexOf(
                phrase, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
