using Reign.Core.Contracts.Economy;

namespace Reign.Mcp.Tests;

public sealed class ReignWarEconomyOptionsTests
{
    private static WarEconomyOptions Options(params (string Key, object Value)[] changes)
    {
        var values = WarEconomyOptions.DefaultValues();
        foreach (var (key, value) in changes) values[key] = value;
        Assert.True(WarEconomyOptions.TryCreate(values, true, out var options, out var error), error);
        return options;
    }
    [Fact]
    public void ExactFieldScheduleMercenaryPremiumNativeOffAndTierClamps()
    {
        var options = WarEconomyOptions.Defaults;
        int[] wages = [3, 6, 12, 18, 24, 40, 68, 92];
        for (int tier = 0; tier < wages.Length; tier++)
        {
            Assert.Equal(wages[tier], options.TroopWage(tier, false, 123));
            Assert.Equal((int)(wages[tier] * 1.5), options.TroopWage(tier, true, 123));
        }
        Assert.Equal(3, options.TroopWage(-1, false, 0));
        Assert.Equal(92, options.TroopWage(20, false, 0));
        var off = Options(("economyTroopWagesEnabled", false));
        Assert.Equal(0, off.TroopWage(7, true, 0));
        Assert.Equal(17, off.TroopWage(6, false, 17));
        Assert.Equal(40, off.Number("economyTroopWageTier5"));
        Assert.Equal(.75, off.GarrisonWageFactor);
    }
    [Fact]
    public void GroupsSwitchIndependentlyAndRetainEnteredValues()
    {
        var off = Options(("economyRecruitPricesEnabled", false), ("economyVillageCurveEnabled", false),
            ("economyGarrisonWagesEnabled", false), ("economyGarrisonFoodEnabled", false), ("economyReservesEnabled", false));
        Assert.Equal(1, off.RecruitMultiplier(5));
        Assert.Equal(8.25, off.Number("economyRecruitTier5"));
        Assert.Equal(12, off.HealthyFood(300, 12));
        Assert.Equal(1, off.GarrisonWageFactor);
        Assert.Equal(20, off.GarrisonMenPerFood);
        Assert.Equal(0, off.ReserveDays(false));
        Assert.Equal(0, off.ReserveDays(true));
        Assert.Equal(14, off.Number("economyUpgradeReserveDays"));
        Assert.Equal(40, off.TroopWage(5, false, 12));
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(10, 2)] [InlineData(300, 11)] [InlineData(600, 16.5)] [InlineData(1200, 22)] [InlineData(1800, 27.5)]
    public void ConfigurableCurvePreservesAnchorsAndZero(double hearth, double expected)
    {
        Assert.Equal(expected, WarEconomyOptions.Defaults.HealthyFood(hearth, 999), 6);
        Assert.Equal(expected * 1.5, Options(("economyVillageOutputScale", 1.5)).HealthyFood(hearth, 999), 6);
    }
    [Theory]
    [InlineData(10)] [InlineData(200)] [InlineData(300)] [InlineData(600)] [InlineData(1200)]
    public void EnabledCurveIsContinuousAtEveryBoundary(double hearth)
    {
        var options = WarEconomyOptions.Defaults;
        Assert.InRange(Math.Abs(options.HealthyFood(hearth + .000001, 12) - options.HealthyFood(hearth - .000001, 12)), 0, .000001);
    }
    [Theory]
    [InlineData("economyTroopWagesEnabled", "false")]
    [InlineData("economyTroopWageTier5", "40")]
    [InlineData("economyTroopWageTier5", 40.5)]
    [InlineData("economyTroopWageTier5", -1)]
    [InlineData("economyRecruitTier2", 3.61)]
    [InlineData("economyVillageFood600", 5)]
    [InlineData("economyVillageFood1200", 200)]
    [InlineData("economyInvented", true)]
    public void InvalidValuesAndMergedCurvesAreRejected(string key, object value)
    {
        var values = WarEconomyOptions.DefaultValues(); values[key] = value;
        Assert.False(WarEconomyOptions.TryCreate(values, true, out _, out _));
    }
    [Fact]
    public void NonfiniteIncompleteAndBooleanNumbersFailEvenWhenDisabled()
    {
        foreach (object invalid in new object[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, true, "", new object() })
        {
            var values = WarEconomyOptions.DefaultValues();
            values["economyTroopWagesEnabled"] = false; values["economyTroopWageTier5"] = invalid;
            Assert.False(WarEconomyOptions.TryCreate(values, true, out _, out _));
        }
        Assert.False(WarEconomyOptions.TryCreate(new Dictionary<string, object>(), true, out _, out _));
        Assert.True(WarEconomyOptions.TryCreate(new Dictionary<string, object>(), false, out var legacy, out _));
        var copy = legacy.Values(); copy["economyTroopWageTier5"] = 500d;
        Assert.Equal(40, legacy.TroopWage(5, false, 0));
    }
    [Fact]
    public void AcceptedFloatingPointNoiseCanonicalizesBeforeIntegerWageCasts()
    {
        var options = Options(("economyTroopWageTier5", 39.999999999), ("economyVillageOutputScale", (float)1.5));
        Assert.Equal(40, options.TroopWage(5, false, 0));
        Assert.Equal(1.5, options.Number("economyVillageOutputScale"));
        var values = WarEconomyOptions.DefaultValues();values["EconomyTroopWagesEnabled"] = false;
        Assert.False(WarEconomyOptions.TryCreate(values, true, out _, out _));
    }
    [Fact]
    public void CompleteStackBudgetDoesNotMultiplyPerUnitQuarteringRounding()
    {
        int count = ReignBeta.Economy.ReignWarEconomyPolicy.AffordableCountExact(20, 15, 15, 0, 0, 0, 1,
            recruits => Math.Round(3 * recruits * .5));
        Assert.Equal(10, count);
        Assert.Equal(15, Math.Round(3 * count * .5));
        Assert.Equal(20, count * Math.Round(3 * .5));
    }
    [Fact]
    public void PublishedTestRouteAndNativeUiCoverageMatchTheSettingsContract()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var options = catalog.RootElement.GetProperty("warEconomy").GetProperty("options");
        Assert.Equal(WarEconomyOptions.Version, options.GetProperty("version").GetInt32());
        Assert.Equal(new[] { 3, 6, 12, 18, 24, 40, 68, 92 }, options.GetProperty("fieldWageDefaults").EnumerateArray().Select(x => x.GetInt32()));
        Assert.Contains("war_economy_options", options.GetProperty("offlineSuite").GetString());
        Assert.Equal(3, options.GetProperty("nativeUiQuotes").GetArrayLength());
        string lab = File.ReadAllText(Path.Combine(root, "ReignServer/src/Modules/Platform/VerificationLab.cs"));
        Assert.Contains("ShouldRunVerificationSuite(requestedSuite, \"war_economy_options\")", lab);
        Assert.Contains("RunWarEconomyOptionsSelfTests()", lab);
    }
}
