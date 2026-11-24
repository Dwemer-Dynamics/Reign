using AIPortraits;
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
            output.WriteLine("Actual WSL share: complete portrait/custom/source/receipt/derivative replacement, rollback and pending marker replacement passed without a provider or campaign.");
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

            Assert.True(File.Exists(Path.Combine(root, PortraitProductTransaction.RecoveryMarkerName)));
            Assert.False(PortraitProductTransaction.TryRecover(root, RecoverableNames));
            Assert.Equal("unknown backup", File.ReadAllText(custom.BackupPath));
            Assert.Equal("new master", File.ReadAllText(custom.Path));
            Assert.Equal("new receipt", File.ReadAllText(receipt.Path));

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
    private static void AssertNoRecoveryFiles(string root)
    {
        AssertNoTemporaryFiles(root);
        Assert.DoesNotContain(Directory.GetFiles(root), path =>
            Path.GetFileName(path).StartsWith(PortraitProductTransaction.RecoveryMarkerName, StringComparison.Ordinal)
            || path.Contains(".pre.") || path.Contains(".undo."));
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
