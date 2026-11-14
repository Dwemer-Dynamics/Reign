using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using Xunit;

namespace Reign.Mcp.Tests.Features.Reputation;

public sealed class ReignRulerSampleIndexTests
{
    private static string? Id(JObject row) => row["id"]?.Value<string>();
    private static double Day(JObject row) => row["day"]?.Value<double?>() ?? 0d;
    private static JObject Row(string? id, double day) => new() { ["id"] = id, ["day"] = day };
    private static bool LegacyContains(JArray source, string? id, double day) =>
        source.OfType<JObject>().Any(row => Id(row) == id && Day(row) == day);
    private static JObject? LegacyPrevious(JArray source, string? id, double day) =>
        source.OfType<JObject>().Where(row => Id(row) == id && Day(row) < day)
            .OrderByDescending(Day).FirstOrDefault();

    [Fact]
    public void Unsorted_history_matches_legacy_including_first_ties_and_special_numbers()
    {
        var source = new JArray(Row("A", 5), Row("A", 1), Row("A", 5), Row("a", 9),
            Row(null, 2), Row("A", double.NaN), Row("A", -0d), Row("A", +0d),
            Row("A", double.NegativeInfinity), Row("A", double.PositiveInfinity), new JValue(42));
        var index = new ReignRulerSampleIndex(Id, Day);
        foreach (string? id in new[] { "A", "a", "missing", null })
        foreach (double day in new[] { -1d, -0d, 1d, 2d, 5d, 6d, 10d, double.NaN, double.PositiveInfinity })
        {
            Assert.Equal(LegacyContains(source, id, day), index.Contains(source, id, day));
            Assert.Same(LegacyPrevious(source, id, day), index.Previous(source, id, day));
        }
    }

    [Fact]
    public void Appends_index_actual_clones_and_preserve_serialized_history()
    {
        var source = new JArray(Row("v", 1));
        var expected = (JArray)source.DeepClone();
        var index = new ReignRulerSampleIndex(Id, Day);
        Assert.False(index.Contains(source, "v", 2));
        var row = Row("v", 2);
        var otherOwner = new JArray(row);
        index.Add(source, row, 2);
        expected.Add(row);
        Assert.NotSame(row, source.Last);
        Assert.True(index.Contains(source, "v", 2));
        Assert.Same(source.Last, index.Previous(source, "v", 3));
        Assert.Equal(expected.ToString(Formatting.None), source.ToString(Formatting.None));
        string before = source.ToString(Formatting.None);
        _ = index.Previous(source, "v", 4);
        Assert.Equal(before, source.ToString(Formatting.None));
    }

    [Fact]
    public void Prune_replacement_load_and_explicit_owner_invalidation_refresh_the_lookup()
    {
        var source = new JArray(Row("v", 1), Row("v", 2), Row("v", 3));
        var index = new ReignRulerSampleIndex(Id, Day);
        Assert.Same(source[1], index.Previous(source, "v", 3));
        source[1]!.Remove(); // Actual PruneArray route.
        Assert.Same(source[0], index.Previous(source, "v", 3));
        var restored = JArray.Parse(source.ToString(Formatting.None));
        Assert.Same(restored[0], index.Previous(restored, "v", 3));
        restored[0]!["day"] = 8;
        index.Invalidate(); // Owner invalidates any exceptional in-place edit.
        Assert.Null(index.Previous(restored, "v", 3));
        restored[0] = Row("v", 0);
        index.Invalidate();
        Assert.Same(restored[0], index.Previous(restored, "v", 3));
    }

    [Fact]
    public void Malformed_unrelated_row_preserves_legacy_short_circuit_and_exception()
    {
        var source = new JArray(Row("good", 1), new JObject { ["id"] = "bad", ["day"] = "invalid" });
        var index = new ReignRulerSampleIndex(Id, Day);
        Assert.True(index.Contains(source, "good", 1));
        Assert.Same(source[0], index.Previous(source, "good", 2));
        var legacy = Record.Exception(() => LegacyContains(source, "bad", 1));
        var actual = Record.Exception(() => index.Contains(source, "bad", 1));
        Assert.NotNull(legacy);
        Assert.Equal(legacy.GetType(), actual?.GetType());
        source.Last!.Remove();
        Assert.False(index.Contains(source, "bad", 1));
    }

    [Fact]
    public void Repeated_queries_and_owned_appends_do_not_rescan_every_village_history()
    {
        var source = new JArray();
        for (int village = 0; village < 100; village++)
        for (int day = 0; day < 40; day++) source.Add(Row("v" + village, day));
        int idReads = 0, dayReads = 0;
        var index = new ReignRulerSampleIndex(row => { idReads++; return Id(row); },
            row => { dayReads++; return Day(row); });
        for (int pass = 0; pass < 20; pass++)
        for (int village = 0; village < 100; village++)
        {
            string id = "v" + village;
            Assert.False(index.Contains(source, id, 40));
            Assert.Equal(39d, Day(index.Previous(source, id, 40)!));
        }
        Assert.Equal(4000, idReads);
        Assert.Equal(4000, dayReads);
        for (int village = 0; village < 100; village++) index.Add(source, Row("v" + village, 40), 40);
        Assert.Equal(4100, idReads);
        Assert.Equal(4100, dayReads);
        Assert.True(index.Contains(source, "v99", 40));
        Assert.Equal(4100, idReads);
    }
}
