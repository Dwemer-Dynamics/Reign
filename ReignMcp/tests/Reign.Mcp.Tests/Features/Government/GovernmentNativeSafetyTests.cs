using System.Text.Json;
using System.Text.RegularExpressions;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentNativeSafetyTests
{
    [Fact]
    public void NewlyOpenedUrgentHearingCanBeVotedAtTheSamePausedNativeInstant()
    {
        const double now = 91154.05328340741;
        var hearing = new ReignGovernmentBusinessRecord
        {
            IsUrgent = true, Status = "hearing", PresentationDay = (float)now, DecisionDay = (float)now,
            FrozenAuthorityLevel = 1
        };
        Assert.True(hearing.DecisionDay > now); // The actual native first-fief award regression.
        Assert.True(ReignGovernmentNativeSafety.IsDecisionDue(hearing, now));
        var restored = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(JsonSerializer.Serialize(hearing, SaveOptions), SaveOptions)!;
        Assert.True(ReignGovernmentNativeSafety.IsDecisionDue(restored, now));
        Assert.False(ReignGovernmentNativeSafety.IsDecisionDue(hearing, now - .01));
    }

    [Theory]
    [InlineData(false, "hearing", false, false)]
    [InlineData(false, "presented", false, false)]
    [InlineData(true, "queued", false, false)]
    [InlineData(true, "presented", false, false)]
    [InlineData(true, "hearing", true, false)]
    [InlineData(true, "hearing", false, true)]
    public void RoundedUrgentClockAllowanceCannotBringAnotherSessionForward(bool urgent, string status, bool legacy, bool deferred)
    {
        const double now = 91154.05328340741;
        var hearing = new ReignGovernmentBusinessRecord
        {
            IsUrgent = urgent, Status = status, LegacySessionMigration = legacy,
            PresentationDay = deferred ? 91139 : (float)now, DecisionDay = (float)now
        };
        Assert.False(ReignGovernmentNativeSafety.IsDecisionDue(hearing, now));
        Assert.True(ReignGovernmentNativeSafety.IsDecisionDue(hearing, hearing.DecisionDay));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidNativeClockCannotAuthorizeARecordedDeadline(double now)
    {
        var hearing = new ReignGovernmentBusinessRecord { DecisionDay = 91154, PresentationDay = 91154, IsUrgent = true };
        Assert.False(ReignGovernmentNativeSafety.IsDecisionDue(hearing, now));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void MissingOrInvalidDeadlineCannotAuthorizeAVote(float due)
    {
        Assert.False(ReignGovernmentNativeSafety.IsDecisionDue(new ReignGovernmentBusinessRecord { DecisionDay = due }, 91154));
        Assert.False(ReignGovernmentNativeSafety.IsDecisionDue(null!, 91154));
    }

    private static (ReignGovernmentBusinessRecord Source, ReignGovernmentBusinessRecord Response) CounterpartRefusal()
    {
        var source = new ReignGovernmentBusinessRecord
        {
            BusinessId = "source", KingdomStringId = "initiating_realm", Kind = "world_action", Status = "decided",
            CounterpartBusinessId = "response", ActionCorrelationId = "accepted_pact",
            ActionPayloadJson = "{\"termsHash\":\"exact_signed_terms\"}", WinningOptionId = "accept",
            DecisionRulerHeroStringId = "initiating_ruler", Outcome = "The counterpart refused."
        };
        var response = new ReignGovernmentBusinessRecord
        {
            BusinessId = "response", KingdomStringId = "receiving_realm", Kind = "world_action", Status = "decided",
            CounterpartOfBusinessId = "source", ActionCorrelationId = source.ActionCorrelationId,
            ActionPayloadJson = source.ActionPayloadJson, WinningOptionId = "reject", ExecutedOptionId = "reject",
            // Actual saved world-action refusals are finalized by FinishBusiness without a native effect.
            ExecutionApplied = true
        };
        return (source, response);
    }

    [Fact]
    public void SavedCounterpartRejectionIsLawfulWithoutRewritingTheInitiatingVoteOrApplyingEffects()
    {
        var (source, response) = CounterpartRefusal();
        string sourceJson = JsonSerializer.Serialize(source, SaveOptions);
        string responseJson = JsonSerializer.Serialize(response, SaveOptions);
        Assert.True(ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(source, response));
        var loadedSource = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(sourceJson, SaveOptions)!;
        var loadedResponse = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(responseJson, SaveOptions)!;
        Assert.True(ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(loadedSource, loadedResponse));
        Assert.Equal(sourceJson, JsonSerializer.Serialize(source, SaveOptions));
        Assert.Equal(responseJson, JsonSerializer.Serialize(response, SaveOptions));
        Assert.Equal("accept", source.WinningOptionId);
        Assert.Equal("initiating_ruler", source.DecisionRulerHeroStringId);
        Assert.False(source.ExecutionApplied);
        Assert.True(response.ExecutionApplied);
        Assert.False(response.NativeEffectStarted);
        Assert.Equal(0, response.NativeEffectInvocationCount);
    }

    [Theory]
    [InlineData("unclosed_source")]
    [InlineData("source_already_accepted")]
    [InlineData("source_not_world_action")]
    [InlineData("source_is_counterpart")]
    [InlineData("missing_source_identity")]
    [InlineData("different_response")]
    [InlineData("different_source")]
    [InlineData("missing_action_identity")]
    [InlineData("different_action_identity")]
    [InlineData("missing_terms")]
    [InlineData("different_terms")]
    [InlineData("same_realm")]
    [InlineData("missing_realm")]
    [InlineData("response_not_world_action")]
    [InlineData("unclosed_response")]
    [InlineData("response_accepted")]
    [InlineData("response_failed")]
    [InlineData("response_expired")]
    [InlineData("source_effect_applied")]
    [InlineData("source_effect_uncertain")]
    [InlineData("source_effect_invoked")]
    [InlineData("response_decision_unfinished")]
    [InlineData("response_effect_uncertain")]
    [InlineData("response_effect_invoked")]
    [InlineData("source_quarantined")]
    [InlineData("response_quarantined")]
    public void CounterpartRefusalCannotHideChangedTermsMissingDecisionsOrUncertainEffects(string fault)
    {
        var (source, response) = CounterpartRefusal();
        switch (fault)
        {
            case "unclosed_source": source.Status = "awaiting_counterpart"; break;
            case "source_already_accepted": source.ExecutedOptionId = "accept"; break;
            case "source_not_world_action": source.Kind = "native_policy"; break;
            case "source_is_counterpart": source.CounterpartOfBusinessId = "another_source"; break;
            case "missing_source_identity": source.BusinessId = ""; response.CounterpartOfBusinessId = ""; break;
            case "different_response": source.CounterpartBusinessId = "another_response"; break;
            case "different_source": response.CounterpartOfBusinessId = "another_source"; break;
            case "missing_action_identity": source.ActionCorrelationId = response.ActionCorrelationId = ""; break;
            case "different_action_identity": response.ActionCorrelationId = "another_action"; break;
            case "missing_terms": source.ActionPayloadJson = response.ActionPayloadJson = ""; break;
            case "different_terms": response.ActionPayloadJson = "{\"termsHash\":\"changed\"}"; break;
            case "same_realm": response.KingdomStringId = source.KingdomStringId; break;
            case "missing_realm": source.KingdomStringId = ""; break;
            case "response_not_world_action": response.Kind = "native_policy"; break;
            case "unclosed_response": response.Status = "awaiting_ruler"; break;
            case "response_accepted": response.ExecutedOptionId = "accept"; break;
            case "response_failed": response.Status = "execution_failed"; break;
            case "response_expired": response.Status = "expired"; break;
            case "source_effect_applied": source.ExecutionApplied = true; break;
            case "source_effect_uncertain": source.NativeEffectStarted = true; break;
            case "source_effect_invoked": source.NativeEffectInvocationCount = 1; break;
            case "response_decision_unfinished": response.ExecutionApplied = false; break;
            case "response_effect_uncertain": response.NativeEffectStarted = true; break;
            case "response_effect_invoked": response.NativeEffectInvocationCount = 1; break;
            case "source_quarantined": source.NativeQuarantineReason = "Uncertain native outcome"; break;
            case "response_quarantined": response.NativeQuarantineReason = "Uncertain native outcome"; break;
            default: throw new InvalidOperationException(fault);
        }
        Assert.False(ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(source, response));
        Assert.False(ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(source, null!));
        Assert.False(ReignGovernmentNativeSafety.IsRecordedCounterpartRefusal(null!, response));
    }

    [Theory]
    [InlineData("proposed", null, false, false)] // Legacy proposals must migrate into a session.
    [InlineData("debate", null, false, false)]
    [InlineData("proposed", "awaiting_ruler", true, false)]
    [InlineData("debate", "awaiting_ruler", true, false)]
    [InlineData("debate", "reconsideration", true, false)]
    [InlineData("proposed", "reconsideration", true, false)]
    [InlineData("debate", "hearing", true, true)] // Weighted execution still requires its own due-date and permit guards.
    [InlineData("proposed", "hearing", true, true)]
    [InlineData("proposed", "presented", true, false)]
    [InlineData("proposed", "queued", true, false)]
    [InlineData("debate", "postponed", true, false)]
    [InlineData("debate", "awaiting_sponsorship", true, false)]
    [InlineData("debate", "decided", true, false)]
    [InlineData("debate", "invalidated", true, false)]
    [InlineData("debate", "expired", true, false)]
    [InlineData("debate", null, true, false)]
    [InlineData("active", "awaiting_ruler", true, false)]
    [InlineData("completed", "awaiting_ruler", true, false)]
    [InlineData("refused", "awaiting_ruler", true, false)]
    [InlineData(null, "awaiting_ruler", true, false)]
    [InlineData("unknown", "awaiting_ruler", true, false)]
    public void ResolutionDecisionEligibilityRequiresRecordedHearingWithoutReopeningTerminalResolutions(
        string? resolutionStatus, string? hearingStatus, bool hasHearing, bool expected)
    {
        Assert.Equal(expected, GovernmentBusinessRules.CanDecideResolution(resolutionStatus!, hearingStatus!, hasHearing));
    }

    [Fact]
    public void RuralVolunteerFallbackPreservesNativeVillageRecruitmentAndDoesNotRelocateAttendees()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "Government", "Campaign", "ReignGovernmentNativeDecisions.cs"));
        string fallback = source[source.IndexOf("internal static class ReignGovernmentRuralVolunteerPatch", StringComparison.Ordinal)
            ..source.IndexOf("internal static class ReignGovernmentNativeNoticePatch", StringComparison.Ordinal)];
        Assert.Contains("typeof(DefaultVolunteerModel), nameof(DefaultVolunteerModel.GetBasicVolunteer)", fallback);
        Assert.Contains("sellerHero?.IsRuralNotable != true", fallback);
        Assert.Contains("sellerHero.CurrentSettlement?.Village?.Bound != null) return true;", fallback);
        Assert.Contains("sellerHero.HomeSettlement?.Village?.Bound != null", fallback);
        Assert.Contains("sellerHero.HomeSettlement : sellerHero.BornSettlement", fallback);
        Assert.Contains("village?.Village?.Bound?.IsCastle == true", fallback);
        Assert.Contains("sellerHero.Culture.EliteBasicTroop : sellerHero.Culture.BasicTroop", fallback);
        Assert.Contains("if (volunteer == null) return true;", fallback);
        Assert.DoesNotMatch(@"sellerHero\.Culture\s*=(?!=)", fallback);
        foreach (string mutation in new[] { "Teleport", "ApplyForCharacterOnly", "VolunteerTypes", "TryCommit", "_governmentAttendance" })
            Assert.DoesNotContain(mutation, fallback);
    }

    [Fact]
    public void VisitorIssueDiscoveryGuardPreservesValidQuestsAndDoesNotMutateAttendance()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "Government", "Campaign", "ReignGovernmentNativeDecisions.cs"));
        string guard = source[source.IndexOf("internal static class ReignGovernmentRuralIssueContextPatch", StringComparison.Ordinal)
            ..source.IndexOf("internal static class ReignGovernmentNativeNoticePatch", StringComparison.Ordinal)];
        Assert.Contains("typeof(CampaignEvents), nameof(CampaignEvents.OnCheckForIssue), new[] { typeof(Hero) }", guard);
        Assert.Contains("ReignGovernmentNativeSafety.AllowNativeIssueDiscovery(", guard);
        Assert.Contains("__0?.IsHeadman == true, __0?.IsRuralNotable == true", guard);
        Assert.Contains("settlement != null, settlement?.Village?.Bound?.Town != null", guard);
        Assert.Contains("__0?.IsGangLeader == true, settlement?.IsTown == true && settlement.Town != null", guard);
        foreach (string mutation in new[] { "Teleport", "OnIssueUpdated", "RemoveIssue", "IssueManager", "TryCommit", "_governmentAttendance" })
            Assert.DoesNotContain(mutation, guard);
    }

    [Theory]
    [InlineData(true, false, true, false, false)] // Headman is distinct from RuralNotable in the installed game.
    [InlineData(false, true, true, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(false, false, true, false, true)] // Urban notable discovery remains native.
    [InlineData(true, false, true, true, true)]
    [InlineData(false, true, true, true, true)]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, false, true, true, true)]
    [InlineData(true, false, false, false, true)] // Native predicates retain null-settlement handling.
    [InlineData(false, true, false, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(false, false, false, false, true)]
    public void NativeIssueEligibilityCoversBothRuralOccupationsAndPreservesValidContexts(
        bool isHeadman, bool isRuralNotable, bool hasCurrentSettlement, bool hasVillageContext, bool expected)
    {
        Assert.Equal(expected, ReignGovernmentNativeSafety.AllowNativeIssueDiscovery(
            isHeadman, isRuralNotable, hasCurrentSettlement, hasVillageContext, false, false));
    }

    [Theory]
    [InlineData(true, true, true, true)] // A gang leader in a town keeps native quest discovery.
    [InlineData(true, true, false, false)] // Village or castle visits lack the required town context.
    [InlineData(true, false, false, false)] // Travelling gang leaders cannot dereference a settlement.
    [InlineData(true, false, true, false)] // Inconsistent context must also fail closed.
    [InlineData(false, true, true, true)]
    [InlineData(false, true, false, true)] // Other occupations retain their existing rules.
    [InlineData(false, false, false, true)]
    [InlineData(false, false, true, true)]
    public void GangLeaderQuestDiscoveryRequiresTownContextWithoutBlockingOtherOccupations(
        bool isGangLeader, bool hasCurrentSettlement, bool hasTownContext, bool expected)
    {
        Assert.Equal(expected, ReignGovernmentNativeSafety.AllowNativeIssueDiscovery(
            false, false, hasCurrentSettlement, false, isGangLeader, hasTownContext));
    }

    private static readonly JsonSerializerOptions SaveOptions = new() { IncludeFields = true };

    private static ReignGovernmentBusinessRecord Captured() => new()
    {
        Kind = "fief_allocation", NativeCaptureVersion = ReignGovernmentNativeSafety.CaptureVersion,
        NativeCaptureSource = "proposal", NativeDecisionType = "SettlementClaimantDecision",
        NativeFingerprint = "khuzait|epinosa|claimants", NativeCapturedOwnerClanId = "ruler"
    };

    [Theory]
    [InlineData("proposal")]
    [InlineData("election")]
    [InlineData("outcome")]
    [InlineData("unresolved")]
    [InlineData("offer")]
    [InlineData("reign")]
    public void VerifiedCaptureCanExecuteOnceAndCannotReenterOrReplayAfterReload(string source)
    {
        var record = Captured(); record.NativeCaptureSource = source;
        Assert.Empty(ReignGovernmentNativeSafety.ExecutionBlock(record, true, record.NativeFingerprint, "ruler"));
        Assert.True(ReignGovernmentNativeSafety.TryBeginEffect(record));
        bool permit = true;
        Assert.True(ReignGovernmentNativeSafety.TryConsumeEffectPermit(record, ref permit));
        Assert.False(ReignGovernmentNativeSafety.TryConsumeEffectPermit(record, ref permit));
        permit = true; // A new transient scope cannot reissue the saved receipt.
        Assert.False(ReignGovernmentNativeSafety.TryConsumeEffectPermit(record, ref permit));
        Assert.Equal(1, record.NativeEffectInvocationCount);
        var loaded = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(JsonSerializer.Serialize(record, SaveOptions), SaveOptions)!;
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(loaded));
        Assert.NotEmpty(ReignGovernmentNativeSafety.ExecutionBlock(loaded, true, loaded.NativeFingerprint, "ruler"));
    }

    [Fact]
    public void ExceptionBeforeNativeReceiptStillCannotRetryIntentAfterReload()
    {
        var record = Captured();
        Assert.True(ReignGovernmentNativeSafety.TryBeginEffect(record));
        var loaded = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(JsonSerializer.Serialize(record, SaveOptions), SaveOptions)!;
        Assert.Equal(0, loaded.NativeEffectInvocationCount);
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(loaded));
        Assert.Contains("already started", ReignGovernmentNativeSafety.ExecutionBlock(loaded, true, loaded.NativeFingerprint, "ruler"));
    }

    [Fact]
    public void LegacyHearingHasNoInventedCaptureAuthority()
    {
        var record = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(
            """{"Kind":"fief_allocation","NativeDecisionType":"SettlementClaimantDecision","NativeFingerprint":"old","Status":"hearing"}""", SaveOptions)!;
        Assert.Equal(0, record.NativeCaptureVersion);
        Assert.Contains("predates", ReignGovernmentNativeSafety.ExecutionBlock(record, true, "old", "ruler"));
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(record));
        Assert.False(record.ExecutionApplied);
        Assert.False(record.ConsequencesApplied);
    }

    [Theory]
    [InlineData(0, "proposal")]
    [InlineData(2, "proposal")]
    [InlineData(1, "")]
    [InlineData(1, "KingdomDecisionAdded")]
    public void UnverifiedOrLateEventCaptureCannotObtainExecutionAuthority(int version, string source)
    {
        var record = Captured(); record.NativeCaptureVersion = version; record.NativeCaptureSource = source;
        Assert.False(ReignGovernmentNativeSafety.IsTrustedCapture(record));
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(record));
    }

    [Fact]
    public void OwnerChangesWithinTheSameKingdomInvalidateTheOldAward()
    {
        var record = Captured();
        Assert.Contains("owner changed", ReignGovernmentNativeSafety.ExecutionBlock(record, true, record.NativeFingerprint, "gereigan"));
        record.NativeQuarantineReason = "Ownership changed outside this hearing.";
        Assert.Equal(record.NativeQuarantineReason, ReignGovernmentNativeSafety.ExecutionBlock(record, true, record.NativeFingerprint, "ruler"));
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(record));
    }

    [Fact]
    public void LostPatchesAndChangedTermsPreventExecution()
    {
        var record = Captured();
        Assert.Contains("unavailable", ReignGovernmentNativeSafety.ExecutionBlock(record, false, record.NativeFingerprint, "ruler"));
        Assert.Contains("identity or terms changed", ReignGovernmentNativeSafety.ExecutionBlock(record, true, "different terms", "ruler"));
        record.NativeCapturedOwnerClanId = "";
        Assert.NotEmpty(ReignGovernmentNativeSafety.ExecutionBlock(record, true, record.NativeFingerprint, "ruler"));
    }

    [Fact]
    public void PendingAndCompletedRecordsPreserveTheirIndependentReceipts()
    {
        var record = Captured(); record.NativeDecisionAliasIndices.AddRange(new[] { 3, 7 });
        var pending = JsonSerializer.Deserialize<ReignGovernmentBusinessRecord>(JsonSerializer.Serialize(record, SaveOptions), SaveOptions)!;
        Assert.Empty(ReignGovernmentNativeSafety.ExecutionBlock(pending, true, pending.NativeFingerprint, "ruler"));
        Assert.Equal(new[] { 3, 7 }, pending.NativeDecisionAliasIndices);
        pending.ExecutionApplied = true; pending.ConsequencesApplied = false;
        Assert.False(ReignGovernmentNativeSafety.TryBeginEffect(pending));
        Assert.False(pending.ConsequencesApplied);
    }

    [Fact]
    public void EveryGovernmentHarmonyClassHasAnExplicitLifecycleRegistration()
    {
        string root = TestOptions.FindWorkspace();
        string folder = Path.Combine(root, "ReignBeta", "src", "Modules", "Government", "Campaign");
        var classes = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"internal static class (ReignGovernment\w+Patch)\b")
                .Select(match => match.Groups[1].Value)).OrderBy(x => x).ToArray();
        string lifecycle = File.ReadAllText(Path.Combine(folder, "ReignGovernmentPatches.cs"));
        string registry = lifecycle[lifecycle.IndexOf("Type[] PatchClasses", StringComparison.Ordinal)..lifecycle.IndexOf("internal static void Apply()", StringComparison.Ordinal)];
        var registered = Regex.Matches(registry, @"typeof\((ReignGovernment\w+Patch)\)").Select(m => m.Groups[1].Value).OrderBy(x => x).ToArray();
        Assert.NotEmpty(classes); Assert.Equal(classes, registered);
        string module = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Platform", "SubModule.cs"));
        Assert.Contains("ReignGovernmentPatches.Apply();", module);
        Assert.Contains("ReignGovernmentPatches.Unapply();", module);
        Assert.Contains("Harmony.GetPatchInfo", lifecycle);
        Assert.Contains("p.owner == HarmonyId", lifecycle);
        string decisions = File.ReadAllText(Path.Combine(folder, "ReignGovernmentNativeDecisions.cs"));
        string fallback = decisions[decisions.IndexOf("private void OnNativeKingdomDecisionAdded", StringComparison.Ordinal)..decisions.IndexOf("private void MigrateNativeGovernmentBusiness", StringComparison.Ordinal)];
        Assert.DoesNotContain("TryCaptureNativeDecision(", fallback);
        Assert.Contains("TryConsumeEffectPermit", decisions);
        Assert.Contains("OnGovernmentSettlementOwnerChanged", decisions);
        Assert.DoesNotContain("if (method != null) yield return method", decisions);
    }
}
