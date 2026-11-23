using System.Diagnostics;

namespace Reign.Mcp.Tests;

public sealed class ReignClientTransportTests
{
    [Fact]
    public async Task SharedClientTransportPreservesMutationAuthorityAndReadFallback()
    {
        string workspace = TestOptions.FindWorkspace();
        string portraitClient = File.ReadAllText(Path.Combine(workspace,
            "ReignBeta/src/Modules/Platform/Integration/ReignServerClient.Characters.cs"));
        // Bind the fake wire recovery to the shipped client's exception and ID handoff.
        Assert.Matches(@"(?s)bool unknownOutcome = ex is ReignHttpTransport\.UnknownMutationOutcomeException;\s*"
            + @"if \(portraitProduct && \(ex is OperationCanceledException \|\| unknownOutcome\)\s*"
            + @"&& !string\.IsNullOrWhiteSpace\(generationOperationId\)\)\s*\{\s*"
            + @"ReignPortraitGenerationResult recovered = await RecoverPortraitGenerationAsync\(\s*cacheKey, heroStringId, generationOperationId,",
            portraitClient);

        Assert.Contains("unknownOutcome && string.Equals(recovered.GenerationStatus, \"not_found\", StringComparison.OrdinalIgnoreCase)",
            portraitClient, StringComparison.Ordinal);

        var start = new ProcessStartInfo("pwsh")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string arg in new[] { "-NoProfile", "-NonInteractive", "-File",
            Path.Combine(workspace, "ReignMcp/tests/Reign.Mcp.Tests/Platform/ReignClientTransportProbe.ps1"),
            "-Workspace", workspace })
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start transport fixture.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            await process.WaitForExitAsync();
            throw new TimeoutException("The fake-HTTP transport fixture exceeded 60 seconds.");
        }
        string report = await output;
        Assert.True(process.ExitCode == 0, report + await error);
        Assert.Equal(15, report.Split('\n').Count(line => line.StartsWith("PASS ", StringComparison.Ordinal)));
    }
}
