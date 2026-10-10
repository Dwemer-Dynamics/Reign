using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        // A read-only selector must not import or mutate government business.
        internal IReadOnlyList<ReignGovernmentBusinessRecord> AgreementBusinesses => _business;

        internal List<string> CheckAgreementClause(ActionClause c)
        {
            if (!IsMemberAgreement(c)) return CheckGovernmentCommand(c);
            var errors = new List<string>();
            var member = ReignActionService.HeroById(c.ActorId);
            var seat = _seats.FirstOrDefault(s => s.HeroStringId == c.ActorId);
            if (member == null || seat == null) { errors.Add("This person does not hold a government seat."); return errors; }
            if (c.ActionId == "government.support_vote")
            {
                var business = _business.FirstOrDefault(b => b.BusinessId == ReignActionService.Arg(c, "businessId") && b.KingdomStringId == seat.KingdomStringId);
                if (business == null || business.Status != "hearing" && business.Status != "recess" && business.Status != "postponed") errors.Add("This proposal is no longer awaiting a vote.");
                else if (!BusinessOptions(business).Any(o => o.Value<string>("id") == ReignActionService.Arg(c, "optionId"))) errors.Add("That exact voting option is unavailable.");
                string existing = GovernmentTerms(c.ActorId, true).Where(x => x.Id != c.Id && x.ActionId == "government.support_vote"
                    && ReignActionService.Arg(x, "businessId") == business?.BusinessId).Select(x => ReignActionService.Arg(x, "optionId")).FirstOrDefault() ?? "";
                if (existing.Length > 0 && existing != ReignActionService.Arg(c, "optionId")) errors.Add("This member is already bound to a different vote on that proposal.");
            }
            else
            {
                string partyId = ReignActionService.Arg(c, "partyId");
                if (!_parties.Any(p => p.PartyId == partyId && p.KingdomStringId == seat.KingdomStringId)) errors.Add("Select a party in this member's own government.");
                string boundParty = GovernmentTerms(c.ActorId, true).Where(x => x.Id != c.Id && x.ActionId == "government.back_platform")
                    .Select(x => ReignActionService.Arg(x, "partyId")).FirstOrDefault() ?? "";
                if (c.ActionId == "government.back_platform" && boundParty.Length > 0 && boundParty != partyId) errors.Add("An existing platform commitment must finish first.");
            }
            return errors;
        }

        internal void ApplyAgreementClause(string agreementId, ActionClause clause, AgreementStep step)
        {
            if (!IsMemberAgreement(clause)) { ApplyGovernmentCommand(clause, step); return; }
            var errors = CheckAgreementClause(clause);
            if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            if (clause.ActionId == "government.join_party")
            {
                var seat = _seats.Single(s => s.HeroStringId == clause.ActorId);
                seat.PartyId = ReignActionService.Arg(clause, "partyId"); seat.Revision++;
                var kingdom = FindKingdom(seat.KingdomStringId);
                RecalculatePartyState(kingdom, EnsureGovernment(kingdom));
                if (seat.PartyId != ReignActionService.Arg(clause, "partyId")) throw new InvalidOperationException("Party membership did not persist.");
            }
            // Promises bind as soon as their saved step starts. Payment waits for actual
            // recorded membership, ballot, or the end of the promised platform term.
        }

        internal bool ObserveAgreementClause(ActionClause clause, AgreementStep step)
        {
            if (!IsMemberAgreement(clause)) return ObserveGovernmentCommand(clause, step);
            var member = ReignActionService.HeroById(clause.ActorId);
            var seat = _seats.FirstOrDefault(s => s.HeroStringId == clause.ActorId);
            if (member == null || seat == null) throw new InvalidOperationException("The promised government member is no longer eligible.");
            if (clause.ActionId == "government.join_party")
            {
                if (seat.PartyId != ReignActionService.Arg(clause, "partyId")) throw new InvalidOperationException("The promised party membership no longer matches.");
                return true;
            }
            if (clause.ActionId == "government.back_platform")
            {
                if (!_parties.Any(p => p.PartyId == ReignActionService.Arg(clause, "partyId") && p.KingdomStringId == seat.KingdomStringId))
                    throw new InvalidOperationException("The promised political party no longer exists in this government.");
                return CurrentDay() >= step.StartedDay + PlatformDays(clause);
            }
            var business = _business.FirstOrDefault(b => b.BusinessId == ReignActionService.Arg(clause, "businessId"));
            if (business == null || business.KingdomStringId != seat.KingdomStringId)
                throw new InvalidOperationException("The promised proposal is no longer available to this member.");
            var ballot = JArray.Parse(business.VotesJson ?? "[]").OfType<JObject>().FirstOrDefault(v => v.Value<string>("memberHeroId") == clause.ActorId);
            if (ballot != null)
            {
                if (ballot.Value<string>("optionId") != ReignActionService.Arg(clause, "optionId"))
                    throw new InvalidOperationException("The recorded ballot does not fulfill the promised vote.");
                return true;
            }
            if (business.Status != "hearing" && business.Status != "recess" && business.Status != "postponed")
                throw new InvalidOperationException("The proposal closed without the promised ballot.");
            return false;
        }

        private static double PlatformDays(ActionClause clause) => clause.DurationDays > 0 ? clause.DurationDays
            : double.Parse(ReignActionService.Arg(clause, "durationDays"), System.Globalization.CultureInfo.InvariantCulture);

        private static bool IsMemberAgreement(ActionClause clause) => clause.ActionId == "government.join_party"
            || clause.ActionId == "government.back_platform" || clause.ActionId == "government.support_vote";

        private IEnumerable<ActionClause> GovernmentTerms(string memberId, bool includePending = false)
        {
            var behavior = ReignAgreementBehavior.Instance;
            if (behavior == null) yield break;
            foreach (var journal in behavior.Agreements.OrderByDescending(j => j.Agreement.CreatedDay))
            {
                if (journal.CommittedHash != journal.Agreement.TermsHash()
                    || journal.Agreement.State == AgreementState.Cancelled || journal.Agreement.State == AgreementState.Impossible) continue;
                foreach (var clause in journal.Agreement.Clauses.Where(c => c.ActorId == memberId && c.ActionId.StartsWith("government.", StringComparison.Ordinal)))
                {
                    var step = journal.Steps.FirstOrDefault(s => s.ClauseId == clause.Id);
                    if (step == null || !step.Reserved || step.Returned || !includePending && !step.Started) continue;
                    if (clause.ActionId == "government.back_platform" && step.Started && CurrentDay() >= step.StartedDay + PlatformDays(clause)) continue;
                    yield return clause;
                }
            }
        }
        private string AgreedVoteOption(string businessId, string memberId) => GovernmentTerms(memberId)
            .Where(c => c.ActionId == "government.support_vote" && ReignActionService.Arg(c, "businessId") == businessId)
            .Select(c => ReignActionService.Arg(c, "optionId")).FirstOrDefault() ?? "";
        private string AgreedPlatformParty(string memberId) => GovernmentTerms(memberId)
            .Where(c => c.ActionId == "government.back_platform").Select(c => ReignActionService.Arg(c, "partyId")).FirstOrDefault() ?? "";
    }
}
