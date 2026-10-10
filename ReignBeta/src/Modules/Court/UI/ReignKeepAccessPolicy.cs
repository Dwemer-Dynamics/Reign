#nullable enable

namespace ReignBeta.UI
{
    // Native castles use their main menu as the keep landing page.
    public static class ReignKeepAccessPolicy
    {
        public const string TownMenuId = "town_keep";
        public const string CastleMenuId = "castle";

        public static string? GetNativeMenuId(bool isTown, bool isCastle)
        {
            if (isTown == isCastle) return null;
            return isTown ? TownMenuId : CastleMenuId;
        }
    }
}
