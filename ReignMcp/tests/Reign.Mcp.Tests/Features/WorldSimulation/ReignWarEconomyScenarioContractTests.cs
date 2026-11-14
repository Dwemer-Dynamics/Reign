using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignWarEconomyScenarioContractTests
{
    [Fact]
    public async Task NativeFixtureCannotDispatchWithoutVerificationControl()
    {
        var options = TestOptions.Create() with { AllowVerificationControl = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.StartWarEconomyTest(null!, options, null!,
            "campaign", "run", confirmation: "start Reign war economy test on disposable save"));
    }

    [Theory]
    [InlineData("reset_treasury", "")]
    [InlineData("advance", "")]
    [InlineData("recruitment", "unknown_free_troops")]
    [InlineData("funding_probe", "reset_treasury")]
    [InlineData("funding_trace", "advance")]
    [InlineData("market_food_probe", "gold_barter")]
    [InlineData("household_probe", "gold_barter")]
    [InlineData("civilian_crew_probe", "gold_barter")]
    [InlineData("raid_setup", "gold_barter")]
    [InlineData("raid_food_probe", "gold_barter")]
    [InlineData("raid_partial_setup", "gold_barter")]
    [InlineData("readiness_probe", "gold_barter")]
    [InlineData("replacement_setup", "gold_barter")]
    [InlineData("cohort_control", "")]
    [InlineData("cohort_control", "reset_treasury")]
    public async Task ClosedProfilesAndSourcesRejectBeforeAnyRuntimeCall(string profile, string source)
    {
        var options = TestOptions.Create() with { AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartWarEconomyTest(null!, options, null!,
            "campaign", "run", profile: profile, partyId: "party", settlementId: "village", source: source,
            confirmation: "start Reign war economy test on disposable save"));
    }

    [Theory]
    [InlineData("production_probe", "")]
    [InlineData("relief_probe", "")]
    [InlineData("production_probe", "../foreign")]
    [InlineData("relief_probe", "town;reset")]
    [InlineData("funding_probe", "")]
    [InlineData("market_food_probe", "")]
    [InlineData("market_food_probe", "town;reset")]
    [InlineData("household_probe", "")]
    [InlineData("household_probe", "../foreign")]
    [InlineData("civilian_crew_probe", "")]
    [InlineData("civilian_crew_probe", "../foreign")]
    [InlineData("ship_funding_probe", "")]
    [InlineData("ship_funding_probe", "../foreign")]
    [InlineData("raid_setup", "")]
    [InlineData("raid_setup", "../foreign")]
    [InlineData("raid_food_probe", "")]
    [InlineData("raid_partial_setup", "../foreign")]
    public async Task ControlledProbesRejectMissingOrForeignIdentifiersBeforeAuthorization(string profile, string locality)
    {
        var options = TestOptions.Create() with { AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartWarEconomyTest(null!, options, null!,
            "campaign", "run", profile: profile, settlementId: locality,
            confirmation: "start Reign war economy test on disposable save"));
    }
    [Theory]
    [InlineData(-1)] [InlineData(1)] [InlineData(5)] [InlineData(41)] [InlineData(100)]
    public async Task PartialFoodScenariosRejectNoncatalogDamageBeforeRuntime(int damage)
    {
        var options=TestOptions.Create() with {AllowVerificationControl=true};
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>TestingTools.StartWarEconomyTest(null!,options,null!,
            "campaign","run",profile:"raid_partial_setup",partyId:"real_party",settlementId:"real_village",damagePercent:damage,
            confirmation:"start Reign war economy test on disposable save"));
    }
    [Fact]
    public void FoodFixtureManifestKeepsNativeAndHumanAcceptancePending()
    {
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignServer/tests/ReignLiveTest/Features/WorldSimulation/war-economy-manifest.json")));
        var food=manifest.RootElement.GetProperty("raidFood");
        Assert.Equal(new[]{0,4,6,10,24,25,40},food.GetProperty("damagePercent").EnumerateArray().Select(x=>x.GetInt32()));
        Assert.Equal(1,food.GetProperty("temporaryFishingParties").GetInt32());
        Assert.True(food.GetProperty("restoreCurrentAfterBranch").GetBoolean());
        Assert.False(food.GetProperty("nativeCombatAcceptance").GetBoolean());
        Assert.False(food.GetProperty("liveFishingAcceptance").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../foreign")]
    [InlineData("party;reset")]
    public async Task ReplacementScenarioRejectsMissingOrForeignPartyBeforeAuthorization(string party)
    {
        var options = TestOptions.Create() with { AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartWarEconomyTest(null!, options, null!,
            "campaign", "run", profile: "replacement_setup", partyId: party,
            confirmation: "start Reign war economy test on disposable save"));
    }

    [Fact]
    public void CatalogManifestAndProcedureDeclareTheSameLimitsAndIncompleteAcceptance()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var economy = catalog.RootElement.GetProperty("warEconomy");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, economy.GetProperty("manifest").GetString()!)));
        Assert.Equal(6, economy.GetProperty("walletOwnershipObservation").GetProperty("fundingTraceVersion").GetInt32());
        Assert.Equal(1, economy.GetProperty("walletOwnershipObservation").GetProperty("version").GetInt32());
        Assert.True(economy.GetProperty("walletOwnershipObservation").GetProperty("unknownEntryBlocksOriginAcceptance").GetBoolean());
        Assert.Equal(100, economy.GetProperty("replacementSetup").GetProperty("maximumRegularLoss").GetInt32());
        Assert.False(economy.GetProperty("replacementSetup").GetProperty("simulatesFullNativeBattle").GetBoolean());
        Assert.True(economy.GetProperty("replacementSetup").GetProperty("restoreAfterBranch").GetBoolean());
        Assert.Equal(.01, economy.GetProperty("navalFunding").GetProperty("npcPurchaseFactor").GetDouble());
        Assert.Equal(.01, economy.GetProperty("navalFunding").GetProperty("purchasedNpcResaleFactor").GetDouble());
        Assert.Equal(15, economy.GetProperty("navalFunding").GetProperty("hookCount").GetInt32());
        Assert.Equal(3, economy.GetProperty("tariffCollection").GetProperty("version").GetInt32());
        Assert.Equal("same_realm_native_food_and_horse_provisioning", economy.GetProperty("tariffCollection").GetProperty("rule").GetString());
        Assert.Equal(128, economy.GetProperty("tariffCollection").GetProperty("historyLimits").GetProperty("recent").GetInt32());
        Assert.Equal(2, economy.GetProperty("fiscalObservation").GetProperty("version").GetInt32());
        Assert.Equal(1, economy.GetProperty("financeSnapshot").GetProperty("version").GetInt32());
        Assert.Equal(1, economy.GetProperty("raidSetup").GetProperty("maximumVillages").GetInt32());
        Assert.False(economy.GetProperty("raidSetup").GetProperty("simulatesCombat").GetBoolean());
        Assert.Equal(.75, economy.GetProperty("militaryReadiness").GetProperty("garrisonWageFactor").GetDouble());
        Assert.Equal(25, economy.GetProperty("militaryReadiness").GetProperty("garrisonMenPerFood").GetInt32());
        foreach (var description in new[] { economy, manifest.RootElement })
        {
            var drawdown = description.GetProperty("militaryReadiness").GetProperty("drawdown");
            Assert.Equal(2, drawdown.GetProperty("allocationVersion").GetInt32());
            Assert.Equal("proportional_healthy_regulars_with_fractional_carry", drawdown.GetProperty("tierAllocation").GetString());
            Assert.Equal(10, drawdown.GetProperty("dailyMaximum").GetInt32());
            Assert.True(drawdown.GetProperty("noReplayOfUncertainOperations").GetBoolean());
        }
        Assert.Contains("native_naval_garrison", economy.GetProperty("sources").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(20, economy.GetProperty("navalRecruitment").GetProperty("maxNativeIncrement").GetInt32());
        Assert.True(economy.GetProperty("navalRecruitment").GetProperty("fullCurrentRestoreAfterProbe").GetBoolean());
        foreach (string section in new[] { "navalFunding", "criminalPressureAcceptance", "balanceRevision", "walletOwnershipObservation", "replacementSetup" })
            Assert.Equal(economy.GetProperty(section).GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""),
                manifest.RootElement.GetProperty(section).GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        Assert.Equal(economy.GetProperty("tariffCollection").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""),
            manifest.RootElement.GetProperty("tariffCollection").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        Assert.Equal(economy.GetProperty("navalRecruitment").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""),
            manifest.RootElement.GetProperty("navalRecruitment").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        Assert.Equal(economy.GetProperty("profiles").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""),
            manifest.RootElement.GetProperty("profiles").GetRawText().Replace(" ", "").Replace("\r", "").Replace("\n", ""));
        foreach (string field in new[] { "modelRoutes", "observedNonEnlistment", "observedNativeFinance", "observedNativeFood", "fundingProbeSources", "fundingTraceControls", "readOnlyBalanceCensus" })
            Assert.Equal(economy.GetProperty(field).EnumerateArray().Select(x => x.GetString()).ToArray(),
                manifest.RootElement.GetProperty(field).EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(20, manifest.RootElement.GetProperty("limits").GetProperty("recruitmentFixtureMaxNativeIncrement").GetInt32());
        Assert.Equal(64, manifest.RootElement.GetProperty("limits").GetProperty("controlledReliefMaxHoldings").GetInt32());
        Assert.Equal(19, manifest.RootElement.GetProperty("limits").GetProperty("productionProbeHearths").GetArrayLength());
        string productionSource = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Economy/ReignWarEconomyTestHost.cs"));
        var productionInputs = System.Text.RegularExpressions.Regex.Match(productionSource,
            @"foreach \(float hearth in new\[\] \{([^}]+)\}");
        Assert.True(productionInputs.Success);
        double[] sourceHearths = productionInputs.Groups[1].Value.Split(',')
            .Select(value => double.Parse(value.Trim().TrimEnd('f'), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(sourceHearths, manifest.RootElement.GetProperty("limits").GetProperty("productionProbeHearths")
            .EnumerateArray().Select(value => value.GetDouble()).ToArray());
        var balance = economy.GetProperty("balanceRevision");
        Assert.Equal(3, balance.GetProperty("productionCurveVersion").GetInt32());
        Assert.Equal(3, balance.GetProperty("recruitmentPriceVersion").GetInt32());
        Assert.Equal(ReignBeta.Economy.ReignWarEconomyPolicy.ProductionCurveVersion,
            balance.GetProperty("productionCurveVersion").GetInt32());
        Assert.Equal(ReignBeta.Economy.ReignWarEconomyPolicy.RecruitmentPriceVersion,
            balance.GetProperty("recruitmentPriceVersion").GetInt32());
        Assert.Equal(2, balance.GetProperty("healthyOutput").GetProperty("anchors").GetProperty("10").GetDouble());
        Assert.Equal(11, balance.GetProperty("healthyOutput").GetProperty("anchors").GetProperty("300").GetDouble());
        Assert.Equal(new[] { 3d, 3.6, 4.5, 6, 8.25, 10.5 }, balance.GetProperty("hireMultipliers").EnumerateArray().Select(x => x.GetDouble()));
        Assert.False(balance.GetProperty("nativeAcceptance").GetBoolean());
        Assert.Equal(3, manifest.RootElement.GetProperty("limits").GetProperty("productionProbeRecovery").GetArrayLength());
        Assert.Equal(8192, manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceRows").GetInt32());
        Assert.Equal(6, manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceVersion").GetInt32());
        Assert.Equal(economy.GetProperty("fundingOriginObservation").GetProperty("fundingTraceVersion").GetInt32(),
            manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceVersion").GetInt32());
        Assert.Equal(32768, manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceWallets").GetInt32());
        Assert.Equal(economy.GetProperty("fundingOriginObservation").GetProperty("walletLimit").GetInt32(),
            manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceWallets").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("limits").GetProperty("consumptionCashObserverVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("limits").GetProperty("balanceCensusVersion").GetInt32());
        Assert.Equal(1, manifest.RootElement.GetProperty("limits").GetProperty("nativeMarketSalesVersion").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("limits").GetProperty("horseSaleNativeCallSites").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("limits").GetProperty("horseProbeMaxMounts").GetInt32());
        Assert.Contains("horse_sale", economy.GetProperty("fundingProbeSources").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("market_purchase", economy.GetProperty("fundingProbeSources").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(10, manifest.RootElement.GetProperty("limits").GetProperty("purchaseProbeNativeCalls").GetInt32());
        Assert.Equal(100000, manifest.RootElement.GetProperty("limits").GetProperty("purchaseProbeMaxPayerCash").GetInt32());
        Assert.Equal(63, economy.GetProperty("balanceTargets").GetProperty("wellSuppliedWarResilienceDays").GetInt32());
        Assert.False(economy.GetProperty("balanceTargets").GetProperty("requiresForcedStarvation").GetBoolean());
        Assert.Equal(256, economy.GetProperty("nativeFoodCensus").GetProperty("maximumSettlements").GetInt32());
        Assert.Equal(2, economy.GetProperty("nativeFoodCensus").GetProperty("version").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("nativeFoodCensus").GetProperty("version").GetInt32());
        Assert.Equal(8, manifest.RootElement.GetProperty("limits").GetProperty("foodProbeCases").GetInt32());
        Assert.Equal(150, manifest.RootElement.GetProperty("limits").GetProperty("foodProbeMaxFish").GetInt32());
        foreach (var description in new[] { economy.GetProperty("subsistencePriority"), manifest.RootElement.GetProperty("subsistencePriority") })
        {
            Assert.Equal(3, description.GetProperty("version").GetInt32());
            Assert.Equal(7, description.GetProperty("forecastDays").GetInt32());
            Assert.Equal(.2, description.GetProperty("reserveRatio").GetDouble());
            Assert.Equal("original_native_budget_and_available_inventory", description.GetProperty("forecastBasis").GetString());
            Assert.False(description.GetProperty("usesPreviousSellLog").GetBoolean());
            Assert.False(description.GetProperty("createsPurchasingPower").GetBoolean());
        }
        Assert.Equal(2, manifest.RootElement.GetProperty("limits").GetProperty("reliefProbeNativeAdapterPasses").GetInt32());
        Assert.Equal(100000, manifest.RootElement.GetProperty("limits").GetProperty("reliefProbeMaxTotalCapacity").GetInt32());
        foreach (var description in new[] { economy.GetProperty("reliefAllocation"), manifest.RootElement.GetProperty("reliefAllocation") })
        {
            Assert.Equal(2, description.GetProperty("version").GetInt32());
            Assert.Equal(30, description.GetProperty("donorMaximumShippedPerDay").GetInt32());
            Assert.Equal(.25, description.GetProperty("recipientMaximumCapacityFraction").GetDouble());
            Assert.Equal("current_stock_not_projected_stock", description.GetProperty("refillUses").GetString());
            Assert.True(description.GetProperty("existingStockOnly").GetBoolean());
            Assert.False(description.GetProperty("samePassReexport").GetBoolean());
        }
        Assert.True(economy.GetProperty("nativeFoodCensus").GetProperty("excludesCivilianFixtures").GetBoolean());
        Assert.Equal(3, manifest.RootElement.GetProperty("limits").GetProperty("fundingTraceNativeDays").GetInt32());
        Assert.Equal(6, manifest.RootElement.GetProperty("limits").GetProperty("fundingProbePrisoners").GetInt32());
        Assert.Equal(40, manifest.RootElement.GetProperty("limits").GetProperty("fundingProbeLootUnits").GetInt32());
        Assert.Equal(450, manifest.RootElement.GetProperty("limits").GetProperty("foodProbeMaxGrain").GetInt32());
        Assert.Equal(200, manifest.RootElement.GetProperty("limits").GetProperty("foodProbeMaxNonFood").GetInt32());
        Assert.Equal(16, manifest.RootElement.GetProperty("limits").GetProperty("foodProbeConsumptionCalls").GetInt32());
        Assert.Equal(3, manifest.RootElement.GetProperty("limits").GetProperty("householdProbeParties").GetInt32());
        Assert.Equal(2, manifest.RootElement.GetProperty("limits").GetProperty("householdProbeCancellationTrials").GetInt32());
        const string admissionGate = "invalid economy steps rejected before live run creation";
        Assert.Contains(economy.GetProperty("gates").EnumerateArray(), gate => gate.GetString() == admissionGate);
        Assert.Contains(manifest.RootElement.GetProperty("gates").EnumerateArray(), gate => gate.GetString() == admissionGate);
        Assert.Equal("pending", manifest.RootElement.GetProperty("acceptance").GetProperty("native").GetString());
        foreach (var layer in new[] { "human", "balance", "providerFreeSoak" })
            Assert.Equal("pending", manifest.RootElement.GetProperty("acceptance").GetProperty(layer).GetString());
        var matched = manifest.RootElement.GetProperty("balanceScenarios").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "matched_campaigns");
        Assert.Equal(63, matched.GetProperty("observationDays").GetInt32());
        Assert.Equal(32, matched.GetProperty("historicalInitialObservationDays").GetInt32());
        var wageObservation = economy.GetProperty("wageObservation");
        Assert.Equal(2, wageObservation.GetProperty("version").GetInt32());
        Assert.Equal("current_settlement_owner", wageObservation.GetProperty("garrisonPayer").GetString());
        Assert.False(wageObservation.GetProperty("historicalCoverageComplete").GetBoolean());
        Assert.Equal(0, wageObservation.GetProperty("resourceWrites").GetInt32());
        var components = economy.GetProperty("persistenceComponentFingerprints");
        Assert.Equal("reign-war-economy-persistence-components-v1", components.GetProperty("schema").GetString());
        Assert.Equal("reign-war-economy-persistence-v3", components.GetProperty("unchangedPrimaryDigest").GetString());
        Assert.True(components.GetProperty("arrayOrderPreserved").GetBoolean());
        Assert.False(components.GetProperty("privateValuesExported").GetBoolean());
        Assert.True(File.Exists(Path.Combine(root, economy.GetProperty("guide").GetString()!)));
        Assert.Equal(126, manifest.RootElement.GetProperty("balanceScenarios").EnumerateArray()
            .Single(x => x.GetProperty("name").GetString() == "provider_free_stability").GetProperty("days").GetInt32());
    }
}
