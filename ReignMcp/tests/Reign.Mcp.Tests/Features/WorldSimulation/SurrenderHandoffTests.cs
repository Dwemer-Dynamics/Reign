using ReignBeta.WorldSimulation.World;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class SurrenderHandoffTests
{
    [Fact]
    public void NativeIntegrationRequiresRealCaptivityAndAdvancesOnlyTheBoundEncounter()
    {
        var workspace = TestOptions.FindWorkspace();
        var source = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "src", "Modules",
            "WorldSimulation", "World", "ReignSurrenderService.cs"));
        Assert.Contains("party.Party.Side == player.OpponentSide", source);
        Assert.Contains("ReferenceEquals(pending.Campaign, NativeCampaign.Current)", source);
        Assert.Contains("ReferenceEquals(PlayerEncounter.Current, pending.Encounter)", source);
        Assert.Contains("ReferenceEquals(PartyBase.MainParty?.MapEvent, pending.Battle)", source);
        Assert.Contains("IsInPlayerCustody(pending.Actor)", source);
        Assert.Contains("actor.IsPrisoner && player != null && actor.PartyBelongedToAsPrisoner == player", source);
        Assert.Contains("t.Character == actor.CharacterObject && t.Number == 1", source);
        Assert.Contains("() => PlayerEncounter.Update()", source);
        Assert.DoesNotContain("TakePrisonerAction.Apply", source.Substring(
            source.IndexOf("internal static void Tick()", StringComparison.Ordinal)));
        Assert.DoesNotContain("PlayerEncounter.Finish()", source);
        Assert.Contains("ReignActionResult.Progress", source);
    }

    [Fact]
    public void PersonalSurrenderRequiresPresenceAndExactConsentAndReportsOnlyHeroCustody()
    {
        var directory = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src", "Modules",
            "WorldSimulation", "World");
        var validator = File.ReadAllText(Path.Combine(directory, "ReignActionValidator.cs"));
        var source = File.ReadAllText(Path.Combine(directory, "ReignSurrenderService.cs"));
        Assert.Contains("return ReignSurrenderService.Validate(action, actorHero, actorParty, proposal, out reason);", validator);
        Assert.Contains("actor.CurrentSettlement != null && actor.CurrentSettlement == player.CurrentSettlement", source);
        Assert.Contains("!string.Equals(action.AcceptedByHeroStringId, actor.StringId", source);
        Assert.Contains("if (!IsEncounterSurrender(actor, party)) return CapturePersonally(actor);", source);
        var capture = source.Substring(source.IndexOf("private static ReignActionResult CapturePersonally", StringComparison.Ordinal));
        capture = capture.Substring(0, capture.IndexOf("private static bool IsInPlayerCustody", StringComparison.Ordinal));
        Assert.Contains("TakePrisonerAction.Apply(player, actor);", capture);
        Assert.Contains("if (!IsInPlayerCustody(actor))", capture);
        Assert.Contains("\"hero_captured\", \"hero\"", capture);
        Assert.DoesNotContain("party_surrendered", capture);
    }

    [Fact]
    public void VictoryWithoutCaptivityDoesNotFulfillAndNativeResultsAreRequestedOnce()
    {
        var handoff = new ReignSurrenderHandoff();
        var calls = 0;
        Assert.Equal(ReignSurrenderHandoffState.AwaitingCapture,
            handoff.Advance(true, false, false, () => calls++));
        Assert.False(handoff.Finished);
        for (var i = 0; i < 20; i++)
            Assert.Equal(ReignSurrenderHandoffState.AwaitingCapture,
                handoff.Advance(true, false, false, () => calls++));
        Assert.Equal(1, calls);
        Assert.Equal(ReignSurrenderHandoffState.Captured,
            handoff.Advance(true, true, true, () => calls++));
        Assert.True(handoff.Finished);
        Assert.Equal(ReignSurrenderHandoffState.Captured,
            handoff.Advance(false, false, false, () => calls++));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void OpenConversationDefersTheNativeCaptureConversation()
    {
        var handoff = new ReignSurrenderHandoff();
        Assert.Equal(ReignSurrenderHandoffState.Waiting,
            handoff.Advance(true, true, false, () => Assert.Fail("Native capture interrupted dialogue.")));
        Assert.False(handoff.ResultsRequested);
        Assert.False(handoff.Finished);
    }

    [Fact]
    public void LostEncounterCannotClaimSuccessOrReplayResults()
    {
        var handoff = new ReignSurrenderHandoff();
        Assert.Equal(ReignSurrenderHandoffState.EncounterLost,
            handoff.Advance(false, false, false, () => Assert.Fail("A foreign encounter was advanced.")));
        Assert.Equal(ReignSurrenderHandoffState.EncounterLost,
            handoff.Advance(true, false, true, () => Assert.Fail("A terminal handoff was replayed.")));
    }

    [Fact]
    public void CaptivityCanCompleteAfterTheNativeEncounterFinalizes()
    {
        var handoff = new ReignSurrenderHandoff();
        Assert.Equal(ReignSurrenderHandoffState.Captured,
            handoff.Advance(false, false, true, () => Assert.Fail("Already captured; no native replay.")));
    }

    [Fact]
    public void ThrowingNativeResultsCallbackCannotBeInvokedAgain()
    {
        var handoff = new ReignSurrenderHandoff();
        Assert.Throws<InvalidOperationException>(() =>
            handoff.Advance(true, false, false, () => throw new InvalidOperationException("native failure")));
        Assert.Equal(ReignSurrenderHandoffState.AwaitingCapture,
            handoff.Advance(true, false, false, () => Assert.Fail("Native effects were retried.")));
    }
}
