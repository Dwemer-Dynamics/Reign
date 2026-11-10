using ReignBeta.Economy;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class ReignWarEconomyLedgerTests
{
    [Fact]
    public void NavalReceiptsPersistUnresolvedPaymentWithoutReplayingOrChangingLegacyBalances()
    {
        var options = new JsonSerializerOptions { IncludeFields = true };
        var state = JsonSerializer.Deserialize<ReignEconomyState>("{\"Version\":2,\"NextOperation\":17}", options)!;
        Assert.Equal(1, state.NavalVersion); Assert.Empty(state.NavalPending); Assert.Empty(state.NavalRecent);
        state.NavalPending.Add(new EconomyNavalReceipt { Id = 18, Hull = "hull", ShipRandom = 12,
            Quote = 30000, WalletBefore = 70000, WalletAfter = 40000, MarketBefore = 100000,
            MarketAfter = 130000, Status = "pending", Buying = true });
        state.NavalAcquisitions["ship_receipt"] = new EconomyShipAcquisition { Id = "ship_receipt", Kind = "npc_purchase", Paid = 300, Hull = "hull" };
        var copy = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.Equal(17, copy.NextOperation); Assert.Single(copy.NavalPending); Assert.Empty(copy.NavalRecent);
        Assert.Equal(40000, copy.NavalPending[0].WalletAfter); Assert.Equal("pending", copy.NavalPending[0].Status);
        Assert.Equal(300, copy.NavalAcquisitions["ship_receipt"].Paid);
        Assert.Equal("npc_purchase", copy.NavalAcquisitions["ship_receipt"].Kind);
    }

    [Fact]
    public void StandingPressureAndActualDrawdownCreditsSurviveSaveWithoutHistoricalReconstruction()
    {
        var state = new ReignEconomyState();
        state.MilitaryStandings["realm"] = new EconomyStandingOrder { Mode = "peace", Ruler = "ruler", Revision = 3, PeacePercent = 40, Automatic = false };
        state.MilitaryPressure.Add(new EconomyPressurePair { Actor = "foreign", Target = "realm", Value = 60 });
        state.DrawdownDays["party:generation1"] = 42;
        state.DrawdownCarry["party:generation1"] = new EconomyDrawdownCarry { Kingdom = "realm", Tiers = new() { [1] = .4m, [6] = -.4m } };
        state.DrawdownRecent.Add(new EconomyDrawdownReceipt { Party = "party", Generation = "generation1", Day = 42,
            Removed = 8, HearthReturned = 2, Status = "completed", HearthCredits = new() { ["village"] = 2 },
            AllocationVersion = 2, HealthyByTierBefore = new() { [1] = 15, [6] = 5 }, HealthyByTierAfter = new() { [1] = 9, [6] = 3 } });
        state.DemobilizedRegulars = 8; state.DemobilizationHearthReturned = 2;
        var options = new JsonSerializerOptions { IncludeFields = true };
        var copy = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.Equal(3, copy.MilitaryStandings["realm"].Revision);
        Assert.Equal(40, copy.MilitaryStandings["realm"].PeacePercent);
        Assert.False(copy.MilitaryStandings["realm"].Automatic);
        Assert.Equal(60, copy.MilitaryPressure[0].Value);
        Assert.Equal(42, copy.DrawdownDays["party:generation1"]);
        Assert.False(copy.DrawdownDays.ContainsKey("party:generation2"));
        Assert.Equal(.4m, copy.DrawdownCarry["party:generation1"].Tiers[1]);
        Assert.Equal(-.4m, copy.DrawdownCarry["party:generation1"].Tiers[6]);
        Assert.False(copy.DrawdownCarry.ContainsKey("party:generation2"));
        Assert.Equal(2, copy.DrawdownRecent[0].AllocationVersion);
        Assert.Equal(5, copy.DrawdownRecent[0].HealthyByTierBefore[6]);
        Assert.Equal(3, copy.DrawdownRecent[0].HealthyByTierAfter[6]);
        Assert.Equal(2, copy.DrawdownRecent[0].HearthCredits["village"]);
        Assert.Equal(8, copy.DemobilizedRegulars);
        Assert.Empty(copy.DrawdownPending);
        var legacy = JsonSerializer.Deserialize<ReignEconomyState>("{\"Version\":2}", options)!;
        Assert.Empty(legacy.DrawdownDays); Assert.Empty(legacy.DrawdownCarry);
        Assert.Empty(legacy.MilitaryStandings); Assert.Equal(0, legacy.DemobilizedRegulars);
    }
    [Fact]
    public void TariffHistoryIsBoundedWhileTotalsAndOwnerAttributionSurviveReload()
    {
        var state = new ReignEconomyState();
        for (int i = 0; i < 140; i++) ReignWarEconomyLedger.RecordTariff(state, new EconomyTariffReceipt {
            Day = 100 + i, Town = "town", OwnerClan = i < 70 ? "old_owner" : "new_owner",
            BuyerClass = "same_owner_military", Purpose = "native_food", Quantity = 1, Received = 1,
            Removed = 1, BuyerDebit = 10, MarketChange = 10, Exempt = true, Supported = true });
        var options = new JsonSerializerOptions { IncludeFields = true };
        var copy = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.Equal(140, copy.TariffSequence);
        Assert.Equal(128, copy.TariffRecent.Count);
        Assert.Equal(13, copy.TariffRecent[0].Id);
        Assert.Equal(2, copy.TariffSales.Count);
        Assert.All(copy.TariffSales.Values, x => { Assert.Equal(70, x.Purchases); Assert.Equal(700, x.BuyerDebit); Assert.Equal(0, x.PoolCredit); });
        Assert.Equal(0, copy.TariffUnsupported);
        ReignWarEconomyLedger.RecordTariff(copy, new EconomyTariffReceipt { Town = "town", OwnerClan = "new_owner",
            BuyerClass = "same_owner_military", Purpose = "native_food", Supported = false });
        Assert.Equal(1, copy.TariffUnsupported);
    }
    [Fact]
    public void NativeArrivalReceiptsPersistDuplicateProtectionWithoutChangingFutureCadence()
    {
        var state = new ReignEconomyState();
        Assert.True(ReignWarEconomyLedger.BeginNativeArrival(state, "naval:caravan:generation:town", 100.25));
        var reloaded = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state,
            new JsonSerializerOptions { IncludeFields = true }), new JsonSerializerOptions { IncludeFields = true })!;
        Assert.False(ReignWarEconomyLedger.BeginNativeArrival(reloaded, "naval:caravan:generation:town", 100.25));
        Assert.True(ReignWarEconomyLedger.BeginNativeArrival(reloaded, "naval:caravan:generation:town", 100.26));
        Assert.True(ReignWarEconomyLedger.BeginNativeArrival(reloaded, "naval:caravan:recreated:town", 100.26));
        Assert.False(ReignWarEconomyLedger.BeginNativeArrival(reloaded, "invalid", double.NaN));
    }

    [Fact]
    public void NativeArrivalHistoryRefusesOverflowUntilOldReceiptsCanExpire()
    {
        var state = new ReignEconomyState();
        for (int i = 0; i < 8192; i++) Assert.True(ReignWarEconomyLedger.BeginNativeArrival(state, "arrival:" + i, 100));
        Assert.False(ReignWarEconomyLedger.BeginNativeArrival(state, "overflow", 100));
        Assert.Equal(1, state.UnsupportedEvents);
        Assert.Equal(8192, state.NativeArrivalDays.Count);
        Assert.True(ReignWarEconomyLedger.BeginNativeArrival(state, "new", 104));
        Assert.Single(state.NativeArrivalDays);
    }
    [Theory]
    [InlineData(14913, 16354, 0L, 0L, true, 1441L)]
    [InlineData(14913, 16354, 0L, 1441L, true, 0L)]
    [InlineData(100, 300, 500L, 550L, true, 150L)]
    [InlineData(0, 0, -100L, -100L, true, 0L)]
    [InlineData(100, 120, -100L, -80L, true, 0L)]
    [InlineData(100, 120, 0L, 40L, false, 0L)]
    [InlineData(100, 120, 0L, -1L, false, 0L)]
    [InlineData(100, 99, 0L, 0L, false, 0L)]
    [InlineData(-1, 20, 0L, 0L, false, 0L)]
    [InlineData(0, 20, long.MinValue, long.MaxValue, false, 0L)]
    public void NativeCashComponentAccountsOnlyMissingCreditAndRejectsDoubleObservation(
        int before, int after, long accountedBefore, long accountedAfter, bool valid, long expected)
    {
        Assert.Equal(valid, ReignWarEconomyLedger.TryNativeCreditRemainder(before, after, accountedBefore, accountedAfter, out long remainder));
        Assert.Equal(expected, remainder);
    }

    private sealed class NativeIdentityStandIn
    {
        public int Id;
        public override int GetHashCode() => Id;
    }

    [Fact]
    public void RegistrationTrackingSurvivesNativeIdentityHashChangeWithoutConfusingInstances()
    {
        var first = new NativeIdentityStandIn(); var other = new NativeIdentityStandIn();
        var parties = new Dictionary<NativeIdentityStandIn, string>(ReignWarEconomyReferenceComparer<NativeIdentityStandIn>.Instance)
            { [first] = "pending-first", [other] = "pending-other" };
        first.Id = 939532583; other.Id = first.Id;
        Assert.Equal("pending-first", parties[first]);
        Assert.Equal("pending-other", parties[other]);
        Assert.True(parties.Remove(first));
        Assert.False(parties.ContainsKey(first));
        Assert.Equal("pending-other", Assert.Single(parties).Value);
    }

    [Fact]
    public void UnregisteredPartiesReceiveDistinctReceiptsThenPermanentNativeIdentity()
    {
        var state = new ReignEconomyState();
        state.PartyGenerations["lord:0"] = "committed:12"; // Ambiguous historical entry cannot exempt a new creation.
        string first = ReignWarEconomyLedger.ProvisionalGeneration(state);
        state.PartyGenerations["lord:" + first] = "committed:10";
        var receipt = Begin(state, "household_retinue");
        receipt.Party = "lord"; receipt.PartyGeneration = first;
        ReignWarEconomyLedger.RegisterGeneration(state, "lord", first, "14001");
        Assert.Equal("14001", receipt.PartyGeneration);
        Assert.Equal("committed:10", state.PartyGenerations["lord:14001"]);
        Assert.False(state.PartyGenerations.ContainsKey("lord:" + first));
        var options = new JsonSerializerOptions { IncludeFields = true };
        state = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        string second = ReignWarEconomyLedger.ProvisionalGeneration(state);
        Assert.NotEqual(first, second);
        state.PartyGenerations["lord:" + second] = "committed:11";
        ReignWarEconomyLedger.RegisterGeneration(state, "lord", second, "14002");
        Assert.Equal(3, state.PartyGenerations.Count);
        Assert.Equal("committed:12", state.PartyGenerations["lord:0"]);
        Assert.Equal("14001", Assert.Single(state.Pending).PartyGeneration);
        Assert.Equal(2, state.NextPartyGeneration);
        Assert.Throws<InvalidOperationException>(() => ReignWarEconomyLedger.RegisterGeneration(state, "lord", second, "14002"));
        Assert.Throws<InvalidOperationException>(() => ReignWarEconomyLedger.RegisterGeneration(state, "lord", second, "0"));
    }

    [Fact]
    public void NativeWageQuotesDoNotBecomePaymentsAndObservedUnpaidWagesSurviveReload()
    {
        var state = new ReignEconomyState();
        ReignWarEconomyLedger.ObserveWages(state, "payer", 20, 120, 120, false);
        Assert.Empty(state.Finance);
        ReignWarEconomyLedger.ObserveWages(state, "payer", 20, 120, 50, true);
        ReignWarEconomyLedger.ObserveWages(state, "payer", 21, 120, 0, true);
        var options = new JsonSerializerOptions { IncludeFields = true };
        state = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.Equal(240, state.Finance["payer"].WagesOwed);
        Assert.Equal(50, state.Finance["payer"].WagesPaid);
        Assert.Equal(2, state.Finance["payer"].WagePayments);
        Assert.Equal(21, state.Finance["payer"].LastPaymentDay);
        Assert.Empty(state.Recent); // Observations never invent recruitment or historical cash transactions.
    }

    [Theory]
    [InlineData(true, null, "current_owner", "current_owner")]
    [InlineData(true, "former_owner", "current_owner", "current_owner")]
    [InlineData(true, "former_owner", null, null)]
    [InlineData(false, "field_clan", "visited_town_owner", "field_clan")]
    public void WageAttributionFollowsGarrisonOwnershipWithoutInventingAnUnknownPayer(
        bool garrison, string? actualClan, string? owner, string? expected)
        => Assert.Equal(expected, ReignWarEconomyLedger.WagePayer(garrison, actualClan!, owner!));

    [Fact]
    public void WageObservationSeparatesCurrentGarrisonAndFieldPaymentsWithoutReconstructingHistory()
    {
        var state = new ReignEconomyState();
        state.Finance["payer"] = new EconomyFinanceTotals { WagesOwed = 1000, WagesPaid = 900, WagePayments = 3 };
        ReignWarEconomyLedger.ObserveWages(state, "payer", 20, 120, 50, true, true);
        ReignWarEconomyLedger.ObserveWages(state, "payer", 20, 80, 80, true);
        ReignWarEconomyLedger.ObserveWages(state, null!, 20, 90, 90, false, true);
        ReignWarEconomyLedger.ObserveWages(state, null!, 20, 90, 90, true, true);
        var options = new JsonSerializerOptions { IncludeFields = true };
        state = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        var observed = state.Finance["payer"];
        Assert.Equal(1200, observed.WagesOwed); Assert.Equal(1030, observed.WagesPaid);
        Assert.Equal(5, observed.WagePayments);
        Assert.Equal(120, observed.GarrisonWagesOwed); Assert.Equal(50, observed.GarrisonWagesPaid);
        Assert.Equal(80, observed.FieldWagesOwed); Assert.Equal(80, observed.FieldWagesPaid);
        Assert.Equal(1, observed.GarrisonWagePayments); Assert.Equal(1, observed.FieldWagePayments);
        Assert.Equal(1, state.UnattributedWagePayments);
        Assert.Empty(state.Pending); Assert.Empty(state.Recent);
    }

    private static EconomyReceipt Begin(ReignEconomyState state, string source, int requested = 3)
    {
        var receipt = new EconomyReceipt { Id = (++state.NextOperation).ToString(), Source = source, Requested = requested,
            QuotedCost = 20, Status = "pending", Party = "lord_1", PartyGeneration = "100" };
        receipt.Population["village"] = new[] { 300f, 2f };
        receipt.Roster["recruit"] = new[] { 10, 0, 0 };
        receipt.Gold["payer"] = 1000;
        state.Pending.Add(receipt);
        return receipt;
    }

    [Theory]
    [InlineData("player_volunteer", false)]
    [InlineData("ai_volunteer", false)]
    [InlineData("native_auto_garrison", true)]
    [InlineData("native_base_garrison", true)]
    [InlineData("castle_guard", false)]
    [InlineData("household_retinue", false)]
    [InlineData("militia_conversion", false)]
    public void EverySourceRecordsActualPartialRecruitmentExactlyOnce(string source, bool liability)
    {
        var state = new ReignEconomyState(); var receipt = Begin(state, source);
        Assert.True(ReignWarEconomyLedger.Finish(state, receipt, 2, 1, liability ? 0 : 40, liability ? 40 : 0));
        Assert.False(ReignWarEconomyLedger.Finish(state, receipt, 2, 1, 40, 0));
        Assert.Empty(state.Pending);
        Assert.Equal(2, state.Sources[source].Enlisted);
        Assert.Equal(1, state.Sources[source].Hearth);
        Assert.Equal(40, state.Sources[source].CashExpense + state.Sources[source].Liability);
        Assert.Empty(receipt.Population);
    }

    [Fact]
    public void SaveReloadPreservesPendingBeforeImagesAndCannotReplayCommittedReceipts()
    {
        var state = new ReignEconomyState(); Begin(state, "player_volunteer");
        var options = new JsonSerializerOptions { IncludeFields = true };
        state = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        var pending = Assert.Single(state.Pending);
        Assert.Equal(300, pending.Population["village"][0]);
        Assert.Equal("100", pending.PartyGeneration);
        Assert.True(ReignWarEconomyLedger.Finish(state, pending, 0, 0, 0, 0, "rolled_back"));
        state = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.False(ReignWarEconomyLedger.Finish(state, state.Recent[0], 3, 1.5, 60, 0));
        Assert.Equal(0, state.Sources["player_volunteer"].Hearth);
        Assert.Equal(1, state.NextOperation);
    }

    [Theory]
    [InlineData(4, 2, 40, 0)]
    [InlineData(3, 1, 40, 0)]
    [InlineData(0, 0, 40, 0)]
    [InlineData(1, .5, 20, 20)]
    [InlineData(1, .5, -1, 0)]
    [InlineData(1, .5, 0, 0)]
    [InlineData(1, .5, 19, 0)]
    public void InvalidConservationCannotCommit(int troops, double hearth, double cash, double liability)
    {
        var state = new ReignEconomyState(); var receipt = Begin(state, "ai_volunteer");
        Assert.Throws<InvalidOperationException>(() => ReignWarEconomyLedger.Finish(state, receipt, troops, hearth, cash, liability));
        Assert.Single(state.Pending);
        Assert.Equal("pending", receipt.Status);
        Assert.Empty(state.Sources);
    }

    [Fact]
    public void CancelledOperationsCostNothingAndHistoryIsBoundedWithoutLosingTotals()
    {
        var state = new ReignEconomyState();
        for (int i = 0; i < 600; i++)
            ReignWarEconomyLedger.Finish(state, Begin(state, "castle_guard"), i % 2, i % 2 * .5, i % 2 * 20, 0);
        Assert.Equal(512, state.Recent.Count);
        Assert.Equal(300, state.Sources["castle_guard"].Enlisted);
        Assert.Equal(150, state.Sources["castle_guard"].Hearth);
        Assert.Equal(6000, state.Sources["castle_guard"].CashExpense);
    }

    [Fact]
    public void LegacyTariffReceiptsRemainHistoricalWhenGuardedPartialPurchasesAreAdded()
    {
        var state = new ReignEconomyState();
        var legacy = JsonSerializer.Deserialize<EconomyTariffReceipt>("{\"Version\":2,\"Town\":\"town\",\"Quantity\":1,\"NativePrice\":7653,\"BuyerDebit\":3347,\"Supported\":false}")!;
        ReignWarEconomyLedger.RecordTariff(state, legacy);
        ReignWarEconomyLedger.RecordTariff(state, new EconomyTariffReceipt { Version = 3, Town = "town", Quantity = 4,
            Received = 2, Removed = 2, BuyerDebit = 85, NativePrice = 185, QuotedUnits = 4,
            FulfilledUnits = 2, AffordabilityBlockedUnits = 2, FulfilledNativePrice = 85, AffordabilityGuarded = true, Supported = true });
        var totals = Assert.Single(state.TariffSales).Value;
        Assert.Equal(3432, totals.BuyerDebit); Assert.Equal(85, totals.FulfilledNativePrice);
        Assert.Equal(2, totals.AffordabilityBlockedUnits); Assert.Equal(1, totals.Unsupported);
        Assert.Equal(2, state.TariffRecent[0].Version); Assert.False(state.TariffRecent[0].Supported);
        var options = new JsonSerializerOptions { IncludeFields = true };
        var loaded = JsonSerializer.Deserialize<ReignEconomyState>(JsonSerializer.Serialize(state, options), options)!;
        Assert.Equal(85, loaded.TariffRecent[1].FulfilledNativePrice);
        Assert.Equal(0, loaded.TariffRecent[0].FulfilledNativePrice);
    }
}
