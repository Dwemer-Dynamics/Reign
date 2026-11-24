using System.Collections.Concurrent;
using System.Text;
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
