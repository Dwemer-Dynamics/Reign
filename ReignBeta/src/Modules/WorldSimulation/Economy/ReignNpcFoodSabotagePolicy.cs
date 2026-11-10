#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Reign.Shared;

namespace ReignBeta.Economy
{
    internal sealed class NpcFoodSabotageState
    {
        public int Version = ReignNpcFoodSabotagePolicy.Version;
        public int LastProcessedDay = -1;
        public long TotalStrikes = 0;
        public List<NpcFoodSabotageStrike> Recent = new List<NpcFoodSabotageStrike>();
    }

    internal sealed class NpcFoodSabotageStrike
    {
        public string SponsorId, TargetId, TownId, VillageId;
        public int Day;
        public bool Ended;
    }

    internal sealed class NpcFoodSabotageTarget
    {
        internal string SponsorId, TargetId, TownId, VillageId;
        internal bool Preferred;
    }

    internal static class ReignNpcFoodSabotagePolicy
    {
        internal const int Version = 1;
        internal const int ChanceBasisPoints = 1200;
        internal const int WindowDays = 30;
        internal const int RealmLimit = 3;
        internal const int TownActiveLimit = 2;
        internal const int DurationDays = 10;
        internal const int ReceiptLimit = 512;
        internal const float FoodFactor = -0.20f;

        internal static bool HasChance(string campaignId, int day, string sponsorId)
            => Roll(campaignId, day, "attempt", sponsorId) % ReignKingdomEventRollerCore.BasisPointScale < ChanceBasisPoints;

        private static uint Roll(string campaignId, int day, string purpose, string id)
            => ReignKingdomEventRollerCore.StableHash(campaignId, day, "npc-food-sabotage-v1|" + purpose + "|" + id);

        internal static void Advance(NpcFoodSabotageState state, string campaignId, int day,
            IEnumerable<NpcFoodSabotageTarget> targets)
        {
            if (day <= state.LastProcessedDay || string.IsNullOrEmpty(campaignId)) return;
            state.LastProcessedDay = day;
            state.Recent.RemoveAll(x => (long)x.Day + WindowDays <= day);
            foreach (var sponsor in targets.GroupBy(x => x.SponsorId, StringComparer.Ordinal)
                .OrderBy(x => Roll(campaignId, day, "sponsor-order", x.Key)).ThenBy(x => x.Key, StringComparer.Ordinal))
            {
                if (state.Recent.Count >= ReceiptLimit) break;
                if (!HasChance(campaignId, day, sponsor.Key) || state.Recent.Count(x => x.SponsorId == sponsor.Key) >= RealmLimit)
                    continue;

                var enemy = sponsor.Where(t => state.Recent.Count(x => x.TargetId == t.TargetId) < RealmLimit
                    && !state.Recent.Any(x => x.VillageId == t.VillageId)
                    && state.Recent.Count(x => x.TownId == t.TownId && WithinEffect(x, day)) < TownActiveLimit)
                    .GroupBy(t => t.TargetId, StringComparer.Ordinal)
                    .OrderBy(x => Roll(campaignId, day, "enemy|" + sponsor.Key, x.Key)).ThenBy(x => x.Key, StringComparer.Ordinal)
                    .FirstOrDefault();
                var target = enemy?.OrderByDescending(t => t.Preferred)
                    .ThenBy(t => Roll(campaignId, day, "village|" + sponsor.Key, t.VillageId))
                    .ThenBy(t => t.VillageId, StringComparer.Ordinal).FirstOrDefault();
                if (target == null) continue;

                state.Recent.Add(new NpcFoodSabotageStrike { SponsorId = sponsor.Key, TargetId = target.TargetId,
                    TownId = target.TownId, VillageId = target.VillageId, Day = day });
                state.TotalStrikes++;
            }
        }

        private static bool WithinEffect(NpcFoodSabotageStrike strike, int day)
            => !strike.Ended && day >= strike.Day && (long)day < (long)strike.Day + DurationDays;

        internal static bool IsActive(NpcFoodSabotageStrike strike, int day, string playerRealmId,
            string ownerRealmId, string boundRealmId, string boundTownId)
            => WithinEffect(strike, day) && strike.SponsorId != playerRealmId && strike.TargetId != playerRealmId
                && ScopeMatches(strike, ownerRealmId, boundRealmId, boundTownId);

        internal static bool ScopeMatches(NpcFoodSabotageStrike strike, string ownerRealmId, string boundRealmId, string boundTownId)
            => strike.TargetId == ownerRealmId && strike.TargetId == boundRealmId && strike.TownId == boundTownId;

        // Kingdom events and existing Spymaster effects already share one additive production clamp.
        internal static float BoundFoodDelta(float production, float existingFactor, float sabotageFactor)
            => production * (Math.Max(-1f, existingFactor + sabotageFactor) - Math.Max(-1f, existingFactor));
    }
}
