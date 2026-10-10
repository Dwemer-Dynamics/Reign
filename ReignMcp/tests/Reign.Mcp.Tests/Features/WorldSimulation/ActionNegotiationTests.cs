using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ActionNegotiationTests
{
    // Captured game-client consent: this literal must never be recomputed by the
    // runtime under test. Its fractional day exposed Framework/modern .NET drift.
    private const string NativeHonorHash = "9cb08740d540a046ae60f62d8df4923980e111a5f70af7c728e04ace643aa25a";
    private static ActionAgreement NativeHonorOffer() => new()
    {
        Id = "73e5507f65e34f9badf7a651b6e5ca7e", Revision = 2,
        PlayerId = "main_hero", NpcId = "lord_5_10", ProposerId = "main_hero",
        ConversationId = "woERXl3xD23E:main_258fb5560008_recovered_64e99061c8_branch_8dcdf209b1:lord_5_10",
        Origin = ActionOrigin.Conversation, State = AgreementState.Offered,
        CreatedDay = 91141.46058793287, PlayerAcceptedHash = NativeHonorHash,
        Clauses = new()
        {
            new() { Id = "196aa476b717459ca1237c949a831284", ActionId = "duel_player",
                ActorId = "lord_5_10", RecipientId = "main_hero",
                Arguments = new() { ["terms.duelMode"] = "honor" },
                DisplayText = "I would like you to duel player (Duel mode: honor)" }
        }
    };

    [Theory] [InlineData("en-US")] [InlineData("de-DE")] [InlineData("fr-FR")] [InlineData("tr-TR")]
    public void NativeFractionalDayConsentSurvivesModernRuntimeAndWireRoundTrip(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            var original = NativeHonorOffer();
            var wire = System.Text.Json.JsonSerializer.Deserialize<ActionAgreement>(
                System.Text.Json.JsonSerializer.Serialize(original))!;
            Assert.Equal(NativeHonorHash, original.TermsHash());
            Assert.Equal(NativeHonorHash, wire.TermsHash());
            Assert.Equal(NativeHonorHash, wire.PlayerAcceptedHash);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Theory] [InlineData("accept")] [InlineData("reject")] [InlineData("counter")]
    public void NativeFractionalDayOfferAllowsOnlyTheExactEchoedDecision(string kind)
    {
        var offer = NativeHonorOffer();
        var decision = new ActionDecision { Kind = kind, ProposalId = offer.Id, Revision = offer.Revision,
            TermsHash = NativeHonorHash };
        if (kind == "counter") decision.Clauses.Add(new() { ActionId = "duel_player",
            ActorId = offer.NpcId, RecipientId = offer.PlayerId, DueDay = 91142,
            Arguments = new() { new() { Key = "terms.duelMode", Value = "honor" } } });
        Assert.Empty(ActionNegotiation.ValidateDecision(offer, decision, offer.PlayerId, offer.NpcId,
            offer.ConversationId, offer.CreatedDay));
        if (kind == "counter")
        {
            var counter = ActionNegotiation.CreateProposal(offer, decision, offer.PlayerId, offer.NpcId,
                offer.ConversationId, offer.CreatedDay);
            Assert.Equal(3, counter.Revision);
            Assert.Empty(counter.PlayerAcceptedHash);
            Assert.Empty(counter.NpcAcceptedHash);
            Assert.False(counter.HasRequiredConsents);
        }
        decision.TermsHash = "ecace0d1cef25cbc6615393561d47cc3efb492101fb285338ee439215d3b23e4";
        Assert.NotEmpty(ActionNegotiation.ValidateDecision(offer, decision, offer.PlayerId, offer.NpcId,
            offer.ConversationId, offer.CreatedDay));
    }

    [Fact] public void SavedNativeFractionalDayConsentRemainsValidWithoutResigning()
    {
        var offer = NativeHonorOffer();
        offer.NpcAcceptedHash = NativeHonorHash;
        offer.State = AgreementState.Completed;
        Assert.True(offer.HasBothConsents);
        var receipt = new AgreementJournal { Agreement = offer, CommittedHash = NativeHonorHash,
            Steps = new() { new() { ClauseId = offer.Clauses[0].Id, Completed = true, FulfilledInstallments = 1 } } };
        var draft = ReplayDraft(receipt);
        Assert.True(AgreementRules.IsExactCompletedReplay(draft, receipt));
        Assert.Empty(draft.PlayerAcceptedHash);
        Assert.Empty(draft.NpcAcceptedHash);
    }

    [Theory] [InlineData("day")] [InlineData("revision")] [InlineData("mode")]
    [InlineData("wording")] [InlineData("conversation")]
    public void NativeHashCompatibilityCannotAuthorizeChangedTerms(string change)
    {
        var offer = NativeHonorOffer();
        var decision = new ActionDecision { Kind = "accept", ProposalId = offer.Id, Revision = offer.Revision,
            TermsHash = NativeHonorHash };
        switch (change)
        {
            case "day": offer.CreatedDay += 0.001; break;
            case "revision": offer.Revision++; break;
            case "mode": offer.Clauses[0].Arguments["terms.duelMode"] = "lethal"; break;
            case "wording": offer.Clauses[0].DisplayText += " tomorrow"; break;
            case "conversation": offer.ConversationId += ":other"; break;
        }
        Assert.NotEqual(NativeHonorHash, offer.TermsHash());
        Assert.NotEmpty(ActionNegotiation.ValidateDecision(offer, decision, offer.PlayerId, offer.NpcId,
            offer.ConversationId, offer.CreatedDay));
    }

    [Theory] [InlineData("created")] [InlineData("expires")] [InlineData("due")]
    [InlineData("duration")] [InlineData("interval")]
    public void NegativeZeroTimingRetainsFrameworkConsentRepresentation(string field)
    {
        var offer = NativeHonorOffer();
        if (field == "created") offer.CreatedDay = 0d;
        string positiveHash = offer.TermsHash();
        double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        switch (field)
        {
            case "created": offer.CreatedDay = negativeZero; break;
            case "expires": offer.ExpiresDay = negativeZero; break;
            case "due": offer.Clauses[0].DueDay = negativeZero; break;
            case "duration": offer.Clauses[0].DurationDays = negativeZero; break;
            case "interval": offer.Clauses[0].IntervalDays = negativeZero; break;
        }
        Assert.Equal(positiveHash, offer.TermsHash());
    }

    private static AgreementJournal CompletedReplayJournal()
    {
        var a = Offer(); a.ConversationId = "campaign:timeline:npc";
        a.Clauses[0] = new() { Id = "appointment", ActionId = "court.serve_chancellor", ActorId = "npc", RecipientId = "player",
            DisplayText = "Serve as Chancellor at 500 denars per Active day", Arguments = new() { ["activeDailySalary"] = "500" } };
        a.PlayerAcceptedHash = a.NpcAcceptedHash = a.TermsHash(); a.State = AgreementState.Completed;
        return new() { Agreement = a, CommittedHash = a.TermsHash(),
            Steps = new() { new() { ClauseId = "appointment", Completed = true, FulfilledInstallments = 1 } } };
    }
    private static ActionAgreement ReplayDraft(AgreementJournal j)
    {
        var a = System.Text.Json.JsonSerializer.Deserialize<ActionAgreement>(System.Text.Json.JsonSerializer.Serialize(j.Agreement))!;
        a.State = AgreementState.Draft; a.PlayerAcceptedHash = a.NpcAcceptedHash = ""; return a;
    }
    [Fact] public void CompletedExactReplayUsesSavedConsentWithoutSigningOrMutatingEitherInput()
    {
        var saved = CompletedReplayJournal(); var draft = ReplayDraft(saved);
        var before = System.Text.Json.JsonSerializer.Serialize(new { saved, draft });
        Assert.True(AgreementRules.IsExactCompletedReplay(draft, saved));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(new { saved, draft }));
        Assert.Empty(draft.PlayerAcceptedHash); Assert.Empty(draft.NpcAcceptedHash);
    }
    [Theory] [InlineData("id")] [InlineData("revision")] [InlineData("version")] [InlineData("player")]
    [InlineData("npc")] [InlineData("proposer")] [InlineData("conversation")] [InlineData("origin")]
    [InlineData("action")] [InlineData("actor")] [InlineData("recipient")] [InlineData("amount")]
    [InlineData("wording")] [InlineData("clause-id")] [InlineData("timing")]
    public void CompletedReplayCannotAuthorizeDifferentIdentityTermsOrPresentation(string change)
    {
        var saved = CompletedReplayJournal(); var draft = ReplayDraft(saved); var c = draft.Clauses[0];
        switch (change)
        {
            case "id": draft.Id += "_new"; break;
            case "revision": draft.Revision++; break;
            case "version": draft.Version++; break;
            case "player": draft.PlayerId = "other"; break;
            case "npc": draft.NpcId = "other"; break;
            case "proposer": draft.ProposerId = "npc"; break;
            case "conversation": draft.ConversationId = "campaign:other-timeline:npc"; break;
            case "origin": draft.Origin = ActionOrigin.Console; break;
            case "action": c.ActionId = "court.leave_chancellor"; break;
            case "actor": c.ActorId = "other"; break;
            case "recipient": c.RecipientId = "other"; break;
            case "amount": c.Arguments["activeDailySalary"] = "1000"; break;
            case "wording": c.DisplayText += " and another payment"; break;
            case "clause-id": c.Id = "new-clause"; break;
            case "timing": c.Timing = TermTiming.OnArrival; break;
        }
        draft.PlayerAcceptedHash = draft.NpcAcceptedHash = draft.TermsHash();
        Assert.False(AgreementRules.IsExactCompletedReplay(draft, saved));
    }
    [Theory] [InlineData("pending")] [InlineData("operator")] [InlineData("console")]
    [InlineData("unsigned")] [InlineData("stale-consent")] [InlineData("hash")]
    [InlineData("no-steps")] [InlineData("incomplete")] [InlineData("no-installment")]
    public void CompletedReplayRequiresAuthoritativeCompletedConversationJournal(string invalid)
    {
        var saved = CompletedReplayJournal(); var draft = ReplayDraft(saved);
        switch (invalid)
        {
            case "pending": saved.Agreement.State = AgreementState.Waiting; break;
            case "operator": saved.OperatorAuthorized = true; break;
            case "console": saved.Agreement.Origin = ActionOrigin.Console; break;
            case "unsigned": saved.Agreement.NpcAcceptedHash = ""; break;
            case "stale-consent": saved.Agreement.PlayerAcceptedHash = "stale"; break;
            case "hash": saved.CommittedHash = "different"; break;
            case "no-steps": saved.Steps.Clear(); break;
            case "incomplete": saved.Steps[0].Completed = false; break;
            case "no-installment": saved.Steps[0].FulfilledInstallments = 0; break;
        }
        Assert.False(AgreementRules.IsExactCompletedReplay(draft, saved));
    }
    [Fact] public void CompletedReplayRequiresAnExistingJournal()
    {
        Assert.False(AgreementRules.IsExactCompletedReplay(Offer(), null!));
        Assert.False(AgreementRules.IsExactCompletedReplay(null!, CompletedReplayJournal()));
    }

    [Theory] [InlineData("renew_temporary_party_guest")] [InlineData("end_temporary_party_guest")]
    public void GuestReviewBindingIsAutomaticTypedAndIncludedInExactConsent(string actionId)
    {
        var clause = new ActionClause { ActionId = actionId, ActorId = "npc", RecipientId = "player" };
        var agreement = new ActionAgreement { PlayerId = "player", NpcId = "npc", Clauses = new() { clause } };
        var unboundHash = agreement.TermsHash();
        const double deadline = 91098.85670105093;
        Assert.True(AgreementRules.BindGuestReview(agreement, clause, "guest-original", deadline, 7));
        Assert.NotEqual(unboundHash, agreement.TermsHash());
        var definition = ReignActionCatalog.Find(actionId)!;
        var bindings = definition.Parameters.Where(p => p.Automatic).ToList();
        Assert.Equal(3, bindings.Count);
        Assert.All(bindings, p => Assert.False(p.Required));
        var terms = AgreementRules.WorldTerms(definition, clause.Arguments);
        Assert.Equal("guest-original", terms["agreementId"]);
        Assert.Equal(deadline, Assert.IsType<double>(terms["expectedReviewDueDay"]));
        Assert.Equal(7, Assert.IsType<int>(terms["expectedAgreementRevision"]));
        agreement.PlayerAcceptedHash = agreement.TermsHash();
        Assert.False(AgreementRules.BindGuestReview(agreement, clause, "replacement-guest", deadline + 5, 8));
        Assert.Equal(agreement.PlayerAcceptedHash, agreement.TermsHash());
        Assert.Equal("guest-original", clause.Arguments["terms.agreementId"]);
        clause.Arguments["terms.expectedAgreementRevision"] = "8";
        Assert.NotEqual(agreement.PlayerAcceptedHash, agreement.TermsHash());
    }

    [Theory] [InlineData("renew_temporary_party_guest")] [InlineData("end_temporary_party_guest")]
    public void GuestReviewCannotFillMissingBindingAfterEitherParticipantSigned(string actionId)
    {
        foreach (bool npcSigned in new[] { false, true })
        {
            var clause = new ActionClause { ActionId = actionId };
            var agreement = new ActionAgreement { Clauses = new() { clause } };
            if (npcSigned) agreement.NpcAcceptedHash = agreement.TermsHash();
            else agreement.PlayerAcceptedHash = agreement.TermsHash();
            Assert.Throws<ArgumentException>(() => AgreementRules.BindGuestReview(agreement, clause, "guest", 91098.5, 0));
            Assert.Empty(clause.Arguments);
        }
    }

    [Fact] public void ExplicitCounterRevisionClearsOldConsentBeforeBindingANewGuestTerm()
    {
        var agreement = Offer();
        agreement.State = AgreementState.Counteroffered;
        agreement.NpcAcceptedHash = agreement.TermsHash();
        var previousRevision = agreement.Revision;
        var clause = new ActionClause { ActionId = "renew_temporary_party_guest", ActorId = "npc", RecipientId = "player" };
        agreement.Clauses.Add(clause);
        agreement.Revise();
        Assert.Equal(previousRevision + 1, agreement.Revision);
        Assert.True(AgreementRules.BindGuestReview(agreement, clause, "guest", 91098.5, 0));
        Assert.Empty(agreement.PlayerAcceptedHash);
        Assert.Empty(agreement.NpcAcceptedHash);
        Assert.False(agreement.HasRequiredConsents);
    }

    [Theory] [InlineData("partial")] [InlineData("missing")] [InlineData("nan")]
    [InlineData("infinite")] [InlineData("zero")] [InlineData("revision")]
    public void GuestReviewRejectsIncompleteOrUnavailableNativeBindingWithoutPartialMutation(string invalid)
    {
        var clause = new ActionClause { ActionId = "renew_temporary_party_guest" };
        if (invalid == "partial") clause.Arguments["terms.agreementId"] = "guest";
        var agreement = new ActionAgreement { Clauses = new() { clause } };
        var hash = agreement.TermsHash();
        var deadline = invalid == "nan" ? double.NaN : invalid == "infinite" ? double.PositiveInfinity : invalid == "zero" ? 0 : 91098.5;
        Assert.Throws<ArgumentException>(() => AgreementRules.BindGuestReview(agreement, clause,
            invalid == "missing" ? "" : "guest", deadline, invalid == "revision" ? int.MaxValue : 0));
        Assert.Equal(hash, agreement.TermsHash());
    }

    [Theory] [InlineData("transfer_item")] [InlineData("trade_package")]
    public void CompatibilityItemTransferMapsItemAndExactIntegerAmountToExecutableTerms(string actionId)
    {
        var terms = AgreementRules.WorldTerms(ReignActionCatalog.Find(actionId)!,
            new Dictionary<string, string> { ["Item"] = "grain", ["Amount"] = "3" });
        Assert.Equal("grain", terms["itemId"]);
        Assert.Equal(3, Assert.IsType<int>(terms["amount"]));
        Assert.False(terms.ContainsKey("Item"));
        Assert.False(terms.ContainsKey("Amount"));
    }
    [Fact] public void CompoundTradePreservesItsItemAlongsideThePromisedPayment()
    {
        var terms = AgreementRules.WorldTerms(ReignActionCatalog.Find("trade_package")!,
            new Dictionary<string, string> { ["GoldAmount"] = "47", ["Item"] = "grain", ["Amount"] = "2" });
        Assert.Equal(47, Assert.IsType<int>(terms["GoldAmount"]));
        Assert.Equal("grain", terms["itemId"]);
        Assert.Equal(2, Assert.IsType<int>(terms["amount"]));
    }
    [Theory] [InlineData("1.5")] [InlineData("0")] [InlineData("-1")]
    [InlineData("2147483648")] [InlineData("NaN")] [InlineData("1e2")]
    public void WorldItemTermsCannotTruncateOrCoerceInvalidQuantities(string amount)
    {
        Assert.Throws<ArgumentException>(() => AgreementRules.WorldTerms(ReignActionCatalog.Find("transfer_item")!,
            new Dictionary<string, string> { ["Item"] = "grain", ["Amount"] = amount }));
    }
    [Fact] public void WorldTermsPreserveTypedNumbersAcrossPrefixedAndPlainFields()
    {
        var payment = AgreementRules.WorldTerms(ReignActionCatalog.Find("transfer_gold")!,
            new Dictionary<string, string> { ["GoldAmount"] = "21" });
        Assert.Equal(21, Assert.IsType<int>(payment["GoldAmount"]));
        var treaty = AgreementRules.WorldTerms(ReignActionCatalog.Find("offer_tribute_peace")!,
            new Dictionary<string, string> { ["terms.dailyTribute"] = "19", ["terms.durationDays"] = "10.5" });
        Assert.Equal(19, Assert.IsType<int>(treaty["dailyTribute"]));
        Assert.Equal(10.5, Assert.IsType<double>(treaty["durationDays"]));
    }

    private static ActionAgreement Offer()
    {
        var a = new ActionAgreement { PlayerId = "player", NpcId = "npc", ProposerId = "player",
            ConversationId = "conversation", State = AgreementState.Offered,
            Clauses = new() { new() { ActionId = "give.gold", ActorId = "player", RecipientId = "npc",
                DisplayText = "I wish to give you 100 denars", Arguments = new() { ["quantity"] = "100" } } } };
        a.PlayerAcceptedHash = a.TermsHash(); return a;
    }
    private static ActionDecision Accept(ActionAgreement a) => new() { Kind = "accept", ProposalId = a.Id, Revision = a.Revision, TermsHash = a.TermsHash() };
    private static List<string> Check(ActionAgreement? a, ActionDecision d) => ActionNegotiation.ValidateDecision(a, d, "player", "npc", "conversation", 0);

    [Fact] public void ExactAcceptanceIsValidButDoesNotMutateOrGrantConsent()
    {
        var a = Offer(); Assert.Empty(Check(a, Accept(a))); Assert.Empty(a.NpcAcceptedHash); Assert.Equal(AgreementState.Offered, a.State);
    }
    [Theory] [InlineData("id")] [InlineData("revision")] [InlineData("hash")] [InlineData("participant")] [InlineData("context")] [InlineData("quantity")]
    public void ChangedIdentityOrTermsCannotReuseAcceptance(string field)
    {
        var a = Offer(); var d = Accept(a);
        switch (field) { case "id": d.ProposalId += "other"; break; case "revision": d.Revision++; break;
            case "hash": d.TermsHash = "forged"; break; case "participant": a.NpcId = "other"; break;
            case "context": a.ConversationId = "other"; break; case "quantity": a.Clauses[0].Arguments["quantity"] = "1000"; break; }
        Assert.NotEmpty(Check(a, d));
    }
    [Fact] public void AcceptCannotSmuggleReplacementTerms()
    {
        var a = Offer(); var d = Accept(a); d.Clauses.Add(new()); Assert.NotEmpty(Check(a, d));
    }
    [Fact] public void CounterCreatesNewRevisionWithoutEitherConsent()
    {
        var a = Offer(); var d = Accept(a); d.Kind = "counter";
        d.Clauses.Add(new() { ActionId = "give.gold", ActorId = "player", RecipientId = "npc", Arguments = new() { new() { Key = "quantity", Value = "200" } } });
        Assert.Empty(Check(a, d));
        var counter = ActionNegotiation.CreateProposal(a, d, "player", "npc", "conversation", 0);
        Assert.Equal(a.Id, counter.Id); Assert.Equal(a.Revision + 1, counter.Revision);
        Assert.Empty(counter.PlayerAcceptedHash); Assert.Empty(counter.NpcAcceptedHash); Assert.NotEqual(a.TermsHash(), counter.TermsHash());
    }
    [Fact] public void NpcCannotForcePlayerMovementOrUseUnknownArguments()
    {
        var travel = ReignActionCatalog.All.First(d => d.MovesActor && d.ConversationEligible);
        var d = new ActionDecision { Kind = "propose", Clauses = new() { new() { ActionId = travel.Id, ActorId = "player", RecipientId = "npc" } } };
        Assert.Contains(Check(null, d), e => e.Contains("own movement"));
        d.Clauses[0].ActionId = "give.gold";
        d.Clauses[0].Arguments = new() { new() { Key = "quantity", Value = "100" }, new() { Key = "script", Value = "anything" } };
        Assert.Contains(Check(null, d), e => e.Contains("Unknown parameter"));
    }
    [Fact] public void DuplicateOrMalformedArgumentsFailWithoutThrowing()
    {
        var d = new ActionDecision { Kind = "propose", Clauses = new() { new() { ActionId = "give.gold", ActorId = "npc", RecipientId = "player",
            Arguments = new() { new() { Key = "quantity", Value = "100" }, new() { Key = "quantity", Value = "200" } } } } };
        Assert.NotEmpty(Check(null, d));
        var a = Offer(); a.Clauses = null!; Assert.NotEmpty(Check(a, d));
    }
    [Fact] public void DurationCannotBeIgnoredOrDisagreeWithExecutableArguments()
    {
        var a = Offer(); a.Clauses[0].DurationDays = 3;
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("no duration"));
        a.Clauses[0] = new() { ActionId = "campaign.patrol", ActorId = "npc", RecipientId = "player", DurationDays = 2,
            Arguments = new() { ["targetSettlementId"] = "town", ["terms.durationHours"] = "72" } };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("disagree"));
        a.Clauses[0].Arguments["terms.durationHours"] = "48";
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
    }
    [Theory] [InlineData("0", true)] [InlineData("20", true)] [InlineData("1.5", false)] [InlineData("-1", false)]
    [InlineData("2147483648", false)] [InlineData("NaN", false)]
    public void ExactRecruitmentPaymentAllowsFreeServiceButRejectsInvalidMoney(string value, bool valid)
    {
        var p = ReignActionCatalog.Find("recruit_encountered_resident")!.Parameters.Single();
        Assert.Equal(valid, AgreementRules.ValidateParameterValue(p, value).Length == 0);
    }
    [Fact] public void GuestDurationPreservesFractionalDaysAndNativeBounds()
    {
        var p = ReignActionCatalog.Find("accept_temporary_party_guest")!.Parameters.Single(x => x.Key == "terms.durationDays");
        Assert.Empty(AgreementRules.ValidateParameterValue(p, "10.5"));
        Assert.NotEmpty(AgreementRules.ValidateParameterValue(p, "127"));
        Assert.NotEmpty(AgreementRules.ValidateParameterValue(p, "Infinity"));
    }
    [Theory]
    [InlineData("minor", true)] [InlineData("serious", true)]
    [InlineData("grave", true)] [InlineData("capital", true)]
    [InlineData("1", false)] [InlineData("100", false)] [InlineData("unknown", false)]
    public void ArrestSeverityAcceptsExecutableNamedChoicesInsteadOfNumbers(string value, bool valid)
    {
        var parameter = ReignActionCatalog.Find("prepare_arrest")!.Parameters.Single(p => p.Key == "terms.severity");
        Assert.Equal(new[] { "minor", "serious", "grave", "capital" }, parameter.Choices);
        Assert.Equal(valid, AgreementRules.ValidateParameterValue(parameter, value).Length == 0);
    }

    [Fact] public void ConsentIsNotAnEditableParameter()
    {
        Assert.DoesNotContain(ReignActionCatalog.All.SelectMany(d => d.Parameters), p => p.Key.Contains("consentConfirmed")
            || p.Key.Contains("playerConfirmed") || p.Key.Contains("releaseConfirmed"));
        var a = Offer(); a.Clauses[0] = new() { ActionId = "create_clan_accord", ActorId = "npc", RecipientId = "player",
            Arguments = new() { ["terms.accordType"] = "Trade", ["terms.consentConfirmed"] = "true" } };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("Unknown parameter"));
    }
    [Fact] public void IndivisibleActionsCannotBeRepeatedAsInstallments()
    {
        var a = Offer(); a.Clauses[0] = new() { ActionId = "give.settlement", ActorId = "npc", RecipientId = "player",
            Installments = 2, IntervalDays = 1, Arguments = new() { ["assetId"] = "town" } };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("divisible"));
    }
    [Fact] public void ASelectedTargetDoesNotBecomeAConsentingParticipant()
    {
        var facts = new ActionAuthorityFacts { ActorId = "player", CounterpartyId = "npc", ActingRulerId = "ruler",
            SelectedTargetHeroId = "ruler", DestinationRulerId = "npc" };
        Assert.NotEmpty(ReignActionAuthority.Validate("DiplomacyMakePeace", facts));
        facts.ActingRulerId = "player";
        Assert.Empty(ReignActionAuthority.Validate("DiplomacyMakePeace", facts));
        facts.DestinationRulerId = "new-ruler";
        Assert.NotEmpty(ReignActionAuthority.Validate("DiplomacyMakePeace", facts));
        Assert.Empty(ReignActionAuthority.Validate("DiplomacyDeclareWar", facts));
    }
    [Theory] [InlineData("RegularJoinKingdom")] [InlineData("RegularHireMercenaryClan")]
    public void ClanCommitmentRequiresBothCurrentLeaders(string action)
    {
        var facts = new ActionAuthorityFacts { ActorId = "lord", CounterpartyId = "ruler",
            AffectedClanLeaderId = "lord", DestinationRulerId = "ruler" };
        Assert.Empty(ReignActionAuthority.Validate(action, facts));
        facts.AffectedClanLeaderId = "other-lord";
        Assert.NotEmpty(ReignActionAuthority.Validate(action, facts));
        facts.AffectedClanLeaderId = "lord"; facts.CounterpartyId = "advisor";
        Assert.NotEmpty(ReignActionAuthority.Validate(action, facts));
    }
    [Fact] public void PersonalMarriageCannotSilentlyReplaceAnExplicitSpouse()
    {
        var a = Offer(); a.Clauses[0] = new() { ActionId = "marriage_alliance", ActorId = "npc", RecipientId = "player",
            Arguments = new() { ["terms.marriageHero1StringId"] = "relative" } };
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("two consenting"));
    }
    [Fact] public void ACompoundBargainCannotPromiseContradictoryVotes()
    {
        var a = Offer();
        foreach (string option in new[] { "accept", "reject" })
            a.Clauses.Add(new() { ActionId = "government.support_vote", ActorId = "npc", RecipientId = "player",
                Arguments = new() { ["businessId"] = "hearing", ["optionId"] = option } });
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("opposing votes"));
        a.Clauses[2].Arguments["businessId"] = "another-hearing";
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
    }
    [Fact] public void CourtBargainsCannotSellTheSameAppointmentTwiceOrImmediatelyUndoIt()
    {
        var a = Offer();
        a.Clauses.Add(new() { ActionId = "court.serve_office", ActorId = "npc", RecipientId = "player", Arguments = new() { ["office"] = "Spymaster" } });
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        a.Clauses.Add(new() { ActionId = "court.serve_office", ActorId = "npc", RecipientId = "player", Arguments = new() { ["office"] = "Marshal" } });
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("repeat the same court"));
        a.Clauses[2].ActionId = "court.leave_office"; a.Clauses[2].Arguments.Clear();
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("both appoint and dismiss"));
    }

    private static ActionAgreement Declaration()
    {
        var a = Offer();
        a.Clauses[0] = new() { ActionId = "start_ruling_clan_rebellion", ActorId = "player", RecipientId = "npc",
            DisplayText = "I declare rebellion against your rule of the realm, as leader of my clan",
            Arguments = new() { ["actorKingdomId"] = "realm", ["actorClanId"] = "player-clan" } };
        a.PlayerAcceptedHash = "";
        return a;
    }

    [Fact] public void OnlyThePlayersOwnDeclarationNeedsNoRulerSignature()
    {
        var a = Declaration();
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        Assert.False(a.HasRequiredConsents);
        Assert.True(AgreementRules.Accept(a, "player", a.TermsHash()));
        Assert.True(a.HasRequiredConsents); Assert.False(a.HasBothConsents);
        Assert.Empty(a.NpcAcceptedHash); Assert.Equal(AgreementState.Accepted, a.State);
        a.Clauses[0].Arguments["actorKingdomId"] = "another-realm";
        Assert.False(a.HasRequiredConsents);
        Assert.False(Offer().HasRequiredConsents);
    }

    [Theory] [InlineData("npc-proposer")] [InlineData("npc-actor")] [InlineData("wrong-recipient")]
    [InlineData("bargain")] [InlineData("advance")] [InlineData("due")]
    [InlineData("duration")] [InlineData("installments")] [InlineData("condition")]
    public void DeclarationCannotBeForgedOrAttachedToABargain(string variant)
    {
        var a = Declaration(); var c = a.Clauses[0];
        switch (variant)
        {
            case "npc-proposer": a.ProposerId = "npc"; break;
            case "npc-actor": c.ActorId = "npc"; c.RecipientId = "player"; break;
            case "wrong-recipient": c.RecipientId = "other"; break;
            case "bargain": a.Clauses.Add(Offer().Clauses[0]); break;
            case "advance": a.AdvancePayment = true; break;
            case "due": c.DueDay = 3; break;
            case "duration": c.DurationDays = 3; break;
            case "installments": c.Installments = 2; c.IntervalDays = 1; break;
            case "condition": c.ConditionKind = "peace"; c.ConditionTargetId = "realm"; break;
        }
        Assert.NotEmpty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
    }

    [Theory] [InlineData("propose")] [InlineData("counter")]
    public void HiddenNpcDecisionCannotDeclareRebellionForThePlayer(string kind)
    {
        var a = kind == "counter" ? Offer() : null;
        var d = a == null ? new ActionDecision() : Accept(a);
        d.Kind = kind;
        d.Clauses.Add(new() { ActionId = "start_ruling_clan_rebellion", ActorId = "player", RecipientId = "npc",
            Arguments = new() { new() { Key = "actorKingdomId", Value = "realm" }, new() { Key = "actorClanId", Value = "player-clan" } } });
        Assert.Contains(Check(a, d), e => e.Contains("player's own standalone"));
    }

    [Fact] public void AdvancePledgeRequiresTheLordsExactConsentAndCannotBecomeAReport()
    {
        var a = Offer();
        a.Clauses[0] = new() { ActionId = "pledge_rebellion_support", ActorId = "npc", RecipientId = "player",
            Arguments = new() { ["actorKingdomId"] = "realm", ["terms.challengedRulerId"] = "ruler" } };
        a.PlayerAcceptedHash = a.TermsHash();
        Assert.True(ReignActionCatalog.Find("pledge_rebellion_support")!.ConversationEligible);
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        Assert.False(a.HasRequiredConsents);
        Assert.True(AgreementRules.Accept(a, "npc", a.TermsHash()));
        Assert.True(a.HasBothConsents);
        a.Clauses[0].Arguments["terms.challengedRulerId"] = "new-ruler";
        Assert.False(a.HasRequiredConsents);
        a.Clauses[0].Arguments["terms.decision"] = "report";
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("Unknown parameter"));
        a.Clauses[0].Arguments.Remove("terms.decision");
        a.Clauses[0].ActorId = "player"; a.Clauses[0].RecipientId = "npc";
        Assert.Contains(AgreementRules.Validate(a, ReignActionCatalog.Find, 0), e => e.Contains("approached lord"));
    }
}
