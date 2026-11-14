using System;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
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
        internal const string Owner = "com.bannerlordreign.encountered_residents";
        private static WeakReference<MissionConversationVM> _conversation;
        internal static MethodBase[] ResolveNativeTargets() => new MethodBase[] {
            AccessTools.Method(typeof(ConversationManager), "GetPlayerSentenceOptions"),
            AccessTools.Method(typeof(CampaignEventDispatcher), "LocationCharactersAreReadyToSpawn"),
            AccessTools.Method("SandBox.Missions.MissionLogics.MissionAgentHandler:SpawnWanderingAgentWithInitialFrame"),
            AccessTools.Method(typeof(Mission), "SpawnAgent", new[] { typeof(AgentBuildData), typeof(bool), typeof(Equipment), typeof(ItemObject) }),
            AccessTools.PropertyGetter(typeof(Agent), nameof(Agent.Name)),
            AccessTools.PropertyGetter(typeof(Agent), nameof(Agent.NameTextObject)),
            AccessTools.PropertySetter(typeof(MissionConversationVM), "CurrentCharacterNameLbl"),
            AccessTools.Method(typeof(DefaultHeroAgentLocationModel), "GetLocationForHero"),
            AccessTools.Method(typeof(DefaultHeroAgentLocationModel), "WillBeListedInOverlay")
        };
        internal static void Install()
        {
            if (_installed) return;
            var targets = ResolveNativeTargets();
            var names = new[] { "ConversationManager.GetPlayerSentenceOptions", "CampaignEventDispatcher.LocationCharactersAreReadyToSpawn",
                "MissionAgentHandler.SpawnWanderingAgentWithInitialFrame", "Mission.SpawnAgent(AgentBuildData, bool, Equipment, ItemObject)",
                "Agent.Name.get", "Agent.NameTextObject.get", "MissionConversationVM.CurrentCharacterNameLbl.set",
                "DefaultHeroAgentLocationModel.GetLocationForHero", "DefaultHeroAgentLocationModel.WillBeListedInOverlay" };
            // Resolve the complete set before touching native methods. Optional
            // arguments do not create a two-parameter overload in Bannerlord 1.5.7.
            for (int i = 0; i < targets.Length; i++)
                if (targets[i] == null) throw new MissingMethodException("Required resident native hook is missing: " + names[i]);
            var harmony = new Harmony(Owner);
            var host = typeof(ReignEncounteredResidentPatches);
            void Patch(System.Reflection.MethodBase original, string prefix = null, string postfix = null)
            {
                if (original == null) throw new MissingMethodException("A required resident native hook is missing.");
                harmony.Patch(original, prefix == null ? null : new HarmonyMethod(host, prefix),
                    postfix == null ? null : new HarmonyMethod(host, postfix));
            }
            try {
                Patch(targets[0], nameof(BeforePlayerOptions));
                Patch(targets[1], postfix: nameof(AfterNativePopulation));
                Patch(targets[2], nameof(BeforeLocationSpawn));
                Patch(targets[3], nameof(BeforeAgentSpawn), nameof(AfterAgentSpawn));
                Patch(targets[4], postfix: nameof(AgentName));
                Patch(targets[5], postfix: nameof(AgentNameObject));
                Patch(targets[6], nameof(ConversationName));
                Patch(targets[7], postfix: nameof(HeroLocation));
                Patch(targets[8], postfix: nameof(OverlayListing));
                _installed = true;
            } catch {
                harmony.UnpatchAll(Owner);
                throw;
            }
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
