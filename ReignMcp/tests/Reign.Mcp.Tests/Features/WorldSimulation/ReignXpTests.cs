using System.Text.Json;
using Reign.Core.Contracts.Dialogue;

namespace Reign.Mcp.Tests;

public sealed class ReignXpTests
{
    [Theory]
    [InlineData(.25, 1.25f, .5f, 2.5f)]
    [InlineData(1, 5f, 2f, 10f)]
    [InlineData(5, 25f, 10f, 50f)]
    public void MultiplierAppliesToEveryRewardWithoutRoundingAwaySmallAmounts(double multiplier, float charm, float bonus, float petition)
    {
        var options = new ReignXpOptions { Confirmed = true, Multiplier = multiplier };
        Assert.Equal(charm, ReignXpRules.Amount(ReignXpRules.ConversationXp, options, ReignXpReason.Conversation));
        Assert.Equal(charm, ReignXpRules.Amount(ReignXpRules.ConversationXp, options, ReignXpReason.SocialCharm));
        Assert.Equal(bonus, ReignXpRules.Amount(ReignXpRules.SocialBonusXp, options, ReignXpReason.SocialBonus));
        Assert.Equal(petition, ReignXpRules.Amount(ReignXpRules.PetitionXp, options, ReignXpReason.PetitionDecision));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(5.25)] [InlineData(.3)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidSettingsCannotAwardXp(double multiplier)
    {
        Assert.False(ReignXpRules.IsValidMultiplier(multiplier));
        Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions { Confirmed = true, Multiplier = multiplier }, ReignXpReason.Conversation));
    }

    [Fact]
    public void DisabledOrNeverConfirmedOptionsGiveNothing()
    {
        foreach (var reason in Enum.GetValues<ReignXpReason>())
        {
            Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions(), reason));
            Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions { Confirmed = true, Enabled = false }, reason));
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(.25)] [InlineData(1)] [InlineData(5)]
    public void EveryCategoryScalesIndependentlyAlongsideUniversalMultiplier(double category)
    {
        var options = new ReignXpOptions { Confirmed = true, Multiplier = 2 };
        foreach (var selected in Enum.GetValues<ReignXpReason>())
        {
            SetCategory(options, selected, category);
            foreach (var reason in Enum.GetValues<ReignXpReason>())
            {
                double baseXp = reason == ReignXpReason.SocialBonus ? 2 : reason == ReignXpReason.PetitionDecision ? 10 : 5;
                Assert.Equal((float)(baseXp * 2 * (reason == selected ? category : 1)), ReignXpRules.Amount(baseXp, options, reason));
            }
            SetCategory(options, selected, 1);
        }
    }

    [Theory]
    [InlineData(-.25)] [InlineData(5.25)] [InlineData(.3)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidCategoryCannotAwardOrChangeOtherCategories(double category)
    {
        Assert.False(ReignXpRules.IsValidCategoryMultiplier(category));
        foreach (var selected in Enum.GetValues<ReignXpReason>())
        {
            var options = new ReignXpOptions { Confirmed = true };
            SetCategory(options, selected, category);
            foreach (var reason in Enum.GetValues<ReignXpReason>())
                Assert.Equal(reason == selected ? 0f : 5f, ReignXpRules.Amount(5, options, reason));
        }
        Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions { Confirmed = true }, (ReignXpReason)999));
    }

    [Fact]
    public void LegacyOptionsDefaultEveryCategoryToOneAndKeepFractionalRewards()
    {
        var options = JsonSerializer.Deserialize<ReignXpOptions>("{\"Confirmed\":true,\"Multiplier\":0.25}")!;
        Assert.Equal(1.25f, ReignXpRules.Amount(5, options, ReignXpReason.Conversation));
        Assert.Equal(1.25f, ReignXpRules.Amount(5, options, ReignXpReason.SocialCharm));
        Assert.Equal(.5f, ReignXpRules.Amount(2, options, ReignXpReason.SocialBonus));
        Assert.Equal(2.5f, ReignXpRules.Amount(10, options, ReignXpReason.PetitionDecision));
        options.SocialBonusMultiplier = .25;
        Assert.Equal(.125f, ReignXpRules.Amount(2, options, ReignXpReason.SocialBonus));
        options.Multiplier = 5;
        options.PetitionMultiplier = 5;
        Assert.Equal(250f, ReignXpRules.Amount(10, options, ReignXpReason.PetitionDecision));
    }

    [Fact]
    public void DisabledCategoryReceiptCannotBeReplayedAfterReenabling()
    {
        var ledger = new ReignXpLedger();
        var options = new ReignXpOptions { Confirmed = true, ConversationMultiplier = 0 };
        Assert.True(ledger.TryClaim("conversation:disabled"));
        Assert.Equal(0, ReignXpRules.Amount(5, options, ReignXpReason.Conversation));
        options.ConversationMultiplier = 1;
        var reloaded = JsonSerializer.Deserialize<ReignXpLedger>(JsonSerializer.Serialize(ledger))!;
        Assert.False(reloaded.TryClaim("conversation:disabled"));
        Assert.True(reloaded.TryClaim("conversation:new"));
        Assert.Equal(5, ReignXpRules.Amount(5, options, ReignXpReason.Conversation));
    }

    private static void SetCategory(ReignXpOptions options, ReignXpReason reason, double value)
    {
        switch (reason)
        {
            case ReignXpReason.Conversation: options.ConversationMultiplier = value; break;
            case ReignXpReason.SocialCharm: options.SocialCharmMultiplier = value; break;
            case ReignXpReason.SocialBonus: options.SocialBonusMultiplier = value; break;
            case ReignXpReason.PetitionDecision: options.PetitionMultiplier = value; break;
        }
    }

    [Theory]
    [InlineData(300, 5, 2, 1, ReignXpSkill.Steward)]
    [InlineData(5, 300, 2, 1, ReignXpSkill.Trade)]
    [InlineData(5, 2, 300, 1, ReignXpSkill.Leadership)]
    [InlineData(5, 2, 1, 300, ReignXpSkill.Tactics)]
    public void SoloConversationUsesHighestRelevantSkill(int steward, int trade, int leadership, int tactics, ReignXpSkill expected)
    {
        var person = new ReignXpParticipant { HeroId = "notable", Steward = steward, Trade = trade, Leadership = leadership, Tactics = tactics };
        Assert.Equal(expected, ReignXpRules.SelectPhaseSkill("event:0", new[] { person }));
    }

    [Fact]
    public void AllDistinctSpeakersParticipateInDrawAndRepeatedSpeechAddsNoWeight()
    {
        var trader = new ReignXpParticipant { HeroId = "trader", Trade = 120 };
        var leader = new ReignXpParticipant { HeroId = "leader", Leadership = 160 };
        var outcomes = new HashSet<ReignXpSkill?>();
        for (int i = 0; i < 100; i++)
        {
            string phase = "event_" + i + ":0";
            var expected = ReignXpRules.SelectPhaseSkill(phase, new[] { trader, leader });
            outcomes.Add(expected);
            Assert.Equal(expected, ReignXpRules.SelectPhaseSkill(phase, new[] { leader, trader, trader, trader }));
        }
        Assert.Equal(2, outcomes.Count);
        Assert.Contains(ReignXpSkill.Trade, outcomes);
        Assert.Contains(ReignXpSkill.Leadership, outcomes);
    }

    [Fact]
    public void TiesStayWithinHighestSkillsAndAreStableForTheSamePhase()
    {
        var person = new ReignXpParticipant { HeroId = "lord", Steward = 1, Trade = 80, Leadership = 80, Tactics = 2 };
        for (int i = 0; i < 40; i++)
        {
            var skill = ReignXpRules.SelectPhaseSkill("phase" + i, new[] { person });
            Assert.True(skill == ReignXpSkill.Trade || skill == ReignXpSkill.Leadership);
            Assert.Equal(skill, ReignXpRules.SelectPhaseSkill("phase" + i, new[] { person }));
        }
        Assert.Null(ReignXpRules.SelectPhaseSkill("phase", Array.Empty<ReignXpParticipant>()));
        Assert.Null(ReignXpRules.SelectPhaseSkill("phase", new[] { new ReignXpParticipant { HeroId = "zero" } }));
    }

    [Fact]
    public void ReceiptsAndParticipantsSurviveSaveReloadWithoutRerollsOrReplays()
    {
        var ledger = new ReignXpLedger();
        var trader = new ReignXpParticipant { HeroId = "trader", Trade = 120 };
        Assert.True(ledger.RecordExchange("event:0", "turn-a", new[] { trader }));
        Assert.True(ledger.TryClaim("charm:event:0"));
        string saved = JsonSerializer.Serialize(ledger);
        var reloaded = JsonSerializer.Deserialize<ReignXpLedger>(saved)!;
        Assert.False(reloaded.RecordExchange("event:0", "turn-a", new[] { trader }));
        Assert.False(reloaded.RecordExchange("event:1", "turn-a", new[] { trader }));
        Assert.False(reloaded.TryClaim("charm:event:0"));
        Assert.Single(reloaded.Phases["event:0"]);
        Assert.Equal(ReignXpSkill.Trade, ReignXpRules.SelectPhaseSkill("event:0", reloaded.Phases["event:0"].Values));
        Assert.True(reloaded.TryClaim("bonus:event:0"));
        Assert.False(reloaded.TryClaim("bonus:event:0"));
        reloaded.Phases.Remove("event:0");
        Assert.True(reloaded.RecordExchange("event:0", "turn-b", new[] { trader }));
        Assert.False(reloaded.Phases.ContainsKey("event:0"));
        Assert.False(reloaded.RecordExchange("event:0", "turn-b", new[] { trader }));
        Assert.True(reloaded.RecordExchange("event:1", "turn-c", new[] { trader }));
        // An earlier native save restores both earlier XP and the earlier claim ledger.
        Assert.True(JsonSerializer.Deserialize<ReignXpLedger>(saved)!.TryClaim("bonus:event:0"));
    }
}
