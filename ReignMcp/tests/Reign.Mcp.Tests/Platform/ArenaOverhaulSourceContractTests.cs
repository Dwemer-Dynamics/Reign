namespace Reign.Mcp.Tests;

public sealed class ArenaOverhaulSourceContractTests
{
    [Fact]
    public void TeamTournamentRosterConditionHandlesMissingDisabledReason()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Arena", "ArenaOverhaul", "CampaignBehaviors", "AOArenaBehavior.cs"));

        Assert.Contains("string.IsNullOrEmpty(disabledText?.ToString())", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("shouldBeDisabled || string.IsNullOrEmpty(disabledText", source,
            StringComparison.Ordinal);
    }
}
