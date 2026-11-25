using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ReignBeta.Shared.Dialogue;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NativeRansomJournalTests
{
    private static NativeRansomCustody Held() => new()
    {
        SameLivingHero = true, IsPrisoner = true, HasCaptor = true, OriginalCaptor = true, OriginalRosterCount = 1,
    };

    private static NativeRansomCustody Freed() => new() { SameLivingHero = true, OriginalRosterCount = 0 };

    private sealed class NativeWorld
    {
        internal NativeRansomJournal Journal = new();
        internal readonly Dictionary<string, NativeRansomCustody> Captives = new()
        {
            ["first"] = Held(), ["second"] = Held(),
        };
        internal int PaymentCalls;
        internal int ReleaseCalls;
        internal int GoldPayer = 100;
        internal int GoldRecipient = 20;
        internal bool Owns = true;
        internal Action<string>? OnRelease;
        internal Action<int>? OnPayment;

        internal NativeRansomAttempt? Run(string key = "action", string binding = "exact", int? amount = 30,
            NativeRansomWorldAction? worldAction = null)
        {
            var releases = Captives.Keys.Select(id => new NativeRansomRelease
            {
                HeroId = id, CaptorId = "mobile:original", RosterCount = 1, Method = "ransom",
            }).ToList();
            var attempt = new NativeRansomAttempt
            {
                Binding = binding, MaterialJson = "{}", Releases = releases, WorldAction = worldAction,
                Payment = amount.HasValue ? new NativeRansomPayment
                {
                    PayerId = "payer", RecipientId = "recipient", Amount = amount.Value,
                } : null,
            };
            return Journal.Execute(key, attempt, () => Owns, _ => true, leg => Captives[leg.HeroId], leg =>
            {
                ReleaseCalls++;
                if (OnRelease != null) OnRelease(leg.HeroId);
                else Captives[leg.HeroId] = Freed();
            }, () => new NativeRansomBalances { Payer = GoldPayer, Recipient = GoldRecipient }, gold =>
            {
                PaymentCalls++;
                if (OnPayment != null) OnPayment(gold);
                else { GoldPayer -= gold; GoldRecipient += gold; }
            });
        }

        internal void Reload() => Journal = JsonSerializer.Deserialize<NativeRansomJournal>(JsonSerializer.Serialize(Journal))!;
    }

    [Fact]
    public void ExactCompletedReceiptSurvivesReloadWithoutRereleasingOrCharging()
    {
        var world = new NativeWorld();
        var first = Assert.IsType<NativeRansomAttempt>(world.Run());
        Assert.True(first.IsVerified());
        world.Reload();
        world.GoldPayer += 8;
        var saved = Assert.IsType<NativeRansomAttempt>(world.Journal.Find("action"));
        Assert.True(saved.IsVerified());
        Assert.Equal(new[] { "first", "second" }, saved.Releases.Select(leg => leg.HeroId));
        var payment = Assert.IsType<NativeRansomPayment>(saved.Payment);
        Assert.Equal(100, payment.Before!.Payer);
        Assert.Equal(70, payment.After!.Payer);
        Assert.Equal(50, payment.After!.Recipient);
        Assert.Null(world.Run());
        Assert.Null(world.Run(binding: "different"));
        Assert.Equal(2, world.ReleaseCalls);
        Assert.Equal(1, world.PaymentCalls);
        Assert.Equal(78, world.GoldPayer);
    }

    [Theory]
    [InlineData("unmoved")]
    [InlineData("flag_only")]
    [InlineData("missing_roster")]
    [InlineData("throws_after_release")]
    [InlineData("callback_changes_authority")]
    public void PartialSecondReleaseRetainsBothObservedResultsAndNeverStartsPayment(string failure)
    {
        var world = new NativeWorld();
        world.OnRelease = id =>
        {
            if (id == "first") { world.Captives[id] = Freed(); return; }
            switch (failure)
            {
                case "flag_only":
                    world.Captives[id] = new NativeRansomCustody { SameLivingHero = true, HasCaptor = true, OriginalRosterCount = 1 };
                    break;
                case "missing_roster":
                    world.Captives[id] = new NativeRansomCustody { SameLivingHero = true };
                    break;
                case "throws_after_release":
                    world.Captives[id] = Freed();
                    throw new InvalidOperationException();
                case "callback_changes_authority":
                    world.Captives[id] = Freed();
                    world.Owns = false;
                    break;
            }
        };

        var result = Assert.IsType<NativeRansomAttempt>(world.Run());
        Assert.Equal(NativeRansomState.Held, result.State);
        Assert.False(result.IsVerified());
        Assert.True(result.Releases[0].Observed?.ProvesRelease() == true);
        Assert.True(result.Releases[1].Invoked);
        Assert.NotNull(result.Releases[1].Observed);
        world.Reload();
        world.Owns = true;
        Assert.Null(world.Run());
        Assert.Equal(2, world.ReleaseCalls);
        Assert.Equal(0, world.PaymentCalls);
        Assert.Equal(100, world.GoldPayer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThrownNativePaymentRetainsObservationsWithoutRefundOrRetry(bool alsoCredits)
    {
        var world = new NativeWorld();
        world.OnPayment = amount =>
        {
            world.GoldPayer -= amount;
            if (alsoCredits) world.GoldRecipient += amount;
            throw new InvalidOperationException();
        };
        var attempt = Assert.IsType<NativeRansomAttempt>(world.Run());
        var payment = Assert.IsType<NativeRansomPayment>(attempt.Payment);
        Assert.Equal(NativeRansomState.Held, attempt.State);
        Assert.False(attempt.IsVerified());
        Assert.False(payment.Returned);
        Assert.Equal(70, payment.After!.Payer);
        Assert.Equal(alsoCredits ? 50 : 20, payment.After!.Recipient);
        world.Reload();
        Assert.Null(world.Run());
        Assert.Equal(1, world.PaymentCalls);
        Assert.Equal(70, world.GoldPayer);
    }

    [Fact]
    public void ReturningNativePaymentWithUnrelatedBalanceChangeIsHeldWithoutOverwritingIt()
    {
        var world = new NativeWorld();
        world.OnPayment = amount =>
        {
            world.GoldPayer -= amount;
            world.GoldRecipient += amount + 7;
        };
        var result = Assert.IsType<NativeRansomAttempt>(world.Run());
        Assert.Equal(NativeRansomState.Held, result.State);
        Assert.Equal("payment_unverified", result.HoldReason);
        Assert.Equal(57, world.GoldRecipient);
        world.Reload();
        Assert.Null(world.Run());
        Assert.Equal(1, world.PaymentCalls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void UnpaidReleaseAndZeroRansomBothRequireNativeReleaseWithoutNativeDebit(int? amount)
    {
        var world = new NativeWorld();
        var result = Assert.IsType<NativeRansomAttempt>(world.Run(amount: amount));
        Assert.True(result.IsVerified());
        Assert.Equal(amount, result.Payment?.Amount);
        Assert.Equal(0, world.PaymentCalls);
        Assert.Equal(2, world.ReleaseCalls);
    }

    [Fact]
    public void ReentrantWorkAndAnySnapshotSavedBeforeReleaseReturnsNeverGainAnotherNativeCall()
    {
        var world = new NativeWorld();
        string? savedWhileInvoking = null;
        world.OnRelease = id =>
        {
            savedWhileInvoking ??= JsonSerializer.Serialize(world.Journal);
            Assert.Null(world.Run(key: "other-route"));
            world.Captives[id] = Freed();
        };
        Assert.True(Assert.IsType<NativeRansomAttempt>(world.Run()).IsVerified());
        Assert.False(world.Journal.HasAttempt("other-route"));
        world.Journal = JsonSerializer.Deserialize<NativeRansomJournal>(savedWhileInvoking!)!;
        var partial = Assert.IsType<NativeRansomAttempt>(world.Journal.Find("action"));
        Assert.Equal(NativeRansomState.Releasing, partial.State);
        Assert.False(partial.IsVerified());
        Assert.Null(world.Run());
        Assert.Equal(2, world.ReleaseCalls);
        Assert.Equal(1, world.PaymentCalls);
    }

    [Fact]
    public void ExactUnaffordablePaymentOrMissingOriginalCustodyFailsBeforeAnyNativeCall()
    {
        var world = new NativeWorld { GoldPayer = 29 };
        Assert.Null(world.Run());
        Assert.False(world.Journal.HasAttempt("action"));
        world.GoldPayer = 100;
        world.Captives["second"] = Freed();
        Assert.Null(world.Run());
        Assert.Equal(0, world.ReleaseCalls);
        Assert.Equal(0, world.PaymentCalls);
    }

    [Fact]
    public void SaveCapturedInsidePaymentDoesNotTurnMatchingCurrentBalancesIntoAReceiptOrRetry()
    {
        var world = new NativeWorld();
        string? savedWhilePaying = null;
        world.OnPayment = gold =>
        {
            world.GoldPayer -= gold;
            world.GoldRecipient += gold;
            savedWhilePaying = JsonSerializer.Serialize(world.Journal);
        };
        Assert.True(Assert.IsType<NativeRansomAttempt>(world.Run()).IsVerified());
        world.Journal = JsonSerializer.Deserialize<NativeRansomJournal>(savedWhilePaying!)!;
        var interrupted = Assert.IsType<NativeRansomAttempt>(world.Journal.Find("action"));
        Assert.Equal(NativeRansomState.Paying, interrupted.State);
        Assert.True(interrupted.Payment!.Invoked);
        Assert.False(interrupted.IsVerified());
        Assert.Null(world.Run());
        Assert.Equal(1, world.PaymentCalls);
        Assert.Equal(70, world.GoldPayer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QueryByIdFindsCompletedOrHeldRansomAfterTheTerminalActionRowAgesOut(bool hold)
    {
        const string actionId = "ordinary_ransom_example";
        var owner = new NativeRansomWorldAction
        {
            CampaignId = "campaign", TimelineId = "timeline", ActionId = actionId,
            IdentityJson = "{\"id\":\"ordinary_ransom_example\",\"type\":15}",
        };
        string key = NativeRansomWorldAction.Key(owner.CampaignId, owner.TimelineId, actionId);
        var world = new NativeWorld();
        if (hold) world.OnPayment = gold => world.GoldPayer -= gold;
        var attempt = Assert.IsType<NativeRansomAttempt>(world.Run(key, owner.Binding(), worldAction: owner));
        if (!hold) attempt.PublicationId = "ransom_" + key;

        // Model the ordinary non-durable history window. The journal query used by
        // GetLastActionResult receives only the requested ID when no action survived.
        var terminalActionIds = Enumerable.Range(1, 129)
            .Select(day => (Id: "unrelated_" + day, LastAttemptDay: day))
            .Append((Id: actionId, LastAttemptDay: 0))
            .OrderByDescending(row => row.LastAttemptDay).Take(128).Select(row => row.Id).ToArray();
        Assert.DoesNotContain(actionId, terminalActionIds);
        world.Reload();
        Assert.True(world.Journal.TryFindWorldAction("campaign", "timeline", actionId, out var saved));
        var exact = Assert.IsType<NativeRansomAttempt>(saved);
        Assert.Equal(hold ? NativeRansomState.Held : NativeRansomState.Verified, exact.State);
        Assert.Equal(!hold, exact.IsVerified());
        Assert.Equal(70, exact.Payment!.After!.Payer);
        Assert.Equal(hold ? 20 : 50, exact.Payment!.After!.Recipient);
        Assert.False(world.Journal.TryFindWorldAction("another_campaign", "timeline", actionId, out _));
        Assert.False(world.Journal.TryFindWorldAction("campaign", "another_timeline", actionId, out _));
        Assert.False(world.Journal.TryFindWorldAction("campaign", "timeline", "ORDINARY_RANSOM_EXAMPLE", out _));
        Assert.Null(world.Run(key, owner.Binding(), worldAction: owner));
        Assert.Equal(2, world.ReleaseCalls);
        Assert.Equal(1, world.PaymentCalls);
    }

    [Fact]
    public void SavedActionBindingMismatchIsAHoldRatherThanAnAbsentAttempt()
    {
        var owner = new NativeRansomWorldAction
        {
            CampaignId = "campaign", TimelineId = "timeline", ActionId = "ordinary",
            IdentityJson = "{\"id\":\"ordinary\",\"type\":15}",
        };
        string key = NativeRansomWorldAction.Key(owner.CampaignId, owner.TimelineId, owner.ActionId);
        var world = new NativeWorld();
        Assert.True(Assert.IsType<NativeRansomAttempt>(world.Run(key, owner.Binding(), worldAction: owner)).IsVerified());
        world.Reload();
        var original = Assert.IsType<NativeRansomAttempt>(world.Journal.Find(key));
        original.WorldAction!.IdentityJson = "{\"id\":\"different\"}";
        Assert.True(world.Journal.TryFindWorldAction("campaign", "timeline", "ordinary", out var saved));
        Assert.Null(saved);
        Assert.Null(world.Run(key, owner.Binding(), worldAction: owner));
        Assert.Equal(1, world.PaymentCalls);
    }
}
