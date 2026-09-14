using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Integration;
using ReignBeta.UI;
using ReignBeta.UI.Calibration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private JObject NobleDocketPreflight(string runId, string saveName)
        {
            List<Hero> candidates = EligibleNobleMatterHeroes(CurrentDay(), 0);
            int protectedVictims = candidates.Count(IsProtectedMurderVictim);
            int suitableUnmarriedPairs = CountSuitableUnmarriedPairs(candidates);
            int reciprocalSpousePairs = candidates.Count(x => x.Spouse != null
                && candidates.Contains(x.Spouse)) / 2;
            JArray assertions = new JArray
            {
                Assertion("player_is_current_ruler", Clan.PlayerClan?.Kingdom?.Leader == Hero.MainHero),
                Assertion("capital_court_has_royal_command_access", HasRoyalCommandAccess),
                Assertion("four_eligible_nobles_available", candidates.Count >= 4)
            };
            return new JObject
            {
                ["ok"] = assertions.All(x => x.Value<bool?>("passed") == true),
                ["runId"] = runId, ["profile"] = "noble_docket", ["phase"] = "noble_preflight",
                ["saveName"] = saveName, ["eligibleNobles"] = candidates.Count,
                ["protectedMurderVictims"] = protectedVictims,
                ["suitableUnmarriedPairs"] = suitableUnmarriedPairs,
                ["reciprocalSpousePairs"] = reciprocalSpousePairs,
                ["templateCount"] = ReignNobleDocketCatalog.Templates.Count,
                ["specialCapabilities"] = new JObject
                {
                    ["murder"] = protectedVictims >= 1 && candidates.Count >= 5,
                    ["marriage"] = suitableUnmarriedPairs >= 1,
                    ["divorce"] = reciprocalSpousePairs >= 1
                },
                ["assertions"] = assertions
            };
        }

        private static int CountSuitableUnmarriedPairs(IReadOnlyList<Hero> candidates)
        {
            int count = 0;
            for (int i = 0; i < candidates.Count; i++)
                for (int j = i + 1; j < candidates.Count; j++)
                    if (candidates[i].Spouse == null && candidates[j].Spouse == null
                        && TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel
                            ?.IsCoupleSuitableForMarriage(candidates[i], candidates[j]) == true)
                        count++;
            return count;
        }

        private JObject PrepareNobleDocketFixture(string runId, JObject options,
            string saveName, string gameInstanceId)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "noble_prepare",
                    "The exact disposable-save ruler-docket fixture confirmation is required.",
                    saveName, saveName);
            string templateId = (options.Value<string>("templateId") ?? string.Empty).Trim();
            ReignNobleMatterTemplate template = ReignNobleDocketCatalog.Find(templateId);
            if (template == null)
                return RulerDocketTestFailure(runId, "noble_prepare",
                    "templateId must name one of the 68 authoritative noble docket templates.",
                    saveName, saveName);
            if (!HasRoyalCommandAccess)
                return RulerDocketTestFailure(runId, "noble_prepare",
                    "Hold production Court in the capital before preparing a noble matter.",
                    saveName, saveName);

            ReignRulerDocketState state = EnsureRulerDocketState();
            state.NobleMatters.RemoveAll(x => x?.MatterId?.StartsWith(
                "noble_test_" + runId + "_", StringComparison.Ordinal) == true);
            int preparedDay = CurrentDay();
            int eligibilitySelectionSlot = StableCourtOrder(
                (runId ?? string.Empty) + "|noble-fixture-participants", preparedDay) % 2048;
            List<Hero> candidates = EligibleNobleMatterHeroes(preparedDay,
                eligibilitySelectionSlot);
            JObject fixtureState = PrepareSpecialNobleEligibility(template, candidates);
            if (fixtureState.Value<bool?>("ok") != true)
                return RulerDocketTestFailure(runId, "noble_prepare",
                    fixtureState.Value<string>("error") ?? "The required noble eligibility could not be prepared.",
                    saveName, saveName);

            ReignNobleDocketMatter matter = SelectDistinctNobleFixtureVariant(template,
                ReignCampaignIdentity.CurrentCampaignId(),
                ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                preparedDay, runId, out int participantSelectionSlot,
                out int participantVariantIndex, out int participantVariantCount);
            if (matter == null)
                return RulerDocketTestFailure(runId, "noble_prepare",
                    "Production candidate selection could not build this template from the disposable campaign.",
                    saveName, saveName);
            string expectedInvestigation = (options.Value<string>(
                "expectedInvestigationOutcome") ?? "any").Trim().ToLowerInvariant();
            matter.MatterId = ChooseNobleTestMatterId(runId, matter,
                expectedInvestigation);
            matter.CanonicalTruthHash = ReignCourtTerms.Hash(matter.MatterId + "|" + matter.CanonicalTruth);
            state.NobleMatters.Add(matter);

            JObject marker = new JObject
            {
                ["runId"] = runId, ["fixtureType"] = "noble_docket",
                ["matterId"] = matter.MatterId, ["templateId"] = template.Id,
                ["action"] = NormalizeNobleFixtureAction(options.Value<string>("action"), template),
                ["preparedDay"] = preparedDay, ["saveName"] = saveName,
                ["gameInstanceId"] = gameInstanceId ?? string.Empty,
                ["eligibilitySelectionSlot"] = eligibilitySelectionSlot,
                ["participantSelectionSlot"] = participantSelectionSlot,
                ["participantVariantIndex"] = participantVariantIndex,
                ["participantVariantCount"] = participantVariantCount,
                ["victimHeroId"] = matter.VictimHeroId,
                ["actualCulpritHeroId"] = matter.ActualCulpritHeroId,
                ["expectedAcceptanceTier"] = options.Value<int?>("expectedAcceptanceTier") ?? -1,
                ["expectedInvestigationOutcome"] = expectedInvestigation,
                ["eligibilityFixture"] = fixtureState
            };
            StoreRulerDocketMarker(marker);
            return new JObject
            {
                ["ok"] = matter.Participants.Count >= template.MinimumPrincipals
                    && matter.Participants.Count <= 4
                    && (!matter.IsMurder || matter.Evidence.Any(x => x.Reliable && x.CompleteChain)),
                ["runId"] = runId, ["profile"] = "noble_docket", ["phase"] = "noble_prepare",
                ["saveName"] = saveName, ["matter"] = NobleMatterTestJson(matter),
                ["participantSelection"] = new JObject
                {
                    ["strategy"] = "fixture_run_distinct_production_variant",
                    ["slot"] = participantSelectionSlot,
                    ["eligibilitySlot"] = eligibilitySelectionSlot,
                    ["selectedVariantIndex"] = participantVariantIndex,
                    ["availableDistinctVariantCount"] = participantVariantCount,
                    ["fixtureRunId"] = runId,
                    ["diversity"] = NobleParticipantDiversityJson(matter)
                },
                ["requiresBaselineRollback"] = true,
                ["productionCandidateSelection"] = true
            };
        }

        private ReignNobleDocketMatter SelectDistinctNobleFixtureVariant(
            ReignNobleMatterTemplate template, string campaignId, string timelineId,
            int day, string runId, out int selectedSlot, out int selectedVariantIndex,
            out int availableVariantCount)
        {
            const int MaximumProductionSlots = 2048;
            const int MaximumDistinctVariants = 64;
            var variants = new List<KeyValuePair<int, ReignNobleDocketMatter>>();
            var participantSignatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int slot = 0; slot < MaximumProductionSlots
                && variants.Count < MaximumDistinctVariants; slot++)
            {
                ReignNobleDocketMatter candidate = TryBuildNobleMatter(template,
                    campaignId, timelineId, day, slot);
                if (candidate == null) continue;
                string signature = string.Join("|", candidate.Participants.Select(
                    participant => participant.Role + ":" + participant.HeroId));
                if (!participantSignatures.Add(signature)) continue;
                variants.Add(new KeyValuePair<int, ReignNobleDocketMatter>(slot, candidate));
            }

            availableVariantCount = variants.Count;
            if (availableVariantCount == 0)
            {
                selectedSlot = -1;
                selectedVariantIndex = -1;
                return null;
            }

            int selectionHash = StableCourtOrder((runId ?? string.Empty)
                + "|noble-fixture-distinct-variant", day);
            selectedVariantIndex = selectionHash % availableVariantCount;
            selectedSlot = variants[selectedVariantIndex].Key;
            return variants[selectedVariantIndex].Value;
        }

        private JObject PrepareSpecialNobleEligibility(ReignNobleMatterTemplate template,
            List<Hero> candidates)
        {
            if (template.RequiresSpouses)
            {
                Hero spouse = candidates.FirstOrDefault(x => x.Spouse != null
                    && candidates.Contains(x.Spouse));
                if (spouse == null)
                    return new JObject { ["ok"] = false,
                        ["error"] = "No eligible reciprocal noble marriage exists on this campaign." };
                Hero other = spouse.Spouse;
                int before = spouse.GetRelation(other);
                if (before > -15)
                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(spouse, other,
                        -15 - before, false);
                return new JObject { ["ok"] = spouse.GetRelation(other) <= -15,
                    ["kind"] = "divorce_disposition", ["heroAId"] = spouse.StringId,
                    ["heroBId"] = other.StringId, ["beforeRelation"] = before,
                    ["afterRelation"] = spouse.GetRelation(other) };
            }
            if (template.RequiresUnmarriedPair)
            {
                for (int i = 0; i < candidates.Count; i++)
                    for (int j = i + 1; j < candidates.Count; j++)
                    {
                        Hero first = candidates[i], second = candidates[j];
                        if (first.Spouse != null || second.Spouse != null
                            || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel
                                ?.IsCoupleSuitableForMarriage(first, second) != true)
                            continue;
                        if (template.RequiresClanLeader && new[] { first.Clan?.Leader, second.Clan?.Leader }
                            .All(x => x == null || x == first || x == second || !x.IsAlive || x.IsPrisoner))
                            continue;
                        int before = first.GetRelation(second);
                        if (template.RequiresLoverAffinity && before < 10)
                            ChangeRelationAction.ApplyRelationChangeBetweenHeroes(first, second,
                                10 - before, false);
                        return new JObject { ["ok"] = !template.RequiresLoverAffinity
                                || first.GetRelation(second) >= 10,
                            ["kind"] = template.RequiresLoverAffinity ? "lover_affinity" : "marriage_suitability",
                            ["heroAId"] = first.StringId, ["heroBId"] = second.StringId,
                            ["beforeRelation"] = before, ["afterRelation"] = first.GetRelation(second) };
                    }
                return new JObject { ["ok"] = false,
                    ["error"] = "No native-suitable unmarried pair satisfies this template." };
            }
            return new JObject { ["ok"] = true, ["kind"] = "none" };
        }

        private JObject OpenNobleDocketFixture(string runId, string saveName)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            bool opened = ReignCourtPetitionScreenManager.TryOpenForAutomation(this, matter,
                out string error);
            JObject marker = FindRulerDocketMarker(runId);
            if (marker != null)
            {
                marker["victimDeadAfterOpen"] = matter?.IsMurder == true
                    && FindAnyHero(matter.VictimHeroId)?.IsAlive == false;
                StoreRulerDocketMarker(marker);
            }
            return new JObject
            {
                ["ok"] = opened && matter?.ActivationCommitted == true
                    && (!matter.IsMurder || FindAnyHero(matter.VictimHeroId)?.IsAlive == false),
                ["runId"] = runId, ["profile"] = "noble_docket", ["phase"] = "noble_open",
                ["saveName"] = saveName, ["matter"] = NobleMatterTestJson(matter),
                ["productionUiOpen"] = opened, ["error"] = error,
                ["murderCommittedOnOpen"] = matter?.IsMurder == true
                    && FindAnyHero(matter.VictimHeroId)?.IsAlive == false
            };
        }

        private JObject SnapshotNobleDocketFixture(string runId, string saveName)
        {
            bool saved = ReignUiCalibrationService.TrySaveSnapshot(
                "ReignCourtPetitionScreen", out string path, out string error);
            return new JObject { ["ok"] = saved, ["runId"] = runId,
                ["profile"] = "noble_docket", ["phase"] = "noble_snapshot",
                ["saveName"] = saveName, ["snapshotPath"] = path ?? string.Empty,
                ["error"] = error ?? string.Empty };
        }

        private JObject DecideNobleDocketFixture(string runId, JObject options, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            if (marker == null || matter == null)
                return RulerDocketTestFailure(runId, "noble_decide",
                    "The run-owned noble matter marker is missing.", saveName, saveName);
            string action = NormalizeNobleFixtureAction(options.Value<string>("action")
                ?? marker.Value<string>("action"), ReignNobleDocketCatalog.Find(matter.TemplateId));
            string automationAction = action;
            if (action == "convict-correct") automationAction = "convict:" + matter.ActualCulpritHeroId;
            if (action == "convict-wrong")
                automationAction = "convict:" + matter.Participants
                    .First(x => x.HeroId != matter.ActualCulpritHeroId).HeroId;
            bool executed = ReignCourtPetitionScreenManager.TryExecuteAutomationAction(
                automationAction, out string error);
            marker["action"] = action;
            marker["decidedState"] = matter.State.ToString();
            marker["convictedHeroId"] = matter.ConvictedHeroId;
            StoreRulerDocketMarker(marker);
            bool expected = action == "defer"
                ? matter.State == ReignNobleMatterState.Deferred
                : action == "acquit-all" ? matter.State == ReignNobleMatterState.Acquitted
                : action == "convict-correct" || action == "convict-wrong"
                    ? matter.State == ReignNobleMatterState.Convicted
                    : matter.State == ReignNobleMatterState.Ruled;
            return new JObject
            {
                ["ok"] = executed && expected, ["runId"] = runId,
                ["profile"] = "noble_docket", ["phase"] = "noble_decide",
                ["saveName"] = saveName, ["action"] = action,
                ["productionViewModelCommand"] = true,
                ["matter"] = NobleMatterTestJson(matter), ["error"] = error
            };
        }

        private JObject ObserveNobleDocketFixture(string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            if (marker == null || matter == null)
                return RulerDocketTestFailure(runId, "noble_observe",
                    "The run-owned noble matter marker is missing.", saveName, saveName);
            string action = marker.Value<string>("action") ?? string.Empty;
            bool history = EnsureRulerDocketState().History.Any(x =>
                x.PetitionId == matter.MatterId && x.Type == "noble_matter"
                && x.Outcome == "judgment");
            bool participantCap = matter.Participants.Count >= 2 && matter.Participants.Count <= 4
                && matter.ActiveAudienceHeroIds.Count <= 4;
            bool demands = matter.DemandsRevealed;
            bool transcript = matter.ConversationLines.Any(x => x.Role == "player")
                && matter.ConversationLines.Count(x => x.Role != "player"
                    && x.Role != "system") >= matter.Participants.Count;
            bool visibleDialogueStatFree = matter.ConversationLines
                .Where(x => x.Role != "player")
                .All(x => !ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(x.Text));
            ReignNobleMatterTemplate template = ReignNobleDocketCatalog.Find(matter.TemplateId);
            bool evidenceGrounded = template?.UsesHiddenTruth != true
                || matter.Evidence.Any(x => x.Reliable && x.Revealed);
            int expectedTier = marker.Value<int?>("expectedAcceptanceTier") ?? -1;
            bool acceptanceTierMatched = expectedTier < 0 || matter.AcceptanceTier == expectedTier;
            bool decision = action == "defer" ? matter.State == ReignNobleMatterState.Deferred
                : matter.EffectsCommitted && !string.IsNullOrWhiteSpace(matter.AppliedEffectsHash);
            bool murderTruth = !matter.IsMurder || marker.Value<bool?>("victimDeadAfterOpen") == true
                && (action == "acquit-all" || action == "defer"
                    || !string.IsNullOrWhiteSpace(matter.ConvictedHeroId));
            JObject familyState = ObserveNobleFamilyState(matter);
            bool familyStateConsistent = familyState.Value<bool?>("consistent") == true;
            JArray expectedReputationEffects = ExpectedNobleReputationEffects(matter);
            JObject relationshipState = ObserveNobleDirectionalRelationshipStateForTest(
                matter.MatterId);
            return new JObject
            {
                ["ok"] = participantCap && demands && transcript && evidenceGrounded
                    && visibleDialogueStatFree && acceptanceTierMatched && decision
                    && murderTruth && familyStateConsistent && (action == "defer" || history),
                ["runId"] = runId, ["profile"] = "noble_docket",
                ["phase"] = "noble_observe", ["saveName"] = saveName,
                ["timelineId"] = matter.TimelineId,
                ["worldDay"] = CurrentDay(),
                ["participantCap"] = participantCap, ["demandsRevealed"] = demands,
                ["naturalConversationPersisted"] = transcript,
                ["visibleDialogueStatFree"] = visibleDialogueStatFree,
                ["evidenceGroundedByVisibleDialogue"] = evidenceGrounded,
                ["acceptanceTierMatched"] = acceptanceTierMatched,
                ["expectedAcceptanceTier"] = expectedTier,
                ["actualAcceptanceTier"] = matter.AcceptanceTier,
                ["judgmentHistoryRecorded"] = history, ["decisionCommitted"] = decision,
                ["murderActivationAndRulingConsistent"] = murderTruth,
                ["nativeFamilyState"] = familyState,
                ["expectedReputationEffects"] = expectedReputationEffects,
                ["directionalRelationshipState"] = relationshipState,
                ["matter"] = NobleMatterTestJson(matter)
            };
        }

        internal JObject ObserveNobleDirectionalRelationshipStateForTest(
            string matterId)
        {
            ReignNobleDocketMatter matter = EnsureRulerDocketState().NobleMatters
                .FirstOrDefault(x => x?.MatterId == matterId);
            if (matter == null)
                return new JObject { ["required"] = false, ["consistent"] = false,
                    ["error"] = "The run-owned noble matter is missing." };

            TryProcessPendingDocketRelationshipAdjustments();
            string prefix = matter.MatterId + "_relation_";
            List<ReignDirectionalRelationAdjustment> actual = EnsureRulerDocketState()
                .PendingRelationAdjustments.Where(x => x != null
                    && (x.AdjustmentId ?? string.Empty).StartsWith(prefix,
                        StringComparison.Ordinal)).ToList();
            List<KeyValuePair<string, int>> expected = ExpectedNobleRelationshipEffects(matter);
            bool exactRulesMatch = actual.Count == expected.Count && expected.All(effect =>
                actual.Count(x => x.ObserverHeroId == effect.Key && x.Delta == effect.Value)
                    == expected.Count(x => x.Key == effect.Key && x.Value == effect.Value));
            bool delivered = actual.All(x => x.Applied
                && !string.IsNullOrWhiteSpace(x.ReceiptId));
            JArray effects = new JArray(actual.OrderBy(x => x.AdjustmentId,
                StringComparer.Ordinal).Select(x => new JObject
                {
                    ["adjustmentId"] = x.AdjustmentId,
                    ["observerHeroId"] = x.ObserverHeroId,
                    ["rulerHeroId"] = x.SubjectHeroId,
                    ["delta"] = x.Delta,
                    ["reason"] = x.Reason,
                    ["applied"] = x.Applied,
                    ["receiptId"] = x.ReceiptId
                }));
            bool required = expected.Count > 0;
            return new JObject
            {
                ["required"] = required,
                ["consistent"] = exactRulesMatch && delivered,
                ["exactRulesMatch"] = exactRulesMatch,
                ["allEffectsDelivered"] = delivered,
                ["expectedEffectCount"] = expected.Count,
                ["actualEffectCount"] = actual.Count,
                ["clanSpilloverEffectCount"] = actual.Count(x =>
                    (x.Reason ?? string.Empty).IndexOf("clan",
                        StringComparison.OrdinalIgnoreCase) >= 0),
                ["effects"] = effects
            };
        }

        private static List<KeyValuePair<string, int>> ExpectedNobleRelationshipEffects(
            ReignNobleDocketMatter matter)
        {
            List<KeyValuePair<string, int>> expected = new List<KeyValuePair<string, int>>();
            if (matter == null)
                return expected;
            if (matter.Ruling == ReignNobleRuling.DeferToChancellor)
            {
                foreach (ReignNobleMatterParticipant principal in matter.Participants
                    .Where(x => x.IsPrincipal).Take(2))
                    AddExpectedRelationship(expected, principal.HeroId, -2);
                return expected;
            }
            if (matter.EffectsCommitted != true || matter.IsMurder
                || matter.Ruling == ReignNobleRuling.AcquitAll)
                return expected;
            int win = ReignRulerDocketRules.NobleWinnerRelation(matter.Severity);
            int loss = ReignRulerDocketRules.AcceptedLossRelation(
                ReignRulerDocketRules.NobleLoserRelation(matter.Severity),
                matter.AcceptanceTier);
            ReignNobleMatterParticipant winner = matter.Participants.FirstOrDefault(x =>
                x.HeroId == matter.RuledForHeroId);
            ReignNobleMatterParticipant loser = matter.Participants.FirstOrDefault(x =>
                x.HeroId == matter.RuledAgainstHeroId);
            if (matter.Category == ReignNobleMatterCategory.Marriage
                && matter.Participants.Any(x => x.Role == "marriage_partner"))
            {
                List<ReignNobleMatterParticipant> couple = matter.Participants.Where(x =>
                    x.Role == "principal_a" || x.Role == "marriage_partner").ToList();
                ReignNobleMatterParticipant leader = matter.Participants.FirstOrDefault(x =>
                    x.Role == "principal_b");
                if (matter.Ruling == ReignNobleRuling.SideA)
                {
                    foreach (ReignNobleMatterParticipant lover in couple)
                        AddExpectedRelationship(expected, lover.HeroId, win);
                    AddExpectedRelationship(expected, leader?.HeroId, loss);
                    AddExpectedClanSpillover(expected, leader?.ClanId, leader?.HeroId,
                        ReignRulerDocketRules.ClanSpilloverRelation(loss));
                }
                else
                {
                    AddExpectedRelationship(expected, leader?.HeroId, win);
                    foreach (ReignNobleMatterParticipant lover in couple)
                        AddExpectedRelationship(expected, lover.HeroId, loss);
                }
                return expected;
            }

            List<ReignNobleMatterParticipant> winners = matter.Participants.Where(x => x.Role ==
                (matter.Ruling == ReignNobleRuling.SideA ? "principal_a" : "principal_b")).ToList();
            List<ReignNobleMatterParticipant> losers = ExpectedRejectedParticipants(matter).ToList();
            foreach (ReignNobleMatterParticipant supported in winners)
                AddExpectedRelationship(expected, supported.HeroId, win);
            foreach (ReignNobleMatterParticipant rejected in losers)
                AddExpectedRelationship(expected, rejected.HeroId, loss);
            foreach (IGrouping<string, ReignNobleMatterParticipant> group in winners
                .Where(supported => losers.Any(rejected => ReignRulerDocketRules.ShouldApplyOpposingClanSpillover(
                    matter.Category, supported.IsClanLeader, rejected.IsClanLeader,
                    supported.ClanId, rejected.ClanId)))
                .GroupBy(x => x.ClanId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            {
                AddExpectedClanSpillover(expected, group.Key, group.First().HeroId,
                    ReignRulerDocketRules.ClanSpilloverRelation(win));
            }
            foreach (IGrouping<string, ReignNobleMatterParticipant> group in losers
                .Where(rejected => winners.Any(supported => ReignRulerDocketRules.ShouldApplyOpposingClanSpillover(
                    matter.Category, supported.IsClanLeader, rejected.IsClanLeader,
                    supported.ClanId, rejected.ClanId)))
                .GroupBy(x => x.ClanId ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                AddExpectedClanSpillover(expected, group.Key, group.First().HeroId,
                    ReignRulerDocketRules.ClanSpilloverRelation(loss));
            return expected;
        }

        private static void AddExpectedRelationship(
            ICollection<KeyValuePair<string, int>> expected, string heroId, int delta)
        {
            if (delta != 0 && !string.IsNullOrWhiteSpace(heroId))
                expected.Add(new KeyValuePair<string, int>(heroId, delta));
        }

        private static void AddExpectedClanSpillover(
            ICollection<KeyValuePair<string, int>> expected, string clanId,
            string principalId, int delta)
        {
            Clan clan = Clan.All.FirstOrDefault(x => x?.StringId == clanId);
            if (clan == null || delta == 0) return;
            foreach (Hero member in clan.Heroes.Where(x => x != null && x.IsAlive
                && x != Hero.MainHero && x.StringId != principalId))
                AddExpectedRelationship(expected, member.StringId, delta);
        }

        private static JArray ExpectedNobleReputationEffects(
            ReignNobleDocketMatter matter)
        {
            JArray expected = new JArray();
            if (matter?.EffectsCommitted != true) return expected;
            if (matter.Category == ReignNobleMatterCategory.Divorce)
            {
                bool granted = (matter.ImmediateTermsJson ?? string.Empty).IndexOf(
                    "divorceHeroAId", StringComparison.OrdinalIgnoreCase) >= 0;
                foreach (ReignNobleMatterParticipant spouse in matter.Participants.Take(2))
                    expected.Add(ReputationExpectation(matter.MatterId,
                        spouse.HeroId, "divorcee", granted));
                return expected;
            }
            if (matter.IsMurder)
            {
                if (matter.State == ReignNobleMatterState.Convicted
                    && !string.IsNullOrWhiteSpace(matter.ConvictedHeroId))
                {
                    expected.Add(ReputationExpectation(matter.MatterId,
                        matter.ConvictedHeroId, "murderer"));
                    expected.Add(ReputationExpectation(matter.MatterId,
                        matter.ConvictedHeroId, "convicted_murderer"));
                }
                return expected;
            }
            ReignNobleMatterTemplate template = ReignNobleDocketCatalog.Find(
                matter.TemplateId);
            if (string.IsNullOrWhiteSpace(matter.RuledAgainstHeroId)) return expected;
            bool rulingEstablishesTags = matter.Ruling == ReignNobleRuling.SideA
                ? template?.NegativeTagsApplyOnSideA == true
                : matter.Ruling == ReignNobleRuling.SideB && template?.NegativeTagsApplyOnSideB == true;
            if (!rulingEstablishesTags) return expected;
            IEnumerable<string> subjects = template?.ApplyNegativeTagsToAllRejectedParticipants == true
                ? ExpectedRejectedParticipants(matter).Select(x => x.HeroId)
                : new[] { matter.RuledAgainstHeroId };
            foreach (string heroId in subjects.Distinct(StringComparer.OrdinalIgnoreCase))
                foreach (string tag in (template?.ApplicableNegativeTags
                        ?? Array.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                    expected.Add(ReputationExpectation(matter.MatterId, heroId, tag));
            return expected;
        }

        private static IEnumerable<ReignNobleMatterParticipant> ExpectedRejectedParticipants(
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

        private static JObject ReputationExpectation(string matterId,
            string heroId, string tagId, bool active = true)
        {
            return new JObject
            {
                ["heroId"] = heroId ?? string.Empty,
                ["tagId"] = tagId ?? string.Empty,
                ["active"] = active,
                ["sourceCorrelationId"] = (matterId ?? string.Empty) + "|judgment|"
                    + (heroId ?? string.Empty) + "|" + (tagId ?? string.Empty)
            };
        }

        private static JObject ObserveNobleFamilyState(ReignNobleDocketMatter matter)
        {
            JObject terms;
            try { terms = string.IsNullOrWhiteSpace(matter?.ImmediateTermsJson)
                ? new JObject() : JObject.Parse(matter.ImmediateTermsJson); }
            catch (JsonException)
            {
                return new JObject { ["required"] = true, ["consistent"] = false,
                    ["error"] = "Committed family terms were not valid typed JSON." };
            }
            string marryAId = terms.Value<string>("marryHeroAId") ?? string.Empty;
            string marryBId = terms.Value<string>("marryHeroBId") ?? string.Empty;
            string divorceAId = terms.Value<string>("divorceHeroAId") ?? string.Empty;
            string divorceBId = terms.Value<string>("divorceHeroBId") ?? string.Empty;
            bool marriageRequired = !string.IsNullOrWhiteSpace(marryAId)
                || !string.IsNullOrWhiteSpace(marryBId);
            bool divorceRequired = !string.IsNullOrWhiteSpace(divorceAId)
                || !string.IsNullOrWhiteSpace(divorceBId);
            ReignNobleMatterParticipant preservedA = matter?.Participants?.FirstOrDefault(
                x => x.Role == "principal_a");
            ReignNobleMatterParticipant preservedB = matter?.Participants?.FirstOrDefault(
                x => x.Role == "principal_b");
            bool deniedDivorcePreservationRequired = matter != null
                && matter.Category == ReignNobleMatterCategory.Divorce
                && matter.Ruling == ReignNobleRuling.SideB && matter.EffectsCommitted;
            Hero marryA = FindAnyHero(marryAId), marryB = FindAnyHero(marryBId);
            Hero divorceA = FindAnyHero(divorceAId), divorceB = FindAnyHero(divorceBId);
            Hero preservedSpouseA = FindAnyHero(preservedA?.HeroId);
            Hero preservedSpouseB = FindAnyHero(preservedB?.HeroId);
            bool marriageConsistent = !marriageRequired || marryA != null && marryB != null
                && marryA.Spouse == marryB && marryB.Spouse == marryA;
            bool divorceConsistent = !divorceRequired || divorceA != null && divorceB != null
                && divorceA.Spouse == null && divorceB.Spouse == null;
            bool deniedDivorcePreserved = !deniedDivorcePreservationRequired
                || preservedSpouseA != null && preservedSpouseB != null
                    && preservedSpouseA.Spouse == preservedSpouseB
                    && preservedSpouseB.Spouse == preservedSpouseA;
            return new JObject
            {
                ["required"] = marriageRequired || divorceRequired
                    || deniedDivorcePreservationRequired,
                ["consistent"] = marriageConsistent && divorceConsistent
                    && deniedDivorcePreserved && !(marriageRequired && divorceRequired)
                    && !(deniedDivorcePreservationRequired
                        && (marriageRequired || divorceRequired)),
                ["marriageRequired"] = marriageRequired,
                ["reciprocalSpouseState"] = marriageRequired && marriageConsistent,
                ["divorceRequired"] = divorceRequired,
                ["reciprocalSpouseStateCleared"] = divorceRequired && divorceConsistent,
                ["deniedDivorcePreservationRequired"] = deniedDivorcePreservationRequired,
                ["reciprocalSpouseStatePreserved"] = deniedDivorcePreservationRequired
                    && deniedDivorcePreserved,
                ["marryHeroAId"] = marryAId, ["marryHeroBId"] = marryBId,
                ["divorceHeroAId"] = divorceAId, ["divorceHeroBId"] = divorceBId,
                ["preservedSpouseAId"] = preservedA?.HeroId ?? string.Empty,
                ["preservedSpouseBId"] = preservedB?.HeroId ?? string.Empty
            };
        }

        private JObject ReverseNobleDocketFixture(string runId, string saveName)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            bool reversed = TryReverseNobleJudgment(matter?.MatterId, out string receipt);
            bool history = matter != null && EnsureRulerDocketState().History.Any(x =>
                x.PetitionId == matter.MatterId && x.Outcome == "reversal");
            return new JObject { ["ok"] = reversed
                    && matter.State == ReignNobleMatterState.Reversed && history,
                ["runId"] = runId, ["phase"] = "noble_reverse",
                ["saveName"] = saveName, ["receipt"] = receipt,
                ["historyRecorded"] = history, ["matter"] = NobleMatterTestJson(matter) };
        }

        private JObject ExecuteNobleDocketSentenceFixture(string runId, string saveName)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            Hero convicted = FindAnyHero(matter?.ConvictedHeroId);
            bool executed = TryExecuteMurderSentence(matter?.MatterId, out string receipt);
            bool dead = convicted != null && !convicted.IsAlive;
            return new JObject { ["ok"] = executed && dead && matter?.Irreversible == true
                    && matter.CustodyHold?.Executed == true,
                ["runId"] = runId, ["phase"] = "noble_execute",
                ["saveName"] = saveName, ["receipt"] = receipt,
                ["nativeDeathCommitted"] = dead, ["irreversible"] = matter?.Irreversible == true,
                ["matter"] = NobleMatterTestJson(matter) };
        }

        private JObject PrepareNobleCourtStayFixture(string runId, string saveName)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            string heroId = matter?.Participants.FirstOrDefault(x =>
                FindAnyHero(x.HeroId)?.IsAlive == true && !FindAnyHero(x.HeroId).IsPrisoner)?.HeroId;
            bool requested = TryRequestCourtStay(matter?.MatterId, heroId, out string receipt);
            ReignCourtStayLease lease = EnsureRulerDocketState().CourtStayLeases
                .LastOrDefault(x => x.MatterId == matter?.MatterId && x.HeroId == heroId);
            JObject marker = FindRulerDocketMarker(runId);
            if (marker != null)
            {
                marker["courtStayHeroId"] = heroId ?? string.Empty;
                marker["courtStayReleaseDay"] = lease?.ReleaseDay ?? -1;
                StoreRulerDocketMarker(marker);
            }
            Hero hero = FindAnyHero(heroId);
            bool atCapital = hero?.CurrentSettlement == CurrentCapital
                || hero?.PartyBelongedTo?.CurrentSettlement == CurrentCapital;
            return new JObject { ["ok"] = requested && lease != null && !lease.Released && atCapital,
                ["runId"] = runId, ["phase"] = "noble_stay_prepare",
                ["saveName"] = saveName, ["receipt"] = receipt,
                ["heroId"] = heroId ?? string.Empty, ["releaseDay"] = lease?.ReleaseDay ?? -1,
                ["atCapital"] = atCapital, ["requiresGuardedAdvancement"] = true };
        }

        private JObject VerifyNobleCourtStayFixture(string runId, string saveName)
        {
            JObject marker = FindRulerDocketMarker(runId);
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            string heroId = marker?.Value<string>("courtStayHeroId") ?? string.Empty;
            ReignCourtStayLease lease = EnsureRulerDocketState().CourtStayLeases
                .LastOrDefault(x => x.MatterId == matter?.MatterId && x.HeroId == heroId);
            int releaseDay = marker?.Value<int?>("courtStayReleaseDay") ?? int.MaxValue;
            return new JObject { ["ok"] = lease?.Released == true && CurrentDay() >= releaseDay,
                ["runId"] = runId, ["phase"] = "noble_stay_verify",
                ["saveName"] = saveName, ["heroId"] = heroId,
                ["releaseDay"] = releaseDay, ["currentDay"] = CurrentDay(),
                ["releasedToNativeAgency"] = lease?.Released == true };
        }

        private JObject VerifyNobleInvestigationFixture(string runId, string saveName)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            ReignChancellorInvestigation investigation = matter?.Investigation;
            bool resolved = investigation?.Resolved == true;
            bool bounded = investigation != null && investigation.SuccessChance >= 0d
                && investigation.SuccessChance <= 0.8d;
            bool outcome = investigation != null && (investigation.Outcome == "correct_conviction"
                || investigation.Outcome == "wrong_conviction"
                || investigation.Outcome == "insufficient_evidence"
                || investigation.Outcome == "side_a" || investigation.Outcome == "side_b");
            JObject marker = FindRulerDocketMarker(runId);
            string expected = marker?.Value<string>("expectedInvestigationOutcome") ?? "any";
            bool expectedMatched = expected == "any"
                || expected == "correct" && investigation?.Outcome == "correct_conviction"
                || expected == "wrong" && investigation?.Outcome == "wrong_conviction"
                || expected == "insufficient" && investigation?.Outcome == "insufficient_evidence";
            return new JObject { ["ok"] = matter != null && resolved && bounded && outcome
                    && expectedMatched
                    && CurrentDay() >= investigation.DueDay,
                ["runId"] = runId, ["phase"] = "noble_investigation_verify",
                ["saveName"] = saveName, ["currentDay"] = CurrentDay(),
                ["dueDay"] = investigation?.DueDay ?? -1,
                ["successChance"] = investigation?.SuccessChance ?? 0d,
                ["successRoll"] = investigation?.SuccessRoll ?? false,
                ["outcome"] = investigation?.Outcome ?? string.Empty,
                ["expectedOutcome"] = expected,
                ["expectedOutcomeMatched"] = expectedMatched,
                ["matter"] = NobleMatterTestJson(matter) };
        }

        private JObject MarkNobleDocketReload(string runId, string saveName,
            string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            if (marker == null || FindNobleDocketTestMatter(runId) == null)
                return RulerDocketTestFailure(runId, "noble_save_prepare",
                    "The run-owned noble matter marker is missing.", saveName, saveName);
            marker["nobleReloadFingerprint"] = NobleDocketFingerprint(runId);
            marker["nobleReloadGameInstanceId"] = gameInstanceId ?? string.Empty;
            StoreRulerDocketMarker(marker);
            return new JObject { ["ok"] = true, ["runId"] = runId,
                ["phase"] = "noble_save_prepare",
                ["fingerprint"] = marker.Value<string>("nobleReloadFingerprint"),
                ["requiresGuardedCheckpointRestart"] = true };
        }

        private JObject VerifyNobleDocketReload(string runId, string saveName,
            string gameInstanceId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            string current = NobleDocketFingerprint(runId);
            bool differentInstance = marker != null && !string.Equals(
                marker.Value<string>("nobleReloadGameInstanceId"), gameInstanceId,
                StringComparison.OrdinalIgnoreCase);
            bool same = marker != null && string.Equals(
                marker.Value<string>("nobleReloadFingerprint"), current,
                StringComparison.OrdinalIgnoreCase);
            return new JObject { ["ok"] = differentInstance && same,
                ["runId"] = runId, ["phase"] = "noble_save_verify",
                ["saveName"] = saveName, ["differentGameInstance"] = differentInstance,
                ["fingerprintMatched"] = same, ["currentFingerprint"] = current };
        }

        private JObject VerifyRoyalProclamationFixture(string runId, JObject options,
            string saveName)
        {
            if (!string.Equals(options.Value<string>("confirmation"),
                    RulerDocketFixtureConfirmation, StringComparison.Ordinal))
                return RulerDocketTestFailure(runId, "royal_proclamation",
                    "The exact disposable-save ruler-docket fixture confirmation is required.",
                    saveName, saveName);
            string exact = options.Value<string>("proclamationText")
                ?? "By our hand, this exact test proclamation enters the realm's public record.";
            int historyBefore = EnsureRulerDocketState().History.Count;
            bool issued = TryIssueRoyalProclamation(exact, out string receipt);
            ReignDocketHistoryRecord record = EnsureRulerDocketState().History.LastOrDefault(x =>
                x.Type == "royal_proclamation" && x.Summary == exact);
            return new JObject { ["ok"] = issued && record != null
                    && EnsureRulerDocketState().History.Count == historyBefore + 1
                    && record.Detail.IndexOf("no direct mechanical effect",
                        StringComparison.OrdinalIgnoreCase) >= 0,
                ["runId"] = runId, ["phase"] = "royal_proclamation",
                ["saveName"] = saveName, ["exactTextPreserved"] = record?.Summary == exact,
                ["historyRecordId"] = record?.RecordId ?? string.Empty,
                ["receipt"] = receipt };
        }

        private ReignNobleDocketMatter FindNobleDocketTestMatter(string runId)
        {
            JObject marker = FindRulerDocketMarker(runId);
            string matterId = marker?.Value<string>("matterId") ?? string.Empty;
            return EnsureRulerDocketState().NobleMatters.LastOrDefault(x =>
                x?.MatterId == matterId || x?.MatterId?.StartsWith(
                    "noble_test_" + runId + "_", StringComparison.Ordinal) == true);
        }

        private static Hero FindAnyHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId)) return null;
            return Hero.AllAliveHeroes.FirstOrDefault(x => x?.StringId == heroId)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(x => x?.StringId == heroId);
        }

        private static string NormalizeNobleFixtureAction(string value,
            ReignNobleMatterTemplate template)
        {
            string action = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            if (string.IsNullOrWhiteSpace(action))
                return template?.IsMurderInvestigation == true ? "acquit-all" : "side-a";
            if (action == "convict") return "convict-correct";
            return action;
        }

        private string ChooseNobleTestMatterId(string runId,
            ReignNobleDocketMatter matter, string expectedInvestigation)
        {
            string prefix = "noble_test_" + runId + "_";
            if (!matter.IsMurder || expectedInvestigation == "any")
                return prefix + Guid.NewGuid().ToString("N");
            Hero chancellor = FindAnyHero(EnsureRulerDocketState().Chancellor?.HeroId);
            int charm = chancellor?.GetSkillValue(DefaultSkills.Charm) ?? 0;
            int leadership = chancellor?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
            int steward = chancellor?.GetSkillValue(DefaultSkills.Steward) ?? 0;
            for (int index = 0; index < 100000; index++)
            {
                string candidate = prefix + index.ToString("D5");
                bool success = ReignRulerDocketRules.ChancellorInvestigationSucceeds(
                    candidate, charm, leadership, steward);
                bool hasPlausibleFailure = StableCourtOrder(
                    candidate + "|chancellor_failure", CurrentDay()) % 2 == 0;
                if (expectedInvestigation == "correct" && success
                    || expectedInvestigation == "wrong" && !success && hasPlausibleFailure
                    || expectedInvestigation == "insufficient" && !success && !hasPlausibleFailure)
                    return candidate;
            }
            throw new InvalidOperationException(
                "No bounded deterministic matter id produced the requested Chancellor result.");
        }

        private string NobleDocketFingerprint(string runId)
        {
            ReignNobleDocketMatter matter = FindNobleDocketTestMatter(runId);
            JObject value = new JObject
            {
                ["matter"] = matter == null ? null : JObject.FromObject(matter),
                ["history"] = new JArray(EnsureRulerDocketState().History
                    .Where(x => x.PetitionId == matter?.MatterId).Select(x => JObject.FromObject(x))),
                ["courtStay"] = new JArray(EnsureRulerDocketState().CourtStayLeases
                    .Where(x => x.MatterId == matter?.MatterId).Select(x => JObject.FromObject(x)))
            };
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(
                    value.ToString(Formatting.None)))).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static JObject NobleMatterTestJson(ReignNobleDocketMatter matter)
        {
            if (matter == null) return null;
            return new JObject
            {
                ["matterId"] = matter.MatterId, ["templateId"] = matter.TemplateId,
                ["title"] = matter.Title, ["severity"] = matter.Severity.ToString(),
                ["category"] = matter.Category.ToString(), ["state"] = matter.State.ToString(),
                ["participantCount"] = matter.Participants.Count,
                ["participantIds"] = new JArray(matter.Participants.Select(x => x.HeroId)),
                ["participants"] = NobleParticipantDiversityJson(matter)["participants"],
                ["demandsRevealed"] = matter.DemandsRevealed,
                ["evidenceCount"] = matter.Evidence.Count,
                ["revealedEvidenceCount"] = matter.Evidence.Count(x => x.Revealed),
                ["conversationLineCount"] = matter.ConversationLines.Count,
                ["conversationLines"] = new JArray(matter.ConversationLines.Select(x =>
                    new JObject
                    {
                        ["speaker"] = x.Speaker,
                        ["role"] = x.Role,
                        ["text"] = x.Text,
                        ["forbiddenStatistics"] = x.Role != "player"
                            && ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(x.Text)
                    })),
                ["truthHash"] = matter.CanonicalTruthHash,
                ["activationCommitted"] = matter.ActivationCommitted,
                ["effectsCommitted"] = matter.EffectsCommitted,
                ["effectsHash"] = matter.AppliedEffectsHash,
                ["ruling"] = matter.Ruling.ToString(),
                ["convictedHeroId"] = matter.ConvictedHeroId,
                ["victimHeroId"] = matter.VictimHeroId,
                ["custodyActive"] = matter.CustodyHold?.Active == true,
                ["irreversible"] = matter.Irreversible,
                ["decisionSummary"] = matter.DecisionSummary
            };
        }

        private static JObject NobleParticipantDiversityJson(ReignNobleDocketMatter matter)
        {
            JArray participants = new JArray((matter?.Participants
                ?? new List<ReignNobleMatterParticipant>()).Select(participant =>
            {
                Hero hero = FindAnyHero(participant.HeroId);
                return new JObject
                {
                    ["heroId"] = participant.HeroId,
                    ["heroName"] = participant.HeroName,
                    ["role"] = participant.Role,
                    ["clanId"] = participant.ClanId,
                    ["cultureId"] = hero?.Culture?.StringId ?? string.Empty,
                    ["isFemale"] = hero?.IsFemale ?? false,
                    ["age"] = hero != null ? (double)hero.Age : 0d,
                    ["occupation"] = hero?.Occupation.ToString() ?? string.Empty,
                    ["isClanLeader"] = participant.IsClanLeader,
                    ["isMarried"] = hero?.Spouse != null
                };
            }));
            List<Hero> heroes = (matter?.Participants
                ?? new List<ReignNobleMatterParticipant>())
                .Select(x => FindAnyHero(x.HeroId)).Where(x => x != null).ToList();
            return new JObject
            {
                ["participants"] = participants,
                ["distinctHeroCount"] = participants.Count,
                ["distinctClanCount"] = heroes.Select(x => x.Clan?.StringId ?? string.Empty)
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                ["femaleCount"] = heroes.Count(x => x.IsFemale),
                ["maleCount"] = heroes.Count(x => !x.IsFemale),
                ["marriedCount"] = heroes.Count(x => x.Spouse != null),
                ["clanLeaderCount"] = heroes.Count(x => x.Clan?.Leader == x)
            };
        }
    }
}
