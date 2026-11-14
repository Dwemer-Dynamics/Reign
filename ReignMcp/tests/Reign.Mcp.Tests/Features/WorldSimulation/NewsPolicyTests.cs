using System.Text.Json;
using Reign.Core.Contracts.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NewsPolicyTests
{
    [Fact]
    public void StandingNewsUsesNormalDiplomacyPreferenceAndRevisionDeduplicationAcrossReload()
    {
        var queue = new ReignNewsState();
        var notice = Article("crier:military-standing:realm:2", ReignNewsCategories.Diplomacy);
        notice.Headline = "The realm moves to War Standing";
        notice.Body = "Field parties may mobilise using local recruits, supplies and clan funds.";
        Assert.True(queue.Publish(notice, new ReignNewsOptions(), true));
        var serializer = new JsonSerializerOptions { IncludeFields = true };
        var saved = JsonSerializer.Deserialize<ReignNewsState>(JsonSerializer.Serialize(queue, serializer), serializer)!;
        saved.NormalizeAfterLoad();
        Assert.False(saved.Publish(notice, new ReignNewsOptions(), true));
        Assert.Single(saved.Pending);
        var peace = Article("crier:military-standing:realm:3", ReignNewsCategories.Diplomacy);
        Assert.True(saved.Publish(peace, new ReignNewsOptions(k => k != ReignNewsCategories.Diplomacy), true));
        Assert.Single(saved.Pending);
        Assert.False(saved.Publish(peace, new ReignNewsOptions(), true));
    }
    [Fact]
    public void Announcement_reading_column_preserves_long_text_and_both_rulers_statements()
    {
        string summary = string.Concat(Enumerable.Repeat("The terms remain binding throughout the coming winter. ", 100));
        string terms = "The city changes hands.\n\nThe tribute is paid each day.";
        string text = ReignNewsCopy.AnnouncementReport(summary, terms, "Caladog", "I will honor the agreement.",
            "Derthert", "The border villages must be protected.", false);
        Assert.StartsWith(summary.Trim() + "\n\n" + terms + "\n\n", text);
        Assert.EndsWith("Derthert gave this reason: “The border villages must be protected.”", text);
        Assert.Contains("Caladog gave this reason: “I will honor the agreement.”", text);
        Assert.True(text.Length > 5000, "Long reports must not be shortened to fit the screen.");
    }

    [Fact]
    public void News_role_captions_do_not_become_quoted_statements_and_empty_paragraphs_are_omitted()
    {
        Assert.Equal("The ruler was captured.", ReignNewsCopy.AnnouncementReport(" The ruler was captured. ", "", "Caladog", "Capturing side", "Derthert", "Taken prisoner", true));
        Assert.Equal("The famine has ended.\n\nFood production has recovered.",
            ReignNewsCopy.AnnouncementReport("The famine has ended.", "Food production has recovered.", "", "", "", "", false));
    }

    private static ReignNewsArticle Article(string id, string category = ReignNewsCategories.Captures) => new()
    { Id = id, Category = category, Title = "News from the realm", Body = "A named leader has been captured.", WorldDay = 12.5 };

    [Theory]
    [InlineData(false, false, false)] [InlineData(true, false, true)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void Ordinary_captures_are_excluded_while_rulers_and_rebels_qualify(bool ruler, bool rebel, bool expected)
        => Assert.Equal(expected, ReignNewsCategories.CaptureQualifies(ruler, rebel));

    [Fact]
    public void Each_type_is_independent_and_master_does_not_erase_preferences()
    {
        var values = ReignNewsCategories.Switches.Keys.ToDictionary(k => k, _ => true);
        var options = new ReignNewsOptions(k => values[k]);
        foreach (var key in values.Keys.Where(k => k != ReignNewsCategories.Master))
        {
            values[key] = false;
            Assert.False(options.Allows(key));
            Assert.All(values.Keys.Where(k => k != key && k != ReignNewsCategories.Master), k => Assert.True(options.Allows(k)));
            values[key] = true;
        }
        values[ReignNewsCategories.Captures] = false; values[ReignNewsCategories.Master] = false;
        Assert.False(options.Allows(ReignNewsCategories.Executions));
        Assert.True(options.Preference(ReignNewsCategories.Executions));
        values[ReignNewsCategories.Master] = true;
        Assert.False(options.Allows(ReignNewsCategories.Captures));
        Assert.True(options.Allows(ReignNewsCategories.Executions));
        Assert.False(options.Allows("ordinary_capture"));
    }

    [Fact]
    public void Disabled_news_is_seen_once_but_never_backlogged_when_enabled()
    {
        var queue = new ReignNewsState();
        var off = new ReignNewsOptions(_ => false);
        Assert.True(queue.Publish(Article("one"), off, true)); // Caller still writes the ordinary text notice.
        Assert.Empty(queue.Pending);
        Assert.False(queue.Publish(Article("one"), new ReignNewsOptions(), true));
        Assert.True(queue.Publish(Article("two"), new ReignNewsOptions(), true));
        Assert.Equal("two", Assert.Single(queue.Pending).Id);
    }

    [Fact]
    public void Settings_unavailable_at_start_retains_news_until_authoritative_preferences_arrive()
    {
        var queue = new ReignNewsState();
        queue.Publish(Article("capture"), new ReignNewsOptions(_ => false), false);
        queue.Publish(Article("execution", ReignNewsCategories.Executions), new ReignNewsOptions(), false);
        Assert.Equal(2, queue.Pending.Count);
        queue.ApplyOptions(new ReignNewsOptions(k => k != ReignNewsCategories.Captures));
        Assert.Equal("execution", Assert.Single(queue.Pending).Id);
        queue.ApplyOptions(new ReignNewsOptions());
        Assert.Single(queue.Pending);
    }

    [Fact]
    public void Save_load_retains_order_complete_text_and_read_state_without_replay()
    {
        var queue = new ReignNewsState();
        queue.Publish(Article("read"), new ReignNewsOptions(), true);
        Assert.True(queue.Acknowledge("read"));
        var longNotice = Article("unread"); longNotice.Body = string.Concat(Enumerable.Repeat("A long report with named houses and consequences. ", 150));
        queue.Publish(longNotice, new ReignNewsOptions(), true);
        queue.Publish(Article("last", ReignNewsCategories.WorldEvents), new ReignNewsOptions(), true);
        queue.CaptivityEpisodes["ruler"] = "capture-episode";
        var serializer = new JsonSerializerOptions { IncludeFields = true };
        var saved = JsonSerializer.Deserialize<ReignNewsState>(JsonSerializer.Serialize(queue, serializer), serializer)!;
        saved.NormalizeAfterLoad();
        Assert.Equal(new[] { "unread", "last" }, saved.Pending.Select(p => p.Id));
        Assert.Equal(longNotice.Body, saved.Pending[0].Body);
        Assert.False(saved.Publish(Article("read"), new ReignNewsOptions(), true));
        Assert.False(saved.Acknowledge("last"));
        Assert.True(saved.Acknowledge("unread"));
        Assert.Equal("capture-episode", saved.CaptivityEpisodes["ruler"]);
    }

    [Fact]
    public void Long_pending_queue_survives_bounded_history_and_cannot_requeue_an_unread_id()
    {
        var queue = new ReignNewsState();
        for (int i = 0; i < 4200; i++) queue.Publish(Article("event" + i), new ReignNewsOptions(), true);
        Assert.Equal(4200, queue.Pending.Count);
        Assert.Equal(4096, queue.Seen.Count);
        Assert.False(queue.Publish(Article("event0"), new ReignNewsOptions(), true));
        Assert.Equal("event0", queue.Pending[0].Id);
    }

    [Theory]
    [InlineData("a", false, "Fen Gruffendoc", "Dey Meroc")]
    [InlineData("b", false, "Dey Meroc", "Fen Gruffendoc")]
    public void Contested_rulings_name_the_winner_and_the_unreconciled_house(string side, bool mediated, string winner, string loser)
    {
        string copy = ReignNewsCopy.ClanRuling("Caladog", "Fen Gruffendoc", "Dey Meroc", side, mediated);
        Assert.Contains("Caladog has ruled in favor of " + winner, copy);
        Assert.Contains(loser + " leaves court embittered", copy);
        Assert.DoesNotContain("Charm", copy); Assert.DoesNotContain("roll", copy);
        Assert.DoesNotContain("Petitioners:", copy);
    }

    [Fact]
    public void Successful_mediation_does_not_declare_a_loser_or_an_unresolved_feud()
    {
        string copy = ReignNewsCopy.ClanRuling("Rhagaea", "Pethros", "Dionicos", "a", true);
        Assert.Contains("Both houses have accepted an accord", copy);
        Assert.DoesNotContain("embittered", copy);
        Assert.DoesNotContain("favor", copy);
        Assert.Contains("confirmed ruling has not", ReignNewsCopy.ClanRuling("Ruler", "A", "B", "unknown", false));
    }

    private static ReignForeignRuling Side(string ruler, string realm, string stance, int before = 10, int after = 10)
        => new() { Ruler = ruler, Realm = realm, Stance = stance, Before = before, After = after };

    [Fact]
    public void Smuggling_refusals_identify_the_demand_own_petitioners_and_actual_scope()
    {
        var origin = Side("Unqid", "Aserai", "preserve_relations");
        var target = Side("Rhagaea", "southern Empire", "preserve_relations");
        origin.Petitioners = new[] { "Awdhan" }; target.Petitioners = new[] { "Oros" };
        string summary = ReignNewsCopy.ForeignRulingSummary("Smuggling Crisis", "treaty_strain", origin, target);
        Assert.Contains("Unqid of Aserai rejected the demand for stronger action against protected smugglers put forward by Awdhan.", summary);
        Assert.Contains("Rhagaea of southern Empire rejected the demand for stronger action against protected smugglers put forward by Oros.", summary);
        Assert.DoesNotContain("each rejected", summary);
        string effects = ReignNewsCopy.ForeignRulingConsequences("hostile", "treaty_strain", origin, target);
        Assert.Contains("Both refusals angered the petitioning nobles", effects);
        Assert.Contains("In Aserai, calls for confrontation or cooperation with southern Empire are unchanged.", effects);
        Assert.Contains("In southern Empire, calls for confrontation or cooperation with Aserai are unchanged.", effects);
        Assert.Contains("did not launch enforcement action or change a treaty", effects);
        Assert.DoesNotContain("unsettled their lands", effects); // No unverified native loyalty claim.
        Assert.Single(effects.Split("\n\n"), p => p.Contains("angered"));
    }

    [Fact]
    public void Material_assistance_support_is_a_proposal_and_never_reported_as_an_aid_transfer()
    {
        var origin = Side("Halthdar", "Nords", "support_lords", 10, -10);
        var target = Side("Derthert", "Vlandians", "support_lords", 0, -20);
        string summary = ReignNewsCopy.ForeignRulingSummary("Material Assistance", "aid_exchange", origin, target);
        Assert.Contains("Halthdar of Nords backed the proposal for a limited subsidy or supply arrangement with Vlandians", summary);
        Assert.Contains("Derthert of Vlandians backed the proposal for a limited subsidy or supply arrangement with Nords", summary);
        string effects = ReignNewsCopy.ForeignRulingConsequences("peaceful", "aid_exchange", origin, target);
        Assert.Contains("In Nords, support for cooperation with Vlandians has grown.", effects);
        Assert.Contains("In Vlandians, support for cooperation with Nords has grown.", effects);
        Assert.Contains("did not transfer money, supplies or prisoners", effects);
        Assert.Contains("requires a separate agreement", effects);
        Assert.DoesNotContain("angered", effects);
        Assert.DoesNotContain("confrontation", effects);
    }

    [Theory]
    [InlineData("support_lords", "backed")]
    [InlineData("preserve_relations", "rejected")]
    [InlineData("refuse_court", "rejected")]
    [InlineData("refuse_lords", "rejected")]
    [InlineData("awaiting_player", "has yet to rule on")]
    [InlineData("unexpected", "has issued no confirmed ruling on")]
    public void Mixed_decisions_keep_each_ruler_and_realm_attributed(string stance, string expected)
    {
        var origin = Side("Caladog", "Battania", stance);
        var target = Side("Derthert", "Vlandia", "awaiting_player");
        string summary = ReignNewsCopy.ForeignRulingSummary("Smuggling Crisis", "treaty_strain", origin, target);
        if (stance == "unexpected") Assert.DoesNotContain("Caladog", summary);
        else Assert.Contains("Caladog of Battania " + expected, summary);
        Assert.Contains("Derthert of Vlandia has yet to rule on", summary);
        string effects = ReignNewsCopy.ForeignRulingConsequences("hostile", "treaty_strain", origin, target);
        Assert.DoesNotContain("In Vlandia,", effects); // A pending ruling has no confirmed sentiment effects.
        if (stance is "awaiting_player" or "unexpected") Assert.DoesNotContain("In Battania,", effects);
        Assert.DoesNotContain("10", effects); Assert.DoesNotContain("20", effects);
        Assert.DoesNotContain("support_lords", summary); Assert.DoesNotContain("political pressure", effects);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void One_sided_records_do_not_turn_the_affected_ruler_into_a_second_decision_maker(bool reverse)
    {
        var deciding = Side("Unqid", "Aserai", "preserve_relations");
        deciding.Petitioners = new[] { "Awdhan" };
        var affected = Side("Rhagaea", "southern Empire", "", 10, 20);
        var origin = reverse ? affected : deciding; var target = reverse ? deciding : affected;
        string summary = ReignNewsCopy.ForeignRulingSummary("Smuggling Crisis", "treaty_strain", origin, target);
        Assert.Contains("Unqid of Aserai rejected", summary);
        Assert.Contains("put forward by Awdhan", summary);
        Assert.Contains("relations with southern Empire", summary);
        Assert.DoesNotContain("Rhagaea", summary);
        Assert.DoesNotContain("Both", summary);
        string effects = ReignNewsCopy.ForeignRulingConsequences("hostile", "treaty_strain", origin, target);
        Assert.DoesNotContain("Both refusals", effects);
        Assert.Contains("In southern Empire, calls for confrontation with Aserai have grown.", effects);
        Assert.DoesNotContain("Rhagaea", effects); // An observed effect is not a decision by the affected ruler.
    }

    [Theory]
    [InlineData(10, 20, "have grown")]
    [InlineData(10, 0, "have eased")]
    [InlineData(100, 100, "are unchanged")]
    public void Sentiment_uses_observed_changes_including_saturated_values(int before, int after, string expected)
    {
        var origin = Side("Caladog", "Battania", "support_lords", before, after);
        var target = Side("Derthert", "Vlandia", "awaiting_player");
        string effects = ReignNewsCopy.ForeignRulingConsequences("hostile", "war", origin, target);
        Assert.Contains(expected, effects);
        Assert.DoesNotContain("In Vlandia,", effects);
        Assert.Contains("did not declare war, conclude peace or enact an agreement", effects);
    }

    [Fact]
    public void All_catalog_petitions_have_specific_copy_and_unknown_topics_preserve_the_fallback()
    {
        string root = TestOptions.FindWorkspace();
        string catalog = File.ReadAllText(Path.Combine(root, "ReignServer", "src", "Modules", "Diplomacy", "PoliticalPressureSystem.cs"));
        var definitions = System.Text.RegularExpressions.Regex.Matches(catalog, "\"(?:hostile|peaceful)\\|([^|]+)\\|([^|]+)\\|[^\"]+\"");
        Assert.Equal(32, definitions.Count);
        foreach (System.Text.RegularExpressions.Match match in definitions)
        {
            string summary = ReignNewsCopy.ForeignRulingSummary(match.Groups[2].Value, match.Groups[1].Value,
                Side("A", "Realm A", "support_lords"), Side("B", "Realm B", "support_lords"));
            Assert.DoesNotContain("petition concerning the other realm", summary);
            Assert.DoesNotContain("proposal for assistance between the realms", summary);
        }
        Assert.Contains("proposal for assistance between the realms", ReignNewsCopy.ForeignRulingSummary("Unknown", "aid_exchange",
            Side("A", "Realm A", "support_lords"), Side("B", "Realm B", "support_lords")));
    }

    [Theory]
    [InlineData("famine", "less food")]
    [InlineData("golden_age", "Construction")]
    [InlineData("pestilence", "households")]
    [InlineData("crime_wave", "security")]
    [InlineData("trade_collapse", "prosperity")]
    [InlineData("construction_stagnation", "slowed")]
    [InlineData("political_unrest", "Loyalty")]
    [InlineData("bountiful_harvest", "more food")]
    [InlineData("population_boom", "households")]
    [InlineData("law_and_order", "Security")]
    [InlineData("trade_boom", "prosperity")]
    [InlineData("national_unity", "Loyalty")]
    public void World_news_describes_the_actual_kind_of_effect(string kind, string detail)
    {
        string copy = ReignNewsCopy.WorldEvent(kind, "Battania", "", "", "", false, false);
        Assert.Contains("Battania", copy); Assert.Contains(detail, copy);
        Assert.DoesNotContain(kind + ":", copy);
        string ended = ReignNewsCopy.WorldEvent(kind, "Battania", "", "", "", true, false);
        Assert.Contains("Battania", ended); Assert.DoesNotContain("recent upheaval", ended);
    }

    [Fact]
    public void Golden_age_does_not_invent_prosperity_bonuses_and_succession_names_both_rulers()
    {
        Assert.DoesNotContain("prosperity", ReignNewsCopy.WorldEvent("golden_age", "Battania", "", "", "", false, false));
        Assert.Equal("Caladog has abdicated the throne of Battania. Corein has succeeded to the crown.",
            ReignNewsCopy.WorldEvent("orderly_succession", "Battania", "", "Caladog", "Corein", false, false));
        Assert.Contains("ceased to exist", ReignNewsCopy.WorldEvent("famine", "Battania", "", "", "", true, true));
    }

    [Fact]
    public void Malformed_records_do_not_enter_the_saved_queue()
    {
        var queue = new ReignNewsState(); var notice = Article("broken");
        notice.WorldDay = double.NaN; Assert.False(queue.Publish(notice, new ReignNewsOptions(), true));
        notice.WorldDay = 1; notice.Category = "ordinary_capture"; Assert.False(queue.Publish(notice, new ReignNewsOptions(), true));
        Assert.Empty(queue.Seen); Assert.Empty(queue.Pending);
        queue.Version = 2; Assert.Throws<InvalidOperationException>(queue.NormalizeAfterLoad);
    }
}
