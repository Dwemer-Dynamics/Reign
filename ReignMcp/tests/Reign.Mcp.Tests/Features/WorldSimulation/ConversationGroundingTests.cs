using System.Text;
using Newtonsoft.Json.Linq;
using ReignBeta.Runtime;
using Reign.Core.Contracts.Dialogue;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ConversationGroundingTests
{
    private static JObject Draft(string id, string session = "old-session", string text = "  *I nod.*\r\nMorning. 🌿 ") => new()
    {
        ["schema"] = "reign-client-input-v1", ["campaignId"] = "campaign", ["timelineId"] = "timeline",
        ["playerId"] = "player", ["heroId"] = "rhagaea", ["sessionId"] = session,
        ["contributionId"] = id, ["text"] = text, ["sourceHash"] = ReignConversationInputJournal.Hash(text),
        ["savedUtc"] = "2026-10-22T20:08:22Z"
    };

    private static void SaveDraft(string directory, JObject row) => File.WriteAllText(
        Path.Combine(directory, ReignConversationInputJournal.Hash(row.Value<string>("contributionId")) + ".json"), row.ToString());

    private static void InJournal(Action<string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "reign-recovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { check(directory); } finally { Directory.Delete(directory, true); }
    }

    private static JArray Pending(string directory) => ReignConversationInputJournal.ReadRecoverableDirectory(
        directory, "campaign", "timeline", "player", "rhagaea");

    [Fact]
    public void EarlierGreetingAndUnboundInputNeverBecomePlayerTurnsUnderAnApproach()
    {
        InJournal(directory =>
        {
            SaveDraft(directory, Draft("old")); SaveDraft(directory, Draft("unbound", ""));
            var lines = new JArray(new JObject { ["role"] = "npc", ["text"] = "A separate approach." });
            var drafts = Pending(directory);
            Assert.Equal(2, drafts.Count);
            ReignConversationInputJournal.AppendPendingRows(lines, drafts, "new-session");
            ReignConversationInputJournal.AppendPendingRows(lines, drafts, "");
            Assert.Single(lines); // Both originals remain available for explicit draft recovery.
            ReignConversationInputJournal.AppendPendingRows(lines, drafts, "old-session");
            Assert.Equal(2, lines.Count);
            Assert.Equal("system", lines[1].Value<string>("role"));
            Assert.DoesNotContain(lines, line => line.Value<string>("role") == "player");
        });
    }

    [Theory]
    [InlineData("campaignId", "other")]
    [InlineData("timelineId", "other")]
    [InlineData("playerId", "other")]
    [InlineData("heroId", "ira")]
    [InlineData("sourceHash", "bad")]
    public void WrongScopeAndDamagedRowsCannotBeRecovered(string field, string value)
    {
        InJournal(directory =>
        {
            var row = Draft("bad"); row[field] = value; SaveDraft(directory, row);
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{partial");
            SaveDraft(directory, Draft("good"));
            Assert.Equal("good", Assert.Single(Pending(directory)).Value<string>("contributionId"));
            Assert.Equal(3, Directory.GetFiles(directory).Length);
        });
    }

    [Fact]
    public void GroupPendingRecoveryRequiresExactSessionAndDoesNotTreatMissingHeroAsWildcard()
    {
        InJournal(directory =>
        {
            SaveDraft(directory, Draft("group", "party-session")); SaveDraft(directory, Draft("other", "other-party-session"));
            Assert.Empty(ReignConversationInputJournal.ReadRecoverableDirectory(directory, "campaign", "timeline", "player", ""));
            var same = ReignConversationInputJournal.ReadRecoverableDirectory(directory, "campaign", "timeline", "player", "", "party-session");
            Assert.Equal("group", Assert.Single(same).Value<string>("contributionId"));
        });
    }

    [Fact]
    public void MalformedReceiptCannotHideAnOriginalOrBreakRecovery()
    {
        InJournal(directory =>
        {
            var row = Draft("unconfirmed");
            row["receipt"] = new JObject { ["persisted"] = "not a boolean", ["sourceHash"] = new JArray("bad"), ["contributionId"] = "unconfirmed" };
            row["acceptedHistory"] = new JObject { ["turnId"] = new JArray("bad") };
            SaveDraft(directory, row);
            Assert.Single(Pending(directory));
        });
    }

    [Fact]
    public void MalformedSessionMetadataDoesNotBreakOtherDraftsOrDeferFreshApproaches()
    {
        InJournal(directory =>
        {
            var broken = Draft("broken"); broken["sessionId"] = new JObject(); SaveDraft(directory, broken);
            SaveDraft(directory, Draft("older"));
            var pending = Pending(directory);
            Assert.Equal("older", Assert.Single(pending).Value<string>("contributionId"));
            Assert.False(ReignRecoveredDraftReview.HasPendingInSession(pending, "new-session"));
            Assert.False(ReignRecoveredDraftReview.HasPendingInSession(pending, ""));
            Assert.True(ReignRecoveredDraftReview.HasPendingInSession(pending, "old-session"));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
        });
    }

    [Fact]
    public void ReceiptWriteFailuresPreserveOriginalWithoutConvertingAcceptedDeliveryIntoSendFailure()
    {
        InJournal(directory =>
        {
            var original = Draft("original"); SaveDraft(directory, original);
            string file = Directory.GetFiles(directory).Single(); byte[] before = File.ReadAllBytes(file);
            var receipt = new JObject { ["persisted"] = true, ["contributionId"] = "original", ["sourceHash"] = "wrong" };
            Assert.NotEmpty(ReignConversationInputJournal.AcknowledgeDirectory(directory, "original", receipt));
            Assert.Equal(before, File.ReadAllBytes(file));
            receipt["sourceHash"] = original["sourceHash"]!.DeepClone();
            Assert.Empty(ReignConversationInputJournal.AcknowledgeDirectory(directory, "original", receipt));
            Assert.Empty(Pending(directory));
            File.WriteAllText(file, "{partial");
            Assert.NotEmpty(ReignConversationInputJournal.AcknowledgeDirectory(directory, "original", receipt));
            Assert.Equal("{partial", File.ReadAllText(file));
        });
    }

    [Fact]
    public void RecoveryRestoresExactTextWithoutOverwritingDraftOrAllowingDoubleClickSend()
    {
        var state = new ReignRecoveredDraftReview(); var row = Draft("original"); var now = DateTime.UtcNow;
        Assert.Null(state.Restore(row, false, now));
        Assert.Equal(row.Value<string>("text"), state.Restore(row, true, now));
        Assert.False(state.CanSubmit(now.AddMilliseconds(100)));
        Assert.True(state.CanSubmit(now.AddSeconds(2)));
        Assert.NotNull(state.SelectedDraft);
        state.InputChanged("An edit of the recovered draft.");
        Assert.NotNull(state.SelectedDraft);
        state.InputChanged(""); state.InputChanged("An unrelated new message.");
        Assert.Null(state.SelectedDraft);
        row["text"] = "changed without hash";
        Assert.Null(state.Restore(row, true, now));
    }

    [Fact]
    public void LostReceiptOnlyReconcilesExactAcceptedOriginalInItsOwnSession()
    {
        InJournal(directory =>
        {
            var row = Draft("original"); SaveDraft(directory, row);
            var accepted = new JObject { ["role"] = "player", ["contributionId"] = "original", ["text"] = row["text"]!.DeepClone(),
                ["sourceIntegrity"] = "original_verified", ["sessionId"] = "new-session", ["turnId"] = "accepted-turn" };
            void Reconcile() => ReignConversationInputJournal.ReconcileAcceptedHistoryDirectory(directory, new JArray(accepted), "campaign", "timeline", "player", "rhagaea");
            Reconcile(); Assert.Single(Pending(directory));
            accepted["sessionId"] = "old-session"; accepted["deliveryStatus"] = "submitted";
            Reconcile(); Assert.Single(Pending(directory));
            accepted.Remove("deliveryStatus"); accepted["text"] = "Different text";
            Reconcile(); Assert.Single(Pending(directory));
            accepted["text"] = row["text"]!.DeepClone(); Reconcile();
            Assert.Empty(Pending(directory));
            var saved = JObject.Parse(File.ReadAllText(Directory.GetFiles(directory).Single()));
            Assert.Equal(row.Value<string>("text"), saved.Value<string>("text"));
            Assert.Null(saved["receipt"]); // A history reference never invents an original ack.
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNewSubmissionOnlyResolvesOldDraftAfterMatchingPersistence(bool edited)
    {
        InJournal(directory =>
        {
            var original = Draft("old"); var next = edited ? Draft("new", "new-session", "An edited draft.") : Draft("new", "new-session");
            SaveDraft(directory, original); SaveDraft(directory, next);
            void Resolve() => ReignConversationInputJournal.ResolveReintroducedDraftDirectory(directory, original, "new");
            Resolve(); Assert.Equal(2, Pending(directory).Count);
            next["receipt"] = new JObject { ["persisted"] = true, ["contributionId"] = "new", ["sourceHash"] = "wrong" };
            SaveDraft(directory, next); Resolve(); Assert.Equal(2, Pending(directory).Count);
            next["receipt"]!["sourceHash"] = next["sourceHash"]!.DeepClone(); SaveDraft(directory, next); Resolve();
            Assert.Empty(Pending(directory));
            var saved = JObject.Parse(File.ReadAllText(Path.Combine(directory, ReignConversationInputJournal.Hash("old") + ".json")));
            Assert.Equal("old", saved.Value<string>("contributionId")); Assert.Null(saved["receipt"]);
            Assert.Equal(original.Value<string>("text"), saved.Value<string>("text"));
            Assert.Equal("new", saved.Value<string>("reintroducedAs"));
        });
    }

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
