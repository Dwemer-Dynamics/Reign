using ReignBeta.Shared.Characters;
using Reign.Core.Contracts.Actions;
using System.Text.Json;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class NpcLivesTests
{
    [Fact]
    public void SoloLeaveUsesTheGuestsOwnSafetyWithoutChangingOtherTravelBindings()
    {
        var npc = new NpcLifeCandidate { HeroId = "guest", Affinity = 100, Available = true,
            UnsafeLeaveLocationIds = new() { "hostile" } };
        var places = new[] {
            new NpcStoryLocation { Id = "hostile", Kind = "town", TravelDays = .25 },
            new NpcStoryLocation { Id = "safe", Kind = "town", TravelDays = .25 },
            new NpcStoryLocation { Id = "sieged", Kind = "town", Available = false },
            new NpcStoryLocation { Id = "hideout", Kind = "hideout" } };
        var restoredNpc = JsonSerializer.Deserialize<NpcLifeCandidate>(JsonSerializer.Serialize(npc))!;
        Assert.Equal(new[] { "safe" }, NpcLivesEngine.LeaveDestinations(restoredNpc, places).Select(p => p.Id));
        Assert.Equal(2, NpcLivesEngine.LeaveDestinations(new NpcLifeCandidate(), places).Count);
        Assert.True(places[0].Available); // It can still be valid for the player or another NPC.
        var options = Options(new() { ["npcLivesChance100"] = 100, ["npcLivesFrequencyPercent"] = 500 },
            "npcLivesBriefEnabled", "npcLivesOutingsEnabled", "npcLivesMajorEnabled", "npcLivesIndependentEnabled");
        var definition = NpcStoryCatalog.All.Single(d => d.Id == "outing_bookbinder");
        for (int seed = 0; seed < 64; seed++)
        {
            var state = new NpcLivesState { CampaignId = "safety_" + seed, TimelineId = "main" };
            Assert.NotEmpty(NpcLivesEngine.Evaluate(state, new[] { restoredNpc }, new[] { definition }, places, options, 10, "origin"));
            Assert.Equal("safe", Assert.Single(state.Leaves).DestinationId);
            Assert.Empty(state.Effects);
            var blocked = new NpcLivesState { CampaignId = "blocked_" + seed, TimelineId = "main" };
            Assert.Empty(NpcLivesEngine.Evaluate(blocked, new[] { restoredNpc }, new[] { definition },
                new[] { places[0] }, options, 10, "origin"));
            Assert.Empty(blocked.Leaves); Assert.Empty(blocked.Effects);
        }
    }

    [Fact]
    public void FreshPersonalInvitationKeepsTheAcceptedClockAndCannotBypassPendingOrRewind()
    {
        var encounter = new NpcConversationEncounter { LastAcceptedDay = 20 };
        Assert.False(encounter.BeginOpening("ordinary", 20, 1));
        Assert.False(encounter.BeginOpening("rewound", 19, 1, freshInvitation: true));
        Assert.False(encounter.BeginOpening("invalid", double.NaN, 1, freshInvitation: true));
        Assert.True(encounter.BeginOpening("invitation", 20, 1, freshInvitation: true));
        Assert.Equal(20, encounter.LastAcceptedDay);
        Assert.False(encounter.BeginOpening("duplicate", 20, 1, freshInvitation: true));
        Assert.True(encounter.AcceptOpening("invitation", "May I ask you something?", "considering a verse", 20));
        var restored = JsonSerializer.Deserialize<NpcConversationEncounter>(JsonSerializer.Serialize(encounter))!;
        Assert.False(restored.BeginOpening("immediate", 20, 1));
        Assert.True(restored.BeginOpening("after_time", 20 + 1d / 24, 1));
    }

    [Fact]
    public void DurationRevisionKeepsActualArrivalHoldsAndSavedAttendance()
    {
        var meeting = new NpcSummonedMeeting { Id = "meeting", IssuerId = "leader", TargetId = "noble",
            DestinationId = "town", Purpose = "Survey", ProposalId = "original", StayDays = .25, SoloAttendance = true };
        Assert.True(meeting.ScheduleArrival(10, 0));
        Assert.True(meeting.Arrive("noble", "town", 10.07, true));
        string original = JsonSerializer.Serialize(meeting);
        var revised = meeting.Revise("extension", "town", .5, 10.1, 0, true);
        Assert.Equal(original, JsonSerializer.Serialize(meeting));
        var restored = JsonSerializer.Deserialize<NpcSummonedMeeting>(JsonSerializer.Serialize(revised))!;
        Assert.Equal(2, restored.Revision); Assert.Equal("extension", restored.ProposalId);
        Assert.Equal("meeting", restored.Status); Assert.True(restored.SoloAttendance);
        Assert.Equal(10.07, restored.ArrivalDay); Assert.Equal(10.57, restored.ReleaseDay);
        Assert.Equal(meeting.ScheduledArrivalDay, restored.ScheduledArrivalDay);
        Assert.True(restored.NeedsHold("leader", 10.4)); Assert.True(restored.NeedsHold("noble", 10.4));
        Assert.False(restored.Complete(10.56)); Assert.True(restored.Complete(10.57));
        Assert.False(restored.NeedsHold("leader", 10.57)); Assert.False(restored.NeedsHold("noble", 10.57));
    }

    [Theory]
    [InlineData("other", 0, true)]
    [InlineData("town", 1, true)]
    [InlineData("town", 0, false)]
    public void ARealRescheduleStillRequiresNewArrival(string destination, double wait, bool bothPresent)
    {
        var meeting = new NpcSummonedMeeting { IssuerId = "leader", TargetId = "noble", DestinationId = "town", StayDays = .25 };
        Assert.True(meeting.ScheduleArrival(10, 0)); Assert.True(meeting.Arrive("noble", "town", 10, true));
        var revised = meeting.Revise("reschedule", destination, .5, 10.1, wait, bothPresent);
        Assert.Equal("agreed", revised.Status); Assert.Null(revised.ArrivalDay); Assert.Null(revised.ReleaseDay);
        Assert.Equal(10.1 + wait, revised.ScheduledArrivalDay);
        Assert.False(revised.NeedsHold("noble", 10.1));
    }

    [Fact]
    public void ShortenedSessionEndsAtOriginalArrivalAndTerminalRevisionIsRejected()
    {
        var meeting = new NpcSummonedMeeting { IssuerId = "leader", TargetId = "noble", DestinationId = "town", StayDays = 1 };
        Assert.True(meeting.ScheduleArrival(10, 0)); Assert.True(meeting.Arrive("noble", "town", 10, true));
        var shorter = meeting.Revise("shorten", "town", .25, 10.5, 0, true);
        Assert.Equal(10.25, shorter.ReleaseDay); Assert.True(shorter.Complete(10.5));
        Assert.Throws<InvalidOperationException>(() => shorter.Revise("late", "town", .5, 10.5, 0, true));
        Assert.Throws<InvalidOperationException>(() => meeting.Revise("invalid", "town", double.NaN, 10.5, 0, true));
    }

    [Fact]
    public void CompletedStoryWordingSurvivesItsTerminalCursorAndReload()
    {
        var story = new NpcStoryInstance {
            Id = "saved_story", Status = "active", PhaseIndex = 1,
            Definition = new NpcStoryDefinition { Title = "A rabbit for the road", Phases = new List<NpcStoryPhase> {
                new() { Title = "Meet the keeper" }, new() { Title = "Choose a companion" }
            } }
        };
        Assert.Equal("Choose a companion", NpcLivesPolicy.StoryActionTitle(story, story.Id));
        story.Status = "completed";
        story.PhaseIndex = story.Definition.Phases.Count;
        var restored = JsonSerializer.Deserialize<NpcStoryInstance>(JsonSerializer.Serialize(story))!;
        string before = JsonSerializer.Serialize(restored);
        Assert.Equal("A rabbit for the road", NpcLivesPolicy.StoryActionTitle(restored, restored.Id));
        Assert.Equal(before, JsonSerializer.Serialize(restored));
    }

    [Fact]
    public void UnavailableStoryWordingKeepsAnIdentityWithoutInventingAValidPhase()
    {
        Assert.Equal("missing_story", NpcLivesPolicy.StoryActionTitle(null!, "missing_story"));
        var story = new NpcStoryInstance { PhaseIndex = -1,
            Definition = new NpcStoryDefinition { Title = "Retained story" } };
        Assert.Equal("Retained story", NpcLivesPolicy.StoryActionTitle(story, "saved_id"));
        story.Definition.Title = "";
        Assert.Equal("saved_id", NpcLivesPolicy.StoryActionTitle(story, "saved_id"));
        Assert.Equal(-1, story.PhaseIndex);
    }

    [Fact]
    public void InitialApproachPreferenceOnlyGatesTheAddedOpening()
    {
        Assert.DoesNotContain(NpcLivesOptions.Numbers, s => s.Key == "npcLivesApproachTimeoutMs");
        Assert.True(new NpcLivesOptions().Allows("approach"));
        var off = new NpcLivesOptions(k => k != NpcLivesOptions.InitialApproachKey, _ => null);
        Assert.False(off.Allows("approach"));
        Assert.True(off.Preference("npcLivesApproachEnabled"));
        foreach (string feature in new[] { "brief", "outing", "major", "leave", "independent", "announcement", "reaction", "meeting" })
            Assert.True(off.Allows(feature));
        var activityOff = new NpcLivesOptions(k => k != "npcLivesApproachEnabled", _ => null);
        Assert.True(activityOff.Preference(NpcLivesOptions.InitialApproachKey));
        Assert.False(activityOff.Allows("approach"));
    }
    [Fact]
    public void SoloAttendanceIsSignedAndHistoricalMeetingsKeepTheirArrangement()
    {
        var historical = JsonSerializer.Deserialize<NpcSummonedMeeting>("{\"Id\":\"old\",\"IssuerId\":\"leader\",\"TargetId\":\"noble\"}")!;
        Assert.False(historical.SoloAttendance);
        var meeting = new NpcSummonedMeeting { SoloAttendance = true, IssuerId = "leader", TargetId = "noble", DestinationId = "town", StayDays = 3 };
        Assert.True(meeting.ScheduleArrival(10, 2));
        var reloaded = JsonSerializer.Deserialize<NpcSummonedMeeting>(JsonSerializer.Serialize(meeting))!;
        Assert.True(reloaded.SoloAttendance);
        Assert.False(reloaded.Arrive("noble", "town", 11, true));
        Assert.True(reloaded.Arrive("noble", "town", 12, true));
        Assert.Equal(15, reloaded.ReleaseDay);
        var clause = new ActionClause { ActionId = "npc_life.summon_meeting" };
        clause.Arguments["attendanceMode"] = "solo_camp";
        var agreement = new ActionAgreement { Clauses = new List<ActionClause> { clause } };
        string signed = agreement.TermsHash();
        clause.Arguments["attendanceMode"] = "legacy_party";
        Assert.NotEqual(signed, agreement.TermsHash());
    }
    [Fact]
    public void SummonsWaitIsFasterByDefaultAndOnlyChangesNewSchedules()
    {
        var options = new NpcLivesOptions();
        Assert.Equal(80, options.Number("npcLivesSummonsTravelTimePercent"));
        Assert.Equal(4, NpcLivesPolicy.SummonsArrivalDelay(5, options));
        Assert.Equal(0, NpcLivesPolicy.SummonsArrivalDelay(0, options));
        Assert.Equal(.01, NpcLivesPolicy.SummonsArrivalDelay(.001, options));
        Assert.Equal(126, NpcLivesPolicy.SummonsArrivalDelay(1000, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcLivesPolicy.SummonsArrivalDelay(double.NaN, options));
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcLivesPolicy.SummonsArrivalDelay(-1, options));
        var faster = new NpcLivesOptions(_ => true, k => k == "npcLivesSummonsTravelTimePercent" ? 60 : null);
        Assert.Equal(3, NpcLivesPolicy.SummonsArrivalDelay(5, faster));
        var m = new NpcSummonedMeeting { IssuerId = "leader", TargetId = "noble", DestinationId = "town", StayDays = 3 };
        Assert.False(m.ScheduleArrival(10, -1)); Assert.False(m.ScheduleArrival(10, double.PositiveInfinity));
        Assert.True(m.ScheduleArrival(10, NpcLivesPolicy.SummonsArrivalDelay(5, options)));
        Assert.False(m.ScheduleArrival(10, NpcLivesPolicy.SummonsArrivalDelay(5, faster)));
        var restored = JsonSerializer.Deserialize<NpcSummonedMeeting>(JsonSerializer.Serialize(m))!;
        Assert.Equal(14, restored.ScheduledArrivalDay); Assert.Equal(4, restored.ArrivalDelayDays);
        Assert.True(restored.NeedsHold("leader", 13)); Assert.False(restored.NeedsHold("noble", 13));
        Assert.False(restored.Arrive("noble", "town", 13.99, true));
        Assert.False(restored.Arrive("noble", "town", 14, false));
        Assert.True(restored.Arrive("noble", "town", 15, true));
        Assert.Equal(18, restored.ReleaseDay); Assert.False(restored.Arrive("noble", "town", 16, true));
        Assert.True(restored.NeedsHold("leader", 17)); Assert.True(restored.NeedsHold("noble", 17));
        Assert.False(restored.Complete(17.99)); Assert.True(restored.Complete(18));
        Assert.False(restored.NeedsHold("leader", 18)); Assert.False(restored.NeedsHold("noble", 18));
        m.Status = "cancelled"; Assert.False(m.Arrive("noble", "town", 20, true));
        Assert.False(m.NeedsHold("leader", 20));
    }
    [Fact]
    public void NativeOutcomeCleanupRequiresTheCallbackAndBothNativeTeardowns()
    {
        foreach (bool mission in new[] { false, true })
        foreach (bool battle in new[] { false, true })
        foreach (string status in new[] { "", "pending", "active", "resolved", "failed" })
            Assert.Equal(!mission && !battle && status == "resolved", NpcLivesPolicy.NativeEncounterSettled(mission, battle, status));
        Assert.False(NpcLivesPolicy.NativeEncounterSettled(false, false, null!));
    }
    [Theory]
    [InlineData("duel", "rescued")]
    [InlineData("duel", "won_later")]
    [InlineData("hideout", "promised")]
    [InlineData("visit", "purchased")]
    public void AuthoringAndReceiptsRejectUnsupportedNativeOutcomes(string kind, string result)
    {
        var definition = JsonSerializer.Deserialize<NpcStoryDefinition>(JsonSerializer.Serialize(NpcStoryCatalog.All[0]))!;
        var objective = definition.Phases[0].Objectives[0];
        objective.Kind = kind; objective.RequiredOutcome = result; objective.RequiredProgress = 1;
        objective.CompletionOutcomes.Clear();
        definition.Phases[0].LocationKind = kind == "hideout" ? "hideout" : "town";
        Assert.NotEmpty(NpcStoryCatalog.Validate(new[] { definition }));
        Assert.False(NpcStoryCatalog.SupportsNativeOutcome(kind, result));
    }
    [Fact]
    public void RescueAndRecoveryRequireAReachableNativeIdentity()
    {
        foreach (string result in new[] { "rescued", "recovered" })
        {
            var definition = JsonSerializer.Deserialize<NpcStoryDefinition>(JsonSerializer.Serialize(NpcStoryCatalog.All[0]))!;
            var phase = definition.Phases[0]; phase.LocationKind = "hideout";
            phase.Objectives[0].Kind = "hideout"; phase.Objectives[0].RequiredOutcome = result; phase.Objectives[0].RequiredProgress = 1;
            phase.Objectives[0].CompletionOutcomes.Clear();
            definition.SupportingRole = "";
            Assert.NotEmpty(NpcStoryCatalog.Validate(new[] { definition }));
            definition.SupportingRole = "old acquaintance"; definition.SupportingRevealPhase = 0;
            Assert.Empty(NpcStoryCatalog.Validate(new[] { definition }));
            definition.SupportingRevealPhase = 1;
            Assert.NotEmpty(NpcStoryCatalog.Validate(new[] { definition }));
        }
    }
    [Fact]
    public void StoryLinksResolveAgainstTheMergedCatalogue()
    {
        Assert.Empty(NpcStoryCatalog.ValidateLinks(NpcStoryCatalog.All));
        var definition = JsonSerializer.Deserialize<NpcStoryDefinition>(JsonSerializer.Serialize(NpcStoryCatalog.All[0]))!;
        definition.RelatedMajorId = "major_missing_story";
        Assert.NotEmpty(NpcStoryCatalog.ValidateLinks(NpcStoryCatalog.All.Where(d => d.Id != definition.Id).Append(definition)));
        definition.RelatedMajorId = NpcStoryCatalog.All.Single(d => d.Id == "major_missing_friend").Id;
        Assert.Empty(NpcStoryCatalog.ValidateLinks(NpcStoryCatalog.All.Where(d => d.Id != definition.Id).Append(definition)));
        Assert.False(NpcStoryCatalog.SupportsNativeOutcome("duel", "lost", requirement: true));
        Assert.True(NpcStoryCatalog.SupportsNativeOutcome("duel", "lost"));
    }
    [Fact]
    public void PartyWideBriefVarietySurvivesSaveAndDoesNotBlockLinkedMajorStories()
    {
        var state = new NpcLivesState { RecentBriefFamilies = NpcStoryCatalog.All.Where(d => d.Category == "brief").Select(d => d.Family).Distinct().Take(8).ToList() };
        var restored = JsonSerializer.Deserialize<NpcLivesState>(JsonSerializer.Serialize(state))!;
        for (int i = 0; i < 60; i++)
        {
            var npc = new NpcLifeCandidate { HeroId = "companion_" + i, Affinity = 100 };
            var selected = NpcLivesEngine.Choose(restored, npc, "brief", NpcStoryCatalog.All, 10, "variety_" + i);
            Assert.NotNull(selected); Assert.DoesNotContain(selected.Family, state.RecentBriefFamilies);
            Assert.NotNull(NpcLivesEngine.Choose(restored, npc, "major", NpcStoryCatalog.All, 10, "major_" + i));
        }
    }
    [Theory]
    [InlineData("Active", true)] [InlineData("ReviewDue", true)] [InlineData("Departing", false)]
    [InlineData("Returning", false)] [InlineData("Completed", false)] [InlineData("HostileBanishmentPending", false)]
    public void PersonalAbsenceCannotRestoreAnEndedGuestContract(string phase, bool expected)
    {
        Assert.Equal(expected, NpcLivesPolicy.CanResumeGuestService("original", "original", phase, false));
        Assert.False(NpcLivesPolicy.CanResumeGuestService("original", "replacement", phase, false));
        Assert.True(NpcLivesPolicy.CanResumeGuestService("", "", phase, false));
        Assert.True(NpcLivesPolicy.CanResumeGuestService("original", "original", phase, true));
    }
    [Fact]
    public void CoastalExperienceCannotInventACoastInAnInlandTown()
    {
        var phase = NpcStoryCatalog.All.Single(d => d.Id == "outing_coastal_walk").Phases[0];
        var inland = new NpcStoryLocation { Id = "inland", Kind = "town" };
        Assert.Equal("", NpcLivesEngine.BindLocation(phase, new[] { inland }, "inland", "seed"));
        Assert.Equal("port", NpcLivesEngine.BindLocation(phase, new[] { inland, new NpcStoryLocation { Id = "port", Kind = "town", Coastal = true } }, "inland", "seed"));
    }
    [Theory]
    [InlineData("npc_life.change_meeting")]
    [InlineData("npc_life.cancel_meeting")]
    public void MeetingControlRequiresExactConsentsNativeReceiptAndRevision(string action)
    {
        AgreementJournal Journal(string id, string kind)
        {
            var a = new ActionAgreement { Id = id, PlayerId = "player", NpcId = "leader", State = AgreementState.Waiting,
                Clauses = new() { new ActionClause { Id = id + "_term", ActionId = kind, ActorId = "leader", RecipientId = "player" } } };
            if (kind != "npc_life.summon_meeting") { a.Clauses[0].Arguments["meetingId"] = "meeting"; a.Clauses[0].Arguments["meetingRevision"] = "2"; }
            string hash = a.TermsHash(); a.PlayerAcceptedHash = a.NpcAcceptedHash = hash;
            return new AgreementJournal { Agreement = a, CommittedHash = hash,
                Steps = new() { new AgreementStep { ClauseId = a.Clauses[0].Id, Reserved = true, Started = true } } };
        }
        var old = Journal("original", "npc_life.summon_meeting"); var control = Journal("control", action); var backend = new MeetingBackend();
        Assert.False(ReignAgreementEngine.RetireObservedMeeting(old, control, "meeting", 2, false, backend, 5));
        Assert.False(ReignAgreementEngine.RetireObservedMeeting(old, control, "meeting", 1, true, backend, 5));
        Assert.False(ReignAgreementEngine.RetireObservedMeeting(old, control, "another", 2, true, backend, 5));
        string consent = control.Agreement.NpcAcceptedHash; control.Agreement.NpcAcceptedHash = "";
        Assert.False(ReignAgreementEngine.RetireObservedMeeting(old, control, "meeting", 2, true, backend, 5));
        control.Agreement.NpcAcceptedHash = consent;
        Assert.True(ReignAgreementEngine.RetireObservedMeeting(old, control, "meeting", 2, true, backend, 5));
        Assert.Equal(AgreementState.Cancelled, old.Agreement.State); Assert.False(old.Steps[0].Completed);
        Assert.Equal(0, old.Steps[0].FulfilledInstallments); Assert.Empty(old.Receipts.Single().ObservedEffects);
        Assert.False(ReignAgreementEngine.RetireObservedMeeting(old, control, "meeting", 2, true, backend, 6));
        Assert.Equal(1, backend.Saves);
    }
    private sealed class MeetingBackend : IAgreementBackend
    {
        public int Saves;
        public IReadOnlyList<string> Preflight(ActionAgreement a) => Array.Empty<string>();
        public string Capture(ActionAgreement a, ActionClause c) => "";
        public void Reserve(ActionAgreement a, ActionClause c, string s) => throw new InvalidOperationException();
        public void Return(ActionAgreement a, ActionClause c, string s, int n) => throw new InvalidOperationException();
        public void Apply(ActionAgreement a, ActionClause c, string s, int n) => throw new InvalidOperationException();
        public void ApplyAssetsAtomically(ActionAgreement a, IReadOnlyList<AgreementStep> s) => throw new InvalidOperationException();
        public bool Observe(ActionAgreement a, ActionClause c, string s, int n) => false;
        public bool ConditionSatisfied(ActionClause c, double day) => false;
        public double DeliveryDays(ActionAgreement a) => 0;
        public void SaveJournal(AgreementJournal j) => Saves++;
    }
    [Fact]
    public void LowTrustExperiencesForeshadowEveryArcWithoutUnlockingItsPrivateChapters()
    {
        foreach (var major in NpcStoryCatalog.All.Where(d => d.Category == "major"))
        {
            var links = NpcStoryCatalog.All.Where(d => d.RelatedMajorId == major.Id).ToList();
            Assert.Contains(links, d => d.Category == "brief" && d.Phases[0].MinimumAffinity < 60);
            Assert.Contains(links, d => d.Category == "outing" && d.Phases[0].MinimumAffinity == 60);
            Assert.True(major.Phases[0].MinimumAffinity >= 80);
        }
        var state = new NpcLivesState();
        var precursor = NpcStoryCatalog.All.Single(d => d.Id == "brief_homesickness_02");
        var prior = new NpcStoryInstance { Id = "memory", HeroId = "npc", Definition = precursor, Status = "completed",
            LinkedLocationId = "city", LinkedLocationName = "The same remembered city" };
        state.Stories.Add(prior);
        var saved = JsonSerializer.Deserialize<NpcLivesState>(JsonSerializer.Serialize(state))!;
        Assert.Equal("city", saved.Stories.Single().LinkedLocationId);
        Assert.Equal("major_missing_friend", saved.Stories.Single().Definition.RelatedMajorId);
    }
    [Fact]
    public void SixBriefExamplesUseAuthoredPromptsAndDistinctAcceptedContributions()
    {
        foreach (int index in new[] { 0, 31, 65, 99, 130, 180 })
        {
            var definition = NpcStoryCatalog.All.Where(d => d.Category == "brief").ElementAt(index);
            var story = new NpcStoryInstance { Id = "sample_" + index, HeroId = "companion", Status = "active", Definition = definition };
            var phase = definition.Phases[0];
            Assert.Contains("Do not", phase.Prompt);
            var state = new NpcLivesState { Stories = new() { story } };
            foreach (var objective in phase.Objectives)
            {
                var sources = new HashSet<string> { "one", "two", "three" };
                NpcStoryEvidence Evidence(string id, string quote) => new() { HeroId = story.HeroId, ObjectiveId = objective.Id, SourceId = id, Quote = quote };
                Assert.False(story.AcceptEvidence(Evidence("unaccepted", "A plausible but unaccepted contribution."), 1, sources, false));
                Assert.True(story.AcceptEvidence(Evidence("one", "I offer a thoughtful suggestion about this experience."), 1, sources, false));
                Assert.False(story.PhaseComplete);
                Assert.False(story.AcceptEvidence(Evidence("one", "A different sentence in the same accepted contribution."), 1, sources, false));
                Assert.False(story.AcceptEvidence(Evidence("two", "I offer a thoughtful suggestion about this experience."), 1, sources, false));
                Assert.False(story.AcceptEvidence(Evidence("two", "I reflect on how your idea changes my suggestion."), 0, sources, false));
                Assert.True(story.AcceptEvidence(Evidence("two", "I reflect on how your idea changes my suggestion."), 1, sources, false));
                Assert.False(story.PhaseComplete);
                Assert.False(state.Advance(story, 10));
                var resolution = Evidence("three", "We have explored both ideas and reached our conclusion together.");
                resolution.Outcome = "completed"; resolution.ResolutionQuote = "I have reached my conclusion after our discussion.";
                Assert.False(story.AcceptEvidence(resolution, 1, sources, false));
                Assert.True(story.AcceptEvidence(resolution, 1, sources, false, true));
            }
            Assert.True(state.Advance(story, 10)); Assert.False(state.Advance(story, 10));
            Assert.Equal("completed", story.Status);
            Assert.Equal(2, state.Effects.Count);
            Assert.All(state.Effects, e => Assert.Equal("companion", e.HeroId));
        }
    }
    [Theory]
    [InlineData("adopted", true)]
    [InlineData("declined", false)]
    public void PetFactRequiresTheVerifiedFinalChoiceAndSurvivesReloadWithoutDuplicateRewards(string outcome, bool hasPet)
    {
        var definition = NpcStoryCatalog.All.Single(d => d.Id == "outing_pet_adoption");
        var story = new NpcStoryInstance { Id = "pet_choice", HeroId = "companion", Status = "active", Definition = definition, LocationId = "town" };
        var phase = definition.Phases[0]; var activity = phase.Objectives.Single(o => !o.Native);
        var state = new NpcLivesState { Stories = new() { story } };
        var sources = new HashSet<string> { "care", "plan", "choice" };
        Assert.True(story.AcceptEvidence(new NpcStoryEvidence { ObjectiveId = phase.Objectives.Single(o => o.Native).Id,
            SourceId = "arrival", HeroId = story.HeroId, Kind = "native", LocationId = "town" }, 1, sources, true));
        foreach (string id in new[] { "care", "plan" })
            Assert.True(story.AcceptEvidence(new NpcStoryEvidence { ObjectiveId = activity.Id, SourceId = id,
                HeroId = story.HeroId, Quote = "Distinct substantive contribution: " + id }, 1, sources, false));
        state = JsonSerializer.Deserialize<NpcLivesState>(JsonSerializer.Serialize(state))!;
        story = state.Stories.Single();
        Assert.False(story.PhaseComplete); Assert.False(state.Advance(story, 10));
        Assert.Empty(state.Effects); Assert.Empty(state.Character(story.HeroId).Facts);
        var choice = new NpcStoryEvidence { ObjectiveId = activity.Id, SourceId = "choice", HeroId = story.HeroId,
            Quote = "We have visited the keeper and made our final choice.", Outcome = outcome,
            ResolutionQuote = outcome == "adopted" ? "I have adopted Clover as my companion." : "I have decided against adopting this rabbit." };
        Assert.False(story.AcceptEvidence(choice, 1, sources, false));
        Assert.True(story.AcceptEvidence(choice, 1, sources, false, true));
        Assert.True(state.Advance(story, 10));
        Assert.Equal(hasPet, state.Character(story.HeroId).Facts.Any(f => f.Kind == "pet"));
        Assert.Equal(!hasPet, state.Character(story.HeroId).Facts.Any(f => f.Id.EndsWith(":rabbit_declined")));
        state = JsonSerializer.Deserialize<NpcLivesState>(JsonSerializer.Serialize(state))!;
        Assert.False(state.Advance(state.Stories.Single(), 11));
        Assert.Equal(2, state.Effects.Count); Assert.Equal(2, state.Effects.Select(e => e.Id).Distinct().Count());
        Assert.Equal(hasPet, state.Character(story.HeroId).Facts.Any(f => f.Kind == "pet"));
    }
    [Fact]
    public void ConditionalFactsCannotReferenceAnotherPhaseOrAnUnsupportedConclusion()
    {
        var definition = JsonSerializer.Deserialize<NpcStoryDefinition>(JsonSerializer.Serialize(NpcStoryCatalog.All.Single(d => d.Id == "outing_pet_adoption")))!;
        var fact = definition.Phases[0].Facts.Single(f => f.Kind == "pet");
        fact.OutcomeObjectiveId = "another_phase_activity";
        Assert.NotEmpty(NpcStoryCatalog.Validate(new[] { definition }));
        fact.OutcomeObjectiveId = definition.Phases[0].Objectives.Single(o => !o.Native).Id;
        fact.RequiredNarrativeOutcome = "promised";
        Assert.NotEmpty(NpcStoryCatalog.Validate(new[] { definition }));
        fact.RequiredNarrativeOutcome = "adopted";
        Assert.Empty(NpcStoryCatalog.Validate(new[] { definition }));
        Assert.All(NpcStoryCatalog.All.SelectMany(d => d.Phases).SelectMany(p => p.Objectives).Where(o => !o.Native),
            o => Assert.NotEmpty(o.CompletionOutcomes));
    }
    [Fact]
    public void EveryAuthoredPhaseIncludesItsProtectedSceneRules()
    {
        Assert.All(NpcStoryCatalog.All.SelectMany(s => s.Phases), p => {
            Assert.Contains("Do not", p.Prompt);
            Assert.False(string.IsNullOrWhiteSpace(p.OpeningText));
            Assert.InRange(p.Objectives.Count, 1, 12);
        });
    }
    [Fact]
    public void LeaveNegotiationIsStableAndDoesNotInventConsent()
    {
        var leave = new NpcPersonalLeave { Id = "same-visit", HeroId = "npc", Purpose = "Visit a sister", ProposalId = "proposal",
            Trust = 45, Compassion = 90, Honor = 65, Urgency = 2 };
        bool result = NpcLeaveNegotiation.Persuaded(leave, "medical_need", true, "campaign|timeline");
        var restored = JsonSerializer.Deserialize<NpcPersonalLeave>(JsonSerializer.Serialize(leave))!;
        Assert.Equal(result, NpcLeaveNegotiation.Persuaded(restored, "medical_need", true, "campaign|timeline"));
        Assert.Equal("discussing", leave.Status); Assert.Null(leave.DepartureDay);
        Assert.False(NpcLeaveNegotiation.Persuaded(leave, "unrelated_trade", true, "campaign|timeline"));
        Assert.Contains("private", NpcLeaveNegotiation.PrivacyRule(20));
        Assert.Contains("separate", NpcLeaveNegotiation.PrivacyRule(60));
        Assert.True(leave.AgreeDeparture("proposal", 1, 10, 0, 5, independent: true));
        Assert.True(leave.IndependentDecision);
    }
    [Fact]
    public void PausedClocksStaleRevisionsAndInvalidNumbersCannotCompleteCommitments()
    {
        var leave = new NpcPersonalLeave { Id = "leave", ProposalId = "p" };
        Assert.False(leave.AgreeDeparture("p", 1, 10, double.NaN, 5));
        Assert.False(leave.AgreeDeparture("p", 1, 10, 2, double.PositiveInfinity));
        Assert.True(leave.AgreeDeparture("p", 1, 10, 2, 5));
        Assert.False(leave.Depart(10, true)); Assert.False(leave.Depart(11.999, true));
        Assert.True(leave.Depart(13, true));
        var restored = JsonSerializer.Deserialize<NpcPersonalLeave>(JsonSerializer.Serialize(leave))!;
        Assert.Equal(18, restored.ReturnDay); Assert.False(restored.Return(17.99, true));
        var meeting = new NpcSummonedMeeting { IssuerId = "leader", TargetId = "vassal", DestinationId = "town", StayDays = .5, Status = "travelling" };
        Assert.False(meeting.Arrive("vassal", "town", double.NaN, true));
        Assert.True(meeting.NeedsHold("leader", 100000)); Assert.False(meeting.Complete(100000));
        Assert.True(meeting.Arrive("vassal", "town", 100001, true));
        Assert.True(meeting.NeedsHold("vassal", 100001.25)); Assert.True(meeting.Complete(100001.5));
    }
    [Fact]
    public void AuthoredCatalogueHasDistinctValidDefinitionsAndSupportedSkills()
    {
        Assert.Empty(NpcStoryCatalog.Validate(NpcStoryCatalog.All));
        Assert.Equal(240, NpcStoryCatalog.All.Count);
        Assert.Equal(200, NpcStoryCatalog.All.Count(s => s.Category == "brief"));
        Assert.Equal(28, NpcStoryCatalog.All.Count(s => s.Category == "outing"));
        Assert.Equal(12, NpcStoryCatalog.All.Count(s => s.Category == "major"));
        Assert.Equal(30, NpcStoryCatalog.All.Where(s => s.Category == "brief").Select(s => s.Family).Distinct().Count());
        Assert.Equal(200, NpcStoryCatalog.All.Where(s => s.Category == "brief").Select(s => s.Phases[0].Prompt).Distinct().Count());
        string[] skills = { "OneHanded", "TwoHanded", "Polearm", "Bow", "Crossbow", "Throwing", "Riding", "Athletics", "Crafting", "Scouting", "Tactics", "Roguery", "Charm", "Leadership", "Trade", "Steward", "Medicine", "Engineering" };
        Assert.All(NpcStoryCatalog.All.SelectMany(s => s.Phases), p => Assert.Contains(p.Skill, skills));
        Assert.Contains(NpcStoryCatalog.All, s => s.Phases.Any(p => p.Facts.Any(f => f.Kind == "pet")));
        Assert.Contains(NpcStoryCatalog.All, s => s.Phases.Any(p => p.Objectives.Any(o => o.Kind == "duel")));
        Assert.Contains(NpcStoryCatalog.All, s => s.Phases.Any(p => p.Objectives.Any(o => o.Kind == "hideout")));
    }
    private static NpcLivesOptions Options(Dictionary<string, double>? values = null, params string[] disabled)
        => new(k => !disabled.Contains(k), k => values != null && values.TryGetValue(k, out var v) ? v : null);

    [Theory]
    [InlineData(-100, 0)] [InlineData(-80, .05)] [InlineData(-60, .1)]
    [InlineData(-40, .2)] [InlineData(-20, .3)] [InlineData(0, .5)]
    [InlineData(20, 1)] [InlineData(40, 5)] [InlineData(60, 12)]
    [InlineData(80, 25)] [InlineData(100, 40)] [InlineData(50, 8.5)]
    public void AffinityChanceInterpolatesApprovedAnchors(int affinity, double expected)
        => Assert.Equal(expected, NpcLivesPolicy.BaseChance(affinity, new NpcLivesOptions()), 8);

    [Fact]
    public void ReducingOneCategoryDoesNotIncreaseOtherEvents()
    {
        var before = NpcLivesPolicy.DailyProbabilities(100, Options(), NpcLivesPolicy.CategoryWeights);
        var after = NpcLivesPolicy.DailyProbabilities(100, Options(new() { ["npcLivesBriefFrequencyPercent"] = 50 }), NpcLivesPolicy.CategoryWeights);
        Assert.Equal(before["brief"] / 2, after["brief"], 10);
        foreach (string kind in before.Keys.Where(k => k != "brief")) Assert.Equal(before[kind], after[kind], 10);
        Assert.True(after.Values.Sum() < before.Values.Sum());
    }
    [Fact]
    public void GlobalAndCategoryMultipliersComposeBeforeOneDailyRoll()
    {
        var options = Options(new() { ["npcLivesFrequencyPercent"] = 200, ["npcLivesBriefFrequencyPercent"] = 50 });
        var result = NpcLivesPolicy.DailyProbabilities(100, options, NpcLivesPolicy.CategoryWeights);
        Assert.Equal(.32, result["brief"], 10);
        Assert.Equal(.08, result["outing"], 10);
        Assert.Equal("", NpcLivesPolicy.SelectCategory(result, .999));
        Assert.Throws<ArgumentOutOfRangeException>(() => NpcLivesPolicy.SelectCategory(result, double.NaN));
    }
    [Fact]
    public void ZeroScaleStopsOnlyNewStartsAndCapPreservesProportions()
    {
        var zero = NpcLivesPolicy.DailyProbabilities(100, Options(new() { ["npcLivesFrequencyPercent"] = 0 }), NpcLivesPolicy.CategoryWeights);
        Assert.Equal(0, zero.Values.Sum());
        Assert.Equal("", NpcLivesPolicy.SelectCategory(zero, 0));
        var capped = NpcLivesPolicy.DailyProbabilities(100, Options(new() { ["npcLivesFrequencyPercent"] = 500 }), NpcLivesPolicy.CategoryWeights);
        Assert.Equal(1, capped.Values.Sum(), 10);
        Assert.Equal(16, capped["brief"] / capped["major"], 10);
    }
    [Fact]
    public void MasterAndStorySwitchesKeepPreferencesAndIndependentFeatures()
    {
        var masterOff = Options(null, "npcLivesEnabled");
        Assert.True(masterOff.Preference("npcLivesBriefEnabled"));
        Assert.False(masterOff.Allows("brief"));
        Assert.False(masterOff.Allows("meeting"));
        var storiesOff = Options(null, "npcLivesStoriesEnabled");
        Assert.False(storiesOff.Allows("outing")); Assert.True(storiesOff.Allows("leave"));
        Assert.True(storiesOff.Allows("announcement")); Assert.True(storiesOff.Allows("approach"));
        Assert.False(storiesOff.Allows("unknown"));
    }
    [Fact]
    public void FrequencyCannotBypassTrustRequirements()
    {
        var p = NpcLivesPolicy.DailyProbabilities(59, Options(new() { ["npcLivesFrequencyPercent"] = 500 }), NpcLivesPolicy.CategoryWeights);
        Assert.False(p.ContainsKey("outing")); Assert.False(p.ContainsKey("major"));
        Assert.True(NpcLivesPolicy.DailyProbabilities(60, Options(), NpcLivesPolicy.CategoryWeights).ContainsKey("outing"));
        Assert.False(NpcLivesPolicy.DailyProbabilities(79, Options(), NpcLivesPolicy.CategoryWeights).ContainsKey("major"));
    }
    [Fact]
    public void TimingScalesAreIndependentAndRejectInvalidPersistedValues()
    {
        var o = Options(new() { ["npcLivesInvitationCooldownPercent"] = 50, ["npcLivesMajorCooldownPercent"] = 200, ["npcLivesReaskDelayPercent"] = 25 });
        Assert.Equal(3.5, NpcLivesPolicy.InvitationDelay(o));
        Assert.Equal(30, NpcLivesPolicy.MajorDelay(o, 0));
        Assert.Equal(1.75, NpcLivesPolicy.ReaskDelay(o));
        Assert.Equal(100, Options(new() { ["npcLivesFrequencyPercent"] = double.NaN }).Number("npcLivesFrequencyPercent"));
        Assert.All(NpcLivesOptions.Numbers, s => { Assert.True(s.Accepts(s.Default)); Assert.False(s.Accepts(double.PositiveInfinity)); Assert.False(s.Accepts(s.Minimum - s.Step)); });
    }
    [Fact]
    public void ReopeningUsesElapsedTimeAndRejectsLateDuplicateOpenings()
    {
        var e = new NpcConversationEncounter();
        Assert.True(e.BeginOpening("first", 1.99, 1));
        Assert.False(e.BeginOpening("duplicate", 1.99, 1));
        Assert.True(e.AcceptOpening("first", "I was mending this strap.", "mending", 1.99));
        Assert.False(e.AcceptOpening("first", "A late replacement", "studying", 1.99));
        Assert.False(e.BeginOpening("clock-boundary", 2, 1));
        Assert.False(e.BeginOpening("59-minutes", 1.99 + 59d / 1440, 1));
        var saved = JsonSerializer.Deserialize<NpcConversationEncounter>(JsonSerializer.Serialize(e))!;
        Assert.True(saved.BeginOpening("after-hour", 1.99 + 1d / 24, 1));
        Assert.False(saved.AcceptOpening("first", "stale", "", 3));
        Assert.True(saved.AcceptOpening("after-hour", "I have finished now.", "resting", 1.99 + 1d / 24));
        Assert.False(saved.AcceptTurn("rewound", 1));
    }
    [Fact]
    public void WaitingBeforeLeaveDoesNotConsumeTimeAway()
    {
        var leave = new NpcPersonalLeave { Id = "leave", HeroId = "npc", ProposalId = "p" };
        Assert.False(leave.AgreeDeparture("wrong", 1, 10, 2, 5));
        Assert.True(leave.AgreeDeparture("p", 1, 10, 2, 5));
        Assert.False(leave.Depart(11, true)); Assert.False(leave.Depart(12, false));
        Assert.True(leave.Depart(13, true)); Assert.Equal(18, leave.ReturnDay);
        Assert.False(leave.Depart(14, true)); Assert.False(leave.Return(18, false));
        Assert.True(leave.Return(19, true)); Assert.False(leave.Return(20, true));
    }
    [Fact]
    public void StayForNowReopensSamePurposeWithoutRandomInvitationOrOldConsent()
    {
        var leave = new NpcPersonalLeave { Id = "leave", Purpose = "Visit my sister", ProposalId = "p" };
        leave.AcceptedArgumentIds.Add("medical_need");
        Assert.True(leave.StayForNow(1, 10, 7));
        Assert.False(leave.Reopen(16)); Assert.True(leave.Reopen(17));
        Assert.Equal("Visit my sister", leave.Purpose);
        Assert.Empty(leave.AcceptedArgumentIds);
        Assert.False(leave.AgreeDeparture("p", 1, 17, 0, 5));
        Assert.True(leave.AgreeDeparture("p", leave.Revision, 17, 0, 5));
    }
    [Fact]
    public void MeetingWaitsIndefinitelyThenHoldsBothFromActualArrival()
    {
        var m = new NpcSummonedMeeting { IssuerId = "ruler", TargetId = "vassal", DestinationId = "town", StayDays = 3, Status = "travelling" };
        Assert.True(m.NeedsHold("ruler", 999)); Assert.False(m.NeedsHold("vassal", 999));
        Assert.False(m.Complete(999)); Assert.False(m.Arrive("other", "town", 1000, true));
        Assert.False(m.Arrive("vassal", "gate", 1000, true)); Assert.False(m.Arrive("vassal", "town", 1000, false));
        Assert.True(m.Arrive("vassal", "town", 1000, true));
        Assert.Equal(1003, m.ReleaseDay); Assert.True(m.NeedsHold("ruler", 1002)); Assert.True(m.NeedsHold("vassal", 1002));
        Assert.False(m.Arrive("vassal", "town", 1001, true)); Assert.False(m.Complete(1002)); Assert.True(m.Complete(1003));
        Assert.False(m.NeedsHold("ruler", 1004)); Assert.False(m.NeedsHold("vassal", 1004));
    }
    [Theory]
    [InlineData(false, false, false, false, false, true, "is no longer available")]
    [InlineData(true, false, true, false, false, true, "has died")]
    [InlineData(true, true, false, false, true, true, "cannot attend as an adult participant")]
    [InlineData(true, true, true, true, false, true, "is a prisoner")]
    [InlineData(true, true, true, false, false, true, "is currently unavailable for travel")]
    [InlineData(true, true, true, false, true, false, "belongs to an inactive or disbanding party")]
    [InlineData(true, true, true, false, true, true, "")]
    public void MeetingAvailabilityUsesNativeStateRatherThanWaitingTimeout(bool exists, bool alive, bool adult,
        bool prisoner, bool activeOrTravelling, bool validParty, string expected)
    {
        Assert.Equal(expected, NpcLivesPolicy.MeetingParticipantBlockReason(exists, alive, adult, prisoner, activeOrTravelling, validParty));
    }
    [Fact]
    public void AnnouncementAudienceAndReactionsSurviveMembershipChanges()
    {
        var a = new NpcPartyAnnouncement { Id = "a", PlayerId = "player", Text = "We march tomorrow", RecipientIds = new() { "companion", "guest" } };
        Assert.False(a.CanRead("town-notable")); Assert.False(a.CanRead("late-recruit")); Assert.True(a.CanRead("guest"));
        Assert.False(a.MarkReacted("companion", "turn", false)); Assert.True(a.ReactionPending("companion"));
        Assert.True(a.MarkReacted("companion", "turn", true)); Assert.False(a.MarkReacted("companion", "retry", true));
        Assert.False(a.MarkReacted("late-recruit", "turn", true));
        Assert.False(a.MarkReviewed("guest", "quiet", "no_comment", false));
        Assert.False(a.MarkReviewed("guest", "quiet", "unknown", true));
        Assert.True(a.MarkReviewed("guest", "quiet", "no_comment", true));
        Assert.False(a.MarkReviewed("guest", "repeat", "commented", true));
        var restored = JsonSerializer.Deserialize<NpcPartyAnnouncement>(JsonSerializer.Serialize(a))!;
        Assert.True(restored.CanRead("guest")); Assert.False(restored.ReactionPending("companion")); Assert.False(restored.ReactionPending("guest"));
        Assert.Equal("no_comment", restored.ReviewChoices["guest"]); Assert.Equal("quiet", restored.Reactions["guest"]);
        Assert.Equal("commented", restored.ReviewChoices["companion"]);
        var legacy = JsonSerializer.Deserialize<NpcPartyAnnouncement>("{\"RecipientIds\":[\"guest\"],\"Reactions\":{\"guest\":\"old_turn\"}}")!;
        Assert.False(legacy.ReactionPending("guest")); Assert.Empty(legacy.ReviewChoices);
    }
    [Fact]
    public void NativeObjectivesRequireMatchingVerifiedOutcomeAndRewardsApplyOnce()
    {
        var s = new NpcStoryInstance { Id = "story", HeroId = "npc", Status = "active", LocationId = "hideout", NativeEncounterId = "mission", StartedDay = 1,
            Definition = new NpcStoryDefinition { Id = "rescue", Family = "friendship", Phases = new() { new NpcStoryPhase { Id = "rescue", Xp = 1000, Affinity = 10,
                Objectives = new() { new NpcStoryObjective { Id = "rescue_friend", Kind = "hideout", RequiredOutcome = "rescued" } },
                Facts = new() { new NpcLifeFact { Id = "friend", Kind = "experience", Text = "My childhood friend was rescued." } } } } } };
        var evidence = new NpcStoryEvidence { ObjectiveId = "rescue_friend", Kind = "native", HeroId = "npc", SourceId = "native-result", EncounterId = "wrong", LocationId = "hideout", Outcome = "rescued" };
        Assert.False(s.AcceptEvidence(evidence, 1, new HashSet<string>(), true));
        evidence.EncounterId = "mission";
        Assert.False(s.AcceptEvidence(evidence, 1, new HashSet<string>(), false));
        evidence.Outcome = "lost"; Assert.False(s.AcceptEvidence(evidence, 1, new HashSet<string>(), true));
        evidence.Outcome = "rescued"; Assert.True(s.AcceptEvidence(evidence, 1, new HashSet<string>(), true));
        var state = new NpcLivesState { Stories = new() { s } };
        Assert.True(state.Advance(s, 3)); Assert.False(state.Advance(s, 3));
        Assert.Equal(2, state.Effects.Count); Assert.All(state.Effects, e => Assert.Equal("npc", e.HeroId));
        Assert.Contains(state.Character("npc").Facts, f => f.Text == "My childhood friend was rescued.");
        Assert.Equal(state.Character("npc").Facts.Count, state.Character("npc").Facts.Select(f => f.Id).Distinct().Count());
        Assert.Equal("completed", s.Status);
    }
}
