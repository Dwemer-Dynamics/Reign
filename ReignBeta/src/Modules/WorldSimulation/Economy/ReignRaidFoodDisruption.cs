using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Helpers;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;

namespace ReignBeta.Economy
{
    internal static class ReignRaidFoodDisruption
    {
        private const string Owner = "com.bannerlordreign.reignbeta.raidfood";
        private static Harmony _harmony;
        private static readonly Dictionary<string, MethodBase> Hooks = new Dictionary<string, MethodBase>();
        private static string _hookError = "";
        private static readonly HashSet<string> ReportedErrors=new HashSet<string>(StringComparer.Ordinal);
        [ThreadStatic] private static RaidUpdateObservation _currentUpdate;
        [ThreadStatic] private static int _outerFoodDepth;
        internal static bool OuterFoodOwnsRecovery => _outerFoodDepth > 0;
        internal static float ObserveQuote(Func<float> quote)
        {
            int previous=_outerFoodDepth;_outerFoodDepth=0;
            try {return quote();}finally {_outerFoodDepth=previous;}
        }
        internal static RaidFoodState State => ReignEconomyCampaignBehavior.Instance?.EconomyState.RaidFood;
        private static double Now => CampaignTime.Now.ToHours;
        private static bool Enabled => ReignWarEconomyPolicy.Options.Enabled("economyRaidFoodEnabled");
        private static double Number(string key) => ReignWarEconomyPolicy.Options.Number(key);
        private static void Unsupported(string error)
        {
            if (State != null) { State.Unsupported++; State.LastError = error; }
            if(ReportedErrors.Count<256 && ReportedErrors.Add(error)) ReignBeta.Integration.ReignLog.Warn("Raid food: " + error);
        }
        internal static void Install()
        {
            if (_harmony == null) _harmony = new Harmony(Owner);
            try
            {
                Add(typeof(RaidEventComponent), "OnInitialize", nameof(RaidInitialized), false);
                Add(typeof(RaidEventComponent), "OnInitialize", nameof(RaidInitializing), true);
                Add(typeof(RaidEventComponent), "Update", nameof(RaidUpdateBefore), true);
                Add(typeof(RaidEventComponent), "Update", nameof(RaidUpdateFinally), finalizer: true);
                Add(typeof(RaidEventComponent), "OnBeforeFinalize", nameof(RaidClosing), true);
                Add(typeof(RaidEventComponent), "OnBeforeFinalize", nameof(RaidClosed), finalizer: true);
                var navalModel = AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCVillageProductionCalculatorModel");
                if (navalModel != null)
                {
                    Add(navalModel, "CalculateDailyProductionAmount", nameof(OuterProductionBefore), true);
                    Add(navalModel, "CalculateDailyProductionAmount", nameof(OuterProductionAfter), false);
                    Add(navalModel, "CalculateDailyProductionAmount", nameof(OuterProductionFinally), finalizer: true);
                }
                var fishing = ReignEconomyPatches.NativeFishingType;
                if (fishing != null)
                {
                    Add(fishing, "CanCreateFishingParties", nameof(FishingCreation), false);
                    Add(fishing, "OnDropOff", nameof(FishingUnloaded), false);
                    Add(fishing, "StartRoaming", nameof(FishingDeparture), true);
                    Add(fishing, "StartRoaming", nameof(FishingDeparted), false);
                    Add(fishing, "OnHourlyTickParty", nameof(FishingHourly), true);
                    const string hook = nameof(CatchTranspiler);
                    if (!Hooks.ContainsKey(hook))
                    {
                        var method = AccessTools.Method(fishing, "CatchFish") ?? throw new MissingMethodException("CatchFish");
                        _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(ReignRaidFoodDisruption), hook));
                        Hooks.Add(hook, method);
                    }
                }
            }
            catch (Exception ex) { _hookError = ex.ToString(); Unsupported(_hookError); }
        }
        private static void Add(Type type, string method, string hook, bool prefix = false, bool finalizer = false)
        {
            if (Hooks.ContainsKey(hook)) return;
            var target = AccessTools.Method(type, method) ?? throw new MissingMethodException(type.FullName, method);
            var patch = new HarmonyMethod(typeof(ReignRaidFoodDisruption), hook);
            _harmony.Patch(target, prefix: prefix ? patch : null, postfix: !prefix && !finalizer ? patch : null, finalizer: finalizer ? patch : null);
            Hooks.Add(hook, target);
        }
        internal static void Initialize()
        {
            var state = State;
            if (state == null) return;
            if (state.Version != ReignRaidFoodPolicy.Version) throw new InvalidOperationException("Unsupported raid food payload; retained, never overwritten.");
            ReignRaidFoodPolicy.NormalizeCollections(state);
            foreach(var village in Village.All) NormalizeRecord(village);
            if (!state.Migrated)
            {
                foreach (var village in Village.All)
                {
                    var record = Get(village);
                    if (!Valid(record)) { Unsupported("Malformed migration record retained: " + village.StringId); continue; }
                    double legacy = ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village) ?? 1;
                    if(!ReignRaidFoodPolicy.Finite(legacy)||legacy<0||legacy>1)
                    {Unsupported("Invalid legacy recovery retained during migration: "+village.StringId);continue;}
                    if (village.VillageState == Village.VillageStates.Looted || legacy < 1)
                    { record.Capacity = legacy; record.Severity = "full"; }
                }
                state.Migrated = true;
            }
            foreach (var village in Village.All)
            {
                var record = Get(village);
                if (!Valid(record)) { Unsupported("Invalid retained food state for " + village.StringId); continue; }
                var raid = village.Settlement.Party.MapEvent?.Component as RaidEventComponent;
                if (raid != null) EnsureRaid(raid, false);
                else if (record.Active != null) Close(village, "closed_during_load");
            }
            // Native party ID is serialized and changes when an old StringId is reused.
            var live = new HashSet<string>(MobileParty.All.Select(BoatKey), StringComparer.Ordinal);
            foreach (var key in state.Boats.Keys.Where(x => !live.Contains(x)).ToArray()) state.Boats.Remove(key);
        }
        private static bool Valid(RaidFoodVillage record) => record != null && ReignRaidFoodPolicy.Finite(record.Capacity)
            && record.Capacity >= 0 && record.Capacity <= 1 && ReignRaidFoodPolicy.Finite(record.LastHours)
            && (record.Severity == "none" || record.Severity == "partial" || record.Severity == "full");
        private static void NormalizeRecord(Village village)
        {
            var r=Get(village);
            Action<string> invalid=message=>{r.InvalidEvidence=message;Unsupported(village.StringId+": "+message);};
            if(!ReignRaidFoodPolicy.Finite(r.LastHours)||r.LastHours<0)
            {invalid("Invalid/missing timestamp="+r.LastHours.ToString(CultureInfo.InvariantCulture)+"; baseline only, no catch-up.");r.LastHours=Now;r.Normal=village.VillageState==Village.VillageStates.Normal;}
            if(r.Severity!="none"&&r.Severity!="partial"&&r.Severity!="full")
            {invalid("Invalid severity="+r.Severity+"; retained capacity uses full recovery duration.");r.Severity=r.Capacity>=1?"none":"full";}
            if(!ReignRaidFoodPolicy.Finite(r.CatchCarry)||r.CatchCarry<0||r.CatchCarry>=1)
            {invalid("Invalid fractional fish="+r.CatchCarry.ToString(CultureInfo.InvariantCulture)+"; no fish granted.");r.CatchCarry=0;}
            if(!ReignRaidFoodPolicy.Finite(r.Capacity)||r.Capacity<0||r.Capacity>1)
                invalid("Invalid retained capacity="+r.Capacity.ToString(CultureInfo.InvariantCulture)+"; legacy fallback, no healthy reset.");
        }
        private static RaidFoodVillage Get(Village village)
        {
            if (!State.Villages.TryGetValue(village.StringId, out var record))
                State.Villages.Add(village.StringId, record = new RaidFoodVillage {
                    LastHours = Now, Normal = village.VillageState == Village.VillageStates.Normal,
                    Stressed = ReignEconomyCampaignBehavior.IsAgricultureStressed(village) });
            return record;
        }
        internal static void PruneBoats()
        {
            if (State == null) return;
            var live = new HashSet<string>(MobileParty.All.Select(BoatKey),StringComparer.Ordinal);
            foreach (string key in State.Boats.Keys.Where(x=>!live.Contains(x)).ToArray()) State.Boats.Remove(key);
        }
        internal static double Capacity(Village village)
        {
            if (village == null || State == null) return 1;
            if (!State.Villages.TryGetValue(village.StringId, out var record)) return 1;
            // Invalid evidence remains retained. Never manufacture a healthy reset or a permanent zero.
            if(record != null && ReignRaidFoodPolicy.Finite(record.Capacity) && record.Capacity >= 0 && record.Capacity <= 1)return record.Capacity;
            double legacy=ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village)??1;
            return ReignRaidFoodPolicy.Finite(legacy)?Math.Max(0,Math.Min(1,legacy)):1;
        }
        internal static float Effective(Village village)
        {
            double capacity=Capacity(village),legacy=ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village)??1;
            return (float)ReignRaidFoodPolicy.Effective(ReignRaidFoodPolicy.Finite(legacy)?legacy:capacity,capacity,Enabled);
        }
        internal static void Settle(Village village)
        {
            if (State == null || village == null) return;
            var r = Get(village);
            if (!Valid(r)) { Unsupported("Invalid food recovery record " + village.StringId); return; }
            if (r.LastHours < 0)
            {
                r.LastHours = Now; r.Normal = village.VillageState == Village.VillageStates.Normal;
                Unsupported("Missing recovery timestamp was baselined without catch-up: " + village.StringId); return;
            }
            double elapsed = Now - r.LastHours;
            if (elapsed < -ReignRaidFoodPolicy.Epsilon) { State.RecoveryTimeRegressions++; Unsupported("Food recovery campaign time regressed."); }
            if(elapsed>2*CampaignTime.HoursInDay)
            {Unsupported("Unobserved recovery interval bounded to one functioning day: "+village.StringId);elapsed=CampaignTime.HoursInDay;}
            // Read actual stress once. The getter is pure; no recursive food simulation or fixed point.
            bool stressed = ReignEconomyCampaignBehavior.IsAgricultureStressed(village);
            double before = r.Capacity;
            r.Capacity = ReignRaidFoodPolicy.Recover(r.Capacity, Math.Max(0, elapsed) / CampaignTime.HoursInDay,
                r.Normal, stressed, r.Severity, Number("economyRaidPartialDays"), Number("economyRaidFullDays"), Number("economyRaidStressSpeed"));
            State.CapacityRestored += r.Capacity - before;
            if (r.Capacity == 1) r.Severity = "none";
            r.LastHours = Now; r.Stressed = stressed;
            r.Normal = village.VillageState == Village.VillageStates.Normal;
        }
        internal static void BeforeSettingsChange()
        {
            if (State?.Migrated != true || TaleWorlds.CampaignSystem.Campaign.Current == null) return;
            foreach (var village in Village.All) Settle(village);
        }
        internal static void VillageChanged(Village village, Village.VillageStates oldState, Village.VillageStates newState)
        {
            if (State == null || village == null) return;
            Settle(village);
            FlushDuringTransition(village);
            var r = Get(village);
            r.Normal = newState == Village.VillageStates.Normal;
            if (newState == Village.VillageStates.Looted)
            {
                MarkDamage(village);
                r.Capacity = 0; r.Severity = "full";
                Close(village, "looted");
            }
            else if (oldState == Village.VillageStates.BeingRaided && newState != oldState) Close(village, "partial");
        }
        private static string NativeKey(RaidEventComponent raid) => raid.MapEvent.MapEventSettlement.StringId + ":"
            + raid.MapEvent.StringId + ":" + raid.MapEvent.BattleStartTime.ToHours.ToString("R", CultureInfo.InvariantCulture);
        private static RaidFoodAttempt EnsureRaid(RaidEventComponent raid, bool initialized)
        {
            var village = raid?.MapEvent.MapEventSettlement?.Village;
            if (State == null || village == null) return null;
            var r = Get(village);
            if (!Valid(r)) { Unsupported("Cannot bind malformed village food state."); return null; }
            const string raidIdentityPrefix="reign_raid_food_";
            if(string.IsNullOrEmpty(raid.MapEvent.StringId))
                raid.MapEvent.StringId=raidIdentityPrefix+(++State.Sequence).ToString(CultureInfo.InvariantCulture);
            if(!raid.MapEvent.StringId.StartsWith(raidIdentityPrefix,StringComparison.Ordinal)
                || !long.TryParse(raid.MapEvent.StringId.Substring(raidIdentityPrefix.Length),NumberStyles.None,CultureInfo.InvariantCulture,out long nativeGeneration)
                || nativeGeneration<=0)
            { Unsupported("Foreign native raid StringId retained; unique persistent identity unsupported.");return null; }
            State.Sequence=Math.Max(State.Sequence,nativeGeneration);
            string native = NativeKey(raid);
            if (r.Active != null)
            {
                if (!ReignRaidFoodPolicy.Finite(r.Active.MaximumHitPoints) || r.Active.MaximumHitPoints != 1
                    || !ReignRaidFoodPolicy.Finite(r.Active.ShutdownFraction) || r.Active.ShutdownFraction <= 0 || r.Active.ShutdownFraction > 1
                    || !ReignRaidFoodPolicy.Finite(r.Active.CountedDamage) || r.Active.CountedDamage < 0 || r.Active.CountedDamage > 1
                    || !ReignRaidFoodPolicy.Finite(r.Active.NativeDamage) || r.Active.NativeDamage < 0 || r.Active.NativeDamage > 1)
                { Unsupported("Malformed active raid receipt retained without replay.");return null; }
                if (r.Active.NativeKey != native) { Unsupported("Overlapping incompatible village raid identity."); return null; }
                return r.Active;
            }
            if (r.LastClosedNativeKey == native || nativeGeneration<=r.LastClosedNativeGeneration)
            {State.DuplicateRaidCallbacks++;return null;}
            Settle(village);
            var attempt = new RaidFoodAttempt { Generation = ++State.Sequence, NativeGeneration=nativeGeneration, MaximumHitPoints=1, NativeKey = native, HistoricalCoverageComplete = initialized,
                ShutdownFraction = Number("economyRaidShutdownPercent") / 100, StartHours = raid.MapEvent.BattleStartTime.ToHours };
            r.Active = attempt;
            if (!initialized && raid.RaidDamage > ReignRaidFoodPolicy.Epsilon)
            {
                // RaidDamage is saved native APPLIED damage (not its uncommitted buffer).
                attempt.HistoricalCoverageComplete = false;
                attempt.NativeDamage = raid.RaidDamage;
                // Native saved RaidDamage is verified applied progress, not its uncommitted buffer.
                // Reconstruct capacity ONCE; do not replay native hearth loss, loot, money or grants.
                double known=ReignRaidFoodPolicy.Unit(raid.RaidDamage);
                MarkDamage(village,attempt.StartHours);
                r.Capacity=ReignRaidFoodPolicy.Damage(r.Capacity,known,attempt.ShutdownFraction);
                if(r.Severity!="full")r.Severity="partial";
                attempt.CountedDamage=known;State.MigratedNativeDamage+=known;
                Unsupported("Loaded native applied raid progress reconstructed once; historical timing remains incomplete: " + village.StringId);
            }
            return attempt;
        }
        private static void RaidInitialized(RaidEventComponent __instance)
        {try {EnsureRaid(__instance,true);}catch(Exception ex){Unsupported(ex.Message);}}
        private static void RaidInitializing(RaidEventComponent __instance)
        {
            try {var village=__instance.MapEvent.MapEventSettlement?.Village;if(village!=null)Settle(village);}
            catch(Exception ex){Unsupported(ex.Message);}
        }
        internal sealed class RaidUpdateObservation
        {
            internal double Hp, Progress;
            internal RaidFoodAttempt Attempt;
            internal RaidEventComponent Raid;
            internal RaidUpdateObservation Previous;
        }
        private static void RaidUpdateBefore(RaidEventComponent __instance, out RaidUpdateObservation __state)
        {
            __state=null;
            try
            {
                __state = new RaidUpdateObservation { Hp = __instance.MapEvent.MapEventSettlement.SettlementHitPoints,
                    Progress = __instance.RaidDamage, Attempt = EnsureRaid(__instance, false),Raid=__instance,Previous=_currentUpdate };
                _currentUpdate=__state;
            }
            catch(Exception ex){Unsupported(ex.Message);}
        }
        private static Exception RaidUpdateFinally(RaidEventComponent __instance, RaidUpdateObservation __state, Exception __exception)
        {
            try
            {
                if (__state?.Attempt == null) return __exception;
                var village = __instance.MapEvent.MapEventSettlement.Village;
                double lost = Math.Max(0, __state.Hp - village.Settlement.SettlementHitPoints);
                // Native caps RaidDamage at 1; its terminal hit may exceed remaining physical HP.
                double applied = ReignRaidFoodPolicy.ObserveApplied(__state.Attempt, __state.Hp,
                    village.Settlement.SettlementHitPoints, __instance.RaidDamage);
                if (lost > ReignRaidFoodPolicy.Epsilon && applied <= ReignRaidFoodPolicy.Epsilon
                    && __instance.RaidDamage <= __state.Progress + ReignRaidFoodPolicy.Epsilon)
                    Unsupported("Raid HP loss without native applied-progress corroboration.");
                ApplyObservedDamage(village, __state.Attempt, applied);
            }
            catch (Exception ex) { Unsupported(ex.Message); }
            finally {if(__state!=null)_currentUpdate=__state.Previous;}
            return __exception;
        }
        private static void FlushDuringTransition(Village village)
        {
            var observation=_currentUpdate;
            if(observation?.Attempt==null || observation.Raid.MapEvent.MapEventSettlement?.Village!=village)return;
            double applied=ReignRaidFoodPolicy.ObserveApplied(observation.Attempt,observation.Hp,
                village.Settlement.SettlementHitPoints,observation.Raid.RaidDamage);
            ApplyObservedDamage(village,observation.Attempt,applied);
        }
        internal static void ApplyObservedDamage(Village village, RaidFoodAttempt attempt, double applied)
        {
            if (attempt == null || applied <= ReignRaidFoodPolicy.Epsilon) return;
            var r = Get(village);
            if (!ReferenceEquals(r.Active, attempt)) { Unsupported("Stale raid damage receipt."); return; }
            applied = Math.Min(applied / attempt.MaximumHitPoints, Math.Max(0, 1 - attempt.CountedDamage));
            if (applied <= 0) return;
            Settle(village);
            MarkDamage(village);
            r.Capacity = ReignRaidFoodPolicy.Damage(r.Capacity, applied, attempt.ShutdownFraction);
            if (r.Severity != "full") r.Severity = "partial";
            attempt.CountedDamage += applied;
            State.DamageApplied += applied;
        }
        private static void RaidClosing(RaidEventComponent __instance)
        {
            try {var village = __instance.MapEvent.MapEventSettlement?.Village;if (village != null) {FlushDuringTransition(village);Settle(village);}}
            catch(Exception ex){Unsupported(ex.Message);}
        }
        private static Exception RaidClosed(RaidEventComponent __instance, Exception __exception)
        {
            try
            {
                var village = __instance.MapEvent.MapEventSettlement?.Village;
                if (village != null) Close(village, village.VillageState == Village.VillageStates.Looted ? "looted" : "partial");
            }
            catch(Exception ex){Unsupported(ex.Message);}
            return __exception;
        }
        private static void Close(Village village, string outcome)
        {
            if(State==null || village==null)return;
            FlushDuringTransition(village);
            var r = Get(village); var attempt = r.Active;
            if (attempt == null) return;
            State.Recent.Add(new RaidFoodReceipt { Village = village.StringId, Generation = attempt.Generation,
                Damage = attempt.CountedDamage, Outcome = outcome, EndHours = Now, Capacity = r.Capacity,
                HistoricalCoverageComplete = attempt.HistoricalCoverageComplete });
            r.LastClosedNativeKey = attempt.NativeKey; r.Active = null;
            r.LastClosedStartHours = Math.Max(r.LastClosedStartHours, attempt.StartHours);
            r.LastClosedNativeGeneration = Math.Max(r.LastClosedNativeGeneration,attempt.NativeGeneration);
            ReignRaidFoodPolicy.Prune(State);
        }
        private static void MarkDamage(Village village,double? definitelyDepartedBeforeHours=null)
        {
            var r = Get(village);
            double legacy=ReignEconomyCampaignBehavior.Instance?.GetStoredRecoveryModifier(village)??1;
            double preDamage=!Enabled?1:ReignRaidFoodPolicy.Effective(ReignRaidFoodPolicy.Finite(legacy)?legacy:Capacity(village),Capacity(village),true);
            foreach (var party in MobileParty.All.Where(p => FishingHome(p) == village && Flag(p.PartyComponent, "IsRoaming")))
            {
                var boat = Boat(party);
                if (boat == null) { Unsupported("Existing fishing voyage has no stable party generation.");continue; }
                if(definitelyDepartedBeforeHours.HasValue && boat.VoyageStartHours>=definitelyDepartedBeforeHours.Value)continue;
                ReignRaidFoodPolicy.ProtectVoyage(boat,preDamage);
            }
            r.LastDamageSequence = ++State.Sequence; r.LastDamageHours = Now;
        }
        private static void OuterProductionBefore(ItemObject item, out bool __state)
        { __state = item?.IsFood == true; if (__state) _outerFoodDepth++; }
        private static void OuterProductionAfter(Village village, ItemObject item, ref ExplainedNumber __result)
        {
            if (item?.IsFood != true || _outerFoodDepth!=1) return;
            ApplyProduction(village, ref __result);
        }
        private static Exception OuterProductionFinally(bool __state, Exception __exception)
        { if (__state) _outerFoodDepth--; return __exception; }
        internal static void ApplyProduction(Village village, ref ExplainedNumber result)
        {
            double recovery = ReignVillageProductionCalculatorModel.IsPotential ? 1
                : village?.VillageState == Village.VillageStates.Normal ? Effective(village) : 0;
            result.LimitMin(0);
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                -(float)(Math.Max(0, result.ResultNumber) * (1 - recovery)), new TextObject("{=!}Food production recovery"));
            float sabotage = ReignNpcFoodSabotage.Factor(village);
            if (sabotage != 0f)
                ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                    Math.Max(0f, result.ResultNumber) * sabotage, new TextObject("{=!}Covert supply disruption"));
            result.LimitMin(0);
        }

        private static Village FishingHome(MobileParty party) => party?.PartyComponent?.GetType().Name == "FishingPartyComponent"
            ? (party.PartyComponent as VillagerPartyComponent)?.Village : null;
        private static string BoatKey(MobileParty party) => party?.StringId + ":" + party?.Id.ToString();
        private static bool Flag(object component, string name) => (bool?)AccessTools.Property(component.GetType(), name)?.GetValue(component, null) == true;
        private static double Voyage(object component)
            => AccessTools.Property(component.GetType(), "RoamingStartTime")?.GetValue(component, null) is CampaignTime time ? time.ToHours : -1;
        private static bool AtHome(MobileParty party, Village home) => home != null
            && party.Position.DistanceSquared(home.Settlement.PortPosition) < .01f;
        private static RaidFoodBoat Boat(MobileParty party)
        {
            var home = FishingHome(party);
            if (State == null || home == null || party.Id.ToString() == "0") return null;
            double voyage=Voyage(party.PartyComponent);
            if(!ReignRaidFoodPolicy.Finite(voyage)||voyage<0){Unsupported("Native fishing trip identity unavailable.");return null;}
            string key = BoatKey(party);
            if (!State.Boats.TryGetValue(key, out var boat))
                State.Boats.Add(key, boat = new RaidFoodBoat { Home = home.StringId, Owner = home.Settlement.OwnerClan?.StringId ?? "",
                    VoyageStartHours = voyage, DepartureSequence = State.Sequence });
            return boat;
        }
        private static void FishingCreation(Village village, ref bool __result)
        { if (__result && Enabled && Capacity(village) <= 0) __result = false; }
        private static void FishingUnloaded(object fishingParty)
        {
            var party = (fishingParty as PartyComponent)?.MobileParty; var boat = Boat(party);
            if (boat == null) { Unsupported("Fishing unload missing stable party/home identity."); return; }
            boat.Unloaded = true; boat.ExemptionConsumed = true;boat.Grandfathered=false;boat.ResumePending=false; State.Unloads++;
        }
        private static bool FishingDeparture(object fishingParty)
        {
            var party = (fishingParty as PartyComponent)?.MobileParty; var home = FishingHome(party); var boat = Boat(party);
            if (boat == null || !boat.Unloaded || !AtHome(party, home) || party.MapEvent != null || party.IsFleeing()) return true;
            if (!Enabled || Capacity(home) > 0) return true;
            boat.Home=home.StringId;boat.Owner=home.Settlement.OwnerClan?.StringId??"";
            boat.Hold = true;boat.ResumePending=false; State.HeldDepartures++;
            return false;
        }
        private static void FishingDeparted(object fishingParty)
        {
            var party = (fishingParty as PartyComponent)?.MobileParty; var home = FishingHome(party); var boat = Boat(party);
            if(boat!=null && (party.IsFleeing() || party.ShortTermBehavior!=party.DefaultBehavior))
            {if(boat.Grandfathered)boat.VoyageStartHours=Voyage(fishingParty);return;}
            if (boat == null || boat.Hold || !AtHome(party, home)) return;
            boat.Home=home.StringId;boat.Owner=home.Settlement.OwnerClan?.StringId??"";
            boat.Unloaded = false; boat.ExemptionConsumed = false;boat.Grandfathered=false;boat.ResumePending=false; boat.VoyageStartHours = Voyage(fishingParty);
            boat.DepartureSequence = ++State.Sequence;
        }
        private static bool FishingHourly(object __instance, MobileParty party)
        {
            var home = FishingHome(party);
            if (home == null || State == null || !State.Boats.TryGetValue(BoatKey(party), out var boat) || (!boat.Hold&&!boat.ResumePending)) return true;
            if (party.MapEvent != null || party.IsFleeing() || party.ShortTermBehavior != party.DefaultBehavior)
            { boat.Hold = false;boat.ResumePending=true; return true; }
            // Native post-unload hold is native Hold (0). Any newly assigned order overrides our hold.
            if ((int)party.DefaultBehavior != 0) { boat.Hold = false;boat.ResumePending=false; return true; }
            if (!AtHome(party, home)) { boat.Hold=false;boat.ResumePending=false;Unsupported("Stale fishing hold away from home released to native movement.");return true; }
            if(boat.ResumePending)return ResumeFishing(__instance,party,boat);
            if (home.StringId != boat.Home || (home.Settlement.OwnerClan?.StringId ?? "") != boat.Owner)
            {
                boat.Home=home.StringId;boat.Owner=home.Settlement.OwnerClan?.StringId??"";
                return ResumeFishing(__instance,party,boat);
            }
            if (Enabled && Capacity(home) <= 0) return false;
            return ResumeFishing(__instance,party,boat);
        }
        private static bool ResumeFishing(object native,MobileParty party,RaidFoodBoat boat)
        {
            boat.Hold = false; boat.Unloaded = false;boat.ResumePending=false;
            try
            {
                AccessTools.Method(native.GetType(), "StartRoaming").Invoke(native, new[] { party.PartyComponent });
                State.ResumedDepartures++;
                return false;
            }
            catch (Exception ex) { Unsupported("Fishing hold resume: " + ex.Message); return true; }
        }
        private static IEnumerable<CodeInstruction> CatchTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var add = AccessTools.Method(typeof(ItemRoster), nameof(ItemRoster.AddToCounts), new[] { typeof(ItemObject), typeof(int) });
            var sites = code.Where(x => x.Calls(add)).ToArray();
            if (sites.Length != 1) throw new InvalidOperationException("Fishing catch shape changed; expected one exact new-fish write.");
            var site = sites[0];
            var component = new CodeInstruction(OpCodes.Ldarg_1);
            component.labels.AddRange(site.labels); component.blocks.AddRange(site.blocks); site.labels.Clear(); site.blocks.Clear();
            code.Insert(code.IndexOf(site), component);
            site.opcode = OpCodes.Call; site.operand = AccessTools.Method(typeof(ReignRaidFoodDisruption), nameof(AddCatch));
            return code;
        }
        private static int AddCatch(ItemRoster roster, ItemObject fish, int planned, object fishingParty)
        {
            if(planned==0)return -1;
            var party = (fishingParty as PartyComponent)?.MobileParty; var home = FishingHome(party);
            if (home == null || State == null || !ReferenceEquals(roster, party.ItemRoster) || fish?.IsFood != true || planned <= 0)
            { Unsupported("Unsupported native fishing catch identity."); return roster.AddToCounts(fish, planned); }
            var boat = Boat(party); var village = Get(home);
            if(boat==null)Unsupported("Fishing catch without a verified party/voyage generation; no exemption granted.");
            if (!Valid(village) || !ReignRaidFoodPolicy.Finite(village.CatchCarry) || village.CatchCarry < 0 || village.CatchCarry >= 1)
            { Unsupported("Malformed fishing carry/state retained; native catch fallback."); return roster.AddToCounts(fish,planned); }
            bool existingVoyage = boat != null && boat.Grandfathered && !boat.ExemptionConsumed && !boat.Unloaded && !boat.Hold
                && ReignRaidFoodPolicy.Finite(boat.GrandfatherFactor) && boat.GrandfatherFactor>=0 && boat.GrandfatherFactor<=1
                && boat.VoyageStartHours == Voyage(fishingParty);
            double factor = !Enabled ? 1 : existingVoyage ? boat.GrandfatherFactor : Effective(home);
            int actual = ReignRaidFoodPolicy.Catch(planned, factor, ref village.CatchCarry);
            State.CatchPlanned += planned; State.CatchProduced += actual;
            return actual > 0 ? roster.AddToCounts(fish, actual) : -1;
        }
        internal static JObject Describe(Village village)
        {
            var state = State;
            RaidFoodVillage record = state != null && state.Villages.TryGetValue(village.StringId, out var r) ? r : null;
            return new JObject { ["version"] = ReignRaidFoodPolicy.Version, ["capacity"] = Capacity(village),
                ["legacyRecovery"] = ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village) ?? 1,
                ["effectiveRecovery"] = Effective(village), ["severity"] = record?.Severity ?? "none",
                ["lastDamageHours"] = record?.LastDamageHours ?? -1, ["activeGeneration"] = record?.Active?.Generation,
                ["normalDaysToFull"] = (1 - Capacity(village)) * Number(record?.Severity == "full" ? "economyRaidFullDays" : "economyRaidPartialDays"),
                ["fishingCarry"] = record?.CatchCarry ?? 0 };
        }
        internal static JObject Diagnostics()
        {
            int expected = 6 + (ReignEconomyPatches.NativeFishingType == null ? 0 : 6)
                + (AccessTools.TypeByName("NavalDLC.GameComponents.NavalDLCVillageProductionCalculatorModel") == null ? 0 : 3);
            bool owned = Hooks.All(pair => Harmony.GetPatchInfo(pair.Value)?.Owners.Contains(Owner) == true);
            return new JObject { ["schema"] = "reign-raid-food-v1", ["enabled"] = Enabled,
                ["hooksInstalled"] = Hooks.Count, ["expectedHooks"] = expected, ["hookOwnershipPresent"] = owned,
                ["hooksHealthy"] = _hookError.Length == 0 && Hooks.Count == expected && owned,
                ["nativeAcceptance"] = "pending", ["error"] = _hookError.Length > 0 ? _hookError : State?.LastError,
                ["fixtureTainted"] = State?.FixtureTainted ?? false,
                ["unsupported"] = State?.Unsupported ?? 0, ["historyCount"] = State?.Recent.Count ?? 0,
                ["catchPlanned"] = State?.CatchPlanned ?? 0, ["catchProduced"] = State?.CatchProduced ?? 0,
                ["migratedNativeDamage"] = State?.MigratedNativeDamage ?? 0,
                ["heldDepartures"] = State?.HeldDepartures ?? 0, ["resumedDepartures"] = State?.ResumedDepartures ?? 0,
                ["currentHolds"] = State?.Boats.Values.Count(x => x.Hold) ?? 0 };
        }
    }
}
