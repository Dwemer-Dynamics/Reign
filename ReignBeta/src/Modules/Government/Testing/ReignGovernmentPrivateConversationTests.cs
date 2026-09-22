using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private readonly Dictionary<string, GovernmentPrivateCaseBaseline> _governmentPrivateCaseBaselines =
            new Dictionary<string, GovernmentPrivateCaseBaseline>(StringComparer.Ordinal);

        // First invocation observes a real open case/member and retains a baseline. The ordinary
        // individual-chat flow must then produce consent; re-running observes that exact new receipt.
        // This harness never fabricates a reply, chooses an RNG roll, grants funds or fulfills a deal.
        private void GovernmentNativeLobbyCase(JArray assertions, Kingdom kingdom,
            ReignGovernmentStateRecord state, string caseId)
        {
            string method = caseId == "GOV-NATIVE-008" ? "persuasion" : caseId == "GOV-NATIVE-009" ? "bribe" : "deal";
            if (kingdom == null || state == null || kingdom.Leader != Hero.MainHero)
            {
                AddGovernmentAssertion(assertions, "government_native_lobbying_production", false,
                    "This case requires the exact enrolled player's current government and a real private conversation.", null);
                return;
            }
            bool newlyPrepared = !_governmentPrivateCaseBaselines.TryGetValue(caseId, out var baseline);
            if (newlyPrepared)
            {
                var pair = (from business in GetBusiness(kingdom.StringId)
                    from seat in GetSeats(kingdom.StringId)
                    where CanDiscussGovernmentBusinessPrivately(business.BusinessId, seat.HeroStringId, out _)
                    let member = FindHero(seat.HeroStringId)
                    where member != null && member != Hero.MainHero
                    orderby business.BusinessId, seat.HeroStringId
                    select new { business, member }).FirstOrDefault();
                if (pair == null)
                {
                    AddGovernmentAssertion(assertions, "government_native_lobbying_production", false,
                        "Arrange a real open hearing and a physically available seated member through the ordinary production flow before preparing this case.", null);
                    return;
                }
                baseline = new GovernmentPrivateCaseBaseline
                {
                    BusinessId = pair.business.BusinessId, MemberId = pair.member.StringId,
                    RulerId = Hero.MainHero.StringId, KingdomId = kingdom.StringId,
                    PlayerGold = Hero.MainHero.Gold, MemberGold = pair.member.Gold,
                    Influence = Clan.PlayerClan?.Influence ?? 0f,
                    ExistingReceipts = new HashSet<string>(_governmentCommitments.Select(x => x.ReceiptId), StringComparer.Ordinal)
                };
                _governmentPrivateCaseBaselines[caseId] = baseline;
            }
            Hero target = FindHero(baseline.MemberId);
            var businessRecord = _business.FirstOrDefault(x => x.BusinessId == baseline.BusinessId);
            var option = businessRecord == null ? null : BusinessOptions(businessRecord).FirstOrDefault();
            if (target == null || businessRecord == null || option == null || baseline.RulerId != Hero.MainHero.StringId
                || baseline.KingdomId != kingdom.StringId)
            {
                _governmentPrivateCaseBaselines.Remove(caseId);
                AddGovernmentAssertion(assertions, "government_native_lobbying_production", false,
                    "The prepared conversation's member, ruler or matter changed; prepare a new exact baseline.", null);
                return;
            }

            JObject reserved = BuildGovernmentPrivateConversationContext(target,
                "government-cert-reservation-guard-" + Guid.NewGuid().ToString("N"),
                "This harness turn contains no agreement, payment or promise.");
            string reservedId = reserved.Value<string>("receiptId") ?? "";
            int beforeCount = _governmentCommitments.Count, playerBeforeGuard = Hero.MainHero.Gold, memberBeforeGuard = target.Gold;
            bool improperlyRecorded;
            try
            {
                improperlyRecorded = RecordGovernmentCommitment(target, Hero.MainHero, businessRecord.BusinessId,
                    option.Value<string>("id"), "persuasion", reservedId, 0, "", out _);
            }
            finally { if (reservedId.Length > 0) _governmentConversationTurns.Remove(reservedId); }
            bool directBlocked = reserved.Value<bool?>("enabled") == true && !improperlyRecorded
                && _governmentCommitments.Count == beforeCount && Hero.MainHero.Gold == playerBeforeGuard && target.Gold == memberBeforeGuard;
            AddGovernmentAssertion(assertions, "government_private_reserved_context_cannot_mint_consent", directBlocked,
                "A genuinely reserved private context without an accepted response cannot create a commitment or transfer gold through the public Record method.",
                new JObject { ["businessId"] = baseline.BusinessId, ["memberId"] = baseline.MemberId, ["contextReserved"] = reservedId.Length > 0 });

            var receipt = _governmentCommitments.LastOrDefault(x => !baseline.ExistingReceipts.Contains(x.ReceiptId)
                && x.BusinessId == baseline.BusinessId && x.MemberHeroStringId == baseline.MemberId
                && x.RulerHeroStringId == baseline.RulerId && x.Method == method);
            bool scope = receipt != null && !string.IsNullOrWhiteSpace(receipt.SessionId)
                && !string.IsNullOrWhiteSpace(receipt.PlayerConsentQuote) && !string.IsNullOrWhiteSpace(receipt.MemberConsentQuote)
                && BusinessOptions(businessRecord).Any(x => x.Value<string>("id") == receipt.OptionId);
            bool payment = receipt != null && (method == "bribe"
                ? receipt.GoldPaid > 0 && baseline.PlayerGold - Hero.MainHero.Gold == receipt.GoldPaid
                    && target.Gold - baseline.MemberGold == receipt.GoldPaid
                    && GovernmentPrivateConversationEligibility.ContainsExactGold(receipt.PlayerConsentQuote, receipt.GoldPaid)
                    && GovernmentPrivateConversationEligibility.ContainsExactGold(receipt.MemberConsentQuote, receipt.GoldPaid)
                : receipt.GoldPaid == 0 && Hero.MainHero.Gold == baseline.PlayerGold && target.Gold == baseline.MemberGold);
            bool obligationComplete = method != "deal" || receipt != null && _resolutions.Any(x => x.ResolutionId == receipt.ObligationId && x.Status == "completed");
            int actualShift = receipt == null ? 0 : GetGovernmentCommitmentShift(receipt.BusinessId, receipt.MemberHeroStringId, receipt.OptionId);
            int expectedShift = method == "bribe" ? 25 : method == "deal" ? 20 : 15;
            bool replaySafe = false;
            if (scope)
            {
                int count = _governmentCommitments.Count, playerGold = Hero.MainHero.Gold, memberGold = target.Gold;
                bool repeat = RecordGovernmentCommitment(target, Hero.MainHero, receipt.BusinessId, receipt.OptionId,
                    receipt.Method, receipt.ReceiptId, receipt.GoldPaid, receipt.ObligationId, out _);
                bool different = RecordGovernmentCommitment(target, Hero.MainHero, receipt.BusinessId, "unoffered-option",
                    receipt.Method, receipt.ReceiptId, receipt.GoldPaid, receipt.ObligationId, out _);
                replaySafe = repeat && !different && count == _governmentCommitments.Count
                    && Hero.MainHero.Gold == playerGold && target.Gold == memberGold;
            }
            bool passed = !newlyPrepared && directBlocked && scope && payment && obligationComplete && replaySafe
                && actualShift == expectedShift && Math.Abs((Clan.PlayerClan?.Influence ?? 0f) - baseline.Influence) < 0.01f;
            AddGovernmentAssertion(assertions, "government_native_lobbying_production", passed,
                passed ? "A new ordinary private conversation produced the exact scoped receipt, payment or actually completed obligation, bounded political shift and replay-safe result."
                    : "Preparation alone is not acceptance: use ordinary Individual Chat with this exact member and matter, complete any promised obligation through production, then rerun this case. No provider acceptance or native effect was fabricated.",
                new JObject { ["method"] = method, ["preparedThisInvocation"] = newlyPrepared,
                    ["businessId"] = baseline.BusinessId, ["memberId"] = baseline.MemberId, ["rulerId"] = baseline.RulerId,
                    ["receipt"] = receipt == null ? null : JObject.FromObject(receipt), ["scopeVerified"] = scope,
                    ["paymentVerified"] = payment, ["obligationCompleted"] = obligationComplete,
                    ["actualShift"] = actualShift, ["expectedShift"] = expectedShift, ["replaySafe"] = replaySafe });
            if (passed) _governmentPrivateCaseBaselines.Remove(caseId);
        }

        private sealed class GovernmentPrivateCaseBaseline
        {
            public string BusinessId;
            public string MemberId;
            public string RulerId;
            public string KingdomId;
            public int PlayerGold;
            public int MemberGold;
            public float Influence;
            public HashSet<string> ExistingReceipts;
        }
    }
}
