using ReignBeta.Shared.Arena;

namespace Reign.Mcp.Tests;

public sealed class ArenaPrizeVarietyTests
{
    private sealed record Prize(string Id);
    private sealed record Entrant(string Name, string? HeroId);

    [Fact]
    public void Regular_tournament_excludes_actual_temporary_guests_without_filtering_shared_troop_types()
    {
        var pool = new[] { new Entrant("Michael", "player"), new Entrant("Rhagaea", "rhagaea"),
            new Entrant("Ira", "ira"), new Entrant("Other lord", "other"), new Entrant("Legionary", null) };
        var roster = new[] { "player", "rhagaea", "ira" };
        var playing = TournamentPartyProtection.Filter(pool, true, p => p.HeroId, "player", roster);
        Assert.Equal(new[] { "Michael", "Other lord", "Legionary" }, playing.Select(p => p.Name));
        Assert.Equal(pool, TournamentPartyProtection.Filter(pool, false, p => p.HeroId, "player", roster));
        Assert.Contains(pool[2], TournamentPartyProtection.Filter(pool, true, p => p.HeroId, "player", new[] { "player", "rhagaea" }));
        // A later intake can re-add the hero; the final guard still removes it.
        Assert.DoesNotContain(pool[1], TournamentPartyProtection.Filter(playing.Append(pool[1]), true, p => p.HeroId, "player", roster));
    }

    [Theory]
    [InlineData(0, 3, 4, false)] [InlineData(1, 3, 4, true)] [InlineData(1, 4, 4, false)]
    [InlineData(1, 5, 3, false)] [InlineData(2, 4, 5, true)] [InlineData(2, 5, 3, false)]
    [InlineData(3, 5, 3, true)] [InlineData(3, 4, 4, false)] [InlineData(99, 4, 4, true)]
    public void Reroll_policy_retains_existing_preferences(int mode, int before, int after, bool expected) =>
        Assert.Equal(expected, TournamentPrizeVariety.ShouldReroll(mode, before, after));

    [Fact]
    public void Recent_prizes_are_excluded_when_the_original_minimum_pool_can_be_preserved()
    {
        var prizes = Enumerable.Range(0, 20).Select(i => new Prize("item" + i)).ToList();
        prizes.Add(prizes[0]);
        var recent = prizes.Take(10).Select(p => p.Id).ToArray();
        var fresh = TournamentPrizeVariety.FreshPool(prizes, p => p.Id, recent);
        Assert.Equal(10, fresh.Count);
        Assert.DoesNotContain(fresh, p => recent.Contains(p.Id));
        Assert.Equal(20, TournamentPrizeVariety.FreshPool(prizes, p => p.Id, prizes.Select(p => p.Id)).Count);
        Assert.Empty(TournamentPrizeVariety.FreshPool(Array.Empty<Prize>(), p => p.Id, recent));
    }

    [Fact]
    public void Small_catalogs_relax_history_instead_of_collapsing_the_native_reward_pool()
    {
        var prizes = new[] { new Prize("a"), new Prize("b") };
        Assert.Equal(2, TournamentPrizeVariety.FreshPool(prizes, p => p.Id, new[] { "a" }).Count);
    }

    [Theory]
    [InlineData(7, 1, 7)] [InlineData(8, 1, 8)] [InlineData(9, 1, 8)] [InlineData(9, 2, 9)]
    public void Original_minimum_eight_candidate_boundary_is_retained(int count, int blocked, int expected)
    {
        var prizes = Enumerable.Range(0, count).Select(i => new Prize("item" + i)).ToList();
        Assert.Equal(expected, TournamentPrizeVariety.FreshPool(prizes, p => p.Id, prizes.Take(blocked).Select(p => p.Id)).Count);
        Assert.Equal(count, TournamentPrizeVariety.FreshPool(prizes, p => p.Id, null).Count);
    }

    [Fact]
    public void Persisted_history_round_trip_is_bounded_and_retains_recency()
    {
        var history = TournamentPrizeVariety.NormalizeHistory(Enumerable.Range(0, 15).Select(i => "item" + i)
            .Concat(new[] { "", "item7", "item14" }));
        var loaded = System.Text.Json.JsonSerializer.Deserialize<List<string>>(System.Text.Json.JsonSerializer.Serialize(history));
        Assert.Equal(10, history.Count);
        Assert.Equal(10, history.Distinct().Count());
        Assert.Equal(new[] { "item7", "item14" }, history.TakeLast(2));
        Assert.Empty(TournamentPrizeVariety.NormalizeHistory(null));
        Assert.Equal(history, TournamentPrizeVariety.NormalizeHistory(loaded));
        Assert.NotSame(history, TournamentPrizeVariety.NormalizeHistory(history));
    }
}
