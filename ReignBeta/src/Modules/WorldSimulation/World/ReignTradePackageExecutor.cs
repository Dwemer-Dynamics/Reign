using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Core;

namespace ReignBeta.World
{
    public static class ReignTradePackageExecutor
    {
        public static ReignActionResult Execute(ReignWorldActionRecord action)
        {
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

            ApplyMaterialTerms(action, terms, result);
            ApplySettlements(action, terms, result);
            ApplyWorkshop(action, terms, result);
            ApplyPrisoner(action, terms, result);
            ApplyShips(action, terms, result);

            if (!ReignActionReceiptFormatter.ValidatePromisedEffects(action, result, out string receiptFailure))
            {
                return ReignActionResult.FailTerminal("Trade execution verification failed: " + receiptFailure, "trade_verification_failed", "state_verification");
            }

            string receipt = ReignActionReceiptFormatter.BuildVerifiedReceipt(result);
            if (!string.IsNullOrWhiteSpace(receipt))
            {
                result.WithMessage("Verified trade: " + receipt + ".");
            }

            return result;
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

        internal static void ApplyMaterialTerms(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            ApplyGold(action, terms, result);
            if (ReignValueService.ReadItemTermObjects(terms).Count == 0)
            {
                ApplyItem(action, terms, result);
            }
            else
            {
                ApplyItemList(action, terms, result);
            }
        }

        private static void ApplyGold(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            int amount = ReignValueService.ReadIntTerm(terms, "gold", ReignValueService.ReadIntTerm(terms, "GoldAmount", ReignValueService.ReadIntTerm(terms, "goldAmount", 0)));
            if (amount <= 0)
            {
                return;
            }

            Hero from = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "goldFromHeroStringId", ReignValueService.ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId)));
            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "goldToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId)));
            if (from == null || to == null)
            {
                return;
            }

            int fromBefore = from.Gold;
            int toBefore = to.Gold;
            GiveGoldAction.ApplyBetweenCharacters(from, to, amount, false);
            if (from.Gold != fromBefore - amount || to.Gold != toBefore + amount)
            {
                return;
            }

            result.WithEffect("gold_transfer", "hero", to.StringId, to.Name.ToString(), "from=" + from.StringId + ";amount=" + amount)
                .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "gold_changed")
                .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "gold_changed");
        }

        private static void ApplyItem(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            string itemId = ReignValueService.ReadStringTerm(terms, "itemId", ReignValueService.ReadStringTerm(terms, "item", string.Empty));
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return;
            }

            Hero from = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "itemFromHeroStringId", ReignValueService.ReadStringTerm(terms, "assetFromHeroStringId", ReignValueService.ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId))));
            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "itemToHeroStringId", ReignValueService.ReadStringTerm(terms, "assetToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))));
            MobileParty fromParty = ReignValueService.PartyForInventory(from);
            MobileParty toParty = ReignValueService.PartyForInventory(to);
            ItemObject item = ReignObjectResolver.FindItem(itemId);
            int amount = ReignValueService.ReadIntTerm(terms, "amount", ReignValueService.ReadIntTerm(terms, "Amount", 1));
            if (from == null || to == null || fromParty == null || toParty == null || item == null || amount <= 0)
            {
                return;
            }

            int available = fromParty.ItemRoster.GetItemNumber(item);
            if (available >= amount)
            {
                int toBefore = toParty.ItemRoster.GetItemNumber(item);
                fromParty.ItemRoster.AddToCounts(item, -amount);
                toParty.ItemRoster.AddToCounts(item, amount);
                if (fromParty.ItemRoster.GetItemNumber(item) != available - amount
                    || toParty.ItemRoster.GetItemNumber(item) != toBefore + amount)
                {
                    return;
                }

                result.WithEffect("item_transfer", "item", item.StringId, item.Name.ToString(), "from=" + from.StringId + ";to=" + to.StringId + ";amount=" + amount + ";source=inventory")
                    .WithChangedEntity("party", fromParty.StringId, PartyName(fromParty), "items_changed")
                    .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
                return;
            }

            if (amount == 1 && ReignObjectResolver.TryFindEquippedItem(
                from,
                itemId,
                ReignValueService.ReadStringTerm(terms, "sourceEquipmentSlot", string.Empty),
                ReignValueService.ReadStringTerm(terms, "sourceEquipmentSet", string.Empty),
                out Equipment equipment,
                out EquipmentIndex slot,
                out EquipmentElement equippedElement,
                out string sourceSet))
            {
                int toBefore = toParty.ItemRoster.GetItemNumber(equippedElement.Item);
                toParty.ItemRoster.AddToCounts(equippedElement, 1);
                equipment[slot].Clear();
                if (!equipment[slot].IsEmpty || toParty.ItemRoster.GetItemNumber(equippedElement.Item) != toBefore + 1)
                {
                    return;
                }

                result.WithEffect("equipped_item_transfer", "item", equippedElement.Item.StringId, equippedElement.Item.Name.ToString(), "from=" + from.StringId + ";to=" + to.StringId + ";amount=1;source=" + sourceSet + ";slot=" + slot)
                    .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "equipment_changed")
                    .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
            }
        }

        private static void ApplyItemList(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            foreach (JObject itemTerm in ReignValueService.ReadItemTermObjects(terms))
            {
                string itemId = ReignValueService.ReadStringTerm(itemTerm, "itemId", ReignValueService.ReadStringTerm(itemTerm, "item", string.Empty));
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    continue;
                }

                Hero from = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(itemTerm, "fromHeroStringId",
                    ReignValueService.ReadStringTerm(itemTerm, "itemFromHeroStringId",
                        ReignValueService.ReadStringTerm(terms, "itemFromHeroStringId",
                            ReignValueService.ReadStringTerm(terms, "assetFromHeroStringId",
                                ReignValueService.ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId))))));
                Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(itemTerm, "toHeroStringId",
                    ReignValueService.ReadStringTerm(itemTerm, "itemToHeroStringId",
                        ReignValueService.ReadStringTerm(terms, "itemToHeroStringId",
                            ReignValueService.ReadStringTerm(terms, "assetToHeroStringId",
                                ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))))));
                MobileParty fromParty = ReignValueService.PartyForInventory(from);
                MobileParty toParty = ReignValueService.PartyForInventory(to);
                ItemObject item = ReignObjectResolver.FindItem(itemId);
                int amount = ReignValueService.ReadIntTerm(itemTerm, "amount", ReignValueService.ReadIntTerm(itemTerm, "Amount", 1));
                if (from == null || to == null || fromParty == null || toParty == null || item == null || amount <= 0)
                {
                    continue;
                }

                int available = fromParty.ItemRoster.GetItemNumber(item);
                if (available >= amount)
                {
                    int toBefore = toParty.ItemRoster.GetItemNumber(item);
                    fromParty.ItemRoster.AddToCounts(item, -amount);
                    toParty.ItemRoster.AddToCounts(item, amount);
                    if (fromParty.ItemRoster.GetItemNumber(item) != available - amount
                        || toParty.ItemRoster.GetItemNumber(item) != toBefore + amount)
                    {
                        continue;
                    }

                    result.WithEffect("item_transfer", "item", item.StringId, item.Name.ToString(), "from=" + from.StringId + ";to=" + to.StringId + ";amount=" + amount + ";source=inventory")
                        .WithChangedEntity("party", fromParty.StringId, PartyName(fromParty), "items_changed")
                        .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
                    continue;
                }

                if (amount == 1 && ReignObjectResolver.TryFindEquippedItem(
                    from,
                    itemId,
                    ReignValueService.ReadStringTerm(itemTerm, "sourceEquipmentSlot", string.Empty),
                    ReignValueService.ReadStringTerm(itemTerm, "sourceEquipmentSet", string.Empty),
                    out Equipment equipment,
                    out EquipmentIndex slot,
                    out EquipmentElement equippedElement,
                    out string sourceSet))
                {
                    int toBefore = toParty.ItemRoster.GetItemNumber(equippedElement.Item);
                    toParty.ItemRoster.AddToCounts(equippedElement, 1);
                    equipment[slot].Clear();
                    if (!equipment[slot].IsEmpty || toParty.ItemRoster.GetItemNumber(equippedElement.Item) != toBefore + 1)
                    {
                        continue;
                    }

                    result.WithEffect("equipped_item_transfer", "item", equippedElement.Item.StringId, equippedElement.Item.Name.ToString(), "from=" + from.StringId + ";to=" + to.StringId + ";amount=1;source=" + sourceSet + ";slot=" + slot)
                        .WithChangedEntity("hero", from.StringId, from.Name.ToString(), "equipment_changed")
                        .WithChangedEntity("party", toParty.StringId, PartyName(toParty), "items_changed");
                }
            }
        }

        private static void ApplySettlements(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            List<string> settlementIds = ReignValueService.ReadStringListTerm(terms, "settlementIds");
            if (!string.IsNullOrWhiteSpace(action.TargetSettlementStringId) && !settlementIds.Contains(action.TargetSettlementStringId))
            {
                settlementIds.Insert(0, action.TargetSettlementStringId);
            }

            string termSettlement = ReignValueService.ReadStringTerm(terms, "targetSettlementStringId", string.Empty);
            if (!string.IsNullOrWhiteSpace(termSettlement) && !settlementIds.Contains(termSettlement))
            {
                settlementIds.Add(termSettlement);
            }

            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "settlementToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            foreach (string settlementId in settlementIds.Distinct())
            {
                Settlement settlement = ReignObjectResolver.FindSettlement(settlementId);
                if (settlement == null || !settlement.IsFortification || to == null)
                {
                    continue;
                }

                string oldKingdom = settlement.MapFaction?.StringId ?? string.Empty;
                ChangeOwnerOfSettlementAction.ApplyByGift(settlement, to);
                if (settlement.OwnerClan != to.Clan && settlement.MapFaction != to.MapFaction)
                {
                    continue;
                }

                result.WithEffect("settlement_transfer", "settlement", settlement.StringId, settlement.Name.ToString(), "fromKingdom=" + oldKingdom + ";toHero=" + to.StringId)
                    .WithChangedEntity("settlement", settlement.StringId, settlement.Name.ToString(), "owner_changed");
            }
        }

        private static void ApplyWorkshop(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            string workshopId = ReignValueService.ReadStringTerm(terms, "workshopId", ReignValueService.ReadStringTerm(terms, "workshop", string.Empty));
            if (string.IsNullOrWhiteSpace(workshopId))
            {
                return;
            }

            Workshop workshop = ReignObjectResolver.FindWorkshop(workshopId);
            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "workshopToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            if (workshop == null || workshop.Owner == null || workshop.WorkshopType == null || to == null)
            {
                return;
            }

            Hero oldOwner = workshop.Owner;
            ChangeOwnerOfWorkshopAction.ApplyByWar(workshop, to, workshop.WorkshopType);
            if (workshop.Owner != to)
            {
                return;
            }

            result.WithEffect("workshop_transfer", "workshop", ReignObjectResolver.WorkshopId(workshop), workshop.Name.ToString(), "from=" + (oldOwner?.StringId ?? string.Empty) + ";to=" + to.StringId)
                .WithChangedEntity("hero", to.StringId, to.Name.ToString(), "workshop_received");
        }

        private static void ApplyPrisoner(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            string prisonerId = ReignValueService.ReadStringTerm(terms, "prisonerHeroStringId", ReignValueService.ReadStringTerm(terms, "prisonerHero", string.Empty));
            if (string.IsNullOrWhiteSpace(prisonerId))
            {
                return;
            }

            Hero prisoner = ReignObjectResolver.FindHero(prisonerId);
            Hero facilitator = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "prisonerToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            if (prisoner == null || !prisoner.IsPrisoner || facilitator == null)
            {
                return;
            }

            EndCaptivityAction.ApplyByReleasedByChoice(prisoner, facilitator);
            if (prisoner.IsPrisoner)
            {
                return;
            }

            result.WithEffect("prisoner_release", "hero", prisoner.StringId, prisoner.Name.ToString(), "facilitator=" + facilitator.StringId)
                .WithChangedEntity("hero", prisoner.StringId, prisoner.Name.ToString(), "released_from_captivity");
        }

        private static void ApplyShips(ReignWorldActionRecord action, JObject terms, ReignActionResult result)
        {
            string assetText = string.Join(" ", new[]
            {
                ReignValueService.ReadStringTerm(terms, "assetClass", string.Empty),
                ReignValueService.ReadStringTerm(terms, "asset", string.Empty),
                ReignValueService.ReadStringTerm(terms, "assetName", string.Empty),
                ReignValueService.ReadStringTerm(terms, "packageText", string.Empty)
            }).ToLowerInvariant();
            bool shipTrade = assetText.Contains("ship") || assetText.Contains("fleet") || assetText.Contains("vessel") || assetText.Contains("boat") || assetText.Contains("naval") || ReignValueService.ReadIntTerm(terms, "shipCount", 0) > 0;
            if (!shipTrade)
            {
                return;
            }

            Hero from = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "assetFromHeroStringId", ReignValueService.ReadStringTerm(terms, "fromHeroStringId", action.ActorHeroStringId)));
            Hero to = ReignValueService.ResolveHero(ReignValueService.ReadStringTerm(terms, "assetToHeroStringId", ReignValueService.ReadStringTerm(terms, "toHeroStringId", action.TargetHeroStringId))) ?? Hero.MainHero;
            Kingdom actorKingdom = ReignObjectResolver.FindKingdom(action.ActorKingdomStringId);
            Kingdom targetKingdom = ReignObjectResolver.FindKingdom(action.TargetKingdomStringId);
            MobileParty sourceParty = ReignValueService.ResolveShipParty(from, actorKingdom ?? targetKingdom);
            MobileParty destinationParty = ReignValueService.ResolveShipParty(to, to?.Clan?.Kingdom ?? targetKingdom ?? actorKingdom);
            if (sourceParty?.Party == null || destinationParty?.Party == null)
            {
                return;
            }

            List<Ship> ships = sourceParty.Ships?
                .Where(x => x != null && (!x.IsUsedByQuest || ReignValueService.ReadBoolTerm(terms, "includeQuestShips", false)))
                .ToList() ?? new List<Ship>();
            int requested = ReignValueService.ReadIntTerm(terms, "shipCount", ReignValueService.ReadIntTerm(terms, "amount", ReignValueService.ReadIntTerm(terms, "Amount", 0)));
            bool all = ReignValueService.ReadBoolTerm(terms, "allMatchingAssets", requested <= 0);
            int count = all || requested <= 0 ? ships.Count : Math.Min(requested, ships.Count);
            foreach (Ship ship in ships.Take(count).ToList())
            {
                ChangeShipOwnerAction.ApplyByTransferring(destinationParty.Party, ship);
                if (!(destinationParty.Ships?.Contains(ship) ?? false) || (sourceParty.Ships?.Contains(ship) ?? false))
                {
                    continue;
                }

                string shipId = ship.ShipHull?.StringId ?? string.Empty;
                string shipName = ship.Name?.ToString() ?? shipId;
                result.WithEffect("ship_transfer", "ship", shipId, shipName, "fromParty=" + (sourceParty.StringId ?? string.Empty) + ";toParty=" + (destinationParty.StringId ?? string.Empty))
                    .WithChangedEntity("ship", shipId, shipName, "owner_changed");
            }
        }

        private static string PartyName(MobileParty party)
        {
            return party?.Name?.ToString() ?? party?.StringId ?? string.Empty;
        }
    }
}
