using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Election;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public JArray GetGovernmentProposalChoices(string kingdomId)
        {
            var choices = GetPolicyBusinessChoices(kingdomId);
            foreach (var choice in choices.OfType<JObject>()) choice["proposalId"] = "policy:" + choice.Value<string>("policyId");
            var kingdom = FindKingdom(kingdomId);
            if (kingdom?.RulingClan == null) return choices;
            foreach (var settlement in kingdom.Settlements.Where(x => x.IsTown || x.IsCastle))
            {
                var decision = new SettlementClaimantPreliminaryDecision(kingdom.RulingClan, settlement);
                choices.Add(new JObject { ["proposalId"] = "fief:" + settlement.StringId,
                    ["label"] = "Review ownership of " + settlement.Name,
                    ["description"] = "Consider whether " + settlement.OwnerClan?.Name + " should retain this fief. If reassignment is approved, a separate allocation hearing will choose an eligible recipient.",
                    ["available"] = decision.IsAllowed() && !_business.Any(x => Same(x.KingdomStringId, kingdomId) && Same(x.TargetId, settlement.StringId)
                        && (x.Kind == "fief_allocation" || x.Kind == "fief_reassignment") && !GovernmentBusinessRules.IsClosed(x.Status)) });
            }
            foreach (var clan in kingdom.Clans.Where(x => x != kingdom.RulingClan && !x.IsUnderMercenaryService))
            {
                var decision = new ExpelClanFromKingdomDecision(kingdom.RulingClan, clan);
                choices.Add(new JObject { ["proposalId"] = "expel:" + clan.StringId, ["label"] = "Consider expelling " + clan.Name,
                    ["description"] = "Consider whether this clan should remain in the realm. Its leader will be recorded as an affected participant.",
                    ["available"] = decision.IsAllowed() && !_business.Any(x => Same(x.KingdomStringId, kingdomId) && x.Kind == "expulsion"
                        && Same(x.TargetId, clan.StringId) && !GovernmentBusinessRules.IsClosed(x.Status)) });
            }
            return choices;
        }

        public bool CreateGovernmentBusiness(string kingdomId, string proposalId, Hero actor, out string businessId, out string result)
        {
            if (proposalId?.StartsWith("policy:", StringComparison.Ordinal) == true)
                return CreatePolicyBusiness(kingdomId, proposalId.Substring(7), actor, out businessId, out result);
            businessId = string.Empty;
            var kingdom = FindKingdom(kingdomId);
            if (actor == null || actor != kingdom?.Leader || EnsureGovernment(kingdom) == null)
            { result = "Only this realm's ruler may introduce this government recommendation."; return false; }
            var choice = GetGovernmentProposalChoices(kingdomId).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == proposalId);
            if (choice?.Value<bool>("available") != true) { result = "That proposal is no longer available."; return false; }
            KingdomDecision decision;
            if (proposalId.StartsWith("fief:", StringComparison.Ordinal))
                decision = new SettlementClaimantPreliminaryDecision(kingdom.RulingClan, kingdom.Settlements.First(x => x.StringId == proposalId.Substring(5)));
            else if (proposalId.StartsWith("expel:", StringComparison.Ordinal))
                decision = new ExpelClanFromKingdomDecision(kingdom.RulingClan, kingdom.Clans.First(x => x.StringId == proposalId.Substring(6)));
            else { result = "Unknown government proposal."; return false; }
            if (!TryCaptureNativeDecision(decision)) { result = "The government could not register the proposal."; return false; }
            var record = _business.First(x => NativeBusinessDecision(x) == decision);
            businessId = record.BusinessId;
            return RecommendBusiness(businessId, "accept", actor, out result);
        }

        public JArray GetPolicyBusinessChoices(string kingdomId)
        {
            Kingdom kingdom = FindKingdom(kingdomId);
            if (kingdom == null || EnsureGovernment(kingdom) == null) return new JArray();
            return new JArray(PolicyObject.All.OrderBy(x => x.Name.ToString(), StringComparer.CurrentCulture).Select(policy =>
                new JObject { ["policyId"] = policy.StringId,
                    ["label"] = (kingdom.HasPolicy(policy) ? "Repeal " : "Enact ") + policy.Name,
                    ["description"] = policy.Description + "\n" + policy.SecondaryEffects,
                    ["isRepeal"] = kingdom.HasPolicy(policy),
                    ["available"] = !_business.Any(x => x.Kind == "policy" && Same(x.KingdomStringId, kingdomId)
                        && Same(x.TargetId, policy.StringId) && !GovernmentBusinessRules.IsClosed(x.Status)) }));
        }

        public bool CreatePolicyBusiness(string kingdomId, string policyId, Hero actor, out string businessId, out string result)
        {
            businessId = string.Empty;
            Kingdom kingdom = FindKingdom(kingdomId);
            if (actor == null || actor != kingdom?.Leader || EnsureGovernment(kingdom) == null)
            { result = "Only this realm's ruler may introduce a government policy recommendation."; return false; }
            PolicyObject policy = PolicyObject.All.FirstOrDefault(x => Same(x.StringId, policyId));
            if (policy == null) { result = "That policy is no longer available."; return false; }
            var existing = _business.FirstOrDefault(x => x.Kind == "policy" && Same(x.KingdomStringId, kingdomId)
                && Same(x.TargetId, policyId) && !GovernmentBusinessRules.IsClosed(x.Status));
            if (existing != null)
            { businessId = existing.BusinessId; result = "This policy already has a pending hearing."; return false; }
            var decision = new KingdomPolicyDecision(kingdom.RulingClan, policy, kingdom.HasPolicy(policy));
            if (!TryCaptureNativeDecision(decision)) { result = "The government could not register this policy hearing."; return false; }
            var record = _business.First(x => NativeBusinessDecision(x) == decision);
            businessId = record.BusinessId;
            return RecommendBusiness(businessId, "accept", actor, out result);
        }
    }
}
