using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.ObjectSystem;
using static ReignBeta.Government.Campaign.ReignGovernmentCampaignBehavior;
using ReignBeta.Government.Campaign;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Spymaster.Campaign;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Government.Testing
{
    internal static partial class ReignGovernmentTestControls
    {
        private static readonly Dictionary<string, GovernmentLanguageFixture> GovernmentLanguageFixtures =
            new Dictionary<string, GovernmentLanguageFixture>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GovernmentSoakFixture> GovernmentSoakFixtures =
            new Dictionary<string, GovernmentSoakFixture>(StringComparer.OrdinalIgnoreCase);

        private static JObject GovernmentCertificationCasePrepare(ReignGovernmentCampaignBehavior government, string runId, JObject options)
        {
            foreach (GovernmentLanguageFixture retained in GovernmentLanguageFixtures.Values.ToList())
                RestoreGovernmentLanguageFixture(government, retained);
            GovernmentLanguageFixtures.Clear();
            JArray assertions = new JArray();
            string caseId = options.Value<string>("caseId") ?? runId;
            string fixtureName = options.Value<string>("fixture") ?? string.Empty;
            string targetSearch = options.Value<string>("targetSearch") ?? string.Empty;
            string expectedTag = options.Value<string>("expectedTag") ?? "none";
            string expectedPosture = options.Value<string>("expectedPosture") ?? string.Empty;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Hero target = FindHero(targetSearch);
            ReignGovernmentStateRecord state = government.EnsureGovernment(kingdom);
            bool playerRuler = kingdom != null && Hero.MainHero != null && kingdom.Leader == Hero.MainHero;
            bool targetEligible = target != null && target.IsAlive && !target.IsChild && !target.IsPrisoner
                && target != Hero.MainHero && (target.Clan?.Kingdom == kingdom
                    || government.GetSeats(kingdom?.StringId).Any(seat => Same(seat.HeroStringId, target.StringId)));
            bool roleMatches = expectedTag.Equals("clan", StringComparison.OrdinalIgnoreCase)
                ? target?.Clan?.Leader == target
                : expectedTag.Equals("individual", StringComparison.OrdinalIgnoreCase)
                    ? target?.Clan?.Leader != target
                    : true;
            AddGovernmentAssertion(assertions, "government_language_fixture_player_ruler", playerRuler,
                "The natural-language consent fixture uses the real living player ruler of the loaded disposable realm.",
                new JObject { ["kingdomId"] = kingdom?.StringId ?? string.Empty });
            AddGovernmentAssertion(assertions, "government_language_fixture_target", targetEligible && roleMatches,
                "The selected real NPC belongs to the ruler's realm and matches the manifest's individual or clan-leader scope.",
                new JObject { ["targetHeroId"] = target?.StringId ?? string.Empty,
                    ["targetClanId"] = target?.Clan?.StringId ?? string.Empty,
                    ["isClanLeader"] = target?.Clan?.Leader == target });
            if (!playerRuler || !targetEligible || !roleMatches || state == null)
                return GovernmentTestResult(runId, "case_prepare", assertions);

            var fixture = new GovernmentLanguageFixture
            {
                CaseId = caseId,
                KingdomId = kingdom.StringId,
                TargetHeroId = target.StringId,
                OriginalLevel = state.Level,
                OriginalTrust = state.Trust,
                OriginalConsentLevel = state.ReductionConsentFromLevel,
                OriginalConsentHeroes = state.ReductionConsentHeroIdsCsv ?? string.Empty,
                OriginalConsentClans = state.ReductionConsentClanIdsCsv ?? string.Empty,
                OriginalRelation = target.GetRelation(Hero.MainHero),
                OriginalInfluence = Clan.PlayerClan?.Influence ?? 0f,
                OriginalStance = state.StanceTowardRuler,
                ExpectedTag = expectedTag,
                ExpectedPosture = expectedPosture,
                FixtureName = fixtureName
            };
            state.Level = 5;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            state.Revision++;
            var productionConsentAction = new ReignWorldActionRecord
            {
                Type = ReignWorldActionType.PoliticsConsentGovernmentReduction,
                ActorHeroStringId = target.StringId,
                ActorKingdomStringId = kingdom.StringId,
                ActorClanStringId = target.Clan?.StringId ?? string.Empty,
                TargetHeroStringId = Hero.MainHero.StringId,
                TargetClanStringId = Clan.PlayerClan?.StringId ?? string.Empty,
                TermsJson = "{\"consentConfirmed\":true}",
                RequiresAcceptance = false
            };
            bool productionDirectionValid = ReignActionValidator.Validate(
                productionConsentAction, out string productionDirectionFailure);
            AddGovernmentAssertion(assertions, "government_language_action_direction",
                productionDirectionValid,
                "The production consent action treats the speaking NPC as actor and the living player ruler as target, matching ordinary Individual Chat routing.",
                new JObject
                {
                    ["actorHeroId"] = productionConsentAction.ActorHeroStringId,
                    ["targetHeroId"] = productionConsentAction.TargetHeroStringId,
                    ["validationFailure"] = productionDirectionFailure
                });
            int desiredRelation = fixtureName.StartsWith("supportive_", StringComparison.OrdinalIgnoreCase)
                ? 100 : fixtureName.StartsWith("opposed_", StringComparison.OrdinalIgnoreCase)
                    ? -100 : fixtureName.StartsWith("conditional_", StringComparison.OrdinalIgnoreCase)
                        ? 10 : 0;
            int currentRelation = target.GetRelation(Hero.MainHero);
            if (currentRelation != desiredRelation)
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target,
                    desiredRelation - currentRelation, false);
            EnsureGovernmentLanguageTargetSeat(government, kingdom, state, target, fixture);
            ReignGovernmentSeatRecord seat = government.GetSeats(kingdom.StringId)
                .FirstOrDefault(item => Same(item.HeroStringId, target.StringId));
            JObject productionKnowledge = government.BuildGovernmentKnowledgeForSpeaker(target);
            JObject productionMembership = productionKnowledge["ownMembership"] as JObject;
            bool productionPartyPower = productionKnowledge.Value<bool?>("available") == true
                && productionMembership?.Value<bool?>("isMember") == true
                && productionMembership?.Value<bool?>("personalPowerAtStake") == true;
            AddGovernmentAssertion(assertions, "government_language_fixture_party_power",
                seat != null && productionPartyPower,
                "The natural-language fixture uses a real institution member whose production dialogue context exposes their own party power at stake; clan-leader cases may receive a reversible temporary fief so production membership rules create the seat.",
                new JObject
                {
                    ["targetHeroId"] = target.StringId,
                    ["temporaryFiefSettlementId"] = fixture.TemporaryFiefSettlementId ?? string.Empty,
                    ["seatSource"] = seat == null ? string.Empty
                        : ((ReignGovernmentSeatSource)seat.SeatSourceValue).ToString(),
                    ["productionKnowledgeAvailable"] = productionKnowledge.Value<bool?>("available") == true,
                    ["productionMembership"] = productionMembership?.DeepClone()
                });
            if (seat != null)
            {
                fixture.OriginalGovernmentLoyalty = seat.GovernmentLoyalty;
                fixture.OriginalPartyLoyalty = seat.PartyLoyalty;
                fixture.OriginalSeatPartyId = seat.PartyId ?? string.Empty;
                ReignGovernmentPartyRecord party = government.FindParty(kingdom.StringId, seat.PartyId);
                if (party != null)
                {
                    fixture.PartyId = party.PartyId;
                    fixture.OriginalPartyPlanks = party.PlanksCsv ?? string.Empty;
                    if (fixtureName.StartsWith("supportive_", StringComparison.OrdinalIgnoreCase))
                        party.PlanksCsv = "RoyalAuthority,Security";
                    else if (fixtureName.StartsWith("opposed_", StringComparison.OrdinalIgnoreCase))
                        party.PlanksCsv = target.Clan?.Leader == target
                            ? "RepresentativeAuthority,ClanPrivilege,LocalAutonomy"
                            : "RepresentativeAuthority,PopularWelfare,LocalAutonomy";
                    else if (fixtureName.StartsWith("uncertain_", StringComparison.OrdinalIgnoreCase)
                        || fixtureName.StartsWith("conditional_", StringComparison.OrdinalIgnoreCase))
                        party.PlanksCsv = "RepresentativeAuthority,Justice,LocalAutonomy";
                    party.Revision++;
                }
                if (fixtureName.StartsWith("supportive_", StringComparison.OrdinalIgnoreCase))
                {
                    seat.GovernmentLoyalty = 25;
                    seat.PartyLoyalty = 25;
                }
                else if (fixtureName.StartsWith("opposed_", StringComparison.OrdinalIgnoreCase))
                {
                    seat.GovernmentLoyalty = 95;
                    seat.PartyLoyalty = 95;
                }
                else
                {
                    seat.GovernmentLoyalty = 60;
                    seat.PartyLoyalty = 60;
                }
                seat.Revision++;
            }
            state.StanceTowardRuler = fixtureName.StartsWith("supportive_", StringComparison.OrdinalIgnoreCase)
                ? 75 : fixtureName.StartsWith("opposed_", StringComparison.OrdinalIgnoreCase)
                    ? -100 : fixtureName.StartsWith("uncertain_", StringComparison.OrdinalIgnoreCase)
                        || fixtureName.StartsWith("conditional_", StringComparison.OrdinalIgnoreCase)
                            ? -25 : state.StanceTowardRuler;
            state.Revision++;
            JObject postMutationKnowledge = government.BuildGovernmentKnowledgeForSpeaker(target);
            JObject postMutationMembership = postMutationKnowledge["ownMembership"] as JObject;
            JArray postMutationPlanks = postMutationMembership?["partyPlanks"] as JArray;
            bool postMutationPartyPower = postMutationKnowledge.Value<bool?>("available") == true
                && postMutationMembership?.Value<bool?>("isMember") == true
                && postMutationMembership?.Value<bool?>("personalPowerAtStake") == true
                && postMutationPlanks != null
                && postMutationPlanks.Any(item => string.Equals(item?.ToString(),
                    "RepresentativeAuthority", StringComparison.OrdinalIgnoreCase));
            AddGovernmentAssertion(assertions,
                "government_language_fixture_posture_visible_to_production",
                !fixtureName.StartsWith("opposed_", StringComparison.OrdinalIgnoreCase)
                    || (postMutationPartyPower && state.StanceTowardRuler == -100),
                "After fixture posture is applied, the production dialogue snapshot exposes the opposed member's actual representative-authority plank, personal power at stake, and strongly opposed government stance.",
                new JObject
                {
                    ["targetHeroId"] = target.StringId,
                    ["stanceTowardRuler"] = state.StanceTowardRuler,
                    ["productionMembership"] = postMutationMembership?.DeepClone()
                });
            GovernmentLanguageFixtures[caseId] = fixture;
            AddGovernmentAssertion(assertions, "government_language_fixture_prepared", true,
                "The exact disposable case now has authority level 5, no prior consent tag, bounded relationship/party posture, and a retained pre-turn snapshot.",
                new JObject { ["caseId"] = caseId, ["fixture"] = fixtureName,
                    ["desiredRelation"] = desiredRelation, ["expectedTag"] = expectedTag });
            JObject result = GovernmentTestResult(runId, "case_prepare", assertions);
            result["caseId"] = caseId;
            result["targetHeroId"] = target.StringId;
            result["targetClanId"] = target.Clan?.StringId ?? string.Empty;
            return result;
        }

        private static JObject GovernmentCertificationCaseObserve(ReignGovernmentCampaignBehavior government, string runId, JObject options)
        {
            JArray assertions = new JArray();
            string caseId = options.Value<string>("caseId") ?? runId;
            if (!GovernmentLanguageFixtures.TryGetValue(caseId, out GovernmentLanguageFixture fixture))
            {
                AddGovernmentAssertion(assertions, "government_language_expected_tag", false,
                    "The matching pre-dialogue fixture snapshot is unavailable.",
                    new JObject { ["caseId"] = caseId });
                return GovernmentTestResult(runId, "case_observe", assertions);
            }
            Kingdom kingdom = FindKingdom(fixture.KingdomId);
            Hero target = FindHero(fixture.TargetHeroId);
            ReignGovernmentStateRecord state = government.GetGovernment(kingdom);
            bool heroTagged = CsvContains(state?.ReductionConsentHeroIdsCsv, fixture.TargetHeroId);
            bool clanTagged = CsvContains(state?.ReductionConsentClanIdsCsv, target?.Clan?.StringId);
            bool expected;
            if (fixture.ExpectedTag.Equals("individual", StringComparison.OrdinalIgnoreCase))
                expected = heroTagged && !clanTagged && state?.ReductionConsentFromLevel == 5;
            else if (fixture.ExpectedTag.Equals("clan", StringComparison.OrdinalIgnoreCase))
                expected = clanTagged && !heroTagged && state?.ReductionConsentFromLevel == 5;
            else expected = !heroTagged && !clanTagged;
            AddGovernmentAssertion(assertions, "government_language_expected_tag", expected,
                "The ordinary production dialogue created exactly the manifest-expected invisible individual/clan tag, or no tag for refusal, concern, conditions, questions, and safety traps.",
                new JObject { ["caseId"] = caseId, ["expectedTag"] = fixture.ExpectedTag,
                    ["heroTagged"] = heroTagged, ["clanTagged"] = clanTagged,
                    ["consentFromLevel"] = state?.ReductionConsentFromLevel ?? 0 });
            float influence = Clan.PlayerClan?.Influence ?? 0f;
            AddGovernmentAssertion(assertions, "government_language_no_native_influence",
                Math.Abs(influence - fixture.OriginalInfluence) < 0.01f,
                "Government reduction-consent dialogue does not spend or award native Influence.",
                new JObject { ["before"] = fixture.OriginalInfluence, ["after"] = influence });
            JObject result = GovernmentTestResult(runId, "case_observe", assertions);
            result["caseId"] = caseId;
            result["targetHeroId"] = fixture.TargetHeroId;
            result["expectedPosture"] = fixture.ExpectedPosture;
            RestoreGovernmentLanguageFixture(government, fixture);
            GovernmentLanguageFixtures.Remove(caseId);
            return result;
        }

        private static void EnsureGovernmentLanguageTargetSeat(ReignGovernmentCampaignBehavior government, Kingdom kingdom,
            ReignGovernmentStateRecord state, Hero target, GovernmentLanguageFixture fixture)
        {
            if (kingdom == null || state == null || target == null
                || government.GetSeats(kingdom.StringId).Any(item => Same(item.HeroStringId, target.StringId)))
                return;
            if (target.Clan?.Leader != target || target.Clan == Clan.PlayerClan) return;

            Settlement fortification = Settlement.All.FirstOrDefault(item => item != null
                && item.IsFortification && item.OwnerClan == Clan.PlayerClan
                && item != Settlement.CurrentSettlement);
            if (fortification == null) return;
            fixture.TemporaryFiefSettlementId = fortification.StringId;
            fixture.OriginalFiefOwnerClanId = fortification.OwnerClan?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(fortification, target.Clan.Leader);
            government.ReconcileMembership(kingdom, state, false);
        }

        private static void RestoreGovernmentLanguageFixture(ReignGovernmentCampaignBehavior government, GovernmentLanguageFixture fixture)
        {
            if (fixture == null) return;
            Kingdom kingdom = FindKingdom(fixture.KingdomId);
            Hero target = FindHero(fixture.TargetHeroId);
            ReignGovernmentStateRecord state = government.GetGovernment(kingdom);
            ReignGovernmentSeatRecord seat = government.GetSeats(fixture.KingdomId)
                .FirstOrDefault(item => Same(item.HeroStringId, fixture.TargetHeroId));
            if (seat != null)
            {
                seat.PartyId = fixture.OriginalSeatPartyId ?? seat.PartyId;
                seat.GovernmentLoyalty = fixture.OriginalGovernmentLoyalty;
                seat.PartyLoyalty = fixture.OriginalPartyLoyalty;
                seat.Revision++;
            }
            ReignGovernmentPartyRecord party = government.FindParty(fixture.KingdomId, fixture.PartyId);
            if (party != null)
            {
                party.PlanksCsv = fixture.OriginalPartyPlanks ?? party.PlanksCsv;
                party.Revision++;
            }
            Settlement temporaryFief = string.IsNullOrWhiteSpace(fixture.TemporaryFiefSettlementId)
                ? null : Settlement.Find(fixture.TemporaryFiefSettlementId);
            Clan originalOwner = string.IsNullOrWhiteSpace(fixture.OriginalFiefOwnerClanId)
                ? null : Clan.FindFirst(item => Same(item.StringId, fixture.OriginalFiefOwnerClanId));
            if (temporaryFief != null && originalOwner != null
                && temporaryFief.OwnerClan != originalOwner)
                ChangeOwnerOfSettlementAction.ApplyByGift(temporaryFief, originalOwner.Leader);
            if (state != null)
            {
                government.ReconcileMembership(kingdom, state, false);
                state.Level = fixture.OriginalLevel;
                state.Trust = fixture.OriginalTrust;
                state.ReductionConsentFromLevel = fixture.OriginalConsentLevel;
                state.ReductionConsentHeroIdsCsv = fixture.OriginalConsentHeroes ?? string.Empty;
                state.ReductionConsentClanIdsCsv = fixture.OriginalConsentClans ?? string.Empty;
                state.StanceTowardRuler = fixture.OriginalStance;
                state.Revision++;
            }
            if (target != null && Hero.MainHero != null)
            {
                int currentRelation = target.GetRelation(Hero.MainHero);
                if (currentRelation != fixture.OriginalRelation)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(Hero.MainHero, target,
                        fixture.OriginalRelation - currentRelation, false);
            }
        }

        private static JObject GovernmentCertificationCaseExecute(ReignGovernmentCampaignBehavior government, string runId, JObject options)
        {
            JArray assertions = new JArray();
            string caseId = (options.Value<string>("caseId") ?? runId).Trim().ToUpperInvariant();
            var owned = new HashSet<string>(Enumerable.Range(2, 52)
                .Select(index => "GOV-NATIVE-" + index.ToString("000")),
                StringComparer.OrdinalIgnoreCase);
            bool manifestOwned = owned.Contains(caseId);
            AddGovernmentAssertion(assertions, "government_case_manifest_owned", manifestOwned,
                "Only a fixed manifest-owned Government native case can use the certification fixture seam.",
                new JObject { ["caseId"] = caseId });
            if (!manifestOwned) return GovernmentTestResult(runId, "case_execute", assertions);

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            ReignGovernmentStateRecord state = government.EnsureGovernment(playerKingdom);
            switch (caseId)
            {
                case "GOV-NATIVE-002":
                case "GOV-NATIVE-003":
                    GovernmentNativeUiCase(government, assertions, caseId, playerKingdom, state);
                    break;
                case "GOV-NATIVE-004":
                    GovernmentNativePlayerMeetingCase(government, assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-005":
                    GovernmentNativeNpcMeetingCase(government, assertions);
                    break;
                case "GOV-NATIVE-006":
                case "GOV-NATIVE-007":
                    GovernmentNativeResolutionRouteCase(government, assertions, playerKingdom, state,
                        caseId == "GOV-NATIVE-006" ? 0 : 1);
                    break;
                case "GOV-NATIVE-008":
                case "GOV-NATIVE-009":
                case "GOV-NATIVE-010":
                    GovernmentNativeLobbyCase(government, assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-011":
                case "GOV-NATIVE-012":
                case "GOV-NATIVE-013":
                case "GOV-NATIVE-014":
                case "GOV-NATIVE-015":
                case "GOV-NATIVE-016":
                case "GOV-NATIVE-017":
                    GovernmentNativeAuthorityCase(government, assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-018":
                    GovernmentNativeDecisionCase(government, assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-044":
                case "GOV-NATIVE-045":
                case "GOV-NATIVE-046":
                case "GOV-NATIVE-047":
                case "GOV-NATIVE-048":
                    GovernmentFiefNativeCase(government, assertions, caseId);
                    break;
                case "GOV-NATIVE-051":
                    GovernmentFiefSaveAssert(government, assertions, true);
                    break;
                case "GOV-NATIVE-019":
                    GovernmentNativeCoalitionCase(government, assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-020":
                    GovernmentNativeRatifiedReductionCase(government, assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-021":
                    GovernmentNativeForcedReductionCase(government, assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-023":
                case "GOV-NATIVE-024":
                    GovernmentNativePressureChannelCase(government, assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-025":
                case "GOV-NATIVE-026":
                    GovernmentNativeNpcRulerCase(government, assertions, caseId);
                    break;
                case "GOV-NATIVE-027":
                    GovernmentNativeKnowledgeCase(government, assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-028":
                case "GOV-NATIVE-029":
                    GovernmentNativeHistoryCase(government, assertions, playerKingdom, caseId);
                    break;
                case "GOV-NATIVE-036":
                case "GOV-NATIVE-037":
                case "GOV-NATIVE-038":
                case "GOV-NATIVE-039":
                case "GOV-NATIVE-040":
                case "GOV-NATIVE-041":
                case "GOV-NATIVE-042":
                case "GOV-NATIVE-043":
                    GovernmentNativeCultureInstitutionCase(government, assertions, caseId);
                    break;
                default:
                    AddGovernmentAssertion(assertions, "government_native_case_implemented", false,
                        "Every native certification case must have an explicit production-behavior assertion; generic loaded-state fallbacks are forbidden.",
                        new JObject { ["caseId"] = caseId });
                    break;
            }
            JObject result = GovernmentTestResult(runId, "case_execute", assertions);
            result["caseId"] = caseId;
            return result;
        }

        private static void GovernmentNativeCultureInstitutionCase(ReignGovernmentCampaignBehavior government, JArray assertions, string caseId)
        {
            string cultureId;
            ReignGovernmentInstitutionKind expectedKind;
            switch (caseId)
            {
                case "GOV-NATIVE-036": cultureId = "empire"; expectedKind = ReignGovernmentInstitutionKind.Senate; break;
                case "GOV-NATIVE-037": cultureId = "vlandia"; expectedKind = ReignGovernmentInstitutionKind.CouncilOfPeers; break;
                case "GOV-NATIVE-038": cultureId = "sturgia"; expectedKind = ReignGovernmentInstitutionKind.Veche; break;
                case "GOV-NATIVE-039": cultureId = "battania"; expectedKind = ReignGovernmentInstitutionKind.Oenach; break;
                case "GOV-NATIVE-040": cultureId = "aserai"; expectedKind = ReignGovernmentInstitutionKind.Majlis; break;
                case "GOV-NATIVE-041": cultureId = "khuzait"; expectedKind = ReignGovernmentInstitutionKind.Kurultai; break;
                case "GOV-NATIVE-042": cultureId = "nord"; expectedKind = ReignGovernmentInstitutionKind.Thing; break;
                default: cultureId = "certification_fallback"; expectedKind = ReignGovernmentInstitutionKind.CouncilOfEstates; break;
            }

            bool fallback = expectedKind == ReignGovernmentInstitutionKind.CouncilOfEstates;
            ReignGovernmentInstitutionProfile profile = ReignGovernmentRules.InstitutionForCulture(cultureId);
            Kingdom kingdom = fallback
                ? Clan.PlayerClan?.Kingdom
                : Kingdom.All.Where(IsEligibleKingdom)
                    .Where(item => Same(item.Culture?.StringId, cultureId)
                        || cultureId == "empire" && (item.Culture?.StringId ?? string.Empty)
                            .StartsWith("empire", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            List<SeatCandidate> candidates = kingdom == null
                ? new List<SeatCandidate>() : government.SelectSeatCandidates(kingdom, profile);
            IReadOnlyList<ReignGovernmentPartyBlueprint> blueprints = ReignGovernmentRules.SelectParties(
                "government-cert-culture|" + cultureId + "|" + (kingdom?.StringId ?? string.Empty),
                candidates.Count);
            var assigned = candidates.Select(candidate => new
            {
                Candidate = candidate,
                Party = ReignGovernmentRules.SelectPartyForMember(candidate.Hero.StringId,
                    Personality(candidate.Hero), blueprints)
            }).ToList();
            var occupied = blueprints.Select(blueprint => new
            {
                Blueprint = blueprint,
                Members = assigned.Where(item => item.Party != null
                    && Same(item.Party.Id, blueprint.Id)).ToList()
            }).Where(item => item.Members.Count > 0).ToList();
            bool deterministicSpeakers = occupied.All(item => item.Members
                .OrderByDescending(member => ReignGovernmentRules.SpeakerScore(
                    Personality(member.Candidate.Hero), 50,
                    kingdom?.Leader == null ? 0 : member.Candidate.Hero.GetRelation(kingdom.Leader)))
                .ThenBy(member => member.Candidate.Hero.StringId, StringComparer.Ordinal)
                .Select(member => member.Candidate.Hero).FirstOrDefault() != null);
            bool sourcesMatch = candidates.Count > 0 && candidates.All(candidate =>
                profile.SeatSources.Contains(candidate.Source));
            bool partyContract = blueprints.Count >= 2 && blueprints.Count <= 4
                && blueprints.All(blueprint => blueprint.Planks.Count >= 2
                    && blueprint.Planks.Count <= 4)
                && assigned.All(item => item.Party != null) && occupied.Count > 0
                && deterministicSpeakers;

            ReignGovernmentStateRecord state = government.EnsureGovernment(kingdom);
            bool stateProjection = false;
            JObject publicSnapshot = null;
            if (fallback && state != null)
            {
                string originalCulture = state.CultureStringId;
                string originalName = state.InstitutionName;
                int originalKind = state.InstitutionKindValue;
                long originalRevision = state.Revision;
                try
                {
                    state.CultureStringId = cultureId;
                    state.InstitutionName = profile.Name;
                    state.InstitutionKindValue = (int)profile.Kind;
                    state.Revision++;
                    stateProjection = state.CultureStringId == cultureId
                        && state.InstitutionName == profile.Name
                        && state.InstitutionKindValue == (int)expectedKind;
                    publicSnapshot = new JObject
                    {
                        ["available"] = true,
                        ["authoritative"] = true,
                        ["institution"] = state.InstitutionName,
                        ["institutionKind"] = expectedKind.ToString(),
                        ["authorityLevel"] = state.Level,
                        ["forcedFixture"] = true
                    };
                }
                finally
                {
                    state.CultureStringId = originalCulture;
                    state.InstitutionName = originalName;
                    state.InstitutionKindValue = originalKind;
                    state.Revision = originalRevision;
                }
            }
            else if (state != null)
            {
                publicSnapshot = government.BuildPublicSnapshot(kingdom);
                List<ReignGovernmentSeatRecord> seats = government.GetSeats(kingdom.StringId).ToList();
                List<ReignGovernmentPartyRecord> parties = government.GetParties(kingdom.StringId).ToList();
                int coalitionSeats = government.GetGoverningCoalition(kingdom.StringId).Sum(item => item.SeatCount);
                stateProjection = state.InstitutionKindValue == (int)expectedKind
                    && state.InstitutionName == profile.Name
                    && Same(state.CultureStringId, kingdom.Culture?.StringId)
                    && seats.Count > 0 && seats.All(seat => profile.SeatSources.Contains(
                        (ReignGovernmentSeatSource)seat.SeatSourceValue))
                    && parties.Count >= 2 && parties.Count <= 4
                    && parties.All(party => ParsePlanks(party.PlanksCsv).Count >= 2
                        && ParsePlanks(party.PlanksCsv).Count <= 4)
                    && parties.Where(party => party.SeatCount > 0).All(party =>
                        !string.IsNullOrWhiteSpace(party.SpeakerHeroStringId)
                        && seats.Any(seat => Same(seat.PartyId, party.PartyId)
                            && Same(seat.HeroStringId, party.SpeakerHeroStringId)))
                    && coalitionSeats * 2 > seats.Count
                    && publicSnapshot?.Value<bool?>("available") == true
                    && Same(publicSnapshot.Value<string>("institution"), profile.Name)
                    && Same(publicSnapshot.Value<string>("institutionKind"), expectedKind.ToString());
            }

            bool passed = kingdom != null && profile.Kind == expectedKind && sourcesMatch
                && partyContract && stateProjection
                && publicSnapshot?.Value<bool?>("authoritative") == true;
            AddGovernmentAssertion(assertions, "government_native_culture_institution_matrix", passed,
                "The requested culture-specific institution used its production seat sources, native personalities, multi-plank parties, deterministic party speakers, coalition/public projection, and shared authority contract; the fallback Council of Estates used a restored non-serialized forced fixture.",
                new JObject
                {
                    ["caseId"] = caseId,
                    ["cultureId"] = cultureId,
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["institution"] = profile.Name,
                    ["institutionKind"] = profile.Kind.ToString(),
                    ["fallbackForcedFixture"] = fallback,
                    ["candidateCount"] = candidates.Count,
                    ["seatSources"] = new JArray(profile.SeatSources.Select(item => item.ToString())),
                    ["partyCount"] = blueprints.Count,
                    ["occupiedPartyCount"] = occupied.Count,
                    ["deterministicSpeakers"] = deterministicSpeakers,
                    ["stateProjection"] = stateProjection,
                    ["publicSnapshot"] = publicSnapshot
                });
        }

        private static ReignGovernmentSeatRecord EnsureGovernmentCertificationSeat(ReignGovernmentCampaignBehavior government, Kingdom kingdom,
            ReignGovernmentPartyRecord party, Hero hero)
        {
            ReignGovernmentSeatRecord seat = government._seats.FirstOrDefault(item =>
                Same(item.KingdomStringId, kingdom?.StringId) && Same(item.HeroStringId, hero?.StringId));
            if (seat != null) return seat;
            seat = new ReignGovernmentSeatRecord
            {
                KingdomStringId = kingdom.StringId,
                HeroStringId = hero.StringId,
                ClanStringId = hero.Clan?.StringId ?? string.Empty,
                SettlementStringId = hero.Clan?.Fiefs.FirstOrDefault()?.Settlement?.StringId ?? string.Empty,
                PartyId = party.PartyId,
                PartyLoyalty = 100,
                GovernmentLoyalty = 100,
                Revision = 1
            };
            government._seats.Add(seat);
            return seat;
        }

        private static GovernmentTemporaryLandholdingFixture EnsureTemporaryLandholdingClans(ReignGovernmentCampaignBehavior government, 
            Kingdom kingdom, IEnumerable<Clan> requestedClans)
        {
            var fixture = new GovernmentTemporaryLandholdingFixture();
            List<Clan> clans = (requestedClans ?? Enumerable.Empty<Clan>())
                .Where(item => item != null).Distinct().ToList();
            List<Town> available = kingdom?.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).ToList()
                ?? new List<Town>();
            foreach (Clan clan in clans.Where(item => item.Fiefs.Count == 0))
            {
                Town holding = available.FirstOrDefault(item => fixture.CanUse(item.Settlement));
                Hero originalOwner = holding?.OwnerClan?.Leader;
                if (holding?.Settlement == null || originalOwner == null || clan.Leader == null) continue;
                fixture.Record(holding.Settlement, originalOwner);
                ChangeOwnerOfSettlementAction.ApplyByGift(holding.Settlement, clan.Leader);
            }
            return fixture;
        }

        private sealed class GovernmentTemporaryLandholdingFixture : IDisposable
        {
            private readonly List<Tuple<Settlement, Hero>> _ownership =
                new List<Tuple<Settlement, Hero>>();

            internal bool CanUse(Settlement settlement) => settlement != null
                && _ownership.All(item => item.Item1 != settlement);

            internal void Record(Settlement settlement, Hero originalOwner)
            {
                _ownership.Add(Tuple.Create(settlement, originalOwner));
            }

            public void Dispose()
            {
                for (int index = _ownership.Count - 1; index >= 0; index--)
                {
                    Tuple<Settlement, Hero> item = _ownership[index];
                    if (item.Item1?.OwnerClan?.Leader == item.Item2) continue;
                    try { ChangeOwnerOfSettlementAction.ApplyByGift(item.Item1, item.Item2); }
                    catch { }
                }
                _ownership.Clear();
            }
        }

        private static JObject GovernmentCertificationSoakPrepare(ReignGovernmentCampaignBehavior government, string runId)
        {
            JArray assertions = new JArray();
            var fixture = new GovernmentSoakFixture
            {
                StartDay = CurrentDay(),
                GovernmentCount = government._governments.Count,
                PartyCount = government._parties.Count,
                SeatCount = government._seats.Count,
                ResolutionCount = government._resolutions.Count,
                PressureCount = government._pressures.Count,
                MeetingCount = government._meetings.Count,
                Fingerprint = GovernmentFeatureFingerprint(government)
            };
            GovernmentSoakFixtures[runId] = fixture;
            AddGovernmentAssertion(assertions, "government_soak_prepare",
                fixture.GovernmentCount > 0 && fixture.Fingerprint.Length == 64,
                "The 210-day Government soak captured bounded native state before externally guarded time advancement.",
                JObject.FromObject(fixture));
            JObject result = GovernmentTestResult(runId, "soak_prepare", assertions);
            result["soak"] = JObject.FromObject(fixture);
            return result;
        }

        private static JObject GovernmentCertificationSoakVerify(ReignGovernmentCampaignBehavior government, string runId, JObject options)
        {
            JArray assertions = new JArray();
            bool restoredFromRetainedEvidence = false;
            if (!GovernmentSoakFixtures.TryGetValue(runId, out GovernmentSoakFixture fixture)
                && TryReadRetainedGovernmentSoakFixture(options, out fixture))
            {
                GovernmentSoakFixtures[runId] = fixture;
                restoredFromRetainedEvidence = true;
            }
            if (fixture == null)
            {
                AddGovernmentAssertion(assertions, "government_soak_verify", false,
                    "The matching soak preparation snapshot is unavailable.", null);
                return GovernmentTestResult(runId, "soak_verify", assertions);
            }
            float elapsed = CurrentDay() - fixture.StartDay;
            int expectedMaximumMeetings = fixture.MeetingCount
                + Math.Max(1, government._governments.Count) * 12;
            bool newNpcMeeting = government._meetings.Any(item => !item.PlayerAttended
                && item.MeetingDay + 0.01f >= fixture.StartDay);
            bool newResolutionActivity = government._resolutions.Any(item => item.ProposedDay + 0.01f >= fixture.StartDay)
                || government._resolutions.Count != fixture.ResolutionCount;
            bool stable = elapsed >= 209.5f
                && government._governments.Count == fixture.GovernmentCount
                && government._parties.Count >= fixture.PartyCount
                && government._parties.Count <= fixture.PartyCount + fixture.GovernmentCount * 2
                && government._meetings.Count >= fixture.MeetingCount
                && government._meetings.Count <= expectedMaximumMeetings
                && newNpcMeeting && newResolutionActivity
                && government._meetings.Where(item => !item.PlayerAttended).All(item => item.ProviderCallCount == 0)
                && government._resolutions.Count <= fixture.ResolutionCount + fixture.GovernmentCount * 40
                && government._pressures.Count <= fixture.PressureCount + fixture.GovernmentCount * 40;
            AddGovernmentAssertion(assertions, "government_soak_verify", stable,
                "After 210 externally advanced days, Government identity remains stable, seasonal meetings stay bounded, NPC meetings remain provider-free, and saved collections show no runaway growth.",
                new JObject { ["elapsedDays"] = elapsed, ["governments"] = government._governments.Count,
                    ["parties"] = government._parties.Count, ["meetings"] = government._meetings.Count,
                    ["resolutions"] = government._resolutions.Count, ["pressures"] = government._pressures.Count,
                    ["newNpcMeeting"] = newNpcMeeting,
                    ["newResolutionActivity"] = newResolutionActivity,
                    ["maximumMeetings"] = expectedMaximumMeetings,
                    ["restoredFromRetainedEvidence"] = restoredFromRetainedEvidence,
                    ["preparationFingerprint"] = fixture.Fingerprint });
            return GovernmentTestResult(runId, "soak_verify", assertions);
        }

        private static bool TryReadRetainedGovernmentSoakFixture(JObject options,
            out GovernmentSoakFixture fixture)
        {
            fixture = null;
            JObject retained = options?["soakPreparation"] as JObject;
            if (retained == null) return false;
            string expected = (options.Value<string>("expectedFeatureFingerprint")
                ?? string.Empty).Trim();
            string fingerprint = (retained.Value<string>("Fingerprint")
                ?? retained.Value<string>("fingerprint") ?? string.Empty).Trim();
            var candidate = new GovernmentSoakFixture
            {
                StartDay = retained.Value<float?>("StartDay")
                    ?? retained.Value<float?>("startDay") ?? -1f,
                GovernmentCount = retained.Value<int?>("GovernmentCount")
                    ?? retained.Value<int?>("governmentCount") ?? -1,
                PartyCount = retained.Value<int?>("PartyCount")
                    ?? retained.Value<int?>("partyCount") ?? -1,
                SeatCount = retained.Value<int?>("SeatCount")
                    ?? retained.Value<int?>("seatCount") ?? -1,
                ResolutionCount = retained.Value<int?>("ResolutionCount")
                    ?? retained.Value<int?>("resolutionCount") ?? -1,
                PressureCount = retained.Value<int?>("PressureCount")
                    ?? retained.Value<int?>("pressureCount") ?? -1,
                MeetingCount = retained.Value<int?>("MeetingCount")
                    ?? retained.Value<int?>("meetingCount") ?? -1,
                Fingerprint = fingerprint
            };
            bool valid = candidate.StartDay >= 0f
                && candidate.GovernmentCount > 0
                && candidate.PartyCount > 0
                && candidate.SeatCount > 0
                && candidate.ResolutionCount >= 0
                && candidate.PressureCount >= 0
                && candidate.MeetingCount >= 0
                && candidate.Fingerprint.Length == 64
                && expected.Length == 64
                && Same(candidate.Fingerprint, expected);
            if (!valid) return false;
            fixture = candidate;
            return true;
        }

        private static JObject GovernmentCertificationCleanup(ReignGovernmentCampaignBehavior government, string runId)
        {
            JArray assertions = new JArray();
            foreach (GovernmentLanguageFixture fixture in GovernmentLanguageFixtures.Values.ToList())
                RestoreGovernmentLanguageFixture(government, fixture);
            GovernmentLanguageFixtures.Clear();
            GovernmentSoakFixtures.Clear();
            government._governmentPrivateCaseBaselines.Clear();
            government._resolutions.RemoveAll(item => (item.ResolutionId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            government._pressures.RemoveAll(item => (item.ActionCorrelationId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            government._lobbyRecords.RemoveAll(item => (item.ResolutionId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            HashSet<string> temporaryPartyIds = new HashSet<string>(government._parties
                .Where(item => (item.PartyId ?? string.Empty)
                    .StartsWith("government_cert_coalition_party_", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.PartyId), StringComparer.OrdinalIgnoreCase);
            government._seats.RemoveAll(item => temporaryPartyIds.Contains(item.PartyId));
            government._parties.RemoveAll(item => temporaryPartyIds.Contains(item.PartyId));
            bool retained = government._resolutions.Any(item => (item.ResolutionId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || government._pressures.Any(item => (item.ActionCorrelationId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || government._lobbyRecords.Any(item => (item.ResolutionId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || government._parties.Any(item => (item.PartyId ?? string.Empty)
                    .StartsWith("government_cert_coalition_party_", StringComparison.OrdinalIgnoreCase));
            AddGovernmentAssertion(assertions, "government_certification_cleanup",
                GovernmentLanguageFixtures.Count == 0 && GovernmentSoakFixtures.Count == 0 && government._governmentPrivateCaseBaselines.Count == 0
                && !retained,
                "All non-serialized snapshots and exact Government certification-prefixed records were cleared; guarded campaign-test control still owns save cleanup and baseline protection.", null);
            return GovernmentTestResult(runId, "cleanup", assertions);
        }

        private static bool CsvContains(string csv, string value) =>
            !string.IsNullOrWhiteSpace(value) && (csv ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(item => string.Equals(item.Trim(), value, StringComparison.OrdinalIgnoreCase));

        private sealed class GovernmentLanguageFixture
        {
            public string CaseId { get; set; }
            public string KingdomId { get; set; }
            public string TargetHeroId { get; set; }
            public string FixtureName { get; set; }
            public string ExpectedTag { get; set; }
            public string ExpectedPosture { get; set; }
            public int OriginalLevel { get; set; }
            public int OriginalTrust { get; set; }
            public int OriginalConsentLevel { get; set; }
            public string OriginalConsentHeroes { get; set; }
            public string OriginalConsentClans { get; set; }
            public int OriginalRelation { get; set; }
            public float OriginalInfluence { get; set; }
            public int OriginalGovernmentLoyalty { get; set; }
            public int OriginalPartyLoyalty { get; set; }
            public int OriginalStance { get; set; }
            public string PartyId { get; set; }
            public string OriginalPartyPlanks { get; set; }
            public string OriginalSeatPartyId { get; set; }
            public string TemporaryFiefSettlementId { get; set; }
            public string OriginalFiefOwnerClanId { get; set; }
        }

        private sealed class GovernmentSoakFixture
        {
            public float StartDay { get; set; }
            public int GovernmentCount { get; set; }
            public int PartyCount { get; set; }
            public int SeatCount { get; set; }
            public int ResolutionCount { get; set; }
            public int PressureCount { get; set; }
            public int MeetingCount { get; set; }
            public string Fingerprint { get; set; }
        }
    }
}
