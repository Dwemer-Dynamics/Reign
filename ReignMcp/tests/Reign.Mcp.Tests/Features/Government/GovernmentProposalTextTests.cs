using Reign.Core.Contracts.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentProposalTextTests
{
    private const string Terms = "Recorded local target: Garontor Castle. The ruler pays 25,000 denars immediately on acceptance. Deadline: 14 days after acceptance.";

    [Fact]
    public void DistinctRemediesRetainTheirOwnTermsEvenWhenTheirBudgetsAreIdentical()
    {
        string text = GovernmentBusinessRules.CompleteProposalText("Choose a remedy.", new[]
        {
            Option("Pay survivors' benefits", Terms),
            Option("Fund a memorial and household relief assembly", Terms)
        });
        Assert.Contains("Pay survivors' benefits: " + Terms, text);
        Assert.Contains("Fund a memorial and household relief assembly: " + Terms, text);
        Assert.Equal(2, text.Split("25,000", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void APlainSummaryMentionOfTheCostDoesNotReplaceANamedRemedysTerms()
    {
        string text = GovernmentBusinessRules.CompleteProposalText(Terms, new[] { Option("Assembly", Terms) });
        Assert.EndsWith("\n\nAssembly: " + Terms, text);
    }

    [Fact]
    public void AnAlreadyPresentWholeLabeledClauseIsNotRepeated()
    {
        string original = "Choose a remedy.\n\nAssembly: " + Terms;
        Assert.Equal(original, GovernmentBusinessRules.CompleteProposalText(original,
            new[] { Option("Assembly", Terms), Option("Assembly", Terms) }));
    }

    [Fact]
    public void ALabelThatIsASuffixOfAnotherLabelStillIdentifiesADifferentRemedy()
    {
        string text = GovernmentBusinessRules.CompleteProposalText("Choose.", new[]
        {
            Option("Public Relief", Terms), Option("Relief", Terms)
        });
        Assert.Contains("\n\nPublic Relief: " + Terms, text);
        Assert.Contains("\n\nRelief: " + Terms, text);
    }

    [Fact]
    public void ShortInlineTermsStayInlineWhileMultilineTermsRemainReadable()
    {
        string text = GovernmentBusinessRules.CompleteProposalText("Choose.", new[]
        {
            Option("Decline", "No payment."), Option("Assembly", "First line.\nSecond line.")
        });
        Assert.DoesNotContain("Decline:", text);
        Assert.Contains("Assembly: First line.\nSecond line.", text);
    }

    [Fact]
    public void MissingOptionalContentDoesNotThrowOrInventTerms()
    {
        Assert.Equal("", GovernmentBusinessRules.CompleteProposalText(null!, null!));
        Assert.Equal("Summary", GovernmentBusinessRules.CompleteProposalText("Summary",
            new[] { Option("Assembly", ""), Option("Assembly", " ") }));
    }

    private static KeyValuePair<string, string> Option(string label, string terms) => new(label, terms);
}
