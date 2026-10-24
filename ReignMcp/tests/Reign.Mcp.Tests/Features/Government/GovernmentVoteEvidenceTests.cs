using System.Text.Json;
using ReignBeta.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentVoteEvidenceTests
{
    private static ReignGovernmentBusinessRecord Matter(string status = "decided", string votes = "[]") =>
        new() { BusinessId = "admission", KingdomStringId = "empire", Status = status, VotesJson = votes };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RecordedServiceSurvivesLossOfOfficeAndSaveReload(bool eligible)
    {
        var matter = Matter(votes: "[{\"memberHeroId\":\"outgoing-senator\",\"optionId\":\"accept\",\"absentee\":true}]");
        var restored = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(JsonSerializer.Serialize(matter,
            new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true })!;
        string before = JsonSerializer.Serialize(restored, new JsonSerializerOptions { IncludeFields = true });
        Assert.True(GovernmentVoteEvidence.Observe(restored, "admission", "outgoing-senator", "accept", eligible));
        Assert.Equal(before, JsonSerializer.Serialize(restored, new JsonSerializerOptions { IncludeFields = true }));
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("")]
    public void WrongVoteOrAbstentionDoesNotFulfillAPromise(string actualOption)
    {
        var matter = Matter(votes: "[{\"memberHeroId\":\"senator\",\"optionId\":\"" + actualOption + "\"}]");
        var error = Assert.Throws<InvalidOperationException>(() =>
            GovernmentVoteEvidence.Observe(matter, "admission", "senator", "accept", false));
        Assert.Contains("does not fulfill", error.Message);
    }

    [Fact]
    public void AnotherProposalOrMembersBallotCannotCompleteTheService()
    {
        var matter = Matter(votes: "[{\"memberHeroId\":\"another-senator\",\"optionId\":\"accept\"}]");
        Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(matter, "other-matter", "another-senator", "accept", true));
        Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(matter, "admission", "promisor", "accept", true));
        Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(null!, "admission", "promisor", "accept", true));
    }

    [Fact]
    public void AnOpenPromiseStillNeedsCurrentEligibilityUntilABallotExists()
    {
        var matter = Matter("presented");
        Assert.False(GovernmentVoteEvidence.Observe(matter, "admission", "senator", "accept", true));
        Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(matter, "admission", "senator", "accept", false));
        matter.Status = "decided";
        var error = Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(matter, "admission", "senator", "accept", true));
        Assert.Contains("closed without", error.Message);
    }

    [Fact]
    public void AmbiguousBallotEvidenceFailsClosed()
    {
        var matter = Matter(votes: "[{\"memberHeroId\":\"senator\",\"optionId\":\"accept\"},{\"memberHeroId\":\"senator\",\"optionId\":\"reject\"}]");
        Assert.Throws<InvalidOperationException>(() => GovernmentVoteEvidence.Observe(matter, "admission", "senator", "accept", true));
    }
}
