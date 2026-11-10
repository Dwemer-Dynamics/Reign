using Reign.Core.Contracts.Economy;

namespace Reign.Mcp.Tests;

public sealed class WarEconomyCohortControlTests
{
    [Theory]
    [InlineData("cohort_preview")]
    [InlineData("cohort_enable")]
    [InlineData("cohort_disable")]
    public async Task CohortControlCannotDispatchWithoutVerificationPermission(string source)
    {
        var options = TestOptions.Create() with { AllowVerificationControl = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reign.Mcp.Server.TestingTools.StartWarEconomyTest(
            null!, options, null!, "campaign", "run", profile: "cohort_control", source: source,
            confirmation: "start Reign war economy test on disposable save"));
    }

    [Fact]
    public void SessionIsolationRemainsEphemeralAndHasIndependentNativeObservations()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Economy/ReignWarEconomyCohortControl.cs"));
        Assert.DoesNotContain("SyncData(", source);
        Assert.DoesNotContain("ChangeHeroGold(", source);
        Assert.DoesNotContain("MakePeaceAction.Apply", source);
        Assert.Contains("NpcRealms()", source);
        Assert.Contains("a.IsAtWarWith(b)", source);
        Assert.Contains("Release(\"cohort_matrix_violation\", true)", source);
        Assert.Contains("DateTime.UtcNow.AddHours(plan.WallHours)", source);
        Assert.Contains("now >= _end", source);
        Assert.Contains("WarEconomyCohortPlan.RetainedSamples", source);
        Assert.Contains("ProbeBlocks.Values.Sum() == hits + 1", source);
        Assert.Contains("JToken.DeepEquals(before, ProbeState())", source);
        Assert.Contains("GuardProbes.Count == _expectedProbes", source);
        Assert.Contains("typeof(MakePeaceAction.MakePeaceDetail)", source);
        Assert.Contains("\"SetNeutral\"", source);
        Assert.Contains("unexercisedHooks", source);
        Assert.Contains("samplesEvicted", source);
        Assert.Contains("_harmony.Unpatch(target, HarmonyPatchType.All, Owner)", source);
    }

    private static readonly string[] Realms = { "north", "khuzait", "west", "battania", "south", "aserai" };
    private static readonly string[] Wars = { WarEconomyCohortPlan.Pair("north", "khuzait"), WarEconomyCohortPlan.Pair("west", "battania") };

    [Fact]
    public void DefaultCapturesBothSidesOfWarsAndPeaceControlsWithoutTouchingThirdParties()
    {
        var scope = new WarEconomyCohortPlan().Resolve(Realms, Wars);
        Assert.Equal(new[] { "aserai", "south" }, scope.Peace);
        Assert.True(scope.BlocksPeace("khuzait", "north"));
        Assert.True(scope.BlocksPeace("battania", "west"));
        Assert.True(scope.BlocksWar("south", "north"));
        Assert.True(scope.BlocksWar("north", "west"));
        Assert.False(scope.BlocksWar("north", "khuzait"));
        Assert.False(scope.BlocksWar("player", "south"));
        Assert.False(scope.BlocksPeace("north", "pirates"));
        Assert.Empty(scope.Violations(Realms, Wars));
        Assert.Contains("locked_war_missing:khuzait:north", scope.Violations(Realms, Wars.Skip(1)));
        Assert.Contains("unexpected_war:north:south", scope.Violations(Realms, Wars.Concat(new[] { "north:south" })));
    }

    [Fact]
    public void ExplicitSubsetAndNaturalControlsRemainSelectable()
    {
        var scope = new WarEconomyCohortPlan(blockOtherWars: false, suppressRebellions: false,
            peaceKingdomIds: "south", warPairs: "north:khuzait").Resolve(Realms, Wars);
        Assert.True(scope.BlocksPeace("north", "khuzait"));
        Assert.False(scope.BlocksPeace("west", "battania"));
        Assert.True(scope.BlocksWar("south", "aserai"));
        Assert.False(scope.BlocksWar("north", "aserai"));
        var natural = new WarEconomyCohortPlan(blockOtherWars: false, peaceKingdomIds: "none", warPairs: "none").Resolve(Realms, Wars);
        Assert.False(natural.BlocksWar("north", "aserai"));
        Assert.False(natural.BlocksPeace("north", "khuzait"));
    }

    [Fact]
    public void AllPeaceAllWarAndLongRunsDoNotRequireAMixedCohort()
    {
        var allPeace = new WarEconomyCohortPlan(100).Resolve(Realms, Array.Empty<string>());
        Assert.Equal(Realms.Length, allPeace.Peace.Count);
        Assert.Empty(allPeace.Wars);
        Assert.True(allPeace.BlocksWar("north", "west"));
        var allWar = new WarEconomyCohortPlan(6300, 12, 7, peaceKingdomIds: "none").Resolve(new[] { "north", "khuzait" }, new[] { Wars[0] });
        Assert.Empty(allWar.Peace);
        Assert.True(allWar.BlocksPeace("north", "khuzait"));
    }

    [Fact]
    public void PreviewDetectsWrongPairingsBeforeActivation()
    {
        var scope = new WarEconomyCohortPlan(peaceKingdomIds: "south", warPairs: "west:khuzait").Resolve(Realms, Wars);
        Assert.Equal(new[] { "locked_war_missing:khuzait:west" }, scope.Violations(Realms, Wars));
        Assert.Contains("peace_kingdom_missing:south", scope.Violations(Realms.Except(new[] { "south" }), Wars));
    }

    [Fact]
    public void NewKingdomsCannotHideWarsAgainstControls()
    {
        var allowedRebels = new WarEconomyCohortPlan(blockOtherWars: false, suppressRebellions: false,
            peaceKingdomIds: "south", warPairs: "auto").Resolve(Realms, Wars);
        Assert.Contains("unexpected_war:rebel:south", allowedRebels.Violations(Realms.Append("rebel"), Wars.Append("south:rebel")));
        var fixedPairs = new WarEconomyCohortPlan(suppressRebellions: false).Resolve(Realms, Wars);
        Assert.Contains("unexpected_war:north:rebel", fixedPairs.Violations(Realms.Append("rebel"), Wars.Append("rebel:north")));
        var noRebels = new WarEconomyCohortPlan().Resolve(Realms, Wars);
        Assert.Contains("unexpected_kingdom:rebel", noRebels.Violations(Realms.Append("rebel"), Wars));
    }

    [Fact]
    public void ObservedPairDirectionIsCanonicalAtBothBoundaries()
    {
        var scope = new WarEconomyCohortPlan().Resolve(Realms, new[] { "north:khuzait", "west:battania" });
        Assert.Empty(scope.Violations(Realms, new[] { "khuzait:north", "battania:west" }));
    }

    [Theory]
    [InlineData("north")] [InlineData("north:west:south")] [InlineData("north:north")]
    public void MalformedObservationsFailClearly(string row)
    {
        Assert.Throws<ArgumentException>(() => new WarEconomyCohortPlan().Resolve(Realms, new[] { row }));
        var scope = new WarEconomyCohortPlan().Resolve(Realms, Wars);
        Assert.Throws<ArgumentException>(() => scope.Violations(Realms, new[] { row }));
    }

    [Theory]
    [InlineData(0, 6, 1)] [InlineData(6301, 6, 1)] [InlineData(double.NaN, 6, 1)]
    [InlineData(double.PositiveInfinity, 6, 1)] [InlineData(100, 0, 1)] [InlineData(100, 13, 1)]
    [InlineData(100, 6, 0)] [InlineData(100, 6, 8)]
    public void InvalidBoundsFailClosed(double days, double hours, double interval)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new WarEconomyCohortPlan(days, hours, interval));

    [Theory]
    [InlineData("north,north", "auto")] [InlineData("auto", "north:khuzait,khuzait:north")]
    [InlineData("auto", "north:north")] [InlineData("auto", "north")]
    [InlineData("north\nwest", "auto")] [InlineData("", "auto")]
    public void AmbiguousSelectionsFailClosed(string peace, string wars)
    {
        Assert.Throws<ArgumentException>(() => new WarEconomyCohortPlan(peaceKingdomIds: peace, warPairs: wars));
    }

    [Theory]
    [InlineData("player", "auto")] [InlineData("missing", "auto")]
    [InlineData("north", "north:khuzait")] [InlineData("none", "west:missing")]
    public void UnknownPlayerAndContradictoryControlsAreRejected(string peace, string wars)
        => Assert.Throws<ArgumentException>(() => new WarEconomyCohortPlan(peaceKingdomIds: peace, warPairs: wars).Resolve(Realms, Wars));

    [Fact]
    public async Task CustomCohortArgumentsCannotBeSmuggledIntoOtherProfiles()
    {
        var options = TestOptions.Create() with { AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => Reign.Mcp.Server.TestingTools.StartWarEconomyTest(
            null!, options, null!, "campaign", "run", cohortDurationDays: 100,
            confirmation: "start Reign war economy test on disposable save"));
    }
}
