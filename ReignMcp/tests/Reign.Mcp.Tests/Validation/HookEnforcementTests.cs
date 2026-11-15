using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class HookEnforcementTests
{
    [Fact]
    public void DirectBuildIsDenied()
    {
        var output = RunHook("dotnet build ReignBeta/ReignBeta.csproj -c Release");

        using var document = JsonDocument.Parse(output);
        var hookOutput = document.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("deny", hookOutput.GetProperty("permissionDecision").GetString());
        Assert.Contains(
            "reign_validate",
            hookOutput.GetProperty("permissionDecisionReason").GetString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dotnet --info")]
    [InlineData("powershell -File ReignMcp/scripts/reign-validate.ps1 -Profile all")]
    [InlineData("dotnet Reign.Mcp.Server.dll --validate-cli --profile all")]
    [InlineData("git -C ../ReignServer diff --check -- scripts/Deploy-LocalWsl.ps1")]
    [InlineData("Get-Content D:/old-worktree/scripts/Deploy-LocalWsl.ps1")]
    public void SafeAndCanonicalCommandsAreAllowed(string command)
    {
        Assert.Equal("", RunHook(command));
    }

    [Theory]
    [InlineData("ReignServer/scripts/Deploy-LocalWsl.ps1")]
    [InlineData("../ReignServer/scripts/Deploy-LocalWsl.ps1")]
    public void CanonicalArtifactDeploymentIsAllowed(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(TestOptions.FindWorkspace(), relativePath));
        Assert.Equal("", RunHook($"& '{path}' -Workspace '{TestOptions.FindWorkspace()}' -LinuxBuildReport 'validated.json' -SkipClient"));
        Assert.Equal("", RunHook($"Get-Content -LiteralPath '{path}'"));
        Assert.Equal("", RunHook($"pwsh -File '{path}' -ValidationReport 'product.json' -LinuxBuildReport 'linux.json'"));
    }

    [Theory]
    [InlineData("scripts/deploy.ps1")]
    [InlineData("scripts/build.ps1")]
    [InlineData("D:/unrelated/scripts/Deploy-LocalWsl.ps1")]
    [InlineData("D:/unrelated/scripts/Deploy-LocalWsl-extra.ps1")]
    public void LegacyOrUnrelatedDeploymentScriptsRemainDenied(string path)
    {
        AssertDenied(RunHook($"& '{path}'"));
    }

    [Theory]
    [InlineData("dotnet build ReignBeta/ReignBeta.csproj")]
    [InlineData("& 'scripts/deploy.ps1'")]
    public void CanonicalDeploymentDoesNotExemptOtherCommands(string otherCommand)
    {
        var path = Path.GetFullPath(Path.Combine(TestOptions.FindWorkspace(), "../ReignServer/scripts/Deploy-LocalWsl.ps1"));
        AssertDenied(RunHook($"& '{path}' -LinuxBuildReport 'validated.json' -SkipClient; {otherCommand}"));
    }

    [Fact]
    public void ReadingALegacyScriptDoesNotExemptAChainedInvocation()
    {
        AssertDenied(RunHook("git diff -- scripts/Deploy-LocalWsl.ps1; & 'D:/old-worktree/scripts/Deploy-LocalWsl.ps1'"));
        AssertDenied(RunHook("Get-Content scripts/deploy.ps1; dotnet build ReignBeta/ReignBeta.csproj"));
    }

    private static void AssertDenied(string output)
    {
        using var document = JsonDocument.Parse(output);
        Assert.Equal("deny", document.RootElement.GetProperty("hookSpecificOutput").GetProperty("permissionDecision").GetString());
    }

    [Fact]
    public void PrimaryHookAttestsBothLinkedRepositoriesAndStillRejectsForeignAndChainedCommands()
    {
        string temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "reign-hook-worktrees-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            string primary = Path.Combine(temporaryRoot, "primary", "Reign");
            string primaryServer = Path.Combine(temporaryRoot, "primary", "ReignServer");
            string linked = Path.Combine(temporaryRoot, "linked", "Reign");
            string linkedServer = Path.Combine(temporaryRoot, "linked", "ReignServer");
            string wrong = Path.Combine(temporaryRoot, "wrong", "Reign");
            string wrongServer = Path.Combine(temporaryRoot, "wrong", "ReignServer");
            string foreign = Path.Combine(temporaryRoot, "foreign", "Reign");
            foreach (string repository in new[] { primary, primaryServer, wrongServer, foreign })
            {
                Directory.CreateDirectory(repository);
                Git(repository, "init", "--quiet");
                Git(repository, "-c", "user.name=ReignHookFixture", "-c", "user.email=fixture@example.invalid", "commit", "--quiet", "--allow-empty", "-m", "isolated hook fixture");
            }
            Git(primary, "worktree", "add", "--quiet", "--detach", linked, "HEAD");
            Git(primaryServer, "worktree", "add", "--quiet", "--detach", linkedServer, "HEAD");
            Git(primary, "worktree", "add", "--quiet", "--detach", wrong, "HEAD");
            string hook = Path.Combine(primary, ".codex", "hooks", "enforce-reign-validation.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
            File.Copy(Path.Combine(TestOptions.FindWorkspace(), ".codex", "hooks", "enforce-reign-validation.ps1"), hook);
            string deployment = Path.Combine(linkedServer, "scripts", "Deploy-LocalWsl.ps1");
            AssertDenied(RunHook($"& '{deployment}' -LinuxBuildReport 'validated.json' -SkipClient", linked, hook));
            AssertDenied(RunHook("& '../ReignServer/scripts/Deploy-LocalWsl.ps1' -LinuxBuildReport 'validated.json'", linked, hook));
            Assert.Equal("", RunHook($"& '{Path.Combine(primaryServer, "scripts", "Deploy-LocalWsl.ps1")}' -SkipClient", primary, hook));
            AssertDenied(RunHook($"& '{deployment}'; dotnet build ReignBeta/ReignBeta.csproj", linked, hook));
            AssertDenied(RunHook($"& '{deployment}'; & 'scripts/deploy.ps1'", linked, hook));
            AssertDenied(RunHook($"& '{Path.Combine(wrongServer, "scripts", "Deploy-LocalWsl.ps1")}'", wrong, hook));
            AssertDenied(RunHook($"& '{deployment}'", foreign, hook));
            AssertDenied(RunHook($"& '{Path.Combine(temporaryRoot, "copy", "scripts", "Deploy-LocalWsl.ps1")}'", linked, hook));
        }
        finally
        {
            string expectedParent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            Assert.StartsWith(expectedParent, temporaryRoot, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("reign-hook-worktrees-", Path.GetFileName(temporaryRoot));
            foreach (string file in Directory.EnumerateFiles(temporaryRoot, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static void Git(string directory, params string[] arguments)
    {
        // MCP hosts can inherit an agent-bundled Git ahead of the installed Git.
        // Use the normal installation for these disposable repository fixtures.
        string installedGit = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe");
        var start = new ProcessStartInfo(File.Exists(installedGit) ? installedGit : "git") { WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(name);
        start.Environment["GIT_CONFIG_GLOBAL"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_CONFIG_SYSTEM"] = OperatingSystem.IsWindows() ? "NUL" : "/dev/null";
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.hooksPath=" + Path.Combine(directory, ".disabled-fixture-hooks"));
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("commit.gpgSign=false");
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(entireProcessTree: true); throw new TimeoutException("Isolated Git fixture timed out: " + string.Join(" ", arguments)); }
        Assert.True(process.ExitCode == 0, error.GetAwaiter().GetResult() + output.GetAwaiter().GetResult());
    }

    private static string RunHook(string command, string? commandDirectory = null, string? hookOverride = null)
    {
        var workspace = TestOptions.FindWorkspace();
        var hook = hookOverride ?? Path.Combine(
            workspace,
            ".codex",
            "hooks",
            "enforce-reign-validation.ps1");
        Assert.True(File.Exists(hook), $"Hook is missing: {hook}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(hook);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        process.StandardInput.Write(JsonSerializer.Serialize(new
        {
            tool_name = "Bash",
            tool_input = new { command, workdir = commandDirectory ?? workspace }
        }));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
