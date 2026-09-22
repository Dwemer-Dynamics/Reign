using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Campaign
{
    internal static class ReignBusinessOwnershipRelease
    {
        // Native ownership operations remove entries from the source collection.
        // Snapshot before executing, and verify each result before proceeding.
        // A later retry sees only the assets that still belong to the recruit.
        internal static int ReleaseAll<T>(IEnumerable<T> ownedAssets, Func<T, bool> stillOwned, Action<T> transfer)
        {
            int released = 0;
            foreach (T asset in ownedAssets.ToArray())
            {
                if (!stillOwned(asset)) continue;
                transfer(asset);
                if (stillOwned(asset))
                    throw new InvalidOperationException("Native business transfer retained the recruited owner.");
                released++;
            }
            if (ownedAssets.Any(stillOwned))
                throw new InvalidOperationException("Business ownership changed during recruitment; cleanup must resume.");
            return released;
        }
    }
}
