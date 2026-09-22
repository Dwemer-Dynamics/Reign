using ArenaOverhaul.CampaignBehaviors.BehaviorManagers;
using ArenaOverhaul.ModSettings;
using ArenaOverhaul.Tournament;

using System;
using System.Collections.Generic;
using System.Linq;

using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TournamentGames;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ArenaOverhaul.Models
{
    public class ArenaOverhaulTournamentModel : TournamentModel
    {
        private readonly TournamentModel _previouslyAssignedModel;

        public ArenaOverhaulTournamentModel(TournamentModel previouslyAssignedModel)
        {
            _previouslyAssignedModel = previouslyAssignedModel;
        }

        public override Equipment GetParticipantArmor(CharacterObject participant)
        {
            var practiceEquipmentSetting = AOArenaBehaviorManager.Instance!.GetParticipantArmorType();
            var practiceEquipment = practiceEquipmentSetting switch
            {
                PracticeEquipmentType.PracticeClothes => GetRandomPracticeClothes(),
                PracticeEquipmentType.CivilianEquipment => participant.RandomCivilianEquipment,
                PracticeEquipmentType.BattleEquipment => participant.RandomBattleEquipment,
                _ => null,
            };

            var equipment = Mission.Current.Mode != MissionMode.Tournament ? practiceEquipment : null;
            return equipment ?? _previouslyAssignedModel.GetParticipantArmor(participant);
        }

        public override TournamentGame CreateTournament(Town town) => _previouslyAssignedModel.CreateTournament(town);

        public override int GetInfluenceReward(Hero winner, Town town) => _previouslyAssignedModel.GetInfluenceReward(winner, town);

        public override int GetNumLeaderboardVictoriesAtGameStart() => _previouslyAssignedModel.GetNumLeaderboardVictoriesAtGameStart();

        public override int GetRenownReward(Hero winner, Town town) => _previouslyAssignedModel.GetRenownReward(winner, town);

        public override (SkillObject skill, int xp) GetSkillXpGainFromTournament(Town town) => _previouslyAssignedModel.GetSkillXpGainFromTournament(town);

        public override float GetTournamentEndChance(TournamentGame tournament) => _previouslyAssignedModel.GetTournamentEndChance(tournament);

        public override float GetTournamentSimulationScore(CharacterObject character) => _previouslyAssignedModel.GetTournamentSimulationScore(character);

        public override float GetTournamentStartChance(Town town) => _previouslyAssignedModel.GetTournamentStartChance(town);

        public override MBList<ItemObject> GetRegularRewardItems(Town town, int minimum, int maximum) =>
            _previouslyAssignedModel.GetRegularRewardItems(town, minimum, maximum);

        public override MBList<ItemObject> GetEliteRewardItems(Town town, int minimum, int maximum)
        {
            Settings settings = Settings.Instance!;
            if (!settings.EnableTournamentPrizeScaling || Clan.PlayerClan.Tier < 2)
                return _previouslyAssignedModel.GetEliteRewardItems(town, minimum, maximum);

            int cultureMode = settings.CultureRestrictedTournamentPrizes.SelectedIndex;
            CultureObject? regularCulture = cultureMode >= 1 ? town.Culture : null;
            var items = TaleWorlds.CampaignSystem.Extensions.Items.All.ToList();
            List<ItemObject> regular = items.Where(item => SuitableElitePrize(item, regularCulture))
                .GroupBy(item => item.Type).SelectMany(group => group.OrderByDescending(item => item.Value).Take(10)).ToList();
            if (regular.Count == 0)
                regular = items.Where(item => SuitableElitePrize(item, null))
                    .GroupBy(item => (item.Type, item.Culture))
                    .SelectMany(group => group.OrderByDescending(item => item.Value).Take(5)).ToList();

            CultureObject? uniqueCulture = cultureMode >= 2 ? town.Culture : null;
            CultureObject? mountCulture = cultureMode == 3 ? town.Culture : null;
            var expanded = regular.Concat(items.Where(item => UniquePrize(item, uniqueCulture)
                    && (item.IsCraftedWeapon && !item.StringId.EndsWith("_blunt") && !item.StringId.StartsWith("practice_") && !item.StringId.StartsWith("peasant_")
                        || item.ArmorComponent != null && !item.StringId.StartsWith("dummy_")
                            && item.StringId != "celtic_frost" && item.StringId != "saddle_of_aeneas" && item.StringId != "fortunas_choice")))
                .Concat(items.Where(item => UniquePrize(item, mountCulture) && item.IsMountable))
                .GroupBy(item => item.StringId).Select(group => group.First()).OrderBy(item => item.Value).ToList();
            var fresh = expanded.Where(item => !TournamentRewardManager.IsRecentlyRolledPrize(item)).ToList();
            if (fresh.Count >= Math.Min(8, expanded.Count)) expanded = fresh;
            if (settings.LogTournamentPrizeRolls)
                Helpers.LoggingHelper.Log("town=" + town.StringId + "; cultureMode=" + cultureMode + "; clanTier=" + Clan.PlayerClan.Tier
                    + "; regular=" + regular.Count + "; elitePool=" + expanded.Count
                    + "; recent=" + string.Join(",", TournamentRewardManager.RecentTournamentPrizeItemIds), "Elite tournament prize pool");
            return expanded.Count > 0 ? new MBList<ItemObject>(expanded) : _previouslyAssignedModel.GetEliteRewardItems(town, minimum, maximum);
        }

        private static bool SuitableElitePrize(ItemObject item, CultureObject? culture) =>
            (item.Tier == ItemObject.ItemTiers.Tier4 || item.Tier == ItemObject.ItemTiers.Tier5)
            && item.Value <= MaxElitePrizeValue() && CultureAllows(item, culture) && !item.NotMerchandise
            && (item.IsCraftedWeapon || item.IsMountable || item.ArmorComponent != null) && !item.IsCraftedByPlayer;

        private static bool UniquePrize(ItemObject item, CultureObject? culture) =>
            item.NotMerchandise && item.Tier >= ItemObject.ItemTiers.Tier3 && !item.IsCraftedByPlayer && CultureAllows(item, culture);

        private static bool CultureAllows(ItemObject item, CultureObject? culture) =>
            culture == null || item.Culture == null || item.Culture == culture || !item.Culture.CanHaveSettlement;

        private static int MaxElitePrizeValue() => Clan.PlayerClan.Tier switch
        {
            3 => 200000,
            4 => 300000,
            5 => 400000,
            >= 6 => 500000,
            _ => 50000
        };

        /* service methods */

        private static Equipment? GetRandomPracticeClothes()
        {
            if (CampaignMission.Current is not { } misson || misson.Mode != MissionMode.Battle || Settlement.CurrentSettlement is not { } settlement || AOArenaBehaviorManager.Instance!.PracticeMode != ArenaPracticeMode.Team)
            {
                return null;
            }

            var settlementCultureId = settlement.MapFaction?.Culture?.StringId ?? "empire";
            var dummyCharacter = Game.Current.ObjectManager.GetObject<CharacterObject>("gear_team_practice_dummy_" + settlementCultureId);
            return dummyCharacter?.RandomBattleEquipment;
        }
    }
}
