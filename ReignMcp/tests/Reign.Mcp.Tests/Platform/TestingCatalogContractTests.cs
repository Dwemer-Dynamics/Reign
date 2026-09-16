using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using ModelContextProtocol.Server;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class TestingCatalogContractTests
{
    [Fact]
    public void ValidationCoordinationIsDiscoverableAndReadOnly()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var status = catalog.RootElement.GetProperty("validation").GetProperty("status");
        Assert.True(status.GetProperty("readOnly").GetBoolean());
        Assert.Equal("reign-validation-lane-v1", status.GetProperty("schema").GetString());
        string tool = status.GetProperty("tool").GetString()!;
        var attribute = typeof(TestingTools).GetMethod(nameof(TestingTools.GetValidationStatus))!
            .GetCustomAttribute<McpServerToolAttribute>()!;
        Assert.Equal(tool, attribute.Name);
        Assert.True(attribute.ReadOnly);
        Assert.False(attribute.Destructive);
        Assert.True(attribute.Idempotent);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "docs/agent/TASK_COORDINATION.md",
            "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains(tool, TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void TestingGuideIndexesEveryProcedureWithoutLoadingItIntoTheRoot()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string guide = File.ReadAllText(Path.Combine(workspace, "docs", "agent", "TESTING_TOOL_GUIDE.md"));
        var documents = catalog.RootElement.GetProperty("guideDocuments").EnumerateArray()
            .Select(item => item.GetString()!).ToArray();
        Assert.NotEmpty(documents);
        Assert.Equal(documents.Length, documents.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var actual = Directory.GetFiles(Path.Combine(workspace, "docs", "agent", "testing"), "*.md")
            .Select(path => Path.GetRelativePath(workspace, path).Replace('\\', '/')).Order().ToArray();
        Assert.Equal(actual, documents.Order().ToArray());
        foreach (string path in documents)
        {
            Assert.StartsWith("docs/agent/testing/", path);
            Assert.DoesNotContain("..", path);
            Assert.Contains("](" + path["docs/agent/".Length..] + ")", guide);
        }
        // The entry point remains a router; existing per-feature contracts below
        // still inspect all indexed procedure content.
        Assert.True(guide.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length < 1200);
    }

    [Fact]
    public void CodexPerformanceCatalogPreservesUsageGatesAndOfflineEvidence()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var performance = catalog.RootElement.GetProperty("codexPerformance");
        Assert.Equal("reign-codex-performance-report-v1", performance.GetProperty("schema").GetString());
        Assert.Equal("codex_subscription", performance.GetProperty("provider").GetString());
        Assert.Contains("turn/start", performance.GetProperty("budget").GetString());
        Assert.Contains("non-listening", performance.GetProperty("offlineTransportIsolation").GetString());
        Assert.Contains("run bounded Codex performance comparison", performance.GetProperty("liveRoute").GetString());
        Assert.Contains("verify one Codex model request", performance.GetProperty("modelVerification").GetString());
        foreach (var path in performance.GetProperty("browserContracts").EnumerateArray())
            Assert.True(File.Exists(Path.Combine(workspace, path.GetString()!)));
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("reign-codex-performance-report-v1", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Theory]
    [InlineData("", "live-llm")]
    [InlineData("{}", "live-llm")]
    [InlineData("{}", "all")]
    public async Task CodexPerformanceRejectsUnboundedOrUngatedRunsBeforeTransport(string plan, string tier)
    {
        var options = new ReignMcpOptions { WorkspaceRoot = TestOptions.FindWorkspace(), BuildRoot = Path.GetTempPath(),
            ServerBaseUri = new Uri("http://127.0.0.1:5101"), AllowVerificationControl = true };
        await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartVerification(null!, options, tier: tier, suite: "codex_performance",
            confirmation: "start Reign verification", codexPerformanceJson: plan));
    }

    [Fact]
    public void CourtAudienceArtworkCatalogKeepsOneCompositeAndHonestReadiness()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var art = catalog.RootElement.GetProperty("courtAudienceArtwork");
        Assert.Equal("reign-court-audience-art-v1", art.GetProperty("schema").GetString());
        Assert.Contains("Exactly one", art.GetProperty("requestContract").GetString());
        Assert.Contains("CourtAudienceSceneTests", art.GetProperty("verification").GetString());
        Assert.Contains("court_life_snapshot", art.GetProperty("evidence").GetString());
        Assert.Contains("disposable-save gates", art.GetProperty("safety").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("reign-court-audience-art-v1", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void ControlCenterNavigationCatalogKeepsBrowserProofIsolated()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var navigation = catalog.RootElement.GetProperty("controlCenterNavigation");
        Assert.Equal("reign-control-center-navigation-ui-v1", navigation.GetProperty("schema").GetString());
        Assert.Contains("interaction_architecture", navigation.GetProperty("verification").GetString());
        Assert.Contains("All browser traffic is intercepted", navigation.GetProperty("isolation").GetString());
        Assert.Contains("Retain retired server-route responses", navigation.GetProperty("legacyCleanup").GetString());
        Assert.True(File.Exists(Path.Combine(workspace, navigation.GetProperty("browserContract").GetString()!)));
        Assert.True(File.Exists(Path.Combine(workspace, navigation.GetProperty("stylePalette").GetString()!)));
        Assert.Contains("control_center_modern_style_contract", navigation.GetProperty("styleCoverage").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("reign-control-center-navigation-ui-v1", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void DialogueMarriageCatalogBindsExactConsentAndExistingIsolatedProof()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var marriage = catalog.RootElement.GetProperty("dialogueMarriage");
        Assert.Equal("reign-dialogue-marriage-v1", marriage.GetProperty("schema").GetString());
        Assert.Contains("personal_marriage_consent", marriage.GetProperty("authority").GetString());
        Assert.Contains("alreadyMarried", marriage.GetProperty("native").GetString());
        Assert.Contains("disposable-save", marriage.GetProperty("acceptance").GetString());
        Assert.Contains("contracts.dialogue_marriage", marriage.GetProperty("verification").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md",
            "ReignMcp/docs/security-model.md", "ReignServer/src/Modules/Platform/VerificationLab.cs" })
            Assert.Contains("contracts.dialogue_marriage", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void ExistingPatronageCatalogRequiresPreservationAndIsolation()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeExistingPatronage").GetString()!;
        foreach (string required in new[] { "reusedExistingMatter=true", "markerOnly=true", "already-bound", "ambiguous",
            "campaign/timeline/reign/kingdom/source/capital", "fixture gold/loyalty/captive", "disposable-save gates",
            "existing_patronage_binding_preserves_matter_and_native_state", "without replaying" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("existing_patronage_binding_preserves_matter_and_native_state", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void PatronageEligibilityDiagnosticsRemainBoundedAndReadOnlyInCatalog()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifePatronageEligibility").GetString()!;
        foreach (string required in new[] { "read-only", "same_template_pending", "public_historical_subject_required",
            "templateKnown=false", "five", "never create or bind", "disposable-campaign gates" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("patronageEligibility", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void CourtFingerprintCatalogPreservesNativeAgeAndDurableFamilyState()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFingerprintContract").GetString()!;
        foreach (string required in new[] { "familyNativeAges", "family_cached_age_refresh_does_not_change_fingerprint",
            "family_native_age_change_changes_fingerprint", "family_profile_value_change_changes_fingerprint", "never silently accepted" })
            Assert.Contains(required, contract);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("nativeState.familyNativeAges", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void CourtRulerIdentityCatalogRequiresNativeScopeAndRealOpeningProof()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeRulerIdentity").GetString()!;
        Assert.Contains("formal_court_audience", contract);
        Assert.Contains("contracts.sovereign_identity/pipeline.identity", contract);
        Assert.Contains("Native foreign visitor/ambassador opening", contract);
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("formal_court_audience", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Theory]
    [InlineData("court_life_preflight", "International")]
    [InlineData("court_life_prepare", "Family")]
    [InlineData("court_life_choose_confirm", "International")]
    public async Task CaptiveInputRejectsWrongPhaseOrSourceBeforeAnyCampaignCall(string profile, string source)
    {
        var options = new ReignMcpOptions { WorkspaceRoot = TestOptions.FindWorkspace(),
            BuildRoot = Path.GetTempPath(), ServerBaseUri = new Uri("http://127.0.0.1:5101"), AllowVerificationControl = true };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartRulerDocketTest(
            null!, options, null!, "campaign", "owned-run", profile: profile,
            confirmation: "start Reign ruler docket test on disposable save", courtLifeSource: source,
            courtLifeFixtureCaptiveHeroId: "exact_hero"));
        Assert.Contains("International court_life_prepare", error.Message);
    }

    [Fact]
    public void CaptiveFixtureCatalogMatchesOptionalInputAndRecoveryEvidence()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeCaptiveFixture");
        string input = contract.GetProperty("input").GetString()!;
        var parameter = typeof(TestingTools).GetMethod("StartRulerDocketTest")!.GetParameters().Single(p => p.Name == input);
        Assert.Equal(typeof(string), parameter.ParameterType);
        Assert.Equal("", parameter.DefaultValue);
        Assert.Equal("court_life_prepare", contract.GetProperty("profile").GetString());
        Assert.Equal("International", contract.GetProperty("source").GetString());
        Assert.Contains("Partial failure", contract.GetProperty("recovery").GetString());
        Assert.Contains("warStateKnown", contract.GetProperty("observation").GetString());
        Assert.Contains("exclusionCounts", contract.GetProperty("preflight").GetString());
        Assert.Contains("unrelated pending matters", contract.GetProperty("preflight").GetString());
        Assert.Contains(input, TestingTools.GetRulerDocketTestManifest()["courtLifeCaptiveFixture"].ToString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains(input, TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void PoliticalPressureObservationCatalogPreservesDirectionAndReadOnlyLimits()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var entry = catalog.RootElement.GetProperty("politicalPressureObservation");
        Assert.Equal("reign_get_world_details", entry.GetProperty("tool").GetString());
        Assert.Equal("political pressures", entry.GetProperty("subsystem").GetString());
        Assert.Contains("actorKingdomId|targetKingdomId", entry.GetProperty("pair").GetString());
        Assert.Contains("1000", entry.GetProperty("evidence").GetString());
        Assert.Contains("incident", entry.GetProperty("evidence").GetString());
        Assert.Contains("activity orientation", entry.GetProperty("pair").GetString());
        Assert.Contains("Read-only", entry.GetProperty("safety").GetString());
        foreach (string path in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/tool-catalog.md", "ReignMcp/docs/security-model.md" })
            Assert.Contains("political pressures", TestingDocumentation.Read(Path.Combine(workspace, path)));
    }

    [Fact]
    public void EncounteredResidentsCatalogPreservesEquippedOutfitAndDisposableNativeBoundary()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var contract = catalog.RootElement.GetProperty("encounteredResidents");
        Assert.Equal("resident_current_native_equipment", contract.GetProperty("clothingPolicy").GetString());
        Assert.Contains("encountered_residents", contract.GetProperty("verification").GetString());
        Assert.Contains("disposable", contract.GetProperty("safety").GetString(), StringComparison.OrdinalIgnoreCase);
        string client = File.ReadAllText(Path.Combine(workspace, "ReignBeta/src/Modules/Characters/Campaign/ReignEncounteredResidentsCampaignBehavior.cs"));
        Assert.Contains("_reign_encountered_residents_v1", client);
        Assert.Contains("RestoreNativePopulation", client);
        Assert.Equal("reign-encountered-resident-runtime-v1", contract.GetProperty("runtimeEvidence").GetProperty("schema").GetString());
        Assert.Contains("reign-encountered-resident-runtime-v1", client);
        Assert.Contains("Take(64)", client);
        const string outfitFrame = "ai_source_resident_full_outfit_v1";
        Assert.Contains(outfitFrame, contract.GetProperty("nativePortraitFraming").GetString());
        Assert.Contains(outfitFrame, File.ReadAllText(Path.Combine(workspace, "ReignServer/tools/portrait-generator/src/NativeCharacterImageGenerator/AiSourceCacheCatalog.cs")));
        Assert.Contains(outfitFrame, File.ReadAllText(Path.Combine(workspace, "ReignServer/src/Modules/Portraits/NativePortraitSourceGeneration.cs")));
    }
    [Fact]
    public void PortraitClothingEditCatalogPreservesFourProfilesAndRecoveryEvidence()
    {
        string workspace = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        var contract = catalog.RootElement.GetProperty("portraitClothingEdit");
        Assert.Equal("reign-portrait-clothing-edit-v1", contract.GetProperty("schema").GetString());
        Assert.Equal(new[] { "portrait", "adultPortrait", "scenery", "adultScenery" },
            contract.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal("bytedance/seedream-v5.0-pro/edit", contract.GetProperty("adultDefaults").GetProperty("model").GetString());
        Assert.Contains("generationStages", contract.GetProperty("evidence").GetString());
        Assert.Contains("warning", contract.GetProperty("evidence").GetString());
        Assert.Contains("ageYears >= 18", contract.GetProperty("eligibility").GetString());
        Assert.Contains("--run-portrait-provider-adapter-tests", contract.GetProperty("verification").GetString());
    }

    [Fact]
    public void GauntletXmlPreviewAuditCoversEveryPrefabAndRuntimeBinding()
    {
        string workspace = TestOptions.FindWorkspace();
        string script = Path.Combine(workspace, "ReignBeta", "tools", "GauntletXmlPreviewer", "audit-preview-contract.mjs");
        var start = new ProcessStartInfo("node")
        {
            WorkingDirectory = workspace,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        using var process = Process.Start(start);
        Assert.NotNull(process);
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(30000), "The Gauntlet XML preview contract audit timed out.");
        Assert.True(process.ExitCode == 0, $"The Gauntlet XML preview contract audit failed: {stderr}\n{stdout}");

        using JsonDocument report = JsonDocument.Parse(stdout);
        JsonElement counts = report.RootElement.GetProperty("counts");
        int discovered = Directory.GetFiles(Path.Combine(workspace, "ReignBeta", "GUI", "Prefabs"), "*.xml").Length;
        Assert.Equal(discovered, counts.GetProperty("prefabFiles").GetInt32());
        Assert.Equal(discovered, counts.GetProperty("catalogedPrefabFiles").GetInt32());
        Assert.Equal(discovered, counts.GetProperty("fixtureFiles").GetInt32());
        Assert.True(counts.GetProperty("checkedBindings").GetInt32() > 0);
        Assert.True(counts.GetProperty("checkedRuntimeBindings").GetInt32() > 0);
        Assert.True(counts.GetProperty("checkedRuntimeCommands").GetInt32() > 0);
        Assert.Equal(12, counts.GetProperty("checkedEditingContracts").GetInt32());
        Assert.Equal(118, counts.GetProperty("checkedToolContracts").GetInt32());
        Assert.Equal(22, counts.GetProperty("checkedNativeCalibrationTargets").GetInt32());
        Assert.Equal(15, counts.GetProperty("checkedNativeAugmentationTargets").GetInt32());
        Assert.Equal(30, counts.GetProperty("checkedNativePatchApplications").GetInt32());
        Assert.Equal(21, counts.GetProperty("discoveredRuntimeMovies").GetInt32());
        Assert.Equal(0, counts.GetProperty("errors").GetInt32());
        Assert.Equal(0, counts.GetProperty("warnings").GetInt32());
    }

    [Fact]
    public void CatalogIncludesEveryCallableToolAndRestartContinuityContract()
    {
        var service = new TestingCatalogService(TestOptions.Create());
        JsonElement result = JsonSerializer.SerializeToElement(service.GetCatalog());
        var cataloged = result.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToHashSet(StringComparer.Ordinal);
        var reflected = typeof(TestingCatalogTools).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Select(method => method.GetCustomAttribute<McpServerToolAttribute>()?.Name)
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(reflected.SetEquals(cataloged),
            $"Catalog drift. Missing: {string.Join(", ", reflected.Except(cataloged))}; extra: {string.Join(", ", cataloged.Except(reflected))}");
        string raw = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"));
        using JsonDocument catalogDocument = JsonDocument.Parse(raw);
        Assert.Equal("2026.09.12.1", catalogDocument.RootElement.GetProperty("version").GetString());
        var castlePromptFixes = catalogDocument.RootElement.GetProperty("sovereignCastlePromptFixes");
        Assert.Contains("contracts.castle_scene_context", castlePromptFixes.GetProperty("quick").GetString());
        Assert.Contains("contracts.sovereign_identity", castlePromptFixes.GetProperty("identity").GetString());
        Assert.Contains("adultScenery exclusively", castlePromptFixes.GetProperty("adultSceneRouting").GetString());
        Assert.Contains("newer user edits block overwrite", castlePromptFixes.GetProperty("acceptance").GetString());
        var backgroundPortraits = catalogDocument.RootElement.GetProperty("backgroundCharacterPortraits");
        Assert.Equal("reign-native-portrait-snapshot-v1", backgroundPortraits.GetProperty("snapshotSchema").GetString());
        Assert.Contains("--character-snapshot", backgroundPortraits.GetProperty("generatorCommand").GetString());
        Assert.Contains("never falls back to a packaged workspace or installed runtime",
            catalogDocument.RootElement.GetProperty("validation").GetProperty("offlineArtifactBinding").GetString(),
            StringComparison.OrdinalIgnoreCase);
        JsonElement rulerDocket = catalogDocument.RootElement.GetProperty("rulerDocket");
        JsonElement warCouncil = catalogDocument.RootElement.GetProperty("warCouncil");
        JsonElement familyChambers = catalogDocument.RootElement.GetProperty("familyChambers");
        JsonElement previewer = catalogDocument.RootElement.GetProperty("gauntletXmlPreviewer");
        JsonElement pregnancyWarningState = previewer.GetProperty("nativeRuntimeStateAcceptance");
        JsonElement government = catalogDocument.RootElement.GetProperty("government");
        JsonElement campaignStorage = catalogDocument.RootElement.GetProperty("campaignStorage");
        JsonElement fidelityCapability = catalogDocument.RootElement.GetProperty("capabilityFamilies")
            .EnumerateArray()
            .Single(capability => string.Equals(
                capability.GetProperty("id").GetString(),
                "gauntlet-approved-reference-fidelity",
                StringComparison.Ordinal));
        Assert.Contains("restart_codex_and_resume_task", raw, StringComparison.Ordinal);
        Assert.Contains("every physical .sav owned by the campaign",
            campaignStorage.GetProperty("portableBackupContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replaceNativeSaves",
            campaignStorage.GetProperty("portableBackupContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("all unfinished Codex tasks", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("installedVersion\": \"1.9.0", raw, StringComparison.Ordinal);
        Assert.Contains("Recovery is Desktop-only for both planned MCP restarts and unexpected closures", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("open-in-another-app locks", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never starts a hidden codex exec resume process", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing-tool-host retry loops", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reign_prepare_campaign_test", raw, StringComparison.Ordinal);
        Assert.Equal("reign-ruler-docket-launch-acceptance-v12", rulerDocket.GetProperty("schema").GetString());
        Assert.Contains("complete persisted player and NPC conversation lines",
            rulerDocket.GetProperty("nobleTranscriptEvidenceContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not to keep, renew, continue, or pursue the quarrel",
            rulerDocket.GetProperty("nobleTranscriptEvidenceContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("judgment is given or heard",
            rulerDocket.GetProperty("nobleTranscriptEvidenceContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quarrel ends here",
            rulerDocket.GetProperty("nobleTranscriptEvidenceContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noble ViewModel before any petition ViewModel",
            rulerDocket.GetProperty("activeAudienceAutomationContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("monotonic send revision",
            rulerDocket.GetProperty("activeAudienceAutomationContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("authoritative persisted matter conversation",
            rulerDocket.GetProperty("activeAudienceAutomationContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("timeout evidence identifies every gate",
            rulerDocket.GetProperty("activeAudienceAutomationContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expectedAcceptanceTier set to 0 through 3",
            rulerDocket.GetProperty("providerBackedLanguageGate").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot satisfy provider-backed language acceptance",
            rulerDocket.GetProperty("providerBackedLanguageGate").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strict UTF-8",
            rulerDocket.GetProperty("providerTransportContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mojibake",
            rulerDocket.GetProperty("providerTransportContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4-kind × 3-severity", rulerDocket.GetProperty("petitionMatrix").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no expiry", rulerDocket.GetProperty("dailyGenerationContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full-strength private long-term memory", rulerDocket.GetProperty("emergencyContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typed ruler-to-petitioner conversation", rulerDocket.GetProperty("dialogueAndArtContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("four or fewer", rulerDocket.GetProperty("dialogueAndArtContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expedition_return_prepare", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("expedition_return_verify", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("petition_insufficient", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("petition_invalid_identity", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("chancellor_schedule_prepare", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("chancellor_schedule_verify", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("chancellor_eligibility_dismissal", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("legacy_migration", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("natural_soak_prepare", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("natural_soak_verify", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("noble_template_case", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("noble_execution", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("noble_save_verify", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("royal_proclamation", rulerDocket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("all 68", rulerDocket.GetProperty("nobleTemplateContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural in-world stances", rulerDocket.GetProperty("nobleTemplateContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must never mention tiers, percentages, scores, relationship values, penalties, or game mechanics",
            rulerDocket.GetProperty("nobleTemplateContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fixtureRunId", rulerDocket.GetProperty("nobleParticipantDiversityContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("available distinct-variant count",
            rulerDocket.GetProperty("nobleParticipantDiversityContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unique-hero", rulerDocket.GetProperty("nobleParticipantDiversityContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cultures", rulerDocket.GetProperty("nobleParticipantDiversityContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("both ruling sides", rulerDocket.GetProperty("nobleParticipantDiversityContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kills the victim exactly once only when the hearing opens",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native observer", rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact activation-source matter correlation",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("activation-source",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("noble_judgment",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("denied divorce",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deniedDivorcePreservationRequired",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("reciprocalSpouseStatePreserved",
            rulerDocket.GetProperty("nobleFamilyAndMurderContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("180-day", rulerDocket.GetProperty("releaseGate").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("opens the production Court", rulerDocket.GetProperty("sessionContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("before", rulerDocket.GetProperty("sessionContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("participant count", rulerDocket.GetProperty("providerTimingContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("480 seconds", rulerDocket.GetProperty("providerTimingContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("closing", rulerDocket.GetProperty("providerTimingContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1800 seconds", rulerDocket.GetProperty("providerTimingContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot advance time", rulerDocket.GetProperty("safety").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reign-government-release-certification-v1", government.GetProperty("schema").GetString());
        Assert.Equal(16, government.GetProperty("profiles").GetArrayLength());
        Assert.Contains(government.GetProperty("profiles").EnumerateArray(), value => value.GetString() == "hearing_compose");
        Assert.Equal(9, government.GetProperty("mcpTools").GetArrayLength());
        Assert.Contains("reign_prepare_government_certification", raw, StringComparison.Ordinal);
        Assert.Contains("reign_carry_forward_government_passes", raw, StringComparison.Ordinal);
        Assert.Contains("reign_evaluate_government_release_readiness", raw, StringComparison.Ordinal);
        Assert.Contains("36 risk-based natural-language", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("43 native", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attempt-suffixed live-run ID",
            government.GetProperty("retryContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("All 85 required case instances", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fallback Council of Estates", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("100% tag/action/safety correctness", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural second player turn", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must never mention numbered authority levels or an authority scale",
            government.GetProperty("dialogueLanguageContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("source-run, live-run, report-path, hash, and scope provenance",
            government.GetProperty("carryForwardContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("numbered one-to-five authority scale is hidden mechanical state",
            government.GetProperty("dialogueLanguageContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("immediately preceding completed production reply", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prose is never interpreted as consent", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("104", government.GetProperty("resolutionContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("-70", government.GetProperty("authorityContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("separate", government.GetProperty("pressureContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zero provider calls", government.GetProperty("providerContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reign-rebellion-certification-manifest-v1", raw,
            StringComparison.Ordinal);
        Assert.Contains("reign_prepare_rebellion_certification", raw,
            StringComparison.Ordinal);
        Assert.Contains("The server-only contract credits only the five language_contract adapters",
            raw, StringComparison.Ordinal);
        Assert.Contains("preserving their durable report references", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mixed_transfer", raw, StringComparison.Ordinal);
        Assert.Contains("foreign_reintegration", raw, StringComparison.Ordinal);
        Assert.Contains("requiresCampaignTestRunId", raw, StringComparison.Ordinal);
        Assert.Contains("saveNameInferenceProhibited", raw, StringComparison.Ordinal);
        Assert.Contains("boundedWorldTestReceipts", raw, StringComparison.Ordinal);
        Assert.Contains("initialBaselineCheckpoint", raw, StringComparison.Ordinal);
        Assert.Contains("missing pre-existing daily rollup", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("latest fully completed campaign day", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"warCouncil\"", raw, StringComparison.Ordinal);
        Assert.Contains("ReignLiveTest ui-open --target war-council", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilMapImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilScrollFrameImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilOuterFrameImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilSettlementImagesAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilFixedOverview", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilMapPanningEnabled", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilPortraitRevision", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilCouncilorName", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilTownSettlementCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilCastleSettlementCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilVillageSettlementCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilTimePaused", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilFirstMobilizableHeroId", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilDetectedForeignPartyCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilCouncilorTactics", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilCouncilorLeadership", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilDetectionRange", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilDetectionChance", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilSelectedCouncilorHeroId", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilEffectiveCouncilorHeroId", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilSettlementListCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilRealmBattleReportCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilPanelFrameImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilTallPanelFrameImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilPanelFrameOverlayImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilRavenImageAvailable", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilMessengerVisible", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilPlayerRealmPartyCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilHostilePartyCount", raw, StringComparison.Ordinal);
        Assert.Contains("warCouncilNavalPartyCount", raw, StringComparison.Ordinal);
        Assert.Contains("black-painted wooden figures", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("brown display-only intelligence figures", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-approved-reference-fidelity.mjs",
            fidelityCapability.GetProperty("entry").GetString());
        Assert.Equal("workspace-output", fidelityCapability.GetProperty("risk").GetString());
        Assert.Equal(22, previewer.GetProperty("currentPrefabCount").GetInt32());
        Assert.Equal(68, previewer.GetProperty("currentInterfaceStateCount").GetInt32());
        Assert.Equal(15, previewer.GetProperty("nativeAugmentationTargetCount").GetInt32());
        Assert.Equal(30, previewer.GetProperty("nativePatchApplicationCount").GetInt32());
        Assert.Equal(166, previewer.GetProperty("providerFreeRenderCaseCount").GetInt32());
        Assert.Equal(37, previewer.GetProperty("nativeAcceptanceSurfaceCount").GetInt32());
        Assert.Equal(1, previewer.GetProperty("nativeRuntimeStateAcceptanceCount").GetInt32());
        Assert.Equal("reign-ui-native-runtime-state-acceptance-v1",
            pregnancyWarningState.GetProperty("schema").GetString());
        Assert.Equal("reign-ui-native-runtime-state-receipt-v1",
            pregnancyWarningState.GetProperty("receiptSchema").GetString());
        Assert.Equal("individual-chat", pregnancyWarningState.GetProperty("parentTargetId").GetString());
        Assert.Equal("individual-chat-pregnancy-warning",
            pregnancyWarningState.GetProperty("stateId").GetString());
        Assert.Equal("inherit-parent", pregnancyWarningState.GetProperty("matrix").GetString());
        Assert.Equal("<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/acceptance.json",
            pregnancyWarningState.GetProperty("acceptanceManifestPath").GetString());
        Assert.Equal("<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/<matrix-case>/state-receipt.json",
            pregnancyWarningState.GetProperty("stateReceiptPath").GetString());
        string pregnancyWarningCapture = pregnancyWarningState.GetProperty("captureBehavior").GetString()!;
        Assert.Contains("ui-open --target individual-chat", pregnancyWarningCapture, StringComparison.Ordinal);
        Assert.Contains("show-pregnancy-warning", pregnancyWarningCapture, StringComparison.Ordinal);
        Assert.Contains("reviewed=false", pregnancyWarningCapture, StringComparison.Ordinal);
        Assert.Contains("does not change the 68 browser states, 166 provider-free renders, or 37 native surfaces",
            pregnancyWarningCapture, StringComparison.OrdinalIgnoreCase);
        string testingGuide = TestingDocumentation.Read(Path.Combine(TestOptions.FindWorkspace(), "docs", "agent",
            "TESTING_TOOL_GUIDE.md"));
        string previewerReadme = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "tools",
            "GauntletXmlPreviewer", "README.md"));
        foreach (string documentation in new[] { testingGuide, previewerReadme })
        {
            Assert.Contains("reign-ui-native-runtime-state-acceptance-v1", documentation, StringComparison.Ordinal);
            Assert.Contains("reign-ui-native-runtime-state-receipt-v1", documentation, StringComparison.Ordinal);
            Assert.Contains("<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/acceptance.json",
                documentation, StringComparison.Ordinal);
            Assert.Contains("<evidence-root>/individual-chat/states/individual-chat-pregnancy-warning/<matrix-case>/state-receipt.json",
                documentation, StringComparison.Ordinal);
            Assert.Contains("68 browser states", documentation, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("166", documentation, StringComparison.Ordinal);
            Assert.Contains("37 native surfaces", documentation, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("33 native surfaces", documentation, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(1080, previewer.GetProperty("minimumSupportedHeight").GetInt32());
        Assert.Equal(
            new[] { "2560x1600", "1920x1080", "2560x1440", "3440x1440", "3840x1600", "3840x2160" },
            previewer.GetProperty("supportedNativeResolutions").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Contains("every movie loaded by ReignBeta client source", previewer.GetProperty("inventoryContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production client ViewModel/member graph", previewer.GetProperty("bindingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alt explicitly moves the selected child", previewer.GetProperty("editingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot inherit global button minimum dimensions", previewer.GetProperty("editingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("reign-ui-direct-manipulation-audit-v1", previewer.GetProperty("directManipulationAuditSchema").GetString());
        Assert.Equal("reign-ui-custom-scale-lock-audit-v1", previewer.GetProperty("customScaleLockAuditSchema").GetString());
        Assert.Contains("DoNotUseCustomScaleAndChildren", previewer.GetProperty("scaleContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("2580x1080 logical root", previewer.GetProperty("scaleContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ultrawide side gutters rather than stretched", previewer.GetProperty("scaleContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1920x1080 and 120% UI scale", previewer.GetProperty("scaleContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact retained resolution/configured-UI-scale matrix case", previewer.GetProperty("nativeEvidenceSelectionContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("loaded-prefab SHA-256", previewer.GetProperty("nativeEvidenceSelectionContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dedicated Codex App Server task", previewer.GetProperty("codexContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-codex-chat-e2e.mjs", previewer.GetProperty("codexChatE2eAudit").GetString());
        Assert.Contains("captures the outbound turn/start payload", previewer.GetProperty("codexE2eContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-native-parity.mjs", previewer.GetProperty("nativeParityAudit").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/capture-native-matrix.ps1", previewer.GetProperty("nativeCaptureBatch").GetString());
        Assert.Equal("reign-ui-native-capture-batch-v1", previewer.GetProperty("nativeCaptureBatchSchema").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/capture-native-display-matrix.ps1", previewer.GetProperty("nativeDisplayMatrix").GetString());
        Assert.Equal("reign-ui-native-display-matrix-v1", previewer.GetProperty("nativeDisplayMatrixSchema").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/capture-native-augmentation.ps1", previewer.GetProperty("nativeAugmentationCapture").GetString());
        Assert.Equal("reign-ui-native-augmentation-capture-v1", previewer.GetProperty("nativeAugmentationCaptureSchema").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/capture-native-augmentation-batch.ps1", previewer.GetProperty("nativeAugmentationCaptureBatch").GetString());
        Assert.Equal("reign-ui-native-capture-batch-v1", previewer.GetProperty("nativeAugmentationCaptureBatchSchema").GetString());
        Assert.Equal("reign-ui-native-contact-sheet-v1", previewer.GetProperty("nativeContactSheetSchema").GetString());
        Assert.Contains("both provider-free campaign capture batches", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("240 minutes", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ok=true", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("armed=true", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("never rely on a stale or external prior arm", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not advance campaign time or save the campaign", previewer.GetProperty("nativeBridgeArmingContract").GetString(), StringComparison.OrdinalIgnoreCase);
        string nativeHeadlessSnapshot = previewer.GetProperty("nativeHeadlessSnapshotContract").GetString()!;
        Assert.Contains("headless", nativeHeadlessSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nativeInjection=false", nativeHeadlessSnapshot, StringComparison.Ordinal);
        Assert.Contains("must never load it as a second movie", nativeHeadlessSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("supportUi payload", nativeHeadlessSnapshot, StringComparison.Ordinal);
        Assert.Contains("not a live-test target", nativeHeadlessSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no native expand or collapse action", nativeHeadlessSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fails closed", nativeHeadlessSnapshot, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reign-ui-native-visual-review-attestation-v1", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("All nine criteria", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ReignSerifDynamic/Cormorant Garamond face and weight", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gold/gray semantic text colors", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no unexpected colors or shapes", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Missing, false, unknown, legacy, or unreviewed attestations remain pending", previewer.GetProperty("nativeVisualReviewContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all 37 native surfaces", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--require-pass exits nonzero", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no calibration-overlay movie, widget, action, or supportUi payload", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("verified-foreground Bannerlord receipt", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accept-native-case", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not add a separate surface", previewer.GetProperty("nativeAcceptanceContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviewed=false", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never advances or saves", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configured UIScale", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UIContext.CustomScale", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("zero provider calls", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("attached-input-thread retries", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per-monitor-DPI-aware", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PrintWindow full-content", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blank output is rejected", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client bounds are recorded separately", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DirectX backbuffer screenshot", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact requested pixel dimensions", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact case evidence directory", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("only the exact run-created Steam screenshot", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same evidence root", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all 15 cataloged production augmentation targets", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("game start-menu", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("process-log-backed GauntletInitialScreen::HandleActivate readiness evidence", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("without saving", previewer.GetProperty("nativeBatchContract").GetString(), StringComparison.OrdinalIgnoreCase);
        string initialScreenReadiness = previewer.GetProperty("initialScreenReadinessContract").GetString()!;
        Assert.Contains("never treats process existence or elapsed time as UI readiness", initialScreenReadiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rgl_log_<pid>.txt", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("512 KiB", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("GauntletInitialScreen::HandleActivate", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("two continuous seconds", initialScreenReadiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("readinessMarker", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("readinessLogPath", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("stableInitialScreenSeconds=2", initialScreenReadiness, StringComparison.Ordinal);
        Assert.Contains("times out fail-closed", initialScreenReadiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact confirmation", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("byte-for-byte backups", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("capture display_mode", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("finally block restores", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SHA-256", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reviewed=false", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-NativeAugmentations", previewer.GetProperty("nativeDisplayMatrixContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("non-persistent four-domain transcript", previewer.GetProperty("nativeFixtureSafetyContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all 37 native surfaces", previewer.GetProperty("readinessUiContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("selected-widget Codex context", previewer.GetProperty("readinessUiContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Installation remains available while Bannerlord runs", previewer.GetProperty("installContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("movie-load time", previewer.GetProperty("parityContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-rendered-preview.mjs", previewer.GetProperty("renderAudit").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-overlay-alpha.mjs", previewer.GetProperty("overlayAlphaAudit").GetString());
        Assert.Contains("analysis.apertures", previewer.GetProperty("overlayAlphaCompoundContract").GetString(), StringComparison.Ordinal);
        Assert.Equal("--self-test", previewer.GetProperty("overlayAlphaSelfTest").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/js/rendered-preview-targets.js", previewer.GetProperty("renderTargetSelectionHelper").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/test-clan-accords-contract.mjs", previewer.GetProperty("renderTargetSelectionTest").GetString());
        Assert.Contains("partialScope=true", previewer.GetProperty("renderTargetSelectionContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("never establishes full-catalog completion", previewer.GetProperty("renderTargetSelectionContract").GetString(), StringComparison.Ordinal);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-approved-reference-fidelity.mjs",
            previewer.GetProperty("approvedReferenceFidelityAudit").GetString());
        Assert.Equal("reign-ui-approved-reference-fidelity-audit-v1",
            previewer.GetProperty("approvedReferenceFidelityAuditSchema").GetString());
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/calibration/full-catalog-approved-reference-fidelity-contract.json",
            previewer.GetProperty("approvedReferenceFidelityContract").GetString());
        Assert.Equal("reign-ui-approved-reference-fidelity-contract-v1",
            previewer.GetProperty("approvedReferenceFidelityContractSchema").GetString());
        Assert.Contains("referenceSurfaceBounds",
            previewer.GetProperty("approvedReferenceFidelityRule").GetString(), StringComparison.Ordinal);
        Assert.Contains("Missing or out-of-bounds geometry fails closed",
            previewer.GetProperty("approvedReferenceFidelityRule").GetString(), StringComparison.Ordinal);
        Assert.Equal("ReignBeta/tools/GauntletXmlPreviewer/audit-typography-parity.mjs",
            previewer.GetProperty("typographyParityAudit").GetString());
        Assert.Equal("reign-ui-typography-parity-audit-v1",
            previewer.GetProperty("typographyParityAuditSchema").GetString());
        Assert.Equal(176, previewer.GetProperty("typographyFixedLiteralTextCount").GetInt32());
        Assert.Equal(376, previewer.GetProperty("typographyDynamicBoundTextCount").GetInt32());
        Assert.Contains("including literal runtime controls and runtime-bound values",
            previewer.GetProperty("typographyParityRule").GetString(), StringComparison.Ordinal);
        Assert.Contains("must not survive as hidden or transparent runtime TextWidgets",
            previewer.GetProperty("typographyParityRule").GetString(), StringComparison.Ordinal);
        Assert.Contains("face/weight/style",
            previewer.GetProperty("typographyParityRule").GetString(), StringComparison.Ordinal);
        Assert.Contains("reviewed native 2560x1600 captures",
            previewer.GetProperty("typographyParityRule").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, previewer.GetProperty("providerFreeRenderMatrix").GetArrayLength());
        Assert.Equal(22, previewer.GetProperty("nativeCalibrationTargets").GetArrayLength());
        Assert.Equal(21, previewer.GetProperty("nativeRuntimeMovieCount").GetInt32());
        Assert.Equal("ReignUiCalibrationOverlay", previewer.GetProperty("previewerSupportMovie").GetString());
        string uiCatalogRaw = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "tools",
            "GauntletXmlPreviewer", "calibration", "ui-catalog.json"));
        using JsonDocument uiCatalogDocument = JsonDocument.Parse(uiCatalogRaw);
        JsonElement uiCatalog = uiCatalogDocument.RootElement;
        Assert.Equal(22, uiCatalog.GetProperty("nativeCalibration").GetProperty("targetCount").GetInt32());
        Assert.Equal("ReignUiCalibrationOverlay",
            uiCatalog.GetProperty("nativeCalibration").GetProperty("previewerSupportMovie").GetString());
        JsonElement calibrationOverlay = uiCatalog.GetProperty("interfaces").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(), "calibration-overlay",
                StringComparison.Ordinal));
        Assert.True(calibrationOverlay.GetProperty("supportUi").GetBoolean());
        Assert.False(calibrationOverlay.GetProperty("nativeInjection").GetBoolean());
        Assert.Equal("XML Previewer > Calibration Overlay", calibrationOverlay.GetProperty("entry").GetString());
        Assert.Equal("previewer only", calibrationOverlay.GetProperty("liveAction").GetString());
        JsonElement individualChat = uiCatalog.GetProperty("interfaces").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(), "individual-chat",
                StringComparison.Ordinal));
        JsonElement pregnancyWarningCatalogState = individualChat.GetProperty("previewStates").EnumerateArray()
            .Single(item => string.Equals(item.GetProperty("id").GetString(),
                "individual-chat-pregnancy-warning", StringComparison.Ordinal));
        JsonElement pregnancyWarningNativeEvidence = pregnancyWarningCatalogState.GetProperty("nativeEvidence");
        Assert.True(pregnancyWarningCatalogState.GetProperty("previewOnly").GetBoolean());
        Assert.False(pregnancyWarningCatalogState.GetProperty("nativeInjection").GetBoolean());
        Assert.True(pregnancyWarningNativeEvidence.GetProperty("required").GetBoolean());
        Assert.Equal("show-pregnancy-warning",
            pregnancyWarningNativeEvidence.GetProperty("setupAction").GetString());
        Assert.Equal("inherit-parent", pregnancyWarningNativeEvidence.GetProperty("matrix").GetString());
        Assert.Equal("states/individual-chat-pregnancy-warning/acceptance.json",
            pregnancyWarningNativeEvidence.GetProperty("acceptanceRelativePath").GetString());
        Assert.Contains("separate explicit opt-in", previewer.GetProperty("fixtureContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("snapshot prefab SHA-256", previewer.GetProperty("fixtureContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporary and provider-free", previewer.GetProperty("nativeFixtureSafetyContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot write conversations, letters, social events", previewer.GetProperty("nativeFixtureSafetyContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("screenshot hashes", previewer.GetProperty("parityContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native augmentation", previewer.GetProperty("parityContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace, installed, and loaded-snapshot hashes", previewer.GetProperty("parityContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("ReignLiveTest ui-open --target family-chambers", familyChambers.GetProperty("entry").GetString());
        JsonElement trainingYard = catalogDocument.RootElement.GetProperty("trainingYard");
        Assert.Equal("ReignLiveTest ui-open --target training-yard", trainingYard.GetProperty("entry").GetString());
        Assert.Contains("floor((Leadership + highest supported weapon skill) / 6)", trainingYard.GetProperty("behaviorContract").GetString(), StringComparison.Ordinal);
        Assert.Contains("trainingYardTotalXpDelivered", raw, StringComparison.Ordinal);
        Assert.Contains("familyChambersCanEnter", raw, StringComparison.Ordinal);
        Assert.Contains("exact age five onward", familyChambers.GetProperty("childSafetyContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UltraFace", familyChambers.GetProperty("imageContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one through four NPCs", familyChambers.GetProperty("selectionContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(44, warCouncil.GetProperty("fixedZoom").GetInt32());
        Assert.Contains("70 world units at 0 to 520 at 300",
            warCouncil.GetProperty("councilorContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("25% at 0 to 90% at 250",
            warCouncil.GetProperty("councilorContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bold red", warCouncil.GetProperty("reportEmphasisContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every town, castle, and village",
            warCouncil.GetProperty("settlementDirectoryContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("transparent Reign-style",
            warCouncil.GetProperty("panelFrameContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(22836, warCouncil.GetProperty("riverSourceMaskPixels").GetInt32());
        Assert.Equal(4771883, warCouncil.GetProperty("riverProjectedMasterPixels").GetInt32());
        Assert.Equal(4718768, warCouncil.GetProperty("riverLandBoundMasterPixels").GetInt32());
        Assert.Contains("actual variable-width water footprint",
            warCouncil.GetProperty("riverGeometryContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same unified land/water contour",
            warCouncil.GetProperty("riverGeometryContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rather than fixed-width centerline overlays",
            warCouncil.GetProperty("riverGeometryContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(warCouncil.GetProperty("mainPartyMarkerRequired").GetBoolean());
        Assert.Equal(108, warCouncil.GetProperty("markerPixelFootprint").GetInt32());
        Assert.Contains("\"mapMasterPixels\": 16384", raw, StringComparison.Ordinal);
        Assert.Contains("\"runtimeTileGrid\": \"4x4\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"runtimeTilePixels\": 4096", raw, StringComparison.Ordinal);
        Assert.Equal("PNG RGB lossless", warCouncil.GetProperty("runtimeTileFormat").GetString());
        Assert.Equal(1048576, warCouncil.GetProperty("runtimeTileMinimumBytes").GetInt64());
        Assert.Contains("exactly sixteen 4,096-pixel RGB PNG runtime tiles", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("without the generic managed decode/re-encode normalization", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(69500, warCouncil.GetProperty("exactFloraInstances").GetInt32());
        Assert.Equal(39, warCouncil.GetProperty("exactStrategicMountainPlacements").GetInt32());
        Assert.Equal(39, warCouncil.GetProperty("exactBridgePlacements").GetInt32());
        Assert.Equal(33, warCouncil.GetProperty("exactPhysicalBridgeSites").GetInt32());
        Assert.Contains("32 river-crossing bridge sites",
            warCouncil.GetProperty("bridgeIllustrationContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("strict-orthographic top-down",
            warCouncil.GetProperty("bridgeIllustrationContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no side elevation",
            warCouncil.GetProperty("bridgeIllustrationContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("meet both banks",
            warCouncil.GetProperty("bridgeIllustrationContract").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(7, warCouncil.GetProperty("editorMaterialLayerCount").GetInt32());
        Assert.Equal(0, warCouncil.GetProperty("proceduralStaticPlacements").GetInt32());
        Assert.Contains("\"playableWorldBounds\"", raw, StringComparison.Ordinal);
        Assert.Contains("Main_map flora.bin FLR2 transforms", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete official river mask", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no river is skeletonized", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("or tinted blue", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stable non-flickering native portraits", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bounded click-drag panning", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pan-map", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("same sealed letter record as Correspondence", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("War Sails NavalDLC Main_map", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"bannerlordEditor\"", raw, StringComparison.Ordinal);
        Assert.Contains("scene_inspect_terrain", raw, StringComparison.Ordinal);
        Assert.Contains("257 by 257", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4097 by 4097", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bakes its rolled-scroll edges", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporary unsaved controller", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("passiveWorldAnnouncementHandling", raw, StringComparison.Ordinal);
        Assert.Contains("tracked queued Reign kingdom-event inquiry", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("controlledAgentAction", raw, StringComparison.Ordinal);
        Assert.Contains("production 58% threshold", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("controlledCounterintelligence", raw, StringComparison.Ordinal);
        Assert.Contains("controlledSelfMitigation", raw, StringComparison.Ordinal);
        Assert.Contains("reign_restart_campaign_test", raw, StringComparison.Ordinal);
        Assert.Contains("reign_restore_campaign_test_checkpoint", raw,
            StringComparison.Ordinal);
        Assert.Contains("reign_verify_party_agency_save_roundtrip", raw,
            StringComparison.Ordinal);
        Assert.Contains("reign_carry_forward_party_agency_passes", raw,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_return_timing_evidence_only", raw,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_recovery_fixture_only", raw,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_missing_return_recovery_fixture_only", raw,
            StringComparison.Ordinal);
        Assert.Contains("preserves the already-proven death and capture variants", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("death and capture directly from an active or refusing guest state", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("temporarily remove the source-party record reference", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prove that native state before return", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PA-NATIVE-015 client-harness positioning and timing-receipt repairs",
            raw, StringComparison.Ordinal);
        Assert.Contains("campaign-command-v9", raw, StringComparison.Ordinal);
        Assert.Contains("llmMatrixEvidence", raw, StringComparison.Ordinal);
        Assert.Contains("All 300 base rows", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("naturalLanguagePolicy", raw, StringComparison.Ordinal);
        Assert.Contains("200 distinct complete natural player commands", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at least 30 active-draft clarification", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retreat and nearest-safe-settlement language must resolve to withdraw rather than generic move", raw,
            StringComparison.Ordinal);
        Assert.Contains("exact player utterance", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("naturalLanguageLaunchGate", raw, StringComparison.Ordinal);
        Assert.Contains("lightVerificationPolicy", raw, StringComparison.Ordinal);
        Assert.Contains("40-case risk-stratified light smoke", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean persisted dialogue history", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("conversation count", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not grade conversational coherence", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profileEvidenceExport", raw, StringComparison.Ordinal);
        Assert.Contains("parsed evidence object", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("player_favoring_dialogue count/isolation behavior owns a versioned compatibility fingerprint", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("versioned compatibility fingerprint", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("batch acceptance_harness hashes the exact shared client and server source contracts", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("batchAcceptanceContract", raw, StringComparison.Ordinal);
        Assert.Contains("12 ruler views across 1000 NPCs", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("worker task", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("every retry joins that same task", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("day 0, 30, 60, and 90", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("blockerCount", raw, StringComparison.Ordinal);
        Assert.Contains("\"product\"", raw, StringComparison.Ordinal);
        Assert.Contains("workspace-wide lease", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ecosystem release", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reign-repository-policy-v1", raw, StringComparison.Ordinal);
        Assert.Contains("repository-hygiene-report", raw, StringComparison.Ordinal);
        Assert.Contains("26214400", raw, StringComparison.Ordinal);
        Assert.Contains("\"audit\"", raw, StringComparison.Ordinal);
        Assert.Contains("\"enforce\"", raw, StringComparison.Ordinal);
        Assert.Contains("matched secret values are never written", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reign-sovereign-demeanor-pairwise-v1", raw,
            StringComparison.Ordinal);
        Assert.Contains("Zero hard political-conduct safety violations", raw,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("One seed per case initially", raw,
            StringComparison.OrdinalIgnoreCase);
        string sovereignManifest = File.ReadAllText(Path.Combine(
            TestOptions.FindWorkspace(), "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "sovereign-demeanor-pairwise-manifest.json"));
        Assert.Contains("\"caseCount\": 36", sovereignManifest,
            StringComparison.Ordinal);
        Assert.Contains("zeonica_castle_bathhouse", sovereignManifest,
            StringComparison.Ordinal);
        Assert.Contains("save_load_persistence", sovereignManifest,
            StringComparison.Ordinal);
        string guide = TestingDocumentation.Read(Path.Combine(TestOptions.FindWorkspace(),
            "docs", "agent", "TESTING_TOOL_GUIDE.md"));
        Assert.Contains("repository-hygiene-report.json", guide, StringComparison.Ordinal);
        Assert.Contains("classified by path rather than inspected", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("one tracked Reign kingdom-event inquiry at a time", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("excludes only agenda day, player-sitting day, and session revision counters", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui-open --target war-council", guide, StringComparison.Ordinal);
        Assert.Contains("ui-open --target family-chambers", guide, StringComparison.Ordinal);
        Assert.Contains("ui-open --target training-yard", guide, StringComparison.Ordinal);
        Assert.Contains("familyChambersCanEnter", guide, StringComparison.Ordinal);
        Assert.Contains("audit-preview-contract.mjs", guide, StringComparison.Ordinal);
        Assert.Contains("audit-rendered-preview.mjs", guide, StringComparison.Ordinal);
        Assert.Contains("--targets clan-accords,economic-report", guide, StringComparison.Ordinal);
        Assert.Contains("partialScope=true", guide, StringComparison.Ordinal);
        Assert.Contains("audit-codex-chat-e2e.mjs", guide, StringComparison.Ordinal);
        Assert.Contains("audit-native-parity.mjs", guide, StringComparison.Ordinal);
        Assert.Contains("accept-native-case", guide, StringComparison.Ordinal);
        Assert.Contains("I confirm this native capture matches the approved Reign reference for all fixed visuals and typography", guide, StringComparison.Ordinal);
        Assert.Contains("nine-point attestation", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("configured `UIScale`", guide, StringComparison.Ordinal);
        Assert.Contains("`UIContext.CustomScale` is a separate", guide, StringComparison.Ordinal);
        Assert.Contains("royalCouncilCalibrationFixture=true", guide, StringComparison.Ordinal);
        Assert.Contains("reign-ui-window-capture-v1", guide, StringComparison.Ordinal);
        Assert.Contains("Every-interface parity gate", guide, StringComparison.Ordinal);
        Assert.Contains("headless runtime snapshot", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("166-case", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace, installed, and snapshot prefab hashes to agree", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all-prefab summary", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hold Alt only when the exact child offset is intentionally required", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bottom Codex dock", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The 22 native runtime targets are", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("15 native Bannerlord prefab targets", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("30 PrefabExtension applications", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui-open --target memories-book", guide, StringComparison.Ordinal);
        Assert.Contains("ReignUiCalibrationOverlay` remains provider-free browser-preview support", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("`nativeInjection=false`", guide, StringComparison.Ordinal);
        Assert.Contains("native click-drag and `pan-map` alter bounded offsets", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("warCouncilPortraitRevision", guide, StringComparison.Ordinal);
        Assert.Contains("close fixed-scale map", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete 69,500-transform `flora.bin` source", guide, StringComparison.Ordinal);
        Assert.Contains("editor-height-derived mountain ranges", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("39 strategic mountain anchors", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("33 physical", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zero procedural static placements", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bypassing the generic managed PNG decode/re-encode step", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("native screenshot proving the raster actually rendered", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bannerlord Editor MCP", guide, StringComparison.Ordinal);
        Assert.Contains("close-without-save", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("global and per-node height ranges", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RB-LANG-007..011", guide, StringComparison.Ordinal);
        Assert.Contains("RB-DET-001..006 require their distinct client-harness reports",
            guide, StringComparison.Ordinal);
        Assert.Contains("exactly 36 initial natural-player cases", guide,
            StringComparison.OrdinalIgnoreCase);
        string validationSource = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(),
            "ReignMcp", "src", "Reign.Mcp.Server", "Modules", "Validation", "ReignValidationService.cs"));
        Assert.Contains("Path.Combine(options.WorkspaceRoot, \"reign.testing.json\")",
            validationSource, StringComparison.Ordinal);
        Assert.Contains("RepositoryHygiene", validationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void InitialScreenStartMenuRequiresNativeGauntletActivationEvidence()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(workspace, "ReignServer", "tests", "ReignLiveTest", "Program.cs"));
        string batch = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "tools", "GauntletXmlPreviewer",
            "capture-native-augmentation-batch.ps1"));

        Assert.Contains("Math.Max(60, Math.Min(600, IntValue(args, \"--wait\", 240)))", source,
            StringComparison.Ordinal);
        Assert.Contains("TryFindInitialScreenActivation(processIds", source, StringComparison.Ordinal);
        Assert.Contains("GauntletInitialScreen::HandleActivate", source, StringComparison.Ordinal);
        Assert.Contains("TimeSpan.FromSeconds(2)", source, StringComparison.Ordinal);
        Assert.Contains("[\"readinessMarker\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"readinessLogPath\"]", source, StringComparison.Ordinal);
        Assert.Contains("[\"stableInitialScreenSeconds\"] = 2", source, StringComparison.Ordinal);
        Assert.Contains("const int maximumTailBytes = 512 * 1024", source, StringComparison.Ordinal);
        Assert.Contains("FileShare.ReadWrite | FileShare.Delete", source, StringComparison.Ordinal);
        Assert.Contains("initialScreenReadySinceUtc = null", source, StringComparison.Ordinal);
        Assert.Contains("@('game', 'start-menu', '--wait', '120', '--json')", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveTestCatalogAndHelpCoverEveryTopLevelCommand()
    {
        string root = TestOptions.FindWorkspace();
        using JsonDocument document = JsonDocument.Parse(TestingDocumentation.Read(
            Path.Combine(root, "reign.testing.json")));
        string[] commands = document.RootElement.GetProperty("liveTestCommands")
            .EnumerateArray().Select(value => value.GetString()!).ToArray();
        string program = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "Program.cs"));
        string catalog = File.ReadAllText(Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "TestingCatalogControl.cs"));
        string[] implemented = System.Text.RegularExpressions.Regex.Matches(program, "case \\\"([^\\\"]+)\\\"")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(implemented, commands.OrderBy(value => value, StringComparer.Ordinal).ToArray());
        foreach (string command in commands)
        {
            Assert.Contains($"case \"{command}\"", program, StringComparison.Ordinal);
            Assert.Contains($"\"{command}\"", catalog, StringComparison.Ordinal);
        }
        Assert.Contains("ui-open --target court|court-petition|castle-layout|castle-chat|ambassador|economic-report|clan-accords|government|family-chambers|training-yard|royal-council|spymaster|war-council|correspondence|individual-chat|party-chat|tavern-house|social-event|wilderness-event|diplomacy-announcement|notable-generation|memories-book",
            program, StringComparison.Ordinal);
        Assert.Contains("ui-action --target ambassador|economic-report|spymaster|war-council|family-chambers|training-yard|royal-council|individual-chat|native-conversation",
            program, StringComparison.Ordinal);
        Assert.Contains("ui-snapshot --target <ui>", program, StringComparison.Ordinal);
        Assert.DoesNotContain("ui-snapshot [--target <ui>]", program, StringComparison.Ordinal);

        JsonElement interaction = document.RootElement.GetProperty("uiInteractionCalibration");
        JsonElement governmentInput = interaction.GetProperty("governmentInputObservation");
        Assert.Equal("reign-government-input-observation-v1", governmentInput.GetProperty("schema").GetString());
        Assert.Equal("ui-status", governmentInput.GetProperty("route").GetString());
        Assert.Contains("never opens a fixture", governmentInput.GetProperty("contract").GetString(), StringComparison.Ordinal);
        string openReadiness = interaction.GetProperty("openReadinessContract").GetString()!;
        Assert.Contains("15-second", openReadiness, StringComparison.Ordinal);
        Assert.Contains("120-second", openReadiness, StringComparison.Ordinal);
        Assert.Contains("at least two native late-update frames", openReadiness, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not relax readiness", openReadiness, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new[] { "set-input", "send", "toggle-player-portrait", "toggle-npc-portrait", "close-portrait", "show-pregnancy-warning" },
            interaction.GetProperty("individualChatActions").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Equal(new[] { "toggle-player-portrait", "toggle-npc-portrait", "close-portrait" },
            interaction.GetProperty("nativeConversationActions").EnumerateArray().Select(value => value.GetString()).ToArray());
        Assert.Contains("exact target opened by the current calibration session",
            interaction.GetProperty("safetyContract").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Continue command", interaction.GetProperty("safetyContract").GetString(), StringComparison.Ordinal);

        string uiHost = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionUiCalibrationHost.cs"));
        Assert.Contains("[\"governmentInput\"] = JObject.FromObject(ReignGovernmentScreenManager.AutomationInputSnapshot)", uiHost, StringComparison.Ordinal);
        Assert.Contains("[\"courtPetitionOpen\"] = ReignCourtPetitionScreenManager.IsOpen", uiHost, StringComparison.Ordinal);
        string nativeHost = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionNativeUiCalibrationHost.cs"));
        string portraitMixin = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Portraits", "AIPortraits", "ConversationPortraitMixin.cs"));
        string individualManager = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Dialogue", "UI", "ReignIndividualChatScreenManager.cs"));
        string calibrationService = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "UI", "UI", "Calibration", "ReignUiCalibrationService.cs"));
        string guide = TestingDocumentation.Read(Path.Combine(root, "docs", "agent", "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(root, "ReignMcp", "docs", "security-model.md"));
        int leaseAcquireIndex = uiHost.IndexOf(
            "ReignUiCalibrationService.AcquireAutomationLease(UiCalibrationAutomationLeaseOwner)",
            StringComparison.Ordinal);
        int nativeRouteIndex = uiHost.IndexOf("if (IsNativeUiTarget(target))", StringComparison.Ordinal);
        Assert.True(leaseAcquireIndex >= 0 && nativeRouteIndex > leaseAcquireIndex,
            "The owner-scoped calibration automation lease must be active before native UI routing.");
        Assert.Contains("if (!keepAutomationLease)", uiHost, StringComparison.Ordinal);
        Assert.Contains("await ReignMainThread.InvokeAsync(CloseNativeUiCalibrationTarget)", uiHost,
            StringComparison.Ordinal);
        Assert.Contains("ReleaseAutomationLease(UiCalibrationAutomationLeaseOwner)", uiHost, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_uiCalibrationTarget, target", uiHost, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(requested)", uiHost, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(requested, _uiCalibrationTarget", uiHost,
            StringComparison.Ordinal);
        Assert.Contains("!string.Equals(activeTarget, _nativeUiCalibrationTarget", uiHost,
            StringComparison.Ordinal);
        Assert.DoesNotContain("NativeUiMovieForTarget(requested)", uiHost, StringComparison.Ordinal);
        Assert.Contains("individualChatCalibrationFixture", uiHost, StringComparison.Ordinal);
        Assert.Contains("int openDeadlineSeconds = target == \"war-council\" ? 120 : 15;", uiHost,
            StringComparison.Ordinal);
        Assert.Contains("ReignWarCouncilMapInputWidget.AutomationLateUpdateCount >= 2", uiHost,
            StringComparison.Ordinal);
        Assert.Contains("Open readiness: ordinary standalone targets use a bounded 15-second", program,
            StringComparison.Ordinal);
        Assert.Contains("War Council alone uses 120 seconds", program, StringComparison.Ordinal);
        Assert.Contains("_nativeUiCalibrationConversationToken", nativeHost, StringComparison.Ordinal);
        Assert.Contains("nativeConversationCalibrationBound", nativeHost, StringComparison.Ordinal);
        Assert.Contains("bool exactCalibrationConversation", nativeHost, StringComparison.Ordinal);
        Assert.Contains("if (exactCalibrationConversation", nativeHost, StringComparison.Ordinal);
        Assert.Contains("ConversationPortraitMixin.ResetAutomationProbeFromPatch(conversationToken)", nativeHost,
            StringComparison.Ordinal);
        int exactConversationIndex = nativeHost.IndexOf("bool exactCalibrationConversation", StringComparison.Ordinal);
        int guardedConversationCloseIndex = nativeHost.IndexOf("if (exactCalibrationConversation",
            exactConversationIndex, StringComparison.Ordinal);
        int endConversationIndex = nativeHost.IndexOf("EndConversation()", guardedConversationCloseIndex,
            StringComparison.Ordinal);
        int closeFinallyIndex = nativeHost.IndexOf("finally", endConversationIndex, StringComparison.Ordinal);
        int probeResetIndex = nativeHost.IndexOf(
            "ConversationPortraitMixin.ResetAutomationProbeFromPatch(conversationToken)",
            closeFinallyIndex, StringComparison.Ordinal);
        Assert.True(exactConversationIndex >= 0
            && guardedConversationCloseIndex > exactConversationIndex
            && endConversationIndex > guardedConversationCloseIndex,
            "Native Conversation may end only beneath the exact calibration-token guard.");
        Assert.True(closeFinallyIndex > endConversationIndex && probeResetIndex > closeFinallyIndex,
            "Token-specific native Conversation probe cleanup must execute from finally.");
        Assert.Contains("BeginAutomationProbeFromPatch", portraitMixin, StringComparison.Ordinal);
        Assert.Contains("ResetAutomationProbeFromPatch(string expectedToken)", portraitMixin, StringComparison.Ordinal);
        Assert.Contains("string.Equals(_activeMixin?._automationProbeToken, expectedToken", portraitMixin,
            StringComparison.Ordinal);
        Assert.DoesNotContain("case \"continue\"", portraitMixin, StringComparison.Ordinal);
        Assert.Contains("AutomationCalibrationFixture", individualManager, StringComparison.Ordinal);
        Assert.Contains("TryCloseAutomationCalibrationFixture", individualManager, StringComparison.Ordinal);
        Assert.Contains("if (!IsOpen || !AutomationCalibrationFixture) return false;", individualManager,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ReignIndividualChatScreenManager.Close()", uiHost, StringComparison.Ordinal);
        Assert.Contains("AutomationLeaseOwners.Count > 0", calibrationService, StringComparison.Ordinal);
        Assert.Contains("AcquireAutomationLease", calibrationService, StringComparison.Ordinal);
        Assert.Contains("ReleaseAutomationLease", calibrationService, StringComparison.Ordinal);
        Assert.Contains("TryAuthorizeHeadlessNativeMovie", calibrationService, StringComparison.Ordinal);
        Assert.Contains("ClearAuthorizedHeadlessNativeMovie", calibrationService, StringComparison.Ordinal);
        Assert.Contains("\"SPConversation\"", calibrationService, StringComparison.Ordinal);
        Assert.Contains("Every ui-action and ui-snapshot requires the exact target",
            interaction.GetProperty("safetyContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("owner-scoped automation lease", interaction.GetProperty("safetyContract").GetString(),
            StringComparison.Ordinal);
        Assert.Contains("ui-snapshot` must name the exact active calibration target", guide,
            StringComparison.Ordinal);
        Assert.Contains("owner-scoped automation lease", security, StringComparison.Ordinal);
        Assert.Contains("bounded 15-second native open-and-layout readiness deadline", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("War Council alone uses a bounded 120-second deadline", guide,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("at least two native late-update frames", security,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("visible native mouse and keyboard acceptance", interaction.GetProperty("safetyContract").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignAdvancementRecoversOnlyNativeMapEscapeMenu()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionPassiveWorldHost.cs"));
        using JsonDocument catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string guide = TestingDocumentation.Read(Path.Combine(workspace, "docs", "agent",
            "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "docs",
            "security-model.md"));

        Assert.Contains("IsEscapeMenuOpened", source, StringComparison.Ordinal);
        Assert.Contains("CloseEscapeMenu", source, StringComparison.Ordinal);
        Assert.Contains("escapeMenuRecoveryCount", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SendInput", source, StringComparison.Ordinal);
        string contract = catalog.RootElement.GetProperty("campaignTest")
            .GetProperty("passiveWorldEscapeMenuRecovery").GetString()!;
        Assert.Contains("already-open native MapScreen Escape menu", contract,
            StringComparison.Ordinal);
        Assert.Contains("No synthetic input", contract, StringComparison.Ordinal);
        Assert.Contains("escapeMenuRecoveryCount", guide, StringComparison.Ordinal);
        Assert.Contains("sends no synthetic input", security,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CampaignCheckpointRecoversNativeMapEscapeMenuBeforeSaveAs()
    {
        string workspace = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionTestHost.cs"));
        using JsonDocument catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string guide = TestingDocumentation.Read(Path.Combine(workspace, "docs", "agent",
            "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "docs",
            "security-model.md"));

        Assert.Contains("CloseNativeMapEscapeMenuIfOpen", source, StringComparison.Ordinal);
        Assert.Contains("escapeMenuRecovered", source, StringComparison.Ordinal);
        Assert.Contains("before SaveAs", source, StringComparison.Ordinal);
        string contract = catalog.RootElement.GetProperty("campaignTest")
            .GetProperty("checkpointEscapeMenuRecovery").GetString()!;
        Assert.Contains("immediately before any guarded native checkpoint", contract,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No synthetic input", contract, StringComparison.Ordinal);
        Assert.Contains("escapeMenuRecovered", guide, StringComparison.Ordinal);
        Assert.Contains("Guarded checkpoint and arm saves", security,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CampaignCheckpointDocumentsManualConversationSafetyAndEvidence()
    {
        string workspace = TestOptions.FindWorkspace();
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("campaignTest")
            .GetProperty("checkpointVisiblePartyConversation").GetString()!;
        string guide = TestingDocumentation.Read(Path.Combine(workspace, "docs", "agent", "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "docs", "security-model.md"));
        foreach (string required in new[] { "Family Chambers", "nativeSaveQueued=false", "visiblePartyConversation" })
        {
            Assert.Contains(required, contract, StringComparison.Ordinal);
            Assert.Contains(required, guide, StringComparison.Ordinal);
            Assert.Contains(required, security, StringComparison.Ordinal);
        }
        Assert.Contains("Busy, changed, or unsuccessfully finished", contract, StringComparison.Ordinal);
        Assert.Contains("in-flight response is preserved", guide, StringComparison.Ordinal);
        Assert.Contains("Never treat a native-start timeout as cancellation", guide, StringComparison.Ordinal);
    }

    [Fact]
    public void FamilyUiSetupDocumentsNestedEnrollmentClockIsolation()
    {
        string workspace = TestOptions.FindWorkspace();
        string catalog = File.ReadAllText(Path.Combine(workspace, "reign.testing.json"));
        string guide = TestingDocumentation.Read(Path.Combine(workspace, "docs", "agent", "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "docs", "security-model.md"));
        foreach (string text in new[] { catalog, guide, security })
        {
            Assert.Contains("family-chambers", text, StringComparison.Ordinal);
            Assert.Contains("docketClock", text, StringComparison.Ordinal);
            Assert.Contains("disposableSaveName", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CampaignEnrollmentCreatesDescriptiveRunOwnedSaveAndPreservesBaseline()
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-campaign-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            File.Copy(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"),
                Path.Combine(temporary, "reign.testing.json"));
            ReignMcpOptions options = TestOptions.Create(temporary);
            var redactor = new SensitiveDataRedactor();
            using var http = new HttpClient(new OfflineHandler()) { BaseAddress = options.ServerBaseUri };
            var service = new CampaignTestService(options,
                new ReignProcessRunner(options, redactor),
                new ReignApiClient(http, options, redactor),
                new TestingCatalogService(options));

            JsonElement result = JsonSerializer.SerializeToElement(service.Prepare(
                "campaignA", "main", "Base One", "run-123", "War Economy Year One"),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            JsonElement state = result.GetProperty("state");
            Assert.Equal("Base One", state.GetProperty("baselineSaveName").GetString());
            Assert.StartsWith("ReignTest_WarEconomyYearOne_", state.GetProperty("currentSaveName").GetString());
            Assert.EndsWith("_Current", state.GetProperty("currentSaveName").GetString());
            Assert.True(state.GetProperty("currentSaveName").GetString()!.Length <= 64);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void CampaignCheckpointRestoreAllowsUnsafeOwnedPreStateButRequiresSafeReload()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignMcp",
            "src", "Reign.Mcp.Server", "Modules", "Platform", "CampaignTestTools.cs"));

        Assert.Contains("ValidateSnapshot(state, activeSnapshot, allowBaseline: false,\n            requireSafeSettlement: false);",
            source, StringComparison.Ordinal);
        Assert.Contains("if (!snapshot.FindBoolean(\"safeSettlement\"))", source,
            StringComparison.Ordinal);
        Assert.Contains("if (requireSafeSettlement && !snapshot.FindBoolean(\"safeSettlement\"))",
            source, StringComparison.Ordinal);
        Assert.Contains("\"--no-save\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptedCampaignAdvancePublishesExactOwnershipAndRecoversWithoutSaving()
    {
        string workspace = TestOptions.FindWorkspace();
        string nativeHost = File.ReadAllText(Path.Combine(workspace, "ReignBeta", "src",
            "Modules", "WorldSimulation", "Campaign", "ReignLiveInteractionTestHost.cs"));
        string campaignTools = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "src",
            "Reign.Mcp.Server", "Modules", "Platform", "CampaignTestTools.cs"));
        string processRunner = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "src",
            "Reign.Mcp.Server", "Modules", "Platform", "ReignProcessRunner.cs"));
        using JsonDocument catalog = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        string guide = TestingDocumentation.Read(Path.Combine(workspace, "docs", "agent",
            "TESTING_TOOL_GUIDE.md"));
        string security = File.ReadAllText(Path.Combine(workspace, "ReignMcp", "docs",
            "security-model.md"));

        int earlyActivation = nativeHost.IndexOf(
            "if (operation.Equals(\"world_advance\"", StringComparison.Ordinal);
        int execution = nativeHost.IndexOf(
            "result = await ExecuteCommandAsync(command)", StringComparison.Ordinal);
        Assert.True(earlyActivation >= 0 && earlyActivation < execution,
            "world_advance must publish its exact active run before long-running execution.");
        Assert.Contains("ActivateHarnessRun(command);", nativeHost, StringComparison.Ordinal);
        Assert.Contains("RecoverInterruptedAdvanceAsync", campaignTools, StringComparison.Ordinal);
        Assert.Contains("/tests/live/run/cancel", campaignTools, StringComparison.Ordinal);
        Assert.Contains("activeAdvancePreemption", campaignTools, StringComparison.Ordinal);
        Assert.Contains("advance_failed_safe", campaignTools, StringComparison.Ordinal);
        Assert.Contains("advance_cancelled_safe", campaignTools, StringComparison.Ordinal);
        Assert.Contains("CancellationToken.None", campaignTools, StringComparison.Ordinal);
        Assert.Contains("non-passive-world run", campaignTools, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", processRunner,
            StringComparison.Ordinal);
        Assert.Contains("callerCancelled", processRunner, StringComparison.Ordinal);
        Assert.Contains("Kill(entireProcessTree: true)", processRunner,
            StringComparison.Ordinal);
        Assert.Contains("if (callerCancelled) throw;", processRunner,
            StringComparison.Ordinal);

        string contract = catalog.RootElement.GetProperty("campaignTest")
            .GetProperty("interruptedAdvanceRecovery").GetString()!;
        Assert.Contains("exact run id", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never saves", contract, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refuses to cancel unrelated feature modes", contract,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("advance_cancelled_safe", guide, StringComparison.Ordinal);
        Assert.Contains("cancellation-independent queue drain", security,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class OfflineHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("offline");
        }
    }
}
