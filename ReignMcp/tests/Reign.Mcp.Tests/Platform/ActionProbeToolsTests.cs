using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ActionProbeToolsTests
{
    [Fact]
    public async Task ActionBatchRefusesMissingVerificationGateBeforeAnyApiCall()
    {
        ReignMcpOptions options = TestOptions.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ActionProbeTools.StartActionBatch(null!, options, null!,
                "campaign", "run", "batch", "[]",
                "start Reign action batch on disposable save"));
    }

    [Fact]
    public async Task ActionBatchRejectsOverrideOnNegativeControlBeforeAnyApiCall()
    {
        ReignMcpOptions options = TestOptions.Create() with
        { AllowVerificationControl = true };
        string cases = JsonSerializer.Serialize(new[]
        {
            new { caseId = "negative-1", actorHeroId = "lord_1",
                expectedCommand = "transfer_gold", playerText = "Do not pay me.",
                negativeControl = true, cooperationOverride = true }
        });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ActionProbeTools.StartActionBatch(null!, options, null!,
                "campaign", "run", "batch", cases,
                "start Reign action batch on disposable save"));
    }

    [Fact]
    public void ActionProbeCatalogAndProcedureDeclareIndependentNativeProof()
    {
        string workspace = TestOptions.FindWorkspace();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(workspace, "reign.testing.json")));
        JsonElement probe = catalog.RootElement.GetProperty("nonDiplomacyActionProbe");
        Assert.Equal("reign_start_action_batch",
            probe.GetProperty("entry").GetString());
        Assert.Contains("independent native state oracle",
            probe.GetProperty("evidence").GetString(), StringComparison.Ordinal);
        string procedure = File.ReadAllText(Path.Combine(workspace, "docs",
            "agent", "testing", "FEATURE_HARNESSES.md"));
        Assert.Contains("completed_no_effect", procedure, StringComparison.Ordinal);
        Assert.Contains("reign_start_action_batch", procedure, StringComparison.Ordinal);
        Assert.Contains("observedItemIds", procedure, StringComparison.Ordinal);
        Assert.Contains("modifier identity", probe.GetProperty("evidence").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActionBatchRejectsMoreThanSixteenObservedItemsBeforeAnyApiCall()
    {
        ReignMcpOptions options = TestOptions.Create() with
        { AllowVerificationControl = true };
        string cases = JsonSerializer.Serialize(new[]
        {
            new { caseId = "item-1", actorHeroId = "lord_1",
                expectedCommand = "transfer_item", playerText = "Take one grain.",
                observedItemIds = Enumerable.Range(1, 17).Select(index =>
                    "item_" + index).ToArray() }
        });
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ActionProbeTools.StartActionBatch(null!, options, null!,
                "campaign", "run", "batch", cases,
                "start Reign action batch on disposable save"));
    }
}
