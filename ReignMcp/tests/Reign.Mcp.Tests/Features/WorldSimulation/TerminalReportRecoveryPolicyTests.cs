using ReignBeta.Platform.Domain;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class TerminalReportRecoveryPolicyTests
{
    private static TerminalReportRecoveryInput Inherited(string phase, bool verified = true, bool sameGeneration = false) => new()
    {
        Phase = phase, PayloadExact = true, Inherited = true, LoadVerified = verified,
        GenerationMatchesLive = sameGeneration, OriginMatchesLoad = true
    };

    [Theory]
    [InlineData("captured")]
    [InlineData("posted")]
    [InlineData("native_pending")]
    public void Inherited_report_under_unverified_load_holds_even_when_generations_match(string phase)
    {
        foreach (bool same in new[] { true, false })
        {
            var decision = ReignTerminalReportRecoveryPolicy.Delivery(Inherited(phase, verified: false, sameGeneration: same));
            Assert.Equal(TerminalReportRoute.Hold, decision.Route);
            Assert.Equal("load_unverified", decision.Reason);
        }
    }

    [Fact]
    public void Verified_inherited_report_uses_v1_with_proof_or_recovery_by_generation()
    {
        var same = ReignTerminalReportRecoveryPolicy.Delivery(Inherited("posted", sameGeneration: true));
        Assert.Equal(TerminalReportRoute.V1, same.Route);
        Assert.True(same.AttachProof);
        var other = ReignTerminalReportRecoveryPolicy.Delivery(Inherited("captured"));
        Assert.Equal(TerminalReportRoute.Recover, other.Route);
        Assert.True(other.AttachProof);
        var wrongOrigin = Inherited("posted");
        wrongOrigin.OriginMatchesLoad = false;
        Assert.Equal("origin_mismatch", ReignTerminalReportRecoveryPolicy.Delivery(wrongOrigin).Reason);
        var invalidated = Inherited("posted", sameGeneration: true);
        invalidated.LoadProofStale = true;
        Assert.Equal("load_proof_invalidated", ReignTerminalReportRecoveryPolicy.Delivery(invalidated).Reason);
    }

    [Fact]
    public void New_report_keeps_unproven_v1_only_in_its_own_generation()
    {
        var degraded = new TerminalReportRecoveryInput { Phase = "captured", PayloadExact = true, GenerationMatchesLive = true };
        var decision = ReignTerminalReportRecoveryPolicy.Delivery(degraded);
        Assert.Equal(TerminalReportRoute.V1, decision.Route);
        Assert.False(decision.AttachProof);
        degraded.GenerationMatchesLive = false;
        Assert.Equal("generation_changed_in_session", ReignTerminalReportRecoveryPolicy.Delivery(degraded).Reason);
    }

    [Fact]
    public void Legacy_and_quarantine_never_send()
    {
        Assert.Equal("legacy_outcome_unknown", ReignTerminalReportRecoveryPolicy.Delivery(Inherited("legacy", sameGeneration: true)).Reason);
        var unproven = Inherited("posted", sameGeneration: true);
        unproven.Quarantined = true;
        unproven.PayloadExact = false;
        Assert.Equal("quarantine_payload_unproven", ReignTerminalReportRecoveryPolicy.Delivery(unproven).Reason);
        var offered = new TerminalReportRecoveryInput { Phase = "offered", Quarantined = true, PayloadExact = true };
        Assert.Equal(TerminalReportRoute.None, ReignTerminalReportRecoveryPolicy.Delivery(offered).Route);
        // An intact captured report on a quarantined record is still delivery-only under its row.
        var intact = Inherited("captured");
        intact.Quarantined = true;
        Assert.Equal(TerminalReportRoute.Recover, ReignTerminalReportRecoveryPolicy.Delivery(intact).Route);
    }

    [Fact]
    public void Settled_reports_reconcile_only_on_redelivery_under_a_verified_load()
    {
        Assert.Equal(TerminalReportRoute.None, ReignTerminalReportRecoveryPolicy.Delivery(Inherited("acknowledged", sameGeneration: true)).Route);
        var redelivered = Inherited("acknowledged", sameGeneration: true);
        redelivered.Redelivered = true;
        Assert.Equal("acknowledged_unprovable", ReignTerminalReportRecoveryPolicy.Delivery(redelivered).Reason);
        redelivered.HasAckNative = true;
        Assert.Equal(TerminalReportRoute.V1, ReignTerminalReportRecoveryPolicy.Delivery(redelivered).Route);
        redelivered.LoadVerified = false;
        Assert.Equal("load_unverified", ReignTerminalReportRecoveryPolicy.Delivery(redelivered).Reason);
    }

    [Fact]
    public void Remaining_delivery_rows_hold_or_recover_as_specified()
    {
        var inheritedStale = Inherited("stale");
        inheritedStale.Redelivered = true;
        Assert.Equal(TerminalReportRoute.Recover, ReignTerminalReportRecoveryPolicy.Delivery(inheritedStale).Route);
        var newSettled = new TerminalReportRecoveryInput { Phase = "acknowledged", PayloadExact = true, GenerationMatchesLive = true,
            Redelivered = true, HasAckNative = true };
        Assert.Equal("load_unverified", ReignTerminalReportRecoveryPolicy.Delivery(newSettled).Reason);
        var newStaleProof = new TerminalReportRecoveryInput { Phase = "posted", PayloadExact = true, GenerationMatchesLive = true,
            LoadVerified = true, LoadProofStale = true };
        Assert.Equal("load_proof_invalidated", ReignTerminalReportRecoveryPolicy.Delivery(newStaleProof).Reason);
    }

    [Theory]
    [InlineData("acknowledged", "absent", null, "none", "none", "done")]
    [InlineData("acknowledged", "absent", null, "none", "applied:x", "hold:native_effect_divergent")]
    [InlineData("refused", "absent", null, "pending", null, "hold:native_phase_unproven")]
    [InlineData("posted", "absent", null, "mystery", null, "hold:native_effect_status_unrecognized")]
    [InlineData("posted", "absent", null, "pending", null, "apply")]
    [InlineData("native_pending", "absent", "v3", "pending", null, "apply")]
    [InlineData("native_pending", "absent", null, "pending", null, "hold:native_pending_premarker")]
    [InlineData("posted", "complete", null, "pending", null, "replay")]
    [InlineData("posted", "complete", null, "applied", null, "done")]
    [InlineData("posted", "absent", null, "applied", null, "hold:native_server_applied_journal_absent")]
    [InlineData("acknowledged", "absent", null, "pending", "applied:x", "hold:native_effect_divergent")]
    [InlineData("posted", "complete", null, "none", null, "hold:native_effect_divergent")]
    [InlineData("posted", "absent", null, "none", null, "done")]
    [InlineData("posted", "mismatch", null, "pending", null, "hold:native_parameter_mismatch")]
    [InlineData("posted", "incomplete", null, "pending", null, "hold:native_observation_incomplete")]
    [InlineData("posted", "unpersisted", null, "pending", null, "hold:native_journal_unpersisted")]
    [InlineData("posted", "unreadable", "v3", "pending", null, "hold:native_journal_unreadable")]
    public void Native_effect_follows_the_persisted_journal(string phase, string journal, string? attempt, string server, string? ackNative, string expected) =>
        Assert.Equal(expected, ReignTerminalReportRecoveryPolicy.NativeAction(phase, journal, attempt, server, ackNative));
}
