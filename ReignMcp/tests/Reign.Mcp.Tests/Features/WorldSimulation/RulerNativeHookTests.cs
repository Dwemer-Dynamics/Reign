using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class RulerNativeHookTests
{
    [ValidatedClientFact]
    public async Task Actual_compiled_ruler_hooks_bind_to_licensed_Bannerlord_without_starting_the_game()
    {
        string client = MapBarInputSafetyTests.CurrentClientArtifact()!;
        string game = Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")!;
        string evidence = Path.Combine(new DirectoryInfo(AppContext.BaseDirectory).Parent!.FullName, "ruler-native-probe", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(evidence);
        string shellRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0");
        var start = new ProcessStartInfo(Path.Combine(shellRoot,"powershell.exe")) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.Environment["PSModulePath"] = Path.Combine(shellRoot,"Modules");
        foreach(string arg in new[]{"-NoProfile","-NonInteractive","-ExecutionPolicy","Bypass","-File",Path.Combine(TestOptions.FindWorkspace(),"ReignMcp/tests/Reign.Mcp.Tests/Features/WorldSimulation/RulerNativeHookProbe.ps1"),"-ClientAssembly",client,"-BannerlordPath",game,"-EvidenceDirectory",evidence}) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw new TimeoutException("Ruler hook probe timed out: "+evidence);}
        Assert.True(process.ExitCode==0,(await output)+(await error));
        using var report=JsonDocument.Parse(File.ReadAllText(Path.Combine(evidence,"report.json")));
        Assert.True(report.RootElement.GetProperty("ok").GetBoolean());Assert.True(report.RootElement.GetProperty("hookCount").GetInt32()>=20);
        Assert.Equal(2, report.RootElement.GetProperty("partyCountChecks").GetInt32());
        Assert.True(report.RootElement.GetProperty("prisonerFixtureChecks").GetInt32() >= 25);
        Assert.Equal(177, report.RootElement.GetProperty("nativeMathAssertions").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("nativeMathProviderCalls").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("nativeMathCampaignMutations").GetInt32());
        Assert.True(report.RootElement.GetProperty("nativeMathProfileRejected").GetBoolean());
        Assert.True(report.RootElement.GetProperty("campaignOptionsObserved").GetBoolean());
        Assert.True(report.RootElement.GetProperty("judgmentDrawChecks").GetInt32() >= 18);
        Assert.True(report.RootElement.GetProperty("imprisonmentCustodyChecks").GetInt32() >= 30);
    }
    private sealed class ValidatedClientFactAttribute : FactAttribute
    {
        public ValidatedClientFactAttribute()
        {
            if (!OperatingSystem.IsWindows() || MapBarInputSafetyTests.CurrentClientArtifact() == null
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REIGN_BANNERLORD_PATH")))
                Skip = "Requires the same-run canonical client artifact and licensed Bannerlord assemblies.";
        }
    }
}
