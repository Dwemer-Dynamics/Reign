using ReignBeta.World;
using Xunit;

namespace Reign.Mcp.Tests.Features.WorldSimulation;

public sealed class RulerReleaseActionPolicyTests
{
    [Theory]
    [InlineData(ReignWorldActionType.DiplomacyCaravanProtectionAgreement)]
    [InlineData(ReignWorldActionType.DiplomacySignTradeAgreement)]
    [InlineData(ReignWorldActionType.DiplomacyDeclareWar)]
    [InlineData(ReignWorldActionType.DiplomacyMakePeace)]
    [InlineData(ReignWorldActionType.DiplomacyExchangePrisoners)]
    [InlineData(ReignWorldActionType.RegularTransferGold)]
    [InlineData(ReignWorldActionType.RegularKillCharacter)]
    public void Counterpart_does_not_become_an_implicit_release_target(ReignWorldActionType type)
        => Assert.Equal("", RulerReleaseActionPolicy.PrisonerId(type, "", "", "sentenced_counterpart"));

    [Theory]
    [InlineData(ReignWorldActionType.DiplomacyRansomPackage)]
    [InlineData(ReignWorldActionType.DiplomacyPackage)]
    [InlineData(ReignWorldActionType.RegularTradePackage)]
    public void Package_protects_only_its_explicit_captive(ReignWorldActionType type)
    {
        Assert.Equal("sentenced_captive", RulerReleaseActionPolicy.PrisonerId(type, "sentenced_captive", "", "other"));
        Assert.Equal("", RulerReleaseActionPolicy.PrisonerId(type, "", "", "sentenced_counterpart"));
    }

    [Fact]
    public void Trade_alias_and_direct_transfer_match_the_executor_targets()
    {
        Assert.Equal("alias_captive", RulerReleaseActionPolicy.PrisonerId(ReignWorldActionType.RegularTradePackage, "", "alias_captive", "buyer"));
        Assert.Equal("named", RulerReleaseActionPolicy.PrisonerId(ReignWorldActionType.RegularTradePackage, "named", "alias", "buyer"));
        Assert.Equal("captive", RulerReleaseActionPolicy.PrisonerId(ReignWorldActionType.RegularTransferPrisoner, "", "", "captive"));
        Assert.Equal("named", RulerReleaseActionPolicy.PrisonerId(ReignWorldActionType.RegularTransferPrisoner, "named", "", "recipient"));
    }

    [Theory]
    [InlineData(ReignWorldActionType.RegularReleaseArrestedCharacter)]
    [InlineData(ReignWorldActionType.RegularRescindArrestAccusation)]
    public void Both_arrest_release_routes_protect_the_actual_accused(ReignWorldActionType type)
        => Assert.Equal("accused", RulerReleaseActionPolicy.PrisonerId(type, "unrelated", "", "accused"));

    [Fact]
    public void Native_validator_keeps_release_bulk_exchange_peace_and_execution_guards_separate()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/World/ReignActionValidator.cs"));
        Assert.Contains("RulerReleaseActionPolicy.PrisonerId(action.Type", source);
        Assert.Contains("BlocksRelease(ReignObjectResolver.FindHero(releasePrisonerId))", source);
        Assert.DoesNotContain("if (ReignRulerCampaignBehavior.BlocksRelease(rulerTarget))", source);
        Assert.Contains("SelectedSentence(targetRealm, actorRealm, limit)", source);
        Assert.Contains("ReignRulerCampaignBehavior.BlocksPeace", source);
        Assert.Contains("ReignRulerCampaignBehavior.Instance?.AwaitsDecision(rulerTarget)", source);
        Assert.Contains("&& (ReignRulerCampaignBehavior.BlocksRelease(rulerTarget)", source);
    }
}
