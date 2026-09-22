namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketParticipantDiversityContractTests
{
    [Fact]
    public void NobleFixtureVariesProductionParticipantsAndRetainsAuditMetadata()
    {
        string root = TestOptions.FindWorkspace();
        string fixture = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Court", "Court", "ReignNobleDocketInGameTests.cs"));
        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignMcp"),
            "TestingTools.Court.cs"));

        Assert.Contains("|noble-fixture-participants", fixture, StringComparison.Ordinal);
        Assert.Contains("MaximumProductionSlots = 2048", fixture, StringComparison.Ordinal);
        Assert.Contains("MaximumDistinctVariants = 64", fixture, StringComparison.Ordinal);
        Assert.Contains("participantSelectionSlot", fixture, StringComparison.Ordinal);
        Assert.Contains("fixture_run_distinct_production_variant", fixture, StringComparison.Ordinal);
        Assert.Contains("SelectDistinctNobleFixtureVariant", fixture, StringComparison.Ordinal);
        Assert.Contains("participantSignatures.Add(signature)", fixture, StringComparison.Ordinal);
        Assert.Contains("availableDistinctVariantCount", fixture, StringComparison.Ordinal);
        Assert.Contains("NobleParticipantDiversityJson", fixture, StringComparison.Ordinal);
        Assert.Contains("[\"fixtureRunId\"] = runId", fixture, StringComparison.Ordinal);
        foreach (string field in new[]
                 {
                     "heroId", "heroName", "role", "clanId", "cultureId", "isFemale", "age",
                     "occupation", "isClanLeader", "isMarried", "distinctHeroCount",
                     "distinctClanCount"
                 })
            Assert.Contains("[\"" + field + "\"]", fixture, StringComparison.Ordinal);

        Assert.True((int)Reign.Mcp.Server.TestingTools.GetRulerDocketTestManifest()["schemaVersion"] >= 11);
        Assert.Contains("participant-diverse noble cases", mcp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every unique fixtureRunId deterministically chooses among", mcp,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("record every persisted player and NPC line verbatim", mcp,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visibleDialogueStatFree", fixture, StringComparison.Ordinal);
        Assert.Contains("forbiddenStatistics", fixture, StringComparison.Ordinal);
    }
}
