using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.CampaignSystem.ViewModelCollection.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.WorldSimulation.Economy
{
    internal static partial class ReignEconomyPatches
    {
        private static bool AutomaticUpgradePrefix(PartyBase party, object[] __args)
        {
            if (party == PartyBase.MainParty) return true;
            var mobile = party?.MobileParty;
            var payer = UpgradePayer(party?.Owner, party?.LeaderHero);
            if (mobile == null || payer?.Clan == null) return true; // Native bandits/other non-clan parties retain their rules.
            object upgradeArgs = __args[2];
            Type type = upgradeArgs.GetType();
            int count = (int)AccessTools.Field(type, "PossibleUpgradeCount").GetValue(upgradeArgs);
            int cost = (int)AccessTools.Field(type, "UpgradeGoldCost").GetValue(upgradeArgs);
            var from = (CharacterObject)AccessTools.Field(type, "Target").GetValue(upgradeArgs);
            var to = (CharacterObject)AccessTools.Field(type, "UpgradeTarget").GetValue(upgradeArgs);
            int allowed = ReignWarEconomyPolicy.AffordableCountExact(count, payer.Gold, payer.Clan.Gold,
                ReignWarEconomyTransactions.ClanWages(payer.Clan), payer.Clan.AutoRecruitmentExpenses, cost,
                ReignWarEconomyPolicy.Options.ReserveDays(true), amount => ReignWarEconomyTransactions.AdditionalWages(mobile, to, amount, from));
            if (allowed < count)
            {
                ReignWarEconomyTransactions.Totals("automatic_upgrade").Blocked += count - allowed;
                if (allowed <= 0) return false;
                int xp = (int)AccessTools.Field(type, "UpgradeXpCost").GetValue(upgradeArgs);
                float chance = (float)AccessTools.Field(type, "UpgradeChance").GetValue(upgradeArgs);
                __args[2] = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new object[] { from, to, allowed, cost, xp, chance }, null);
            }
            return true;
        }

        private static IEnumerable<CodeInstruction> CommodityBaselineTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var level = AccessTools.Method(typeof(Village), "GetHearthLevel");
            int replacements = 0;
            for (int i = 0; i + 5 < code.Count; i++)
            {
                if (!code[i].Calls(level) || code[i + 1].opcode != OpCodes.Ldc_I4_1 || code[i + 2].opcode != OpCodes.Add
                    || code[i + 3].opcode != OpCodes.Conv_R4 || code[i + 4].opcode != OpCodes.Ldc_R4
                    || !Equals(code[i + 4].operand, .5f) || code[i + 5].opcode != OpCodes.Mul) continue;
                code[i].opcode = OpCodes.Call;
                code[i].operand = AccessTools.Method(typeof(ReignVillageProductionCalculatorModel), nameof(ReignVillageProductionCalculatorModel.CommodityBaseline));
                for (int j = 1; j <= 5; j++) { code[i + j].opcode = OpCodes.Nop; code[i + j].operand = null; }
                replacements++;
            }
            if (replacements != 1) throw new InvalidOperationException("Native commodity hearth baseline changed; expected one exact IL match, got " + replacements);
            return code;
        }

        private static IEnumerable<CodeInstruction> NativeAiSpawnLocalityTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var gate = AccessTools.PropertyGetter(typeof(Settlement), nameof(Settlement.GatePosition));
            var spawn = AccessTools.Method(typeof(MobilePartyHelper), nameof(MobilePartyHelper.SpawnLordParty),
                new[] { typeof(Hero), typeof(CampaignVec2), typeof(float) });
            var gates = code.Where(x => x.Calls(gate)).ToArray();
            var spawns = code.Where(x => x.Calls(spawn)).ToArray();
            if (gates.Length != 1 || spawns.Length != 1)
                throw new InvalidOperationException("Native AI lord spawn locality call sites changed.");
            int gateIndex = code.IndexOf(gates[0]), spawnIndex = code.IndexOf(spawns[0]);
            if (spawnIndex <= gateIndex || code.Skip(gateIndex + 1).Take(spawnIndex - gateIndex)
                .Any(x => x.labels.Count != 0 || x.blocks.Count != 0 || x.opcode.FlowControl == FlowControl.Branch
                    || x.opcode.FlowControl == FlowControl.Cond_Branch))
                throw new InvalidOperationException("Native AI lord spawn locality no longer follows a straight call path.");
            // Leave the exact settlement on the stack alongside its native coordinates.
            var dup = new CodeInstruction(OpCodes.Dup);
            dup.labels.AddRange(gates[0].labels);
            dup.blocks.AddRange(gates[0].blocks);
            gates[0].labels.Clear();
            gates[0].blocks.Clear();
            code.Insert(gateIndex, dup);
            spawns[0].opcode = OpCodes.Call;
            spawns[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(SpawnLordPartyWithLocality));
            return code;
        }

        private static MobileParty SpawnLordPartyWithLocality(Hero owner, Settlement locality, CampaignVec2 position, float radius)
        {
            var previous = _nativeAiSpawn;
            _nativeAiSpawn = (owner, locality);
            try { return MobilePartyHelper.SpawnLordParty(owner, position, radius); }
            finally { _nativeAiSpawn = previous; }
        }

        private static void RansomQuotePrefix(out int __state)
        {
            __state = _ransomQuoteDepth;
            _ransomQuoteDepth++;
        }

        private static void CivilianConsumptionPrefix(Town town, Dictionary<ItemCategory, float> categoryDemand,
            out ReignWarEconomyFoodMarket.Consumption __state)
            => __state = ReignWarEconomyFoodMarket.Before(town, categoryDemand);

        private static void CivilianConsumptionPostfix(Dictionary<ItemCategory, int> saleLog,
            ReignWarEconomyFoodMarket.Consumption __state)
            => ReignWarEconomyFoodMarket.After(saleLog, __state);

        private static Exception RansomQuoteFinalizer(Exception __exception, int __state)
        {
            _ransomQuoteDepth = __state;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> NativeDailyFinanceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var give = AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters),
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(bool) });
            var wallet = AccessTools.PropertySetter(typeof(Kingdom), nameof(Kingdom.KingdomBudgetWallet));
            var payments = code.Where(x => x.Calls(give)).ToArray();
            var credits = code.Where(x => x.Calls(wallet)).ToArray();
            if (payments.Length != 1 || credits.Length != 1)
                throw new InvalidOperationException("Native clan finance payout or treasury credit call sites changed.");
            payments[0].opcode = OpCodes.Call;
            payments[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(ObserveNativeClanDailyPayout));
            credits[0].opcode = OpCodes.Call;
            credits[0].operand = AccessTools.Method(typeof(ReignEconomyPatches), nameof(ObserveNativeKingdomBudgetCredit));
            return code;
        }

        private static EconomyFinanceTotals LiveFinanceTotals(Clan clan)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null || clan == null || TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted != true) return null;
            if (!state.Finance.TryGetValue(clan.StringId, out var totals))
                state.Finance[clan.StringId] = totals = new EconomyFinanceTotals();
            return totals;
        }

        private static void ObserveNativeClanDailyPayout(Hero giver, Hero receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Gold ?? 0;
            GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, disableNotification);
            if (giver != null || receiver == null || receiver.Clan?.Leader != receiver) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            if (totals == null) return;
            totals.NativeClanPayoutNet += (double)receiver.Gold - before;
            totals.NativeClanPayouts++;
        }

        private static void ObserveNativeKingdomBudgetCredit(Kingdom kingdom, int value)
        {
            int before = kingdom.KingdomBudgetWallet;
            kingdom.KingdomBudgetWallet = value;
            double added = (double)kingdom.KingdomBudgetWallet - before;
            if (added <= 0) return;
            var totals = LiveFinanceTotals(kingdom.RulingClan);
            if (totals == null) return;
            totals.NativeKingdomBudgetCredits += added;
            totals.NativeKingdomBudgetCreditCount++;
        }

        private static bool NpcCommanderEligibilityPrefix(Hero hero, ref float __result)
        {
            // Native's fallback pass omits CanLeadParty, but both passes reject a zero score.
            if (ReignMilitaryServiceEligibility.CanEstablishNpcParty(hero)) return true;
            __result = 0f;
            return false;
        }

        private static bool NativeKingdomSupportPrefix(Clan clan, bool applyWithdrawals, out int? __state)
        {
            __state = null;
            if (!ReignMilitaryServiceEligibility.CanReceiveKingdomSupport(clan)) return false;
            __state = applyWithdrawals ? clan.Kingdom.KingdomBudgetWallet : (int?)null;
            return true;
        }

        private static void NativeKingdomSupportPostfix(Clan clan, int? __state)
        {
            if (!__state.HasValue || clan?.Kingdom == null) return;
            double withdrawal = (double)__state.Value - clan.Kingdom.KingdomBudgetWallet;
            if (withdrawal <= 0) return;
            var totals = LiveFinanceTotals(clan);
            if (totals != null) totals.NativeKingdomSupportReceived += withdrawal;
        }

        private static void NativePartyIncomePostfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
        {
            if (!applyWithdrawals || __result <= 0 || party == null) return;
            var totals = LiveFinanceTotals(clan);
            if (totals == null) return;
            if (party.IsCaravan)
            {
                totals.NativeCaravanRemittances += __result;
                ReignNpcCaravanInvestments.ObserveRemittance(party, clan, __result);
            }
            else totals.NativeMilitaryPartyRemittances += __result;
        }

        private static void NativeLordPurseTopUpPostfix(MobileParty party, Clan clan, bool applyWithdrawals, int __result)
        {
            // The negative native result is the amount just credited to PartyTradeGold and billed to the clan.
            var lord = party?.LeaderHero;
            if (!applyWithdrawals || __result >= 0 || clan == null || party?.IsLordParty != true
                || lord == null || lord == clan.Leader || lord.Clan != clan) return;
            var totals = LiveFinanceTotals(clan);
            if (totals != null) totals.NativeLordPurseTopUps -= (double)__result;
        }

        private static void NativeTownMarketBalancePrefix(Town town, out int __state) => __state = town?.Gold ?? 0;

        private static void NativeTownMarketBalancePostfix(Town town, int __state)
        {
            if (town == null) return;
            double delta = (double)town.Gold - __state;
            if (delta == 0) return;
            var totals = LiveFinanceTotals(town.OwnerClan);
            if (totals == null) return;
            totals.NativeTownMarketCredits += Math.Max(0, delta);
            totals.NativeTownMarketDebits += Math.Max(0, -delta);
        }

        private static void PatchFinanceTranspiler(Type type, string nativeMethod, string patchMethod)
        {
            var method = AccessTools.Method(type, nativeMethod) ?? throw new MissingMethodException(type.FullName, nativeMethod);
            _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), patchMethod));
            PatchedHooks.Add(patchMethod);
            HookTargets[patchMethod] = method;
        }

        private static void ObserveCall(List<CodeInstruction> code, MethodInfo original, string observer, int expectedCalls = 1)
        {
            var calls = original == null ? Array.Empty<CodeInstruction>() : code.Where(x => x.Calls(original)).ToArray();
            if (calls.Length != expectedCalls) throw new InvalidOperationException("Native finance call site changed: " + observer);
            foreach (var call in calls)
            {
                call.opcode = OpCodes.Call;
                call.operand = AccessTools.Method(typeof(ReignEconomyPatches), observer);
            }
        }

        private static IEnumerable<CodeInstruction> NativePrisonerPayoutTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            ObserveCall(code, AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters),
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(bool) }), nameof(ObservePrisonerHeroPayout));
            ObserveCall(code, AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyForPartyToSettlement),
                new[] { typeof(PartyBase), typeof(Settlement), typeof(int), typeof(bool) }), nameof(ObservePrisonerSettlementPayout));
            return code;
        }

        private static void ObservePrisonerHeroPayout(Hero giver, Hero receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Gold ?? 0;
            GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, disableNotification);
            if (giver != null || receiver?.Clan == null) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            double credit = Math.Max(0, (double)receiver.Gold - before);
            if (totals == null || credit <= 0) return;
            if (receiver == receiver.Clan.Leader) totals.NativePrisonerAwardsToClanLeader += credit;
            else totals.NativePrisonerAwardsToOtherHeroes += credit;
        }

        private static void ObservePrisonerSettlementPayout(PartyBase giver, Settlement receiver, int amount, bool disableNotification)
        {
            int before = receiver?.Town?.Gold ?? 0;
            GiveGoldAction.ApplyForPartyToSettlement(giver, receiver, amount, disableNotification);
            if (giver != null || receiver?.Town == null) return;
            var totals = LiveFinanceTotals(receiver.OwnerClan);
            if (totals != null) totals.NativePrisonerAwardsToSettlement += Math.Max(0, (double)receiver.Town.Gold - before);
        }

        private static IEnumerable<CodeInstruction> NativeLootSaleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            ObserveCall(code, AccessTools.Method(typeof(SellItemsAction), nameof(SellItemsAction.Apply),
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) }), nameof(ObserveLordLootSale));
            return code;
        }

        private static void ObserveLordLootSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement)
            => ObserveLordMarketSale(seller, buyer, item, amount, settlement, false);

        private static IEnumerable<CodeInstruction> NativeHorseMarketSaleTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            // This native method sells non-food cargo as well as spare mounts. Its separate BuyHorses method stays native.
            ObserveCall(code, AccessTools.Method(typeof(SellItemsAction), nameof(SellItemsAction.Apply),
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) }),
                nameof(ObserveLordHorseMarketSale), 2);
            return code;
        }

        private static void ObserveLordHorseMarketSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement)
            => ObserveLordMarketSale(seller, buyer, item, amount, settlement, true);

        private static void ObserveLordMarketSale(PartyBase seller, PartyBase buyer, ItemRosterElement item, int amount, Settlement settlement, bool horseMarket)
        {
            var receiver = seller?.LeaderHero;
            int before = receiver?.Gold ?? 0;
            if (seller?.MobileParty != null && settlement?.Town != null && buyer?.Settlement == settlement)
            {
                // Native market sales may overdraw town gold across successive stacks. Quote
                // and sell one actual item at a time against the remaining wallet, so an
                // exhausted market cannot create gold and unsold equipment stays with its owner.
                for (int i = 0; i < Math.Min(Math.Max(0, amount), item.Amount); i++)
                {
                    int price = settlement.Town.GetItemPrice(item.EquipmentElement, seller.MobileParty, true);
                    if (price < 0 || price > settlement.Town.Gold) break;
                    SellItemsAction.Apply(seller, buyer, item, 1, settlement);
                }
            }
            else SellItemsAction.Apply(seller, buyer, item, amount, settlement);
            if (receiver?.Clan == null || seller.MobileParty?.IsLordParty != true) return;
            var totals = LiveFinanceTotals(receiver.Clan);
            double credit = Math.Max(0, (double)receiver.Gold - before);
            if (totals == null || credit <= 0) return;
            if (horseMarket)
            {
                if (receiver == receiver.Clan.Leader) totals.NativeLordHorseMarketSalesToClanLeader += credit;
                else totals.NativeLordHorseMarketSalesToOtherLords += credit;
            }
            else if (receiver == receiver.Clan.Leader) totals.NativeLordLootSalesToClanLeader += credit;
            else totals.NativeLordLootSalesToOtherLords += credit;
        }

        private static bool PatrolCreationPrefix(Settlement homeSettlement)
        {
            return homeSettlement?.Town?.GarrisonParty != null
                && homeSettlement.Town.GarrisonParty.MemberRoster.TotalHealthyCount >= MinimumPatrolSize;
        }

        private static void PatrolCreationPostfix(ref MobileParty __result)
        {
            if (__result?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return;
            }

            int targetSize = __result.MemberRoster.TotalRegulars;
            __result.MemberRoster.Clear();
            int transferred = TransferHealthyGarrisonTroops(
                __result.PatrolPartyComponent.HomeSettlement,
                __result,
                targetSize);
            if (transferred < MinimumPatrolSize)
            {
                ReignLog.Warn("Patrol creation produced fewer than " + MinimumPatrolSize + " transferred garrison troops.");
            }
        }

        private static bool PatrolReplenishmentPrefix(MobileParty party)
        {
            if (party?.PatrolPartyComponent?.HomeSettlement == null)
            {
                return true;
            }

            Settlement home = party.PatrolPartyComponent.HomeSettlement;
            PartyTemplateObject template = TaleWorlds.CampaignSystem.Campaign.Current.Models.SettlementPatrolModel
                .GetPartyTemplateForPatrolParty(home, party.PatrolPartyComponent.IsNaval);
            int targetSize = TaleWorlds.CampaignSystem.Campaign.Current.Models.PartySizeLimitModel
                .FindAppropriateInitialRosterForMobileParty(party, template)
                .TotalManCount;

            ReturnPatrolTroopsToGarrison(party, home);
            TransferHealthyGarrisonTroops(home, party, targetSize);
            party.PatrolPartyComponent.SortRoster();
            return false;
        }

        private static void PatrolRemovalPrefix(Settlement settlement)
        {
            MobileParty patrol = settlement?.PatrolParty?.MobileParty;
            if (patrol != null && patrol.MapEvent == null)
            {
                ReturnPatrolTroopsToGarrison(patrol, settlement);
            }
        }

        private static int TransferHealthyGarrisonTroops(Settlement home, MobileParty patrol, int targetSize)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (garrison == null || patrol == null || targetSize <= 0)
            {
                return 0;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            int remaining = targetSize;
            List<TroopRosterElement> candidates = garrison.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero && element.Number > element.WoundedNumber)
                .OrderBy(element => element.Character.Tier)
                .ToList();
            foreach (TroopRosterElement element in candidates)
            {
                int healthy = element.Number - element.WoundedNumber;
                int transfer = Math.Min(healthy, remaining);
                if (transfer <= 0)
                {
                    continue;
                }

                garrison.MemberRoster.AddToCounts(element.Character, -transfer);
                patrol.MemberRoster.AddToCounts(element.Character, transfer);
                remaining -= transfer;
                if (remaining == 0)
                {
                    break;
                }
            }

            int transferred = targetSize - remaining;
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignServices.Economy?.RecordPatrolTransfer(
                transferred, totalBefore != totalAfter);
            return transferred;
        }

        private static void ReturnPatrolTroopsToGarrison(MobileParty patrol, Settlement home)
        {
            MobileParty garrison = home?.Town?.GarrisonParty;
            if (patrol == null || garrison == null)
            {
                return;
            }

            int totalBefore = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            List<TroopRosterElement> returning = patrol.MemberRoster.GetTroopRoster()
                .Where(element => !element.Character.IsHero)
                .ToList();
            int returned = 0;
            foreach (TroopRosterElement element in returning)
            {
                returned += element.Number;
                garrison.MemberRoster.AddToCounts(
                    element.Character,
                    element.Number,
                    false,
                    element.WoundedNumber,
                    element.Xp);
                patrol.MemberRoster.AddToCounts(
                    element.Character,
                    -element.Number,
                    false,
                    -element.WoundedNumber,
                    -element.Xp);
            }
            int totalAfter = garrison.MemberRoster.TotalManCount + patrol.MemberRoster.TotalManCount;
            ReignServices.Economy?.RecordPatrolReturn(
                returned, totalBefore != totalAfter);
        }

        private static CharacterObject ChooseWeightedTroop(List<(CharacterObject Troop, float Weight)> candidates)
        {
            float total = candidates.Sum(candidate => candidate.Weight);
            float roll = MBRandom.RandomFloat * total;
            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll <= 0f)
                {
                    return candidate.Troop;
                }
            }

            return candidates[candidates.Count - 1].Troop;
        }
    }
}
