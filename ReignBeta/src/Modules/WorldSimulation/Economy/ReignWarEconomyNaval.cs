using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using Helpers;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    // These hooks change ship funding only. Native ship damage, capture, sibling transfers,
    // production, quests and ownership events continue to use their existing adapters.
    internal static class ReignWarEconomyNaval
    {
        internal const int Version = 1;
        internal const float NpcMarketFactor = .01f;
        private const string HarmonyId = "com.bannerlordreign.reignbeta.economy.naval";
        private static readonly Dictionary<string, MethodBase> Bound = new Dictionary<string, MethodBase>();
        private static readonly HashSet<string> AcquisitionKinds = new HashSet<string>(StringComparer.Ordinal) {
            "npc_purchase", "player_purchase", "market_stock", "captured", "legacy_player_unknown",
            "legacy_npc_unknown", "native_npc_grant", "native_player_grant" };
        private static string _error = "";
        private static PropertyInfo _merchantPerk, _repairPerk;
        [ThreadStatic] private static int _legacyProbe;
        [ThreadStatic] private static int _fixtureAuthorization;
        [ThreadStatic] private static Trade _trade;
        [ThreadStatic] private static Repair _repair;
        [ThreadStatic] private static Price _price;
        private const int ExpectedHooks = 15;
        private static Type CostType => AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCShipCostModel");
        private static Type DistributionType => AccessTools.TypeByName("NavalDLC.CampaignBehaviors.NavalShipDistributionCampaignBehavior");
        internal static bool LegacyProbe => _legacyProbe > 0;
        internal static bool CanFundTradeConvoys => CostType != null && Healthy;
        internal static void AssertCampaignScope()
        {
            if (_legacyProbe != 0 || _fixtureAuthorization != 0)
                throw new InvalidOperationException("Ship fixture scope must be closed before a natural campaign tick.");
        }
        internal static bool Healthy => CostType == null || Bound.Count == ExpectedHooks && _error.Length == 0 && Bound.All(x =>
        {
            var info = Harmony.GetPatchInfo(x.Value);
            return info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers)
                .Any(p => p.owner == HarmonyId && p.PatchMethod.DeclaringType == typeof(ReignWarEconomyNaval) && p.PatchMethod.Name == x.Key);
        });

        internal static IDisposable ObserveLegacyNative()
        {
            if (_fixtureAuthorization == 0 || NativeCampaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop || _trade != null)
                throw new InvalidOperationException("Legacy ship rules require the exact paused ship fixture scope.");
            _legacyProbe++;
            return new LegacyScope();
        }
        internal static IDisposable AuthorizeFixture(JObject command)
        {
            if (command.Value<string>("profile") != "ship_funding_probe"
                || string.IsNullOrWhiteSpace(command.Value<string>("campaignTestRunId"))
                || string.IsNullOrWhiteSpace(command.Value<string>("expectedSaveName"))
                || command.Value<string>("expectedSaveName") != ReignServerClient.ActiveNativeSaveName()
                || NativeCampaign.Current?.TimeControlMode != CampaignTimeControlMode.Stop)
                throw new InvalidOperationException("Ship comparison requires an enrolled paused fixture command.");
            _fixtureAuthorization++;
            return new AuthorizationScope();
        }
        private sealed class AuthorizationScope : IDisposable
        {
            private bool _disposed;
            public void Dispose() { if (!_disposed) { _disposed = true; _fixtureAuthorization--; } }
        }
        private sealed class LegacyScope : IDisposable
        {
            private bool _disposed;
            public void Dispose() { if (!_disposed) { _disposed = true; _legacyProbe--; } }
        }

        internal static void Install()
        {
            if (CostType == null || Bound.Count == ExpectedHooks || _error.Length != 0) return;
            var harmony = new Harmony(HarmonyId);
            try
            {
                var perkType = AccessTools.TypeByName("NavalDLC.CharacterDevelopment.NavalPerks+Boatswain");
                _merchantPerk = AccessTools.Property(perkType, "MerchantPrince");
                _repairPerk = AccessTools.Property(perkType, "MasterShipwright");
                if (_merchantPerk?.PropertyType != typeof(PerkObject) || _repairPerk?.PropertyType != typeof(PerkObject))
                    throw new MissingMemberException("Native naval commission perk declarations changed.");
                Bind(harmony, AccessTools.Method(CostType, "GetShipBaseValue"), nameof(PriceTranspiler), "transpiler");
                Bind(harmony, AccessTools.Method(CostType, "GetShipUpgradePieceValueInternal"), nameof(UpgradePriceTranspiler), "transpiler");
                var quote = AccessTools.Method(CostType, "GetShipTradeValue");
                Bind(harmony, quote, nameof(PricePrefix), "prefix");
                Bind(harmony, quote, nameof(PricePostfix), "postfix");
                Bind(harmony, quote, nameof(PriceFinalizer), "finalizer");
                Bind(harmony, AccessTools.Method(DistributionType, "RecoverGoldFromRemainingShipsAfterDistribution"), nameof(RecoveryPrefix), "prefix");
                Bind(harmony, AccessTools.Method(typeof(MapEvent), "LootDefeatedPartyShips"), nameof(PrizeTranspiler), "transpiler");
                var trade = AccessTools.Method(typeof(ChangeShipOwnerAction), "ApplyInternal");
                Bind(harmony, trade, nameof(TradePrefix), "prefix");
                Bind(harmony, trade, nameof(TradePostfix), "postfix");
                Bind(harmony, trade, nameof(TradeFinalizer), "finalizer");
                Bind(harmony, trade, nameof(TradeTranspiler), "transpiler");
                var behavior = AccessTools.TypeByName("NavalDLC.CampaignBehaviors.ShipTradeCampaignBehavior");
                Bind(harmony, AccessTools.Method(behavior, "OnShipOwnerChanged"), nameof(TradeCommissionPrefix), "prefix");
                Bind(harmony, AccessTools.Method(behavior, "OnShipRepaired"), nameof(RepairCommissionPrefix), "prefix");
                Bind(harmony, AccessTools.Method(typeof(RepairShipAction), "Apply"), nameof(RepairPrefix), "prefix");
                Bind(harmony, AccessTools.Method(typeof(RepairShipAction), "Apply"), nameof(RepairFinalizer), "finalizer");
            }
            catch (Exception ex)
            {
                _error = ex.ToString();
                harmony.UnpatchAll(HarmonyId);
                Bound.Clear();
                ReignLog.Warn("Reign naval funding hooks failed: " + ex);
            }
        }

        internal static void Reset()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
            Bound.Clear(); _error = ""; _trade = null; _repair = null; _price = null;
            _merchantPerk = null; _repairPerk = null;
            _legacyProbe = 0; _fixtureAuthorization = 0;
        }

        private static void Bind(Harmony harmony, MethodBase target, string name, string kind)
        {
            if (target == null) throw new MissingMethodException("Naval funding target missing: " + name);
            var hook = new HarmonyMethod(typeof(ReignWarEconomyNaval), name);
            if (kind == "prefix") hook.priority = Priority.Last;
            harmony.Patch(target, prefix: kind == "prefix" ? hook : null,
                postfix: kind == "postfix" ? hook : null,
                transpiler: kind == "transpiler" ? hook : null,
                finalizer: kind == "finalizer" ? hook : null);
            Bound.Add(name, target);
        }

        internal static JObject Coverage() => new JObject {
            ["version"] = Version, ["required"] = CostType != null,
            ["expected"] = CostType == null ? 0 : ExpectedHooks, ["bound"] = Bound.Count,
            ["healthy"] = Healthy, ["error"] = _error,
            ["priceParity"] = Bound.ContainsKey(nameof(PriceTranspiler)) && Bound.ContainsKey(nameof(UpgradePriceTranspiler)),
            ["destroyedShipCashBlocked"] = Bound.ContainsKey(nameof(RecoveryPrefix)) && Bound.ContainsKey(nameof(PrizeTranspiler)),
            ["fundedTrades"] = Bound.ContainsKey(nameof(TradeTranspiler)),
            ["npcPurchaseFactor"] = NpcMarketFactor, ["purchasedNpcResaleFactor"] = NpcMarketFactor,
            ["capturedResale"] = "native_30_percent_less_repairs", ["playerPurchase"] = "native",
            ["acquisitions"] = ReignWarEconomyTransactions.State?.NavalAcquisitions.Count ?? 0,
            ["acquisitionCoverageComplete"] = (ReignWarEconomyTransactions.State?.NavalAcquisitionFailures ?? 0) == 0,
            ["acquisitionFailures"] = ReignWarEconomyTransactions.State?.NavalAcquisitionFailures ?? 0,
            ["legacyFixtureActive"] = LegacyProbe, ["fixtureAuthorizationActive"] = _fixtureAuthorization != 0,
            ["pending"] = ReignWarEconomyTransactions.State?.NavalPending.Count ?? 0 };

        private sealed class Price
        {
            internal Price Parent;
            internal bool Sale, Discounted;
        }
        private static void PricePrefix(Ship ship, PartyBase seller, PartyBase buyer, out Price __state)
        {
            __state = null;
            if (LegacyProbe) return;
            bool sale = seller?.IsMobile == true && buyer?.IsSettlement == true;
            __state = new Price { Parent = _price, Sale = sale, Discounted = sale && DiscountedAcquisition(ship, seller) };
            _price = __state;
        }
        private static void PricePostfix(Price __state, ref float __result)
        {
            if (__state?.Discounted == true) __result = Math.Max(0, __result * NpcMarketFactor);
        }
        private static Exception PriceFinalizer(Exception __exception, Price __state)
        {
            if (__state != null) _price = __state.Parent;
            return __exception;
        }
        // A resale quote first uses normal hull/upgrade/repair values, then receives one
        // acquisition discount. Actual NPC procurement, repairs and upgrades remain native.
        private static float AiValueFactor() => !LegacyProbe && _price?.Sale == true ? 1f : NpcMarketFactor;
        private static bool DiscountedAcquisition(Ship ship, PartyBase seller)
        {
            var behavior = ReignEconomyCampaignBehavior.Instance;
            if (behavior != null && behavior.NavalShipIndex.TryGetValue(ship, out string id)
                && behavior.EconomyState.NavalAcquisitions.TryGetValue(id, out var record))
                return record.Kind == "npc_purchase" || record.Kind == "legacy_npc_unknown" || record.Kind == "native_npc_grant";
            // Legacy history is unavailable. This is an explicit owner-class fallback,
            // never a fabricated historical price or capture receipt.
            return seller?.MobileParty != null && seller.MobileParty.ActualClan != Clan.PlayerClan;
        }
        internal static void RestoreAcquisitions(ReignEconomyCampaignBehavior behavior)
        {
            behavior.NavalShips = behavior.NavalShips ?? new List<Ship>();
            behavior.NavalShipIds = behavior.NavalShipIds ?? new List<string>();
            behavior.NavalShipIndex.Clear();
            if (behavior.NavalShips.Count != behavior.NavalShipIds.Count || behavior.NavalShips.Count > 8192)
                throw new InvalidOperationException("Naval native reference/receipt lists do not match.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < behavior.NavalShips.Count; i++)
            {
                var ship = behavior.NavalShips[i]; string id = behavior.NavalShipIds[i];
                if (ship == null || string.IsNullOrWhiteSpace(id) || !ids.Add(id) || behavior.NavalShipIndex.ContainsKey(ship)
                    || !behavior.EconomyState.NavalAcquisitions.TryGetValue(id, out var record) || record.Id != id
                    || !AcquisitionKinds.Contains(record.Kind) || record.Paid < 0
                    || ship.ShipHull == null || record.Hull != ship.ShipHull.StringId)
                    throw new InvalidOperationException("Naval acquisition identity is corrupt; refusing to invent or discard a receipt.");
                behavior.NavalShipIndex.Add(ship, id);
            }
            if (ids.Count != behavior.EconomyState.NavalAcquisitions.Count)
                throw new InvalidOperationException("Naval acquisition receipt has no native ship reference.");
        }
        internal static void PruneAcquisitions()
        {
            var b = ReignEconomyCampaignBehavior.Instance;
            if (b == null) return;
            for (int i = b.NavalShips.Count - 1; i >= 0; i--)
                if (b.NavalShips[i]?.Owner == null)
                {
                    if (b.NavalShips[i] != null) b.NavalShipIndex.Remove(b.NavalShips[i]);
                    b.EconomyState.NavalAcquisitions.Remove(b.NavalShipIds[i]);
                    b.NavalShips.RemoveAt(i); b.NavalShipIds.RemoveAt(i);
                }
        }
        private static void Acquisition(Trade ctx)
        {
            var b = ReignEconomyCampaignBehavior.Instance;
            if (b == null || !ReferenceEquals(ctx.Ship.Owner, ctx.Buyer)) return;
            bool npc = ctx.Buyer?.MobileParty != null && ctx.Buyer.MobileParty.ActualClan != Clan.PlayerClan;
            string kind;
            if (ctx.Change == ChangeShipOwnerAction.ShipOwnerChangeDetail.ApplyByTrade)
            {
                if (ctx.Receipt == null) { Unsupported("Ship trade acquisition has no payment receipt."); return; }
                kind = ctx.Receipt.Buying ? npc ? "npc_purchase" : "player_purchase" : "market_stock";
            }
            else if (ctx.Change == ChangeShipOwnerAction.ShipOwnerChangeDetail.ApplyByLooting)
            {
                // This exact native action is also used for deferred player battle loot,
                // after the defeated party/faction may have gone away. Friendly transfers
                // use ApplyByTransferring and cannot promote an acquisition to captured.
                kind = "captured";
            }
            else
            {
                if (b.NavalShipIndex.ContainsKey(ctx.Ship)) return; // friendly transfer preserves acquisition
                kind = ctx.Seller?.MobileParty != null
                    ? ctx.Seller.MobileParty.ActualClan == Clan.PlayerClan ? "legacy_player_unknown" : "legacy_npc_unknown"
                    : ctx.Buyer?.IsSettlement == true ? "market_stock" : npc ? "native_npc_grant" : "native_player_grant";
            }
            if (!b.NavalShipIndex.TryGetValue(ctx.Ship, out string id))
            {
                PruneAcquisitions();
                if (b.NavalShips.Count >= 8192)
                {
                    b.EconomyState.NavalAcquisitionFailures++;
                    Unsupported("Naval acquisition capacity reached; untracked hull trades are blocked, no receipt was evicted.");
                    return;
                }
                id = Guid.NewGuid().ToString("N");
                b.NavalShips.Add(ctx.Ship); b.NavalShipIds.Add(id); b.NavalShipIndex.Add(ctx.Ship, id);
            }
            b.EconomyState.NavalAcquisitions[id] = new EconomyShipAcquisition { Id = id, Kind = kind,
                Hull = ctx.Ship.ShipHull.StringId, Party = ctx.Buyer?.MobileParty?.StringId ?? ctx.Buyer?.Settlement?.StringId ?? "",
                Paid = ctx.Receipt?.Buying == true ? ctx.Payment : 0, Day = CampaignTime.Now.ToDays };
        }
        private static IEnumerable<CodeInstruction> PriceTranspiler(IEnumerable<CodeInstruction> input)
            => ReplaceDiscount(input);
        private static IEnumerable<CodeInstruction> UpgradePriceTranspiler(IEnumerable<CodeInstruction> input)
            => ReplaceDiscount(input);
        private static IEnumerable<CodeInstruction> ReplaceDiscount(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var sites = code.Where(x => x.opcode == OpCodes.Ldc_R4 && x.operand is float value && value == .01f).ToList();
            if (sites.Count != 1) throw new InvalidOperationException("Native naval discount changed; expected one exact 0.01 site.");
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = AccessTools.Method(typeof(ReignWarEconomyNaval), nameof(AiValueFactor));
            return code;
        }

        // The caller already transfers ships to eligible sibling parties. Native RemoveParty
        // destroys its remaining hulls. Destroyed hulls generate neither a sale nor compensation.
        private static bool RecoveryPrefix() => LegacyProbe;

        private static IEnumerable<CodeInstruction> PrizeTranspiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var target = AccessTools.Method(typeof(ShipCostModel), nameof(ShipCostModel.GetShipTradeValue));
            var sites = code.Where(x => x.Calls(target)).ToList();
            if (sites.Count != 1) throw new InvalidOperationException("Native unclaimed-ship valuation changed.");
            sites[0].opcode = OpCodes.Call;
            sites[0].operand = AccessTools.Method(typeof(ReignWarEconomyNaval), nameof(UnclaimedShipValue));
            return code;
        }
        private static float UnclaimedShipValue(ShipCostModel model, Ship ship, PartyBase seller, PartyBase buyer)
            => LegacyProbe ? model.GetShipTradeValue(ship, seller, buyer) : 0;

        internal sealed class Trade
        {
            internal Trade Parent;
            internal Ship Ship;
            internal PartyBase Seller, Buyer, Mobile;
            internal Settlement Market;
            internal Hero Hero;
            internal float NativeQuote;
            internal EconomyNavalReceipt Receipt;
            internal int Payment;
            internal bool PaymentAttempted;
            internal bool PaymentUncertain;
            internal bool CommissionObserved;
            internal ChangeShipOwnerAction.ShipOwnerChangeDetail Change;
            internal int Wallet => Hero != null ? Hero.Gold : Mobile.MobileParty.PartyTradeGold;
        }

        private sealed class Repair
        {
            internal Repair Parent;
            internal Ship Ship;
            internal PartyBase Owner;
            internal Settlement Port;
            internal int Wallet, Market;
            internal float HitPoints;
            internal bool CommissionObserved;
        }

        private static void RepairPrefix(Ship ship, Settlement repairPort, out Repair __state)
        {
            __state = null;
            if (LegacyProbe || ship?.Owner?.MobileParty == null || repairPort?.Town == null) return;
            __state = new Repair { Parent = _repair, Ship = ship, Owner = ship.Owner, Port = repairPort,
                Wallet = ship.Owner.MobileParty.PartyTradeGold, Market = repairPort.Town.Gold, HitPoints = ship.HitPoints };
            _repair = __state;
        }
        private static Exception RepairFinalizer(Exception __exception, Repair __state)
        {
            if (__state != null) _repair = __state.Parent;
            return __exception;
        }
        private static bool TradeCommissionPrefix(Ship ship, PartyBase oldOwner, ChangeShipOwnerAction.ShipOwnerChangeDetail details)
        {
            if (LegacyProbe) return true;
            var ctx = _trade;
            if (details != ChangeShipOwnerAction.ShipOwnerChangeDetail.ApplyByTrade || ctx == null
                || !ReferenceEquals(ship, ctx.Ship) || !ReferenceEquals(oldOwner, ctx.Seller)
                || !ReferenceEquals(ship.Owner, ctx.Buyer) || ctx.PaymentUncertain || ctx.Payment != ctx.Receipt.Quote || ctx.CommissionObserved) return false;
            ctx.CommissionObserved = true;
            Commission(ctx.Market, ctx.Mobile.MobileParty.ActualClan, "MerchantPrince", ctx.Payment, ctx.Receipt);
            return false;
        }
        private static bool RepairCommissionPrefix(Ship ship, Settlement repairPort)
        {
            if (LegacyProbe) return true;
            var ctx = _repair;
            if (ctx == null || !ReferenceEquals(ship, ctx.Ship) || !ReferenceEquals(repairPort, ctx.Port)
                || ctx.CommissionObserved || !ReferenceEquals(ship.Owner, ctx.Owner) || ship.HitPoints <= ctx.HitPoints) return false;
            ctx.CommissionObserved = true;
            int paid = Math.Max(0, Math.Min(ctx.Wallet - ctx.Owner.MobileParty.PartyTradeGold, ctx.Port.Town.Gold - ctx.Market));
            var receipt = new EconomyNavalReceipt { Id = ++ReignWarEconomyTransactions.State.NextOperation,
                Hull = ship.ShipHull.StringId, ShipRandom = ship.RandomValue, Party = ctx.Owner.MobileParty.StringId,
                Market = repairPort.StringId, Status = "repair_commission", Quote = paid, Day = CampaignTime.Now.ToDays };
            try { Commission(ctx.Port, ctx.Owner.MobileParty.ActualClan, "MasterShipwright", paid, receipt); }
            finally { Record(receipt); }
            return false;
        }
        private static void Commission(Settlement port, Clan trader, string perkName, int actualPrice, EconomyNavalReceipt receipt)
        {
            var governor = port?.Town?.Governor;
            if (governor == null) return;
            var property = perkName == "MerchantPrince" ? _merchantPerk : _repairPerk;
            var perk = property?.GetValue(null) as PerkObject;
            if (perk == null)
            {
                receipt.Error = "Native naval commission perk has not initialized: " + perkName;
                Unsupported(receipt.Error); return;
            }
            var value = new ExplainedNumber(0);
            PerkHelper.AddPerkBonusForTown(perk, port.Town, ref value);
            receipt.CommissionQuoted = Math.Max(0, value.RoundedResultNumber);
            // The native SettlementToCharacter wrapper does not cap settlement funds.
            // Clamp explicitly: no debt, negative treasury, or bonus exceeding real business.
            int paid = ReignWarEconomyPolicy.NavalCommission(receipt.CommissionQuoted, actualPrice,
                port.Town.Gold, governor.Gold, trader != null && trader == governor.Clan);
            int before = governor.Gold;
            try { if (paid > 0) GiveGoldAction.ApplyForSettlementToCharacter(port, governor, paid); }
            finally { receipt.CommissionPaid = Math.Max(0, governor.Gold - before); }
        }

        private static bool TradePrefix(PartyBase newOwner, Ship ship,
            ChangeShipOwnerAction.ShipOwnerChangeDetail changeDetail, out Trade __state)
        {
            __state = null;
            if (LegacyProbe) return true;
            if (changeDetail != ChangeShipOwnerAction.ShipOwnerChangeDetail.ApplyByTrade)
            {
                if (ship != null && newOwner != null && !ReferenceEquals(ship.Owner, newOwner))
                    __state = new Trade { Ship = ship, Seller = ship.Owner, Buyer = newOwner, Change = changeDetail };
                return true;
            }
            var state = ReignWarEconomyTransactions.State;
            if (state == null || !Healthy || ship?.ShipHull == null || ship.Owner == null || newOwner == null) return false;
            if (ReferenceEquals(ship.Owner, newOwner)) return false; // repeated callback has no price or transfer
            var behavior = ReignEconomyCampaignBehavior.Instance;
            if (state.NavalAcquisitionFailures > 0 && !behavior.NavalShipIndex.ContainsKey(ship))
            { Unsupported("Untracked hull cannot trade after acquisition coverage failed."); return false; }
            if (behavior.NavalShips.Count >= 8192 && !behavior.NavalShipIndex.ContainsKey(ship))
            {
                PruneAcquisitions();
                if (behavior.NavalShips.Count >= 8192) { Unsupported("Ship acquisition capacity reached; trade was not charged."); return false; }
            }
            bool buying = ship.Owner.IsSettlement && newOwner.IsMobile;
            bool selling = ship.Owner.IsMobile && newOwner.IsSettlement;
            if (!buying && !selling) { Unsupported("Unsupported ship trade endpoints."); return false; }
            var mobile = buying ? newOwner : ship.Owner;
            var market = buying ? ship.Owner.Settlement : newOwner.Settlement;
            var party = mobile.MobileParty;
            Hero hero = party.IsCaravan || party.IsVillager ? null : party.ActualClan?.Leader ?? party.LeaderHero;
            var receipt = new EconomyNavalReceipt { Id = ++state.NextOperation,
                Hull = ship.ShipHull.StringId, ShipRandom = ship.RandomValue, Party = party.StringId,
                Market = market.StringId, Hero = hero?.StringId ?? "", Buying = buying, Day = CampaignTime.Now.ToDays };
            var ctx = new Trade { Parent = _trade, Ship = ship, Seller = ship.Owner, Buyer = newOwner,
                Mobile = mobile, Market = market, Hero = hero, Receipt = receipt, Change = changeDetail };
            ctx.NativeQuote = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(ship, ship.Owner, newOwner);
            if (float.IsNaN(ctx.NativeQuote) || float.IsInfinity(ctx.NativeQuote) || ctx.NativeQuote < 0 || (double)ctx.NativeQuote > int.MaxValue)
            { Unsupported("Unsupported ship price."); return false; }
            receipt.Quote = (int)ctx.NativeQuote;
            receipt.WalletBefore = ctx.Wallet; receipt.MarketBefore = market.SettlementComponent.Gold;
            long payer = buying ? receipt.WalletBefore : receipt.MarketBefore;
            long receiver = buying ? receipt.MarketBefore : receipt.WalletBefore;
            if (payer < receipt.Quote || receiver + receipt.Quote > int.MaxValue)
            { receipt.Status = "blocked_funds"; receipt.WalletAfter = receipt.WalletBefore;
                receipt.MarketAfter = receipt.MarketBefore; Record(receipt); return false; }
            if (state.NavalPending.Count >= 32 || state.NavalPending.Any(x => x.ShipRandom == receipt.ShipRandom && x.Hull == receipt.Hull))
            { Unsupported("Ship has an unresolved transaction or transaction capacity was reached."); return false; }
            state.NavalPending.Add(receipt);
            __state = ctx; _trade = ctx;
            return true;
        }

        private static void TradePostfix(Trade __state)
        {
            if (__state?.Receipt == null) return;
            if (!ReferenceEquals(__state.Ship.Owner, __state.Buyer))
                __state.Receipt.Error = "Native ship trade did not transfer the hull.";
            if (__state.Payment != __state.Receipt.Quote)
                __state.Receipt.Error = "Native ship payment did not match its accepted quote.";
        }

        private static Exception TradeFinalizer(Exception __exception, Trade __state)
        {
            if (__state == null) return __exception;
            if (__state.Receipt == null)
            {
                // Non-trade ownership actions never push _trade; preserve any outer trade.
                try { Acquisition(__state); }
                catch (Exception ex) { Unsupported("Naval transfer acquisition failed: " + ex.Message); return __exception ?? ex; }
                return __exception;
            }
            try
            {
                var r = __state.Receipt;
                bool transferred = ReferenceEquals(__state.Ship.Owner, __state.Buyer);
                if (__state.PaymentUncertain || transferred && __state.Payment != r.Quote || !transferred && !ReferenceEquals(__state.Ship.Owner, __state.Seller))
                    throw new InvalidOperationException("Ship ownership/payment diverged; retained unresolved receipt without inventing a settlement.");
                if (!transferred && __state.Payment > 0)
                {
                    // Reverse only this price transfer; do not reset wallets or erase authored bonuses.
                    int expected = __state.Payment, before = __state.Wallet, marketBefore = __state.Market.SettlementComponent.Gold;
                    Exception callbackError = null;
                    try { Pay(__state, expected, !r.Buying); }
                    catch (Exception ex) { callbackError = ex; }
                    finally
                    {
                        long returned = r.Buying ? (long)__state.Wallet - before : (long)before - __state.Wallet;
                        long marketReturned = r.Buying ? (long)marketBefore - __state.Market.SettlementComponent.Gold
                            : (long)__state.Market.SettlementComponent.Gold - marketBefore;
                        __state.PaymentUncertain = returned < 0 || returned > expected || returned != marketReturned;
                        int safeReturned = (int)Math.Max(0, Math.Min(expected, returned));
                        r.Returned += safeReturned; __state.Payment = expected - safeReturned;
                    }
                    if (__state.Payment != 0 || __state.PaymentUncertain)
                        throw new InvalidOperationException("Ship price rollback could not return the complete transfer.");
                    if (callbackError != null) r.Error += " Refund completed before callback error: " + callbackError.Message;
                }
                r.WalletAfter = __state.Wallet; r.MarketAfter = __state.Market.SettlementComponent.Gold;
                r.Status = transferred ? (__exception == null ? "completed" : "completed_native_error") : "rolled_back";
                if (__exception != null) r.Error += " " + __exception.Message;
                if (transferred) Acquisition(__state);
                ReignWarEconomyTransactions.State.NavalPending.Remove(r);
                Record(r);
            }
            catch (Exception recovery)
            { Unsupported("Unresolved ship trade: " + recovery.Message); __state.Receipt.Status = "unresolved";
                __state.Receipt.Error += " Native: " + __exception?.Message + " Recovery: " + recovery; }
            finally
            {
                _trade = __state.Parent;
                __state.Receipt.Paid = __state.Payment;
                try
                {
                    __state.Receipt.WalletAfter = __state.Wallet;
                    __state.Receipt.MarketAfter = __state.Market.SettlementComponent.Gold;
                }
                catch (Exception ex) { Unsupported("Ship final balance snapshot failed: " + ex.Message); }
            }
            return __exception;
        }

        private static void Unsupported(string error)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state != null) { state.UnsupportedEvents++; state.LastError = error; }
        }
        private static void Record(EconomyNavalReceipt receipt)
        {
            var state = ReignWarEconomyTransactions.State;
            if (state == null) return;
            state.NavalRecent.Add(receipt);
            if (state.NavalRecent.Count > 128) state.NavalRecent.RemoveRange(0, state.NavalRecent.Count - 128);
        }

        private static void Pay(Trade ctx, int amount, bool buying, bool disabled = true)
        {
            long payer = buying ? ctx.Wallet : ctx.Market.SettlementComponent.Gold;
            long receiver = buying ? ctx.Market.SettlementComponent.Gold : ctx.Wallet;
            amount = (int)Math.Max(0L, Math.Min((long)amount, Math.Min(Math.Max(0L, payer), Math.Max(0L, int.MaxValue - receiver))));
            if (amount == 0) return;
            if (ctx.Hero != null)
            {
                if (buying) GiveGoldAction.ApplyForCharacterToSettlement(ctx.Hero, ctx.Market, amount, disabled);
                else GiveGoldAction.ApplyForSettlementToCharacter(ctx.Market, ctx.Hero, amount, disabled);
            }
            else if (buying) GiveGoldAction.ApplyForPartyToSettlement(ctx.Mobile, ctx.Market, amount, disabled);
            else GiveGoldAction.ApplyForSettlementToParty(ctx.Market, ctx.Mobile, amount, disabled);
        }

        private static float QuotedValue(ShipCostModel model, Ship ship, PartyBase seller, PartyBase buyer)
            => _trade != null && ReferenceEquals(_trade.Ship, ship) ? _trade.NativeQuote : model.GetShipTradeValue(ship, seller, buyer);
        private static void TradeBetween(Hero giver, Hero receiver, int amount, bool disabled)
        {
            if (_trade == null) { GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, disabled); return; }
            var ctx = _trade;
            if (ctx.Hero == null || (ctx.Receipt.Buying ? giver != ctx.Hero || receiver != null : giver != null || receiver != ctx.Hero))
                throw new InvalidOperationException("Unexpected ship-trade hero payer.");
            ExecutePayment(ctx, amount, disabled);
        }
        private static void TradePartyToHero(PartyBase giver, Hero receiver, int amount, bool disabled)
        {
            if (_trade == null) { GiveGoldAction.ApplyForPartyToCharacter(giver, receiver, amount, disabled); return; }
            if (!_trade.Receipt.Buying || _trade.Hero != null || giver != _trade.Mobile || receiver != null)
                throw new InvalidOperationException("Unexpected ship-trade party payer.");
            ExecutePayment(_trade, amount, disabled);
        }
        private static void TradeHeroToParty(Hero giver, PartyBase receiver, int amount, bool disabled)
        {
            if (_trade == null) { GiveGoldAction.ApplyForCharacterToParty(giver, receiver, amount, disabled); return; }
            if (_trade.Receipt.Buying || _trade.Hero != null || giver != null || receiver != _trade.Mobile)
                throw new InvalidOperationException("Unexpected ship-trade party recipient.");
            ExecutePayment(_trade, amount, disabled);
        }
        private static void ExecutePayment(Trade ctx, int amount, bool disabled)
        {
            if (ctx.PaymentAttempted || amount != ctx.Receipt.Quote) throw new InvalidOperationException("Repeated or changed ship payment.");
            ctx.PaymentAttempted = true;
            int before = ctx.Wallet;
            int marketBefore = ctx.Market.SettlementComponent.Gold;
            try { Pay(ctx, amount, ctx.Receipt.Buying, disabled); }
            finally
            {
                // Native gold events can throw after the transfer. Preserve the actual debit
                // for the finalizer instead of misreporting that failed call as zero expense.
                long delta = ctx.Receipt.Buying ? (long)before - ctx.Wallet : (long)ctx.Wallet - before;
                long marketDelta = ctx.Receipt.Buying ? (long)ctx.Market.SettlementComponent.Gold - marketBefore
                    : (long)marketBefore - ctx.Market.SettlementComponent.Gold;
                ctx.Receipt.WalletPaymentDelta = delta; ctx.Receipt.MarketPaymentDelta = marketDelta;
                ctx.PaymentUncertain = delta < 0 || delta > int.MaxValue || delta != marketDelta;
                ctx.Payment = delta >= 0 && delta <= int.MaxValue ? (int)delta : 0;
            }
            if (ctx.PaymentUncertain || ctx.Payment != ctx.Receipt.Quote)
                throw new InvalidOperationException("Ship payment changed during execution; ownership was not authorized.");
        }

        private static IEnumerable<CodeInstruction> TradeTranspiler(IEnumerable<CodeInstruction> input)
        {
            var code = input.ToList();
            var replacements = new[] {
                (AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters)), nameof(TradeBetween), 4),
                (AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyForPartyToCharacter)), nameof(TradePartyToHero), 2),
                (AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyForCharacterToParty)), nameof(TradeHeroToParty), 2),
                (AccessTools.Method(typeof(ShipCostModel), nameof(ShipCostModel.GetShipTradeValue)), nameof(QuotedValue), 1) };
            foreach (var rule in replacements)
            {
                var sites = code.Where(x => x.Calls(rule.Item1)).ToList();
                if (sites.Count != rule.Item3) throw new InvalidOperationException("Native ship trade source changed: " + rule.Item2);
                foreach (var site in sites) { site.opcode = OpCodes.Call; site.operand = AccessTools.Method(typeof(ReignWarEconomyNaval), rule.Item2); }
            }
            return code;
        }
    }
}
