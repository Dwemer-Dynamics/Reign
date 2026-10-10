using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class StructuredActionToolsTests
{
    [Theory]
    [InlineData("0.05")]
    [InlineData("0.75")]
    public void ShipFixturePreservesExactInputsAndBoundaries(string ratio)
    {
        var row = new { caseId = "damage", phase = "fixture", kind = "fixture", operationId = "damage_1",
            actionId = "fixture.ship_damage", arguments = new { shipId = "ship:42:hull", ownerPartyId = "party_1",
                expectedHitPoints = "123.5", remainingRatio = ratio } };
        var parsed = Assert.Single(StructuredActionTools.ParseCases(JsonSerializer.Serialize(new[] { row })));
        Assert.Equal("123.5", parsed.GetProperty("arguments").GetProperty("expectedHitPoints").GetString());
        Assert.Equal(ratio, parsed.GetProperty("arguments").GetProperty("remainingRatio").GetString());
    }
    [Theory]
    [InlineData("NaN", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("Infinity", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("0", "0.5", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.049", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.751", "fixture", "fixture.ship_damage")]
    [InlineData("100", "NaN", "fixture", "fixture.ship_damage")]
    [InlineData("100", "0.5", "run", "fixture.ship_damage")]
    [InlineData("100", "0.5", "fixture", "fixture.any_property")]
    public void InvalidShipFixturesCannotReachCampaignControl(string expected, string ratio, string phase, string actionId)
    {
        var row = new { caseId = "damage", phase, kind = "fixture", operationId = "damage_1", actionId,
            arguments = new { shipId = "ship:42:hull", ownerPartyId = "party_1", expectedHitPoints = expected, remainingRatio = ratio } };
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(JsonSerializer.Serialize(new[] { row })));
    }
    [Theory]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\",\"remainingRatio\":\"0.5\",\"property\":\"Gold\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":100,\"remainingRatio\":\"0.5\"}")]
    [InlineData("{\"shipId\":\"ship\",\"ownerPartyId\":\"party\",\"expectedHitPoints\":\"100\",\"expectedHitPoints\":\"100\"}")]
    public void ShipFixtureRefusesMissingDuplicateExtraOrUntypedArguments(string args)
        => Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(
            "[{\"caseId\":\"damage\",\"phase\":\"fixture\",\"kind\":\"fixture\",\"operationId\":\"op\",\"actionId\":\"fixture.ship_damage\",\"arguments\":" + args + "}]"));
    [Fact] public void NativeObservationContractNamesDistinctGoldAndClanHolders()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        string observations = doc.RootElement.GetProperty("nativeActionConsole").GetProperty("observations").GetString()!;
        Assert.Contains("PartyTradeGold", observations);
        Assert.Contains("settlement-component Gold", observations);
        Assert.Contains("Influence and Renown", observations);
        foreach (string state in new[] { "IsFugitive", "IsTraveling", "IsReleased", "DeathMark", "IsPregnant", "HeroState", "IsInRaftState", "MaxHitPoints", "IsWaitingForDisband" })
        {
            Assert.Contains(state, observations);
            Assert.Contains("\"" + state + "\"", File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
                "ReignBeta/src/Modules/WorldSimulation/Actions/ReignNativeActionConsole.cs")));
        }
    }
    [Fact] public void NativeRomanceAndTributeObservationsRetainScopeBoundsAndAuthority()
    {
        string root = TestOptions.FindWorkspace();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var row = doc.RootElement.GetProperty("nativeActionConsole").GetProperty("relationshipAndTributeObservations");
        const string schema = "reign-native-relationship-tribute-observations-v1";
        Assert.Equal(schema, row.GetProperty("schema").GetString());
        Assert.Equal(32, row.GetProperty("romanceRowLimit").GetInt32());
        Assert.Contains("both identified heroes", row.GetProperty("scope").GetString());
        Assert.Contains("Duplicate native records", row.GetProperty("scope").GetString());
        Assert.Contains("never inferred zero", row.GetProperty("tribute").GetString());
        Assert.Contains("no command", row.GetProperty("authority").GetString());
        foreach (string path in new[] { "docs/agent/testing/FEATURE_HARNESSES.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains(schema, File.ReadAllText(Path.Combine(root, path)));
    }
    [Fact] public async Task VerificationGatePrecedesAnyApiOrCampaignAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredActionTools.TestStructuredActions(null!, TestOptions.Create(), null!,
            "campaign", "run", "batch", "[]", "test structured actions on disposable save"));
    }
    [Fact] public async Task MissingConfirmationPrecedesAnyApiOrCampaignAccess()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => StructuredActionTools.TestStructuredActions(null!, TestOptions.Create() with { AllowVerificationControl = true }, null!,
            "campaign", "run", "batch", "[]"));
    }
    [Theory]
    [InlineData("[]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"send\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\",\"script\":\"no\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\"},{\"caseId\":\"a\",\"phase\":\"catalog\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"catalog\",\"phase\":\"run\"}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"run\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"unregistered\",\"arguments\":{}}]")]
    [InlineData("[{\"caseId\":\"a\",\"phase\":\"run\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"native.test\",\"arguments\":{\"operation\":\"override\"}}]")]
    public void InvalidCasesCannotReachCampaignControl(string json) => Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));

    [Fact] public void RetainsExactAgreementAndNativeValuesWithoutTextInference()
    {
        var cases = StructuredActionTools.ParseCases("[{\"caseId\":\"a\",\"phase\":\"preview\",\"kind\":\"native\",\"operationId\":\"op\",\"actionId\":\"native.test\",\"arguments\":{\"value\":\"100\"}},{\"caseId\":\"b\",\"phase\":\"run\",\"kind\":\"agreement\",\"operationId\":\"b\",\"agreement\":{\"Clauses\":[]}}]");
        Assert.Equal("100", cases[0].GetProperty("arguments").GetProperty("value").GetString());
        Assert.Equal(0, cases[1].GetProperty("agreement").GetProperty("Clauses").GetArrayLength());
    }
    [Fact] public void RefusesOversizedObservationAndBatch()
    {
        string json = JsonSerializer.Serialize(new[] { new { caseId = "a", phase = "catalog", heroIds = Enumerable.Range(0, 17).Select(i => "hero" + i) } });
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(json));
        Assert.Throws<ArgumentException>(() => StructuredActionTools.ParseCases(JsonSerializer.Serialize(Enumerable.Range(0, 9).Select(i => new { caseId = "a" + i, phase = "catalog" }))));
    }
    [Fact] public void CatalogDeclaresNoAutomaticSemanticPassOrImplicitConsent()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json")));
        var row = doc.RootElement.GetProperty("structuredActionAgreements");
        Assert.Equal("reign_test_structured_actions", row.GetProperty("entry").GetString());
        Assert.Contains("independent native effect", row.GetProperty("evidence").GetString());
        Assert.Contains("operator", row.GetProperty("isolation").GetString());
    }
}
