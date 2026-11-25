using ReignLiveTest;
using ReignBeta.Shared.Characters;
using System.Text.Json;

namespace Reign.Mcp.Tests;

public sealed class PassiveWorldCheckpointPolicyTests
{
    [Theory]
    [InlineData(false, 0, 0, 0, false, true, "")]
    [InlineData(true, -1, 0, 0, false, true, "")]
    [InlineData(true, 1, 0, 0, false, true, "")]
    [InlineData(true, 0, -1, 0, false, true, "")]
    [InlineData(true, 0, 1, 0, false, true, "")]
    [InlineData(true, 0, 0, -1, false, true, "")]
    [InlineData(true, 0, 0, 1, false, true, "")]
    [InlineData(true, 0, 0, 0, true, true, "")]
    [InlineData(true, 0, 0, 0, null, true, "")]
    [InlineData(true, 0, 0, 0, false, false, "")]
    [InlineData(true, 0, 0, 0, false, true, "transport failure")]
    [InlineData(true, 0, 0, 0, false, true, null)]
    public void PendingOrUnobservableGossipBlocksCheckpoint(bool valid, long pending, long ready, long due,
        bool? inFlight, bool current, string? error) => Assert.False(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(
            valid, true, true, pending, ready, due, inFlight, current, error!));

    [Fact]
    public void FutureCorrespondenceAndAnUninitializedEmptySchemaPermitCheckpoint()
    {
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, true, 0, 0, 0, false, true, ""));
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, true, false, 0, 0, 0, false, false, ""));
        Assert.True(PassiveWorldCheckpointPolicy.PersonalGossipQuiescent(true, false, true, 1, 1, 1, false, false, ""));
    }
    [Theory]
    [InlineData("", true, true, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", false, true, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, null, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, false, false, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, null, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, true, 0, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, -1, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 1, 0, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, -1, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 1, "")]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 0, null)]
    [InlineData("reign-government-runtime-v1", true, true, false, 0, 0, "sync failed")]
    public void MissingBusyOrFailedGovernmentRefusesCheckpoint(string schema, bool available,
        bool? calendar, bool? inFlight, long missing, long pending, string? error)
    {
        Assert.False(PassiveWorldCheckpointPolicy.GovernmentQuiescent(schema, available, calendar, inFlight, missing, pending, error!));
    }

    [Fact]
    public void AppliedGovernmentStateAllowsOtherCheckpointGates()
    {
        Assert.True(PassiveWorldCheckpointPolicy.GovernmentQuiescent("reign-government-runtime-v1", true, true, false, 0, 0, ""));
    }

    [Theory]
    [InlineData("", true, true, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", false, true, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, null, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, null, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, -1, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, -1, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, false, false, 0, 0, 0, 0, 0, "npc_lives_snapshot_pending")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, true, 0, 0, 0, 0, 0, "npc_lives_mutation_in_flight")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 2, 0, 0, 0, 0, "npc_lives_receipts_pending")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 1, 0, 0, 0, "npc_lives_native_rewards_due")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, 0, 0, 0, "")]
    [InlineData("reign-npc-lives-checkpoint-v1", true, true, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, -1, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, 1, 0, 0, "npc_lives_native_personal_travel_due")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 1, 1, 0, 0, "npc_lives_native_rewards_due")]
    [InlineData("reign-npc-lives-checkpoint-v2", true, true, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, 0, -1, 0, "npc_lives_telemetry_unavailable")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, 0, 1, 0, "npc_lives_native_meeting_release_due")]
    [InlineData("reign-npc-lives-checkpoint-v4", true, true, false, 0, 0, 1, 1, 0, "npc_lives_native_personal_travel_due")]
    [InlineData("reign-npc-lives-checkpoint-v3", true, true, false, 0, 0, 0, 0, 0, "npc_lives_telemetry_unavailable")]
    public void NpcLivesCheckpointNeedsScopedIdleNativeState(string schema, bool current, bool? snapshotReady,
        bool? mutation, long receipts, long rewards, long personalTravel, long meetingRelease, long weddingRelease,
        string expected) => Assert.Equal(expected, NpcLivesCheckpointPolicy.Blocker(schema, current, snapshotReady,
            mutation, receipts, rewards, personalTravel, meetingRelease, weddingRelease));

    [Theory]
    [InlineData(10.99, true, false, false, false, "")]
    [InlineData(11.0, true, false, false, false, "npc_lives_native_meeting_release_due")]
    [InlineData(12.0, true, false, false, false, "npc_lives_native_meeting_release_due")]
    [InlineData(11.0, false, false, false, false, "")]
    [InlineData(11.0, true, false, true, false, "")]
    [InlineData(11.0, false, true, true, false, "npc_lives_native_meeting_release_due")]
    [InlineData(12.0, true, false, false, true, "")]
    public void RetainedMeetingBlocksOnlyARealEligibleRelease(double day, bool nativeAdvance, bool interrupt,
        bool campBattle, bool completed, string expected)
    {
        var meeting = new NpcSummonedMeeting
        {
            Id = "owned_meeting", IssuerId = "issuer", TargetId = "guest",
            DestinationId = "actual_town", Revision = 4, StayDays = 1d
        };
        Assert.True(meeting.ScheduleArrival(9d, 1d));
        Assert.True(meeting.Arrive("guest", "actual_town", 10d, true));
        if (completed) Assert.True(meeting.Complete(11d));
        string retained = JsonSerializer.Serialize(meeting);
        var loaded = JsonSerializer.Deserialize<NpcSummonedMeeting>(retained)!;

        bool due = loaded.CanComplete(day)
            && NpcLivesCheckpointPolicy.MeetingReleaseCanDrain(interrupt, nativeAdvance, campBattle);
        Assert.Equal(string.Empty, NpcLivesCheckpointPolicy.Blocker(NpcLivesCheckpointPolicy.Schema,
            true, true, false, 0, 0, 0, 0, 0));
        Assert.Equal(expected, NpcLivesCheckpointPolicy.Blocker(NpcLivesCheckpointPolicy.Schema,
            true, true, false, 0, 0, 0, due ? 1 : 0, 0));
        Assert.Equal(retained, JsonSerializer.Serialize(loaded));
    }

    [Theory]
    [InlineData(0, -1, "npc_lives_telemetry_unavailable")]
    [InlineData(0, 0, "")]
    [InlineData(0, 1, "npc_lives_native_wedding_attendance_release_due")]
    [InlineData(1, 1, "npc_lives_native_meeting_release_due")]
    public void ScopedCheckpointRequiresWeddingAttendanceReleaseCount(long meetingRelease, long weddingRelease, string expected)
    {
        Assert.Equal(expected, NpcLivesCheckpointPolicy.Blocker(NpcLivesCheckpointPolicy.Schema, true, true,
            false, 0, 0, 0, meetingRelease, weddingRelease));
    }

    [Fact]
    public void WeddingAttendanceObservationSharesTheSafeTickReleasePredicate()
    {
        string root = TestOptions.FindWorkspace();
        string characters = Path.Combine(root, "ReignBeta/src/Modules/Characters/Campaign");
        string travel = File.ReadAllText(Path.Combine(characters, "ReignNpcLivesTravel.cs"));
        int predicate = travel.IndexOf("private static bool WeddingAttendanceNeedsRelease(", StringComparison.Ordinal);
        int count = travel.IndexOf("private int CountDueWeddingAttendanceReleases()", StringComparison.Ordinal);
        int refresh = travel.IndexOf("private void RefreshWeddingAttendance()", StringComparison.Ordinal);
        int restore = travel.IndexOf("private void RestoreReleasedMeetingDuties(", StringComparison.Ordinal);
        Assert.True(predicate >= 0 && count > predicate && refresh > count && restore > refresh);

        string releaseCheck = travel[predicate..count];
        Assert.Contains("OwnsWeddingAttendance(ownerId) != true", releaseCheck, StringComparison.Ordinal);
        Assert.Contains("|| !Free(hero) || hero.CurrentSettlement != town || !SafePlace(hero, town)", releaseCheck,
            StringComparison.Ordinal);

        string observation = travel[count..refresh];
        int eligible = observation.IndexOf("!TravelSafe() || !LifeTravelTickDue(CampaignTime.Now.ToDays)",
            StringComparison.Ordinal);
        const string samePredicate = "WeddingAttendanceNeedsRelease(row.Value.OwnerId, hero, town)";
        Assert.True(eligible >= 0 && observation.IndexOf(samePredicate, StringComparison.Ordinal) > eligible);
        Assert.DoesNotContain("ReleaseWeddingGuest(", observation, StringComparison.Ordinal);
        Assert.DoesNotContain("HoldMeetingHero(", observation, StringComparison.Ordinal);
        Assert.Contains(samePredicate, travel[refresh..restore], StringComparison.Ordinal);
        Assert.Contains("ReleaseWeddingGuest(row.Key, row.Value)", travel[refresh..restore], StringComparison.Ordinal);

        string native = File.ReadAllText(Path.Combine(characters, "ReignNpcLivesBehavior.cs"));
        Assert.Contains("""["dueNativeWeddingAttendanceRelease"] = sample.WeddingAttendanceReleases""", native,
            StringComparison.Ordinal);
        int readyCheck = native.IndexOf("if (!ready) return default;", StringComparison.Ordinal);
        int weddingCount = native.IndexOf("CountDueWeddingAttendanceReleases()", StringComparison.Ordinal);
        Assert.True(readyCheck >= 0 && weddingCount > readyCheck);
        Assert.Contains("sample.WeddingAttendanceReleases);", native, StringComparison.Ordinal);

        string controller = File.ReadAllText(Path.Combine(root,
            "ReignServer/tests/ReignLiveTest/Features/WorldSimulation/PassiveWorldControl.cs"));
        Assert.Contains("""Count("dueNativeWeddingAttendanceRelease")""", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void MeetingObservationKeepsV3WireOutsideTheV4CheckpointGate()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Characters/Campaign/ReignNpcLivesBehavior.cs"));
        Assert.Equal("reign-npc-lives-checkpoint-v3", NpcLivesCheckpointPolicy.MeetingObservationSchema);
        Assert.Equal("reign-npc-lives-checkpoint-v4", NpcLivesCheckpointPolicy.Schema);
        Assert.Contains("meetingOnly ? NpcLivesCheckpointPolicy.MeetingObservationSchema : NpcLivesCheckpointPolicy.Schema",
            native, StringComparison.Ordinal);
        Assert.Contains("if (!meetingOnly)", native, StringComparison.Ordinal);
        Assert.Contains("ReadPassiveCheckpointWork(meetingOnly: true)", native, StringComparison.Ordinal);
        Assert.Contains("meetingOnly ? 0 : CountDueWeddingAttendanceReleases()", native, StringComparison.Ordinal);
        Assert.Contains("PassiveCheckpointTelemetry(sample, meetingOnly: true)", native, StringComparison.Ordinal);
        Assert.Contains("Blocker(NpcLivesCheckpointPolicy.Schema", native, StringComparison.Ordinal);
        string checkpoints = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.Checkpoints.cs"));
        Assert.Contains("""["npcLivesSchema"] = NpcLivesCheckpointPolicy.MeetingObservationSchema""",
            checkpoints, StringComparison.Ordinal);
        Assert.Equal(NpcLivesCheckpointPolicy.TelemetryUnavailable, NpcLivesCheckpointPolicy.Blocker(
            NpcLivesCheckpointPolicy.MeetingObservationSchema, true, true, false, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void OwnedNativeSaveRechecksAtTheFinalSubmissionBoundary()
    {
        string path = Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.Checkpoints.cs");
        string source = File.ReadAllText(path);
        int queue = source.IndexOf("bool queued = await ReignMainThread.InvokeAsync", StringComparison.Ordinal);
        Assert.True(queue >= 0);
        int end = source.IndexOf("}).ConfigureAwait(false);", queue, StringComparison.Ordinal);
        Assert.True(end > queue);
        string block = source[queue..end];
        int owner = block.IndexOf("if (ownership != null && !ValidCheckpointDestination(ownership, saveName))", StringComparison.Ordinal);
        int sample = block.IndexOf("NpcLives?.PassiveCheckpointBlocker(", StringComparison.Ordinal);
        int refuse = block.IndexOf("if (npcLivesBlocker.Length > 0) return false;", StringComparison.Ordinal);
        int sequence = block.IndexOf("expectedSequence = ReignSaveSyncCampaignBehavior.NativeSaveObservation()", StringComparison.Ordinal);
        int expect = block.IndexOf("ExpectNativeSaveName(saveName);", StringComparison.Ordinal);
        int save = block.IndexOf("SaveHandler.SaveAs(saveName);", StringComparison.Ordinal);
        Assert.True(owner >= 0 && sample > owner && refuse > sample && sequence > refuse && expect > sequence && save > expect);
        Assert.Contains("CheckpointNotQueuedEvidence(ownership, new JObject { [\"npcLivesCheckpointBlocker\"] = npcLivesBlocker })",
            source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(20)]
    public void MissingOrFailedActionHealthRefusesCheckpoint(long failures)
    {
        Assert.NotEmpty(PassiveWorldCheckpointPolicy.ActionFailureError(failures));
    }

    [Fact]
    public void NoActionFailuresAllowsOtherQuiescenceChecks()
    {
        Assert.Empty(PassiveWorldCheckpointPolicy.ActionFailureError(0));
    }
}
