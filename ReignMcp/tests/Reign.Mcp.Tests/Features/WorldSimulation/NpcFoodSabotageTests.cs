using Newtonsoft.Json;
using ReignBeta.Economy;

namespace Reign.Mcp.Tests;

public sealed class NpcFoodSabotageTests
{
    private static NpcFoodSabotageTarget Target(string sponsor, string enemy, string town, string village, bool preferred = true)
        => new() { SponsorId = sponsor, TargetId = enemy, TownId = town, VillageId = village, Preferred = preferred };

    private static int RollDay(params string[] sponsors)
        => Enumerable.Range(40, 20000).First(day => sponsors.All(id => ReignNpcFoodSabotagePolicy.HasChance("campaign", day, id)));

    [Fact]
    public void ThirtyDayTwoRealmWarAveragesTwoToThreeDistinctStrikesEachWay()
    {
        var totals = new int[4];
        var twoOrThree = new int[4];
        var targets = Enumerable.Range(0, 6)
            .SelectMany(i => new[] { Target("a", "b", $"b-town-{i}", $"b-village-{i}"),
                Target("b", "a", $"a-town-{i}", $"a-village-{i}") }).ToArray();
        const int campaigns = 1000;
        for (int seed = 0; seed < campaigns; seed++)
        {
            var state = new NpcFoodSabotageState();
            for (int day = 0; day < 120; day++)
            {
                ReignNpcFoodSabotagePolicy.Advance(state, $"campaign-{seed}", day, targets);
                Assert.All(state.Recent.GroupBy(x => x.SponsorId), group => Assert.InRange(group.Count(), 1, 3));
                Assert.All(state.Recent.GroupBy(x => x.TargetId), group => Assert.InRange(group.Count(), 1, 3));
                if (day % 30 != 29) continue;

                foreach (string sponsor in new[] { "a", "b" })
                {
                    var hits = state.Recent.Where(x => x.SponsorId == sponsor).ToArray();
                    Assert.Equal(hits.Length, hits.Select(x => x.VillageId).Distinct().Count());
                    if (hits.Length >= 2) twoOrThree[day / 30]++;
                    totals[day / 30] += hits.Length;
                }
            }
        }
        Assert.InRange((double)totals[0] / (campaigns * 2), 2.48, 2.70);
        Assert.All(totals.Skip(1), total => Assert.InRange((double)total / (campaigns * 2), 2.02, 2.36));
        Assert.All(twoOrThree, count => Assert.InRange((double)count / (campaigns * 2), 0.72, 0.94));
    }

    [Fact]
    public void CappedEnemyFallsThroughToAnOpenFrontAndRecentReceiptsSurviveUntilDayThirty()
    {
        int day = RollDay("a");
        var state = new NpcFoodSabotageState
        {
            Recent = new() {
                new() { SponsorId = "c", TargetId = "b", TownId = "b1", VillageId = "v1", Day = day - 30 },
                new() { SponsorId = "d", TargetId = "b", TownId = "b2", VillageId = "v2", Day = day - 29 },
                new() { SponsorId = "e", TargetId = "b", TownId = "b3", VillageId = "v3", Day = day - 28, Ended = true },
                new() { SponsorId = "f", TargetId = "b", TownId = "b5", VillageId = "v6", Day = day - 27 }
            }
        };
        ReignNpcFoodSabotagePolicy.Advance(state, "campaign", day,
            new[] { Target("a", "b", "b4", "v4"), Target("a", "c", "c1", "v5") });
        Assert.DoesNotContain(state.Recent, x => x.VillageId == "v1");
        Assert.Equal("c", Assert.Single(state.Recent, x => x.Day == day).TargetId);
        Assert.Equal(3, state.Recent.Count(x => x.TargetId == "b"));
    }

    [Fact]
    public void TwoVillagesCanBeHitAtOneTownAndSelectionIgnoresEnumerationOrder()
    {
        int day = Enumerable.Range(40, 20000).First(d => ReignNpcFoodSabotagePolicy.HasChance("campaign", d, "a")
            && ReignNpcFoodSabotagePolicy.HasChance("campaign", d, "c")
            && Enumerable.Range(d + 1, 9).Any(next => ReignNpcFoodSabotagePolicy.HasChance("campaign", next, "d")));
        var targets = new[] { Target("a", "b", "town", "first"), Target("c", "b", "town", "second") };
        var first = new NpcFoodSabotageState();
        var second = new NpcFoodSabotageState();
        ReignNpcFoodSabotagePolicy.Advance(first, "campaign", day, targets);
        ReignNpcFoodSabotagePolicy.Advance(second, "campaign", day, targets.Reverse());
        Assert.Equal(2, first.Recent.Count);
        Assert.All(first.Recent, x => Assert.Equal("town", x.TownId));
        Assert.Equal(first.Recent.Select(x => x.VillageId), second.Recent.Select(x => x.VillageId));
        Assert.Equal(2, first.Recent.Select(x => x.VillageId).Distinct().Count());

        // A third village belonging to the same town is ineligible while both effects are active.
        int next = Enumerable.Range(day + 1, 9).First(d => ReignNpcFoodSabotagePolicy.HasChance("campaign", d, "d"));
        ReignNpcFoodSabotagePolicy.Advance(first, "campaign", next,
            new[] { Target("d", "b", "town", "third"), Target("d", "b", "other-town", "fourth") });
        Assert.Equal("other-town", Assert.Single(first.Recent, x => x.SponsorId == "d").TownId);
    }

    [Fact]
    public void SaveRoundTripKeepsTheRollFenceAndNoEnemyMeansNoNewStrike()
    {
        int day = RollDay("a");
        var state = new NpcFoodSabotageState();
        var targets = new[] { Target("a", "b", "town-a", "v1", false), Target("a", "b", "town-b", "v2") };
        ReignNpcFoodSabotagePolicy.Advance(state, "campaign", day, targets);
        Assert.Equal("v2", Assert.Single(state.Recent).VillageId);
        var copy = JsonConvert.DeserializeObject<NpcFoodSabotageState>(JsonConvert.SerializeObject(state))!;
        ReignNpcFoodSabotagePolicy.Advance(copy, "campaign", day, targets);
        ReignNpcFoodSabotagePolicy.Advance(copy, "campaign", day + 1, Array.Empty<NpcFoodSabotageTarget>());
        Assert.Equal(1L, copy.TotalStrikes);
        Assert.Equal(day + 1, copy.LastProcessedDay);
        Assert.Equal(day, Assert.Single(copy.Recent).Day);
        Assert.Empty(JsonConvert.DeserializeObject<ReignEconomyState>("{}")!.NpcFoodSabotage.Recent);
    }

    [Fact]
    public void EffectEndsAfterTenDaysAndOnlyAppliesInsideTheOriginalNpcTarget()
    {
        var hit = new NpcFoodSabotageStrike { SponsorId = "a", TargetId = "b", TownId = "town", VillageId = "v", Day = 25 };
        Assert.True(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "player", "b", "b", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 35, "player", "b", "b", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "a", "b", "b", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "b", "b", "b", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "player", "c", "b", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "player", "b", "c", "town"));
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "player", "b", "b", "new-town"));
        hit.Ended = true;
        Assert.False(ReignNpcFoodSabotagePolicy.IsActive(hit, 34, "player", "b", "b", "town"));
    }

    [Fact]
    public void TwoVillageLossesFeedOneTownReliefDemand()
    {
        var donor = new ReignWarEconomyPolicy.Holding { Id = "donor", Stock = 190, Capacity = 200, Security = 100 };
        var town = new ReignWarEconomyPolicy.Holding { Id = "town", Stock = 120, Capacity = 200, Security = 100 };
        var realm = new[] { donor, town };
        Assert.Empty(ReignWarEconomyPolicy.AllocateRelief(realm).Shipments);

        float factor = ReignNpcFoodSabotagePolicy.FoodFactor;
        town.Change = ReignNpcFoodSabotagePolicy.BoundFoodDelta(10f, 0f, factor)
            + ReignNpcFoodSabotagePolicy.BoundFoodDelta(15f, 0f, factor);
        var relief = ReignWarEconomyPolicy.AllocateRelief(realm);
        Assert.Equal(15d, Assert.Single(relief.Decisions).Demand, 5);
        var shipment = Assert.Single(relief.Shipments);
        Assert.Equal("town", shipment.Recipient);
        Assert.Equal(15d, shipment.Shipped, 5);
        Assert.Equal(13.5d, shipment.Delivered, 5);
        Assert.Equal(120d, town.Stock);
    }

    [Theory]
    [InlineData(0, -2)]
    [InlineData(-0.35, -2)]
    [InlineData(-0.90, -1)]
    [InlineData(-1.10, 0)]
    public void BoundDeltaUsesTheExistingAdditiveProductionClamp(float existing, float expected)
        => Assert.Equal(expected, ReignNpcFoodSabotagePolicy.BoundFoodDelta(10, existing, -0.20f), 5);
}
