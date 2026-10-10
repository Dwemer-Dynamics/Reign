using ReignBeta.World;
using TaleWorlds.MountAndBlade;

namespace Reign.Mcp.Tests
{
    public sealed class SceneTeamSafetyTests
    {
        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void CivilianSentinelNeverEntersNativeHostilityLookup(bool actorValid, bool targetValid)
        {
            var actor = new Team(actorValid, true);
            var target = new Team(targetValid, true);
            Assert.False(ReignSceneTeamSafety.AreHostile(actor, target));
            Assert.Equal(0, actor.HostilityQueries);
            Assert.Equal(0, target.HostilityQueries);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RealTeamsPreserveTheEnginesHostilityDecision(bool hostile)
        {
            var actor = new Team(true, hostile);
            var target = new Team(true, false);
            Assert.Equal(hostile, ReignSceneTeamSafety.AreHostile(actor, target));
            Assert.Equal(1, actor.HostilityQueries);
            Assert.Same(target, actor.LastQueriedTeam);
        }

        [Fact]
        public void AbsentTeamsAreNeutralWithoutCallingNativeCode()
        {
            var present = new Team(true, true);
            Assert.False(ReignSceneTeamSafety.AreHostile(null!, present));
            Assert.False(ReignSceneTeamSafety.AreHostile(present, null!));
            Assert.False(ReignSceneTeamSafety.AreHostile(null!, null!));
            Assert.Equal(0, present.HostilityQueries);
        }

        [Theory]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void BattleSamplingRejectsInvalidTeamsWithoutNativeLookup(bool agentValid, bool playerValid)
        {
            var agentTeam = new Team(agentValid, true);
            var playerTeam = new Team(playerValid, true);

            Assert.False(ReignSceneTeamSafety.IsPlayerTeamOrEnemy(agentTeam, playerTeam));
            Assert.Equal(0, agentTeam.HostilityQueries);
            Assert.Equal(0, playerTeam.HostilityQueries);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SameTeamCountsOnlyWhenValidAndNeedsNoNativeLookup(bool valid)
        {
            var team = new Team(valid, true);

            Assert.Equal(valid, ReignSceneTeamSafety.IsPlayerTeamOrEnemy(team, team));
            Assert.Equal(0, team.HostilityQueries);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void BattleSamplingCountsEnemiesAndRejectsNeutralTeams(bool hostile)
        {
            var agentTeam = new Team(true, hostile);
            var playerTeam = new Team(true, false);

            Assert.Equal(hostile, ReignSceneTeamSafety.IsPlayerTeamOrEnemy(agentTeam, playerTeam));
            Assert.Equal(1, agentTeam.HostilityQueries);
            Assert.Same(playerTeam, agentTeam.LastQueriedTeam);
        }

        [Fact]
        public void BattleSamplingRejectsMissingTeamsWithoutNativeLookup()
        {
            var present = new Team(true, true);

            Assert.False(ReignSceneTeamSafety.IsPlayerTeamOrEnemy(null!, present));
            Assert.False(ReignSceneTeamSafety.IsPlayerTeamOrEnemy(present, null!));
            Assert.False(ReignSceneTeamSafety.IsPlayerTeamOrEnemy(null!, null!));
            Assert.Equal(0, present.HostilityQueries);
        }
    }
}

namespace TaleWorlds.MountAndBlade
{
    // Engine boundary double: the captured civilian had MBTeam.Index=-1 and
    // MBTeam._mission=null. Real Team.IsEnemyOf dereferences that missing mission.
    // The linked production guard must keep that call outside the invalid cases.
    public sealed class Team
    {
        private readonly bool _hostile;
        public Team(bool valid, bool hostile) { IsValid = valid; _hostile = hostile; }
        public bool IsValid { get; }
        public int HostilityQueries { get; private set; }
        public Team? LastQueriedTeam { get; private set; }
        public bool IsEnemyOf(Team other)
        {
            HostilityQueries++;
            LastQueriedTeam = other;
            if (!IsValid || !other.IsValid)
                throw new NullReferenceException("Invalid team has no native mission.");
            return _hostile;
        }
    }
}
