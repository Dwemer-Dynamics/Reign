using System;

namespace ReignBeta.PartyAgency
{
    internal static class ReignGuestWagePolicy
    {
        // Only completed service periods accrue. A stopped agreement has a fixed
        // accrual boundary; retries after departure can settle debt without adding it.
        internal static bool TryDue(int rate, double period, double nextDue, double now,
            double endDay, out int periods, out long gold)
        {
            periods = 0;
            gold = 0;
            if (rate <= 0 || period < 1 || nextDue <= 0 || now <= 0
                || !Finite(period) || !Finite(nextDue) || !Finite(now) || !Finite(endDay)) return false;
            double through = endDay > 0 ? Math.Min(now, endDay) : now;
            if (through < nextDue) return true;
            double count = Math.Floor((through - nextDue) / period) + 1;
            if (count > int.MaxValue) return false;
            periods = (int)count;
            gold = (long)rate * periods;
            return true;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
