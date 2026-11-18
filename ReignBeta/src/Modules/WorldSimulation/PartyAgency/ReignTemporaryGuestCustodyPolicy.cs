namespace ReignBeta.WorldSimulation.PartyAgency
{
    // Native facts are supplied by the adapter; no model-authored term grants custody authority.
    public static class ReignTemporaryGuestCustodyPolicy
    {
        public static bool CanRelease(bool isPrisoner, bool playerAvailable, int prisonerCount,
            bool inMainParty, bool inPlayerClanDungeon)
        {
            return isPrisoner && playerAvailable && prisonerCount == 1
                && (inMainParty || inPlayerClanDungeon);
        }
    }
}
