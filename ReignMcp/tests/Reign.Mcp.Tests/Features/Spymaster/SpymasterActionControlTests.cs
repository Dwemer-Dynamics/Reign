using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class SpymasterActionControlTests
{
    private const string ControlConfirmation = "control Reign Spymaster action test on disposable save";
    private const string IrreversibleConfirmation = "run irreversible Reign Spymaster test on disposable save";

    [Fact] public async Task DestructiveRouteRequiresBothConfirmationsBeforeApiOrEnrollment()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.ControlSpymasterDestructiveActionTest(
            null!, TestOptions.Create(), null!, "campaign", "run", "fixture", "prepare",
            confirmation: ControlConfirmation, irreversibleConfirmation: IrreversibleConfirmation));
        foreach (var confirmations in new[] { ("", IrreversibleConfirmation), (ControlConfirmation, ""),
            (ControlConfirmation, IrreversibleConfirmation + "."), ("wrong", IrreversibleConfirmation) })
            await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.ControlSpymasterDestructiveActionTest(
                null!, TestOptions.Create() with { AllowVerificationControl = true }, null!, "campaign", "run", "fixture", "prepare",
                confirmation: confirmations.Item1, irreversibleConfirmation: confirmations.Item2));
    }

    [Fact] public void DestructiveRequestSeparatesItsAuthorityAndCannotChooseMissionIdsOrRolls()
    {
        var auth = new CampaignTestAuthorization("owned", "campaign", "timeline", "BaseThree", "prefix", "prefix_Current");
        Assert.Throws<InvalidOperationException>(() => TestingTools.BuildSpymasterDestructiveActionControlRequest(
            "fixture", "control", "batch", ["signed"], auth, ControlConfirmation, ""));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(TestingTools.BuildSpymasterDestructiveActionControlRequest(
            "fixture", "control", "batch", ["signed"], auth, ControlConfirmation, IrreversibleConfirmation)));
        var root = doc.RootElement; var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.Equal(root.GetProperty("enrollment").GetRawText(), step.GetProperty("enrollment").GetRawText());
        Assert.Equal("reign_mcp_spymaster_destructive_action_control", step.GetProperty("enrollment").GetProperty("source").GetString());
        Assert.Equal("prefix_Current", step.GetProperty("enrollment").GetProperty("disposableSaveName").GetString());
        Assert.Equal("BaseThree", step.GetProperty("enrollment").GetProperty("protectedBaselineSaveName").GetString());
        Assert.Equal("destructive_action_control_fixture", step.GetProperty("fixtureRunId").GetString());
        Assert.Equal("destructive_action_batch", step.GetProperty("batch").GetString());
        Assert.True(step.GetProperty("destructiveActionControl").GetBoolean());
        Assert.Equal(ControlConfirmation, step.GetProperty("controlConfirmation").GetString());
        Assert.Equal(IrreversibleConfirmation, step.GetProperty("irreversibleConfirmation").GetString());
        Assert.Equal("signed", Assert.Single(step.GetProperty("agreementIds").EnumerateArray()).GetString());
        foreach (string forbidden in new[] { "missionIds", "unitRoll", "consequenceUnitRoll", "structuredActionDraft", "allowIrreversible" })
            Assert.False(step.TryGetProperty(forbidden, out _));
    }

    [Fact] public void NativeDestructiveAuthorityRemainsSeparateAndRequiresActualDeaths()
    {
        string workspace = TestOptions.FindWorkspace();
        string host = File.ReadAllText(Path.Combine(workspace, "ReignBeta/src/Modules/Spymaster/Campaign/ReignLiveInteractionSpymasterHost.cs"));
        string native = File.ReadAllText(Path.Combine(workspace, "ReignBeta/src/Modules/Spymaster/Court/ReignSpymasterOrganicInGameTests.cs"));
        Assert.Contains("destructive && (command.Value<string>(\"irreversibleConfirmation\")", host);
        Assert.Contains("expected != ActiveSaveName()", host);
        Assert.Contains("CampaignTimeControlMode.Stop", host);
        Assert.Contains("StartsWith(\"spymaster.assassinate_\", StringComparison.Ordinal) != destructive", native);
        Assert.Contains("accepted_assassination_native_deaths", native);
        Assert.Contains("target?.IsDead == true", native);
        Assert.Contains("target == Hero.MainHero || target == ActiveSpymaster", native);
        Assert.Contains("FindSettlement(mission.TargetStringId)?.Town?.Governor", native);
        Assert.Contains("before[\"acceptedAssassinationTargets\"] = targets", native);
        Assert.Contains("CapturedAssassinationHero(batch, m.MissionId)", native);
        Assert.Contains("target != capturedTarget", native);
        Assert.Contains("journal.OperatorAuthorized", native);
        Assert.Contains("journal.CommittedHash != agreement.TermsHash()", native);
    }

    [Fact] public void DestructiveCatalogRequiresRecoveryAndIndependentNativeDeathEvidence()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var row = doc.RootElement.GetProperty("spymasterDestructiveActionControl");
        Assert.Equal("reign_control_spymaster_destructive_action_test", row.GetProperty("entry").GetString());
        Assert.Contains("irreversible", row.GetProperty("gates").GetString());
        Assert.Contains("native deaths", row.GetProperty("evidence").GetString());
        Assert.Contains("Restore", row.GetProperty("cleanup").GetString());
    }

    [Fact] public async Task GatesPrecedeApiAndEnrollmentAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.ControlSpymasterActionTest(
            null!, TestOptions.Create(), null!, "campaign", "run", "fixture", "prepare",
            confirmation: "control Reign Spymaster action test on disposable save"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => TestingTools.ControlSpymasterActionTest(
            null!, TestOptions.Create() with { AllowVerificationControl = true }, null!,
            "campaign", "run", "fixture", "prepare"));
    }

    [Theory]
    [InlineData("capture", "")]
    [InlineData("control", "a,a")]
    [InlineData("verify", "a,b,c,d")]
    [InlineData("capture", "a,,b")]
    [InlineData("capture", "a/b")]
    [InlineData("prepare", "a")]
    [InlineData("observe", "a")]
    [InlineData("assassinate", "a")]
    public void RejectsAmbiguousOrUnboundedAuthority(string phase, string ids) =>
        Assert.Throws<ArgumentException>(() => TestingTools.ParseSpymasterActionControlIds(phase, ids));

    [Fact] public void RequestCarriesExactEnrollmentAndCannotSubmitOrSynthesizeActions()
    {
        var auth = new CampaignTestAuthorization("owned", "campaign", "timeline", "BaseThree", "prefix", "prefix_Current");
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(TestingTools.BuildSpymasterActionControlRequest(
            "fixture", "control", "batch", ["agreement1", "agreement2"], auth)));
        var root = doc.RootElement;
        var step = Assert.Single(root.GetProperty("steps").EnumerateArray());
        Assert.Equal(root.GetProperty("enrollment").GetRawText(), step.GetProperty("enrollment").GetRawText());
        Assert.Equal("BaseThree", step.GetProperty("enrollment").GetProperty("protectedBaselineSaveName").GetString());
        Assert.Equal("prefix_Current", step.GetProperty("enrollment").GetProperty("disposableSaveName").GetString());
        Assert.Equal("timeline", step.GetProperty("enrollment").GetProperty("timelineId").GetString());
        Assert.Equal("owned", step.GetProperty("enrollment").GetProperty("campaignTestRunId").GetString());
        Assert.Equal("spymaster_organic", step.GetProperty("operation").GetString());
        Assert.Equal("action_control_fixture", step.GetProperty("fixtureRunId").GetString());
        Assert.True(step.GetProperty("actionControl").GetBoolean());
        Assert.False(step.TryGetProperty("missionIds", out _));
        Assert.False(step.TryGetProperty("unitRoll", out _));
        Assert.False(step.TryGetProperty("structuredActionDraft", out _));
    }

    [Fact] public void CatalogRequiresIndependentNativeProofAndExistingDestructiveRoute()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var row = doc.RootElement.GetProperty("spymasterActionControl");
        Assert.Equal("reign_control_spymaster_action_test", row.GetProperty("entry").GetString());
        Assert.Contains("independent", row.GetProperty("evidence").GetString());
        Assert.Contains("assassination", row.GetProperty("exclusions").GetString());
    }
}
