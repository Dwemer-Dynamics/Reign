using System.Diagnostics;

namespace Reign.Mcp.Tests;

public sealed class ReleaseInstallerTests
{
    [Fact]
    public async Task DatabaseCredentialAclUsesItsHostModuleWithAnIncompatibleInheritedModulePath()
    {
        if (!OperatingSystem.IsWindows()) return;
        string workspace = TestOptions.FindWorkspace();
        string root = Path.Combine(Path.GetTempPath(), "Reign security module", Guid.NewGuid().ToString("N"));
        string incompatible = Path.Combine(root, "modules", "Microsoft.PowerShell.Security");
        Directory.CreateDirectory(incompatible);
        await File.WriteAllTextAsync(Path.Combine(incompatible, "Microsoft.PowerShell.Security.psd1"),
            "@{ RootModule='wrong-host.psm1'; ModuleVersion='99.0'; GUID='fa91246d-15dc-4e54-812c-d57ff59e50af'; FunctionsToExport=@('Get-Acl') }");
        await File.WriteAllTextAsync(Path.Combine(incompatible, "wrong-host.psm1"),
            "function Get-Acl { throw 'wrong-host-security-module' }; Export-ModuleMember -Function Get-Acl");
        string fixture = Path.Combine(root, "credential-fixture.bin");
        await File.WriteAllTextAsync(fixture, "isolated fixture, no credential");
        string script = Path.Combine(root, "check.ps1");
        await File.WriteAllTextAsync(script, """
            param([string]$Source, [string]$Fixture, [switch]$Production)
            $ErrorActionPreference='Stop'
            if ($Production) {
                $tokens=$null; $errors=$null
                $ast=[Management.Automation.Language.Parser]::ParseFile($Source,[ref]$tokens,[ref]$errors)
                $functions=@($ast.FindAll({param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Protect-PrivateFile'},$true))
                if ($errors.Count -or $functions.Count -ne 1) { throw 'Expected the one production credential ACL function.' }
                . ([scriptblock]::Create($functions[0].Extent.Text))
                Protect-PrivateFile $Fixture
                $module=(Get-Command Get-Acl).Module.Path
                if (-not $module.StartsWith($PSHOME,[StringComparison]::OrdinalIgnoreCase)) { throw 'Security command came from another host.' }
                $owner=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
                $acl=Get-Acl -LiteralPath $Fixture
                $grants=@($acl.GetAccessRules($true,$true,[Security.Principal.SecurityIdentifier]))
                if (-not $acl.AreAccessRulesProtected -or $grants.Count -ne 2 -or @($grants|Where-Object {$_.IdentityReference.Value -notin @($owner,'S-1-5-18')}).Count) { throw 'Credential grants differ from user and SYSTEM only.' }
                'HOST_SECURITY_ACL_OK'
            } else { Get-Acl -LiteralPath $Fixture | Out-Null }
            """);
        foreach (bool production in new[] { false, true })
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = root
            };
            start.Environment["PSModulePath"] = Path.Combine(root, "modules") + ";" + Path.Combine(Path.GetDirectoryName(start.FileName)!, "Modules");
            foreach (string argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                "-Source", Path.Combine(workspace, "ReignRelease", "Initialize-PostgreSql.ps1"), "-Fixture", fixture }) start.ArgumentList.Add(argument);
            if (production) start.ArgumentList.Add("-Production");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
            string output = await stdout + await stderr;
            if (production) { Assert.True(process.ExitCode == 0, output); Assert.Contains("HOST_SECURITY_ACL_OK", output); }
            else { Assert.NotEqual(0, process.ExitCode); Assert.Contains("wrong-host-security-module", output); }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetupRejectsUnsupportedDatabasePathsBeforeCreatingInstallationState(bool unicodeProgram)
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        string proofRoot = Path.Combine(Path.GetTempPath(), "Reign path preflight", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(proofRoot);
        string program = Path.Combine(proofRoot, unicodeProgram ? "Program \u6f22\u5b57" : "Program");
        string data = Path.Combine(proofRoot, unicodeProgram ? "Data" : "Data \u6f22\u5b57");
        string record = Path.Combine(proofRoot, "installation.json");
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root
        };
        // No manifest, payloads or game exist. A meaningful path error must win
        // before any of those inputs are read or installation state is created.
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignRelease", "Install-Reign.ps1"),
            "-Manifest", Path.Combine(proofRoot, "missing-package.json"),
            "-PayloadDirectory", Path.Combine(proofRoot, "Payloads"),
            "-BannerlordRoot", Path.Combine(proofRoot, "Game"),
            "-ProgramRoot", program, "-DataRoot", data, "-InstallationFile", record }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("must use ASCII characters", output);
        Assert.False(Directory.Exists(program));
        Assert.False(Directory.Exists(data));
        Assert.False(File.Exists(record));
    }

    [Fact]
    public async Task WindowsPowerShellInstallerRejectsUnsafePayloadsAndPreservesPlayerPortraits()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        string proofRoot = Path.Combine(Path.GetTempPath(), "Reign installer contracts");
        Directory.CreateDirectory(proofRoot);
        var start = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = root
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignRelease", "tests", "Test-Installer.ps1"), "-EvidenceRoot", proofRoot }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        string output = await stdout + await stderr;
        Assert.True(process.ExitCode == 0, output);
        Assert.Contains("installer contracts passed", output);
    }
}
