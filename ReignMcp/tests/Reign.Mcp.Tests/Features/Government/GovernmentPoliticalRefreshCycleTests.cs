using ReignBeta.Government;
using Reign.Core.Contracts.Government;
using Newtonsoft.Json.Linq;

namespace Reign.Mcp.Tests;

public sealed class GovernmentPoliticalRefreshCycleTests
{
    private static readonly DateTime Start = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

    private static GovernmentPoliticalProfile Profile(string hero = "hero", long revision = 1) => new() {
        HeroId = hero, CampaignId = "campaign", TimelineId = "timeline", Revision = revision,
        Values = Enum.GetValues<ReignGovernmentPlank>().Select(x => new GovernmentPoliticalValue { Value = x, Stance = 0, Importance = 50 }).ToList() };

    private static JObject Sent(GovernmentPoliticalProfile? profile, string hero = "hero") => new() {
        ["heroes"] = new JArray(new JObject { ["heroStringId"] = hero,
            ["appliedPoliticalProfile"] = profile == null ? JValue.CreateNull() : JObject.FromObject(profile) }) };
    private static JObject Response(params GovernmentPoliticalProfile[] profiles) => new() {
        ["campaignId"] = "campaign", ["timelineId"] = "timeline", ["profiles"] = JArray.FromObject(profiles) };

    [Fact]
    public void WorkerPreparationPreservesUnchangedProfilesAndDetachesChangedResponsesWithoutMutatingInputs()
    {
        var baseline = Profile();
        var sent = Sent(baseline);
        var unchanged = Response(baseline);
        ((JObject)((JArray)unchanged["profiles"]!)[0]!)["ignoredServerMetadata"] = "not a political field";
        Assert.False(Assert.Single(GovernmentPoliticalBatchPreparation.Prepare(sent, unchanged)).ChangedFromSent);
        var changed = Profile(revision: 2); changed.Values[0].Stance = 1;
        var response = Response(changed);
        string originalSent = sent.ToString(), originalResponse = response.ToString();
        var prepared = Assert.Single(GovernmentPoliticalBatchPreparation.Prepare(sent, response));
        Assert.True(prepared.ChangedFromSent);
        Assert.NotSame(changed, prepared.Profile);
        prepared.Profile.Values[0].Stance = 42;
        Assert.Equal(originalSent, sent.ToString());
        Assert.Equal(originalResponse, response.ToString());
        Assert.Equal(0, baseline.Values[0].Stance);
    }

    [Fact]
    public void PreparationRejectsUnsentForeignAndIncompleteProfilesAndBoundsResponseSize()
    {
        var baseline = Profile(); var sent = Sent(baseline);
        Assert.Empty(GovernmentPoliticalBatchPreparation.Prepare(sent, Response(Profile("unsent"))));
        var foreign = Profile(); foreign.CampaignId = "other";
        Assert.Empty(GovernmentPoliticalBatchPreparation.Prepare(sent, Response(foreign)));
        foreign = Profile(); foreign.TimelineId = "other";
        Assert.Empty(GovernmentPoliticalBatchPreparation.Prepare(sent, Response(foreign)));
        var incomplete = Profile(); incomplete.Values.RemoveAt(0);
        Assert.Empty(GovernmentPoliticalBatchPreparation.Prepare(sent, Response(incomplete)));
        Assert.Throws<InvalidOperationException>(() => GovernmentPoliticalBatchPreparation.Prepare(sent, Response(Enumerable.Repeat(baseline, 65).ToArray())));
    }

    [Fact]
    public void CommitGuardRejectsReplacedOrMutatedRevisionsAndAcceptsAnAbsentInitialBase()
    {
        var baseline = Profile();
        Assert.True(GovernmentPoliticalBatchPreparation.BaseStillCurrent(baseline, baseline, 1));
        Assert.False(GovernmentPoliticalBatchPreparation.BaseStillCurrent(Profile(), baseline, 1));
        baseline.Revision++;
        Assert.False(GovernmentPoliticalBatchPreparation.BaseStillCurrent(baseline, baseline, 1));
        Assert.True(GovernmentPoliticalBatchPreparation.BaseStillCurrent(null, null, 0));
        Assert.False(GovernmentPoliticalBatchPreparation.BaseStillCurrent(Profile(), null, 0));
        Assert.False(GovernmentPoliticalBatchPreparation.BaseStillCurrent(null, baseline, 1));
        Assert.True(Assert.Single(GovernmentPoliticalBatchPreparation.Prepare(Sent(null), Response(Profile()))).ChangedFromSent);
    }

    [Fact]
    public void SlowFiveThousandCharacterSweepFinishesWithoutRefreshingItsFirstBatchesAgain()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var acknowledgements = new DateTime?[5000];
        var now = Start;
        cycle.Observe(now, 10);
        for (int offset = 0; offset < acknowledgements.Length; offset += 64)
        {
            cycle.Observe(now, 10);
            Assert.All(acknowledgements.Take(offset), last => Assert.False(cycle.NeedsRefresh(last)));
            int end = Math.Min(offset + 64, acknowledgements.Length);
            for (int i = offset; i < end; i++)
            {
                Assert.True(cycle.NeedsRefresh(acknowledgements[i]));
                acknowledgements[i] = now;
            }
            now = now.AddSeconds(8);
        }
        Assert.True(now - Start > TimeSpan.FromMinutes(5));
        Assert.True(cycle.CompleteIfAcknowledged(acknowledgements, now));
        cycle.Observe(now.AddMinutes(4), 10);
        Assert.All(acknowledgements, last => Assert.False(cycle.NeedsRefresh(last)));
    }

    [Fact]
    public void QuietIntervalStartsAfterTheFinalAcknowledgement()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        var finished = Start.AddMinutes(12);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, finished }, finished));
        cycle.Observe(finished.AddMinutes(5).AddTicks(-1), 10);
        Assert.False(cycle.NeedsRefresh(Start));
        cycle.Observe(finished.AddMinutes(5), 10);
        Assert.True(cycle.NeedsRefresh(Start));
        Assert.True(cycle.NeedsRefresh(finished));
    }

    [Fact]
    public void MissingOrInvalidatedAcknowledgementRemainsUrgentDuringTheQuietInterval()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        cycle.Observe(Start.AddSeconds(1), 10);
        Assert.False(cycle.NeedsRefresh(Start));
        Assert.True(cycle.NeedsRefresh(null));
    }

    [Fact]
    public void UnseenPoliticalEvidenceRemainsUrgentDespiteAFreshCacheAcknowledgement()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        Assert.True(cycle.NeedsRefresh(Start, required: true));
        Assert.False(cycle.NeedsRefresh(Start, required: false));
    }

    [Fact]
    public void NewPoliticalIntervalImmediatelyStartsAFreshSweep()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        cycle.Observe(Start.AddSeconds(1), 11);
        Assert.True(cycle.NeedsRefresh(Start));
        Assert.False(cycle.NeedsRefresh(Start.AddSeconds(1)));
    }

    [Fact]
    public void UnacknowledgedLivingCharactersPreventCompletion()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, null }, Start.AddMinutes(8)));
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, Start.AddTicks(-1) }, Start.AddMinutes(8)));
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, Start.AddMinutes(8) }, Start.AddMinutes(8)));
    }

    [Fact]
    public void RemovedCharactersDoNotRetainARefreshOrSaveBlock()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { Start, null }, Start));
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start.AddSeconds(1)));
    }

    [Fact]
    public void EmptyPopulationIsQuietAndRepeatedCompletionCannotExtendTheTimer()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 10);
        Assert.True(cycle.CompleteIfAcknowledged(Array.Empty<DateTime?>(), Start));
        Assert.False(cycle.CompleteIfAcknowledged(Array.Empty<DateTime?>(), Start.AddMinutes(4)));
        cycle.Observe(Start.AddMinutes(5), 10);
        Assert.True(cycle.NeedsRefresh(Start));
    }

    [Fact]
    public void Active_finite_sweep_has_no_success_idle_and_retains_completion_cadence()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        cycle.Observe(now, 1);
        int acknowledged = 0, batches = 0;
        while (acknowledged < 4937)
        {
            acknowledged += Math.Min(64, 4937 - acknowledged);
            now = now.AddMilliseconds(100); // Explicit bounded transport/capture fixture.
            Assert.Equal(now, cycle.RetryAfterBatch(now, true, true));
            batches++;
        }
        Assert.Equal(78, batches);
        Assert.True(cycle.CompleteIfAcknowledged(Enumerable.Repeat<DateTime?>(now, 4937), now));
        Assert.Equal(now.AddSeconds(.75), cycle.RetryAfterBatch(now, true));
        cycle.Observe(now.AddMinutes(4), 1);
        Assert.False(cycle.Active);
        cycle.Observe(now.AddMinutes(5), 1);
        Assert.True(cycle.Active);
    }

    [Fact]
    public void Failed_batch_and_new_interval_keep_backoff_and_reject_old_acknowledgements()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        cycle.Observe(now, 1);
        Assert.Equal(now.AddSeconds(30), cycle.RetryAfterBatch(now, false));
        var next = now.AddSeconds(31);
        cycle.Observe(next, 2);
        Assert.Equal(next, cycle.RetryAfterBatch(next, true, true));
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { now }, next));
        Assert.True(cycle.NeedsRefresh(now));
    }

    [Fact]
    public void No_acknowledgement_progress_never_creates_a_hot_resend_loop()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var now = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        cycle.Observe(now, 1);
        Assert.Equal(now.AddSeconds(.75), cycle.RetryAfterBatch(now, true, false));
        Assert.Equal(now.AddSeconds(30), cycle.RetryAfterBatch(now, false, false));
    }

    [Fact]
    public void Refresh_timer_starts_after_a_long_sweep_completes()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        var start = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
        cycle.Observe(start, 1);
        var finish = start.AddMinutes(4.5);
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { finish }, finish));
        cycle.Observe(start.AddMinutes(5), 1);
        Assert.False(cycle.Active);
        cycle.Observe(finish.AddMinutes(5), 1);
        Assert.True(cycle.Active);
    }

    [Fact]
    public void Repeated_acknowledgements_with_pending_evidence_do_not_advance_the_sweep()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        cycle.Observe(Start, 1);
        DateTime? acknowledged = null;
        var first = Start.AddSeconds(1);
        Assert.True(cycle.AcknowledgementAdvancesSweep(acknowledged));
        Assert.Equal(first, cycle.RetryAfterBatch(first, true, cycle.AcknowledgementAdvancesSweep(acknowledged)));
        acknowledged = first;
        // Pending evidence can keep selecting this hero; neither a duplicate
        // response row nor a later repeated batch is new cohort progress.
        Assert.True(cycle.NeedsRefresh(acknowledged, required: true));
        for (int duplicate = 0; duplicate < 3; duplicate++)
        {
            var later = first.AddSeconds(duplicate + 1);
            Assert.False(cycle.AcknowledgementAdvancesSweep(acknowledged));
            Assert.Equal(later.AddSeconds(.75), cycle.RetryAfterBatch(later, true, cycle.AcknowledgementAdvancesSweep(acknowledged)));
            acknowledged = later;
        }
        Assert.False(cycle.CompleteIfAcknowledged(new DateTime?[] { acknowledged, null }, first.AddMinutes(1)));
        Assert.True(cycle.AcknowledgementAdvancesSweep(null)); // A different unacknowledged hero still progresses.
    }

    [Fact]
    public void Progress_requires_an_active_sweep_and_an_acknowledgement_older_than_its_start()
    {
        var cycle = new GovernmentPoliticalRefreshCycle();
        Assert.False(cycle.AcknowledgementAdvancesSweep(null));
        cycle.Observe(Start, 1);
        Assert.True(cycle.AcknowledgementAdvancesSweep(Start.AddTicks(-1)));
        Assert.False(cycle.AcknowledgementAdvancesSweep(Start));
        Assert.False(cycle.AcknowledgementAdvancesSweep(Start.AddTicks(1)));
        Assert.True(cycle.CompleteIfAcknowledged(new DateTime?[] { Start }, Start));
        Assert.False(cycle.AcknowledgementAdvancesSweep(null));
        cycle.Observe(Start.AddSeconds(1), 2);
        Assert.True(cycle.AcknowledgementAdvancesSweep(Start));
    }
}
