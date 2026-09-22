namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketDiplomacyPresentationContractTests
{
    [Fact]
    public void PlayerFacingDiplomacyNoticesUseNarrativeInsteadOfRawStatistics()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Diplomacy", "Campaign", "ReignWorldDiplomacyCampaignBehavior.cs"));

        Assert.Contains("DescribePoliticalPressureShift", source, StringComparison.Ordinal);
        Assert.Contains("political pressure mounted", source, StringComparison.Ordinal);
        Assert.Contains("political pressure eased", source, StringComparison.Ordinal);
        Assert.Contains("political pressure held steady", source, StringComparison.Ordinal);
        Assert.Contains("mediation brought the clans to an accord", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("; pressure \" + notice.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("% mediation chance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Charm \" + notice.CharmSkill", source, StringComparison.Ordinal);
    }
}
