using AIPortraits;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;

namespace Reign.Mcp.Tests;

[CollectionDefinition("WSL portrait filesystem", DisableParallelization = true)]
public sealed class WslPortraitFilesystemCollection { }

[Collection("WSL portrait filesystem")]
public sealed class PortraitProductTransactionTests
{
    private readonly ITestOutputHelper output;
    public PortraitProductTransactionTests(ITestOutputHelper output) => this.output = output;

    private static void Unsupported(string source, string destination, string backup) =>
        throw new IOException("The request is not supported.", unchecked((int)0x80070032));

    [Theory]
    [InlineData(@"\\wsl.localhost\ReignServer\tmp\portrait.png", true)]
    [InlineData(@"\\WSL$\ReignServer\tmp\portrait.png", true)]
    [InlineData(@"\\fileserver\share\portrait.png", false)]
    [InlineData(@"\\wsl.localhost.example\share\portrait.png", false)]
    [InlineData(@"C:\portraits\portrait.png", false)]
    public void OnlyKnownWslSharesBypassUnsupportedReplaceFile(string path, bool expected)
        => Assert.Equal(expected, PortraitProductTransaction.UsesWslRename(path));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompleteProductReplacesEveryArtifactAndRemovesBackups(bool unsupported)
    {
        InFixture(Path.GetTempPath(), root => VerifyCompleteProduct(root, unsupported ? Unsupported : null));
    }

    [Fact]
    public void FailedFinalValidationRestoresOriginalsAndRemovesNewFiles()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var old = Stage(root, "portrait.png", "new master", "old master", Unsupported);
            var added = Stage(root, "source.png", "new source", null, Unsupported);
            var receipt = Stage(root, ".ai_generation.json", "new receipt", "old receipt", Unsupported);
            Assert.Throws<InvalidDataException>(() => PortraitProductTransaction.Commit(new[] { old, added, receipt },
                () => throw new InvalidDataException("Invalid final manifest")));
            Assert.Equal("old master", File.ReadAllText(old.Path));
            Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
            Assert.False(File.Exists(added.Path));
            AssertNoTemporaryFiles(root);
        });
    }

    [Fact]
    public void LateCompletionCannotReleaseAReplacedPendingSlot()
    {
        var pending = new System.Collections.Concurrent.ConcurrentDictionary<string, string>();
        const string key = "campaign|character";
        Assert.True(pending.TryAdd(key, "old refresh"));

        // The player clears the local product and starts either another refresh
        // or ordinary generation for the same cache key.
        Assert.True(pending.TryRemove(key, out _));
        Assert.True(pending.TryAdd(key, "new refresh"));
        Assert.False(PortraitProductTransaction.RemovePendingReservation(pending, key, "old refresh"));
        Assert.False(PortraitProductTransaction.RemovePendingReservation(pending, key, string.Empty));
        Assert.Equal("new refresh", pending[key]);

        Assert.True(PortraitProductTransaction.RemovePendingReservation(pending, key, "new refresh"));
        Assert.True(pending.TryAdd(key, string.Empty));
        Assert.False(PortraitProductTransaction.RemovePendingReservation(pending, key, "old refresh"));
        Assert.True(PortraitProductTransaction.RemovePendingReservation(pending, key, string.Empty));
    }

    [Fact]
    public void AbsentPortraitBasisDoesNotConferPendingMarkerOwnership()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var paths = (Master: Path.Combine(root, "portrait.png"), Custom: Path.Combine(root, "custom.png"),
                GenerationReceipt: Path.Combine(root, ".ai_generation.json"), OutfitReceipt: Path.Combine(root, ".outfit_portrait.json"));
            string marker = Path.Combine(root, ".portrait_generation_pending.json");
            var absentBasis = PortraitProductTransaction.ReadPublicationBasis(paths);
            bool OwnsMarker(string text) => text == "original operation";

            Assert.False(PortraitProductTransaction.CurrentFileMatches(marker, OwnsMarker));
            PortraitProductTransaction.WriteAllBytes(marker, "replacement operation"u8.ToArray());
            Assert.Equal(absentBasis, PortraitProductTransaction.ReadPublicationBasis(paths));
            Assert.False(PortraitProductTransaction.CurrentFileMatches(marker, OwnsMarker));

            PortraitProductTransaction.WriteAllBytes(marker, "original operation"u8.ToArray());
            Assert.True(PortraitProductTransaction.CurrentFileMatches(marker, OwnsMarker));
        });
    }

    [Fact]
    public void CompletedLegacyOperationCanRetireAliasButNotANewerMarker()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "Portraits", "AIPortraits", "PortraitCache.cs"));
        Assert.Contains("completed.WireCampaignId == ReignServerClient.GetCampaignId() && completed.WireCacheKey == id", source);
        Assert.Contains("completed.WireOperationId, completed.WireHeroId, allowLegacyIdentity: true", source);
        Assert.Contains("Guid.ParseExact(current, \"N\") == expectedOperation : current == operationId", source);
        Assert.Contains("expectedHero == null || hero == expectedHero", source);
        Assert.Contains("ClearPendingGenerationOperation(cacheKey, OperationId, heroId, directory, folder)", source);

        InFixture(Path.GetTempPath(), root =>
        {
            string path = Path.Combine(root, ".portrait_generation_pending.json");
            object ownerLock = new();
            const string operation = "abcdef0123456789abcdef0123456789";
            const string newer = "fedcba9876543210fedcba9876543210";
            void Write(string guid, string hero)
            {
                var marker = new Newtonsoft.Json.Linq.JObject
                {
                    ["schema"] = "reign-portrait-generation-pending-v1",
                    ["campaignId"] = "LEGACY-FOLDER",
                    ["cacheKey"] = "portrait-key",
                    ["heroStringId"] = hero,
                    ["generationOperationId"] = guid
                };
                lock (ownerLock) File.WriteAllText(path, marker.ToString());
            }
            bool Owns(string text)
            {
                var marker = Newtonsoft.Json.Linq.JObject.Parse(text);
                return marker.Value<string>("schema") == "reign-portrait-generation-pending-v1"
                    && string.Equals(marker.Value<string>("campaignId"), "legacy-folder", StringComparison.OrdinalIgnoreCase)
                    && marker.Value<string>("cacheKey") == "portrait-key"
                    && marker.Value<string>("heroStringId") == "exact-hero"
                    && Guid.TryParseExact(marker.Value<string>("generationOperationId"), "N", out Guid current)
                    && current == Guid.ParseExact(operation, "N");
            }

            Write(newer, "exact-hero");
            byte[] newerMarker = File.ReadAllBytes(path);
            Assert.False(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, Owns));
            Assert.Equal(newerMarker, File.ReadAllBytes(path));

            Write(operation.ToUpperInvariant(), "different-hero");
            Assert.False(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, Owns));
            Assert.True(File.Exists(path));

            Write(operation.ToUpperInvariant(), "exact-hero");
            Assert.True(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, Owns));
            Assert.False(File.Exists(path));
        });
    }

    [Fact]
    public void CompletedOperationRetiresOnlyItsCurrentMarker()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            string path = Path.Combine(root, ".portrait_generation_pending.json");
            object ownerLock = new();
            void Write(string marker)
            {
                lock (ownerLock)
                    PortraitProductTransaction.WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(marker));
            }

            const string completed = "exact campaign hero and completed operation";
            bool IsCompleted(string marker)
            {
                Assert.True(System.Threading.Monitor.IsEntered(ownerLock));
                return marker == completed;
            }

            Write("newer operation");
            Assert.False(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, IsCompleted));
            Assert.Equal("newer operation", File.ReadAllText(path));
            Write("unreadable marker contents");
            Assert.False(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, IsCompleted));
            Assert.Equal("unreadable marker contents", File.ReadAllText(path));

            Write(completed);
            Assert.True(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, IsCompleted));
            Assert.False(File.Exists(path));
            Assert.False(PortraitProductTransaction.DeleteIfCurrent(path, ownerLock, IsCompleted));
        });
    }

    [Fact]
    public void LateRefreshCannotPublishOverSameImageOutfit()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var paths = (Master: Path.Combine(root, "portrait.png"), Custom: Path.Combine(root, "custom.png"),
                GenerationReceipt: Path.Combine(root, ".ai_generation.json"), OutfitReceipt: Path.Combine(root, ".outfit_portrait.json"));
            byte[] image = "same image"u8.ToArray();
            PortraitProductTransaction.WriteAllBytes((paths.Custom, image), (paths.OutfitReceipt, "first operation"u8.ToArray()));
            var before = PortraitProductTransaction.ReadPublicationBasis(paths);

            PortraitProductTransaction.WriteAllBytes((paths.Custom, image), (paths.OutfitReceipt, "later operation"u8.ToArray()));
            int publications = 0;
            void Publish()
            {
                publications++;
                File.WriteAllText(paths.Master, "queued refresh");
            }

            Assert.False(PortraitProductTransaction.PublishIfCurrent(before, paths, Publish));
            Assert.Equal(0, publications);
            Assert.False(File.Exists(paths.Master));
            Assert.Equal("later operation", File.ReadAllText(paths.OutfitReceipt));

            var current = PortraitProductTransaction.ReadPublicationBasis(paths);
            Assert.Equal(before.Custom, current.Custom);
            Assert.True(PortraitProductTransaction.PublishIfCurrent(current, paths, Publish));
            Assert.Equal(1, publications);
            Assert.Equal("queued refresh", File.ReadAllText(paths.Master));
            Assert.False(PortraitProductTransaction.PublishIfCurrent(current, paths, Publish));
        });
    }

    [Fact]
    public void OutfitPairFailureKeepsOldMasterAndCanRetry()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            string master = Path.Combine(root, "custom.png");
            string receipt = Path.Combine(root, ".outfit_portrait.json");
            File.WriteAllText(master, "old image");
            Directory.CreateDirectory(receipt);

            Exception? failure = Record.Exception(() => PortraitProductTransaction.WriteAllBytes(
                (master, "new image"u8.ToArray()), (receipt, "new operation receipt"u8.ToArray())));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
            Assert.Equal("old image", File.ReadAllText(master));
            Assert.True(Directory.Exists(receipt));
            AssertNoTemporaryFiles(root);

            Directory.Delete(receipt);
            PortraitProductTransaction.WriteAllBytes((master, "new image"u8.ToArray()), (receipt, "new operation receipt"u8.ToArray()));
            Assert.Equal("new image", File.ReadAllText(master));
            Assert.Equal("new operation receipt", File.ReadAllText(receipt));
            AssertNoTemporaryFiles(root);
        });
    }

    [Fact]
    public void FailureInstallingFallbackRestoresTheMovedOriginal()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            Action<string, string> move = (source, destination) =>
            {
                if (source.Contains(".tmp.")) throw new IOException("Fixture install failure");
                File.Move(source, destination);
            };
            var file = Stage(root, "portrait.png", "new", "old", Unsupported, move);
            Assert.Throws<IOException>(() => PortraitProductTransaction.Commit(new[] { file }, () => { }));
            Assert.Equal("old", File.ReadAllText(file.Path));
            AssertNoTemporaryFiles(root);
        });
    }

    [Fact]
    public void AccessDeniedDoesNotTryRenameAndRollsBackEarlierFiles()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var first = Stage(root, "portrait.png", "new master", "old master", Unsupported);
            int moves = 0;
            var denied = Stage(root, "receipt.json", "new receipt", "old receipt",
                (a, b, c) => throw new IOException("Access denied", unchecked((int)0x80070005)),
                (a, b) => { moves++; File.Move(a, b); });
            Assert.Throws<IOException>(() => PortraitProductTransaction.Commit(new[] { first, denied }, () => { }));
            Assert.Equal(0, moves);
            Assert.Equal("old master", File.ReadAllText(first.Path));
            Assert.Equal("old receipt", File.ReadAllText(denied.Path));
            AssertNoTemporaryFiles(root);
        });
    }

    [Fact]
    public void FailedRollbackRetainsLastOriginalBackup()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var file = Stage(root, "portrait.png", "new", "old", Unsupported, (source, destination) =>
            {
                if (source.Contains(".bak.")) throw new IOException("Fixture restore failure");
                File.Move(source, destination);
            });
            var failure = Assert.Throws<AggregateException>(() => PortraitProductTransaction.Commit(new[] { file },
                () => throw new InvalidDataException("Fixture validation failure")));
            file.Cleanup(false); // Also exercise the caller's staging cleanup.
            Assert.Equal("old", File.ReadAllText(file.BackupPath));
            Assert.Contains(file.BackupPath, failure.InnerExceptions[1].Message);
            Assert.False(File.Exists(file.TemporaryPath));
        });
    }

    [Fact]
    public void PartialReplaceFailureRestoresBackupWithoutAttemptingFallback()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var file = Stage(root, "portrait.png", "new", "old", (source, destination, backup) =>
            {
                File.Move(destination, backup);
                throw new IOException("The request is not supported.", unchecked((int)0x80070032));
            });
            Assert.Throws<IOException>(() => PortraitProductTransaction.Commit(new[] { file }, () => { }));
            Assert.Equal("old", File.ReadAllText(file.Path));
            AssertNoTemporaryFiles(root);
        });
    }

    [Fact]
    public void ActualWslShareReplacementWhenAvailable()
    {
        const string wslTemp = @"\\wsl.localhost\ReignServer\tmp";
        if (!OperatingSystem.IsWindows() || !Directory.Exists(wslTemp))
        {
            output.WriteLine("WSL filesystem acceptance not run: the selected share is unavailable. Local injected error-50 contracts still run.");
            return;
        }
        InFixture(wslTemp, root =>
        {
            // Use the real filesystem implementation with no injected replacement.
            VerifyCompleteProduct(root, null);
            var file = Stage(root, "rollback.png", "new", "old");
            Assert.Throws<InvalidDataException>(() => PortraitProductTransaction.Commit(new[] { file },
                () => throw new InvalidDataException("Fixture manifest failure")));
            Assert.Equal("old", File.ReadAllText(file.Path));
            string pending = Path.Combine(root, ".portrait_generation_pending.json");
            PortraitProductTransaction.WriteAllBytes(pending, "first"u8.ToArray());
            PortraitProductTransaction.WriteAllBytes(pending, "updated"u8.ToArray());
            Assert.Equal("updated", File.ReadAllText(pending));
            AssertNoTemporaryFiles(root);
            string custom = Path.Combine(root, "custom.png");
            string receipt = Path.Combine(root, ".outfit_portrait.json");
            PortraitProductTransaction.WriteRecoverable(root, RecoverableNames,
                (custom, "committed WSL master"u8.ToArray()), (receipt, "committed WSL receipt"u8.ToArray()));
            Assert.Equal("committed WSL master", File.ReadAllText(custom));
            Assert.Equal("committed WSL receipt", File.ReadAllText(receipt));
            AssertNoRecoveryFiles(root);

            var nextCustom = Stage(root, "custom.png", "uncommitted WSL master", null);
            var nextReceipt = Stage(root, ".outfit_portrait.json", "uncommitted WSL receipt", null);
            Assert.Throws<InvalidDataException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { nextCustom, nextReceipt }, RecoverableNames, () =>
                {
                    Assert.True(PortraitProductTransaction.HasRecoveryJournal(root));
                    Assert.Equal("uncommitted WSL master", File.ReadAllText(custom));
                    Assert.Equal("uncommitted WSL receipt", File.ReadAllText(receipt));
                    throw new InvalidDataException("Injected failure before commitment.");
                }));
            Assert.Equal("committed WSL master", File.ReadAllText(custom));
            Assert.Equal("committed WSL receipt", File.ReadAllText(receipt));
            Assert.False(PortraitProductTransaction.HasRecoveryJournal(root));
            AssertNoRecoveryFiles(root);
            output.WriteLine("Actual WSL share: journalled portrait publication and rollback used native calls on an isolated directory.");
        });
    }

    private static readonly string[] RecoverableNames = { "custom.png", ".outfit_portrait.json", "portrait.png" };

    [Fact]
    public void RecoverableOutfitPublishesBothMembersAndRetiresItsJournal()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            string custom = Path.Combine(root, "custom.png");
            string receipt = Path.Combine(root, ".outfit_portrait.json");
            File.WriteAllText(custom, "old master");
            PortraitProductTransaction.WriteRecoverable(root, RecoverableNames,
                (custom, "new master"u8.ToArray()), (receipt, "new receipt"u8.ToArray()));

            Assert.Equal("new master", File.ReadAllText(custom));
            Assert.Equal("new receipt", File.ReadAllText(receipt));
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            AssertNoRecoveryFiles(root);
        });
    }

    [Fact]
    public void PreparedRecoveryRestoresAnInterruptedRenameAndRemovesExactlyItsNewFile()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var custom = Stage(root, "custom.png", "new master", null);
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt",
                (source, destination, backup) =>
                {
                    File.Move(destination, backup);
                    throw new IOException("Interrupted replacement.");
                });
            File.WriteAllText(Path.Combine(root, "neighbor.txt"), "keep");

            Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { custom, receipt }, RecoverableNames, () => { }));
            Assert.False(File.Exists(custom.Path));
            Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(root, "neighbor.txt")));
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            AssertNoRecoveryFiles(root);
        });
    }

    [Fact]
    public void RecoveryLeavesUnknownBytesAndBlocksUntilExactOwnershipCanBeProved()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var custom = Stage(root, "custom.png", "new master", "old master");
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt",
                (source, destination, backup) =>
                {
                    File.WriteAllText(destination, "other writer");
                    throw new IOException("Replacement did not establish ownership.");
                });
            Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { custom, receipt }, RecoverableNames, () => { }));
            receipt.Cleanup(false); // A refresh caller also cleans staging in its finally.
            Assert.True(File.Exists(receipt.TemporaryPath));
            Assert.True(File.Exists(Path.Combine(root, PortraitProductTransaction.RecoveryMarkerName)));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("other writer", File.ReadAllText(receipt.Path));
            Assert.Equal("new master", File.ReadAllText(custom.Path));
            Assert.Equal("old master", File.ReadAllText(custom.BackupPath));

            File.WriteAllText(receipt.Path, "old receipt");
            Assert.False(PortraitProductTransaction.TryRecover(root, new[] { "custom.png" }));
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("old master", File.ReadAllText(custom.Path));
            Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
            AssertNoRecoveryFiles(root);
        });
    }

    [Fact]
    public void RetriedCompensationKeepsThePreparedJournalAndRestoreImageUntilSuccess()
    {
        if (!OperatingSystem.IsWindows()) return;
        InFixture(Path.GetTempPath(), root =>
        {
            var custom = Stage(root, "custom.png", "new master", "old master");
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt");
            FileStream? blocked = null;
            try
            {
                Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                    new[] { custom, receipt }, RecoverableNames, () =>
                    {
                        blocked = File.Open(custom.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                        throw new InvalidDataException("Injected validation failure.");
                    }));
                Assert.True(File.Exists(Path.Combine(root, PortraitProductTransaction.RecoveryMarkerName)));
                Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
                Assert.Equal("old master", File.ReadAllText(custom.BackupPath));
                Assert.Equal("new master", File.ReadAllText(custom.Path));
            }
            finally { blocked?.Dispose(); }

            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("old master", File.ReadAllText(custom.Path));
            Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
            AssertNoRecoveryFiles(root);
        });
    }

    [Fact]
    public void CommittedRecoveryRetiresEvidenceButNeverRevertsAcceptedFiles()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var custom = Stage(root, "custom.png", "new master", "old master");
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt");
            Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { custom, receipt }, RecoverableNames, () => File.WriteAllText(custom.BackupPath, "unknown backup")));

            string preparedMarker = Path.Combine(root, PortraitProductTransaction.RecoveryMarkerName);
            string committedMarker = Path.Combine(root, PortraitProductTransaction.CommittedRecoveryMarkerName);
            Assert.False(File.Exists(preparedMarker));
            Assert.True(File.Exists(committedMarker));
            Assert.True(PortraitProductTransaction.HasRecoveryJournal(root));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("unknown backup", File.ReadAllText(custom.BackupPath));

            File.Copy(committedMarker, preparedMarker);
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal(File.ReadAllBytes(committedMarker), File.ReadAllBytes(preparedMarker));
            File.Delete(preparedMarker);
            Assert.Equal("new master", File.ReadAllText(custom.Path));
            Assert.Equal("new receipt", File.ReadAllText(receipt.Path));

            // One per-file cleanup may already have finished before a restart.
            File.Delete(receipt.BackupPath);
            File.WriteAllText(custom.BackupPath, "old master");
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("new master", File.ReadAllText(custom.Path));
            Assert.Equal("new receipt", File.ReadAllText(receipt.Path));
            AssertNoRecoveryFiles(root);
        });
    }

    [Fact]
    public void UnknownRecoveryMarkerIsPreserved()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            string marker = Path.Combine(root, PortraitProductTransaction.RecoveryMarkerName);
            Assert.False(PortraitProductTransaction.HasRecoveryJournal(root));
            File.WriteAllText(marker, "unknown marker");
            Assert.True(PortraitProductTransaction.HasRecoveryJournal(root));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("unknown marker", File.ReadAllText(marker));
            File.Delete(marker);
            Directory.CreateDirectory(marker);
            Assert.True(PortraitProductTransaction.HasRecoveryJournal(root));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.True(Directory.Exists(marker));
        });
    }

    [Fact]
    public void PublicationStopsBeforeOverwritingAnotherWritersLaterFile()
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt");
            var custom = Stage(root, "custom.png", "new master", "old master",
                (source, destination, backup) =>
                {
                    File.Replace(source, destination, backup);
                    File.WriteAllText(receipt.Path, "a later receipt");
                });
            Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { custom, receipt }, RecoverableNames, () => { }));

            Assert.Equal("a later receipt", File.ReadAllText(receipt.Path));
            Assert.Equal("new receipt", File.ReadAllText(receipt.TemporaryPath));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            File.WriteAllText(receipt.Path, "old receipt");
            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("old master", File.ReadAllText(custom.Path));
            AssertNoRecoveryFiles(root);
        });
    }
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void WslStyleRestoreResumesAfterEachRenameBoundary(int faultAfter)
    {
        InFixture(Path.GetTempPath(), root =>
        {
            var custom = Stage(root, "custom.png", "new master", "old master");
            var receipt = Stage(root, ".outfit_portrait.json", "new receipt", "old receipt",
                (source, destination, backup) =>
                {
                    File.WriteAllText(destination, "unowned until repaired");
                    throw new IOException("Interrupted publication.");
                });
            Assert.Throws<IOException>(() => PortraitProductTransaction.CommitRecoverable(root,
                new[] { custom, receipt }, RecoverableNames, () => { }));
            File.WriteAllText(receipt.Path, "old receipt");
            int renames = 0;
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames, (source, destination) =>
            {
                File.Move(source, destination);
                if (++renames == faultAfter) throw new IOException("Interrupted after the rename.");
            }));
            Assert.Equal(faultAfter, renames);
            Assert.True(PortraitProductTransaction.HasRecoveryJournal(root));
            string displaced = Assert.Single(Directory.GetFiles(root, "custom.png.post.*"));
            Assert.Equal("new master", File.ReadAllText(displaced));

            if (faultAfter == 1)
            {
                Assert.False(File.Exists(custom.Path));
                // Absence after moving the after-image is meaningful only
                // when the known displaced image (or original forward backup) remains.
                File.Delete(custom.BackupPath);
                File.WriteAllText(displaced, "unowned image");
                Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames, File.Move));
                Assert.False(File.Exists(custom.Path));
                Assert.Equal("unowned image", File.ReadAllText(displaced));
                File.WriteAllText(displaced, "new master");
            }
            else Assert.Equal("old master", File.ReadAllText(custom.Path));

            Assert.True(PortraitProductTransaction.TryRecover(root, RecoverableNames, File.Move));
            Assert.Equal("old master", File.ReadAllText(custom.Path));
            Assert.Equal("old receipt", File.ReadAllText(receipt.Path));
            AssertNoRecoveryFiles(root);
        });
    }
    [ValidatedClientFact]
    public async Task ConfirmedOutfitDerivativesSettleBothSidesOfTheCommitAcrossClientProcesses()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string workspace = Path.GetFullPath(TestOptions.FindWorkspace());
        string id = "reign-d49-" + Guid.NewGuid().ToString("N");
        string root = Path.Combine(workspace, ".codex-build", id);
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName,
            "portrait-derivative-restart", id);
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(evidence);
        bool recoveryFailed = false;
        try
        {
            var receipts = new List<object>();
            foreach (bool committed in new[] { false, true })
            {
                string phase = committed ? "committed" : "prepared";
                string fixture = Path.Combine(root, phase);
                string hero = Path.Combine(fixture, "hero");
                string shared = Path.Combine(fixture, "shared");
                string other = Path.Combine(fixture, "other-save");
                foreach (string directory in new[] { hero, shared, other }) Directory.CreateDirectory(directory);

                string source = Path.Combine(hero, "custom.png");
                string outfit = Path.Combine(hero, ".outfit_portrait.json");
                string baseMaster = Path.Combine(hero, "portrait.png");
                string baseReceipt = Path.Combine(hero, ".ai_generation.json");
                string sharedFile = Path.Combine(shared, "portrait.png");
                string otherFile = Path.Combine(other, "custom.png");
                File.WriteAllBytes(sharedFile, DerivativeFixturePng(30));
                File.WriteAllBytes(otherFile, DerivativeFixturePng(60));

                WriteDerivativeFixture(baseMaster, baseReceipt, 90, id + "-old");
                await RunDerivativeChild(workspace, client, root, hero, "publish", 0);
                string[] outputs = { "thumbnail.png", "thumbnail_wide.png", "portrait_chest.png",
                    "zoom.png", ".portrait_derivatives.json" };
                string[] paths = outputs.Select(name => Path.Combine(hero, name)).ToArray();
                Assert.All(paths, path => Assert.True(File.Exists(path), path));
                byte[] oldWide = File.ReadAllBytes(paths[1]);
                File.Delete(paths[1]);
                string?[] before = paths.Select(HashIfPresent).ToArray();

                WriteDerivativeFixture(source, outfit, 180, id + "-new");
                string[] retained = { source, outfit, baseMaster, baseReceipt, sharedFile, otherFile };
                string?[] retainedHashes = retained.Select(HashIfPresent).ToArray();
                Assert.All(retainedHashes, hash => Assert.NotNull(hash));

                await RunDerivativeChild(workspace, client, root, hero, phase, committed ? 87 : 86);
                string marker = Path.Combine(hero, committed ? PortraitProductTransaction.CommittedRecoveryMarkerName
                    : PortraitProductTransaction.RecoveryMarkerName);
                Assert.True(File.Exists(marker));
                Assert.False(File.Exists(Path.Combine(hero, committed ? PortraitProductTransaction.RecoveryMarkerName
                    : PortraitProductTransaction.CommittedRecoveryMarkerName)));
                string?[] attempted = paths.Select(HashIfPresent).ToArray();
                Assert.All(attempted, hash => Assert.NotNull(hash));
                Assert.All(Enumerable.Range(0, paths.Length), index => Assert.NotEqual(before[index], attempted[index]));
                Assert.Equal(retainedHashes, retained.Select(HashIfPresent).ToArray());
                await RunDerivativeChild(workspace, client, root, hero, "inspect-held", 0);

                await RecoverWithFailureEvidence("initial");
                string?[] settled = paths.Select(HashIfPresent).ToArray();
                Assert.Equal(committed ? attempted : before, settled);
                Assert.Equal(retainedHashes, retained.Select(HashIfPresent).ToArray());
                AssertNoRecoveryFiles(hero);
                if (committed) await RunDerivativeChild(workspace, client, root, hero, "inspect", 0);

                await RecoverWithFailureEvidence("repeat");
                Assert.Equal(settled, paths.Select(HashIfPresent).ToArray());
                Assert.Equal(retainedHashes, retained.Select(HashIfPresent).ToArray());
                AssertNoRecoveryFiles(hero);

                if (!committed)
                {
                    byte[] oldReceipt = File.ReadAllBytes(outfit);
                    await RunDerivativeChild(workspace, client, root, hero, "changed-receipt", 88);
                    Assert.Equal(before, paths.Select(HashIfPresent).ToArray());
                    Assert.Equal(retainedHashes[0], HashIfPresent(source));
                    Assert.Equal(oldReceipt.Concat(System.Text.Encoding.UTF8.GetBytes(Environment.NewLine)).ToArray(),
                        File.ReadAllBytes(outfit));
                    Assert.Equal(retainedHashes.Skip(2).ToArray(), retained.Skip(2).Select(HashIfPresent).ToArray());
                    AssertNoRecoveryFiles(hero);

                    // Restore the known missing fixture file to prove that an
                    // otherwise valid old base manifest cannot hide the outfit.
                    File.WriteAllBytes(paths[1], oldWide);
                    await RunDerivativeChild(workspace, client, root, hero, "inspect-outfit-source", 0);
                    await RunDerivativeChild(workspace, client, root, hero, "publish-outfit", 0);
                    await RunDerivativeChild(workspace, client, root, hero, "inspect", 0);
                    Assert.Equal(attempted, paths.Select(HashIfPresent).ToArray());
                    Assert.Equal(retainedHashes[0], HashIfPresent(source));
                    Assert.Equal(retainedHashes.Skip(2).ToArray(), retained.Skip(2).Select(HashIfPresent).ToArray());
                    AssertNoRecoveryFiles(hero);
                }
                receipts.Add(new { phase, outputs, before, attempted, settled, retainedHashes });

                async Task RecoverWithFailureEvidence(string attempt)
                {
                    try { await RunDerivativeChild(workspace, client, root, hero, "recover", 0); }
                    catch
                    {
                        recoveryFailed = true;
                        try { WriteDerivativeRecoveryFailure(root, hero, evidence, phase, attempt, outputs); }
                        catch { /* Preserve the original recovery failure if diagnostics cannot be written. */ }
                        throw;
                    }
                }
            }
            File.WriteAllText(Path.Combine(evidence, "restart-evidence.json"), JsonSerializer.Serialize(new
            {
                clientSha256 = HashIfPresent(client),
                cases = receipts
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            try
            {
                string generated = Path.GetFullPath(Path.Combine(workspace, ".codex-build"));
                if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(root)), generated, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetFileName(root), id, StringComparison.Ordinal)
                    || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Portrait fixture cleanup escaped its owned directory.");
                Directory.Delete(root, recursive: true);
            }
            catch when (recoveryFailed) { /* Keep the original recovery error if the fixture cannot be removed. */ }
        }
    }

    private static void WriteDerivativeRecoveryFailure(string root, string hero, string evidence,
        string phase, string attempt, string[] outputs)
    {
        const int entryLimit = 64, hashByteLimit = 8 * 1024 * 1024;
        const FileAttributes directoryBits = FileAttributes.Directory | FileAttributes.ReparsePoint;
        foreach (string directory in new[] { Path.GetDirectoryName(root)!, root, Path.GetDirectoryName(hero)!, hero, evidence })
            if ((File.GetAttributes(directory) & directoryBits) != FileAttributes.Directory)
                throw new IOException("Derivative failure evidence requires ordinary fixture directories.");

        string[] markers = { PortraitProductTransaction.RecoveryMarkerName, PortraitProductTransaction.CommittedRecoveryMarkerName };
        string[] expected = outputs.Concat(markers).ToArray();
        string[] observed = Directory.EnumerateFileSystemEntries(hero).Take(entryLimit + 1)
            .Select(path => Path.GetFileName(path)).ToArray();
        bool truncated = observed.Length > entryLimit;
        var files = new List<object>();
        foreach (string name in observed.Take(entryLimit).OrderBy(name => name, StringComparer.Ordinal))
        {
            string? kind = ArtifactKind(name);
            if (kind == null) continue;
            string relativePath = $"{phase}/hero/{name}";
            try
            {
                string file = Path.Combine(hero, name);
                FileAttributes attributes = File.GetAttributes(file);
                if ((attributes & directoryBits) != 0)
                {
                    files.Add(new { relativePath, kind, attributes = attributes.ToString(), status = "not_regular" });
                    continue;
                }
                using var stream = File.OpenRead(file);
                long bytes = stream.Length;
                if (bytes > hashByteLimit)
                    files.Add(new { relativePath, kind, bytes, status = "size_limit" });
                else
                    files.Add(new { relativePath, kind, bytes, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() });
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                files.Add(new { relativePath, kind, status = "unreadable", error = error.GetType().Name, hresult = error.HResult });
            }
        }
        if (!truncated)
            foreach (string name in expected.Where(name => !observed.Contains(name, StringComparer.Ordinal)))
                files.Add(new { relativePath = $"{phase}/hero/{name}", kind = ArtifactKind(name), status = "missing" });

        using var output = new FileStream(Path.Combine(evidence, "restart-failure.json"), FileMode.CreateNew);
        JsonSerializer.Serialize(output, new { phase, attempt, inventoryTruncated = truncated, files },
            new JsonSerializerOptions { WriteIndented = true });

        string? ArtifactKind(string name)
        {
            if (markers.Contains(name, StringComparer.OrdinalIgnoreCase)) return "marker";
            string markerPrefix = markers[0] + ".";
            const string prepareSuffix = ".prepare";
            if (name.Length == markerPrefix.Length + 32 + prepareSuffix.Length
                && name.StartsWith(markerPrefix, StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(prepareSuffix, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(name.Substring(markerPrefix.Length, 32), "N", out _)) return "staged_marker";
            foreach (string target in outputs)
            {
                if (name.Equals(target, StringComparison.OrdinalIgnoreCase)) return "current";
                string prefix = target + ".";
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string[] suffix = name[prefix.Length..].Split('.');
                if (suffix.Length != 2 || suffix[1].Length != 32 || !Guid.TryParseExact(suffix[1], "N", out _)) continue;
                return suffix[0].ToLowerInvariant() switch
                {
                    "pre" => "preimage",
                    "bak" => "backup",
                    "tmp" => "staged",
                    "undo" => "restore_image",
                    "post" => "displaced_image",
                    _ => null,
                };
            }
            return null;
        }
    }

    private static string? HashIfPresent(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteDerivativeFixture(string source, string receipt, byte color, string id)
    {
        File.WriteAllBytes(source, DerivativeFixturePng(color));
        File.SetLastWriteTimeUtc(source, new DateTime(2020, 1, 1, 0, color / 6, 0, DateTimeKind.Utc));
        File.WriteAllText(receipt, JsonSerializer.Serialize(new
        {
            schema = "reign-portrait-product-v1",
            accepted = true,
            reignOutfitOperationId = id,
            master = new { sha256 = HashIfPresent(source) },
            focus = new
            {
                method = "fixture_detected_face", model = "fixture", confidence = 0.99, candidateCount = 1,
                x = 0.4, y = 0.08, width = 0.2, height = 0.14
            }
        }));
    }

    private static byte[] DerivativeFixturePng(byte color)
    {
        const int width = 768, height = 1024;
        byte[] pixels = new byte[width * height * 4];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color;
            pixels[index + 1] = (byte)(color / 2);
            pixels[index + 2] = (byte)(color / 3);
            pixels[index + 3] = 255;
        }
        return PngEncoder.EncodeRgba(pixels, width, height);
    }

    private static async Task RunDerivativeChild(string workspace, string client, string root, string hero,
        string phase, int expectedExit)
    {
        string shell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shell, "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = root,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        start.Environment["PSModulePath"] = Path.Combine(shell, "Modules");
        start.Environment["REIGN_VALIDATION_MODE"] = "1";
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(workspace, "ReignMcp/tests/Reign.Mcp.Tests/Platform/PortraitDerivativeRestartProbe.ps1"),
            "-Workspace", workspace, "-ClientAssembly", client, "-FixtureRoot", root, "-HeroDirectory", hero,
            "-Phase", phase, "-BannerlordPath", Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")! })
            start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start portrait restart probe.");
        child.StandardInput.Close();
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            child.Kill(true);
            await child.WaitForExitAsync();
            throw new TimeoutException("The isolated portrait restart child did not exit in phase " + phase);
        }
        string diagnostic = (await stdout) + (await stderr);
        Assert.True(child.ExitCode == expectedExit, $"{phase} returned {child.ExitCode}, expected {expectedExit}. {diagnostic}");
    }

    private sealed class ValidatedClientFactAttribute : FactAttribute
    {
        public ValidatedClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires same-run canonical Windows client artifact and licensed game references.";
        }
    }

    private static void AssertNoRecoveryFiles(string root)
    {
        AssertNoTemporaryFiles(root);
        Assert.DoesNotContain(Directory.GetFiles(root), path =>
            Path.GetFileName(path).StartsWith(PortraitProductTransaction.RecoveryMarkerName, StringComparison.Ordinal)
            || path.Contains(".pre.") || path.Contains(".undo.") || path.Contains(".post."));
    }
    private static void VerifyCompleteProduct(string root, Action<string, string, string>? replace)
    {
        string[] names = { "portrait.png", "custom.png", "source.png", "prompt.txt", "portrait_input.json",
            ".ai_generation.json", ".outfit_portrait.json", "thumbnail.png", "thumbnail_wide.png", "portrait_chest.png", "zoom.png", ".portrait_derivatives.json" };
        var files = names.Select(name => Stage(root, name, "new " + name, "old " + name, replace)).ToArray();
        PortraitProductTransaction.Commit(files, () =>
        {
            foreach (var file in files) Assert.Equal("new " + Path.GetFileName(file.Path), File.ReadAllText(file.Path));
        });
        AssertNoTemporaryFiles(root);
    }

    private static PortraitProductTransaction.FileCommit Stage(string root, string name, string staged,
        string? original, Action<string, string, string>? replace = null, Action<string, string>? move = null)
    {
        var file = new PortraitProductTransaction.FileCommit(Path.Combine(root, name), Guid.NewGuid().ToString("N"), replace, move);
        if (original != null) File.WriteAllText(file.Path, original);
        File.WriteAllText(file.TemporaryPath, staged);
        return file;
    }

    private static void AssertNoTemporaryFiles(string root) =>
        Assert.DoesNotContain(Directory.GetFiles(root), path => path.Contains(".tmp.") || path.Contains(".bak."));

    private static void InFixture(string parent, Action<string> action)
    {
        string root = Path.Combine(parent, "reign-portrait-commit-test-" + Guid.NewGuid().ToString("N"));
        Assert.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            Path.GetFullPath(root));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally
        {
            // WSL UNC deletion can report missing after removing the directory.
            // Accept only verified absence; never hide a remaining fixture or
            // replace an assertion failure from the test body with cleanup noise.
            try { Directory.Delete(root, true); }
            catch (DirectoryNotFoundException) when (!Directory.Exists(root)) { }
        } // Only this fresh, boundary-checked test-owned directory.
    }
}
