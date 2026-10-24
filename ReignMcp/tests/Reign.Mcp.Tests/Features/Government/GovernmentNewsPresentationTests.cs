using Reign.Core.Contracts.Government;
using Xunit;

namespace Reign.Mcp.Tests.Features.Government;

public sealed class GovernmentNewsPresentationTests
{
    [Theory]
    [InlineData("fief_allocation", "which eligible clan receives this fief")]
    [InlineData("fief_reassignment", "whether to reopen the allocation of this fief")]
    public void Allocation_proceedings_do_not_assign_the_reader_a_royal_role(string kind, string question)
    {
        const string legacy = "As king, you must decide the ownership of this fief.";
        string result = GovernmentBusinessRules.PublicMatterSummary(kind, legacy);
        Assert.Contains(question, result);
        Assert.DoesNotContain("king", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("you", result, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(result, GovernmentBusinessRules.PublicMatterSummary(kind, result));
    }

    [Theory]
    [InlineData("policy", "Settlement loyalty is increased by 2 per day. Settlement production is reduced by 5%.")]
    [InlineData("peace", "Pay 1,200 denars per day for 30 days.")]
    [InlineData("seasonal", "Pay 25,000 denars immediately; deliver 40 grain within 14 days.")]
    [InlineData("expulsion", "Decide whether to expel the named clan.")]
    [InlineData("", "No matter selected.")]
    public void Other_motion_terms_are_preserved_exactly(string kind, string terms)
        => Assert.Equal(terms, GovernmentBusinessRules.PublicMatterSummary(kind, terms));

    [Fact]
    public void Allocation_projection_preserves_named_candidate_requirements()
    {
        string result = GovernmentBusinessRules.CompleteProposalText(
            GovernmentBusinessRules.PublicMatterSummary("fief_allocation", "As king, choose."),
            new[] { new KeyValuePair<string, string>("Clan A", "Receive Ormanfard Castle.\nThe recorded eligible candidate retains this exact claim.") });
        Assert.Contains("Clan A: Receive Ormanfard Castle.", result);
        Assert.Contains("The recorded eligible candidate retains this exact claim.", result);
    }
}
