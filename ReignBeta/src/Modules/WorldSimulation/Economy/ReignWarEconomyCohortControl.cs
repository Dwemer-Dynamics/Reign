#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
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
        private static readonly Dictionary<string, long> Blocked = new Dictionary<string, long>(StringComparer.Ordinal);
        private static readonly JArray Samples = new JArray();
        private static readonly JArray GuardProbes = new JArray();
        private static readonly Dictionary<string, long> ProbeBlocks = new Dictionary<string, long>(StringComparer.Ordinal);
        private static bool _probing;
        private static TaleWorlds.CampaignSystem.Campaign _campaign;
        private static string _save, _instance, _run, _terminal = "not_enabled";
        private static double _start, _lastSample;
        private static DateTime _expiresUtc;
        private static Harmony _harmony;

        internal static JObject Control(JObject command, string instance)
        {
            string source = command.Value<string>("source");
            if (source != "cohort_enable" && source != "cohort_disable")
                throw new ArgumentException("Cohort control accepts only cohort_enable or cohort_disable.");
            Update(instance);
            if (_harmony != null)
            {
                if (_run != command.Value<string>("campaignTestRunId") || _instance != instance
                    || _save != command.Value<string>("expectedSaveName"))
                    throw new InvalidOperationException("Another cohort-control owner is active.");
                if (source == "cohort_disable") Release("operator_disabled", true);
                return Status(); // Same-owner replay cannot reset counters or extend expiry.
            }
            if (source == "cohort_disable") return Status();
            if (_run == command.Value<string>("campaignTestRunId"))
                throw new InvalidOperationException("A terminated cohort session cannot be renewed under the same run.");
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null || campaign.TimeControlMode != CampaignTimeControlMode.Stop
                || string.IsNullOrEmpty(instance) || instance != ReignLiveInteractionTestHost.GameInstanceId
                || string.IsNullOrEmpty(command.Value<string>("campaignTestRunId"))
                || command.Value<string>("expectedSaveName") != ReignServerClient.ActiveNativeSaveName())
                throw new InvalidOperationException("Cohort isolation requires the exact paused enrolled Current.");
            var realms = Kingdom.All.Where(k => !k.IsEliminated).ToArray();
            if (realms.Length > 64) throw new InvalidOperationException("Cohort isolation supports at most64 current kingdoms.");
            if (Clan.All.Any(c => !c.IsEliminated && c.IsRebelClan)
                || ReignRebellionCampaignBehavior.Instance?.Movements?.Any(m => m.Stage == "civil_war") == true)
                throw new InvalidOperationException("Start cohort isolation before any rebellion, not after a civil split.");
            var peaceful = realms.Where(k => k != Clan.PlayerClan?.Kingdom
                && !realms.Any(other => other != k && k.IsAtWarWith(other))).ToArray();
            if (peaceful.Length == 0 || !realms.Any(k => k != Clan.PlayerClan?.Kingdom
                && realms.Any(other => other != k && other != Clan.PlayerClan?.Kingdom && k.IsAtWarWith(other))))
                throw new InvalidOperationException("Arrange actual NPC peace and war controls before enabling isolation.");
            _campaign = campaign; _instance = instance; _run = command.Value<string>("campaignTestRunId");
            _save = command.Value<string>("expectedSaveName"); _start = CampaignTime.Now.ToDays;
            _lastSample = double.NegativeInfinity; _expiresUtc = DateTime.UtcNow.AddHours(6);
            _terminal = "active"; Peace.Clear(); Blocked.Clear(); Samples.Clear(); GuardProbes.Clear(); ProbeBlocks.Clear();
            foreach (var realm in peaceful) Peace.Add(realm.StringId);
            try { Bind(); Update(instance); ProbeGuards(peaceful[0], realms.First(k => k != peaceful[0] && k != Clan.PlayerClan?.Kingdom)); }
            catch { Release("activation_failed", true); throw; }
            return Status();
        }

        private static void Bind()
        {
            _harmony = new Harmony(Owner); Targets.Clear();
            BindOne("war_action", AccessTools.Method(typeof(DeclareWarAction), "ApplyInternal",
                new[] { typeof(IFaction), typeof(IFaction), typeof(DeclareWarAction.DeclareWarDetail) }), nameof(WarPrefix));
            BindOne("direct_war", AccessTools.Method(typeof(FactionManager), "DeclareWar", new[] { typeof(IFaction), typeof(IFaction) }), nameof(WarPrefix));
            BindOne("town_rebellion", AccessTools.Method(typeof(RebellionsCampaignBehavior), "StartRebellionEvent", new[] { typeof(Settlement) }), nameof(RebellionPrefix));
            BindOne("reign_weekly_rebellion", AccessTools.Method(typeof(ReignRebellionCampaignBehavior), "TryStartWeeklyRelationshipEvaluation", new[] { typeof(float) }), nameof(RebellionPrefix));
            BindOne("reign_civil_war", AccessTools.Method(typeof(ReignRebellionCampaignBehavior), "StartCivilWar",
                new[] { typeof(ReignBeta.World.ReignRebellionMovementRecord) }), nameof(RebellionPrefix));
        }
        private static void BindOne(string key, MethodInfo target, string prefix)
        {
            if (target == null || target.ReturnType != typeof(void)) throw new InvalidOperationException("Missing cohort hook: " + key);
            var existing = Harmony.GetPatchInfo(target);
            if (existing != null && existing.Prefixes.Concat(existing.Postfixes).Concat(existing.Finalizers).Any(p => p.owner != Owner))
                throw new InvalidOperationException("Unsupported foreign callback on cohort hook: " + key);
            Targets.Add(key, target);
            _harmony.Patch(target, prefix: new HarmonyMethod(AccessTools.Method(typeof(ReignWarEconomyCohortControl), prefix)) { priority = Priority.First });
            if (Harmony.GetPatchInfo(target)?.Prefixes.Any(p => p.owner == Owner) != true)
                throw new InvalidOperationException("Cohort hook did not bind: " + key);
        }
        private static bool Owns()
            => _harmony != null && ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && _instance == ReignLiveInteractionTestHost.GameInstanceId
                && _save == ReignServerClient.ActiveNativeSaveName()
                && DateTime.UtcNow < _expiresUtc && CampaignTime.Now.ToDays >= _start && CampaignTime.Now.ToDays < _start + 61;
        private static void Count(string key)
        { var counts = _probing ? ProbeBlocks : Blocked; counts.TryGetValue(key, out long value); counts[key] = value + 1; }

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
        private static void ProbeGuards(Kingdom peaceful, Kingdom other)
        {
            if (!Owns() || !Peace.Contains(peaceful.StringId) || peaceful.IsAtWarWith(other))
                throw new InvalidOperationException("Cohort guard proof has no exact active peaceful scope.");
            var town = peaceful.Fiefs.FirstOrDefault(t => t.IsTown)?.Settlement
                ?? throw new InvalidOperationException("Cohort guard proof requires an existing peaceful NPC town.");
            var native = _campaign.GetCampaignBehavior<RebellionsCampaignBehavior>()
                ?? throw new InvalidOperationException("The native town-rebellion behavior is not loaded; coverage is unavailable.");
            var reign = ReignRebellionCampaignBehavior.Instance
                ?? throw new InvalidOperationException("The Reign rebellion behavior is not loaded; coverage is unavailable.");
            if (Hero.AllAliveHeroes.Count > 32768 || Clan.All.Count > 4096 || Settlement.All.Count > 4096)
                throw new InvalidOperationException("Cohort guard proof exceeds its bounded native census.");
            var before = ProbeState();
            _probing = true;
            try
            {
                foreach (var key in new[] { "war_action", "direct_war", "town_rebellion", "reign_weekly_rebellion", "reign_civil_war" })
                {
                    object receiver = key == "town_rebellion" ? native : key.StartsWith("reign_", StringComparison.Ordinal) ? reign : null;
                    object[] args = key == "war_action" ? new object[] { peaceful, other, DeclareWarAction.DeclareWarDetail.Default }
                        : key == "direct_war" ? new object[] { peaceful, other }
                        : key == "town_rebellion" ? new object[] { town }
                        : key == "reign_weekly_rebellion" ? new object[] { (float)CampaignTime.Now.ToDays } : new object[] { null };
                    long hits = ProbeBlocks.Values.Sum();
                    string counter = Targets[key].DeclaringType.Name + (key == "war_action" || key == "direct_war"
                        ? ":" + peaceful.StringId + ":" + other.StringId : ":" + Targets[key].Name);
                    ProbeBlocks.TryGetValue(counter, out long beforeHits);
                    Targets[key].Invoke(receiver, args);
                    ProbeBlocks.TryGetValue(counter, out long afterHits);
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
            if (!Peace.Contains(__0?.StringId ?? "") && !Peace.Contains(__1?.StringId ?? "")) return true;
            Count(__originalMethod.DeclaringType.Name + ":" + (__0?.StringId ?? "") + ":" + (__1?.StringId ?? ""));
            return false;
        }
        private static bool RebellionPrefix(MethodBase __originalMethod)
        {
            if (!Owns()) return true;
            Count(__originalMethod.DeclaringType.Name + ":" + __originalMethod.Name);
            return false;
        }

        internal static void Update(string instance)
        {
            if (_harmony == null) return;
            if (!ReferenceEquals(_campaign, TaleWorlds.CampaignSystem.Campaign.Current) || _instance != instance
                || _save != ReignServerClient.ActiveNativeSaveName())
            { Release("ownership_changed", true); return; }
            double now = CampaignTime.Now.ToDays;
            if (now < _start || now >= _start + 61 || DateTime.UtcNow >= _expiresUtc)
            { Release("expired", true); return; }
            if (now - _lastSample < .25) return;
            _lastSample = now;
            if (Targets.Values.Any(t => Harmony.GetPatchInfo(t)?.Prefixes.Any(p => p.owner == Owner) != true))
            { Release("hook_coverage_lost", true); return; }
            var realms = Kingdom.All.Where(k => !k.IsEliminated).OrderBy(k => k.StringId, StringComparer.Ordinal).ToArray();
            var wars = new JArray(realms.SelectMany((a, i) => realms.Skip(i + 1).Where(b => a.IsAtWarWith(b))
                .Select(b => new JObject { ["first"] = a.StringId, ["second"] = b.StringId })));
            bool violation = wars.Cast<JObject>().Any(w => Peace.Contains(w.Value<string>("first")) || Peace.Contains(w.Value<string>("second")))
                || Peace.Any(id => !realms.Any(k => k.StringId == id));
            Samples.Add(new JObject { ["day"] = now, ["wars"] = wars, ["peaceViolation"] = violation,
                ["peaceCohorts"] = new JArray(realms.Where(k => Peace.Contains(k.StringId)).Select(k => new JObject {
                    ["kingdomId"] = k.StringId, ["clans"] = new JArray(k.Clans.Select(c => c.StringId).OrderBy(s => s, StringComparer.Ordinal)),
                    ["holdings"] = new JArray(k.Fiefs.Select(f => f.Settlement.StringId).OrderBy(s => s, StringComparer.Ordinal)) })) });
            if (Samples.Count > 256) { Release("sample_capacity_exceeded", true); return; }
            if (violation) Release("peace_matrix_violation", true);
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
        internal static JObject Status(bool detailed = true) => new JObject { ["ok"] = _terminal != "cleanup_failed", ["schema"] = "reign-war-economy-cohort-control-v1",
            ["active"] = Owns(), ["status"] = _terminal, ["runId"] = _run, ["gameInstanceId"] = _instance,
            ["saveName"] = _save, ["startDay"] = _start, ["expiresDay"] = _start + 61,
            ["expiresUtc"] = _expiresUtc.ToString("O"), ["peaceKingdomIds"] = new JArray(Peace.OrderBy(x => x, StringComparer.Ordinal)),
            ["boundHooks"] = _harmony == null ? 0 : Targets.Count, ["blocked"] = JObject.FromObject(Blocked),
            ["selfProbePassed"] = GuardProbes.Count == 5 && GuardProbes.Cast<JObject>().All(p => p.Value<bool>("passed")),
            ["guardProbes"] = GuardProbes.DeepClone(), ["syntheticProbeBlocks"] = JObject.FromObject(ProbeBlocks),
            ["sampleCount"] = Samples.Count, ["samples"] = detailed ? Samples.DeepClone() : Samples.Count == 0 ? new JArray() : new JArray(Samples.Last.DeepClone()),
            ["persisted"] = false, ["warDurationLocked"] = false };
    }
}
