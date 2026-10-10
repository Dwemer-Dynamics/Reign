#nullable enable
using System;

namespace ReignBeta.Campaign
{
    // Native Clan and CompanionOf maintain different caches. A civilian assigned
    // only to Clan is neither a lord nor a companion after native cache rebuilds.
    internal static class ReignResidentCompanionRegistration
    {
        internal static void Ensure<TClan>(TClan playerClan, Func<TClan?> clan, Action<TClan?> setFamilyClan,
            Func<TClan?> companionOf, Action addCompanion, Func<bool> inCompanionRoster,
            Func<bool> inHeroRoster, Action rebuildCompanionCache,
            Func<bool> isWanderer, Action setWanderer) where TClan : class
        {
            if (playerClan == null || !ReferenceEquals(clan(), playerClan))
                throw new InvalidOperationException("Only an existing player-clan resident can be repaired.");
            // Keep existing membership until native registration commits. A native
            // event can throw after setting CompanionOf; retry keeps that progress.
            if (!ReferenceEquals(companionOf(), playerClan)) addCompanion();
            if (!ReferenceEquals(companionOf(), playerClan) || !inCompanionRoster())
                throw new InvalidOperationException("Native companion registration is incomplete.");
            // Clan now resolves through CompanionOf, so clearing the old family
            // field cannot produce a temporary clan departure.
            setFamilyClan(null);
            // PreAfterLoad omits non-lords from the family cache. OnLordRemoved
            // nevertheless removes one Heroes entry, which can be the new companion.
            // Rebuild through the native property only when that entry is absent;
            // do not fire a second recruitment action/event.
            if (!inHeroRoster()) rebuildCompanionCache();
            if (!ReferenceEquals(companionOf(), playerClan) || !inCompanionRoster() || !inHeroRoster())
                throw new InvalidOperationException("Native clan hero caches are incomplete.");
            if (!isWanderer()) setWanderer();
            if (!isWanderer())
                throw new InvalidOperationException("The resident retained their former native occupation.");
        }
    }
}
