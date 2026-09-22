using System;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace AIPortraits;

internal static class NativePortraitIdentityPatch
{
    // Settlement/party/overlay portraits use CampaignUIHelper's string-based code factory,
    // not CharacterCode.CreateFrom(BasicCharacterObject, Equipment). Both routes are needed.
    [HarmonyPatch(typeof(CampaignUIHelper), nameof(CampaignUIHelper.GetCharacterCode),
        new[] { typeof(CharacterObject), typeof(bool) })]
    internal static class CampaignCodePatch
    {
        private static void Postfix(CharacterObject character, CharacterCode __result) => Remember(character, __result);
    }

    [HarmonyPatch(typeof(CharacterCode), nameof(CharacterCode.CreateFrom),
        new[] { typeof(BasicCharacterObject), typeof(Equipment) })]
    internal static class CharacterCodePatch
    {
        private static void Postfix(BasicCharacterObject character, CharacterCode __result) =>
            Remember(character as CharacterObject, __result);
    }

    [HarmonyPatch(typeof(CharacterImageIdentifierVM), MethodType.Constructor, new[] { typeof(CharacterCode) })]
    internal static class ImageIdentifierPatch
    {
        private static void Postfix(CharacterImageIdentifierVM __instance, CharacterCode characterCode)
        {
            try
            {
                __instance.AdditionalArgs = NativePortraitIdentity.CreateArguments(characterCode,
                    __instance.Id, ReignCampaignIdentity.CurrentCampaignId(), __instance.AdditionalArgs);
            }
            catch (Exception ex) { Debug.Print("[AIPortraits] Native image identity binding failed: " + ex.Message); }
        }
    }

    private static void Remember(CharacterObject character, CharacterCode code)
    {
        try
        {
            // Hidden/unmet heroes return an empty code; do not reveal them or replace non-hero troops.
            if (character?.HeroObject == null || code == null || code.IsEmpty
                || !ReignCampaignIdentity.HasActiveCampaign()) return;
            NativePortraitIdentity.Remember(code, code.Code, ReignCampaignIdentity.CurrentCampaignId(),
                CharacterCacheId.ForHero(character.HeroObject));
        }
        catch (Exception ex) { Debug.Print("[AIPortraits] Native character identity capture failed: " + ex.Message); }
    }
}
