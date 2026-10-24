using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        // The return lease must retire before native travel resumes. Otherwise
        // return cleanup can move a governor back after their queued trip fires.
        internal bool HasOwnedMeetingTeleportDeferral(string heroId) => !string.IsNullOrEmpty(heroId)
            && (_lifeDuties.ContainsKey(heroId) || IsMeetingHeld(heroId));

        private static void NormalizePresentMeetingAttendee(Hero hero, string destination)
        {
            var place = Settlement.Find(destination);
            if (place != null && Free(hero) && hero.PartyBelongedTo == null
                && hero.CurrentSettlement == place && hero.IsTraveling)
                hero.ChangeState(Hero.CharacterStates.Active);
        }
    }

    internal static class NpcMeetingQueuedTeleportPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.DeclaredMethod(typeof(DelayedTeleportationModel),
                nameof(DelayedTeleportationModel.CanPerformImmediateTeleport),
                new[] { typeof(Hero), typeof(MobileParty), typeof(Settlement) })
                ?? throw new MissingMethodException("Native delayed teleport eligibility is unavailable.");
            var replacement = AccessTools.DeclaredMethod(typeof(NpcMeetingQueuedTeleportPatch), nameof(CanPerformImmediateTeleport));
            var rows = instructions.Select(i => new CodeInstruction(i)).ToList();
            var sites = rows.Where(i => i.opcode == OpCodes.Callvirt && Equals(i.operand, original)).ToList();
            if (sites.Count != 1)
                throw new InvalidOperationException("Expected exactly one native queued teleport eligibility call; found " + sites.Count + ".");
            // Keep the original model/hero/party/settlement stack and every label
            // and exception boundary. No native queue or cancellation is touched.
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = replacement;
            return rows;
        }

        private static bool CanPerformImmediateTeleport(DelayedTeleportationModel model, Hero hero, MobileParty party, Settlement settlement)
        {
            // Dispatch the actual campaign model once, including custom models.
            // Native rejection always wins. Defer before the queue entry is removed.
            if (!model.CanPerformImmediateTeleport(hero, party, settlement)) return false;
            var lives = ReignNpcLivesBehavior.Instance;
            if (hero == null || lives == null || hero == Hero.MainHero
                || !lives.HasOwnedMeetingTeleportDeferral(hero.StringId)) return true;
            lives.ObserveMeetingMovement(hero, "queued_teleport_deferred", settlement?.StringId ?? party?.StringId ?? "",
                "native_queue_retained", allowed: false);
            return false;
        }
    }
}
