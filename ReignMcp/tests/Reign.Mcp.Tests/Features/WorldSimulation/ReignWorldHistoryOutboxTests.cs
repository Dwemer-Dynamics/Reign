using System.Text;
using ReignBeta.Integration;

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
