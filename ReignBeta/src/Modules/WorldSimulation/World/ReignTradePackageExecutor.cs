using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using ReignBeta.World;
using ReignBeta.Shared.Dialogue;

namespace ReignBeta.WorldSimulation.World
{
    public static class ReignTradePackageExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
            ReignActionResult replay = ReplayMaterialTrade(action);
            if (replay != null) return replay;

            if (!ReignValueService.ValidateTradePackage(action, out string validationFailure, out ReignTradeAppraisal appraisal))
            {
                return ReignActionResult.ValidationFailed(validationFailure);
            }

            JObject terms = ReignValueService.ParseTerms(action);
            ReignActionResult result = ReignActionResult.Done("Trade package executed. " + appraisal.Summary)
                .WithEffect("trade_package", "action", action.ActionId, action.Type.ToString(), appraisal.Summary)
                .WithDiagnostic("playerOfferValue", appraisal.PlayerOfferValue.ToString(CultureInfo.InvariantCulture))
                .WithDiagnostic("npcOfferValue", appraisal.NpcOfferValue.ToString(CultureInfo.InvariantCulture))
                .WithDiagnostic("fairnessRatio", appraisal.FairnessRatio.ToString("0.00", CultureInfo.InvariantCulture))
                .WithDiagnostic("recommendedCounter", appraisal.RecommendedCounter.ToString(CultureInfo.InvariantCulture))
                .WithDiagnostic("fairnessBypassed", appraisal.BypassedFairness.ToString(CultureInfo.InvariantCulture));

            foreach (ReignTradeTermQuote quote in appraisal.Quotes)
            {
                result.WithDiagnostic("quote." + quote.AssetType + "." + quote.AssetId, quote.AssetName + ";amount=" + quote.Amount + ";value=" + quote.TotalValue + ";source=" + quote.ValueSource + ";side=" + quote.Side);
            }

            if (ReignValueService.ReadSettlementIds(action, terms).Count > 0)
            {
                return ExecuteSettlementTradeAtomically(action, terms, result);
            }

            return ExecuteMaterialTrade(action, terms, result);
        }

        private static ReignActionResult ExecuteSettlementTradeAtomically(
            ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            List<SettlementRollbackState> settlementStates = ReignValueService
                .ReadSettlementIds(action, terms)
                .Select(ReignObjectResolver.FindSettlement)
                .Where(x => x != null)
                .Select(x => new SettlementRollbackState
                {
                    Settlement = x,
                    PreviousOwner = x.OwnerClan?.Leader,
                    PreviousOwnerClan = x.OwnerClan,
                    PreviousKingdomId = x.MapFaction?.StringId ?? string.Empty
                }).ToList();
            Hero recipient = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(
                terms, "settlementToHeroStringId", ReignValueService.ReadStringTerm(terms,
                    "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            int gold = ReignValueService.ReadIntTerm(terms, "gold",
                ReignValueService.ReadIntTerm(terms, "GoldAmount",
                    ReignValueService.ReadIntTerm(terms, "goldAmount", 0)));
            Hero goldFrom = gold <= 0 ? null : ReignValueService.ResolveHero(
                ReignValueService.ReadStringTerm(terms, "goldFromHeroStringId",
                    ReignValueService.ReadStringTerm(terms, "fromHeroStringId",
                        action.ActorHeroStringId)));
            Hero goldTo = gold <= 0 ? null : ReignValueService.ResolveHero(
                ReignValueService.ReadStringTerm(terms, "goldToHeroStringId",
                    ReignValueService.ReadStringTerm(terms, "toHeroStringId",
                        action.TargetHeroStringId)));
            GoldRollbackState goldState = gold <= 0 ? null : new GoldRollbackState
            {
                From = goldFrom,
                To = goldTo,
                FromGold = goldFrom?.Gold ?? 0,
                ToGold = goldTo?.Gold ?? 0
            };

            if (recipient?.Clan == null || settlementStates.Count == 0
                || settlementStates.Any(x => x.PreviousOwner == null
                    || x.PreviousOwnerClan == null)
                || (gold > 0 && (goldFrom == null || goldTo == null)))
            {
                return ReignActionResult.ValidationFailed(
                    "Atomic settlement trade could not resolve every participant and current owner.");
            }

            try
            {
                if (gold > 0)
                {
                    GiveGoldAction.ApplyBetweenCharacters(goldFrom, goldTo, gold, false);
                    if (goldFrom.Gold != goldState.FromGold - gold
                        || goldTo.Gold != goldState.ToGold + gold)
                    {
                        return AtomicSettlementFailure(settlementStates, goldState,
                            "Gold transfer did not apply exactly.");
                    }
                }

                foreach (SettlementRollbackState state in settlementStates)
                {
                    ChangeOwnerOfSettlementAction.ApplyByGift(state.Settlement, recipient);
                    if (state.Settlement.OwnerClan != recipient.Clan)
                    {
                        return AtomicSettlementFailure(settlementStates, goldState,
                            "Native settlement transfer did not assign "
                            + state.Settlement.Name + " to the receiving clan.");
                    }
                }

                if (gold > 0)
                {
                    result.WithEffect("gold_transfer", "hero", goldTo.StringId,
                            goldTo.Name.ToString(), "from=" + goldFrom.StringId
                            + ";amount=" + gold)
                        .WithChangedEntity("hero", goldFrom.StringId,
                            goldFrom.Name.ToString(), "gold_changed")
                        .WithChangedEntity("hero", goldTo.StringId,
                            goldTo.Name.ToString(), "gold_changed");
                }
                foreach (SettlementRollbackState state in settlementStates)
                {
                    result.WithEffect("settlement_transfer", "settlement",
                            state.Settlement.StringId, state.Settlement.Name.ToString(),
                            "fromKingdom=" + state.PreviousKingdomId + ";fromClan="
                            + state.PreviousOwnerClan.StringId + ";toHero="
                            + recipient.StringId)
                        .WithChangedEntity("settlement", state.Settlement.StringId,
                            state.Settlement.Name.ToString(), "owner_changed");
                }

                if (!ReignActionReceiptFormatter.ValidatePromisedEffects(action, result,
                    out string receiptFailure))
                {
                    return AtomicSettlementFailure(settlementStates, goldState,
                        "Trade execution verification failed: " + receiptFailure);
                }

                string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
                if (!string.IsNullOrWhiteSpace(receipt))
                {
                    result.WithMessage("Verified trade: " + receipt + ".");
                }
                return result;
            }
            catch (Exception ex)
            {
                return AtomicSettlementFailure(settlementStates, goldState,
                    "Native settlement trade threw: " + ex.Message);
            }
        }

        private static ReignActionResult AtomicSettlementFailure(
            List<SettlementRollbackState> settlementStates, GoldRollbackState goldState,
            string failure)
        {
            bool rollbackOk = true;
            try
            {
                if (goldState?.From != null && goldState.To != null)
                {
                    goldState.From.ChangeHeroGold(goldState.FromGold - goldState.From.Gold);
                    goldState.To.ChangeHeroGold(goldState.ToGold - goldState.To.Gold);
                    rollbackOk = goldState.From.Gold == goldState.FromGold
                        && goldState.To.Gold == goldState.ToGold;
                }
            }
            catch
            {
                rollbackOk = false;
            }

            foreach (SettlementRollbackState state in settlementStates.AsEnumerable().Reverse())
            {
                try
                {
                    if (state.Settlement?.OwnerClan != state.PreviousOwnerClan
                        && state.PreviousOwner != null)
                    {
                        ChangeOwnerOfSettlementAction.ApplyByGift(state.Settlement,
                            state.PreviousOwner);
                    }
                    rollbackOk = rollbackOk && state.Settlement?.OwnerClan
                        == state.PreviousOwnerClan;
                }
                catch
                {
                    rollbackOk = false;
                }
            }

            return rollbackOk
                ? ReignActionResult.FailTerminal(failure
                    + " All applied settlement-trade effects were rolled back.",
                    "trade_atomicity_failed", "state_verification")
                : ReignActionResult.FailTerminal(failure
                    + " Rollback could not restore every prior owner or balance.",
                    "trade_rollback_failed", "state_verification");
        }

        private sealed class SettlementRollbackState
        {
            public Settlement Settlement;
            public Hero PreviousOwner;
            public Clan PreviousOwnerClan;
            public string PreviousKingdomId;
        }

        private sealed class GoldRollbackState
        {
            public Hero From;
            public Hero To;
            public int FromGold;
            public int ToGold;
        }

        internal static ReignActionResult ReplayMaterialTrade(ReignWorldActionRecord action)
        {
            if (string.IsNullOrWhiteSpace(action.ActionId)) return null;
            try
            {
                var journal = ReignGoldTransferService.Instance?.TradePackages;
                var receipt = journal?.Find(action.ActionId, Binding(action));
                if (receipt?.State == LegacyTradeJournal.Applying && journal.IsApplying(action.ActionId))
                    return ReignActionResult.Progress("The native trade is still being applied.");
                return receipt == null ? null : TradeResult(receipt);
            }
            catch (InvalidOperationException ex)
            {
                return ReignActionResult.ValidationFailed(ex.Message);
            }
        }

        private static string Binding(ReignWorldActionRecord action)
            => PlayerActionRules.Hash(new JArray(action.TypeValue, action.ActorHeroStringId, action.TargetHeroStringId,
                action.ActorKingdomStringId, action.TargetKingdomStringId, action.TargetSettlementStringId,
                action.ActorClanStringId, action.TargetClanStringId, action.AuthorizationMode, action.NegotiationId,
                action.TermsHash, action.TermsJson).ToString(Formatting.None));

        private static ReignActionResult ExecuteMaterialTrade(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            var journal = ReignGoldTransferService.Instance?.TradePackages;
            if (journal == null)
                return ReignActionResult.ValidationFailed("The native trade journal is unavailable.");

            try
            {
                var legs = new List<LegacyTradeLeg>();
                AddGoldLeg(action, terms, result, legs);
                if (!ReignValueService.TryResolveTradeItems(action, terms, out var items, out string reason))
                    return ReignActionResult.ValidationFailed(reason);
                foreach (var item in items) AddItemLeg(item, result, legs);

                var receipt = journal.Execute(action.ActionId, Binding(action), legs, () =>
                {
                    if (!ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string failure))
                        throw new InvalidOperationException("Trade receipt verification failed: " + failure);
                    string summary = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
                    if (!string.IsNullOrWhiteSpace(summary)) result.WithMessage("Verified trade: " + summary + ".");
                    return new JObject
                    {
                        ["message"] = result.Message,
                        ["effects"] = JArray.FromObject(result.Effects),
                        ["changedEntities"] = JArray.FromObject(result.ChangedEntities),
                        ["diagnostics"] = JArray.FromObject(result.Diagnostics)
                    };
                });
                return TradeResult(receipt);
            }
            catch (InvalidOperationException ex)
            {
                // Complete admission before the journal starts its first mutation.
                return ReignActionResult.ValidationFailed(ex.Message);
            }
        }

        private static ReignActionResult TradeResult(LegacyTradeReceipt receipt)
        {
            if (receipt.State == LegacyTradeJournal.Applied)
            {
                var result = ReignActionResult.Done(receipt.Result.Value<string>("message"));
                result.Effects.AddRange(receipt.Result["effects"].ToObject<List<Dictionary<string, string>>>());
                result.ChangedEntities.AddRange(receipt.Result["changedEntities"].ToObject<List<Dictionary<string, string>>>());
                result.Diagnostics.AddRange(receipt.Result["diagnostics"].ToObject<List<Dictionary<string, string>>>());
                return result;
            }
            if (receipt.State == LegacyTradeJournal.Restored)
                return ReignActionResult.FailTerminal("Trade failed and the prior holdings were restored. " + receipt.Message,
                    "trade_rolled_back", "state_verification");

            return ReignActionResult.FailTerminal("Trade requires recovery. Do not repeat this exchange. " + receipt.Message,
                "trade_recovery_required", "recovery_required")
                .WithDiagnostic("attemptedLegs", receipt.AttemptedLegs.ToString(CultureInfo.InvariantCulture))
                .WithDiagnostic("tradeLegs", receipt.Legs?.ToString(Formatting.None))
                .WithDiagnostic("unresolvedHoldings", receipt.Unresolved?.ToString(Formatting.None));
        }

        private static void AddGoldLeg(ReignWorldActionRecord action, JObject terms, ReignActionResult result, List<LegacyTradeLeg> legs)
        {
            int amount = ReignValueService.ReadIntTerm(terms, "gold",
                ReignValueService.ReadIntTerm(terms, "GoldAmount", ReignValueService.ReadIntTerm(terms, "goldAmount", 0)));
            if (amount <= 0) return;
            Hero from = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "goldFromHeroStringId",
                ReignValueService.ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId)));
            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "goldToHeroStringId",
                ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId)));
            if (from == null || to == null || from == to)
                throw new InvalidOperationException("Trade payment requires two distinct available heroes.");

            LegacyTradeBalance Balance(Hero hero) => new LegacyTradeBalance(Holding("gold", hero.StringId),
                () => hero.Gold, gold => hero.ChangeHeroGold(gold - hero.Gold));
            legs.Add(new LegacyTradeLeg(Balance(from), Balance(to), amount,
                () => GiveGoldAction.ApplyBetweenCharacters(from, to, amount, false)));
            result.WithEffect("gold_transfer", "hero", to.StringId, to.Name.ToString(), "from=" + from.StringId + ";amount=" + amount)
                .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "gold_changed")
                .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "gold_changed");
        }

        private static string Holding(params string[] identity)
            => new JArray(identity).ToString(Formatting.None);

        private static void AddItemLeg(ReignValueService.TradeItemTransfer transfer,
            ReignActionResult result, List<LegacyTradeLeg> legs)
        {
            Hero from = transfer.From;
            MobileParty source = transfer.Source;
            MobileParty destination = transfer.Destination;
            ItemRoster sourceRoster = source.ItemRoster;
            ItemRoster destinationRoster = destination.ItemRoster;
            EquipmentElement element = transfer.Element;
            ItemObject item = element.Item;
            LegacyTradeBalance receiver = ItemBalance(destination, destinationRoster, element);
            int amount = transfer.Amount;
            string flow = "from=" + from.StringId + ";to=" + transfer.To.StringId + ";amount=" + amount;
            Equipment equipment = transfer.Equipment;
            if (equipment != null)
            {
                EquipmentIndex slot = transfer.Slot;
                string set = transfer.EquipmentSet;
                int ReadSlot()
                {
                    Equipment currentSet = set == "battle" ? from.BattleEquipment : from.CivilianEquipment;
                    if (!ReferenceEquals(currentSet, equipment))
                        throw new InvalidOperationException("The agreed equipment set was replaced.");
                    EquipmentElement current = equipment[slot];
                    if (current.IsEmpty) return 0;
                    if (current.Equals(element)) return 1;
                    throw new InvalidOperationException("The native equipment slot contains " + current.Item.StringId
                        + " (modifier: " + Variant(current) + ").");
                }

                string variant = Variant(element);
                var equipped = new LegacyTradeBalance(Holding("equipment", from.StringId, set, slot.ToString(), item.StringId, variant),
                    ReadSlot, value => equipment[slot] = value == 1 ? element : EquipmentElement.Invalid);
                legs.Add(new LegacyTradeLeg(equipped, receiver, 1, () =>
                {
                    int before = receiver.Read();
                    destinationRoster.AddToCounts(element, 1);
                    if (receiver.Read() != before + 1 || ReadSlot() != 1)
                        throw new InvalidOperationException("The equipment transfer changed before its slot could be cleared.");
                    equipment[slot] = EquipmentElement.Invalid;
                }));
                result.WithEffect("equipped_item_transfer", "item", item.StringId, item.Name.ToString(),
                    flow + ";source=" + set + ";slot=" + slot + ";variant=" + variant + ";toParty=" + destination.StringId)
                    .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "equipment_changed")
                    .WithChangedEntity("party", destination.StringId, PartyName(destination), "items_changed");
                return;
            }

            LegacyTradeBalance inventory = ItemBalance(source, sourceRoster, element);
            legs.Add(new LegacyTradeLeg(inventory, receiver, amount, () =>
            {
                int sourceBefore = inventory.Read();
                int receiverBefore = receiver.Read();
                sourceRoster.AddToCounts(element, -amount);
                if (inventory.Read() != sourceBefore - amount || receiver.Read() != receiverBefore)
                    throw new InvalidOperationException("The item transfer changed before its destination could be credited.");
                destinationRoster.AddToCounts(element, amount);
            }));
            result.WithEffect("item_transfer", "item", item.StringId, item.Name.ToString(),
                flow + ";source=inventory;variant=" + Variant(element)
                    + ";fromParty=" + source.StringId + ";toParty=" + destination.StringId)
                .WithChangedEntity("party", source.StringId, PartyName(source), "items_changed")
                .WithChangedEntity("party", destination.StringId, PartyName(destination), "items_changed");
        }

        private static string Variant(EquipmentElement element) => element.ItemModifier?.StringId ?? string.Empty;

        private static LegacyTradeBalance ItemBalance(MobileParty party, ItemRoster roster, EquipmentElement element)
        {
            int Count()
            {
                if (!ReferenceEquals(party.ItemRoster, roster))
                    throw new InvalidOperationException("The agreed party inventory was replaced.");
                return ReignValueService.CountTradeStack(roster, element);
            }

            return new LegacyTradeBalance(Holding("inventory", party.StringId, element.Item.StringId, Variant(element)),
                Count, value => roster.AddToCounts(element, value - Count()));
        }

        private static string PartyName(MobileParty party)
        {
            return party?.Name?.ToString() ?? party?.StringId ?? string.Empty;
        }
    }
}
