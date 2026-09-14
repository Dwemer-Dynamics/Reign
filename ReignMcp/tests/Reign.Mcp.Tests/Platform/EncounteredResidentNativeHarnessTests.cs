using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class EncounteredResidentNativeHarnessTests
{
    [Fact]
    public void NativeApproachCatalogMatchesBothBridgeEndsAndDocumentsItsNarrowAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var approach = catalog.RootElement.GetProperty("encounteredResidents").GetProperty("nativeApproach");
        string operation = approach.GetProperty("operation").GetString()!;
        string client = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        string server = File.ReadAllText(Path.Combine(root, "ReignBetaServer/src/Modules/WorldSimulation/LiveInteractionTest.cs"));
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign/ReignEncounteredResidentNativeTestHost.cs"));
        Assert.Contains("\"" + operation + "\"", client);
        Assert.Contains("\"" + operation + "\"", server);
        Assert.Contains(approach.GetProperty("confirmation").GetString()!, native);
        Assert.Contains(approach.GetProperty("schema").GetString()!, native);
        Assert.Contains(operation, TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md")));
        Assert.Contains("bodySha256", approach.GetProperty("target").GetString());
        Assert.Contains("No provider call", approach.GetProperty("safety").GetString());
        Assert.DoesNotContain("ExecuteResidentAction", native);
        Assert.DoesNotContain("AddCompanionAction", native);
        Assert.DoesNotContain("CreateSpecialHero", native);
        var leave = catalog.RootElement.GetProperty("encounteredResidents").GetProperty("nativeLeave");
        string leaveOperation = leave.GetProperty("operation").GetString()!;
        Assert.Contains("\"" + leaveOperation + "\"", client);
        Assert.Contains("\"" + leaveOperation + "\"", server);
        Assert.Contains(leave.GetProperty("confirmation").GetString()!, native);
        Assert.Contains(leave.GetProperty("schema").GetString()!, native);
        Assert.Contains(leaveOperation, TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md")));
        Assert.Contains("logic.OnEndMissionRequest(out bool canLeave)", native);
        Assert.Contains("if (!canLeave || inquiry != null)", native);
        Assert.Contains("Mission.Current.IsFieldBattle", native);
        Assert.Contains("ReignIndividualChatScreenManager.IsOpen", native);
        Assert.DoesNotContain("SaveAs(", native);
        string saveMethod = client[client.IndexOf("private static async Task<LiveCommandResult> SaveCheckpointAsync", StringComparison.Ordinal)..];
        Assert.Contains("Mission.Current != null", saveMethod);
        Assert.True(saveMethod.IndexOf("Mission.Current != null", StringComparison.Ordinal)
            < saveMethod.IndexOf("SaveHandler.SaveAs", StringComparison.Ordinal));
        Assert.Contains("no save was queued", saveMethod);
    }
}
