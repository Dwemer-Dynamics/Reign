namespace Reign.Mcp.Tests;

public sealed class ReignApiRouteContractTests
{
    [Fact]
    public void EveryWrappedRouteExistsInCurrentReignRouter()
    {
        var workspace = TestOptions.FindWorkspace();
        var router = string.Join(Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(workspace, "ReignBetaServer", "src"),
                    "Program*.cs",
                    SearchOption.AllDirectories)
                .Order(StringComparer.OrdinalIgnoreCase)
                .Select(File.ReadAllText));
        string[] exactRoutes =
        [
            "/health",
            "/api/diagnostics",
            "/api/provider/status",
            "/api/background/status",
            "/memory/background/status",
            "/api/telemetry/status",
            "/llm/prompt-cache/status",
            "/api/prompts",
            "/api/logs",
            "/api/campaigns/storage-audit",
            "/api/campaigns/cleanup-zero-save",
            "/audit/query",
            "/world-test/campaigns",
            "/world-test/overview",
            "/world-test/details",
            "/world-history/query",
            "/actions/catalog",
            "/actions/failures",
            "/character-editor/list",
            "/relationships/ambient/status",
            "/rebellions/query",
            "/verification/status",
            "/verification/results",
            "/tests/live/runtime",
            "/tests/live/run/status",
            "/tests/live/run/report",
            "/tests/live/readiness",
            "/tests/social-balance/status",
            "/court/spymaster/agents/status",
            "/court/ruler-petition/respond",
            "/verification/run",
            "/verification/cancel"
        ];

        Assert.All(exactRoutes, route =>
            Assert.True(
                router.Contains($"request.Path == \"{route}\"", StringComparison.Ordinal) ||
                router.Contains($"case \"{route}\":", StringComparison.Ordinal),
                $"The current router does not declare {route}."));
        Assert.Contains(
            "request.Path.StartsWith(\"/world-history/event/\"",
            router,
            StringComparison.Ordinal);
        Assert.Contains(
            "request.Path.StartsWith(\"/world-history/correlation/\"",
            router,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RulerPetitionDialogueAndCourtArtRetainTheirAuthorityBoundaries()
    {
        var workspace = TestOptions.FindWorkspace();
        string dialogue = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"), "RulerDocketDialogue.cs",
            "/src/Modules/Court/"));
        string art = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"), "ReignRulerPetitionSceneClient.cs",
            "/src/Modules/Court/"));
        string audience = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"), "ReignCourtPetitionScreenVM.cs",
            "/src/Modules/Court/"));
        string liveHost = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"), "ReignLiveInteractionRulerDocketHost.cs",
            "/src/Modules/Court/"));
        string assetManifest = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "artwork",
            "ui-modern-style-kit", "asset-manifest.json"));
        string generationProvenance = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "artwork",
            "ui-modern-style-kit", "generation-provenance.json"));

        Assert.Contains("phase != \"opening\" && phase != \"conversation\" && phase != \"closing\"", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("A ruler message is required for a petition conversation turn", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("Answer the ruler's latest words directly and naturally", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("player_text TEXT NOT NULL DEFAULT ''", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("Use only the supplied facts", dialogue, StringComparison.Ordinal);
        Assert.Contains("Do not narrate the ruler, invent actions, change terms", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("[\"nativeActions\"] = new ArrayList()", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("[\"relationshipAssessments\"] = new ArrayList()", dialogue,
            StringComparison.Ordinal);
        Assert.Contains("[\"reputationChanges\"] = new ArrayList()", dialogue,
            StringComparison.Ordinal);
        var artRequest = ReignBeta.Integration.ReignCourtAudienceScene.BuildRequest(
            new ReignBeta.Integration.CourtSceneSnapshot
            {
                CampaignId = "contract", TimelineId = "main", AudienceId = "petition", HostId = "host",
                CultureId = "empire", PublicContext = "The petition", Hall = new byte[] { 1 },
                People = new()
                {
                    new() { Id = "petitioner", Name = "Petitioner", Age = 35, ReferenceStamp = "p" },
                    new() { Id = "witness", Name = "Witness", Age = 40, ReferenceStamp = "w" }
                }
            }, new byte[] { 1 }, 2);
        string prompt = artRequest.Value<string>("prompt")!;
        foreach (string constraint in new[] { "seated viewpoint on an elevated dais", "looking slightly downward",
            "at the foot of the dais", "looking upward toward the camera", "entrance behind the courtiers",
            "No visible throne", "LEFT portrait grid", "RIGHT approved hall panel",
            "Do not rotate, reverse", "No table, desk, railing, lectern", "The ruler must never appear",
            "A visitor's culture must never change the hall", "Show exactly all 2",
            "top of the hair to the soles of both feet", "clear floor visible below and around the feet",
            "No person may stand in a near foreground", "consistent apparent scale",
            "complete, modest, culturally plausible formal court clothing", "No bare chest",
            "reference portrait is cropped or underdressed" })
            Assert.Contains(constraint, prompt, StringComparison.Ordinal);
        Assert.True(Reign.Core.Contracts.Court.ReignCourtAudienceArtRules.HasRequiredConstraints(prompt));
        Assert.Equal("court_audience_all_cast_host_hall_v6", artRequest.Value<string>("renderContract"));
        Assert.Single(artRequest.Properties(), property => property.Name.EndsWith("Base64", StringComparison.Ordinal));
        foreach (string culture in new[] { "empire", "vlandia", "sturgia", "battania", "aserai", "khuzait" })
            Assert.EndsWith("-" + culture + ".png", ReignBeta.Integration.ReignCourtAudienceScene.HallReference(culture));
        var cultureReferenceHashes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["empire"] = "62178f5c0ce9c12ef2f605f2bd158c8588c212baaf24101aac12719a5ad26c6e",
            ["vlandia"] = "72cc2326d0958086869a1cfcc13d8b805e60962ed935803c205c66611b4252bf",
            ["sturgia"] = "8f958365c8a661a0dc1a7114deb3984706cca5a8dc6a675f276e88842c8ea982",
            ["battania"] = "fc0729236c715201fab78426a730c18b8d0ee43d33664c4fb6b778c80f771ab2",
            ["aserai"] = "718eef440a36c697e1490ae70f0b4803a6f851ad29ab2cd990043ddd45d22057",
            ["khuzait"] = "b21634bc03104d660182f93a47ba53b96027d0e95a03534c144d92f3b198fc72"
        };
        foreach ((string cultureId, string expectedHash) in cultureReferenceHashes)
        {
            string fileName = "ruler-petition-throne-viewpoint-" + cultureId + ".png";
            string compositionReferencePath = Path.Combine(workspace, "ReignBeta", "GUI", "UiCalibration",
                "reference-scenes", fileName);
            Assert.True(File.Exists(compositionReferencePath),
                "The " + cultureId + " petition composition reference must ship with the module.");
            string compositionReferenceHash = Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(compositionReferencePath)))
                .ToLowerInvariant();
            Assert.Equal(expectedHash, compositionReferenceHash);
            Assert.Contains(expectedHash, assetManifest, StringComparison.Ordinal);
            Assert.Contains(fileName, assetManifest, StringComparison.Ordinal);
            Assert.Contains(expectedHash, generationProvenance, StringComparison.Ordinal);
            Assert.Contains("openai/gpt-image-2/text-to-image", generationProvenance,
                StringComparison.Ordinal);
        }
        Assert.Contains("PortraitCache.GetDiskBytes", art, StringComparison.Ordinal);
        Assert.DoesNotContain("GenerateSharedPortraitAsync", art, StringComparison.Ordinal);
        Assert.DoesNotContain("TryRequestPortrait(petitioner", audience, StringComparison.Ordinal);
        Assert.DoesNotContain("ArmPortraitCapture", audience, StringComparison.Ordinal);
        Assert.Contains("PortraitTextureProviderName", audience, StringComparison.Ordinal);
        Assert.Contains("ScenePreparationComplete", audience, StringComparison.Ordinal);
        Assert.Contains("AutomationScenePreparationComplete", liveHost, StringComparison.Ordinal);
        Assert.Contains("DateTime.UtcNow.AddSeconds(240)", liveHost, StringComparison.Ordinal);
        Assert.Contains("The ruler-docket throne-room art did not meet its native readiness gate", liveHost,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeAndLogRoutesKeepPollingResponsesBounded()
    {
        var workspace = TestOptions.FindWorkspace();
        string platform = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"), "Program.cs", "/src/Modules/Platform/"));
        string liveTests = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"), "LiveInteractionTest.cs", "/src/Modules/WorldSimulation/"));

        Assert.Contains("LiveTestRunSummary(run, false)", liveTests, StringComparison.Ordinal);
        Assert.Contains("WriteIdleLiveTestRunIndex", liveTests, StringComparison.Ordinal);
        Assert.Contains("ReadBool(indexed, \"scanComplete\", false)", liveTests, StringComparison.Ordinal);
        Assert.Contains("[\"status\"] = \"idle\"", liveTests, StringComparison.Ordinal);
        Assert.Contains("maxResponseCharacters = 256 * 1024", platform, StringComparison.Ordinal);
        Assert.Contains("Math.Min(500, limit)", platform, StringComparison.Ordinal);
        Assert.Contains("IndexOf(search, StringComparison.OrdinalIgnoreCase)", platform, StringComparison.Ordinal);
        Assert.Contains("[\"truncated\"] = rows.Count < candidates.Count", platform, StringComparison.Ordinal);
        Assert.Contains("server.client_disconnect", platform, StringComparison.Ordinal);
        Assert.Contains("[\"path\"] = request?.Path", platform, StringComparison.Ordinal);
        Assert.Contains("catch (DirectoryNotFoundException ex)", platform, StringComparison.Ordinal);
        Assert.Contains("[\"error\"] = ex.ToString()", platform, StringComparison.Ordinal);
        Assert.Contains("[\"ok\"] = false", platform, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCenterShutdownRunsWithoutAConfirmationPopup()
    {
        var workspace = TestOptions.FindWorkspace();
        string platform = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"), "Program.cs", "/src/Modules/Platform/"));

        Assert.Contains("async function shutdownServer()", platform, StringComparison.Ordinal);
        Assert.Contains("fetch('/api/shutdown'", platform, StringComparison.Ordinal);
        Assert.DoesNotContain("confirm('Shut down the Bannerlord Reign server?", platform,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NativeSaveDeletionCarriesLoadedStateAndUnloadBoundary()
    {
        var workspace = TestOptions.FindWorkspace();
        string coordinator = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "ReignSaveSyncCoordinator.cs",
            "/src/Modules/Persistence/Integration/"));
        string subModule = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBeta"),
            "SubModule.cs",
            "/src/Modules/Platform/"));
        string server = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"),
            "SaveSync.cs",
            "/src/Modules/Persistence/"));
        string campaignBackups = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignBetaServer"),
            "CampaignBackups.cs",
            "/src/Modules/Persistence/"));

        Assert.Contains("[\"campaignLoaded\"] = CurrentCampaignMatches(campaignId)",
            coordinator, StringComparison.Ordinal);
        Assert.Contains("/save-sync/campaign-unloaded", coordinator,
            StringComparison.Ordinal);
        Assert.Contains("ObserveCampaignLifecycle", subModule,
            StringComparison.Ordinal);
        Assert.Contains("remainingSavePoints", server, StringComparison.Ordinal);
        Assert.Contains("campaignDeleted", server, StringComparison.Ordinal);
        Assert.Contains("campaignDeletionPending", server, StringComparison.Ordinal);
        Assert.Contains("countsBySubsystem", server, StringComparison.Ordinal);
        Assert.Contains("FileAttributes.ReparsePoint", campaignBackups,
            StringComparison.Ordinal);
        Assert.Contains("Campaign deletion target escaped its owned storage root",
            campaignBackups, StringComparison.Ordinal);
        Assert.Contains("_shared", campaignBackups, StringComparison.Ordinal);
    }
}
