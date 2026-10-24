namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketDiplomacyPresentationContractTests
{
    [Fact]
    public void PlayerFacingDiplomacyNoticesUseNarrativeInsteadOfRawStatistics()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Diplomacy", "Campaign", "ReignWorldDiplomacyCampaignBehavior.cs"));

        Assert.Contains("ReignNewsCampaignBehavior.Instance?.ForeignRuling(notice)", source, StringComparison.Ordinal);
        Assert.Contains("ReignNewsCampaignBehavior.Instance?.ClanRuling(notice)", source, StringComparison.Ordinal);
        string news = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "News", "ReignNewsNativeNotices.cs"));
        Assert.Contains("ReignNewsCopy.ClanRuling", news, StringComparison.Ordinal);
        Assert.Contains("ReignNewsCopy.ForeignRuling", news, StringComparison.Ordinal);
        Assert.DoesNotContain("notice.CharmRoll", news, StringComparison.Ordinal);
        Assert.DoesNotContain("notice.CharmChanceBasisPoints", news, StringComparison.Ordinal);
        // The shared formatter's behavioral tests cover both mediation outcomes,
        // both winning houses, pending rulings and hostile/peaceful pressure changes.
        Assert.DoesNotContain("; pressure \" + notice.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("% mediation chance", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Charm \" + notice.CharmSkill", source, StringComparison.Ordinal);
    }
}
