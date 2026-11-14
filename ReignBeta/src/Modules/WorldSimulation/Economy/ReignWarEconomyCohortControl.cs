#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Economy;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Economy
{
    // Ephemeral diagnostic isolation. Never serialized or enabled by ordinary campaign code.
    internal static class ReignWarEconomyCohortControl
    {
        private const string Owner = "com.bannerlordreign.reignbeta.economy.cohortfixture";
        private static readonly Dictionary<string, MethodBase> Targets = new Dictionary<string, MethodBase>();
        private static readonly HashSet<string> Peace = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Activations = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, long> Blocked = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly JArray Samples = new JArray();
        private static readonly JArray GuardProbes = new JArray();
        private static readonly Dictionary<string, long> ProbeBlocks = new Dictionary<string, long>(StringComparer.Ordinal);
        private static bool _probing;
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static string _save, _instance, _run, _terminal = "not_enabled";
        private static double _start, _end, _lastSample, _lastCheck;
        private static long _sampleCount, _checks;
        private static int _expectedProbes;
        private static string _activation, _planIdentity, _playerKingdom;
        private static WarEconomyCohortPlan _plan;
        private static WarEconomyCohortScope _scope;
        private static JArray _violations = new JArray();
        private static DateTime _expiresUtc;
        private static Harmony _harmony;

        internal static JObject Control(JObject command, string instance)
        {
            string source = command.Value<string>("source");
            if (source != "cohort_preview" && source != "cohort_enable" && source != "cohort_disable")
                throw new ArgumentException("Cohort control accepts only preview, enable or disable.");
            if (source == "cohort_preview") return Preview(ReadPlan(command));
            if (_harmony != null && (_run != command.Value<string>("campaignTestRunId") || _instance != instance
                || _save != command.Value<string>("expectedSaveName")))
                throw new InvalidOperationException("Another cohort-control owner is active.");
            // Disable is a same-run safety exit, independent of proposed plan/operation fields.
            if (source == "cohort_disable")
            { if (_harmony != null) Release("operator_disabled", true); return Status(); }
            var plan = ReadPlan(command);
            string identity = PlanJson(plan).ToString(Newtonsoft.Json.Formatting.None);
            if (string.IsNullOrWhiteSpace(command.Value<string>("fixtureRunId")) || string.IsNullOrWhiteSpace(command.Value<string>("operationId")))
                throw new ArgumentException("Cohort activation requires explicit fixture and operation IDs.");
            string activation = new JArray(instance, command.Value<string>("campaignTestRunId"),
                command.Value<string>("fixtureRunId"), command.Value<string>("operationId")).ToString(Newtonsoft.Json.Formatting.None);
            Update(instance);
            if (_harmony != null)
            {
                if (_planIdentity != identity || _activation != activation)
                    throw new InvalidOperationException("Active cohort parameters differ. Disable, export and enable a new operation while paused.");
                return Status(); // Same-owner replay cannot reset counters or extend expiry.
            }
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlMode != CampaignTimeControlMode.Stop
                || string.IsNullOrEmpty(instance) || instance != ReignLiveInteractionTestHost.GameInstanceId
                || string.IsNullOrEmpty(command.Value<string>("campaignTestRunId"))
                || command.Value<string>("expectedSaveName") != ReignServerClient.ActiveNativeSaveName())
                throw new InvalidOperationException("Cohort isolation requires the exact paused enrolled Current.");
            var realms = Kingdom.All.Where(k => !k.IsEliminated).ToArray();
            if (realms.Length > 64) throw new InvalidOperationException("Cohort isolation supports at most64 current kingdoms.");
            if (plan.SuppressRebellions && (Clan.All.Any(c => !c.IsEliminated && c.IsRebelClan)
                || ReignRebellionCampaignBehavior.Instance?.Movements?.Any(m => m.Stage == "civil_war") == true))
                throw new InvalidOperationException("Start cohort isolation before any rebellion, not after a civil split.");
            var scope = Resolve(plan);
            var preview = Preview(plan);
            if (!preview.Value<bool>("ready"))
                throw new InvalidOperationException("Arrange the requested native relations before enable: " + preview["violations"]);
            if (Activations.Contains(activation)) throw new InvalidOperationException("A terminated operation cannot reactivate guards; use a new operation ID.");
            if (Activations.Count >= WarEconomyCohortPlan.MaximumSessions) throw new InvalidOperationException("Session capacity reached; export evidence and stop testing.");
            Activations.Add(activation); _activation = activation; _plan = plan; _scope = scope; _planIdentity = identity;
            _campaign = campaign; _instance = instance; _run = command.Value<string>("campaignTestRunId");
            _playerKingdom = Clan.PlayerClan?.Kingdom?.StringId;
            _save = command.Value<string>("expectedSaveName"); _start = CampaignTime.Now.ToDays;
            _end = _start + plan.DurationDays; _lastSample = _lastCheck = double.NegativeInfinity;
            _expiresUtc = DateTime.UtcNow.AddHours(plan.WallHours); _sampleCount = _checks = 0; _violations = new JArray();
            _terminal = "active"; Peace.Clear(); Blocked.Clear(); Samples.Clear(); GuardProbes.Clear(); ProbeBlocks.Clear();
            foreach (var id in scope.Peace) Peace.Add(id);
            try { Bind(); Update(instance); ProbeGuards(); }
            catch { Release("activation_failed", true); throw; }
            return Status();
        }

        private static WarEconomyCohortPlan ReadPlan(JObject c) => new WarEconomyCohortPlan(
            c.Value<double?>("cohortDurationDays") ?? 126, c.Value<double?>("cohortWallHours") ?? 6,
            c.Value<double?>("cohortSampleIntervalDays") ?? 1, c.Value<bool?>("cohortBlockOtherWars") ?? true,
            c.Value<bool?>("cohortSuppressRebellions") ?? true, c.Value<string>("peaceKingdomIds") ?? "auto",
            c.Value<string>("warPairs") ?? "auto");
        private static JObject PlanJson(WarEconomyCohortPlan p) => new JObject {
            ["durationDays"] = p.DurationDays, ["wallHours"] = p.WallHours, ["sampleIntervalDays"] = p.SampleIntervalDays,
            ["blockOtherWars"] = p.BlockOtherWars, ["suppressRebellions"] = p.SuppressRebellions,
            ["peaceKingdomIds"] = p.PeaceKingdomIds, ["warPairs"] = p.WarPairs };
        private static Kingdom[] NpcRealms() => Kingdom.All.Where(k => !k.IsEliminated && k != Clan.PlayerClan?.Kingdom)
            .OrderBy(k => k.StringId, StringComparer.Ordinal).ToArray();
        private static string[] ActualWars(Kingdom[] realms) => realms.SelectMany((a, i) => realms.Skip(i + 1)
            .Where(b => a.IsAtWarWith(b)).Select(b => WarEconomyCohortPlan.Pair(a.StringId, b.StringId))).ToArray();
        private static WarEconomyCohortScope Resolve(WarEconomyCohortPlan plan)
        {
            var realms = NpcRealms(); var scope = plan.Resolve(realms.Select(k => k.StringId), ActualWars(realms));
            if (Clan.PlayerClan?.Kingdom != null && realms.Any(k => scope.Peace.Contains(k.StringId) && k.IsAtWarWith(Clan.PlayerClan.Kingdom)))
                throw new InvalidOperationException("A peace control is at war with the player kingdom. Resolve that relation explicitly; the NPC guard cannot control player wars.");
            return scope;
        }
        private static JObject Preview(WarEconomyCohortPlan plan)
        {
            var scope = Resolve(plan); var realms = NpcRealms();
            var errors = scope.Violations(realms.Select(k => k.StringId), ActualWars(realms));
            return new JObject { ["ok"] = true, ["schema"] = "reign-war-economy-cohort-preview-v2",
                ["ready"] = errors.Length == 0, ["plan"] = PlanJson(plan), ["violations"] = new JArray(errors),
                ["existingControlActive"] = Owns(), ["existingControlStatus"] = _terminal,
                ["peaceKingdomIds"] = new JArray(scope.Peace), ["lockedWarPairs"] = new JArray(scope.Wars),
                ["actualWarPairs"] = new JArray(ActualWars(realms)), ["nativeEffects"] = false,
                ["timeAdvanced"] = false, ["saved"] = false };
        }

        private static void Bind()
        {
            _harmony = new Harmony(Owner); Targets.Clear();
            BindOne("war_action", AccessTools.Method(typeof(DeclareWarAction), "ApplyInternal",
                new[] { typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail) }), nameof(WarPrefix));
            BindOne("direct_war", AccessTools.Method(typeof(FactionManager), "DeclareWar", new[] { typeof(IFaction), typeof(IFaction) }), nameof(WarPrefix));
            BindOne("peace_action", AccessTools.Method(typeof(MakePeaceAction), "ApplyInternal", new[] {
                typeof(IFaction), typeof(IFaction), typeof(int), typeof(int), typeof(MakePeaceAction.MakePeaceDetail) }), nameof(PeacePrefix));
            BindOne("direct_peace", AccessTools.Method(typeof(FactionManager), "SetNeutral", new[] { typeof(IFaction), typeof(IFaction) }), nameof(PeacePrefix));
            if (!_plan.SuppressRebellions) return;
            BindOne("town_rebellion", AccessTools.Method(typeof(RebellionsCampaignBehavior), "StartRebellionEvent", new[] { typeof(Settlement) }), nameof(RebellionPrefix));
            BindOne("reign_weekly_rebellion", AccessTools.Method(typeof(ReignRebellionCampaignBehavior), "TryStartWeeklyRelationshipEvaluation", new[] { typeof(float) }), nameof(RebellionPrefix));
            BindOne("reign_civil_war", AccessTools.Method(typeof(ReignRebellionCampaignBehavior), "StartCivilWar",
                new[] { typeof(ReignBeta.World.ReignRebellionMovementRecord) }), nameof(RebellionPrefix));
        }
        private static void BindOne(string key, MethodInfo target, string prefix)
        {
            if (target == null || target.ReturnType != typeof(void)) throw new InvalidOperationException("Missing cohort hook: " + key);
            var existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Prefixes.Concat(existing.Postfixes).Concat(existing.Finalizers).Concat(existing.Transpilers).Any(p => p.owner != Owner))
                throw new InvalidOperationException("Unsupported foreign callback on cohort hook: " + key);
            Targets.Add(key, target);
            _harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(ReignWarEconomyCohortControl), prefix)) { priority = Priority.First });
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == Owner) != true)
                throw new InvalidOperationException("Cohort hook did not bind: " + key);
        }
        private static bool Owns()
            => _harmony != null && _campaign != null && ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && _instance == ReignLiveInteractionTestHost.GameInstanceId
                && _save == ReignServerClient.ActiveNativeSaveName()
                && _playerKingdom == Clan.PlayerClan?.Kingdom?.StringId
                && DateTime.UtcNow < _expiresUtc && CampaignTime.Now.ToDays >= _start && CampaignTime.Now.ToDays < _end;
        private static void Count(string key)
        { var counts = _probing ? ProbeBlocks : Blocked; counts.TryGetValue(key, out long value); counts[key] = value + 1; }
        private static void CountHook(MethodBase method)
        {
            string key = Targets.FirstOrDefault(t => t.Value.Equals(method)).Key;
            if (key != null) { Count(key); return; }
            // A diagnostic identity gap must stop evidence collection, not throw out of a native action.
            _terminal = "hook_identity_lost"; _violations.Add("unmapped_hook:" + method?.Name);
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
        }

        private static JObject ProbeState()
        {
            var realms = Kingdom.All.Where(k => !k.IsEliminated).OrderBy(k => k.StringId, StringComparer.Ordinal).ToArray();
            return new JObject {
                ["wars"] = new JArray(realms.SelectMany((a, i) => realms.Skip(i + 1).Where(b => a.IsAtWarWith(b)).Select(b => a.StringId + ":" + b.StringId))),
                ["treasuries"] = new JArray(realms.Select(k => new JObject { ["id"] = k.StringId, ["cash"] = k.KingdomBudgetWallet })),
                ["heroes"] = new JArray(Hero.AllAliveHeroes.Select(h => new JObject { ["id"] = h.Id.ToString(), ["gold"] = h.Gold })),
                ["clans"] = new JArray(Clan.All.Select(c => new JObject { ["id"] = c.StringId, ["kingdom"] = c.Kingdom?.StringId, ["influence"] = c.Influence })),
                ["settlements"] = new JArray(Settlement.All.Select(s => new JObject { ["id"] = s.StringId, ["owner"] = s.OwnerClan?.StringId,
                    ["food"] = s.Town?.FoodStocks, ["hearth"] = s.Village?.Hearth })),
                ["operation"] = ReignWarEconomyTransactions.State?.NextOperation,
                ["day"] = CampaignTime.Now.ToDays };
        }
        private static void ProbeGuards()
        {
            if (!Owns()) throw new InvalidOperationException("Cohort guard proof has no exact active scope.");
            var realms = NpcRealms();
            var pairs = realms.SelectMany((a, i) => realms.Skip(i + 1).Select(b => new[] { a, b })).ToArray();
            var peaceful = pairs.FirstOrDefault(p => _scope.BlocksWar(p[0].StringId, p[1].StringId));
            var war = pairs.FirstOrDefault(p => _scope.BlocksPeace(p[0].StringId, p[1].StringId));
            var cases = new Dictionary<string, Tuple<object, object[]>>();
            if (peaceful != null)
            {
                cases.Add("war_action", Tuple.Create<object, object[]>(null, new object[] { peaceful[0], peaceful[1], DeclareWarAction.DeclareWarDetail.Default }));
                cases.Add("direct_war", Tuple.Create<object, object[]>(null, new object[] { peaceful[0], peaceful[1] }));
            }
            if (war != null)
            {
                cases.Add("peace_action", Tuple.Create<object, object[]>(null, new object[] { war[0], war[1], 0, 0, MakePeaceAction.MakePeaceDetail.Default }));
                cases.Add("direct_peace", Tuple.Create<object, object[]>(null, new object[] { war[0], war[1] }));
            }
            if (_plan.SuppressRebellions)
            {
                var town = realms.SelectMany(k => k.Fiefs).FirstOrDefault(t => t.IsTown)?.Settlement
                    ?? throw new InvalidOperationException("Rebellion guard proof requires an existing NPC town.");
                var native = _campaign.GetCampaignBehavior<RebellionsCampaignBehavior>()
                    ?? throw new InvalidOperationException("Native rebellion behavior unavailable.");
                var reign = ReignRebellionCampaignBehavior.Instance
                    ?? throw new InvalidOperationException("Reign rebellion behavior unavailable.");
                cases.Add("town_rebellion", Tuple.Create<object, object[]>(native, new object[] { town }));
                cases.Add("reign_weekly_rebellion", Tuple.Create<object, object[]>(reign, new object[] { (float)CampaignTime.Now.ToDays }));
                cases.Add("reign_civil_war", Tuple.Create<object, object[]>(reign, new object[] { null }));
            }
            _expectedProbes = Targets.Count;
            if (Hero.AllAliveHeroes.Count > 32768 || Clan.All.Count > 4096 || Settlement.All.Count > 4096)
                throw new InvalidOperationException("Cohort guard proof exceeds its bounded native census.");
            var before = ProbeState();
            _probing = true;
            try
            {
                foreach (var test in cases)
                {
                    string key = test.Key; object receiver = test.Value.Item1; object[] args = test.Value.Item2;
                    long hits = ProbeBlocks.Values.Sum();
                    ProbeBlocks.TryGetValue(key, out long beforeHits);
                    Targets[key].Invoke(receiver, args);
                    ProbeBlocks.TryGetValue(key, out long afterHits);
                    bool exactHook = ProbeBlocks.Values.Sum() == hits + 1 && afterHits == beforeHits + 1;
                    bool unchanged = JToken.DeepEquals(before, ProbeState());
                    bool passed = exactHook && unchanged;
                    GuardProbes.Add(new JObject { ["hook"] = key, ["passed"] = passed,
                        ["exactHookHitOnce"] = exactHook, ["observedStateUnchanged"] = unchanged });
                    if (!passed) throw new InvalidOperationException("Cohort guard proof failed; restore Current before advancement: " + key);
                }
            }
            finally { _probing = false; }
        }
        private static bool WarPrefix(IFaction __0, IFaction __1, MethodBase __originalMethod)
        {
            if (!Owns()) return true;
            // Preserve native hostility and suppression against bandits/minor factions.
            if (!(__0 is Kingdom) || !(__1 is Kingdom)) return true;
            if (!_scope.BlocksWar(__0.StringId, __1.StringId)) return true;
            CountHook(__originalMethod);
            return false;
        }
        private static bool PeacePrefix(IFaction __0, IFaction __1, MethodBase __originalMethod)
        {
            if (!Owns() || !(__0 is Kingdom) || !(__1 is Kingdom) || __0.IsEliminated || __1.IsEliminated
                || !_scope.BlocksPeace(__0.StringId, __1.StringId)) return true;
            CountHook(__originalMethod);
            return false;
        }
        private static bool RebellionPrefix(MethodBase __originalMethod)
        {
            if (!Owns()) return true;
            CountHook(__originalMethod);
            return false;
        }

        internal static void Update(string instance)
        {
            if (_harmony == null) return;
            try { UpdateCore(instance); }
            catch (Exception ex) when (_terminal != "cleanup_failed")
            {
                _violations.Add("observation_failed:" + ex.GetType().Name);
                Release("observation_failed", true);
            }
        }
        private static void UpdateCore(string instance)
        {
            if (_harmony == null) return;
            if (_terminal == "hook_identity_lost") { Release(_terminal, true); return; }
            if (!ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current) || _instance != instance
                || _save != ReignServerClient.ActiveNativeSaveName() || _playerKingdom != Clan.PlayerClan?.Kingdom?.StringId)
            { Release("ownership_changed", true); return; }
            double now = CampaignTime.Now.ToDays;
            if (now < _start || now >= _end || DateTime.UtcNow >= _expiresUtc)
            { Release("expired", true); return; }
            if (now - _lastCheck < .25) return;
            _lastCheck = now; _checks++;
            if (Targets.Values.Any(t => {
                var patches = Harmony.GetPatchInfo(t);
                return patches?.Prefixes.Any(p => p.owner == Owner) != true
                    || patches.Prefixes.Concat(patches.Postfixes).Concat(patches.Finalizers).Concat(patches.Transpilers).Any(p => p.owner != Owner);
            }))
            { Release("hook_coverage_lost", true); return; }
            var realms = NpcRealms();
            if (realms.Length > 64) { Release("kingdom_capacity_exceeded", true); return; }
            var wars = new JArray(realms.SelectMany((a, i) => realms.Skip(i + 1).Where(b => a.IsAtWarWith(b))
                .Select(b => new JObject { ["first"] = a.StringId, ["second"] = b.StringId })));
            _violations = new JArray(_scope.Violations(realms.Select(k => k.StringId), ActualWars(realms)));
            if (Clan.PlayerClan?.Kingdom != null)
                foreach (var realm in realms.Where(k => Peace.Contains(k.StringId) && k.IsAtWarWith(Clan.PlayerClan.Kingdom)))
                    _violations.Add("peace_player_war:" + realm.StringId);
            bool violation = _violations.Count > 0;
            if (!violation && now - _lastSample < _plan.SampleIntervalDays) return;
            _lastSample = now; _sampleCount++;
            Samples.Add(new JObject { ["day"] = now, ["wars"] = wars, ["peaceViolation"] = violation,
                ["peaceCohorts"] = new JArray(realms.Where(k => Peace.Contains(k.StringId)).Select(k => new JObject {
                    ["kingdomId"] = k.StringId, ["clans"] = new JArray(k.Clans.Select(c => c.StringId).OrderBy(s => s, StringComparer.Ordinal)),
                    ["holdings"] = new JArray(k.Fiefs.Select(f => f.Settlement.StringId).OrderBy(s => s, StringComparer.Ordinal)) })) });
            while (Samples.Count > WarEconomyCohortPlan.RetainedSamples) Samples.First.Remove();
            if (violation) Release("cohort_matrix_violation", true);
        }
        private static void Release(string reason, bool pause)
        {
            if (pause && TaleWorlds.CampaignSystem.Campaign.Current != null)
                TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            Exception cleanupError = null;
            if (_harmony != null)
                foreach (var target in Targets.Values)
                    try { _harmony.Unpatch(target, HarmonyPatchType.All, Owner); }
                    catch (Exception ex) { cleanupError = cleanupError ?? ex; }
            if (cleanupError != null)
            {
                _campaign = null; _terminal = "cleanup_failed";
                throw new InvalidOperationException("Cohort cleanup failed; keep native time stopped and end this game process.", cleanupError);
            }
            _harmony = null; _campaign = null; _terminal = reason;
        }
        internal static JObject Status(bool detailed = true) => new JObject { ["ok"] = _terminal == "not_enabled" || _terminal == "active"
                || _terminal == "operator_disabled" || _terminal == "expired", ["schema"] = "reign-war-economy-cohort-control-v2",
            ["active"] = Owns(), ["status"] = _terminal, ["runId"] = _run, ["gameInstanceId"] = _instance,
            ["saveName"] = _save, ["startDay"] = _start, ["expiresDay"] = _end, ["activationId"] = _activation,
            ["plan"] = _plan == null ? null : PlanJson(_plan), ["lockedWarPairs"] = new JArray(_scope?.Wars ?? Array.Empty<string>()),
            ["expiresUtc"] = _expiresUtc.ToString("O"), ["peaceKingdomIds"] = new JArray(Peace.OrderBy(x => x, StringComparer.Ordinal)),
            ["boundHooks"] = _harmony == null ? 0 : Targets.Count, ["blocked"] = JObject.FromObject(Blocked),
            ["selfProbePassed"] = _expectedProbes > 0 && GuardProbes.Count == _expectedProbes && GuardProbes.Cast<JObject>().All(p => p.Value<bool>("passed")),
            ["applicableProbesPassed"] = GuardProbes.Count > 0 && GuardProbes.Cast<JObject>().All(p => p.Value<bool>("passed")),
            ["expectedProbes"] = _expectedProbes,
            ["unexercisedHooks"] = new JArray(Targets.Keys.Where(k => !GuardProbes.Cast<JObject>().Any(p => p.Value<string>("hook") == k))),
            ["guardProbes"] = GuardProbes.DeepClone(), ["syntheticProbeBlocks"] = JObject.FromObject(ProbeBlocks),
            ["sampleCount"] = _sampleCount, ["retainedSampleCount"] = Samples.Count, ["samplesEvicted"] = _sampleCount - Samples.Count,
            ["fullSampleHistoryRetained"] = _sampleCount == Samples.Count, ["matrixCheckCount"] = _checks, ["violations"] = _violations.DeepClone(),
            ["samples"] = detailed ? Samples.DeepClone() : Samples.Count == 0 ? new JArray() : new JArray(Samples.Last.DeepClone()),
            ["persisted"] = false, ["warDurationLocked"] = _scope?.Wars.Count > 0 };
    }
}
