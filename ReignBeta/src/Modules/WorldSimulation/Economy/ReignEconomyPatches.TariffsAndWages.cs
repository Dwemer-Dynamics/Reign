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
        private static void FoodProvisionPrefix(MobileParty mobileParty, Settlement settlement,
            out ReignWarEconomyTariffs.Scope __state)
            => __state = ReignWarEconomyTariffs.Enter(mobileParty, settlement, "native_food");

        private static void HorseProvisionPrefix(MobileParty mobileParty, Town town,
            out ReignWarEconomyTariffs.Scope __state)
            => __state = ReignWarEconomyTariffs.Enter(mobileParty, town?.Settlement, "native_horses");

        private static Exception ProvisionFinalizer(Exception __exception, ReignWarEconomyTariffs.Scope __state)
        { ReignWarEconomyTariffs.Leave(__state); return __exception; }

        private static Exception HorseProvisionFinalizer(Exception __exception, ReignWarEconomyTariffs.Scope __state)
        { ReignWarEconomyTariffs.Leave(__state); return __exception; }

        private static void TariffPurchasePrefix(PartyBase sellerParty, PartyBase buyerParty,
            ItemRosterElement itemRosterElement, int number, out ReignWarEconomyTariffs.Sale __state)
            => __state = ReignWarEconomyTariffs.Before(sellerParty, buyerParty, itemRosterElement, number);

        private static Exception TariffPurchaseFinalizer(Exception __exception, ReignWarEconomyTariffs.Sale __state)
        { ReignWarEconomyTariffs.After(__state, __exception); return __exception; }

        private static void TariffPoolPrefix(Town __instance, out int? __state)
            => __state = ReignWarEconomyTariffs.PoolBefore(__instance);

        private static void TariffPoolPostfix(Town __instance, int? __state)
        {
            try { ReignWarEconomyTariffs.PoolAfter(__instance, __state); }
            catch (Exception ex)
            {
                if (ReignWarEconomyTransactions.State != null) ReignWarEconomyTransactions.State.TariffUnsupported++;
                ReignLog.Warn("Tariff pool observation failed: " + ex.GetType().Name);
            }
        }

        private static IEnumerable<CodeInstruction> TariffPurchaseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var ratio = AccessTools.Method(typeof(SettlementTaxModel), nameof(SettlementTaxModel.GetTownTaxRatio));
            var price = AccessTools.Method(typeof(SettlementComponent), nameof(SettlementComponent.GetItemPrice),
                new[] { typeof(EquipmentElement), typeof(MobileParty), typeof(bool) });
            var stock = AccessTools.Method(typeof(ItemRoster), nameof(ItemRoster.AddToCounts),
                new[] { typeof(EquipmentElement), typeof(int) });
            var rounding = AccessTools.Method(typeof(MBRandom), nameof(MBRandom.RoundRandomized), new[] { typeof(float) });
            RequirePurchaseLoop(code, price, stock);
            int ratios = 0, rounds = 0, prices = 0, stocks = 0;
            foreach (var instruction in code)
            {
                if (instruction.Calls(price))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.QuotePrice));
                    prices++;
                }
                else if (instruction.Calls(ratio))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.TaxRatio));
                    ratios++;
                }
                else if (instruction.Calls(rounding))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.RoundCommission));
                    rounds++;
                }
                else if (instruction.Calls(stock))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = AccessTools.Method(typeof(ReignWarEconomyTariffs), nameof(ReignWarEconomyTariffs.TransferQuotedUnit));
                    stocks++;
                }
            }
            if (prices != 1 || ratios != 1 || rounds != 1 || stocks != 2)
                throw new InvalidOperationException("Native purchase shape changed; expected one price, two stock writes, one town ratio and one commission rounding call.");
            return code;
        }

        private static int PurchaseLocal(CodeInstruction instruction, bool store)
        {
            var op = instruction.opcode;
            var shortCodes = store ? new[] { OpCodes.Stloc_0, OpCodes.Stloc_1, OpCodes.Stloc_2, OpCodes.Stloc_3 }
                : new[] { OpCodes.Ldloc_0, OpCodes.Ldloc_1, OpCodes.Ldloc_2, OpCodes.Ldloc_3 };
            for (int i = 0; i < shortCodes.Length; i++) if (op == shortCodes[i]) return i;
            if (op != (store ? OpCodes.Stloc : OpCodes.Ldloc) && op != (store ? OpCodes.Stloc_S : OpCodes.Ldloc_S)) return -1;
            return PurchaseLocalOperand(instruction.operand);
        }

        private static int PurchaseLocalOperand(object operand) => operand is LocalBuilder builder ? builder.LocalIndex
            : operand is LocalVariableInfo variable ? variable.LocalIndex
            : operand is int index ? index : operand is byte small ? small : -1;

        private static bool PurchaseArgumentAddress(CodeInstruction instruction, int index)
            => (instruction.opcode == OpCodes.Ldarga || instruction.opcode == OpCodes.Ldarga_S)
                && (instruction.operand is ParameterInfo parameter ? parameter.Position : PurchaseLocalOperand(instruction.operand)) == index;

        // A rejected quote may be zeroed only when its sole consumer is the native cost sum,
        // and both stock results are discarded. Keep the original loop and its buyer-null branch.
        private static void RequirePurchaseLoop(List<CodeInstruction> code, MethodInfo price, MethodInfo stock)
        {
            var prices = code.Select((x, i) => new { x, i }).Where(x => x.x.Calls(price)).ToArray();
            var stocks = code.Select((x, i) => new { x, i }).Where(x => x.x.Calls(stock)).ToArray();
            var target = AccessTools.Method(typeof(SellItemsAction), "ApplyInternal",
                new[] { typeof(PartyBase), typeof(PartyBase), typeof(ItemRosterElement), typeof(int), typeof(Settlement) });
            if (target == null || !target.IsStatic) throw new InvalidOperationException("Native purchase signature changed.");
            if (prices.Length != 1 || stocks.Length != 2) throw new InvalidOperationException("Unsupported native purchase loop calls.");
            int p = prices[0].i;
            if (p < 10 || p + 28 >= code.Count || stocks[0].i != p + 11 || stocks[1].i != p + 20)
                throw new InvalidOperationException("Unsupported native purchase stock ordering.");
            if (code.Take(p + 29).Any(x => x.operand is MethodInfo method
                && (method.DeclaringType == typeof(GiveGoldAction)
                    || (method.DeclaringType == typeof(SettlementComponent) && method.Name == nameof(SettlementComponent.ChangeGold))
                    || (method.DeclaringType == typeof(Hero) && (method.Name == nameof(Hero.ChangeHeroGold) || method.Name == "set_Gold"))
                    || (method.DeclaringType == typeof(MobileParty) && method.Name == "set_PartyTradeGold"))))
                throw new InvalidOperationException("Native purchase payment moved ahead of the complete item loop.");
            if (!code.Skip(p + 29).Any(x => x.operand is MethodInfo method && method.DeclaringType == typeof(GiveGoldAction)))
                throw new InvalidOperationException("Native purchase payment is missing after the item loop.");
            int unit = PurchaseLocal(code[p + 1], true), total = PurchaseLocal(code[p + 2], false);
            int iteration = PurchaseLocal(code[p + 22], false);
            var roster = AccessTools.PropertyGetter(typeof(PartyBase), nameof(PartyBase.ItemRoster));
            var equipment = AccessTools.PropertyGetter(typeof(ItemRosterElement), nameof(ItemRosterElement.EquipmentElement));
            if (unit < 0 || total < 0 || PurchaseLocal(code[p + 3], false) != unit
                || code.Count(x => PurchaseLocal(x, false) == unit) != 1
                || code.Count(x => PurchaseLocal(x, true) == unit) != 1
                || code.Count(x => PurchaseLocal(x, true) == total) != 2
                || code.Any(x => (x.opcode == OpCodes.Ldloca || x.opcode == OpCodes.Ldloca_S)
                    && (PurchaseLocalOperand(x.operand) == unit || PurchaseLocalOperand(x.operand) == total))
                || code[p - 10].opcode != OpCodes.Ldc_I4_0 || PurchaseLocal(code[p - 9], true) != total
                || code[p - 8].opcode != OpCodes.Ldc_I4_0 || PurchaseLocal(code[p - 7], true) != iteration
                || (code[p - 6].opcode != OpCodes.Br && code[p - 6].opcode != OpCodes.Br_S)
                || !(code[p - 6].operand is Label check) || !code[p + 26].labels.Contains(check)
                || PurchaseLocal(code[p - 5], false) < 0 || !PurchaseArgumentAddress(code[p - 4], 2)
                || !code[p - 3].Calls(equipment) || PurchaseLocal(code[p - 2], false) < 0 || PurchaseLocal(code[p - 1], false) < 0
                || code[p + 4].opcode != OpCodes.Add || PurchaseLocal(code[p + 5], true) != total
                || code[p + 6].opcode != OpCodes.Ldarg_0 || !code[p + 7].Calls(roster)
                || !PurchaseArgumentAddress(code[p + 8], 2) || !code[p + 9].Calls(equipment)
                || code[p + 10].opcode != OpCodes.Ldc_I4_M1 || code[p + 12].opcode != OpCodes.Pop
                || code[p + 13].opcode != OpCodes.Ldarg_1
                || (code[p + 14].opcode != OpCodes.Brfalse && code[p + 14].opcode != OpCodes.Brfalse_S)
                || !(code[p + 14].operand is Label skip) || !code[p + 22].labels.Contains(skip)
                || code[p + 15].opcode != OpCodes.Ldarg_1 || !code[p + 16].Calls(roster)
                || !PurchaseArgumentAddress(code[p + 17], 2) || !code[p + 18].Calls(equipment)
                || code[p + 19].opcode != OpCodes.Ldc_I4_1 || code[p + 21].opcode != OpCodes.Pop
                || iteration < 0 || code[p + 23].opcode != OpCodes.Ldc_I4_1 || code[p + 24].opcode != OpCodes.Add
                || PurchaseLocal(code[p + 25], true) != iteration || PurchaseLocal(code[p + 26], false) != iteration
                || code[p + 27].opcode != OpCodes.Ldarg_3
                || (code[p + 28].opcode != OpCodes.Blt && code[p + 28].opcode != OpCodes.Blt_S)
                || !(code[p + 28].operand is Label loop) || !code[p - 5].labels.Contains(loop)
                || code.Skip(p - 5).Take(34).Any(x => x.blocks.Count != 0)
                || code.Skip(p - 4).Take(26).Any(x => x.labels.Count != 0))
                throw new InvalidOperationException("Native purchase price or stock consumer shape changed.");
        }

        private static int WageInQuarters(int nativeStackWage, MobileParty party)
        {
            return !QuarteringCounterfactual && party?.IsGarrison == true && party.CurrentSettlement?.IsFortification == true
                && party.GarrisonPartyComponent?.Settlement == party.CurrentSettlement
                ? GarrisonStackWage(nativeStackWage) : nativeStackWage;
        }

        private static int GarrisonStackWage(int nativeStackWage)
            => MathF.Round((float)(nativeStackWage * ReignWarEconomyPolicy.GarrisonWageFactor));

        private static void KingdomWagePostfix(PartyWageModel __instance, MobileParty mobileParty, TroopRoster troopRoster,
            ref ExplainedNumber __result)
            => ApplyKingdomWage(__instance, mobileParty, troopRoster, ref __result);

        private static void NavalKingdomWagePostfix(PartyWageModel __instance, MobileParty mobileParty, TroopRoster troopRoster,
            ref ExplainedNumber __result)
            => ApplyKingdomWage(__instance, mobileParty, troopRoster, ref __result);

        private static Kingdom WageKingdom(MobileParty party)
        {
            if (!ReignWarEconomyOptions.UsesTroopWages(party)) return null;
            Clan clan = party.IsGarrison ? party.GarrisonPartyComponent?.Settlement?.OwnerClan
                : party.PatrolPartyComponent != null ? party.PatrolPartyComponent.HomeSettlement?.OwnerClan
                : party.ActualClan;
            if (clan == null || clan.IsEliminated || clan.IsUnderMercenaryService || clan.IsClanTypeMercenary) return null;
            return clan.Kingdom?.IsEliminated == false ? clan.Kingdom : null;
        }

        private static void ApplyKingdomWage(PartyWageModel model, MobileParty party, TroopRoster roster, ref ExplainedNumber result)
        {
            // War Sails delegates to the Reign model. Only the active outer quote is adjusted.
            if (_exemptWageQuote || result.ResultNumber <= 0f || roster == null
                || !ReferenceEquals(model, TaleWorlds.CampaignSystem.Campaign.Current?.Models.PartyWageModel)) return;
            Kingdom kingdom = WageKingdom(party);
            if (kingdom == null) return;

            bool hasRegulars = false;
            TroopRoster exemptRoster = null;
            for (int i = 0; i < roster.Count; i++)
            {
                var member = roster.GetElementCopyAtIndex(i);
                var troop = member.Character;
                if (!troop.IsHero && troop.Occupation != Occupation.Mercenary)
                {
                    hasRegulars = true;
                    continue;
                }
                // These heroes already contribute zero in the native wage calculation.
                if (troop.IsHero && (troop.HeroObject == troop.HeroObject.Clan?.Leader
                    || (party.IsMainParty && troop.HeroObject.Clan == Clan.PlayerClan && troop.HeroObject.Occupation == Occupation.Lord)))
                    continue;
                if (exemptRoster == null) exemptRoster = TroopRoster.CreateDummyTroopRoster();
                exemptRoster.AddToCounts(troop, member.Number, false, member.WoundedNumber, member.Xp);
            }
            if (!hasRegulars) return;

            // Quote only the exempt roster through the same model so its native
            // perks and salaries are preserved. Ordinary regular-only bills need one quote.
            float exemptWages = 0f;
            if (exemptRoster != null)
            {
                _exemptWageQuote = true;
                try { exemptWages = model.GetTotalWage(party, exemptRoster).ResultNumber; }
                finally { _exemptWageQuote = false; }
            }
            bool atWar = ReignMilitaryReadiness.AtWar(kingdom);
            Diplomacy.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                (float)ReignWarEconomyPolicy.KingdomWageDelta(result.ResultNumber, exemptWages, atWar),
                new TaleWorlds.Localization.TextObject(atWar ? "{=!}Wartime hazard pay" : "{=!}Peacetime wage reduction"));
        }

        private static void ApplyFieldWageUi()
        {
            // A changed presentation hook must be visible as incomplete coverage,
            // without disabling recruitment, accounting or actual wage payments.
            try
            {
                Patch(typeof(PartyVM), "RefreshCurrentCharacterInformation", nameof(FieldWageSelectionPostfix), prefix: false);
                var recruitCard = AccessTools.Constructor(typeof(RecruitVolunteerTroopVM), new[] {
                    typeof(RecruitVolunteerVM), typeof(CharacterObject), typeof(int), typeof(Action<RecruitVolunteerTroopVM>), typeof(Action<RecruitVolunteerTroopVM>) });
                if (recruitCard == null) throw new MissingMethodException("Native volunteer wage card constructor changed.");
                _harmony.Patch(recruitCard, postfix: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(RecruitFieldWagePostfix)));
                PatchedHooks.Add(nameof(RecruitFieldWagePostfix)); HookTargets[nameof(RecruitFieldWagePostfix)] = recruitCard;
                var characterTooltip = AccessTools.Method(typeof(TooltipRefresherCollection), "RefreshCharacterTooltip")
                    ?? throw new MissingMethodException("Native character wage tooltip changed.");
                _harmony.Patch(characterTooltip, transpiler: new HarmonyMethod(typeof(ReignEconomyPatches), nameof(FieldWageTooltipTranspiler)));
                PatchedHooks.Add(nameof(FieldWageTooltipTranspiler)); HookTargets[nameof(FieldWageTooltipTranspiler)] = characterTooltip;
            }
            catch (Exception ex) { RecordFieldWageUiError(ex); }
        }

        private static void FieldWageSelectionPostfix(PartyVM __instance)
        {
            try
            {
                var row = __instance?.CurrentCharacter;
                if (row?.Character == null || row.Character.IsHero || row.Type != PartyScreenLogic.TroopType.Member
                    || string.IsNullOrEmpty(__instance.CurrentCharacterWageLbl)) return;
                var logic = __instance.PartyScreenLogic;
                var owner = row.Side == PartyScreenLogic.PartyRosterSide.Right ? logic?.RightOwnerParty
                    : row.Side == PartyScreenLogic.PartyRosterSide.Left ? logic?.LeftOwnerParty : null;
                var quoteParty = owner?.MobileParty ?? (TaleWorlds.CampaignSystem.Campaign.Current == null ? null : MobileParty.MainParty);
                __instance.CurrentCharacterWageLbl = quoteParty == null ? "" : ReignWarEconomyOptions.FinalUnitWage(row.Character, quoteParty, owner?.MobileParty != null).ToString();
            }
            catch (Exception ex) { RecordFieldWageUiError(ex); }
        }

        private static void RecruitFieldWagePostfix(RecruitVolunteerTroopVM __instance)
        {
            try
            {
                if (__instance?.Character != null && !__instance.Character.IsHero)
                    __instance.Wage = ReignWarEconomyOptions.FinalPlayerUnitWage(__instance.Character);
            }
            catch (Exception ex) { RecordFieldWageUiError(ex); }
        }

        private static void RecordFieldWageUiError(Exception ex)
        {
            if (!string.IsNullOrEmpty(_fieldWageUiError)) return;
            _fieldWageUiError = ex.GetType().Name + ": " + ex.Message;
            ReignLog.Warn("Field wage UI quote retained its native fallback: " + _fieldWageUiError);
        }

        private static IEnumerable<CodeInstruction> FieldWageTooltipTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getter = AccessTools.PropertyGetter(typeof(CharacterObject), "TroopWage");
            int wages = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].Calls(getter))
                {
                    code[i].opcode = OpCodes.Call;
                    code[i].operand = AccessTools.Method(typeof(ReignWarEconomyOptions), nameof(ReignWarEconomyOptions.FinalTooltipUnitWage), new[] { typeof(CharacterObject) });
                    wages++;
                }
            }
            if (wages != 2) throw new InvalidOperationException("Native generic character wage tooltip changed.");
            return code;
        }

        private static IEnumerable<CodeInstruction> GarrisonQuarteringTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var getter = AccessTools.PropertyGetter(typeof(CharacterObject), "TroopWage");
            var count = AccessTools.PropertyGetter(typeof(TroopRosterElement), "Number");
            var wrapper = AccessTools.Method(typeof(ReignEconomyPatches), nameof(WageInQuarters));
            var code = instructions.ToList();
            var wageSites = code.Select((instruction, index) => new { instruction, index }).Where(x => x.instruction.Calls(getter)).ToList();
            if (wageSites.Count != 3)
                throw new InvalidOperationException("Native garrison wage shape changed; expected two hero quotes and one regular stack quote.");
            int regular = wageSites[2].index;
            if (regular + 4 >= code.Count || (code[regular + 1].opcode != OpCodes.Ldloca_S && code[regular + 1].opcode != OpCodes.Ldloca)
                || !code[regular + 2].Calls(count) || code[regular + 3].opcode != OpCodes.Mul
                || !code[regular + 4].opcode.Name.StartsWith("stloc", StringComparison.Ordinal)
                || code[regular + 4].labels.Count != 0)
                throw new InvalidOperationException("Native regular stack wage multiplication changed; quartering is unsupported.");
            for (int index = 0; index < code.Count; index++)
            {
                if (index == regular)
                {
                    // Character is already on the stack. Only the regular getter gains party context;
                    // both negotiated hero salary branches remain native instructions.
                    if (code[index].labels.Count != 0 || code[index].blocks.Count != 0)
                        throw new InvalidOperationException("Native regular wage getter gained a control-flow boundary.");
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ReignWarEconomyOptions), nameof(ReignWarEconomyOptions.UnitWage)));
                    continue;
                }
                yield return code[index];
                if (index == regular + 3)
                {
                    // Keep both hero salary branches and all native unit quotes intact.
                    // Round once after multiplying the regular stack, before native category modifiers.
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Call, wrapper);
                }
            }
        }

        internal static Hero UpgradePayer(Hero owner, Hero leader)
            => owner?.IsAlive == true ? owner : leader?.IsAlive == true ? leader : null;
    }
}
