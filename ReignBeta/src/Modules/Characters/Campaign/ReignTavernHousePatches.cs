using System;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;

namespace ReignBeta.Campaign
{
    internal static class ReignTavernHousePatches
    {
        private const string Owner = "com.bannerlordreign.tavern_house.v1";
        internal static bool Ready { get; private set; }
        internal static void Install()
        {
            if (Ready) return;
            var harmony = new Harmony(Owner);
            try
            {
                void Patch(Type type, string method, string prefix = null, string postfix = null)
                {
                    var original = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
                    harmony.Patch(original, prefix == null ? null : new HarmonyMethod(typeof(ReignTavernHousePatches), prefix),
                        postfix == null ? null : new HarmonyMethod(typeof(ReignTavernHousePatches), postfix));
                }
                Patch(typeof(DefaultHeroAgentLocationModel), "GetLocationForHero", postfix: nameof(HeroLocation));
                Patch(typeof(DefaultHeroAgentLocationModel), "WillBeListedInOverlay", postfix: nameof(Overlay));
                Patch(typeof(AddCompanionAction), "Apply", prefix: nameof(Recruitment));
                Ready = true;
            }
            catch (Exception ex) { harmony.UnpatchAll(Owner); ReignLog.Exception("Tavern house native protection hooks", ex); }
        }
        private static void HeroLocation(Hero hero, Settlement settlement, ref Location __result)
        {
            var behavior = ReignTavernHouseCampaignBehavior.Instance;
            var person = behavior?.GetPerson(hero);
            if (person == null) return;
            if (behavior.IsActiveStaff(hero)) __result = null;
            else if (person.Retired && hero.IsAlive && !hero.IsPrisoner && hero.PartyBelongedTo == null && hero.CurrentSettlement == settlement)
                __result = settlement.LocationComplex.GetLocationWithId("center");
        }
        private static void Overlay(LocationCharacter locationCharacter, ref bool __result)
        {
            if (ReignTavernHouseCampaignBehavior.Instance?.IsActiveStaff(locationCharacter?.Character?.HeroObject) == true) __result = false;
        }
        private static bool Recruitment(Hero companion) => ReignTavernHouseCampaignBehavior.Instance?.AllowsRecruitment(companion) != false;
    }
}
