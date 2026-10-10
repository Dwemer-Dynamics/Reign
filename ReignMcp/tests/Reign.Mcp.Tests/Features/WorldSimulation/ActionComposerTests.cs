using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ActionComposerTests
{
    private static ActionAgreement Gift(int quantity = 100) => new()
    {
        PlayerId = "player", NpcId = "npc", ProposerId = "player",
        Clauses = new List<ActionClause> { new() { ActionId = "give.gold", ActorId = "player", RecipientId = "npc",
            DisplayText = $"I wish to give you {quantity} denars", Arguments = new() { ["quantity"] = quantity.ToString() } } }
    };

    [Fact]
    public void EveryCharacterDeletionRemovesOnlyTheAttachedSentence()
    {
        string sentence = Gift().Sentence;
        for (int offset = 0; offset < sentence.Length; offset++)
        {
            var editor = new ActionComposer();
            editor.SetText(sentence + " This is my case. Thank you.");
            int insertion = (sentence + " This is my case.").Length;
            editor.Select(insertion, insertion, insertion);
            editor.Insert(Gift());
            editor.Replace(editor.ActionStart + offset, 1, "");
            Assert.Null(editor.Draft);
            Assert.Equal(sentence + " This is my case.  Thank you.", editor.Text);
        }
    }

    [Fact]
    public void EditsOnEitherSidePreserveExactAttachmentAndTerms()
    {
        var editor = new ActionComposer(); editor.SetText("A B"); editor.Select(1, 1, 1); editor.Insert(Gift());
        string hash = editor.Draft!.TermsHash();
        editor.Replace(0, 1, "My argument");
        editor.Replace(editor.Text.Length, 0, " My explanation");
        Assert.True(editor.HasValidAttachment);
        Assert.Equal(hash, editor.Draft!.TermsHash());
        Assert.Equal("My argument I wish to give you 100 denars. B My explanation", editor.Text);
    }

    [Theory]
    [InlineData(-2, 5, "replacement ", "beforreplacement  after")]
    [InlineData(5, 0, "replacement", "before replacement after")]
    public void ReplacementTouchingActionCancelsWholeAction(int offset, int length, string text, string expected)
    {
        var editor = new ActionComposer(); editor.SetText("before after"); editor.Select(6, 6, 6); editor.Insert(Gift());
        editor.Replace(editor.ActionStart + offset, length, text);
        Assert.Null(editor.Draft); Assert.Equal(expected, editor.Text);
    }

    [Fact]
    public void ReplacingWholeComposerCannotRetainHiddenExecutableTerms()
    {
        var editor = new ActionComposer(); editor.Insert(Gift());
        editor.Replace(0, editor.Text.Length, "I wish to give you 1,000,000 denars.");
        Assert.Null(editor.Draft); Assert.False(editor.HasValidAttachment);
    }

    [Fact]
    public void RevisionReplacesAttachedSentenceAndPreservesContext()
    {
        var editor = new ActionComposer(); editor.SetText("hello there"); editor.Select(5, 5, 5); editor.Insert(Gift());
        editor.Insert(Gift(200));
        Assert.Equal("hello I wish to give you 200 denars. there", editor.Text);
        Assert.True(editor.HasValidAttachment);
    }

    [Fact]
    public void SelectionInsertionAndUnicodeContextRetainExactOffsets()
    {
        var editor = new ActionComposer(); editor.SetText("Éléonore 🙂 replace this 後");
        int start = editor.Text.IndexOf("replace", StringComparison.Ordinal);
        editor.Select(start + 12, start, start + 12); editor.Insert(Gift());
        Assert.Equal("Éléonore 🙂 I wish to give you 100 denars. 後", editor.Text);
        Assert.True(editor.HasValidAttachment);
        editor.Replace(editor.ActionStart, editor.ActionLength, "");
        Assert.Equal("Éléonore 🙂  後", editor.Text); Assert.Null(editor.Draft);
    }

    [Fact]
    public void AlteringTermsWithoutUpdatingDisplayedSentenceInvalidatesAttachment()
    {
        var editor = new ActionComposer(); var gift = Gift(); editor.Insert(gift);
        gift.Clauses[0].Arguments["quantity"] = "500";
        Assert.False(editor.HasValidAttachment);
    }
}
