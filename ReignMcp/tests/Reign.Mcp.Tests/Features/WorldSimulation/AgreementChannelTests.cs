using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class AgreementChannelTests
{
    private static ActionAgreement Package(AgreementChannel channel, double due = 0)
    {
        var agreement = new ActionAgreement { PlayerId = "player", NpcId = "sira", ProposerId = "player",
            ConversationId = "campaign:timeline:sira", Origin = ActionOrigin.Conversation, Channel = channel,
            State = AgreementState.Accepted, CreatedDay = 100,
            Clauses = new() {
                new() { ActionId = "give.prisoner", ActorId = "sira", RecipientId = "player", Arguments = new() { ["assetId"] = "ira" } },
                new() { ActionId = "give.prisoner", ActorId = "sira", RecipientId = "player", Arguments = new() { ["assetId"] = "rhagaea" } },
                new() { ActionId = "accept_temporary_party_guest", ActorId = "sira", RecipientId = "player", DueDay = due }
            } };
        agreement.PlayerAcceptedHash = agreement.NpcAcceptedHash = agreement.TermsHash();
        return agreement;
    }

    [Theory]
    [InlineData(AgreementChannel.InPerson, 0)]
    [InlineData(AgreementChannel.Correspondence, 5)]
    [InlineData(AgreementChannel.Unknown, 5)]
    public void MixedPackageUsesChannelAndExecutesExactlyOnce(AgreementChannel channel, int delivery)
    {
        var journal = new AgreementJournal { Agreement = Package(channel) };
        var backend = new Backend();
        var accepted = ReignAgreementEngine.Commit(journal, backend, 100);
        Assert.Equal(100 + delivery, journal.ArrivalDay);
        Assert.Equal(delivery == 0 ? AgreementState.Completed : AgreementState.InTransit, accepted.State);
        if (delivery > 0) { Assert.Equal(0, backend.Joins); Assert.Equal(0, backend.Prisoners); }
        ReignAgreementEngine.Advance(journal, backend, 105);
        ReignAgreementEngine.Commit(journal, backend, 105);
        ReignAgreementEngine.Advance(journal, backend, 106);
        Assert.Equal(1, backend.Joins);
        Assert.Equal(2, backend.Prisoners);
        Assert.All(ReignAgreementEngine.Describe(journal, "", 106).Clauses, c => Assert.Equal("completed", c.Status));
    }

    [Fact]
    public void TransitObservationsDoNotAccumulateCountdownReceipts()
    {
        var journal = new AgreementJournal { Agreement = Package(AgreementChannel.Correspondence) };
        var backend = new Backend();
        ReignAgreementEngine.Commit(journal, backend, 100);
        int originalCount = journal.Receipts.Count;
        for (int tick = 1; tick < 200; tick++) ReignAgreementEngine.Advance(journal, backend, 100 + tick / 1000d);
        Assert.Equal(originalCount, journal.Receipts.Count);
        Assert.Contains("campaign days remain", ReignAgreementEngine.Describe(journal, "", 101).Message);
        Assert.Equal(0, backend.Joins);
    }

    [Fact]
    public void ReadOnlyRecoveryViewToleratesMissingOrDuplicateClauseIdentity()
    {
        var journal = new AgreementJournal { Agreement = Package(AgreementChannel.Correspondence), ArrivalDay = 105,
            Steps = new() { new() { ClauseId = "missing", Reserved = true } } };
        Assert.Equal("recovery_required", Assert.Single(ReignAgreementEngine.Describe(journal, "", 101).Clauses).Status);
        Assert.Equal(AgreementState.Accepted, journal.Agreement.State);
        Assert.Empty(journal.Receipts);
    }

    [Fact]
    public void InPersonTransportDoesNotEraseExplicitFutureTerms()
    {
        var journal = new AgreementJournal { Agreement = Package(AgreementChannel.InPerson, 112) };
        var backend = new Backend();
        Assert.Equal(AgreementState.Waiting, ReignAgreementEngine.Commit(journal, backend, 100).State);
        Assert.Equal(100, journal.ArrivalDay);
        Assert.Equal(0, backend.Joins);
        Assert.Equal(0, backend.Prisoners);
        ReignAgreementEngine.Advance(journal, backend, 112);
        Assert.Equal(1, backend.Joins);
        Assert.Equal(2, backend.Prisoners);
    }

    [Fact]
    public void AChannelFlagWithoutConversationOriginAndContextIsNotImmediate()
    {
        var agreement = Package(AgreementChannel.InPerson);
        agreement.Origin = ActionOrigin.Console;
        Assert.False(AgreementRules.IsImmediateInPerson(agreement));
        agreement.Origin = ActionOrigin.Conversation; agreement.ConversationId = "";
        Assert.False(AgreementRules.IsImmediateInPerson(agreement));
    }

    [Fact]
    public void NamedPrisonerHandoffPinsAControlledDungeonAndVerifiesNativeCustody()
    {
        var root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Actions", "ReignAssetTransfer.cs"));
        Assert.Contains("settlement == from.CurrentSettlement", source);
        Assert.Contains("receiverClan.Kingdom?.Leader == to && settlement.OwnerClan?.Kingdom == receiverClan.Kingdom", source);
        Assert.Contains("result[\"recipientSettlementId\"] = destination.Settlement.StringId", source);
        Assert.Contains("PrisonerRecipient(from, to, Troop(c)) != SavedPrisonerCustody(saved, \"recipient\")", source);
        Assert.Contains("TransferPrisonerAction.Apply(prisoner, source, recipient)", source);
        Assert.Contains("!HoldsNamedPrisoner(recipient, prisoner) || source.PrisonRoster.GetTroopCount(prisoner) != 0", source);
        Assert.Contains("TransferPrisonerAction.Apply(prisoner, recipient, source)", source);
        Assert.Contains("if (!HoldsNamedPrisoner(source, prisoner))", source);
    }

    private sealed class Backend : IAgreementBackend
    {
        public int Joins, Prisoners;
        private readonly HashSet<string> _applied = new();
        public IReadOnlyList<string> Preflight(ActionAgreement a) => Array.Empty<string>();
        public string Capture(ActionAgreement a, ActionClause c) => c.ActionId;
        public void Reserve(ActionAgreement a, ActionClause c, string s) { }
        public void Return(ActionAgreement a, ActionClause c, string s, int n) { }
        public void Apply(ActionAgreement a, ActionClause c, string s, int n) { Assert.True(_applied.Add(c.Id)); Joins++; }
        public void ApplyAssetsAtomically(ActionAgreement a, IReadOnlyList<AgreementStep> steps) => Prisoners += steps.Count;
        public bool Observe(ActionAgreement a, ActionClause c, string s, int n) => _applied.Contains(c.Id);
        public bool ConditionSatisfied(ActionClause c, double now) => true;
        public double DeliveryDays(ActionAgreement a) => AgreementRules.IsImmediateInPerson(a) ? 0 : 5;
        public void SaveJournal(AgreementJournal j) { }
    }
}
