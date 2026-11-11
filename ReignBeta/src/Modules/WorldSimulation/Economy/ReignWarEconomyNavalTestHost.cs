using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    // Deliberately synthetic, paused, disposable-save inputs. Never save these inputs or
    // treat their seeded currency/hulls as campaign income. Restore Current after the batch.
    internal static class ReignWarEconomyNavalTestHost
    {
        [ThreadStatic] private static Ship _fixtureShip;
        [ThreadStatic] private static Hero _fixtureGovernor;
        [ThreadStatic] private static PerkObject _merchant, _shipwright;
        private static bool FixturePerk(Hero __instance, PerkObject perk, ref bool __result)
        {
            if (__instance != _fixtureGovernor || perk != _merchant && perk != _shipwright) return true;
            __result = true; return false;
        }
        private static bool KeepFixtureShip(Ship __0, ref bool __result)
        {
            if (!ReferenceEquals(__0, _fixtureShip)) return true;
            __result = false;
            return false;
        }

        internal static JObject Run(JObject command)
        {
            var party = MobileParty.All.SingleOrDefault(p => p.StringId == command.Value<string>("partyId")
                && p.IsActive && p.IsLordParty && !p.IsMainParty && p.LeaderHero?.PartyBelongedTo == p);
            var town = Settlement.Find(command.Value<string>("settlementId"))?.Town;
            if (party?.ActualClan?.Leader == null || party.ActualClan == Clan.PlayerClan || party.MapEvent != null
                || party.SiegeEvent != null || party.LeaderHero.IsPrisoner || party.LeaderHero.IsDead
                || town == null || !town.Settlement.HasPort || town.IsUnderSiege || town.OwnerClan == Clan.PlayerClan
                || town.Governor == party.ActualClan.Leader || town.Governor?.Clan == Clan.PlayerClan
                || town.Governor != null && town.Governor.CurrentSettlement != town.Settlement
                || party.MapFaction.IsAtWarWith(town.Settlement.MapFaction)
                || ReignWarEconomyTransactions.State?.NavalPending.Count != 0
                || ReignWarEconomyTransactions.State?.NavalAcquisitionFailures != 0
                || !ReignWarEconomyNaval.Healthy || ReignWarEconomyFundingAudit.Coverage().Value<bool>("active"))
                throw new InvalidOperationException("Ship fixture needs one free NPC lord party, a safe friendly NPC port, distinct governor/payer, healthy hooks and no active trace.");
            var hull = town.AvailableShips.Where(s => s.IsTradeable && !s.IsUsedByQuest && s.ShipHull.Value > 0)
                .Select(s => s.ShipHull).OrderBy(h => h.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("Choose a port with one existing ordinary hull definition.");
            var owner = Hero.AllAliveHeroes.Where(h => h != Hero.MainHero && h.Clan != null && h.Clan != Clan.PlayerClan
                && h.Clan.Leader != null && h.Clan != party.ActualClan && h.MapFaction == party.MapFaction && h.IsActive && h.Issue == null
                && !h.IsPrisoner && !h.IsNotable && h.PartyBelongedTo == null && h.GovernorOf == null
                && h.CompanionOf == null && h.CanLeadParty()).OrderBy(h => h.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("No existing free NPC hero for the bounded disband fixture.");
            var friendly = MobileParty.AllLordParties.Where(p => p != party && p.IsActive && p.ActualClan != null && p.ActualClan != Clan.PlayerClan
                && p.LeaderHero?.PartyBelongedTo == p && p.LeaderHero.IsAlive && !p.LeaderHero.IsPrisoner
                && p.MapFaction == party.MapFaction && p.MapEvent == null && p.SiegeEvent == null).OrderBy(p => p.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("No free friendly party for the bounded transfer fixture.");
            var hostile = MobileParty.AllLordParties.Where(p => p.IsActive && p.ActualClan != null && p.ActualClan != Clan.PlayerClan
                && p.LeaderHero?.PartyBelongedTo == p && p.LeaderHero.IsAlive && !p.LeaderHero.IsPrisoner
                && p.MapEvent == null && p.SiegeEvent == null && p.MapFaction.IsAtWarWith(party.MapFaction))
                .OrderBy(p => p.StringId, StringComparer.Ordinal).FirstOrDefault()
                ?? throw new InvalidOperationException("No free hostile party for the bounded capture-action fixture.");
            var heroes = Hero.AllAliveHeroes.ToDictionary(h => h, h => h.Gold, ReignWarEconomyReferenceComparer<Hero>.Instance);
            var markets = Town.AllTowns.ToDictionary(t => t, t => t.Gold, ReignWarEconomyReferenceComparer<Town>.Instance);
            var created = new List<Ship>();
            var auxiliaryIds = new List<string>();
            var cleanupErrors = new List<Exception>();
            Exception fixtureError = null;
            var cases = new JArray();
            MobileParty auxiliary = null;
            var patch = new Harmony("com.bannerlordreign.reignbeta.naval-fixture");
            var transferTarget = AccessTools.Method(AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCShipDistributionModel"), "CanSendShipToParty")
                ?? throw new MissingMethodException("Native ship distribution fixture binding unavailable.");
            var blockTransfer = AccessTools.Method(typeof(ReignWarEconomyNavalTestHost), nameof(KeepFixtureShip));
            double day = CampaignTime.Now.ToDays;
            var payer = party.ActualClan.Leader;
            int tradeGold = party.PartyTradeGold;
            var originalGovernor = town.Governor;
            var perkTarget = AccessTools.Method(typeof(Hero), nameof(Hero.GetPerkValue), new[] { typeof(PerkObject) });
            var perkPrefix = AccessTools.Method(typeof(ReignWarEconomyNavalTestHost), nameof(FixturePerk));
            Func<PartyBase, Ship> make = target =>
            {
                var s = new Ship(hull);
                created.Add(s); s.Owner = target;
                return s;
            };
            Action<string, bool, JObject> check = (name, passed, detail) =>
            { detail["case"] = name; detail["passed"] = passed; cases.Add(detail); };
            var authorization = ReignWarEconomyNaval.AuthorizeFixture(command);
            try
            {
                payer.Gold = 1000000;
                town.ChangeGold(1000000 - town.Gold);
                var legacyShip = make(town.Settlement.Party);
                float productionNpc = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(legacyShip, town.Settlement.Party, party.Party);
                float productionPlayer = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(legacyShip, town.Settlement.Party, PartyBase.MainParty);
                using (ReignWarEconomyNaval.ObserveLegacyNative())
                {
                    float nativeNpc = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(legacyShip, town.Settlement.Party, party.Party);
                    float nativePlayer = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(legacyShip, town.Settlement.Party, PartyBase.MainParty);
                    check("native_player_and_discounted_npc_purchase_prices", productionNpc == nativeNpc
                        && productionPlayer > 0 && productionPlayer == nativePlayer,
                        new JObject { ["productionNpc"] = productionNpc, ["nativeNpc"] = nativeNpc,
                            ["productionPlayer"] = productionPlayer, ["nativePlayer"] = nativePlayer,
                            ["npcPurchaseFactor"] = ReignWarEconomyNaval.NpcMarketFactor });
                    int start = payer.Gold, market = town.Gold;
                    ChangeShipOwnerAction.ApplyByTrade(party.Party, legacyShip);
                    int cost = start - payer.Gold;
                    int afterBuy = payer.Gold;
                    ChangeShipOwnerAction.ApplyByTrade(town.Settlement.Party, legacyShip);
                    int sale = payer.Gold - afterBuy;
                    check("legacy_native_buy_sell", cost > 0 && sale > cost && town.Gold == market
                        && ReferenceEquals(legacyShip.Owner, town.Settlement.Party), new JObject {
                        ["purchaseDebit"] = cost, ["saleCredit"] = sale, ["netNativeMint"] = sale - cost,
                        ["marketChange"] = town.Gold - market, ["syntheticHull"] = legacyShip.RandomValue });
                }
                DestroyShipAction.Apply(legacyShip);

                var ship = make(town.Settlement.Party);
                int startWallet = payer.Gold, startMarket = town.Gold;
                int buyQuote = (int)NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(ship, town.Settlement.Party, party.Party);
                ChangeShipOwnerAction.ApplyByTrade(party.Party, ship);
                int buyCommission = ReignWarEconomyTransactions.State.NavalRecent.Last().CommissionPaid;
                check("funded_native_purchase", startWallet - payer.Gold == buyQuote && town.Gold - startMarket + buyCommission == buyQuote
                    && ReferenceEquals(ship.Owner, party.Party), new JObject { ["quote"] = buyQuote,
                        ["payerDebit"] = startWallet - payer.Gold, ["marketCredit"] = town.Gold - startMarket, ["commissionPaid"] = buyCommission });
                string purchaseId = ReignEconomyCampaignBehavior.Instance.NavalShipIndex[ship];
                ChangeShipOwnerAction.ApplyByTransferring(friendly.Party, ship);
                ChangeShipOwnerAction.ApplyByTransferring(party.Party, ship);
                check("friendly_transfers_preserve_purchase", ReignEconomyCampaignBehavior.Instance.NavalShipIndex[ship] == purchaseId
                    && ReignWarEconomyTransactions.State.NavalAcquisitions[purchaseId].Kind == "npc_purchase",
                    new JObject { ["receiptId"] = purchaseId, ["temporaryFriendlyParty"] = friendly.StringId });

                town.ChangeGold(-town.Gold);
                int wallet = payer.Gold;
                ChangeShipOwnerAction.ApplyByTrade(town.Settlement.Party, ship);
                check("unfunded_market_rejected", payer.Gold == wallet && town.Gold == 0 && ReferenceEquals(ship.Owner, party.Party),
                    new JObject { ["walletChange"] = payer.Gold - wallet, ["marketGold"] = town.Gold });

                town.ChangeGold(1000000);
                wallet = payer.Gold; int marketBefore = town.Gold;
                int sellQuote = (int)NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(ship, party.Party, town.Settlement.Party);
                ChangeShipOwnerAction.ApplyByTrade(town.Settlement.Party, ship);
                int saleCommission = ReignWarEconomyTransactions.State.NavalRecent.Last().CommissionPaid;
                check("funded_native_sale", payer.Gold - wallet == sellQuote && marketBefore - town.Gold - saleCommission == sellQuote
                    && sellQuote <= buyQuote && ReferenceEquals(ship.Owner, town.Settlement.Party), new JObject { ["quote"] = sellQuote,
                        ["sellerCredit"] = payer.Gold - wallet, ["marketDebit"] = marketBefore - town.Gold, ["commissionPaid"] = saleCommission });
                wallet = payer.Gold; marketBefore = town.Gold;
                int governorBefore = town.Governor?.Gold ?? 0;
                ChangeShipOwnerAction.ApplyByTrade(town.Settlement.Party, ship);
                check("duplicate_same_owner", payer.Gold == wallet && town.Gold == marketBefore
                    && (town.Governor?.Gold ?? 0) == governorBefore, new JObject { ["payerChange"] = payer.Gold - wallet });

                ChangeShipOwnerAction.ApplyByTrade(party.Party, ship);
                wallet = payer.Gold; marketBefore = town.Gold;
                ship.OnShipDamaged(ship.MaxHitPoints * 100, null, out float damageAdjustment);
                check("forced_native_sinking", ship.HitPoints <= 0 && ship.Owner == null && payer.Gold == wallet && town.Gold == marketBefore,
                    new JObject { ["hitPoints"] = ship.HitPoints, ["ownerRemoved"] = ship.Owner == null,
                        ["payerChange"] = payer.Gold - wallet, ["marketChange"] = town.Gold - marketBefore,
                        ["nativeDamageAdjustment"] = damageAdjustment });

                var valueShip = make(hostile.Party);
                ChangeShipOwnerAction.ApplyByLooting(party.Party, valueShip);
                int capturedQuote = (int)NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(valueShip, party.Party, town.Settlement.Party);
                string capturedId = ReignEconomyCampaignBehavior.Instance.NavalShipIndex[valueShip];
                check("native_capture_action_normal_resale", capturedQuote > sellQuote * 50
                    && ReignWarEconomyTransactions.State.NavalAcquisitions[capturedId].Kind == "captured",
                    new JObject { ["capturedQuote"] = capturedQuote, ["purchasedQuote"] = sellQuote,
                        ["nativeCaptureActionExercised"] = true, ["fullNativeBattleExercised"] = false });
                var cashMethod = AccessTools.Method(typeof(ReignWarEconomyNaval), "UnclaimedShipValue");
                float actual = (float)cashMethod.Invoke(null, new object[] { NativeCampaign.Current.Models.ShipCostModel, valueShip, party.Party, null });
                float original;
                using (ReignWarEconomyNaval.ObserveLegacyNative())
                    original = (float)cashMethod.Invoke(null, new object[] { NativeCampaign.Current.Models.ShipCostModel, valueShip, party.Party, null });
                check("bound_unclaimed_ship_value", actual == 0 && original > 0,
                    new JObject { ["productionCash"] = actual, ["legacyNativeCash"] = original,
                        ["fullNativeBattleExercised"] = false });

                patch.Patch(transferTarget, prefix: new HarmonyMethod(blockTransfer));
                foreach (bool legacy in new[] { true, false })
                {
                    owner.Clan.Leader.Gold = 1000000;
                    string auxiliaryId = owner.StringId + "_naval_probe_" + (++ReignWarEconomyTransactions.State.NextOperation);
                    if (MobileParty.All.Any(p => p.StringId == auxiliaryId)) throw new InvalidOperationException("Fixture party ID already exists.");
                    auxiliaryIds.Add(auxiliaryId);
                    auxiliary = LordPartyComponent.CreateLordParty(auxiliaryId,
                        owner, town.Settlement.GatePosition, 2f, town.Settlement, owner);
                    if (auxiliary.Ships.Count != 0) throw new InvalidOperationException("Auxiliary party created unexpected native ships; fixture bounds changed.");
                    var sample = make(auxiliary.Party);
                    _fixtureShip = sample;
                    auxiliary.Anchor.SetSettlement(town.Settlement);
                    int before = owner.Clan.Leader.Gold;
                    float value;
                    using (legacy ? ReignWarEconomyNaval.ObserveLegacyNative() : null)
                    {
                        value = NativeCampaign.Current.Models.ShipCostModel.GetShipTradeValue(sample, auxiliary.Party, null);
                        DestroyPartyAction.ApplyForDisbanding(auxiliary, town.Settlement);
                    }
                    int paid = owner.Clan.Leader.Gold - before;
                    check(legacy ? "legacy_native_disband_callbacks" : "production_native_disband_callbacks",
                        sample.Owner == null && (legacy ? paid == 2 * (int)value : paid == 0), new JObject {
                            ["recoveryValue"] = value, ["actualCash"] = paid, ["hullDestroyed"] = sample.Owner == null,
                            ["shipRandom"] = sample.RandomValue, ["syntheticHouseholdRequiresReload"] = true });
                    auxiliary = null; _fixtureShip = null;
                }
                var perkType = AccessTools.TypeByName("NavalDLC.CharacterDevelopment.NavalPerks+Boatswain");
                _merchant = AccessTools.Property(perkType, "MerchantPrince").GetValue(null) as PerkObject;
                _shipwright = AccessTools.Property(perkType, "MasterShipwright").GetValue(null) as PerkObject;
                _fixtureGovernor = owner;
                if (owner.CurrentSettlement != town.Settlement) TeleportHeroAction.ApplyImmediateTeleportToSettlement(owner, town.Settlement);
                ChangeGovernorAction.Apply(town, owner);
                patch.Patch(perkTarget, prefix: new HarmonyMethod(perkPrefix));
                town.ChangeGold(1000000 - town.Gold);
                int governorGold = owner.Gold, capturedSeller = payer.Gold, capturedMarket = town.Gold;
                ChangeShipOwnerAction.ApplyByTrade(town.Settlement.Party, valueShip);
                var saleReceipt = ReignWarEconomyTransactions.State.NavalRecent.Last();
                check("native_outside_trade_commission_funded", saleReceipt.CommissionQuoted > 0 && saleReceipt.CommissionPaid > 0
                    && owner.Gold - governorGold == saleReceipt.CommissionPaid
                    && payer.Gold - capturedSeller == saleReceipt.Quote
                    && capturedMarket - town.Gold == saleReceipt.Quote + saleReceipt.CommissionPaid,
                    new JObject { ["commissionQuoted"] = saleReceipt.CommissionQuoted, ["commissionPaid"] = saleReceipt.CommissionPaid });
                var commission = AccessTools.Method(typeof(ReignWarEconomyNaval), "Commission");
                var self = new EconomyNavalReceipt(); governorGold = owner.Gold; int portGold = town.Gold;
                commission.Invoke(null, new object[] { town.Settlement, owner.Clan, "MerchantPrince", 1000, self });
                check("same_clan_commission_zero", self.CommissionQuoted > 0 && self.CommissionPaid == 0
                    && owner.Gold == governorGold && town.Gold == portGold,
                    new JObject { ["commissionPaid"] = self.CommissionPaid, ["directCommissionBoundary"] = true });
                town.ChangeGold(120 - town.Gold); governorGold = owner.Gold;
                var poor = new EconomyNavalReceipt();
                commission.Invoke(null, new object[] { town.Settlement, party.ActualClan, "MerchantPrince", 1000, poor });
                check("poor_port_commission_capped", poor.CommissionQuoted >= 120 && poor.CommissionPaid == 120
                    && owner.Gold - governorGold == 120 && town.Gold == 0,
                    new JObject { ["quoted"] = poor.CommissionQuoted, ["paid"] = poor.CommissionPaid,
                        ["unpaidPersistsAsDebt"] = false, ["directCommissionBoundary"] = true });
                town.ChangeGold(1000000); payer.Gold = 1000000;
                ChangeShipOwnerAction.ApplyByTrade(party.Party, valueShip);
                valueShip.HitPoints = valueShip.MaxHitPoints * .5f;
                party.PartyTradeGold = 1000000;
                int repairWallet = party.PartyTradeGold; portGold = town.Gold; governorGold = owner.Gold;
                RepairShipAction.Apply(valueShip, town.Settlement);
                var repairReceipt = ReignWarEconomyTransactions.State.NavalRecent.Last();
                int repairPaid = repairWallet - party.PartyTradeGold;
                check("native_paid_repair_commission_funded", repairPaid > 0 && repairReceipt.CommissionPaid > 0
                    && owner.Gold - governorGold == repairReceipt.CommissionPaid
                    && town.Gold - portGold == repairPaid - repairReceipt.CommissionPaid,
                    new JObject { ["repairPaid"] = repairPaid, ["commissionPaid"] = repairReceipt.CommissionPaid });
                valueShip.HitPoints = valueShip.MaxHitPoints * .5f;
                governorGold = owner.Gold; portGold = town.Gold; repairWallet = party.PartyTradeGold;
                RepairShipAction.ApplyForFree(valueShip);
                check("native_free_repair_no_commission", owner.Gold == governorGold && town.Gold == portGold
                    && party.PartyTradeGold == repairWallet, new JObject { ["governorChange"] = owner.Gold - governorGold });
            }
            catch (Exception ex) { fixtureError = ex; }
            finally
            {
                Action<Action> clean = action => { try { action(); } catch (Exception ex) { cleanupErrors.Add(ex); } };
                clean(() => patch.Unpatch(perkTarget, perkPrefix));
                clean(() => patch.Unpatch(transferTarget, blockTransfer));
                _fixtureGovernor = null; _merchant = null; _shipwright = null;
                _fixtureShip = null;
                clean(() => authorization.Dispose());
                clean(() => { if (town.Governor != originalGovernor) ChangeGovernorAction.Apply(town, originalGovernor); });
                foreach (var p in MobileParty.All.Where(p => p.IsActive && auxiliaryIds.Contains(p.StringId)).ToList())
                    clean(() => DestroyPartyAction.Apply(null, p));
                foreach (var s in created.Where(s => s.Owner != null).ToList()) clean(() => DestroyShipAction.Apply(s));
                clean(() => ReignWarEconomyNaval.PruneAcquisitions());
                clean(() => party.PartyTradeGold = tradeGold);
                foreach (var pair in heroes) clean(() => pair.Key.Gold = pair.Value);
                foreach (var pair in markets) clean(() => pair.Key.ChangeGold(pair.Value - pair.Key.Gold));
            }
            if (fixtureError != null) cleanupErrors.Insert(0, fixtureError);
            if (cleanupErrors.Count > 0) throw new AggregateException("Ship fixture failed; all cleanup steps attempted. Full Current restore required.", cleanupErrors);
            bool patched = Harmony.GetPatchInfo(transferTarget)?.Prefixes.Any(p => p.PatchMethod == blockTransfer) == true;
            bool perkPatched = Harmony.GetPatchInfo(perkTarget)?.Prefixes.Any(p => p.PatchMethod == perkPrefix) == true;
            return new JObject { ["ok"] = cases.Count == 17 && cases.All(c => c.Value<bool>("passed"))
                    && !patched && !perkPatched && !ReignWarEconomyNaval.LegacyProbe && CampaignTime.Now.ToDays == day
                    && town.Governor == originalGovernor && heroes.All(p => p.Key.Gold == p.Value)
                    && markets.All(p => p.Key.Gold == p.Value) && created.All(s => s.Owner == null)
                    && !MobileParty.All.Any(p => p.IsActive && auxiliaryIds.Contains(p.StringId))
                    && party.PartyTradeGold == tradeGold
                    && ReignWarEconomyTransactions.State.NavalPending.Count == 0,
                ["shipFundingVersion"] = ReignWarEconomyNaval.Version, ["observations"] = cases,
                ["sourceParty"] = party.StringId, ["settlementId"] = town.Settlement.StringId,
                ["syntheticHullsAndSeededGold"] = true, ["maximumSyntheticHulls"] = 5,
                ["maximumAuxiliaryParties"] = 2, ["timeAdvanced"] = false, ["providerCalls"] = 0,
                ["fullCurrentRestoreRequired"] = true, ["fixturesAreNotHistoricalFunding"] = true,
                ["nativeBattleSimulationExercised"] = false, ["temporaryTransferHookRemoved"] = !patched,
                ["temporaryPerkHookRemoved"] = !perkPatched, ["priorGovernorRestored"] = town.Governor == originalGovernor };
        }
    }
}
