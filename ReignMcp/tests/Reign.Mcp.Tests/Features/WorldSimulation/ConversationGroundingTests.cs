using System.Text;
using Reign.Core.Contracts.Dialogue;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ConversationGroundingTests
{
    [Fact]
    public void OriginalUnicodeBytesSurviveReceiptReplacementAndInjectedFailures()
    {
        string directory = Path.Combine(Path.GetTempPath(), "reign-input-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "original.json");
            byte[] original = Encoding.UTF8.GetBytes("  *I mock-duel with a 🌿.*\r\nHello.\t ");
            ConversationInputJournalStore.WriteAtomic(file, original);
            Assert.Equal(original, File.ReadAllBytes(file));
            foreach (string stage in new[] { "before_flush", "before_publish" })
            {
                Assert.Throws<IOException>(() => ConversationInputJournalStore.WriteAtomic(file, Encoding.UTF8.GetBytes("partial receipt"),
                    checkpoint => { if (checkpoint == stage) throw new IOException("Injected journal failure"); }));
                Assert.Equal(original, File.ReadAllBytes(file));
                Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
            }
            byte[] receipt = Encoding.UTF8.GetBytes("ack\r\n" + Encoding.UTF8.GetString(original));
            ConversationInputJournalStore.WriteAtomic(file, receipt);
            Assert.Equal(receipt, File.ReadAllBytes(file));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FailedFirstSubmissionCannotAppearAsSaved()
    {
        string directory = Path.Combine(Path.GetTempPath(), "reign-input-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "original.json");
            Assert.Throws<IOException>(() => ConversationInputJournalStore.WriteAtomic(file, Encoding.UTF8.GetBytes("*I reach over.*"),
                stage => { if (stage == "before_publish") throw new IOException("Disk unavailable"); }));
            Assert.False(File.Exists(file));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally { Directory.Delete(directory, true); }
    }
}
