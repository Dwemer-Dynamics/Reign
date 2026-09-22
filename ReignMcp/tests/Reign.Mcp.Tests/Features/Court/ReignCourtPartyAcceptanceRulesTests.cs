using Reign.Core.Contracts.Court;
using Xunit;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class ReignCourtPartyAcceptanceRulesTests
{
    [Fact]
    public void LaterEnvoyAcceptanceRetainsDomesticSettlementMitigation()
    {
        var first = ReignCourtPartyAcceptanceRules.Merge(null, false, "domestic", "domestic", 2);
        var both = ReignCourtPartyAcceptanceRules.Merge(first, true, "envoy", "envoy", 2);
        Assert.Equal(2, both.Count);
        Assert.Equal(2, ReignCourtPartyAcceptanceRules.LosingPartyTier(true, "domestic", ["envoy"], both));
        Assert.Equal(2, both["envoy"]);
        Assert.Equal("envoy", ReignCourtPartyAcceptanceRules.AuthorizationActor("domestic", "envoy", true, ["envoy"]));
    }

    [Fact]
    public void ReverseSpeakerOrderRetainsForeignSettlementMitigation()
    {
        var first = ReignCourtPartyAcceptanceRules.Merge(null, false, "envoy", "envoy", 1);
        var both = ReignCourtPartyAcceptanceRules.Merge(first, true, "domestic", "domestic", 2);
        Assert.Equal(1, ReignCourtPartyAcceptanceRules.LosingPartyTier(false, "domestic", ["envoy"], both));
        Assert.Equal("envoy", ReignCourtPartyAcceptanceRules.AuthorizationActor("envoy", "domestic", true, ["envoy"]));
    }

    [Fact]
    public void ChangedOptionOrTermsDiscardPreviousParties()
    {
        var prior = ReignCourtPartyAcceptanceRules.Merge(null, false, "domestic", "domestic", 3);
        var changed = ReignCourtPartyAcceptanceRules.Merge(prior, false, "envoy", "envoy", 2);
        Assert.False(changed.ContainsKey("domestic"));
        Assert.Equal(0, ReignCourtPartyAcceptanceRules.LosingPartyTier(true, "domestic", ["envoy"], changed));
        Assert.Equal("domestic", ReignCourtPartyAcceptanceRules.AuthorizationActor("envoy", "domestic", false, ["envoy"]));
    }

    [Fact]
    public void FreshAcceptanceFromSameSpeakerReplacesItsPriorTier()
    {
        var prior = ReignCourtPartyAcceptanceRules.Merge(null, false, "domestic", "domestic", 3);
        var next = ReignCourtPartyAcceptanceRules.Merge(prior, true, "domestic", "domestic", 1);
        Assert.Single(next);
        Assert.Equal(1, next["domestic"]);
        Assert.Equal(3, prior["domestic"]);
    }

    [Fact]
    public void SpeakerCannotSupplyAnotherPersonsAcceptance()
    {
        var result = ReignCourtPartyAcceptanceRules.Merge(null, false, "envoy", "domestic", 3);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("compensate", "foreign", true)]
    [InlineData("compromise", "foreign", true)]
    [InlineData("compensate", "domestic", false)]
    [InlineData("compromise", "domestic", false)]
    [InlineData("favor_foreign", "", true)]
    [InlineData("favor_domestic", "", false)]
    public void ActualRecipientAndRulingSelectTheAffectedSide(string option, string recipient, bool foreignBenefits)
    {
        Assert.Equal(foreignBenefits, ReignCourtPartyAcceptanceRules.ForeignSideBenefits(option, recipient, "domestic"));
    }

    [Fact]
    public void MissingLosingPartyAssentDoesNotBorrowTheWinnersTier()
    {
        var winnerOnly = ReignCourtPartyAcceptanceRules.Merge(null, false, "envoy", "envoy", 3);
        Assert.Equal(0, ReignCourtPartyAcceptanceRules.LosingPartyTier(true, "domestic", ["envoy"], winnerOnly));
        Assert.Equal(0, ReignCourtPartyAcceptanceRules.LosingPartyTier(false, "domestic", ["different_envoy"], winnerOnly));
    }
}
