using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace ReignBeta.PartyAgency
{
    internal static class ReignTemporaryPartyGuestPatches
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.temporary-party-guests";
        private static readonly HashSet<string> PatchedHooks = new HashSet<string>(StringComparer.Ordinal);
        private static Harmony _harmony;
        private static string _lastPatchError = string.Empty;

        internal static void Apply()
        {
            if (_harmony != null) return;
            try
            {
                PatchedHooks.Clear();
                _lastPatchError = string.Empty;
                _harmony = new Harmony(HarmonyId);
                Patch(typeof(DisbandPartyAction), nameof(DisbandPartyAction.StartDisband),
                    nameof(ProtectedPartyPrefix), new[] { typeof(MobileParty) });
                Patch(typeof(DestroyPartyAction), nameof(DestroyPartyAction.Apply),
                    nameof(DestroyPartyPrefix), new[] { typeof(PartyBase), typeof(MobileParty) });
                Patch(typeof(DestroyPartyAction), nameof(DestroyPartyAction.ApplyForDisbanding),
                    nameof(DestroyDisbandingPartyPrefix), null);
                Patch(typeof(EncounterManager), nameof(EncounterManager.StartPartyEncounter),
                    nameof(StartPartyEncounterPrefix), new[] { typeof(PartyBase), typeof(PartyBase) });
                Patch(typeof(MapEvent), nameof(MapEvent.CanPartyJoinBattle),
                    nameof(CanPartyJoinBattlePrefix),
                    new[] { typeof(PartyBase), typeof(BattleSideEnum) });
                Patch(typeof(MapEventSide), "AddNearbyPartyToPlayerMapEvent",
                    nameof(AddNearbyPartyToPlayerMapEventPrefix),
                    new[] { typeof(MobileParty) });
                Patch(typeof(FoodConsumptionBehavior), "DailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(DesertionCampaignBehavior), "DailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(PartyUpgraderCampaignBehavior), "DailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(RecruitPrisonersCampaignBehavior), "DailyTickAIMobileParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(FindingItemOnMapBehavior), "DailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(CampaignBattleRecoveryBehavior), "DailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(MobilePartyTrainingBehavior), "HourlyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(MobilePartyTrainingBehavior), "OnDailyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(PartiesBuyFoodCampaignBehavior), "HourlyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(DiscardItemsCampaignBehavior), "OnHourlyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(PrisonerReleaseCampaignBehavior), "HourlyPartyTick", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(RecruitmentCampaignBehavior), "HourlyTickParty", nameof(ProtectedPartyPrefix), null);
                Patch(typeof(DefaultClanFinanceModel), "AddExpenseFromLeaderParty", nameof(LeaderPartyExpensePrefix), null);
                Patch(typeof(DefaultClanFinanceModel), "AddPartyExpense", nameof(ProtectedPartyExpensePrefix), null);
                ReignLog.Info("Temporary noble guest camp protections loaded hooks=" + PatchedHooks.Count + ".");
            }
            catch (Exception ex)
            {
                _lastPatchError = ex.ToString();
                ReignLog.Warn("Temporary noble guest patch setup failed: " + ex);
                _harmony?.UnpatchAll(HarmonyId);
                _harmony = null;
            }
        }

        internal static JObject BuildDiagnostics()
        {
            return new JObject
            {
                ["active"] = _harmony != null,
                ["patchedHookCount"] = PatchedHooks.Count,
                ["patchedHooks"] = new JArray(PatchedHooks.OrderBy(x => x)),
                ["lastPatchError"] = _lastPatchError ?? string.Empty
            };
        }

        internal static JObject BuildProtectedCampSafetyEvidence(MobileParty party, Clan guestClan)
        {
            string[] requiredHooks =
            {
                nameof(DefaultClanFinanceModel) + ".AddExpenseFromLeaderParty",
                nameof(DefaultClanFinanceModel) + ".AddPartyExpense",
                nameof(EncounterManager) + ".StartPartyEncounter",
                nameof(MapEvent) + ".CanPartyJoinBattle",
                nameof(MapEventSide) + ".AddNearbyPartyToPlayerMapEvent"
            };
            bool hooksInstalled = requiredHooks.All(PatchedHooks.Contains);
            bool protectedCamp = IsProtected(party);
            bool partyExpenseSuppressed = ShouldSuppressProtectedPartyExpense(party);
            bool leaderExpenseSuppressed = ShouldSuppressLeaderPartyExpense(guestClan);
            bool encounterBlockedAsAttacker = ShouldBlockEncounter(
                party?.Party, MobileParty.MainParty?.Party);
            bool encounterBlockedAsDefender = ShouldBlockEncounter(
                MobileParty.MainParty?.Party, party?.Party);
            bool battleJoinBlocked = ShouldBlockBattleJoin(party?.Party);
            bool nearbyReinforcementBlocked = ShouldBlockNearbyReinforcement(party);
            bool ok = _harmony != null && hooksInstalled && protectedCamp
                && partyExpenseSuppressed && leaderExpenseSuppressed
                && encounterBlockedAsAttacker && encounterBlockedAsDefender
                && battleJoinBlocked && nearbyReinforcementBlocked;
            return new JObject
            {
                ["ok"] = ok,
                ["harmonyActive"] = _harmony != null,
                ["requiredHooksInstalled"] = hooksInstalled,
                ["requiredHooks"] = new JArray(requiredHooks),
                ["protectedCampPredicate"] = protectedCamp,
                ["protectedPartyExpenseSuppressed"] = partyExpenseSuppressed,
                ["guestClanLeaderExpenseSuppressed"] = leaderExpenseSuppressed,
                ["encounterBlockedAsAttacker"] = encounterBlockedAsAttacker,
                ["encounterBlockedAsDefender"] = encounterBlockedAsDefender,
                ["battleJoinBlocked"] = battleJoinBlocked,
                ["nearbyReinforcementBlocked"] = nearbyReinforcementBlocked
            };
        }

        private static void Patch(Type targetType, string targetName, string patchName, Type[] parameterTypes)
        {
            var target = parameterTypes == null
                ? AccessTools.Method(targetType, targetName)
                : AccessTools.Method(targetType, targetName, parameterTypes);
            if (target == null) throw new MissingMethodException(targetType.FullName, targetName);
            _harmony.Patch(target, prefix: new HarmonyMethod(typeof(ReignTemporaryPartyGuestPatches), patchName));
            PatchedHooks.Add(targetType.Name + "." + targetName);
        }

        private static bool ProtectedPartyPrefix(MobileParty __0)
        {
            return !IsProtected(__0);
        }

        private static bool DestroyPartyPrefix(MobileParty destroyedParty)
        {
            return !IsProtected(destroyedParty);
        }

        private static bool DestroyDisbandingPartyPrefix(MobileParty disbandedParty)
        {
            return !IsProtected(disbandedParty);
        }

        private static bool StartPartyEncounterPrefix(PartyBase attackerParty, PartyBase defenderParty)
        {
            return !ShouldBlockEncounter(attackerParty, defenderParty);
        }

        private static bool CanPartyJoinBattlePrefix(PartyBase __0, ref bool __result)
        {
            if (!ShouldBlockBattleJoin(__0)) return true;
            __result = false;
            return false;
        }

        private static bool AddNearbyPartyToPlayerMapEventPrefix(MobileParty __0)
        {
            return !ShouldBlockNearbyReinforcement(__0);
        }

        private static bool LeaderPartyExpensePrefix(Clan clan, ref int __result)
        {
            if (!ShouldSuppressLeaderPartyExpense(clan))
                return true;
            __result = 0;
            return false;
        }

        private static bool ProtectedPartyExpensePrefix(MobileParty party, ref int __result)
        {
            if (!ShouldSuppressProtectedPartyExpense(party)) return true;
            __result = 0;
            return false;
        }

        private static bool ShouldBlockEncounter(PartyBase attackerParty, PartyBase defenderParty)
        {
            return IsProtected(attackerParty?.MobileParty)
                || IsProtected(defenderParty?.MobileParty);
        }

        private static bool ShouldBlockBattleJoin(PartyBase party)
        {
            return IsProtected(party?.MobileParty);
        }

        private static bool ShouldBlockNearbyReinforcement(MobileParty party)
        {
            return IsProtected(party);
        }

        private static bool ShouldSuppressLeaderPartyExpense(Clan clan)
        {
            return ReignTemporaryPartyGuestCampaignBehavior.Instance
                ?.IsGuestClanLeaderInMainParty(clan) == true;
        }

        private static bool ShouldSuppressProtectedPartyExpense(MobileParty party)
        {
            return IsProtected(party);
        }

        private static bool IsProtected(MobileParty party)
        {
            return ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(party) == true;
        }
    }
}
