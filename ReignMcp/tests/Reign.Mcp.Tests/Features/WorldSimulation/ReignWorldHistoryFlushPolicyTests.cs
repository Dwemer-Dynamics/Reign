using ReignBeta.Integration;

namespace Reign.Mcp.Tests;

public sealed class ReignWorldHistoryFlushPolicyTests
{
    [Fact]
    public void Unattributed_reconciliation_noise_never_enters_the_durable_outbox()
    {
        Assert.False(
            ReignWorldHistoryFlushPolicy.ShouldPersistEvent(
                "unattributed_state_change"));
        Assert.True(
            ReignWorldHistoryFlushPolicy.ShouldPersistEvent(
                "settlement_owner_changed"));
    }

    [Fact]
    public void Critical_save_boundary_has_time_to_finish_normal_local_uploads()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(30),
            ReignWorldHistoryFlushPolicy.CriticalSaveBoundaryBudget);
    }

    [Fact]
    public void Save_boundary_waits_only_for_events_captured_by_that_save()
    {
        Assert.True(
            ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(42, 42));
        Assert.True(
            ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(41, 42));
        Assert.False(
            ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(43, 42));
        Assert.True(
            ReignWorldHistoryFlushPolicy.IsSequenceWithinBoundary(null, 42));
    }

    [Fact]
    public void Critical_save_flush_uses_a_larger_bounded_upload_batch()
    {
        Assert.Equal(1000,
            ReignWorldHistoryFlushPolicy.UploadBatchSize(urgent: false));
        Assert.Equal(5000,
            ReignWorldHistoryFlushPolicy.UploadBatchSize(urgent: true));
    }

    [Fact]
    public void Normal_uploads_coalesce_microbatches_but_urgent_flushes_do_not_wait()
    {
        Assert.True(ReignWorldHistoryFlushPolicy
            .ShouldCoalesceNormalUpload(false, 1, 0));
        Assert.True(ReignWorldHistoryFlushPolicy
            .ShouldCoalesceNormalUpload(false, 127, 1999));
        Assert.False(ReignWorldHistoryFlushPolicy
            .ShouldCoalesceNormalUpload(false, 128, 0));
        Assert.False(ReignWorldHistoryFlushPolicy
            .ShouldCoalesceNormalUpload(false, 1, 2000));
        Assert.False(ReignWorldHistoryFlushPolicy
            .ShouldCoalesceNormalUpload(true, 1, 0));
    }

    [Fact]
    public void Transport_stages_the_configured_upload_batch_instead_of_the_legacy_hundred_event_cap()
    {
        var workspace = TestOptions.FindWorkspace();
        var source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignWorldHistoryClient.cs",
            "/src/Modules/WorldSimulation/"));

        Assert.Contains("newlyQueued.Count < uploadBatchSize", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("newlyQueued.Count < 100", source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_outboxes_do_not_restart_a_worker_while_waiting_for_it_to_stop()
    {
        Assert.False(
            ReignWorldHistoryFlushPolicy.ShouldKickWorker(
                memoryPending: false,
                diskPending: false,
                workerRunning: false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Pending_work_starts_a_worker_only_when_one_is_not_already_running(
        bool memoryPending,
        bool diskPending)
    {
        Assert.True(
            ReignWorldHistoryFlushPolicy.ShouldKickWorker(
                memoryPending,
                diskPending,
                workerRunning: false));
        Assert.False(
            ReignWorldHistoryFlushPolicy.ShouldKickWorker(
                memoryPending,
                diskPending,
                workerRunning: true));
    }
}
