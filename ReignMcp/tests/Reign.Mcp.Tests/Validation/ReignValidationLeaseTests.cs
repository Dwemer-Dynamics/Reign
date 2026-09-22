using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignValidationLeaseTests : IDisposable
{
    private readonly string buildRoot = Path.Combine(TestOptions.FindWorkspace(), ".codex-build",
        "reign-mcp-tests", Guid.NewGuid().ToString("N"));
    private const string TaskId = "11111111-2222-4333-8444-555555555555";

    private static ValidationPlan Plan() => new ReignProjectCatalog(TestOptions.Create())
        .CreatePlan("changed", "Release", false, "AGENTS.md");

    [Fact]
    public void StatusOfMissingWorkspaceDoesNotCreateFiles()
    {
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.True(status.CanStart);
        Assert.Null(status.Owner);
        Assert.False(Directory.Exists(buildRoot));
    }

    [Fact]
    public void HeldLeaseExposesOwnerScopeAndProgressButStillRejectsAnotherWriter()
    {
        using var held = ReignValidationLease.Acquire(buildRoot, Plan(), TaskId);
        held.Update("verifying", "mcp-tests", 3, "example-run",
            Path.Combine(buildRoot, "validation", "example-run", "validation-report.json"), "abc");
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.Equal("busy", status.Status);
        Assert.False(status.CanStart);
        Assert.Equal(TaskId, status.Owner!.RequestingTaskId);
        Assert.Equal(Environment.ProcessId, status.Owner.ProcessId);
        Assert.Equal("changed", status.Owner.Profile);
        Assert.Equal(new[] { "AGENTS.md" }, status.Owner.ChangedPaths);
        Assert.Equal("verifying", status.Owner.Phase);
        Assert.Equal(3, status.Owner.CompletedOperations);
        Assert.Equal("example-run", status.Owner.RunId);
        var error = Assert.Throws<InvalidOperationException>(() =>
            ReignValidationLease.Acquire(buildRoot, Plan(), Guid.NewGuid().ToString()));
        Assert.Contains("reign_get_validation_status", error.Message);
        Assert.Equal(status.Owner.LeaseId, ReignValidationLease.ReadStatus(buildRoot).Owner!.LeaseId);
    }

    [Fact]
    public void DisposedUnfinishedLeaseIsAvailableAndRetainsInterruptedEvidence()
    {
        using (var held = ReignValidationLease.Acquire(buildRoot, Plan(), TaskId))
            held.Update("verifying", "contracts");
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.True(status.CanStart);
        Assert.Null(status.Owner);
        Assert.Equal("interrupted", status.LastRun!.Phase);
        Assert.Equal(TaskId, status.LastRun.RequestingTaskId);
    }

    [Fact]
    public void LegacyExclusiveLeaseIsBusyWithUnknownOwner()
    {
        Directory.CreateDirectory(buildRoot);
        using var held = new FileStream(Path.Combine(buildRoot, "validation.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.Equal("busy", status.Status);
        Assert.False(status.CanStart);
        Assert.Null(status.Owner);
    }

    [Fact]
    public void MismatchedOrCorruptProgressCannotReplaceTheActualLeaseOwner()
    {
        using var held = ReignValidationLease.Acquire(buildRoot, Plan(), TaskId);
        var original = ReignValidationLease.ReadStatus(buildRoot).Owner!;
        string statePath = Path.Combine(buildRoot, "validation-state.json");
        File.WriteAllText(statePath, JsonSerializer.Serialize(original with
        {
            LeaseId = Guid.NewGuid().ToString("N"),
            RequestingTaskId = Guid.NewGuid().ToString(),
            Phase = "completed"
        }));
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.Equal(original.LeaseId, status.Owner!.LeaseId);
        Assert.Equal(TaskId, status.Owner.RequestingTaskId);
        Assert.Equal("planning", status.Owner.Phase);
        File.WriteAllText(statePath, "partial JSON");
        Assert.Equal(original.LeaseId, ReignValidationLease.ReadStatus(buildRoot).Owner!.LeaseId);
    }

    [Fact]
    public void AttributionRejectsNonTaskTextBeforeCreatingAnyLease()
    {
        Assert.Throws<ArgumentException>(() =>
            ReignValidationLease.Acquire(buildRoot, Plan(), "pretend task / command"));
        Assert.False(Directory.Exists(buildRoot));
    }

    [Fact]
    public void InaccessibleLeasePathNeverReportsAvailability()
    {
        Directory.CreateDirectory(Path.Combine(buildRoot, "validation.lock"));
        var status = ReignValidationLease.ReadStatus(buildRoot);
        Assert.Equal("unavailable", status.Status);
        Assert.False(status.CanStart);
        Assert.Null(status.Owner);
    }

    [Fact]
    public void CorruptOptionalFieldsCannotBreakStatusOrRelease()
    {
        using var held = ReignValidationLease.Acquire(buildRoot, Plan(), TaskId);
        string statePath = Path.Combine(buildRoot, "validation-state.json");
        string state = File.ReadAllText(statePath);
        using var document = JsonDocument.Parse(state);
        var fields = document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        fields["ChangedPaths"] = null;
        File.WriteAllText(statePath, JsonSerializer.Serialize(fields));
        Assert.Equal(TaskId, ReignValidationLease.ReadStatus(buildRoot).Owner!.RequestingTaskId);
    }

    public void Dispose()
    {
        if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true);
    }
}
