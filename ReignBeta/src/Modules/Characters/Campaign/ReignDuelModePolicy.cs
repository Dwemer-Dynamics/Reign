using System;

namespace ReignBeta.Campaign
{
    internal static class ReignDuelModePolicy
    {
        internal static string Resolve(string requestedMode, bool legacyLethal)
        {
            string mode = (requestedMode ?? string.Empty).Trim().ToLowerInvariant()
                .Replace('-', '_').Replace(' ', '_');
            // An explicitly agreed nonlethal mode cannot be escalated by a legacy flag.
            if (mode == "honor" || mode == "honour") return "honor";
            if (mode == "practice") return "practice";
            if (mode == "training") return "training";
            if (mode == "lethal" || mode == "death" || mode == "to_the_death") return "lethal";
            return legacyLethal ? "lethal" : "training";
        }

        internal static bool IsLethal(string mode) => string.Equals(mode, "lethal", StringComparison.Ordinal);
        internal static bool IsPractice(string mode) => string.Equals(mode, "practice", StringComparison.Ordinal);

        internal static string DisplayName(string mode)
        {
            if (IsLethal(mode)) return "duel to the death";
            if (IsPractice(mode)) return "practice duel";
            return string.Equals(mode, "honor", StringComparison.Ordinal) ? "duel of honor" : "training duel";
        }
    }
}
