using TaleWorlds.MountAndBlade;

namespace ReignBeta.World
{
    internal static class ReignSceneTeamSafety
    {
        internal static bool AreHostile(Team actorTeam, Team targetTeam)
        {
            // Town civilians can have Team.Invalid, a non-null sentinel whose
            // MBTeam has no mission. Calling IsEnemyOf on it dereferences null.
            return actorTeam?.IsValid == true && targetTeam?.IsValid == true
                && actorTeam.IsEnemyOf(targetTeam);
        }
    }
}
