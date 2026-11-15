using Reign.Core.Contracts.World;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PublicNewsContractTests
{
    [Theory]
    [InlineData("war_declared", "bannerlord_native", "major_world", true)]
    [InlineData("peace_made", "bannerlord_native", "major_world", true)]
    [InlineData("war_declared", "unknown", "major_world", false)]
    [InlineData("peace_made", "bannerlord_native", "participant_only", false)]
    [InlineData("hero_killed", "bannerlord_native", "major_world", false)]
    public void OnlyVerifiedNativeWarAndPeaceUseImmediatePublicDiplomacy(string type, string source, string audience, bool expected)
        => Assert.Equal(expected, ReignPublicNews.IsNativePublicDiplomacy(type, source, audience));
    [Fact]
    public void PublicationQueueIsIndependentOfSeenAndIsIdempotentAcrossNativeSaveReload()
    {
        var article = new ReignNewsArticle { Id = "capture", Category = ReignNewsCategories.Captures,
            Title = "A captive", Body = "A public capture was announced.", WorldDay = 100,
            SourceEventType = "ruler_judgment", SourceCorrelationId = "judgment", SourcePhase = "sentenced",
            PublicVerdict = "CleanExecution", ExecutionDueDay = 111 };
        var state = new ReignNewsState();
        Assert.True(state.Publish(article, new ReignNewsOptions(_ => false), true));
        Assert.True(state.QueuePublicHistory(article, 103));
        Assert.Equal(103, article.PublicationDay);
        Assert.False(state.QueuePublicHistory(article, 104));
        string originalBody = state.HistoryOutbox[0].Body;
        article.Body = "Caller changed a retained UI object.";
        Assert.Equal(originalBody, state.HistoryOutbox[0].Body);
        article.ExecutionDueDay = 999;
        Assert.Equal(111d, state.HistoryOutbox[0].ExecutionDueDay);
        Assert.Equal(111d, ReignPublicNews.Snapshot(state.HistoryOutbox[0])["executionDueDay"]);
        var options = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        var restored = System.Text.Json.JsonSerializer.Deserialize<ReignNewsState>(System.Text.Json.JsonSerializer.Serialize(state, options), options)!;
        restored.NormalizeAfterLoad();
        Assert.Single(restored.HistoryOutbox);
        Assert.False(restored.QueuePublicHistory(article, 105));
        restored.MarkPublicHistoryEnqueued(article.Id);
        Assert.Empty(restored.HistoryOutbox);
        Assert.False(restored.QueuePublicHistory(article, 106));
        Assert.Contains(article.Id, restored.HistoryPublishedIds);
        Assert.NotEqual(ReignPublicNews.EventId("a|", "b"), ReignPublicNews.EventId("a", "|b"));
    }
    [Fact]
    public void EveryCategoryHasAUiIndependentPublicSnapshotAndStableTimelineIdentity()
    {
        foreach (string category in ReignNewsCategories.Switches.Keys.Where(ReignNewsCategories.IsCategory))
        {
            var article = new ReignNewsArticle { Id = category, Category = category, Title = "A public report",
                Body = "An accusation was publicly made; it remains an allegation.", Consequences = "A hearing will follow.",
                WorldDay = 100, PublicationDay = 103, ActorCaption = "OMITTED_PRODUCER_FIELD" };
            var snapshot = ReignPublicNews.Snapshot(article);
            var state = new ReignNewsState();
            Assert.True(state.Publish(article, new ReignNewsOptions(_ => false), true));
            Assert.Empty(state.Pending);
            Assert.Equal(103d, snapshot["publicationDay"]);
            Assert.Equal(100d, snapshot["eventDay"]);
            Assert.Equal("public_report", snapshot["evidenceKind"]);
            Assert.Equal(article.Body, snapshot["body"]);
            Assert.DoesNotContain("OMITTED_PRODUCER_FIELD", System.Text.Json.JsonSerializer.Serialize(snapshot));
            Assert.Equal(ReignPublicNews.EventId("timeline", article.Id), ReignPublicNews.EventId("timeline", article.Id));
            Assert.NotEqual(ReignPublicNews.EventId("timeline", article.Id), ReignPublicNews.EventId("other", article.Id));
        }
    }
}
