using System.Text.Json;
using Reign.Core.Contracts.Court;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class InternationalDocketRulesTests
{
    [Fact]
    public void EverySeverityHasThePromisedBreadthAndOnlySupportedRemedies()
    {
        Assert.Equal(75, ReignInternationalDocketCatalog.Templates.Count);
        Assert.Equal(75, ReignInternationalDocketCatalog.Templates.Select(x => x.Id).Distinct().Count());
        Assert.Equal(75, ReignInternationalDocketCatalog.Templates.Select(x => x.Premise).Distinct().Count());
        foreach (ReignNobleMatterSeverity severity in Enum.GetValues<ReignNobleMatterSeverity>())
            Assert.Equal(severity == ReignNobleMatterSeverity.Petty ? 30 : 15, ReignInternationalDocketCatalog.ForSeverity(severity).Count);
        string[] supported = { "compensation", "apology", "gift", "goodwill", "sign_trade_agreement", "prisoner" };
        Assert.All(ReignInternationalDocketCatalog.Templates, x =>
        {
            Assert.Contains(x.Remedy, supported);
            Assert.NotEmpty(x.ResolutionIds);
            Assert.True(x.Gold > 0);
            if (x.RequiresCaptive) Assert.Equal("prisoner", x.Remedy);
            if (x.Extortion) { Assert.False(x.Constructive); Assert.Contains("refuse", x.ResolutionIds); }
        });
    }

    [Fact]
    public void SeverityDrawIsFiftyThirtyFifteenFiveAndRejectsOutOfRange()
    {
        int[] actual = Enum.GetValues<ReignNobleMatterSeverity>().Select(s => Enumerable.Range(0, 100)
            .Count(roll => ReignInternationalDocketRules.SeverityForRoll(roll) == s)).ToArray();
        Assert.Equal(new[] { 50, 30, 15, 5 }, actual);
        Assert.Throws<ArgumentOutOfRangeException>(() => ReignInternationalDocketRules.SeverityForRoll(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReignInternationalDocketRules.SeverityForRoll(100));
    }

    [Theory]
    [InlineData(49, 0)] [InlineData(50, 1)] [InlineData(99, 1)] [InlineData(100, 2)]
    [InlineData(149, 2)] [InlineData(150, 3)] [InlineData(199, 3)] [InlineData(200, 4)] [InlineData(300, 4)]
    public void AmbassadorBandsHaveExactBoundaries(int charm, int band)
        => Assert.Equal(band, ReignInternationalDocketRules.AmbassadorCharmBand(charm));

    [Fact]
    public void ExtortionNeedsPowerAndBothPersonalityConditions()
    {
        Assert.True(ReignInternationalDocketRules.ExtortionEligible(150, 100, 61, 40));
        Assert.False(ReignInternationalDocketRules.ExtortionEligible(149, 100, 61, 40));
        Assert.False(ReignInternationalDocketRules.ExtortionEligible(150, 100, 60, 40));
        Assert.False(ReignInternationalDocketRules.ExtortionEligible(150, 100, 61, 41));
        Assert.True(ReignInternationalDocketRules.ExtortionEligible(.6, .4, 61, 40));
        Assert.False(ReignInternationalDocketRules.ExtortionEligible(.59, .4, 61, 40));
        Assert.False(ReignInternationalDocketRules.ExtortionEligible(150, 0, 61, 40));
    }

    [Fact]
    public void CounteroffersRejectNegativeOverflowAndEmptyTerms()
    {
        Assert.True(ReignInternationalDocketRules.CounterTermsWithinBounds(0, 1));
        Assert.True(ReignInternationalDocketRules.CounterTermsWithinBounds(1000000, 365));
        Assert.True(ReignInternationalDocketRules.CounterTermsWithinBounds(null, 120));
        Assert.False(ReignInternationalDocketRules.CounterTermsWithinBounds(null, null));
        Assert.False(ReignInternationalDocketRules.CounterTermsWithinBounds(-1, null));
        Assert.False(ReignInternationalDocketRules.CounterTermsWithinBounds(long.MaxValue, 20));
        Assert.False(ReignInternationalDocketRules.CounterTermsWithinBounds(50, 0));
        Assert.False(ReignInternationalDocketRules.CounterTermsWithinBounds(50, 366));
    }

    [Theory]
    [InlineData("accept", "prisoner", false, false)]
    [InlineData("accept", "prisoner", true, true)]
    [InlineData("accept", "gift", false, true)]
    [InlineData("accept", "sign_trade_agreement", false, true)]
    [InlineData("accept", "goodwill", false, true)]
    [InlineData("compromise", "prisoner", false, true)]
    [InlineData("refuse", "prisoner", false, false)]
    [InlineData("favor_foreign", "compensation", false, false)]
    public void OnlyUnconditionalPrisonerReleaseIsAUnilateralAcceptDecision(
        string optionId, string remedy, bool ransom, bool expected)
        => Assert.Equal(expected, ReignInternationalDocketRules.RequiresForeignAcceptance(optionId, remedy, ransom));

    [Fact]
    public void UnusableSovereignResponseInThePersistedReferralIsTechnicallyUnavailable()
    {
        using var receipt = JsonDocument.Parse("""
            {
                "referral_id": "court_referral_1",
                "status": "model_output_unusable",
                "outcome": "format_failure",
                "authoritative_receipt_json": "{\"technicalFailure\":true}"
            }
            """);
        var persisted = receipt.RootElement;
        Assert.Equal(ReignInternationalReplyDisposition.Unavailable,
            ReignInternationalDocketRules.ReadSovereignReplyDisposition(
                persisted.GetProperty("status").GetString()!, persisted.GetProperty("outcome").GetString()!, false));
    }

    [Theory]
    [InlineData(null, null, "ruler", false)]
    [InlineData(null, "", "ruler", false)]
    [InlineData(null, " ", "ruler", false)]
    [InlineData(null, "ruler", "ruler", true)]
    [InlineData(null, "predecessor", "ruler", false)]
    [InlineData("ruler", null, "ruler", false)]
    [InlineData("ruler", "ruler", "ruler", true)]
    [InlineData("predecessor", "ruler", "ruler", false)]
    public void SovereignAssentRequiresTheHistoricalResponder(string? frozen, string? responder, string expected, bool valid)
        => Assert.Equal(valid, ReignInternationalDocketRules.HasHistoricalSovereignResponder(frozen, responder, expected));

    [Theory]
    [InlineData("accepted_awaiting_court", "accept", true, ReignInternationalReplyDisposition.Accepted)]
    [InlineData("accepted_awaiting_court", "accept", false, ReignInternationalReplyDisposition.Unavailable)]
    [InlineData("accepted_awaiting_court", "counter_accept", true, ReignInternationalReplyDisposition.Unavailable)]
    [InlineData("countered", "counter", true, ReignInternationalReplyDisposition.Countered)]
    [InlineData("countered", "counter", false, ReignInternationalReplyDisposition.Unavailable)]
    [InlineData("refused", "refuse", false, ReignInternationalReplyDisposition.Refused)]
    [InlineData("refused", "validation_failed", false, ReignInternationalReplyDisposition.Unavailable)]
    [InlineData("cancelled", "posting_ended", false, ReignInternationalReplyDisposition.Unavailable)]
    public void PersistedSovereignReplyRequiresTheExactOutcomeAndAction(
        string status, string outcome, bool hasNormalizedAction, ReignInternationalReplyDisposition expected)
        => Assert.Equal(expected, ReignInternationalDocketRules.ReadSovereignReplyDisposition(status, outcome, hasNormalizedAction));

    [Theory]
    [InlineData("sign_trade_agreement", "sign_trade_agreement", "sign_trade_agreement", "DiplomacySignTradeAgreement", true)]
    [InlineData("sign_trade_agreement", "sign_trade_agreement", "sign_trade_agreement", "DiplomacySignDefensivePact", false)]
    [InlineData("sign_trade_agreement", "sign_defensive_pact", "sign_defensive_pact", "DiplomacySignDefensivePact", false)]
    [InlineData("record_promise", "record_promise", "record_promise", "DiplomacyRecordPromise", true)]
    [InlineData("record_promise", "ransom_package", "ransom_package", "DiplomacyRansomPackage", false)]
    public void SovereignCounterKeepsTheFrozenCommandAndFixedNativeActionType(
        string frozen, string authored, string normalized, string nativeType, bool valid)
        => Assert.Equal(valid, ReignInternationalDocketRules.MatchesSovereignReplyAction(frozen, authored, normalized, nativeType));

    [Theory]
    [InlineData("1", true)]
    [InlineData("365", true)]
    [InlineData("366", false)]
    [InlineData("0", false)]
    [InlineData("30.0", false)]
    [InlineData("\"30\"", false)]
    public void TradeDurationRequiresOneMaterialIntegerInRange(string duration, bool valid)
    {
        var terms = new Dictionary<string, string> { ["durationDays"] = duration };
        Assert.Equal(valid, ReignInternationalDocketRules.BindsCourtTradeDuration(terms));
        terms["gold"] = "200";
        Assert.False(ReignInternationalDocketRules.BindsCourtTradeDuration(terms));
        Assert.False(ReignInternationalDocketRules.BindsCourtTradeDuration(new Dictionary<string, string>()));
    }

    [Fact]
    public void ExactTermsPermitOnlyResolverAliasesMatchingTheBoundIdentities()
    {
        var authored = new Dictionary<string, string> { ["durationDays"] = "90" };
        var aliases = new Dictionary<string, string> { ["fromHeroStringId"] = "\"ruler1\"",
            ["targetHeroStringId"] = "\"ruler2\"", ["toHeroStringId"] = "\"ruler2\"" };
        var normalized = new Dictionary<string, string>(aliases) { ["durationDays"] = "90" };

        Assert.True(ReignInternationalDocketRules.MatchesCourtNormalizedTerms(authored, normalized, aliases));
        normalized["durationDays"] = "91";
        Assert.False(ReignInternationalDocketRules.MatchesCourtNormalizedTerms(authored, normalized, aliases));
        normalized["durationDays"] = "90";
        normalized["fromHeroStringId"] = "\"otherRuler\"";
        Assert.False(ReignInternationalDocketRules.MatchesCourtNormalizedTerms(authored, normalized, aliases));
        authored["fromHeroStringId"] = "\"ruler1\"";
        normalized.Remove("fromHeroStringId");
        Assert.False(ReignInternationalDocketRules.MatchesCourtNormalizedTerms(authored, normalized, aliases));
    }

    [Fact]
    public void CaptiveCounterKeepsTheNamedCaptiveAndRansomMode()
    {
        var original = new Dictionary<string, string> { ["captiveId"] = "\"capt1\"", ["ransom"] = "true",
            ["gold"] = "500", ["payerHeroId"] = "\"lord1\"", ["recipientHeroId"] = "\"lord2\"" };
        var revised = new Dictionary<string, string>(original) { ["gold"] = "600" };
        Assert.True(ReignInternationalDocketRules.BindsCourtSettlementTerms(original, revised));
        revised["captiveId"] = "\"capt2\"";
        Assert.False(ReignInternationalDocketRules.BindsCourtSettlementTerms(original, revised));
        revised.Remove("captiveId");
        Assert.False(ReignInternationalDocketRules.BindsCourtSettlementTerms(original, revised));
        revised["captiveId"] = original["captiveId"];
        revised["ransom"] = "false";
        Assert.False(ReignInternationalDocketRules.BindsCourtSettlementTerms(original, revised));
        revised["ransom"] = original["ransom"];
        revised["newCaptiveId"] = "\"capt2\"";
        Assert.False(ReignInternationalDocketRules.BindsCourtSettlementTerms(original, revised));
    }

    [Fact]
    public void RulingForThePlayerActivelyReducesExistingForeignPressure()
    {
        Assert.Equal(24, 40 + ReignInternationalDocketRules.NpcPressureDelta(ReignNobleMatterSeverity.Grave, true, true));
        Assert.Equal(56, 40 + ReignInternationalDocketRules.NpcPressureDelta(ReignNobleMatterSeverity.Grave, false, true));
        Assert.Equal(-6, ReignInternationalDocketRules.NpcPressureDelta(ReignNobleMatterSeverity.Petty, true, true));
        Assert.Equal(-24, ReignInternationalDocketRules.NpcPressureDelta(ReignNobleMatterSeverity.Exceptional, false, false));
    }

    [Theory]
    [InlineData(5, 1)] [InlineData(9, 2)] [InlineData(15, 4)] [InlineData(23, 6)]
    [InlineData(-6, -2)] [InlineData(-15, -4)] [InlineData(-30, -8)] [InlineData(-50, -13)]
    public void RulerGetsQuarterEffectWithSymmetricMidpointRounding(int noble, int ruler)
        => Assert.Equal(ruler, ReignInternationalDocketRules.RulerRelation(noble));
}
