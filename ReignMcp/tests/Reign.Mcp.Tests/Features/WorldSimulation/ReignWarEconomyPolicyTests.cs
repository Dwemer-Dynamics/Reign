using ReignBeta.Economy;

namespace Reign.Mcp.Tests;

public sealed class ReignWarEconomyPolicyTests
{
    [Theory]
    [InlineData(59999, 30000, 50, 0, 0, false)]
    [InlineData(60000, 30000, 50, 0, 0, false)]
    [InlineData(60001, 30000, 50, 0, 0, true)]
    [InlineData(39999, 15000, 50, 0, 0, false)]
    [InlineData(40000, 15000, 50, 0, 0, true)]
    [InlineData(46500, 15000, 50, 4500, 1, false)]
    [InlineData(46501, 15000, 50, 4500, 1, true)]
    [InlineData(50000, 12500, 25, 0, 0, false)]
    [InlineData(50001, 12500, 25, 0, 0, true)]
    [InlineData(int.MaxValue, int.MaxValue, 100, 0, 0, false)]
    [InlineData(100000, -1, 50, 0, 0, false)]
    [InlineData(100000, 15000, 101, 0, 0, false)]
    public void CaravanFeeMustBeStrictlyBelowTheConfiguredShareAndPreserveTheMilitaryReserve(
        int cash, int fee, int cashPercent, double wages, int liabilities, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.CanFundCaravan(cash, fee, cashPercent, wages, liabilities));

    [Fact]
    public void LegacyCaravanReceiptsRetainSponsorFundedCapitalWithoutCountingNewSeed()
    {
        var state = Newtonsoft.Json.JsonConvert.DeserializeObject<EconomyCaravanState>(
            "{\"CapitalInvested\":15000,\"ConsideredWeek\":{\"old_clan\":4},\"Recent\":[{\"Capital\":15000}]}");
        Assert.NotNull(state);
        Assert.Equal(15000L, state.CapitalInvested);
        Assert.Equal(0L, state.CapitalSeeded);
        Assert.Empty(state.ConsideredDay);
        var receipt = Assert.Single(state.Recent);
        Assert.True(receipt.CapitalFundedBySponsor);
        Assert.False(receipt.IsConvoy);
    }

    [Theory]
    [InlineData(5000, 12000L, 60000, 3000, 1000, 0, 140)]
    [InlineData(7940, 12000L, 60000, 3000, 1000, 0, 60)]
    [InlineData(8000, 12000L, 60000, 3000, 1000, 0, 0)]
    [InlineData(5000, 15000L, 60000, 3000, 1000, 0, 0)]
    [InlineData(6008, 90907L, 60000, 3000, 1000, 0, 0)]
    [InlineData(5000, 12000L, 25000, 3000, 1000, 0, 0)]
    [InlineData(5000, 12000L, 25070, 3000, 1000, 0, 70)]
    [InlineData(5000, 12000L, 36100, 5000, 1000, 0, 100)]
    [InlineData(5000, 12000L, 60000, 3000, 1000, 280, 0)]
    [InlineData(5000, 12000L, 60000, 3000, 1000, 200, 80)]
    public void HouseholdStipendProtectsBothWalletsAndTheWeeklyCap(int recipientCash, long householdCash,
        int payerCash, double dailyWages, int liability, int paidThisWeek, int expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.HouseholdStipendAmount(
            recipientCash, householdCash, payerCash, dailyWages, liability, paidThisWeek));

    [Fact]
    public void HouseholdStipendWeekAndOwnershipSurviveReloadWithoutBackpay()
    {
        var state = new EconomyHouseholdStipendState();
        Assert.False(state.BeginWeek(100));
        Assert.False(state.BeginWeek(100));
        Assert.True(state.BeginWeek(103));
        Assert.False(state.ObserveOwner("home", "host", 722.5).HasFullWeek(729.49));
        Assert.True(state.ObserveOwner("home", "host", 729.5).HasFullWeek(729.5));
        Assert.False(state.ObserveOwner("home", "conqueror", 729.5).HasFullWeek(729.5));
        state.Record(new EconomyStipendReceipt { Week = 103, Amount = 140, PayerBefore = 50000,
            PayerAfter = 49860, RecipientBefore = 2000, RecipientAfter = 2140 });

        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var restored = System.Text.Json.JsonSerializer.Deserialize<EconomyHouseholdStipendState>(
            System.Text.Json.JsonSerializer.Serialize(state, options), options)!;
        Assert.False(restored.BeginWeek(103));
        Assert.False(restored.BeginWeek(102));
        Assert.True(restored.BeginWeek(104));
        Assert.False(restored.ObserveOwner("home", "conqueror", 734).HasFullWeek(734));
        Assert.True(restored.ObserveOwner("home", "conqueror", 736.5).HasFullWeek(736.5));
        Assert.Equal(140L, restored.Paid);
        var receipt = Assert.Single(restored.Recent);
        Assert.Equal(receipt.Amount, receipt.PayerBefore - receipt.PayerAfter);
        Assert.Equal(receipt.Amount, receipt.RecipientAfter - receipt.RecipientBefore);
    }

    [Theory]
    [InlineData(100, 0, true, 5)]
    [InlineData(100, 0, false, -5)]
    [InlineData(100, 40, true, 3)]
    [InlineData(100, 40, false, -3)]
    [InlineData(75, 0, false, -3.75)]
    [InlineData(100, 100, true, 0)]
    [InlineData(100, 120, false, 0)]
    [InlineData(0, 0, true, 0)]
    public void KingdomWageAdjustmentAppliesOnlyToTheFinalRegularWagePortion(
        double total, double exempt, bool atWar, double expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.KingdomWageDelta(total, exempt, atWar), 6);

    [Theory]
    [InlineData(500, 300, 1000, 0, false, 300)]
    [InlineData(500, 1000, 120, 0, false, 120)]
    [InlineData(500, 1000, 1000, 0, true, 0)]
    [InlineData(500, 0, 1000, 0, false, 0)]
    [InlineData(500, 1000, -20, 0, false, 0)]
    [InlineData(500, 1000, 1000, 2147483640, false, 7)]
    [InlineData(-5, 1000, 1000, 0, false, 0)]
    public void ShipCommissionRequiresOutsidePaidBusinessAndRealPortFunds(int authored, int price, int market, int governor, bool sameClan, int expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.NavalCommission(authored, price, market, governor, sameClan));

    [Fact]
    public void SuccessionInvalidatesThePreviousSovereignsStandingOrder()
    {
        Assert.True(ReignWarEconomyPolicy.StandingOrderApplies("player", "player"));
        Assert.False(ReignWarEconomyPolicy.StandingOrderApplies("npc", "player"));
        Assert.False(ReignWarEconomyPolicy.StandingOrderApplies("player", "npc"));
        Assert.False(ReignWarEconomyPolicy.StandingOrderApplies(null, null));
        Assert.False(ReignWarEconomyPolicy.StandingOrderApplies("", ""));
    }
    [Fact]
    public void DrawdownApportionsTiersThenReleasesLeastExperiencedHealthyStacks()
    {
        var plan = ReignWarEconomyPolicy.SelectDischarges(new[] {
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "elite", Tier = 5, Experience = 0, Healthy = 20 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "trained", Tier = 1, Experience = 90, Healthy = 4 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "raw", Tier = 1, Experience = 0, Healthy = 3 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "wounded", Tier = 0, Healthy = 0 }
        }, ReignWarEconomyPolicy.DrawdownBatch(200, 90, 200, true), null, 0, out _);
        Assert.Equal(new[] { "raw", "elite" }, plan.Select(x => x.Id));
        Assert.Equal(new[] { 3, 7 }, plan.Select(x => x.Healthy));
        Assert.Equal(0, ReignWarEconomyPolicy.DrawdownBatch(200, 90, 200, false));
        Assert.Equal(0, ReignWarEconomyPolicy.DrawdownBatch(90, 90, 200, true));
        Assert.Equal(2, ReignWarEconomyPolicy.DrawdownBatch(92, 90, 200, true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void DrawdownCarriesFractionalSharesThroughSmallBatches(int batch)
    {
        var troops = new[] {
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "low", Tier = 1, Healthy = 50 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "mid", Tier = 3, Healthy = 30 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "high", Tier = 6, Healthy = 20 },
        };
        Dictionary<int, decimal>? carry = null;
        for (int released = 0; released < 20; released += batch)
        {
            var plan = ReignWarEconomyPolicy.SelectDischarges(troops, batch, carry, (uint)released, out var next);
            Assert.Equal(batch, plan.Sum(x => x.Healthy));
            foreach (var row in plan) troops.Single(x => x.Id == row.Id).Healthy -= row.Healthy;
            carry = next;
        }
        Assert.Equal(new[] { 40, 24, 16 }, troops.Select(x => x.Healthy));
    }

    [Fact]
    public void DrawdownBreaksExactTiesAcrossTiersAndIgnoresAbsentCarry()
    {
        var troops = new[] {
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "low", Tier = 1, Healthy = 2 },
            new ReignWarEconomyPolicy.DischargeCandidate { Id = "high", Tier = 6, Healthy = 2 },
        };
        var plan = ReignWarEconomyPolicy.SelectDischarges(troops, 1, new Dictionary<int, decimal> { [2] = 1m }, 1, out var carry);
        Assert.Equal("high", Assert.Single(plan).Id);
        Assert.False(carry.ContainsKey(2));
        troops[1].Healthy--;
        plan = ReignWarEconomyPolicy.SelectDischarges(troops.Reverse(), 1, carry, 1, out _);
        Assert.Equal("low", Assert.Single(plan).Id);
        Assert.Empty(ReignWarEconomyPolicy.SelectDischarges(troops, 0, carry, 0, out _));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(200)]
    public void HearthReturnHasOneConservedRealmBudgetRegardlessOfVillageCount(int villages)
    {
        var recipients = Enumerable.Range(0, villages).Select(i => new ReignWarEconomyPolicy.HearthRecipient {
            Id = i.ToString("D3"), Hearth = 300 }).ToList();
        var plan = ReignWarEconomyPolicy.PlanHearthReturn(100, recipients);
        double returned = plan.Sum(x => (double)x.Value - 300);
        Assert.InRange(returned, 24.999, 25);
        Assert.Equal(25, 100 * ReignWarEconomyPolicy.HearthReturnPerDischarge);
        Assert.Empty(ReignWarEconomyPolicy.PlanHearthReturn(0, recipients));
        Assert.Empty(ReignWarEconomyPolicy.PlanHearthReturn(10, Array.Empty<ReignWarEconomyPolicy.HearthRecipient>()));
        Assert.Equal(plan, ReignWarEconomyPolicy.PlanHearthReturn(100, recipients.AsEnumerable().Reverse()));
    }
    [Fact]
    public void FractionalHearthReturnCannotCreatePopulationAtNativeFloatBoundaries()
    {
        var recipients = Enumerable.Range(0, 1000).Select(i => new ReignWarEconomyPolicy.HearthRecipient {
            Id = i.ToString("D4"), Hearth = 1234.5678f }).ToList();
        var plan = ReignWarEconomyPolicy.PlanHearthReturn(1, recipients);
        Assert.InRange(plan.Sum(x => (double)x.Value - 1234.5678f), 0, .25);
    }

    [Theory]
    [InlineData(false, false, 0, true, true, .45)]
    [InlineData(false, false, -100, true, true, .45)]
    [InlineData(false, false, 10, true, true, .45)]
    [InlineData(false, false, 55, true, true, .60)]
    [InlineData(false, false, 100, true, true, .75)]
    [InlineData(false, false, 100, false, true, .60)]
    [InlineData(false, false, 100, true, false, .45)]
    [InlineData(true, false, -100, false, false, 1)]
    [InlineData(false, true, 0, false, false, 1)]
    public void FieldStandingUsesHostileSignFreshnessAndActualWar(bool war, bool orderedWar, int pressure, bool frontier, bool fresh, double expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.FieldReadiness(war, orderedWar, pressure, frontier, fresh), 6);

    [Theory]
    [InlineData(10, 10, true)]
    [InlineData(10, 11, true)]
    [InlineData(10, 11.001, false)]
    [InlineData(10, 9.99, false)]
    public void PressureSnapshotCannotCrossExpiryOrRewind(double observed, double now, bool fresh)
        => Assert.Equal(fresh, ReignWarEconomyPolicy.PressureFresh(observed, now));

    [Fact]
    public void PlayerSovereignCanKeepChosenLowPeaceForcesDuringActualWar()
    {
        Assert.Equal(.40, ReignWarEconomyPolicy.FieldReadiness(true, false, 100, true, true, true, 40));
        Assert.Equal(.90, ReignWarEconomyPolicy.FieldReadiness(true, false, 100, true, true, true, 90));
        Assert.Equal(1, ReignWarEconomyPolicy.FieldReadiness(true, false, 100, true, true, false, 40));
        Assert.Equal(1, ReignWarEconomyPolicy.FieldReadiness(false, true, 0, false, false, true, 40));
    }

    [Theory]
    [InlineData("owner", "owner", null, null, true, true, true, true, true)]
    [InlineData("owner", "other", "realm", "realm", true, true, true, true, true)]
    [InlineData("owner", "other", "realm", "foreign", true, true, true, true, false)]
    [InlineData("owner", "other", null, null, true, true, true, true, false)]
    [InlineData("owner", "other", "realm", "rebel_realm", true, true, true, true, false)]
    [InlineData("owner", "other", "realm", "realm", false, true, true, true, false)]
    [InlineData("owner", "other", "realm", "realm", true, false, true, true, false)]
    [InlineData("owner", "other", "realm", "realm", true, true, false, true, false)]
    [InlineData("owner", "other", "realm", "realm", true, true, true, false, false)]
    [InlineData("", "other", "realm", "realm", true, true, true, true, false)]
    [InlineData("owner", null, "realm", "realm", true, true, true, true, false)]
    public void ProcurementExemptionUsesCurrentKingdomOrIndependentClanAndVerifiedMilitaryPurpose(string owner, string? buyer,
        string? ownerKingdom, string? buyerKingdom, bool military, bool purpose, bool item, bool started, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.InternalProvisioning(owner, buyer, ownerKingdom, buyerKingdom, military, purpose, item, started));

    [Theory]
    [InlineData(1, 1, 1, 1000L, 300L, 700L, 630L, false, true)]
    [InlineData(1, 1, 1, 1000L, 1000L, 0L, 0L, true, true)]
    [InlineData(1, 1, 1, 0L, 0L, 0L, 0L, true, true)]
    [InlineData(1, 1, 1, 1000L, 1000L, 700L, 700L, false, false)]
    [InlineData(1, 1, 1, 1000L, 300L, 700L, 700L, true, false)]
    [InlineData(2, 1, 2, 1000L, 1000L, 0L, 0L, true, false)]
    [InlineData(1, 1, 1, 1000L, 300L, 700L, 701L, false, false)]
    [InlineData(1, 1, 1, -1L, -1L, 0L, 0L, false, false)]
    public void TariffEvidenceRequiresCashStockAndPoolConservation(int requested, int received, int removed,
        long paid, long market, long commission, long pool, bool exempt, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.TariffConserved(requested, received, removed, paid, market, commission, pool, exempt, paid, requested));
    [Fact]
    public void BalancedCashCannotHideAnUnpaidNativeQuoteOrAChargeForNoGoods()
    {
        Assert.False(ReignWarEconomyPolicy.TariffConserved(5, 5, 5, 0, 0, 0, 0, true, 100, 5));
        Assert.False(ReignWarEconomyPolicy.TariffConserved(0, 0, 0, 100, 100, 0, 0, true, 100, 0));
        Assert.False(ReignWarEconomyPolicy.TariffConserved(5, 5, 5, 100, 100, 0, 0, true, 100, 4));
        Assert.True(ReignWarEconomyPolicy.TariffConserved(5, 5, 5, 0, 0, 0, 0, true, 0, 5));
    }
    [Theory]
    [InlineData(0, 0)] [InlineData(10, 2)] [InlineData(50, 4.480925558298786)]
    [InlineData(100, 6.342346692001636)] [InlineData(200, 8.977020715518409)]
    [InlineData(300, 11)] [InlineData(400, 12.833333333333333)] [InlineData(600, 16.5)]
    [InlineData(900, 19.25)] [InlineData(1200, 22)] [InlineData(2000, 29.333333333333333)]
    public void EveryProductionChannelSharesContinuousHouseholdOutput(double hearth, double food)
    {
        Assert.Equal(food, ReignWarEconomyPolicy.HealthyFood(hearth), 8);
        Assert.Equal(food * .3, ReignWarEconomyPolicy.Production(hearth, .3, true), 8);
        Assert.Equal(0, ReignWarEconomyPolicy.Production(hearth, 1, false));
    }

    [Theory]
    [InlineData(10)] [InlineData(200)] [InlineData(300)] [InlineData(600)]
    public void OldThresholdsHaveNoProductionDiscontinuity(double hearth)
    {
        double left = ReignWarEconomyPolicy.HealthyFood(hearth - .0001);
        double right = ReignWarEconomyPolicy.HealthyFood(hearth + .0001);
        Assert.InRange(right - left, 0, .00003);
    }

    [Fact]
    public void RevisionThreeTrimsAtMostOneTwelfthWithoutBuffingSubsistenceOrCappingLargeVillages()
    {
        double previous = 0;
        for (int quarterHearth = 0; quarterHearth <= 40000; quarterHearth++)
        {
            double hearth = quarterHearth / 4d;
            double oldOutput = hearth <= 300 ? 12 * Math.Pow(hearth / 300, Math.Log(6) / Math.Log(30))
                : hearth <= 600 ? 12 + .02 * (hearth - 300) : 18 + .01 * (hearth - 600);
            double output = ReignWarEconomyPolicy.HealthyFood(hearth);
            Assert.True(double.IsFinite(output));
            Assert.True(output >= previous);
            Assert.True(output <= oldOutput + 1e-10);
            Assert.True(output + 1e-10 >= oldOutput * (11d / 12));
            if (hearth <= 10) Assert.Equal(oldOutput, output, 10);
            previous = output;
        }
        Assert.Equal(0, ReignWarEconomyPolicy.HealthyFood(-10));
        Assert.Equal(2 * ReignWarEconomyPolicy.HealthyFood(300), ReignWarEconomyPolicy.HealthyFood(1200), 10);
        Assert.InRange(ReignWarEconomyPolicy.HealthyFood(5), 1.38, 1.40);
        Assert.Equal(3, ReignWarEconomyPolicy.ProductionCurveVersion);
        Assert.Equal(3, ReignWarEconomyPolicy.RecruitmentPriceVersion);
    }

    [Fact]
    public void CostlyDraftingLeavesPersistentButRecoverablePopulationDamage()
    {
        double hearth = 300, strain = 0;
        for (int day = 0; day < 96; day++)
        {
            strain = ReignWarEconomyPolicy.DecayStrain(strain);
            if (day < 32) { hearth -= 4; strain += 8; }
            hearth += ReignWarEconomyPolicy.NaturalGrowth(hearth, strain, false);
            if (day == 31) Assert.InRange(hearth, 176, 178);
        }
        Assert.InRange(hearth, 216, 218);
        Assert.Equal(0, strain);
    }

    [Fact]
    public void SafePeaceDoesNotLosePopulationAndStressSlowsOnlyNaturalRecovery()
    {
        double hearth = 300;
        for (int day = 0; day < 126; day++) hearth += ReignWarEconomyPolicy.NaturalGrowth(hearth, 0, false);
        Assert.InRange(hearth, 380, 400);
        Assert.Equal(ReignWarEconomyPolicy.NaturalGrowth(300, 20, false) / 2,
            ReignWarEconomyPolicy.NaturalGrowth(300, 20, true), 10);
    }

    [Fact]
    public void RecoveryRequiresTwelveSafeDaysOrTwentyFourStressedDaysToRecruit()
    {
        double safe = 0, stressed = 0;
        for (int i = 0; i < 12; i++) { safe = ReignWarEconomyPolicy.Recover(safe, true, false); stressed = ReignWarEconomyPolicy.Recover(stressed, true, true); }
        Assert.Equal(580, ReignWarEconomyPolicy.RecruitCapacity(300, safe, true));
        Assert.Equal(0, ReignWarEconomyPolicy.RecruitCapacity(300, stressed, true));
        for (int i = 0; i < 12; i++) stressed = ReignWarEconomyPolicy.Recover(stressed, true, true);
        Assert.Equal(580, ReignWarEconomyPolicy.RecruitCapacity(300, stressed, true));
        Assert.Equal(safe, ReignWarEconomyPolicy.Recover(safe, false, false));
        Assert.Equal(0, ReignWarEconomyPolicy.RecruitCapacity(10.49, 1, true));
    }

    [Theory]
    [InlineData(-.35, -.9, 0)] [InlineData(-.35, -.35, 3.3)] [InlineData(.35, -.35, 11)]
    public void FoodEffectsAreAdditiveAndCannotProduceNegativeAgriculture(double events, double spies, double expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.Production(300, 1, true, events + spies), 8);

    [Fact]
    public void OutstandingNativeRecruitmentLiabilityIsReservedOnce()
    {
        // 1000 cash - 100 liability - seven days at 100 = 200 available.
        Assert.Equal(2, ReignWarEconomyPolicy.AffordableCount(10, 1000, 1000, 100, 100, 100, 0, 7));
        Assert.Equal(1, ReignWarEconomyPolicy.AffordableCount(10, 150, 1000, 100, 100, 100, 0, 7));
        Assert.Equal(0, ReignWarEconomyPolicy.AffordableCount(1, 700, 700, 100, 1, 0, 0, 7));
        Assert.Equal(10, ReignWarEconomyPolicy.AffordableCount(10, 1000, 1000, 100, 0, 100, 0, 0));
        Assert.Equal(0, ReignWarEconomyPolicy.AffordableCount(1, 1400, 1400, 100, 0, 1, 0, 14));
        Assert.Equal(10, ReignWarEconomyPolicy.AffordableCount(10, 0, 0, 0, 0, 0, 0, 7));
    }

    [Theory]
    [InlineData(0, 1, 3)] [InlineData(1, 1, 3)] [InlineData(2, 1.15, 3.6)]
    [InlineData(3, 1.5, 4.5)] [InlineData(4, 2, 6)] [InlineData(5, 3, 8.25)] [InlineData(6, 4, 10.5)] [InlineData(9, 4, 10.5)]
    public void TierScheduleIsExact(int tier, double wage, double hire)
    {
        Assert.Equal(wage, ReignWarEconomyPolicy.WageMultiplier(tier));
        Assert.Equal(hire, ReignWarEconomyPolicy.HireMultiplier(tier));
    }

    [Fact]
    public void RebuildingOneHundredTierFiveRegularsCostsMoreWithoutRaisingTheirWages()
    {
        int[] nativeHire = { 20, 50, 100, 200, 400 };
        int[] revisedHire = nativeHire.Select((cost, i) =>
            (int)Math.Round(cost * ReignWarEconomyPolicy.HireMultiplier(i + 1))).ToArray();
        Assert.Equal(new[] { 60, 180, 450, 1200, 3300 }, revisedHire);
        double total = revisedHire[0];
        int[] expectedUpgrade = { 60, 135, 375, 1050 };
        for (int tier = 1; tier < revisedHire.Length; tier++)
        {
            double upgrade = (revisedHire[tier] - revisedHire[tier - 1]) / 2d;
            Assert.Equal(expectedUpgrade[tier - 1], upgrade);
            total += upgrade;
        }
        Assert.Equal(168000, 100 * total);
        const int previousReignCost = 56000;
        Assert.Equal(3 * previousReignCost, 100 * total);
        int tierSixHire = (int)Math.Round(800 * ReignWarEconomyPolicy.HireMultiplier(6));
        Assert.Equal(8400, tierSixHire);
        Assert.Equal(2550, (tierSixHire - revisedHire[4]) / 2);
        Assert.Equal(423000, 100 * (total + (tierSixHire - revisedHire[4]) / 2));
        Assert.Equal(3, ReignWarEconomyPolicy.WageMultiplier(5));
        // Same seven-day wage reserve, but fewer paid enlistments fit the remaining cash.
        int previousAffordable = ReignWarEconomyPolicy.AffordableCount(10, 1000, 1000, 100, 0, 60, 0, 7);
        int revisedAffordable = ReignWarEconomyPolicy.AffordableCount(10, 1000, 1000, 100, 0, revisedHire[1], 0, 7);
        Assert.Equal(5, previousAffordable);
        Assert.Equal(1, revisedAffordable);
        Assert.True(revisedAffordable < previousAffordable);
    }

    private static ReignWarEconomyPolicy.CivilianBudget Budget(string id, double amount, bool food = false)
        => new() { Id = id, Budget = amount, Food = food };
    private static ReignWarEconomyPolicy.MarketFood Food(string id, string category, int stock, double price)
        => new() { Id = id, Category = category, Stock = stock, Price = price };

    [Theory]
    [InlineData(65, 300, -39, 27, true)]
    [InlineData(87.7, 400, -76, 44, true)]
    [InlineData(100, 400, -40, 40, false)]
    [InlineData(79.99, 400, -40, 50, true)]
    [InlineData(87, 400, -2, 1, false)] //exactly20% after seven days
    [InlineData(86.999, 400, -2, 1, true)]
    [InlineData(500, 400, -40, 40, false)] //native capacity may have fallen
    public void SubsistenceForecastUsesTheOriginalInventoryCappedPlan(double stock, double capacity, double baseNet, double units, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.NeedsSubsistencePriority(stock, capacity, baseNet, units));

    [Fact]
    public void OriginalFoodPlanIsNeverReducedWhenItAlreadyExceedsTheTarget()
    {
        var input = new[] { Budget("grain", 900, true), Budget("tools", 100) };
        var food = new[] { Food("grain", "grain", 200, 10) };
        var native = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 0);
        Assert.True(ReignWarEconomyPolicy.NeedsSubsistencePriority(10, 300, -80, native.NativePlannedUnits));
        var result = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, Math.Max(88, native.NativePlannedUnits));
        Assert.Equal(native.PlannedUnits, result.PlannedUnits);
        Assert.Equal(0, result.Redirected);
        Assert.Equal(100, result.Budgets["tools"]);
        var lowerTarget = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 88);
        Assert.Equal(native.PlannedUnits, lowerTarget.PlannedUnits);
        Assert.Equal(0, lowerTarget.Redirected);
    }

    [Fact]
    public void PrioritizedFoodCannotTurnItsOwnNeedOffThroughYesterdaysSellLog()
    {
        var input = new[] { Budget("grain", 200, true), Budget("tools", 800) };
        int inventory = 4000;
        double reserves = 160;
        for (int day = 0; day < 20; day++)
        {
            var offers = new[] { Food("grain", "grain", inventory, 10) };
            var native = ReignWarEconomyPolicy.PrioritizeSubsistence(input, offers, 0);
            Assert.Equal(20, native.NativePlannedUnits);
            Assert.True(ReignWarEconomyPolicy.NeedsSubsistencePriority(reserves, 300, -80, native.NativePlannedUnits));
            var active = ReignWarEconomyPolicy.PrioritizeSubsistence(input, offers, 80);
            Assert.Equal(active.TotalBefore, active.TotalAfter, 9);
            Assert.Equal(80, active.PlannedUnits);
            reserves += -80 + active.PlannedUnits;
            inventory -= (int)active.PlannedUnits;
            Assert.Equal(160, reserves); //actual net is now zero, but the native counterfactual still underfeeds
        }
        Assert.Equal(2400, inventory);
    }

    [Fact]
    public void ForecastUsesAvailableInventoryAndCannotTurnMissingFoodIntoPurchasingPower()
    {
        var input = new[] { Budget("grain", 10000, true), Budget("tools", 100) };
        var food = new[] { Food("grain", "grain", 3, 10) };
        var native = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 0);
        Assert.Equal(3, native.NativePlannedUnits);
        Assert.True(ReignWarEconomyPolicy.NeedsSubsistencePriority(160, 300, -80, native.NativePlannedUnits));
        var active = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 80);
        Assert.Equal(3, active.PlannedUnits); Assert.Equal(77, active.UnmetUnits);
        Assert.Equal(0, active.Redirected);
    }

    [Fact]
    public void NativeAndPriorityQuotesReuseUnchangedOriginalInputs()
    {
        var input = new[] { Budget("grain", 200, true), Budget("tools", 800) };
        var food = new[] { Food("grain", "grain", 300, 10) };
        var first = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 0);
        var active = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 80);
        var second = ReignWarEconomyPolicy.PrioritizeSubsistence(input, food, 0);
        Assert.Equal(first.NativePlannedUnits, second.NativePlannedUnits);
        Assert.Equal(first.NativePlannedUnits, active.NativePlannedUnits);
        Assert.Equal(200, input[0].Budget); Assert.Equal(800, input[1].Budget);
        Assert.Equal(300, food[0].Stock); Assert.Equal(10, food[0].Price);
    }

    [Fact]
    public void InvalidSubsistenceForecastsAreUnsupportedRatherThanHealthy()
    {
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(bad, 300, -80, 20));
            Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, bad, -80, 20));
            Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, 300, bad, 20));
            Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, 300, -80, bad));
        }
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, 0, -80, 20));
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(-1, 300, -80, 20));
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, 300, -80, -1));
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.NativePlanFoodProjection(90, 300, double.MaxValue, 20));
    }

    [Fact]
    public void SubsistenceRedirectsExistingNonFoodPurchasingPowerWithoutCreatingEitherResource()
    {
        var budgets = new[] { Budget("grain", 20, true), Budget("tools", 100) };
        var plan = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, new[] { Food("grain", "grain", 20, 10) }, 8);
        Assert.Equal(2, plan.NativePlannedUnits);
        Assert.Equal(8, plan.PlannedUnits);
        Assert.Equal(60, plan.Redirected);
        Assert.Equal(80, plan.Budgets["grain"]);
        Assert.Equal(40, plan.Budgets["tools"]);
        Assert.Equal(120, plan.TotalBefore);
        Assert.Equal(plan.TotalBefore, plan.TotalAfter, 9);
        Assert.Equal(20, budgets[0].Budget);
        Assert.Equal(100, budgets[1].Budget);
    }

    [Fact]
    public void RealFoodStockAndPurchasingBudgetsCanStillLeaveHunger()
    {
        var budgets = new[] { Budget("grain", 20, true), Budget("tools", 100) };
        var limited = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, new[] { Food("grain", "grain", 3, 10) }, 8);
        Assert.Equal(3, limited.PlannedUnits); Assert.Equal(5, limited.UnmetUnits);
        Assert.Equal(10, limited.Redirected);
        var empty = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, Array.Empty<ReignWarEconomyPolicy.MarketFood>(), 8);
        Assert.Equal(0, empty.PlannedUnits); Assert.Equal(8, empty.UnmetUnits); Assert.Equal(0, empty.Redirected);
        var poor = ReignWarEconomyPolicy.PrioritizeSubsistence(new[] { Budget("grain", 1, true), Budget("tools", 2) },
            new[] { Food("grain", "grain", 100, 10) }, 8);
        Assert.Equal(.3, poor.PlannedUnits, 9); Assert.Equal(7.7, poor.UnmetUnits, 9);
        Assert.Equal(0, poor.Budgets["tools"]);
        Assert.Equal(poor.TotalBefore, poor.TotalAfter, 9);
    }

    [Fact]
    public void NativeFoodAllocationsAndHealthyReserveControlsRemainIntact()
    {
        var budgets = new[] { Budget("grain", 200, true), Budget("tools", 100) };
        foreach (double need in new[] { 0d, 8d })
        {
            var plan = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, new[] { Food("grain", "grain", 20, 10) }, need);
            Assert.Equal(0, plan.Redirected);
            Assert.Equal(200, plan.Budgets["grain"]); Assert.Equal(100, plan.Budgets["tools"]);
        }
    }

    [Fact]
    public void ExtraFoodUsesAvailableCheapStaplesWithStableOrderingAndProportionalSacrifice()
    {
        var budgets = new[] { Budget("grain", 10, true), Budget("fish", 40, true), Budget("tools", 60), Budget("cloth", 40) };
        var offers = new[] { Food("fish", "fish", 20, 20), Food("grain", "grain", 20, 10) };
        var plan = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets, offers, 10);
        var reversed = ReignWarEconomyPolicy.PrioritizeSubsistence(budgets.Reverse(), offers.Reverse(), 10);
        Assert.Equal(plan.Budgets.OrderBy(x => x.Key), reversed.Budgets.OrderBy(x => x.Key));
        Assert.Equal(80, plan.Budgets["grain"]); Assert.Equal(40, plan.Budgets["fish"]);
        Assert.Equal(18, plan.Budgets["tools"], 9); Assert.Equal(12, plan.Budgets["cloth"], 9);
        Assert.Equal(150, plan.TotalAfter, 9);
    }

    [Fact]
    public void UnsupportedCategoryShapesOrInvalidQuotesCannotProduceABudgetPlan()
    {
        var budgets = new[] { Budget("grain", 20, true), Budget("tools", 100) };
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.PrioritizeSubsistence(budgets,
            new[] { Food("grain", "grain", 20, 10), Food("other", "grain", 20, 10) }, 8));
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.PrioritizeSubsistence(budgets,
            new[] { Food("grain", "grain", 20, double.NaN) }, 8));
        Assert.Throws<ArgumentException>(() => ReignWarEconomyPolicy.PrioritizeSubsistence(budgets,
            Array.Empty<ReignWarEconomyPolicy.MarketFood>(), double.PositiveInfinity));
    }

    [Theory]
    [InlineData(1, 0, 20, 40, true)]
    [InlineData(1, double.NaN, 20, 40, false)]
    [InlineData(1, 0, double.NaN, 40, false)]
    [InlineData(1, 0, double.PositiveInfinity, 40, false)]
    [InlineData(0, 0, 20, 40, false)]
    [InlineData(1, 1, 20, 40, false)]
    [InlineData(1, 0, 15, 40, false)]
    public void UnknownOrNonfiniteNativeBudgetShapesFailClosed(double unit, double zero, double half, double whole, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.CompatibleCivilianBudgetQuotes(unit, zero, half, whole));

    [Theory]
    [InlineData(3347, 0, 7653, false)]
    [InlineData(0, 0, 7653, false)]
    [InlineData(0, 0, 0, true)]
    [InlineData(7653, 0, 7653, true)]
    [InlineData(100, 60, 40, true)]
    [InlineData(100, 60, 41, false)]
    [InlineData(-1, 0, 1, false)]
    [InlineData(100, 0, -1, false)]
    [InlineData(int.MaxValue, int.MaxValue, 1, false)]
    public void NativeMarketPurchasesRequireTheExactPayersRemainingCash(long cash, long accepted, int quote, bool expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.CanPayQuotedUnit(cash, accepted, quote));

    [Fact]
    public void ChangingNativeMarginalPricesPermitOnlyPaidUnits()
    {
        int[] prices = { 40, 45, 50, 50 };
        long accepted = 0; int units = 0;
        foreach (int price in prices)
            if (ReignWarEconomyPolicy.CanPayQuotedUnit(100, accepted, price)) { accepted += price; units++; }
        Assert.Equal(2, units); Assert.Equal(85, accepted);
        Assert.True(ReignWarEconomyPolicy.GuardedPurchaseConserved(4, 2, 2, 2, 2, 85, 25, 60, 57, false, 185, 4, 85));
        Assert.True(ReignWarEconomyPolicy.GuardedPurchaseConserved(1, 0, 1, 0, 0, 0, 0, 0, 0, false, 7653, 1, 0));
        Assert.True(ReignWarEconomyPolicy.GuardedPurchaseConserved(1, 1, 0, 1, 1, 0, 0, 0, 0, true, 0, 1, 0));
        Assert.False(ReignWarEconomyPolicy.GuardedPurchaseConserved(1, 1, 0, 1, 1, 3347, -1582, 5357, 5357, false, 7653, 1, 7653));
        Assert.False(ReignWarEconomyPolicy.GuardedPurchaseConserved(1, 0, 1, 1, 1, 0, 0, 0, 0, false, 7653, 1, 0));
    }

    [Fact]
    public void CoherentNativeNegativeDemandContributesNoSpendableMoney()
    {
        Assert.True(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-160.116913, 1.7236850261688232, 0, -137.9955596923828, -275.9911193847656));
        Assert.False(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-10, 2, 0, -5, -20));
        Assert.False(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-10, 2, 0, -10, -21));
        Assert.False(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-10, 2, 1, -10, -20));
        Assert.False(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-10, double.NaN, 0, -10, -20));
        Assert.False(ReignWarEconomyPolicy.CompatibleNegativeCivilianBudget(-10, 0, 0, -10, -20));
        Assert.Equal(0, ReignWarEconomyPolicy.NativeSpendableBudget(-275.9911193847656));
        Assert.Equal(0, ReignWarEconomyPolicy.NativeSpendableBudget(.01f));
        Assert.Equal(0, ReignWarEconomyPolicy.NativeSpendableBudget(.005));
        Assert.Equal(.02, ReignWarEconomyPolicy.NativeSpendableBudget(.02));
        var plan = ReignWarEconomyPolicy.PrioritizeSubsistence(new[] { Budget("grain", 0, true), Budget("tools", 100) },
            new[] { Food("grain", "grain", 20, 10) }, 8);
        Assert.Equal(100, plan.TotalBefore); Assert.Equal(100, plan.TotalAfter);
        Assert.Equal(8, plan.PlannedUnits); Assert.Equal(80, plan.Redirected);
    }

    [Fact]
    public void NativeBudgetDustIsMergedWithoutIncreasingTheFundedAllocation()
    {
        var plan = new Dictionary<string, double> { ["grain"] = 10, ["tools"] = .005, ["cloth"] = .006 };
        double before = plan.Values.Sum();
        ReignWarEconomyPolicy.ConsolidateBudgetDust(plan);
        Assert.Equal(before, plan.Values.Sum(), 12);
        Assert.Equal(before, plan.Values.Sum(ReignWarEconomyPolicy.NativeSpendableBudget), 12);
        Assert.Equal(0, plan["tools"]); Assert.Equal(0, plan["cloth"]);
        var partial = ReignWarEconomyPolicy.PrioritizeSubsistence(new[] { Budget("grain", 0, true), Budget("tools", 100) },
            new[] { Food("grain", "grain", 20, 10) }, 9.9995);
        Assert.Equal(100, partial.TotalAfter, 9);
        Assert.Equal(100, partial.Budgets.Values.Sum(ReignWarEconomyPolicy.NativeSpendableBudget), 9);
        Assert.Equal(0, partial.Budgets["tools"]);
    }

    [Fact]
    public void NativeDiscountedAndRoundedWagesDetermineTheActualFundingReserve()
    {
        // A legitimate native discount rounds the first added troop to zero, but not the second.
        double RoundedWages(int count) => Math.Floor(count * .6);
        Assert.Equal(1, ReignWarEconomyPolicy.AffordableCountExact(10, 705, 705, 100, 0, 0, 7, RoundedWages));
        Assert.Equal(3, ReignWarEconomyPolicy.AffordableCountExact(10, 707, 707, 100, 0, 0, 7, RoundedWages));
        Assert.Equal(0, ReignWarEconomyPolicy.AffordableCountExact(10, 707, 707, 100, 8, 0, 7, RoundedWages));
        Assert.Equal(2, ReignWarEconomyPolicy.AffordableCountExact(10, 20, 707, 100, 8, 10, 0, RoundedWages));
        Assert.Equal(int.MaxValue, ReignWarEconomyPolicy.AffordableCountExact(int.MaxValue, 0, 0, 0, 0, 0, 14, _ => 0));
    }

    private static ReignWarEconomyPolicy.Holding Holding(string id, double stock, double change = 0, double x = 0)
        => new() { Id = id, Stock = stock, Capacity = 200, Change = change, X = x, Security = 100 };

    [Fact]
    public void DecliningInteriorDonorsCanShipBelowTheOldSixtyPercentFloor()
    {
        var donor = Holding("donor", 110, -1);
        var recipient = Holding("recipient", 5, -3);
        var plan = ReignWarEconomyPolicy.AllocateRelief(new[] { donor, recipient });
        Assert.InRange(plan.DonorFloor, .2, .3);
        Assert.NotEmpty(plan.Shipments);
        Assert.InRange(plan.Shipments.Sum(x => x.Shipped), 14.99, 15);
        Assert.Equal(110, donor.Stock); // Planning is pure; native adapter owns stock changes.
        Assert.True(plan.UnmetFood > 0);
        Assert.True(plan.UnmetRecipients > 0);
    }

    [Fact]
    public void DonorAndRecipientLimitsCapacityLossAndOrderingAreConserved()
    {
        var holdings = new[] { Holding("b", 180), Holding("a", 180), Holding("low", 0, -2), Holding("higher", 20, -1) };
        var plan = ReignWarEconomyPolicy.AllocateRelief(holdings);
        var reversed = ReignWarEconomyPolicy.AllocateRelief(holdings.Reverse());
        Assert.Equal(plan.Shipments.Select(x => (x.Donor, x.Recipient, x.Shipped)), reversed.Shipments.Select(x => (x.Donor, x.Recipient, x.Shipped)));
        foreach (var donor in plan.Shipments.GroupBy(x => x.Donor)) Assert.InRange(donor.Sum(x => x.Shipped), 0, 15.000001);
        foreach (var recipient in plan.Shipments.GroupBy(x => x.Recipient)) Assert.InRange(recipient.Sum(x => x.Delivered), 0, 20.000001);
        Assert.All(plan.Shipments, x => Assert.InRange(x.Delivered / x.Shipped, .699999, .900001));
        foreach (var donor in holdings.Where(x => x.Id == "a" || x.Id == "b"))
        {
            double outgoing = plan.Shipments.Where(x => x.Donor == donor.Id).Sum(x => x.Shipped);
            Assert.True(donor.Stock - outgoing >= plan.DonorFloor * donor.Capacity - 1e-6);
            Assert.True(donor.Stock + 7 * donor.Change - outgoing >= plan.DonorFloor * donor.Capacity - 1e-6);
        }
        foreach (var recipient in holdings.Where(x => x.Id == "low" || x.Id == "higher"))
        {
            double incoming = plan.Shipments.Where(x => x.Recipient == recipient.Id).Sum(x => x.Delivered);
            Assert.InRange(recipient.Stock + incoming, 0, recipient.Capacity);
        }
        Assert.DoesNotContain(plan.Shipments, x => x.Donor == "low" || x.Donor == "higher");
    }

    [Fact]
    public void SiegeAndLongRoutesReportUnmetDemandBeforeEmptyStocks()
    {
        var donor = Holding("donor", 190, 0, 181);
        var target = Holding("target", 30, -1);
        var plan = ReignWarEconomyPolicy.AllocateRelief(new[] { donor, target });
        Assert.Empty(plan.Shipments);
        Assert.True(plan.BlockedRoutes > 0);
        Assert.True(plan.UnmetFood > 0);
        donor.X = 0; target.Besieged = true;
        plan = ReignWarEconomyPolicy.AllocateRelief(new[] { donor, target });
        Assert.Empty(plan.Shipments);
        Assert.True(plan.BlockedRoutes > 0);
    }

    [Fact]
    public void ReliefRefillDoesNotCountTheSevenDayDeficitTwice()
    {
        var town = new ReignWarEconomyPolicy.Holding { Capacity = 400, Stock = 150, Change = -30 };
        Assert.Equal(38, ReignWarEconomyPolicy.ReliefRecipientLimit(town, .5)); //30 +50/7, rounded once.
        town.Capacity = 300; town.Stock = 15; town.Change = -53;
        Assert.Equal(73, ReignWarEconomyPolicy.ReliefRecipientLimit(town, .5));
        town.Change = -96;
        Assert.Equal(75, ReignWarEconomyPolicy.ReliefRecipientLimit(town, .5));
    }

    [Theory]
    [InlineData(100, 10)]
    [InlineData(200, 15)]
    [InlineData(400, 30)]
    [InlineData(10000, 30)]
    public void DonorTransportCapacityIsFiniteAndScalesWithStorage(double capacity, double expected)
        => Assert.Equal(expected, ReignWarEconomyPolicy.ReliefDonorLimit(new() { Capacity = capacity }));

    [Fact]
    public void OldFixedConvoysLeaveAChronicTownEmptyDespiteUnusedInteriorProduction()
    {
        static ReignWarEconomyPolicy.Holding[] Network() => new[] {
            new ReignWarEconomyPolicy.Holding { Id="town",Capacity=300,Stock=15,Change=-53,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="a",Capacity=600,Stock=550,Change=20,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="b",Capacity=600,Stock=550,Change=20,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="c",Capacity=600,Stock=550,Change=20,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="d",Capacity=600,Stock=550,Change=20,Security=100 } };
        var active = Network(); var legacy = Network();
        for (int day = 0; day < 14; day++)
        {
            foreach (var pair in new[] { (holdings: active, prior: false), (holdings: legacy, prior: true) })
            {
                var plan = ReignWarEconomyPolicy.AllocateRelief(pair.holdings, pair.prior);
                double before = pair.holdings.Sum(x=>x.Stock);
                foreach (var shipment in plan.Shipments)
                {
                    pair.holdings.Single(x=>x.Id==shipment.Donor).Stock -= shipment.Shipped;
                    pair.holdings.Single(x=>x.Id==shipment.Recipient).Stock += shipment.Delivered;
                }
                Assert.Equal(before - plan.Shipments.Sum(x=>x.Shipped-x.Delivered), pair.holdings.Sum(x=>x.Stock), 7);
                foreach (var h in pair.holdings) h.Stock = ReignWarEconomyPolicy.Clamp(h.Stock+h.Change,0,h.Capacity);
            }
            Assert.True(active.Single(x=>x.Id=="town").Stock > 0);
            Assert.Equal(0,legacy.Single(x=>x.Id=="town").Stock);
        }
        Assert.True(active.Where(x=>x.Id!="town").All(x=>x.Stock>360));
    }

    [Fact]
    public void NeedScaledReliefStillExhaustsFiniteDecliningInteriorStocks()
    {
        var holdings = new[] { new ReignWarEconomyPolicy.Holding { Id="town",Capacity=300,Stock=15,Change=-53,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="a",Capacity=600,Stock=550,Change=-5,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="b",Capacity=600,Stock=550,Change=-5,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="c",Capacity=600,Stock=550,Change=-5,Security=100 },
            new ReignWarEconomyPolicy.Holding { Id="d",Capacity=600,Stock=550,Change=-5,Security=100 } };
        bool exhausted=false;
        for(int day=0;day<45;day++)
        {
            var plan=ReignWarEconomyPolicy.AllocateRelief(holdings);
            foreach(var shipment in plan.Shipments)
            { holdings.Single(x=>x.Id==shipment.Donor).Stock-=shipment.Shipped;holdings.Single(x=>x.Id==shipment.Recipient).Stock+=shipment.Delivered; }
            foreach(var h in holdings) h.Stock=ReignWarEconomyPolicy.Clamp(h.Stock+h.Change,0,h.Capacity);
            exhausted |= holdings[0].Stock==0 && plan.UnmetFood>0;
        }
        Assert.True(exhausted);Assert.True(holdings.Skip(1).All(x=>x.Stock<360));
    }

    [Fact]
    public void FoodZeroStreakRequiresDistinctAdjacentObservedDays()
    {
        Assert.Equal(3,ReignWarEconomyPolicy.NextFoodZeroStreak(12,11,true,2));
        Assert.Equal(2,ReignWarEconomyPolicy.NextFoodZeroStreak(11,11,true,2));
        Assert.Equal(2,ReignWarEconomyPolicy.NextFoodZeroStreak(11,11,false,2)); //first sample owns the day.
        Assert.Equal(1,ReignWarEconomyPolicy.NextFoodZeroStreak(13,11,true,2));
        Assert.Equal(0,ReignWarEconomyPolicy.NextFoodZeroStreak(12,11,false,2));
    }

    [Fact]
    public void VariedReliefNetworksConserveStockRespectRoutesAndNeverReexportIncomingAid()
    {
        var random=new Random(1729);
        for(int trial=0;trial<120;trial++)
        {
            var holdings=Enumerable.Range(0,12).Select(i=>new ReignWarEconomyPolicy.Holding {
                Id="h"+i,Capacity=100+random.Next(600),Change=random.Next(-30,31),X=random.Next(241),Y=random.Next(241),
                Security=random.Next(101),Besieged=random.Next(12)==0 }).ToArray();
            foreach(var h in holdings) h.Stock=random.NextDouble()*h.Capacity;
            var stocks=holdings.ToDictionary(x=>x.Id,x=>x.Stock);
            var plan=ReignWarEconomyPolicy.AllocateRelief(holdings);
            var reverse=ReignWarEconomyPolicy.AllocateRelief(holdings.Reverse());
            Assert.All(holdings,h=>Assert.Equal(stocks[h.Id],h.Stock));
            Assert.Equal(plan.Shipments.Select(x=>(x.Donor,x.Recipient,x.Shipped,x.Delivered)),
                reverse.Shipments.Select(x=>(x.Donor,x.Recipient,x.Shipped,x.Delivered)));
            Assert.Empty(plan.Shipments.Select(x=>x.Donor).Intersect(plan.Shipments.Select(x=>x.Recipient)));
            foreach(var s in plan.Shipments)
            {
                var d=holdings.Single(x=>x.Id==s.Donor);var r=holdings.Single(x=>x.Id==s.Recipient);
                Assert.False(d.Besieged || r.Besieged);
                Assert.True(Math.Sqrt(Math.Pow(d.X-r.X,2)+Math.Pow(d.Y-r.Y,2))<=180);
                stocks[s.Donor]-=s.Shipped;stocks[s.Recipient]+=s.Delivered;
                Assert.True(stocks[s.Donor]>=plan.DonorFloor*d.Capacity-1e-6);
                Assert.True(stocks[s.Donor]+7*d.Change>=plan.DonorFloor*d.Capacity-1e-6);
                Assert.True((stocks[s.Donor]+7*d.Change)/d.Capacity+1e-6 >= (stocks[s.Recipient]+7*r.Change)/r.Capacity);
                Assert.InRange(stocks[s.Recipient],0,r.Capacity+1e-6);
                Assert.InRange(s.Delivered/s.Shipped,.699999,.900001);
            }
            Assert.Equal(holdings.Sum(x=>x.Stock)-plan.Shipments.Sum(x=>x.Shipped-x.Delivered),stocks.Values.Sum(),7);
            Assert.All(plan.Shipments.GroupBy(x=>x.Donor),g=>Assert.InRange(g.Sum(x=>x.Shipped),0,30.000001));
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidNativeReliefInputsCannotSilentlyProduceAnEmptySuccessfulPlan(double invalid)
    {
        foreach(var set in new Action<ReignWarEconomyPolicy.Holding>[] { h=>h.Stock=invalid,h=>h.Capacity=invalid,
            h=>h.Change=invalid,h=>h.X=invalid,h=>h.Y=invalid,h=>h.Security=invalid })
        {
            var h=Holding("town",100);set(h);
            Assert.Throws<ArgumentException>(()=>ReignWarEconomyPolicy.AllocateRelief(new[]{h}));
        }
    }

    [Fact]
    public void DuplicateReliefIdentitiesCannotMergeDistinctStocks()
        => Assert.Throws<ArgumentException>(()=>ReignWarEconomyPolicy.AllocateRelief(new[]{Holding("same",100),Holding("same",50)}));

    [Fact]
    public void BlockedTransitSpoilageDoesNotDependOnHourlyTickFrequency()
    {
        double hourly = 100;
        for (int i = 0; i < 24; i++) hourly = ReignWarEconomyPolicy.BlockedCargo(hourly, 1d / 24);
        Assert.Equal(98, hourly, 8);
        Assert.Equal(96.04, ReignWarEconomyPolicy.BlockedCargo(100, 2), 8);
        Assert.Equal(.8, ReignWarEconomyPolicy.Stress(.4, .5, .2, .8));
    }
}
