using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace ReignBeta.World
{
    public static class ReignObjectResolver
    {
        public static Hero FindHero(string stringId)
        {
            if (string.IsNullOrWhiteSpace(stringId))
            {
                return null;
            }

            return Hero.AllAliveHeroes.FirstOrDefault(x => x != null && x.StringId == stringId)
                ?? Hero.DeadOrDisabledHeroes.FirstOrDefault(x => x != null && x.StringId == stringId);
        }

        public static Clan FindClan(string stringId)
        {
            if (string.IsNullOrWhiteSpace(stringId))
            {
                return null;
            }

            return Clan.All.FirstOrDefault(x => x != null && x.StringId == stringId);
        }

        public static Kingdom FindKingdom(string stringId)
        {
            if (string.IsNullOrWhiteSpace(stringId))
            {
                return null;
            }

            return Kingdom.All.FirstOrDefault(x => x != null && x.StringId == stringId);
        }

        public static Settlement FindSettlement(string stringId)
        {
            if (string.IsNullOrWhiteSpace(stringId))
            {
                return null;
            }

            return Settlement.All.FirstOrDefault(x => x != null && x.StringId == stringId);
        }

        public static MobileParty FindHeroParty(string heroStringId)
        {
            Hero hero = FindHero(heroStringId);
            return hero?.PartyBelongedTo;
        }

        public static MobileParty FindParty(string partyStringId)
        {
            if (string.IsNullOrWhiteSpace(partyStringId))
            {
                return null;
            }

            return MobileParty.All.FirstOrDefault(x => x != null && x.StringId == partyStringId);
        }

        public static ItemObject FindItem(string itemStringId)
        {
            if (string.IsNullOrWhiteSpace(itemStringId))
            {
                return null;
            }

            ItemObject exact = Game.Current?.ObjectManager?.GetObject<ItemObject>(itemStringId)
                ?? MBObjectManager.Instance.GetObjectTypeList<ItemObject>().FirstOrDefault(x => x != null && x.StringId == itemStringId);
            if (exact != null)
            {
                return exact;
            }

            string normalized = itemStringId.Trim().ToLowerInvariant();
            if (normalized == "horse" || normalized == "horses" || normalized == "mount" || normalized == "mounts")
            {
                return MBObjectManager.Instance.GetObject<ItemObject>("imperial_charger")
                    ?? MBObjectManager.Instance.GetObject<ItemObject>("sumpter_horse")
                    ?? MBObjectManager.Instance.GetObjectTypeList<ItemObject>().FirstOrDefault(x => x != null && x.HorseComponent != null);
            }

            return MBObjectManager.Instance.GetObjectTypeList<ItemObject>()
                .FirstOrDefault(x => x != null
                    && x.Name != null
                    && x.Name.ToString().IndexOf(itemStringId, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool TryFindEquippedItem(Hero hero, string itemStringId, string slotName, string equipmentSet, out Equipment equipment, out EquipmentIndex slot, out EquipmentElement element, out string sourceSet)
        {
            equipment = null;
            slot = EquipmentIndex.None;
            element = EquipmentElement.Invalid;
            sourceSet = string.Empty;

            if (hero == null || string.IsNullOrWhiteSpace(itemStringId))
            {
                return false;
            }

            string wantedSet = (equipmentSet ?? string.Empty).Trim().ToLowerInvariant();
            if (wantedSet == "civilian" && TryFindEquippedItemInSet(hero.CivilianEquipment, "civilian", itemStringId, slotName, out equipment, out slot, out element, out sourceSet))
            {
                return true;
            }

            if (wantedSet == "battle" && TryFindEquippedItemInSet(hero.BattleEquipment, "battle", itemStringId, slotName, out equipment, out slot, out element, out sourceSet))
            {
                return true;
            }

            if (TryFindEquippedItemInSet(hero.BattleEquipment, "battle", itemStringId, slotName, out equipment, out slot, out element, out sourceSet))
            {
                return true;
            }

            return TryFindEquippedItemInSet(hero.CivilianEquipment, "civilian", itemStringId, slotName, out equipment, out slot, out element, out sourceSet);
        }

        private static bool TryFindEquippedItemInSet(Equipment candidateEquipment, string candidateSet, string itemStringId, string slotName, out Equipment equipment, out EquipmentIndex slot, out EquipmentElement element, out string sourceSet)
        {
            equipment = null;
            slot = EquipmentIndex.None;
            element = EquipmentElement.Invalid;
            sourceSet = string.Empty;

            if (candidateEquipment == null)
            {
                return false;
            }

            string normalizedSlot = (slotName ?? string.Empty).Trim();
            foreach (EquipmentIndex index in System.Enum.GetValues(typeof(EquipmentIndex)))
            {
                if (!string.IsNullOrWhiteSpace(normalizedSlot) && !string.Equals(index.ToString(), normalizedSlot, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    EquipmentElement candidate = candidateEquipment[index];
                    ItemObject item = candidate.Item;
                    if (item == null)
                    {
                        continue;
                    }

                    if (string.Equals(item.StringId, itemStringId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        equipment = candidateEquipment;
                        slot = index;
                        element = candidate;
                        sourceSet = candidateSet;
                        return true;
                    }
                }
                catch
                {
                    // Some EquipmentIndex values are sentinels on some Bannerlord versions.
                }
            }

            return false;
        }

        public static Workshop FindWorkshop(string workshopId)
        {
            if (string.IsNullOrWhiteSpace(workshopId))
            {
                return null;
            }

            foreach (Town town in Town.AllTowns.Where(x => x != null))
            {
                Workshop[] workshops = town.Workshops;
                if (workshops == null)
                {
                    continue;
                }

                for (int i = 0; i < workshops.Length; i++)
                {
                    Workshop workshop = workshops[i];
                    if (workshop == null)
                    {
                        continue;
                    }

                    string id = WorkshopId(workshop, i);
                    if (string.Equals(id, workshopId, System.StringComparison.OrdinalIgnoreCase)
                        || string.Equals(workshop.Tag ?? string.Empty, workshopId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return workshop;
                    }
                }
            }

            return null;
        }

        public static string WorkshopId(Workshop workshop, int index = -1)
        {
            if (workshop == null)
            {
                return string.Empty;
            }

            string settlementId = workshop.Settlement?.StringId ?? string.Empty;
            string tag = workshop.Tag ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return settlementId + ":workshop:" + tag;
            }

            string typeId = workshop.WorkshopType?.StringId ?? "workshop";
            return settlementId + ":workshop:" + typeId + ":" + (index < 0 ? "0" : index.ToString());
        }
    }
}
