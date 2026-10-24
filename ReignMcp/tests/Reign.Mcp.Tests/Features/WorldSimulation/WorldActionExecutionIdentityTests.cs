using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class WorldActionExecutionIdentityTests
{
    [Fact]
    public void CompleteClauseIdentitySeparatesSharedPrefixesAndTupleBoundaries()
    {
        const string prefix = "same_sixteen_chr";
        Assert.Equal(16, prefix.Length);
        Assert.NotEqual(WorldActionExecutionIdentity.Create("deal", prefix + "one", 0),
            WorldActionExecutionIdentity.Create("deal", prefix + "two", 0));
        Assert.NotEqual(WorldActionExecutionIdentity.Create("a_b", "c", 0),
            WorldActionExecutionIdentity.Create("a", "b_c", 0));
        Assert.NotEqual(WorldActionExecutionIdentity.Create("deal", "A", 0),
            WorldActionExecutionIdentity.Create("deal", "a", 0));
        Assert.NotEqual(WorldActionExecutionIdentity.Create("deal", "é", 0),
            WorldActionExecutionIdentity.Create("deal", "e\u0301", 0));
        string large = WorldActionExecutionIdentity.Create(new string('a', 400), new string('b', 400), int.MaxValue);
        Assert.Equal(78, large.Length);
        Assert.Matches("^sa_[0-9a-f]{64}_2147483647$", large);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2147483647)]
    public void NewAndRetainedIdentitiesKeepTheirStyleAcrossInstallmentsAndReplay(int installment)
    {
        const string agreement = "deal_1"; const string clause = "sixteen_chars_id_more";
        string captured = WorldActionExecutionIdentity.Create(agreement, clause, 0);
        string next = WorldActionExecutionIdentity.RebindCaptured(captured, agreement, clause, installment);
        Assert.Equal(WorldActionExecutionIdentity.Create(agreement, clause, installment), next);
        Assert.Equal(next, WorldActionExecutionIdentity.RebindCaptured(next, agreement, clause, installment));
        Assert.True(WorldActionExecutionIdentity.TryGetInstallment(next, agreement, clause, out int found));
        Assert.Equal(installment, found);
        const string historical = "sa_deal_1_sixteen_chars_id_0";
        string retained = WorldActionExecutionIdentity.RebindCaptured(historical, agreement, clause, installment);
        Assert.Equal("sa_deal_1_sixteen_chars_id_" + installment, retained);
        Assert.True(WorldActionExecutionIdentity.TryGetInstallment(retained, agreement, clause, out found));
        Assert.Equal(installment, found);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("+1")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("2147483648")]
    [InlineData("1_other")]
    public void MalformedNativeSuffixFailsClosedForBothFormats(string suffix)
    {
        string newId = WorldActionExecutionIdentity.Create("deal", "clause", 0);
        foreach (string prefix in new[] { newId[..^1], "sa_deal_clause_" })
        {
            Assert.False(WorldActionExecutionIdentity.TryGetInstallment(prefix + suffix, "deal", "clause", out _));
            Assert.Throws<InvalidOperationException>(() =>
                WorldActionExecutionIdentity.RebindCaptured(prefix + suffix, "deal", "clause", 0));
        }
    }

    [Fact]
    public void MissingAndForeignSnapshotsCannotBeRebound()
    {
        foreach (string? id in new string?[] { null, "", WorldActionExecutionIdentity.Create("other", "clause", 0),
            WorldActionExecutionIdentity.Create("deal", "other", 0), "sa_other_clause_0" })
        {
            Assert.False(WorldActionExecutionIdentity.TryGetInstallment(id, "deal", "clause", out _));
            Assert.Throws<InvalidOperationException>(() => WorldActionExecutionIdentity.RebindCaptured(id, "deal", "clause", 1));
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldActionExecutionIdentity.Create("deal", "clause", -1));
        Assert.Throws<ArgumentException>(() => WorldActionExecutionIdentity.Create("", "clause", 0));
    }
}
