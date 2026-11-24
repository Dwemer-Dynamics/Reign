using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Buildings;
using NativeCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace ReignBeta.Government.Campaign
{
    internal static class ReignGovernmentConstructionNativeObservation
    {
        private const string HarmonyId = "com.bannerlordreign.reignbeta.government.construction-observation";
        private static readonly Guid VerifiedStockModule = new Guid("aa6e6095-4606-4c71-a5a8-2f29566591af");
        private static Harmony _harmony;
        private static Hook[] _hooks;
        private static MethodInfo[] _nativeCallers;
        private static bool _ready;

        private sealed class Hook
        {
            internal readonly MethodInfo Target;
            internal readonly MethodInfo Prefix;
            internal readonly MethodInfo Postfix;

            internal Hook(MethodInfo target, string prefix, string postfix)
            {
                Target = target;
                Prefix = AccessTools.DeclaredMethod(typeof(ReignGovernmentConstructionNativeObservation), prefix);
                Postfix = AccessTools.DeclaredMethod(typeof(ReignGovernmentConstructionNativeObservation), postfix);
            }

            internal bool IsInstalledExactlyOnce()
            {
                Patches info = Harmony.GetPatchInfo(Target);
                return info != null
                    && ExactPatch(info.Prefixes, Prefix)
                    && ExactPatch(info.Postfixes, Postfix)
                    && info.Transpilers.Count == 0
                    && info.Finalizers.Count == 0;
            }

            private static bool ExactPatch(IReadOnlyCollection<Patch> patches, MethodInfo method) =>
                patches.Count == 1 && patches.First().owner == HarmonyId && patches.First().PatchMethod == method;
        }

        internal sealed class LoadOwner
        {
            internal readonly NativeCampaign Campaign;
            internal readonly ReignGovernmentCampaignBehavior Behavior;
            internal readonly string CampaignId;
            internal readonly string TimelineId;
            private readonly ReignServerClient.RequestFence _fence;

            internal LoadOwner(NativeCampaign campaign, ReignGovernmentCampaignBehavior behavior,
                string campaignId, string timelineId, ReignServerClient.RequestFence fence)
            {
                Campaign = campaign;
                Behavior = behavior;
                CampaignId = campaignId;
                TimelineId = timelineId;
                _fence = fence;
            }

            internal bool IsCurrent() => ReignMainThread.IsMainThread
                && !ReignCampaignInitializationGate.IsPending && !ReignSaveSyncCoordinator.IsAlignmentPending
                && Campaign != null && ReferenceEquals(Campaign, NativeCampaign.Current)
                && Behavior != null && ReferenceEquals(Behavior, ReignServices.Government)
                && ReignServices.WorldHistory?.TimelineReady == true
                && string.Equals(TimelineId, ReignServices.WorldHistory.TimelineId, StringComparison.Ordinal)
                && _fence.IsCurrent();

            internal bool Matches(ReignGovernmentConstructionTerms terms) => terms?.IsValid == true
                && CampaignId == terms.CampaignId && TimelineId == terms.TimelineId;
        }

        private sealed class Snapshot
        {
            internal readonly LoadOwner Owner;
            internal readonly ReignGovernmentConstructionTerms Terms;
            internal readonly Town Town;
            internal readonly Kingdom Realm;
            internal readonly Building Project;
            internal readonly Building PreviousHead;
            internal readonly int LevelBefore;
            internal readonly double Day;

            internal Snapshot(LoadOwner owner, ReignGovernmentConstructionTerms terms, Town town, Kingdom realm,
                Building project, Building previousHead, double day)
            {
                Owner = owner;
                Terms = terms;
                Town = town;
                Realm = realm;
                Project = project;
                PreviousHead = previousHead;
                LevelBefore = project.CurrentLevel;
                Day = day;
            }
        }

        internal static void Apply()
        {
            if (VerifyReady()) return;
            try
            {
                Unapply();
                var stock = typeof(Town).Assembly;
                if (stock.ManifestModule.ModuleVersionId != VerifiedStockModule)
                    throw new InvalidOperationException("Installed campaign module has no reviewed construction queue contract.");

                Type helper = stock.GetType("Helpers.BuildingHelper", throwOnError: true);
                Type behavior = stock.GetType(
                    "TaleWorlds.CampaignSystem.CampaignBehaviors.BuildingsCampaignBehavior", throwOnError: true);
                const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
                const BindingFlags privateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
                _hooks = new[]
                {
                    new Hook(NativeMethod(helper, "ChangeCurrentBuildingQueue", publicStatic, 0x06000113,
                        typeof(List<Building>), typeof(Town)), nameof(CommitPrefix), nameof(CommitPostfix)),
                    new Hook(NativeMethod(behavior, "DecideBuildingQueue", BindingFlags.NonPublic | BindingFlags.Static,
                        0x06003d64, typeof(Town)), nameof(DecidePrefix), nameof(DecidePostfix)),
                    new Hook(NativeMethod(behavior, "TickCurrentBuildingForTown", privateInstance, 0x06003d66,
                        typeof(Town)), nameof(TickPrefix), nameof(TickPostfix)),
                    new Hook(NativeMethod(typeof(Building), "LevelUp", BindingFlags.Public | BindingFlags.Instance,
                        0x06003c04), nameof(LevelPrefix), nameof(LevelPostfix))
                };
                _nativeCallers = new[]
                {
                    NativeMethod(behavior, "DailyTickSettlement", privateInstance, 0x06003d65, typeof(Settlement)),
                    NativeMethod(helper, "CheckIfBuildingIsComplete", publicStatic, 0x06000111, typeof(Building))
                };
                if (_hooks.Any(h => h.Prefix == null || h.Postfix == null || !HasNoPatches(h.Target))
                    || !_nativeCallers.All(HasNoPatches))
                    throw new InvalidOperationException("A construction hook is unavailable or overlaps another Harmony owner.");

                _harmony = new Harmony(HarmonyId);
                foreach (Hook hook in _hooks)
                    _harmony.Patch(hook.Target, prefix: new HarmonyMethod(hook.Prefix), postfix: new HarmonyMethod(hook.Postfix));
                if (!InventoryIntact())
                    throw new InvalidOperationException("Construction hook inventory is incomplete or overlaps another Harmony owner.");
                _ready = true;
            }
            catch (Exception ex)
            {
                Unapply();
                Disable(ex);
            }
        }

        internal static void Unapply()
        {
            _ready = false;
            try { _harmony?.UnpatchAll(HarmonyId); }
            catch (Exception ex) { Disable(ex); }
            finally
            {
                _harmony = null;
                _hooks = null;
                _nativeCallers = null;
            }
        }

        internal static bool VerifyReady()
        {
            if (!_ready) return false;
            try
            {
                if (InventoryIntact()) return true;
                Disable(new InvalidOperationException("Construction hook ownership changed after startup."));
            }
            catch (Exception ex) { Disable(ex); }
            return false;
        }

        private static bool InventoryIntact() => _hooks?.Length == 4 && _nativeCallers?.Length == 2
            && _hooks.All(h => h.IsInstalledExactlyOnce()) && _nativeCallers.All(HasNoPatches);

        private static bool HasNoPatches(MethodBase target)
        {
            Patches info = Harmony.GetPatchInfo(target);
            return info == null || info.Prefixes.Count == 0 && info.Postfixes.Count == 0
                && info.Transpilers.Count == 0 && info.Finalizers.Count == 0;
        }

        private static MethodInfo NativeMethod(Type type, string name, BindingFlags binding, int token, params Type[] arguments)
        {
            MethodInfo method = type.GetMethod(name, binding | BindingFlags.DeclaredOnly, null, arguments, null);
            if (method == null || method.ReturnType != typeof(void) || method.MetadataToken != token)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }

        private static void Disable(Exception error)
        {
            _ready = false;
            try { ReignLog.Warn("Government named construction observation unavailable: " + error); }
            catch { /* Observation and diagnostics never interrupt Bannerlord. */ }
        }

        internal static bool TryCaptureOwner(out LoadOwner owner)
        {
            owner = null;
            if (!ReignMainThread.IsMainThread || ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCoordinator.IsAlignmentPending)
                return false;
            var campaign = NativeCampaign.Current;
            var behavior = ReignServices.Government;
            var history = ReignServices.WorldHistory;
            string campaignId = campaign?.UniqueGameId?.Trim();
            if (behavior == null || history?.TimelineReady != true || string.IsNullOrWhiteSpace(history.TimelineId)
                || string.IsNullOrWhiteSpace(campaignId))
                return false;
            owner = new LoadOwner(campaign, behavior, campaignId, history.TimelineId, ReignServerClient.RequestFence.Capture());
            return owner.IsCurrent();
        }

        internal static Building AvailableProject(Town town, Kingdom realm, string expectedSettlementId,
            string expectedProjectId, bool observerReady)
        {
            if (!observerReady || !OwnsTown(town, realm, expectedSettlementId)) return null;
            var current = NativeHead(town);
            if (!CanStart(current) || !ReferenceEquals(current, ResolveExactProject(town, current.BuildingType.StringId))) return null;
            var requested = ResolveExactProject(town, expectedProjectId);
            return CanStart(requested) && current.BuildingType.StringId != expectedProjectId ? requested : null;
        }

        private static Snapshot Capture(Town town)
        {
            if (!_ready || !ReignMainThread.IsMainThread || !(town?.Settlement?.MapFaction is Kingdom realm)
                || !TryCaptureOwner(out LoadOwner owner))
                return null;
            var terms = owner.Behavior.GetActiveConstructionObservationTerms(town, realm, owner);
            if (!VerifyReady() || !owner.Matches(terms)) return null;
            var project = BoundProject(terms, town, realm);
            double day = CampaignTime.Now.ToDays;
            return project != null && project.CurrentLevel == terms.LevelAtAcceptance && terms.IsWithinWindow(day)
                ? new Snapshot(owner, terms, town, realm, project, NativeHead(town), day)
                : null;
        }

        private static void Finish(Snapshot before, string transition, bool ownsTransition)
        {
            if (before == null || !ownsTransition || !before.Owner.IsCurrent()) return;
            if (CampaignTime.Now.ToDays != before.Day || !VerifyReady()) return;
            var current = before.Owner.Behavior.GetActiveConstructionObservationTerms(before.Town, before.Realm, before.Owner);
            if (!before.Terms.Matches(current) || !ReferenceEquals(BoundProject(current, before.Town, before.Realm), before.Project)
                || !ReferenceEquals(NativeHead(before.Town), before.Project))
                return;
            var observation = ReignGovernmentConstructionObservation.Create(current, transition, before.Day, before.Project.CurrentLevel);
            if (observation != null) before.Owner.Behavior.PublishConstructionObservation(observation);
        }

        private static void CommitPrefix(List<Building> __0, Town __1, out Snapshot __state)
        {
            __state = null;
            try
            {
                var before = Capture(__1);
                // The stock helper enqueues the provided finite projects verbatim.
                // Tie the receipt to its actual first queued selection.
                if (before != null && ReferenceEquals(__0?.FirstOrDefault(b => b?.BuildingType?.IsDailyProject == false), before.Project))
                    __state = before;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void CommitPostfix(Snapshot __state) => FinishHead(__state);

        private static void DecidePrefix(Town __0, out Snapshot __state)
        {
            __state = null;
            try
            {
                // The stock scorer only enqueues when the queue is empty.
                if (_ready && ReignMainThread.IsMainThread && __0?.BuildingsInProgress?.Count == 0)
                    __state = Capture(__0);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void DecidePostfix(Snapshot __state)
        {
            try
            {
                if (ReignMainThread.IsMainThread && __state?.Town?.BuildingsInProgress?.Count == 1)
                    FinishHead(__state);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void TickPrefix(Town __0, out Snapshot __state)
        {
            __state = null;
            try
            {
                if (!_ready || !ReignMainThread.IsMainThread || !(__0?.BuildingsInProgress?.Count > 1)) return;
                var before = Capture(__0);
                // A stock tick can expose the existing next project by removing
                // a max-level head. Demand that exact successor.
                if (before != null && ReferenceEquals(__0.BuildingsInProgress.Skip(1).First(), before.Project))
                    __state = before;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void TickPostfix(Snapshot __state)
        {
            try
            {
                if (ReignMainThread.IsMainThread && __state?.PreviousHead?.CurrentLevel == BuildingType.MaxLevel)
                    FinishHead(__state);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void FinishHead(Snapshot before)
        {
            try
            {
                if (before == null || !ReignMainThread.IsMainThread) return;
                Finish(before, ReignGovernmentConstructionObservation.PriorityChanged,
                    !ReferenceEquals(before.PreviousHead, before.Project) && CanStart(before.Project));
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void LevelPrefix(Building __instance, out Snapshot __state)
        {
            __state = null;
            try
            {
                if (!_ready || !ReignMainThread.IsMainThread || __instance == null) return;
                var before = Capture(__instance.Town);
                if (before != null && ReferenceEquals(before.Project, __instance)
                    && ReferenceEquals(before.PreviousHead, __instance))
                    __state = before;
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static void LevelPostfix(Snapshot __state)
        {
            try
            {
                // Stock LevelUp increments exactly one level and dispatches its
                // event before returning to the helper, which removes the
                // completed queue entry.
                if (__state == null || !ReignMainThread.IsMainThread) return;
                Finish(__state, ReignGovernmentConstructionObservation.LevelCompleted,
                    __state.Project.CurrentLevel == __state.LevelBefore + 1);
            }
            catch (Exception ex) { Disable(ex); }
        }

        private static Building BoundProject(ReignGovernmentConstructionTerms terms, Town town, Kingdom realm) =>
            realm?.StringId == terms.KingdomId && OwnsTown(town, realm, terms.SettlementId)
                && BuildingType.MaxLevel == terms.LevelCapAtAcceptance ? ResolveExactProject(town, terms.ProjectId) : null;

        private static bool OwnsTown(Town town, Kingdom realm, string expectedSettlementId) =>
            town?.Settlement != null && realm != null && !realm.IsEliminated
            && !string.IsNullOrWhiteSpace(expectedSettlementId) && town.Settlement.StringId == expectedSettlementId
            && ReferenceEquals(town.Settlement.Town, town) && ReferenceEquals(town.Settlement.MapFaction, realm);

        private static Building ResolveExactProject(Town town, string id)
        {
            if (town?.Buildings == null || string.IsNullOrWhiteSpace(id)) return null;
            var matches = town.Buildings.Where(building => building?.BuildingType?.StringId == id).Take(2).ToArray();
            return matches.Length == 1 && ReferenceEquals(matches[0].Town, town)
                && !matches[0].BuildingType.IsDailyProject ? matches[0] : null;
        }

        private static bool CanStart(Building building) => building?.BuildingType != null
            && !building.BuildingType.IsDailyProject && !string.IsNullOrWhiteSpace(building.BuildingType.StringId)
            && building.CurrentLevel >= 0 && building.CurrentLevel < BuildingType.MaxLevel;

        private static Building NativeHead(Town town) =>
            town?.BuildingsInProgress?.Count > 0 ? town.BuildingsInProgress.Peek() : null;
    }

    public sealed partial class ReignGovernmentCampaignBehavior
    {
        internal ReignGovernmentConstructionTerms GetActiveConstructionObservationTerms(Town town, Kingdom realm,
            ReignGovernmentConstructionNativeObservation.LoadOwner owner)
        {
            ReignGovernmentConstructionTerms terms = null;
            FindActiveConstructionTerms(town, realm, owner.CampaignId, owner.TimelineId, ref terms);
            return terms;
        }

        internal void PublishConstructionObservation(ReignGovernmentConstructionObservation observation) =>
            StoreConstructionObservation(observation);

        partial void FindActiveConstructionTerms(Town town, Kingdom realm, string campaignId, string timelineId,
            ref ReignGovernmentConstructionTerms terms);
        partial void StoreConstructionObservation(ReignGovernmentConstructionObservation observation);
    }
}
