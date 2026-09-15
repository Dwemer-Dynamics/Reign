using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using Reign.Core.Contracts.Platform;

namespace Reign.Mcp.Tests;

public sealed class LaunchInstallationTests
{
    [Fact]
    public void WslClientRecordAllowsOnlyTheSelectedManagedDistroPaths()
    {
        if (!OperatingSystem.IsWindows()) return;
        var record = Record(Path.Combine(Path.GetTempPath(), "Reign WSL contract"));
        record.ServerMode = "dwemerdistro-wsl";
        record.WslDistro = "DwemerAI4Skyrim3";
        record.ServerRoot = @"\\wsl.localhost\DwemerAI4Skyrim3\opt\dwemerdistro\reign\current";
        record.DataRoot = record.ContentRoot = @"\\wsl.localhost\DwemerAI4Skyrim3\var\lib\dwemerdistro\reign";
        record.PostgresPort = 5432;
        record.PostgresBin = "";
        record.Validate();
        string validRoot = record.DataRoot;
        foreach (string invalid in new[] { @"\\remote\share", validRoot + @"\..", validRoot.Replace("DwemerAI4Skyrim3", "OtherDistro") })
        {
            record.DataRoot = invalid;
            Assert.Throws<InvalidDataException>(record.Validate);
        }
        record.DataRoot = validRoot;
        record.WslDistro = "../OtherDistro";
        Assert.Throws<InvalidDataException>(record.Validate);
    }

    [Fact]
    public void ProfileDiscoveryUsesSelectedFoldersWithoutAnEnvironmentOverrideOrLegacyAppData()
    {
        string root = Path.Combine(Path.GetTempPath(), "Reign discovery O'Brien 測試", Guid.NewGuid().ToString("N"));
        try
        {
            string profile = Path.Combine(root, "another Windows user");
            string legacy = Path.Combine(profile, "AppData", "Local", "Bannerlord Reign", "installation.json");
            WriteRecord(legacy, Record(Path.Combine(root, "outdated installation")));
            Assert.Null(ReignInstallation.TryLoad(profile));

            var expected = Record(Path.Combine(root, "selected drive and folders"));
            string current = ReignInstallation.GetDefaultRecordPath(profile);
            Assert.Equal(Path.Combine(profile, ".reign", "installation.json"), current);
            WriteRecord(current, expected);
            var found = Assert.IsType<ReignInstallation>(ReignInstallation.TryLoad(profile));
            Assert.Equal(expected.SharedPortraitRoot, found.SharedPortraitRoot);
            Assert.Equal(expected.PortraitCacheRoot, found.PortraitCacheRoot);
            Assert.Equal(expected.ModuleRoot, found.ModuleRoot);

            // Uninstall must not make an earlier setup's preserved record active.
            File.Move(current, current + ".uninstalled");
            Assert.Null(ReignInstallation.TryLoad(profile));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitRecordWinsAndMissingOrCorruptOverridesNeverFallBack()
    {
        string profile = Path.Combine(Path.GetTempPath(), "Reign discovery overrides", Guid.NewGuid().ToString("N"));
        try
        {
            WriteRecord(ReignInstallation.GetDefaultRecordPath(profile), Record(Path.Combine(profile, "default")));
            string explicitPath = Path.Combine(profile, "custom", "record.json");
            var expected = Record(Path.Combine(profile, "explicit"));
            WriteRecord(explicitPath, expected);
            Assert.Equal(expected.DataRoot, ReignInstallation.TryLoad(profile, explicitPath)!.DataRoot);
            File.Delete(explicitPath);
            Assert.Throws<FileNotFoundException>(() => ReignInstallation.TryLoad(profile, explicitPath));
            File.WriteAllText(explicitPath, "{}");
            Assert.Throws<InvalidDataException>(() => ReignInstallation.TryLoad(profile, explicitPath));
            Assert.Null(ReignInstallation.TryLoad(profile, explicitPath, validationMode: true));
            Assert.Null(ReignInstallation.TryLoad(profile, validationMode: true));
            Assert.Throws<InvalidDataException>(() => ReignInstallation.GetDefaultRecordPath(""));
            Assert.Throws<InvalidDataException>(() => ReignInstallation.GetDefaultRecordPath("relative-profile"));
        }
        finally { if (Directory.Exists(profile)) Directory.Delete(profile, true); }
    }

    private static void WriteRecord(string path, ReignInstallation record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        new DataContractJsonSerializer(typeof(ReignInstallation)).WriteObject(stream, record);
    }

    [Fact]
    public void InstallationRoundTripsWithSpacesAndUnicodeOutsideProgramFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "Reign launch O'Brien 測試", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var record = Record(root);
            var path = Path.Combine(root, "installation.json");
            using (var file = File.Create(path))
                new DataContractJsonSerializer(typeof(ReignInstallation)).WriteObject(file, record);
            var read = ReignInstallation.ReadFrom(path);
            Assert.Equal(record.ServerRoot, read.ServerRoot);
            Assert.Equal(Path.Combine(root, "state", "PortraitCache"), read.PortraitCacheRoot);
            Assert.Equal(Path.Combine(root, "content", "PortraitCache", "_shared"), read.SharedPortraitRoot);
            Assert.False(read.PortraitCacheRoot.StartsWith(read.ServerRoot, StringComparison.OrdinalIgnoreCase));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("protocol")]
    [InlineData("relative")]
    [InlineData("different-game")]
    [InlineData("state-in-program")]
    [InlineData("state-in-module")]
    [InlineData("content-in-program")]
    [InlineData("port")]
    public void InstallationRejectsMismatchedOrUnsafeLayout(string change)
    {
        var record = Record(Path.Combine(Path.GetTempPath(), "Reign isolated layout"));
        switch (change)
        {
            case "schema": record.Schema = "unexpected"; break;
            case "protocol": record.ProtocolVersion++; break;
            case "relative": record.DataRoot = "relative-state"; break;
            case "different-game": record.ModuleRoot = Path.Combine(record.BannerlordRoot, "Modules", "Wrong"); break;
            case "state-in-program": record.DataRoot = Path.Combine(record.ServerRoot, "data"); break;
            case "state-in-module": record.DataRoot = Path.Combine(record.ModuleRoot, "data"); break;
            case "content-in-program": record.ContentRoot = Path.Combine(record.ServerRoot, "content"); break;
            case "port": record.PostgresPort = 0; break;
        }
        Assert.Throws<InvalidDataException>(record.Validate);
    }

    [Fact]
    public void NativeArchiveCommandUsesInstalledToolsAndKeepsPasswordOutOfArguments()
    {
        string root = Path.Combine(Path.GetTempPath(), "Reign tools O'Brien 測試");
        string archive = Path.Combine(root, "campaign backup.dump");
        var command = PostgreSqlTools.CreateCommand(root, "", ["pg_dump", "--file", archive], "fixture-only", root);
        Assert.Equal(Path.Combine(root, "pg_dump.exe"), command.FileName);
        Assert.False(command.UseShellExecute);
        Assert.True(command.CreateNoWindow);
        Assert.DoesNotContain("fixture-only", command.Arguments);
        Assert.Equal("fixture-only", command.EnvironmentVariables["PGPASSWORD"]);
        Assert.Equal(archive, PostgreSqlTools.ArchivePath(archive, native: true));
    }

    [Fact]
    public void LegacyArchiveCommandUsesExplicitExecAndEnvironmentPassword()
    {
        var command = PostgreSqlTools.CreateCommand("", "ExplicitLegacyDistro", ["pg_restore", "/mnt/d/a b.dump"], "fixture-only", Path.GetTempPath());
        Assert.Equal("wsl.exe", command.FileName);
        Assert.Contains("--exec", command.Arguments);
        Assert.DoesNotContain("fixture-only", command.Arguments);
        Assert.Contains("PGPASSWORD/u", command.EnvironmentVariables["WSLENV"]);
        Assert.Throws<ArgumentException>(() => PostgreSqlTools.CreateCommand("", "", ["pg_dump"], "", Path.GetTempPath()));
        Assert.Throws<ArgumentException>(() => PostgreSqlTools.CreateCommand(Path.GetTempPath(), "", ["cmd.exe"], "", Path.GetTempPath()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("with spaces")]
    [InlineData("O'Brien 測試")]
    [InlineData("embedded\"quote")]
    [InlineData("D:\\folder name\\")]
    [InlineData("slashes\\\\\"then quote\\")]
    public void WindowsArgumentsRoundTripThroughNativeParser(string value)
    {
        if (!OperatingSystem.IsWindows()) return;
        nint argv = CommandLineToArgvW("program.exe " + PostgreSqlTools.QuoteWindowsArgument(value), out int count);
        Assert.NotEqual(nint.Zero, argv);
        try
        {
            Assert.Equal(2, count);
            Assert.Equal(value, Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, IntPtr.Size)));
        }
        finally { LocalFree(argv); }
    }

    private static ReignInstallation Record(string root) => new()
    {
        Schema = ReignInstallation.SchemaName,
        Version = "0.1.0-preview.1", ProtocolVersion = 1, ContentVersion = "2026.09.14",
        ServerRoot = Path.Combine(root, "program"), ContentRoot = Path.Combine(root, "content"),
        DataRoot = Path.Combine(root, "state"), BannerlordRoot = Path.Combine(root, "game"),
        ModuleRoot = Path.Combine(root, "game", "Modules", "ReignBeta"),
        PostgresBin = Path.Combine(root, "program", "postgresql", "bin"), PostgresPort = 55432
    };

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(string command, out int count);
    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
