#if !REIGN_EXCLUDE_COURT
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        public bool HasConflictingGovernmentAttendanceDuty(Hero hero, bool requiresRelocation = true)
        {
            if (hero == null) return true;
            string id = hero.StringId;
            return (requiresRelocation && !Chancellor.IsVacant && Chancellor.HeroId == id)
                || (_rulerDocketState?.NobleMatters?.Any(x => x.IsPending && x.Participants.Any(p => p.HeroId == id)) ?? false)
                || (_rulerDocketState?.CourtStayLeases?.Any(x => !x.Released && x.HeroId == id) ?? false);
        }
    }
}
#endif
