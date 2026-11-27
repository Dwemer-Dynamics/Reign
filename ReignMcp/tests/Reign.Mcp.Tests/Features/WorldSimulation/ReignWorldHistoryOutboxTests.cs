using System.Collections.Concurrent;
using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.Integration;

namespace Reign.Mcp.Tests;

public sealed class ReignWorldHistoryOutboxTests
{
    [Theory]
    [InlineData(211)] // The recovered-branch directory from the 2026-10-21 crash dump.
    [InlineData(245)] // pending.jsonl still fits the legacy 260-character path limit.
    public void Rewrites_long_existing_outboxes_without_lengthening_the_path(int parentLength)
    {
        var root = Path.Combine(Path.GetTempPath(), "reign-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            var parent = root;
            while (parentLength - parent.Length > 100)
                parent = Path.Combine(parent, new string('b', 80));
            parent = Path.Combine(parent, new string('t', parentLength - parent.Length - 1));
            Directory.CreateDirectory(parent);
            var path = Path.Combine(parent, "pending.jsonl");
            File.WriteAllText(path, "before\nIra — retained\nafter\n", new UTF8Encoding(false));
            Assert.Equal(parentLength, parent.Length);
            Assert.True((path + ".save_sync_" + new string('a', 32)).Length >= 260);
            ReignWorldHistoryOutbox.Rewrite(path, (reader, writer) =>
            {
                var scratch = Assert.Single(Directory.GetFiles(parent), p => p != path);
                Assert.True(scratch.Length <= path.Length);
                reader.ReadLine();
                writer.WriteLine(reader.ReadLine());
            });
            Assert.Equal("Ira — retained" + Environment.NewLine, File.ReadAllText(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(parent)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Replacement_failure_retains_the_complete_scratch_when_the_destination_disappears()
    {
        var root = Path.Combine(Path.GetTempPath(), "reign-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "pending.jsonl");
            File.WriteAllText(path, "original source");
            Assert.ThrowsAny<IOException>(() => ReignWorldHistoryOutbox.Rewrite(path, (reader, writer) =>
            {
                writer.Write("complete recovery copy");
                reader.Dispose();
                File.Delete(path);
            }));
            Assert.False(File.Exists(path));
            var recovery = Assert.Single(Directory.GetFiles(root));
            Assert.Equal("complete recovery copy", File.ReadAllText(recovery));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Ordinary_selection_and_acknowledgement_stop_at_the_persisted_owned_record()
    {
        string earlier = """{"eventId":"before","sequence":2}""";
        string misplacedLater = """{"eventId":"ahead","sequence":8}""";
        string owned = """{"eventId":"before","sequence":5,"_reignOwnedHistory":{"version":1}}""";
        string later = """{"eventId":"before","sequence":6}""";
        string misplacedAfter = """{"eventId":"after","sequence":3}""";
        string[] records = { earlier, misplacedLater, owned, later, misplacedAfter };
        long? fence = ReignWorldHistoryOutbox.FindOwnedSequence(records, Array.Empty<JObject>());
        Assert.Equal(5L, fence);
        Assert.Equal("before", Assert.Single(ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 10, 100, fence)).Value<string>("eventId"));

        WithOutbox(records, path =>
        {
            ReignWorldHistoryOutbox.RemoveOrdinaryAcknowledged(path,
                new HashSet<string>(new[] { "before", "ahead", "after" }, StringComparer.OrdinalIgnoreCase), fence);
            Assert.Equal(new[] { misplacedLater, owned, later, misplacedAfter }, File.ReadAllLines(path));
        });
    }

    [Fact]
    public void Corrupt_owned_identity_holds_the_record_and_unusable_sequence_holds_the_whole_batch()
    {
        string earlier = """{"eventId":"before","sequence":2}""";
        string corruptIdentity = """{"eventId":{"unexpected":1},"sequence":5,"_reignOwnedHistory":null}""";
        string later = """{"eventId":"after","sequence":6}""";
        Assert.False(ReignWorldHistoryOutbox.TryReadOrdinaryRecord(corruptIdentity, out _, out bool recognized));
        Assert.True(recognized);
        string[] records = { earlier, corruptIdentity, later };
        long? fence = ReignWorldHistoryOutbox.FindOwnedSequence(records, Array.Empty<JObject>());
        Assert.Equal(5L, fence);
        Assert.Equal("before", Assert.Single(ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 10, 100, fence)).Value<string>("eventId"));

        foreach (string corrupt in new[]
        {
            """{"_reignOwnedHistory":true,"sequence":"5"}""",
            """{"\u005freignOwnedHistory":null,"sequence":""",
        })
        {
            Assert.False(ReignWorldHistoryOutbox.TryReadOrdinaryRecord(corrupt, out _, out recognized));
            Assert.True(recognized);
            records[1] = corrupt;
            fence = ReignWorldHistoryOutbox.FindOwnedSequence(records, Array.Empty<JObject>());
            Assert.Equal(0L, fence);
            Assert.Empty(ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 10, 100, fence));
        }
    }

    [Fact]
    public void Saved_restoration_retains_in_scope_marker_bytes_and_excludes_known_future_rows()
    {
        string earlier = """{"eventId":"before","sequence":2}""";
        string owned = """{"sequence":5,"eventId":"owned","_reignOwnedHistory":{"version":2,"unsupported":true}}""";
        string damaged = """{"_reignOwnedHistory":null,"sequence":""";
        string future = """{"eventId":"future","sequence":9,"_reignOwnedHistory":true}""";
        WithOutbox(new[] { earlier, owned, damaged, future }, path =>
        {
            ReignWorldHistoryOutbox.RestoreThroughSequence(path, 5);
            Assert.Equal(new[] { earlier, owned, damaged }, File.ReadAllLines(path));
            long? restoredFence = ReignWorldHistoryOutbox.FindOwnedSequence(File.ReadLines(path), Array.Empty<JObject>());
            Assert.Equal(0L, restoredFence);
            Assert.Empty(ReignWorldHistoryOutbox.ReadOrdinaryPrefix(File.ReadLines(path), 10, 100, restoredFence));
        });
    }

    [Fact]
    public void Ordinary_legacy_rows_keep_existing_limits_and_ignore_nested_or_textual_markers()
    {
        string[] records =
        {
            """{"eventId":"too_new","sequence":99}""",
            """{"eventId":"earlier","sequence":2,"payload":{"_reignOwnedHistory":true}}""",
            "unrelated malformed line",
            """{"eventId":"EARLIER","sequence":2,"summary":"quoted _reignOwnedHistory only"}""",
            """{"eventId":"next","sequence":4}""",
            """{"eventId":"beyond_limit","sequence":5}""",
        };
        long? fence = ReignWorldHistoryOutbox.FindOwnedSequence(records, Array.Empty<JObject>());
        Assert.Null(fence);
        var prefix = ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 1, 5, fence);
        Assert.Equal(new[] { "earlier" }, prefix.Select(item => item.Value<string>("eventId")));
        prefix = ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 10, 3, fence);
        Assert.Equal(new[] { "EARLIER" }, prefix.Select(item => item.Value<string>("eventId")));
    }

    [Fact]
    public void Queued_owned_record_blocks_a_physically_earlier_later_sequence_before_append()
    {
        string[] records = { """{"eventId":"allowed","sequence":2}""", """{"eventId":"ahead","sequence":7}""" };
        JObject queued = JObject.Parse("""{"eventId":"queued","sequence":4,"_reignOwnedHistory":true}""");
        long? fence = ReignWorldHistoryOutbox.FindOwnedSequence(records, new[] { queued });
        Assert.Equal(4L, fence);
        var prefix = ReignWorldHistoryOutbox.ReadOrdinaryPrefix(records, 10, 100, fence);
        Assert.Equal("allowed", Assert.Single(prefix).Value<string>("eventId"));
    }

    private static void WithOutbox(string[] records, Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "reign-owned-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "pending.jsonl");
            File.WriteAllLines(path, records);
            test(path);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Owned_acknowledgment_retires_only_the_exact_first_record()
    {
        const string owned = """{"eventId":"same","sequence":5,"_reignOwnedHistory":{"version":1}}""";
        const string later = """{"eventId":"same","sequence":6}""";
        WithOutbox(new[] { owned, later }, path =>
        {
            var expected = JObject.Parse(owned);
            var changed = (JObject)expected.DeepClone();
            changed["sequence"] = 7;
            Assert.False(ReignWorldHistoryOutbox.AcknowledgeExactOwnedFirstRecord(path, changed));
            Assert.Equal(new[] { owned, later }, File.ReadAllLines(path));
            Assert.True(ReignWorldHistoryOutbox.AcknowledgeExactOwnedFirstRecord(path, expected));
            Assert.Equal(new[] { later }, File.ReadAllLines(path));
        });
    }

    [Fact]
    public void Keeps_the_exact_ordered_prefix_in_memory_until_the_append_succeeds()
    {
        var pending = new ConcurrentQueue<string>(new[] { "first", "second", "later" });
        Assert.Throws<IOException>(() => ReignWorldHistoryOutbox.PersistQueuePrefix(pending, 2, staged =>
        {
            Assert.Equal(new[] { "first", "second" }, staged);
            Assert.Equal(new[] { "first", "second", "later" }, pending.ToArray());
            throw new IOException("an isolated append failed");
        }));
        Assert.Equal(new[] { "first", "second", "later" }, pending.ToArray());

        int count = ReignWorldHistoryOutbox.PersistQueuePrefix(pending, 2, staged =>
        {
            Assert.Equal(new[] { "first", "second" }, staged);
            pending.Enqueue("concurrent producer");
            Assert.True(pending.TryPeek(out string? head));
            Assert.Equal("first", head);
        });
        Assert.Equal(2, count);
        Assert.Equal(new[] { "later", "concurrent producer" }, pending.ToArray());
    }

    [Fact]
    public void A_torn_last_line_cannot_swallow_the_first_complete_replay_record()
    {
        var root = Path.Combine(Path.GetTempPath(), "reign-outbox-tail-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "pending.jsonl");
            const string first = "{\"eventId\":\"prior\"}", torn = "{\"eventId\":";
            File.WriteAllText(path, first + "\n" + torn, new UTF8Encoding(false));
            const string replay = "{\"eventId\":\"retry\"}";
            const string later = "{\"eventId\":\"later\"}";
            ReignWorldHistoryOutbox.AppendRecords(path, replay + "\n");
            ReignWorldHistoryOutbox.AppendRecords(path, later + "\n");
            Assert.Equal(new[] { first, torn, replay, later }, File.ReadAllLines(path));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Ready_system_history_publishes_head_only_after_scoped_queue_admission()
    {
        string workspace = TestOptions.FindWorkspace();
        string client = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignWorldHistoryClient.cs", "/src/Modules/WorldSimulation/Integration/"));
        int stageStart = client.IndexOf("internal static bool TryStageReadySystemEvent", StringComparison.Ordinal);
        int legacyStart = client.IndexOf("internal static void Enqueue", stageStart, StringComparison.Ordinal);
        string stage = client[stageStart..legacyStart];
        int owner = stage.IndexOf("epoch != _epoch || campaignId != _campaignId || timelineId != _timelineId", StringComparison.Ordinal);
        int retained = stage.IndexOf("Pending.Enqueue(envelope);", StringComparison.Ordinal);
        int published = stage.IndexOf("publishHead();", StringComparison.Ordinal);
        Assert.Contains("lock (StateLock)", stage);
        Assert.True(owner >= 0 && owner < retained && retained < published);

        string behavior = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignWorldHistoryCampaignBehavior.cs", "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("stageReadySystemFact: true", behavior);
        Assert.Contains("long sequence = publishAfterStage ? checked(_sequence + 1) : ++_sequence;", behavior);
        Assert.Contains("if (!publishAfterStage) PublishHead();", behavior);
        Assert.Contains("evt, stagedCampaign, stagedTimeline, stagedEpoch, PublishHead", behavior);
        Assert.Contains("beforeEnqueue: e => _publicNewsAttempts[eventId]", behavior);
    }

    [Fact]
    public void Failed_rewrites_preserve_the_original_bytes_and_remove_only_the_owned_scratch_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "reign-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "pending.jsonl");
            byte[] original = Encoding.UTF8.GetBytes("unchanged — source\n");
            File.WriteAllBytes(path, original);
            Assert.Throws<IOException>(() => ReignWorldHistoryOutbox.Rewrite(path, (reader, writer) =>
            {
                writer.Write("incomplete replacement");
                throw new IOException("isolated rewrite failure");
            }));
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(path, Assert.Single(Directory.GetFiles(root)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
