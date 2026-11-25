using System.Diagnostics;
using System.Text.Json;

namespace Reign.Mcp.Tests;

[CollectionDefinition("TesterPackageProcess", DisableParallelization = true)]
public sealed class TesterPackageCollection { }

[Collection("TesterPackageProcess")]
public sealed class TesterPackageTests
{
    [Fact]
    public async Task TesterDistributionHasProviderFreeIntegrityAndRecoveryContracts()
    {
        string directory = Path.Combine(TestOptions.FindWorkspace(), "ReignServer", "scripts", "tester-package");
        string python = Environment.GetEnvironmentVariable("REIGN_TEST_PYTHON") ?? "python";
        string installs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Python");
        if (python == "python" && Directory.Exists(installs))
            python = Directory.EnumerateDirectories(installs, "pythoncore-*")
                .Select(path => Path.Combine(path, "python.exe")).FirstOrDefault(File.Exists) ?? python;
        var start = new ProcessStartInfo(python) { WorkingDirectory = directory,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true,
            UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add("-S");
        start.ArgumentList.Add("-u");
        start.ArgumentList.Add(Path.Combine(directory, "test_package.py"));
        string evidence = Path.Combine(TestOptions.FindWorkspace(), ".codex-build", "tester-contract-start.json");
        File.WriteAllText(evidence, JsonSerializer.Serialize(new { interpreter = python, start.WorkingDirectory, exists = File.Exists(python) }));
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        File.WriteAllText(evidence, JsonSerializer.Serialize(new { interpreter = python, process.Id, start.WorkingDirectory }));
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw new TimeoutException("Tester package contracts exceeded deadline; interpreter=" + python + ". "
                + await output.WaitAsync(TimeSpan.FromSeconds(5)) + "\n" + await error.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.True(process.ExitCode == 0, await output + "\n" + await error);
    }

    [Fact]
    public void TesterDistributionGatesAndEvidenceAreDocumented()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var distribution = catalog.RootElement.GetProperty("testerDistribution");
        Assert.Equal("reign-tester-package-v1", distribution.GetProperty("schema").GetString());
        Assert.Contains("provider-free", distribution.GetProperty("contracts").GetString());
        Assert.Contains("reign-tester-runtime-proof-v1", distribution.GetProperty("nativeProbe").GetString());
        Assert.Contains("reign-tester-image-receipt-v2", distribution.GetProperty("imageEvidence").GetString());
        foreach (string path in new[] { "docs/agent/testing/RELEASE_PACKAGING.md", "ReignMcp/docs/security-model.md" })
        {
            string text = File.ReadAllText(Path.Combine(root, path));
            Assert.Contains("tester-package", text);
            Assert.Contains("reign-tester-image-receipt-v2", text);
            Assert.Contains("native", text);
        }
    }
}
