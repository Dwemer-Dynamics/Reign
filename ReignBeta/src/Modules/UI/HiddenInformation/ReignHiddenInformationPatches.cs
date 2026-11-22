using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.ClanManagement;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.CampaignSystem.ViewModelCollection.Education;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.HeirSelectionPopup;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Generic;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using ReignBeta.Platform.Integration;

namespace ReignBeta.UI.HiddenInformation
{
    internal static class ReignHiddenInformationPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.hidden_information";
        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            _harmony = new Harmony(HarmonyId);
            try
            {
                Patch(
                    AccessTools.Method(typeof(GameTexts), nameof(GameTexts.FindText),
                        new[] { typeof(string), typeof(string) }),
                    prefix: nameof(PartyMoraleGameTextPrefix));
                Patch(AccessTools.Method(typeof(HeroVM), nameof(HeroVM.GetRelation)), postfix: nameof(HeroRelationPostfix));
                Patch(AccessTools.Method(typeof(TooltipRefresherCollection), nameof(TooltipRefresherCollection.RefreshHeroTooltip)), postfix: nameof(RefreshHeroTooltipPostfix));
                Patch(AccessTools.Method(typeof(EncyclopediaHeroPageVM), nameof(EncyclopediaHeroPageVM.Refresh)), postfix: nameof(EncyclopediaHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(ClanLordItemVM), nameof(ClanLordItemVM.UpdateProperties)), postfix: nameof(ClanLordRefreshPostfix));
                Patch(AccessTools.Method(typeof(MarriageOfferPopupHeroVM), nameof(MarriageOfferPopupHeroVM.RefreshValues)), postfix: nameof(MarriageHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(HeirSelectionPopupHeroVM), nameof(HeirSelectionPopupHeroVM.RefreshValues)), postfix: nameof(HeirHeroRefreshPostfix));
                Patch(AccessTools.Method(typeof(GameMenuPartyItemVM), nameof(GameMenuPartyItemVM.RefreshProperties)), postfix: nameof(GameMenuPartyRefreshPostfix));
                Patch(AccessTools.Method(typeof(RecruitVolunteerOwnerVM), nameof(RecruitVolunteerOwnerVM.RefreshValues)), postfix: nameof(RecruitOwnerRefreshPostfix));
                Patch(AccessTools.Method(typeof(RecruitVolunteerTroopVM), nameof(RecruitVolunteerTroopVM.ExecuteBeginHint)), prefix: nameof(RecruitHintPrefix));
                Patch(AccessTools.Method(typeof(MissionConversationVM), nameof(MissionConversationVM.Refresh)), postfix: nameof(ConversationRefreshPostfix));

                PatchAllConstructors(typeof(RecruitVolunteerOwnerVM), nameof(RecruitOwnerConstructedPostfix));
                PatchRelationComparer();

                ReignLog.Info("Character relationship presentation firewall loaded; native character skill presentation retained.");
            }
            catch (Exception ex)
            {
                _harmony.UnpatchAll(HarmonyId);
                _harmony = null;
                ReignLog.Warn("Hidden-information patches failed: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
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
            if (propertyBasedTooltipVM != null && ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                propertyBasedTooltipVM.Mode = 1;
                ReignHiddenInformationPolicy.MaskHeroTooltipRelationships(propertyBasedTooltipVM.TooltipPropertyList);
            }
        }

        private static void EncyclopediaHeroRefreshPostfix(EncyclopediaHeroPageVM __instance)
        {
            Hero hero = AccessTools.Field(typeof(EncyclopediaHeroPageVM), "_hero")?.GetValue(__instance) as Hero;
            if (hero == null)
            {
                return;
            }
            if (!ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                return;
            }
            if (__instance.Stats != null && GameTexts.TryGetText("str_enc_sf_relation", out TextObject relationText))
            {
                string relationLabel = relationText.ToStringWithoutClear();
                foreach (StringPairItemVM item in __instance.Stats)
                {
                    if (!string.IsNullOrEmpty(relationLabel) &&
                        string.Equals(item?.Definition, relationLabel, StringComparison.Ordinal))
                    {
                        item.Value = string.Empty;
                    }
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
            if (ReignHiddenInformationPolicy.ShouldMask(hero))
            {
                __instance.RelationToMainHeroText = string.Empty;
            }
        }

        private static void MarriageHeroRefreshPostfix(MarriageOfferPopupHeroVM __instance)
        {
            if (ReignHiddenInformationPolicy.ShouldMask(__instance?.Hero))
            {
                __instance.Relation = 0;
            }
        }

        private static void HeirHeroRefreshPostfix(HeirSelectionPopupHeroVM __instance)
        {
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
    }
}
