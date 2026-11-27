using System.Text.Json;
using ReignBeta.Shared.Rulers;
using Reign.Mcp.Server;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class RulerPolicyTests
{
    [Theory]
    [InlineData(-50, 1, false)]
    [InlineData(0, 1, false)]
    [InlineData(25, 1, false)]
    [InlineData(26, 1, true)]
    [InlineData(26, 2, false)]
    [InlineData(75, 50, true)]
    [InlineData(75, 51, false)]
    [InlineData(100, 75, true)]
    [InlineData(100, 76, false)]
    [InlineData(100, 0, false)]
    public void Influence_uses_the_relation_percentage_minus_twenty_five(int relation, int roll, bool success)
        => Assert.Equal(success, ReignRulerPolicy.InfluenceSucceeds(relation, roll));

    [Theory]
    [InlineData(false, false, RulerVerdict.Submission)]
    [InlineData(true, false, RulerVerdict.Freedom)]
    [InlineData(true, true, RulerVerdict.Reunification)]
    public void Influence_reopens_neither_the_attempt_nor_the_preference_after_succession(
        bool civilWar, bool rebelVictory, RulerVerdict verdict)
    {
        var j = new RulerJudgment { Id = "first", VictorId = "ruler", CapturedDay = 100,
            Stage = RulerJudgmentStage.AwaitingDecision, CivilWar = civilWar, RebelVictory = rebelVictory };
        Assert.False(ReignRulerPolicy.InfluenceOpen(j, 99));
        Assert.True(ReignRulerPolicy.InfluenceOpen(j, 106.999));
        Assert.False(ReignRulerPolicy.InfluenceOpen(j, 107));
        j.PendingVerdict = RulerVerdict.CleanExecution;
        Assert.False(ReignRulerPolicy.InfluenceOpen(j, 105));
        j.PendingVerdict = null;
        j.Influence = new RulerJudgmentInfluence { OperationId = "appeal", RulerId = "ruler", Verdict = verdict };
        Assert.Null(ReignRulerPolicy.InfluencedVerdict(j));
        Assert.False(ReignRulerPolicy.InfluenceOpen(j, 105));
        j.Influence.Succeeded = true;
        var saved = JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        Assert.False(ReignRulerPolicy.InfluenceOpen(saved, 105));
        Assert.Equal(verdict, ReignRulerPolicy.InfluencedVerdict(saved));
        Assert.Null(saved.PendingVerdict);
        ReignRulerPolicy.SetDecisionAuthority(saved, "successor");
        ReignRulerPolicy.SetDecisionAuthority(saved, "ruler");
        Assert.Null(ReignRulerPolicy.InfluencedVerdict(saved));
        Assert.Equal("appeal", saved.Influence!.OperationId);
        Assert.False(ReignRulerPolicy.InfluenceOpen(saved, 105));
        Assert.True(ReignRulerPolicy.InfluenceOpen(new RulerJudgment {
            Id = "recaptured", CapturedDay = 110, Stage = RulerJudgmentStage.AwaitingDecision }, 111));
    }

    [Theory]
    [InlineData(106.999, false)] [InlineData(107, true)] [InlineData(107.001, true)]
    public void Captivity_decision_waits_seven_full_days(double now, bool due)
    {
        var j = new RulerJudgment { CapturedDay = 100, Stage = RulerJudgmentStage.AwaitingDecision };
        Assert.Equal(due, ReignRulerPolicy.DecisionDue(j, now));
        j.Stage = RulerJudgmentStage.Cancelled;
        Assert.False(ReignRulerPolicy.DecisionDue(j, now));
    }

    [Fact]
    public void Missing_legacy_capture_time_starts_a_full_window_and_persists_intended_verdict()
    {
        var j = JsonSerializer.Deserialize<RulerJudgment>("{\"Stage\":1}")!;
        Assert.False(ReignRulerPolicy.DecisionDue(j, 100));
        ReignRulerPolicy.EnsureCaptureDay(j, 100.25);
        j.PendingVerdict = RulerVerdict.CleanExecution;
        var loaded = JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        Assert.Equal(100.25, loaded.CapturedDay);
        Assert.Equal(RulerVerdict.CleanExecution, loaded.PendingVerdict);
        Assert.False(ReignRulerPolicy.DecisionDue(loaded, 107.249));
        Assert.True(ReignRulerPolicy.DecisionDue(loaded, 107.25));
        ReignRulerPolicy.EnsureCaptureDay(loaded, 90);
        Assert.Equal(100.25, loaded.CapturedDay);
        Assert.False(ReignRulerPolicy.DecisionDue(loaded, double.NaN));
        Assert.True(ReignRulerPolicy.Cancel(loaded, "escaped_or_rescued"));
        Assert.False(ReignRulerPolicy.DecisionDue(loaded, 200));
    }

    [Fact]
    public void Execution_has_eight_more_days_while_an_existing_absolute_deadline_is_preserved()
    {
        Assert.Equal(7, ReignRulerPolicy.DecisionDays);
        Assert.Equal(8, ReignRulerPolicy.ExecutionDays);
        var j = new RulerJudgment { CapturedDay = 100, SentencedDay = 107, Deadline = 107 + ReignRulerPolicy.ExecutionDays,
            Stage = RulerJudgmentStage.Sentenced, Verdict = RulerVerdict.CleanExecution };
        Assert.False(ReignRulerPolicy.SentenceDue(j, 114.999));
        Assert.True(ReignRulerPolicy.SentenceDue(j, 115));
        j.Deadline = 122; // An existing save's former fifteen-day sentence.
        var loaded = JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        Assert.False(ReignRulerPolicy.SentenceDue(loaded, 115));
        Assert.True(ReignRulerPolicy.SentenceDue(loaded, 122));
    }

    [Fact]
    public void Sovereign_handover_starts_the_clock_and_legacy_migration_preserves_sentences()
    {
        var j = new RulerJudgment { Stage = RulerJudgmentStage.AwaitingAuthority, CapturedDay = 99 };
        ReignRulerPolicy.RestoreDecisionCustodyClock(j);
        ReignRulerPolicy.EnsureCaptureDay(j, 100.5);
        Assert.Equal(-1, j.CapturedDay);
        Assert.False(ReignRulerPolicy.DecisionDue(j, 110));
        j.Stage = RulerJudgmentStage.AwaitingDecision;
        ReignRulerPolicy.EnsureCaptureDay(j, 110);
        var reloaded = JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        ReignRulerPolicy.RestoreDecisionCustodyClock(reloaded);
        ReignRulerPolicy.EnsureCaptureDay(reloaded, 112);
        Assert.Equal(110, reloaded.CapturedDay);
        Assert.False(ReignRulerPolicy.DecisionDue(reloaded, 116.999));
        Assert.True(ReignRulerPolicy.DecisionDue(reloaded, 117));

        var oldPending = JsonSerializer.Deserialize<RulerJudgment>("{\"Stage\":1,\"CapturedDay\":99}")!;
        ReignRulerPolicy.RestoreDecisionCustodyClock(oldPending);
        Assert.False(ReignRulerPolicy.DecisionDue(oldPending, 110));
        ReignRulerPolicy.EnsureCaptureDay(oldPending, 111);
        Assert.Equal(111, oldPending.CapturedDay);
        j.Stage = RulerJudgmentStage.Sentenced;
        j.Deadline = 122;
        ReignRulerPolicy.RestoreDecisionCustodyClock(j);
        ReignRulerPolicy.EnsureCaptureDay(j, 120);
        Assert.Equal(110, j.CapturedDay);
        Assert.Equal(122, j.Deadline);
    }

    [Fact]
    public void Clan_appeals_are_independent_and_bound_to_the_saved_clan_leader_and_sovereign()
    {
        var j = new RulerJudgment { Id = "lead", VictorId = "ruler", CapturedDay = 100,
            Stage = RulerJudgmentStage.AwaitingDecision, CivilWar = true,
            Influence = new RulerJudgmentInfluence { OperationId = "lead-appeal", RulerId = "ruler" } };
        j.ClanInfluences.Add("clan-a", new RulerJudgmentInfluence { RulerId = "ruler", SubjectId = "leader-a",
            OperationId = "appeal-a", Verdict = RulerVerdict.Freedom, Chance = 50, Roll = 40, Succeeded = true });
        j.ClanInfluences.Add("clan-b", new RulerJudgmentInfluence { RulerId = "ruler", SubjectId = "leader-b",
            OperationId = "appeal-b", Verdict = RulerVerdict.CleanExecution, Chance = 50, Roll = 70 });
        Assert.False(ReignRulerPolicy.InfluenceOpen(j, 106));
        Assert.True(ReignRulerPolicy.AppealWindowOpen(j, 106));
        Assert.False(ReignRulerPolicy.AppealWindowOpen(j, 107));

        var loaded = JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        loaded.Stage = RulerJudgmentStage.Applying;
        Assert.False(ReignRulerPolicy.AppealWindowOpen(loaded, 106));
        Assert.Equal("appeal-a", loaded.ClanInfluences["clan-a"].OperationId);
        Assert.Equal(40, loaded.ClanInfluences["clan-a"].Roll);
        Assert.Equal(RulerVerdict.Freedom, ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-a", "leader-a", "ruler", true));
        Assert.Null(ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-a", "successor", "ruler", true));
        Assert.Null(ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-b", "leader-b", "ruler", true));
        Assert.Null(ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-c", "leader-a", "ruler", true));
        Assert.Null(ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-a", "leader-a", "other-ruler", true));
        Assert.False(ReignRulerPolicy.ClanAppealVerdictEligible(RulerVerdict.CleanExecution, false));
        Assert.False(ReignRulerPolicy.ClanAppealVerdictEligible(RulerVerdict.Submission, true));
        ReignRulerPolicy.SetDecisionAuthority(loaded, "successor");
        ReignRulerPolicy.SetDecisionAuthority(loaded, "ruler");
        Assert.Null(ReignRulerPolicy.InfluencedClanVerdict(loaded, "clan-a", "leader-a", "ruler", true));

        string targetId = ReignRulerPolicy.ClanAppealId(j.Id, "clan-a", "leader-a");
        Assert.Equal(targetId, ReignRulerPolicy.ClanAppealId(loaded.Id, "clan-a", "leader-a"));
        Assert.NotEqual(targetId, ReignRulerPolicy.ClanAppealId(j.Id, "clan-a", "successor"));
        Assert.NotEqual(ReignRulerPolicy.ClanAppealId(j.Id, "a", "bc"), ReignRulerPolicy.ClanAppealId(j.Id, "ab", "c"));
    }

    [Fact]
    public void Corrupt_nonfinite_sentence_deadline_recovers_without_shortening_a_valid_saved_sentence()
    {
        var j = new RulerJudgment { Stage = RulerJudgmentStage.Sentenced, Verdict = RulerVerdict.CleanExecution,
            SentencedDay = 107.25, Deadline = double.NaN };
        ReignRulerPolicy.EnsureSentenceDeadline(j, 110);
        Assert.Equal(115.25, j.Deadline);
        j.Deadline = 122.25;
        ReignRulerPolicy.EnsureSentenceDeadline(j, 200);
        Assert.Equal(122.25, j.Deadline);
        j.SentencedDay = 0; j.Deadline = double.NaN;
        ReignRulerPolicy.EnsureSentenceDeadline(j, 100);
        Assert.Equal(8, j.Deadline);
        j.SentencedDay = -1; j.Deadline = double.NaN;
        ReignRulerPolicy.EnsureSentenceDeadline(j, 100);
        Assert.Equal(108, j.Deadline);
    }

    [Fact]
    public void A_successor_does_not_inherit_the_prior_sovereigns_queued_verdict()
    {
        var j = new RulerJudgment { CapturedDay = 100, VictorId = "old_ruler", PendingVerdict = RulerVerdict.CleanExecution };
        ReignRulerPolicy.SetDecisionAuthority(j, "old_ruler");
        Assert.Equal(RulerVerdict.CleanExecution, j.PendingVerdict);
        ReignRulerPolicy.SetDecisionAuthority(j, "new_ruler");
        Assert.Null(j.PendingVerdict);
        Assert.Equal(100, j.CapturedDay);
    }

    [Fact]
    public void Read_only_timing_evidence_does_not_invent_a_verdict_before_the_decision()
    {
        var j = new RulerJudgment { Id = "judgment", CapturedDay = 100.25, Stage = RulerJudgmentStage.AwaitingDecision };
        var waiting = ReignRulerPolicy.TimingObservation(j, 106);
        Assert.Equal(107.25, waiting["decisionDueDay"]);
        Assert.Equal(false, waiting["timeGateOpen"]);
        Assert.Equal("", waiting["verdict"]);
        Assert.Equal(-1d, waiting["executionDueDay"]);
        Assert.Equal(100.25, j.CapturedDay);
        j.Stage = RulerJudgmentStage.Sentenced;
        j.SentencedDay = 107.25; j.Deadline = 115.25; j.Verdict = RulerVerdict.CleanExecution;
        Assert.Equal(115.25, ReignRulerPolicy.TimingObservation(j, 108)["executionDueDay"]);
        Assert.True(ReignRulerPolicy.Cancel(j, "escaped_or_rescued"));
        var cancelled = ReignRulerPolicy.TimingObservation(j, 200);
        Assert.Equal(115.25, cancelled["executionDueDay"]);
        Assert.Equal(false, cancelled["executionDue"]);
        Assert.Equal("escaped_or_rescued", cancelled["cancellationReason"]);
        Assert.False(ReignRulerPolicy.Cancel(j, "escaped_or_rescued"));
        Assert.False(ReignRulerPolicy.SentenceDue(j, 200));
    }
    [Theory]
    [InlineData(false, 2000, false)] [InlineData(true, 999, false)] [InlineData(true, 1000, true)] [InlineData(true, 1001, true)]
    public void Departure_requires_command_and_one_thousand_present_healthy_troops(bool commander, int healthy, bool expected)
        => Assert.Equal(expected, ReignRulerPolicy.CanDepart(commander, healthy));
    [Fact]
    public void CancellationPreservesActualSentenceProvenanceAndHasFiniteTiming()
    {
        var waiting = new RulerJudgment { Stage = RulerJudgmentStage.AwaitingDecision, CapturedDay = 100,
            PendingVerdict = RulerVerdict.CleanExecution };
        Assert.True(ReignRulerPolicy.Cancel(waiting, "escaped_or_rescued", 101));
        Assert.False(ReignRulerPolicy.HasIssuedSentence(waiting));
        var observed = ReignRulerPolicy.TimingObservation(waiting, double.NaN);
        Assert.Equal("", observed["verdict"]);
        Assert.Equal(-1d, observed["observedDay"]);
        Assert.Equal(101d, observed["resolvedDay"]);
        Assert.Equal(true, observed["terminal"]);
        Assert.False(ReignRulerPolicy.Cancel(waiting, "different", 200));
        Assert.Equal(101d, waiting.ResolvedDay);
        var legacy = new RulerJudgment { Stage = RulerJudgmentStage.Cancelled, SentencedDay = -1,
            Verdict = RulerVerdict.CleanExecution, Deadline = 115, CancellationReason = "escaped_or_rescued" };
        Assert.True(ReignRulerPolicy.HasIssuedSentence(legacy));
        Assert.Equal(115d, ReignRulerPolicy.TimingObservation(legacy, 200)["executionDueDay"]);
        Assert.Equal(false, ReignRulerPolicy.TimingObservation(legacy, 200)["executionDue"]);
        legacy.Deadline = 0; legacy.SentencedDay = 0;
        Assert.False(ReignRulerPolicy.HasIssuedSentence(legacy));
    }
    [Fact] public void Relocation_and_return_are_explicit_exceptions() => Assert.True(ReignRulerPolicy.CanDepart(false, 0, true));
    [Theory] [InlineData(999, false)] [InlineData(1000, true)]
    public void Arrival_after_reload_ends_the_return_exception_before_the_next_departure(int healthy, bool canDepart)
    {
        var saved = new RulerState { Returns = new() { ["ruler"] = "capital", ["other_ruler"] = "other_capital" } };
        var state = JsonSerializer.Deserialize<RulerState>(JsonSerializer.Serialize(saved))!;
        Assert.False(ReignRulerPolicy.CompleteReturnIfArrived(state, "ruler", null));
        Assert.False(ReignRulerPolicy.CompleteReturnIfArrived(state, "ruler", "old_capital"));
        Assert.True(ReignRulerPolicy.CanDepart(false, 0, state.Returns.ContainsKey("ruler")));
        Assert.True(ReignRulerPolicy.CompleteReturnIfArrived(state, "ruler", "capital"));
        Assert.Equal(canDepart, ReignRulerPolicy.CanDepart(true, healthy, state.Returns.ContainsKey("ruler")));
        Assert.Equal("other_capital", state.Returns["other_ruler"]);
        Assert.False(ReignRulerPolicy.CompleteReturnIfArrived(state, "ruler", "capital"));
        Assert.DoesNotContain("ruler", JsonSerializer.Deserialize<RulerState>(JsonSerializer.Serialize(state))!.Returns.Keys);
    }
    [Theory] [InlineData(749, true)] [InlineData(750, false)] [InlineData(999, false)]
    public void Return_uses_a_separate_threshold(int count, bool expected) => Assert.Equal(expected, ReignRulerPolicy.ShouldReturn(true, count, false));
    [Fact] public void Disband_and_objective_completion_require_return()
    { Assert.True(ReignRulerPolicy.ShouldReturn(false, 2000, false)); Assert.True(ReignRulerPolicy.ShouldReturn(true, 2000, true)); }
    [Fact] public void Capital_prefers_own_clan_city_then_other_city_then_castle_by_combat_strength()
    {
        var candidates = new[] { C("own", true, true, 50), C("foreign", false, true, 5000), C("castle", true, false, 10000) };
        Assert.Equal("own", ReignRulerPolicy.SelectCapital(candidates)!.Id);
        Assert.Equal("foreign", ReignRulerPolicy.SelectCapital(candidates.Skip(1))!.Id);
        Assert.Equal("castle", ReignRulerPolicy.SelectCapital(candidates.Skip(2))!.Id);
        Assert.Null(ReignRulerPolicy.SelectCapital(Array.Empty<RulerCapitalCandidate>()));
    }
    [Fact] public void Capital_ties_are_stable_and_strength_precedes_clan_tier()
    {
        var a=C("a",true,true,100);var b=C("b",true,true,101);a.ClanTier=6;
        Assert.Equal("b",ReignRulerPolicy.SelectCapital(new[]{a,b})!.Id);
        b.GarrisonStrength=100;Assert.Equal("a",ReignRulerPolicy.SelectCapital(new[]{b,a})!.Id);
    }
    [Theory] [InlineData(false, .249999, true)] [InlineData(false, .25, false)] [InlineData(true, .499999, true)] [InlineData(true, .5, false)]
    [InlineData(false, -1, false)] [InlineData(true, 1, false)]
    public void Capture_uses_exact_single_roll_boundaries(bool wounded, double roll, bool expected) => Assert.Equal(expected, ReignRulerPolicy.Capture(wounded, roll));
    [Fact] public void Seeded_capture_samples_match_the_configured_probability()
    {
        foreach(bool wounded in new[]{false,true}) {var rng=new Random(1337);int captured=Enumerable.Range(0,10000).Count(_=>ReignRulerPolicy.Capture(wounded,rng.NextDouble()));Assert.InRange(captured/10000d,(wounded?.5:.25)-.015,(wounded?.5:.25)+.015);}
        Assert.False(ReignRulerPolicy.Capture(true,double.NaN));
    }
    [Theory] [InlineData(RulerVerdict.TortureExecution,-20,true)] [InlineData(RulerVerdict.TortureExecution,-19,false)]
    [InlineData(RulerVerdict.CleanExecution,-10,true)] [InlineData(RulerVerdict.CleanExecution,-9,false)] [InlineData(RulerVerdict.RebelExecution,-100,false)]
    public void Defection_cutoffs_are_exact_and_exclude_civil_war(RulerVerdict verdict,int affinity,bool expected)=>Assert.Equal(expected,ReignRulerPolicy.Defects(verdict,affinity));
    [Theory] [InlineData(RulerVerdict.TortureExecution,60)] [InlineData(RulerVerdict.CleanExecution,30)] [InlineData(RulerVerdict.Exile,45)] [InlineData(RulerVerdict.Submission,45)]
    public void Full_strength_fade_and_expiry_share_one_clock(RulerVerdict verdict,double duration)
    {
        Assert.Equal(duration,ReignRulerPolicy.Duration(verdict));Assert.Equal(0,ReignRulerPolicy.Fade(verdict,100,99));
        Assert.Equal(1,ReignRulerPolicy.Fade(verdict,100,100+duration*2/3));
        Assert.Equal(.5,ReignRulerPolicy.Fade(verdict,100,100+duration*5/6),8);
        Assert.Equal(0,ReignRulerPolicy.Fade(verdict,100,100+duration));
    }
    [Theory] [InlineData(RulerVerdict.TortureExecution,-3.5,2,1)] [InlineData(RulerVerdict.CleanExecution,-2,1,.5)]
    public void Execution_effects_distinguish_conquered_and_war_start_homeland(RulerVerdict verdict,double penalty,double prosperity,double security)
    {
        var e=Effect(verdict);var captured=ReignRulerPolicy.Modifiers(new[]{e},"captured","victor",0);var home=ReignRulerPolicy.Modifiers(new[]{e},"home","victor",0);
        Assert.Equal(penalty,captured.Loyalty);Assert.Equal(0,captured.Prosperity);Assert.Equal(prosperity,home.Prosperity);Assert.Equal(security,home.Security);
        Assert.Equal(0,ReignRulerPolicy.Modifiers(new[]{e},"captured","third",0).Loyalty);
    }
    [Fact] public void Exile_penalties_apply_to_both_realms_with_loser_surcharge()
    {var e=Effect(RulerVerdict.Exile);Assert.Equal(-.75,ReignRulerPolicy.Modifiers(new[]{e},"home","victor",0).Loyalty);Assert.Equal(-2.5,ReignRulerPolicy.Modifiers(new[]{e},"own","defeated",0).Loyalty);}
    [Theory] [InlineData(RulerVerdict.TortureExecution,0,2,1)] [InlineData(RulerVerdict.CleanExecution,0,1,.5)] [InlineData(RulerVerdict.Submission,-.75,0,-.25)]
    public void Recaptured_war_start_homeland_keeps_its_homeland_classification(RulerVerdict verdict,double loyalty,double prosperity,double security)
    {
        var e=Effect(verdict);e.Conquered.Add("home");
        var regained=ReignRulerPolicy.Modifiers(new[]{e},"home","victor",0);
        Assert.Equal(loyalty,regained.Loyalty);Assert.Equal(prosperity,regained.Prosperity);Assert.Equal(security,regained.Security);
        Assert.Equal(0,ReignRulerPolicy.Modifiers(new[]{e},"home","third",0).Loyalty);
    }
    [Fact] public void Submission_has_combined_and_annexed_penalties()
    {var e=Effect(RulerVerdict.Submission);Assert.Equal(-.75,ReignRulerPolicy.Modifiers(new[]{e},"home","victor",0).Loyalty);var m=ReignRulerPolicy.Modifiers(new[]{e},"captured","victor",0);Assert.Equal(-2,m.Loyalty);Assert.Equal(-.25,m.Security);}
    [Fact] public void Later_conquests_inherit_remaining_time_and_effects_do_not_stack_without_bound()
    {
        var e=Effect(RulerVerdict.TortureExecution);e.Conquered.Add("later");var later=ReignRulerPolicy.Modifiers(new[]{e},"later","victor",50);
        Assert.Equal(-1.75,later.Loyalty);Assert.Equal(0,ReignRulerPolicy.Modifiers(new[]{e},"later","victor",60).Loyalty);
        var m=ReignRulerPolicy.Modifiers(new[]{e,e,Effect(RulerVerdict.Submission)},"captured","victor",0);Assert.Equal(-3.5,m.Loyalty);
        var home=ReignRulerPolicy.Modifiers(new[]{e,e,Effect(RulerVerdict.CleanExecution),Effect(RulerVerdict.Submission)},"home","victor",0);Assert.Equal(2,home.Prosperity);Assert.Equal(.75,home.Security);
    }
    [Fact] public void Escape_cancels_the_pending_sentence_and_recapture_starts_a_new_deadline()
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.Sentenced,Verdict=RulerVerdict.CleanExecution,SentencedDay=100,Deadline=115};
        Assert.True(ReignRulerPolicy.BlocksRelease(j));Assert.False(ReignRulerPolicy.SentenceDue(j,114.999));Assert.True(ReignRulerPolicy.SentenceDue(j,115));
        Assert.True(ReignRulerPolicy.Cancel(j,"rescue"));Assert.False(ReignRulerPolicy.BlocksRelease(j));Assert.False(ReignRulerPolicy.SentenceDue(j,200));
        var next=new RulerJudgment{Stage=RulerJudgmentStage.Sentenced,Verdict=RulerVerdict.CleanExecution,SentencedDay=110,Deadline=125};Assert.False(ReignRulerPolicy.SentenceDue(next,115));
    }
    [Fact] public void Completed_death_cannot_be_cancelled_or_resurrected()
    {var j=new RulerJudgment{Stage=RulerJudgmentStage.Applying,NativeDeathObserved=true};Assert.False(ReignRulerPolicy.Cancel(j,"late_rescue"));}
    [Theory] [InlineData(RulerVerdict.Exile)] [InlineData(RulerVerdict.Submission)]
    public void A_partial_political_resolution_cannot_disappear_as_an_escape(RulerVerdict verdict)
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.Applying,Verdict=verdict,Failure="Native consequence uncertain: transfer",Receipts=new(){"submission_release","payment:treasury"}};
        Assert.False(ReignRulerPolicy.Cancel(j,"escaped_or_rescued"));
        Assert.Equal(RulerJudgmentStage.Applying,j.Stage);Assert.NotEmpty(j.Failure);Assert.Equal(2,j.Receipts.Count);
    }
    [Fact] public void Cancelling_a_pending_decision_clears_its_obsolete_retry_failure()
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.AwaitingDecision,Failure="Context unavailable"};
        Assert.True(ReignRulerPolicy.Cancel(j,"feature_disabled"));Assert.Empty(j.Failure);Assert.Equal("feature_disabled",j.CancellationReason);
    }
    [Theory] [InlineData("defeated",-20,true)] [InlineData("third",-100,false)] [InlineData("victor",-100,false)] [InlineData(null,-100,false)] [InlineData("defeated",-19,false)]
    public void Sentencing_affinity_does_not_pull_clans_out_of_other_realms(string? currentKingdom,int affinity,bool expected)
        => Assert.Equal(expected,ReignRulerPolicy.DefectsFromRealm(RulerVerdict.TortureExecution,affinity,currentKingdom,"defeated"));
    [Fact] public void An_uncertain_execution_keeps_its_ransom_guard_until_death_or_rescue()
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.Applying,Verdict=RulerVerdict.CleanExecution,Failure="Native consequence uncertain: fixture"};
        Assert.True(ReignRulerPolicy.BlocksRelease(j));Assert.True(ReignRulerPolicy.Cancel(j,"escape"));Assert.False(ReignRulerPolicy.BlocksRelease(j));
        j.Stage=RulerJudgmentStage.Applying;j.NativeDeathObserved=true;Assert.False(ReignRulerPolicy.BlocksRelease(j));
    }
    [Fact] public void Saved_state_retains_rolls_cutoffs_receipts_and_independent_clocks()
    {
        var state=new RulerState();state.Judgments.Add(new RulerJudgment{Id="j",Draw=.42,VictorAffinity=-37,TraitSource="shipped_foundation_traits",Deadline=15,Stage=RulerJudgmentStage.Sentenced,Clans=new(){new(){ClanId="c",Affinity=-20}},Receipts=new(){"paid"}});
        state.Exiles.Add(new(){UntilDay=126});state.Exiles.Add(new(){UntilDay=31.5,Fugitive=true});state.Effects.Add(Effect(RulerVerdict.TortureExecution));
        var saved=JsonSerializer.Deserialize<RulerState>(JsonSerializer.Serialize(state))!;Assert.Equal(.42,saved.Judgments[0].Draw);Assert.Equal(-20,saved.Judgments[0].Clans[0].Affinity);Assert.Contains("paid",saved.Judgments[0].Receipts);Assert.Equal(126,saved.Exiles[0].UntilDay);Assert.Equal(31.5,saved.Exiles[1].UntilDay);
        Assert.Equal(-37, saved.Judgments[0].VictorAffinity);Assert.Equal("shipped_foundation_traits", saved.Judgments[0].TraitSource);
    }
    [Fact] public void Legacy_judgments_do_not_invent_a_frozen_affinity_or_trait_source()
    {
        var saved=JsonSerializer.Deserialize<RulerJudgment>("{\"Id\":\"old\",\"Draw\":0.42,\"Verdict\":1}")!;
        Assert.Null(saved.VictorAffinity);Assert.Equal("", saved.TraitSource);Assert.Equal(.42, saved.Draw);
        var reloaded=JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(saved))!;
        Assert.Null(reloaded.VictorAffinity);Assert.Equal(saved.Verdict, reloaded.Verdict);Assert.Equal(saved.Draw, reloaded.Draw);
    }
    [Fact] public void Native_heir_selection_waits_for_death_without_execution_receipts_or_escape_cancellation()
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.Applying,Verdict=RulerVerdict.CleanExecution};
        Assert.False(ReignRulerPolicy.ObserveNativeExecution(j,false,true));
        Assert.True(j.NativeExecutionRequested);Assert.False(j.NativeDeathObserved);Assert.DoesNotContain("execution",j.Receipts);
        Assert.False(ReignRulerPolicy.Cancel(j,"escaped_or_rescued"));Assert.True(ReignRulerPolicy.BlocksRelease(j));Assert.Empty(j.Failure);
        j=JsonSerializer.Deserialize<RulerJudgment>(JsonSerializer.Serialize(j))!;
        Assert.True(j.NativeExecutionRequested);Assert.False(ReignRulerPolicy.ObserveNativeExecution(j,false,true));
        j.NativeDeathDay=115.25;
        Assert.True(ReignRulerPolicy.ObserveNativeExecution(j,true,true));Assert.True(j.NativeDeathObserved);
        Assert.Contains("execution",j.Receipts);Assert.False(ReignRulerPolicy.BlocksRelease(j));Assert.Equal(115.25,j.NativeDeathDay);
        Assert.True(ReignRulerPolicy.ObserveNativeExecution(j,true,true));Assert.Single(j.Receipts);
    }
    [Fact] public void Native_execution_rejection_does_not_fabricate_a_pending_death_or_receipt()
    {
        var j=new RulerJudgment{Stage=RulerJudgmentStage.Applying,Verdict=RulerVerdict.CleanExecution};
        Assert.Throws<InvalidOperationException>(()=>ReignRulerPolicy.ObserveNativeExecution(j,false,false));
        Assert.False(j.NativeExecutionRequested);Assert.False(j.NativeDeathObserved);Assert.Empty(j.Receipts);
        Assert.True(ReignRulerPolicy.Cancel(j,"rescue"));
    }
    [Fact] public void Trait_selection_is_weighted_eligible_and_replayable()
    {
        var traits=new Dictionary<string,double>{{"mercy",100},{"compassion",100},{"vengefulness",0},{"aggression",0}};
        var scores=ReignRulerPolicy.Scores(traits,100,false);Assert.True(scores[RulerVerdict.Exile]>scores[RulerVerdict.TortureExecution]);
        Assert.Equal(ReignRulerPolicy.Choose(scores,.42),ReignRulerPolicy.Choose(scores,.42));scores.Remove(RulerVerdict.Exile);
        Assert.DoesNotContain(RulerVerdict.Exile,Enumerable.Range(0,1000).Select(i=>ReignRulerPolicy.Choose(scores,i/1000d)));
        Assert.Equal(3,ReignRulerPolicy.Scores(traits,0,true).Count);Assert.Throws<ArgumentOutOfRangeException>(()=>ReignRulerPolicy.Choose(scores,1));
    }
    [Fact] public void Options_preserve_individual_preferences_when_master_is_disabled()
    {
        var settings=RulerOptions.Switches.Keys.ToDictionary(k=>k,_=>true);var o=new RulerOptions(k=>settings[k]);
        Assert.All(RulerOptions.Switches.Keys,k=>Assert.True(o.Enabled(k)));settings[RulerOptions.Master]=false;
        Assert.All(RulerOptions.Switches.Keys,k=>Assert.False(o.Enabled(k)));Assert.True(o.Preference(RulerOptions.Capture));
        settings[RulerOptions.Master]=true;settings[RulerOptions.Capture]=false;Assert.False(o.Enabled(RulerOptions.Capture));Assert.True(o.Enabled(RulerOptions.Judgments));
    }
    [Theory] [InlineData(0,0)] [InlineData(3,0)] [InlineData(100,25)] [InlineData(-1,0)]
    public void Reparations_never_create_debt(long wealth,long payment)=>Assert.Equal(payment,ReignRulerPolicy.Reparations(wealth));
    [Fact] public void Native_removed_governor_culture_penalty_is_not_an_active_option()
    {
        var options = new RulerOptions(_ => true);
        Assert.DoesNotContain(RulerOptions.GovernorCulture, RulerOptions.Switches.Keys);
        Assert.False(options.Preference(RulerOptions.GovernorCulture));
        Assert.False(options.Enabled(RulerOptions.GovernorCulture));
    }
    [Fact] public void Fixed_ruler_fixture_rejects_arbitrary_mutation_targets_and_invalid_amounts()
    {
        var args = new Dictionary<string,string>{{"profile","army"},{"heroId","ruler"},{"settlementId","capital"},{"otherHeroId",""},{"amount","1000"}};
        string Case()=>JsonSerializer.Serialize(new[]{new{caseId="army",phase="fixture",kind="fixture",actionId="fixture.ruler_setup",operationId="army_1",arguments=args}});
        Assert.Single(StructuredActionTools.ParseCases(Case()));args["amount"]="4001";Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));
        args["amount"]="1000";args["profile"]="arbitrary_method";Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));
        args["profile"]="army";args["passReceipt"]="true";Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));
        args.Remove("passReceipt");args["profile"]="capture_roll";
        foreach(string roll in new[]{"0","4000"}) {args["amount"]=roll;Assert.Single(StructuredActionTools.ParseCases(Case()));}
        args["amount"]="-1";Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));
    }
    [Fact] public void Ruler_manifest_preserves_bounded_clock_driven_native_acceptance()
    {
        using var doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),"ReignServer/tests/ReignLiveTest/scenarios/capital-ruler-judgment-manifest.json")));
        var root=doc.RootElement;Assert.Equal("reign-ruler-certification-manifest-v1",root.GetProperty("schema").GetString());
        var ids=root.GetProperty("cases").EnumerateArray().Select(c=>c.GetProperty("id").GetString()).ToArray();Assert.Equal(ids.Length,ids.Distinct().Count());
        Assert.Equal(JsonValueKind.Null,root.GetProperty("fixedSoakDays").ValueKind);Assert.Equal(1,root.GetProperty("consecutivePassesRequired").GetInt32());
        Assert.True(root.GetProperty("oneRollingCheckpoint").GetBoolean());Assert.Contains(126d,root.GetProperty("clockBoundaries").EnumerateArray().Select(d=>d.GetDouble()));
    }
    [Fact] public void Judgment_draw_fixture_accepts_only_valid_draws_and_an_explicit_distinct_ai_ruler()
    {
        var args=new Dictionary<string,string>{{"profile","judgment_draw"},{"heroId","prisoner"},{"settlementId","safe_town"},{"otherHeroId","ai_ruler"},{"amount","0"}};
        string Case()=>JsonSerializer.Serialize(new[]{new{caseId="judgment",phase="fixture",kind="fixture",actionId="fixture.ruler_setup",operationId="judgment_draw_1",arguments=args}});
        foreach(string draw in new[]{"0","1000","2000","3999"}){args["amount"]=draw;Assert.Single(StructuredActionTools.ParseCases(Case()));}
        foreach(string draw in new[]{"-1","4000","NaN","0.5"}){args["amount"]=draw;Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));}
        args["amount"]="2000";
        foreach(string ruler in new[]{"","prisoner","main_hero"}){args["otherHeroId"]=ruler;Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));}
        args["otherHeroId"]="ai_ruler";args["verdict"]="Reunification";
        Assert.Throws<ArgumentException>(()=>StructuredActionTools.ParseCases(Case()));
    }
    private static RulerCapitalCandidate C(string id,bool own,bool town,double strength)=>new(){Id=id,RulingClan=own,Town=town,GarrisonStrength=strength};
    private static RulerEffect Effect(RulerVerdict verdict)=>new(){Verdict=verdict,VictorKingdomId="victor",DefeatedKingdomId="defeated",Homeland=new(){"home"},Conquered=new(){"captured"}};
}
