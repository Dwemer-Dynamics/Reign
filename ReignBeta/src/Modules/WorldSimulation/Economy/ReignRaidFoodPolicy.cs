#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Economy
{
    // Campaign-independent policy. Rates describe productive capacity, never population or stocks.
    internal static class ReignRaidFoodPolicy
    {
        internal const int Version = 1, HistoryLimit = 512;
        internal const double Epsilon = 1e-6;
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static double Unit(double value)
        {
            if (!Finite(value)) throw new ArgumentOutOfRangeException(nameof(value));
            return Math.Max(0, Math.Min(1, value));
        }
        internal static double Damage(double capacity, double appliedFraction, double shutdownFraction)
        {
            if (!Finite(appliedFraction) || appliedFraction < 0 || !Finite(shutdownFraction) || shutdownFraction <= 0 || shutdownFraction > 1)
                throw new ArgumentOutOfRangeException(nameof(appliedFraction));
            return Snap(Unit(capacity) - appliedFraction / shutdownFraction);
        }
        internal static double Snap(double capacity)
        {
            capacity = Unit(capacity);
            return capacity <= Epsilon ? 0 : capacity >= 1 - Epsilon ? 1 : capacity;
        }
        internal static double Recover(double capacity, double elapsedDays, bool normal, bool stressed,
            string severity, double partialDays, double fullDays, double stressSpeed)
        {
            if (!Finite(elapsedDays) || !Finite(partialDays) || partialDays <= 0 || !Finite(fullDays) || fullDays < partialDays
                || !Finite(stressSpeed) || stressSpeed <= 0 || stressSpeed > 1)
                throw new ArgumentOutOfRangeException(nameof(elapsedDays));
            double rate = 1 / (severity == "full" ? fullDays : partialDays);
            return Snap(Unit(capacity) + (normal ? Math.Max(0, elapsedDays) * rate * (stressed ? stressSpeed : 1) : 0));
        }
        internal static double Effective(double legacy, double capacity, bool enabled) => Math.Min(Unit(legacy), enabled ? Unit(capacity) : 1);
        internal static double ObserveApplied(RaidFoodAttempt attempt, double beforeHp, double afterHp, double nativeProgress)
        {
            if (attempt == null) throw new ArgumentNullException(nameof(attempt));
            if (!Finite(beforeHp) || !Finite(afterHp) || !Finite(nativeProgress) || nativeProgress < 0 || nativeProgress > 1 + Epsilon)
                throw new ArgumentOutOfRangeException(nameof(nativeProgress));
            nativeProgress = Math.Min(1,nativeProgress);
            double unseen = Math.Max(0, nativeProgress - attempt.NativeDamage);
            attempt.NativeDamage = Math.Max(attempt.NativeDamage, nativeProgress);
            return Math.Min(Math.Max(0, beforeHp - afterHp), unseen);
        }
        internal static int Catch(int planned, double factor, ref double carry)
        {
            if (planned < 0 || !Finite(carry) || carry < 0 || carry >= 1) throw new ArgumentOutOfRangeException(nameof(planned));
            factor = Unit(factor);
            if(factor==1)return planned;
            if(factor==0 || planned==0)return 0;
            double exact = planned * factor + carry;
            int caught = Math.Min(planned, (int)Math.Floor(exact));
            carry = exact - caught;
            // Multiplication plus an old remainder can never exceed the native planned capacity.
            if (carry >= 1 || carry < 0) throw new InvalidOperationException("Invalid fishing production remainder.");
            return caught;
        }
        internal static void ProtectVoyage(RaidFoodBoat boat,double preDamageFactor)
        {
            if(boat.Grandfathered||boat.ExemptionConsumed||boat.Unloaded||boat.Hold)return;
            boat.GrandfatherFactor=Unit(preDamageFactor);boat.Grandfathered=true;
        }
        internal static void Prune(RaidFoodState state)
        {
            // Unresolved village/raid/hold state is NOT operational history.
            if (state.Recent.Count > HistoryLimit)
                state.Recent = state.Recent.OrderByDescending(x => x.EndHours).ThenBy(x => x.Village, StringComparer.Ordinal)
                    .ThenByDescending(x => x.Generation).Take(HistoryLimit).ToList();
        }
        internal static void NormalizeCollections(RaidFoodState state)
        {
            state.MalformedEntries=state.MalformedEntries??new List<string>();
            if (state.Villages == null || state.Boats == null || state.Recent == null)
            {
                state.Unsupported++; state.LastError = "Null raid-food collection retained as malformed-save evidence; unknown history cannot certify coverage.";
                state.MalformedCollections = true;
            }
            state.Villages = state.Villages == null ? new Dictionary<string,RaidFoodVillage>(StringComparer.Ordinal)
                : new Dictionary<string,RaidFoodVillage>(state.Villages,StringComparer.Ordinal);
            state.Boats = state.Boats == null ? new Dictionary<string,RaidFoodBoat>(StringComparer.Ordinal)
                : new Dictionary<string,RaidFoodBoat>(state.Boats,StringComparer.Ordinal);
            state.Recent = state.Recent ?? new List<RaidFoodReceipt>();
            foreach (string key in state.Villages.Where(x=>x.Value==null).Select(x=>x.Key).ToArray())
            { state.MalformedEntries.Add("null village:"+key);state.Villages.Remove(key);state.Unsupported++; }
            foreach (string key in state.Boats.Where(x=>x.Value==null).Select(x=>x.Key).ToArray())
            { state.MalformedEntries.Add("null boat:"+key);state.Boats.Remove(key);state.Unsupported++; }
            if (state.Recent.Any(x=>x==null)) {state.MalformedEntries.Add("null completed history entry");state.Unsupported++;state.Recent=state.Recent.Where(x=>x!=null).ToList();}
            state.Sequence=Math.Max(Math.Max(0,state.Sequence),state.Villages.Values.Select(x=>Math.Max(x.LastDamageSequence,
                Math.Max(x.LastClosedNativeGeneration,x.Active?.NativeGeneration??0))).DefaultIfEmpty(0).Max());
        }
    }
    internal sealed class RaidFoodState
    {
        public int Version = ReignRaidFoodPolicy.Version;
        public bool Migrated = false;
        public bool MalformedCollections = false;
        public bool FixtureTainted = false;
        public List<string> MalformedEntries = new List<string>();
        public long Sequence = 0, Unsupported = 0, RecoveryTimeRegressions = 0, DuplicateRaidCallbacks = 0;
        public string LastError = "";
        public Dictionary<string, RaidFoodVillage> Villages = new Dictionary<string, RaidFoodVillage>(StringComparer.Ordinal);
        public Dictionary<string, RaidFoodBoat> Boats = new Dictionary<string, RaidFoodBoat>(StringComparer.Ordinal);
        public List<RaidFoodReceipt> Recent = new List<RaidFoodReceipt>();
        public long CatchPlanned = 0, CatchProduced = 0, HeldDepartures = 0, ResumedDepartures = 0, Unloads = 0;
        public double DamageApplied = 0, CapacityRestored = 0, MigratedNativeDamage = 0;
    }
    internal sealed class RaidFoodVillage
    {
        public double Capacity = 1,  LastHours = -1,  LastDamageHours = -1, CatchCarry = 0,  LastClosedStartHours = -1;
        public string Severity = "none", LastClosedNativeKey = "", InvalidEvidence = "";
        public bool Normal = false, Stressed = false;
        public long LastDamageSequence = 0, LastClosedNativeGeneration = 0;
        public RaidFoodAttempt Active = null;
    }
    internal sealed class RaidFoodAttempt
    {
        public long Generation = 0, NativeGeneration = 0;
        public string NativeKey = "";
        // Bannerlord raid HP/progress use a normalized maximum of 1. Frozen at generation start.
        public double MaximumHitPoints = 1,  ShutdownFraction = .25, CountedDamage = 0, NativeDamage = 0;
        public double StartHours = 0;
        public bool HistoricalCoverageComplete = false;
    }
    internal sealed class RaidFoodReceipt
    {
        public long Generation = 0;
        public string Village = "", Outcome = "";
        public double Damage = 0, EndHours = 0, Capacity = 0;
        public bool HistoricalCoverageComplete = false;
    }
    internal sealed class RaidFoodBoat
    {
        public string Home = "", Owner = "";
        public bool Hold = false, Unloaded = false, ExemptionConsumed = false, Grandfathered = false, ResumePending = false;
        public double GrandfatherFactor = 1;
        public double VoyageStartHours = 0;
        public long DepartureSequence = 0;
    }
}
