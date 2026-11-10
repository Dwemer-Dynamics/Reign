using System.Diagnostics;
using System.Globalization;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class ChatViewTests
{
    [Fact]
    public async Task CacheWritesWorkAtTheReportedSteamPathLengthUnderDotNetFramework()
    {
        if (!OperatingSystem.IsWindows()) return;
        string root = TestOptions.FindWorkspace();
        string scratch = Path.Combine(Path.GetTempPath(), "rv_" + Guid.NewGuid().ToString("N"));
        Assert.False(Directory.Exists(scratch));
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot, "powershell.exe"))
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot, "Modules");
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
            Path.Combine(root, "ReignMcp/tests/Reign.Mcp.Tests/Platform/ChatViewCacheProbe.ps1"), "-Source",
            Path.Combine(root, "ReignBeta/src/Modules/Dialogue/Integration/ReignChatViewCacheFile.cs"), "-Scratch", scratch })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            string result = await output;
            Assert.True(process.ExitCode == 0, result + await error);
            using var json = JsonDocument.Parse(result);
            Assert.True(json.RootElement.GetProperty("ok").GetBoolean());
            Assert.Contains(".NET Framework", json.RootElement.GetProperty("runtime").GetString());
            Assert.Equal(282, json.RootElement.GetProperty("oldTemporaryLength").GetInt32());
            Assert.InRange(json.RootElement.GetProperty("newTemporaryLength").GetInt32(), 1, 259);
        }
        catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw new TimeoutException("Legacy cache path probe timed out."); }
        finally
        {
            string absolute = Path.GetFullPath(scratch);
            Assert.Equal(Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar), Path.GetDirectoryName(absolute));
            Assert.StartsWith("rv_", Path.GetFileName(absolute));
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
    }

    [Fact]
    public void EveryChatSurfaceHasImageToggleAndUndistortedSceneGeometry()
    {
        foreach (string kind in new[] { "Individual", "Party", "Castle" })
        {
            var document = XDocument.Load(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/GUI/Prefabs", $"Reign{kind}ChatScreen.xml"));
            var nodes = document.Descendants().ToArray();
            Assert.Single(nodes, n => (string?)n.Attribute("Id") == "ChatViewControl");
            foreach (string id in new[] { "ChatViewInlineArt", "ChatViewEnlargedArt" })
            {
                var art = nodes.Single(n => (string?)n.Attribute("Id") == id);
                double width = double.Parse((string)art.Attribute("SuggestedWidth")!, CultureInfo.InvariantCulture);
                double height = double.Parse((string)art.Attribute("SuggestedHeight")!, CultureInfo.InvariantCulture);
                Assert.Equal(1.5d, width / height, 6);
            }
            Assert.Contains(nodes, n => (string?)n.Attribute("Command.Click") == "ExecuteView");
            Assert.Contains(nodes, n => (string?)n.Attribute("Command.Click") == "ExecuteOpenScene");
            Assert.Contains(nodes, n => (string?)n.Attribute("Command.Click") == "ExecuteCloseImage");
        }
    }

    [Fact]
    public void ProviderFreeFixturesCoverReadyPendingFailureLongAndEnlargedStates()
    {
        var start = new ProcessStartInfo("node") { WorkingDirectory = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/tools/GauntletXmlPreviewer"), RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("test-chat-view-contract.mjs");
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000), "The provider-free fixture contract timed out.");
        Assert.True(process.ExitCode == 0, output + error);
        Assert.Contains("\"checks\":15", output);
    }

    [Fact]
    public void LeaveStoppedPropagatesToActivationAndRecovery()
    {
        string root = TestOptions.FindWorkspace();
        string deployment = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/Deploy-LocalWsl.ps1"));
        string control = File.ReadAllText(Path.Combine(root, "ReignServer/scripts/reignctl"));
        Assert.Contains("[switch]$LeaveStopped", deployment);
        Assert.Contains("[string[]]$activationOptions = if ($LeaveStopped) { @('--leave-stopped') } else { @() }", deployment);
        Assert.Contains("activate $artifact @activationOptions", deployment);
        Assert.Contains("rollback $previousVersion @activationOptions", deployment);
        Assert.DoesNotContain("if ($wasManagedRunning) {", deployment);
        Assert.Contains("if ($LeaveStopped) {", deployment);
        Assert.Contains("if [ \"$leave_stopped\" = 0 ] && ! start; then", control);
        Assert.Contains("Reign runtime activated; service left stopped", control);
    }

    [Fact]
    public void LeaveStoppedIsOneNativeArgumentRatherThanIndividualCharacters()
    {
        string deployment = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignServer/scripts/Deploy-LocalWsl.ps1"));
        string declaration = deployment.Split('\n').Single(line => line.Contains("$activationOptions = if ($LeaveStopped)", StringComparison.Ordinal)).Trim();
        var start = new ProcessStartInfo("pwsh") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$LeaveStopped = $true; " + declaration + "; $activationOptions.GetType().FullName; $activationOptions.Count; $activationOptions[0]");
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000), "Argument binding probe timed out.");
        Assert.True(process.ExitCode == 0, error);
        Assert.Equal(new[] { "System.String[]", "1", "--leave-stopped" }, output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public void ReusedControlAndApertureAssetsMatchTheirRecordedHashes()
    {
        string module = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta");
        using var contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(module, "GUI/UiCalibration/modern-style-contract.json")));
        foreach (var asset in contract.RootElement.GetProperty("chatView").GetProperty("assets").EnumerateArray())
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(module, asset.GetProperty("path").GetString()!));
            Assert.Equal(asset.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        }
    }
}
