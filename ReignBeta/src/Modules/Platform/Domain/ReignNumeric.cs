namespace ReignBeta.Platform
{
    // Shared numeric guards for game-free client policies.
    internal static class ReignNumeric
    {
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
