using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests;

public sealed class PartyAgencyHarnessContractTests
{
    [Theory]
    [InlineData("match", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("client_mismatch", "observed_metadata_only", "differs", "matches", true)]
    [InlineData("server_mismatch", "observed_metadata_only", "matches", "differs", true)]
    [InlineData("missing_licensed_observation", "observed_metadata_only", "matches", "matches", false)]
    [InlineData("licensed_mismatch", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("wrong_installed_approval", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("wrong_installed_client_hash", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("wrong_installed_save", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("wrong_installed_timeline", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("wrong_report_timeline", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("installed_before_approval", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("installed_after_lease", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("missing_issue_time", "observed_metadata_only", "matches", "matches", true)]
    [InlineData("forged_result_mvid", "observed_metadata_only", "unavailable", "matches", true)]
    [InlineData("no_witness", "no_approved_build_witness", null, null, false)]
    [InlineData("other_approved_run", "no_approved_build_witness", null, null, false)]
    [InlineData("foreign_enrollment", "report_scope_unavailable", null, null, false)]
    [InlineData("legacy_report", "report_scope_unavailable", null, null, false)]
    [InlineData("wrong_game", "no_bound_completed_send_observation", null, null, false)]
    [InlineData("superseded_retry", "no_bound_completed_send_observation", null, null, false)]
    [InlineData("wrong_ack", "no_bound_completed_send_observation", null, null, false)]
    [InlineData("wrong_transport", "no_bound_completed_send_observation", null, null, false)]
    [InlineData("duplicate_command", "no_bound_completed_send_observation", null, null, false)]
    [InlineData("too_many_commands", "report_command_limit_exceeded", null, null, false)]
    public void PartyAgencyArtifactDiagnosticsOnlyUsePinnedBuildAndOriginalIssuedMetadata(
        string mutation, string status, string? clientMatch, string? serverMatch, bool licensedObserved)
    {
        string hash = new('a', 64);
        Guid expectedClient = Guid.NewGuid(), expectedServer = Guid.NewGuid();
        Guid expectedCampaign = Guid.NewGuid(), expectedCore = Guid.NewGuid();
        var witness = new PartyAgencyApprovedArtifactWitness(PartyAgencyApprovedArtifactWitness.WitnessSchema,
            mutation == "other_approved_run" ? "other" : "certification",
            "campaign", "enrollment", "2026-11-24T06:00:00.0000000+00:00", hash, hash, hash, hash,
            hash, "client-report", hash, hash, "server-report", hash, hash,
            new PartyAgencyApprovedModule("ReignBeta", expectedClient, hash),
            new PartyAgencyApprovedModule("ReignServer", expectedServer, hash));
        var state = new PartyAgencyCertificationState
        {
            RunId = "certification", CampaignId = "campaign", CampaignTestRunId = "enrollment",
            DisposableSaveName = "Test_Current", TimelineId = "branch",
            ApprovedArtifactWitness = mutation == "no_witness" ? null : witness
        };
        var installed = new PartyAgencyInstalledArtifactWitness(PartyAgencyInstalledArtifactWitness.WitnessSchema,
            "certification", "campaign", "enrollment", "branch", "Test_Current", "2026-11-24T06:10:00.0000000+00:00",
            PartyAgencyApprovedArtifacts.ApprovalWitnessSha256(witness), hash, "20261124114300-1",
            witness.Client, witness.Server, new PartyAgencyApprovedModule("TaleWorlds.CampaignSystem", expectedCampaign, hash),
            new PartyAgencyApprovedModule("TaleWorlds.Core", expectedCore, hash));
        installed = mutation switch
        {
            "wrong_installed_approval" => installed with { ApprovedWitnessSha256 = new string('b', 64) },
            "wrong_installed_client_hash" => installed with { Client = installed.Client with { Sha256 = new string('b', 64) } },
            "wrong_installed_save" => installed with { DisposableSaveName = "Other_Current" },
            "wrong_installed_timeline" => installed with { TimelineId = "foreign" },
            "installed_before_approval" => installed with { CapturedUtc = "2026-11-24T05:55:00.0000000+00:00" },
            "installed_after_lease" => installed with { CapturedUtc = "2026-11-24T06:11:00.0000000+00:00" },
            _ => installed
        };
        string original = Guid.NewGuid().ToString("N"), transport = Guid.NewGuid().ToString("N");
        string clientProcess = Guid.NewGuid().ToString("N");
        var command = new
        {
            status = "completed", operation = "send", mode = "party_chat", commandId = "native-command",
            leaseId = transport, executionRetryGeneration = mutation == "superseded_retry" ? 2 : 1,
            executionEvidence = new
            {
                status = "pending_artifact_review",
                transportLeaseId = mutation == "wrong_transport" ? original : transport,
                executionObservation = new
                {
                    campaignId = "campaign", runId = "live-run", commandId = "native-command",
                    gameInstanceId = mutation == "wrong_game" ? "reloaded" : "enrolled-game",
                    leaseId = original, executionRetryGeneration = 1, clientProcessInstanceId = clientProcess,
                    observedUtc = mutation == "missing_issue_time" ? "" : "2026-11-24T06:10:30.0000000+00:00",
                    serverProcessInstanceId = Guid.NewGuid().ToString("N"),
                    serverModuleId = (mutation == "server_mismatch" ? Guid.NewGuid() : expectedServer).ToString("D"),
                    clientModuleIds = new Dictionary<string, string>
                    {
                        ["ReignBeta"] = mutation == "forged_result_mvid" ? ""
                            : (mutation == "client_mismatch" ? Guid.NewGuid() : expectedClient).ToString("D"),
                        ["TaleWorlds.CampaignSystem"] = (mutation == "licensed_mismatch" ? Guid.NewGuid() : expectedCampaign).ToString("D"),
                        ["TaleWorlds.Core"] = mutation == "missing_licensed_observation" ? "" : expectedCore.ToString("D")
                    }
                }
            },
            partyAgencyAcceptedSendExecution = new
            {
                leaseId = mutation == "wrong_ack" ? transport : original,
                clientProcessInstanceId = clientProcess,
                retryGeneration = 1
            },
            result = new { clientModuleIds = new { ReignBeta = expectedClient }, installedAndLoadedArtifactsAttested = true }
        };
        JsonElement report = JsonSerializer.SerializeToElement(new
        {
            mode = "party_agency", campaignId = "campaign", runId = "live-run", gameInstanceId = "enrolled-game",
            executionObservationVersion = mutation == "legacy_report" ? 0 : 1,
            commands = Enumerable.Repeat(command, mutation switch { "too_many_commands" => 129, "duplicate_command" => 2, _ => 1 }),
            enrollment = new
            {
                campaignId = "campaign", certificationRunId = "certification", disposableSaveName = "Test_Current",
                campaignTestRunId = mutation == "foreign_enrollment" ? "other" : "enrollment",
                timelineId = mutation == "wrong_report_timeline" ? "foreign" : "branch"
            }
        });

        var diagnostic = TestingTools.ProjectPartyAgencyOriginalModuleDiagnostics(state, "live-run", report);
        Assert.Equal(status, diagnostic["status"]);
        Assert.DoesNotContain("installedAndLoadedArtifactsAttested", diagnostic.Keys);
        var rows = Assert.IsType<List<Dictionary<string, object?>>>(diagnostic["commands"]);
        Assert.True(installed.MatchesInstallation(installed with { CapturedUtc = "2026-11-24T06:03:00.0000000+00:00" }));
        Assert.False(installed.MatchesInstallation(installed with { LinuxRuntimeVersion = "20261124060300-567" }));
        Assert.False(installed.MatchesInstallation(installed with { InstallationRecordSha256 = new string('c', 64) }));
        var installedDiagnostic = TestingTools.ProjectPartyAgencyOriginalModuleDiagnostics(state, "live-run", report, installed);
        Assert.DoesNotContain("installedAndLoadedArtifactsAttested", installedDiagnostic.Keys);
        var installedRows = Assert.IsType<List<Dictionary<string, object?>>>(installedDiagnostic["commands"]);
        if (clientMatch is null)
        {
            Assert.Empty(rows);
            Assert.Empty(installedRows);
            return;
        }
        var result = Assert.Single(rows);
        Assert.Equal(original, result["originalLeaseId"]);
        Assert.Equal(clientMatch, result["reignBetaMvid"]);
        Assert.Equal(serverMatch, result["reignServerMvid"]);
        Assert.Equal(licensedObserved, result["licensedMvidsObserved"]);

        if (mutation == "wrong_report_timeline")
        {
            Assert.Equal("report_scope_unavailable", installedDiagnostic["status"]);
            Assert.Empty(installedRows);
            return;
        }
        var installedRow = Assert.Single(installedRows);
        if (mutation is "wrong_installed_approval" or "wrong_installed_client_hash" or "wrong_installed_save"
            or "wrong_installed_timeline" or "installed_before_approval")
        {
            Assert.Equal("scope_or_approval_mismatch", installedDiagnostic["installedWitness"]);
            Assert.DoesNotContain("installedMvidComparisons", installedRow.Keys);
            return;
        }

        Assert.Equal("bound_to_approved_enrollment", installedDiagnostic["installedWitness"]);
        Assert.Equal("observed_metadata_only", installedDiagnostic["status"]);
        if (mutation is "installed_after_lease" or "missing_issue_time")
        {
            Assert.DoesNotContain("installedMvidComparisons", installedRow.Keys);
            return;
        }
        var comparisons = Assert.IsType<Dictionary<string, string>>(installedRow["installedMvidComparisons"]);
        Assert.Equal(4, comparisons.Count);
        Assert.Equal(clientMatch, comparisons["ReignBeta"]);
        Assert.Equal(serverMatch, comparisons["ReignServer"]);
        Assert.Equal(mutation == "licensed_mismatch" ? "differs" : "matches", comparisons["TaleWorlds.CampaignSystem"]);
        Assert.Equal(licensedObserved ? "matches" : "unavailable", comparisons["TaleWorlds.Core"]);
        Assert.Equal(clientMatch == "matches" && serverMatch == "matches" && licensedObserved
            && mutation != "licensed_mismatch", installedRow["allInstalledMvidsMatch"]);
    }

    [Fact]
    public void PartyAgencyApprovedArtifactMeasuresHashAndManagedIdentityFromTheSameBytes()
    {
        var assembly = typeof(PartyAgencyHarnessContractTests).Assembly;
        string name = assembly.GetName().Name!;
        string root = Path.Combine(Path.GetTempPath(), "reign-approved-artifact-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "approved-fixture.dll");
        Directory.CreateDirectory(root);
        try
        {
            File.Copy(assembly.Location, file);
            string originalHash = ReignValidationService.FileSha256(file);
            var approved = PartyAgencyApprovedArtifacts.ReadManagedArtifact(file, name, originalHash);
            Assert.Equal(new PartyAgencyApprovedModule(name, assembly.ManifestModule.ModuleVersionId, originalHash), approved);
            Assert.Equal(approved, PartyAgencyApprovedArtifacts.ReadManagedArtifact(file, name));
            Assert.Throws<InvalidDataException>(() =>
                PartyAgencyApprovedArtifacts.ReadManagedArtifact(file, "RenamedForeignAssembly", originalHash));

            // A trailing byte changes the approved binary without changing the managed manifest.
            using (var append = File.Open(file, FileMode.Append))
                append.WriteByte(0x2a);
            string changedHash = ReignValidationService.FileSha256(file);
            Assert.NotEqual(originalHash, changedHash);
            Assert.Equal(approved.Mvid,
                PartyAgencyApprovedArtifacts.ReadManagedArtifact(file, name, changedHash).Mvid);
            Assert.Throws<InvalidDataException>(() =>
                PartyAgencyApprovedArtifacts.ReadManagedArtifact(file, name, originalHash));
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(root);
        }
    }

    [Theory]
    [InlineData("exact")]
    [InlineData("other_candidate")]
    [InlineData("other_prior")]
    [InlineData("other_delta")]
    [InlineData("missing_path")]
    [InlineData("duplicate_path")]
    public void PartyAgencyApprovedArtifactsRequireTheExactRetainedReviewBinding(string mutation)
    {
        string candidateSha = new('1', 64), priorSha = new('2', 64), diffSha = new('3', 64);
        JsonElement candidate = JsonSerializer.SerializeToElement(new
        {
            Schema = "reign-deployment-authority-content-v1",
            PriorAuthoritySha256 = priorSha,
            DiffSha256 = diffSha,
            Diff = new[] { new { path = "client/ReignBeta.csproj" } }
        });
        string[] paths = mutation switch
        {
            "missing_path" => [],
            "duplicate_path" => ["client/ReignBeta.csproj", "client/ReignBeta.csproj"],
            _ => ["client/ReignBeta.csproj"]
        };
        JsonElement review = JsonSerializer.SerializeToElement(new
        {
            CandidateSha256 = mutation == "other_candidate" ? diffSha : candidateSha,
            PriorAuthoritySha256 = mutation == "other_prior" ? candidateSha : priorSha,
            DiffSha256 = mutation == "other_delta" ? candidateSha : diffSha,
            Reason = "Reviewed exact managed build sources.",
            ReviewedPaths = paths
        });

        if (mutation == "exact")
            PartyAgencyApprovedArtifacts.ValidateReviewBinding(candidate, review, candidateSha);
        else
            Assert.Throws<InvalidDataException>(() =>
                PartyAgencyApprovedArtifacts.ValidateReviewBinding(candidate, review, candidateSha));
    }

    [Fact]
    public void PartyAgencyApprovedArtifactWitnessRetainsItsRunScopeThroughSerialization()
    {
        string sha = new('a', 64);
        var witness = new PartyAgencyApprovedArtifactWitness(
            PartyAgencyApprovedArtifactWitness.WitnessSchema, "run", "campaign", "enrollment",
            "2026-11-24T15:00:00.0000000+00:00", sha, sha, sha, sha, sha,
            "20261124-150000-12345678", sha, sha, "20261124-150001-12345678", sha, sha,
            new("ReignBeta", Guid.NewGuid(), sha), new("ReignServer", Guid.NewGuid(), sha));
        var restored = JsonSerializer.Deserialize<PartyAgencyCertificationState>(
            JsonSerializer.Serialize(new PartyAgencyCertificationState { ApprovedArtifactWitness = witness }))!;
        Assert.Equal(witness, restored.ApprovedArtifactWitness);
        Assert.True(witness.BelongsTo("run", "campaign", "enrollment"));
        Assert.False(witness.BelongsTo("other-run", "campaign", "enrollment"));
        Assert.False(witness.BelongsTo("run", "other-campaign", "enrollment"));
        Assert.False(witness.BelongsTo("run", "campaign", "other-enrollment"));
        Assert.False((witness with { LinuxManifestSha256 = "release-label" })
            .BelongsTo("run", "campaign", "enrollment"));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("legacy")]
    [InlineData("foreign_campaign")]
    [InlineData("foreign_run")]
    [InlineData("duplicate_stage")]
    [InlineData("wrong_player_load")]
    [InlineData("wrong_lease")]
    [InlineData("superseded_execution")]
    [InlineData("wrong_phase")]
    [InlineData("wrong_native_command")]
    [InlineData("wrong_save")]
    public void PartyAgencyCheckpointRequiresOneExactAcceptedNativeStage(string mutation)
    {
        const string receipt = "party_agency_save_stage_fba930e74e0144a0917b3a91e2e579dd";
        var command = new
        {
            operation = "party_agency_stage_save_phase", status = "completed",
            commandId = "native-command", caseId = "PA-NATIVE-017", variant = "Active",
            executionRetryGeneration = mutation == "superseded_execution" ? 2 : 1,
            executionEvidence = new
            {
                status = "pending_artifact_review",
                executionObservation = new
                {
                    commandId = "native-command", campaignId = "target-campaign",
                    runId = "stage-run", gameInstanceId = "game-before",
                    leaseId = "original-execution-lease", executionRetryGeneration = 1
                }
            },
            result = new
            {
                ok = false, saveRoundtripStageReady = true, receiptId = receipt,
                operation = "party_agency_stage_save_phase", caseId = "PA-NATIVE-017", variant = "Active",
                activeSaveName = mutation == "wrong_save" ? "other-Current" : "Test_Current",
                stagedLiveRunId = "stage-run",
                stagedCommandId = mutation == "wrong_native_command" ? "other-command" : "native-command",
                stagedGameInstanceId = mutation == "wrong_player_load" ? "other-game" : "game-before",
                stagedExecutionLeaseId = mutation == "wrong_lease" ? "current-transport-lease" : "original-execution-lease",
                stagedRetryGeneration = 1,
                stagedAgreement = new
                {
                    agreementId = "exact-agreement", agreementRevision = 3, heroId = "fixture-guest",
                    originalClanId = "guest-clan", originalKingdomId = "", sourcePartyId = "guest-party",
                    phase = mutation == "wrong_phase" ? "Departing" : "Active"
                }
            }
        };
        JsonElement report = JsonSerializer.SerializeToElement(new
        {
            mode = "party_agency", campaignId = "target-campaign", runId = "stage-run", gameInstanceId = "game-before",
            executionObservationVersion = mutation == "legacy" ? 0 : 1,
            enrollment = new
            {
                campaignId = mutation == "foreign_campaign" ? "other-campaign" : "target-campaign",
                campaignTestRunId = "campaign-test", certificationRunId = mutation == "foreign_run" ? "other-run" : "cert-run",
                disposableSaveName = "Test_Current"
            },
            commands = mutation == "duplicate_stage" ? new[] { command, command } : new[] { command }
        });
        var observed = TestingTools.ReadPartyAgencyCheckpointStage(report, "target-campaign",
            "campaign-test", "cert-run", "stage-run", "Active", "Test_Current");
        if (mutation == "valid")
        {
            Assert.NotNull(observed);
            Assert.Equal("original-execution-lease", observed.ExecutionLeaseId);
            Assert.Equal("exact-agreement", observed.Agreement.AgreementId);
            Assert.Equal(3, observed.Agreement.AgreementRevision);
        }
        else
        {
            Assert.Null(observed);
        }
    }
    [Theory]
    [InlineData(true, true, 1, true, false, true)]
    [InlineData(true, true, 1, false, true, true)]
    [InlineData(true, true, 1, false, false, false)]
    [InlineData(false, true, 1, true, false, false)]
    [InlineData(true, false, 1, true, false, false)]
    [InlineData(true, false, 1, false, true, false)]
    [InlineData(true, true, 0, true, false, false)]
    [InlineData(true, true, 2, true, false, false)]
    [InlineData(true, true, -1, false, true, false)]
    public void GuestReleaseUsesNativeCustodyAuthority(bool prisoner, bool playerAvailable, int count,
        bool mainParty, bool clanDungeon, bool expected)
    {
        Assert.Equal(expected, ReignBeta.WorldSimulation.PartyAgency.ReignTemporaryGuestCustodyPolicy.CanRelease(
            prisoner, playerAvailable, count, mainParty, clanDungeon));
    }

    [Fact]
    public void PrisonerGuestTransferRequiresCustodyConsentNativeReleaseAndRollback()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "PartyAgency", "ReignTemporaryPartyGuestCampaignBehavior.cs"));
        string accept = source[source.IndexOf("public ReignActionResult AcceptGuest(", StringComparison.Ordinal)
            ..source.IndexOf("public bool ValidateAction(", StringComparison.Ordinal)];
        int validation = accept.IndexOf("if (!ValidateAcceptAction(action, hero, out string reason))", StringComparison.Ordinal);
        int deferred = accept.IndexOf("if (ReadBool(terms, \"deferStart\", false))", StringComparison.Ordinal);
        int release = accept.IndexOf("EndCaptivityAction.ApplyByReleasedByChoice(hero, Hero.MainHero)", StringComparison.Ordinal);
        int activate = accept.IndexOf("hero.ChangeState(Hero.CharacterStates.Active)", StringComparison.Ordinal);
        int transfer = accept.IndexOf("AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false)", StringComparison.Ordinal);
        int rollback = accept.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        int recapture = accept.IndexOf("TakePrisonerAction.Apply(priorCustody, hero)", StringComparison.Ordinal);
        Assert.True(validation >= 0 && deferred > validation && release > deferred && transfer > release);
        Assert.True(activate > release && transfer > activate);
        Assert.Contains("(priorCustody != null && !hero.IsActive)", accept, StringComparison.Ordinal);
        Assert.True(rollback > transfer && recapture > rollback);
        Assert.Contains("if (!CanReleasePrisonerForGuest(hero))", accept, StringComparison.Ordinal);
        Assert.Contains("priorCustody.PrisonRoster.GetTroopCount(hero.CharacterObject) != 0", accept, StringComparison.Ordinal);
        Assert.Contains("MemberRoster.GetTroopCount(hero.CharacterObject) != 1", accept, StringComparison.Ordinal);
        Assert.Contains("hero.PartyBelongedToAsPrisoner != priorCustody", accept, StringComparison.Ordinal);
        Assert.Contains("temporary_guest_custody_restore_failed", accept, StringComparison.Ordinal);

        string preflight = source[source.IndexOf("public bool ValidateAcceptAction(", StringComparison.Ordinal)
            ..source.IndexOf("public void ApplicationTick()", StringComparison.Ordinal)];
        Assert.Contains("hero.IsPrisoner && !releasablePrisoner", preflight, StringComparison.Ordinal);
        Assert.Contains("custody.Settlement?.IsUnderSiege == true", preflight, StringComparison.Ordinal);
        Assert.Contains("custody.MapEvent != null", preflight, StringComparison.Ordinal);
        Assert.Contains("IsCurrentlyUsedByAQuest == true", preflight, StringComparison.Ordinal);
        Assert.Contains("!ReadBool(terms, \"consentConfirmed\", false)", preflight, StringComparison.Ordinal);
        Assert.Contains("ReignRulerCampaignBehavior.BlocksRelease(hero)", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("EndCaptivityAction.", preflight, StringComparison.Ordinal);
        string custody = source[source.IndexOf("internal static bool CanReleasePrisonerForGuest(", StringComparison.Ordinal)
            ..source.IndexOf("private static bool IsInPerson(", StringComparison.Ordinal)];
        Assert.Contains("custody == PartyBase.MainParty", custody, StringComparison.Ordinal);
        Assert.Contains("custody.Settlement.OwnerClan == Clan.PlayerClan", custody, StringComparison.Ordinal);
        Assert.Contains("MobileParty.MainParty?.CurrentSettlement == custody.Settlement", custody, StringComparison.Ordinal);
        Assert.Contains("!Hero.MainHero.IsPrisoner", custody, StringComparison.Ordinal);
        Assert.Contains("ReignTemporaryGuestCustodyPolicy.CanRelease(", custody, StringComparison.Ordinal);
        Assert.Contains("custody.PrisonRoster.GetTroopCount(hero.CharacterObject)", custody, StringComparison.Ordinal);

        string resident = File.ReadAllText(Path.Combine(root, "ReignBeta", "src", "Modules", "Characters",
            "Campaign", "ReignEncounteredResidentRecruitment.cs"));
        Assert.Contains("bool releaseIntoGuestService = false", resident, StringComparison.Ordinal);
        Assert.Contains("ReignTemporaryPartyGuestCampaignBehavior.CanReleasePrisonerForGuest(hero)", resident, StringComparison.Ordinal);
        Assert.Contains("resident.Military && !resident.ReleasedFromDuty", resident, StringComparison.Ordinal);

        string fixture = TestSourceLocator.PartialTypeSourceText(Path.Combine(root, "ReignBeta", "src", "Modules", "WorldSimulation",
            "Campaign", "ReignLiveInteractionPartyAgencyFixtures.cs"));
        string prisonerFixture = fixture[fixture.IndexOf("case \"prisoner\":", StringComparison.Ordinal)
            ..fixture.IndexOf("case \"dead\":", StringComparison.Ordinal)];
        Assert.Contains("settlement.OwnerClan != Clan.PlayerClan", prisonerFixture, StringComparison.Ordinal);
        Assert.Contains("TakePrisonerAction.Apply(foreignDungeon.Party, target)", prisonerFixture, StringComparison.Ordinal);
        Assert.DoesNotContain("TakePrisonerAction.Apply(PartyBase.MainParty", prisonerFixture, StringComparison.Ordinal);
    }

    [Fact]
    public void ObservationalPolicyContractsMatchThePersistedGuestLifecycle()
    {
        string root = TestOptions.FindWorkspace();
        string guestRoot = Path.Combine(root, "ReignBeta", "src", "Modules", "WorldSimulation");
        string host = TestSourceLocator.PartialTypeSourceText(Path.Combine(guestRoot, "Campaign", "ReignLiveInteractionTestHost.cs"));
        string behavior = File.ReadAllText(Path.Combine(guestRoot, "PartyAgency", "ReignTemporaryPartyGuestCampaignBehavior.cs"));
        string record = File.ReadAllText(Path.Combine(guestRoot, "PartyAgency", "ReignTemporaryPartyGuestRecord.cs"));
        using JsonDocument catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string[] rules = catalog.RootElement.GetProperty("partyAgency").GetProperty("deterministicAssertions")
            .EnumerateArray().Select(rule => rule.GetString()!).ToArray();

        // Keep the native observation gate consistent with the shipped timing policy and save values.
        foreach ((string constant, string value) in new[]
        {
            ("OpenEndedReviewDays", "30"), ("MinimumFixedTermDays", "1"),
            ("MinimumReturnHours", "1"), ("MaximumReturnHours", "12")
        })
        {
            Assert.Contains($"{constant} = {value}f", behavior, StringComparison.Ordinal);
            Assert.Contains($"ReignTemporaryPartyGuestCampaignBehavior.{constant} == {value}f", host,
                StringComparison.Ordinal);
        }
        string calendar = File.ReadAllText(Path.Combine(guestRoot, "Runtime", "ReignCalendarService.cs"));
        Assert.Contains("DaysPerYear = 126d", calendar, StringComparison.Ordinal);
        Assert.Contains("MaximumFixedTermDays = (float)ReignCalendarService.DaysPerYear", behavior,
            StringComparison.Ordinal);
        Assert.Contains("ReignTemporaryPartyGuestCampaignBehavior.MaximumFixedTermDays == 126f", host,
            StringComparison.Ordinal);
        string[] phases = { "Active", "ReviewDue", "Departing", "Returning", "Completed", "HostileBanishmentPending", "AwaitingStart" };
        for (int index = 0; index < phases.Length; index++)
            Assert.Contains($"{phases[index]} = {index}", record, StringComparison.Ordinal);
        Assert.Contains($"Enum.GetValues(typeof(ReignTemporaryGuestPhase)).Length == {phases.Length}", host,
            StringComparison.Ordinal);
        Assert.Contains("fixedTermRangeIsOneToOneHundredTwentySixDays", host, StringComparison.Ordinal);
        Assert.DoesNotContain("fixedTermRangeIsOneToThirtyDays", host, StringComparison.Ordinal);
        Assert.Contains(rules, rule => rule == "open-ended review interval is exactly 30 campaign days");
        Assert.Contains(rules, rule => rule.Contains("1 through 126 campaign days", StringComparison.Ordinal));
        Assert.Contains(rules, rule => rule.Contains("all seven persistent lifecycle phases", StringComparison.Ordinal));
    }

    [Fact]
    public void GuestInvitationRelinquishesGovernorshipOnlyAfterConsentAndAtActualDeparture()
    {
        string source = File.ReadAllText(Path.Combine(TestOptions.FindWorkspace(), "ReignBeta", "src",
            "Modules", "WorldSimulation", "PartyAgency", "ReignTemporaryPartyGuestCampaignBehavior.cs"));
        string accept = source[source.IndexOf("public ReignActionResult AcceptGuest(", StringComparison.Ordinal)
            ..source.IndexOf("public bool ValidateAction(", StringComparison.Ordinal)];
        int validation = accept.IndexOf("if (!ValidateAcceptAction(action, hero, out string reason))", StringComparison.Ordinal);
        int deferred = accept.IndexOf("if (ReadBool(terms, \"deferStart\", false))", StringComparison.Ordinal);
        int removal = accept.IndexOf("ChangeGovernorAction.RemoveGovernorOf(hero)", StringComparison.Ordinal);
        int transfer = accept.IndexOf("AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false)", StringComparison.Ordinal);
        int rollback = accept.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        int restore = accept.IndexOf("ChangeGovernorAction.Apply(priorGovernorship, hero)", StringComparison.Ordinal);
        Assert.True(validation >= 0 && deferred > validation && removal > deferred && transfer > removal);
        Assert.True(rollback > transfer && restore > rollback);
        Assert.Contains("priorGovernorship.Governor == null && hero.GovernorOf == null", accept, StringComparison.Ordinal);
        Assert.Contains("hero.GovernorOf != null || priorGovernorship.Governor == hero", accept, StringComparison.Ordinal);
        Assert.Contains("IdentityMatches(record, hero)", accept, StringComparison.Ordinal);
        Assert.Contains("relinquishedGovernorship=", accept, StringComparison.Ordinal);
        string preflight = source[source.IndexOf("public bool ValidateAcceptAction(", StringComparison.Ordinal)
            ..source.IndexOf("public void ApplicationTick()", StringComparison.Ordinal)];
        Assert.DoesNotContain("hero.GovernorOf", preflight, StringComparison.Ordinal);
        Assert.DoesNotContain("ChangeGovernorAction.", preflight, StringComparison.Ordinal);
        Assert.Contains("!proposal && !ReignActionValidator.HasOperatorAuthorization(action)", preflight, StringComparison.Ordinal);
        Assert.Contains("!ReadBool(terms, \"consentConfirmed\", false)", preflight, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeReturnTimingReceiptSurvivesCompletedReturnAndUsesManifestVariants()
    {
        string root = TestOptions.FindWorkspace();
        string source = TestSourceLocator.PartialTypeSourceText(Path.Combine(root, "ReignBeta", "src", "Modules",
            "WorldSimulation", "Campaign", "ReignLiveInteractionPartyAgencyFixtures.cs"));

        Assert.Contains("return_due_observed_on_returning_transition", source,
            StringComparison.Ordinal);
        Assert.Contains("returningRecord.ReturnDueDay - currentDay", source,
            StringComparison.Ordinal);
        Assert.Contains("plannedReturnHoursBasis", source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyReturnVariantMatches(variant, \"min\", \"minimum\")",
            source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyReturnVariantMatches(variant, \"max\", \"maximum\")",
            source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("same", "utf8", true)]
    [InlineData("same", "utf8-bom", true)]
    [InlineData("same", "utf16-bom", true)]
    [InlineData("newline", "utf8", false)]
    [InlineData("bom", "utf8", false)]
    [InlineData("removed", "utf8", false)]
    [InlineData("swapped", "utf8", false)]
    [InlineData("legacy", "utf8", false)]
    public async Task PreparedCatalogGatesReadinessWithoutDiscardingHistoricalReports(
        string change, string initialEncoding, bool expectedMatch)
    {
        DirectoryInfo folder = Directory.CreateTempSubdirectory("reign-party-agency-catalog-");
        try
        {
            const string caseId = "PA-NATIVE-001";
            const string callerLabel = "caller-catalog-label";
            const string campaignId = "campaign";
            const string guardId = "owned-run";
            const string runId = "catalog-run";
            const string liveRun = "retained-live-run";
            string catalog = "{\n  \"cases\": [{ \"id\": \"PA-NATIVE-001\", \"required\": true }]\n}\n";
            Encoding encoding = initialEncoding switch
            {
                "utf8-bom" => new UTF8Encoding(true),
                "utf16-bom" => Encoding.Unicode,
                _ => new UTF8Encoding(false)
            };
            if (initialEncoding != "utf8")
                catalog = catalog.Replace("\n", "\r\n", StringComparison.Ordinal);
            byte[] preparedBytes = [.. encoding.GetPreamble(), .. encoding.GetBytes(catalog)];
            string catalogPath = Path.Combine(folder.FullName, "ReignServer", "tests", "ReignLiveTest",
                "scenarios", "party-agency-manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
            File.WriteAllBytes(catalogPath, preparedBytes);

            var options = TestOptions.Create(folder.FullName) with { AllowVerificationControl = true };
            var transport = new PartyAgencyCatalogReportHandler(liveRun);
            using var http = new HttpClient(transport) { BaseAddress = options.ServerBaseUri };
            var api = new ReignApiClient(http, options, new SensitiveDataRedactor());
            var campaigns = new CampaignTestService(options, null!, api, new TestingCatalogService(options));
            campaigns.Prepare(campaignId, "main", "Immutable", guardId, "Party Agency catalog");
            IReadOnlyDictionary<string, object?> Prepare() => TestingTools.PreparePartyAgencyTest(
                options, campaigns, null!, campaignId, guardId, runId,
                "source", "client", "server", "provider", callerLabel);
            var state = Assert.IsType<PartyAgencyCertificationState>(Prepare()["state"]);
            string digest = Convert.ToHexStringLower(SHA256.HashData(preparedBytes));
            Assert.Equal(digest, state.PreparedCatalogSha256);
            Assert.Equal(callerLabel, state.CatalogFingerprint);
            Assert.Equal(caseId, Assert.Single(state.RequiredInstances));
            Assert.Equal(digest, TestingTools.GetPartyAgencyTestManifest(options)["observedCatalogSha256"]);

            string fixtureHash = new('a', 64);
            state = state with
            {
                ApprovedArtifactWitness = new PartyAgencyApprovedArtifactWitness(
                    PartyAgencyApprovedArtifactWitness.WitnessSchema, runId, campaignId, guardId,
                    "2026-01-01T00:00:00.0000000Z", fixtureHash, fixtureHash, fixtureHash, fixtureHash,
                    fixtureHash, "windows-run", fixtureHash, fixtureHash, "linux-run", fixtureHash, fixtureHash,
                    new PartyAgencyApprovedModule("ReignBeta", Guid.NewGuid(), fixtureHash),
                    new PartyAgencyApprovedModule("ReignServer", Guid.NewGuid(), fixtureHash))
            };
            state.Executions.Add(new PartyAgencyExecution { CaseId = caseId, LiveRunId = liveRun, StartAccepted = true });
            string statePath = Path.Combine(folder.FullName, ".codex-live-artifacts", "party-agency", runId + ".json");
            JsonObject durable = JsonSerializer.SerializeToNode(state, new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
            if (change == "legacy")
                Assert.True(durable.Remove("preparedCatalogSha256"));
            File.WriteAllText(statePath, durable.ToJsonString());
            if (change == "newline")
                File.WriteAllText(catalogPath, catalog.Replace("\n", "\r\n", StringComparison.Ordinal), new UTF8Encoding(false));
            if (change == "bom")
                File.WriteAllText(catalogPath, catalog, new UTF8Encoding(true));
            if (change == "removed")
                File.WriteAllText(catalogPath, "{\"cases\":[]}");
            if (change == "swapped")
                File.WriteAllText(catalogPath, catalog.Replace(caseId, "PA-NATIVE-002", StringComparison.Ordinal));

            string retainedState = File.ReadAllText(statePath);
            if (expectedMatch)
                Assert.Equal("already_prepared", Prepare()["status"]);
            else
                Assert.Contains("exact prepared Party Agency catalog bytes",
                    Assert.Throws<InvalidOperationException>(() => Prepare()).Message, StringComparison.Ordinal);

            var ready = await TestingTools.EvaluatePartyAgencyReleaseReadiness(api, options, campaigns,
                campaignId, guardId, runId);
            Assert.Equal(expectedMatch, ready["preparedCatalogMatches"]);
            Assert.Equal(expectedMatch, ready["readyToDeployForNativeCertification"]);
            Assert.True(Assert.IsType<bool>(ready["approvedArtifactWitnessAvailable"]));
            Assert.False(Assert.IsType<bool>(ready["installedAndLoadedArtifactsAttested"]));
            Assert.False(Assert.IsType<bool>(ready["readyToRelease"]));
            Assert.Equal(1, ready["requiredInstanceCount"]);
            Assert.Equal(expectedMatch ? 1 : 0, ready["passedInstanceCount"]);
            var checkedState = Assert.IsType<PartyAgencyCertificationState>(ready["state"]);
            Assert.Equal(callerLabel, checkedState.CatalogFingerprint);
            Assert.Equal(change == "legacy" ? null : digest, checkedState.PreparedCatalogSha256);
            Assert.Equal(caseId, Assert.Single(checkedState.RequiredInstances));

            var retained = Assert.IsType<Dictionary<string, object?>>(Assert.Single(
                Assert.IsType<List<object?>>(ready["reports"])));
            Assert.True(Assert.IsType<bool>(retained["completed"]));
            Assert.Equal(expectedMatch, retained["passed"]);
            Assert.True(retained.ContainsKey("artifactIdentityDiagnostics"));
            var original = Assert.IsType<ApiEnvelope>(retained["report"]);
            Assert.Equal("original-evidence", original.Data!.Value.GetProperty("reportMarker").GetString());
            if (!expectedMatch)
            {
                Assert.Contains(Assert.IsType<List<string>>(ready["blockers"]), blocker =>
                    blocker.Contains("exact prepared Party Agency catalog bytes", StringComparison.Ordinal));
                var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    TestingTools.CheckpointPartyAgencySaveRoundtrip(api, options, campaigns, campaignId,
                        guardId, runId, "Active", liveRun, "save Reign campaign-test checkpoint"));
                Assert.Contains("exact prepared Party Agency catalog bytes", mismatch.Message, StringComparison.Ordinal);
            }
            Assert.Equal(1, transport.ReportReads);
            Assert.Equal(retainedState, File.ReadAllText(statePath));
        }
        finally { folder.Delete(recursive: true); }
    }

    private sealed class PartyAgencyCatalogReportHandler(string runId) : HttpMessageHandler
    {
        public int ReportReads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/tests/live/run/report", request.RequestUri!.AbsolutePath);
            Assert.Contains("runId=" + runId, request.RequestUri.Query, StringComparison.Ordinal);
            ReportReads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"ok":true,"status":"completed","reportMarker":"original-evidence"}""",
                    Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public void ManifestOwnsFortyNaturalLanguageAndNativeReleaseCases()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios",
            "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement manifest = document.RootElement;
        Assert.Equal(2, manifest.GetProperty("schemaVersion").GetInt32());
        Assert.False(manifest.GetProperty("preparedOnly").GetBoolean());
        Assert.True(manifest.GetProperty("autonomous").GetBoolean());
        Assert.True(manifest.GetProperty("naturalLanguagePolicy")
            .GetProperty("directDialogueActionInvocationProhibited").GetBoolean());
        JsonElement[] cases = manifest.GetProperty("cases").EnumerateArray().ToArray();
        Assert.Equal(40, cases.Length);
        Assert.Equal(18, cases.Count(item => item.GetProperty("id").GetString()!
            .StartsWith("PA-LANG-", StringComparison.Ordinal)));
        Assert.All(cases.Where(item => item.GetProperty("id").GetString()!
            .StartsWith("PA-LANG-", StringComparison.Ordinal)), item =>
        {
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.NotEmpty(turns);
            Assert.All(turns, turn => Assert.False(string.IsNullOrWhiteSpace(turn)));
            Assert.DoesNotContain("RegularAcceptTemporaryPartyGuest", string.Join(" ", turns),
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("consentConfirmed", string.Join(" ", turns),
                StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-017"
            && item.GetProperty("variants").GetArrayLength() == 6);
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-019"
            && item.GetProperty("variants").EnumerateArray()
                .Any(variant => variant.GetString() == "naval"));
        Assert.Contains(cases, item => item.GetProperty("id").GetString() == "PA-NATIVE-021"
            && item.GetProperty("expected").GetProperty("guestEquipmentProtected").GetBoolean());
    }

    [Fact]
    public void McpSurfaceDerivesTheDisposableSaveAndProhibitsActionBypass()
    {
        string root = TestOptions.FindWorkspace();
        string source = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "TestingTools.PartyAgency.cs"));
        foreach (string tool in new[]
        {
            "reign_get_party_agency_test_manifest", "reign_prepare_party_agency_test",
            "reign_carry_forward_party_agency_passes",
            "reign_start_party_agency_test", "reign_verify_party_agency_save_roundtrip",
            "reign_evaluate_party_agency_release_readiness"
        }) Assert.Contains(tool, source, StringComparison.Ordinal);
        Assert.Contains("RequireArmedOwnedSaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("selfContainedHostilityLanguageCase", source,
            StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-LANG-017\"", source, StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-LANG-018\"", source, StringComparison.Ordinal);
        Assert.Contains("fixtureRole = \"party_leader\"", source,
            StringComparison.Ordinal);
        Assert.Contains("manifest.GetProperty(\"nativeInvitationTemplates\")", source,
            StringComparison.Ordinal);
        Assert.Contains("operation = \"party_agency_prepare_hostility\"", source,
            StringComparison.Ordinal);
        Assert.Contains("authorization.CurrentSaveName", source, StringComparison.Ordinal);
        Assert.Contains("start Reign Party Agency test on disposable save", source,
            StringComparison.Ordinal);
        Assert.Contains("directDialogueActionInvocation", source, StringComparison.Ordinal);
        Assert.Contains("contextualTurnIndexes", source, StringComparison.Ordinal);
        Assert.Contains("contextualPartyAgencyResponse", source, StringComparison.Ordinal);
        Assert.Contains("prepare_party_agency_fixture", source, StringComparison.Ordinal);
        Assert.Contains("@party_agency_fixture", source, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyDialogueOwner", source, StringComparison.Ordinal);
        Assert.Contains("temporary_guest_equipment_lock_only", source, StringComparison.Ordinal);
        Assert.Contains("temporary_guest_relationship_isolation_only", source,
            StringComparison.Ordinal);
        Assert.Contains("temporary_guest_waiting_camp_encounter_exclusion_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_hostile_battle_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_hostility_language_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_partyless_invitation_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_remaining_native_invitation_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_ineligible_native_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_companion_native_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_native_time_driver_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_save_roundtrip_state_driver_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_accepted_lifecycle_router_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_simultaneous_review_queue_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_departure_language_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_return_timing_evidence_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_recovery_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_missing_return_recovery_fixture_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_final_fingerprint_harness_only", source,
            StringComparison.Ordinal);
        Assert.Contains("party_agency_transcript_evidence_parser_only", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedMissingReturnRecoveryFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("finalFingerprintHarnessOnly", source,
            StringComparison.Ordinal);
        Assert.Contains("caseId.Equals(\"PA-NATIVE-022\"", source,
            StringComparison.Ordinal);
        Assert.Contains("new Dictionary<string, object?>()", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedHostileBattle", source, StringComparison.Ordinal);
        Assert.Contains("affectedWaitingCampEncounter", source, StringComparison.Ordinal);
        Assert.Contains("affectedHostilityLanguage", source, StringComparison.Ordinal);
        Assert.Contains("affectedPartylessInvitation", source, StringComparison.Ordinal);
        Assert.Contains("affectedRemainingNativeInvitation", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedIneligibleNativeFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedCompanionNativeFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedNativeTimeDriver", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedSaveRoundtripStateDriver", source,
            StringComparison.Ordinal);
        Assert.Contains("Party Agency save/reload requires a checkpoint bound to a stage in this certification run.",
            source, StringComparison.Ordinal);
        Assert.Contains("VerifyPartyAgencyBoundRoundtripAsync(api, state, execution, report.Data.Value",
            source, StringComparison.Ordinal);
        Assert.Contains("ReadPartyAgencyLiveReport(api, state.CampaignId, stage.LiveRunId",
            source, StringComparison.Ordinal);
        Assert.Contains("affectedSimultaneousReviewQueue", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedDepartureLanguage", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedReturnTimingEvidence", source,
            StringComparison.Ordinal);
        Assert.Contains("affectedRecoveryFixture", source,
            StringComparison.Ordinal);
        Assert.Contains("&& !partylessInvitationFixtureOnly", source,
            StringComparison.Ordinal);
        Assert.Contains("transcriptEvidenceParserOnly", source, StringComparison.Ordinal);
        Assert.Contains("relationshipIsolationOnly && !string.Equals(source.ClientBuild",
            source, StringComparison.Ordinal);
        Assert.Contains("PA-NATIVE-021", source, StringComparison.Ordinal);
        Assert.Contains("CarriedEvidence", source, StringComparison.Ordinal);
        Assert.Contains("ContainsPartyAgencyTranscriptTurn", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("raw.Contains(turn", source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("disposableSaveName,", source.Split("StartPartyAgencyTest", 2,
            StringSplitOptions.None)[1].Split("EvaluatePartyAgencyReleaseReadiness", 2,
            StringSplitOptions.None)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ServerAllowsEveryPartyAgencyNativeHarnessOperation()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerFileGroupText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "LiveInteractionTest.cs",
            "/src/Modules/WorldSimulation/"));
        foreach (string operation in new[]
        {
            "prepare_party_agency_fixture", "party_agency_advance_time",
            "party_agency_aggregate_fixtures", "party_agency_review_provider_result",
            "party_agency_recovery_fixture", "party_agency_verify_unrelated_state",
            "party_agency_prepare_hostility", "party_agency_hostile_battle",
            "party_agency_stage_save_phase", "party_agency_verify_save_phase",
            "party_agency_test"
        }) Assert.Contains($"\"{operation}\"", server, StringComparison.Ordinal);
    }

    [Fact]
    public void PositiveInvitationCasesProvideCompleteContextAndRequestExplicitConsent()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios",
            "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement[] cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();
        string[] positiveInvitations =
        {
            "PA-LANG-001", "PA-LANG-002", "PA-LANG-003", "PA-LANG-004", "PA-LANG-005",
            "PA-LANG-006", "PA-LANG-007", "PA-LANG-008", "PA-LANG-013"
        };

        foreach (string caseId in positiveInvitations)
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            JsonElement contract = item.GetProperty("consentContract");
            Assert.True(contract.GetProperty("purposeStated").GetBoolean());
            Assert.True(contract.GetProperty("knownRiskStated").GetBoolean());
            Assert.True(contract.GetProperty("npcRelevanceStated").GetBoolean());
            Assert.True(contract.GetProperty("termOrReviewStated").GetBoolean());
            Assert.True(contract.GetProperty("askedNotOrdered").GetBoolean());
            Assert.True(contract.GetProperty("explicitAgreementRequested").GetBoolean());

            string dialogue = string.Join(" ", item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty));
            Assert.True(dialogue.Length >= 180,
                $"{caseId} must provide enough natural context for an informed decision.");
            Assert.Contains("agree", dialogue, StringComparison.OrdinalIgnoreCase);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2,
                $"{caseId} must allow a natural negotiation before final consent.");
            Assert.Contains("explicitly agree", turns[^1], StringComparison.OrdinalIgnoreCase);
            int[] contextualIndexes = item.GetProperty("contextualTurnIndexes")
                .EnumerateArray().Select(index => index.GetInt32()).ToArray();
            Assert.Contains(turns.Length - 1, contextualIndexes);
            JsonElement expected = item.GetProperty("expected");
            Assert.True(expected.TryGetProperty("termKind", out _),
                $"{caseId} must assert the persisted schedule kind.");
            Assert.True(expected.TryGetProperty("reviewIntervalDays", out _),
                $"{caseId} must assert the persisted review interval.");
        }

        JsonElement guardedFamilyMatter = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-LANG-006");
        string guardedFamilyDialogue = string.Join(" ", guardedFamilyMatter.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString() ?? string.Empty));
        Assert.Contains("adult cousin", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("negotiation only", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never an abduction", guardedFamilyDialogue, StringComparison.OrdinalIgnoreCase);

        JsonElement identifiedBorderMission = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-LANG-005");
        string identifiedBorderDialogue = string.Join(" ", identifiedBorderMission.GetProperty("turns")
            .EnumerateArray().Select(turn => turn.GetString() ?? string.Empty));
        Assert.Contains("sovereign of fen Niabar", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Zeonica's western border", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inspect the survivors' statements", identifiedBorderDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("withdraw and appeal to Zeonica's court", guardedFamilyDialogue,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a quarrel with your empire", guardedFamilyDialogue,
            StringComparison.OrdinalIgnoreCase);

        foreach (string caseId in new[] { "PA-LANG-014", "PA-LANG-015", "PA-LANG-017" })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2,
                $"{caseId} must allow a natural negotiation before final consent.");
            Assert.Contains("explicitly agree", turns[^1], StringComparison.OrdinalIgnoreCase);
            int[] contextualIndexes = item.GetProperty("contextualTurnIndexes")
                .EnumerateArray().Select(index => index.GetInt32()).ToArray();
            Assert.Contains(turns.Length - 1, contextualIndexes);
        }

        foreach (string caseId in new[]
        {
            "PA-LANG-009", "PA-LANG-010", "PA-LANG-011", "PA-LANG-012"
        })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.False(item.TryGetProperty("consentContract", out _));
            Assert.Equal(0, item.GetProperty("expected").GetProperty("recordDelta").GetInt32());
        }
    }

    [Fact]
    public void ClientHarnessSupportsOneNpcFirstReviewAndEvidenceAssertions()
    {
        string root = TestOptions.FindWorkspace();
        string host = TestSourceLocator.PartialTypeSourceText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignLiveInteractionTestHost.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("targetedReview", host, StringComparison.Ordinal);
        Assert.Contains("OpenTargetedReview", host, StringComparison.Ordinal);
        Assert.Contains("naturalLanguageOnly", host, StringComparison.Ordinal);
        Assert.Contains("directDialogueActionInvocation", host, StringComparison.Ordinal);
        Assert.Contains("EvaluatePartyAgencyExpected", host, StringComparison.Ordinal);
        Assert.Contains("phase.Equals(\"before\", StringComparison.OrdinalIgnoreCase)\n"
            + "                ? new JObject()", host, StringComparison.Ordinal);
        Assert.Contains("BuildPartyAgencyCandidateMatrix", host, StringComparison.Ordinal);
        Assert.Contains("reviewIntervalDays", host, StringComparison.Ordinal);
        Assert.Contains("fixedTermDays", host, StringComparison.Ordinal);
        Assert.Contains("BuildPartyAgencyContextualPlayerResponse", host,
            StringComparison.Ordinal);
        Assert.Contains("\"give me yours\"", host, StringComparison.Ordinal);
        Assert.Contains("\"hear a name\"", host, StringComparison.Ordinal);
        Assert.Contains("\"name from your mouth\"", host, StringComparison.Ordinal);
        Assert.Contains("\"written record\"", host, StringComparison.Ordinal);
        Assert.Contains("\"addendum\"", host, StringComparison.Ordinal);
        Assert.Contains("I place them in your hands now for inspection", host,
            StringComparison.Ordinal);
        Assert.Contains("_lastNpcReplyText", host, StringComparison.Ordinal);
        Assert.Contains("[\"playerText\"] = text", host, StringComparison.Ordinal);
        Assert.Contains("PartyAgencyNativeEvidence", host, StringComparison.Ordinal);
        Assert.Contains("nativeEvidenceReceipt", host, StringComparison.Ordinal);
        // Queued-action evidence stays combined (test directives, then dialogue actions)
        // while the Individual view imports only test directives, so dialogue actions
        // already imported by the conversation wrapper are never applied twice.
        Assert.Contains("[\"queuedActionCount\"] = reply.QueuedActions?.Count ?? 0", host,
            StringComparison.Ordinal);
        string serverClient = TestSourceLocator.PartialTypeText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignServerClient.cs",
            "/src/Modules/Platform/Integration/"));
        Assert.Matches(@"reply\.TestDirectiveActions = current \? ParseQueuedActionRows\(response\[""queuedTestDirectiveActions""\], fence\)[^;]*;\s*"
            + @"reply\.QueuedActions = new List<ReignWorldActionRecord>\(reply\.TestDirectiveActions\);\s*"
            + @"if \(current\) reply\.QueuedActions\.AddRange\(ParseQueuedActionRows\(response\[""queuedDialogueActions""\], fence\)\);",
            serverClient);
        string individual = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignIndividualChatScreenVM.cs",
            "/src/Modules/Dialogue/UI/ViewModels/"));
        Assert.Contains("TryImportForPresentation(reply.TestDirectiveActions,", individual,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TryImportForPresentation(reply.QueuedActions", individual,
            StringComparison.Ordinal);
        Assert.DoesNotContain("EnqueueAndExecuteServerActions(reply.QueuedActions", individual,
            StringComparison.Ordinal);

        string fixture = TestSourceLocator.PartialTypeSourceText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignLiveInteractionPartyAgencyFixtures.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("confirmDisposableCampaign", fixture, StringComparison.Ordinal);
        Assert.Contains("expectedSaveName", fixture, StringComparison.Ordinal);
        Assert.Contains("directGuestActionInvoked", fixture, StringComparison.Ordinal);
        Assert.Contains("IsDisposablePartyAgencyFixtureConversationActive", fixture,
            StringComparison.Ordinal);
        Assert.Contains("activeSave.StartsWith(\"ReignTest_\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("activeSave.EndsWith(\"_Current\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("CampaignTimeControlMode.Stop", fixture, StringComparison.Ordinal);
        Assert.Contains("TryStartPartyAgencyNativeSettlementWait", fixture,
            StringComparison.Ordinal);
        Assert.Contains("safe settlement wait menu", fixture, StringComparison.Ordinal);
        Assert.Contains("deferredUnrelatedReviewDueDays", fixture,
            StringComparison.Ordinal);
        Assert.Contains("nativeSettlementWaitUsed", fixture, StringComparison.Ordinal);
        Assert.Contains("deferredUnrelatedReviewCount", fixture, StringComparison.Ordinal);
        Assert.Contains("CreateLordParty", fixture, StringComparison.Ordinal);
        Assert.Contains("CreateArmy", fixture, StringComparison.Ordinal);
        Assert.Contains("party_agency_stage_save_phase", host, StringComparison.Ordinal);
        Assert.Contains("party_agency_verify_save_phase", host, StringComparison.Ordinal);
        Assert.Contains("requiresNativeCheckpointAndRestart", fixture,
            StringComparison.Ordinal);
        Assert.Contains("restartReceipt", fixture, StringComparison.Ordinal);
        Assert.Contains("financeAndInteractionSafety", fixture, StringComparison.Ordinal);
        Assert.Contains("guestEquipmentProtected", fixture, StringComparison.Ordinal);
        Assert.Contains("CanHeroEquipmentBeChanged()", fixture, StringComparison.Ordinal);
        Assert.Contains("battleEquipmentSignature", fixture, StringComparison.Ordinal);
        Assert.Contains("civilianEquipmentSignature", fixture, StringComparison.Ordinal);
        Assert.Contains("terminalGuestState", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryStartingPhase", fixture, StringComparison.Ordinal);
        Assert.Contains("naturalDepartureReachedReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryFixtureStagedReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryMutationAppliedBeforeReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("recoveryNativeStateObservedBeforeReturn", fixture, StringComparison.Ordinal);
        Assert.Contains("record.SourcePartyStringId = string.Empty;", fixture,
            StringComparison.Ordinal);
        Assert.Contains("record.SourcePartyStringId = releasedSourceId;", fixture,
            StringComparison.Ordinal);
        Assert.Contains("variant == \"missing_source_party\" || variant == \"invalid_return_target\"", fixture,
            StringComparison.Ordinal);
        Assert.Contains("after preserving the natural departure response", fixture,
            StringComparison.Ordinal);
        Assert.Contains("sourcePartyDefaultBehavior", fixture, StringComparison.Ordinal);
        Assert.Contains("sourcePartyUsedByQuest", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptGuest(", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("RegularAcceptTemporaryPartyGuest", fixture,
            StringComparison.Ordinal);

        string guestBehavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("IsDisposablePartyAgencyFixtureConversationActive(hero)", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("CanHeroEquipmentBeChangedEvent.AddNonSerializedListener", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("IsTemporaryGuestEquipmentLocked", guestBehavior, StringComparison.Ordinal);
        Assert.Contains("record.ProtectedCampMorale = party.Morale", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("party.RecentEventsMorale += moraleDelta", guestBehavior,
            StringComparison.Ordinal);
        Assert.Contains("[\"protectedTroopSignature\"]", fixture,
            StringComparison.Ordinal);
        Assert.Contains("hero?.StringId, StringComparison.OrdinalIgnoreCase",
            fixture, StringComparison.Ordinal);
        Assert.Contains("\"sourcePartyId\", \"sourcePartyClanId\", \"protectedTroopSignature\"",
            fixture, StringComparison.Ordinal);

        string guestRecord = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestRecord.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("[SaveableField(27)] public float ProtectedCampMorale", guestRecord,
            StringComparison.Ordinal);
        Assert.Contains("[SaveableField(28)] public bool ProtectedCampMoraleCaptured", guestRecord,
            StringComparison.Ordinal);

        string validator = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignActionValidator.cs",
            "/src/Modules/WorldSimulation/World/"));
        Assert.Contains("ReignServices.PersonalItemService", validator,
            StringComparison.Ordinal);
        string personalItems = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignPersonalItemService.cs",
            "/src/Modules/WorldSimulation/World/"));
        Assert.Contains("IsTemporaryGuestEquipmentLocked(from)", personalItems, StringComparison.Ordinal);
        Assert.Contains("action.AcceptedByHeroStringId == from.StringId", personalItems, StringComparison.Ordinal);
        string valueService = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignValueService.cs",
            "/src/Modules/WorldSimulation/World/"));
        Assert.Contains("IsTemporaryGuestEquipmentLocked(from)", valueService,
            StringComparison.Ordinal);

        string patches = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestPatches.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("BuildProtectedCampSafetyEvidence", patches,
            StringComparison.Ordinal);
        Assert.Contains("protectedPartyExpenseSuppressed", patches,
            StringComparison.Ordinal);
        Assert.Contains("guestClanLeaderExpenseSuppressed", patches,
            StringComparison.Ordinal);
        Assert.Contains("encounterBlockedAsAttacker", patches,
            StringComparison.Ordinal);
        Assert.Contains("encounterBlockedAsDefender", patches,
            StringComparison.Ordinal);
        Assert.Contains("battleJoinBlocked", patches,
            StringComparison.Ordinal);
        Assert.Contains("nearbyReinforcementBlocked", patches,
            StringComparison.Ordinal);
        Assert.Contains("nameof(MapEvent.CanPartyJoinBattle)", patches,
            StringComparison.Ordinal);
        Assert.Contains("AddNearbyPartyToPlayerMapEvent", patches,
            StringComparison.Ordinal);

        string nativeFixtures = TestSourceLocator.PartialTypeSourceText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignLiveInteractionPartyAgencyFixtures.cs",
            "/src/Modules/WorldSimulation/Campaign/"));
        Assert.Contains("protectedCampExcludedFromBattle", nativeFixtures,
            StringComparison.Ordinal);
        Assert.Contains("protectedCampExcludedFromBattle\"] = protectedCampExcluded",
            nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("playerSideWon", nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("playerSafetyPreserved", nativeFixtures, StringComparison.Ordinal);
        Assert.Contains("mapEvent.WinningSide == mapEvent.PlayerSide", nativeFixtures,
            StringComparison.Ordinal);

        string partyChat = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignPartyChatScreenVM.cs",
            "/src/Modules/Dialogue/UI/ViewModels/"));
        Assert.Contains("await WaitForAutomationReadyAsync()", partyChat,
            StringComparison.Ordinal);
        Assert.Contains("activeBatch == null || activeBatch.IsCompleted", partyChat,
            StringComparison.Ordinal);
        Assert.Contains("Timed out waiting for party chat to become ready", partyChat,
            StringComparison.Ordinal);
        string campaignTest = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignMcp"), "CampaignTestTools.cs"));
        Assert.Contains("VerifyReloadedCurrent(state, snapshot, activeSnapshot);",
            campaignTest, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(campaignTest,
            "requireSafeSettlement: false",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant).Count);
        Assert.Contains("ValidateSnapshot(state, activeSnapshot, allowBaseline: false,\n            requireSafeSettlement: false);",
            campaignTest, StringComparison.Ordinal);
        Assert.Contains("ValidateSnapshot(state, snapshot, allowBaseline: false, requireSafeSettlement: false);",
            campaignTest, StringComparison.Ordinal);
        Assert.Contains("ValidatePausedActionSnapshot(state, snapshot);", campaignTest, StringComparison.Ordinal);
        Assert.Contains("Paused action tests require the exact enrolled Current save.", campaignTest, StringComparison.Ordinal);
        Assert.Contains("Paused action tests require stopped native campaign time.", campaignTest, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeEligibilityCasesUseGuardedFixturesAndNaturalInvitationTemplates()
    {
        string root = TestOptions.FindWorkspace();
        string path = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest",
            "scenarios", "party-agency-manifest.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement manifest = document.RootElement;
        JsonElement templates = manifest.GetProperty("nativeInvitationTemplates");
        foreach (string template in new[]
        {
            "fixed", "partyless_mercenary_fixed", "remaining_native_fixed",
            "army_fixed", "remaining_native_army_fixed", "fixed_one",
            "remaining_native_fixed_one", "open_ended",
            "remaining_native_open_ended", "fixed_thirty",
            "remaining_native_fixed_thirty"
        })
        {
            JsonElement item = templates.GetProperty(template);
            string[] turns = item.GetProperty("turns").EnumerateArray()
                .Select(turn => turn.GetString() ?? string.Empty).ToArray();
            Assert.True(turns.Length >= 2);
            Assert.Contains("explicitly agree", turns[^1],
                StringComparison.OrdinalIgnoreCase);
            Assert.Contains(turns.Length - 1,
                item.GetProperty("contextualTurnIndexes").EnumerateArray()
                    .Select(index => index.GetInt32()));
        }
        JsonElement[] cases = manifest.GetProperty("cases").EnumerateArray().ToArray();
        JsonElement partylessTemplate = templates.GetProperty("partyless_mercenary_fixed");
        string partylessClosingTurn = partylessTemplate.GetProperty("turns")[1].GetString()!;
        Assert.Contains("My name is Egan", partylessClosingTurn,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("twenty thousand denars", partylessClosingTurn,
            StringComparison.OrdinalIgnoreCase);
        JsonElement partylessCase = Assert.Single(cases,
            candidate => candidate.GetProperty("id").GetString() == "PA-NATIVE-001");
        Assert.Equal("partyless_mercenary_fixed",
            partylessCase.GetProperty("invitationTemplate").GetString());
        foreach (string preservedCaseId in new[]
        {
            "PA-NATIVE-002", "PA-NATIVE-018", "PA-NATIVE-021"
        })
        {
            JsonElement preserved = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == preservedCaseId);
            Assert.Equal("fixed", preserved.GetProperty("invitationTemplate").GetString());
        }
        foreach (string detailedCaseId in new[]
        {
            "PA-NATIVE-003", "PA-NATIVE-006", "PA-NATIVE-007", "PA-NATIVE-010",
            "PA-NATIVE-015", "PA-NATIVE-016", "PA-NATIVE-017", "PA-NATIVE-019",
            "PA-NATIVE-020"
        })
        {
            JsonElement detailed = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == detailedCaseId);
            Assert.Equal("remaining_native_fixed",
                detailed.GetProperty("invitationTemplate").GetString());
        }
        foreach (int number in Enumerable.Range(1, 7))
        {
            string caseId = "PA-NATIVE-" + number.ToString("000");
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("invitationTemplate", out _),
                $"{caseId} must perform the guest decision through natural dialogue.");
        }
        foreach (int number in Enumerable.Range(9, 4))
        {
            string caseId = "PA-NATIVE-" + number.ToString("000");
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("fixtureRole", out _));
            Assert.True(item.TryGetProperty("invitationTemplate", out _));
        }
        foreach (string caseId in new[] { "PA-NATIVE-015", "PA-NATIVE-016" })
        {
            JsonElement item = Assert.Single(cases,
                candidate => candidate.GetProperty("id").GetString() == caseId);
            Assert.True(item.TryGetProperty("invitationTemplate", out _));
            Assert.True(item.TryGetProperty("departureTemplate", out _));
        }
    }

    [Fact]
    public void TemporaryGuestScheduleIsCanonicalizedAndProvedEndToEnd()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerProgramText(root);
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("NormalizeTemporaryGuestTermKind(terms, errors)", server,
            StringComparison.Ordinal);
        Assert.Contains("terms[\"termKind\"] = \"fixed\"", server,
            StringComparison.Ordinal);
        Assert.Contains("social role such as independent ally", server,
            StringComparison.Ordinal);
        Assert.Contains("pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)", server,
            StringComparison.Ordinal);
        Assert.Contains("new[] { \"fields\", \"arguments\" }", server,
            StringComparison.Ordinal);
        Assert.Contains("copy[\"terms\"] = terms", server,
            StringComparison.Ordinal);
        Assert.Contains("never use temporary_party_guest", server,
            StringComparison.Ordinal);
        Assert.Contains("set reviewIntervalDays=30, and omit durationDays", server,
            StringComparison.Ordinal);
        Assert.Contains("terms.Remove(\"durationDays\")", server,
            StringComparison.Ordinal);
        Assert.Contains("duration >= MinimumFixedTermDays", behavior,
            StringComparison.Ordinal);

        string manifestPath = Path.Combine(root, "ReignServer", "tests", "ReignLiveTest", "scenarios", "party-agency-manifest.json");
        string manifest = File.ReadAllText(manifestPath);
        Assert.Contains("There is no fixed end date", manifest, StringComparison.Ordinal);
        Assert.Contains("review every thirty days", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedGuestInvitationSurvivesTheActionPlannerCandidateCut()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerProgramText(root);

        Assert.Contains("TemporaryPartyGuestCandidateToPreserve(actionGate, playerText, payload, hero)", server,
            StringComparison.Ordinal);
        Assert.Contains("return \"accept_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"renew_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"end_temporary_party_guest\"", server, StringComparison.Ordinal);
        Assert.Contains("return \"acknowledge_own_faction_combat_risk\"", server,
            StringComparison.Ordinal);
        Assert.Contains("\"join my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"travel with my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"ride with my party\"", server, StringComparison.Ordinal);
        Assert.Contains("\"return to your people\"", server, StringComparison.Ordinal);
        Assert.Contains("\"renew our arrangement\"", server, StringComparison.Ordinal);
        Assert.Contains("\"accept banishment\"", server, StringComparison.Ordinal);
        Assert.Contains("temporaryGuestLifecycleCommands", server, StringComparison.Ordinal);
        Assert.Contains("retrieved.RemoveAll(candidate =>", server, StringComparison.Ordinal);
        Assert.Contains("!string.Equals(candidateCommand, temporaryGuestCommand", server,
            StringComparison.Ordinal);
        Assert.DoesNotContain("PlayerExplicitlyEndsTemporaryPartyGuest", server,
            StringComparison.Ordinal);
        Assert.DoesNotContain("playerAuthorizedDeparture", server, StringComparison.Ordinal);
        Assert.DoesNotContain("playerAuthorizedGuestDeparture", server, StringComparison.Ordinal);
        Assert.Contains("PlanActionsFromGate(campaignId, payload, hero, playerText, visibleReply, actionGate)",
            server, StringComparison.Ordinal);
    }

    [Fact]
    public void GuestLifecycleActionsBindToTheSpeakingNpcBeforeResolution()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerProgramText(root);

        int binding = server.IndexOf(
            "ApplyTemporaryPartyGuestActorBinding(raw, terms, payload",
            StringComparison.Ordinal);
        int resolution = server.IndexOf(
            "ResolveActionReferences(raw, terms, payload",
            StringComparison.Ordinal);
        Assert.True(binding >= 0 && resolution > binding);
        Assert.Contains("action[\"actorHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
        Assert.Contains("terms[\"fromHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"renew_temporary_party_guest\"", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"end_temporary_party_guest\"", server,
            StringComparison.Ordinal);
        Assert.Contains("canonical != \"acknowledge_own_faction_combat_risk\"", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void GuestLifecycleActionsDoNotCreateRelationshipEffects()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerProgramText(root);
        string relationships = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "Relationships.cs",
            "/src/Modules/Relationships/"));

        Assert.Contains("IsTemporaryPartyGuestLifecycleConversation(actionGate, playerText",
            server,
            StringComparison.Ordinal);
        Assert.Contains("TemporaryPartyGuestLifecycleCommandFromConversation(",
            server,
            StringComparison.Ordinal);
        Assert.Contains("relationshipAssessments.Clear();", server,
            StringComparison.Ordinal);
        Assert.Contains("relationshipUpdates.Clear();", server,
            StringComparison.Ordinal);
        Assert.Matches(@"IsTemporaryPartyGuestLifecycleAction\((Program\.|ActionPipelineService\.)?CanonicalCommand\(actionType\)\)",
            relationships);
        Assert.Contains("case \"accept_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"renew_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"end_temporary_party_guest\"", relationships,
            StringComparison.Ordinal);
        Assert.Contains("case \"acknowledge_own_faction_combat_risk\"", relationships,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptedGuestLifecycleRecoversFromAnEmptySecondaryPlannerResult()
    {
        string root = TestOptions.FindWorkspace();
        string server = TestSourceLocator.ServerProgramText(root);

        Assert.Contains("BuildAcceptedTemporaryPartyGuestFallbackAction(", server,
            StringComparison.Ordinal);
        Assert.Contains("if (candidates.Count == 0)", server, StringComparison.Ordinal);
        Assert.Contains("accepted_temporary_guest_fallback", server, StringComparison.Ordinal);
        Assert.Contains("TemporaryPartyGuestCandidateToPreserve(actionGate, playerText, payload, hero)", server,
            StringComparison.Ordinal);
        Assert.Contains("replacedMismatchedPlannerAction", server,
            StringComparison.Ordinal);
        Assert.Contains("plannerReturnedAcceptedGuestCommand", server,
            StringComparison.Ordinal);
        Assert.True(server.IndexOf("return \"accept_temporary_party_guest\"",
                StringComparison.Ordinal)
            < server.IndexOf("return \"renew_temporary_party_guest\"",
                StringComparison.Ordinal),
            "An explicit initial join request must outrank incidental review wording.");
        Assert.True(server.IndexOf("return \"accept_temporary_party_guest\"",
                StringComparison.Ordinal)
            < server.IndexOf("return \"end_temporary_party_guest\"",
                StringComparison.Ordinal),
            "An explicit initial join request must outrank future departure safeguards in an open-ended agreement.");
        Assert.Contains("terms[\"consentConfirmed\"] = true", server,
            StringComparison.Ordinal);
        Assert.Contains("TryCompleteAcceptedGuestSchedule(terms, payload, acceptedExchange)", server, StringComparison.Ordinal);
        string reliability = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignServer"), "DialogueActionReliability.cs", "/src/Modules/Dialogue/"));
        Assert.Contains("ExtractItemAmount(text, \"days?\")", reliability, StringComparison.Ordinal);
        Assert.Contains("days > MaximumGuestTermDays", reliability, StringComparison.Ordinal);
        Assert.Contains("terms[\"reviewIntervalDays\"] = 30", reliability, StringComparison.Ordinal);
        Assert.Contains("[\"actorHeroStringId\"] = speakerHeroId", server,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ApplicationTicksQueueEveryPastDueGuestBeforeShowingAReview()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));

        int promote = behavior.IndexOf("PromotePastDueReviews();",
            StringComparison.Ordinal);
        int show = behavior.IndexOf("TryShowDueReview();", StringComparison.Ordinal);
        Assert.True(promote >= 0 && show > promote,
            "Every already-due agreement must enter the review queue before the first popup locks campaign time.");
        Assert.Contains("now >= x.ReviewDueDay", behavior, StringComparison.Ordinal);
        Assert.Contains("record.Phase = ReignTemporaryGuestPhase.ReviewDue", behavior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReviewLifecycleUsesTestedSessionPolicyAndOwnsOnlyItsCleanup()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestCampaignBehavior.cs", "/src/Modules/WorldSimulation/PartyAgency/"));
        string closeResolution = behavior.Split("private void ResolveClosedReviews()", StringSplitOptions.None)[1]
            .Split("private void ClearReviewInquiry()", StringSplitOptions.None)[0];
        Assert.Contains("pending.Value.Resolve(", closeResolution);
        Assert.DoesNotContain("BeginDeparture(", closeResolution);
        Assert.DoesNotContain("AddSeconds(3)", behavior);
        Assert.Contains("ReferenceEquals(current, session)", behavior);
        Assert.Contains("_reviewInquirySessionId != inquirySessionId", behavior);
        Assert.Contains("!_reviewCloseResolutions.ContainsKey(x.HeroStringId)", behavior);
        Assert.Contains("ReignGuestReviewSession.MatchesRenewal", behavior);
        Assert.Contains("record.AgreementRevision++", behavior);
        string record = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignTemporaryPartyGuestRecord.cs", "/src/Modules/WorldSimulation/PartyAgency/"));
        Assert.Contains("[SaveableField(37)] public int AgreementRevision", record);
        var fieldIds = System.Text.RegularExpressions.Regex.Matches(record, @"SaveableField\((\d+)\)")
            .Cast<System.Text.RegularExpressions.Match>()
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(fieldIds.Length, fieldIds.Distinct().Count());
        string manager = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignPartyChatScreenManager.cs", "/src/Modules/Dialogue/UI/"));
        Assert.Contains("finally", manager);
        Assert.Contains("ReignPartyChatCleanup.Run(LogCleanupFailure", manager);
        Assert.Contains("() => dataSource?.OnFinalize()", manager);
        Assert.Contains("_releaseActiveStateDisable = () => manager.UnregisterActiveStateDisableRequest", manager);
        Assert.Contains("_pendingReviewStillValid != null && !_pendingReviewStillValid()", manager);
        string vm = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignPartyChatScreenVM.cs", "/src/Modules/Dialogue/UI/ViewModels/"));
        Assert.Contains("_guestReviewSession?.CompleteTurn", vm);
        Assert.Contains("if (_isFinalized) break;", vm);
        Assert.Contains("RefreshTargetedReviewReceipt", vm);
        string tracker = vm.Split("private async Task<ReignPartyChatBatchResult> RequestLiveNpcTurns(", StringSplitOptions.None)[1]
            .Split("public void RefreshTargetedReviewReceipt()", StringSplitOptions.None)[0];
        Assert.Matches(@"if \(_guestReviewSession != null\)\s*\{\s*IsBusy = false;\s*BusyText = string.Empty;", tracker);
        // A closed screen, a changed load or a changed player owns no terminal
        // presentation, persistence or XP of the batch. The owner keeps the screen check.
        Assert.Matches(@"bool IsPartyTurnCurrent\(PartyTurnOwner owner\) => owner != null && !_isFinalized\s*"
            + @"&& owner\.Fence\.IsCurrent\(\) && ReferenceEquals\(owner\.Player, Hero\.MainHero\);", vm);
        Assert.Matches(@"if \(!turnCurrent\(\)\) return false;\s*if \([^)]*!hadFailure\)[^;]*;\s*PersistCastleTranscript\(\);", vm);
        Assert.Contains("() => { if (turnCurrent()) xp.Award(", vm);
        // The approach opening is owned by the same fenced owner from acceptance through
        // dispatch, transcript restore, persistence and the busy reset.
        string lives = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignPartyChatLives.cs", "/src/Modules/Dialogue/UI/ViewModels/"));
        string opening = lives.Split("RequestLifeOpening(List<Hero> heroes)", StringSplitOptions.None)[1];
        Assert.Contains("var owner = new PartyTurnOwner(ReignServerClient.RequestFence.Capture(), Hero.MainHero);", opening);
        Assert.Contains("Func<bool> current = () => IsPartyTurnCurrent(owner);", opening);
        Assert.Contains("OpenNpcLifeApproachAsync(hero, session, individualContext, requestOwner: current)", opening);
        Assert.Contains("PostOwnedJsonAsync(\"/party-chat/history\"", opening);
        Assert.Matches(@"if \(!current\(\)\) return;\s*if \(restored\.Count > 0\)[\s\S]*PersistCastleTranscript\(\);", opening);
        Assert.Contains("if (current()) { IsBusy = false;", opening);
        Assert.DoesNotContain("() => !_isFinalized", opening);
        Assert.DoesNotContain("if (!_isFinalized) AddSystemLine", opening);
        // One shared opener: the optional owner guards capture, dispatch and polling,
        // while a null owner keeps the legacy continueWaiting path.
        string npcLives = File.ReadAllText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignNpcLivesClient.cs", "/src/Modules/Characters/Integration/"));
        string ownedOpening = npcLives.Split("Task<JObject> OpenNpcLifeApproachAsync(", StringSplitOptions.None)[1];
        Assert.Contains("EnsureRequestOwner(requestOwner, \"approach opening capture\");", ownedOpening);
        Assert.Contains("EnsureRequestOwner(requestOwner, \"approach audience capture\");", ownedOpening);
        Assert.Contains("PostOwnedJsonAsync(\"/npc-lives/open\", p, requestOwner)", ownedOpening);
        Assert.Contains("PostOwnedJsonAsync(\"/npc-lives/opening\", p, requestOwner)", ownedOpening);
        Assert.Contains("continueWaiting == null || continueWaiting()", ownedOpening);
        Assert.DoesNotContain("PostJsonAsync(", ownedOpening);
        string client = TestSourceLocator.PartialTypeText(TestSourceLocator.Unique(Path.Combine(root, "ReignBeta"),
            "ReignServerClient.cs", "/src/Modules/Platform/Integration/"));
        Assert.DoesNotContain("OpenOwnedNpcLifeApproachAsync", client);
    }

    [Fact]
    public void InPersonGuestConsentRecognizesThePlayersSettlementMenuPresence()
    {
        string root = TestOptions.FindWorkspace();
        string behavior = File.ReadAllText(TestSourceLocator.Unique(
            Path.Combine(root, "ReignBeta"), "ReignTemporaryPartyGuestCampaignBehavior.cs",
            "/src/Modules/WorldSimulation/PartyAgency/"));

        Assert.Contains("Hero.MainHero?.CurrentSettlement", behavior, StringComparison.Ordinal);
        Assert.Contains("hero.CurrentSettlement == playerSettlement", behavior,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CampaignTestStartDoesNotTrustAStaleCampaignHeartbeatAfterTheGameStops()
    {
        string root = TestOptions.FindWorkspace();
        string service = File.ReadAllText(Path.Combine(root, "ReignMcp", "src",
            "Reign.Mcp.Server", "Modules", "Platform", "CampaignTestTools.cs"));

        Assert.Contains("status.FindBoolean(\"running\")", service,
            StringComparison.Ordinal);
        Assert.Contains("!string.Equals(lifecycleStatus, \"campaign_ready\"",
            service, StringComparison.Ordinal);
        Assert.Contains("status.FindString(\"activeSaveName\"), expectedSave",
            service, StringComparison.Ordinal);
        Assert.Contains("await VerifyStartedRuntimeAsync(state, expectedSave,",
            service, StringComparison.Ordinal);
        Assert.Contains("/tests/live/runtime", service, StringComparison.Ordinal);
        Assert.Contains("ValidateSnapshot(state, snapshot, allowBaseline: true);",
            service, StringComparison.Ordinal);
        Assert.Contains("\"game\", \"start\", \"--save\", expectedSave", service,
            StringComparison.Ordinal);
    }
}
