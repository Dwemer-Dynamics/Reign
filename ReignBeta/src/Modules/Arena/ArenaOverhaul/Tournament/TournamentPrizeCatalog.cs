using System.Collections.Generic;
using System.Linq;
using ArenaOverhaul.ModSettings;
using ReignBeta.Shared.Arena;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ArenaOverhaul.Tournament
{
    internal static class TournamentPrizeCatalog
    {
        internal static List<ItemObject> Build(Town town)
        {
            var all = Items.All.ToList();
            int mode = Settings.Instance!.CultureRestrictedTournamentPrizes.SelectedIndex;
            CultureObject? standardCulture = mode >= 1 ? town.Culture : null;
            int maximum = MaxStandardPrizeValue();
            // Restore the ordinary high-tier equipment already supported by the
            // expanded pool. Neither scaling settings nor campaign progression
            // may replace this catalog with the native named-prize shortlist.
            var eligibleStandard = all.Where(item => !item.NotMerchandise && !item.IsCraftedByPlayer
                && item.Value <= maximum
                && (item.Tier == ItemObject.ItemTiers.Tier5 || item.Tier == ItemObject.ItemTiers.Tier6)
                && (item.IsCraftedWeapon || item.IsMountable || item.ArmorComponent != null)
                && item.Type != ItemObject.ItemTypeEnum.Banner
                && item.Type != ItemObject.ItemTypeEnum.Arrows && item.Type != ItemObject.ItemTypeEnum.Bolts
                && item.Type != ItemObject.ItemTypeEnum.SlingStones && item.Type != ItemObject.ItemTypeEnum.Bullets
                && !item.StringId.StartsWith("practice_") && !item.StringId.StartsWith("dummy_")).ToList();
            List<ItemObject> standard = standardCulture == null ? new List<ItemObject>()
                : eligibleStandard.Where(item => CultureAllows(item, standardCulture))
                    .GroupBy(item => item.Type).SelectMany(group => group.OrderByDescending(item => item.Value)
                        .ThenBy(item => item.StringId).Take(10)).ToList();
            if (standard.Count == 0)
                standard = eligibleStandard.GroupBy(item => (item.Type, item.Culture))
                    .SelectMany(group => group.OrderByDescending(item => item.Value).ThenBy(item => item.StringId).Take(5)).ToList();
            CultureObject? uniqueCulture = mode >= 2 ? town.Culture : null;
            CultureObject? mountCulture = mode == 3 ? town.Culture : null;
            var expanded = standard.Concat(all.Where(item => Unique(item, uniqueCulture)
                && (item.IsCraftedWeapon && !item.StringId.EndsWith("_blunt")
                    && !item.StringId.StartsWith("practice_") && !item.StringId.StartsWith("peasant_")
                    || item.ArmorComponent != null && !item.StringId.StartsWith("dummy_")
                        && item.StringId != "celtic_frost" && item.StringId != "saddle_of_aeneas" && item.StringId != "fortunas_choice")))
                .Concat(all.Where(item => Unique(item, mountCulture) && item.IsMountable));
            var pool = TournamentPrizeVariety.FreshPool(expanded, item => item.StringId,
                TournamentRewardManager.RecentTournamentPrizeItemIds).OrderBy(item => item.Value).ThenBy(item => item.StringId).ToList();
            if (Settings.Instance.LogTournamentPrizeRolls)
                ArenaOverhaul.Helpers.LoggingHelper.Log("town=" + town.StringId + "; cultureMode=" + mode
                    + "; regular=" + standard.Count + "; elitePool=" + pool.Count
                    + "; recent=" + string.Join(",", TournamentRewardManager.RecentTournamentPrizeItemIds), "Elite tournament prize pool");
            return pool;
        }

        private static int MaxStandardPrizeValue() => (Clan.PlayerClan?.Tier ?? 0) switch
        {
            < 3 => 50000,
            3 => 200000,
            4 => 300000,
            5 => 400000,
            _ => 500000
        };

        private static bool CultureAllows(ItemObject item, CultureObject? culture) => culture == null
            || item.Culture == null || item.Culture == culture || !item.Culture.CanHaveSettlement;

        private static bool Unique(ItemObject item, CultureObject? culture) => item.NotMerchandise
            && item.Tier >= ItemObject.ItemTiers.Tier3 && !item.IsCraftedByPlayer && CultureAllows(item, culture);
    }
}
