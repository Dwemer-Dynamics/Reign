using System.Text.Json;
using System.Text.RegularExpressions;
using ReignBeta.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentNativeSafetyTests
{
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
        var classes = new[] { "ReignGovernmentNativeDecisions.cs", "ReignGovernmentNativeControls.cs", "ReignGovernmentNativeOffers.cs" }
            .SelectMany(file => Regex.Matches(File.ReadAllText(Path.Combine(folder, file)), @"internal static class (ReignGovernment\w+Patch)\b")
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
