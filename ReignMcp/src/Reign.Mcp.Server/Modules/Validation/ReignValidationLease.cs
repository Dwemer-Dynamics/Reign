using System.Text.Json;

namespace Reign.Mcp.Server;

/// <summary>The OS file lease remains authoritative; snapshots only explain its owner.</summary>
public sealed class ReignValidationLease : IDisposable
{
    private const string LeaseFile = "validation.lock";
    private const string StateFile = "validation-state.json";
    private const int MaximumStateBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string buildRoot;
    private readonly FileStream lease;
    private ValidationLeaseState state;
    private bool disposed;
    private bool completed;

    private ReignValidationLease(string buildRoot, FileStream lease, ValidationLeaseState state)
    {
        this.buildRoot = buildRoot;
        this.lease = lease;
        this.state = state;
    }

    public static ReignValidationLease Acquire(string buildRoot, ValidationPlan plan, string? requestingTaskId)
    {
        string? taskId = NormalizeTaskId(requestingTaskId);
        Directory.CreateDirectory(buildRoot);
        FileStream stream;
        try
        {
            // Readers may observe attribution; competing writers still cannot acquire a lease.
            stream = new FileStream(Path.Combine(buildRoot, LeaseFile),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            throw new InvalidOperationException(
                "Another Reign validation currently owns the workspace build lane. "
                + "Call reign_get_validation_status to inspect its owner and progress; wait or reuse "
                + "a matching successful report. Do not bypass the lease.", error);
        }

        try
        {
            stream.SetLength(0);
            var now = DateTimeOffset.UtcNow.ToString("O");
            var state = new ValidationLeaseState
            {
                LeaseId = Guid.NewGuid().ToString("N"),
                ProcessId = Environment.ProcessId,
                RequestingTaskId = taskId,
                Profile = plan.Profile,
                Configuration = plan.Configuration,
                ChangedPaths = plan.ChangedPaths.Take(256).ToArray(),
                ChangedPathCount = plan.ChangedPaths.Count,
                AcquiredUtc = now,
                UpdatedUtc = now,
                Phase = "planning"
            };
            JsonSerializer.Serialize(stream, state, JsonOptions);
            stream.SetLength(stream.Position);
            stream.Flush(flushToDisk: true);
            var result = new ReignValidationLease(buildRoot, stream, state);
            result.Publish();
            return result;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Update(string phase, string? operation = null, int? completedOperations = null,
        string? runId = null, string? reportPath = null, string? sourceFingerprint = null)
    {
        state = state with
        {
            Phase = phase,
            Operation = operation,
            CompletedOperations = completedOperations ?? state.CompletedOperations,
            RunId = runId ?? state.RunId,
            ReportPath = reportPath ?? state.ReportPath,
            SourceFingerprintSha256 = sourceFingerprint ?? state.SourceFingerprintSha256,
            UpdatedUtc = DateTimeOffset.UtcNow.ToString("O")
        };
        Publish();
    }

    public void Complete(ValidationReport report)
    {
        Update(report.Reused ? "reused" : report.Ok ? "completed" : "failed",
            completedOperations: report.Results.Count, runId: report.RunId,
            reportPath: report.ReportPath, sourceFingerprint: report.SourceFingerprintSha256);
        completed = true;
    }

    public void CompleteOperation(bool ok, string runId, string reportPath, string fingerprint)
    {
        Update(ok ? "completed" : "failed", completedOperations: 1,
            runId: runId, reportPath: reportPath, sourceFingerprint: fingerprint);
        completed = true;
    }

    public static ValidationLaneStatus ReadStatus(string buildRoot)
    {
        string lockPath = Path.Combine(buildRoot, LeaseFile);
        try
        {
            // Open only: never creates, truncates, removes or takes over a lease.
            using var probe = new FileStream(lockPath, FileMode.Open,
                FileAccess.ReadWrite, FileShare.ReadWrite);
        }
        catch (FileNotFoundException)
        {
            return Snapshot("available", null, ReadState(Path.Combine(buildRoot, StateFile)));
        }
        catch (DirectoryNotFoundException)
        {
            return Snapshot("available", null, null);
        }
        catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
        {
            var owner = ReadState(lockPath);
            var progress = ReadState(Path.Combine(buildRoot, StateFile));
            // A previous run's sidecar can never supply attribution for another lease.
            if (owner is not null && progress?.LeaseId == owner.LeaseId)
                owner = progress;
            return Snapshot("busy", owner, null,
                owner is null ? "Lease is held; owner metadata is unavailable (legacy owner or publication in progress)." : null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return Snapshot("unavailable", null, null,
                "Lease status could not be read. Availability is unknown; do not infer ownership or remove the lock.");
        }

        return Snapshot("available", null, ReadState(Path.Combine(buildRoot, StateFile)));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        try
        {
            if (!completed)
                Update("interrupted");
        }
        finally
        {
            lease.Dispose();
        }
    }

    private void Publish()
    {
        string temporary = Path.Combine(buildRoot, "validation-state-" + state.LeaseId + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temporary, Path.Combine(buildRoot, StateFile), overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The readable OS lease still supplies authoritative owner attribution.
            // Missing/stale progress must not abort a running build or hide its final report.
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    private static ValidationLeaseState? ReadState(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumStateBytes) return null;
            var result = JsonSerializer.Deserialize<ValidationLeaseState>(stream, JsonOptions);
            return result is not null && result.Schema == "reign-validation-lease-v1"
                && Guid.TryParseExact(result.LeaseId, "N", out _) && result.ProcessId > 0
                && result.ChangedPaths is not null && result.ChangedPaths.Count <= 256 ? result : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? NormalizeTaskId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Guid.TryParse(value, out var id))
            throw new ArgumentException("requestingTaskId must be the exact Codex task UUID.", nameof(value));
        return id.ToString();
    }

    private static ValidationLaneStatus Snapshot(string status, ValidationLeaseState? owner,
        ValidationLeaseState? lastRun, string? diagnostic = null) => new()
    {
        Status = status,
        ObservedUtc = DateTimeOffset.UtcNow.ToString("O"),
        Owner = owner,
        LastRun = lastRun,
        Diagnostic = diagnostic
    };
}

public sealed record ValidationLaneStatus
{
    public string Schema { get; init; } = "reign-validation-lane-v1";
    public required string Status { get; init; }
    public bool CanStart => Status == "available";
    public required string ObservedUtc { get; init; }
    public ValidationLeaseState? Owner { get; init; }
    public ValidationLeaseState? LastRun { get; init; }
    public string? Diagnostic { get; init; }
}

public sealed record ValidationLeaseState
{
    public string Schema { get; init; } = "reign-validation-lease-v1";
    public required string LeaseId { get; init; }
    public required int ProcessId { get; init; }
    public string? RequestingTaskId { get; init; }
    public required string Profile { get; init; }
    public required string Configuration { get; init; }
    public IReadOnlyList<string> ChangedPaths { get; init; } = [];
    public int ChangedPathCount { get; init; }
    public bool ChangedPathsTruncated => ChangedPathCount > ChangedPaths.Count;
    public required string AcquiredUtc { get; init; }
    public required string UpdatedUtc { get; init; }
    public required string Phase { get; init; }
    public string? Operation { get; init; }
    public int CompletedOperations { get; init; }
    public string? RunId { get; init; }
    public string? ReportPath { get; init; }
    public string? SourceFingerprintSha256 { get; init; }
}
