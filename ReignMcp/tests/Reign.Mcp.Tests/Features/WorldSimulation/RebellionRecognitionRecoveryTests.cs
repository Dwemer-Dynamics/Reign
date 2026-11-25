using System;
using System.Collections.Generic;
using System.Text.Json;
using Reign.Core.Contracts.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class RebellionRecognitionRecoveryTests
{
    private sealed class World
    {
        public ReignRecognitionReceipt Receipt = new()
        {
            CampaignId = "campaign", TimelineId = "timeline", MovementId = "movement",
            ActionId = "original_action", ActionBinding = "original_binding", RebelKingdomId = "rebel",
            ParentKingdomId = "parent", RebelRulerId = "rebel_ruler", ParentRulerId = "parent_ruler",
            AgreementId = "exact_agreement",
        };

        public bool? AtWar = true;
        public int NativeCalls;
        public int Publications;
        public Action? NativeOverride;
        public Func<bool>? PublicationOverride;
        public readonly List<ReignRecognitionPhase> Phases = new();
        public readonly Dictionary<ReignRecognitionPhase, string> Snapshots = new();

        public bool Advance(bool start) => Receipt.Advance(start, () => AtWar, () =>
        {
            NativeCalls++;
            if (NativeOverride == null) AtWar = false;
            else NativeOverride();
        }, () => 142f, () =>
        {
            Publications++;
            return PublicationOverride == null || PublicationOverride();
        }, () =>
        {
            Phases.Add(Receipt.Phase);
            Snapshots[Receipt.Phase] = JsonSerializer.Serialize(Receipt);
        });

        public void Load(ReignRecognitionPhase phase)
            => Receipt = JsonSerializer.Deserialize<ReignRecognitionReceipt>(Snapshots[phase])!;
    }

    [Fact]
    public void ExactAuthorizationAndInvokingIntentPrecedeNativePeace()
    {
        var world = new World();
        world.NativeOverride = () =>
        {
            var invoking = JsonSerializer.Deserialize<ReignRecognitionReceipt>(world.Snapshots[ReignRecognitionPhase.InvokingPeace])!;
            Assert.True(invoking.Matches("campaign", "timeline", "movement", "original_action", "original_binding"));
            Assert.Equal("rebel_ruler", invoking.RebelRulerId);
            Assert.Equal("parent_ruler", invoking.ParentRulerId);
            Assert.True(invoking.WarObservedBefore);
            Assert.False(invoking.PeaceObserved);
            Assert.False(invoking.Matches("campaign", "timeline", "movement", "other_action", "original_binding"));
            Assert.False(invoking.Matches("campaign", "other_timeline", "movement", "original_action", "original_binding"));
            Assert.False(invoking.Matches("campaign", "timeline", "movement", "original_action", "changed_terms"));
            world.AtWar = false;
        };
        Assert.True(world.Advance(start: true));
        Assert.Equal(new[] { ReignRecognitionPhase.InvokingPeace, ReignRecognitionPhase.PeaceVerified,
            ReignRecognitionPhase.Publishing, ReignRecognitionPhase.Published }, world.Phases);
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
    }

    [Fact]
    public void SavedObservedPeaceCompletesOriginalPublicationWithoutAnotherNativeCall()
    {
        var world = new World();
        Assert.True(world.Advance(start: true));
        world.Load(ReignRecognitionPhase.PeaceVerified);
        // Restore the recorded boundary before its local publication.
        world.Publications = 0;
        world.PublicationOverride = () =>
        {
            Assert.Equal("exact_agreement", world.Receipt.AgreementId);
            Assert.Equal(142f, world.Receipt.PeaceDay);
            return true;
        };

        Assert.True(world.Advance(start: false));
        world.Load(ReignRecognitionPhase.Published);
        Assert.True(world.Advance(start: false));
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
    }

    [Fact]
    public void PublishedRecognitionReplaysAsHistoryAfterLaterWarOrRulerChange()
    {
        var world = new World();
        Assert.True(world.Advance(start: true));
        world.Load(ReignRecognitionPhase.Published);
        Assert.True(world.Receipt.Matches("campaign", "timeline", "movement", "original_action", "original_binding"));
        Assert.False(world.Receipt.Matches("campaign", "later_timeline", "movement", "original_action", "original_binding"));
        Assert.True(world.Receipt.HasPublishedOutcome());
        Assert.True(world.Receipt.Advance(false, () => throw new InvalidOperationException("Live authority or war was inspected."),
            () => throw new InvalidOperationException("Native peace repeated."), () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException("Publication repeated."), () => throw new InvalidOperationException()));
        world.AtWar = true;
        Assert.True(world.Advance(start: false));
        world.AtWar = null;
        Assert.True(world.Advance(start: false));
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
        world.Receipt.PeaceDay = null;
        Assert.False(world.Receipt.HasPublishedOutcome());
        Assert.False(world.Advance(start: false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [InlineData(null)]
    public void UnpublishedPeaceProofStillRequiresCurrentExactPeace(bool? currentWar)
    {
        var world = new World();
        Assert.True(world.Advance(start: true));
        world.Load(ReignRecognitionPhase.PeaceVerified);
        world.AtWar = currentWar;
        Assert.Equal(currentWar == false, world.Advance(start: false));
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(currentWar == false ? 2 : 1, world.Publications);
    }

    [Theory]
    [InlineData(ReignRecognitionPhase.InvokingPeace)]
    [InlineData(ReignRecognitionPhase.Publishing)]
    public void ReloadAtAnInvokingBoundaryNeverInfersSuccessOrRepeatsTheEffect(ReignRecognitionPhase phase)
    {
        var world = new World();
        Assert.True(world.Advance(start: true));
        world.Load(phase);
        Assert.False(world.Advance(start: false));
        Assert.Equal(ReignRecognitionPhase.Held, world.Receipt.Phase);
        Assert.Equal("interrupted_transition", world.Receipt.Failure);
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
    }

    [Theory]
    [InlineData(ReignRecognitionPhase.InvokingPeace, true)]
    [InlineData(ReignRecognitionPhase.InvokingPeace, false)]
    [InlineData(ReignRecognitionPhase.Publishing, true)]
    [InlineData(ReignRecognitionPhase.Publishing, false)]
    public void ReloadedRecognitionHoldsOtherCivilWarMutationsBeforeInspectingNativeWar(
        ReignRecognitionPhase phase, bool currentWar)
    {
        var world = new World();
        Assert.True(world.Advance(start: true));
        world.Load(phase);
        world.AtWar = currentWar;
        Assert.False(world.Advance(start: false));
        Assert.Equal(ReignRecognitionPhase.Held, world.Receipt.Phase);
        int observations = 0;
        bool? ObserveWar() { observations++; return currentWar; }
        Assert.False(ReignRecognitionReceipt.AllowsCivilWarMutation(true, true, ObserveWar));
        Assert.Equal(0, observations);
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, null, false)]
    [InlineData(false, true, false)]
    public void OrdinaryCivilWarAdmissionPreservesLiveWarAndRejectsObsoleteOrUnknownPairs(
        bool activeWarRecord, bool? observedWar, bool admitted)
    {
        int effects = 0;
        if (ReignRecognitionReceipt.AllowsCivilWarMutation(activeWarRecord, false, () => observedWar))
            effects++;
        Assert.Equal(admitted ? 1 : 0, effects);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public void EstablishedExternalPeaceOrUnverifiablePairCannotAuthorizeTheFirstNativeCall(bool? knownWar)
    {
        var world = new World { AtWar = knownWar };
        Assert.False(world.Advance(start: true));
        Assert.Equal("exact_war_not_reestablished", world.Receipt.Failure);
        Assert.Equal(0, world.NativeCalls);
        Assert.Equal(0, world.Publications);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoOpOrThrowingNativeCallbackHoldsWithoutPublicationOrRetry(bool throwsAfterPeace)
    {
        var world = new World();
        world.NativeOverride = () =>
        {
            if (!throwsAfterPeace) return;
            world.AtWar = false;
            throw new InvalidOperationException();
        };
        Assert.False(world.Advance(start: true));
        world.Load(ReignRecognitionPhase.Held);
        Assert.False(world.Advance(start: false));
        Assert.False(world.Receipt.PeaceObserved);
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(0, world.Publications);
    }

    [Fact]
    public void MissingObservationAfterNativeCallCannotCompleteASettlement()
    {
        var world = new World();
        world.NativeOverride = () => world.AtWar = null;
        Assert.False(world.Advance(start: true));
        Assert.Equal("exact_peace_unverified", world.Receipt.Failure);
        Assert.Equal(0, world.Publications);
    }

    [Fact]
    public void UncertainPublicationRetainsNativePeaceWithoutPublishingTwice()
    {
        var world = new World { PublicationOverride = () => throw new InvalidOperationException() };
        Assert.False(world.Advance(start: true));
        world.Load(ReignRecognitionPhase.Held);
        Assert.True(world.Receipt.PeaceObserved);
        Assert.False(world.Advance(start: false));
        Assert.Equal(1, world.NativeCalls);
        Assert.Equal(1, world.Publications);
    }

    [Fact]
    public void SavedPreparedIntentAloneDoesNotAuthorizeRecoveryToMakePeace()
    {
        var world = new World();
        Assert.False(world.Advance(start: false));
        Assert.Equal(ReignRecognitionPhase.Held, world.Receipt.Phase);
        Assert.Equal(0, world.NativeCalls);
        Assert.Equal(0, world.Publications);
    }
}
