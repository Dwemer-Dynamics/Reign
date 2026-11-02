using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    internal static class ReignShipTradeGovernorPatch
    {
        private const string Owner = "com.bannerlordreign.reignbeta.ship_trade_governor";
        private static Harmony _harmony;
        private static MethodInfo _target;

        internal static void Apply()
        {
            if (_harmony != null) return;
            // War Sails is optional. Do not add an assembly dependency to the client.
            var type = AccessTools.TypeByName("NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior");
            if (type == null) return;
            var harmony = new Harmony(Owner);
            try
            {
                var target = AccessTools.Method(type, "OnShipOwnerChanged", new[] {
                    typeof(Ship), typeof(PartyBase), typeof(ChangeShipOwnerAction.ShipOwnerChangeDetail) });
                if (target == null || target.IsStatic || target.ReturnType != typeof(void))
                    throw new InvalidOperationException("The native ship-trade callback signature changed.");
                harmony.Patch(target, transpiler: new HarmonyMethod(typeof(ReignShipTradeGovernorPatch), nameof(Transpiler)));
                _target = target; _harmony = harmony;
                ReignLog.Info("Ship-trade governor perks now use the trading port's settlement.");
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(Owner);
                ReignLog.Warn("Native ship-trade governor context patch is unavailable: " + ex.Message);
            }
        }

        internal static void Unapply()
        {
            if (_harmony == null) return;
            _harmony.Unpatch(_target, HarmonyPatchType.All, Owner);
            _harmony = null; _target = null;
        }

        internal static Settlement SelectTradePort(Settlement oldOwnerPort, Settlement newOwnerPort)
            => oldOwnerPort ?? newOwnerPort;

        // The original Hero receiver is already on the IL stack. Its location is
        // deliberately irrelevant: a governor can be travelling or visiting elsewhere.
        private static Settlement ResolveTradePort(Hero governor, Ship ship, PartyBase oldOwner)
            => SelectTradePort(oldOwner?.Settlement, ship?.Owner?.Settlement);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var source = instructions.ToList();
            var current = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.CurrentSettlement));
            var town = AccessTools.Field(typeof(Settlement), nameof(Settlement.Town));
            var sites = source.Select((instruction, index) => new { instruction, index })
                .Where(x => x.instruction.Calls(current)).Select(x => x.index).ToArray();
            if (sites.Length != 1 || sites[0] + 1 >= source.Count || source[sites[0] + 1].opcode != OpCodes.Ldfld
                || !Equals(source[sites[0] + 1].operand, town))
                throw new InvalidOperationException("Expected one native governor CurrentSettlement.Town perk lookup.");
            var resolve = AccessTools.Method(typeof(ReignShipTradeGovernorPatch), nameof(ResolveTradePort));
            for (int i = 0; i < source.Count; i++)
            {
                if (i != sites[0]) { yield return source[i]; continue; }
                var ship = new CodeInstruction(OpCodes.Ldarg_1);
                ship.labels.AddRange(source[i].labels);
                ship.blocks.AddRange(source[i].blocks.Where(b => b.blockType != ExceptionBlockType.EndExceptionBlock));
                var call = new CodeInstruction(OpCodes.Call, resolve);
                call.blocks.AddRange(source[i].blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock));
                yield return ship;
                yield return new CodeInstruction(OpCodes.Ldarg_2);
                yield return call;
            }
        }
    }
}
