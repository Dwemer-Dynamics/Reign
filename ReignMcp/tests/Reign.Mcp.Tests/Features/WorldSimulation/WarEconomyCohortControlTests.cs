namespace Reign.Mcp.Tests;

public sealed class WarEconomyCohortControlTests
{
    [Theory]
    [InlineData("cohort_enable")]
    [InlineData("cohort_disable")]
    public async Task CohortControlCannotDispatchWithoutVerificationPermission(string source)
    {
        var options = TestOptions.Create() with { AllowVerificationControl = false };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Reign.Mcp.Server.TestingTools.StartWarEconomyTest(
            null!, options, null!, "campaign", "run", profile: "cohort_control", source: source,
            confirmation: "start Reign war economy test on disposable save"));
    }

    [Fact]
    public void SessionIsolationRemainsEphemeralAndHasIndependentNativeObservations()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignBeta/src/Modules/WorldSimulation/Economy/ReignWarEconomyCohortControl.cs"));
        Assert.DoesNotContain("SyncData(", source);
        Assert.DoesNotContain("ChangeHeroGold(", source);
        Assert.DoesNotContain("MakePeaceAction.Apply", source);
        Assert.Contains("k.IsAtWarWith(other)", source);
        Assert.Contains("a.IsAtWarWith(b)", source);
        Assert.Contains("Release(\"peace_matrix_violation\", true)", source);
        Assert.Contains("DateTime.UtcNow.AddHours(6)", source);
        Assert.Contains("now >= _start + 61", source);
        Assert.Contains("Samples.Count > 256", source);
        Assert.Contains("ProbeBlocks.Values.Sum() == hits + 1", source);
        Assert.Contains("JToken.DeepEquals(before, ProbeState())", source);
        Assert.Contains("GuardProbes.Count == 5", source);
        Assert.Contains("_harmony.Unpatch(target, HarmonyPatchType.All, Owner)", source);
    }
}
