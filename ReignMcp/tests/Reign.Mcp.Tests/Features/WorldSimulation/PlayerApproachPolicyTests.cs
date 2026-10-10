using ReignBeta.Shared;
using ReignBeta.UI;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class PlayerApproachPolicyTests
{
    [Theory]
    [InlineData(true, true, false, false, true)] // An external noble visiting the keep.
    [InlineData(true, true, true, true, false)] // A noble travelling with the player.
    [InlineData(true, true, true, false, false)] // Native party ownership alone excludes.
    [InlineData(true, true, false, true, false)] // Roster membership alone excludes a guest.
    [InlineData(false, true, false, false, false)] // A settlement notable is not a noble.
    [InlineData(true, false, false, false, false)] // An absent noble cannot approach.
    public void KeepInvitationsRequireExternalNobles(bool lord, bool present, bool playerParty, bool playerRoster, bool eligible)
        => Assert.Equal(eligible, PlayerApproachPolicy.EligibleKeepNoble(lord, present, playerParty, playerRoster));

    [Fact]
    public void NobleJoiningPlayerPartyInvalidatesQueuedKeepInvitation()
    {
        Assert.True(PlayerApproachPolicy.EligibleKeepNoble(true, true, false, false));
        Assert.False(PlayerApproachPolicy.EligibleKeepNoble(true, true, true, true));
    }

    [Fact]
    public void EverySwitchCombinationRetainsIndependentPreferences()
    {
        string[] keys = PlayerApproachOptions.SettingKeys.ToArray();
        string[] kinds = { "shared", "keep", "patrol", "battle_rescue", "prison_rescue" };
        string[] kindKeys = { PlayerApproachOptions.JealousyKey, PlayerApproachOptions.KeepKey, PlayerApproachOptions.PatrolKey,
            PlayerApproachOptions.BattleKey, PlayerApproachOptions.PrisonKey };
        for (int mask = 0; mask < 128; mask++)
        {
            var preferences = keys.Select((key, index) => (key, value: (mask & (1 << index)) != 0)).ToDictionary(p => p.key, p => p.value);
            var options = new PlayerApproachOptions(key => preferences[key]);
            foreach (string key in keys) Assert.Equal(preferences[key], options.Preference(key));
            Assert.Equal(preferences[PlayerApproachOptions.MasterKey] && preferences[PlayerApproachOptions.AttentionKey], options.TrackAttention);
            for (int i = 0; i < kinds.Length; i++) Assert.Equal(preferences[PlayerApproachOptions.MasterKey] && preferences[kindKeys[i]], options.Allows(kinds[i]));
            Assert.False(options.Allows("unknown"));
            // Caller mutations cannot alter the snapshot used for this interaction.
            preferences[PlayerApproachOptions.MasterKey] = !preferences[PlayerApproachOptions.MasterKey];
            Assert.Equal((mask & 1) != 0, options.Preference(PlayerApproachOptions.MasterKey));
        }
        var defaults = new PlayerApproachOptions();
        Assert.True(defaults.TrackAttention);
        Assert.All(kinds, kind => Assert.True(defaults.Allows(kind)));
    }

    [Fact]
    public void PresentationDoesNotHoldTimeOrTakeFocusUntilNativeLoadingCompletes()
    {
        bool timeHeld = false;
        int presentations = 0;
        Action present = () => { timeHeld = true; presentations++; };

        for (int tick = 0; tick < 8; tick++)
            Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(true, false, true, present));
        // The map can report render readiness before its loading artwork clears.
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(true, true, true, present));
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(false, true, false, present));
        Assert.False(timeHeld);
        Assert.Equal(0, presentations);

        Assert.True(ReignPlayerApproachPresentationPolicy.TryPresent(true, true, false, present));
        Assert.True(timeHeld);
        Assert.Equal(1, presentations);
        // The displayed popup takes focus, preventing another presentation.
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(false, true, false, present));
        Assert.Equal(1, presentations);
    }

    [Fact]
    public void PresentationRechecksReadinessAfterAnEarlierEligibleObservation()
    {
        Assert.True(ReignPlayerApproachPresentationPolicy.CanPresent(true, true, false));
        int presentations = 0;
        Action present = () => presentations++;
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(true, true, true, present));
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(true, false, false, present));
        Assert.False(ReignPlayerApproachPresentationPolicy.TryPresent(false, true, false, present));
        Assert.Equal(0, presentations);
        Assert.True(ReignPlayerApproachPresentationPolicy.TryPresent(true, true, false, present));
        Assert.Equal(1, presentations);
    }

    [Theory]
    [InlineData(0, 100, 10, .25)]
    [InlineData(50, 50, 6, 1)]
    [InlineData(100, 0, 2, 2.25)]
    public void PersonalityControlsPacingAndSensitivity(int jealousy, int patience, int interval, double sensitivity)
    {
        Assert.Equal(interval, PlayerApproachPolicy.SharedDayInterval(jealousy, patience));
        Assert.Equal(sensitivity, PlayerApproachPolicy.Sensitivity(jealousy, patience));
    }
    [Fact] public void AttentionToTwoPeopleIsNotPooledIntoOneRival()
    {
        Assert.Equal(5, PlayerApproachPolicy.NeglectScore(new[] { 4, 4 }, 50, 50));
        Assert.Equal(8, PlayerApproachPolicy.NeglectScore(new[] { 8 }, 50, 50));
        Assert.Equal(2, PlayerApproachPolicy.NeglectLevel(5));
        Assert.Equal(3, PlayerApproachPolicy.NeglectLevel(8));
    }
    [Theory]
    [InlineData(19, 5)] [InlineData(20, 10)] [InlineData(49, 10)]
    [InlineData(50, 15)] [InlineData(99, 15)] [InlineData(100, 20)]
    [InlineData(199, 20)] [InlineData(200, 25)]
    public void HealthyPartySizeHasExactPatrolThresholds(int members, int chance) => Assert.Equal(chance, PlayerApproachPolicy.PatrolApproachChance(members));
    [Fact] public void IndependentNobleChancesHaveCorrectCombinedProbability()
    {
        Assert.Equal(.36, PlayerApproachPolicy.AnyApproachProbability(new[] { 20, 20 }), 8);
        Assert.Equal(0, PlayerApproachPolicy.KeepApproachChance(19, 99));
        Assert.Equal(2, PlayerApproachPolicy.KeepApproachChance(19, 100));
        Assert.Equal(100, PlayerApproachPolicy.KeepApproachChance(95, 500));
    }
    [Fact] public void NeglectAndPassingConsequencesRespectCaps()
    {
        Assert.Equal(0, PlayerApproachPolicy.NeglectApproachChance(100, 0, 100, 0, 100));
        Assert.Equal(80, PlayerApproachPolicy.NeglectApproachChance(100, 0, 100, 4, 100));
        Assert.Equal(8, PlayerApproachPolicy.NeglectRefusalPenalty(4, 100, 0));
        Assert.Equal(1, PlayerApproachPolicy.NeglectRefusalPenalty(1, 0, 100));
        Assert.Equal(5, PlayerApproachPolicy.PassingPenalty(100, 100, 0));
        Assert.Equal(1, PlayerApproachPolicy.PassingPenalty(0, 0, 100));
    }
    [Fact] public void BattleRescueUsesFrozenHealthyTierStrengthAndStrictOutmatch()
    {
        Assert.Equal(360, PlayerApproachPolicy.FightingUnitPower(10, 5));
        Assert.Equal(0, PlayerApproachPolicy.FightingUnitPower(-1, 5));
        Assert.True(PlayerApproachPolicy.OutmatchedBeforeJoining(100, 101));
        Assert.False(PlayerApproachPolicy.OutmatchedBeforeJoining(100, 100));
        Assert.False(PlayerApproachPolicy.OutmatchedBeforeJoining(0, 100));
        Assert.False(PlayerApproachPolicy.OutmatchedBeforeJoining(100, double.PositiveInfinity));
    }
}
