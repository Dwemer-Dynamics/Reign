using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;

namespace ReignBeta.Government
{
    /// <summary>A completed ballot remains evidence after its voter leaves office.</summary>
    internal static class GovernmentVoteEvidence
    {
        internal static bool Observe(ReignGovernmentBusinessRecord business, string businessId,
            string memberId, string optionId, bool currentlyEligible)
        {
            if (business == null || string.IsNullOrWhiteSpace(businessId) || business.BusinessId != businessId
                || string.IsNullOrWhiteSpace(memberId) || string.IsNullOrWhiteSpace(optionId))
                throw new InvalidOperationException("The promised proposal is no longer available to this member.");

            var ballots = JArray.Parse(business.VotesJson ?? "[]").OfType<JObject>()
                .Where(v => v.Value<string>("memberHeroId") == memberId).ToList();
            if (ballots.Count > 1)
                throw new InvalidOperationException("The recorded ballot evidence is ambiguous.");
            if (ballots.Count == 1)
            {
                if (ballots[0].Value<string>("optionId") != optionId)
                    throw new InvalidOperationException("The recorded ballot does not fulfill the promised vote.");
                return true;
            }
            if (!GovernmentBusinessRules.CanRecommend(business.Status))
                throw new InvalidOperationException("The proposal closed without the promised ballot.");
            if (!currentlyEligible)
                throw new InvalidOperationException("The promised government member is no longer eligible.");
            return false;
        }
    }
}
