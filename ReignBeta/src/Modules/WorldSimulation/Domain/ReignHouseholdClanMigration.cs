namespace ReignBeta.WorldSimulation.Domain
{
    /// <summary>
    /// World-Simulation-owned flag that Court sets while it migrates court-noble households between clans, so
    /// world history can skip the clan-change events that migration raises.
    /// </summary>
    internal static class ReignHouseholdClanMigration
    {
        internal static bool InProgress { get; set; }
    }
}
