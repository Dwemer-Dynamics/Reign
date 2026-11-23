using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TaleWorlds.Core;

namespace ReignBeta.Platform.Compatibility
{
    // Execute the licensed game's original IL; do not reproduce its algorithms.
    // Guards select those originals only for detached economic calculations.
    internal static class ReignRbmNativeCalculations
    {
        private static readonly DefaultItemValueModel ReferenceModel = new DefaultItemValueModel();
        private static readonly List<MethodBase> Guarded = new List<MethodBase>();
        private static string _owner;

        internal static void Install(Harmony harmony)
        {
            _owner = harmony.Id; Guarded.Clear();
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateValue", nameof(Value), nameof(GuardValue));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateTier", nameof(Tier), nameof(GuardTier));
            Bind(harmony, typeof(DefaultItemValueModel), "GetEquipmentValueFromTier", nameof(EquipmentValue), nameof(GuardEquipmentValue));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateHorseTier", nameof(HorseTier), nameof(GuardHorseTier));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateTierMeleeWeapon", nameof(MeleeTier), nameof(GuardMeleeTier));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateAmmoTier", nameof(AmmoTier), nameof(GuardAmmoTier));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateShieldTier", nameof(ShieldTier), nameof(GuardShieldTier));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateRangedWeaponTier", nameof(RangedTier), nameof(GuardRangedTier));
            Bind(harmony, typeof(DefaultItemValueModel), "CalculateArmorTier", nameof(ArmorTier), nameof(GuardArmorTier));
            Bind(harmony, typeof(CombatStatCalculator), "CalculateStrikeMagnitudeForThrust", nameof(Thrust), nameof(GuardThrust));
            Bind(harmony, typeof(CombatStatCalculator), "CalculateStrikeMagnitudeForSwing", nameof(Swing), nameof(GuardSwing));
            Bind(harmony, typeof(CombatStatCalculator), "CalculateBaseBlowMagnitudeForSwing", nameof(BaseSwing), nameof(GuardBaseSwing));
            harmony.Patch(AccessTools.PropertyGetter(typeof(ItemObject), nameof(ItemObject.Tierf)),
                prefix: Prefix(nameof(GuardTierf)));
            Guarded.Add(AccessTools.PropertyGetter(typeof(ItemObject), nameof(ItemObject.Tierf)));
        }

        private static HarmonyMethod Prefix(string name) => new HarmonyMethod(typeof(ReignRbmNativeCalculations), name) {
            priority = int.MaxValue, before = new[] { "com.rbmcombat" }
        };

        private static void Bind(Harmony harmony, Type owner, string originalName, string standinName, string guardName)
        {
            MethodInfo original = AccessTools.DeclaredMethod(owner, originalName);
            MethodInfo standin = AccessTools.DeclaredMethod(typeof(ReignRbmNativeCalculations), standinName);
            if (original == null || standin == null) throw new MissingMethodException(owner.FullName, originalName);
            harmony.CreateReversePatcher(original, new HarmonyMethod(standin)).Patch(HarmonyReversePatchType.Original);
            harmony.Patch(original, prefix: Prefix(guardName));
            Guarded.Add(original);
        }

        internal static void VerifyIsolation()
        {
            foreach (MethodBase method in Guarded) {
                Patches patches = Harmony.GetPatchInfo(method);
                if (patches == null) continue;
                bool foreignResults = patches.Postfixes.Concat(patches.Finalizers).Concat(patches.Transpilers).Any(p => p.owner != _owner);
                bool foreignPrefix = patches.Prefixes.Any(p => p.owner != _owner && p.owner != "com.rbmcombat");
                if (foreignResults || foreignPrefix)
                    throw new InvalidOperationException("Unreviewed economic calculation patch owner on " + method.DeclaringType?.FullName + "." + method.Name);
            }
        }

        private static bool GuardValue(ItemObject __0, ref int __result)
        {
            if (ReignRbmNativeEconomics.InShadow) { __result = Value(ReferenceModel, __0); return false; }
            if (!ReignRbmNativeEconomics.TryGet(__0, out var baseline)) return true;
            __result = baseline.Value;
            return false;
        }

        private static bool GuardTier(ItemObject __0, ref float __result)
        {
            if (ReignRbmNativeEconomics.InShadow) { __result = Tier(ReferenceModel, __0); return false; }
            if (!ReignRbmNativeEconomics.TryGet(__0, out var baseline)) return true;
            __result = baseline.Tier;
            return false;
        }

        private static bool GuardTierf(ItemObject __instance, ref float __result)
        {
            if (ReignRbmNativeEconomics.InShadow || !ReignRbmNativeEconomics.TryGet(__instance, out var baseline)) return true;
            __result = baseline.Tier;
            return false;
        }

        private static bool GuardEquipmentValue(float __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = EquipmentValue(ReferenceModel, __0); return false; }
        private static bool GuardHorseTier(HorseComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = HorseTier(ReferenceModel, __0); return false; }
        private static bool GuardMeleeTier(WeaponComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = MeleeTier(ReferenceModel, __0); return false; }
        private static bool GuardAmmoTier(WeaponComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = AmmoTier(ReferenceModel, __0); return false; }
        private static bool GuardShieldTier(WeaponComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = ShieldTier(ReferenceModel, __0); return false; }
        private static bool GuardRangedTier(WeaponComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = RangedTier(ReferenceModel, __0); return false; }
        private static bool GuardArmorTier(ArmorComponent __0, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = ArmorTier(ReferenceModel, __0); return false; }
        private static bool GuardThrust(float __0, float __1, float __2, bool __3, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = Thrust(__0, __1, __2, __3); return false; }
        private static bool GuardSwing(float __0, float __1, float __2, float __3, float __4, float __5, float __6, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = Swing(__0, __1, __2, __3, __4, __5, __6); return false; }
        private static bool GuardBaseSwing(float __0, float __1, float __2, float __3, float __4, float __5, float __6, ref float __result)
        { if (!ReignRbmNativeEconomics.InShadow) return true; __result = BaseSwing(__0, __1, __2, __3, __4, __5, __6); return false; }

        [MethodImpl(MethodImplOptions.NoInlining)] internal static int Value(DefaultItemValueModel model, ItemObject item) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float Tier(DefaultItemValueModel model, ItemObject item) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float EquipmentValue(DefaultItemValueModel model, float tier) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float HorseTier(DefaultItemValueModel model, HorseComponent horse) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float MeleeTier(DefaultItemValueModel model, WeaponComponent weapon) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float AmmoTier(DefaultItemValueModel model, WeaponComponent weapon) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float ShieldTier(DefaultItemValueModel model, WeaponComponent weapon) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float RangedTier(DefaultItemValueModel model, WeaponComponent weapon) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float ArmorTier(DefaultItemValueModel model, ArmorComponent armor) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float Thrust(float speed, float weight, float extraSpeed, bool thrown) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float Swing(float speed, float impact, float weight, float length, float inertia, float center, float extraSpeed) => throw Unbound();
        [MethodImpl(MethodImplOptions.NoInlining)] internal static float BaseSwing(float speed, float reach, float weight, float inertia, float center, float impact, float extraSpeed) => throw Unbound();
        private static Exception Unbound() => new InvalidOperationException("Native economic calculation has not been bound to the reviewed game assembly.");
    }
}
