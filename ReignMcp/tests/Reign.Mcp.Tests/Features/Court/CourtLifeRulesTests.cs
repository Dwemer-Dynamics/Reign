using Reign.Core.Contracts.Court;

namespace Reign.Mcp.Tests;

public sealed class CourtLifeRulesTests
{
    [Fact]
    public void EveryRollHasOneSourceAndPatronageIsLeastFrequent()
    {
        var counts = Enumerable.Range(0, 30).Select(ReignCourtLifeRules.SourceForRoll)
            .GroupBy(x => x).ToDictionary(x => x.Key, x => x.Count());
        Assert.Equal(6, counts.Count);
        Assert.Equal(6, counts[ReignDocketSource.Notable]);
        Assert.Equal(6, counts[ReignDocketSource.Visitor]);
        Assert.Equal(5, counts[ReignDocketSource.DomesticNoble]);
        Assert.Equal(5, counts[ReignDocketSource.International]);
        Assert.Equal(5, counts[ReignDocketSource.Family]);
        Assert.Equal(3, counts[ReignDocketSource.Patronage]);
    }

    [Fact]
    public void DueRepliesReserveCapacityWithoutSuppressingAllOrdinaryRolls()
    {
        for (int day = 0; day < 1000; day++)
        for (int replies = 0; replies <= 12; replies++)
        {
            int ordinary = ReignCourtLifeRules.OrdinarySlots("campaign", "branch", day, replies);
            int reserved = Math.Min(5, replies);
            Assert.InRange(ordinary + reserved, 1, 5);
            Assert.Equal(replies >= 5, ordinary == 0);
        }
    }

    [Fact]
    public void SameSaveSeedReplaysAndBranchesCanDiffer()
    {
        var first = Enumerable.Range(0, 365).Select(day => ReignCourtLifeRules.SourceForSlot("campaign", "a", day, 0)).ToArray();
        Assert.Equal(first, Enumerable.Range(0, 365).Select(day => ReignCourtLifeRules.SourceForSlot("campaign", "a", day, 0)));
        Assert.NotEqual(first, Enumerable.Range(0, 365).Select(day => ReignCourtLifeRules.SourceForSlot("campaign", "b", day, 0)).ToArray());
        Assert.Equal(6, first.Distinct().Count());
    }
}
