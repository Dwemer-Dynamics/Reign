using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class ReignProjectCatalogTests
{
    [Fact]
    public void CurrentWorkspaceCatalogAndModuleCoverageAreComplete()
    {
        var plan = Catalog().CreatePlan("all", "Release", restore: false);

        Assert.Equal("ready", plan.Status);
        Assert.True(plan.CoverageComplete,
            string.Join(Environment.NewLine, plan.CoverageAudit.UnmappedPaths
                .Concat(plan.CoverageAudit.AmbiguousPaths)));
        Assert.Equal(4, plan.SelectedTier);
        Assert.Empty(plan.UnmanagedProjects);
        Assert.True(plan.CoverageAudit.InspectedPaths > 0);
        Assert.Empty(plan.CoverageAudit.UnmappedPaths);
        Assert.Empty(plan.CoverageAudit.AmbiguousPaths);
    }

    [Fact]
    public void ProductProfileExcludesIndependentSideToolProjects()
    {
        var plan = Catalog().CreatePlan("product", "Release", restore: false);

        Assert.Equal("ready", plan.Status);
        Assert.Equal(4, plan.SelectedTier);
        Assert.Equal("product", plan.Scope);
        Assert.Equal("quick", plan.VerificationTier);
        Assert.Equal(9, plan.Projects.Count);
        Assert.DoesNotContain(plan.Projects, item => item.Group == "tooling");
        Assert.Contains(plan.Projects, item => item.Id == "client");
        Assert.Contains(plan.Projects, item => item.Id == "mcp-tests");
    }

    [Fact]
    public void TestingCatalogChangeUsesProductBoundaryInsteadOfEcosystemTierFour()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false, "reign.testing.json");

        Assert.Equal("ready", plan.Status);
        Assert.Equal(3, plan.SelectedTier);
        Assert.Equal("module", plan.Scope);
        Assert.Equal("quick", plan.VerificationTier);
        Assert.Equal(new[] { "client", "live-test", "mcp", "mcp-tests", "server", "verification" },
            plan.Projects.Select(item => item.Id).Order().ToArray());
        Assert.DoesNotContain(plan.Projects, item => item.Group == "tooling");
    }

    [Fact]
    public void AgentPolicyChangeIsNoBuildDocumentation()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false, "AGENTS.md");

        Assert.True(plan.NonCodeOnly);
        Assert.Equal(1, plan.SelectedTier);
        Assert.Empty(plan.Projects);
    }

    [Theory]
    [InlineData(".gitignore")]
    [InlineData(".gitattributes")]
    [InlineData("reign.repository.json")]
    public void RepositoryPolicyFilesSelectEcosystemTierFour(string path)
    {
        var plan = Catalog().CreatePlan("changed", "Release", false, path);

        Assert.Equal("ready", plan.Status);
        Assert.Equal(4, plan.SelectedTier);
        Assert.Equal("ecosystem", plan.Scope);
        Assert.Equal("offline", plan.VerificationTier);
    }

    [Fact]
    public void DeletedLegacyReadinessTestRemainsClassified()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignMcp/tests/Reign.Mcp.Tests/ReignCampaignReadinessStateTests.cs");

        Assert.Equal("ready", plan.Status);
        Assert.True(plan.CoverageComplete);
        Assert.Equal("platform", Assert.Single(plan.PathResolutions).ModuleId);
    }

    [Fact]
    public void ToolProjectFileRoutesToItsBoundaryWithoutSelectingEveryToolFamily()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignServer/tools/portrait-generator/tests/NativeCharacterImageGenerator.Tests/NativeCharacterImageGenerator.Tests.csproj");

        Assert.Equal(3, plan.SelectedTier);
        Assert.Contains(plan.Projects, item => item.Id.Contains("nativecharacterimagegenerator", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Projects, item => item.Id.StartsWith("bannerlordeditor", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Projects, item => item.Id == "legacy-sqlite-importer");
    }

    [Theory]
    [InlineData("ReignServer/shared/Reign.Core.Contracts/ReignRelationshipBaselinePolicy.cs", 1, "core", "domain", "core-contracts")]
    [InlineData("ReignServer/src/Modules/Persistence/DatabaseParameterExtensions.cs", 2, "persistence", "server", "server")]
    [InlineData("ReignBeta/src/Modules/Persistence/Campaign/ReignSaveSyncCampaignBehavior.cs", 2, "persistence", "bannerlord", "client")]
    [InlineData("ReignServer/src/Modules/Characters/CharacterTraits.cs", 2, "characters", "server", "server")]
    [InlineData("ReignBeta/src/Modules/Characters/Family/ReignConceptionRecord.cs", 2, "characters", "bannerlord", "client")]
    [InlineData("ReignServer/shared/Reign.Relationships/RelationshipCompatibilityPolicy.cs", 1, "relationships", "domain", "relationships")]
    [InlineData("ReignServer/src/Modules/Relationships/Relationships.cs", 2, "relationships", "server", "server")]
    [InlineData("ReignBeta/src/Modules/Relationships/Campaign/ReignRelationshipCampaignBehavior.cs", 2, "relationships", "bannerlord", "client")]
    [InlineData("ReignServer/src/Modules/Reputation/SocialReputation.cs", 2, "reputation", "server", "server")]
    [InlineData("ReignServer/src/Modules/Dialogue/MotiveAwareConversation.cs", 2, "dialogue", "server", "server")]
    [InlineData("ReignBeta/src/Modules/Dialogue/Runtime/ReignConversationEligibility.cs", 2, "dialogue", "bannerlord", "client")]
    [InlineData("ReignServer/tests/ReignLiveTest/Features/Dialogue/QualificationRunner.cs", 2, "dialogue", "harness", "live-test")]
    [InlineData("ReignServer/src/Modules/Diplomacy/WorldDiplomacyDirector.cs", 2, "diplomacy", "server", "server")]
    [InlineData("ReignServer/src/Modules/Court/CourtSystem.cs", 2, "court", "server", "server")]
    [InlineData("ReignServer/src/Modules/Spymaster/SpymasterSystem.cs", 2, "spymaster", "server", "server")]
    [InlineData("ReignBeta/src/Modules/Spymaster/Court/ReignSpymasterCampaignBehavior.cs", 2, "spymaster", "bannerlord", "client")]
    [InlineData("ReignMcp/tests/Reign.Mcp.Tests/Features/Spymaster/SpymasterOrganicScenarioContractTests.cs", 2, "spymaster", "mcp", "mcp-tests")]
    [InlineData("ReignBeta/src/Modules/KingdomEvents/Campaign/ReignKingdomEventsCampaignBehavior.cs", 2, "kingdom-events", "bannerlord", "client")]
    [InlineData("ReignServer/src/Modules/Rebellion/RebellionDirector.cs", 2, "rebellion", "server", "server")]
    [InlineData("ReignServer/src/Modules/WorldSimulation/WorldTest.cs", 2, "world-simulation", "server", "server")]
    [InlineData("ReignServer/src/Modules/Portraits/PortraitGeneration.cs", 2, "portraits", "server", "server")]
    [InlineData("ReignBeta/src/Modules/UI/UI/ReignRuntimeSpriteSheets.cs", 2, "ui", "bannerlord", "client")]
    [InlineData("ReignMcp/src/Reign.Mcp.Server/Modules/Platform/ReignApiClient.cs", 2, "platform", "mcp", "mcp")]
    [InlineData("BannerlordEditorMcp/src/Bannerlord.EditorBridge/EditorBridgeController.cs", 2, "bannerlord-editor", "tooling", "bannerlordeditormcp-src-bannerlord-editorbridge-bannerlord-editorbridge-csproj")]
    public void EveryRepresentativeModuleFacetRoutesNarrowly(
        string path, int tier, string module, string facet, string project)
    {
        Assert.True(File.Exists(Path.Combine(TestOptions.Create().WorkspaceRoot,
            path.Replace('/', Path.DirectorySeparatorChar))), "Fixture path does not exist: " + path);

        var plan = Catalog().CreatePlan("changed", "Release", false, path);

        Assert.Equal("ready", plan.Status);
        Assert.True(plan.CoverageComplete);
        Assert.Equal(tier, plan.SelectedTier);
        Assert.Contains(plan.Projects, item => item.Id == project);
        var resolution = Assert.Single(plan.PathResolutions);
        Assert.Equal(module, resolution.ModuleId);
        Assert.Equal(facet, resolution.FacetId);
        Assert.NotNull(resolution.MatchedRule);
    }

    [Fact]
    public void SpymasterMcpChangeUsesFilteredTierTwoAndExcludesUnrelatedProjects()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignMcp/tests/Reign.Mcp.Tests/Features/Spymaster/SpymasterOrganicScenarioContractTests.cs");

        Assert.Equal(2, plan.SelectedTier);
        Assert.Equal(new[] { "mcp", "mcp-tests" }, plan.Projects.Select(item => item.Id).Order().ToArray());
        Assert.Contains(plan.VerificationOperations, operation =>
            operation.Kind == "test-filter" && operation.TestFilter == "FullyQualifiedName~Spymaster");
        Assert.DoesNotContain(plan.Projects, item => item.Id is "client" or "server");
    }

    [Fact]
    public void GovernmentBannerlordChangeSelectsEveryProjectRequiredByItsVerification()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentCampaignBehavior.cs");

        Assert.Equal(2, plan.SelectedTier);
        Assert.Contains(plan.Projects, item => item.Id == "client");
        Assert.Contains(plan.Projects, item => item.Id == "server");
        Assert.Contains(plan.VerificationOperations, operation =>
            operation.ModuleId == "government" && operation.ProjectId == "server");
        Assert.All(plan.VerificationOperations, operation =>
            Assert.Contains(plan.Projects, project => project.Id == operation.ProjectId));
    }

    [Fact]
    public void SpymasterContractChangeUsesOnlyBoundaryTierThree()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false, "ReignServer/shared/Reign.Shared.Source/Core/ReignSpymasterCore.cs");

        Assert.Equal(3, plan.SelectedTier);
        Assert.Contains(plan.Projects, item => item.Id == "client");
        Assert.Contains(plan.Projects, item => item.Id == "server");
        Assert.Contains(plan.Projects, item => item.Id == "mcp");
        Assert.DoesNotContain(plan.Projects, item => item.Id.StartsWith("nativecharacter", StringComparison.Ordinal));
        Assert.DoesNotContain(plan.Projects, item => item.Id.StartsWith("bannerlordeditor", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleModuleChangeEscalatesToTierThree()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignServer/src/Modules/Relationships/Relationships.cs;ReignServer/src/Modules/Reputation/SocialReputation.cs");

        Assert.Equal(3, plan.SelectedTier);
        Assert.Contains(plan.SelectionReasons, value => value.Contains("boundary", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DocumentationOnlyChangeSucceedsWithoutBuilds()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignServer/docs/server/SpymasterTesting.md;REIGN_ROADMAP.md");

        Assert.Equal("ready", plan.Status);
        Assert.Equal(1, plan.SelectedTier);
        Assert.True(plan.NonCodeOnly);
        Assert.Empty(plan.Projects);
        Assert.All(plan.PathResolutions, item => Assert.Equal("non-code", item.Status));
    }

    [Fact]
    public void EmptyChangedPathsFailImmediately()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            Catalog().CreatePlan("changed", "Release", false, ""));
        Assert.Contains("requires at least one", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownPathBlocksWithoutSelectingTierOrProjects()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false,
            "ReignServer/src/Modules/Future/FutureSystem.cs");

        Assert.Equal("blocked", plan.Status);
        Assert.Null(plan.SelectedTier);
        Assert.Empty(plan.Projects);
        Assert.Equal("unmapped", Assert.Single(plan.PathResolutions).Status);
    }

    [Fact]
    public void ExplicitInfrastructureChangeSelectsTierFour()
    {
        var plan = Catalog().CreatePlan("changed", "Release", false, "reign.modules.json");
        Assert.Equal("ready", plan.Status);
        Assert.Equal(4, plan.SelectedTier);
        Assert.Equal("ecosystem", plan.Scope);
        Assert.Equal("offline", plan.VerificationTier);
        Assert.Equal(20, plan.Projects.Count);
    }

    [Fact]
    public void FutureManagedProjectsAreAutomaticAndUnmanagedProjectsFailClosed()
    {
        var root = Directory.CreateTempSubdirectory("reign-catalog-test-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Active", "src", "Future"));
            Directory.CreateDirectory(Path.Combine(root.FullName, "Surprise"));
            File.WriteAllText(Path.Combine(root.FullName, "Active", "src", "Future", "Future.Tests.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "Surprise", "Unknown.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root.FullName, "reign-projects.json"), JsonSerializer.Serialize(new
            {
                schema = "reign-project-catalog-v1",
                managedRoots = new[] { new { path = "Active", group = "core" } },
                ignoredRoots = Array.Empty<object>(), excludedDirectoryNames = new[] { "bin", "obj" },
                projectOverrides = new Dictionary<string, object>()
            }));
            File.WriteAllText(Path.Combine(root.FullName, "reign.modules.json"), JsonSerializer.Serialize(new
            {
                schema = "reign-module-manifest-v2", enforcement = "enforce",
                tier4Paths = new[] { "**/*.csproj" }, nonCodePaths = Array.Empty<object>(), modules = Array.Empty<object>()
            }));

            var plan = new ReignProjectCatalog(TestOptions.Create(root.FullName))
                .CreatePlan("all", "Release", false);

            Assert.False(plan.CoverageComplete);
            Assert.Single(plan.UnmanagedProjects);
            Assert.Equal("Surprise/Unknown.csproj", plan.UnmanagedProjects[0].ProjectPath);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ReignProjectCatalog Catalog() => new(TestOptions.Create());
}
