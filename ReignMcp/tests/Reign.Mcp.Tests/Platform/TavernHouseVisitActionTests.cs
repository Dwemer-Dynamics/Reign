using System.Globalization;
using System.Text.Json;
using Reign.Core.Contracts.Actions;
using ReignBeta.Shared.Characters;

namespace Reign.Mcp.Tests;

public sealed class TavernHouseVisitActionTests
{
    private static TavernHouseActionContext Scope() => new() { CampaignId = "campaign", TimelineId = "timeline", TownId = "town", SessionId = "session",
        PlayerId = "player", MadamId = "madam", Roster = Enumerable.Range(0, 5).Select(i => new TavernHouseActionPerson {
            HeroId = i == 0 ? "madam" : "worker" + i, Name = i == 0 ? "Aelia" : "Worker " + i, Available = true, Adult = true }).ToList() };
    private static ActionAgreement Offer(TavernHouseActionContext scope, params int[] prices)
    {
        var a = new ActionAgreement { Id = "visit", PlayerId = scope.PlayerId, NpcId = scope.MadamId, ProposerId = scope.PlayerId,
            ConversationId = "conversation", Origin = ActionOrigin.Conversation, State = AgreementState.Offered };
        var c = new ActionClause { Id = "visit-clause", ActionId = TavernHouseVisitAction.Id, ActorId = a.PlayerId, RecipientId = a.NpcId };
        TavernHouseVisitAction.Bind(c, scope);
        for (int i = 0; i < prices.Length; i++) { c.Arguments["participant" + (i + 1)] = scope.Roster[i].HeroId; c.Arguments["price" + (i + 1)] = prices[i].ToString(CultureInfo.InvariantCulture); }
        c.Arguments["totalGold"] = prices.Sum(p => (long)p).ToString(CultureInfo.InvariantCulture); a.Clauses.Add(c); return a;
    }
    [Fact] public void ExactNamesPricesFreeAndFourParticipantsSurviveSerialization()
    {
        var scope = Scope(); var a = Offer(scope, 120, 0, 200, 300);
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 1)); Assert.Empty(TavernHouseVisitAction.Validate(a, scope));
        var restored = JsonSerializer.Deserialize<ActionAgreement>(JsonSerializer.Serialize(a))!;
        Assert.Equal(620, TavernHouseVisitAction.Quote(restored, scope).TotalGold);
        Assert.Equal(a.TermsHash(), restored.TermsHash()); Assert.Equal(TavernHouseVisitAction.Fingerprint(a), TavernHouseVisitAction.Fingerprint(restored));
        Assert.Contains("Aelia: 120 denars", TavernHouseVisitAction.Sentence(a, scope));
        Assert.Contains("Worker 1: 0 denars", TavernHouseVisitAction.Sentence(a, scope));
        Assert.Contains("agreed free", TavernHouseVisitAction.Sentence(Offer(scope, 0), scope));
    }
    [Fact] public void InvalidAmountsDuplicatesMissingPairsAndOverflowCannotBecomeBookings()
    {
        var scope = Scope();
        foreach (string value in new[] { "-1", "1.5", "1,000", "+1", "2147483648", "one hundred" })
        { var a = Offer(scope, 120); a.Clauses[0].Arguments["price1"] = value; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope)); }
        var duplicate = Offer(scope, 1, 2); duplicate.Clauses[0].Arguments["participant2"] = "madam"; Assert.NotEmpty(TavernHouseVisitAction.Validate(duplicate, scope));
        var gap = Offer(scope, 1, 2); gap.Clauses[0].Arguments.Remove("price1"); Assert.NotEmpty(TavernHouseVisitAction.Validate(gap, scope));
        Assert.NotEmpty(TavernHouseVisitAction.Validate(Offer(scope, int.MaxValue, 1), scope));
        var wrongTotal = Offer(scope, 20); wrongTotal.Clauses[0].Arguments["totalGold"] = "21"; Assert.NotEmpty(TavernHouseVisitAction.Validate(wrongTotal, scope));
        var fifth = Offer(scope, 1); fifth.Clauses[0].Arguments["participant5"] = "worker4"; fifth.Clauses[0].Arguments["price5"] = "1";
        Assert.NotEmpty(AgreementRules.Validate(fifth, ReignActionCatalog.Find, 1));
    }
    [Theory] [InlineData("campaignId")] [InlineData("timelineId")] [InlineData("townId")] [InlineData("sessionId")] [InlineData("rosterHash")]
    public void ContextCannotBeCarriedToAnotherHouseSessionOrRoster(string key)
    { var scope = Scope(); var a = Offer(scope, 1); a.Clauses[0].Arguments[key] = "other"; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope)); }
    [Fact] public void UnavailableChildrenMixedPackagesAndSchedulingAreRejected()
    {
        var scope = Scope(); var a = Offer(scope, 1); scope.Roster[0].Available = false; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
        scope.Roster[0].Available = true; scope.Roster[0].Adult = false; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
        scope = Scope(); a = Offer(scope, 1); a.Clauses.Add(new ActionClause { ActionId = "give.gold" }); Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
        a = Offer(scope, 1); a.Clauses[0].DueDay = 2; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
        a = Offer(scope, 1); a.Clauses[0].Installments = 2; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
        a = Offer(scope, 1); a.Origin = ActionOrigin.Console; Assert.NotEmpty(TavernHouseVisitAction.Validate(a, scope));
    }
    [Fact] public void DiscoveryRequiresTavernPlayerActorAndEmptyPackage()
    {
        var action = ReignActionCatalog.Find(TavernHouseVisitAction.Id)!;
        Assert.False(TavernHouseVisitAction.Visible(action, null, true, 0)); Assert.True(TavernHouseVisitAction.Visible(action, Scope(), true, 0));
        Assert.False(TavernHouseVisitAction.Visible(action, Scope(), false, 0)); Assert.False(TavernHouseVisitAction.Visible(action, Scope(), true, 1));
    }
    [Fact] public void FingerprintBindsExecutableTermsWithoutTransientIdsOrCulture()
    {
        var scope = Scope(); var a = Offer(scope, 120, 50); string fingerprint = TavernHouseVisitAction.Fingerprint(a);
        a.Id = "generated-elsewhere"; a.Clauses[0].Id = "generated-clause"; a.Clauses[0].DisplayText = "Presentation";
        var prior = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA"); Assert.Equal(fingerprint, TavernHouseVisitAction.Fingerprint(a)); }
        finally { CultureInfo.CurrentCulture = prior; }
        a.Clauses[0].Arguments["price1"] = "121"; Assert.NotEqual(fingerprint, TavernHouseVisitAction.Fingerprint(a));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void UnknownNativeOutcomeNeverRetriesOrInfersPaymentFromLaterWallet(bool debitBeforeThrow)
    {
        int gold = 1000, calls = 0; var receipt = new TavernHouseVisitReceipt { PaidGold = 120 };
        Assert.False(TavernHouseVisitAction.TryPay(receipt, () => gold, () => { calls++; if (debitBeforeThrow) gold -= 120; throw new InvalidOperationException(); }));
        receipt = JsonSerializer.Deserialize<TavernHouseVisitReceipt>(JsonSerializer.Serialize(receipt))!;
        gold = 50;
        Assert.False(TavernHouseVisitAction.TryPay(receipt, () => gold, () => calls++)); Assert.Equal(1, calls);
        Assert.False(receipt.Paid); Assert.Equal("recovery_required", receipt.PaymentState);
        Assert.Equal(1000, receipt.PaymentBeforeGold); Assert.Equal(debitBeforeThrow ? 880 : 1000, receipt.PaymentAfterGold);
    }
    [Fact] public void PaidReceiptAndJournalReplayChargeOnceAcrossLostConfirmationAndReload()
    {
        var a = Offer(Scope(), 120); AgreementRules.Accept(a, a.PlayerId, a.TermsHash()); AgreementRules.Accept(a, a.NpcId, a.TermsHash());
        var backend = new VisitBackend(); var journal = new AgreementJournal { Agreement = a };
        ReignAgreementEngine.Commit(journal, backend, 1); Assert.Equal(880, backend.Gold); Assert.Equal(AgreementState.Waiting, a.State);
        journal = JsonSerializer.Deserialize<AgreementJournal>(JsonSerializer.Serialize(journal))!;
        backend.Receipt = JsonSerializer.Deserialize<TavernHouseVisitReceipt>(JsonSerializer.Serialize(backend.Receipt))!;
        for (int i = 0; i < 3; i++) ReignAgreementEngine.Advance(journal, backend, 1);
        Assert.Equal(1, backend.Transfers); Assert.Equal(880, backend.Gold);
        backend.Receipt.ServerConfirmed = true; ReignAgreementEngine.Advance(journal, backend, 1);
        Assert.Equal(AgreementState.Completed, journal.Agreement.State); Assert.Equal(1, backend.Transfers);
        Assert.True(TavernHouseVisitAction.TryPay(backend.Receipt, () => backend.Gold, () => backend.Transfers++)); Assert.Equal(1, backend.Transfers);
    }
    private sealed class VisitBackend : IAgreementBackend
    {
        public int Gold = 1000, Transfers; public TavernHouseVisitReceipt Receipt = new() { PaidGold = 120 };
        public IReadOnlyList<string> Preflight(ActionAgreement a) => TavernHouseVisitAction.Validate(a, Scope());
        public string Capture(ActionAgreement a, ActionClause c) => "prepared";
        public void Apply(ActionAgreement a, ActionClause c, string s, int i) { if (!TavernHouseVisitAction.TryPay(Receipt, () => Gold, () => { Transfers++; Gold -= 120; })) throw new InvalidOperationException(); }
        public bool Observe(ActionAgreement a, ActionClause c, string s, int i) => Receipt.ServerConfirmed;
        public void Reserve(ActionAgreement a, ActionClause c, string s) { }
        public void Return(ActionAgreement a, ActionClause c, string s, int i) { }
        public void ApplyAssetsAtomically(ActionAgreement a, IReadOnlyList<AgreementStep> s) => throw new InvalidOperationException("A visit must not use the generic gold adapter.");
        public bool ConditionSatisfied(ActionClause c, double now) => true;
        public double DeliveryDays(ActionAgreement a) => 0;
        public void SaveJournal(AgreementJournal j) { }
    }
}
