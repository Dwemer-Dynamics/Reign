using EscapePolicy = ReignBeta.Campaign.ReignRebellionResidentEscape;

namespace Reign.Mcp.Tests;

public sealed class RebellionResidentEscapeTests
{
    private static EscapePolicy.Resident Resident(string id = "rebel_noble", bool rebel = true,
        string origin = "loyalist_town") => new()
    {
        HeroId = id, ClanId = rebel ? "rebel_clan" : "loyalist_clan", SettlementId = origin,
        HomeSettlementId = string.Empty, IsRebel = rebel, IsParticipant = true, IsNoble = true, IsAlive = true
    };

    private static EscapePolicy.Stronghold Holding(string id, bool rebel, string clan = "",
        bool safe = true) => new() { SettlementId = id, IsRebel = rebel, OwnerClanId = clan, IsSafe = safe };

    private static EscapePolicy.Stronghold[] Holdings() =>
        [Holding("loyalist_town", false, "loyalist_clan"), Holding("rebel_castle", true, "rebel_clan")];

    [Fact]
    public void Every_threatened_rebel_family_member_escapes_without_a_chance_roll()
    {
        var family = new[] { "father", "mother", "adult_child", "minor_child", "infant" }
            .Select(id => Resident(id)).ToArray();
        var plan = EscapePolicy.Plan("outbreak", family, Holdings(), loyalistEscapePercent: 0);
        Assert.Equal(family.Length, plan.Count);
        Assert.All(plan, escape =>
        {
            Assert.True(escape.Escapes);
            Assert.Equal(0, escape.Roll);
            Assert.Equal("rebel_castle", escape.DestinationSettlementId);
        });
    }

    [Fact]
    public void Exact_six_screenshot_residents_receive_rebel_sanctuary()
    {
        var family = new[] { "reign_court_town_V5_child_2", "reign_court_town_V5_child_1",
            "reign_court_town_V5_mother", "reign_court_town_V3_child_2",
            "reign_court_town_V3_child_1", "reign_court_castle_V7_child_3" }
            .Select(id => Resident(id, origin: "town_V2")).ToArray();
        var plan = EscapePolicy.Plan("reb_50eff71c66b444c2b9809826567e5d4c", family,
            [Holding("town_V2", false), Holding("castle_rebel", true)]);
        Assert.Equal(6, plan.Count);
        Assert.All(plan, escape => Assert.True(escape.Escapes && escape.DestinationSettlementId == "castle_rebel"));
    }

    [Fact]
    public void Loyalists_use_the_exact_25_percent_boundary_and_failed_rolls_remain_for_native_capture()
    {
        Assert.Equal(25, EscapePolicy.LoyalistEscapePercent);
        // Find identities with every possible roll, then exercise production Plan.
        var ids = Enumerable.Range(0, 10000).Select(i => "loyalist_" + i)
            .GroupBy(id => EscapePolicy.LoyalistRoll("outbreak", id))
            .ToDictionary(group => group.Key, group => group.First());
        Assert.Equal(100, ids.Count);
        var plan = EscapePolicy.Plan("outbreak", ids.Values.Select(id => Resident(id, false, "rebel_castle")), Holdings());
        Assert.Equal(25, plan.Count(escape => escape.Escapes));
        Assert.All(plan.Where(escape => escape.Roll <= 25), escape =>
        {
            Assert.True(escape.Escapes);
            Assert.Equal("loyalist_town", escape.DestinationSettlementId);
        });
        Assert.All(plan.Where(escape => escape.Roll >= 26), escape => Assert.False(escape.Escapes));
    }

    [Theory]
    [InlineData("prisoner")]
    [InlineData("dead")]
    [InlineData("party")]
    [InlineData("player")]
    [InlineData("foreign")]
    [InlineData("civilian")]
    [InlineData("no_clan")]
    [InlineData("no_settlement")]
    [InlineData("village")]
    public void Existing_captives_mobile_parties_player_foreigners_and_non_nobles_are_untouched(string exclusion)
    {
        var resident = Resident();
        switch (exclusion)
        {
            case "prisoner": resident.IsPrisoner = true; break;
            case "dead": resident.IsAlive = false; break;
            case "party": resident.HasParty = true; break;
            case "player": resident.IsPlayer = true; break;
            case "foreign": resident.IsParticipant = false; break;
            case "civilian": resident.IsNoble = false; break;
            case "no_clan": resident.ClanId = string.Empty; break;
            case "no_settlement": resident.SettlementId = string.Empty; break;
            case "village": resident.SettlementId = "unlisted_village"; break;
        }
        Assert.Empty(EscapePolicy.Plan("outbreak", [resident], Holdings()));
    }

    [Theory]
    [InlineData(true, "rebel_castle")]
    [InlineData(false, "loyalist_town")]
    public void Nobles_already_in_same_side_holdings_are_not_relocated(bool rebel, string origin)
        => Assert.Empty(EscapePolicy.Plan("outbreak", [Resident(rebel: rebel, origin: origin)], Holdings()));

    [Fact]
    public void Safe_home_then_clan_holding_is_preferred_over_another_rebel_holding()
    {
        var resident = Resident();
        var holdings = new[] { Holding("loyalist_town", false), Holding("a_other", true, "other_clan"),
            Holding("z_clan", true, "rebel_clan"), Holding("safe_home", true, "other_clan") };
        Assert.Equal("z_clan", Assert.Single(EscapePolicy.Plan("outbreak", [resident], holdings)).DestinationSettlementId);
        resident.HomeSettlementId = "safe_home";
        Assert.Equal("safe_home", Assert.Single(EscapePolicy.Plan("outbreak", [resident], holdings)).DestinationSettlementId);
    }

    [Fact]
    public void Besieged_or_active_battle_destinations_are_excluded_even_when_they_are_home()
    {
        var resident = Resident();
        resident.HomeSettlementId = "rebel_castle";
        var plan = EscapePolicy.Plan("outbreak", [resident],
            [Holding("loyalist_town", false), Holding("rebel_castle", true, "rebel_clan", safe: false), Holding("safe_rebel", true)]);
        Assert.Equal("safe_rebel", Assert.Single(plan).DestinationSettlementId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Successful_escape_goes_into_hiding_if_the_side_has_no_safe_stronghold(bool rebel)
    {
        string origin = rebel ? "loyalist_town" : "rebel_castle";
        var plan = EscapePolicy.Plan("outbreak", [Resident(rebel: rebel, origin: origin)],
            [Holding(origin, !rebel)], loyalistEscapePercent: 100);
        var escape = Assert.Single(plan);
        Assert.True(escape.Escapes);
        Assert.Empty(escape.DestinationSettlementId);
    }

    [Fact]
    public void Replay_input_order_and_duplicate_residents_cannot_reroll_or_repeat_an_escape()
    {
        var residents = Enumerable.Range(0, 40).Select(i => Resident("loyalist_" + i, false, "rebel_castle")).ToArray();
        var first = EscapePolicy.Plan("outbreak", residents, Holdings());
        var retry = EscapePolicy.Plan("outbreak", residents.Reverse().Concat(residents), Holdings().Reverse());
        Assert.Equal(first.Select(x => (x.HeroId, x.Roll, x.Escapes, x.DestinationSettlementId)),
            retry.Select(x => (x.HeroId, x.Roll, x.Escapes, x.DestinationSettlementId)));
        foreach (var escape in first.Where(x => x.Escapes))
            residents.Single(x => x.HeroId == escape.HeroId).SettlementId = escape.DestinationSettlementId;
        Assert.All(EscapePolicy.Plan("outbreak", residents, Holdings()), escape => Assert.False(escape.Escapes));
    }

    [Fact]
    public void Escape_chance_is_bound_to_both_the_outbreak_and_individual_noble()
    {
        var ids = Enumerable.Range(0, 40).Select(i => "noble_" + i).ToArray();
        Assert.True(ids.Select(id => EscapePolicy.LoyalistRoll("outbreak_a", id)).Distinct().Count() > 1);
        Assert.Contains(ids, id => EscapePolicy.LoyalistRoll("outbreak_a", id) != EscapePolicy.LoyalistRoll("outbreak_b", id));
        Assert.All(ids, id => Assert.InRange(EscapePolicy.LoyalistRoll("outbreak_a", id), 1, 100));
    }

    [Fact]
    public void Catalog_binds_the_production_chance_and_preserves_the_native_acceptance_gap()
    {
        string root = TestOptions.FindWorkspace();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var contract = document.RootElement.GetProperty("rebellion").GetProperty("residentEscape");
        Assert.Equal(EscapePolicy.LoyalistEscapePercent, contract.GetProperty("loyalistEscapePercent").GetInt32());
        Assert.True(contract.GetProperty("rebelsGuaranteed").GetBoolean());
        Assert.True(File.Exists(Path.Combine(root, contract.GetProperty("contractTests").GetString()!)));
        Assert.Contains("pending", contract.GetProperty("acceptanceGate").GetString());
        Assert.Contains("disposable Current", contract.GetProperty("nativeEvidence").GetString());
    }

    [Fact]
    public void Native_outbreak_evacuation_precedes_any_faction_change_or_war_callback()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignRebellionCampaignBehavior.cs", "/src/Modules/Rebellion/Campaign/"));
        string start = source[source.IndexOf("private void StartCivilWar", StringComparison.Ordinal)..];
        start = start[..start.IndexOf("private bool EvacuateResidentsBeforeOutbreak", StringComparison.Ordinal)];
        int evacuation = start.IndexOf("EvacuateResidentsBeforeOutbreak(movement, parent)", StringComparison.Ordinal);
        Assert.True(evacuation > 0 && evacuation < start.IndexOf("CreateRebelKingdom(movement)", StringComparison.Ordinal));
        Assert.True(evacuation < start.IndexOf("MoveClanToKingdom(clan, rebels)", StringComparison.Ordinal));
        Assert.True(evacuation < start.IndexOf("DeclareWarWithOrigin", StringComparison.Ordinal));
    }
}
