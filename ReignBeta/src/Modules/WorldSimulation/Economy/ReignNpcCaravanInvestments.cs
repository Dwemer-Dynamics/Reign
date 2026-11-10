using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using Reign.Shared;
using Reign.Core.Contracts.Economy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Economy
{
    internal static class ReignNpcCaravanInvestments
    {
        private const string HookOwner = "com.bannerlordreign.reignbeta.npccaravaninvestment";
        private static bool _bound;
        private static string _bindingError = "";
        [ThreadStatic] private static LaunchScope _launch;
        private static EconomyCaravanState State => ReignWarEconomyTransactions.State?.CaravanInvestments;

        private sealed class LaunchScope
        {
            internal Hero Owner;
            internal Settlement Town;
            internal EconomyCaravanReceipt Receipt;
            internal MobileParty Party;
            internal bool Funded, DuplicateSeed, AmbiguousCreation;
            internal bool Matches(MobileParty party) => party?.CaravanPartyComponent?.Owner == Owner
                && party.CaravanPartyComponent.Settlement == Town && party.LeaderHero == null;
        }

        internal static void Install()
        {
            if (_bound) return;
            var harmony = new Harmony(HookOwner);
            try
            {
                var seed = AccessTools.Method(typeof(MobileParty), nameof(MobileParty.InitializePartyTrade), new[] { typeof(int) })
                    ?? throw new InvalidOperationException("Native caravan trade initialization is unavailable.");
                var destroy = AccessTools.Method(typeof(DestroyPartyAction), "ApplyInternal", new[] { typeof(PartyBase), typeof(MobileParty) })
                    ?? throw new InvalidOperationException("Native caravan destruction is unavailable.");
                harmony.Patch(seed, prefix: new HarmonyMethod(typeof(ReignNpcCaravanInvestments), nameof(InitializeTradePrefix)));
                harmony.Patch(destroy, transpiler: new HarmonyMethod(typeof(ReignNpcCaravanInvestments), nameof(InsuranceTranspiler)));
                _bound = true;
                _bindingError = "";
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(HookOwner);
                _bindingError = ex.Message;
            }
        }

        private static bool InitializeTradePrefix(MobileParty __instance, ref int initialGold)
        {
            var scope = _launch;
            if (scope == null || scope.Party != __instance || !scope.Matches(__instance)) return true;
            if (scope.Funded)
            {
                scope.DuplicateSeed = true;
                return false;
            }
            initialGold = scope.Receipt.Capital;
            scope.Funded = true;
            return true;
        }

        internal static void OnCreated(MobileParty party)
        {
            var scope = _launch;
            if (scope == null || !scope.Matches(party)) return;
            if (scope.Party != null)
            {
                scope.AmbiguousCreation = true;
                throw new InvalidOperationException("Ambiguous native caravan creation.");
            }
            scope.Party = party;
        }

        private static IEnumerable<CodeInstruction> InsuranceTranspiler(IEnumerable<CodeInstruction> source)
        {
            var code = source.ToList();
            var pay = AccessTools.Method(typeof(GiveGoldAction), nameof(GiveGoldAction.ApplyBetweenCharacters),
                new[] { typeof(Hero), typeof(Hero), typeof(int), typeof(bool) });
            var matches = code.Where(x => x.Calls(pay)).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("Expected exactly one native insurance award.");
            var call = matches[0];
            var loadParty = new CodeInstruction(OpCodes.Ldarg_1);
            loadParty.labels.AddRange(call.labels);
            loadParty.blocks.AddRange(call.blocks);
            call.labels.Clear();
            call.blocks.Clear();
            code.Insert(code.IndexOf(call), loadParty);
            call.operand = AccessTools.Method(typeof(ReignNpcCaravanInvestments), nameof(PayNativeInsurance));
            return code;
        }

        private static void PayNativeInsurance(Hero giver, Hero receiver, int amount, bool silent, MobileParty party)
        {
            if (Find(party) != null || (_launch?.Matches(party) ?? false)) return;
            GiveGoldAction.ApplyBetweenCharacters(giver, receiver, amount, silent);
        }

        internal static int Fee(Hero sponsor, bool convoy)
            => Fee(sponsor, convoy, ReignWarEconomyPolicy.Options);

        private static int Fee(Hero sponsor, bool convoy, WarEconomyOptions options)
        {
            int price = options.NpcTradePrice(convoy);
            var feat = DefaultCulturalFeats.AseraiTraderFeat;
            return sponsor.Culture.HasFeat(feat) ? TaleWorlds.Library.MathF.Round(price * feat.EffectBonus) : price;
        }

        internal static int InitialTradeGold(bool convoy)
            => NativeCampaign.Current.Models.CaravanModel.GetInitialTradeGold(Hero.MainHero, convoy, false);

        internal static bool CanSponsor(Clan clan)
            => clan != null && clan != Clan.PlayerClan && clan.IsNoble && !clan.IsEliminated && !clan.IsMinorFaction
                && !clan.IsUnderMercenaryService && clan.Kingdom != null
                && clan.Leader is Hero owner && owner.IsAlive && owner.IsActive && !owner.IsPrisoner
                && owner.Clan == clan && owner.Culture?.CaravanMaster != null
                && owner.HomeSettlement?.OwnerClan?.MapFaction != null;

        internal static bool CanLaunchAt(Clan clan, Settlement town)
            => CanSponsor(clan) && town?.IsTown == true && town.SiegeEvent == null && town.Party.MapEvent == null
                && town.OwnerClan?.Kingdom == clan.Kingdom && town.Town.Security >= 40
                && (!town.HasPort || ReignWarEconomyNaval.CanFundTradeConvoys)
                && town.Culture.CaravanPartyTemplates.Any(t => t.ShipHulls.Any() == town.HasPort);

        private static Settlement FindTown(Clan clan, bool convoy, IEnumerable<Settlement> towns)
        {
            var owner = clan.Leader;
            return towns.Where(town => town.HasPort == convoy && CanLaunchAt(clan, town))
                .OrderByDescending(town => town.OwnerClan == clan)
                .ThenByDescending(town => town == owner.HomeSettlement || town == owner.CurrentSettlement)
                .ThenBy(town => ReignKingdomEventRollerCore.StableHash(clan.StringId, 0, town.StringId))
                .ThenBy(town => town.StringId, StringComparer.Ordinal).FirstOrDefault();
        }

        private static bool CanUseSlot(EconomyCaravanState state, Clan clan, WarEconomyOptions options, bool convoy, int day)
        {
            if (!options.NpcTradeEnabled(convoy) || state.Pending != null
                || state.Active.Count >= ReignWarEconomyPolicy.CaravanWorldCap
                || state.Active.Values.Any(row => row.Clan == clan.StringId && row.Day == day)
                || (state.AvailableDay.TryGetValue(clan.StringId, out int available) && day < available))
                return false;

            return clan.Heroes.SelectMany(h => h.OwnedCaravans)
                .Where(c => c.MobileParty?.IsActive == true && c.CanHaveNavalNavigationCapability == convoy)
                .Select(c => c.MobileParty.StringId)
                .Concat(state.Active.Values.Where(row => row.Clan == clan.StringId && row.IsConvoy == convoy).Select(row => row.Party))
                .Distinct(StringComparer.Ordinal).Count() < options.NpcTradeLimit(convoy);
        }

        private static bool CanFund(Clan clan, int fee, int cashPercent)
            => ReignWarEconomyPolicy.CaravanPriceBelowShare(clan.Gold, fee, cashPercent)
                && ReignWarEconomyPolicy.CanFundCaravan(clan.Gold, fee, cashPercent,
                    ReignWarEconomyTransactions.ClanWages(clan), clan.AutoRecruitmentExpenses);

        internal static void Tick(int day)
        {
            var state = State;
            if (state == null) return;
            ReconcileOwners(state, day);
            var options = ReignWarEconomyPolicy.Options;
            if (!_bound || state.Pending != null || state.Active.Count >= ReignWarEconomyPolicy.CaravanWorldCap
                || options.NpcTradeDailyBasisPoints <= 0 || (!options.NpcTradeEnabled(false) && !options.NpcTradeEnabled(true)))
                return;
            string campaign = NativeCampaign.Current?.UniqueGameId;
            if (string.IsNullOrEmpty(campaign)) return;

            var townsByRealm = Town.AllTowns.Select(t => t.Settlement)
                .Where(town => town.OwnerClan?.Kingdom != null).ToLookup(town => town.OwnerClan.Kingdom);
            foreach (var clan in Clan.NonBanditFactions.Where(CanSponsor)
                .OrderBy(c => ReignKingdomEventRollerCore.StableHash(campaign, day, "npc-trade-order|" + c.StringId))
                .ThenBy(c => c.StringId, StringComparer.Ordinal))
            {
                if (state.Active.Count >= ReignWarEconomyPolicy.CaravanWorldCap || state.Pending != null) break;
                if (state.ConsideredDay.TryGetValue(clan.StringId, out int considered) && considered >= day) continue;
                var towns = townsByRealm[clan.Kingdom];
                Settlement land = null, sea = null;
                if (CanUseSlot(state, clan, options, false, day) && CanFund(clan, Fee(clan.Leader, false, options), options.NpcTradeCashPercent))
                    land = FindTown(clan, false, towns);
                if (ReignWarEconomyNaval.CanFundTradeConvoys && CanUseSlot(state, clan, options, true, day)
                    && CanFund(clan, Fee(clan.Leader, true, options), options.NpcTradeCashPercent))
                    sea = FindTown(clan, true, towns);
                if (land == null && sea == null) continue;
                state.ConsideredDay[clan.StringId] = day;
                uint roll = ReignKingdomEventRollerCore.StableHash(campaign, day, "npc-trade-roll|" + clan.StringId);
                if (roll % 10000 >= options.NpcTradeDailyBasisPoints) continue;
                var town = (roll / 10000 % 2 == 0 ? land ?? sea : sea ?? land);
                TryLaunch(clan, town, day, options, false);
            }
        }

        internal static MobileParty TryLaunch(Clan clan, Settlement town, int day, bool exerciseRollback = false)
            => TryLaunch(clan, town, day, ReignWarEconomyPolicy.Options, exerciseRollback);

        private static MobileParty TryLaunch(Clan clan, Settlement town, int day, WarEconomyOptions options, bool exerciseRollback)
        {
            var state = State;
            if (!_bound || state == null || _launch != null || !CanLaunchAt(clan, town)
                || !CanUseSlot(state, clan, options, town.HasPort, day))
                return null;
            bool convoy = town.HasPort;
            Hero owner = clan.Leader;
            int fee = Fee(owner, convoy, options);
            if (!CanFund(clan, fee, options.NpcTradeCashPercent) || town.Town.Gold > int.MaxValue - fee) return null;
            int capital = InitialTradeGold(convoy);
            var template = CaravanHelper.GetRandomCaravanTemplate(town.Culture, false, !convoy);
            if (capital <= 0 || template == null || template.ShipHulls.Any() != convoy) return null;

            var receipt = new EconomyCaravanReceipt { Clan = clan.StringId, Payer = owner.StringId,
                Town = town.StringId, Day = day, Fee = fee, Capital = capital, IsConvoy = convoy,
                CapitalFundedBySponsor = false, WalletBefore = owner.Gold, MarketBefore = town.Town.Gold };
            var scope = new LaunchScope { Owner = owner, Town = town, Receipt = receipt };
            var existing = new HashSet<MobileParty>(owner.OwnedCaravans.Select(x => x.MobileParty));
            state.Pending = receipt;
            _launch = scope;
            bool paidFee = false;
            try
            {
                // Match player checkout: one formation payment, plus native trade liquidity.
                owner.Gold -= fee;
                var party = CaravanPartyComponent.CreateCaravanParty(owner, town, template, caravanLeader: null);
                if (party == null || scope.Party != party || !scope.Matches(party) || !scope.Funded || scope.DuplicateSeed || scope.AmbiguousCreation
                    || party.ActualClan != clan || !party.IsActive || party.PartyTradeGold != capital
                    || party.CaravanPartyComponent.CanHaveNavalNavigationCapability != convoy
                    || party.MemberRoster.GetTroopCount(owner.Culture.CaravanMaster) < 1
                    || town.Town.Gold != receipt.MarketBefore || owner.Gold != receipt.WalletBefore - fee)
                    throw new InvalidOperationException("Native merchant-style formation did not match the transaction.");

                town.Town.ChangeGold(fee);
                paidFee = town.Town.Gold == receipt.MarketBefore + fee;
                if (!paidFee) throw new InvalidOperationException("Native market could not receive the exact formation fee.");
                if (exerciseRollback) throw new InvalidOperationException("Controlled interruption after the formation fee.");
                receipt.Party = party.StringId;
                receipt.NativeId = party.Id.ToString();
                if (receipt.NativeId == "0") throw new InvalidOperationException("Native caravan has no stable identity.");
                receipt.WalletAfter = owner.Gold;
                receipt.MarketAfter = town.Town.Gold;
                receipt.TradeAfter = party.PartyTradeGold;
                state.Active.Add(receipt.Party, receipt);
                receipt.Status = "active";
                state.Launches++;
                state.FeesPaid += fee;
                state.CapitalSeeded += capital;
                state.LastError = "";
                state.Pending = null;
                return party;
            }
            catch (Exception ex)
            {
                state.LastError = ex.Message;
                try
                {
                    var created = scope.Party;
                    if (created != null && scope.Matches(created) && created.IsActive)
                        DestroyPartyAction.Apply(null, created);
                    if ((created != null && MobileParty.All.Contains(created))
                        || owner.OwnedCaravans.Any(x => !existing.Contains(x.MobileParty))
                        || town.Town.Gold != receipt.MarketBefore + (paidFee ? fee : 0)
                        || owner.Gold != receipt.WalletBefore - fee)
                        throw new InvalidOperationException("Caravan rollback could not prove removal and reconcile the wallets.");
                    if (paidFee) town.Town.ChangeGold(-fee);
                    owner.Gold = receipt.WalletBefore;
                    if (owner.Gold != receipt.WalletBefore || town.Town.Gold != receipt.MarketBefore)
                        throw new InvalidOperationException("Caravan rollback did not restore the exact wallets.");
                    receipt.Status = "rolled_back";
                    state.Record(receipt);
                    state.Pending = null;
                }
                catch (Exception cleanup)
                {
                    state.LastError += " Rollback quarantined: " + cleanup.Message;
                    receipt.Status = "quarantined";
                }
                return null;
            }
            finally { _launch = null; }
        }

        private static EconomyCaravanReceipt Find(MobileParty party)
            => party != null && State?.Active.TryGetValue(party.StringId, out var row) == true
                && row.NativeId == party.Id.ToString() ? row : null;

        internal static void OnDisbanded(MobileParty party, Settlement settlement)
        {
            var row = Find(party);
            if (row != null) row.Disbanding = true;
        }

        internal static void OnDestroyed(MobileParty party, PartyBase destroyer)
        {
            var row = Find(party);
            if (row == null) return;
            row.EndGold = Math.Max(0, party.PartyTradeGold);
            row.EndCargo = party.ItemRoster.Sum(x => x.Amount);
            row.Destroyer = destroyer?.MobileParty?.StringId ?? "";
            Finish(State, row, (int)Math.Floor(CampaignTime.Now.ToDays));
        }

        private static void Finish(EconomyCaravanState state, EconomyCaravanReceipt row, int day)
        {
            row.EndDay = day;
            row.Status = row.Disbanding ? "disbanded" : "lost";
            if (row.Disbanding) state.Disbands++; else state.Losses++;
            state.AvailableDay[row.Clan] = day + (row.Disbanding
                ? ReignWarEconomyPolicy.CaravanDisbandCooldownDays : ReignWarEconomyPolicy.CaravanDefeatCooldownDays);
            state.Active.Remove(row.Party);
            state.Record(row);
        }

        private static void ReconcileOwners(EconomyCaravanState state, int day)
        {
            foreach (var row in state.Active.Values.ToList())
            {
                var party = MobileParty.All.FirstOrDefault(p => p.StringId == row.Party && p.Id.ToString() == row.NativeId);
                if (party?.CaravanPartyComponent == null || !party.IsActive)
                {
                    Finish(state, row, day);
                    continue;
                }
                var clan = Clan.All.FirstOrDefault(x => x.StringId == row.Clan);
                if (clan == null || clan.IsEliminated)
                {
                    if (party.MapEvent == null) DestroyPartyAction.Apply(null, party);
                    continue;
                }
                if (clan.Leader != null && clan.Leader.IsAlive && party.MapEvent == null
                    && party.LeaderHero != clan.Leader
                    && party.CaravanPartyComponent.Owner != clan.Leader)
                    CaravanPartyComponent.TransferCaravanOwnership(party, clan.Leader, party.CaravanPartyComponent.Settlement);
            }
        }

        internal static void ObserveRemittance(MobileParty party, Clan clan, int amount)
        {
            var row = Find(party);
            if (row == null || row.Clan != clan.StringId || amount <= 0) return;
            row.Remitted += amount;
            State.Remitted += amount;
        }

        private static IEnumerable<object> FixtureCandidates()
        {
            var towns = Town.AllTowns.Select(t => t.Settlement).ToArray();
            foreach (bool convoy in new[] { false, true })
            {
                foreach (bool landless in new[] { false, true })
                {
                    var clans = Clan.NonBanditFactions.Where(c => CanSponsor(c) && (c.Fiefs.Count == 0) == landless)
                        .OrderBy(c => c.StringId, StringComparer.Ordinal);
                    foreach (var candidate in clans.Select(clan => new { clan, town = FindTown(clan, convoy, towns) })
                        .Where(row => row.town != null).Take(16))
                        yield return new { clanId = candidate.clan.StringId, settlementId = candidate.town.StringId, landless, convoy };
                }
            }
        }

        internal static JObject Diagnostics(bool detailed)
        {
            var state = State;
            var options = ReignWarEconomyPolicy.Options;
            return new JObject { ["version"] = state?.Version ?? 0, ["formationPricingVersion"] = 2, ["bound"] = _bound,
                ["bindingError"] = _bindingError, ["enabled"] = options.NpcTradeEnabled(false) || options.NpcTradeEnabled(true),
                ["caravansEnabled"] = options.NpcTradeEnabled(false), ["convoysEnabled"] = options.NpcTradeEnabled(true),
                ["convoysAvailable"] = ReignWarEconomyNaval.CanFundTradeConvoys,
                ["launches"] = state?.Launches ?? 0, ["losses"] = state?.Losses ?? 0, ["disbands"] = state?.Disbands ?? 0,
                ["feesPaid"] = state?.FeesPaid ?? 0, ["capitalInvested"] = state?.CapitalInvested ?? 0,
                ["capitalSeeded"] = state?.CapitalSeeded ?? 0, ["remitted"] = state?.Remitted ?? 0,
                ["activeCount"] = state?.Active.Count ?? 0, ["pending"] = state?.Pending != null, ["lastError"] = state?.LastError ?? "",
                ["fixtureCandidates"] = detailed ? JArray.FromObject(FixtureCandidates().ToList()) : new JArray(),
                ["active"] = detailed && state != null ? JArray.FromObject(state.Active.Values) : new JArray(),
                ["recent"] = detailed && state != null ? JArray.FromObject(state.Recent) : new JArray() };
        }
    }
}
