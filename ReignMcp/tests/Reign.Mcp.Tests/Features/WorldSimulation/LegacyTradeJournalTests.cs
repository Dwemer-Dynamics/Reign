using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class LegacyTradeJournalTests
{
    private sealed class Holding
    {
        internal Holding(string id, int amount)
        {
            Id = id;
            Amount = amount;
        }

        internal string Id { get; }
        internal int Amount { get; set; }
        internal bool RefuseRestore { get; set; }

        internal LegacyTradeBalance Balance => new(Id, () => Amount, amount =>
        {
            if (RefuseRestore) throw new InvalidOperationException("restore refused");
            Amount = amount;
        });
    }

    private static LegacyTradeLeg Move(Holding from, Holding to, int amount, bool omitCredit = false, bool throwAfterDebit = false)
        => new(from.Balance, to.Balance, amount, () =>
        {
            from.Amount -= amount;
            if (throwAfterDebit) throw new InvalidOperationException("native callback failed");
            if (!omitCredit) to.Amount += amount;
        });

    private static JObject Done() => new() { ["verified"] = true };

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void FaultInAnyLegRestoresGoldAndEveryStackAndReloadCannotReplay(int failingLeg, bool throws)
    {
        var payer = new Holding("payer", 100);
        var payee = new Holding("payee", 25);
        var grainSeller = new Holding("grain:seller", 10);
        var grainBuyer = new Holding("grain:buyer", 2);
        var horseSeller = new Holding("horse:seller", 6);
        var horseBuyer = new Holding("horse:buyer", 1);
        LegacyTradeLeg Leg(int index, Holding from, Holding to, int amount)
            => Move(from, to, amount, index == failingLeg && !throws, index == failingLeg && throws);

        var ledger = new LegacyTradeJournal();
        var receipt = ledger.Execute("sale", "signed terms", new[]
        {
            Leg(0, payer, payee, 30),
            Leg(1, grainSeller, grainBuyer, 4),
            Leg(2, horseSeller, horseBuyer, 2)
        }, () => throw new Exception("The final verifier should not run."));

        Assert.Equal(LegacyTradeJournal.Restored, receipt.State);
        Assert.Equal(failingLeg + 1, receipt.AttemptedLegs);
        Assert.Empty(receipt.Unresolved);
        Assert.Equal((100, 25, 10, 2, 6, 1),
            (payer.Amount, payee.Amount, grainSeller.Amount, grainBuyer.Amount, horseSeller.Amount, horseBuyer.Amount));

        var loaded = RoundTrip(ledger);
        var replay = loaded.Execute("sale", "signed terms", new[] { Move(payer, payee, 30) }, Done);
        Assert.Equal(LegacyTradeJournal.Restored, replay.State);
        Assert.Equal((100, 25), (payer.Amount, payee.Amount));
    }

    [Fact]
    public void FailedCompensationRetainsExactUnresolvedHoldingAndOtherRestoresContinue()
    {
        var payer = new Holding("payer", 100);
        var payee = new Holding("payee", 25) { RefuseRestore = true };
        var seller = new Holding("grain:seller", 10);
        var buyer = new Holding("grain:buyer", 2);
        var ledger = new LegacyTradeJournal();
        var receipt = ledger.Execute("sale", "terms", new[] { Move(payer, payee, 30), Move(seller, buyer, 4, true) }, Done);

        Assert.Equal(LegacyTradeJournal.RecoveryRequired, receipt.State);
        var unresolved = Assert.Single(receipt.Unresolved);
        Assert.Equal("payee", unresolved.Value<string>("holding"));
        Assert.Equal(25, unresolved.Value<int>("before"));
        Assert.Equal(55, unresolved.Value<int>("expected"));
        Assert.Equal(55, unresolved.Value<int>("observed"));
        Assert.Equal("restore refused", unresolved.Value<string>("rollbackError"));
        Assert.Equal((100, 55, 10, 2), (payer.Amount, payee.Amount, seller.Amount, buyer.Amount));

        var loaded = RoundTrip(ledger);
        Assert.True(JToken.DeepEquals(receipt.Unresolved, loaded.Find("sale", "terms").Unresolved));
        Assert.Equal(LegacyTradeJournal.RecoveryRequired,
            loaded.Execute("sale", "terms", new[] { Move(payer, payee, 30) }, Done).State);
        Assert.Equal((100, 55), (payer.Amount, payee.Amount));
    }

    [Fact]
    public void UnexpectedNativeCountRemainsForRecovery()
    {
        var seller = new Holding("inventory:seller", 10);
        var buyer = new Holding("inventory:buyer", 2);
        var receipt = new LegacyTradeJournal().Execute("sale", "terms", new[]
        {
            new LegacyTradeLeg(seller.Balance, buyer.Balance, 4, () =>
            {
                seller.Amount -= 3;
                buyer.Amount += 4;
            })
        }, Done);

        Assert.Equal(LegacyTradeJournal.RecoveryRequired, receipt.State);
        Assert.Equal((7, 2), (seller.Amount, buyer.Amount));
        Assert.Equal(7, Assert.Single(receipt.Unresolved).Value<int>("observed"));
    }

    [Fact]
    public void WholeBatchPreflightRejectsOvercommittedStackBeforeGoldMoves()
    {
        var payer = new Holding("payer", 100);
        var payee = new Holding("payee", 25);
        var seller = new Holding("grain:seller", 10);
        var buyer = new Holding("grain:buyer", 2);
        var ledger = new LegacyTradeJournal();
        Assert.Throws<InvalidOperationException>(() => ledger.Execute("sale", "terms", new[]
        {
            Move(payer, payee, 30), Move(seller, buyer, 6), Move(seller, buyer, 5)
        }, Done));
        Assert.Empty(ledger.Receipts);
        Assert.Equal((100, 25, 10, 2), (payer.Amount, payee.Amount, seller.Amount, buyer.Amount));
    }

    [Fact]
    public void FinalReceiptFailureRestoresRepeatedHoldings()
    {
        var payer = new Holding("payer", 100);
        var payee = new Holding("payee", 25);
        var seller = new Holding("grain:seller", 10);
        var buyer = new Holding("grain:buyer", 2);
        var receipt = new LegacyTradeJournal().Execute("sale", "terms", new[]
        {
            Move(payer, payee, 30), Move(seller, buyer, 4), Move(seller, buyer, 3)
        }, () => throw new InvalidOperationException("promised receipt did not match"));

        Assert.Equal(LegacyTradeJournal.Restored, receipt.State);
        Assert.Equal((100, 25, 10, 2), (payer.Amount, payee.Amount, seller.Amount, buyer.Amount));
    }

    [Fact]
    public void SuccessfulRoundTripReturnsTheExactReceiptWithoutMutationAndRejectsChangedTerms()
    {
        var payer = new Holding("payer", 100);
        var payee = new Holding("payee", 25);
        var ledger = new LegacyTradeJournal();
        var receipt = ledger.Execute("sale", "terms", new[] { Move(payer, payee, 30) }, Done);
        Assert.Equal(LegacyTradeJournal.Applied, receipt.State);

        var loaded = RoundTrip(ledger);
        Assert.True(JToken.DeepEquals(receipt.Result,
            loaded.Execute("sale", "terms", new[] { Move(payer, payee, 30) }, Done).Result));
        Assert.Throws<InvalidOperationException>(() =>
            loaded.Execute("sale", "changed terms", new[] { Move(payer, payee, 1) }, Done));
        Assert.Equal((70, 55), (payer.Amount, payee.Amount));
    }

    [Fact]
    public void ReentryAndSavedInFlightReceiptNeverRepeatAStartedLeg()
    {
        var source = new Holding("source", 100);
        var destination = new Holding("destination", 25);
        var ledger = new LegacyTradeJournal();
        var receipt = ledger.Execute("sale", "terms", new[]
        {
            new LegacyTradeLeg(source.Balance, destination.Balance, 30, () =>
            {
                source.Amount -= 30;
                Assert.Throws<InvalidOperationException>(() =>
                    ledger.Execute("nested-sale", "other terms", new[] { Move(source, destination, 5) }, Done));
                var loaded = RoundTrip(ledger);
                Assert.True(ledger.IsApplying("sale"));
                Assert.False(loaded.IsApplying("sale"));
                foreach (var journal in new[] { ledger, loaded })
                {
                    var inFlight = journal.Execute("sale", "terms", new[] { Move(source, destination, 30) }, Done);
                    Assert.Equal(LegacyTradeJournal.Applying, inFlight.State);
                    Assert.Equal(1, inFlight.AttemptedLegs);
                }
                destination.Amount += 30;
            })
        }, Done);
        Assert.Equal(LegacyTradeJournal.Applied, receipt.State);
        Assert.Equal((70, 55), (source.Amount, destination.Amount));
    }

    private static LegacyTradeJournal RoundTrip(LegacyTradeJournal journal)
        => JsonConvert.DeserializeObject<LegacyTradeJournal>(JsonConvert.SerializeObject(journal))!;
}
