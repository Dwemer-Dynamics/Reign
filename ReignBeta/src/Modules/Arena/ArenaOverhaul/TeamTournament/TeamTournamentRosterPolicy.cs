using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;

namespace ArenaOverhaul.TeamTournament
{
    internal static class TeamTournamentRosterPolicy
    {
        public static List<CharacterObject> BuildOpponents(
            IEnumerable<CharacterObject> heroes,
            IEnumerable<CharacterObject> regularTroops,
            IEnumerable<CharacterObject> fallbackTroops,
            IEnumerable<CharacterObject> playerPartyMembers,
            IEnumerable<CharacterObject> selectedMembers,
            bool isPlayerParticipating,
            int requiredCount)
        {
            if (requiredCount < 0) throw new ArgumentOutOfRangeException(nameof(requiredCount));

            var party = new HashSet<CharacterObject>(playerPartyMembers);
            var selected = new HashSet<CharacterObject>(selectedMembers);
            var result = heroes.Where(x => x.IsHero && !x.IsPlayerCharacter
                    && !selected.Contains(x) && (!isPlayerParticipating || !party.Contains(x)))
                .Distinct().Take(requiredCount).ToList();
            if (result.Count == requiredCount) return result;

            // The caller excludes the main party as a troop source. Character
            // identity is a shared template for regular soldiers, so filtering
            // those templates against the player roster would exclude other
            // parties' soldiers of the same type too.
            var troopPool = regularTroops.Where(x => !x.IsHero && x.CanBeAParticipant(true)).Distinct().ToList();
            if (troopPool.Count == 0)
                troopPool = fallbackTroops.Where(x => !x.IsHero && x.CanBeAParticipant(true)).Distinct().ToList();
            if (troopPool.Count == 0)
                throw new InvalidOperationException("No regular arena troops are available to fill opposing teams.");

            for (int index = 0; result.Count < requiredCount; index++)
                result.Add(troopPool[index % troopPool.Count]);
            return result;
        }
    }
}
