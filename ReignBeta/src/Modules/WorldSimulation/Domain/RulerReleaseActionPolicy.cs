namespace ReignBeta.World
{
    // Identify the captive an operation can actually release or transfer. A
    // diplomatic counterpart is not implicitly a prisoner term.
    internal static class RulerReleaseActionPolicy
    {
        internal static string PrisonerId(ReignWorldActionType type, string namedPrisonerId,
            string tradePrisonerId, string targetHeroId)
        {
            switch (type)
            {
                case ReignWorldActionType.RegularTransferPrisoner:
                    return string.IsNullOrWhiteSpace(namedPrisonerId) ? targetHeroId ?? "" : namedPrisonerId;
                case ReignWorldActionType.RegularReleaseArrestedCharacter:
                case ReignWorldActionType.RegularRescindArrestAccusation:
                    return targetHeroId ?? "";
                case ReignWorldActionType.DiplomacyRansomPackage:
                case ReignWorldActionType.DiplomacyPackage:
                    return namedPrisonerId ?? "";
                case ReignWorldActionType.RegularTradePackage:
                    return string.IsNullOrWhiteSpace(namedPrisonerId) ? tradePrisonerId ?? "" : namedPrisonerId;
                default:
                    return "";
            }
        }
    }
}
