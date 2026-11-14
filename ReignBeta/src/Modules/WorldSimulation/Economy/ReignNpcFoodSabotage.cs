using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    internal static class ReignNpcFoodSabotage
    {
        private static NpcFoodSabotageState State => ReignEconomyCampaignBehavior.Instance?.EconomyState.NpcFoodSabotage;
        private static string PlayerRealmId => Clan.PlayerClan?.Kingdom?.StringId;
        private static int Day => (int)Math.Floor(CampaignTime.Now.ToDays);

        internal static void Tick(int day)
        {
            var state = State;
            if (state == null || day <= state.LastProcessedDay) return;

            // Once a holding leaves the target realm, its old effect cannot return on recapture.
            foreach (var strike in state.Recent.Where(x => !x.Ended && (long)x.Day + ReignNpcFoodSabotagePolicy.DurationDays > day))
                RetireIfOwnershipChanged(strike);

            var playerRealm = Clan.PlayerClan?.Kingdom;
            var realms = Kingdom.All.Where(k => !k.IsEliminated && k != playerRealm && k.Leader != Hero.MainHero).ToArray();
            var villages = Village.All.Where(v => v.VillageState == Village.VillageStates.Normal
                    && v.Bound?.IsTown == true && v.Bound.Town != null && !v.Bound.Town.IsUnderSiege
                    && v.Settlement.OwnerClan?.Kingdom != null && v.Settlement.OwnerClan.Kingdom == v.Bound.OwnerClan?.Kingdom)
                .ToLookup(v => v.Settlement.OwnerClan.Kingdom);
            ReignNpcFoodSabotagePolicy.Advance(state, TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId, day,
                Targets(realms, villages));
        }

        private static IEnumerable<NpcFoodSabotageTarget> Targets(Kingdom[] realms, ILookup<Kingdom, Village> villages)
        {
            foreach (var sponsor in realms)
                foreach (var enemy in realms.Where(k => k != sponsor && sponsor.IsAtWarWith(k)))
                    foreach (var village in villages[enemy])
                        yield return new NpcFoodSabotageTarget { SponsorId = sponsor.StringId, TargetId = enemy.StringId,
                            TownId = village.Bound.StringId, VillageId = village.Settlement.StringId,
                            Preferred = ReignRaidFoodDisruption.Effective(village) >= 0.5f };
        }

        internal static void OwnershipChanged(Settlement settlement)
        {
            var state = State;
            if (state == null || settlement == null) return;
            foreach (var strike in state.Recent.Where(x => !x.Ended && (x.TownId == settlement.StringId || x.VillageId == settlement.StringId)))
                RetireIfOwnershipChanged(strike);
        }

        internal static void ClanChangedRealm(Clan clan, Kingdom newRealm)
        {
            var state = State;
            if (state == null || clan == null) return;
            foreach (var strike in state.Recent.Where(x => !x.Ended && x.TargetId != newRealm?.StringId))
            {
                var village = Settlement.Find(strike.VillageId)?.Village;
                if (village?.Settlement.OwnerClan == clan || village?.Bound?.OwnerClan == clan)
                    strike.Ended = true;
            }
        }

        private static void RetireIfOwnershipChanged(NpcFoodSabotageStrike strike)
        {
            var village = Settlement.Find(strike.VillageId)?.Village;
            if (!ReignNpcFoodSabotagePolicy.ScopeMatches(strike, village?.Settlement.OwnerClan?.Kingdom?.StringId,
                village?.Bound?.OwnerClan?.Kingdom?.StringId, village?.Bound?.StringId))
                strike.Ended = true;
        }

        internal static float Factor(Village village)
        {
            var state = State;
            if (state == null || state.Recent.Count == 0 || village?.Bound == null) return 0f;
            int day = Day;
            string playerRealmId = PlayerRealmId;
            return state.Recent.Any(x => x.VillageId == village.Settlement.StringId && IsActive(x, village, day, playerRealmId))
                ? ReignNpcFoodSabotagePolicy.FoodFactor : 0f;
        }

        private static bool IsActive(NpcFoodSabotageStrike strike, Village village, int day, string playerRealmId)
            => ReignNpcFoodSabotagePolicy.IsActive(strike, day, playerRealmId, village?.Settlement.OwnerClan?.Kingdom?.StringId,
                village?.Bound?.OwnerClan?.Kingdom?.StringId, village?.Bound?.StringId);

        internal static JObject Observation(bool detailed)
        {
            var state = State;
            var day = Day;
            var playerRealmId = PlayerRealmId;
            var active = (state?.Recent ?? Enumerable.Empty<NpcFoodSabotageStrike>())
                .Where(x => IsActive(x, Settlement.Find(x.VillageId)?.Village, day, playerRealmId)).ToList();
            return new JObject {
                ["version"] = ReignNpcFoodSabotagePolicy.Version, ["lastProcessedDay"] = state?.LastProcessedDay ?? -1,
                ["dailyChanceBasisPoints"] = ReignNpcFoodSabotagePolicy.ChanceBasisPoints,
                ["totalStrikes"] = state?.TotalStrikes ?? 0, ["activeStrikes"] = active.Count,
                ["atReceiptCapacity"] = state?.Recent.Count >= ReignNpcFoodSabotagePolicy.ReceiptLimit,
                ["recent"] = detailed && state != null ? JArray.FromObject(state.Recent) : new JArray()
            };
        }
    }
}
