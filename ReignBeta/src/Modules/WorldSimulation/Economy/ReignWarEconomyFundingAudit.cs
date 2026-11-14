using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Library;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Economy
{
    // Operator evidence only. Never changes production finance and never survives a process restart.
    internal static class ReignWarEconomyFundingAudit
    {
        internal const int MaxRows = 8192;
        internal const int MaxWallets = 32768;
        internal const int TraceVersion = 6, WalletOwnershipVersion = 1, ConsumptionObserverVersion = 1, PopulationObserverVersion = 1, OriginObserverVersion = 2;
        private static readonly string[] Components = { "CalculateClanIncomeInternal", "CalculateClanExpensesInternal",
            "AddPaymentForDebts", "AddRulingClanIncome", "AddExpensesForHiredMercenaries", "AddExpensesForTributes",
            "AddExpensesForCallToWarAgreements", "AddSettlementIncome", "AddMercenaryIncome", "AddIncomeFromKingdomBudget",
            "AddIncomeFromTribute", "AddIncomeFromTradeAgreements", "AddIncomeFromCallToWarAgrements", "AddIncomeFromParties",
            "AddExpensesFromPartiesAndGarrisons", "AddExpensesForAutoRecruitment" };
        private static Session _session;
        [ThreadStatic] private static long _transfer;
        private static long _nextTransfer;
        [ThreadStatic] private static long _financeFlow, _battleCommitFlow;
        private static readonly FieldInfo PartyGoldField = AccessTools.Field(typeof(MobileParty), "_partyTradeGold")
            ?? throw new MissingFieldException(typeof(MobileParty).FullName, "_partyTradeGold");
        private static readonly Dictionary<string, MethodBase> Hooks = new Dictionary<string, MethodBase>();

        private sealed class Session
        {
            internal NativeCampaign Campaign;
            internal long NextNode, NextFlow;
            internal HashSet<string> NodeNames = new HashSet<string>(StringComparer.Ordinal);
            internal Dictionary<object, Dictionary<string, string>> Nodes = new Dictionary<object, Dictionary<string, string>>(ReignWarEconomyReferenceComparer<object>.Instance);
            internal Dictionary<MapEvent, long> Battles = new Dictionary<MapEvent, long>(ReignWarEconomyReferenceComparer<MapEvent>.Instance);
            internal Dictionary<MapEventParty, PlannedBattleGold> BattlePlans = new Dictionary<MapEventParty, PlannedBattleGold>(ReignWarEconomyReferenceComparer<MapEventParty>.Instance);
            internal string Owner;
            internal double Day;
            internal DateTime Started;
            internal HashSet<string> Clans;
            internal Dictionary<string, Func<int>> Wallets = new Dictionary<string, Func<int>>();
            internal Dictionary<string, int> Opening = new Dictionary<string, int>();
            internal Dictionary<string, long> Net = new Dictionary<string, long>();
            internal Dictionary<string, object> OwnerNodes = new Dictionary<string, object>(StringComparer.Ordinal);
            internal Dictionary<string, JObject> OpeningOwners = new Dictionary<string, JObject>(StringComparer.Ordinal);
            internal HashSet<string> LateWallets = new HashSet<string>(StringComparer.Ordinal);
            internal Dictionary<string, long> HeroSetterCalls = new Dictionary<string, long>();
            internal Dictionary<string, string> Leaders = new Dictionary<string, string>();
            internal Dictionary<string, Func<float>> Hearths = new Dictionary<string, Func<float>>();
            internal Dictionary<string, float> OpeningHearths = new Dictionary<string, float>();
            internal Dictionary<string, double> HearthNet = new Dictionary<string, double>();
            internal JArray Rows = new JArray();
            internal JArray Transfers = new JArray();
            internal int Dropped;
            internal int Errors;
            internal int PendingAllocations;
            internal bool Expired;
            internal int ConsumptionObservations;
            internal long ConsumptionSupplement;
            internal int LeaderlessWageStores;
            internal long LeaderlessWageSupplement;
        }

        internal sealed class MarketComponentCapture
        {
            internal object SessionToken;
            internal string Wallet;
            internal int ActualBefore;
            internal long AccountedBefore;
            internal long SetterCallsBefore;
        }

        internal static long UnobservedCashDelta(int before, int after, long accountedBefore, long accountedAfter)
            => checked(((long)after - before) - (accountedAfter - accountedBefore));

        internal static MarketComponentCapture BeginLeaderlessWageStore(Hero payer)
        {
            try
            {
                if (payer == null || !Live()) return null;
                var s = _session; var wallet = HeroWallet(s, payer);
                int before = payer.Gold; AddWallet(s, wallet.Id, wallet.Read, before);
                return new MarketComponentCapture { SessionToken = s, Wallet = wallet.Id, ActualBefore = before,
                    AccountedBefore = s.Net[wallet.Id], SetterCallsBefore = s.HeroSetterCalls.TryGetValue(wallet.Id, out long calls) ? calls : 0 };
            }
            catch { if (_session != null) _session.Errors++; return null; }
        }

        internal static void EndLeaderlessWageStore(Hero payer, MarketComponentCapture capture, int requestedAfter)
        {
            if (capture == null) return;
            try
            {
                if (!Live() || !ReferenceEquals(_session, capture.SessionToken)) return;
                var s = _session; int after = payer.Gold;
                long accountedAfter = s.Net[capture.Wallet];
                long missing = UnobservedCashDelta(capture.ActualBefore, after, capture.AccountedBefore, accountedAfter);
                long setterCalls = (s.HeroSetterCalls.TryGetValue(capture.Wallet, out long calls) ? calls : 0) - capture.SetterCallsBefore;
                s.LeaderlessWageStores++;
                // The exact native store debits wages. A positive correction would be
                // unexpected overcounting and must remain failed evidence.
                bool supported = missing <= 0 && after <= capture.ActualBefore;
                if (!supported) s.Errors++;
                else { s.Net[capture.Wallet] += missing; s.LeaderlessWageSupplement += missing; }
                if (s.Rows.Count >= MaxRows) { s.Dropped++; return; }
                s.Rows.Add(new JObject { ["cashComponent"] = "native_leaderless_party_wage_store",
                    ["componentWallet"] = capture.Wallet, ["actualBefore"] = capture.ActualBefore,
                    ["walletOwner"] = DescribeWallet(s, capture.Wallet),
                    ["actualAfter"] = after, ["accountedBefore"] = capture.AccountedBefore,
                    ["requestedNativeAfter"] = requestedAfter,
                    ["accountedAfter"] = accountedAfter, ["supplementalDelta"] = missing,
                    ["setterObserverCalls"] = setterCalls, ["supported"] = supported,
                    ["financeFlowId"] = _financeFlow, ["day"] = CampaignTime.Now.ToDays,
                    ["route"] = Route(), ["physicalResourceWritesByObserver"] = 0 });
            }
            catch { if (_session != null) _session.Errors++; }
        }

        internal static MarketComponentCapture BeginConsumption(Town town, int actualBefore)
        {
            if (!Live()) return null;
            string id = NodeId(_session, town, "market", town?.Owner?.Settlement?.StringId);
            if (!_session.Net.TryGetValue(id, out long accounted)) return null;
            return new MarketComponentCapture { SessionToken = _session, Wallet = id,
                ActualBefore = actualBefore, AccountedBefore = accounted };
        }

        internal static void EndConsumption(Town town, MarketComponentCapture capture)
        {
            if (capture == null || !Live() || !ReferenceEquals(capture.SessionToken, _session)) return;
            try
            {
                var s = _session;
                int after = town.Gold;
                long accountedAfter = s.Net[capture.Wallet];
                bool supported = ReignWarEconomyLedger.TryNativeCreditRemainder(capture.ActualBefore, after,
                    capture.AccountedBefore, accountedAfter, out long supplemental);
                s.ConsumptionObservations++;
                if (!supported) s.Errors++;
                else
                {
                    // Observational net only. Never change actual gold, and never count an already observed credit twice.
                    s.Net[capture.Wallet] += supplemental;
                    s.ConsumptionSupplement += supplemental;
                }
                if (s.Rows.Count >= MaxRows) { s.Dropped++; return; }
                s.Rows.Add(new JObject { ["cashComponent"] = "native_civilian_market_consumption",
                    ["componentWallet"] = capture.Wallet, ["day"] = CampaignTime.Now.ToDays,
                    ["walletOwner"] = DescribeWallet(s, capture.Wallet),
                    ["actualBefore"] = capture.ActualBefore, ["actualAfter"] = after,
                    ["actualDelta"] = (long)after - capture.ActualBefore,
                    ["accountedBefore"] = capture.AccountedBefore, ["accountedAfter"] = accountedAfter,
                    ["supplementalDelta"] = supported ? (JToken)new JValue(supplemental) : JValue.CreateNull(),
                    ["supported"] = supported, ["physicalResourceWrites"] = 0 });
            }
            catch { if (_session != null) _session.Errors++; }
        }

        private sealed class Transfer
        {
            internal long Id;
            internal long Previous;
            internal string Route;
            internal string Giver;
            internal string Receiver;
            internal Func<int> ReadGiver;
            internal Func<int> ReadReceiver;
            internal int BeforeGiver;
            internal int BeforeReceiver;
            internal int Requested;
        }

        internal static void Install(Harmony harmony)
        {
            Hooks.Clear();
            Bind(harmony, AccessTools.PropertySetter(typeof(Hero), nameof(Hero.Gold)), nameof(HeroBefore), nameof(HeroAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Village), nameof(Village.Hearth)), nameof(HearthBefore), nameof(HearthAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(MobileParty), nameof(MobileParty.PartyTradeGold)), nameof(PartyGoldBefore), nameof(PartyGoldAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Town), nameof(Town.TradeTaxAccumulated)), nameof(TownTaxBefore), nameof(TownTaxAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Village), nameof(Village.TradeTaxAccumulated)), nameof(VillageTaxBefore), nameof(VillageTaxAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Workshop), nameof(Workshop.Capital)), nameof(WorkshopBefore), nameof(WorkshopAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Clan), nameof(Clan.TributeWallet)), nameof(ClanTributeBefore), nameof(ClanTributeAfter));
            Bind(harmony, AccessTools.PropertySetter(typeof(Kingdom), nameof(Kingdom.TributeWallet)), nameof(KingdomTributeBefore), nameof(KingdomTributeAfter));
            BindFinal(harmony, AccessTools.Method(typeof(ClanVariablesCampaignBehavior), "DailyTickClan"), nameof(FinanceFlowBefore), nameof(FinanceFlowFinally));
            Bind(harmony, AccessTools.Method(typeof(MapEvent), "CalculatePlunderedAndLostGoldAmounts"), nameof(OrdinaryPlunderBefore), nameof(OrdinaryPlunderAfter));
            Bind(harmony, AccessTools.Method(typeof(MapEvent), "LootDefeatedPartyShips"), nameof(ShipPlunderBefore), nameof(ShipPlunderAfter));
            BindFinal(harmony, AccessTools.Method(typeof(MapEventParty), "CommitGoldChanges"), nameof(BattleCommitBefore), nameof(BattleCommitFinally));
            Bind(harmony, AccessTools.PropertySetter(typeof(Kingdom), nameof(Kingdom.KingdomBudgetWallet)), nameof(KingdomBefore), nameof(KingdomAfter));
            Bind(harmony, AccessTools.Method(typeof(SettlementComponent), nameof(SettlementComponent.ChangeGold)), nameof(MarketBefore), nameof(MarketAfter));
            var give = AccessTools.Method(typeof(GiveGoldAction), "ApplyInternal", new[] { typeof(Hero), typeof(PartyBase), typeof(Hero), typeof(PartyBase), typeof(int), typeof(bool), typeof(string) })
                ?? throw new MissingMethodException("Native gold transfer boundary changed.");
            harmony.Patch(give, prefix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), nameof(TransferBefore)),
                finalizer: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), nameof(TransferFinally)));
            Hooks[nameof(TransferBefore)] = give;
            Hooks[nameof(TransferFinally)] = give;
            foreach (string name in Components)
            {
                var methods = typeof(DefaultClanFinanceModel).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(x => x.Name == name && x.GetParameters().Length >= 3
                        && x.GetParameters()[0].ParameterType == typeof(Clan)
                        && x.GetParameters()[1].ParameterType == typeof(ExplainedNumber).MakeByRefType()).ToList();
                if (methods.Count != 1) throw new MissingMethodException("Native finance component changed: " + name);
                harmony.Patch(methods[0], prefix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), nameof(ComponentBefore)),
                    postfix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), nameof(ComponentAfter)));
                Hooks[name + ":before"] = methods[0]; Hooks[name + ":after"] = methods[0];
            }
            Bind(harmony, AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateHeroIncomeFromWorkshops"), nameof(AssetBefore), nameof(AssetAfter));
        }

        private static void Bind(Harmony harmony, MethodBase method, string before, string after)
        {
            if (method == null) throw new MissingMethodException("Funding audit binding unavailable: " + before);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), before),
                postfix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), after));
            Hooks[before] = method; Hooks[after] = method;
        }

        private static void BindFinal(Harmony harmony, MethodBase method, string before, string finalizer)
        {
            if (method == null) throw new MissingMethodException("Funding origin boundary unavailable: " + before);
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), before),
                finalizer: new HarmonyMethod(typeof(ReignWarEconomyFundingAudit), finalizer));
            Hooks[before] = method; Hooks[finalizer] = method;
        }

        internal static JObject Coverage()
        {
            int bound = Hooks.Count(x => {
                var info = Harmony.GetPatchInfo(x.Value);
                string method = x.Key.EndsWith(":before", StringComparison.Ordinal) ? nameof(ComponentBefore)
                    : x.Key.EndsWith(":after", StringComparison.Ordinal) ? nameof(ComponentAfter) : x.Key;
                return info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers)
                    .Any(p => p.owner == "com.bannerlordreign.reignbeta.economy"
                        && p.PatchMethod == AccessTools.Method(typeof(ReignWarEconomyFundingAudit), method));
            });
            return new JObject { ["expected"] = 32 + 2 * Components.Length, ["bound"] = bound,
                ["active"] = _session != null, ["processLocal"] = true,
                ["fundingTraceVersion"] = TraceVersion, ["walletOwnershipVersion"] = WalletOwnershipVersion, ["consumptionObserverVersion"] = ConsumptionObserverVersion,
                ["walletLimit"] = MaxWallets, ["historyRowLimit"] = MaxRows,
                ["populationObserverVersion"] = PopulationObserverVersion, ["originObserverVersion"] = OriginObserverVersion,
                ["leaderlessWageStoreObserverVersion"] = 1 };
        }

        internal static void Reset() { _session = null; _transfer = _financeFlow = _battleCommitFlow = 0; Hooks.Clear(); }

        private static bool Live()
        {
            var s = _session;
            if (s == null) return false;
            if (!ReferenceEquals(NativeCampaign.Current, s.Campaign) || CampaignTime.Now.ToDays - s.Day > 3
                || CampaignTime.Now.ToDays < s.Day || DateTime.UtcNow - s.Started > TimeSpan.FromHours(1)) s.Expired = true;
            return !s.Expired;
        }

        private static bool Watched(Hero hero) => hero != null && _session != null;

        private static void ComponentBefore(Clan clan, bool applyWithdrawals, ref ExplainedNumber goldChange, out float? __state)
            => __state = Live() && applyWithdrawals ? (float?)goldChange.ResultNumber : null;

        private static void ComponentAfter(Clan clan, ref ExplainedNumber goldChange, float? __state, MethodBase __originalMethod)
        {
            if (!__state.HasValue || !Live()) return;
            if (_session.Rows.Count >= MaxRows) { _session.Dropped++; return; }
            _session.Rows.Add(new JObject { ["component"] = __originalMethod.Name, ["clan"] = clan.StringId,
                ["day"] = CampaignTime.Now.ToDays, ["before"] = __state.Value, ["after"] = goldChange.ResultNumber,
                ["delta"] = goldChange.ResultNumber - __state.Value, ["financeFlowId"] = _financeFlow, ["isCashMutation"] = false });
        }

        private static void AssetBefore(Hero hero, bool applyWithdrawals, ref ExplainedNumber goldChange, out float? __state)
            => __state = Live() && applyWithdrawals && Watched(hero) ? (float?)goldChange.ResultNumber : null;

        private static void AssetAfter(Hero hero, ref ExplainedNumber goldChange, float? __state)
        {
            if (!__state.HasValue || !Live()) return;
            if (_session.Rows.Count >= MaxRows) { _session.Dropped++; return; }
            _session.Rows.Add(new JObject { ["component"] = "CalculateHeroIncomeFromWorkshops", ["clan"] = hero.Clan?.StringId,
                ["hero"] = hero.StringId, ["day"] = CampaignTime.Now.ToDays, ["before"] = __state.Value,
                ["after"] = goldChange.ResultNumber, ["delta"] = goldChange.ResultNumber - __state.Value, ["isCashMutation"] = false });
        }

        private static void AddWallet(Session s, string id, Func<int> read, int? before = null)
        {
            if (s.Wallets.ContainsKey(id)) return;
            if (s.Wallets.Count >= MaxWallets) { s.Dropped++; throw new InvalidOperationException("Funding wallet observation capacity exceeded: " + MaxWallets + "."); }
            int opening = before ?? read();
            var owner = DescribeWallet(s, id);
            s.Wallets.Add(id, read); s.Opening.Add(id, opening); s.Net.Add(id, 0);
            s.OpeningOwners.Add(id, owner);
            if (ReferenceEquals(s, _session)) s.LateWallets.Add(id);
            if (ReferenceEquals(s, _session)) OriginRow(s, new JObject {
                ["originStage"] = "wallet_first_observed", ["wallet"] = id,
                ["opening"] = s.Opening[id], ["priorHistoryKnown"] = false,
                ["day"] = CampaignTime.Now.ToDays });
        }

        private static string Route()
        {
            return string.Join(" > ", (new StackTrace(2, false).GetFrames() ?? Array.Empty<StackFrame>())
                .Select(x => x.GetMethod()).Where(x => x?.DeclaringType != typeof(ReignWarEconomyFundingAudit))
                .Take(8).Select(x => x.DeclaringType?.FullName + "." + x.Name));
        }

        private static void Record(string id, Func<int> read, int before)
        {
            if (!Live()) return;
            try
            {
                int after = read();
                if (after == before) return;
                var s = _session;
                AddWallet(s, id, read, before);
                s.Net[id] += (long)after - before;
                if (s.Rows.Count >= MaxRows) { s.Dropped++; return; }
                s.Rows.Add(new JObject { ["wallet"] = id, ["before"] = before, ["after"] = after,
                    ["walletOwner"] = DescribeWallet(s, id),
                    ["delta"] = (long)after - before, ["day"] = CampaignTime.Now.ToDays,
                    ["transferId"] = _transfer, ["financeFlowId"] = _financeFlow, ["battleCommitId"] = _battleCommitFlow, ["route"] = Route() });
            }
            catch { if (_session != null) _session.Errors++; }
        }

        private static void HeroBefore(Hero __instance, out int? __state)
        {
            __state = AccountBefore(() => HeroWallet(_session, __instance));
            if (!__state.HasValue) return;
            try
            {
                var s = _session; string id = HeroWallet(s, __instance).Id;
                s.HeroSetterCalls[id] = (s.HeroSetterCalls.TryGetValue(id, out long count) ? count : 0) + 1;
            }
            catch { if (_session != null) _session.Errors++; }
        }
        private static void HearthBefore(Village __instance, out float? __state)
        {
            __state = null;
            try
            {
                if (Live() && __instance?.Settlement != null && _session.Hearths.ContainsKey(__instance.Settlement.StringId))
                    __state = __instance.Hearth;
            }
            catch { if (_session != null) _session.Errors++; }
        }

        private static void HearthAfter(Village __instance, float? __state)
        {
            if (!__state.HasValue || !Live()) return;
            try
            {
                string id = __instance.Settlement.StringId;
                float after = __instance.Hearth;
                if (after == __state.Value) return;
                double delta = (double)after - __state.Value;
                _session.HearthNet[id] += delta;
                if (_session.Rows.Count >= MaxRows) { _session.Dropped++; return; }
                _session.Rows.Add(new JObject { ["populationVillage"] = id,
                    ["beforeHearths"] = __state.Value, ["afterHearths"] = after, ["hearthDelta"] = delta,
                    ["day"] = CampaignTime.Now.ToDays, ["route"] = Route(), ["isCashMutation"] = false,
                    ["physicalResourceWrites"] = 0 });
            }
            catch { if (_session != null) _session.Errors++; }
        }
        private static void HeroAfter(Hero __instance, int? __state)
            => AccountAfter(() => HeroWallet(_session, __instance), __state);
        private static void KingdomBefore(Kingdom __instance, out int? __state)
            => __state = AccountBefore(() => (NodeId(_session, __instance, "kingdom", __instance.StringId), (Func<int>)(() => __instance.KingdomBudgetWallet)));
        private static void KingdomAfter(Kingdom __instance, int? __state)
            => AccountAfter(() => (NodeId(_session, __instance, "kingdom", __instance.StringId), (Func<int>)(() => __instance.KingdomBudgetWallet)), __state);
        private static void MarketBefore(SettlementComponent __instance, out int? __state)
            => __state = AccountBefore(() => (NodeId(_session, __instance, "market", __instance.Owner?.Settlement?.StringId), (Func<int>)(() => __instance.Gold)));
        private static void MarketAfter(SettlementComponent __instance, int? __state)
            => AccountAfter(() => (NodeId(_session, __instance, "market", __instance.Owner?.Settlement?.StringId), (Func<int>)(() => __instance.Gold)), __state);

        private static int? AccountBefore(Func<(string Id, Func<int> Read)> get)
        {
            try
            {
                if (!Live()) return null;
                var w = get(); int before = w.Read(); AddWallet(_session, w.Id, w.Read, before); return before;
            }
            catch { if (_session != null) _session.Errors++; return null; }
        }
        private static void AccountAfter(Func<(string Id, Func<int> Read)> get, int? before)
        {
            if (!before.HasValue) return;
            try { if (Live()) { var w = get(); Record(w.Id, w.Read, before.Value); } }
            catch { if (_session != null) _session.Errors++; }
        }
        private static void PartyGoldBefore(MobileParty __instance, out int? __state)
        {
            // Native lord PartyTradeGold aliases Hero.Gold. Only its physical hero wallet is counted.
            __state = __instance.IsLordParty && __instance.LeaderHero != null ? null : AccountBefore(() => PartyWallet(_session, __instance));
        }
        private static void PartyGoldAfter(MobileParty __instance, int? __state) => AccountAfter(() => PartyWallet(_session, __instance), __state);
        private static void TownTaxBefore(Town __instance, out int? __state) => __state = AccountBefore(() => (NodeId(_session, __instance, "town_tax_pool", __instance.Settlement.StringId), (Func<int>)(() => __instance.TradeTaxAccumulated)));
        private static void TownTaxAfter(Town __instance, int? __state) => AccountAfter(() => (NodeId(_session, __instance, "town_tax_pool", __instance.Settlement.StringId), (Func<int>)(() => __instance.TradeTaxAccumulated)), __state);
        private static void VillageTaxBefore(Village __instance, out int? __state) => __state = AccountBefore(() => (NodeId(_session, __instance, "village_tax_pool", __instance.Settlement.StringId), (Func<int>)(() => __instance.TradeTaxAccumulated)));
        private static void VillageTaxAfter(Village __instance, int? __state) => AccountAfter(() => (NodeId(_session, __instance, "village_tax_pool", __instance.Settlement.StringId), (Func<int>)(() => __instance.TradeTaxAccumulated)), __state);
        private static void WorkshopBefore(Workshop __instance, out int? __state) => __state = AccountBefore(() => (NodeId(_session, __instance, "workshop_capital", __instance.Settlement.StringId), (Func<int>)(() => __instance.Capital)));
        private static void WorkshopAfter(Workshop __instance, int? __state) => AccountAfter(() => (NodeId(_session, __instance, "workshop_capital", __instance.Settlement.StringId), (Func<int>)(() => __instance.Capital)), __state);
        private static void ClanTributeBefore(Clan __instance, out int? __state) => __state = AccountBefore(() => (NodeId(_session, __instance, "clan_tribute", __instance.StringId), (Func<int>)(() => __instance.TributeWallet)));
        private static void ClanTributeAfter(Clan __instance, int? __state) => AccountAfter(() => (NodeId(_session, __instance, "clan_tribute", __instance.StringId), (Func<int>)(() => __instance.TributeWallet)), __state);
        private static void KingdomTributeBefore(Kingdom __instance, out int? __state) => __state = AccountBefore(() => (NodeId(_session, __instance, "kingdom_tribute", __instance.StringId), (Func<int>)(() => __instance.TributeWallet)));
        private static void KingdomTributeAfter(Kingdom __instance, int? __state) => AccountAfter(() => (NodeId(_session, __instance, "kingdom_tribute", __instance.StringId), (Func<int>)(() => __instance.TributeWallet)), __state);

        private static void OriginRow(Session s, JObject row)
        {
            if (s.Rows.Count >= MaxRows) { s.Dropped++; return; }
            row["isCashMutation"] = false; s.Rows.Add(row);
        }
        private sealed class FinanceCapture
        { internal Session Session; internal Clan Clan; internal long Id, Previous; internal int Before; }
        private static void FinanceFlowBefore(Clan clan, out FinanceCapture __state)
        {
            __state = null;
            try
            {
                if (!Live() || clan?.Leader == null) return;
                __state = new FinanceCapture { Session = _session, Clan = clan, Id = ++_session.NextFlow, Previous = _financeFlow, Before = clan.Gold };
                _financeFlow = __state.Id;
            }
            catch { if (_session != null) _session.Errors++; }
        }
        private static Exception FinanceFlowFinally(Exception __exception, FinanceCapture __state)
        {
            if (__state == null) return __exception;
            try
            {
                if (Live() && ReferenceEquals(_session, __state.Session))
                    OriginRow(_session, new JObject { ["originStage"] = "clan_daily_finance", ["financeFlowId"] = __state.Id,
                        ["clan"] = __state.Clan.StringId, ["clanLeaderCashBefore"] = __state.Before,
                        ["clanLeaderCashAfter"] = __state.Clan.Gold, ["day"] = CampaignTime.Now.ToDays,
                        ["failedNativeOperation"] = __exception != null, ["quotesOverlapCashMutations"] = true });
            }
            catch { if (_session != null) _session.Errors++; }
            finally { _financeFlow = __state.Previous; }
            return __exception;
        }

        private sealed class PlannedBattleGold
        { internal long BattleId, Ordinary, Ships, AssignedLoss; }
        private sealed class AllocationCapture
        {
            internal Session Session; internal long BattleId; internal bool Ships;
            internal MapEventParty[] Parties; internal int[] BeforeGold, BeforeLost;
        }
        private static AllocationCapture BeginAllocation(MapEvent battle, IEnumerable<MapEventParty> winners, IEnumerable<MapEventParty> losers, bool ships)
        {
            try
            {
                if (!Live()) return null;
                var s = _session;
                var parties = winners.Concat(losers).Distinct(ReignWarEconomyReferenceComparer<MapEventParty>.Instance).ToArray();
                if (parties.Length > 1024) { s.Dropped += parties.Length; return null; }
                if (!s.Battles.TryGetValue(battle, out long id))
                {
                    if (s.Battles.Count >= 1024) { s.Dropped += parties.Length; return null; }
                    s.Battles.Add(battle, id = ++s.NextFlow);
                }
                var capture = new AllocationCapture { Session = s, BattleId = id, Ships = ships, Parties = parties,
                    BeforeGold = parties.Select(p => p.PlunderedGold).ToArray(), BeforeLost = parties.Select(p => p.GoldLost).ToArray() };
                // A native exception skips the postfix. Its outstanding capture then
                // makes export fail, even if no later settlement invokes CommitGoldChanges.
                s.PendingAllocations++;
                return capture;
            }
            catch { if (_session != null) _session.Errors++; return null; }
        }
        private static void EndAllocation(AllocationCapture state)
        {
            if (state == null) return;
            try
            {
                if (!Live() || !ReferenceEquals(_session, state.Session)) return;
                for (int i = 0; i < state.Parties.Length; i++)
                {
                    try
                    {
                    var p = state.Parties[i];
                    long gold = (long)p.PlunderedGold - state.BeforeGold[i], lost = (long)p.GoldLost - state.BeforeLost[i];
                    if (!_session.BattlePlans.TryGetValue(p, out var plan))
                    {
                        if (_session.BattlePlans.Count >= MaxRows) { _session.Dropped++; continue; }
                        _session.BattlePlans.Add(p, plan = new PlannedBattleGold { BattleId = state.BattleId });
                    }
                    if (plan.BattleId != state.BattleId) { _session.Errors++; continue; }
                    if (state.Ships) plan.Ships += gold; else plan.Ordinary += gold;
                    plan.AssignedLoss += lost;
                    OriginRow(_session, new JObject { ["originStage"] = state.Ships ? "naval_prize_cash_allocation" : "ordinary_battle_gold_allocation",
                        ["battleId"] = state.BattleId, ["partyId"] = p.Party?.Id, ["heroId"] = p.Party?.LeaderHero?.StringId,
                        ["partyNode"] = NodeId(_session, p.Party, "battle_party", p.Party?.Id.ToString()),
                        ["plunderedGoldBefore"] = state.BeforeGold[i], ["plunderedGoldAfter"] = p.PlunderedGold,
                        ["allocatedGoldDelta"] = gold, ["goldLostBefore"] = state.BeforeLost[i], ["goldLostAfter"] = p.GoldLost,
                        ["assignedLossDelta"] = lost, ["day"] = CampaignTime.Now.ToDays,
                        ["rewardModel"] = NativeCampaign.Current?.Models?.BattleRewardModel?.GetType().FullName,
                        ["shipCostModel"] = NativeCampaign.Current?.Models?.ShipCostModel?.GetType().FullName,
                        ["allocationIsNotCashPayment"] = true });
                    }
                    catch { _session.Errors++; }
                }
            }
            catch { if (_session != null) _session.Errors++; }
            finally { state.Session.PendingAllocations--; }
        }
        private static void OrdinaryPlunderBefore(MapEvent __instance, MBReadOnlyList<MapEventParty> winnerParties, MBReadOnlyList<MapEventParty> defeatedParties, out AllocationCapture __state)
            => __state = BeginAllocation(__instance, winnerParties, defeatedParties, false);
        private static void OrdinaryPlunderAfter(AllocationCapture __state) => EndAllocation(__state);
        private static void ShipPlunderBefore(MapEvent __instance, MBReadOnlyList<MapEventParty> winnerParties, MBReadOnlyList<MapEventParty> defeatedParties, out AllocationCapture __state)
            => __state = BeginAllocation(__instance, winnerParties, defeatedParties, true);
        private static void ShipPlunderAfter(AllocationCapture __state) => EndAllocation(__state);

        private sealed class BattleCommitCapture
        {
            internal Session Session; internal MapEventParty Party; internal PlannedBattleGold Plan;
            internal string Wallet; internal Func<int> Read; internal int Before, Plunder, Lost;
            internal bool HeroBranch;
            internal long Id, Previous, AccountedBefore;
        }
        private static void BattleCommitBefore(MapEventParty __instance, out BattleCommitCapture __state)
        {
            __state = null;
            try
            {
                if (!Live()) return;
                var s = _session; var party = __instance.Party;
                var wallet = party.LeaderHero != null ? Wallet(party.LeaderHero, null)
                    : party.IsMobile && party.MobileParty.IsPartyTradeActive ? Wallet(null, party) : ("none", (Func<int>)(() => 0));
                if (wallet.Item1 != "none") AddWallet(s, wallet.Item1, wallet.Item2);
                s.BattlePlans.TryGetValue(__instance, out var plan);
                __state = new BattleCommitCapture { Session = s, Party = __instance, Plan = plan, Wallet = wallet.Item1, Read = wallet.Item2,
                    Before = wallet.Item2(), Plunder = __instance.PlunderedGold, Lost = __instance.GoldLost,
                    HeroBranch = party.LeaderHero != null,
                    Id = ++s.NextFlow, Previous = _battleCommitFlow,
                    AccountedBefore = wallet.Item1 == "none" ? 0 : s.Net[wallet.Item1] };
                _battleCommitFlow = __state.Id;
                if (__state.Plunder != (plan?.Ordinary ?? 0) + (plan?.Ships ?? 0)
                    || __state.Lost != (plan?.AssignedLoss ?? 0)) s.Errors++;
            }
            catch { if (_session != null) _session.Errors++; }
        }
        private static Exception BattleCommitFinally(Exception __exception, BattleCommitCapture __state)
        {
            if (__state == null) return __exception;
            try
            {
                if (Live() && ReferenceEquals(_session, __state.Session))
                {
                    int after = __state.Read();
                    long actual = (long)after - __state.Before;
                    long recorded = __state.Wallet == "none" ? 0 : _session.Net[__state.Wallet] - __state.AccountedBefore;
                    // Native hero losses clamp to available cash after the plunder credit.
                    // Native raw party cash applies the signed net without that transfer clamp.
                    long expected = __state.Wallet == "none" ? 0
                        : __state.HeroBranch
                            ? (long)__state.Plunder - Math.Min((long)__state.Before + __state.Plunder, __state.Lost)
                            : (long)__state.Plunder - __state.Lost;
                    var tagged = _session.Rows.OfType<JObject>().Where(r => r["delta"] != null
                        && r.Value<long?>("battleCommitId") == __state.Id).ToList();
                    long taggedDelta = tagged.Sum(r => r.Value<long>("delta"));
                    bool offWallet = tagged.Any(r => r.Value<string>("wallet") != __state.Wallet);
                    if (actual != recorded || actual != expected || taggedDelta != actual || offWallet || __exception != null) _session.Errors++;
                    OriginRow(_session, new JObject { ["originStage"] = "battle_gold_settlement", ["battleCommitId"] = __state.Id,
                        ["battleId"] = __state.Plan?.BattleId, ["wallet"] = __state.Wallet,
                        ["nativeRecipientBranch"] = __state.Wallet == "none" ? "none" : __state.HeroBranch ? "hero" : "party_trade",
                        ["ordinaryAllocated"] = __state.Plan?.Ordinary ?? 0, ["navalAllocated"] = __state.Plan?.Ships ?? 0,
                        ["nativePlunderedGold"] = __state.Plunder, ["nativeGoldLost"] = __state.Lost,
                        ["cashBefore"] = __state.Before, ["cashAfter"] = after, ["actualCashDelta"] = actual,
                        ["expectedNativeCashDelta"] = expected, ["allTaggedCashDelta"] = taggedDelta,
                        ["offWalletMutation"] = offWallet, ["allocatedCashWithoutRecipient"] = __state.Wallet == "none",
                        ["observedWalletDelta"] = recorded, ["unexplained"] = actual - recorded,
                        ["day"] = CampaignTime.Now.ToDays, ["failedNativeOperation"] = __exception != null });
                }
            }
            catch { if (_session != null) _session.Errors++; }
            finally { __state.Session.BattlePlans.Remove(__state.Party); _battleCommitFlow = __state.Previous; }
            return __exception;
        }

        private static string NodeId(Session s, object node, string kind, string label)
        {
            if (!s.Nodes.TryGetValue(node, out var kinds)) s.Nodes.Add(node, kinds = new Dictionary<string, string>());
            if (kinds.TryGetValue(kind, out string existing)) return existing;
            string id = kind + ":" + label;
            if (string.IsNullOrEmpty(label) || s.NodeNames.Contains(id)) id += ":generation" + (++s.NextNode);
            s.NodeNames.Add(id);
            kinds.Add(kind, id); s.OwnerNodes.Add(id, node); return id;
        }

        // Ownership is observation, not authority to spend another hero's cash.
        // Keep object-generation identity and inactive/dead wallets so captivity,
        // succession or a party role change cannot masquerade as income/spending.
        private static JObject DescribeWallet(Session s, string id)
        {
            s.OwnerNodes.TryGetValue(id, out object node);
            int separator = id.IndexOf(':');
            string kind = separator < 0 ? "unknown" : id.Substring(0, separator);
            if (node == null) return new JObject { ["wallet"] = id, ["kind"] = kind, ["knownNode"] = false };
            Hero hero = node as Hero;
            MobileParty party = node as MobileParty;
            Workshop workshop = node as Workshop;
            Settlement place = (node as SettlementComponent)?.Owner?.Settlement ?? workshop?.Settlement;
            Clan clan = hero != null ? hero.Clan : party != null ? party.ActualClan
                : workshop != null ? workshop.Owner?.Clan : node is Clan ownedClan ? ownedClan : place?.OwnerClan;
            Kingdom kingdom = node as Kingdom ?? clan?.Kingdom;
            bool known = (kind == "hero" && hero != null) || (kind == "party" && party != null)
                || (kind == "workshop_capital" && workshop != null) || (kind == "market" && node is SettlementComponent)
                || (kind == "town_tax_pool" && node is Town) || (kind == "village_tax_pool" && node is Village)
                || (kind == "clan_tribute" && node is Clan) || ((kind == "kingdom" || kind == "kingdom_tribute") && node is Kingdom);
            return new JObject {
                ["wallet"] = id, ["kind"] = kind, ["knownNode"] = known,
                ["identityScope"] = "reference_bound_session_with_native_ids_when_available",
                ["nativeObjectId"] = hero != null ? hero.Id.ToString() : party != null ? party.Id.ToString() : null,
                ["heroId"] = hero?.StringId, ["partyId"] = party?.StringId,
                ["workshopOwnerHeroId"] = workshop?.Owner?.StringId,
                ["settlementId"] = place?.StringId, ["clanId"] = clan?.StringId,
                ["locationKingdomId"] = place?.OwnerClan?.Kingdom?.StringId,
                ["kingdomId"] = kingdom?.StringId, ["independentClan"] = clan != null && kingdom == null,
                ["heroAlive"] = hero == null ? (JToken)JValue.CreateNull() : new JValue(hero.IsAlive),
                ["heroPrisoner"] = hero == null ? (JToken)JValue.CreateNull() : new JValue(hero.IsPrisoner),
                ["heroIsClanLeader"] = hero != null && hero.Clan?.Leader == hero,
                ["heroFieldPartyId"] = hero?.PartyBelongedTo?.StringId,
                ["partyActive"] = party == null ? (JToken)JValue.CreateNull() : new JValue(party.IsActive),
                ["partyStorageExcludesHeroAlias"] = party?.IsLordParty == true && party.LeaderHero != null,
                ["day"] = NativeCampaign.Current == null ? s.Day : CampaignTime.Now.ToDays, ["physicalResourceWrites"] = 0 };
        }
        internal static int RawPartyGold(MobileParty party) => (int)PartyGoldField.GetValue(party);
        private static (string Id, Func<int> Read) HeroWallet(Session s, Hero hero)
            => (NodeId(s, hero, "hero", hero.StringId), () => hero.Gold);
        private static (string Id, Func<int> Read) PartyWallet(Session s, MobileParty party)
            => (NodeId(s, party, "party", party.Id.ToString()), () => RawPartyGold(party));
        private static (string Id, Func<int> Read) Wallet(Hero hero, PartyBase party)
        {
            if (hero != null) return HeroWallet(_session, hero);
            if (party?.IsMobile == true)
            {
                if (party.MobileParty.IsLordParty && party.LeaderHero != null) return HeroWallet(_session, party.LeaderHero);
                return PartyWallet(_session, party.MobileParty);
            }
            if (party?.IsSettlement == true) return (NodeId(_session, party.Settlement.SettlementComponent, "market", party.Settlement.StringId), () => party.Settlement.SettlementComponent.Gold);
            return ("none", () => 0);
        }

        private static void TransferBefore(Hero giverHero, PartyBase giverParty, Hero recipientHero, PartyBase recipientParty, int goldAmount, out Transfer __state)
        {
            __state = null;
            if (!Live()) return;
            try
            {
                var giver = Wallet(giverHero, giverParty); var receiver = Wallet(recipientHero, recipientParty);
                if (giver.Id != "none") AddWallet(_session, giver.Id, giver.Read);
                if (receiver.Id != "none") AddWallet(_session, receiver.Id, receiver.Read);
                __state = new Transfer { Id = ++_nextTransfer, Previous = _transfer, Giver = giver.Id,
                    Receiver = receiver.Id, ReadGiver = giver.Read, ReadReceiver = receiver.Read,
                    BeforeGiver = giver.Read(), BeforeReceiver = receiver.Read(), Requested = goldAmount, Route = Route() };
                _transfer = __state.Id;
            }
            catch { _session.Errors++; }
        }

        private static Exception TransferFinally(Exception __exception, Transfer __state)
        {
            if (__state == null) return __exception;
            try
            {
                if (Live())
                {
                    if (_session.Transfers.Count >= MaxRows) _session.Dropped++;
                    else _session.Transfers.Add(new JObject { ["id"] = __state.Id, ["day"] = CampaignTime.Now.ToDays,
                        ["giver"] = __state.Giver, ["receiver"] = __state.Receiver, ["requested"] = __state.Requested,
                        ["giverBefore"] = __state.BeforeGiver, ["giverAfter"] = __state.ReadGiver(),
                        ["receiverBefore"] = __state.BeforeReceiver, ["receiverAfter"] = __state.ReadReceiver(),
                        ["route"] = __state.Route, ["financeFlowId"] = _financeFlow, ["battleCommitId"] = _battleCommitFlow, ["threw"] = __exception != null });
                }
            }
            catch { if (_session != null) _session.Errors++; }
            finally { _transfer = __state.Previous; }
            return __exception;
        }

        internal static JObject Trace(JObject command)
        {
            string owner = command.Value<string>("campaignTestRunId");
            string source = command.Value<string>("source");
            if (source == "trace_begin")
            {
                if (_session != null) throw new InvalidOperationException("Export and end the existing funding trace first.");
                var settlement = Settlement.Find(command.Value<string>("settlementId"));
                var clans = Clan.All.Where(x => !x.IsEliminated && !x.IsBanditFaction && x.MapFaction == settlement?.MapFaction).ToList();
                if (settlement?.Town == null || clans.Count == 0 || clans.Count > 64) throw new InvalidOperationException("Trace needs a fortification in a realm with 1-64 clans.");
                var s = new Session { Campaign = NativeCampaign.Current, Owner = owner, Day = CampaignTime.Now.ToDays, Started = DateTime.UtcNow,
                    Clans = new HashSet<string>(clans.Select(x => x.StringId), StringComparer.Ordinal) };
                foreach (var clan in clans) s.Leaders[clan.StringId] = clan.Leader?.StringId;
                foreach (var hero in Hero.AllAliveHeroes.Concat(Hero.DeadOrDisabledHeroes)
                    .Concat(Clan.All.SelectMany(c => c.Heroes)).Where(h => h != null)
                    .Distinct(ReignWarEconomyReferenceComparer<Hero>.Instance))
                { var w = HeroWallet(s, hero); AddWallet(s, w.Id, w.Read); }
                // Raw storage is distinct from the public lord alias and remains observable
                // if a party changes leader. It never reads or duplicates Hero.Gold.
                foreach (var party in MobileParty.All)
                { var w = PartyWallet(s, party); AddWallet(s, w.Id, w.Read); }
                foreach (var place in Settlement.All)
                {
                    var component = place.SettlementComponent;
                    if (component != null) AddWallet(s, NodeId(s, component, "market", place.StringId), () => component.Gold);
                    if (place.Town != null)
                    {
                        var town = place.Town;
                        AddWallet(s, NodeId(s, town, "town_tax_pool", place.StringId), () => town.TradeTaxAccumulated);
                        int index = 0;
                        foreach (var shop in town.Workshops)
                        {
                            var workshop = shop;
                            if (workshop == null) continue;
                            AddWallet(s, NodeId(s, workshop, "workshop_capital", place.StringId + ":" + index++), () => workshop.Capital);
                        }
                    }
                    if (place.Village != null)
                    {
                        var village = place.Village;
                        AddWallet(s, NodeId(s, village, "village_tax_pool", place.StringId), () => village.TradeTaxAccumulated);
                        if (place.MapFaction == settlement.MapFaction)
                        { s.Hearths.Add(place.StringId, () => village.Hearth); s.OpeningHearths.Add(place.StringId, village.Hearth); s.HearthNet.Add(place.StringId, 0d); }
                    }
                }
                foreach (var kingdom in Kingdom.All)
                {
                    AddWallet(s, NodeId(s, kingdom, "kingdom", kingdom.StringId), () => kingdom.KingdomBudgetWallet);
                    AddWallet(s, NodeId(s, kingdom, "kingdom_tribute", kingdom.StringId), () => kingdom.TributeWallet);
                }
                foreach (var clan in Clan.All)
                    AddWallet(s, NodeId(s, clan, "clan_tribute", clan.StringId), () => clan.TributeWallet);
                _session = s;
            }
            else if (source != "trace_observe" && source != "trace_end") throw new ArgumentException("Unsupported funding trace control.");
            var current = _session ?? throw new InvalidOperationException("No process-local funding trace. Reloads cannot preserve this evidence.");
            if (current.Owner != owner) throw new InvalidOperationException("Funding trace belongs to another enrollment.");
            Live();
            var closingOwners = new JArray(current.Wallets.Keys.OrderBy(x => x, StringComparer.Ordinal).Select(id => DescribeWallet(current, id)));
            var openingOwners = new JArray(current.Wallets.Keys.OrderBy(x => x, StringComparer.Ordinal).Select(id => current.OpeningOwners[id].DeepClone()));
            bool ownershipComplete = openingOwners.Count == current.Wallets.Count && closingOwners.Count == current.Wallets.Count
                && openingOwners.All(x => x.Value<bool>("knownNode")) && closingOwners.All(x => x.Value<bool>("knownNode"));
            var lateOpenings = new JArray(current.LateWallets.OrderBy(x => x, StringComparer.Ordinal)
                .Where(id => current.Opening[id] != 0).Select(id => new JObject {
                    ["wallet"] = id, ["cashOnFirstObservation"] = current.Opening[id],
                    ["priorHistoryKnown"] = false, ["owner"] = current.OpeningOwners[id].DeepClone() }));
            var balances = new JArray(current.Wallets.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new JObject {
                ["wallet"] = x.Key, ["opening"] = current.Opening[x.Key], ["closing"] = x.Value(),
                ["observedNet"] = current.Net[x.Key], ["unexplained"] = (long)x.Value() - current.Opening[x.Key] - current.Net[x.Key] }));
            var changes = new JArray(Clan.All.Where(x => current.Leaders.ContainsKey(x.StringId)
                && current.Leaders[x.StringId] != x.Leader?.StringId).Select(x => new JObject {
                    ["clan"] = x.StringId, ["beforeLeader"] = current.Leaders[x.StringId], ["afterLeader"] = x.Leader?.StringId }));
            var hearthBalances = new JArray(current.Hearths.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new JObject {
                ["villageId"] = x.Key, ["opening"] = current.OpeningHearths[x.Key], ["closing"] = x.Value(),
                ["observedNet"] = current.HearthNet[x.Key],
                ["unexplained"] = (double)x.Value() - current.OpeningHearths[x.Key] - current.HearthNet[x.Key] }));
            var result = new JObject { ["ok"] = ownershipComplete && current.Dropped == 0 && current.Errors == 0 && current.PendingAllocations == 0 && !current.Expired
                    && balances.All(x => x.Value<long>("unexplained") == 0)
                    && hearthBalances.All(x => Math.Abs(x.Value<double>("unexplained")) <= 0.0001),
                ["schema"] = "reign-funding-audit-v1", ["startDay"] = current.Day, ["endDay"] = CampaignTime.Now.ToDays,
                ["fixedClanIds"] = new JArray(current.Clans.OrderBy(x => x, StringComparer.Ordinal)), ["balances"] = balances,
                ["originObserverVersion"] = OriginObserverVersion, ["walletScope"] = "world_cash_and_native_tax_business_accounts",
                ["openingWealthHistoryKnown"] = false,
                ["walletOwnershipVersion"] = WalletOwnershipVersion,
                ["walletOwnersOpening"] = openingOwners, ["walletOwnersClosing"] = closingOwners,
                ["walletOwnershipComplete"] = ownershipComplete,
                ["newNonzeroWalletOpenings"] = lateOpenings,
                ["newWalletEntryHistoryComplete"] = lateOpenings.Count == 0,
                ["observationClosureIsNotOriginClosure"] = true,
                ["populationObserverVersion"] = PopulationObserverVersion, ["hearthBalances"] = hearthBalances,
                ["mutations"] = current.Rows.DeepClone(), ["transfers"] = current.Transfers.DeepClone(), ["leaderChanges"] = changes,
                ["dropped"] = current.Dropped, ["errors"] = current.Errors, ["expired"] = current.Expired,
                ["pendingAllocations"] = current.PendingAllocations,
                ["fundingTraceVersion"] = TraceVersion, ["consumptionObserverVersion"] = ConsumptionObserverVersion,
                ["walletCount"] = current.Wallets.Count, ["walletLimit"] = MaxWallets, ["historyRowLimit"] = MaxRows,
                ["consumptionObservations"] = current.ConsumptionObservations,
                ["consumptionSupplementalNet"] = current.ConsumptionSupplement,
                ["leaderlessWageStores"] = current.LeaderlessWageStores,
                ["leaderlessWageSupplementalNet"] = current.LeaderlessWageSupplement,
                ["coverage"] = Coverage(), ["timeAdvanced"] = false, ["resourceMutations"] = 0 };
            if (source == "trace_end") _session = null;
            return result;
        }

        // Bounded native component probes. Synthetic inputs are explicit; restore Current afterwards
        // to discard native event/skill side effects. These never establish an AI political agreement.
        internal static JObject Probe(JObject command)
        {
            if (_session != null) throw new InvalidOperationException("End the observational funding trace before controlled inputs.");
            var matches = MobileParty.All.Where(x => x.StringId == command.Value<string>("partyId")
                && x.IsActive && x.LeaderHero?.PartyBelongedTo == x).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("Funding probe requires one exact active current party, not a stale reused party ID.");
            var party = matches[0];
            var town = Settlement.Find(command.Value<string>("settlementId"))?.Town;
            var clan = party?.LeaderHero?.Clan;
            if (party == null || !party.IsActive || party.MapEvent != null || !party.IsLordParty || party.IsMainParty || clan == null || clan == Clan.PlayerClan
                || clan.Leader == null || party.LeaderHero == null || town == null || town.IsUnderSiege
                || party.LeaderHero.IsPrisoner || party.LeaderHero.IsDead || town.OwnerClan == Clan.PlayerClan
                || party.MapFaction.IsAtWarWith(town.Settlement.MapFaction)) throw new InvalidOperationException("Choose a free NPC lord party and safe friendly town.");
            var heroes = Clan.All.SelectMany(x => x.Heroes).Distinct().ToDictionary(x => x, x => x.Gold);
            var markets = Settlement.All.Where(x => x.Town != null).ToDictionary(x => x.Town, x => x.Town.Gold);
            var kingdoms = Kingdom.All.ToDictionary(x => x, x => x.KingdomBudgetWallet);
            var leader = party.LeaderHero;
            var originalLeaderParty = leader.PartyBelongedTo;
            var members = party.MemberRoster.CloneRosterData(); var prisoners = party.PrisonRoster.CloneRosterData();
            float originalMorale = party.RecentEventsMorale, originalUnpaid = party.HasUnpaidWages;
            var items = party.ItemRoster.ToList(); var townItems = town.Owner.ItemRoster.ToList();
            var finance = JsonConvert.SerializeObject(ReignWarEconomyTransactions.State.Finance);
            double day = CampaignTime.Now.ToDays;
            var observations = new JArray();
            var model = new DefaultClanFinanceModel();
            string source = command.Value<string>("source");
            try
            {
                if (source == "lord_purse")
                {
                    if (party.LeaderHero == clan.Leader) throw new InvalidOperationException("Purse probe needs a non-leading lord in their own clan party.");
                    foreach (var troop in party.MemberRoster.GetTroopRoster().Where(x => !x.Character.IsHero).ToList())
                        party.MemberRoster.AddToCounts(troop.Character, -troop.Number, false, -troop.WoundedNumber);
                    int initialPurse = Math.Max(100, party.TotalWage + 100);
                    if (initialPurse >= 5000) throw new InvalidOperationException("Choose a secondary party whose hero wages fit below the native refill threshold.");
                    foreach (int budget in new[] { 50000, 250, 0 })
                    {
                        clan.Leader.Gold = budget; party.LeaderHero.Gold = initialPurse;
                        party.RecentEventsMorale = originalMorale; party.HasUnpaidWages = originalUnpaid;
                        double paidBefore = ReignWarEconomyTransactions.State.Finance.TryGetValue(clan.StringId, out var beforeTotals) ? beforeTotals.WagesPaid : 0;
                        int expense = (int)AccessTools.Method(typeof(DefaultClanFinanceModel), "AddPartyExpense")
                            .Invoke(model, new object[] { party, clan, new ExplainedNumber(0), true });
                        double paid = (ReignWarEconomyTransactions.State.Finance.TryGetValue(clan.StringId, out var afterTotals) ? afterTotals.WagesPaid : 0) - paidBefore;
                        GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, expense, true);
                        int gain = party.LeaderHero.Gold - initialPurse, debit = budget - clan.Gold;
                        observations.Add(new JObject { ["budget"] = budget, ["lordBefore"] = initialPurse,
                            ["lordAfter"] = party.LeaderHero.Gold, ["clanAfter"] = clan.Gold,
                            ["nativeExpense"] = expense, ["actualNativeWagesPaid"] = paid,
                            ["passed"] = gain + paid == debit && debit == -expense
                                && (budget != 0 || gain == 0) && (budget != 50000 || party.LeaderHero.Gold == 5000) });
                    }
                }
                else if (source == "kingdom_support")
                {
                    if (clan.Kingdom == null) throw new InvalidOperationException("Support needs an existing kingdom.");
                    foreach (int wallet in new[] { 500000, 0 })
                    {
                        clan.Leader.Gold = 1000; clan.Kingdom.KingdomBudgetWallet = wallet;
                        object[] args = { clan, new ExplainedNumber(0), true };
                        AccessTools.Method(typeof(DefaultClanFinanceModel), "AddIncomeFromKingdomBudget").Invoke(model, args);
                        int payout = TaleWorlds.Library.MathF.Round(((ExplainedNumber)args[1]).ResultNumber);
                        GiveGoldAction.ApplyBetweenCharacters(null, clan.Leader, payout, true);
                        observations.Add(new JObject { ["kingdomBefore"] = wallet, ["kingdomAfter"] = clan.Kingdom.KingdomBudgetWallet,
                            ["clanBefore"] = 1000, ["clanAfter"] = clan.Gold, ["nativeComponent"] = payout,
                            ["passed"] = clan.Gold - 1000 == wallet - clan.Kingdom.KingdomBudgetWallet
                                && (wallet != 0 || clan.Gold == 1000) });
                    }
                }
                else if (source == "market_balance")
                {
                    foreach (int wallet in new[] { 0, 200000 })
                    {
                        town.ChangeGold(wallet - town.Gold);
                        int quote = NativeCampaign.Current.Models.SettlementEconomyModel.GetTownGoldChange(town);
                        AccessTools.Method(typeof(ItemConsumptionBehavior), "UpdateTownGold")
                            .Invoke(NativeCampaign.Current.GetCampaignBehavior<ItemConsumptionBehavior>(), new object[] { town });
                        observations.Add(new JObject { ["marketBefore"] = wallet, ["marketAfter"] = town.Gold,
                            ["nativeQuote"] = quote, ["clanBefore"] = heroes[clan.Leader], ["clanAfter"] = clan.Gold,
                            ["passed"] = town.Gold - wallet == quote && clan.Gold == heroes[clan.Leader] });
                    }
                }
                else if (source == "gold_barter")
                {
                    var payer = town.OwnerClan.Leader;
                    if (payer == party.LeaderHero || payer == Hero.MainHero || payer.Gold < 1000)
                        throw new InvalidOperationException("Choose a different funded NPC town owner as barter payer.");
                    int beforePayer = payer.Gold, beforeReceiver = party.LeaderHero.Gold;
                    var gold = new GoldBarterable(payer, party.LeaderHero, payer.PartyBelongedTo?.Party, party.Party, 1000) { CurrentAmount = 1000 };
                    gold.Apply();
                    observations.Add(new JObject { ["payer"] = payer.StringId, ["recipient"] = party.LeaderHero.StringId,
                        ["payerBefore"] = beforePayer, ["payerAfter"] = payer.Gold,
                        ["recipientBefore"] = beforeReceiver, ["recipientAfter"] = party.LeaderHero.Gold,
                        ["passed"] = beforePayer - payer.Gold == 1000 && party.LeaderHero.Gold - beforeReceiver == 1000,
                        ["politicalAgreementTested"] = false });
                }
                else if (source == "prisoner_sale")
                {
                    var tiers = Game.Current.ObjectManager.GetObjectTypeList<CharacterObject>()
                        .Where(x => !x.IsHero && x.Culture == clan.Culture && x.Occupation == Occupation.Soldier && x.Tier >= 1 && x.Tier <= 6)
                        .GroupBy(x => x.Tier).OrderBy(x => x.Key).Select(x => x.OrderBy(t => t.StringId, StringComparer.Ordinal).First()).ToList();
                    if (tiers.Count != 6) throw new InvalidOperationException("Choose a main-culture party with regular troop tiers1-6 for the bounded ransom matrix.");
                    foreach (var troop in tiers)
                    {
                        party.PrisonRoster.AddToCounts(troop, 1);
                        var selected = TroopRoster.CreateDummyTroopRoster(); selected.AddToCounts(troop, 1);
                        int before = party.LeaderHero.Gold, marketBefore = town.Gold;
                        int count = party.PrisonRoster.GetTroopCount(troop);
                        var financeState = ReignWarEconomyTransactions.State;
                        double awardsBefore = financeState.Finance.TryGetValue(clan.StringId, out var prior)
                            ? prior.NativePrisonerAwardsToClanLeader + prior.NativePrisonerAwardsToOtherHeroes : 0;
                        int defaultCost = new DefaultPartyWageModel().GetTroopRecruitmentCost(troop, null).RoundedResultNumber;
                        int loadedCost = NativeCampaign.Current.Models.PartyWageModel.GetTroopRecruitmentCost(troop, null).RoundedResultNumber;
                        int quote = NativeCampaign.Current.Models.RansomValueCalculationModel.PrisonerRansomValue(troop, party.LeaderHero);
                        SellPrisonersAction.ApplyForSelectedPrisoners(party.Party, town.Owner, selected);
                        double awardsAfter = financeState.Finance.TryGetValue(clan.StringId, out var later)
                            ? later.NativePrisonerAwardsToClanLeader + later.NativePrisonerAwardsToOtherHeroes : 0;
                        bool otherWalletsUnchanged = heroes.Where(x => x.Key != party.LeaderHero).All(x => x.Key.Gold == x.Value)
                            && markets.All(x => x.Key.Gold == x.Value) && kingdoms.All(x => x.Key.KingdomBudgetWallet == x.Value);
                        observations.Add(new JObject { ["syntheticRegularPrisoners"] = 1, ["troop"] = troop.StringId, ["tier"] = troop.Tier,
                            ["defaultModelRecruitCost"] = defaultCost, ["loadedRecruitCost"] = loadedCost,
                            ["nativeQuote"] = quote, ["nullPayerAwardObserved"] = awardsAfter - awardsBefore,
                            ["recipientBefore"] = before, ["recipientAfter"] = party.LeaderHero.Gold,
                            ["marketBefore"] = marketBefore, ["marketAfter"] = town.Gold, ["otherObservedWalletsUnchanged"] = otherWalletsUnchanged,
                            ["passed"] = party.LeaderHero.Gold - before == quote && awardsAfter - awardsBefore == quote
                                && otherWalletsUnchanged && party.PrisonRoster.GetTroopCount(troop) == count - 1 });
                    }
                }
                else if (source == "market_purchase") ProbeMarketPurchases(party, town, observations);
                else if (source == "loot_sale" || source == "horse_sale")
                {
                    bool horseMarket = source == "horse_sale";
                    if (horseMarket && (party.Party.NumberOfRegularMembers < 2
                        || town.MarketData.GetItemCountOfCategory(DefaultItemCategories.Horse) == 0))
                        throw new InvalidOperationException("Horse sale probe requires at least two regulars and a town with existing ordinary horse stock.");
                    Action sell = () => {
                        if (horseMarket)
                        {
                            // Prevent this sales fixture from purchasing horses first; assets sold below are explicit bounded inputs.
                            party.LeaderHero.Gold = 0;
                            NativeCampaign.Current.GetCampaignBehavior<PartiesBuyHorseCampaignBehavior>().OnSettlementEntered(party, town.Settlement, party.LeaderHero);
                        }
                        else NativeCampaign.Current.GetCampaignBehavior<PartiesSellLootCampaignBehavior>().OnSettlementEntered(party, town.Settlement, party.LeaderHero);
                    };
                    var allItems = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>();
                    var item = allItems.FirstOrDefault(x => x.StringId == "tools")
                        ?? allItems.First(x => x.IsTradeGood && !x.IsFood && x.Value > 0);
                    party.ItemRoster.Clear(); party.ItemRoster.AddToCounts(item, 1);
                    town.ChangeGold(100000 - town.Gold);
                    if (horseMarket) party.LeaderHero.Gold = 0;
                    int before = party.LeaderHero.Gold, marketBefore = town.Gold;
                    int beforeCount = town.Owner.ItemRoster.GetItemNumber(item);
                    sell();
                    int gain = party.LeaderHero.Gold - before;
                    observations.Add(new JObject { ["syntheticLootUnits"] = 1, ["item"] = item.StringId,
                        ["recipientBefore"] = before, ["recipientAfter"] = party.LeaderHero.Gold,
                        ["marketBefore"] = marketBefore, ["marketAfter"] = town.Gold,
                        ["passed"] = gain > 0 && gain == marketBefore - town.Gold && party.ItemRoster.GetItemNumber(item) == 0
                            && town.Owner.ItemRoster.GetItemNumber(item) == beforeCount + 1 });
                    // Keep both stacks partially unsold so the native roster-index loop
                    // visits both. Its cached opening market wallet must not be spent twice.
                    int price = town.GetItemPrice(new EquipmentElement(item), party, true);
                    var second = allItems.Where(x => x != item && x.IsTradeGood && !x.IsFood && x.ItemType != ItemObject.ItemTypeEnum.Horse)
                        .OrderBy(x => x.StringId, StringComparer.Ordinal)
                        .FirstOrDefault(x => { int p = town.GetItemPrice(new EquipmentElement(x), party, true);
                            return p > 0 && p >= price / 2 && p <= price * 2; });
                    if (price <= 0 || second == null) throw new InvalidOperationException("No two bounded positive-price loot stacks available.");
                    party.ItemRoster.Clear(); party.ItemRoster.AddToCounts(item, 20); party.ItemRoster.AddToCounts(second, 20);
                    int opening = Math.Max(price, town.GetItemPrice(new EquipmentElement(second), party, true)) * 2;
                    town.ChangeGold(opening - town.Gold);
                    if (horseMarket) party.LeaderHero.Gold = 0;
                    before = party.LeaderHero.Gold;
                    int firstTown = town.Owner.ItemRoster.GetItemNumber(item), secondTown = town.Owner.ItemRoster.GetItemNumber(second);
                    sell();
                    gain = party.LeaderHero.Gold - before;
                    int firstSold = 20 - party.ItemRoster.GetItemNumber(item), secondSold = 20 - party.ItemRoster.GetItemNumber(second);
                    observations.Add(new JObject { ["case"] = "two_partial_stacks_limited_market",
                        ["syntheticLootUnits"] = 40, ["items"] = new JArray(item.StringId, second.StringId),
                        ["recipientBefore"] = before, ["recipientAfter"] = party.LeaderHero.Gold,
                        ["marketBefore"] = opening, ["marketAfter"] = town.Gold,
                        ["actualCredit"] = gain, ["actualMarketDebit"] = opening - town.Gold,
                        ["unbackedCredit"] = gain - (opening - town.Gold),
                        ["soldByStack"] = new JArray(firstSold, secondSold),
                        ["passed"] = gain > 0 && gain == opening - town.Gold
                            && town.Owner.ItemRoster.GetItemNumber(item) - firstTown == firstSold
                            && town.Owner.ItemRoster.GetItemNumber(second) - secondTown == secondSold });
                    if (horseMarket)
                    {
                        var mount = allItems.Where(x => x.IsMountable && x.GetItemCategory() == DefaultItemCategories.Horse)
                            .OrderBy(x => x.StringId, StringComparer.Ordinal)
                            .FirstOrDefault(x => town.GetItemPrice(new EquipmentElement(x), party, true) > 0);
                        if (mount == null) throw new InvalidOperationException("No positive-price ordinary mount for the bounded sale probe.");
                        foreach (bool funded in new[] { false, true })
                        {
                            party.ItemRoster.Clear(); party.ItemRoster.AddToCounts(mount, 2);
                            party.LeaderHero.Gold = 0;
                            int openingMount = funded ? town.GetItemPrice(new EquipmentElement(mount), party, true) : 0;
                            town.ChangeGold(openingMount - town.Gold);
                            int stockBefore = town.Owner.ItemRoster.GetItemNumber(mount);
                            sell();
                            int sold = 2 - party.ItemRoster.GetItemNumber(mount), credit = party.LeaderHero.Gold;
                            observations.Add(new JObject { ["case"] = funded ? "partial_mount_sale" : "empty_market_mount_sale",
                                ["syntheticMounts"] = 2, ["item"] = mount.StringId,
                                ["marketBefore"] = openingMount, ["marketAfter"] = town.Gold,
                                ["actualCredit"] = credit, ["actualMarketDebit"] = openingMount - town.Gold,
                                ["sold"] = sold, ["remaining"] = party.ItemRoster.GetItemNumber(mount),
                                ["passed"] = credit == openingMount - town.Gold && town.Gold >= 0
                                    && town.Owner.ItemRoster.GetItemNumber(mount) - stockBefore == sold
                                    && sold >= 0 && sold < 2 && (funded ? sold > 0 : sold == 0) });
                        }
                    }
                }
                else throw new ArgumentException("Unsupported native funding probe.");
            }
            finally
            {
                foreach (var pair in heroes) pair.Key.Gold = pair.Value;
                foreach (var pair in markets) pair.Key.ChangeGold(pair.Value - pair.Key.Gold);
                foreach (var pair in kingdoms) pair.Key.KingdomBudgetWallet = pair.Value;
                RestoreRegulars(party.MemberRoster, members);
                RestoreRegulars(party.PrisonRoster, prisoners);
                party.RecentEventsMorale = originalMorale; party.HasUnpaidWages = originalUnpaid;
                party.ItemRoster.Clear(); foreach (var item in items) party.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
                town.Owner.ItemRoster.Clear(); foreach (var item in townItems) town.Owner.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
                ReignWarEconomyTransactions.State.Finance = JsonConvert.DeserializeObject<Dictionary<string, EconomyFinanceTotals>>(finance);
            }
            bool restored = heroes.All(x => x.Key.Gold == x.Value) && markets.All(x => x.Key.Gold == x.Value)
                && kingdoms.All(x => x.Key.KingdomBudgetWallet == x.Value) && CampaignTime.Now.ToDays == day
                && leader.PartyBelongedTo == originalLeaderParty && party.LeaderHero == leader
                && RosterKey(party.MemberRoster) == RosterKey(members) && RosterKey(party.PrisonRoster) == RosterKey(prisoners)
                && ItemKey(party.ItemRoster) == ItemKey(items) && ItemKey(town.Owner.ItemRoster) == ItemKey(townItems);
            return new JObject { ["ok"] = restored && observations.Count > 0 && observations.All(x => x.Value<bool>("passed")),
                ["schema"] = "reign-funding-probe-v1", ["source"] = source, ["partyId"] = party.StringId,
                ["clanId"] = clan.StringId, ["observations"] = observations, ["resourceMutationsRestored"] = restored,
                ["requiresCheckpointRestore"] = true, ["nativeEventAndSkillSideEffectsDiscardByReload"] = true,
                ["timeAdvanced"] = false, ["providerCalls"] = 0, ["operatorProbe"] = true };
        }

        private static void ProbeMarketPurchases(MobileParty lord, Town town, JArray observations)
        {
            var caravan = MobileParty.All.Where(x => x.IsActive && x.IsCaravan && !x.IsMainParty
                && x.MapEvent == null && x.ActualClan != Clan.PlayerClan && x.MapFaction != null
                && !x.MapFaction.IsAtWarWith(town.Settlement.MapFaction))
                .OrderBy(x => x.StringId, StringComparer.Ordinal).FirstOrDefault();
            if (caravan == null) throw new InvalidOperationException("A real friendly NPC caravan is required for purchase payer coverage.");
            if (town.Owner.ItemRoster.Count > 4096 || lord.ItemRoster.Count > 4096 || caravan.ItemRoster.Count > 4096)
                throw new InvalidOperationException("Purchase fixture inventory exceeds bounded inputs.");
            var townItems = town.Owner.ItemRoster.ToList();
            var lordItems = lord.ItemRoster.ToList(); var caravanItems = caravan.ItemRoster.ToList();
            int townCash = town.Gold, pool = town.TradeTaxAccumulated, lordCash = lord.LeaderHero.Gold,
                caravanCash = caravan.PartyTradeGold;
            try
            {
                ReignVillageProductionCalculatorModel.ObserveWithoutRandomAdvance(() => {
                    var item = Game.Current.ObjectManager.GetObjectTypeList<ItemObject>()
                        .Where(x => x.IsMountable && x.GetItemCategory() == DefaultItemCategories.Horse)
                        .OrderBy(x => x.StringId, StringComparer.Ordinal)
                        .FirstOrDefault(x => { int quote = town.GetItemPrice(new EquipmentElement(x), lord);
                            return quote > 4 && quote < 20000; });
                    if (item == null) throw new InvalidOperationException("No bounded positive-price ordinary horse for purchase fixture.");
                    var equipment = new EquipmentElement(item);
                    foreach (var buyer in new[] { lord, caravan })
                    foreach (string scenario in new[] { "empty", "underfunded", "exact_unit", "funded", "partial_stack" })
                    {
                        town.Owner.ItemRoster.Clear(); town.Owner.ItemRoster.AddToCounts(item, 10);
                        buyer.ItemRoster.Clear(); town.ChangeGold(-town.Gold); town.TradeTaxAccumulated = 0;
                        int firstQuote = town.GetItemPrice(equipment, buyer);
                        if (firstQuote <= 4 || firstQuote > 25000) throw new InvalidOperationException("Purchase fixture price outside supported cash bounds.");
                        int cash = scenario == "empty" ? 0 : scenario == "underfunded" ? firstQuote / 2
                            : scenario == "exact_unit" ? firstQuote : scenario == "partial_stack" ? firstQuote * 2 : 100000;
                        if (buyer.IsCaravan) buyer.PartyTradeGold = cash; else buyer.LeaderHero.Gold = cash;
                        int requested = scenario == "funded" || scenario == "partial_stack" ? 4 : 1;
                        long sequence = ReignWarEconomyTransactions.State.TariffSequence;
                        SellItemsAction.Apply(town.Settlement.Party, buyer.Party, new ItemRosterElement(equipment, 10), requested, town.Settlement);
                        var receipt = ReignWarEconomyTransactions.State.TariffRecent.LastOrDefault();
                        int paid = cash - (buyer.IsCaravan ? buyer.PartyTradeGold : buyer.LeaderHero.Gold);
                        int received = buyer.ItemRoster.GetItemNumber(item);
                        int expectedPool = receipt == null ? -1 : (int)NativeCampaign.Current.Models.SettlementTaxModel
                            .GetTownCommissionChangeBasedOnSecurity(town, receipt.CommissionDebit);
                        bool expected = scenario == "empty" || scenario == "underfunded" ? received == 0 && paid == 0
                            : scenario == "partial_stack" ? received > 0 && received < requested && receipt != null
                                && receipt.MinimumBlockedNativePrice > cash - paid
                            : scenario == "exact_unit" ? received == 1 && paid == firstQuote
                            : received == requested && paid > 0;
                        observations.Add(new JObject { ["case"] = scenario, ["buyerClass"] = buyer.IsCaravan ? "caravan" : "lord",
                            ["buyerParty"] = buyer.StringId, ["syntheticMarketUnits"] = 10, ["syntheticPayerCash"] = cash,
                            ["requested"] = requested, ["received"] = received, ["actualPayment"] = paid,
                            ["syntheticLocationContext"] = true, ["expectedTariffPool"] = expectedPool,
                            ["marketCash"] = town.Gold, ["tariffPool"] = town.TradeTaxAccumulated,
                            ["receipt"] = receipt == null ? null : JObject.FromObject(receipt),
                            ["passed"] = expected && receipt != null && receipt.Id == sequence + 1
                                && ReignWarEconomyTransactions.State.TariffSequence == sequence + 1
                                && receipt.BuyerParty == buyer.StringId && receipt.Town == town.Settlement.StringId
                                && receipt.Item == item.StringId && receipt.Quantity == requested && receipt.Version == ReignWarEconomyTariffs.Version
                                && receipt.Supported && receipt.AffordabilityGuarded && receipt.QuotedUnits == requested
                                && paid >= 0 && paid <= cash && town.TradeTaxAccumulated == expectedPool
                                && paid == receipt.FulfilledNativePrice && town.Owner.ItemRoster.GetItemNumber(item) == 10 - received
                                && town.Gold + receipt.CommissionDebit == paid && town.TradeTaxAccumulated <= receipt.CommissionDebit });
                    }
                    return 0;
                });
            }
            finally
            {
                lord.LeaderHero.Gold = lordCash; caravan.PartyTradeGold = caravanCash;
                town.ChangeGold(townCash - town.Gold); town.TradeTaxAccumulated = pool;
                lord.ItemRoster.Clear(); foreach (var row in lordItems) lord.ItemRoster.AddToCounts(row.EquipmentElement, row.Amount);
                caravan.ItemRoster.Clear(); foreach (var row in caravanItems) caravan.ItemRoster.AddToCounts(row.EquipmentElement, row.Amount);
                town.Owner.ItemRoster.Clear(); foreach (var row in townItems) town.Owner.ItemRoster.AddToCounts(row.EquipmentElement, row.Amount);
            }
            observations.Add(new JObject { ["case"] = "cash_stock_pool_restored", ["passed"] = town.Gold == townCash
                && town.TradeTaxAccumulated == pool && lord.LeaderHero.Gold == lordCash && caravan.PartyTradeGold == caravanCash
                && ItemKey(lord.ItemRoster) == ItemKey(lordItems) && ItemKey(caravan.ItemRoster) == ItemKey(caravanItems)
                && ItemKey(town.Owner.ItemRoster) == ItemKey(townItems) });
        }

        private static string RosterKey(TroopRoster roster) => string.Join(";", roster.GetTroopRoster()
            .OrderBy(x => x.Character.StringId, StringComparer.Ordinal).Select(x => x.Character.StringId + ":" + x.Number + ":" + x.WoundedNumber + ":" + x.Xp));

        private static void RestoreRegulars(TroopRoster target, TroopRoster original)
        {
            if (RosterKey(target) == RosterKey(original)) return;
            // Clearing a complete native roster detaches its hero party/custody references.
            // These probes only change regulars; never remove/re-add a hero for cleanup.
            foreach (var row in target.GetTroopRoster().Where(x => !x.Character.IsHero).ToList())
                target.AddToCounts(row.Character, -row.Number, false, -row.WoundedNumber);
            foreach (var row in original.GetTroopRoster().Where(x => !x.Character.IsHero)) target.Add(row);
        }
        private static string ItemKey(IEnumerable<ItemRosterElement> roster) => string.Join(";", roster
            .OrderBy(x => x.EquipmentElement.Item.StringId, StringComparer.Ordinal)
            .ThenBy(x => x.EquipmentElement.ItemModifier?.StringId, StringComparer.Ordinal)
            .Select(x => x.EquipmentElement.Item.StringId + ":" + x.EquipmentElement.ItemModifier?.StringId + ":" + x.Amount));
    }
}
