using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class SpouseSeparationTests
{
    private static ActionAgreement Draft()
    {
        var a = new ActionAgreement { PlayerId = "player", NpcId = "npc", ProposerId = "player" };
        a.Clauses.Add(new ActionClause { ActionId = "leave_spouse", ActorId = "npc", RecipientId = "player" });
        ReignSpouseSeparation.Bind(a, a.Clauses[0], "spouse", "clan");
        return a;
    }

    [Fact] public void AvailableAsAnNpcRequestWithoutPlayerMovementOrUnilateralAuthority()
    {
        var definition = ReignActionCatalog.Find("leave_spouse")!;
        Assert.True(definition.ConversationEligible);
        Assert.False(definition.MovesActor);
        Assert.True(ReignActionConversationPolicy.Allows(definition, false));
        Assert.False(ReignActionConversationPolicy.Allows(definition, true));
        Assert.All(definition.Parameters, p => Assert.True(p.Automatic));
        Assert.Empty(AgreementRules.Validate(Draft(), ReignActionCatalog.Find, 0));
        Assert.False(AgreementRules.IsPlayerDeclaration(Draft()));
    }

    [Fact] public void MarriageBindingSurvivesNewSpouseAndInvalidatesChangedConsent()
    {
        var a = Draft(); var c = a.Clauses[0];
        string hash = a.TermsHash();
        a.State = AgreementState.Offered;
        Assert.True(AgreementRules.Accept(a, "player", hash));
        Assert.False(a.HasRequiredConsents);
        ReignSpouseSeparation.Bind(a, c, "new_spouse", "new_clan");
        Assert.Equal("spouse", c.Arguments["spouseId"]);
        Assert.Equal("clan", c.Arguments["spouseClanId"]);
        Assert.True(AgreementRules.Accept(a, "npc", hash));
        Assert.True(a.HasBothConsents);
        c.Arguments["spouseId"] = "new_spouse";
        Assert.False(a.HasBothConsents);
    }

    [Fact] public void IncompleteOrSignedUnboundRequestCannotAcquireANewMarriage()
    {
        var a = Draft(); var c = a.Clauses[0];
        c.Arguments.Remove("spouseClanId");
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
        Assert.Throws<ArgumentException>(() => ReignSpouseSeparation.Bind(a, c, "spouse", "clan"));
        c.Arguments.Clear(); a.NpcAcceptedHash = a.TermsHash();
        Assert.Throws<ArgumentException>(() => ReignSpouseSeparation.Bind(a, c, "spouse", "clan"));
    }

    [Theory]
    [InlineData(-100, -100)] [InlineData(-70, -100)] [InlineData(0, -50)] [InlineData(100, 50)]
    public void SeverePenaltyRespectsNativeBounds(int before, int expected)
        => Assert.Equal(expected, ReignSpouseSeparation.RelationAfter(before));

    [Fact] public void ClanSpilloverExcludesActorAndCountsSpouseOnce()
        => Assert.Equal(new[] { "other", "spouse" }, ReignSpouseSeparation.AffectedHeroes("npc", "spouse",
            new[] { "npc", "spouse", "other", "other", "" }));

    [Theory] [InlineData("player")] [InlineData("third_party")]
    public void CannotEndSomeoneElsesMarriage(string actor)
    {
        var a = Draft(); a.Clauses[0].ActorId = actor;
        Assert.NotEmpty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
    }

    [Fact] public void CannotRepeatDeferOrBundleANewMarriage()
    {
        var a = Draft(); var c = a.Clauses[0];
        c.DueDay = 2;
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
        c.DueDay = 0; c.ConditionKind = "day";
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
        c.ConditionKind = ""; c.Installments = 2;
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
        c.Installments = 1; a.Clauses.Add(c);
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
        a.Clauses.RemoveAt(1); a.Clauses.Add(new ActionClause { ActionId = "marriage_alliance" });
        Assert.NotEmpty(ReignSpouseSeparation.Validate(a, c));
    }

    [Fact] public void ExistingExchangeCanIncludeASeparatePaymentWithoutImplyingANewRelationship()
    {
        var a = Draft(); a.Clauses.Add(new ActionClause { ActionId = "give.gold", ActorId = "player", RecipientId = "npc",
            Arguments = new() { ["quantity"] = "100" } });
        Assert.Empty(AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
        string letter = ReignSpouseSeparation.Letter("A spouse");
        Assert.Contains("marriage is over", letter);
        Assert.DoesNotContain("player", letter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("major and consequential", ReignSpouseSeparation.DecisionGuidance);
        Assert.Contains("may refuse", ReignSpouseSeparation.DecisionGuidance);
    }
}
