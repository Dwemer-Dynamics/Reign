using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ReignBeta.Shared.Dialogue;
using Xunit;

namespace Reign.Mcp.Tests.Platform;

public sealed class PlayerActionRulesTests
{
    [Theory]
    [InlineData("*I give her the 2000 denars to join as a companion*", 2000, "recruitment_advance")]
    [InlineData("I hand you two thousand gold.", 2000, "gift")]
    [InlineData("Here, take 2,000 denars.", 2000, "gift")]
    [InlineData("I transfer 125 gold to Isolde Oakley.", 125, "gift")]
    [InlineData("I give you one hundred and twenty-five coins.", 125, "gift")]
    public void CurrentPlayerHandoverResolvesExactly(string text, int amount, string purpose)
    {
        var action = Assert.Single(PlayerActionRules.Extract(text, "Isolde Oakley"));
        Assert.Equal("", action.Error); Assert.Equal("transfer_gold", action.Command);
        Assert.Equal(amount, action.Amount); Assert.Equal(purpose, action.Purpose);
        Assert.Contains(action.Quote, text);
    }

    [Theory]
    [InlineData("I gave her 2000 gold yesterday.")]
    [InlineData("I will give you 2000 gold if you join.")]
    [InlineData("I give you 2000 gold if you join.")]
    [InlineData("I do not give you 2000 gold.")]
    [InlineData("I give you 2000 gold, not now.")]
    [InlineData("I give you 2000 gold?")]
    [InlineData("Imagine I give you 2000 gold.")]
    [InlineData("She says: \"I give you 2000 gold\".")]
    [InlineData("The 2000 gold I gave you is an advance.")]
    [InlineData("I give you 2000 gold for your horse.")]
    [InlineData("*She takes 2000 gold from me*")]
    [InlineData("For example:\nI give you 2000 gold.")]
    [InlineData("Imagine this.\nI give you 2000 gold.")]
    [InlineData("If you join:\nI give you 2000 gold.")]
    [InlineData("```text\nI give you 2000 gold\n```")]
    [InlineData("> I give you 2000 gold.")]
    [InlineData("'An example:\nI give you 2000 gold.'")]
    [InlineData("Do not execute this:\nI give you 2000 gold.")]
    [InlineData("A quoted example:\n```\nI give you 2000 gold\n```")]
    public void PromiseQuestionConditionHistoryAndOtherActorCannotDebit(string text)
        => Assert.Empty(PlayerActionRules.Extract(text, "Isolde Oakley"));

    [Theory]
    [InlineData("*I give Pagarious a friendly smile* Nothing in particular, my friend.")]
    [InlineData("I give her a smile.")]
    [InlineData("I give Isolde Oakley a warm hug.")]
    [InlineData("I give you my word.")]
    public void SocialGesturesDoNotQueueHandovers(string text)
        => Assert.Empty(PlayerActionRules.Extract(text, "Isolde Oakley"));

    [Fact]
    public void TangibleGiftWithSmileStillQueuesHandover()
    {
        var action = Assert.Single(PlayerActionRules.Extract("I give Isolde Oakley a silver ring with a smile.", "Isolde Oakley"));
        Assert.Equal("transfer_item", action.Command);
        Assert.Empty(action.Error);
    }

    [Theory]
    [InlineData("0")][InlineData("-1")][InlineData("1.5")][InlineData("2,00")]
    [InlineData("2147483648")][InlineData("two two")][InlineData("thousand")]
    [InlineData("two thousand thousand")][InlineData("twenty thirty")][InlineData("one and")]
    public void InvalidAmountsAreNotGuessed(string amount) => Assert.False(PlayerActionRules.TryAmount(amount, out _));

    [Fact]
    public void SeparateIntentsKeepSlotsAndAmbiguousPeopleCannotReceive()
    {
        Assert.Equal(2, PlayerActionRules.Extract("I give you 100 gold and I hand you 50 coins.", "Isolde").Count);
        Assert.NotEqual("", Assert.Single(PlayerActionRules.Extract("I give her 100 gold", "Isolde", false)).Error);
        Assert.Equal("", Assert.Single(PlayerActionRules.Extract("I give Isolde 100 gold", "Isolde", false)).Error);
        Assert.NotEqual(PlayerActionRules.ActionId("c", "t", "turn1", 0), PlayerActionRules.ActionId("c", "t", "turn2", 0));
        Assert.NotEqual(PlayerActionRules.ActionId("c", "t", "turn1", 0), PlayerActionRules.ActionId("c", "t", "turn1", 1));
        var actual = Assert.Single(PlayerActionRules.Extract("I give you 100 gold.\n```\nI give you 2000 gold\n```", "Isolde"));
        Assert.Equal(100, actual.Amount);
    }

    [Theory]
    [InlineData("")][InlineData("uncommitted")][InlineData("not committed")]
    [InlineData("conditionally accepted")][InlineData("not accepted")][InlineData("accepted later")]
    public void CommitmentRequiresCanonicalPositiveState(string state) => Assert.False(PlayerActionRules.IsAcceptedCommitment(state));

    private sealed class Wallets
    {
        public readonly Dictionary<string, int> Balances = new() { ["player"] = 10000, ["npc"] = 0 };
        public GoldPaymentLedger Ledger = new();
        public int Moves;
        public GoldPaymentReceipt Pay(string id, int amount, string purpose = "gift", string agreement = "")
            => Ledger.Transfer(id, "player", "npc", amount, purpose, agreement, k => Balances[k],
                (from, to, n) => { Moves++; Balances[from] -= n; Balances[to] += n; }, (k, n) => Balances[k] = n);
    }

    [Fact]
    public void PaymentReloadRetryAndLaterRecruitmentChargeOnlyRemainingBalance()
    {
        var w = new Wallets();
        Assert.Equal("applied", w.Pay("advance1", 700, "recruitment_advance", "resident:1").Status);
        w.Pay("advance2", 800, "recruitment_advance", "resident:1");
        w.Pay("unrelatedGift", 500);
        w.Ledger = JsonSerializer.Deserialize<GoldPaymentLedger>(JsonSerializer.Serialize(w.Ledger))!;
        w.Pay("advance1", 700, "recruitment_advance", "resident:1");
        Assert.Equal(3, w.Moves);
        Assert.Equal(1500, w.Ledger.AvailableCredit("player", "npc", "resident:1"));
        int credit = w.Ledger.ConsumeCredit("player", "npc", "resident:1", "recruit", 2000);
        Assert.Equal(1500, credit);
        Assert.Equal(1500, w.Ledger.ConsumeCredit("player", "npc", "resident:1", "recruit", 2000));
        w.Pay("recruit:payment", 2000 - credit, "recruitment_balance", "resident:1");
        Assert.Equal(7500, w.Balances["player"]); Assert.Equal(2500, w.Balances["npc"]);
        Assert.Equal(0, w.Ledger.AvailableCredit("player", "npc", "resident:1"));
        Assert.Throws<InvalidOperationException>(() => w.Pay("advance1", 800, "recruitment_advance", "resident:1"));
        Assert.Equal(4, w.Moves);
    }

    [Fact]
    public void PartialMutationRollsBackAndInterruptedReceiptNeverDebitsAgain()
    {
        var w = new Wallets();
        var failed = w.Ledger.Transfer("partial", "player", "npc", 2000, "gift", "", k => w.Balances[k],
            (from, to, n) => { w.Balances[from] -= n; throw new Exception("native interruption"); }, (k, n) => w.Balances[k] = n);
        Assert.Equal("failed", failed.Status); Assert.Equal(10000, w.Balances["player"]); Assert.Equal(0, w.Balances["npc"]);
        Assert.Equal("failed", w.Pay("partial", 2000).Status); Assert.Equal(0, w.Moves);
        var pending = w.Pay("interrupted", 2000); pending.Status = "pending";
        Assert.Equal("unknown", w.Pay("interrupted", 2000).Status); Assert.Equal(1, w.Moves);
    }

    [Fact]
    public void InsufficientFundsOverflowAndUnrelatedAgreementsHaveNoEffect()
    {
        var w = new Wallets();
        Assert.Equal("failed", w.Pay("large", 10001).Status);
        w.Balances["npc"] = int.MaxValue;
        Assert.Equal("failed", w.Pay("overflow", 1).Status); Assert.Equal(0, w.Moves);
        w.Balances["npc"] = 0;
        w.Pay("advance", 3000, "recruitment_advance", "resident:1");
        Assert.Equal(0, w.Ledger.AvailableCredit("player", "npc", "resident:2"));
        Assert.Equal(2000, w.Ledger.ConsumeCredit("player", "npc", "resident:1", "recruit", 2000));
        Assert.Equal(0, w.Ledger.AvailableCredit("player", "npc", "resident:1"));
        Assert.Equal(3000, w.Balances["npc"]); // Surplus stays with the recipient; no automatic refund.
    }

    [Fact]
    public void ExplicitRefundRemovesAdvanceCreditAndCannotRepeat()
    {
        var w = new Wallets(); w.Pay("advance", 2000, "recruitment_advance", "resident:1");
        Func<GoldPaymentReceipt> refund = () => w.Ledger.Transfer("refund", "npc", "player", 500, "refund", "",
            k => w.Balances[k], (from, to, n) => { w.Moves++; w.Balances[from] -= n; w.Balances[to] += n; }, (k, n) => w.Balances[k] = n, "advance");
        Assert.Equal("applied", refund().Status); Assert.Equal("applied", refund().Status);
        Assert.Equal(2, w.Moves); Assert.Equal(8500, w.Balances["player"]); Assert.Equal(1500, w.Balances["npc"]);
        Assert.Equal(1500, w.Ledger.AvailableCredit("player", "npc", "resident:1"));
        Assert.Throws<InvalidOperationException>(() => w.Ledger.Transfer("excess_refund", "npc", "player", 2000, "refund", "",
            k => w.Balances[k], (f,t,n) => throw new Exception("must not move"), (k,n) => {}, "advance"));
    }

    [Fact]
    public void RestoringSavedBalancesAndLedgerTogetherRestoresCredit()
    {
        var w = new Wallets(); w.Pay("advance", 2000, "recruitment_advance", "resident:1");
        string checkpoint = JsonSerializer.Serialize(w.Ledger);
        w.Ledger.ConsumeCredit("player", "npc", "resident:1", "recruit", 2000);
        w.Pay("future", 500);
        w.Ledger = JsonSerializer.Deserialize<GoldPaymentLedger>(checkpoint)!;
        w.Balances["player"] = 8000; w.Balances["npc"] = 2000;
        Assert.Equal(2000, w.Ledger.AvailableCredit("player", "npc", "resident:1"));
        w.Pay("advance", 2000, "recruitment_advance", "resident:1");
        Assert.Equal(8000, w.Balances["player"]); Assert.Equal(2000, w.Balances["npc"]);
    }
}
