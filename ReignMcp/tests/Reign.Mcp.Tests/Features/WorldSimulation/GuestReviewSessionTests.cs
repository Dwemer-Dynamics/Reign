using ReignBeta.PartyAgency;
using ReignBeta.UI;

namespace Reign.Mcp.Tests;

public sealed class GuestReviewSessionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void CleanupAttemptsEveryOwnedResourceAndCallbackEvenWhenOneStepThrows(int failedStep)
    {
        var attempted = new List<int>();
        var failures = new List<Exception>();
        Action[] steps = Enumerable.Range(0, 7).Select<int, Action>(index => () =>
        {
            attempted.Add(index);
            if (index == failedStep) throw new InvalidOperationException("injected cleanup failure");
        }).ToArray();
        ReignPartyChatCleanup.Run(failures.Add, steps);
        Assert.Equal(Enumerable.Range(0, 7), attempted);
        Assert.Single(failures);
    }

    [Fact]
    public void BrokenFinalizationAndLoggerCannotKeepTheChatStateLockOrSkipClosure()
    {
        var locks = new HashSet<string> { "party-chat", "another-screen" };
        bool inputLocked = true;
        int notifications = 0;
        ReignPartyChatCleanup.Run(_ => throw new InvalidOperationException("logger failed"),
            () => inputLocked = false,
            () => throw new InvalidOperationException("finalize failed"),
            () => locks.Remove("party-chat"),
            () => notifications++);
        Assert.False(inputLocked);
        Assert.Equal(new[] { "another-screen" }, locks);
        Assert.Equal(1, notifications);
    }

    private static readonly DateTime Now = new(2026, 9, 22, 6, 0, 0, DateTimeKind.Utc);
    private static ReignGuestReviewResolution Resolve(ReignGuestReviewSession session, string phase = "ReviewDue",
        bool pending = false, int seconds = 0, string agreement = "agreement", string hero = "hero", bool eligible = true)
        => session.Resolve(hero, agreement, phase, eligible, pending, Now.AddSeconds(seconds));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosingWithoutNativeRenewalAlwaysOffersRetryOrExplicitDeparture(bool providerOk)
    {
        var session = new ReignGuestReviewSession("hero", "agreement");
        session.BeginTurn();
        session.CompleteTurn(providerOk, new[] { "action" });
        session.Close(Now);
        Assert.Equal(ReignGuestReviewResolution.Retry, Resolve(session));
    }

    [Fact]
    public void LateReplyAndNativeReceiptStayPendingThenResolveWithoutReopening()
    {
        var session = new ReignGuestReviewSession("hero", "agreement");
        session.BeginTurn();
        session.Close(Now);
        Assert.Equal(ReignGuestReviewResolution.Pending, Resolve(session, seconds: 4));
        session.CompleteTurn(true, new[] { "renew", "renew" });
        Assert.Single(session.ActionIds);
        Assert.Equal(ReignGuestReviewResolution.Pending, Resolve(session, pending: true, seconds: 5));
        Assert.Equal(ReignGuestReviewResolution.Resolved, Resolve(session, phase: "Active", seconds: 6));
    }

    [Fact]
    public void HungReplyOrMissingReceiptTimesOutToChoiceNeverDeparture()
    {
        var session = new ReignGuestReviewSession("hero", "agreement");
        session.BeginTurn();
        session.Close(Now);
        session.Close(Now.AddSeconds(29)); // duplicate callbacks cannot keep postponing the choice
        Assert.Equal(ReignGuestReviewResolution.Retry, Resolve(session, pending: true, seconds: 30));
    }

    [Theory]
    [InlineData("other", "agreement", true)]
    [InlineData("hero", "replacement", true)]
    [InlineData("hero", "agreement", false)]
    public void StaleOrDepartedSessionsCannotActOnCurrentGuest(string hero, string agreement, bool eligible)
    {
        var session = new ReignGuestReviewSession("hero", "agreement");
        session.Close(Now);
        Assert.Equal(ReignGuestReviewResolution.Stale, Resolve(session, hero: hero, agreement: agreement, eligible: eligible));
    }

    [Fact]
    public void SeparateGuestsAndRapidReopenHaveIndependentSessions()
    {
        var first = new ReignGuestReviewSession("hero", "agreement");
        var reopened = new ReignGuestReviewSession("hero", "agreement");
        var other = new ReignGuestReviewSession("other", "other-agreement");
        first.Close(Now);
        Assert.NotEqual(first.SessionId, reopened.SessionId);
        Assert.Equal(ReignGuestReviewResolution.Pending, Resolve(reopened));
        Assert.Equal(ReignGuestReviewResolution.Pending, Resolve(other, hero: "other", agreement: "other-agreement"));
    }

    [Theory]
    [InlineData("Active")]
    [InlineData("ReviewDue")]
    public void RenewalBindsAgreementAndDeadlineAndCannotApplyTwice(string phase)
    {
        const double due = 91106.6484375; // real campaign-scale float precision
        Assert.True(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "agreement", due));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due + 20, "Active", "agreement", due));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("replacement", due, phase, "agreement", due));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due, "Returning", "agreement", due));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "", due));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "agreement", double.NaN));
        // A same-day revision can leave the deadline identical; it must still reject the prior accepted turn.
        Assert.True(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "agreement", due, 3, 3));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "agreement", due, 4, 3));
        Assert.False(ReignGuestReviewSession.MatchesRenewal("agreement", due, phase, "agreement", due, int.MaxValue, int.MaxValue));
    }
}
