using System;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CraftingSystem;
using TaleWorlds.Core;

namespace ReignBeta.Compatibility
{
    // Extend the native load validator to cover the generator's later rejection.
    // The native consumer removes invalid pending orders; inventory is untouched.
    internal static class ReignCraftingOrderLoadPatches
    {
        internal const string Owner = "com.bannerlordreign.crafting-order-load.v1";
        private static bool _installed;
        private static FieldInfo _orderData, _craftedData;
        [ThreadStatic] private static int _restoreDepth;
        private static int _rejectedOrders;
        internal static int RejectedOrders => Volatile.Read(ref _rejectedOrders);

        internal static MethodInfo LoadHandler() => AccessTools.Method(typeof(CraftingCampaignBehavior),
            "TaleWorlds.CampaignSystem.CampaignBehaviors.INonReadyObjectHandler.OnBeforeNonReadyObjectsDeleted", Type.EmptyTypes);

        internal static void Apply()
        {
            if (_installed) return;
            var handler = LoadHandler();
            var validator = AccessTools.Method(typeof(CraftingOrder), nameof(CraftingOrder.IsPreCraftedWeaponDesignValid), Type.EmptyTypes);
            _orderData = AccessTools.Field(typeof(CraftingOrder), "_preCraftedWeaponDesignItemData");
            _craftedData = _orderData == null ? null : AccessTools.Field(_orderData.FieldType, "CraftedData");
            if (handler == null || validator == null || _craftedData?.FieldType != typeof(WeaponDesign))
                throw new MissingMemberException("Native crafting-order load validation layout changed.");
            var harmony = new Harmony(Owner);
            try
            {
                harmony.Patch(handler, prefix: new HarmonyMethod(typeof(ReignCraftingOrderLoadPatches), nameof(BeginRestore)),
                    finalizer: new HarmonyMethod(typeof(ReignCraftingOrderLoadPatches), nameof(EndRestore)));
                harmony.Patch(validator, postfix: new HarmonyMethod(typeof(ReignCraftingOrderLoadPatches), nameof(ValidateSavedOrder)));
                _installed = true;
            }
            catch { harmony.UnpatchAll(Owner); throw; }
        }

        internal static void Unapply()
        {
            new Harmony(Owner).UnpatchAll(Owner);
            _installed = false;
            _restoreDepth = 0;
        }

        private static void BeginRestore() { _restoreDepth++; }
        private static Exception EndRestore(Exception __exception)
        {
            if (_restoreDepth > 0) _restoreDepth--;
            return __exception; // Never swallow an unrelated loading exception.
        }

        private static void ValidateSavedOrder(CraftingOrder __instance, ref bool __result)
        {
            if (_restoreDepth == 0 || !__result || __instance == null) return;
            object data = _orderData.GetValue(__instance);
            var design = data == null ? null : _craftedData.GetValue(data) as WeaponDesign;
            string piece = IncompatiblePiece(design);
            if (piece == null) return;
            __result = false;
            Interlocked.Increment(ref _rejectedOrders);
            if (Game.Current != null)
                ReignLog.Warn("Discarding unrestorable pending smithing order during native load: item="
                    + (__instance.PreCraftedWeaponDesignItem?.StringId ?? "unknown")
                    + " template=" + design.Template.StringId + " piece=" + piece
                    + ". Native order cleanup will remove it; player inventory is unchanged by this repair.");
        }

        internal static string IncompatiblePiece(WeaponDesign design)
        {
            // Malformed metadata outside this known generator mismatch stays with
            // native validation; do not turn unknown failures into silent deletion.
            if (design?.Template?.Pieces == null || design.UsedPieces == null) return null;
            foreach (var element in design.UsedPieces)
            {
                var piece = element?.CraftingPiece;
                if (piece == null) continue;
                if ((element.IsValid && !design.Template.Pieces.Contains(piece))
                    || (piece.IsInitialized && !element.IsValid))
                    return piece.StringId ?? "unknown";
            }
            return null;
        }
    }
}
