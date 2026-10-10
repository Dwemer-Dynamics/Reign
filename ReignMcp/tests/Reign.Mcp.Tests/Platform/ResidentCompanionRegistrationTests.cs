using ReignBeta.Campaign;

namespace Reign.Mcp.Tests;

public sealed class ResidentCompanionRegistrationTests
{
    [Fact]
    public void LegacyCivilianBecomesExactlyOneCompanionWithoutChangingIdentityOrCaravan()
    {
        var state = new NativeState();
        object identity = state.Hero, caravan = state.Party;
        state.Ensure();
        state.Ensure();
        Assert.Same(identity, state.Hero);
        Assert.Same(caravan, state.Party);
        Assert.Same(state.PlayerClan, state.Clan);
        Assert.Same(state.PlayerClan, state.CompanionOf);
        Assert.Null(state.FamilyClan);
        Assert.Empty(state.Family);
        Assert.Same(identity, Assert.Single(state.Companions));
        Assert.Same(identity, Assert.Single(state.Heroes));
        Assert.True(state.Wanderer);
        Assert.Equal(1, state.AddCalls);
        Assert.Equal(1, state.OccupationCalls);
        Assert.All(state.ClanEvents, clan => Assert.Same(state.PlayerClan, clan));
    }

    [Fact]
    public void ExistingCompanionWithLegacyFamilyEntryDoesNotRegisterTwice()
    {
        var state = new NativeState();
        state.CompanionOf = state.PlayerClan;
        state.Companions.Add(state.Hero);
        state.Ensure();
        Assert.Empty(state.Family);
        Assert.Single(state.Companions);
        Assert.Equal(0, state.AddCalls);
        Assert.True(state.Wanderer);
    }

    [Fact]
    public void AlreadyRebuiltNativeCachesStillRepairTheSameSavedHero()
    {
        var state = new NativeState();
        // Native load omitted the non-lord from family caches, but retained Clan.
        state.Family.Clear();
        state.Heroes.Clear();
        state.Ensure();
        Assert.Same(state.Hero, Assert.Single(state.Companions));
        Assert.Same(state.Hero, Assert.Single(state.Heroes));
        Assert.Null(state.FamilyClan);
        Assert.Equal(1, state.CacheRebuilds);
        Assert.Equal(1, state.AddCalls);
    }

    [Fact]
    public void MissingCombinedRosterCannotBeReportedAsCompleteAndRetryDoesNotRecruitAgain()
    {
        var state = new NativeState();
        state.FamilyClan = null;
        state.Family.Clear();
        state.CompanionOf = state.PlayerClan;
        state.Companions.Add(state.Hero);
        state.Heroes.Clear();
        state.RebuildCache = () => { };
        Assert.Throws<InvalidOperationException>(state.Ensure);
        Assert.False(state.Wanderer);
        state.RebuildCache = state.NativeRebuild;
        state.Ensure();
        Assert.Same(state.Hero, Assert.Single(state.Heroes));
        Assert.Single(state.Companions);
        Assert.Equal(0, state.AddCalls);
        Assert.Equal(1, state.CacheRebuilds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedRegistrationPreservesOriginalMembershipAndCanRetry(bool throws)
    {
        var state = new NativeState();
        state.Add = () => { if (throws) throw new InvalidOperationException("native unavailable"); };
        Assert.Throws<InvalidOperationException>(state.Ensure);
        Assert.Same(state.PlayerClan, state.Clan);
        Assert.Same(state.Hero, Assert.Single(state.Family));
        Assert.Empty(state.Companions);
        Assert.False(state.Wanderer);
        state.Add = state.NativeAdd;
        state.Ensure();
        Assert.Empty(state.Family);
        Assert.Single(state.Companions);
    }

    [Fact]
    public void ThrowingNativeEventAfterRegistrationRetainsProgressWithoutDuplicateCompanion()
    {
        var state = new NativeState();
        state.Add = () => { state.NativeAdd(); throw new InvalidOperationException("event failed after commit"); };
        Assert.Throws<InvalidOperationException>(state.Ensure);
        Assert.Same(state.PlayerClan, state.FamilyClan);
        Assert.Single(state.Companions);
        state.Ensure();
        Assert.Null(state.FamilyClan);
        Assert.Single(state.Companions);
        Assert.Equal(1, state.AddCalls);
        Assert.True(state.Wanderer);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOccupationChangeNeverReportsSuccessAndResumesWithoutReadding(bool afterCommit)
    {
        var state = new NativeState();
        state.ChangeOccupation = () => {
            if (afterCommit) state.NativeOccupation();
            throw new InvalidOperationException("occupation event failed");
        };
        Assert.Throws<InvalidOperationException>(state.Ensure);
        Assert.Single(state.Companions);
        state.ChangeOccupation = state.NativeOccupation;
        state.Ensure();
        Assert.True(state.Wanderer);
        Assert.Equal(1, state.AddCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClanlessTemporaryGuestOrForeignResidentCannotBeRecruitedByRepair(bool foreign)
    {
        var state = new NativeState();
        object? original = foreign ? new object() : null;
        state.FamilyClan = original;
        Assert.Throws<InvalidOperationException>(state.Ensure);
        Assert.Same(original, state.FamilyClan);
        Assert.Equal(0, state.AddCalls);
        Assert.Equal(0, state.OccupationCalls);
    }

    [Fact]
    public void NativeAdapterUsesClanAuthorityAndGuardsNewJoinCompanionLimits()
    {
        string root = TestOptions.FindWorkspace();
        string resident = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Characters/Campaign/ReignEncounteredResidentRecruitment.cs"));
        string repair = resident[resident.IndexOf("private void RepairResidentClanMembers()", StringComparison.Ordinal)..];
        repair = repair[..repair.IndexOf("private static void CaptureDutySource", StringComparison.Ordinal)];
        Assert.Contains("hero.Clan != Clan.PlayerClan", repair);
        Assert.Contains("hero.IsLord || hero.IsNotable", repair);
        Assert.DoesNotContain("CaravanPartyComponent", repair);
        Assert.DoesNotContain("AddHeroToPartyAction", repair);
        Assert.Contains("Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit", resident);
        Assert.Contains("resident.Military && !resident.ReleasedFromDuty", resident);
        string validator = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/World/ReignActionValidator.cs"));
        string join = validator[validator.IndexOf("case ReignWorldActionType.RegularJoinClan:", StringComparison.Ordinal)..];
        join = join[..join.IndexOf("case ReignWorldActionType.RegularLeaveClan:", StringComparison.Ordinal)];
        Assert.Contains("ValidateCompanionMembership(hero, out reason)", join);
        string behavior = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Characters/Campaign/ReignEncounteredResidentsCampaignBehavior.cs"));
        Assert.Contains("OnGameLoadFinishedEvent.AddNonSerializedListener(this, RepairResidentClanMembers)", behavior);
        string history = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignWorldHistoryCampaignBehavior.cs"));
        Assert.Contains("if (hero?.Clan == oldClan) return;", history);
    }

    private sealed class NativeState
    {
        public readonly object PlayerClan = new(), Hero = new(), Party = new();
        public object? FamilyClan, CompanionOf;
        public object? Clan => CompanionOf ?? FamilyClan;
        public readonly List<object> Family = new(), Companions = new(), Heroes = new();
        public readonly List<object?> ClanEvents = new();
        public bool Wanderer;
        public int AddCalls, OccupationCalls, CacheRebuilds;
        public Action Add, ChangeOccupation, RebuildCache;

        public NativeState()
        {
            FamilyClan = PlayerClan;
            Family.Add(Hero);
            Heroes.Add(Hero);
            Add = NativeAdd;
            ChangeOccupation = NativeOccupation;
            RebuildCache = NativeRebuild;
        }

        public void NativeAdd()
        {
            AddCalls++;
            CompanionOf = PlayerClan;
            Companions.Add(Hero);
            Heroes.Add(Hero);
        }

        public void NativeRebuild()
        {
            CacheRebuilds++;
            // Native CompanionOf setter removes then adds both cache entries,
            // without dispatching AddCompanionAction's recruitment event.
            CompanionOf = null;
            Companions.Remove(Hero);
            Heroes.Remove(Hero);
            CompanionOf = PlayerClan;
            Companions.Add(Hero);
            Heroes.Add(Hero);
        }

        public void NativeOccupation() { OccupationCalls++; Wanderer = true; }

        private void SetFamilyClan(object? clan)
        {
            if (ReferenceEquals(FamilyClan, clan)) return;
            Family.Remove(Hero);
            Heroes.Remove(Hero);
            FamilyClan = clan;
            if (clan != null) { Family.Add(Hero); Heroes.Add(Hero); }
            ClanEvents.Add(Clan);
        }

        public void Ensure() => ReignResidentCompanionRegistration.Ensure(PlayerClan, () => Clan,
            SetFamilyClan, () => CompanionOf, () => Add(), () => Companions.Contains(Hero),
            () => Heroes.Contains(Hero), () => RebuildCache(),
            () => Wanderer, () => ChangeOccupation());
    }
}
