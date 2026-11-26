using System.Text.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.WorldSimulation.Campaign;
using Reign.Core.Contracts.Court;
using Reign.Mcp.Server;

namespace Reign.Mcp.Tests.Features.Court;

public sealed class CourtLifeHarnessContractTests
{
    [Fact]
    public void PaidCourtSupplyAcceptanceUsesOwnedCurrentAndVerifiedOriginalCommand()
    {
        string[] profiles = ["court_life_paid_supply_preflight", "court_life_paid_supply_apply", "court_life_paid_supply_verify"];
        var manifest = Assert.IsType<string[]>(TestingTools.GetRulerDocketTestManifest()["profiles"]);
        Assert.All(profiles, profile => Assert.Contains(profile, manifest));

        string root = TestOptions.FindWorkspace();
        string court = Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign");
        string host = File.ReadAllText(Path.Combine(court, "ReignLiveInteractionCourtLifeHost.cs"));
        string poll = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        string issuer = File.ReadAllText(Path.Combine(root, "ReignMcp/src/Reign.Mcp.Server/Modules/Court/TestingTools.Court.cs"));
        string paidIssuer = issuer[issuer.IndexOf("private static async Task<ApiEnvelope> StartPaidCourtSupplyAsync(", StringComparison.Ordinal)..];
        int route = poll.IndexOf("await ExecutePaidCourtCommandAsync(", StringComparison.Ordinal);
        int cache = poll.IndexOf("CompletedCommandResults.TryGetValue(commandId", StringComparison.Ordinal);
        Assert.True(route >= 0 && route < cache);
        Assert.Contains("bindCheckpoint: true", paidIssuer);
        Assert.Contains("postArmCurrentDiagnostic: true", paidIssuer);
        Assert.Contains("RequireRetainedRecoveryCheckpoint(authorization)", paidIssuer);
        Assert.Contains("SetAccessRuleProtection(isProtected: true, preserveInheritance: false)", paidIssuer);
        Assert.Contains("FileMode.CreateNew", paidIssuer);
        int ack = host.IndexOf("MatchesPaidCourtAcknowledgment(", StringComparison.Ordinal);
        int dispatch = host.IndexOf("ReignMainThread.RunOwnedAsync(", StringComparison.Ordinal);
        int consume = host.IndexOf("ConsumePaidCourtSupplyGrant(command, owner)", StringComparison.Ordinal);
        int prepare = host.IndexOf("PreparePaidSupplyAcceptance(", StringComparison.Ordinal);
        int invoke = host.IndexOf("court.TransferSupplyAsync(transfer.Request, transfer.OwnsNativeLeg,", StringComparison.Ordinal);
        Assert.True(ack >= 0 && ack < dispatch && dispatch < consume && consume < prepare && prepare < invoke);
        Assert.Contains("DateTimeOffset.UtcNow < deadline", host, StringComparison.Ordinal);
        Assert.Contains("GetTimestamp() - pollStarted <", host, StringComparison.Ordinal);
        Assert.Contains("transfer.Verify ? Task.FromResult(false) : beginNativeDispatch(ownsTransfer, JObject.FromObject(transfer.Request))", host, StringComparison.Ordinal);
        Assert.Contains("expectedPaidTimelineId = authorization.TimelineId", paidIssuer, StringComparison.Ordinal);
        Assert.Contains("expectedPaidSavePointId = checkpoint.SavePointId", paidIssuer, StringComparison.Ordinal);
        string transport = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/Integration/ReignLiveTestClient.cs"));
        Assert.Contains("SendPinnedJsonAsync(origin, terminal ?", transport, StringComparison.Ordinal);
        Assert.Contains("File.Move(path, claimed)", host, StringComparison.Ordinal);
        Assert.Contains("MatchesPaidCourtGrantBinding(grant, command, owner,", host, StringComparison.Ordinal);
        Assert.Contains("NativeSaveHasPoint(name, owner.Value<string>(\"campaignId\"), point)", host, StringComparison.Ordinal);
        Assert.Contains("NativeCheckpointHash(name), hash", host, StringComparison.Ordinal);
        Assert.Contains("Paid Court acceptance requires a fresh acknowledged local admission.", host, StringComparison.Ordinal);

        string native = File.ReadAllText(Path.Combine(court, "ReignCourtLifeInGameTests.cs"));
        Assert.Contains("source.OwnerClan != Clan.PlayerClan", native, StringComparison.Ordinal);
        Assert.Contains("GoldRecipientStringId = selected.StringId", native, StringComparison.Ordinal);
        Assert.Contains("ItemAmount = 1, GoldAmount = 1", native, StringComparison.Ordinal);
        Assert.Contains("marker.Value<string>(\"gameInstanceId\") == instance", native, StringComparison.Ordinal);
        Assert.Contains("marker.Value<string>(\"savePointId\") == point", native, StringComparison.Ordinal);
        Assert.Contains("marker.Value<string>(\"clientProcessInstanceSha256\") == processHash", native, StringComparison.Ordinal);
        Assert.Contains("marker.Value<string>(\"checkpointAttemptId\") == checkpoint", native, StringComparison.Ordinal);
        Assert.Contains("Verification must name the original saved target.", native, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(source.OwnerClan, clan) && ReferenceEquals(selected.OwnerClan, clan)", native, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(source.ItemRoster, sourceRoster) && ReferenceEquals(selected.ItemRoster, targetRoster)", native, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(selected.SettlementComponent, targetWallet)", native, StringComparison.Ordinal);
        Assert.Contains("!JToken.DeepEquals(marker[\"terms\"], terms)", native, StringComparison.Ordinal);
        Assert.Contains("saved?.Value<bool>(\"exactVerifiedReceipt\") != true", native, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(transfer.Before, holdings)", native, StringComparison.Ordinal);
        Assert.Contains("transfer.Session.Revision == transfer.SessionRevision", native, StringComparison.Ordinal);

        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var acceptance = catalog.RootElement.GetProperty("courtSupplyTransfer").GetProperty("paidRecovery")
            .GetProperty("nativeProfiles");
        Assert.All(profiles, profile => Assert.Contains(acceptance.EnumerateArray(), row => row.GetString() == profile));
    }

    [Theory]
    [InlineData("court_life_paid_supply_apply", "", -1, "paidSupplyTargetSettlementId is required")]
    [InlineData("court_life_paid_supply_verify", "", -1, "paidSupplyTargetSettlementId is required")]
    [InlineData("court_life_paid_supply_preflight", "paid_target", -1, "only for apply or verify")]
    [InlineData("court_life_paid_supply_apply", "paid_target", 10, "does not admit other Court fixture mutations")]
    public async Task PaidCourtSupplyRejectsMissingOrUnsupportedTargetsBeforeCampaignAccess(
        string profile, string target, int fixtureGold, string expected)
    {
        var options = new ReignMcpOptions { WorkspaceRoot = TestOptions.FindWorkspace(), BuildRoot = Path.GetTempPath(),
            ServerBaseUri = new Uri("http://127.0.0.1:5101"), AllowVerificationControl = true };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartRulerDocketTest(null!, options, null!,
            "campaign", "owned-run", profile: profile, confirmation: "start Reign ruler docket test on disposable save",
            courtLifeFixtureGold: fixtureGold, paidSupplyTargetSettlementId: target));
        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("proof", "")]
    [InlineData("ack", "ok")]
    [InlineData("ack", "idempotent")]
    [InlineData("run", "mode")]
    [InlineData("run", "status")]
    [InlineData("run", "runId")]
    [InlineData("run", "campaignId")]
    [InlineData("run", "gameInstanceId")]
    [InlineData("command", "status")]
    [InlineData("command", "commandId")]
    [InlineData("command", "leaseId")]
    [InlineData("command", "operation")]
    [InlineData("command", "phase")]
    [InlineData("command", "fixtureRunId")]
    [InlineData("command", "campaignTestRunId")]
    [InlineData("command", "expectedSaveName")]
    [InlineData("command", "paidSupplyTargetSettlementId")]
    [InlineData("command", "paidCourtAuthorityVersion")]
    [InlineData("command", "paidCourtNativeDispatch")]
    [InlineData("command", "paidCourtAcceptedProcessId")]
    [InlineData("command", "paidCourtLeasedProcessId")]
    [InlineData("command", "leaseExpiresUtc")]
    [InlineData("command", "expectedPaidTimelineId")]
    [InlineData("command", "expectedPaidSavePointId")]
    public void PaidCourtSupplyRequiresFreshExactTypedAcknowledgment(string section, string field)
    {
        const string process = "00000000000000000000000000000004";
        var start = new DateTimeOffset(2026, 11, 25, 21, 0, 0, TimeSpan.Zero);
        DateTimeOffset deadline = start.AddSeconds(45);
        var issued = new JObject
        {
            ["runId"] = "court_paid_00000000000000000000000000000001",
            ["commandId"] = "court_paid_00000000000000000000000000000002",
            ["status"] = "leased", ["leaseId"] = "00000000000000000000000000000003", ["operation"] = "ruler_docket_test",
            ["leaseStartedUtc"] = start.ToString("O"), ["leaseExpiresUtc"] = deadline.ToString("O"),
            ["paidCourtLeasedProcessId"] = process,
            ["phase"] = "court_life_paid_supply_apply", ["fixtureRunId"] = "fixture", ["campaignTestRunId"] = "enrollment",
            ["expectedSaveName"] = "ReignTest_Court_Current", ["paidSupplyTargetSettlementId"] = "fortification",
            ["campaignId"] = "campaign", ["gameInstanceId"] = "game",
            ["expectedPaidTimelineId"] = "main", ["expectedPaidSavePointId"] = "10000000000000000000000000000001"
        };
        var accepted = (JObject)issued.DeepClone();
        accepted["status"] = "accepted";
        accepted["paidCourtAuthorityVersion"] = 1;
        accepted["paidCourtAcceptedProcessId"] = process;
        accepted["paidCourtNativeDispatch"] = false;
        var run = new JObject
        {
            ["runId"] = issued["runId"], ["campaignId"] = "campaign", ["gameInstanceId"] = "game",
            ["status"] = "running", ["mode"] = "spymaster"
        };
        var acknowledgment = new JObject { ["ok"] = true, ["command"] = accepted, ["run"] = run };
        var terms = new JObject { ["CommandId"] = "the-bound-command", ["VassalConsentGranted"] = false };
        bool Matches(JObject? ack, bool dispatch, DateTimeOffset when) =>
            ReignRelationshipNativePullWire.MatchesPaidCourtAcknowledgment(ack, issued, "campaign", "game", process, dispatch, when,
                dispatch ? terms : null);
        if (section == "proof")
        {
            Assert.True(Matches(acknowledgment, false, start));
            Assert.False(Matches(null, false, start));
            Assert.False(Matches(acknowledgment, false, deadline));
            Assert.False(Matches(acknowledgment, true, start));
            accepted["status"] = "running";
            accepted["paidCourtNativeDispatch"] = true;
            Assert.False(Matches(acknowledgment, true, start));
            accepted["paidCourtNativeDispatchTerms"] = new JObject { ["CommandId"] = "other", ["VassalConsentGranted"] = false };
            Assert.False(Matches(acknowledgment, true, start));
            accepted["paidCourtNativeDispatchTerms"] = terms.DeepClone();
            Assert.True(Matches(acknowledgment, true, deadline.AddTicks(-1)));
            Assert.False(Matches(acknowledgment, true, deadline));
            accepted["status"] = "accepted";
            accepted["paidCourtNativeDispatch"] = false;
            issued["phase"] = accepted["phase"] = "court_life_paid_supply_preflight";
            issued["paidSupplyTargetSettlementId"] = accepted["paidSupplyTargetSettlementId"] = string.Empty;
            Assert.True(Matches(acknowledgment, false, start));
            return;
        }
        JObject changed = section switch { "ack" => acknowledgment, "run" => run, _ => accepted };
        changed[field] = field == "ok" ? "true" : field == "idempotent" ? "false" : "substituted";
        Assert.False(Matches(acknowledgment, false, start));
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("expired")]
    [InlineData("future")]
    [InlineData("coerced timestamp")]
    [InlineData("extended expiry")]
    [InlineData("fixture")]
    [InlineData("target")]
    [InlineData("phase")]
    [InlineData("checkpoint")]
    [InlineData("different native process")]
    [InlineData("different loaded generation")]
    [InlineData("different issued timeline")]
    [InlineData("different issued point")]
    public void PaidCourtSupplyGrantBindsOneCommandAndExactNativeLoad(string change)
    {
        const long now = 700;
        var owner = new JObject { ["nativeProcessId"] = 54, ["clientProcessInstanceId"] = "live-process",
            ["restoreGenerationSha256"] = "active-generation", ["timelineId"] = "main" };
        var grant = new JObject
        {
            ["schema"] = "reign-court-paid-supply-admission-v1", ["issuedUtc"] = now, ["expiresUtc"] = now + 120,
            ["runId"] = "run", ["commandId"] = "command", ["phase"] = "court_life_paid_supply_apply",
            ["fixtureRunId"] = "fixture", ["targetSettlementId"] = "fortification", ["campaignTestRunId"] = "enrollment",
            ["baselineSaveName"] = "Baseline", ["saveName"] = "ReignTest_Court_Current", ["savePrefix"] = "ReignTest_Court",
            ["checkpointAttemptId"] = "00000000000000000000000000000001", ["checkpointSha256"] = "bytes",
            ["checkpointSavePointId"] = "point", ["owner"] = owner.DeepClone()
        };
        var command = new JObject
        {
            ["runId"] = "run", ["commandId"] = "command", ["phase"] = "court_life_paid_supply_apply",
            ["fixtureRunId"] = "fixture", ["paidSupplyTargetSettlementId"] = "fortification",
            ["campaignTestRunId"] = "enrollment", ["expectedSaveName"] = "ReignTest_Court_Current",
            ["expectedPaidTimelineId"] = "main", ["expectedPaidSavePointId"] = "point"
        };
        long observedAt = now;
        switch (change)
        {
            case "expired": observedAt += 120; break;
            case "future": observedAt--; break;
            case "coerced timestamp": grant["issuedUtc"] = "700"; break;
            case "extended expiry": grant["expiresUtc"] = now + 121; break;
            case "fixture": command["fixtureRunId"] = "other"; break;
            case "target": command["paidSupplyTargetSettlementId"] = "other"; break;
            case "phase": command["phase"] = "court_life_paid_supply_verify"; break;
            case "checkpoint": grant["checkpointAttemptId"] = "unregistered"; break;
            case "different native process": owner["clientProcessInstanceId"] = "other-process"; break;
            case "different loaded generation": owner["restoreGenerationSha256"] = "other-generation"; break;
            case "different issued timeline": command["expectedPaidTimelineId"] = "other"; break;
            case "different issued point": command["expectedPaidSavePointId"] = "other"; break;
        }
        Assert.Equal(change == "valid", ReignRelationshipNativePullWire.MatchesPaidCourtGrantBinding(grant, command, owner, observedAt));
    }

    [Fact]
    public void PaidCourtSupplyDispatchFollowsTermsAndGuardsFirstNativeMutation()
    {
        string court = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/src/Modules/Court/Campaign");
        string directives = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.Directives.cs"));
        int terms = directives.IndexOf("await ReignCourtServerClient.ValidateSupplyTransferAsync(", StringComparison.Ordinal);
        int begin = directives.IndexOf("await authorizeFixtureDispatch(OwnsTransfer)", StringComparison.Ordinal);
        int dispatch = directives.IndexOf("beforePaidFixtureNative: markFixtureInvoked", StringComparison.Ordinal);
        Assert.True(terms >= 0 && terms < begin && begin < dispatch);

        string native = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.NativeActions.cs"));
        int owner = native.IndexOf("if (!requestOwner() ||", StringComparison.Ordinal);
        int marker = native.IndexOf("!beforePaidFixtureNative()", StringComparison.Ordinal);
        int journal = native.IndexOf("ApplyOwnedPaidTransfer(", StringComparison.Ordinal);
        Assert.True(owner >= 0 && owner < marker && marker < journal);

        string fixture = File.ReadAllText(Path.Combine(court, "ReignCourtLifeInGameTests.cs"));
        int mark = fixture.IndexOf("bool MarkInvoked()", StringComparison.Ordinal);
        int store = fixture.IndexOf("StoreRulerDocketMarker(court, marker)", StringComparison.Ordinal);
        Assert.True(mark >= 0 && mark < store);
        Assert.Contains("if (verify || !OwnsNativeLeg()", fixture[mark..store], StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(court.Session, session) && session.State == ReignCourtSessionState.Sitting", fixture, StringComparison.Ordinal);
        Assert.Contains("session.Revision == revision", fixture, StringComparison.Ordinal);
        Assert.Contains("OwnsNativeState(initialRevision)", fixture, StringComparison.Ordinal);
        Assert.Contains("OwnsNativeState(expectedResultRevision)", fixture, StringComparison.Ordinal);
        Assert.Contains("bool scope = transfer.OwnsExpectedResult()", fixture, StringComparison.Ordinal);
        Assert.Contains("expectedResultRevision = verify ? initialRevision : initialRevision + 1", fixture, StringComparison.Ordinal);
        Assert.Contains("OwnsTransferFor(fixtureCompletionOwner ?? fixtureOwner)", directives, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtSupplyOwnerGatePrecedesNativeReplayOrTransfer()
    {
        string root = TestOptions.FindWorkspace();
        string court = Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign");
        string directives = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.Directives.cs"));
        string transfer = directives[directives.IndexOf("public async Task<string> TransferSupplyAsync(", StringComparison.Ordinal)
            ..directives.IndexOf("public async Task<string> SubmitNativeActionAsync(", StringComparison.Ordinal)];
        int capture = transfer.IndexOf("ReignServerClient.RequestFence.Capture()", StringComparison.Ordinal);
        int validate = transfer.IndexOf("await ReignCourtServerClient.ValidateSupplyTransferAsync(", StringComparison.Ordinal);
        Assert.True(capture >= 0 && validate > capture);
        Assert.Contains("ReignMainThread.IsMainThread", transfer);
        Assert.Contains("ReferenceEquals(this, ReignServices.Court)", transfer);
        Assert.Contains("ReferenceEquals(session, _session) && session.SessionId == sessionId && session.Authority == authority", transfer);
        Assert.Contains("!ReignCampaignInitializationGate.IsPending && fence.IsCurrentActionPoll()", transfer);
        Assert.Contains("ReferenceEquals(sitting, _courtSittingOwner)", transfer);
        Assert.Contains("!ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress && !ReignSaveSyncCoordinator.IsSaveFinalizationPending", transfer);
        Assert.Contains("ReferenceEquals(saveSync, ReignServices.SaveSync)", transfer);
        Assert.Contains("string.Equals(savePoint, saveSync?.CurrentSavePointId, StringComparison.Ordinal)", transfer);
        Assert.Contains("if (!OwnsTransfer()) return CourtOwnedActionUnavailable;", transfer[..validate]);
        Assert.Contains("OwnsTransferFor(fixtureOwner)", transfer[..validate]);
        Assert.Contains("requestOwner: OwnsTransfer", transfer[validate..]);
        Assert.Contains("request, _serverRevision, fence.SharedActivityGeneration, OwnsTransfer", transfer[validate..]);

        string lifecycle = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.cs"));
        int open = lifecycle.IndexOf("public bool TryOpenSession(", StringComparison.Ordinal);
        int reopen = lifecycle.IndexOf("bool reuseRoyalDocket", open, StringComparison.Ordinal);
        int close = lifecycle.IndexOf("public void CloseSession(", StringComparison.Ordinal);
        int closeServer = lifecycle.IndexOf("CloseServerSessionAsync(reason)", close, StringComparison.Ordinal);
        Assert.Contains("_courtSittingOwner = new object()", lifecycle[open..reopen]);
        Assert.Contains("_courtSittingOwner = new object()", lifecycle[close..closeServer]);
        int observe = lifecycle.IndexOf("private bool ObserveAuthorityFailure(", StringComparison.Ordinal);
        int retire = lifecycle.IndexOf("ReignCourtAuthorityStability.RecordObservedFailure(", observe, StringComparison.Ordinal);
        int grace = lifecycle.IndexOf("return ReignCourtAuthorityStability.ShouldInterrupt(", retire, StringComparison.Ordinal);
        int reset = lifecycle.IndexOf("private void ResetAuthorityFailures()", grace, StringComparison.Ordinal);
        int interrupt = lifecycle.IndexOf("private void InterruptAndClose(", reset, StringComparison.Ordinal);
        Assert.True(observe >= 0 && retire > observe && grace > retire && reset > grace && interrupt > reset);
        Assert.Contains("ref _courtSittingOwner", lifecycle[retire..grace]);
        Assert.DoesNotContain("_courtSittingOwner", lifecycle[reset..interrupt]);

        string executor = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.NativeActions.cs"));
        int nativeThread = executor.IndexOf("return await ReignMainThread.InvokeAsync(() =>", StringComparison.Ordinal);
        int owner = executor.IndexOf("if (requestOwner != null && !requestOwner())", StringComparison.Ordinal);
        int replay = executor.IndexOf("_appliedNativeCommandIds.Contains(nativeCommandId)", StringComparison.Ordinal);
        int apply = executor.IndexOf("ReignCourtSupplyService.ApplyTransfer(", StringComparison.Ordinal);
        Assert.True(nativeThread >= 0 && owner > nativeThread && replay > owner && apply > replay);
    }

    [Fact]
    public async Task CourtSupplyHeldReplyIsRetiredByObservedAuthorityLossEvenAfterRecovery()
    {
        object currentOwner = new();
        int consecutiveFailures = 0;
        bool hasRoyalAccess = true;
        var reply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> AdmitReplyAsync(object capturedOwner)
            => await reply.Task && hasRoyalAccess && ReferenceEquals(capturedOwner, currentOwner);

        object originalOwner = currentOwner;
        Task<bool> held = AdmitReplyAsync(originalOwner);
        Assert.False(held.IsCompleted);

        hasRoyalAccess = false;
        consecutiveFailures = ReignCourtAuthorityStability.RecordObservedFailure(consecutiveFailures, ref currentOwner);
        Assert.Equal(1, consecutiveFailures);
        Assert.False(ReignCourtAuthorityStability.ShouldInterrupt(consecutiveFailures));
        Assert.NotSame(originalOwner, currentOwner);

        // Ordinary recovery clears the failure count and leaves the new owner in place.
        hasRoyalAccess = true;
        Task<bool> fresh = AdmitReplyAsync(currentOwner);
        reply.SetResult(true);

        Assert.False(await held);
        Assert.True(await fresh);
    }

    [Fact]
    public void CourtSupplyRequiresLiveRoyalAccessAndTheOriginalCapitalDesignation()
    {
        string court = Path.Combine(TestOptions.FindWorkspace(), "ReignBeta/src/Modules/Court/Campaign");
        string directives = File.ReadAllText(Path.Combine(court, "ReignCourtCampaignBehavior.Directives.cs"));
        int capture = directives.IndexOf("KingdomCapitalDesignation designation = ReadDesignation()", StringComparison.Ordinal);
        int owner = directives.IndexOf("bool OwnsTransfer()", capture, StringComparison.Ordinal);
        int validate = directives.IndexOf("await ReignCourtServerClient.ValidateSupplyTransferAsync(", owner, StringComparison.Ordinal);
        Assert.True(capture >= 0 && owner > capture && validate > owner);
        string gate = directives[owner..validate];
        Assert.Contains("HasRoyalCommandAccess", gate);
        Assert.Contains("ReferenceEquals(kingdom, Clan.PlayerClan?.Kingdom)", gate);
        Assert.Contains("host.OwnerClan?.Kingdom == kingdom", gate);
        Assert.Contains("session.HostSettlementStringId == hostId", gate);
        Assert.Contains("ReferenceEquals(designation, ReadDesignation())", gate);
        Assert.Contains("designation.Revision == capitalRevision && designation.SettlementStringId == hostId", gate);
        Assert.DoesNotContain("CurrentCapitalDesignation()", directives[capture..validate]);

        string capital = File.ReadAllText(Path.Combine(court, "ReignCourtCapitalAmbassadors.cs"));
        int apply = capital.IndexOf("internal static void ApplyCapitalDesignation(", StringComparison.Ordinal);
        int clear = capital.IndexOf("internal static void ClearCapitalDesignation(", apply, StringComparison.Ordinal);
        int next = capital.IndexOf("internal static ReignCourtScope ResolveCourtScope(", clear, StringComparison.Ordinal);
        Assert.True(apply >= 0 && clear > apply && next > clear);
        Assert.Contains("record.Revision++", capital[apply..clear]);
        Assert.Contains("record.Revision++", capital[clear..next]);
    }

    [Fact]
    public void CourtSupplyValidationRequiresOneExactEchoedTransfer()
    {
        string root = TestOptions.FindWorkspace();
        string integration = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Integration/ReignCourtServerClient.cs"));
        string validation = integration[integration.IndexOf("ValidateSupplyTransferAsync(", StringComparison.Ordinal)
            ..integration.IndexOf("ValidateNativeActionAsync(", StringComparison.Ordinal)];
        Assert.Contains("expectedAction[\"type\"] = \"transfer_item\"", validation);
        Assert.Contains("expectedAction[\"commandId\"] = commandId", validation);
        Assert.Contains("receipt == null || !JToken.DeepEquals(receipt[\"result\"], \"court_supply_transfer_validated\")", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"campaignId\"], payload[\"campaignId\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"timelineId\"], payload[\"timelineId\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"sessionId\"], payload[\"sessionId\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"sessionRevision\"], payload[\"expectedRevision\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"sharedActivityGeneration\"], payload[\"sharedActivityGeneration\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"commandId\"], payload[\"commandId\"])", validation);
        Assert.Contains("!JToken.DeepEquals(receipt[\"termsHash\"], payload[\"termsHash\"])", validation);
        Assert.Contains("response.NativeActions.Count != 1 || !JToken.DeepEquals(response.NativeActions[0], expectedAction)", validation);

        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var checks = catalog.RootElement.GetProperty("courtSupplyTransfer").GetProperty("contractChecks").EnumerateArray();
        Assert.Contains(checks, check => check.GetString() == nameof(CourtSupplyOwnerGatePrecedesNativeReplayOrTransfer));
        Assert.Contains(checks, check => check.GetString() == nameof(CourtSupplyValidationRequiresOneExactEchoedTransfer));
        Assert.Contains(checks, check => check.GetString() == nameof(CourtSupplyRequiresLiveRoyalAccessAndTheOriginalCapitalDesignation));
        Assert.Contains(checks, check => check.GetString() == nameof(CourtSupplyHeldReplyIsRetiredByObservedAuthorityLossEvenAfterRecovery));
    }

    [Fact]
    public void CourtSupplyOwnerPassesThroughReadinessAndEachDirectDispatch()
    {
        string root = TestOptions.FindWorkspace();
        string integration = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Integration/ReignCourtServerClient.cs"));
        Assert.Contains("PostAsync(\"/court/supply/validate-owned\", payload, requestOwner)", integration);
        int ready = integration.IndexOf("await ReignSaveSyncCoordinator.WaitForReadyAsync(route)", StringComparison.Ordinal);
        int owner = integration.IndexOf("await ReignRequestOwnership.EnsureAsync(requestOwner, \"Court readiness\")", StringComparison.Ordinal);
        int transport = integration.IndexOf("await ReignHttpTransport.PostDirectJsonAsync(", StringComparison.Ordinal);
        Assert.True(ready >= 0 && owner > ready && transport > owner);
        Assert.Contains("requestOwner: requestOwner", integration[transport..]);

        string http = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Platform/Integration/ReignHttpTransport.cs"));
        int direct = http.IndexOf("internal static async Task<DirectJsonResponse> PostDirectJsonAsync(", StringComparison.Ordinal);
        int attempt = http.IndexOf("WithCandidatesAsync(HttpMethod.Post", direct, StringComparison.Ordinal);
        int dispatch = http.IndexOf("await EnsureRequestOwnerAsync(requestOwner, \"direct transport dispatch\")", attempt, StringComparison.Ordinal);
        int send = http.IndexOf("await attempt.SendAsync(client, request)", dispatch, StringComparison.Ordinal);
        Assert.True(direct >= 0 && attempt > direct && dispatch > attempt && send > dispatch);
    }

    [Fact]
    public void DecisionEvidenceHandlesNonInternationalJsonNullAndChecksResolvedEffects()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("JObject international = native?[\"international\"] as JObject", native, StringComparison.Ordinal);
        Assert.DoesNotContain("native?[\"international\"]?[\"nativeDiplomaticAction\"]", native, StringComparison.Ordinal);
        Assert.Contains("else if (decisionExpectation == \"resolved\")", native, StringComparison.Ordinal);
        Assert.Contains("AddCourtLifeDecisionAssertions(court, decisionAssertions, marker, matter)", native, StringComparison.Ordinal);
    }

    [Fact]
    public void DiplomaticCompletionCatalogRequiresIndependentNativeCompletion()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var completion = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeDiplomaticCompletion");
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        foreach (var assertion in completion.GetProperty("assertions").EnumerateArray())
            Assert.Contains(assertion.GetString()!, native);
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
            Assert.Contains("nativeDiplomaticAction", TestingDocumentation.Read(Path.Combine(root, file)));
        Assert.Contains("government_hearing_pending", completion.GetProperty("contract").GetString());
        Assert.Contains("pending_diplomacy_confirmation_has_no_settlement_effects", completion.GetProperty("pendingConfirmation").GetString());
        Assert.Contains("pending_diplomacy_confirmation_has_no_settlement_effects", native);
        string government = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Government/Campaign/ReignGovernmentBusinessAdapters.cs"));
        foreach (string field in new[] { "governmentBusinessId", "governmentKingdomId", "governmentStatus" })
        {
            Assert.Contains(field, completion.GetProperty("governmentAuthority").GetString());
            Assert.Contains(field, government);
        }
    }

    [Theory]
    [InlineData("court_life_preflight", "International", -1, -1, false, "", "")]
    [InlineData("court_life_prepare", "Family", -1, -1, false, "", "")]
    [InlineData("court_life_prepare", "International", 0, -1, false, "", "")]
    [InlineData("court_life_prepare", "Patronage", -1, 50, false, "", "")]
    [InlineData("court_life_prepare", "International", -1, -1, true, "", "")]
    [InlineData("court_life_prepare", "International", -1, -1, false, "captive", "")]
    [InlineData("court_life_prepare", "International", -1, -1, false, "", "participant")]
    public async Task ExistingBindingRejectsIncompatibleInputsBeforeCampaignAccess(string profile, string source,
        int gold, int loyalty, bool age, string captive, string participant)
    {
        var options = new ReignMcpOptions { WorkspaceRoot = TestOptions.FindWorkspace(), BuildRoot = Path.GetTempPath(),
            ServerBaseUri = new Uri("http://127.0.0.1:5101"), AllowVerificationControl = true };
        var error = await Assert.ThrowsAsync<ArgumentException>(() => TestingTools.StartRulerDocketTest(null!, options, null!,
            "campaign", "owned-run", profile: profile, confirmation: "start Reign ruler docket test on disposable save",
            courtLifeSource: source, courtLifeExistingMatterId: "exact_matter", courtLifeFixtureGold: gold,
            courtLifeFixtureLoyalty: loyalty, courtLifeFixtureAgeRulerTo34: age,
            courtLifeFixtureCaptiveHeroId: captive, courtLifeHeroId: participant));
        Assert.Contains("Existing matter binding", error.Message);
    }

    [Fact]
    public void ExistingBindingCatalogDocumentsExactIdentityAndIndependentNativeProof()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var binding = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeExistingMatter");
        var input = typeof(TestingTools).GetMethod(nameof(TestingTools.StartRulerDocketTest))!.GetParameters()
            .Single(p => p.Name == binding.GetProperty("input").GetString());
        Assert.Equal("", input.DefaultValue);
        Assert.True(TestingTools.GetRulerDocketTestManifest().ContainsKey("courtLifeExistingMatter"));
        foreach (string file in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
        {
            string document = TestingDocumentation.Read(Path.Combine(root, file));
            Assert.Contains("courtLifeExistingMatterId", document);
            Assert.Contains("pendingMatterInventory", document);
        }
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("existing_matter_binding_preserves_matter_and_native_state", native);
        Assert.Contains("[\"limit\"] = 100", native);
        Assert.Contains("payload[\"player\"]?.Value<string>(\"leaderHeroId\") == Hero.MainHero?.StringId", native);
    }

    [Fact]
    public void PersistenceContractKeepsFamilyValuesWhileIgnoringDatabaseRowOrder()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFingerprintContract").GetString()!;
        Assert.Contains("hero_id", contract, StringComparison.Ordinal);
        Assert.Contains("visit_id", contract, StringComparison.Ordinal);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        foreach (string assertion in new[] { "family_row_order_does_not_change_fingerprint", "family_value_change_changes_fingerprint" })
        {
            Assert.Contains(assertion, contract, StringComparison.Ordinal);
            Assert.Contains(assertion, native, StringComparison.Ordinal);
        }
        Assert.Contains("AddCourtLifeFingerprintContractAssertions(assertions)", native, StringComparison.Ordinal);
    }

    [Fact]
    public void VisitorPreparationContractRequiresImmediateNativePlacement()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string timing = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeTimeContract").GetString()!;
        Assert.Contains("immediately", timing, StringComparison.Ordinal);
        Assert.DoesNotContain("advancement to arrival", timing, StringComparison.Ordinal);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        Assert.Contains("visitors_placed_immediately_without_time_advance", native, StringComparison.Ordinal);
        string visitors = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignNobleVisitorCampaignBehavior.cs"));
        Assert.DoesNotContain("AmbassadorTravelDays", visitors, StringComparison.Ordinal);
        Assert.DoesNotContain("now < stay.ArrivalDay", visitors, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDatesRetainNativePrecisionThroughEligibilityAndEvidence()
    {
        string root = TestOptions.FindWorkspace();
        string directory = Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign");
        foreach (string file in new[] { "ReignCourtLifeCampaignBehavior.cs", "ReignCourtLifeInGameTests.cs",
            "ReignCourtLifeFamilyFixture.cs", "ReignNobleVisitorCampaignBehavior.cs",
            "ReignPatronageCampaignBehavior.cs", "ReignPatronageHistory.cs" })
        {
            string source = File.ReadAllText(Path.Combine(directory, file));
            Assert.DoesNotContain("CurrentDayFloat()", source, StringComparison.Ordinal);
            Assert.Contains("CurrentCourtLifeDay()", source, StringComparison.Ordinal);
        }
        string behavior = File.ReadAllText(Path.Combine(directory, "ReignCourtLifeCampaignBehavior.cs"));
        Assert.Contains("internal static double CurrentCourtLifeDay() => CampaignTime.Now.ToDays;", behavior, StringComparison.Ordinal);
        Assert.Contains("x.AvailableDay <= CurrentCourtLifeDay()", behavior, StringComparison.Ordinal);
        Assert.Contains("matter.AvailableDay > CurrentCourtLifeDay()", behavior, StringComparison.Ordinal);
        Assert.Contains("CurrentCourtLifeDay() >= matter.ExpiresDay", behavior, StringComparison.Ordinal);
    }

    [Fact]
    public void PatronageSpeakerContextIsBoundToTheCurrentCommission()
    {
        string root = TestOptions.FindWorkspace();
        string patronage = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignPatronageCampaignBehavior.cs"));
        Assert.Contains("This audience concerns only the current commission", patronage, StringComparison.Ordinal);
        Assert.Contains("Do not introduce a subject, passage, attribution question, proposed line, objective, or dilemma from another patronage commission", patronage, StringComparison.Ordinal);
        Assert.Contains("Current commission premise:", patronage, StringComparison.Ordinal);
        Assert.Contains("explicitly label it as fiction before narrating it", patronage, StringComparison.Ordinal);
        Assert.Contains("Never state or imply that invented events actually happened in the ruler's reign", patronage, StringComparison.Ordinal);
        Assert.Contains("PrivateContext = BuildPatronagePrivateContext(template, state, creator.ActorId, ruler)", patronage, StringComparison.Ordinal);
        Assert.Contains("if (!matter.IsPending) continue;", patronage, StringComparison.Ordinal);
        Assert.Contains("participant.PrivateContext = BuildPatronagePrivateContext(template, state, participant.ActorId, Hero.MainHero)", patronage, StringComparison.Ordinal);
    }

    private static readonly string[] Profiles = { "court_life_preflight", "court_life_family_fixture", "court_life_prepare", "court_life_open", "court_life_converse",
        "court_life_paid_supply_preflight", "court_life_paid_supply_apply", "court_life_paid_supply_verify", "court_life_choose_confirm", "court_life_snapshot", "court_life_observe", "court_life_clock_observe", "court_life_save_prepare", "court_life_save_verify" };

    [Fact]
    public void ManifestCatalogAndNativeHandlersAgreeOnCourtLifeProfiles()
    {
        var manifest = TestingTools.GetRulerDocketTestManifest();
        string[] profiles = Assert.IsType<string[]>(manifest["profiles"]);
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        var docket = catalog.RootElement.GetProperty("rulerDocket");
        string[] catalogProfiles = docket.GetProperty("profiles").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Equal(Profiles.Order(), profiles.Where(x => x.StartsWith("court_life_", StringComparison.Ordinal)).Order());
        Assert.Equal(Profiles.Order(), catalogProfiles.Where(x => x.StartsWith("court_life_", StringComparison.Ordinal)).Order());
        Assert.Equal("reign-court-life-evidence-v1", docket.GetProperty("courtLifeEvidenceSchema").GetString());
        Assert.Equal("reign-court-life-evidence-v1", manifest["courtLifeEvidenceSchema"]);
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        string guide = TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md"));
        string[] paidProfiles = Profiles.Where(x => x.StartsWith("court_life_paid_supply_", StringComparison.Ordinal)).ToArray();
        var paidCatalog = catalog.RootElement.GetProperty("courtSupplyTransfer").GetProperty("paidRecovery").GetProperty("nativeProfiles");
        Assert.Equal(paidProfiles.Order(), paidCatalog.EnumerateArray().Select(x => x.GetString()!).Order());
        Assert.Equal(paidProfiles.Order(), Profiles.Where(ReignRelationshipNativePullWire.IsPaidCourtPhase).Order());
        Assert.False(ReignRelationshipNativePullWire.IsPaidCourtPhase("court_life_paid_supply_apply_extra"));

        foreach (string profile in Profiles)
        {
            if (!ReignRelationshipNativePullWire.IsPaidCourtPhase(profile))
                Assert.Contains(profile, native + host, StringComparison.Ordinal);
            Assert.Contains(profile, guide, StringComparison.Ordinal);
        }

        string poll = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionTestHost.cs"));
        int operation = poll.IndexOf("if (ReignRelationshipNativePullWire.PaidCourtText(command, \"operation\") == \"ruler_docket_test\"", StringComparison.Ordinal);
        int phase = poll.IndexOf("&& ReignRelationshipNativePullWire.IsPaidCourtPhase(ReignRelationshipNativePullWire.PaidCourtText(command, \"phase\"))", StringComparison.Ordinal);
        int dispatch = poll.IndexOf("await ExecutePaidCourtCommandAsync(", StringComparison.Ordinal);
        Assert.True(operation >= 0 && operation < phase && phase < dispatch);

        int rejectPaid = host.IndexOf("if (ReignRelationshipNativePullWire.IsPaidCourtPhase(phase))", StringComparison.Ordinal);
        int ordinaryGate = host.IndexOf("ReignCourtTestControls.RunRulerDocketTestProfile(", StringComparison.Ordinal);
        Assert.True(rejectPaid >= 0 && rejectPaid < ordinaryGate);
        Assert.Contains("return LiveCommandResult.Failed(\"Paid Court acceptance requires a fresh acknowledged local admission.\")",
            host[rejectPaid..ordinaryGate], StringComparison.Ordinal);
    }

    [Fact]
    public void HouseholdFixtureRetainsNativeCreationAndDisposableGuards()
    {
        string root = TestOptions.FindWorkspace();
        string fixture = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeFamilyFixture.cs"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        Assert.Equal("court_life_family_fixture", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("profile").GetString());
        Assert.Contains("RulerDocketFixtureConfirmation", fixture, StringComparison.Ordinal);
        Assert.Contains("IsCoupleSuitableForMarriage", fixture, StringComparison.Ordinal);
        Assert.Contains("MarriageAction.Apply", fixture, StringComparison.Ordinal);
        Assert.Contains("HeroCreator.CreateChild", fixture, StringComparison.Ordinal);
        Assert.Contains("child.IsNotSpawned", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixtureChildren", fixture, StringComparison.Ordinal);
        Assert.Contains("father.Culture.LordTemplates", fixture, StringComparison.Ordinal);
        Assert.Contains("!father.Clan.Kingdom.IsAtWarWith(c.Kingdom)", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixtureOriginClanId", fixture, StringComparison.Ordinal);
        Assert.Contains("at peace", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("originSelection").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("child.ChangeState", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("DeliverOffSpring", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("OnGivenBirth", fixture, StringComparison.Ordinal);
        Assert.DoesNotContain("SetRelation", fixture, StringComparison.Ordinal);
        var ageOption = typeof(TestingTools).GetMethods().Single(m => m.Name == nameof(TestingTools.StartRulerDocketTest))
            .GetParameters().Single(p => p.Name == "courtLifeFixtureAgeRulerTo34");
        Assert.Equal(false, ageOption.DefaultValue);
        Assert.Contains("familyFixturePlayerAgeBefore", fixture, StringComparison.Ordinal);
        Assert.Contains("familyFixturePlayerAgeAdjusted", fixture, StringComparison.Ordinal);
        Assert.Contains("Retry must retain the original ruler-age setup option", fixture, StringComparison.Ordinal);
        Assert.Contains("courtLifeFixtureAgeRulerTo34", catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyFixture").GetProperty("ageAdjustment").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DocketClockIsolationIsDocumentedAndConfinedToEnrolledSetup()
    {
        string root = TestOptions.FindWorkspace();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeClockIsolation").GetString()!;
        Assert.Contains("Observation phases never advance or pause time", contract, StringComparison.Ordinal);
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/WorldSimulation/Campaign/ReignLiveInteractionUiCalibrationHost.cs"));
        string setup = host[..host.IndexOf("private static", host.IndexOf("private static async Task<LiveCommandResult> UiOpenAsync", StringComparison.Ordinal) + 1, StringComparison.Ordinal)];
        Assert.Contains("command.Value<string>(\"campaignTestRunId\")", setup, StringComparison.Ordinal);
        Assert.Contains("command.Value<string>(\"expectedSaveName\")", setup, StringComparison.Ordinal);
        Assert.Contains("clock.Value<double>(\"worldDayAfter\") != docketWorldDayBefore", setup, StringComparison.Ordinal);
        foreach (string document in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/README.md" })
            Assert.Contains("docketClock", TestingDocumentation.Read(Path.Combine(root, document)), StringComparison.Ordinal);
    }

    [Fact]
    public void NaturalHarnessRequiresExactVisibleMatterAndSeparateReviewedConfirmation()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        string host = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        Assert.Contains("RulerDocketFixtureConfirmation", native, StringComparison.Ordinal);
        Assert.Contains("AutomationCourtLifeMatterId", host, StringComparison.Ordinal);
        Assert.Contains("AutomationHasPersistedPlayerLine(text)", host, StringComparison.Ordinal);
        Assert.Contains("CompletedPlayerTurnIds.Count == before + 1", host, StringComparison.Ordinal);
        Assert.Contains("AutomationProviderBackedPhaseComplete(\"conversation\")", host, StringComparison.Ordinal);
        Assert.Contains("matter == null || !matter.IsPending || matter.EffectsCommitted", host, StringComparison.Ordinal);
        Assert.Contains("already resolved and cannot accept another natural turn", host, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"choose\"", native, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"confirm\"", native, StringComparison.Ordinal);
        Assert.Contains("HasPendingFamilyVisitWork", native, StringComparison.Ordinal);
        Assert.Contains("HasPendingInternationalCourtLifeWork", native, StringComparison.Ordinal);
        Assert.Contains("different_game_instance", native, StringComparison.Ordinal);
        Assert.Contains("state_fingerprint_survived_reload", native, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyCourtLifeDecision(", host + native, StringComparison.Ordinal);
        Assert.DoesNotContain("CampaignTime.Now =", host + native, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDialoguePinsEveryReplyAndStageDirectionToTheActiveSpeaker()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("context[\"activeSpeakerName\"] = person.Name", viewModel, StringComparison.Ordinal);
        Assert.Contains("You are \" + person.Name + \", the active speaker", viewModel, StringComparison.Ordinal);
        Assert.Contains("stage directions may describe only \" + person.Name + \"'s own", viewModel, StringComparison.Ordinal);
        Assert.Contains("Never attribute another attendee's gesture, clothing, equipment or action to the active speaker", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not write any stage direction that names, uses a pronoun for, or describes another attendee", viewModel, StringComparison.Ordinal);
        Assert.Contains("another person's visible reaction belongs only in that person's own reply", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not prefix the reply with a standalone speaker name, settlement or location name", viewModel, StringComparison.Ordinal);
        Assert.Contains("Keep dialogue, narration and stage directions in that same language", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ReopeningCourtLifeAudienceReusesPersistedOpeningWithoutAnotherProviderTurn()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("RecoverPersistedOpeningReceipts(matter);", viewModel, StringComparison.Ordinal);
        Assert.Contains("!HasCompleteOpeningReceipts(matter)", viewModel, StringComparison.Ordinal);
        Assert.Contains("matter.CompletedReplyKeys.Contains(\"opening|\" + person.ActorId)", viewModel, StringComparison.Ordinal);
        Assert.Contains("matter.ReplyTextsByKey[key] = matter.ConversationLines[index].Text", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeHarnessReopensOnlyTheExactPendingAudienceAfterTechnicalFailure()
    {
        string root = TestOptions.FindWorkspace();
        string host = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignLiveInteractionCourtLifeHost.cs"));
        Assert.Contains("string.Equals(providerStatus, \"technical-failure\"", host, StringComparison.Ordinal);
        Assert.Contains("AutomationCourtLifeMatterId != matter.MatterId", host, StringComparison.Ordinal);
        Assert.Contains("AutomationCanTechnicalReturn", host, StringComparison.Ordinal);
        Assert.Contains("TryExecuteAutomationAction(\"technical-return\"", host, StringComparison.Ordinal);
        Assert.Contains("TryOpenForAutomation(court, matter", host, StringComparison.Ordinal);
        Assert.Contains("technicalAudienceRecovered", host, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeDialogueNormalizesAnAccidentalDoubleTerminalPeriodWithoutFlatteningEllipses()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("visible = NormalizeAssistantReply(visible);", viewModel, StringComparison.Ordinal);
        Assert.Contains("normalized.EndsWith(\"..\", StringComparison.Ordinal)", viewModel, StringComparison.Ordinal);
        Assert.Contains("!normalized.EndsWith(\"...\", StringComparison.Ordinal)", viewModel, StringComparison.Ordinal);
        Assert.Contains("normalized.Substring(0, normalized.Length - 1)", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void InternationalDialogueDoesNotSubstituteTheRulerForAClaimantOrCargoOwner()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        Assert.Contains("The ruler is the adjudicator, not the owner, customer, traveler, debtor, claimant, accused party, or actor", viewModel, StringComparison.Ordinal);
        Assert.Contains("A possible payment from the ruler's treasury is a proposed remedy only", viewModel, StringComparison.Ordinal);
        Assert.Contains("Do not replace them with the ruler because the ruler may decide or fund the remedy", viewModel, StringComparison.Ordinal);
        Assert.Contains("is unresolved and independent of every earlier hearing", viewModel, StringComparison.Ordinal);
        Assert.Contains("Never describe this matter's money as already paid or its terms as already accepted", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void ConstructiveCatalogOffersRetainTheForeignRulersStartingAuthority()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        string decisions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocketDecisions.cs"));
        Assert.Contains("data[\"sovereignAuthorized\"] = template?.Constructive == true", docket, StringComparison.Ordinal);
        Assert.Contains("data.Property(\"sovereignAuthorized\") == null && savedTemplate?.Constructive == true", docket, StringComparison.Ordinal);
        Assert.Contains("data.Value<bool?>(\"counterProposed\") != true", docket, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(data.Value<string>(\"referralId\")) && data[\"candidate\"] == null", docket, StringComparison.Ordinal);
        Assert.Contains("A standing-charter permission marked false limits your ability to invent or independently change terms", docket, StringComparison.Ordinal);
        Assert.Contains("Do not claim that the represented ruler must approve these same exact terms again", docket, StringComparison.Ordinal);
        Assert.Contains("HasExactSovereignOffer(matter, data, option, offeredTerms)", decisions, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(InternationalOfferedTerms(data, option", docket, StringComparison.Ordinal);
        Assert.Contains("data.Value<string>(\"remedy\") != \"goodwill\"", decisions, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtLifeInterpretationCannotApplyAnOptionOrPromoteSavedProse()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        int interpret = viewModel.IndexOf("private async Task ConverseAsync(", StringComparison.Ordinal);
        int apply = viewModel.IndexOf("private bool ApplyOption(", StringComparison.Ordinal);
        Assert.True(interpret >= 0 && apply > interpret);
        Assert.DoesNotContain("_court.ApplyCourtLifeDecision(", viewModel[interpret..apply], StringComparison.Ordinal);
        Assert.Contains("_court.ApplyCourtLifeDecision(_matter, decision", viewModel[apply..], StringComparison.Ordinal);
        Assert.Contains("SameOfferedOption(interpretationPayload, selected)", viewModel[interpret..apply], StringComparison.Ordinal);
        Assert.DoesNotContain("npcAccepted", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("privateMeetingAccepted", viewModel, StringComparison.Ordinal);

        string decisions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocketDecisions.cs"));
        Assert.DoesNotContain("npcAccepted", decisions, StringComparison.Ordinal);
        Assert.DoesNotContain("acceptedPartyTiers", decisions, StringComparison.Ordinal);
        Assert.Contains("!effectCommitted && (consensus || promotedOffer) && !HasExactSovereignOffer", decisions, StringComparison.Ordinal);

        string visitors = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignNobleVisitorCampaignBehavior.cs"));
        Assert.Contains("stay.Invitations.Any(x => x.InvitationId == id)", visitors, StringComparison.Ordinal);
        Assert.Contains("NpcAccepted = false", visitors, StringComparison.Ordinal);
        Assert.DoesNotContain("agreedInvitations", visitors, StringComparison.Ordinal);

        string delivery = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignRulerDocketCampaignBehavior.cs"));
        int legacyInvitation = delivery.IndexOf("job.EventType == \"noble_visitor_invitation\"", StringComparison.Ordinal);
        int send = delivery.IndexOf("request = ReignCorrespondenceClient.RecordRulerDocketMemoryAsync(", StringComparison.Ordinal);
        Assert.True(legacyInvitation >= 0 && send > legacyInvitation);
        Assert.Contains("skipped_unconfirmed_invitation_", delivery[legacyInvitation..send], StringComparison.Ordinal);
    }

    [Fact]
    public void CourtAssentAnnotationUsesTheCurrentAcceptedSpeakerReplyAndOfferedTerms()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        int annotation = viewModel.IndexOf("private void ShowAssentProposal(", StringComparison.Ordinal);
        int snapshot = viewModel.IndexOf("private JObject CaptureReviewOption(", StringComparison.Ordinal);
        Assert.True(annotation >= 0 && snapshot > annotation);
        string display = viewModel[annotation..snapshot];
        Assert.Contains("proposal.Value<string>(\"speakerHeroId\") != person.HeroId", display, StringComparison.Ordinal);
        Assert.Contains("SameOfferedOption(payload, id)", display, StringComparison.Ordinal);
        Assert.Contains("acceptedReply.Substring(start, quote.Length)", display, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(terms, ReignCourtCampaignBehavior.ParseCourtLifeJson(option.TermsJson))", display, StringComparison.Ordinal);
        Assert.Contains("Transcript.Add(new ReignChatLineVM", display, StringComparison.Ordinal);
        Assert.Contains("This proposal is unconfirmed.", display, StringComparison.Ordinal);
        Assert.DoesNotContain("AddLine(", display, StringComparison.Ordinal);
        Assert.DoesNotContain("AcceptedDecisionJson", display, StringComparison.Ordinal);
    }

    [Fact]
    public void CourtOptionConfirmationUsesTheFrozenSnapshotOfTheDisplayedChoice()
    {
        string root = TestOptions.FindWorkspace();
        string viewModel = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/UI/ViewModels/ReignCourtLifeScreenVM.cs"));
        string activation = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeCampaignBehavior.cs"));
        string decisions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocketDecisions.cs"));
        Assert.Contains("ToDictionary(x => x.OptionId, CaptureReviewOption", viewModel, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(_reviewedOption, current)", viewModel, StringComparison.Ordinal);
        Assert.Contains("ApplyOption(_matter.SelectedOptionId, _reviewedOption[\"terms\"] as JObject)", viewModel, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(reviewedTerms, ParseCourtLifeJson(option.TermsJson))", activation, StringComparison.Ordinal);
        Assert.Contains("remedy == \"goodwill\" && data.Value<string>(\"kind\") == \"incident\"", decisions, StringComparison.Ordinal);
    }

    [Fact]
    public void TechnicalSovereignFailureDoesNotReplaceOptionsOrBecomeARefusal()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        int failure = docket.IndexOf("// Preserve the original options and exact candidate", StringComparison.Ordinal);
        Assert.True(failure >= 0);
        int pending = docket.IndexOf("matter.State = ReignCourtLifeMatterState.Pending;", failure, StringComparison.Ordinal);
        Assert.True(pending > failure);
        Assert.DoesNotContain("SetInternationalOptions", docket[failure..pending], StringComparison.Ordinal);
        Assert.DoesNotContain("declined", docket[failure..pending], StringComparison.Ordinal);

        string decisions = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocketDecisions.cs"));
        Assert.Contains("candidate?.Value<string>(\"publicReason\")", decisions, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"publicReason\"] = matter.Summary", decisions, StringComparison.Ordinal);
        Assert.Contains("sameTerms ? previousTerms.Value<string>(\"promise\")", decisions, StringComparison.Ordinal);
        Assert.Contains("if (!replayLegacy)", decisions, StringComparison.Ordinal);
        Assert.Contains("savedReply.Value<string>(\"status\") == \"model_output_unusable\"", decisions, StringComparison.Ordinal);
        Assert.Contains("terminalReply && JToken.DeepEquals(sent, candidate)", decisions, StringComparison.Ordinal);
        Assert.Contains("response.Raw.Value<bool?>(\"technicalFailure\") == true", decisions, StringComparison.Ordinal);
        Assert.Contains("data[\"replyPromoted\"] = unavailable", decisions, StringComparison.Ordinal);
        Assert.Contains("data[\"sovereignAuthorized\"] = false", decisions, StringComparison.Ordinal);
        Assert.Contains("ParseObject(savedReply.Value<string>(\"exact_terms_json\"))", decisions, StringComparison.Ordinal);
        Assert.Contains("data[\"sentReferralCandidate\"] = candidate.DeepClone()", decisions, StringComparison.Ordinal);
    }

    [Fact]
    public void SovereignCourtReplyAdmissionChecksReferralIdentityAndFrozenTerms()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        Assert.Contains("!MatchesInternationalReferral(matter, data, reply)", docket, StringComparison.Ordinal);
        Assert.Contains("reply.Value<string>(\"referral_id\") != referralId", docket, StringComparison.Ordinal);
        Assert.Contains("reply.Value<string>(\"matter_id\") != matter.MatterId", docket, StringComparison.Ordinal);
        Assert.Contains("reply.Value<string>(\"campaign_id\") != matter.CampaignId", docket, StringComparison.Ordinal);
        Assert.Contains("reply.Value<string>(\"timeline_id\") != matter.TimelineId", docket, StringComparison.Ordinal);
        Assert.Contains("reply.Value<string>(\"terms_hash\") == termsHash", docket, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(exact, offered)", docket, StringComparison.Ordinal);
        Assert.Contains("MatchesCourtNormalizedTerms(CourtMaterialValues(typedTerms)", docket, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(savedCounter, ParseObject(reply.Value<string>(\"counter_terms_json\")))", docket, StringComparison.Ordinal);
        Assert.Contains("SetSovereignReviewText(exactOption, data, bound, normalized)", docket, StringComparison.Ordinal);
        Assert.Contains("option.Label = \"Pay \" + terms.Value<int>(\"gold\")", docket, StringComparison.Ordinal);
        Assert.Contains("HasHistoricalSovereignResponder(frozenRuler, author, data.Value<string>(\"foreignRulerId\"))", docket, StringComparison.Ordinal);
        Assert.Contains("ParseObject(reply.Value<string>(\"authoritative_receipt_json\")).Value<string>(\"responderHeroId\")", docket, StringComparison.Ordinal);
        Assert.Contains("BoundSovereignCourtTerms(reply, data, normalized, disposition)", docket, StringComparison.Ordinal);
        Assert.Contains("JToken.DeepEquals(normalized, data[\"normalizedAction\"])", docket, StringComparison.Ordinal);

        string activation = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeCampaignBehavior.cs"));
        Assert.Contains("RefreshSavedInternationalOffer(matter);", activation, StringComparison.Ordinal);
        Assert.Contains("The saved sovereign terms could not be verified", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void RepeatedExtortionKeepsTheCurrentMatterSeparateFromEarlierPayments()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        Assert.Contains("current matter \" + matter.MatterId", docket, StringComparison.Ordinal);
        Assert.Contains("unresolved and distinct from every earlier demand", docket, StringComparison.Ordinal);
        Assert.Contains("A payment or settlement recorded for another matter does not satisfy this one", docket, StringComparison.Ordinal);
        Assert.Contains("Do not say this current demand was already paid", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void PoliticalIncidentAudienceRetainsTheNpcRulerChoiceAndAppliedPressure()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        Assert.Contains("continues an already generated political incident involving the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("already reduced the foreign kingdom's diplomatic pressure toward the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("already applied the incident's diplomatic pressure toward the player kingdom", docket, StringComparison.Ordinal);
        Assert.Contains("Do not say that no incident reached the ruler or that no ruling occurred", docket, StringComparison.Ordinal);
        Assert.Contains("The pressure consequence is already committed", docket, StringComparison.Ordinal);
        Assert.Contains("The player's response remains pending", docket, StringComparison.Ordinal);
        Assert.Contains("it does not create a treaty, payment, tariff exemption, caravan guarantee", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void LateForeignRulerReplyUsesItsExistingDocketReservation()
    {
        string root = TestOptions.FindWorkspace();
        string docket = File.ReadAllText(Path.Combine(root,
            "ReignBeta/src/Modules/Court/Campaign/ReignInternationalDocket.cs"));
        Assert.Contains("reservedDay.HasValue && reservedDay.Value <= CurrentDay()", docket, StringComparison.Ordinal);
        Assert.DoesNotContain("Value<int?>(\"replyReservedDay\") == CurrentDay()", docket, StringComparison.Ordinal);
    }

    [Fact]
    public void FamilyExpectationsRequireExactVisitTransitionsAndRetainedNeglectEvidence()
    {
        string root = TestOptions.FindWorkspace();
        string native = File.ReadAllText(Path.Combine(root, "ReignBeta/src/Modules/Court/Campaign/ReignCourtLifeInGameTests.cs"));
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reign.testing.json")));
        string contract = catalog.RootElement.GetProperty("rulerDocket").GetProperty("courtLifeFamilyTransitionContract").GetString()!;
        Assert.Equal(contract, TestingTools.GetRulerDocketTestManifest()["courtLifeFamilyTransitionContract"]);
        Assert.Contains("resolved or busy audience", contract, StringComparison.Ordinal);
        Assert.Contains("neither a completed turn nor decision authority", contract, StringComparison.Ordinal);
        foreach (string document in new[] { "docs/agent/TESTING_TOOL_GUIDE.md", "ReignMcp/docs/security-model.md", "ReignMcp/docs/tool-catalog.md" })
            Assert.Contains("resolved or busy audience", TestingDocumentation.Read(Path.Combine(root, document)), StringComparison.Ordinal);
        Assert.Contains("familyBaseline", native, StringComparison.Ordinal);
        Assert.Contains("SameCourtLifeFamilyObservationScope", native, StringComparison.Ordinal);
        Assert.Contains("exact_visit_new_dismissal_and_counter_increment", native, StringComparison.Ordinal);
        Assert.Contains("exact_visit_crossed_patience_into_neglect", native, StringComparison.Ordinal);
        Assert.Contains("reconciliation_follows_proven_neglect_for_exact_visit", native, StringComparison.Ordinal);
        Assert.Contains("member.Value<int?>(\"dismissals\") == before.Value<int?>(\"dismissals\") + 1", native, StringComparison.Ordinal);
        Assert.Contains("marker[\"familyNeglectEvidence\"] = current.DeepClone()", native, StringComparison.Ordinal);
        Assert.DoesNotContain("family?.Value<int?>(\"dismissals\") > 0", native, StringComparison.Ordinal);
        int snapshotStart = native.IndexOf("if (phase == \"court_life_snapshot\")", StringComparison.Ordinal);
        int snapshotEnd = native.IndexOf("if (phase == \"court_life_open\")", snapshotStart, StringComparison.Ordinal);
        Assert.DoesNotContain("StoreRulerDocketMarker", native[snapshotStart..snapshotEnd], StringComparison.Ordinal);
        Assert.Contains("familyNeglectEvidence", TestingDocumentation.Read(Path.Combine(root, "docs/agent/TESTING_TOOL_GUIDE.md")), StringComparison.Ordinal);
    }
}
