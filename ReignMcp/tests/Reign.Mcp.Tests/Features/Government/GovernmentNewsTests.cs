using System.Text.Json;
using Reign.Core.Contracts.Government;
using Reign.Core.Contracts.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.Government;

public sealed class GovernmentNewsTests
{
    private static GovernmentNewsItem Item(string id, string stage = "presented") => new()
        { ReceiptId = id, BusinessId = id.Split(':')[0], Stage = stage, Text = stage + " — A public matter." };

    [Fact]
    public void One_edition_combines_kingdoms_and_waits_for_the_following_day()
    {
        var ledger = new GovernmentNewsLedger();
        Assert.True(ledger.Record(14.2, "Spring 15", "empire", "Southern Empire", "Senate", Item("a:presented")));
        Assert.True(ledger.Record(14.7, "Spring 15", "battania", "Battania", "Assembly", Item("b:decided", "decided")));
        Assert.Empty(ledger.PublishDue(14.99));
        var edition = Assert.Single(ledger.PublishDue(15));
        Assert.Equal(2, edition.Kingdoms.Count);
        Assert.Contains("Southern Empire", edition.Body);
        Assert.Contains("Battania", edition.Body);
        Assert.Equal(1, edition.Kingdoms.Sum(k => k.PresentedCount));
        Assert.Equal(1, edition.Kingdoms.Sum(k => k.DecidedCount));
        Assert.Empty(ledger.PublishDue(16));
    }

    [Fact]
    public void Save_reload_deduplicates_receipts_and_retains_archive_after_popup_acknowledgement()
    {
        var ledger = new GovernmentNewsLedger();
        ledger.Record(29, "Spring 30", "vlandia", "Vlandia", "Council", Item("a:presented"));
        var options = new JsonSerializerOptions { IncludeFields = true };
        ledger = JsonSerializer.Deserialize<GovernmentNewsLedger>(JsonSerializer.Serialize(ledger, options), options)!;
        ledger.Normalize();
        Assert.False(ledger.Record(29, "Spring 30", "vlandia", "Vlandia", "Council", Item("a:presented")));
        var edition = Assert.Single(ledger.PublishDue(30));
        var queue = new ReignNewsState();
        Assert.True(queue.Publish(new ReignNewsArticle { Id = edition.Id, Category = ReignNewsCategories.Government,
            Title = "Herald", Body = edition.Body, WorldDay = 30, GovernmentNews = edition }, new ReignNewsOptions(), true));
        Assert.True(queue.Acknowledge(edition.Id));
        Assert.Empty(queue.Pending);
        Assert.Single(ledger.Archive);
    }

    [Fact]
    public void Disabling_government_news_suppresses_popup_without_erasing_public_archive()
    {
        var ledger = new GovernmentNewsLedger();
        ledger.Record(14, "Spring 15", "empire", "Empire", "Senate", Item("a:presented"));
        var edition = Assert.Single(ledger.PublishDue(15));
        var queue = new ReignNewsState();
        queue.Publish(new ReignNewsArticle { Id = edition.Id, Category = ReignNewsCategories.Government,
            Title = "Herald", Body = edition.Body, WorldDay = 15 }, new ReignNewsOptions(k => k != ReignNewsCategories.Government), true);
        Assert.Empty(queue.Pending);
        Assert.Single(ledger.Archive);
        Assert.False(queue.Publish(new ReignNewsArticle { Id = edition.Id, Category = ReignNewsCategories.Government,
            Title = "Herald", Body = edition.Body, WorldDay = 15 }, new ReignNewsOptions(), true));
    }

    [Fact]
    public void Late_decision_is_a_new_edition_and_does_not_rewrite_original_proceedings()
    {
        var ledger = new GovernmentNewsLedger();
        ledger.Record(14, "Spring 15", "empire", "Empire", "Senate", Item("a:presented"));
        var original = Assert.Single(ledger.PublishDue(15));
        string snapshot = original.Body;
        Assert.False(ledger.Record(14, "Spring 15", "empire", "Empire", "Senate", Item("a:decided", "decided")));
        Assert.True(ledger.Record(15, "Spring 16", "empire", "Empire", "Senate", Item("a:decided", "decided")));
        Assert.Single(ledger.PublishDue(16));
        Assert.Equal(snapshot, original.Body);
        Assert.Equal(2, ledger.Archive.Count);
    }

    [Fact]
    public void Pending_outcome_can_be_completed_before_publication_but_published_copy_is_immutable()
    {
        var ledger = new GovernmentNewsLedger();
        var pending = Item("a:decision", "decided"); pending.Text = "Approved; execution pending.";
        ledger.Record(14, "Spring 15", "empire", "Empire", "Senate", pending);
        var complete = Item("a:decision", "decided"); complete.Text = "Approved and enacted.";
        Assert.True(ledger.Record(14.5, "Spring 15", "empire", "Empire", "Senate", complete));
        var edition = Assert.Single(ledger.PublishDue(15));
        Assert.Single(edition.Kingdoms[0].Items);
        Assert.Contains("Approved and enacted.", edition.Body);
        Assert.True(ledger.IsPublished("a:decision"));
        Assert.False(ledger.Record(15, "Spring 16", "empire", "Empire", "Senate", pending));
    }

    [Fact]
    public void Summer_session_report_waits_for_its_actual_calendar_date_boundary()
    {
        var ledger = new GovernmentNewsLedger();
        ledger.Record(45.5, "Summer 15", "empire", "Empire", "Senate", Item("a:presented"));
        Assert.Empty(ledger.PublishDue(46.49));
        var edition = Assert.Single(ledger.PublishDue(46.5));
        Assert.Equal(45.5, edition.SessionDay);
    }

    [Fact]
    public void Invalid_dates_and_unknown_future_save_versions_fail_safely()
    {
        var ledger = new GovernmentNewsLedger();
        Assert.False(ledger.Record(double.NaN, "", "empire", "Empire", "Senate", Item("a")));
        Assert.False(ledger.Record(double.PositiveInfinity, "", "empire", "Empire", "Senate", Item("a")));
        Assert.Empty(ledger.PublishDue(double.NaN));
        ledger.Version = 99;
        Assert.Throws<InvalidOperationException>(() => ledger.Normalize());
    }
}
