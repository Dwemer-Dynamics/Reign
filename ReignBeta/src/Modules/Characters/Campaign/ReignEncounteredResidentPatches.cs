using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Locations;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal static class ReignEncounteredResidentPatches
    {
        private static bool _installed;
        private static WeakReference<MissionConversationVM> _conversation;
        internal static void Install()
        {
            if (_installed) return;
            var harmony = new Harmony("com.bannerlordreign.encountered_residents");
            var host = typeof(ReignEncounteredResidentPatches);
            void Patch(System.Reflection.MethodBase original, string prefix = null, string postfix = null)
            {
                if (original == null) throw new MissingMethodException("A required resident native hook is missing.");
                harmony.Patch(original, prefix == null ? null : new HarmonyMethod(host, prefix),
                    postfix == null ? null : new HarmonyMethod(host, postfix));
            }
            Patch(AccessTools.Method(typeof(ConversationManager), "GetPlayerSentenceOptions"), nameof(BeforePlayerOptions));
            Patch(AccessTools.Method(typeof(CampaignEventDispatcher), "LocationCharactersAreReadyToSpawn"), postfix: nameof(AfterNativePopulation));
            Patch(AccessTools.Method("SandBox.Missions.MissionLogics.MissionAgentHandler:SpawnWanderingAgentWithInitialFrame"), nameof(BeforeLocationSpawn));
            Patch(AccessTools.Method(typeof(Mission), "SpawnAgent", new[] { typeof(AgentBuildData), typeof(bool) }), nameof(BeforeAgentSpawn), nameof(AfterAgentSpawn));
            Patch(AccessTools.PropertyGetter(typeof(Agent), nameof(Agent.Name)), postfix: nameof(AgentName));
            Patch(AccessTools.PropertyGetter(typeof(Agent), nameof(Agent.NameTextObject)), postfix: nameof(AgentNameObject));
            Patch(AccessTools.PropertySetter(typeof(MissionConversationVM), "CurrentCharacterNameLbl"), nameof(ConversationName));
            Patch(AccessTools.Method(typeof(DefaultHeroAgentLocationModel), "GetLocationForHero"), postfix: nameof(HeroLocation));
            Patch(AccessTools.Method(typeof(DefaultHeroAgentLocationModel), "WillBeListedInOverlay"), postfix: nameof(OverlayListing));
            _installed = true;
        }

        private static void BeforePlayerOptions(ConversationManager __instance)
            => ReignIndividualConversationBehavior.PrepareResidentConversationOption(__instance.ActiveToken);

        private static void AfterNativePopulation()
            => ReignEncounteredResidentsCampaignBehavior.Instance?.RestoreNativePopulation();

        private static void BeforeLocationSpawn(LocationCharacter locationCharacter)
        {
            ReignEncounteredResidentsCampaignBehavior.Instance?.PrepareLocationCharacter(locationCharacter, CampaignMission.Current?.Location);
        }

        private static void BeforeAgentSpawn(AgentBuildData agentBuildData)
            => ReignEncounteredResidentsCampaignBehavior.Instance?.BeforeSpawn(agentBuildData);
        private static void AfterAgentSpawn(Agent __result)
            => ReignEncounteredResidentsCampaignBehavior.Instance?.AfterSpawn(__result);
        private static void AgentName(Agent __instance, ref string __result)
        {
            Hero hero = ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(__instance);
            if (hero != null && !__instance.Character.IsHero) __result = hero.Name.ToString();
        }
        private static void AgentNameObject(Agent __instance, ref TextObject __result)
        {
            Hero hero = ReignEncounteredResidentsCampaignBehavior.Instance?.ResolveAgent(__instance);
            if (hero != null && !__instance.Character.IsHero) __result = hero.Name;
        }
        private static void ConversationName(MissionConversationVM __instance, ref string value)
        {
            _conversation = new WeakReference<MissionConversationVM>(__instance);
            Hero hero = ReignEncounteredResidentsCampaignBehavior.Instance?.ConversationHero;
            if (hero != null && ReignEncounteredResidentsCampaignBehavior.Instance.Find(hero) != null) value = hero.Name.ToString();
        }
        internal static void RefreshKnownName()
        {
            Hero hero = ReignEncounteredResidentsCampaignBehavior.Instance?.ConversationHero;
            if (hero != null && _conversation != null && _conversation.TryGetTarget(out var vm))
                vm.CurrentCharacterNameLbl = hero.Name.ToString();
        }
        private static void HeroLocation(Hero hero, ref Location __result)
        {
            if (ReignEncounteredResidentsCampaignBehavior.Instance?.SuppressAutomaticLocation(hero) == true) __result = null;
        }
        private static void OverlayListing(LocationCharacter locationCharacter, ref bool __result)
        {
            if (ReignEncounteredResidentsCampaignBehavior.Instance?.SuppressAutomaticLocation(locationCharacter?.Character?.HeroObject) == true)
                __result = false;
        }
    }
}
