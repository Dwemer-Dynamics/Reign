using System.Net;
using System.Text;
using System.Text.Json;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class GovernmentTestingToolsTests
{
    [Theory]
    [InlineData("decided", "reject", true)]
    [InlineData("decided", "accept", false)]
    [InlineData("execution_failed", "reject", false)]
    [InlineData("expired", "reject", false)]
    [InlineData("reconsideration", "reject", false)]
    [InlineData("", "reject", false)]
    public void OnlyRecordedRejectionIsALawfulAuthorizationRefusal(string status, string option, bool expected)
    {
        Assert.Equal(expected, Reign.Core.Contracts.Government.GovernmentBusinessRules
            .IsRecordedAuthorizationRefusal(status, option));
    }

    [Theory]
    [InlineData("preflight")]
    [InlineData("hearing_compose")]
    public async Task GovernmentToolRefusesAnUnarmedCampaignTestEnrollment(string profile)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "reign-government-test-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        File.Copy(Path.Combine(TestOptions.FindWorkspace(), "reign.testing.json"),
            Path.Combine(temporary, "reign.testing.json"));
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json")
        });
        ReignMcpOptions options = TestOptions.Create(temporary) with { AllowVerificationControl = true };
        var redactor = new SensitiveDataRedactor();
        using var http = new HttpClient(handler) { BaseAddress = options.ServerBaseUri };
        var api = new ReignApiClient(http, options, redactor);
        var campaignTests = new CampaignTestService(options,
            new ReignProcessRunner(options, redactor), api, new TestingCatalogService(options));
        campaignTests.Prepare("campaign_test", "timeline_test", "BaselineSave", "government-run",
            "Government Feature Acceptance");

        try
        {
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                TestingTools.StartGovernmentTest(api, options, campaignTests,
                    campaignId: "campaign_test", campaignTestRunId: "government-run",
                    profile: profile, fixtureRunId: "government_fixture",
                    businessId: profile == "hearing_compose" ? "government_business_test" : "",
                    playerMessage: profile == "hearing_compose" ? "What are the terms?" : "",
                    expectedFeatureFingerprint: "", previousGameInstanceId: "",
                    confirmation: "start Reign government test on disposable save",
                    cancellationToken: CancellationToken.None));
            Assert.Contains("not armed", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public void GovernmentManifestAndHarnessCoverTheApprovedSystem()
    {
        string workspace = TestOptions.FindWorkspace();
        string manifestPath = Path.Combine(workspace, "ReignBetaServer", "ReignLiveTest",
            "scenarios", "government-system-manifest.json");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        JsonElement root = manifest.RootElement;
        Assert.False(root.GetProperty("preparedOnly").GetBoolean());
        Assert.True(root.GetProperty("requiresCampaignTestRunId").GetBoolean());
        Assert.Equal(16, root.GetProperty("profiles").GetArrayLength());
        Assert.Contains(root.GetProperty("profiles").EnumerateArray(), value => value.GetString() == "hearing_compose");
        Assert.Equal(104, root.GetProperty("contracts").GetProperty("resolutionTemplateCount").GetInt32());
        JsonElement hearing = root.GetProperty("contracts").GetProperty("hearingIntegration");
        Assert.Equal(7, hearing.GetProperty("recessDays").GetInt32());
        Assert.Equal(1, hearing.GetProperty("maximumPostponements").GetInt32());
        Assert.Equal(21, hearing.GetProperty("unsponsoredPetitionExpiryDays").GetInt32());
        Assert.True(hearing.GetProperty("absenteeVoting").GetBoolean());
        Assert.Contains("binding individual vote", hearing.GetProperty("levelFive").GetString());
        Assert.Contains("accepted scoped individual-chat", hearing.GetProperty("privateLobbying").GetString());
        Assert.Contains("dated spymaster snapshots", hearing.GetProperty("relationshipVisibility").GetString());
        Assert.Contains("inert", hearing.GetProperty("governmentTrust").GetString());
        Assert.Contains("ClassifyWorldAction", hearing.GetProperty("constitutionalWorldActionScope").GetString());
        Assert.Contains("Only a new scoped receipt counts", hearing.GetProperty("privateLobbyCertification").GetString());
        using JsonDocument testingCatalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "reign.testing.json")));
        JsonElement governmentCatalog = testingCatalog.RootElement.GetProperty("government");
        Assert.Contains("Level five", governmentCatalog.GetProperty("hearingIntegrationContract").GetString());
        Assert.Contains("GovernmentBusinessRules", governmentCatalog.GetProperty("hearingDeterministicVerification").GetString());
        Assert.Equal(-70, root.GetProperty("contracts").GetProperty("forcedFiveToFour")
            .GetProperty("settlementLoyalty").GetInt32());
        Assert.Equal(-70, root.GetProperty("contracts").GetProperty("forcedFiveToFour")
            .GetProperty("nonRulingLandholdingClanLeaderRelation").GetInt32());
        Assert.Equal(-40, root.GetProperty("contracts").GetProperty("forcedFiveToFour")
            .GetProperty("nonLandholdingDissenterRelation").GetInt32());
        Assert.True(root.GetProperty("contracts").GetProperty("reductionConsent")
            .GetProperty("playerClanAlwaysExempt").GetBoolean());
        Assert.True(root.GetProperty("contracts").GetProperty("reductionConsent")
            .GetProperty("consumedOnAuthorityChange").GetBoolean());
        Assert.Equal(17, root.GetProperty("nativeAcceptance").GetArrayLength());
        JsonElement certification = root.GetProperty("releaseCertification");
        Assert.Equal("reign-government-release-certification-v1",
            certification.GetProperty("schema").GetString());
        Assert.Contains("hidden mechanical state",
            certification.GetProperty("dialogueLanguageContract").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(36, certification.GetProperty("naturalLanguageCases").GetArrayLength());
        JsonElement memoryIsolation = certification.GetProperty("naturalLanguageMemoryIsolation");
        Assert.True(memoryIsolation.GetProperty("required").GetBoolean());
        Assert.True(memoryIsolation.GetProperty("neverForcesOutcome").GetBoolean());
        Assert.Equal("prior_government_certification_dialogue_only",
            memoryIsolation.GetProperty("scope").GetString());
        JsonElement languageCases = certification.GetProperty("naturalLanguageCases");
        Assert.Equal(12, languageCases.EnumerateArray().Count(testCase =>
            testCase.GetProperty("fixture").GetString()?.StartsWith(
                "supportive_", StringComparison.Ordinal) == true
            && testCase.GetProperty("followUpWhenNoConsent").GetBoolean()
            && testCase.TryGetProperty("followUpText", out JsonElement followUp)
            && followUp.GetString()?.Contains("small", StringComparison.OrdinalIgnoreCase) == true
            && followUp.GetString()?.Contains("limited", StringComparison.OrdinalIgnoreCase) == true));
        Assert.Equal(6, languageCases.EnumerateArray().Count(testCase =>
            testCase.GetProperty("fixture").GetString() == "supportive_clan_leader"
            && testCase.TryGetProperty("followUpText", out JsonElement followUp)
            && followUp.GetString()?.Contains("next fief", StringComparison.OrdinalIgnoreCase) == true
            && followUp.GetString()?.Contains("limited reduction", StringComparison.OrdinalIgnoreCase) == true));
        Assert.All(languageCases.EnumerateArray(), testCase =>
        {
            Assert.DoesNotContain("level", testCase.GetProperty("playerText").GetString(),
                StringComparison.OrdinalIgnoreCase);
            if (testCase.TryGetProperty("followUpText", out JsonElement followUp))
                Assert.DoesNotContain("level", followUp.GetString(),
                    StringComparison.OrdinalIgnoreCase);
        });
        string dialogueSource = File.ReadAllText(Path.Combine(workspace, "ReignBetaServer", "src",
            "Modules", "Dialogue", "MotiveAwareConversation.cs"));
        Assert.Contains("Government authority levels are hidden mechanics", dialogueSource,
            StringComparison.Ordinal);
        Assert.Contains("numbered levels/scale; describe small/modest/limited control change", dialogueSource,
            StringComparison.Ordinal);
        Assert.DoesNotContain(languageCases.EnumerateArray(), testCase =>
            testCase.GetProperty("fixture").GetString()?.StartsWith(
                "supportive_", StringComparison.Ordinal) != true
            && testCase.TryGetProperty("followUpText", out _));
        Assert.Equal(6, certification.GetProperty("deterministicCases").GetArrayLength());
        JsonElement nativeCases = certification.GetProperty("nativeCases");
        Assert.Equal(43, nativeCases.GetArrayLength());
        Assert.All(nativeCases.EnumerateArray(), testCase =>
        {
            Assert.True(testCase.TryGetProperty("requiredAssertions", out JsonElement assertions));
            Assert.Equal(JsonValueKind.Array, assertions.ValueKind);
            Assert.NotEmpty(assertions.EnumerateArray());
        });
        Assert.Contains(nativeCases.EnumerateArray(), testCase =>
            testCase.GetProperty("id").GetString() == "GOV-NATIVE-022"
            && testCase.GetProperty("profile").GetString() == "save_prepare"
            && testCase.GetProperty("requiredAssertions").EnumerateArray()
                .Any(value => value.GetString() == "government_save_prepare_consent_tags"));
        Assert.Contains(nativeCases.EnumerateArray(), testCase =>
            testCase.GetProperty("id").GetString() == "GOV-NATIVE-035"
            && testCase.GetProperty("profile").GetString() == "save_verify"
            && testCase.GetProperty("requiredAssertions").EnumerateArray()
                .Any(value => value.GetString() == "government_save_verify_consent_tags"));
        string[] institutionVariants =
        {
            "empire", "vlandia", "sturgia", "battania",
            "aserai", "khuzait", "nord", "fallback"
        };
        Assert.All(institutionVariants, variant => Assert.Contains(nativeCases.EnumerateArray(), testCase =>
            testCase.GetProperty("kind").GetString() == "culture_institution"
            && testCase.GetProperty("variant").GetString() == variant
            && testCase.GetProperty("requiredAssertions").EnumerateArray()
                .Any(value => value.GetString() == "government_native_culture_institution_matrix")));
        Assert.Equal(1d, certification.GetProperty("thresholds")
            .GetProperty("safetyAndActionCorrectness").GetDouble());
        Assert.Equal(0.95d, certification.GetProperty("thresholds")
            .GetProperty("naturalLanguageContextQuality").GetDouble());
        Assert.Equal(0, certification.GetProperty("thresholds")
            .GetProperty("falsePositiveConsentTolerance").GetInt32());

        string host = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionGovernmentHost.cs", "/src/Modules/Government/"));
        string tests = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignGovernmentInGameTests.cs", "/src/Modules/Government/"));
        string certificationTests = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignGovernmentCertificationTests.cs", "/src/Modules/Government/"));
        string nativeDecisions = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignGovernmentNativeDecisions.cs", "/src/Modules/Government/"));
        string liveHost = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBeta"),
            "ReignLiveInteractionTestHost.cs", "/src/Modules/WorldSimulation/"));
        string serverLive = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignBetaServer"),
            "LiveInteractionTest.cs", "/src/Modules/WorldSimulation/"));
        string serverProgram = File.ReadAllText(Path.Combine(workspace,
            "ReignBetaServer", "src", "Modules", "Platform", "Program.cs"));
        string mcp = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(workspace, "ReignMcp"),
            "TestingTools.Government.cs"));
        string certificationMcp = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(workspace, "ReignMcp"), "TestingTools.GovernmentCertification.cs"));
        Assert.Contains("\"government\"", liveHost, StringComparison.Ordinal);
        Assert.Contains("\"government_test\"", liveHost, StringComparison.Ordinal);
        Assert.Contains("\"government\"", serverLive, StringComparison.Ordinal);
        Assert.Contains("\"government_test\"", serverLive, StringComparison.Ordinal);
        Assert.Contains("RunGovernmentTestProfile", host, StringComparison.Ordinal);
        Assert.Contains("GovernmentFeatureFingerprint", tests, StringComparison.Ordinal);
        Assert.Contains("government_weighted_starting_levels", tests, StringComparison.Ordinal);
        Assert.Contains("government_104_tangible_two_route_resolutions", tests, StringComparison.Ordinal);
        Assert.Contains("government_reduction_vote_every_member_relation", tests, StringComparison.Ordinal);
        Assert.Contains("government_people_and_noble_pressure_separate", tests, StringComparison.Ordinal);
        Assert.Contains("government_public_governing_coalition", tests, StringComparison.Ordinal);
        Assert.Contains("government_reign_political_action_coverage", tests, StringComparison.Ordinal);
        Assert.Contains("government_reduction_consent_scopes", tests, StringComparison.Ordinal);
        Assert.Contains("government_save_prepare_consent_tags", tests, StringComparison.Ordinal);
        Assert.Contains("government_save_verify_consent_tags", tests, StringComparison.Ordinal);
        Assert.Contains("government_native_culture_institution_matrix", certificationTests, StringComparison.Ordinal);
        Assert.Contains("ReductionConsentHeroIdsCsv", tests, StringComparison.Ordinal);
        Assert.Contains("ReductionConsentClanIdsCsv", tests, StringComparison.Ordinal);
        Assert.Contains("TryCaptureNativeDecision", nativeDecisions, StringComparison.Ordinal);
        Assert.Contains("kingdom.RemoveDecision(decision)", nativeDecisions, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", mcp, StringComparison.Ordinal);
        Assert.Contains("expectedSaveName", mcp, StringComparison.Ordinal);
        Assert.Contains("start Reign government test on disposable save", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("save_checkpoint", mcp, StringComparison.Ordinal);
        Assert.DoesNotContain("advance_time", mcp, StringComparison.Ordinal);
        Assert.Contains("PrepareGovernmentCertification", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("naturalLanguageContextQuality", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("falsePositiveConsentCount", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("directDialogueActionBypassCount", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("followUpText", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("naturalLanguageFollowUp", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("skipWhenPriorDialogueActionQueued", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("governmentCertificationMemoryIsolation = true", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("GovernmentCertificationMemoryIsolationDirective", certificationMcp,
            StringComparison.Ordinal);
        const string directivePattern =
            "GovernmentCertificationMemoryIsolationDirective\\s*=\\s*\"([^\"]+)\";";
        string mcpDirective = System.Text.RegularExpressions.Regex.Match(
            certificationMcp, directivePattern).Groups[1].Value;
        string serverDirective = System.Text.RegularExpressions.Regex.Match(
            serverProgram, directivePattern).Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(mcpDirective));
        Assert.Equal(serverDirective, mcpDirective);
        Assert.Contains("This instruction is outcome-neutral", mcpDirective,
            StringComparison.Ordinal);
        Assert.Contains("GovernmentLiveRunId(runId, instance, attempt)", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("soakPreparationLiveRunId", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("RequireGovernmentSoakPreparation", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("TryReadRetainedGovernmentSoakFixture", certificationTests,
            StringComparison.Ordinal);
        Assert.Contains("restoredFromRetainedEvidence", certificationTests,
            StringComparison.Ordinal);
        Assert.Contains("-attempt-", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("governmentCertificationMemoryIsolation", liveHost,
            StringComparison.Ordinal);
        Assert.Contains("authorized_government_certification_memory_isolation", serverProgram,
            StringComparison.Ordinal);
        Assert.Contains("recalled out-of-session certification material is excluded",
            serverProgram, StringComparison.Ordinal);
        Assert.Contains("governmentCertificationMemoryIsolationPassed", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("reign_carry_forward_government_passes", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("GovernmentNaturalLanguageCoreAssertionsPassed", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("ContainsGovernmentNaturalLanguageCaseSend", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("government_hidden_language_and_release_recovery_only",
            certificationMcp, StringComparison.Ordinal);
        Assert.Contains("government_lifecycle_prune_evidence_only",
            certificationMcp, StringComparison.Ordinal);
        Assert.Contains("government_consent_persistence_harness_only",
            certificationMcp, StringComparison.Ordinal);
        Assert.Contains("GOV-NATIVE-035", certificationMcp,
            StringComparison.Ordinal);
        Assert.Contains("CarriedEvidence", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("ReportSha256", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("durableReportPresent", certificationMcp, StringComparison.Ordinal);
        Assert.Contains("SkipConditionalLiveTestCommandsWithSatisfiedDialogueActions", serverLive,
            StringComparison.Ordinal);
        Assert.Contains("turnIndex = 1", certificationMcp, StringComparison.Ordinal);
        Assert.DoesNotContain("save_checkpoint", certificationMcp, StringComparison.Ordinal);
        Assert.DoesNotContain("advance_time", certificationMcp, StringComparison.Ordinal);
    }

    [Fact]
    public void VerificationBundlePackagesGovernmentHarnessSafetyInputs()
    {
        string workspace = TestOptions.FindWorkspace();
        string project = File.ReadAllText(Path.Combine(workspace, "ReignBetaServer",
            "ReignBetaServer.csproj"));

        Assert.Contains("VerificationLiveTestScenario Include=", project,
            StringComparison.Ordinal);
        Assert.Contains("ReignLiveTest\\scenarios\\%(RecursiveDir)%(Filename)%(Extension)",
            project, StringComparison.Ordinal);
        Assert.Contains("VerificationGovernmentMcpSource Include=", project,
            StringComparison.Ordinal);
        Assert.Contains("ReignMcp\\src\\Reign.Mcp.Server\\Modules\\Government",
            project, StringComparison.Ordinal);
        Assert.Contains("<RemoveDir Directories=\"$(VerificationWorkspace)\" />", project,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("hearing_compose", "", "What are the terms?")]
    [InlineData("hearing_compose", "government_business_test", " ")]
    [InlineData("snapshot", "government_business_test", "What are the terms?")]
    public async Task HearingComposerRejectsInvalidScopeBeforeRuntimeAccess(string profile, string businessId, string message)
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace()) with { AllowVerificationControl = true };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => TestingTools.StartGovernmentTest(
            null!, options, null!, "campaign_test", "government-run", profile: profile,
            businessId: businessId, playerMessage: message,
            confirmation: "start Reign government test on disposable save"));
    }

    [Fact]
    public async Task HearingComposerRejectsOversizedTextBeforeRuntimeAccess()
    {
        var options = TestOptions.Create(TestOptions.FindWorkspace()) with { AllowVerificationControl = true };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => TestingTools.StartGovernmentTest(
            null!, options, null!, "campaign_test", "government-run", profile: "hearing_compose",
            businessId: "government_business_test", playerMessage: new string('x', 1201),
            confirmation: "start Reign government test on disposable save"));
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> response)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(response(request));
    }
}
