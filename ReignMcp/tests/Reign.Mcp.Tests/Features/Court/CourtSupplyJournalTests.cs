using System;
using Newtonsoft.Json;
using ReignBeta.Court.Campaign;
using Xunit;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class CourtSupplyJournalTests
{
    [Fact]
    public void ExactPaidSupplySurvivesReloadWithoutAnotherNativeLeg()
    {
        var journal = new ReignCourtSupplyJournal();
        var world = new Holdings();

        var receipt = Assert.IsType<CourtPaidSupplyReceipt>(world.Apply(journal));
        Assert.True(receipt.ProvesCompletion(4, 10));
        Assert.True(world.Read().Matches(16, 7, 90, 40));
        Assert.True(receipt.AfterRemoval?.Matches(16, 3, 100, 30) == true);
        Assert.True(receipt.AfterDelivery?.Matches(16, 7, 100, 30) == true);
        Assert.Equal(3, world.Calls);

        var reloaded = Reload(journal);
        world.Payer -= 5; // Later independent work cannot erase the saved receipt or rerun the command.
        Assert.True(world.Apply(reloaded)?.ProvesCompletion(4, 10) == true);
        Assert.Equal(3, world.Calls);
        Assert.True(world.Read().Matches(16, 7, 85, 40));

        Assert.Throws<InvalidOperationException>(() => world.Apply(reloaded, binding: "different restored timeline or terms"));
        Assert.Equal(3, world.Calls);
    }

    [Theory]
    [InlineData("no_stock_debit", 20, 3, 100, 30, 1)]
    [InlineData("no_stock_credit", 16, 3, 100, 30, 2)]
    [InlineData("authority_loss", 16, 3, 100, 30, 1)]
    [InlineData("debit_only", 16, 7, 90, 30, 3)]
    [InlineData("credit_only", 16, 7, 100, 40, 3)]
    [InlineData("callback_after_both", 16, 7, 90, 40, 3)]
    [InlineData("unrelated_change", 16, 7, 89, 40, 3)]
    public void AmbiguousPaidSupplyRetainsObservedEffectsWithoutCompensationOrReplay(string failure,
        int source, int target, int payer, int recipient, int calls)
    {
        var journal = new ReignCourtSupplyJournal();
        var world = new Holdings { Failure = failure };

        var receipt = Assert.IsType<CourtPaidSupplyReceipt>(world.Apply(journal));
        Assert.Equal(CourtPaidSupplyPhase.Held, receipt.Phase);
        Assert.False(receipt.ProvesCompletion(4, 10));
        Assert.True(world.Read().Matches(source, target, payer, recipient));
        var observed = receipt.AfterPayment ?? receipt.AfterDelivery ?? receipt.AfterRemoval;
        Assert.True(observed?.Matches(source, target, payer, recipient) == true);
        Assert.Equal(calls, world.Calls);

        var reloaded = Reload(journal);
        world.Failure = "";
        var replay = Assert.IsType<CourtPaidSupplyReceipt>(world.Apply(reloaded));
        Assert.Equal(CourtPaidSupplyPhase.Held, replay.Phase);
        Assert.False(replay.ProvesCompletion(4, 10));
        Assert.True(world.Read().Matches(source, target, payer, recipient));
        Assert.Equal(calls, world.Calls);
    }

    [Theory]
    [InlineData(3, 3, 100, 30)]
    [InlineData(20, int.MaxValue - 3, 100, 30)]
    [InlineData(20, 3, 9, 30)]
    [InlineData(20, 3, 100, int.MaxValue - 9)]
    public void UnfundedOrOverflowingSupplyInvokesNoNativeLeg(int source, int target, int payer, int recipient)
    {
        var journal = new ReignCourtSupplyJournal();
        var world = new Holdings { Source = source, Target = target, Payer = payer, Recipient = recipient };
        Assert.Null(world.Apply(journal));
        Assert.Empty(journal.Receipts);
        Assert.Equal(0, world.Calls);
        Assert.True(world.Read().Matches(source, target, payer, recipient));
    }

    [Fact]
    public void UnownedPaidSupplyStockCreatesNoAttemptAndMovesNothing()
    {
        var journal = new ReignCourtSupplyJournal();
        var world = new Holdings();
        Assert.Null(journal.Apply("supply-command", "owned terms required", 4, 10,
            () => false, world.Read, () => world.Calls++, () => world.Calls++, () => world.Calls++));
        Assert.Empty(journal.Receipts);
        Assert.Equal(0, world.Calls);
        Assert.True(world.Read().Matches(20, 3, 100, 30));
    }

    [Fact]
    public void SaveInsideNativePaymentCannotUpgradeVisibleBalancesToACallbackReturn()
    {
        var journal = new ReignCourtSupplyJournal();
        var world = new Holdings();
        ReignCourtSupplyJournal? interrupted = null;
        world.OnPay = () =>
        {
            interrupted = Reload(journal);
            Assert.Equal(CourtPaidSupplyPhase.Paying, Assert.IsType<CourtPaidSupplyReceipt>(world.Apply(journal)).Phase);
        };

        Assert.True(world.Apply(journal)?.ProvesCompletion(4, 10) == true);
        var fresh = Assert.IsType<ReignCourtSupplyJournal>(interrupted);
        var pending = Assert.IsType<CourtPaidSupplyReceipt>(world.Apply(fresh));
        Assert.Equal(CourtPaidSupplyPhase.Paying, pending.Phase);
        Assert.False(pending.ProvesCompletion(4, 10));
        Assert.True(world.Read().Matches(16, 7, 90, 40));
        Assert.Equal(3, world.Calls);
    }

    private static ReignCourtSupplyJournal Reload(ReignCourtSupplyJournal journal)
    {
        var restored = JsonConvert.DeserializeObject<ReignCourtSupplyJournal>(JsonConvert.SerializeObject(journal))
            ?? throw new InvalidOperationException("No saved supply journal.");
        restored.EnsureReadable();
        return restored;
    }

    private sealed class Holdings
    {
        public int Source = 20, Target = 3, Payer = 100, Recipient = 30;
        public int Calls;
        public string Failure = "";
        public Action? OnPay;

        public CourtPaidSupplyBalances Read() => new(Source, Target, Payer, Recipient);

        public CourtPaidSupplyReceipt? Apply(ReignCourtSupplyJournal journal, string binding = "original campaign and terms")
        {
            return journal.Apply("supply-command", binding, 4, 10,
                () => Failure != "authority_loss" || Calls == 0, Read,
                () => { ++Calls; if (Failure != "no_stock_debit") Source -= 4; },
                () => { ++Calls; if (Failure != "no_stock_credit") Target += 4; },
                () =>
                {
                    ++Calls;
                    if (Failure != "credit_only") Payer -= 10;
                    if (Failure != "debit_only") Recipient += 10;
                    if (Failure == "unrelated_change") Payer--;
                    OnPay?.Invoke();
                    if (Failure == "callback_after_both") throw new InvalidOperationException("A native listener interrupted payment.");
                });
        }
    }
}
