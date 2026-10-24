using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Government
{
    internal sealed class GovernmentResidenceLease
    {
        public string HeroId = "";
        public string KingdomId = "";
        public string HostSettlementId = "";
        public string ConstituencySettlementId = "";
        public bool Released;
    }

    // Physical presence at a hearing is not a change of constituency. Only an
    // authenticated, still-owned attendance record can override native residence.
    internal static class GovernmentAttendanceResidencePolicy
    {
        public static bool Qualifies(string heroId, string kingdomId, string constituencyId,
            string currentSettlementId, bool nativeResident, IEnumerable<GovernmentResidenceLease> leases)
        {
            if (string.IsNullOrWhiteSpace(heroId) || string.IsNullOrWhiteSpace(kingdomId)
                || string.IsNullOrWhiteSpace(constituencyId)) return false;
            var visits = (leases ?? Enumerable.Empty<GovernmentResidenceLease>()).Where(x => x != null
                && !x.Released && x.HeroId == heroId && !string.IsNullOrWhiteSpace(x.HostSettlementId)
                && x.HostSettlementId == currentSettlementId).ToList();
            if (visits.Count == 0) return nativeResident;
            // Multiple simultaneous hearings must agree. Unknown origins and a
            // foreign attendance record cannot manufacture a new host-city seat.
            if (visits.Any(x => x.KingdomId != kingdomId || string.IsNullOrWhiteSpace(x.ConstituencySettlementId))) return false;
            var homes = visits.Select(x => x.ConstituencySettlementId).Distinct(StringComparer.Ordinal).ToList();
            return homes.Count == 1 && homes[0] == constituencyId;
        }
    }

    internal enum GovernmentAttendanceReturnAction { Release, Defer, ReturnHome }

    internal static class GovernmentAttendanceReturnPolicy
    {
        public static GovernmentAttendanceReturnAction Decide(bool wasMoved, bool livingActiveHero,
            bool atHost, bool hasConflictingDuty)
        {
            if (!wasMoved || !livingActiveHero || !atHost) return GovernmentAttendanceReturnAction.Release;
            // Another duty owns the hero's immediate availability, but it does
            // not turn our temporary relocation into a permanent move of home.
            return hasConflictingDuty ? GovernmentAttendanceReturnAction.Defer : GovernmentAttendanceReturnAction.ReturnHome;
        }
    }
}
