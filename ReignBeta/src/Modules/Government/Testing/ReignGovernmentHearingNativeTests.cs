using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Election;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private bool GovernmentNativeResolveSeasonalFixture(ReignGovernmentResolutionRecord resolution,
            string optionId, Kingdom kingdom, int fixtureLevel, out string result)
        {
            var state = GetGovernment(kingdom);
            result = "No government exists for the seasonal fixture.";
            if (state == null) return false;
            int originalLevel = state.Level;
            state.Level = fixtureLevel;
            try
            {
                ImportSeasonalBusiness();
                var business = _business.FirstOrDefault(x => x.Kind == "seasonal" && x.TargetId == resolution.ResolutionId);
                return business != null && RecommendBusiness(business.BusinessId, optionId, kingdom.Leader, out result)
                    && VoteBusiness(business.BusinessId, kingdom.Leader, out result)
                    && ResolveBusiness(business.BusinessId, optionId, kingdom.Leader,
                        business.WinningOptionId != optionId, out result);
            }
            finally { state.Level = originalLevel; }
        }

        private void GovernmentNativeAuthorityCase(JArray assertions, Kingdom kingdom,
            ReignGovernmentStateRecord state, string caseId)
        {
            if (kingdom == null || state == null || kingdom.Leader != Hero.MainHero || GetSeats(kingdom.StringId).Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_authority_production", false,
                    "The exact enrolled player-ruler kingdom needs eligible individual voters.", null);
                return;
            }
            int level = caseId == "GOV-NATIVE-011" ? 1 : caseId == "GOV-NATIVE-012" ? 2
                : caseId == "GOV-NATIVE-013" ? 3 : caseId == "GOV-NATIVE-014" || caseId == "GOV-NATIVE-015" ? 4 : 5;
            bool favorable = caseId == "GOV-NATIVE-014" || caseId == "GOV-NATIVE-017";
            state.Level = level;
            foreach (var party in GetParties(kingdom.StringId))
                party.PlanksCsv = favorable ? "RoyalAuthority,MilitaryStrength" : "Peace,PopularWelfare";
            foreach (var seat in GetSeats(kingdom.StringId))
            {
                Hero member = FindHero(seat.HeroStringId);
                if (member == null || member == kingdom.Leader) continue;
                int desired = favorable ? 100 : -100;
                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(kingdom.Leader, member,
                    desired - member.GetRelation(kingdom.Leader), false);
            }
            int legacyTrust = state.Trust;
            var action = new ReignWorldActionRecord { ActionId = "government-cert-authority-" + caseId + "-" + Guid.NewGuid().ToString("N"),
                Type = ReignWorldActionType.DiplomacyDeclareWar, ActorHeroStringId = kingdom.Leader.StringId,
                ActorClanStringId = kingdom.RulingClan.StringId, ActorKingdomStringId = kingdom.StringId,
                Reason = "Government hearing authority fixture; this case does not execute a war." };
            bool firstAllowed = TryAuthorizeAction(action, out ReignActionResult held);
            var business = _business.SingleOrDefault(x => x.ActionCorrelationId == action.ActionId);
            bool caseCreated = !firstAllowed && held != null && business != null && business.Status == "hearing";
            bool recommended = business != null && RecommendBusiness(business.BusinessId, "accept", kingdom.Leader, out _);
            bool voted = recommended && VoteBusiness(business.BusinessId, kingdom.Leader, out _);
            string voteStatus = business?.Status ?? "";
            bool opposed = business?.WinningOptionId == "reject";
            bool immediate = level != 3 || opposed && business.IsUrgent && voteStatus == "reconsideration"
                && Math.Abs(business.ReconsiderUntilDay - CurrentDay()) < 0.01f;
            bool resolved;
            if (level == 5)
            {
                resolved = voted && !ResolveBusiness(business.BusinessId, "accept", kingdom.Leader, true, out _)
                    && (favorable ? business.Status == "authorized" && business.WinningOptionId == "accept"
                        : business.Status == "decided" && business.ExecutedOptionId == "reject" && opposed);
            }
            else
            {
                bool explicitOverride = !opposed || level < 3 || !ResolveBusiness(business.BusinessId, "accept", kingdom.Leader, false, out _);
                resolved = voted && explicitOverride && ResolveBusiness(business.BusinessId, "accept", kingdom.Leader, opposed, out _)
                    && business.Status == "authorized";
            }
            bool canExecute = TryAuthorizeAction(action, out ReignActionResult after);
            int caseCount = _business.Count(x => x.ActionCorrelationId == action.ActionId);
            TryAuthorizeAction(action, out _);
            var votes = JArray.Parse(business?.VotesJson ?? "[]").OfType<JObject>().ToArray();
            bool passed = caseCreated && recommended && voted && immediate && resolved && state.Trust == legacyTrust
                && votes.Length > 0 && votes.Select(x => x.Value<string>("memberHeroId")).Distinct().Count() == votes.Length
                && caseCount == 1 && _business.Count(x => x.ActionCorrelationId == action.ActionId) == 1
                && canExecute == (level != 5 || favorable);
            AddGovernmentAssertion(assertions, "government_native_authority_production", passed,
                "The production action queue creates one held hearing, records the recommendation and individual ballots, respects urgent reconsideration and permitted overrides, and binds maximum authority to the vote. Authorization is distinct from execution; this test starts no war.",
                new JObject { ["caseId"] = caseId, ["level"] = level, ["initiallyHeld"] = !firstAllowed,
                    ["business"] = business == null ? null : JObject.FromObject(business), ["voteStage"] = voteStatus,
                    ["immediateReconsideration"] = immediate, ["canExecuteOriginalAction"] = canExecute,
                    ["legacyTrustUnchanged"] = state.Trust == legacyTrust, ["nativeWarExecutedByHarness"] = false });
        }

        private void GovernmentNativeDecisionCase(JArray assertions, Kingdom kingdom)
        {
            var state = EnsureGovernment(kingdom);
            if (state == null || kingdom?.RulingClan == null || GetSeats(kingdom.StringId).Count == 0)
            {
                AddGovernmentAssertion(assertions, "government_native_decision_level_five_enforced", false,
                    "Native policy integration requires a real kingdom and occupied government seats.", null);
                return;
            }
            int originalLevel = state.Level;
            state.Level = 5;
            try
            {
                var policy = MBObjectManager.Instance.GetObjectTypeList<PolicyObject>().Where(x => x != null)
                    .Where(x => !_business.Any(b => b.KingdomStringId == kingdom.StringId && b.Kind == "policy"
                        && b.TargetId == x.StringId && !GovernmentBusinessRules.IsClosed(b.Status)))
                    .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
                if (policy == null)
                {
                    AddGovernmentAssertion(assertions, "government_native_decision_level_five_enforced", false, "No native policy is available.", null);
                    return;
                }
                bool activeBefore = kingdom.HasPolicy(policy);
                var decision = new KingdomPolicyDecision(kingdom.RulingClan, policy, activeBefore);
                decision.IsEnforced = true;
                int countBefore = _business.Count;
                kingdom.AddDecision(decision, true);
                var business = _business.FirstOrDefault(x => x.NativeDecisionIndex >= 0
                    && x.NativeDecisionIndex < _ownedNativeDecisions.Count && ReferenceEquals(_ownedNativeDecisions[x.NativeDecisionIndex], decision));
                bool captured = business != null && _business.Count == countBefore + 1 && !kingdom.UnresolvedDecisions.Contains(decision)
                    && !decision.NotifyPlayer && !decision.NeedsPlayerResolution && decision.PlayerExamined && business.IsRepeal == activeBefore;
                bool repeatedCapture = TryCaptureNativeDecision(decision) && _business.Count == countBefore + 1;
                bool bypassBlocked = !CanApplyNativeDecision(decision);
                bool proposed = captured && RecommendBusiness(business.BusinessId, "accept", kingdom.Leader, out _);
                bool recess = proposed && PostponeBusiness(business.BusinessId, kingdom.Leader, out _)
                    && Math.Abs(business.RecessUntilDay - CurrentDay() - 7f) < 0.01f
                    && !PostponeBusiness(business.BusinessId, kingdom.Leader, out _)
                    && ReconveneBusiness(business.BusinessId, kingdom.Leader, out _)
                    && !PostponeBusiness(business.BusinessId, kingdom.Leader, out _);
                bool voted = recess && VoteBusiness(business.BusinessId, kingdom.Leader, out _);
                var votes = JArray.Parse(business?.VotesJson ?? "[]").OfType<JObject>().ToArray();
                bool activeAfter = kingdom.HasPolicy(policy);
                bool expectedActive = business?.WinningOptionId == "accept" ? !activeBefore : activeBefore;
                long revision = business?.Revision ?? 0;
                bool reentryBlocked = business != null && !VoteBusiness(business.BusinessId, kingdom.Leader, out _)
                    && !ResolveBusiness(business.BusinessId, "reject", kingdom.Leader, true, out _)
                    && !RecommendBusiness(business.BusinessId, "reject", kingdom.Leader, out _)
                    && business.Revision == revision && kingdom.HasPolicy(policy) == activeAfter;
                bool passed = captured && repeatedCapture && bypassBlocked && recess && voted && votes.Length > 0
                    && business.Status == "decided" && business.ExecutionApplied && business.ConsequencesApplied
                    && business.ExecutedOptionId == business.WinningOptionId && activeAfter == expectedActive && reentryBlocked;
                AddGovernmentAssertion(assertions, "government_native_decision_level_five_enforced", passed,
                    "A real native policy event is captured without native election controls, survives duplicate capture, takes only one seven-day recess, and applies the individual binding vote once. Repeated vote, recommendation and ruler veto cannot replay its effects.",
                    new JObject { ["policyId"] = policy.StringId, ["activeBefore"] = activeBefore, ["activeAfter"] = activeAfter,
                        ["captured"] = captured, ["duplicateCaptureSuppressed"] = repeatedCapture, ["directNativeApplyBlocked"] = bypassBlocked,
                        ["enforcedNativeDecision"] = decision.IsEnforced, ["nativeNotifyPlayer"] = decision.NotifyPlayer,
                        ["nativeNeedsPlayerResolution"] = decision.NeedsPlayerResolution,
                        ["singleRecess"] = recess, ["reentryBlocked"] = reentryBlocked,
                        ["business"] = business == null ? null : JObject.FromObject(business), ["nativeUiVisualEvidenceRequiredSeparately"] = true });
                var missingNative = new ReignGovernmentBusinessRecord { KingdomStringId = kingdom.StringId,
                    Kind = "policy", TargetId = policy.StringId, NativeDecisionIndex = -1,
                    OptionsJson = "[{\"id\":\"accept\",\"affirmative\":true},{\"id\":\"reject\",\"affirmative\":false}]" };
                _business.Add(missingNative);
                bool invalidVote = VoteBusiness(missingNative.BusinessId, kingdom.Leader, out string invalidReason);
                AddGovernmentAssertion(assertions, "government_native_missing_decision_invalidates_without_effects",
                    !invalidVote && missingNative.Status == "invalidated" && !missingNative.ExecutionApplied
                    && !missingNative.ConsequencesApplied && kingdom.HasPolicy(policy) == activeAfter,
                    "The production vote path cancels a case whose saved native decision reference is absent without policy changes or rejection consequences.",
                    new JObject { ["businessId"] = missingNative.BusinessId, ["status"] = missingNative.Status, ["reason"] = invalidReason });
            }
            finally { state.Level = originalLevel; }
        }
    }
}
