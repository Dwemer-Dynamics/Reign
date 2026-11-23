using System;

namespace ReignBeta.Platform.Domain
{
    // Shared numeric guards for game-free client policies.
    internal static class ReignNumeric
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        internal static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
