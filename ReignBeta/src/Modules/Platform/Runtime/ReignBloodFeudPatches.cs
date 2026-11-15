using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace ReignBeta.Runtime
{
    // Keep native save fields and explicit execution orders, while disabling the
    // clan-feud machinery and automatic execution queues inherited from old saves.
    internal static class ReignBloodFeudPatches
    {
        internal const string Owner = "com.bannerlordreign.blood-feuds-disabled.v1";
        private static Harmony _harmony;
        private static FieldInfo _settlementExecutions, _pendingExecutions, _mainHeroExecution;

        internal static void Apply()
        {
            if (_harmony != null) return;
            var harmony = new Harmony(Owner);
            try
            {
                Type behavior = typeof(ExecutionCampaignBehavior);
                _settlementExecutions = RequiredField(behavior, "_pendingClanMemberSettlementExecutions", typeof(IDictionary));
                _pendingExecutions = RequiredField(behavior, "_heroesPendingMapEventEndToBeExecuted", typeof(IList));
                _mainHeroExecution = RequiredField(behavior, "_isMainHeroExecuted", typeof(bool));
                Patch(harmony, AccessTools.PropertyGetter(typeof(Clan), nameof(Clan.HasBloodFeudWithPlayer)), nameof(NoActiveFeud));
                Patch(harmony, AccessTools.PropertySetter(typeof(Clan), nameof(Clan.HasBloodFeudWithPlayer)), nameof(KeepFeudInactive));
                Patch(harmony, Required(typeof(ChangeBloodFeudStateAction), "ApplyInternal"), nameof(SkipFeudOperation));
                foreach (string method in new[] {
                    "OnPlayerExecutedHero", "OnPlayerClanMemberExecuted", "OnBloodFeudStateChanged",
                    "OnHeroTakenPrisoner", "OnPrisonerDonatedToSettlement", "DailyTickHero", "OnGameMenuOpened"
                }) Patch(harmony, Required(behavior, method), nameof(SkipFeudOperation));
                Patch(harmony, Required(behavior, "OnSessionLaunched"), nameof(SkipFeudSession));
                Patch(harmony, Required(behavior, "QuarterHourlyTick"), nameof(DiscardFeudQueues));
                Patch(harmony, Required(behavior, "CanHeroBeReleased"), nameof(PreserveExplicitReleaseVeto));
                harmony.Patch(Required(behavior, "SyncData"), postfix:
                    new HarmonyMethod(typeof(ReignBloodFeudPatches), nameof(AfterSyncData)));
                _harmony = harmony;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(Owner);
                throw new InvalidOperationException("Reign could not disable the native blood-feud system. Check the supported Bannerlord version.", ex);
            }
        }

        internal static void Unapply()
        {
            _harmony?.UnpatchAll(Owner);
            _harmony = null;
        }

        private static MethodInfo Required(Type type, string name)
            => AccessTools.DeclaredMethod(type, name) ?? throw new MissingMethodException(type.FullName, name);

        private static FieldInfo RequiredField(Type type, string name, Type shape)
        {
            var field = AccessTools.DeclaredField(type, name);
            if (field == null || !shape.IsAssignableFrom(field.FieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void Patch(Harmony harmony, MethodBase target, string prefix)
        {
            if (target == null) throw new MissingMethodException("A required native blood-feud member is absent.");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(ReignBloodFeudPatches), prefix));
        }

        private static bool NoActiveFeud(ref bool __result) { __result = false; return false; }
        private static void KeepFeudInactive(ref bool value) { value = false; }
        private static bool SkipFeudOperation() => false;

        private static bool SkipFeudSession(ExecutionCampaignBehavior __instance)
        {
            // Clear persisted flags too: native callers may inline this trivial
            // property getter. Historical counters and relations remain intact.
            ClearClanFeuds(TaleWorlds.CampaignSystem.Campaign.Current == null ? null : Clan.All);
            DiscardFeudQueues(__instance);
            // Only feud broker and automatic execution dialogue is registered here.
            return false;
        }

        private static void ClearClanFeuds(IEnumerable<Clan> clans)
        {
            if (clans == null) return;
            foreach (var clan in clans)
                if (clan != null) clan.HasBloodFeudWithPlayer = false;
        }

        private static void AfterSyncData(ExecutionCampaignBehavior __instance, IDataStore store)
        {
            if (store.IsLoading) DiscardFeudQueues(__instance);
        }

        private static bool PreserveExplicitReleaseVeto(ExecutionCampaignBehavior __instance, Hero hero, ref bool result)
        {
            DiscardFeudQueues(__instance);
            // This listener may veto release only for a retained explicit order.
            // Leave an earlier listener's refusal untouched.
            if (((IList)_pendingExecutions.GetValue(__instance))?.Contains(hero) == true)
                result = false;
            return false;
        }

        private static void DiscardFeudQueues(ExecutionCampaignBehavior __instance)
        {
            ((IDictionary)_settlementExecutions.GetValue(__instance))?.Clear();
            _mainHeroExecution.SetValue(__instance, false);
            var pending = (IList)_pendingExecutions.GetValue(__instance);
            if (pending == null) return;
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                // The same list holds explicit player orders. Preserve their native
                // death marks and the ordinary quarter-hour completion callback.
                var hero = pending[i] as Hero;
                if (hero == null || hero.DeathMark != KillCharacterAction.KillCharacterActionDetail.ExecutionAfterMapEvent)
                    pending.RemoveAt(i);
            }
        }
    }
}
