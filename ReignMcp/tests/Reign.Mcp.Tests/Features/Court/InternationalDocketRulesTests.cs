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
