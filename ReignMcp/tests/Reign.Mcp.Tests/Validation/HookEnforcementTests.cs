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
    public void SafeAndCanonicalCommandsAreAllowed(string command)
    {
        Assert.Equal("", RunHook(command));
    }

    private static string RunHook(string command)
    {
        var workspace = TestOptions.FindWorkspace();
        var hook = Path.Combine(
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
            tool_input = new { command }
        }));
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd().Trim();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error);
        return output;
    }
}
