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
