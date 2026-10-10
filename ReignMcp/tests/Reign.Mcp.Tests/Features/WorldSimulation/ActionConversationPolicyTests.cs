using Reign.Core.Contracts.Actions;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class ActionConversationPolicyTests
{
    public static IEnumerable<object[]> EveryDefinition() => ReignActionCatalog.All.Select(d => new object[] { d.Id });

    [Theory, MemberData(nameof(EveryDefinition))]
    public void EveryActionHasImmersiveChoiceAndSentenceWording(string actionId)
    {
        var d = ReignActionCatalog.Find(actionId)!;
        foreach (bool playerActs in new[] { false, true })
        {
            Assert.DoesNotMatch(@"(?i)\bplayer\b", ReignActionLanguage.ChoiceLabel(d, playerActs));
            Assert.DoesNotMatch(@"(?i)\bplayer\b", ReignActionLanguage.Subject(playerActs, 0) + ReignActionLanguage.Verb(d, playerActs));
            Assert.DoesNotMatch(@"(?i)\bplayer\b", ReignActionLanguage.Subject(playerActs, 1) + ReignActionLanguage.Verb(d, playerActs));
            Assert.Equal(actionId, d.Id); // Presentation never changes dispatch identity.
        }
    }

    [Theory]
    [InlineData("duel_player", "I would like you to duel me")]
    [InlineData("attack_player_party", "I would like you to attack my party")]
    [InlineData("surrender_to_player", "I would like you to surrender to me")]
    [InlineData("hire_player_as_mercenary", "I would like you to hire me as a mercenary")]
    [InlineData("offer_player_vassalage", "I would like you to accept my oath of vassalage")]
    public void NpcRequestsAddressTheSpeakerNaturally(string id, string expected)
        => Assert.Equal(expected, ReignActionLanguage.Subject(false, 0) + ReignActionLanguage.Verb(ReignActionCatalog.Find(id)!, false));

    [Fact] public void BargainKeepsBothActorsAndNaturalPronouns()
    {
        string sentence = ReignActionLanguage.Subject(true, 0) + "give you 100 denars "
            + ReignActionLanguage.Subject(false, 1) + ReignActionLanguage.Verb(ReignActionCatalog.Find("duel_player")!, false);
        Assert.Equal("I wish to give you 100 denars and you will duel me", sentence);
    }

    [Theory]
    [InlineData("duel_player", false)] [InlineData("surrender_to_player", false)]
    [InlineData("accept_temporary_party_guest", false)] [InlineData("hire_player_as_mercenary", false)]
    [InlineData("court.serve_office", false)] [InlineData("spymaster.person_skills", false)]
    [InlineData("prepare_arrest", true)] [InlineData("rescind_arrest_accusation", true)]
    [InlineData("player_attack_party", true)] [InlineData("join_rebellion", true)]
    public void FixedActorCapabilitiesAreOfferedOnlyInTheirExecutableDirection(string id, bool playerActs)
    {
        var d = ReignActionCatalog.Find(id)!;
        Assert.True(ReignActionConversationPolicy.Allows(d, playerActs));
        Assert.False(ReignActionConversationPolicy.Allows(d, !playerActs));
    }

    [Theory, MemberData(nameof(EveryDefinition))]
    public void EveryImpossibleDirectionIsAlsoRejectedAtConversationValidation(string id)
    {
        var definition = ReignActionCatalog.Find(id)!;
        foreach (bool playerActs in new[] { false, true })
        {
            if (!definition.ConversationEligible || ReignActionConversationPolicy.Allows(definition, playerActs)) continue;
            var agreement = new ActionAgreement { PlayerId = "hero_one", NpcId = "hero_two", ProposerId = "hero_one", Origin = ActionOrigin.Conversation };
            agreement.Clauses.Add(new() { ActionId = id, ActorId = playerActs ? "hero_one" : "hero_two", RecipientId = playerActs ? "hero_two" : "hero_one" });
            Assert.Contains("This action cannot be performed in the selected conversation direction.", AgreementRules.Validate(agreement, ReignActionCatalog.Find, 0));
        }
    }

    [Fact] public void PlayerMovementIsHiddenAndExchangesRemainAvailableOnBothSides()
    {
        Assert.All(ReignActionCatalog.All.Where(d => d.MovesActor), d => Assert.False(ReignActionConversationPolicy.Allows(d, true)));
        Assert.All(ReignActionCatalog.All.Where(d => d.Id.StartsWith("give.")), d =>
        {
            Assert.True(ReignActionConversationPolicy.Allows(d, true));
            Assert.True(ReignActionConversationPolicy.Allows(d, false));
        });
        var declaration = ReignActionCatalog.Find("start_ruling_clan_rebellion")!;
        Assert.True(ReignActionConversationPolicy.Allows(declaration, true));
        Assert.False(ReignActionConversationPolicy.Allows(declaration, true, 1));
        Assert.False(ReignActionConversationPolicy.Allows(declaration, false));
    }

    [Fact] public void OperatorCapabilitiesKeepTheirOriginalScope()
    {
        var a = new ActionAgreement { Origin = ActionOrigin.Console, PlayerId = "hero_one", NpcId = "hero_two", ProposerId = "hero_one" };
        a.Clauses.Add(new() { ActionId = "duel_player", ActorId = "hero_one", RecipientId = "hero_two", Arguments = new() { ["terms.duelMode"] = "honor" } });
        Assert.DoesNotContain("This action cannot be performed in the selected conversation direction.", AgreementRules.Validate(a, ReignActionCatalog.Find, 0));
    }

    [Theory]
    [InlineData("follow", "TargetHero", "hero_one", false)]
    [InlineData("follow", "TargetHero", "hero_two", true)]
    [InlineData("kill_character", "TargetHero", "hero_one", false)]
    [InlineData("prepare_arrest", "TargetHero", "unrelated", false)]
    [InlineData("prepare_arrest", "TargetHero", "hero_two", true)]
    [InlineData("marriage_alliance", "terms.marriageHero1StringId", "hero_two", false)]
    [InlineData("marriage_alliance", "terms.marriageHero1StringId", "hero_one", true)]
    [InlineData("marriage_alliance", "terms.marriageHero2StringId", "hero_two", true)]
    [InlineData("marriage_alliance", "terms.marriageHero2StringId", "unrelated", false)]
    [InlineData("accept_temporary_party_guest", "terms.wageRecipientHeroStringId", "hero_one", true)]
    public void TargetChoicesPreserveParticipantsAndExcludeSelfActions(string id, string key, string value, bool expected)
        => Assert.Equal(expected, ReignActionConversationPolicy.AllowsParticipantSelection(id, key, value, "hero_one", "hero_two"));
}
