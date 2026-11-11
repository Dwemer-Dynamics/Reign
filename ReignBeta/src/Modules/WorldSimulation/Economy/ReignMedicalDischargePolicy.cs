#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace ReignBeta.Economy
{
    internal sealed class MedicalParty
    {
        public string Key = "", Clan = null, Kingdom = null, Status = "enrolled", Reason = "";
        public int Medicine = 0, Removed = 0;
        public Dictionary<string, int[]> Before = new Dictionary<string, int[]>(), After = new Dictionary<string, int[]>();
        public double HearthReturned = 0, HearthUnallocated = 0;
        public Dictionary<string, int[]> Joining = new Dictionary<string, int[]>();
        public Dictionary<string, int> Selected = new Dictionary<string, int>();
        public Dictionary<string, double> Credits = new Dictionary<string, double>();
    }
    internal sealed class MedicalEvent
    {
        public long Id;
        public ulong Seed;
        public string Status = "enrolled";
        public List<MedicalParty> Parties = new List<MedicalParty>();
    }
    // Version 1: user-approved uniform HARD cap, not a binomial or fixed quota.
    internal static class ReignMedicalDischargePolicy
    {
        internal const int Version = 1;
        internal static int Maximum(int wounded, int medicine)
        {
            if (wounded < 0) throw new ArgumentOutOfRangeException(nameof(wounded));
            int m = Math.Max(0, Math.Min(300, medicine));
            // Exact rational interpolation: 60 -> 45 -> 30 -> 15 -> 5 percent.
            long numerator = m <= 225 ? 300 - m : 675 - 2 * m;
            long denominator = m <= 225 ? 500 : 1500;
            return (int)((long)wounded * numerator / denominator);
        }

        internal static bool Retained(IDictionary<string, int[]> joined, IDictionary<string, int[]> now,
            IDictionary<string, int> wounds, IDictionary<string, int> dead, IDictionary<string, int> routed)
        {
            var ids = joined.Keys.Concat(now.Keys).Concat(wounds.Keys).Concat(dead.Keys).Concat(routed.Keys).Distinct();
            foreach (string id in ids)
            {
                joined.TryGetValue(id, out var before); now.TryGetValue(id, out var after);
                wounds.TryGetValue(id, out int w); dead.TryGetValue(id, out int d); routed.TryGetValue(id, out int r);
                if (w < 0 || d < 0 || r < 0 || (after?[0] ?? 0) != (before?[0] ?? 0) - d - r
                    || (after?[1] ?? 0) != (before?[1] ?? 0) + w) return false;
            }
            return true;
        }

        internal sealed class RandomStream
        {
            private ulong state;
            internal RandomStream(ulong seed) { state = seed; }
            private uint Next()
            {
                // SplitMix64, versioned with policy; independent of native battle RNG.
                unchecked
                {
                    ulong z = (state += 0x9E3779B97F4A7C15UL);
                    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                    z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                    return (uint)((z ^ (z >> 31)) >> 32);
                }
            }
            internal int Below(int bound)
            {
                if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
                uint b = (uint)bound, threshold = unchecked(0u - b) % b, value;
                do { value = Next(); } while (value < threshold);
                return (int)(value % b);
            }
        }

        internal static Dictionary<string, int> Select(IDictionary<string, int> wounds, int medicine, ulong seed)
        {
            if (wounds == null || wounds.Any(x => string.IsNullOrEmpty(x.Key) || x.Value < 0))
                throw new ArgumentException("Invalid wound cohort.");
            var rows = wounds.OrderBy(x => x.Key, StringComparer.Ordinal).ToArray();
            int total = checked(rows.Sum(x => x.Value));
            if (total > 1000000) throw new ArgumentOutOfRangeException(nameof(wounds));
            var rng = new RandomStream(seed);
            int remaining = rng.Below(Maximum(total, medicine) + 1), population = total;
            var selected = new Dictionary<string, int>(StringComparer.Ordinal);
            // Sequential sampling of soldier indices without replacement, never stack quotas.
            foreach (var row in rows)
            {
                int count = 0;
                for (int i = 0; i < row.Value; i++)
                {
                    if (remaining > 0 && rng.Below(population) < remaining) { count++; remaining--; }
                    population--;
                }
                if (count > 0) selected.Add(row.Key, count);
            }
            return selected;
        }
    }
}
