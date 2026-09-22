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
        Assert.Equal(charm, ReignXpRules.Amount(ReignXpRules.ConversationXp, options));
        Assert.Equal(bonus, ReignXpRules.Amount(ReignXpRules.SocialBonusXp, options));
        Assert.Equal(petition, ReignXpRules.Amount(ReignXpRules.PetitionXp, options));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(5.25)] [InlineData(.3)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)]
    public void InvalidSettingsCannotAwardXp(double multiplier)
    {
        Assert.False(ReignXpRules.IsValidMultiplier(multiplier));
        Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions { Confirmed = true, Multiplier = multiplier }));
    }

    [Fact]
    public void DisabledOrNeverConfirmedOptionsGiveNothing()
    {
        Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions()));
        Assert.Equal(0, ReignXpRules.Amount(5, new ReignXpOptions { Confirmed = true, Enabled = false }));
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
