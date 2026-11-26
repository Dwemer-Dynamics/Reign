namespace Reign.Mcp.Tests.Features.Court;

public sealed class RulerDocketDiplomacyPresentationContractTests
{
    [Fact]
    public void ProviderQueueDeferralRetriesWithoutCountingItAsAnOutage()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Diplomacy", "Campaign", "ReignWorldDiplomacyCampaignBehavior.cs"));

        int branch = source.IndexOf("if (result.Status == \"provider_queue_timeout\")", StringComparison.Ordinal);
        int failure = source.IndexOf("RecordEvaluationFailure(result.Error)", StringComparison.Ordinal);
        Assert.True(branch >= 0 && failure > branch);
        string deferral = source[branch..failure];
        Assert.Contains("_providerQueueDeferred = true;", deferral, StringComparison.Ordinal);
        Assert.Contains("return;", deferral, StringComparison.Ordinal);
        Assert.DoesNotContain("_consecutiveEvaluationFailures", deferral, StringComparison.Ordinal);
        Assert.Contains("EvaluationRetryPending => _providerQueueDeferred ||", source, StringComparison.Ordinal);
        Assert.Contains("EvaluationRetryPending && now - _lastRetryDay >= 0.25f", source, StringComparison.Ordinal);
        Assert.Contains("_ = EvaluateAsync(true);", source, StringComparison.Ordinal);
        Assert.Contains("ref _providerQueueDeferred", source, StringComparison.Ordinal);
        Assert.Contains("_consecutiveEvaluationFailures++", source, StringComparison.Ordinal);
        Assert.Contains("InvokeAsync(ShowOutageOnce)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayerFacingDiplomacyNoticesUseNarrativeInsteadOfRawStatistics()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "Diplomacy", "Campaign", "ReignWorldDiplomacyCampaignBehavior.cs"));

        Assert.Contains("ReignServices.News?.ForeignRuling(notice)", source, StringComparison.Ordinal);
        Assert.Contains("ReignServices.News?.ClanRuling(notice)", source, StringComparison.Ordinal);
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
