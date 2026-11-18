using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using ReignBeta.Court.WarCouncil;

namespace ReignBeta.Court
{
    public static class ReignCourtOfficeService
    {
        public static bool IsEligible(Hero hero, ReignCourtAuthority authority, IEnumerable<CourtOfficeAssignment> assignments, out string reason)
        {
            reason = string.Empty;
            if (authority != ReignCourtAuthority.Royal) { reason = "Court offices require royal authority."; return false; }
            if (hero == null || !hero.IsAlive || !hero.IsActive) { reason = "The candidate is not a living active hero."; return false; }
            if (hero.IsPrisoner) { reason = "A captive cannot hold court office."; return false; }
            if (hero.Age < TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge) { reason = "The candidate has not reached adulthood."; return false; }
            if (assignments != null && assignments.Any(x => x != null && x.IsActive && x.HeroStringId == hero.StringId)) { reason = "One person may hold only one major office."; return false; }
            if (string.Equals(ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId, hero.StringId, StringComparison.OrdinalIgnoreCase))
            { reason = "The War Councilor must be distinct from every other Royal Council seat."; return false; }

            bool playerClan = hero.Clan == Clan.PlayerClan;
            bool companion = hero.CompanionOf == Clan.PlayerClan;
            bool generatedCourtier = ReignCourtNobleCampaignBehavior.IsCourtNoble(hero)
                && (hero.Clan == Clan.PlayerClan || authority == ReignCourtAuthority.Royal && hero.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);
            bool royalVassal = authority == ReignCourtAuthority.Royal && hero.Clan?.Kingdom != null && hero.Clan.Kingdom == Clan.PlayerClan?.Kingdom;
            if (!playerClan && !companion && !generatedCourtier && !royalVassal)
            {
                reason = "The candidate is outside this court's eligible household and vassal pool.";
                return false;
            }
            return true;
        }

        public static bool TryAssign(List<CourtOfficeAssignment> assignments, ReignCourtOffice office, Hero hero, ReignCourtAuthority authority,
            string commandId, long expectedRevision, float day, out CourtOfficeAssignment result, out string error)
        {
            assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            result = assignments.FirstOrDefault(x => x != null && x.IsActive && x.Office == office);
            CourtOfficeAssignment existing = result;
            if (existing != null && existing.LastCommandId == commandId) { error = string.Empty; return true; }
            if (existing != null && existing.Revision != expectedRevision) { error = "The office changed after this view was loaded."; return false; }
            if (!IsEligible(hero, authority, assignments.Where(x => x != existing), out error)) return false;

            if (result != null)
            {
                result.IsActive = false;
                result.DismissedDay = day;
                result.DismissalReason = "replaced";
                result.Revision++;
                result.LastCommandId = commandId ?? string.Empty;
            }
            result = new CourtOfficeAssignment
            {
                Office = office,
                HeroStringId = hero.StringId,
                AssignedDay = day,
                Revision = 1,
                LastCommandId = commandId ?? string.Empty
            };
            assignments.Add(result);
            error = string.Empty;
            return true;
        }

        public static bool TryDismiss(List<CourtOfficeAssignment> assignments, ReignCourtOffice office, string commandId, long expectedRevision,
            float day, string reason, out string error)
        {
            CourtOfficeAssignment active = assignments?.FirstOrDefault(x => x != null && x.IsActive && x.Office == office);
            if (active == null) { error = "That office is already vacant."; return false; }
            if (active.LastCommandId == commandId) { error = string.Empty; return true; }
            if (active.Revision != expectedRevision) { error = "The office changed after this view was loaded."; return false; }
            active.IsActive = false;
            active.DismissedDay = day;
            active.DismissalReason = reason ?? "dismissed";
            active.LastCommandId = commandId ?? string.Empty;
            active.Revision++;
            error = string.Empty;
            return true;
        }
    }
}
