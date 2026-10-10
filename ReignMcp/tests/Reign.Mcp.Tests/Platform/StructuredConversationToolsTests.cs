using System.Text.Json;
using Reign.Mcp.Server;
using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests;

public sealed class StructuredConversationToolsTests
{
    [Fact] public void CompletedReplayUsesAuthoritativeSavedJournalAndNormalComposerAndSend()
    {
        var source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionTestHost.cs"));
        var start = source.IndexOf("var replayCandidate", StringComparison.Ordinal);
        var end = source.IndexOf("string composed", start, StringComparison.Ordinal);
        var preflight = source.Substring(start, end - start);
        Assert.Contains("ReignActionService.Copy(draft)", preflight);
        Assert.Contains("ReignAgreementBehavior.Instance?.Agreements", preflight);
        Assert.Contains("j.Agreement.Id == draft.Id", preflight);
        Assert.Contains("ReignCampaignIdentity.CurrentCampaignId()", preflight);
        Assert.Contains("ReignWorldHistoryCampaignBehavior.Instance?.TimelineId", preflight);
        Assert.Contains("!reviewNpcOffer", preflight);
        Assert.Contains("AgreementRules.IsExactCompletedReplay(replayCandidate, savedJournal)", preflight);
        Assert.Contains("ReignActionService.Preview(draft)", preflight);
        Assert.DoesNotContain("PrepareOffer(", preflight);
        Assert.DoesNotContain("Commit(", preflight);
        Assert.Contains("PrepareAutomationActionLine(draft, text)", source);
        Assert.Contains("SendAutomationLineAsync", source);
    }

    private static object Case(string text = "Here is my offer. {action} I hope this helps.") => new
    {
        caseId = "gold", actorHeroId = "npc", playerText = text, cooperationOverride = true,
        agreement = new { Id = "proposal", PlayerId = "main_hero", NpcId = "npc", ProposerId = "main_hero",
            Clauses = new[] { new { Id = "gold", ActionId = "give.gold", ActorId = "main_hero",
                RecipientId = "npc", Arguments = new Dictionary<string, string> { ["quantity"] = "137" } } } }
    };

    [Fact] public async Task GateAndConfirmationPrecedeApiAndCampaignAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredConversationTools.TestActionConversations(
            null!, TestOptions.Create(), null!, "campaign", "run", "batch", "[]",
            "test action conversations on disposable save"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredConversationTools.TestActionConversations(
            null!, TestOptions.Create() with { AllowVerificationControl = true }, null!,
            "campaign", "run", "batch", "[]"));
    }
    [Fact] public void PreservesNaturalContextAndSeparateTypedTerms()
    {
        var row = Assert.Single(StructuredConversationTools.ParseCases(JsonSerializer.Serialize(new[] { Case() })));
        Assert.Equal("Here is my offer. {action} I hope this helps.", row.GetProperty("playerText").GetString());
        Assert.Equal("137", row.GetProperty("agreement").GetProperty("Clauses")[0].GetProperty("Arguments").GetProperty("quantity").GetString());
    }
    [Fact] public void EverySendCarriesTheSameExactEnrollmentAsItsRun()
    {
        var auth = new CampaignTestAuthorization("owned_run", "campaign", "timeline", "BaseThree",
            "ReignTest_Owned", "ReignTest_Owned_Current");
        var cases = StructuredConversationTools.ParseCases(JsonSerializer.Serialize(new[] { Case() }));
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(
            StructuredConversationTools.BuildRequest("live_run", "batch", cases, auth)));
        var root = payload.RootElement;
        var send = root.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("operation").GetString() == "send");
        var binding = send.GetProperty("enrollment");
        Assert.Equal(root.GetProperty("enrollment").GetRawText(), binding.GetRawText());
        Assert.Equal("owned_run", binding.GetProperty("campaignTestRunId").GetString());
        Assert.Equal("campaign", binding.GetProperty("campaignId").GetString());
        Assert.Equal("timeline", binding.GetProperty("timelineId").GetString());
        Assert.Equal("BaseThree", binding.GetProperty("protectedBaselineSaveName").GetString());
        Assert.Equal("reign_mcp_action_probe", binding.GetProperty("source").GetString());
        Assert.Equal(send.GetProperty("expectedSaveName").GetString(), binding.GetProperty("disposableSaveName").GetString());
        Assert.True(send.GetProperty("naturalLanguageCase").GetBoolean());
        Assert.Equal("npc", send.GetProperty("structuredActionActorHeroId").GetString());
    }
    [Theory]
    [InlineData("Here is my offer.")]
    [InlineData("{action} and {action}")]
    public void RefusesAmbiguousInsertion(string text) => Assert.Throws<ArgumentException>(() =>
        StructuredConversationTools.ParseCases(JsonSerializer.Serialize(new[] { Case(text) })));

    [Fact] public void RejectsNestedDuplicatesOperatorPrimitivesAndOversizedBatches()
    {
        string valid = JsonSerializer.Serialize(new[] { Case() });
        Assert.Throws<ArgumentException>(() => StructuredConversationTools.ParseCases(valid.Replace("\"quantity\":\"137\"", "\"quantity\":\"137\",\"quantity\":\"999\"")));
        Assert.Throws<ArgumentException>(() => StructuredConversationTools.ParseCases(valid.Replace("give.gold", "native.GiveGold.ApplyBetweenCharacters")));
        Assert.Throws<ArgumentException>(() => StructuredConversationTools.ParseCases(JsonSerializer.Serialize(Enumerable.Repeat(Case(), 9))));
        Assert.Throws<ArgumentException>(() => StructuredConversationTools.ParseCases("[]"));
    }
    [Fact] public void ProtectedSentenceKeepsContextAndCancelsAsOneUnit()
    {
        var a = new ActionAgreement { PlayerId = "main_hero", NpcId = "npc", ProposerId = "main_hero" };
        a.Clauses.Add(new ActionClause { ActionId = "give.gold", ActorId = "main_hero", RecipientId = "npc",
            DisplayText = "I wish to give you 137 denars" });
        var composer = new ActionComposer();
        composer.SetText("Please accept this help. ");
        composer.Insert(a);
        composer.Replace(composer.Text.Length, 0, " Your family needs it.");
        Assert.True(composer.HasValidAttachment);
        Assert.Equal("Please accept this help. " + a.Sentence + " Your family needs it.", composer.Text);
        composer.Replace(composer.ActionStart + 5, 1, "");
        Assert.False(composer.HasValidAttachment);
        Assert.Equal("Please accept this help.  Your family needs it.", composer.Text);
    }
    private static object NpcReview() => new
    {
        caseId = "review", actorHeroId = "npc", playerText = "I accept these exact terms. {action}", reviewNpcOffer = true,
        agreement = new { Id = "saved_offer", Revision = 2, PlayerId = "main_hero", NpcId = "npc", ProposerId = "npc",
            Clauses = new[] { new { Id = "grain", ActionId = "give.item", ActorId = "npc", RecipientId = "main_hero",
                Arguments = new Dictionary<string, string> { ["assetId"] = "grain", ["quantity"] = "1" } } } }
    };
    [Fact] public void NpcReviewPreservesReviewedTermsAndEnrollmentWithoutSynthesizingSignatures()
    {
        var cases = StructuredConversationTools.ParseCases(JsonSerializer.Serialize(new[] { NpcReview() }));
        var auth = new CampaignTestAuthorization("owned_run", "campaign", "timeline", "BaseThree",
            "ReignTest_Owned", "ReignTest_Owned_Current");
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(StructuredConversationTools.BuildRequest("live", "review_batch", cases, auth)));
        var root = payload.RootElement;
        var send = root.GetProperty("steps").EnumerateArray().Single(x => x.GetProperty("operation").GetString() == "send");
        Assert.True(send.GetProperty("reviewNpcOffer").GetBoolean());
        Assert.Equal(root.GetProperty("enrollment").GetRawText(), send.GetProperty("enrollment").GetRawText());
        Assert.Equal("npc", send.GetProperty("structuredActionDraft").GetProperty("ProposerId").GetString());
        Assert.Equal(2, send.GetProperty("structuredActionDraft").GetProperty("Revision").GetInt32());
        Assert.Equal(cases[0].GetProperty("agreement").GetRawText(), send.GetProperty("structuredActionDraft").GetRawText());
        Assert.False(send.GetProperty("structuredActionDraft").TryGetProperty("NpcAcceptedHash", out _));
        Assert.False(send.GetProperty("structuredActionDraft").TryGetProperty("PlayerAcceptedHash", out _));
    }
    [Fact] public void NpcReviewRejectsMalformedFlagsWrongAuthorAndMissingReviewedRevision()
    {
        string valid = JsonSerializer.Serialize(new[] { NpcReview() });
        foreach (string invalid in new[] { valid.Replace("\"reviewNpcOffer\":true", "\"reviewNpcOffer\":\"true\""),
            valid.Replace("\"ProposerId\":\"npc\"", "\"ProposerId\":\"main_hero\""),
            valid.Replace("\"Revision\":2", "\"Revision\":0"), valid.Replace("\"Revision\":2", "\"Revision\":\"2\""),
            valid.Replace("\"Revision\":2", "\"Revision\":2.5"), valid.Replace("\"Revision\":2,", "") })
            Assert.Throws<ArgumentException>(() => StructuredConversationTools.ParseCases(invalid));
    }
    [Fact] public void CatalogRecordsProviderGateAndIndependentProof()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var row = doc.RootElement.GetProperty("structuredActionConversationProbe");
        Assert.Equal("reign_test_action_conversations", row.GetProperty("entry").GetString());
        Assert.Contains("provider authorization", row.GetProperty("safety").GetString());
        Assert.Contains("independent native effect assertion", row.GetProperty("evidence").GetString());
    }
}
