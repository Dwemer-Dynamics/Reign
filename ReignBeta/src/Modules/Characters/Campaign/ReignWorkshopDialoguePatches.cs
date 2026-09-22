using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Localization;

namespace ReignBeta.Campaign
{
    // Native eligibility compares two settlements for equality: null == null also
    // admits a recruited workshop owner on the map. Reject that branch before
    // ConversationManager evaluates its clickable condition.
    internal static class ReignWorkshopDialoguePatches
    {
        private static Harmony _harmony;

        internal static void Apply()
        {
            if (_harmony != null) return;
            var harmony = new Harmony("com.bannerlordreign.workshop_dialogue_context");
            var owner = typeof(WorkshopsCharactersCampaignBehavior);
            var patches = typeof(ReignWorkshopDialoguePatches);
            foreach (string method in new[]
            {
                "workshop_notable_owner_begin_single_on_condition",
                "workshop_notable_owner_begin_multiple_on_condition"
            })
                harmony.Patch(AccessTools.Method(owner, method)
                    ?? throw new MissingMethodException(owner.FullName, method),
                    prefix: new HarmonyMethod(patches, nameof(BeforeOwnerEligibility)));
            harmony.Patch(AccessTools.Method(owner, "player_war_status_clickable_condition")
                ?? throw new MissingMethodException(owner.FullName, "player_war_status_clickable_condition"),
                prefix: new HarmonyMethod(patches, nameof(BeforeWarStatus)));
            harmony.Patch(AccessTools.Method(owner, "can_player_buy_workshop_clickable_condition")
                ?? throw new MissingMethodException(owner.FullName, "can_player_buy_workshop_clickable_condition"),
                prefix: new HarmonyMethod(patches, nameof(BeforePurchase)));
            _harmony = harmony;
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(_harmony.Id);
            _harmony = null;
        }

        private static bool TryGetContext(out Settlement settlement, out IFaction playerFaction)
        {
            settlement = null;
            playerFaction = null;
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || CharacterObject.PlayerCharacter == null)
                return false;
            settlement = Settlement.CurrentSettlement;
            playerFaction = Hero.MainHero?.MapFaction;
            return settlement?.Town != null && settlement.MapFaction != null && playerFaction != null;
        }

        private static bool BeforeOwnerEligibility(ref bool __result)
        {
            if (TryGetContext(out Settlement settlement, out IFaction _)
                && Hero.OneToOneConversationHero?.CurrentSettlement == settlement) return true;
            __result = false;
            return false;
        }

        private static bool BeforeWarStatus(ref bool __result, out TextObject explanation)
        {
            // Use one settlement snapshot, including when another mod overrides
            // its getter. Preserve the native war rule without a second lookup.
            bool available = TryGetContext(out Settlement settlement, out IFaction playerFaction);
            bool enemy = available && playerFaction.IsAtWarWith(settlement.MapFaction);
            __result = available && !enemy;
            explanation = __result ? null : new TextObject(enemy
                ? "{=QkiqdcKa}You cannot own a workshop in an enemy town."
                : "A workshop purchase requires a conversation in a town.");
            return false;
        }

        private static bool BeforePurchase(Workshop workshop, ref bool __result, out TextObject explanation)
        {
            explanation = null;
            if (TryGetContext(out Settlement _, out IFaction _) && workshop?.Owner != null
                && workshop.WorkshopType != null && Clan.PlayerClan != null) return true;
            __result = false;
            explanation = new TextObject("This workshop is unavailable for purchase in the current conversation.");
            return false;
        }
    }
}
