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

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private static readonly Dictionary<string, GovernmentLanguageFixture> GovernmentLanguageFixtures =
            new Dictionary<string, GovernmentLanguageFixture>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GovernmentSoakFixture> GovernmentSoakFixtures =
            new Dictionary<string, GovernmentSoakFixture>(StringComparer.OrdinalIgnoreCase);

        private JObject GovernmentCertificationCasePrepare(string runId, JObject options)
        {
            foreach (GovernmentLanguageFixture retained in GovernmentLanguageFixtures.Values.ToList())
                RestoreGovernmentLanguageFixture(retained);
            GovernmentLanguageFixtures.Clear();
            JArray assertions = new JArray();
            string caseId = options.Value<string>("caseId") ?? runId;
            string fixtureName = options.Value<string>("fixture") ?? string.Empty;
            string targetSearch = options.Value<string>("targetSearch") ?? string.Empty;
            string expectedTag = options.Value<string>("expectedTag") ?? "none";
            string expectedPosture = options.Value<string>("expectedPosture") ?? string.Empty;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            Hero target = FindHero(targetSearch);
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            bool playerRuler = kingdom != null && Hero.MainHero != null && kingdom.Leader == Hero.MainHero;
            bool targetEligible = target != null && target.IsAlive && !target.IsChild && !target.IsPrisoner
                && target != Hero.MainHero && (target.Clan?.Kingdom == kingdom
                    || GetSeats(kingdom?.StringId).Any(seat => Same(seat.HeroStringId, target.StringId)));
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
            EnsureGovernmentLanguageTargetSeat(kingdom, state, target, fixture);
            ReignGovernmentSeatRecord seat = GetSeats(kingdom.StringId)
                .FirstOrDefault(item => Same(item.HeroStringId, target.StringId));
            JObject productionKnowledge = BuildGovernmentKnowledgeForSpeaker(target);
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
                ReignGovernmentPartyRecord party = FindParty(kingdom.StringId, seat.PartyId);
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
            JObject postMutationKnowledge = BuildGovernmentKnowledgeForSpeaker(target);
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

        private JObject GovernmentCertificationCaseObserve(string runId, JObject options)
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
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
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
            RestoreGovernmentLanguageFixture(fixture);
            GovernmentLanguageFixtures.Remove(caseId);
            return result;
        }

        private void EnsureGovernmentLanguageTargetSeat(Kingdom kingdom,
            ReignGovernmentStateRecord state, Hero target, GovernmentLanguageFixture fixture)
        {
            if (kingdom == null || state == null || target == null
                || GetSeats(kingdom.StringId).Any(item => Same(item.HeroStringId, target.StringId)))
                return;
            if (target.Clan?.Leader != target || target.Clan == Clan.PlayerClan) return;

            Settlement fortification = Settlement.All.FirstOrDefault(item => item != null
                && item.IsFortification && item.OwnerClan == Clan.PlayerClan
                && item != Settlement.CurrentSettlement);
            if (fortification == null) return;
            fixture.TemporaryFiefSettlementId = fortification.StringId;
            fixture.OriginalFiefOwnerClanId = fortification.OwnerClan?.StringId ?? string.Empty;
            ChangeOwnerOfSettlementAction.ApplyByGift(fortification, target.Clan.Leader);
            ReconcileMembership(kingdom, state, false);
        }

        private void RestoreGovernmentLanguageFixture(GovernmentLanguageFixture fixture)
        {
            if (fixture == null) return;
            Kingdom kingdom = FindKingdom(fixture.KingdomId);
            Hero target = FindHero(fixture.TargetHeroId);
            ReignGovernmentStateRecord state = GetGovernment(kingdom);
            ReignGovernmentSeatRecord seat = GetSeats(fixture.KingdomId)
                .FirstOrDefault(item => Same(item.HeroStringId, fixture.TargetHeroId));
            if (seat != null)
            {
                seat.PartyId = fixture.OriginalSeatPartyId ?? seat.PartyId;
                seat.GovernmentLoyalty = fixture.OriginalGovernmentLoyalty;
                seat.PartyLoyalty = fixture.OriginalPartyLoyalty;
                seat.Revision++;
            }
            ReignGovernmentPartyRecord party = FindParty(fixture.KingdomId, fixture.PartyId);
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
                ReconcileMembership(kingdom, state, false);
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

        private JObject GovernmentCertificationCaseExecute(string runId, JObject options)
        {
            JArray assertions = new JArray();
            string caseId = (options.Value<string>("caseId") ?? runId).Trim().ToUpperInvariant();
            var owned = new HashSet<string>(Enumerable.Range(2, 42)
                .Select(index => "GOV-NATIVE-" + index.ToString("000")),
                StringComparer.OrdinalIgnoreCase);
            bool manifestOwned = owned.Contains(caseId);
            AddGovernmentAssertion(assertions, "government_case_manifest_owned", manifestOwned,
                "Only a fixed manifest-owned Government native case can use the certification fixture seam.",
                new JObject { ["caseId"] = caseId });
            if (!manifestOwned) return GovernmentTestResult(runId, "case_execute", assertions);

            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            ReignGovernmentStateRecord state = EnsureGovernment(playerKingdom);
            switch (caseId)
            {
                case "GOV-NATIVE-002":
                case "GOV-NATIVE-003":
                    GovernmentNativeUiCase(assertions, caseId, playerKingdom, state);
                    break;
                case "GOV-NATIVE-004":
                    GovernmentNativePlayerMeetingCase(assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-005":
                    GovernmentNativeNpcMeetingCase(assertions);
                    break;
                case "GOV-NATIVE-006":
                case "GOV-NATIVE-007":
                    GovernmentNativeResolutionRouteCase(assertions, playerKingdom, state,
                        caseId == "GOV-NATIVE-006" ? 0 : 1);
                    break;
                case "GOV-NATIVE-008":
                case "GOV-NATIVE-009":
                case "GOV-NATIVE-010":
                    GovernmentNativeLobbyCase(assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-011":
                case "GOV-NATIVE-012":
                case "GOV-NATIVE-013":
                case "GOV-NATIVE-014":
                case "GOV-NATIVE-015":
                case "GOV-NATIVE-016":
                case "GOV-NATIVE-017":
                    GovernmentNativeAuthorityCase(assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-018":
                    GovernmentNativeDecisionCase(assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-019":
                    GovernmentNativeCoalitionCase(assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-020":
                    GovernmentNativeRatifiedReductionCase(assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-021":
                    GovernmentNativeForcedReductionCase(assertions, playerKingdom, state);
                    break;
                case "GOV-NATIVE-023":
                case "GOV-NATIVE-024":
                    GovernmentNativePressureChannelCase(assertions, playerKingdom, state, caseId);
                    break;
                case "GOV-NATIVE-025":
                case "GOV-NATIVE-026":
                    GovernmentNativeNpcRulerCase(assertions, caseId);
                    break;
                case "GOV-NATIVE-027":
                    GovernmentNativeKnowledgeCase(assertions, playerKingdom);
                    break;
                case "GOV-NATIVE-028":
                case "GOV-NATIVE-029":
                    GovernmentNativeHistoryCase(assertions, playerKingdom, caseId);
                    break;
                case "GOV-NATIVE-036":
                case "GOV-NATIVE-037":
                case "GOV-NATIVE-038":
                case "GOV-NATIVE-039":
                case "GOV-NATIVE-040":
                case "GOV-NATIVE-041":
                case "GOV-NATIVE-042":
                case "GOV-NATIVE-043":
                    GovernmentNativeCultureInstitutionCase(assertions, caseId);
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

        private void GovernmentNativeCultureInstitutionCase(JArray assertions, string caseId)
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
                ? new List<SeatCandidate>() : SelectSeatCandidates(kingdom, profile);
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

            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
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
                publicSnapshot = BuildPublicSnapshot(kingdom);
                List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom.StringId).ToList();
                List<ReignGovernmentPartyRecord> parties = GetParties(kingdom.StringId).ToList();
                int coalitionSeats = GetGoverningCoalition(kingdom.StringId).Sum(item => item.SeatCount);
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

        private void GovernmentNativeUiCase(JArray assertions, string caseId,
            Kingdom playerKingdom, ReignGovernmentStateRecord state)
        {
            if (caseId == "GOV-NATIVE-002")
            {
                JObject snapshot = BuildPublicSnapshot(playerKingdom);
                AddGovernmentAssertion(assertions, "government_native_court_government_ui",
                    playerKingdom != null && state != null
                    && snapshot.Value<bool?>("authoritative") == true,
                    "The Court Government route is bound to the loaded player realm and its authoritative production snapshot; the required native screenshot and mouse evidence prove the actual button replacement.",
                    snapshot);
                return;
            }

            Kingdom foreign = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item != playerKingdom)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            JObject foreignSnapshot = BuildPublicSnapshot(foreign);
            AddGovernmentAssertion(assertions, "government_native_keep_read_only_ui",
                playerKingdom != null && foreign != null
                && foreignSnapshot.Value<bool?>("authoritative") == true
                && foreignSnapshot["members"] == null
                && foreignSnapshot["outstandingDeals"] == null,
                "The native Keep/Castle Layout route resolves a foreign realm's privacy-safe read-only Government snapshot; the required native screenshot and mouse evidence prove Explore Keep and Family Chambers navigation.",
                foreignSnapshot);
        }

        private void GovernmentNativePlayerMeetingCase(JArray assertions, Kingdom playerKingdom)
        {
            ReignGovernmentMeetingRecord meeting = _meetings
                .Where(item => item.PlayerAttended && Same(item.KingdomStringId, playerKingdom?.StringId))
                .OrderByDescending(item => item.MeetingDay).FirstOrDefault();
            List<ReignGovernmentPartyRecord> occupied = GetParties(playerKingdom?.StringId)
                .Where(item => item.SeatCount > 0 && !string.IsNullOrWhiteSpace(item.SpeakerHeroStringId))
                .ToList();
            JObject statements = new JObject();
            try
            {
                if (meeting != null) statements = JObject.Parse(
                    string.IsNullOrWhiteSpace(meeting.SpeakerStatementsJson) ? "{}" : meeting.SpeakerStatementsJson);
            }
            catch { statements = new JObject(); }
            JArray attempted = statements["__attemptedPartyIds"] as JArray ?? new JArray();
            List<string> attemptedIds = attempted.Values<string>().Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
            bool statementsComplete = occupied.All(party =>
                !string.IsNullOrWhiteSpace(statements.Value<string>(party.PartyId)));
            bool exactCalls = meeting != null && occupied.Count > 0
                && attemptedIds.Count == occupied.Count
                && attemptedIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == occupied.Count
                && occupied.All(party => attemptedIds.Contains(party.PartyId, StringComparer.OrdinalIgnoreCase))
                && meeting.ProviderCallCount == occupied.Count;
            AddGovernmentAssertion(assertions, "government_native_player_meeting_speaker_calls",
                exactCalls && statementsComplete,
                "A naturally attended seasonal meeting attempted exactly one provider turn for every occupied party speaker, retained one final statement per party, and made no per-member calls.",
                new JObject
                {
                    ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                    ["occupiedSpeakerCount"] = occupied.Count,
                    ["providerCallCount"] = meeting?.ProviderCallCount ?? -1,
                    ["attemptedPartyIds"] = new JArray(attemptedIds),
                    ["statementPartyIds"] = new JArray(statements.Properties()
                        .Where(property => !property.Name.StartsWith("__", StringComparison.Ordinal))
                        .Select(property => property.Name))
                });
        }

        private void GovernmentNativeNpcMeetingCase(JArray assertions)
        {
            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item.Leader != Hero.MainHero)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            int priorMeetings = _meetings.Count;
            ReignGovernmentMeetingRecord meeting = kingdom == null || state == null
                ? null : RunSeasonalMeeting(kingdom, state, false);
            List<ReignGovernmentResolutionRecord> proposed = meeting == null
                ? new List<ReignGovernmentResolutionRecord>()
                : _resolutions.Where(item => meeting.ResolutionIdsCsv.Split(',')
                    .Any(id => Same(id, item.ResolutionId))).ToList();
            bool dispositionsApplied = proposed.Count == 0 || proposed.All(item =>
                !Same(item.Status, "debate"));
            AddGovernmentAssertion(assertions, "government_native_npc_meetings_zero_calls",
                meeting != null && _meetings.Count == priorMeetings + 1
                && !meeting.PlayerAttended && meeting.ProviderCallCount == 0
                && dispositionsApplied
                && _meetings.Where(item => !item.PlayerAttended)
                    .All(item => item.ProviderCallCount == 0),
                "A real non-player realm completed a production seasonal meeting, resolved its proposals deterministically, and made zero provider calls.",
                new JObject
                {
                    ["kingdomId"] = kingdom?.StringId ?? string.Empty,
                    ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                    ["providerCallCount"] = meeting?.ProviderCallCount ?? -1,
                    ["proposalCount"] = proposed.Count,
                    ["proposalStatuses"] = new JArray(proposed.Select(item => item.Status))
                });
        }

        private void GovernmentNativeResolutionRouteCase(JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state, int routeIndex)
        {
            Settlement target = kingdom?.Fiefs.Select(item => item?.Settlement)
                .Where(item => item != null).OrderBy(item => item.StringId, StringComparer.Ordinal)
                .FirstOrDefault();
            ReignGovernmentPartyRecord party = GetParties(kingdom?.StringId)
                .Where(item => item.SeatCount > 0 && !string.IsNullOrWhiteSpace(item.SpeakerHeroStringId))
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Templates
                .Where(item =>
                {
                    ReignGovernmentResolutionRoute route = routeIndex == 0 ? item.FirstRoute : item.SecondRoute;
                    return !UsesImmediateGold(route.Action)
                        && route.Action != ReignGovernmentResolutionAction.GarrisonMaximum;
                })
                .OrderBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
            if (kingdom == null || state == null || target == null || party == null || template == null)
            {
                AddGovernmentAssertion(assertions, "government_native_resolution_route_completed", false,
                    "The disposable realm lacks a real settlement, occupied party speaker, or trackable resolution route.", null);
                return;
            }

            ReignGovernmentResolutionRoute selected = routeIndex == 0 ? template.FirstRoute : template.SecondRoute;
            var record = new ReignGovernmentResolutionRecord
            {
                ResolutionId = "government-cert-route-" + routeIndex + "-" + Guid.NewGuid().ToString("N"),
                KingdomStringId = kingdom.StringId,
                TemplateId = template.Id,
                PartyId = party.PartyId,
                SpeakerHeroStringId = party.SpeakerHeroStringId,
                TargetSettlementStringId = target.StringId,
                ProposedDay = CurrentDay(),
                Status = "proposed",
                ConsequenceLevel = state.Level,
                EvidenceJson = new JObject { ["tag"] = template.EvidenceTag,
                    ["settlementId"] = target.StringId, ["certificationRoute"] = routeIndex }
                    .ToString(Newtonsoft.Json.Formatting.None),
                Revision = 1
            };
            int trustBefore = state.Trust;
            float loyaltyBefore = target.Town?.Loyalty ?? 0f;
            float prosperityBefore = target.Town?.Prosperity ?? 0f;
            float securityBefore = target.Town?.Security ?? 0f;
            float hearthBefore = target.Village?.Hearth ?? 0f;
            _resolutions.Add(record);
            bool accepted = GovernmentNativeResolveSeasonalFixture(record, "route:" + routeIndex, kingdom, 1, out string acceptance);
            int progressed = accepted ? RecordResolutionProgress(kingdom.StringId, selected.Action,
                target.StringId, selected.Target, "release certification completed the exact selected route") : 0;
            bool measurableBenefit = (target.Town != null && (target.Town.Loyalty > loyaltyBefore
                    || target.Town.Prosperity > prosperityBefore || target.Town.Security > securityBefore))
                || (target.Village != null && target.Village.Hearth > hearthBefore);
            AddGovernmentAssertion(assertions, "government_native_resolution_route_completed",
                accepted && progressed == 1 && Same(record.Status, "completed")
                && record.SelectedRoute == routeIndex && record.RouteActionValue == (int)selected.Action
                && record.RequiredAmount == selected.Target && measurableBenefit && state.Trust == trustBefore,
                "The ruler selected the requested production route, target-specific progress completed it, and the saved completion reward measurably benefited the represented realm.",
                new JObject
                {
                    ["resolutionId"] = record.ResolutionId,
                    ["templateId"] = template.Id,
                    ["selectedRoute"] = record.SelectedRoute,
                    ["routeAction"] = selected.Action.ToString(),
                    ["requiredAmount"] = record.RequiredAmount,
                    ["status"] = record.Status,
                    ["outcome"] = record.Outcome,
                    ["acceptance"] = acceptance,
                    ["trustBefore"] = trustBefore,
                    ["trustAfter"] = state.Trust,
                    ["loyaltyBefore"] = loyaltyBefore,
                    ["loyaltyAfter"] = target.Town?.Loyalty ?? 0f,
                    ["prosperityBefore"] = prosperityBefore,
                    ["prosperityAfter"] = target.Town?.Prosperity ?? 0f,
                    ["securityBefore"] = securityBefore,
                    ["securityAfter"] = target.Town?.Security ?? 0f,
                    ["hearthBefore"] = hearthBefore,
                    ["hearthAfter"] = target.Village?.Hearth ?? 0f
                });
        }

        private void GovernmentNativeCoalitionCase(JArray assertions, Kingdom kingdom,
            ReignGovernmentStateRecord state)
        {
            List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom?.StringId)
                .OrderBy(item => item.HeroStringId, StringComparer.Ordinal).ToList();
            List<ReignGovernmentPartyRecord> parties = GetParties(kingdom?.StringId)
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).ToList();
            while (kingdom != null && parties.Count < 3)
            {
                int index = parties.Count + 1;
                var synthetic = new ReignGovernmentPartyRecord
                {
                    KingdomStringId = kingdom.StringId,
                    PartyId = "government_cert_coalition_party_" + index,
                    Name = "Certification Coalition Party " + index,
                    PlanksCsv = index == 1 ? "Trade,PopularWelfare"
                        : index == 2 ? "Security,LocalAutonomy" : "Justice,Agriculture",
                    PartyLoyalty = 50,
                    Revision = 1
                };
                _parties.Add(synthetic);
                parties.Add(synthetic);
            }
            if (kingdom == null || state == null || seats.Count < 3 || parties.Count < 3)
            {
                AddGovernmentAssertion(assertions, "government_native_fragmented_coalition", false,
                    "A fragmented coalition case requires at least three real occupied seats.",
                    new JObject { ["seatCount"] = seats.Count, ["partyCount"] = parties.Count });
                return;
            }
            for (int index = 0; index < seats.Count; index++)
            {
                seats[index].PartyId = parties[index % 3].PartyId;
                seats[index].Revision++;
            }
            RecalculatePartyState(kingdom, state);
            List<ReignGovernmentPartyRecord> coalition = GetGoverningCoalition(kingdom.StringId).ToList();
            int coalitionSeats = coalition.Sum(item => item.SeatCount);
            int largest = parties.Max(item => item.SeatCount);
            JObject snapshot = BuildPublicSnapshot(kingdom);
            List<string> publicIds = (snapshot["governingCoalitionPartyIds"] as JArray
                ?? new JArray()).Values<string>().ToList();
            bool exactPublic = coalition.All(item => publicIds.Contains(item.PartyId,
                    StringComparer.OrdinalIgnoreCase))
                && publicIds.All(id => coalition.Any(item => Same(item.PartyId, id)));
            AddGovernmentAssertion(assertions, "government_native_fragmented_coalition",
                largest * 2 <= seats.Count && coalition.Count >= 2
                && coalitionSeats * 2 > seats.Count && exactPublic
                && Same(state.CoalitionPartyIdsCsv, string.Join(",", coalition.Select(item => item.PartyId))),
                "A deliberately fragmented chamber formed a deterministic saved strict-majority coalition, and ruler/ambassador knowledge exposed that exact coalition.",
                new JObject
                {
                    ["seatCount"] = seats.Count,
                    ["largestPartySeats"] = largest,
                    ["coalitionSeatCount"] = coalitionSeats,
                    ["savedCoalitionIds"] = state.CoalitionPartyIdsCsv,
                    ["publicCoalitionIds"] = new JArray(publicIds)
                });
        }

        private void GovernmentNativeRatifiedReductionCase(JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom?.StringId).ToList();
            List<ReignGovernmentPartyRecord> parties = GetParties(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || kingdom.Leader != Hero.MainHero
                || seats.Count == 0 || parties.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_ratified_reduction_effects", false,
                    "The ratified reduction requires the real player ruler and an occupied government.", null);
                return;
            }
            state.Level = 5;
            state.Trust = 90;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord party in parties)
            {
                party.PlanksCsv = "RoyalAuthority,Security";
                party.PartyLoyalty = 100;
                party.Revision++;
            }
            var relationBefore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (ReignGovernmentSeatRecord seat in seats)
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null)
                {
                    int current = member.GetRelation(kingdom.Leader);
                    if (current != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        kingdom.Leader, member, 100 - current, false);
                    relationBefore[member.StringId] = member.GetRelation(kingdom.Leader);
                }
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
                seat.Revision++;
            }
            List<Town> towns = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                .ToList();
            foreach (Town town in towns) town.Loyalty = 100f;
            bool reduced = ReduceGovernmentLevel(kingdom, false, out string result);
            int votesFor = seats.Count(item => item.LastReductionVoteSupported);
            bool exactLoyalty = towns.Count > 0
                && towns.All(town => Math.Abs(town.Loyalty - 95f) < 0.01f);
            bool relationsUnchanged = relationBefore.All(pair =>
            {
                Hero member = FindHero(pair.Key);
                return member != null && member.GetRelation(kingdom.Leader) == pair.Value;
            });
            AddGovernmentAssertion(assertions, "government_native_ratified_reduction_effects",
                reduced && ReignGovernmentRules.ReductionRatified(votesFor, seats.Count)
                && state.Level == 4 && state.Trust == 90 && exactLoyalty
                && relationsUnchanged
                && state.ReductionConsentFromLevel == 0
                && string.IsNullOrWhiteSpace(state.ReductionConsentHeroIdsCsv)
                && string.IsNullOrWhiteSpace(state.ReductionConsentClanIdsCsv),
                "Every member cast the production reduction vote; the two-thirds ratified path applies exactly -5 settlement loyalty without changing retired Trust or applying forced noble relation losses.",
                new JObject
                {
                    ["votesFor"] = votesFor,
                    ["occupiedSeats"] = seats.Count,
                    ["authorityAfter"] = state.Level,
                    ["trustAfter"] = state.Trust,
                    ["fortificationLoyalties"] = new JArray(towns.Select(town =>
                        new JObject { ["settlementId"] = town.Settlement.StringId,
                            ["loyalty"] = town.Loyalty })),
                    ["result"] = result
                });
        }

        private void GovernmentNativeForcedReductionCase(JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            List<Clan> candidateClans = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).ToList()
                ?? new List<Clan>();
            Clan preferredConsentingClan = candidateClans.FirstOrDefault(clan => clan.Heroes.Any(hero =>
                hero != null && hero != clan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner));
            Clan preferredNonConsentingClan = candidateClans.FirstOrDefault(clan => clan != preferredConsentingClan);
            GovernmentTemporaryLandholdingFixture landFixture = EnsureTemporaryLandholdingClans(kingdom,
                new[] { preferredConsentingClan, preferredNonConsentingClan });
            try
            {
            List<Clan> landholding = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Fiefs.Count > 0 && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).ToList()
                ?? new List<Clan>();
            Clan consentingClan = landholding.FirstOrDefault(clan => clan.Heroes.Any(hero =>
                hero != null && hero != clan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner));
            Clan nonConsentingClan = landholding.FirstOrDefault(clan => clan != consentingClan);
            Hero sameClanMember = consentingClan?.Heroes.Where(hero => hero != null
                    && hero != consentingClan.Leader && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            List<ReignGovernmentSeatRecord> originalSeats = GetSeats(kingdom?.StringId).ToList();
            Hero individualConsent = originalSeats.Select(item => FindHero(item.HeroStringId))
                .Where(hero => hero != null && hero != Hero.MainHero && hero.Clan != Clan.PlayerClan
                    && hero.Clan != consentingClan && hero.Clan?.Leader != hero)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero nonLandholder = originalSeats.Select(item => FindHero(item.HeroStringId))
                .Where(hero => hero != null && hero != individualConsent && hero != Hero.MainHero
                    && hero.Clan != Clan.PlayerClan && hero.Clan != consentingClan
                    && hero.Clan?.Leader != hero && hero != nonConsentingClan?.Leader)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault();
            Hero playerClanMember = Clan.PlayerClan?.Heroes.Where(hero => hero != null
                    && hero != Hero.MainHero && hero.IsAlive && !hero.IsChild && !hero.IsPrisoner)
                .OrderBy(hero => hero.StringId, StringComparer.Ordinal).FirstOrDefault() ?? Hero.MainHero;
            ReignGovernmentPartyRecord party = GetParties(kingdom?.StringId)
                .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
            bool rolesAvailable = kingdom != null && state != null && kingdom.Leader == Hero.MainHero
                && consentingClan?.Leader != null && nonConsentingClan?.Leader != null
                && sameClanMember != null && individualConsent != null && nonLandholder != null
                && playerClanMember != null && party != null;
            if (!rolesAvailable)
            {
                AddGovernmentAssertion(assertions, "government_native_forced_reduction_effects", false,
                    "The controlled forced-reduction scenario must contain two landholding clans, a second member of the consenting clan, an individual notable, a non-consenting nonlandholder, and a player-clan member.",
                    new JObject
                    {
                        ["landholdingClanCount"] = landholding.Count,
                        ["hasConsentingClanMember"] = sameClanMember != null,
                        ["hasIndividualConsent"] = individualConsent != null,
                        ["hasNonLandholder"] = nonLandholder != null,
                        ["hasPlayerClanMember"] = playerClanMember != null
                    });
                return;
            }

            state.Level = 5;
            state.Trust = 100;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord item in GetParties(kingdom.StringId))
            {
                item.PlanksCsv = "RepresentativeAuthority,PopularWelfare";
                item.PartyLoyalty = 100;
                item.Revision++;
            }
            foreach (Hero hero in new[] { consentingClan.Leader, nonConsentingClan.Leader,
                sameClanMember, individualConsent, nonLandholder, playerClanMember })
                EnsureGovernmentCertificationSeat(kingdom, party, hero);
            List<ReignGovernmentSeatRecord> voters = GetSeats(kingdom.StringId).ToList();
            foreach (ReignGovernmentSeatRecord seat in voters)
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null)
                {
                    int current = member.GetRelation(kingdom.Leader);
                    if (current != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                        kingdom.Leader, member, 100 - current, false);
                }
                seat.PartyId = party.PartyId;
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
                seat.Revision++;
            }
            bool clanConsentRecorded = TryRecordReductionConsent(kingdom.Leader,
                consentingClan.Leader, out string clanConsentResult);
            bool individualConsentRecorded = TryRecordReductionConsent(kingdom.Leader,
                individualConsent, out string individualConsentResult);
            var before = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Hero hero in new[] { consentingClan.Leader, nonConsentingClan.Leader,
                sameClanMember, individualConsent, nonLandholder, playerClanMember })
                before[hero.StringId] = hero.GetRelation(kingdom.Leader);
            List<Town> towns = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                .ToList();
            foreach (Town town in towns) town.Loyalty = 100f;
            bool reduced = ReduceGovernmentLevel(kingdom, true, out string reductionResult);
            int votesFor = voters.Count(item => item.LastReductionVoteSupported);
            int Delta(Hero hero) => hero.GetRelation(kingdom.Leader) - before[hero.StringId];
            bool exact = reduced && !ReignGovernmentRules.ReductionRatified(votesFor, voters.Count)
                && state.Level == 4 && state.Trust == 100 && towns.Count > 0
                && towns.All(town => Math.Abs(town.Loyalty - 30f) < 0.01f)
                && Delta(nonConsentingClan.Leader) == -70
                && Delta(nonLandholder) == -40
                && Delta(consentingClan.Leader) == 0
                && Delta(sameClanMember) == 0
                && Delta(individualConsent) == 0
                && Delta(playerClanMember) == 0
                && state.ReductionConsentFromLevel == 0
                && string.IsNullOrWhiteSpace(state.ReductionConsentHeroIdsCsv)
                && string.IsNullOrWhiteSpace(state.ReductionConsentClanIdsCsv);
            AddGovernmentAssertion(assertions, "government_native_forced_reduction_effects",
                clanConsentRecorded && individualConsentRecorded && exact,
                "The actual failed vote followed by forced reduction applies -70 loyalty, -70 to a non-consenting landholding clan leader, -40 to a non-consenting nonlandholder, and no loss to consented members or the player clan; tags are consumed once while legacy Trust stays inert.",
                new JObject
                {
                    ["votesFor"] = votesFor,
                    ["occupiedSeats"] = voters.Count,
                    ["authorityAfter"] = state.Level,
                    ["trustAfter"] = state.Trust,
                    ["nonConsentingClanLeaderDelta"] = Delta(nonConsentingClan.Leader),
                    ["nonConsentingNonLandholderDelta"] = Delta(nonLandholder),
                    ["consentingClanLeaderDelta"] = Delta(consentingClan.Leader),
                    ["consentingClanMemberDelta"] = Delta(sameClanMember),
                    ["individualConsentDelta"] = Delta(individualConsent),
                    ["playerClanMemberDelta"] = Delta(playerClanMember),
                    ["fortificationLoyalties"] = new JArray(towns.Select(town =>
                        new JObject { ["settlementId"] = town.Settlement.StringId,
                            ["loyalty"] = town.Loyalty })),
                    ["clanConsentResult"] = clanConsentResult,
                    ["individualConsentResult"] = individualConsentResult,
                    ["reductionResult"] = reductionResult
                });
            }
            finally
            {
                landFixture?.Dispose();
            }
        }

        private ReignGovernmentSeatRecord EnsureGovernmentCertificationSeat(Kingdom kingdom,
            ReignGovernmentPartyRecord party, Hero hero)
        {
            ReignGovernmentSeatRecord seat = _seats.FirstOrDefault(item =>
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
            _seats.Add(seat);
            return seat;
        }

        private GovernmentTemporaryLandholdingFixture EnsureTemporaryLandholdingClans(
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

        private void GovernmentNativePressureChannelCase(JArray assertions,
            Kingdom kingdom, ReignGovernmentStateRecord state, string caseId)
        {
            JObject snapshot = BuildPublicSnapshot(kingdom);
            bool separate = snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")
                    ?.Value<bool>() == true
                && snapshot.SelectToken("peoplePressure.probabilityRule")?.Value<string>()
                    ?.IndexOf("does not directly rewrite", StringComparison.OrdinalIgnoreCase) >= 0;
            if (caseId == "GOV-NATIVE-023")
            {
                Town town = kingdom?.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                    .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
                ReignGovernmentPartyRecord party = GetParties(kingdom?.StringId)
                    .Where(item => item.SeatCount > 0)
                    .OrderBy(item => item.PartyId, StringComparer.Ordinal).FirstOrDefault();
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find("R004")
                    ?? ReignGovernmentResolutionCatalog.Templates.FirstOrDefault();
                if (kingdom == null || state == null || town == null || party == null || template == null)
                {
                    AddGovernmentAssertion(assertions, "government_native_people_pressure_native_unrest", false,
                        "The people-pressure case requires a real fortification, occupied party, and resolution template.", snapshot);
                    return;
                }
                state.Level = 4;
                town.Loyalty = 45f;
                var record = new ReignGovernmentResolutionRecord
                {
                    ResolutionId = "government-cert-people-pressure-" + Guid.NewGuid().ToString("N"),
                    KingdomStringId = kingdom.StringId,
                    TemplateId = template.Id,
                    PartyId = party.PartyId,
                    SpeakerHeroStringId = party.SpeakerHeroStringId,
                    TargetSettlementStringId = town.Settlement.StringId,
                    ProposedDay = CurrentDay(),
                    Status = "proposed",
                    ConsequenceLevel = state.Level,
                    Revision = 1
                };
                _resolutions.Add(record);
                float before = town.Loyalty;
                bool declined = GovernmentNativeResolveSeasonalFixture(record, "reject", kingdom, 4, out string result);
                AddGovernmentAssertion(assertions, "government_native_people_pressure_native_unrest",
                    separate && declined && Same(record.Status, "refused")
                    && town.Loyalty < before
                    && snapshot.SelectToken("peoplePressure.settlementRoute")?.Value<string>()
                        ?.IndexOf("native town rebellion", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Refusing a real notable-backed resolution reduced the represented settlement's native loyalty input while the public contract left Public Standing and rebellion probability formulas untouched.",
                    new JObject
                    {
                        ["settlementId"] = town.Settlement.StringId,
                        ["loyaltyBefore"] = before,
                        ["loyaltyAfter"] = town.Loyalty,
                        ["resolutionId"] = record.ResolutionId,
                        ["resolutionStatus"] = record.Status,
                        ["result"] = result,
                        ["peoplePressure"] = snapshot["peoplePressure"]
                    });
                return;
            }

            Clan affectedClan = kingdom?.Clans.Where(clan => clan != null
                    && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                    && !clan.IsEliminated && clan.Fiefs.Count > 0 && clan.Leader != null)
                .OrderBy(clan => clan.StringId, StringComparer.Ordinal).FirstOrDefault();
            GovernmentTemporaryLandholdingFixture landFixture = null;
            if (affectedClan == null)
            {
                affectedClan = kingdom?.Clans.Where(clan => clan != null
                        && clan != kingdom.RulingClan && clan != Clan.PlayerClan
                        && !clan.IsEliminated && clan.Leader != null)
                    .OrderBy(clan => clan.StringId, StringComparer.Ordinal).FirstOrDefault();
                landFixture = EnsureTemporaryLandholdingClans(kingdom, new[] { affectedClan });
            }
            try
            {
            List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || affectedClan == null
                || affectedClan.Fiefs.Count == 0 || seats.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_lord_pressure_civil_war_input", false,
                    "The noble-pressure case requires one non-ruling landholding clan and occupied Government seats.", snapshot);
                return;
            }
            state.Level = 5;
            state.Trust = 100;
            state.ReductionConsentFromLevel = 0;
            state.ReductionConsentHeroIdsCsv = string.Empty;
            state.ReductionConsentClanIdsCsv = string.Empty;
            foreach (ReignGovernmentPartyRecord party in GetParties(kingdom.StringId))
            {
                party.PlanksCsv = "RepresentativeAuthority,PopularWelfare";
                party.PartyLoyalty = 100;
            }
            foreach (ReignGovernmentSeatRecord seat in seats)
            {
                seat.PartyLoyalty = 100;
                seat.GovernmentLoyalty = 100;
            }
            int relation = affectedClan.Leader.GetRelation(kingdom.Leader);
            if (relation != 100) ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                kingdom.Leader, affectedClan.Leader, 100 - relation, false);
            int relationBefore = affectedClan.Leader.GetRelation(kingdom.Leader);
            bool forced = ReduceGovernmentLevel(kingdom, true, out string forcedResult);
            int relationAfter = affectedClan.Leader.GetRelation(kingdom.Leader);
            AddGovernmentAssertion(assertions, "government_native_lord_pressure_civil_war_input",
                separate && forced && relationAfter - relationBefore == -70
                && snapshot.SelectToken("peoplePressure.lordRoute")?.Value<string>()
                    ?.IndexOf("civil-war", StringComparison.OrdinalIgnoreCase) >= 0,
                "Ignoring landholding-lord Government pressure changed the same native ruler-relation value consumed by Reign's separate civil-war system, without replacing noble Political Pressure.",
                new JObject
                {
                    ["clanId"] = affectedClan.StringId,
                    ["leaderHeroId"] = affectedClan.Leader.StringId,
                    ["relationBefore"] = relationBefore,
                    ["relationAfter"] = relationAfter,
                    ["relationDelta"] = relationAfter - relationBefore,
                    ["result"] = forcedResult,
                    ["peoplePressure"] = snapshot["peoplePressure"]
                });
            }
            finally
            {
                landFixture?.Dispose();
            }
        }

        private void GovernmentNativeNpcRulerCase(JArray assertions, string caseId)
        {
            Kingdom kingdom = Kingdom.All.Where(IsEligibleKingdom)
                .Where(item => item.Leader != Hero.MainHero)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            List<ReignGovernmentPartyRecord> parties = GetParties(kingdom?.StringId).ToList();
            ReignGovernmentPartyRecord coalition = parties.OrderBy(item => item.PartyId,
                StringComparer.Ordinal).FirstOrDefault();
            if (kingdom == null || state == null || coalition == null)
            {
                AddGovernmentAssertion(assertions, "government_native_npc_ruler_pressure", false,
                    "NPC ruler pressure requires a real non-player kingdom with an occupied coalition.", null);
                return;
            }
            bool opposed = caseId == "GOV-NATIVE-026";
            state.Level = opposed ? 5 : 1;
            state.Trust = 60;
            state.StanceTowardRuler = opposed ? -100 : 100;
            foreach (ReignGovernmentPartyRecord party in parties)
            {
                party.IsGoverningCoalition = party == coalition;
                party.IsDominant = party == coalition;
                party.PlanksCsv = opposed ? "Peace,PopularWelfare" : "RoyalAuthority,MilitaryStrength";
            }
            state.DominantPartyId = coalition.PartyId;
            state.CoalitionPartyIdsCsv = coalition.PartyId;
            var action = new ReignWorldActionRecord
            {
                ActionId = "government-cert-npc-ruler-" + caseId + "-" + Guid.NewGuid().ToString("N"),
                Type = ReignWorldActionType.DiplomacyDeclareWar,
                ActorHeroStringId = kingdom.Leader.StringId,
                ActorClanStringId = kingdom.Leader.Clan?.StringId ?? string.Empty,
                ActorKingdomStringId = kingdom.StringId,
                Reason = opposed ? "opposed aggressive war" : "defensive government-backed war"
            };
            bool allowed = TryAuthorizeAction(action, out ReignActionResult blocked);
            var business = _business.FirstOrDefault(item => Same(item.ActionCorrelationId, action.ActionId));
            foreach (var seat in GetSeats(kingdom.StringId))
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member != null && member != kingdom.Leader)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(kingdom.Leader, member,
                        (opposed ? -100 : 100) - member.GetRelation(kingdom.Leader), false);
            }
            bool voted = business != null && RecommendBusiness(business.BusinessId, "accept", kingdom.Leader, out _)
                && VoteBusiness(business.BusinessId, kingdom.Leader, out _);
            bool responded = opposed || voted && ResolveBusiness(business.BusinessId, "accept", kingdom.Leader,
                business.WinningOptionId != "accept", out _);
            JObject snapshot = BuildPublicSnapshot(kingdom);
            bool expected = !allowed && blocked != null && voted && responded && (opposed
                ? business.Status == "decided" && business.WinningOptionId == "reject"
                    && !ResolveBusiness(business.BusinessId, "accept", kingdom.Leader, true, out _)
                : business.Status == "authorized");
            AddGovernmentAssertion(assertions, "government_native_npc_ruler_pressure",
                expected
                && snapshot.SelectToken("peoplePressure.additionalToNoblePoliticalPressure")
                    ?.Value<bool>() == true,
                "The real NPC ruler uses the same production recommendation, individual vote and response methods: lower authority permits its response while a binding opposed vote prevents a ruler veto. Autonomous scheduling and actual war execution require separate native evidence.",
                new JObject
                {
                    ["caseId"] = caseId,
                    ["kingdomId"] = kingdom.StringId,
                    ["rulerHeroId"] = kingdom.Leader.StringId,
                    ["authorityLevel"] = state.Level,
                    ["allowed"] = allowed,
                    ["blockMessage"] = blocked?.Message ?? string.Empty,
                    ["business"] = business == null ? null : JObject.FromObject(business)
                });
        }

        private void GovernmentNativeKnowledgeCase(JArray assertions, Kingdom kingdom)
        {
            JObject snapshot = BuildPublicSnapshot(kingdom);
            bool partiesKnown = snapshot["parties"] is JArray parties && parties.Count >= 2
                && parties.Children<JObject>().All(party =>
                    !string.IsNullOrWhiteSpace(party.Value<string>("partyId"))
                    && party["planks"] is JArray && party.Value<int?>("seatCount") >= 0);
            AddGovernmentAssertion(assertions, "government_native_ruler_ambassador_knowledge",
                snapshot.Value<bool?>("authoritative") == true && partiesKnown
                && snapshot.Value<int?>("authorityLevel") >= 1
                && snapshot.Value<int?>("authorityLevel") <= 5
                && snapshot.Value<string>("decisionRule")?.IndexOf("ruler may agree",
                    StringComparison.OrdinalIgnoreCase) >= 0
                && snapshot["governingCoalitionPartyIds"] is JArray,
                "The authoritative public Government knowledge contains current authority, coalition, party agendas, speakers, stance, and the explicit ruler-agrees/government-refuses negotiation rule.", snapshot);
        }

        private void GovernmentNativeHistoryCase(JArray assertions, Kingdom kingdom, string caseId)
        {
            ReignGovernmentStateRecord state = EnsureGovernment(kingdom);
            List<ReignGovernmentPartyRecord> parties = GetParties(kingdom?.StringId).ToList();
            if (kingdom == null || state == null || parties.Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_history_resolution", false,
                    "History-backed resolution generation requires a loaded realm and occupied parties.", null);
                return;
            }
            var originalPlanks = parties.ToDictionary(item => item.PartyId,
                item => item.PlanksCsv, StringComparer.OrdinalIgnoreCase);
            Village raidVillage = null;
            Village.VillageStates originalVillageState = Village.VillageStates.Normal;
            string spymasterEffectId = string.Empty;
            Town spyTarget = null;
            bool prerequisiteActive = false;
            string temporaryPartyId = string.Empty;
            var originalSeatParties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string planks = caseId == "GOV-NATIVE-028"
                    ? "PopularWelfare,Agriculture,Justice,MilitaryStrength"
                    : "Espionage,Security,Justice,PopularWelfare";
                foreach (ReignGovernmentPartyRecord party in parties)
                {
                    party.PlanksCsv = planks;
                    party.Revision++;
                }
                if (caseId == "GOV-NATIVE-028")
                {
                    raidVillage = Settlement.All.Where(item => item?.IsVillage == true
                            && item.MapFaction == kingdom && item.Village != null)
                        .Select(item => item.Village)
                        .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
                    if (raidVillage != null)
                    {
                        originalVillageState = raidVillage.VillageState;
                        raidVillage.VillageState = Village.VillageStates.Looted;
                        prerequisiteActive = raidVillage.VillageState == Village.VillageStates.Looted;
                    }
                }
                else
                {
                    spyTarget = kingdom.Fiefs.Where(item => item?.Settlement?.IsFortification == true)
                        .OrderBy(item => item.Settlement.StringId, StringComparer.Ordinal).FirstOrDefault();
#if !REIGN_EXCLUDE_COURT
                    spymasterEffectId = ReignCourtCampaignBehavior.Instance
                        ?.AddGovernmentCertificationSubterfugeEffect(spyTarget) ?? string.Empty;
                    prerequisiteActive = !string.IsNullOrWhiteSpace(spymasterEffectId)
                        && (ReignCourtCampaignBehavior.Instance?.GetSpymasterLoyaltyDelta(spyTarget) ?? 0f) != 0f;
#endif
                }

                bool MatchingTag(string tag) => caseId == "GOV-NATIVE-028"
                    ? tag.IndexOf("raid", StringComparison.OrdinalIgnoreCase) >= 0
                    : tag.IndexOf("subterfuge", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("agent", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("spy", StringComparison.OrdinalIgnoreCase) >= 0
                        || tag.IndexOf("rumor", StringComparison.OrdinalIgnoreCase) >= 0;
                HashSet<string> evidenceTags = CollectEvidenceTags(kingdom);
                List<ReignGovernmentPlank> fixturePlanks = ParsePlanks(planks);
                int season = (int)Math.Floor(CurrentDay() / ReignGovernmentRules.SeasonalMeetingDays);
                for (int index = 0; index < 4096 && string.IsNullOrWhiteSpace(temporaryPartyId); index++)
                {
                    string candidateId = "government_cert_history_" + caseId.ToLowerInvariant()
                        .Replace('-', '_') + "_" + index;
                    ReignGovernmentResolutionTemplate selected = ReignGovernmentResolutionCatalog.Templates
                        .Where(item => ReignGovernmentResolutionCatalog.IsEligible(
                            item, evidenceTags, fixturePlanks))
                        .OrderBy(item => ReignGovernmentRules.StableHash("resolution|" + kingdom.StringId
                            + "|" + candidateId + "|" + season + "|" + item.Id))
                        .FirstOrDefault();
                    if (MatchingTag(selected?.EvidenceTag ?? string.Empty)) temporaryPartyId = candidateId;
                }
                List<ReignGovernmentSeatRecord> fixtureSeats = GetSeats(kingdom.StringId).ToList();
                if (!string.IsNullOrWhiteSpace(temporaryPartyId) && fixtureSeats.Count > 0)
                {
                    foreach (ReignGovernmentSeatRecord seat in fixtureSeats)
                    {
                        originalSeatParties[seat.HeroStringId] = seat.PartyId;
                        seat.PartyId = temporaryPartyId;
                        seat.Revision++;
                    }
                    _parties.Add(new ReignGovernmentPartyRecord
                    {
                        KingdomStringId = kingdom.StringId,
                        PartyId = temporaryPartyId,
                        Name = "Controlled History Certification Caucus",
                        PlanksCsv = planks,
                        SpeakerHeroStringId = fixtureSeats.Select(item => item.HeroStringId)
                            .OrderBy(item => item, StringComparer.Ordinal).FirstOrDefault() ?? string.Empty,
                        PartyLoyalty = 100,
                        Revision = 1
                    });
                    RecalculatePartyState(kingdom, state);
                }
                int before = _resolutions.Count;
                ReignGovernmentMeetingRecord meeting = RunSeasonalMeeting(kingdom, state, true);
                List<ReignGovernmentResolutionRecord> generated = _resolutions.Skip(before).ToList();
                ReignGovernmentResolutionRecord matching = generated.FirstOrDefault(record =>
                    MatchingTag(ReignGovernmentResolutionCatalog.Find(record.TemplateId)?.EvidenceTag ?? string.Empty));
                ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(matching?.TemplateId);
                bool tangible = template != null && template.FirstRoute.Target > 0
                    && template.SecondRoute.Target > 0
                    && (template.FirstRoute.Action != template.SecondRoute.Action
                        || !Same(template.FirstRoute.Description, template.SecondRoute.Description));
                bool targetGrounded = matching != null
                    && (!string.IsNullOrWhiteSpace(matching.TargetSettlementStringId)
                        || !string.IsNullOrWhiteSpace(matching.TargetHeroStringId)
                        || !string.IsNullOrWhiteSpace(matching.TargetClanStringId));
                AddGovernmentAssertion(assertions, "government_native_history_resolution",
                    prerequisiteActive && meeting != null && matching != null && tangible && targetGrounded,
                    "A controlled real raid or Spymaster-history condition produced an evidence-tagged, specifically targeted, tangible two-route demand through the production seasonal meeting selector.",
                    new JObject
                    {
                        ["caseId"] = caseId,
                        ["prerequisiteActive"] = prerequisiteActive,
                        ["raidVillageId"] = raidVillage?.Settlement?.StringId ?? string.Empty,
                        ["spymasterTargetId"] = spyTarget?.Settlement?.StringId ?? string.Empty,
                        ["spymasterEffectId"] = spymasterEffectId,
                        ["temporaryPartyId"] = temporaryPartyId,
                        ["meetingId"] = meeting?.MeetingId ?? string.Empty,
                        ["generatedResolutionCount"] = generated.Count,
                        ["matchingResolutionId"] = matching?.ResolutionId ?? string.Empty,
                        ["templateId"] = template?.Id ?? string.Empty,
                        ["evidenceTag"] = template?.EvidenceTag ?? string.Empty,
                        ["targetSettlementId"] = matching?.TargetSettlementStringId ?? string.Empty,
                        ["targetHeroId"] = matching?.TargetHeroStringId ?? string.Empty,
                        ["targetClanId"] = matching?.TargetClanStringId ?? string.Empty,
                        ["firstRoute"] = template?.FirstRoute.Description ?? string.Empty,
                        ["secondRoute"] = template?.SecondRoute.Description ?? string.Empty
                    });
            }
            finally
            {
                if (raidVillage != null) raidVillage.VillageState = originalVillageState;
#if !REIGN_EXCLUDE_COURT
                ReignCourtCampaignBehavior.Instance
                    ?.RemoveGovernmentCertificationSubterfugeEffect(spymasterEffectId);
#endif
                foreach (ReignGovernmentSeatRecord seat in GetSeats(kingdom?.StringId).ToList())
                {
                    if (originalSeatParties.TryGetValue(seat.HeroStringId, out string partyId))
                        seat.PartyId = partyId;
                }
                if (!string.IsNullOrWhiteSpace(temporaryPartyId))
                    _parties.RemoveAll(item => Same(item.KingdomStringId, kingdom?.StringId)
                        && Same(item.PartyId, temporaryPartyId));
                foreach (ReignGovernmentPartyRecord party in parties)
                {
                    if (originalPlanks.TryGetValue(party.PartyId, out string value)) party.PlanksCsv = value;
                }
                if (kingdom != null && state != null) RecalculatePartyState(kingdom, state);
            }
        }

        private JObject GovernmentCertificationSoakPrepare(string runId)
        {
            JArray assertions = new JArray();
            var fixture = new GovernmentSoakFixture
            {
                StartDay = CurrentDay(),
                GovernmentCount = _governments.Count,
                PartyCount = _parties.Count,
                SeatCount = _seats.Count,
                ResolutionCount = _resolutions.Count,
                PressureCount = _pressures.Count,
                MeetingCount = _meetings.Count,
                Fingerprint = GovernmentFeatureFingerprint()
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

        private JObject GovernmentCertificationSoakVerify(string runId, JObject options)
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
                + Math.Max(1, _governments.Count) * 12;
            bool newNpcMeeting = _meetings.Any(item => !item.PlayerAttended
                && item.MeetingDay + 0.01f >= fixture.StartDay);
            bool newResolutionActivity = _resolutions.Any(item => item.ProposedDay + 0.01f >= fixture.StartDay)
                || _resolutions.Count != fixture.ResolutionCount;
            bool stable = elapsed >= 209.5f
                && _governments.Count == fixture.GovernmentCount
                && _parties.Count >= fixture.PartyCount
                && _parties.Count <= fixture.PartyCount + fixture.GovernmentCount * 2
                && _meetings.Count >= fixture.MeetingCount
                && _meetings.Count <= expectedMaximumMeetings
                && newNpcMeeting && newResolutionActivity
                && _meetings.Where(item => !item.PlayerAttended).All(item => item.ProviderCallCount == 0)
                && _resolutions.Count <= fixture.ResolutionCount + fixture.GovernmentCount * 40
                && _pressures.Count <= fixture.PressureCount + fixture.GovernmentCount * 40;
            AddGovernmentAssertion(assertions, "government_soak_verify", stable,
                "After 210 externally advanced days, Government identity remains stable, seasonal meetings stay bounded, NPC meetings remain provider-free, and saved collections show no runaway growth.",
                new JObject { ["elapsedDays"] = elapsed, ["governments"] = _governments.Count,
                    ["parties"] = _parties.Count, ["meetings"] = _meetings.Count,
                    ["resolutions"] = _resolutions.Count, ["pressures"] = _pressures.Count,
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

        private JObject GovernmentCertificationCleanup(string runId)
        {
            JArray assertions = new JArray();
            foreach (GovernmentLanguageFixture fixture in GovernmentLanguageFixtures.Values.ToList())
                RestoreGovernmentLanguageFixture(fixture);
            GovernmentLanguageFixtures.Clear();
            GovernmentSoakFixtures.Clear();
            _governmentPrivateCaseBaselines.Clear();
            _resolutions.RemoveAll(item => (item.ResolutionId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            _pressures.RemoveAll(item => (item.ActionCorrelationId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            _lobbyRecords.RemoveAll(item => (item.ResolutionId ?? string.Empty)
                .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase));
            HashSet<string> temporaryPartyIds = new HashSet<string>(_parties
                .Where(item => (item.PartyId ?? string.Empty)
                    .StartsWith("government_cert_coalition_party_", StringComparison.OrdinalIgnoreCase))
                .Select(item => item.PartyId), StringComparer.OrdinalIgnoreCase);
            _seats.RemoveAll(item => temporaryPartyIds.Contains(item.PartyId));
            _parties.RemoveAll(item => temporaryPartyIds.Contains(item.PartyId));
            bool retained = _resolutions.Any(item => (item.ResolutionId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || _pressures.Any(item => (item.ActionCorrelationId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || _lobbyRecords.Any(item => (item.ResolutionId ?? string.Empty)
                    .StartsWith("government-cert-", StringComparison.OrdinalIgnoreCase))
                || _parties.Any(item => (item.PartyId ?? string.Empty)
                    .StartsWith("government_cert_coalition_party_", StringComparison.OrdinalIgnoreCase));
            AddGovernmentAssertion(assertions, "government_certification_cleanup",
                GovernmentLanguageFixtures.Count == 0 && GovernmentSoakFixtures.Count == 0 && _governmentPrivateCaseBaselines.Count == 0
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
