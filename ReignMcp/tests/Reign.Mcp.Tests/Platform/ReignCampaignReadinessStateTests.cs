using ReignBeta.Integration;

namespace Reign.Mcp.Tests;

public sealed class ReignCampaignReadinessStateTests
{
    [Fact]
    public void Campaign_preparation_can_start_before_the_map_screen_exists()
    {
        Assert.True(ReignCampaignPreparationStartPolicy.CanStart(
            sessionLaunched: true,
            campaignAvailable: true,
            playerAvailable: true,
            saveSyncReady: true,
            pipelineStarted: false));
    }

    [Fact]
    public void Personality_completion_cannot_release_the_campaign()
    {
        var state = ReignCampaignReadinessState.Start("campaign-a", "generation-a");

        state.Advance(ReignInitializationStage.Personalities);

        Assert.False(state.CanRelease);
        Assert.Equal(ReignInitializationStage.NativeFoundations, state.RequiredStage);
    }

    [Fact]
    public void Release_requires_two_identical_quiescent_observations_and_server_ack()
    {
        var state = ReadyThroughCriticalDrain();
        var idle = ReignInitializationQueueObservation.Idle(historySequence: 42);

        Assert.False(state.ObserveQuiescence(idle));
        Assert.True(state.ObserveQuiescence(idle));
        Assert.False(state.CanRelease);

        state.AcceptServerAcknowledgement(
            "campaign-a",
            "main-a",
            42,
            sealVersion: ReignCampaignReadinessState.CurrentSealVersion);

        Assert.True(state.CanRelease);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    public void Journal_selects_expected_load_mode(int sealVersion, int expected)
    {
        Assert.Equal((ReignInitializationMode)expected, ReignInitializationJournal.SelectLoadMode(
            sealVersion,
            personalitiesComplete: true,
            establishedCampaign: true));
    }

    [Fact]
    public void Sealed_load_cannot_release_before_reopening_its_timeline()
    {
        var state = ReignCampaignReadinessState.Resume(
            SealedJournal("main-a", historySequence: 42),
            "campaign-a",
            "generation-a");

        Assert.True(state.RequiresTimelineHandshake);
        Assert.False(state.CanRelease);
    }

    [Fact]
    public void Matching_timeline_handshake_restores_the_sealed_fast_path()
    {
        var state = ReignCampaignReadinessState.Resume(
            SealedJournal("main-a", historySequence: 42),
            "campaign-a",
            "generation-a");

        Assert.True(state.AcceptTimelineHandshake("generation-a", "main-a"));

        Assert.False(state.RequiresTimelineHandshake);
        Assert.True(state.CanRelease);
        Assert.Equal(ReignInitializationStage.Ready, state.RequiredStage);
        Assert.Equal(42, state.SealedHistorySequence);
    }

    [Fact]
    public void Branched_timeline_invalidates_downstream_readiness_and_resumes_at_identity()
    {
        var state = ReignCampaignReadinessState.Resume(
            SealedJournal("main-a", historySequence: 42),
            "campaign-a",
            "generation-a");

        Assert.True(state.AcceptTimelineHandshake("generation-a", "main-a_branch_1"));

        Assert.False(state.RequiresTimelineHandshake);
        Assert.False(state.CanRelease);
        Assert.Equal("main-a_branch_1", state.TimelineId);
        Assert.Equal(ReignInitializationStage.Identity, state.RequiredStage);
        Assert.Equal(0, state.SealedHistorySequence);
        Assert.Equal(
            ReignInitializationStageMask.Personalities
            | ReignInitializationStageMask.NativeFoundations
            | ReignInitializationStageMask.Timeline,
            state.CompletedStageMask);
    }

    [Fact]
    public void Stale_timeline_handshake_cannot_mutate_the_active_generation()
    {
        var state = ReignCampaignReadinessState.Resume(
            SealedJournal("main-a", historySequence: 42),
            "campaign-a",
            "generation-new");

        Assert.False(state.AcceptTimelineHandshake("generation-old", "main-a_branch_1"));

        Assert.True(state.RequiresTimelineHandshake);
        Assert.Equal("main-a", state.TimelineId);
        Assert.False(state.CanRelease);
    }

    [Fact]
    public void Stale_generation_cannot_advance_active_state()
    {
        var state = ReignCampaignReadinessState.Start("campaign-a", "generation-new");

        Assert.False(state.TryAdvance("generation-old", ReignInitializationStage.Personalities));
        Assert.Equal(ReignInitializationStage.Personalities, state.RequiredStage);
    }

    [Fact]
    public void Partial_save_with_personalities_complete_resumes_at_native_foundations()
    {
        var journal = new ReignInitializationJournal
        {
            SealVersion = 0,
            CompletedStageMask = ReignInitializationStageMask.Personalities
        };

        var state = ReignCampaignReadinessState.Resume(
            journal,
            "campaign-a",
            "generation-a");

        Assert.Equal(ReignInitializationStage.NativeFoundations, state.RequiredStage);
    }

    [Fact]
    public void Authoritative_response_for_previous_generation_is_rejected()
    {
        var state = ReignCampaignReadinessState.Start("campaign-a", "generation-new");

        Assert.False(state.AcceptStageResult(
            "generation-old",
            ReignInitializationStage.Personalities,
            success: true));
        Assert.Equal(ReignInitializationStage.Personalities, state.RequiredStage);
    }

    [Fact]
    public void New_work_between_idle_observations_resets_stability()
    {
        var state = ReadyThroughCriticalDrain();

        Assert.False(state.ObserveQuiescence(ReignInitializationQueueObservation.Idle(42)));
        Assert.False(state.ObserveQuiescence(new ReignInitializationQueueObservation
        {
            HistorySequence = 42,
            NativeReceiptCount = 1
        }));
        Assert.False(state.ObserveQuiescence(ReignInitializationQueueObservation.Idle(42)));
    }

    [Fact]
    public void Failed_native_projection_blocks_seal()
    {
        var observation = ReignInitializationQueueObservation.Idle(42);
        observation.NativeProjectionFailureCount = 1;

        Assert.False(observation.IsQuiescent);
    }

    [Fact]
    public void Only_matching_generation_with_issued_release_token_can_mark_ready()
    {
        var authorization = ReignInitializationAuthorization.Begin("generation-a");

        Assert.False(authorization.CanRelease(
            "generation-a",
            default(ReignInitializationReleaseToken)));
        Assert.False(authorization.CanRelease(
            "generation-b",
            authorization.IssueReleaseToken("generation-a")));
        Assert.True(authorization.CanRelease(
            "generation-a",
            authorization.IssueReleaseToken("generation-a")));
    }

    private static ReignCampaignReadinessState ReadyThroughCriticalDrain()
    {
        var state = ReignCampaignReadinessState.Start("campaign-a", "generation-a");
        state.SetTimeline("main-a");
        state.Advance(ReignInitializationStage.Personalities);
        state.Advance(ReignInitializationStage.NativeFoundations);
        state.Advance(ReignInitializationStage.Timeline);
        state.Advance(ReignInitializationStage.Identity);
        state.Advance(ReignInitializationStage.AuthoritativeSnapshots);
        state.Advance(ReignInitializationStage.Relationships);
        state.Advance(ReignInitializationStage.CriticalDrain);
        state.Advance(ReignInitializationStage.InitialSocialWorld);
        return state;
    }

    private static ReignInitializationJournal SealedJournal(
        string timelineId,
        long historySequence)
    {
        return new ReignInitializationJournal
        {
            SealVersion = ReignCampaignReadinessState.CurrentSealVersion,
            CampaignId = "campaign-a",
            TimelineId = timelineId,
            CompletedStageMask =
                ReignInitializationStageMask.Personalities
                | ReignInitializationStageMask.NativeFoundations
                | ReignInitializationStageMask.Timeline
                | ReignInitializationStageMask.Identity
                | ReignInitializationStageMask.AuthoritativeSnapshots
                | ReignInitializationStageMask.Relationships
                | ReignInitializationStageMask.CriticalDrain
                | ReignInitializationStageMask.InitialSocialWorld
                | ReignInitializationStageMask.ServerAcknowledgement,
            SealedHistorySequence = historySequence
        };
    }
}
