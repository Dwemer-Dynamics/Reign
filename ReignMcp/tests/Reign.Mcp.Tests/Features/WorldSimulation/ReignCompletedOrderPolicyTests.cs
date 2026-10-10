using System.Text.Json;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ReignCompletedOrderPolicyTests
{
    private static AgreementJournal ActiveHold()
    {
        var j = CompletedPatrol();
        j.Agreement.State = AgreementState.Waiting;
        j.Agreement.Clauses[0].ActionId = "campaign.hold_position";
        j.CommittedHash = j.Agreement.PlayerAcceptedHash = j.Agreement.NpcAcceptedHash = j.Agreement.TermsHash();
        j.Steps[0].Completed = false; j.Steps[0].FulfilledInstallments = 0;
        return j;
    }

    private static AgreementJournal AcceptedCancellation()
    {
        var j = CompletedPatrol(); j.Agreement.Id = "mutual-cancellation";
        j.Agreement.Clauses[0].ActionId = "cancel_campaign_order";
        j.Agreement.Clauses[0].Arguments = new() { ["terms.orderId"] = "exact-hold-order" };
        j.CommittedHash = j.Agreement.PlayerAcceptedHash = j.Agreement.NpcAcceptedHash = j.Agreement.TermsHash();
        return j;
    }

    private static ActionReceipt CancelObserved(AgreementJournal original, AgreementJournal cancellation, CancellationBackend backend) =>
        ReignAgreementEngine.CancelObservedOrder(original, cancellation, "patrol", "exact-hold-order", backend, 10);

    [Fact]
    public void Observed_mutual_cancellation_survives_reload_without_claiming_fulfillment_or_reexecuting()
    {
        var j = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(ActiveHold()))!;
        string committed = j.CommittedHash; var backend = new CancellationBackend(); var accepted = AcceptedCancellation();
        Assert.Equal(AgreementState.Cancelled, CancelObserved(j, accepted, backend).State);
        Assert.True(j.Steps[0].Started); Assert.False(j.Steps[0].Completed);
        Assert.Equal(0, j.Steps[0].FulfilledInstallments);
        Assert.Equal(committed, j.Agreement.TermsHash()); Assert.True(j.Agreement.HasBothConsents);
        Assert.Empty(j.Receipts.Last().ObservedEffects);
        j = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(j))!;
        Assert.Equal(AgreementState.Cancelled, CancelObserved(j, accepted, backend).State);
        Assert.Empty(backend.Returns); Assert.Equal(1, backend.Saves);
    }

    [Fact]
    public void Cancellation_returns_only_undelivered_reservations_and_retains_paid_installments()
    {
        var j = ActiveHold();
        j.Agreement.Clauses.Add(new ActionClause { Id = "payment", ActionId = "give.gold", ActorId = "main_hero",
            RecipientId = "lord_1_1", Installments = 3, Arguments = new() { ["amount"] = "100" } });
        j.Steps.Add(new AgreementStep { ClauseId = "payment", Reserved = true, Started = true,
            Installment = 1, FulfilledInstallments = 1, Snapshot = "reserved-three-payments" });
        j.CommittedHash = j.Agreement.PlayerAcceptedHash = j.Agreement.NpcAcceptedHash = j.Agreement.TermsHash();
        var backend = new CancellationBackend(); var accepted = AcceptedCancellation();
        Assert.Equal(AgreementState.Cancelled, CancelObserved(j, accepted, backend).State);
        Assert.Equal(("payment", "reserved-three-payments", 1), Assert.Single(backend.Returns));
        Assert.True(j.Steps[1].Returned); Assert.Equal(1, j.Steps[1].FulfilledInstallments);
        Assert.Equal(AgreementState.Cancelled, CancelObserved(j, accepted, backend).State); Assert.Single(backend.Returns);
    }

    [Theory]
    [InlineData("missing-player-consent")]
    [InlineData("missing-npc-consent")]
    [InlineData("changed-terms")]
    [InlineData("different-order")]
    [InlineData("different-npc")]
    [InlineData("different-actor")]
    [InlineData("unfinished-cancellation")]
    [InlineData("unstarted-cancellation")]
    [InlineData("unreserved-cancellation")]
    [InlineData("returned-cancellation")]
    [InlineData("missing-step")]
    [InlineData("wrong-step")]
    public void Cancellation_rejects_foreign_or_unverified_authority_before_resource_mutations(string fault)
    {
        var original = ActiveHold(); var cancellation = AcceptedCancellation(); var a = cancellation.Agreement;
        switch (fault)
        {
            case "missing-player-consent": a.PlayerAcceptedHash = ""; break;
            case "missing-npc-consent": a.NpcAcceptedHash = ""; break;
            case "changed-terms": a.Clauses[0].DisplayText = "different reviewed sentence"; break;
            case "different-order": a.Clauses[0].Arguments["terms.orderId"] = "other-order"; break;
            case "different-npc": a.NpcId = "other-npc"; break;
            case "different-actor": a.Clauses[0].ActorId = "other-npc"; break;
            case "unfinished-cancellation": a.State = AgreementState.Waiting; break;
            case "unstarted-cancellation": cancellation.Steps[0].Started = false; break;
            case "unreserved-cancellation": cancellation.Steps[0].Reserved = false; break;
            case "returned-cancellation": cancellation.Steps[0].Returned = true; break;
            case "missing-step": cancellation.Steps.Clear(); break;
            case "wrong-step": cancellation.Steps[0].ClauseId = "unrelated"; break;
        }
        if (fault.StartsWith("different-")) cancellation.CommittedHash = a.PlayerAcceptedHash = a.NpcAcceptedHash = a.TermsHash();
        var backend = new CancellationBackend(); string before = JsonSerializer.Serialize(original);
        Assert.Throws<InvalidOperationException>(() => CancelObserved(original, cancellation, backend));
        Assert.Equal(before, JsonSerializer.Serialize(original)); Assert.Empty(backend.Returns); Assert.Equal(0, backend.Saves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Other_started_services_or_unproven_asset_returns_remain_in_explicit_recovery(bool returnFails)
    {
        var j = ActiveHold();
        j.Agreement.Clauses.Add(new ActionClause { Id = "other", ActionId = returnFails ? "give.gold" : "campaign.patrol" });
        j.Steps.Add(new AgreementStep { ClauseId = "other", Reserved = true, Started = true });
        j.CommittedHash = j.Agreement.PlayerAcceptedHash = j.Agreement.NpcAcceptedHash = j.Agreement.TermsHash();
        var backend = new CancellationBackend { ReturnFails = returnFails };
        Assert.Equal(AgreementState.RecoveryRequired, CancelObserved(j, AcceptedCancellation(), backend).State);
        Assert.False(j.Steps[0].Completed); Assert.False(j.Steps[1].Returned);
        Assert.NotEmpty(j.Agreement.Failure); Assert.Equal(1, backend.Saves);
    }

    [Fact]
    public void Cancellation_policy_requires_the_exact_cancelled_applied_order_and_original_consent()
    {
        var j = ActiveHold();
        bool Can(string native = "cancelled", string world = "Executing", bool captured = true, bool applied = true) =>
            ReignCompletedOrderPolicy.CanReconcileCancellation(j, j.Steps[0], captured, applied, native, world);
        Assert.True(Can()); Assert.True(Can(world: "Cancelled"));
        Assert.False(Can(native: "active")); Assert.False(Can(native: "completed"));
        Assert.False(Can(world: "Failed")); Assert.False(Can(world: "Completed"));
        Assert.False(Can(captured: false)); Assert.False(Can(applied: false));
        j.Steps[0].Completed = true; Assert.False(Can()); j.Steps[0].Completed = false;
        j.Steps[0].Returned = true; Assert.False(Can()); j.Steps[0].Returned = false;
        j.Agreement.NpcAcceptedHash = ""; j.OperatorAuthorized = true; Assert.False(Can());
        j.Agreement.Origin = ActionOrigin.Console; j.CommittedHash = j.Agreement.TermsHash(); Assert.True(Can());
        j.Agreement.State = AgreementState.RecoveryRequired; Assert.False(Can());
    }

    private sealed class CancellationBackend : IAgreementBackend
    {
        public List<(string ClauseId, string Snapshot, int Fulfilled)> Returns { get; } = new();
        public bool ReturnFails { get; init; }
        public int Saves { get; private set; }
        public IReadOnlyList<string> Preflight(ActionAgreement agreement) => throw new InvalidOperationException("Cancellation must not rerun preflight.");
        public string Capture(ActionAgreement agreement, ActionClause clause) => throw new InvalidOperationException("Cancellation must not recapture.");
        public void Reserve(ActionAgreement agreement, ActionClause clause, string snapshot) => throw new InvalidOperationException("Cancellation must not reserve again.");
        public void Apply(ActionAgreement agreement, ActionClause clause, string snapshot, int installment) => throw new InvalidOperationException("Cancellation must not execute again.");
        public void ApplyAssetsAtomically(ActionAgreement agreement, IReadOnlyList<AgreementStep> steps) => throw new InvalidOperationException("Cancellation must not pay again.");
        public bool Observe(ActionAgreement agreement, ActionClause clause, string snapshot, int installment) => throw new InvalidOperationException("Cancellation uses the already verified native receipt.");
        public bool ConditionSatisfied(ActionClause clause, double now) => throw new InvalidOperationException();
        public double DeliveryDays(ActionAgreement agreement) => throw new InvalidOperationException();
        public void SaveJournal(AgreementJournal journal) => Saves++;
        public void Return(ActionAgreement agreement, ActionClause clause, string snapshot, int fulfilledInstallments)
        {
            Returns.Add((clause.Id, snapshot, fulfilledInstallments));
            if (ReturnFails) throw new InvalidOperationException("Return not proven.");
        }
    }

    [Fact]
    public void A_saved_premature_duplicate_receipt_resumes_only_its_exact_unfulfilled_native_order()
    {
        var j = CompletedPatrol(); j.Agreement.State = AgreementState.InTransit;
        j.Steps[0].Completed = false; j.Steps[0].FulfilledInstallments = 0;
        j = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(j))!;
        string before = JsonSerializer.Serialize(j);
        bool Restore(string native = "active", string world = "Completed", bool captured = true, bool applied = true) =>
            ReignCompletedOrderPolicy.CanRestoreInProgress(j, j.Steps[0], 0, captured, applied, native, world);
        Assert.True(Restore()); Assert.True(Restore("accepted")); Assert.True(Restore("awaiting_guidance"));
        Assert.False(Restore("completed")); Assert.False(Restore("failed")); Assert.False(Restore("cancelled"));
        Assert.False(Restore(world: "Failed")); Assert.False(Restore(world: "Rejected"));
        Assert.False(Restore(captured: false)); Assert.False(Restore(applied: false));
        Assert.Equal(before, JsonSerializer.Serialize(j));
        j.Steps[0].FulfilledInstallments = 1; Assert.False(Restore());
        j.Steps[0].FulfilledInstallments = 0; j.Steps[0].Started = false; Assert.False(Restore());
        j.Steps[0].Started = true; j.Steps[0].Returned = true; Assert.False(Restore());
        j.Steps[0].Returned = false; j.Steps[0].Reserved = false; Assert.False(Restore());
        j.Steps[0].Reserved = true; j.Agreement.NpcAcceptedHash = ""; Assert.False(Restore());
        j.Agreement.NpcAcceptedHash = j.CommittedHash; j.Agreement.State = AgreementState.Completed; Assert.False(Restore());
        j.Agreement.State = AgreementState.RecoveryRequired; Assert.False(Restore());
    }

    private static AgreementJournal CompletedPatrol()
    {
        var agreement = new ActionAgreement
        {
            Id = "saved-patrol", PlayerId = "main_hero", NpcId = "lord_1_1",
            ProposerId = "main_hero", ConversationId = "campaign:timeline:lord_1_1",
            Origin = ActionOrigin.Conversation, State = AgreementState.Completed,
            Clauses = new() { new ActionClause { Id = "patrol", ActionId = "patrol_around_settlement",
                ActorId = "lord_1_1", RecipientId = "main_hero",
                Arguments = new() { ["targetSettlementId"] = "town_EN2", ["terms.durationHours"] = "2" } } }
        };
        string hash = agreement.TermsHash();
        agreement.PlayerAcceptedHash = agreement.NpcAcceptedHash = hash;
        return new AgreementJournal { Agreement = agreement, CommittedHash = hash,
            Steps = new() { new AgreementStep { ClauseId = "patrol", Reserved = true, Started = true,
                Completed = true, FulfilledInstallments = 1, Snapshot = "exact captured record" } } };
    }

    private static bool Reconcile(AgreementJournal j, string nativeStatus = "completed",
        string worldStatus = "Executing", string failure = "", bool capturedMatches = true, bool applied = true) =>
        ReignCompletedOrderPolicy.CanReconcile(j, j.Steps[0], 0, capturedMatches, applied,
            nativeStatus, worldStatus, failure);

    [Fact]
    public void A_reloaded_completed_patrol_retires_its_stale_queue_without_changing_consent_or_fulfillment()
    {
        var original = CompletedPatrol();
        var loaded = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(original))!;
        string before = JsonSerializer.Serialize(loaded);
        Assert.True(Reconcile(loaded));
        Assert.Equal(before, JsonSerializer.Serialize(loaded));
        Assert.True(loaded.Agreement.HasBothConsents);
        Assert.Equal(1, loaded.Steps[0].FulfilledInstallments);
    }

    [Theory]
    [InlineData(AgreementState.Waiting)]
    [InlineData(AgreementState.InTransit)]
    public void Native_completion_can_be_published_before_the_agreement_tick(AgreementState state)
    {
        var j = CompletedPatrol(); j.Agreement.State = state; j.Steps[0].Completed = false;
        j.Steps[0].FulfilledInstallments = 0;
        Assert.True(Reconcile(j));
        Assert.Equal(0, j.Steps[0].FulfilledInstallments);
    }

    [Fact]
    public void Only_the_known_false_authorization_failure_can_be_recovered()
    {
        var j = CompletedPatrol();
        Assert.True(Reconcile(j, worldStatus: "Failed", failure: ReignCompletedOrderPolicy.StaleAuthorizationFailure));
        Assert.False(Reconcile(j, worldStatus: "Failed", failure: "The target was lost."));
        Assert.False(Reconcile(j, worldStatus: "Failed", failure: ""));
    }

    [Theory]
    [InlineData("active")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("awaiting_guidance")]
    public void An_unfulfilled_native_order_cannot_be_claimed_as_completed(string status) =>
        Assert.False(Reconcile(CompletedPatrol(), nativeStatus: status));

    [Theory]
    [InlineData("Completed")]
    [InlineData("Rejected")]
    [InlineData("Cancelled")]
    [InlineData("Proposed")]
    public void Other_terminal_or_unaccepted_world_actions_are_not_rewritten(string status) =>
        Assert.False(Reconcile(CompletedPatrol(), worldStatus: status));

    [Fact]
    public void Forged_or_unapplied_captured_records_never_reconcile()
    {
        Assert.False(Reconcile(CompletedPatrol(), capturedMatches: false));
        Assert.False(Reconcile(CompletedPatrol(), applied: false));
        var j = CompletedPatrol();
        Assert.False(ReignCompletedOrderPolicy.CanReconcile(j, new AgreementStep { Reserved = true, Started = true }, 0,
            true, true, "completed", "Executing", ""));
    }

    [Fact]
    public void Returned_unreserved_or_unstarted_steps_never_reconcile()
    {
        var j = CompletedPatrol(); j.Steps[0].Returned = true; Assert.False(Reconcile(j));
        j = CompletedPatrol(); j.Steps[0].Reserved = false; Assert.False(Reconcile(j));
        j = CompletedPatrol(); j.Steps[0].Started = false; j.Steps[0].FulfilledInstallments = 0; Assert.False(Reconcile(j));
    }

    [Theory]
    [InlineData(AgreementState.Cancelled)]
    [InlineData(AgreementState.Rejected)]
    [InlineData(AgreementState.RecoveryRequired)]
    public void Withdrawn_or_uncertain_agreements_never_reconcile(AgreementState state)
    {
        var j = CompletedPatrol(); j.Agreement.State = state; Assert.False(Reconcile(j));
    }

    [Fact]
    public void Changed_terms_or_missing_either_consent_never_reconcile()
    {
        var j = CompletedPatrol(); j.Agreement.Clauses[0].Arguments["terms.durationHours"] = "4"; Assert.False(Reconcile(j));
        j = CompletedPatrol(); j.Agreement.PlayerAcceptedHash = ""; Assert.False(Reconcile(j));
        j = CompletedPatrol(); j.Agreement.NpcAcceptedHash = ""; Assert.False(Reconcile(j));
        j = CompletedPatrol(); j.CommittedHash = "different"; Assert.False(Reconcile(j));
    }

    [Fact]
    public void Operator_completion_requires_the_actual_console_origin()
    {
        var j = CompletedPatrol(); j.OperatorAuthorized = true; j.Agreement.NpcAcceptedHash = "";
        Assert.False(Reconcile(j));
        j.Agreement.Origin = ActionOrigin.Console; j.CommittedHash = j.Agreement.TermsHash();
        Assert.True(Reconcile(j));
    }

    [Fact]
    public void A_completed_prior_installment_can_reconcile_while_the_next_waits_without_starting_it()
    {
        var j = CompletedPatrol(); j.Agreement.State = AgreementState.Waiting;
        j.Steps[0].Installment = 1; j.Steps[0].Completed = false; j.Steps[0].Started = false;
        Assert.True(Reconcile(j));
        Assert.False(ReignCompletedOrderPolicy.CanReconcile(j, j.Steps[0], 1, true, true, "completed", "Executing", ""));
        Assert.False(ReignCompletedOrderPolicy.CanReconcile(j, j.Steps[0], -1, true, true, "completed", "Executing", ""));
        Assert.Equal(1, j.Steps[0].FulfilledInstallments);
        Assert.False(j.Steps[0].Started);
    }
}
