using System;
using HarmonyLib;
using Helpers;
using ReignBeta.Integration;
using ReignBeta.UI.HiddenInformation;
using SandBox.CampaignBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Runtime
{
    /// <summary>
    /// Makes a Reign-era opinion belong to the people involved in the exchange.
    /// Bannerlord's default noble routing can substitute household leaders for
    /// those people, turning a personal reaction into a family-wide value.
    /// </summary>
    internal static class ReignIndividualRelationPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.individual_relations";
        private static Harmony _harmony;

        public static void Apply()
        {
            if (_harmony != null)
            {
                return;
            }

            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.Patch(
                    AccessTools.Method(typeof(DefaultDiplomacyModel), "GetHeroesForEffectiveRelation"),
                    prefix: new HarmonyMethod(typeof(ReignIndividualRelationPatches), nameof(PreserveParticipantPairPrefix)));
                _harmony.Patch(
                    AccessTools.Method(typeof(DefaultDiplomacyModel), "GetEffectiveRelation"),
                    prefix: new HarmonyMethod(typeof(ReignIndividualRelationPatches), nameof(UseReignNpcRelationPrefix)));
                _harmony.Patch(
                    AccessTools.Method(typeof(DefaultNotificationsCampaignBehavior), "OnRelationChanged"),
                    prefix: new HarmonyMethod(typeof(ReignIndividualRelationPatches), nameof(ReplaceClanRelationNoticePrefix)));
                ReignLog.Info("Personal-opinion routing loaded; noble reactions now stay with the people who took part.");
            }
            catch (Exception ex)
            {
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
                ReignLog.Warn("Individual noble relationship patches failed: " + ex);
            }
        }

        public static void Unapply()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = null;
        }

        private static bool PreserveParticipantPairPrefix(
            Hero hero1,
            Hero hero2,
            out Hero effectiveHero1,
            out Hero effectiveHero2)
        {
            effectiveHero1 = hero1;
            effectiveHero2 = hero2;
            return false;
        }

        private static bool UseReignNpcRelationPrefix(Hero hero1, Hero hero2, ref int __result)
        {
            if (hero1 == null || hero2 == null || hero1 == hero2)
            {
                return true;
            }

            // Reign has already evaluated both characters' complete personality
            // profiles in its directional affinities. Bannerlord normally adds a
            // second Honor/Valor/Mercy compatibility modifier here, so writing a
            // projected average of 3 can immediately read back as -1 or 7. This
            // applies to NPC/player pairs as well: the server has already projected
            // the NPC's directional outlook toward the player into the native slot.
            __result = hero1.GetBaseHeroRelation(hero2);
            return false;
        }

        private static bool ReplaceClanRelationNoticePrefix(
            Hero effectiveHero,
            Hero effectiveHeroGainedRelationWith,
            int relationChange,
            bool showNotification,
            ChangeRelationAction.ChangeRelationDetail detail,
            Hero originalHero,
            Hero originalGainedRelationWith)
        {
            if (showNotification
                && relationChange != 0
                && (effectiveHero == Hero.MainHero || effectiveHeroGainedRelationWith == Hero.MainHero))
            {
                Hero otherHero = effectiveHero.IsHumanPlayerCharacter
                    ? effectiveHeroGainedRelationWith
                    : effectiveHero;
                if (otherHero != null)
                {
                    bool reveal = ReignHiddenInformationPolicy.IsCheatRevealEnabled;
                    TextObject message = reveal
                        ? relationChange > 0
                            ? new TextObject("{=reign_personal_opinion_improved}Your standing with {HERO.NAME} improved by {MAGNITUDE}. Personal relation: {VALUE}.")
                            : new TextObject("{=reign_personal_opinion_declined}Your standing with {HERO.NAME} declined by {MAGNITUDE}. Personal relation: {VALUE}.")
                        : relationChange > 0
                            ? new TextObject("{=reign_personal_opinion_improved_hidden}Your standing with {HERO.NAME} improved.")
                            : new TextObject("{=reign_personal_opinion_declined_hidden}Your standing with {HERO.NAME} declined.");
                    StringHelpers.SetCharacterProperties("HERO", otherHero.CharacterObject, message);
                    if (reveal)
                    {
                        message.SetTextVariable("VALUE", otherHero.GetRelation(Hero.MainHero));
                        message.SetTextVariable("MAGNITUDE", MathF.Abs(relationChange));
                    }
                    MBInformationManager.AddQuickInformation(
                        message,
                        0,
                        otherHero.IsNotable ? otherHero.CharacterObject : null,
                        null,
                        "event:/ui/notification/relation");
                }
            }

            // The stock handler describes landed nobles through their clan.
            // Reign has already shown the personal result, so suppress that notice.
            return false;
        }
    }
}
