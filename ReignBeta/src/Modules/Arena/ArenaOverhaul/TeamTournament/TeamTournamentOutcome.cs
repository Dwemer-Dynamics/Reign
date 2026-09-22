using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace ArenaOverhaul.TeamTournament
{
    /// <summary>Result metadata missing from Bannerlord's single-winner tournament event.</summary>
    public sealed class TeamTournamentOutcome
    {
        private static TeamTournamentOutcome? _latest;
        private readonly string _townId;
        private readonly string _winnerId;
        private readonly float _day;

        private TeamTournamentOutcome(Town town, CharacterObject winner, IEnumerable<CharacterObject> teammates)
        {
            _townId = town.StringId;
            _winnerId = winner.StringId;
            _day = (float)CampaignTime.Now.ToDays;
            WinningTeam = teammates.Where(x => x != null).GroupBy(x => x.StringId).Select(x => x.First()).ToArray();
        }

        public IReadOnlyList<CharacterObject> WinningTeam { get; }

        public static void Capture(Town town, CharacterObject? winner, IEnumerable<CharacterObject>? teammates)
        {
            _latest = town == null || winner == null || teammates == null
                ? null : new TeamTournamentOutcome(town, winner, teammates);
        }

        public static TeamTournamentOutcome? ForFinishedEvent(Town town, CharacterObject winner)
        {
            TeamTournamentOutcome? outcome = _latest;
            if (outcome == null || town == null || winner == null) return null;
            return string.Equals(outcome._townId, town.StringId, StringComparison.Ordinal)
                && string.Equals(outcome._winnerId, winner.StringId, StringComparison.Ordinal)
                && Math.Abs((float)CampaignTime.Now.ToDays - outcome._day) < 0.001f ? outcome : null;
        }
    }
}
