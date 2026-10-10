using System.Text.Json;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ReignUnfulfilledTreatyPolicyTests
{
    private static AgreementJournal Pending(string actionId = "demilitarized_border")
    {
        var a = new ActionAgreement { Id = "old-treaty", Origin = ActionOrigin.Conversation,
            PlayerId = "main_hero", NpcId = "lord_1_1", ProposerId = "main_hero", State = AgreementState.Waiting,
            Clauses = [new ActionClause { Id = "treaty", ActionId = actionId, ActorId = "lord_1_1",
                RecipientId = "main_hero", Installments = 1,
                Arguments = new() { ["actorKingdomId"] = "empire", ["targetKingdomId"] = "dissolved-realm" } }] };
        string hash = a.TermsHash();
        a.PlayerAcceptedHash = a.NpcAcceptedHash = hash;
        return new AgreementJournal { Agreement = a, CommittedHash = hash,
            Steps = [new AgreementStep { ClauseId = "treaty", Reserved = true, Started = true, StartedDay = 10 }],
            Receipts = [new ActionReceipt { OperationId = a.Id, TermsHash = hash, State = AgreementState.Waiting,
                Message = "Awaiting agreed delivery or fulfillment.", Day = 10 }] };
    }

    private static bool Retire(AgreementJournal j, bool captured = true, bool applied = true,
        string status = "Executing", string failure = "", bool eliminated = true, bool recorded = false) =>
        ReignUnfulfilledTreatyPolicy.Retire(j, captured, applied, status, failure, eliminated, recorded, 20);

    [Theory]
    [InlineData("demilitarized_border")]
    [InlineData("caravan_protection_agreement")]
    [InlineData("sign_defensive_pact")]
    public void Dissolved_realm_ends_only_the_exact_unfulfilled_treaty_and_retains_history(string action)
    {
        var j = Pending(action); string hash = j.CommittedHash;
        Assert.True(Retire(j));
        Assert.Equal(AgreementState.Impossible, j.Agreement.State);
        Assert.True(j.Steps[0].Started); Assert.False(j.Steps[0].Completed);
        Assert.False(j.Steps[0].Returned); Assert.Equal(0, j.Steps[0].FulfilledInstallments);
        Assert.Equal(hash, j.Agreement.TermsHash()); Assert.True(j.Agreement.HasBothConsents);
        Assert.Equal(2, j.Receipts.Count); Assert.Empty(j.Receipts.Last().ObservedEffects);
        j = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(j))!;
        Assert.True(Retire(j, status: "Cancelled"));
        Assert.Equal(2, j.Receipts.Count); Assert.Equal(hash, j.CommittedHash);
    }

    [Fact]
    public void Known_old_validation_failure_is_preserved_instead_of_being_credited_as_success()
    {
        var j = Pending(); const string failure = "This settlement or treaty requires the current rulers of both named kingdoms.";
        j.Agreement.State = AgreementState.RecoveryRequired; j.Agreement.Failure = failure;
        j.Receipts.Add(new ActionReceipt { State = AgreementState.RecoveryRequired, Message = failure });
        Assert.True(Retire(j, status: "Failed", failure: failure));
        Assert.Equal(AgreementState.Impossible, j.Agreement.State);
        Assert.StartsWith(failure, j.Agreement.Failure);
        Assert.Contains(j.Receipts, r => r.State == AgreementState.RecoveryRequired && r.Message == failure);
        Assert.DoesNotContain(j.Receipts, r => r.State == AgreementState.Completed);
    }

    [Theory]
    [InlineData("live-realm")]
    [InlineData("treaty-recorded")]
    [InlineData("wrong-record")]
    [InlineData("not-applied")]
    [InlineData("native-error")]
    [InlineData("uncertain-recovery")]
    [InlineData("missing-npc-consent")]
    [InlineData("modified-terms")]
    [InlineData("payment-bargain")]
    [InlineData("fulfilled")]
    [InlineData("returned")]
    [InlineData("later-installment")]
    [InlineData("repeated-delivery")]
    [InlineData("unrelated-action")]
    [InlineData("already-completed")]
    [InlineData("operator")]
    public void Recovery_refuses_ambiguous_effects_custody_or_authority_without_any_mutation(string invalid)
    {
        var j = Pending(); bool captured = true, applied = true, eliminated = true, recorded = false;
        string status = "Executing", failure = "";
        switch (invalid)
        {
            case "live-realm": eliminated = false; break;
            case "treaty-recorded": recorded = true; break;
            case "wrong-record": captured = false; break;
            case "not-applied": applied = false; break;
            case "native-error": status = "Failed"; failure = j.Agreement.Failure = "Native executor failed."; break;
            case "uncertain-recovery": j.RecoveryDetails = "Unproven rollback."; break;
            case "missing-npc-consent": j.Agreement.NpcAcceptedHash = ""; break;
            case "modified-terms": j.Agreement.Clauses[0].Arguments["targetKingdomId"] = "different"; break;
            case "payment-bargain": j.Agreement.Clauses.Add(new ActionClause { Id = "pay", ActionId = "give.gold" }); break;
            case "fulfilled": j.Steps[0].FulfilledInstallments = 1; break;
            case "returned": j.Steps[0].Returned = true; break;
            case "later-installment": j.Steps[0].Installment = 1; break;
            case "repeated-delivery": j.Agreement.Clauses[0].Installments = 2; break;
            case "unrelated-action": j.Agreement.Clauses[0].ActionId = "supply_agreement"; break;
            case "already-completed": j.Agreement.State = AgreementState.Completed; break;
            case "operator": j.OperatorAuthorized = true; j.Agreement.Origin = ActionOrigin.Console; break;
        }
        // Re-sign shape changes so the action/effect guards, rather than only the hash guard, are exercised.
        if (invalid is "payment-bargain" or "repeated-delivery" or "unrelated-action" or "operator")
            j.CommittedHash = j.Agreement.PlayerAcceptedHash = j.Agreement.NpcAcceptedHash = j.Agreement.TermsHash();
        string before = JsonSerializer.Serialize(j);
        Assert.False(Retire(j, captured, applied, status, failure, eliminated, recorded));
        Assert.Equal(before, JsonSerializer.Serialize(j));
    }
}
