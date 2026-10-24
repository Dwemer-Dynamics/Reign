using System.Text.Json;
using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests;

public sealed class AgreementPaymentFactsTests
{
    private static AgreementJournal Paid(string id = "sponsorship", int quantity = 500, int installments = 1)
    {
        var agreement = new ActionAgreement { Id = id, PlayerId = "player", NpcId = "npc",
            ProposerId = "player", Origin = ActionOrigin.Conversation, State = AgreementState.Completed,
            Clauses = new() {
                new() { Id = "fee", ActionId = "give.gold", ActorId = "player", RecipientId = "npc",
                    Installments = installments, Arguments = new() { ["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
                new() { Id = "service", ActionId = "government.sponsor_petition", ActorId = "npc", RecipientId = "player",
                    Arguments = new() { ["petitionId"] = "petition", ["petitionRevision"] = "1" } }
            } };
        string hash = agreement.TermsHash();
        agreement.PlayerAcceptedHash = hash; agreement.NpcAcceptedHash = hash;
        return new AgreementJournal { Agreement = agreement, CommittedHash = hash,
            Steps = new() {
                new() { ClauseId = "fee", Reserved = true, Started = true, Completed = true,
                    FulfilledInstallments = 1, StartedDay = 91077.375, Snapshot = "{\"total\":500}" },
                new() { ClauseId = "service", Reserved = true, Started = true, Completed = true, FulfilledInstallments = 1 }
            } };
    }

    [Fact]
    public void SettledSponsorshipSurvivesJournalRoundTripWithoutReplayingOrChangingIt()
    {
        var journal = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(Paid()))!;
        string before = JsonSerializer.Serialize(journal);
        var history = ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc");
        var payment = Assert.Single(history.Rows);
        Assert.Equal(500, payment.Amount);
        Assert.Equal("sponsorship", payment.AgreementId);
        Assert.Equal(journal.CommittedHash, payment.TermsHash);
        Assert.Contains("government.sponsor_petition", payment.AssociatedActionIds);
        Assert.Equal(before, JsonSerializer.Serialize(journal));
        Assert.Equal(1, history.Total); Assert.False(history.Truncated);
    }

    [Theory]
    [InlineData("unsigned")]
    [InlineData("changed_terms")]
    [InlineData("unreserved")]
    [InlineData("unfulfilled")]
    [InlineData("inconsistent_installment")]
    [InlineData("duplicate_step")]
    [InlineData("duplicate_clause")]
    [InlineData("foreign_recipient")]
    [InlineData("offered")]
    [InlineData("non_gold")]
    public void UnverifiedOrUnrelatedStateCannotEstablishPayment(string fault)
    {
        var journal = Paid(); var step = journal.Steps[0]; var clause = journal.Agreement.Clauses[0];
        switch (fault)
        {
            case "unsigned": journal.Agreement.NpcAcceptedHash = ""; break;
            case "changed_terms": clause.Arguments["quantity"] = "600"; break;
            case "unreserved": step.Reserved = false; break;
            case "unfulfilled": step.FulfilledInstallments = 0; break;
            case "inconsistent_installment": step.Installment = 1; break;
            case "duplicate_step": journal.Steps.Add(step); break;
            case "duplicate_clause": journal.Agreement.Clauses.Add(clause); Resign(journal); break;
            case "foreign_recipient": clause.RecipientId = "other"; Resign(journal); break;
            case "offered": journal.Agreement.State = AgreementState.Offered; break;
            case "non_gold": clause.ActionId = "give.item"; Resign(journal); break;
        }
        Assert.Empty(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc").Rows);
    }

    [Fact]
    public void DeliveredInstallmentsRemainPaidWhenTheNextIsPendingOrUnusedEscrowReturns()
    {
        var journal = Paid(installments: 3);
        journal.Agreement.State = AgreementState.Waiting;
        var step = journal.Steps[0]; step.Installment = 1; step.Started = false; step.Completed = false;
        Assert.Equal(500, Assert.Single(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc").Rows).Amount);
        journal.Agreement.State = AgreementState.Cancelled; step.Returned = true;
        var payment = Assert.Single(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc").Rows);
        Assert.Equal(500, payment.Amount); Assert.Equal(1, payment.FulfilledInstallments);
        Assert.Equal(3, payment.ScheduledInstallments); Assert.True(payment.UnusedReservationReturned);
    }

    [Fact]
    public void CompletedAdvanceDoesNotRequireOrInventCompletionOfTheReciprocalService()
    {
        var journal = Paid(); journal.Agreement.State = AgreementState.Waiting;
        var service = journal.Steps[1]; service.Started = false; service.Completed = false; service.FulfilledInstallments = 0;
        var payment = Assert.Single(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc").Rows);
        Assert.Equal(500, payment.Amount); Assert.False(service.Completed); Assert.Equal(0, service.FulfilledInstallments);
    }

    [Fact]
    public void FactsAreScopedToTheExactPairAndCurrentLoadedJournalSet()
    {
        var journal = Paid();
        Assert.Single(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "npc").Rows);
        Assert.Empty(ReignAgreementPaymentFacts.Capture(new[] { journal }, "player", "other").Rows);
        Assert.Empty(ReignAgreementPaymentFacts.Capture(Array.Empty<AgreementJournal>(), "player", "npc").Rows);
        Assert.Empty(ReignAgreementPaymentFacts.Capture(new[] { journal, journal }, "player", "npc").Rows);
    }

    [Fact]
    public void BothDirectionsStayDistinctAndAmountUses64BitDeliveredTotals()
    {
        var first = Paid("large", int.MaxValue, 3);
        first.Steps[0].Installment = 2; first.Steps[0].FulfilledInstallments = 3;
        var reverse = Paid("reverse", 200);
        reverse.Agreement.Clauses[0].ActorId = "npc"; reverse.Agreement.Clauses[0].RecipientId = "player"; Resign(reverse);
        var rows = ReignAgreementPaymentFacts.Capture(new[] { first, reverse }, "player", "npc").Rows;
        Assert.Equal(2, rows.Count);
        Assert.Equal(3L * int.MaxValue, rows.Single(p => p.AgreementId == "large").Amount);
        Assert.Equal("npc", rows.Single(p => p.AgreementId == "reverse").FromHeroId);
        Assert.Equal(200, rows.Single(p => p.AgreementId == "reverse").Amount);
    }

    [Fact]
    public void BoundedHistoryReportsOmissionsAndUsesStableOrdering()
    {
        var journals = Enumerable.Range(0, 13).Select(i => { var j = Paid("paid" + i.ToString("D2"));
            j.Steps[0].StartedDay = i; return j; }).ToArray();
        var history = ReignAgreementPaymentFacts.Capture(journals, "player", "npc");
        Assert.Equal(13, history.Total); Assert.True(history.Truncated); Assert.Equal(12, history.Rows.Count);
        Assert.Equal("paid12", history.Rows[0].AgreementId);
        Assert.Equal(history.Rows.Select(p => p.AgreementId), ReignAgreementPaymentFacts.Capture(journals.Reverse(), "player", "npc").Rows.Select(p => p.AgreementId));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReignAgreementPaymentFacts.Capture(journals, "player", "npc", 0));
    }

    private static void Resign(AgreementJournal journal)
    {
        string hash = journal.Agreement.TermsHash(); journal.CommittedHash = hash;
        journal.Agreement.PlayerAcceptedHash = hash; journal.Agreement.NpcAcceptedHash = hash;
    }
}
