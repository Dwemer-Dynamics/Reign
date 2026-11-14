using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Economy
{
    // Provisioning changes tariff diversion; paid-unit guards preserve native commercial rates and prices.
    internal static class ReignWarEconomyTariffs
    {
        internal const int Version = 3;
        [ThreadStatic] private static Scope _scope;
        [ThreadStatic] private static Sale _sale;
        private static long _processPurchases;
        private static long _processPoolChanges;
        private static int _loggedFailures;
        private static long _guardedVillagePurchases, _blockedVillageUnits, _invalidVillageQuotes;

        internal sealed class Scope
        {
            internal Scope Previous;
            internal MobileParty Party;
            internal Settlement Locality;
            internal string Purpose;
            internal Sale ActiveSale;
        }
        internal sealed class Sale
        {
            internal Sale Previous;
            internal MobileParty Buyer;
            internal Town Town;
            internal ItemObject Item;
            internal int BuyerGold, MarketGold, Pool, BuyerItems, SellerItems;
            internal bool RatioSeen, CommissionSeen;
            internal EconomyTariffReceipt Receipt;
            internal Scope UsedScope;
            internal bool GuardPurchases, RejectUnit, VillagePurchase;
            internal ItemRoster SellerRoster, BuyerRoster;
            internal long AcceptedCost;
        }
        private static bool Live => TaleWorlds.CampaignSystem.Campaign.Current?.GameStarted == true
            && ReignWarEconomyTransactions.State != null;

        internal static Scope Enter(MobileParty party, Settlement locality, string purpose)
        {
            var scope = new Scope { Previous = _scope, Party = party, Locality = locality, Purpose = purpose };
            _scope = scope;
            return scope;
        }
        internal static void Leave(Scope scope) { if (scope != null) _scope = scope.Previous; }
        internal static void Reset() { _scope = null; _sale = null; _processPurchases = 0; _processPoolChanges = 0; _loggedFailures = 0; _guardedVillagePurchases = 0; _blockedVillageUnits = 0; _invalidVillageQuotes = 0; }
        // SellItemsAction uses this exact branch; PartyTradeGold aliases hero gold only for lord parties.
        private static int PayerGold(MobileParty buyer) => buyer == null ? 0
            : buyer.IsCaravan ? buyer.PartyTradeGold : buyer.LeaderHero?.Gold ?? 0;

        internal static Sale Before(PartyBase seller, PartyBase buyer, ItemRosterElement subject, int quantity)
        {
            // Push even ignored/nested calls, so they cannot inherit another purchase's exemption.
            var sale = new Sale { Previous = _sale };
            _sale = sale;
            if (Live && seller?.IsSettlement == true && buyer?.MobileParty != null && !buyer.MobileParty.IsMainParty)
            {
                sale.GuardPurchases = true;
                sale.Buyer = buyer.MobileParty;
                sale.SellerRoster = seller.ItemRoster; sale.BuyerRoster = buyer.ItemRoster;
                sale.VillagePurchase = seller.Settlement.IsVillage;
                if (sale.VillagePurchase) _guardedVillagePurchases++;
            }
            try
            {
            if (!Live || seller?.IsSettlement != true || seller.Settlement?.Town == null
                || buyer?.MobileParty == null || subject.EquipmentElement.Item == null) return sale;
            var party = buyer.MobileParty;
            var town = seller.Settlement.Town;
            bool military = party.IsLordParty || party.IsGarrison;
            var owner = town.OwnerClan;
            // Native military SellItemsAction pays from the leader's wallet, not a pooled realm treasury.
            var clan = military ? party.LeaderHero?.Clan : party.ActualClan;
            bool scopeMatches = _scope?.Party == party && _scope.Locality == seller.Settlement && _scope.ActiveSale == null;
            string purpose = scopeMatches ? _scope.Purpose : "commercial_or_unscoped";
            sale.Buyer = party; sale.Town = town; sale.Item = subject.EquipmentElement.Item;
            sale.BuyerGold = PayerGold(party); sale.MarketGold = town.Gold; sale.Pool = town.TradeTaxAccumulated;
            sale.BuyerItems = buyer.ItemRoster.GetItemNumber(sale.Item);
            sale.SellerItems = seller.ItemRoster.GetItemNumber(sale.Item);
            sale.Receipt = new EconomyTariffReceipt { Version = Version, Day = CampaignTime.Now.ToDays,
                Town = town.Settlement.StringId, OwnerClan = owner?.StringId, BuyerClan = clan?.StringId,
                BuyerPartyClan = party.ActualClan?.StringId, PayerHero = party.LeaderHero?.StringId,
                OwnerKingdom = owner?.Kingdom?.StringId, BuyerKingdom = clan?.Kingdom?.StringId,
                BuyerParty = party.StringId, Purpose = purpose, Item = sale.Item.StringId, Quantity = quantity,
                AffordabilityGuarded = sale.GuardPurchases,
                BuyerClass = !military ? (party.IsCaravan ? "caravan" : "other_commercial")
                    : owner != null && clan == owner ? "same_owner_military"
                    : clan?.Kingdom != null && clan.Kingdom == owner?.Kingdom ? "same_realm_other_clan_military"
                    : "other_military" };
            if (scopeMatches) { sale.UsedScope = _scope; _scope.ActiveSale = sale; }
            return sale;
            }
            catch (Exception ex)
            {
                if (ReignWarEconomyTransactions.State != null) ReignWarEconomyTransactions.State.TariffUnsupported++;
                ReignBeta.Integration.ReignLog.Warn("Tariff capture unavailable: " + ex.GetType().Name);
                sale.Receipt = null;
                return sale; // A diagnostic failure cannot disable the already bound affordability guard.
            }
        }

        // Observe the actual per-item prices as native stock changes, without a second model quotation.
        internal static int QuotePrice(SettlementComponent market, EquipmentElement item, MobileParty trader, bool isSelling)
        {
            int native = market.GetItemPrice(item, trader, isSelling);
            var sale = _sale;
            var receipt = sale?.Receipt;
            if (receipt != null) { receipt.NativePrice += native; receipt.QuotedUnits++; }
            if (sale == null) return native;
            // An unexpected reentrant sale must not spend an outer sale's uncommitted purse.
            // Native reviewed buying has no such call; defer it explicitly rather than exempt it.
            bool reentrant = sale.GuardPurchases && sale.Previous != null;
            sale.RejectUnit = sale.GuardPurchases && (reentrant
                || !ReignWarEconomyPolicy.CanPayQuotedUnit(PayerGold(sale.Buyer), sale.AcceptedCost, native));
            if (sale.RejectUnit)
            {
                if (receipt != null)
                {
                    receipt.AffordabilityBlockedUnits++;
                    if (receipt.AffordabilityBlockedUnits == 1 || native < receipt.MinimumBlockedNativePrice)
                        receipt.MinimumBlockedNativePrice = native;
                    if (native < 0) receipt.Error = "Negative native purchase quote";
                    else if (reentrant) receipt.Error = "Reentrant native purchase deferred";
                }
                else if (sale.VillagePurchase)
                {
                    _blockedVillageUnits++;
                    if (native < 0) _invalidVillageQuotes++;
                    if (native < 0 || reentrant) ReignWarEconomyTransactions.State.TariffUnsupported++;
                }
                return 0; // Both native stock writes are gated below; no goods are granted at this zero.
            }
            sale.AcceptedCost += native;
            if (receipt != null) { receipt.FulfilledNativePrice += native; receipt.FulfilledUnits++; }
            return native;
        }
        internal static int TransferQuotedUnit(ItemRoster roster, EquipmentElement item, int amount)
        {
            var sale = _sale;
            if (sale?.GuardPurchases == true)
            {
                if (!((amount == -1 && ReferenceEquals(roster, sale.SellerRoster))
                    || (amount == 1 && ReferenceEquals(roster, sale.BuyerRoster))))
                    throw new InvalidOperationException("Native purchase stock route changed.");
                if (sale.RejectUnit) return -1;
            }
            return roster.AddToCounts(item, amount);
        }

        // Exact call replacement retains the actual loaded model, policy modifiers and native invocation count.
        internal static float TaxRatio(SettlementTaxModel model, Town town)
        {
            float native = model.GetTownTaxRatio(town);
            var sale = _sale;
            if (sale?.Receipt == null || sale.Town != town) return native;
            try
            {
            sale.RatioSeen = true;
            sale.Receipt.NativeRatio = native;
            sale.Receipt.TaxModel = model.GetType().AssemblyQualifiedName;
            bool food = sale.Item.IsFood || (sale.Item.HasHorseComponent && sale.Item.HorseComponent.IsLiveStock);
            bool eligible = sale.Receipt.Purpose == "native_food" ? food
                : sale.Receipt.Purpose == "native_horses" && sale.Item.HasHorseComponent;
            bool exempt = sale.Receipt.OwnerClan == town.OwnerClan?.StringId
                && sale.Receipt.BuyerClan == sale.Buyer.LeaderHero?.Clan?.StringId
                && sale.Receipt.OwnerKingdom == town.OwnerClan?.Kingdom?.StringId
                && sale.Receipt.BuyerKingdom == sale.Buyer.LeaderHero?.Clan?.Kingdom?.StringId
                && !float.IsNaN(native) && !float.IsInfinity(native) && native >= 0
                && sale.Receipt.Quantity > 0 && ReignWarEconomyPolicy.InternalProvisioning(
                    sale.Receipt.OwnerClan, sale.Receipt.BuyerClan, sale.Receipt.OwnerKingdom, sale.Receipt.BuyerKingdom,
                    sale.Buyer.IsLordParty || sale.Buyer.IsGarrison,
                    sale.Receipt.Purpose == "native_food" || sale.Receipt.Purpose == "native_horses", eligible, Live);
            sale.Receipt.Exempt = exempt;
            return exempt ? 0f : native;
            }
            catch (Exception ex)
            {
                sale.Receipt.Exempt = false;
                sale.Receipt.Error = "Unsupported tariff context: " + ex.GetType().Name;
                return native;
            }
        }
        internal static int RoundCommission(float value)
        {
            int rounded = MBRandom.RoundRandomized(value); // Preserve native fractional rounding and random-stream use.
            if (_sale?.Receipt != null) { _sale.CommissionSeen = true; _sale.Receipt.CommissionDebit = rounded; }
            return rounded;
        }
        internal static void After(Sale sale, Exception failure)
        {
            if (sale == null) return;
            try
            {
                var r = sale.Receipt;
                if (r == null) return;
                r.BuyerDebit = (long)sale.BuyerGold - PayerGold(sale.Buyer);
                r.MarketChange = (long)sale.Town.Gold - sale.MarketGold;
                r.PoolCredit = (long)sale.Town.TradeTaxAccumulated - sale.Pool;
                r.Received = sale.Buyer.ItemRoster.GetItemNumber(sale.Item) - sale.BuyerItems;
                r.Removed = sale.SellerItems - sale.Town.Owner.ItemRoster.GetItemNumber(sale.Item);
                r.Supported = failure == null && sale.RatioSeen && sale.CommissionSeen && string.IsNullOrEmpty(r.Error)
                    && r.OwnerClan == sale.Town.OwnerClan?.StringId && ReignWarEconomyPolicy.GuardedPurchaseConserved(
                        r.Quantity, r.FulfilledUnits, r.AffordabilityBlockedUnits, r.Received, r.Removed,
                        r.BuyerDebit, r.MarketChange, r.CommissionDebit, r.PoolCredit, r.Exempt,
                        r.NativePrice, r.QuotedUnits, r.FulfilledNativePrice);
                r.Error = r.Supported ? "" : !string.IsNullOrEmpty(r.Error) ? r.Error
                    : failure?.GetType().Name ?? "Native purchase conservation or tariff call shape differs";
                ReignWarEconomyLedger.RecordTariff(ReignWarEconomyTransactions.State, r);
                _processPurchases++;
                // Preserve early failures before ordinary purchases evict them from the recent receipt window.
                // Operator evidence only: no resources, saved payload or coverage decisions change.
                if (!r.Supported && _loggedFailures < 16)
                {
                    _loggedFailures++;
                    try
                    {
                        ReignBeta.Integration.ReignLog.Warn("Tariff unsupported purchase: " + new JObject {
                            ["receipt"] = JObject.FromObject(r), ["ratioSeen"] = sale.RatioSeen,
                            ["commissionSeen"] = sale.CommissionSeen
                        }.ToString(Newtonsoft.Json.Formatting.None));
                    }
                    catch { /* Optional logging must not alter the recorded coverage result. */ }
                }
            }
            catch (Exception ex)
            {
                if (ReignWarEconomyTransactions.State != null) ReignWarEconomyTransactions.State.TariffUnsupported++;
                ReignBeta.Integration.ReignLog.Warn("Tariff observation failed: " + ex.GetType().Name);
            }
            finally
            {
                if (sale.UsedScope?.ActiveSale == sale) sale.UsedScope.ActiveSale = null;
                _sale = sale.Previous;
            }
        }
        internal static int? PoolBefore(Town town) => Live ? (int?)town.TradeTaxAccumulated : null;
        internal static void PoolAfter(Town town, int? before)
        {
            if (!before.HasValue || !Live) return;
            long delta = (long)town.TradeTaxAccumulated - before.Value;
            if (delta == 0) return;
            var state = ReignWarEconomyTransactions.State;
            string key = town.Settlement.StringId + "|" + town.OwnerClan?.StringId + "|" + town.OwnerClan?.Kingdom?.StringId;
            if (!state.TariffPools.TryGetValue(key, out var totals))
            {
                if (state.TariffPools.Count >= 8192) { state.TariffUnsupported++; return; }
                state.TariffPools[key] = totals = new EconomyTariffPoolTotals();
            }
            totals.Changes++; totals.Credits += Math.Max(0, delta); totals.Debits += Math.Max(0, -delta);
            if (delta > 0 && _sale?.Town != town) totals.OutsideObservedSaleCredits += delta;
            totals.LastDay = CampaignTime.Now.ToDays;
            _processPoolChanges++;
        }
        internal static JObject Diagnostics(bool detailed)
        {
            var state = ReignWarEconomyTransactions.State;
            return new JObject {
                ["version"] = Version, ["rule"] = "same_realm_native_food_and_horse_provisioning",
                ["commercialRatesChanged"] = false, ["historicalAttributionComplete"] = false,
                ["purchaseAffordabilityVersion"] = 1, ["guardedVillagePurchases"] = _guardedVillagePurchases,
                ["blockedVillageUnits"] = _blockedVillageUnits,
                ["invalidVillageQuotes"] = _invalidVillageQuotes,
                ["currentProcessPurchases"] = _processPurchases, ["currentProcessPoolChanges"] = _processPoolChanges,
                ["unsupported"] = state?.TariffUnsupported ?? 0, ["sequence"] = state?.TariffSequence ?? 0,
                ["sourceKeyFields"] = new JArray("town", "ownerClan", "ownerKingdom", "buyerClass", "purpose"),
                ["salesByTownOwnerRealmBuyerPurpose"] = JObject.FromObject(state?.TariffSales ?? new System.Collections.Generic.Dictionary<string, EconomyTariffTotals>()),
                ["poolKeyFields"] = new JArray("town", "ownerClan", "ownerKingdom"),
                ["poolChangesByTownOwnerRealm"] = JObject.FromObject(state?.TariffPools ?? new System.Collections.Generic.Dictionary<string, EconomyTariffPoolTotals>()),
                ["recent"] = detailed ? JArray.FromObject(state?.TariffRecent ?? new System.Collections.Generic.List<EconomyTariffReceipt>()) : new JArray(),
                ["coverageNote"] = "New observed purchases only; pool changes outside SellItemsAction remain explicit. Historical totals do not prove current-process or all-source coverage." };
        }
    }
}
