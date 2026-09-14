using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Education;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.HeirSelectionPopup;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.UI.HiddenInformation
{
    internal static class ReignHiddenInformationPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.hidden_information";
        private static Harmony _harmony;
        private static readonly HashSet<string> OptionalPatchKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly List<OptionalPresentationPatch> OptionalPatches = new List<OptionalPresentationPatch>
        {
            new OptionalPresentationPatch("SandBox.ViewModelCollection", "SandBox.ViewModelCollection.SPOrderOfBattleVM", "GetAgentTooltip", nameof(DynamicTooltipResultPostfix)),
            new OptionalPresentationPatch("NavalDLC.ViewModelCollection", "NavalDLC.ViewModelCollection.NavalOrderOfBattleHeroItemVM", "GetTooltip", nameof(DynamicTooltipResultPostfix))
        };

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            _harmony = new Harmony(HarmonyId);
            try
            {
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoaded;
                Patch(
                    AccessTools.Method(typeof(GameTexts), nameof(GameTexts.FindText),
                        new[] { typeof(string), typeof(string) }),
                    prefix: nameof(PartyMoraleGameTextPrefix));
                Patch(AccessTools.Method(typeof(HeroVM), nameof(HeroVM.GetRelation)), postfix: nameof(HeroRelationPostfix));
                Patch(AccessTools.Method(typeof(TooltipRefresherCollection), nameof(TooltipRefresherCollection.RefreshHeroTooltip)), postfix: nameof(RefreshHeroTooltipPostfix));
                Patch(AccessTools.Method(typeof(TooltipRefresherCollection), nameof(TooltipRefresherCollection.RefreshCharacterTooltip)), postfix: nameof(RefreshCharacterTooltipPostfix));
                Patch(AccessTools.Method(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.Refresh)), postfix: nameof(EncyclopediaHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(ClanLordItemVM), nameof(ClanLordItemVM.UpdateProperties)), postfix: nameof(ClanLordRefreshPostfix));
                Patch(AccessTools.Method(typeof(MarriageOfferPopupHeroVM), nameof(MarriageOfferPopupHeroVM.RefreshValues)), postfix: nameof(MarriageHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(HeirSelectionPopupHeroVM), nameof(HeirSelectionPopupHeroVM.RefreshValues)), postfix: nameof(HeirHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(GameMenuPartyItemVM), nameof(GameMenuPartyItemVM.RefreshProperties)), postfix: nameof(GameMenuPartyRefreshPostfix));
                Patch(AccessTools.Method(typeof(RecruitVolunteerOwnerVM), nameof(RecruitVolunteerOwnerVM.RefreshValues)), postfix: nameof(RecruitOwnerRefreshPostfix));
                Patch(AccessTools.Method(typeof(RecruitVolunteerTroopVM), nameof(RecruitVolunteerTroopVM.ExecuteBeginHint)), prefix: nameof(RecruitHintPrefix));
                Patch(AccessTools.Method(typeof(WeaponDesignVM), "OnCraftingHeroChanged"), postfix: nameof(WeaponDesignHeroChangedPostfix));
                Patch(AccessTools.Method(typeof(MissionConversationVM), nameof(MissionConversationVM.Refresh)), postfix: nameof(ConversationRefreshPostfix));
                Patch(AccessTools.Method(typeof(CharacterDeveloperVM), "SetCurrentHero"), postfix: nameof(CharacterDeveloperHeroChangedPostfix));

                PatchAllConstructors(typeof(EncyclopediaHeroPageVM), nameof(HeroPresentationConstructedPostfix));
                PatchAllConstructors(typeof(ClanLordItemVM), nameof(HeroPresentationConstructedPostfix));
                PatchAllConstructors(typeof(MarriageOfferPopupHeroVM), nameof(HeroPresentationConstructedPostfix));
                PatchAllConstructors(typeof(HeirSelectionPopupHeroVM), nameof(HeroPresentationConstructedPostfix));
                PatchAllConstructors(typeof(EducationGainedPropertiesVM), nameof(HeroPresentationConstructedPostfix));
                PatchAllConstructors(typeof(RecruitVolunteerOwnerVM), nameof(RecruitOwnerConstructedPostfix));
                PatchRelationComparer();
                foreach (OptionalPresentationPatch optionalPatch in OptionalPatches)
                {
                    TryPatchOptionalPresentation(optionalPatch, null);
                }
                PatchClanCardPropertyConstructors();

                ReignLog.Info("Character relationship presentation firewall loaded; character skills remain visible.");
            }
            catch (Exception ex)
            {
                _harmony.UnpatchAll(HarmonyId);
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
                _harmony = null;
                ReignLog.Warn("Hidden-information patches failed: " + ex);
            }
        }

        public static void Unapply()
        {
            AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoaded;
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
            OptionalPatchKeys.Clear();
        }

        private static bool PartyMoraleGameTextPrefix(string id, string variation, ref TextObject __result)
        {
            if (!string.Equals(id, "str_party_morale", StringComparison.Ordinal) || variation != null)
            {
                return true;
            }

            // CampaignUIHelper caches this one native string in its static
            // initializer. Bannerlord 1.4.7 can first initialize that helper
            // while GameTexts is temporarily cleared during New Game or load.
            // Return the exact native localized TextObject without touching the
            // transient manager; all other GameTexts lookups remain untouched.
            __result = new TextObject("{=alMmQrhK}Party Morale");
            return false;
        }

        private static void Patch(MethodBase original, string prefix = null, string postfix = null)
        {
            if (original == null)
            {
                throw new MissingMethodException("Required hidden-information presentation method was not found.");
            }
            _harmony.Patch(
                original,
                prefix == null ? null : new HarmonyMethod(typeof(ReignHiddenInformationPatches), prefix),
                postfix == null ? null : new HarmonyMethod(typeof(ReignHiddenInformationPatches), postfix));
        }

        private static void PatchAllConstructors(Type type, string postfix)
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Patch(constructor, postfix: postfix);
            }
        }

        private static void HeroRelationPostfix(Hero hero, ref int __result)
        {
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                __result = 0;
            }
        }

        private static void RefreshHeroTooltipPostfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            Hero hero = args != null && args.Length > 0 ? args[0] as Hero : null;
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                propertyBasedTooltipVM.Mode = 1;
                ReignHiddenInformationPolicy.MaskRelationshipProperties(propertyBasedTooltipVM?.TooltipPropertyList);
            }
        }

        private static void RefreshCharacterTooltipPostfix(PropertyBasedTooltipVM propertyBasedTooltipVM, object[] args)
        {
            CharacterObject character = args != null && args.Length > 0 ? args[0] as CharacterObject : null;
            if (ReignHiddenInformationPolicy.ShouldMask(character))
            {
                ReignHiddenInformationPolicy.MaskRelationshipProperties(propertyBasedTooltipVM?.TooltipPropertyList);
            }
        }

        private static void EncyclopediaHeroRefreshPostfix(EncyclopediaHeroPageVM __instance)
        {
            Hero hero = AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_hero")?.GetValue(__instance) as Hero;
            if (hero == null)
            {
                return;
            }
            ReignHiddenInformationPolicy.AssociateSkillTree(__instance, hero);
            if (!ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                return;
            }
            foreach (StringPairItemVM item in __instance.Stats ?? new MBBindingList<StringPairItemVM>())
            {
                if ((item?.Definition ?? string.Empty).IndexOf("relation", StringComparison.CurrentCultureIgnoreCase) >= 0)
                {
                    item.Value = string.Empty;
                }
            }
            ClearRelationshipList(__instance.Allies);
            ClearRelationshipList(__instance.Enemies);
            __instance.AdditionalAllies?.Clear();
            __instance.AdditionalEnemies?.Clear();
            __instance.AnyAdditionalAllies = false;
            __instance.AnyAdditionalEnemies = false;
            __instance.AdditionalAlliesString = string.Empty;
            __instance.AdditionalEnemiesString = string.Empty;
        }

        private static void ClearRelationshipList(MBBindingList<HeroVM> list)
        {
            if (list == null)
            {
                return;
            }
            list.ApplyActionOnAllItems(item => item?.OnFinalize());
            list.Clear();
        }

        private static void ClanLordRefreshPostfix(ClanLordItemVM __instance)
        {
            Hero hero = __instance?.GetHero();
            ReignHiddenInformationPolicy.AssociateSkillTree(__instance, hero);
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                __instance.RelationToMainHeroText = string.Empty;
            }
        }

        private static void MarriageHeroRefreshPostfix(MarriageOfferPopupHeroVM __instance)
        {
            ReignHiddenInformationPolicy.AssociateSkillTree(__instance, __instance?.Hero);
            if (ReignHiddenInformationPolicy.ShouldMask(__instance?.Hero))
            {
                __instance.Relation = 0;
            }
        }

        private static void HeirHeroRefreshPostfix(HeirSelectionPopupHeroVM __instance)
        {
            ReignHiddenInformationPolicy.AssociateSkillTree(__instance, __instance?.Hero);
            if (ReignHiddenInformationPolicy.ShouldMask(__instance?.Hero))
            {
                __instance.RelationToMainHero = string.Empty;
            }
        }

        private static void GameMenuPartyRefreshPostfix(GameMenuPartyItemVM __instance)
        {
            Hero hero = __instance?.Character?.HeroObject ?? __instance?.Party?.LeaderHero;
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                __instance.Relation = 0;
            }
        }

        private static void RecruitOwnerRefreshPostfix(RecruitVolunteerOwnerVM __instance)
        {
            if (ReignHiddenInformationPolicy.ShouldMask(__instance?.Hero))
            {
                __instance.Relation = 0;
                __instance.RelationToPlayer = 0;
            }
        }

        private static void RecruitOwnerConstructedPostfix(RecruitVolunteerOwnerVM __instance)
        {
            RecruitOwnerRefreshPostfix(__instance);
        }

        private static bool RecruitHintPrefix(RecruitVolunteerTroopVM __instance)
        {
            Hero ownerHero = __instance?.Owner?.OwnerHero;
            if (__instance == null
                || __instance.PlayerHasEnoughRelation
                || ownerHero == null
                || !ReignHiddenInformationPolicy.ShouldMask(ownerHero))
            {
                return true;
            }
            MBInformationManager.ShowHint(new TextObject("{=reign_recruit_relation_unknown}Your standing with this character is insufficient. The exact relationship is unknown.").ToString());
            return false;
        }

        private static void ConversationRefreshPostfix(MissionConversationVM __instance)
        {
            CharacterObject character = CharacterObject.OneToOneConversationCharacter;
            if (!ReignHiddenInformationPolicy.ShouldMask(character))
            {
                return;
            }
            __instance.Relation = 0;
            __instance.RelationText = string.Empty;
        }

        private static void CharacterDeveloperHeroChangedPostfix(CharacterDeveloperVM __instance)
        {
            __instance?.OnPropertyChanged("ReignIsCurrentHeroSkillHidden");
            __instance?.OnPropertyChanged("ReignIsCurrentHeroSkillVisible");
        }

        private static void HeroPresentationConstructedPostfix(object __instance, object[] __args)
        {
            Hero hero = null;
            if (__args != null)
            {
                foreach (object argument in __args)
                {
                    if (ReignHiddenInformationPolicy.TryResolveHero(argument, out hero))
                    {
                        break;
                    }
                }
            }
            if (hero == null)
            {
                ReignHiddenInformationPolicy.TryResolveHero(__instance, out hero);
            }
            ReignHiddenInformationPolicy.AssociateSkillTree(__instance, hero);
        }

        private static void WeaponDesignHeroChangedPostfix(WeaponDesignVM __instance, object[] __args)
        {
            Hero hero = null;
            if (__args != null && __args.Length > 0)
            {
                ReignHiddenInformationPolicy.TryResolveHero(__args[0], out hero);
            }
            ReignHiddenInformationPolicy.Associate(__instance, hero);
            __instance?.OnPropertyChanged("ReignIsCraftingSkillHidden");
            __instance?.OnPropertyChanged("ReignIsCraftingSkillVisible");
            __instance?.OnPropertyChanged("ReignCraftingSkillText");
        }

        private static void DynamicTooltipResultPostfix(object[] __args, object __result)
        {
            Hero hero = null;
            if (__args != null)
            {
                foreach (object argument in __args)
                {
                    if (ReignHiddenInformationPolicy.TryResolveHero(argument, out hero))
                    {
                        break;
                    }
                    object character = argument?.GetType().GetProperty("Character", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(argument, null);
                    if (ReignHiddenInformationPolicy.TryResolveHero(character, out hero))
                    {
                        break;
                    }
                }
            }
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                ReignHiddenInformationPolicy.MaskRelationshipProperties((__result as IEnumerable)?.Cast<object>().OfType<TooltipProperty>());
            }
        }

        private static void ClanCardPropertyConstructedPostfix(object __instance)
        {
            if (__instance == null)
            {
                return;
            }
            PropertyInfo titleProperty = __instance.GetType().GetProperty("Title") ?? __instance.GetType().GetProperty("Definition");
            PropertyInfo valueProperty = __instance.GetType().GetProperty("Value");
            string title = titleProperty?.GetValue(__instance, null)?.ToString() ?? string.Empty;
            if (valueProperty?.CanWrite != true)
            {
                return;
            }
            if (title.IndexOf("relation", StringComparison.CurrentCultureIgnoreCase) >= 0)
            {
                valueProperty.SetValue(__instance, string.Empty, null);
            }
        }

        private static bool RelationComparerPrefix(HeroVM x, HeroVM y, ref int __result)
        {
            if (ReignHiddenInformationPolicy.IsCheatRevealEnabled)
            {
                return true;
            }

            int leaderOrder = (y?.IsKingdomLeader ?? false).CompareTo(x?.IsKingdomLeader ?? false);
            __result = leaderOrder != 0
                ? leaderOrder
                : string.Compare(x?.NameText, y?.NameText, StringComparison.CurrentCultureIgnoreCase);
            return false;
        }

        private static void PatchRelationComparer()
        {
            MethodInfo compare = typeof(HeroRelationComparer).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(method => method.Name.EndsWith(".Compare", StringComparison.Ordinal)
                    || string.Equals(method.Name, "Compare", StringComparison.Ordinal));
            if (compare != null)
            {
                Patch(compare, prefix: nameof(RelationComparerPrefix));
            }
        }

        private static void OnAssemblyLoaded(object sender, AssemblyLoadEventArgs args)
        {
            if (_harmony == null || args?.LoadedAssembly == null)
            {
                return;
            }
            foreach (OptionalPresentationPatch optionalPatch in OptionalPatches.Where(spec =>
                string.Equals(spec.AssemblyName, args.LoadedAssembly.GetName().Name, StringComparison.OrdinalIgnoreCase)))
            {
                TryPatchOptionalPresentation(optionalPatch, args.LoadedAssembly);
            }
        }

        private static void TryPatchOptionalPresentation(OptionalPresentationPatch spec, Assembly loadedAssembly)
        {
            string key = spec.TypeName + "." + spec.MethodName;
            if (OptionalPatchKeys.Contains(key))
            {
                return;
            }
            try
            {
                Assembly assembly = loadedAssembly ?? AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(candidate => string.Equals(candidate.GetName().Name, spec.AssemblyName, StringComparison.OrdinalIgnoreCase));
                if (assembly == null)
                {
                    try { assembly = Assembly.Load(spec.AssemblyName); }
                    catch { return; }
                    if (OptionalPatchKeys.Contains(key)) return;
                }
                Type type = assembly.GetType(spec.TypeName, false);
                MethodInfo method = type?.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, spec.MethodName, StringComparison.Ordinal));
                if (method == null) return;
                Patch(method, postfix: spec.Postfix);
                OptionalPatchKeys.Add(key);
            }
            catch (Exception ex)
            {
                ReignHiddenInformationPolicy.LogOnce("optional-patch:" + key, ex);
            }
        }

        private sealed class OptionalPresentationPatch
        {
            public OptionalPresentationPatch(string assemblyName, string typeName, string methodName, string postfix)
            {
                AssemblyName = assemblyName;
                TypeName = typeName;
                MethodName = methodName;
                Postfix = postfix;
            }

            public string AssemblyName { get; }
            public string TypeName { get; }
            public string MethodName { get; }
            public string Postfix { get; }
        }

        private static void PatchClanCardPropertyConstructors()
        {
            Type propertyType = typeof(ClanLordItemVM).Assembly.GetType(
                "TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement.ClanCardSelectionItemPropertyInfo",
                false);
            if (propertyType == null)
            {
                return;
            }
            foreach (ConstructorInfo constructor in propertyType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                Patch(constructor, postfix: nameof(ClanCardPropertyConstructedPostfix));
            }
        }
    }
}
