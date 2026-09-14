using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class NarrativeToolsTests
{
    [Fact]
    public async Task AuthoringRequiresTrustedGatesBeforeArtifactOrProviderAccess()
    {
        var options = TestOptions.Create();
        await Assert.ThrowsAsync<InvalidOperationException>(() => NarrativeTools.Author(options, null!, "missing", "narrative-test"));
        var enabled = options with { AllowVerificationControl = true, AllowOfflineVerification = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => NarrativeTools.Author(enabled, null!, "missing", "narrative-test", confirmation: "wrong"));
        await Assert.ThrowsAsync<ArgumentException>(() => NarrativeTools.Author(enabled, null!, "missing", "../escape",
            confirmation: "author character narratives using configured provider"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => NarrativeTools.Author(enabled, null!, "missing", "valid", count: 26,
            confirmation: "author character narratives using configured provider"));
        await Assert.ThrowsAsync<ArgumentException>(() => NarrativeTools.Author(enabled, null!, "missing", "valid", operation: "deploy",
            confirmation: "author character narratives using configured provider"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => NarrativeTools.Author(enabled, null!, "missing", "valid", operation: "evaluate",
            confirmation: "unapproved"));
    }

    [Fact]
    public void CatalogAndDocumentationDeclareAuthoringCostAndIsolation()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var entry = catalog.RootElement.GetProperty("characterNarrative");
        Assert.Equal("character_narrative", entry.GetProperty("suite").GetString());
        Assert.Equal("reign_author_character_narratives", entry.GetProperty("authoringTool").GetString());
        Assert.Contains("ReignValidation", entry.GetProperty("isolation").GetString());
        Assert.Equal(3, entry.GetProperty("authoringConcurrency").GetInt32());
        Assert.Equal("single_call_v1", entry.GetProperty("constructionMethod").GetString());
        var dreamCompatibility = entry.GetProperty("dreamCompatibility");
        Assert.Equal("reign-narrative-dream-compatibility-v1", dreamCompatibility.GetProperty("schema").GetString());
        Assert.Equal("incompatibleDreamCount", dreamCompatibility.GetProperty("evidenceField").GetString());
        Assert.Equal(0, dreamCompatibility.GetProperty("maximumIncompatibleDreams").GetInt32());
        Assert.True(File.Exists(Path.Combine(root, dreamCompatibility.GetProperty("policyPath").GetString()!)));
        var concernCompatibility = entry.GetProperty("concernCompatibility");
        Assert.Equal("reign-narrative-concern-compatibility-v1", concernCompatibility.GetProperty("schema").GetString());
        Assert.Equal("incompatibleConcernCount", concernCompatibility.GetProperty("evidenceField").GetString());
        Assert.Equal(0, concernCompatibility.GetProperty("maximumIncompatibleConcerns").GetInt32());
        Assert.True(File.Exists(Path.Combine(root, concernCompatibility.GetProperty("policyPath").GetString()!)));
        var factCompatibility = entry.GetProperty("factCompatibility");
        Assert.Equal("reign-narrative-fact-compatibility-v1", factCompatibility.GetProperty("schema").GetString());
        Assert.Equal("incompatibleFactCount", factCompatibility.GetProperty("evidenceField").GetString());
        Assert.Equal(0, factCompatibility.GetProperty("maximumIncompatibleFacts").GetInt32());
        Assert.True(File.Exists(Path.Combine(root, factCompatibility.GetProperty("policyPath").GetString()!)));
        Assert.Equal(1, entry.GetProperty("constructionNormalCalls").GetInt32());
        Assert.Equal(3, entry.GetProperty("constructionMaxAttempts").GetInt32());
        Assert.Contains("providerCalls", entry.GetProperty("constructionEvidence").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("implementation task", entry.GetProperty("initialRoster").GetString());
        Assert.Contains("evaluate", entry.GetProperty("operations").EnumerateArray().Select(x => x.GetString()));
        var quality = entry.GetProperty("qualityAudit");
        Assert.Equal("reign-narrative-quality-v1", quality.GetProperty("schema").GetString());
        Assert.Equal(1, quality.GetProperty("proseNormalizationVersion").GetInt32());
        Assert.Contains("fieldDiversity", quality.GetProperty("fields").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("0.85", quality.GetProperty("lifeSimilarityMethod").GetString());
        Assert.Contains("roster-audit.json", entry.GetProperty("evidence").GetString());
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
        {
            string text = TestingDocumentation.Read(Path.Combine(root, file));
            Assert.Contains("author character narratives using configured provider", text);
            Assert.Contains("reign_inspect_character_narrative", text);
            Assert.Contains("non-listening", text);
        }
    }
}
