using System;

namespace ReignBeta.Campaign
{
    internal static class ReignDuelModePolicy
    {
        internal const float DamageMultiplier = 0.05f;
        internal const float BlockedXpMultiplier = 0.05f;
        internal const float HitXpMultiplier = 0.15f;

        internal static float ScaleExperience(float normalXp, bool blocked)
            => IsPositiveFinite(normalXp) ? normalXp * (blocked ? BlockedXpMultiplier : HitXpMultiplier) : 0f;

        internal static float HealthAfterHit(float health, float nativeNextHealth, float normalDamage)
        {
            // Preserve native invulnerability/no-damage decisions. Scale before the
            // remaining-health cap, and keep fractional damage for light strikes.
            if (!IsPositiveFinite(health) || !IsPositiveFinite(normalDamage)
                || float.IsNaN(nativeNextHealth) || nativeNextHealth >= health) return nativeNextHealth;
            return Math.Max(0f, health - normalDamage * DamageMultiplier);
        }

        private static bool IsPositiveFinite(float value) => value > 0f && !float.IsInfinity(value);

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

        internal static bool IsExperiencePair(string player, string opponent, string attacker, string target)
        {
            if (string.IsNullOrWhiteSpace(player) || string.IsNullOrWhiteSpace(opponent)
                || string.Equals(player, opponent, StringComparison.Ordinal)) return false;
            return (string.Equals(attacker, player, StringComparison.Ordinal) && string.Equals(target, opponent, StringComparison.Ordinal))
                || (string.Equals(attacker, opponent, StringComparison.Ordinal) && string.Equals(target, player, StringComparison.Ordinal));
        }

        internal static string DisplayName(string mode)
        {
            if (IsLethal(mode)) return "duel to the death";
            if (IsPractice(mode)) return "practice duel";
            return string.Equals(mode, "honor", StringComparison.Ordinal) ? "duel of honor" : "training duel";
        }
    }
}
