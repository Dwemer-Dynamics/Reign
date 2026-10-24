using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using ReignBeta.Actions;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public static string CaucusSessionLabel(GovernmentPartyCaucus caucus)
        {
            double calendarDay = caucus.TargetInterval % 2 == 1
                ? (caucus.TargetInterval / 2) * GovernmentSessionRules.SeasonDays + 14
                : (caucus.TargetInterval / 2 - 1) * GovernmentSessionRules.SeasonDays + 29;
            if (!ReignCalendarService.TryFromAbsoluteCalendarDay(calendarDay, out CampaignTime time)
                || !ReignCalendarService.TryGetCalendarPosition(time, out int year, out _, out int season, out int day, out _)) return "Awaiting calendar";
            return new[] { "Spring", "Summer", "Autumn", "Winter" }[season] + " " + (day + 1) + ", " + year;
        }

        private List<string> CurrentCaucusMembers(GovernmentPartyCaucus caucus) => GetSeats(caucus.KingdomId)
            .Where(x => x.PartyId == caucus.PartyId && GetParticipationAccess(caucus.KingdomId, FindHero(x.HeroStringId)).Member
                && FindHero(x.HeroStringId)?.IsPrisoner == false).Select(x => x.HeroStringId).ToList();

        public bool CheckNominationSupport(string actorId, string petitionId, long revision, string caucusId, out string reason)
        {
            reason = "Select an open nomination in this member's own frozen party caucus.";
            var caucus = _participation.Caucuses.FirstOrDefault(x => GovernmentParticipationRules.CaucusId(x) == caucusId);
            if (caucus == null || caucus.Closed
                || !caucus.Roll.Contains(actorId) || !CurrentCaucusMembers(caucus).Contains(actorId) || !caucus.Nominations.Contains(petitionId)) return false;
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId && x.KingdomId == caucus.KingdomId);
            reason = "The exact disclosed nomination revision must still be awaiting selection with valid sponsorship.";
            if (petition == null || petition.Revision != revision || revision < 1 || petition.Status != "party_consideration"
                || petition.BusinessId.Length > 0 || !GovernmentParticipationRules.CanRead(petition, actorId)) return false;
            if (!petition.Consents.Any(c => c.Operation == "sponsor" && c.Revision == revision
                && caucus.TargetInterval < c.ExpiresInterval && GetParticipationAccess(petition.KingdomId, FindHero(c.ActorId)).Member))
            {
                reason = "Renew a current member's sponsorship through " + CaucusSessionLabel(caucus) + " before seeking nomination votes.";
                return false;
            }
            if (!ValidatePetitionTerms(petition, out reason)) return false;
            reason = ""; return true;
        }

        private List<string> CheckNominationAgreement(ActionClause clause)
        {
            if (!long.TryParse(ReignActionService.Arg(clause, "petitionRevision"), NumberStyles.None, CultureInfo.InvariantCulture, out long revision))
                return new List<string> { "The exact nomination revision is required." };
            string caucusId = ReignActionService.Arg(clause, "caucusId"), petitionId = ReignActionService.Arg(clause, "petitionId");
            if (!CheckNominationSupport(clause.ActorId, petitionId, revision, caucusId, out string reason)) return new List<string> { reason };
            bool conflict = GovernmentTerms(clause.ActorId, true).Any(c => c.Id != clause.Id && c.ActionId == "government.support_nomination"
                && ReignActionService.Arg(c, "caucusId") == caucusId
                && (ReignActionService.Arg(c, "petitionId") != petitionId || ReignActionService.Arg(c, "petitionRevision") != ReignActionService.Arg(clause, "petitionRevision")));
            return conflict ? new List<string> { "This member is already bound to a different nomination in that party selection. Resolve the existing agreement first." } : new List<string>();
        }

        private void ApplyNominationAgreement(ActionClause clause)
        {
            var caucus = _participation.Caucuses.Single(x => GovernmentParticipationRules.CaucusId(x) == ReignActionService.Arg(clause, "caucusId"));
            if (!GovernmentParticipationRules.EndorseNomination(caucus, clause.ActorId, ReignActionService.Arg(clause, "petitionId"),
                CurrentCaucusMembers(caucus), long.Parse(ReignActionService.Arg(clause, "petitionRevision"), CultureInfo.InvariantCulture)))
                throw new InvalidOperationException("The promised nomination preference could not be recorded.");
        }

        private bool ObserveNominationAgreement(ActionClause clause)
        {
            string caucusId = ReignActionService.Arg(clause, "caucusId"), petitionId = ReignActionService.Arg(clause, "petitionId");
            long.TryParse(ReignActionService.Arg(clause, "petitionRevision"), out long revision);
            var caucus = _participation.Caucuses.FirstOrDefault(x => GovernmentParticipationRules.CaucusId(x) == caucusId);
            // Selection-time evidence precedes present office/party/terms checks. A
            // losing motion still fulfills a supporter who cast the promised vote.
            return GovernmentParticipationRules.ObserveNominationVote(caucus, caucusId, clause.ActorId, petitionId, revision,
                CheckNominationSupport(clause.ActorId, petitionId, revision, caucusId, out _));
        }

        private ActionClause AgreedCaucusNomination(GovernmentPartyCaucus caucus, string actorId) => GovernmentTerms(actorId)
            .FirstOrDefault(c => c.ActionId == "government.support_nomination" && ReignActionService.Arg(c, "caucusId") == GovernmentParticipationRules.CaucusId(caucus));
    }
}
