using System.Diagnostics;

namespace Reign.Mcp.Tests;

public sealed class ReignValidationScriptTests
{
    [Fact]
    public void RestoreOptionAllowsValidatorBootstrapToRestoreMissingAssets()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "reign-validation-script-" + Guid.NewGuid().ToString("N"));
        string scriptDirectory = Path.Combine(root, "ReignMcp", "scripts");
        string shimDirectory = Path.Combine(root, "shims");
        string invocationLog = Path.Combine(root, "dotnet-invocations.txt");
        Directory.CreateDirectory(scriptDirectory);
        Directory.CreateDirectory(shimDirectory);
        try
        {
            string workspace = TestOptions.FindWorkspace();
            string sourceScript = Path.Combine(workspace, "ReignMcp", "scripts",
                "reign-validate.ps1");
            string testScript = Path.Combine(scriptDirectory, "reign-validate.ps1");
            File.Copy(sourceScript, testScript);
            File.WriteAllText(Path.Combine(shimDirectory, "dotnet.cmd"),
                "@echo %*>>\"%DOTNET_INVOCATION_LOG%\"\r\n@exit /b 0\r\n");

            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(testScript);
            startInfo.ArgumentList.Add("-Restore");
            startInfo.ArgumentList.Add("-Profile");
            startInfo.ArgumentList.Add("all");
            startInfo.Environment["DOTNET_INVOCATION_LOG"] = invocationLog;
            startInfo.Environment["PATH"] = shimDirectory + Path.PathSeparator
                + Environment.GetEnvironmentVariable("PATH");

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start PowerShell.");
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
            string[] invocations = File.ReadAllLines(invocationLog);
            Assert.Equal(2, invocations.Length);
            Assert.DoesNotContain("--no-restore", invocations[0],
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains("--restore", invocations[1],
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
