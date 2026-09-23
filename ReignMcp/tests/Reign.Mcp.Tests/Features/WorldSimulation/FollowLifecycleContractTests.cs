namespace Reign.Mcp.Tests;

public sealed class FollowLifecycleContractTests
{
    private static string Read(string file, string suffix)
    {
        string root = TestOptions.FindWorkspace();
        return File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"), file, suffix));
    }

    [Fact]
    public void OneFollowActionUsesThePersistentAgreementForBothLegacyTypes()
    {
        string executor = Read("ReignRegularActionExecutor.cs", "/src/Modules/WorldSimulation/World/");
        Assert.Equal(2, executor.Split("ReignFollowCampaignBehavior.Instance.Start(action, actor, target, sceneActions)").Length - 1);
        Assert.DoesNotContain("TryApplyConcurrentMapFollow", executor, StringComparison.Ordinal);
    }

    [Fact]
    public void AgreementPersistsAndProjectsToMapSettlementAndMissionRepresentations()
    {
        string behavior = Read("ReignFollowCampaignBehavior.cs", "/src/Modules/WorldSimulation/World/");
        Assert.Contains("_reign_follow_agreements_v1", behavior, StringComparison.Ordinal);
        Assert.Contains("CreateCustomPartyWithTroopRoster", behavior, StringComparison.Ordinal);
        Assert.Contains("GetActionForEscortingParty", behavior, StringComparison.Ordinal);
        Assert.Contains("AddAccompanyingCharacter(character, true)", behavior, StringComparison.Ordinal);
        Assert.Contains("ActiveSceneFollowers", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void StopRemovesEachOwnedRepresentationAndReturnsManagedFollowerHome()
    {
        string behavior = Read("ReignFollowCampaignBehavior.cs", "/src/Modules/WorldSimulation/World/");
        Assert.Contains("RemoveAccompanyingCharacter(actor)", behavior, StringComparison.Ordinal);
        Assert.Contains("DestroyPartyAction.Apply(null, party)", behavior, StringComparison.Ordinal);
        Assert.Contains("EnterSettlementAction.ApplyForCharacterOnly(actor, destination)", behavior, StringComparison.Ordinal);
        Assert.Contains("party.MapEvent != null || party.SiegeEvent != null", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void MissionRebindUsesStableHeroesAndTheOwnedNativeFollowBehavior()
    {
        string mission = Read("ReignSceneActionMissionBehavior.cs", "/src/Modules/WorldSimulation/World/");
        Assert.Contains("EnsurePersistentFollowers();", mission, StringComparison.Ordinal);
        Assert.Contains("persistent_follow_", mission, StringComparison.Ordinal);
        Assert.Contains("AcceptedFollowBehavior", mission, StringComparison.Ordinal);
        Assert.Contains("ReignFollowCampaignBehavior.RuntimeSnapshot()", mission, StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignBehaviorIsRegisteredAndContinuouslyReconciled()
    {
        string module = Read("SubModule.cs", "/src/Modules/Platform/");
        Assert.Contains("campaignStarter.AddBehavior(new ReignFollowCampaignBehavior())", module, StringComparison.Ordinal);
        Assert.Contains("persistent follow representation", module, StringComparison.Ordinal);
    }
}
