using System.Diagnostics;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignSourceLayoutTests
{
    [Fact]
    public void MissingManifestKeepsOrdinaryWorkspaceBehavior()
    {
        var layout = new ReignSourceLayout(Path.GetTempPath());
        Assert.False(layout.IsPaired);
    }

    [Fact]
    public void DeclaredSourceLinksResolveOnlyIntoTheExactSiblingCheckout()
    {
        using var fixture = new Fixture();
        fixture.LinkAll();
        var layout = new ReignSourceLayout(fixture.Client);
        layout.ValidateProjections();
        Assert.True(layout.IsPaired);
        Assert.Equal(fixture.Server, layout.ServerRoot);
        foreach (string name in ReignSourceLayout.ServerDirectories)
            Assert.True(layout.CanTraverse(new DirectoryInfo(Path.Combine(fixture.Client, name))));
        var arbitrary = Path.Combine(fixture.Client, "unregistered-link");
        fixture.Link(arbitrary, Path.Combine(fixture.Server, "ReignModules"));
        Assert.False(layout.CanTraverse(new DirectoryInfo(arbitrary)));
    }

    [Fact]
    public void MisdirectedSourceLinkFailsThePlanInsteadOfSilentlyOmittingSource()
    {
        using var fixture = new Fixture();
        fixture.LinkAll();
        string link = Path.Combine(fixture.Client, "ReignBetaServer");
        Directory.Delete(link);
        fixture.Link(link, Path.Combine(fixture.Server, "ReignModules"));
        var layout = new ReignSourceLayout(fixture.Client);
        Assert.False(layout.CanTraverse(new DirectoryInfo(link)));
        Assert.Throws<InvalidDataException>(layout.ValidateProjections);
    }

    [Theory]
    [InlineData("../../outside")]
    [InlineData("../Unrelated")]
    public void RepositoryPathCannotExpandTheSourceBoundary(string serverPath)
    {
        using var fixture = new Fixture();
        fixture.WriteManifest(serverPath);
        Assert.Throws<InvalidDataException>(() => new ReignSourceLayout(fixture.Client));
    }

    [Fact]
    public void OrdinaryCopiedSourceIsRejectedInPairedMode()
    {
        using var fixture = new Fixture();
        foreach (string name in ReignSourceLayout.ServerDirectories)
            Directory.CreateDirectory(Path.Combine(fixture.Client, name));
        Assert.Throws<InvalidDataException>(new ReignSourceLayout(fixture.Client).ValidateProjections);
    }

    [Fact]
    public void DirectReadsAndDeepSearchesCannotBypassAnUnregisteredAncestor()
    {
        using var fixture = new Fixture();
        fixture.LinkAll();
        string outside = Path.Combine(fixture.Server, "private-state", "nested");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "fixture.json"), "{}");
        fixture.Link(Path.Combine(fixture.Server, "ReignBetaServer", "escape"), Path.GetDirectoryName(outside)!);
        var options = TestOptions.Create() with { WorkspaceRoot = fixture.Client };
        var workspace = new WorkspaceAccess(options, new SensitiveDataRedactor());
        Assert.Throws<InvalidDataException>(() => workspace.ReadSource("ReignBetaServer/escape/nested/fixture.json", 1, 10));
        Assert.Throws<InvalidDataException>(() => workspace.Search("fixture", "ReignBetaServer/escape/nested", 10, false));
        string valid = Path.Combine(fixture.Server, "ReignBetaServer", "source.cs");
        File.WriteAllText(valid, "// visible");
        Assert.Contains("visible", workspace.ReadSource("ReignBetaServer/source.cs", 1, 10).Text);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<string> links = [];
        private readonly string root = Path.Combine(TestOptions.FindWorkspace(), ".codex-build", "source-layout-tests", Guid.NewGuid().ToString("N"));
        public string Client => Path.Combine(root, "Reign");
        public string Server => Path.Combine(root, "ReignServer");

        public Fixture()
        {
            Directory.CreateDirectory(Client);
            Directory.CreateDirectory(Path.Combine(Server, ".git"));
            foreach (string name in ReignSourceLayout.ServerDirectories)
                Directory.CreateDirectory(Path.Combine(Server, name));
            WriteManifest("../ReignServer");
        }

        public void WriteManifest(string relativePath) => File.WriteAllText(Path.Combine(Client, "reign.repositories.json"), JsonSerializer.Serialize(new
        {
            schema = "reign-paired-repositories-v1", mode = "paired",
            client = new { name = "Reign", origin = ReignSourceLayout.ClientOrigin },
            server = new { name = "ReignServer", origin = ReignSourceLayout.ServerOrigin, relativePath },
            serverSourceDirectories = ReignSourceLayout.ServerDirectories
        }));

        public void LinkAll()
        {
            foreach (string name in ReignSourceLayout.ServerDirectories)
                Link(Path.Combine(Client, name), Path.Combine(Server, name));
        }

        public void Link(string link, string target)
        {
            Assert.StartsWith(root + Path.DirectorySeparatorChar, Path.GetFullPath(link), StringComparison.OrdinalIgnoreCase);
            links.Add(link);
            if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
            var start = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardError = true, RedirectStandardOutput = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path $env:REIGN_TEST_LINK -Target $env:REIGN_TEST_TARGET | Out-Null");
            start.Environment["REIGN_TEST_LINK"] = link;
            start.Environment["REIGN_TEST_TARGET"] = target;
            using var process = Process.Start(start)!;
            Assert.True(process.WaitForExit(15000), "Timed out creating an isolated source-layout junction.");
            Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
        }

        public void Dispose()
        {
            // Remove the run-owned links before their sibling targets; recursive
            // deletion can otherwise encounter dangling junctions on Windows.
            foreach (string path in links.Distinct().OrderByDescending(path => path.Length))
                if (Directory.Exists(path) || new DirectoryInfo(path).LinkTarget != null) Directory.Delete(path);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
