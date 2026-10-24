using Reign.Core.Contracts.Government;

namespace Reign.Mcp.Tests;

public sealed class GovernmentResolutionCostTests
{
    [Theory]
    [InlineData(ReignGovernmentResolutionScale.Minor, 10000)]
    [InlineData(ReignGovernmentResolutionScale.Standard, 25000)]
    [InlineData(ReignGovernmentResolutionScale.Major, 60000)]
    public void FundedAssembliesUseTheTreasuryScaleRatherThanAnActionCount(
        ReignGovernmentResolutionScale scale, int expectedGold)
    {
        Assert.Equal(expectedGold, ReignGovernmentResolutionCatalog.TargetFor(
            ReignGovernmentResolutionAction.HoldFeast, scale));
        foreach (var template in ReignGovernmentResolutionCatalog.Templates.Where(x => x.Scale == scale))
        foreach (var route in new[] { template.FirstRoute, template.SecondRoute })
        {
            if (route.Action != ReignGovernmentResolutionAction.TreasuryPayment
                && route.Action != ReignGovernmentResolutionAction.CompensateClan
                && route.Action != ReignGovernmentResolutionAction.HoldFeast) continue;
            Assert.Equal(ReignGovernmentResolutionUnit.Gold, route.Unit);
            Assert.Equal(expectedGold, route.Target);
        }
    }

    [Theory]
    [InlineData("R008", 0, 60000)]
    [InlineData("R094", 1, 25000)]
    public void ActualReliefAndMemorialProposalsRequireTheirFullFundedBudget(
        string templateId, int routeIndex, int expectedGold)
    {
        var template = ReignGovernmentResolutionCatalog.Find(templateId)!;
        var route = routeIndex == 0 ? template.FirstRoute : template.SecondRoute;
        Assert.Equal(ReignGovernmentResolutionAction.HoldFeast, route.Action);
        Assert.Equal(ReignGovernmentResolutionUnit.Gold, route.Unit);
        Assert.Equal(expectedGold, route.Target);
    }

    [Fact]
    public void DiplomaticActionsRemainActionsAndExistingResolutionRewardsAreUnchanged()
    {
        var peace = ReignGovernmentResolutionCatalog.Templates
            .SelectMany(x => new[] { x.FirstRoute, x.SecondRoute })
            .First(x => x.Action == ReignGovernmentResolutionAction.MakePeace);
        Assert.Equal(ReignGovernmentResolutionUnit.Actions, peace.Unit);
        Assert.Equal(1, peace.Target);
        var reward = ReignGovernmentResolutionCatalog.CompletionReward(ReignGovernmentResolutionScale.Standard);
        Assert.Equal(2, reward.Loyalty);
        Assert.Equal(125, reward.Prosperity);
        Assert.Equal(10, reward.HearthOrSecurity);
    }
}
