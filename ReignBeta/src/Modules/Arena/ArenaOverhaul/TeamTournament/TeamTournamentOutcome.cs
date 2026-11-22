using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ArenaOverhaul.TeamTournament
{
    /// <summary>Result metadata missing from Bannerlord's single-winner tournament event.</summary>
    public sealed class TeamTournamentOutcome
    {
        [ThreadStatic] private static TeamTournamentOutcome? _dispatchOutcome;
        private readonly Campaign _campaign;
        private readonly MBReadOnlyList<CharacterObject> _participants;

        private TeamTournamentOutcome(Campaign campaign, MBReadOnlyList<CharacterObject> participants,
            IEnumerable<CharacterObject> teammates)
        {
            _campaign = campaign;
            _participants = participants;
            WinningTeam = teammates.Where(x => x != null).GroupBy(x => x.StringId).Select(x => x.First()).ToArray();
        }

        public IReadOnlyList<CharacterObject> WinningTeam { get; }

        internal static void PublishFinishedEvent(Campaign? campaign, CharacterObject? winner,
            List<CharacterObject> participants, Town? town, ItemObject prize, IEnumerable<CharacterObject>? teammates)
        {
            // The native dispatcher forwards this exact roster object to each synchronous listener.
            var eventParticipants = new MBReadOnlyList<CharacterObject>(participants);
            var previous = _dispatchOutcome;
            _dispatchOutcome = campaign != null && ReferenceEquals(campaign, Campaign.Current)
                && town != null && winner != null && teammates != null
                ? new TeamTournamentOutcome(campaign, eventParticipants, teammates)
                : null;
            try
            {
                CampaignEventDispatcher.Instance.OnTournamentFinished(winner, eventParticipants, town, prize);
            }
            finally
            {
                _dispatchOutcome = previous;
            }
        }

        public static TeamTournamentOutcome? ForFinishedEvent(MBReadOnlyList<CharacterObject> participants)
        {
            var outcome = _dispatchOutcome;
            return outcome != null && ReferenceEquals(outcome._campaign, Campaign.Current)
                && ReferenceEquals(outcome._participants, participants) ? outcome : null;
        }
    }
}
