#if !REIGN_EXCLUDE_COURT
using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.UI;
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;

namespace ReignBeta.WorldSimulation.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task ExecutePaidCourtCommandAsync(JObject command, ReignMainThread.GameScope nativeScope,
            string campaignId, string gameInstanceId, string origin, long pollStarted)
        {
            if (!ReignRelationshipNativePullWire.TryPaidCourtLeaseWindow(command, out var deadline)
                || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || !uri.IsLoopback || uri.Scheme != Uri.UriSchemeHttp
                || !ReignRelationshipNativePullWire.IsPaidCourtIdentifier(ReignRelationshipNativePullWire.PaidCourtText(command, "runId"))
                || !ReignRelationshipNativePullWire.IsPaidCourtIdentifier(ReignRelationshipNativePullWire.PaidCourtText(command, "commandId")))
                return;

            string process = PartyAgencyClientProcessInstanceId;
            bool Current() => _armed && nativeScope != null && ReferenceEquals(nativeScope, ReignMainThread.CurrentGameScope)
                && _gameInstanceId == gameInstanceId && ReignServerClient.GetCampaignId() == campaignId
                && origin == ReignHttpTransport.PinnableOrigin(ReignServerEndpoint.ConfiguredBaseUrl())
                && DateTimeOffset.UtcNow < deadline
                && System.Diagnostics.Stopwatch.GetTimestamp() - pollStarted <
                    ReignRelationshipNativePullWire.PaidCourtLeaseSeconds * System.Diagnostics.Stopwatch.Frequency;
            if (!Current()) return;

            JObject acknowledgment = await ReignLiveTestServerClient.SendPaidCourtCommandStatusAsync(
                origin, command, campaignId, gameInstanceId, process, "accepted", Current).ConfigureAwait(false);
            if (!Current() || !ReignRelationshipNativePullWire.MatchesPaidCourtAcknowledgment(
                acknowledgment, command, campaignId, gameInstanceId, process, false, DateTimeOffset.UtcNow))
                return;

            async Task<bool> BeginNativeDispatchAsync(Func<bool> owner, JObject terms)
            {
                JObject begin = await ReignLiveTestServerClient.SendPaidCourtCommandStatusAsync(
                    origin, command, campaignId, gameInstanceId, process, "running", owner, nativeTerms: terms).ConfigureAwait(false);
                return Current() && ReignRelationshipNativePullWire.MatchesPaidCourtAcknowledgment(
                    begin, command, campaignId, gameInstanceId, process, true, DateTimeOffset.UtcNow, terms);
            }

            LiveCommandResult result;
            try
            {
                result = await ReignMainThread.RunOwnedAsync(nativeScope, Current,
                    () => RunAdmittedPaidCourtSupplyAsync(command, campaignId, gameInstanceId, Current, BeginNativeDispatchAsync))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!Current())
            {
                return;
            }
            if (Current())
                await ReignLiveTestServerClient.SendPaidCourtCommandStatusAsync(origin, command, campaignId, gameInstanceId,
                    process, result.Status, Current, result.Message, result.Error, result.Data, result.CorrelationIds).ConfigureAwait(false);
        }

        private static async Task<LiveCommandResult> RunAdmittedPaidCourtSupplyAsync(JObject command, string campaignId,
            string gameInstanceId, Func<bool> current, Func<Func<bool>, JObject, Task<bool>> beginNativeDispatch)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var court = ReignServices.Court;
            var load = ReignSaveSyncCoordinator.CurrentVerifiedLoad;
            if (campaign == null || court == null || load == null || !current())
                return LiveCommandResult.Failed("Paid Court acceptance requires the currently loaded paused checkpoint.");
            string timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
            string saveName = ActiveSaveName();
            string point = ReignServices.SaveSync?.CurrentSavePointId ?? string.Empty;
            JObject owner = ObservePassiveWorldCurrentLoad(command, campaign, campaignId, timeline, saveName, point);
            if (owner == null || !current())
                return LiveCommandResult.Failed("Paid Court acceptance requires the currently loaded paused checkpoint.");
            JObject grant = ConsumePaidCourtSupplyGrant(command, owner);
            if (grant == null)
                return LiveCommandResult.Failed("Paid Court acceptance requires one unexpired admission for the exact checkpoint and command.");

            bool OwnsLoad() => ReignMainThread.IsMainThread && current() && ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current)
                && ReferenceEquals(court, ReignServices.Court) && ReferenceEquals(load, ReignSaveSyncCoordinator.CurrentVerifiedLoad)
                && campaign.TimeControlMode == TaleWorlds.CampaignSystem.CampaignTimeControlMode.Stop
                && ActiveSaveName() == saveName && ReignServices.SaveSync?.CurrentSavePointId == point
                && ReignServices.WorldHistory?.TimelineId == timeline
                && ReignServices.WorldHistory?.SharedActivityGeneration == load.RestoreGeneration
                && !ReignSaveSyncCampaignBehavior.IsNativeSaveInProgress
                && !ReignSaveSyncCoordinator.IsSaveFinalizationPending && !ReignSaveSyncCoordinator.IsAlignmentPending
                && ReignSaveSyncCoordinator.IsReadyForCampaign(campaignId)
                && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < grant.Value<long>("expiresUtc");

            string phase = grant.Value<string>("phase");
            string fixture = grant.Value<string>("fixtureRunId");
            JObject evidence = ReignCourtTestControls.PreparePaidSupplyAcceptance(court, fixture, phase,
                grant, saveName, gameInstanceId, OwnsLoad, out var transfer);
            if (transfer != null)
            {
                string error;
                try
                {
                    error = await court.TransferSupplyAsync(transfer.Request, transfer.OwnsNativeLeg,
                        ownsTransfer => transfer.Verify ? Task.FromResult(false) : beginNativeDispatch(ownsTransfer, JObject.FromObject(transfer.Request)),
                        transfer.MarkInvoked, transfer.OwnsExpectedResult).ConfigureAwait(false);
                }
                catch (Exception exception) { error = exception.Message; }
                await ReignMainThread.InvokeAsync(() =>
                    ReignCourtTestControls.FinishPaidSupplyAcceptance(court, transfer, evidence, error)).ConfigureAwait(false);
            }

            return evidence.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("Court paid supply acceptance evidence captured.", evidence)
                : LiveCommandResult.Failed(evidence.Value<string>("error")
                    ?? "Court paid supply acceptance did not meet its evidence gate.", evidence);
        }

        private static JObject ConsumePaidCourtSupplyGrant(JObject command, JObject owner)
        {
            string runId = ReignRelationshipNativePullWire.PaidCourtText(command, "runId");
            string commandId = ReignRelationshipNativePullWire.PaidCourtText(command, "commandId");
            if (!ReignRelationshipNativePullWire.IsPaidCourtIdentifier(runId)
                || !ReignRelationshipNativePullWire.IsPaidCourtIdentifier(commandId))
                return null;
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    SecurityIdentifier sid = identity.User;
                    string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    if (sid == null || string.IsNullOrEmpty(local)) return null;
                    string reign = Path.Combine(local, "Reign");
                    var directory = new DirectoryInfo(Path.Combine(reign, "CourtPaidSupplyAdmission-v1"));
                    if (IsPaidCourtReparsePoint(local) || IsPaidCourtReparsePoint(reign) || IsPaidCourtReparsePoint(directory.FullName)
                        || !IsPaidCourtPrivate(directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), sid, true))
                        return null;
                    string path = Path.Combine(directory.FullName, runId + "_" + commandId + ".json");
                    byte[] bytes = ReadPaidCourtSupplyGrant(path, sid);
                    if (bytes == null) return null;
                    JObject grant;
                    using (var text = new StringReader(new UTF8Encoding(false, true).GetString(bytes)))
                    using (var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None, MaxDepth = 4 })
                    {
                        grant = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                        if (reader.Read()) return null;
                    }
                    if (!MatchesPaidCourtSupplyGrant(grant, command, owner)) return null;

                    string claimed = path + ".used";
                    File.Move(path, claimed);
                    try
                    {
                        byte[] consumed = ReadPaidCourtSupplyGrant(claimed, sid);
                        return consumed != null && bytes.SequenceEqual(consumed) ? grant : null;
                    }
                    finally
                    {
                        File.Delete(claimed);
                    }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is JsonException
                || error is ArgumentException || error is InvalidOperationException || error is System.Security.SecurityException
                || error is NotSupportedException || error is OverflowException)
            {
                return null;
            }
        }

        private static bool MatchesPaidCourtSupplyGrant(JObject grant, JObject command, JObject owner)
        {
            string Text(string name) => ReignRelationshipNativePullWire.PaidCourtText(grant, name);
            if (!ReignRelationshipNativePullWire.MatchesPaidCourtGrantBinding(grant, command, owner,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
                return false;
            string prefix = Text("savePrefix"), name = Text("saveName"), baseline = Text("baselineSaveName");
            string point = Text("checkpointSavePointId"), hash = Text("checkpointSha256");
            return prefix != null && prefix.StartsWith("ReignTest_", StringComparison.Ordinal) && name == prefix + "_Current"
                && !string.IsNullOrWhiteSpace(baseline) && !string.Equals(name, baseline, StringComparison.OrdinalIgnoreCase)
                && point == owner.Value<string>("savePointId") && name == owner.Value<string>("saveName")
                && hash != null && hash.Length == 64 && hash.All(Uri.IsHexDigit)
                && NativeSaveHasPoint(name, owner.Value<string>("campaignId"), point)
                && string.Equals(NativeCheckpointHash(name), hash, StringComparison.OrdinalIgnoreCase)
                && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < grant.Value<long>("expiresUtc");
        }

        private static bool IsPaidCourtReparsePoint(string path)
            => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

        private static bool IsPaidCourtPrivate(FileSystemSecurity acl, SecurityIdentifier sid, bool protect)
            => (!protect || acl.AreAccessRulesProtected) && sid.Equals(acl.GetOwner(typeof(SecurityIdentifier)))
                && !acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>()
                    .Any(rule => rule.AccessControlType == AccessControlType.Allow && !sid.Equals(rule.IdentityReference));

        private static byte[] ReadPaidCourtSupplyGrant(string path, SecurityIdentifier sid)
        {
            var file = new FileInfo(path);
            if (IsPaidCourtReparsePoint(file.FullName)
                || !IsPaidCourtPrivate(file.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access), sid, false))
                return null;
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 1 || stream.Length > 8192) return null;
                byte[] bytes = new byte[(int)stream.Length];
                int read = 0;
                while (read < bytes.Length)
                {
                    int count = stream.Read(bytes, read, bytes.Length - read);
                    if (count == 0) return null;
                    read += count;
                }
                return stream.ReadByte() == -1 ? bytes : null;
            }
        }

        private static async Task<LiveCommandResult> ExecuteCourtLifeTestAsync(JObject command,
            ReignCourtCampaignBehavior court, string runId, string phase)
        {
            if (ReignRelationshipNativePullWire.IsPaidCourtPhase(phase))
                return LiveCommandResult.Failed("Paid Court acceptance requires a fresh acknowledged local admission.");
            JObject gate = await ReignMainThread.InvokeAsync(() => ReignCourtTestControls.RunRulerDocketTestProfile(court, runId,
                "court_life_check", command, GameInstanceId)).ConfigureAwait(false);
            if (gate.Value<bool?>("ok") != true) return LiveCommandResult.Failed("The disposable native-save gate failed.", gate);
            bool bindExisting = !string.IsNullOrEmpty(command.Value<string>("courtLifeExistingMatterId"));
            if (phase == "court_life_prepare" && !bindExisting && string.Equals(command.Value<string>("courtLifeSource"), "International", StringComparison.OrdinalIgnoreCase))
            {
                Task<bool> preparation = null;
                await ReignMainThread.InvokeAsync(() => { preparation = court.PrepareInternationalFixtureAsync(); }).ConfigureAwait(false);
                if (!await preparation.ConfigureAwait(false))
                    return LiveCommandResult.Failed("The international fixture needs a current production world and personality snapshot.");
            }
            if (phase == "court_life_prepare" && !bindExisting && string.Equals(command.Value<string>("courtLifeSource"), "Patronage", StringComparison.OrdinalIgnoreCase))
            {
                Task<bool> history = null;
                await ReignMainThread.InvokeAsync(() => { history = court.PreparePatronagePublicHistoryAsync(); }).ConfigureAwait(false);
                if (!await history.ConfigureAwait(false))
                    return LiveCommandResult.Failed("The patronage fixture requires current scoped public history from the production server.");
            }
            bool conversational = phase == "court_life_converse" || phase == "court_life_choose_confirm";
            bool recoveredTechnicalAudience = false;
            string expectedId = string.Empty;
            if (conversational || phase == "court_life_snapshot")
            {
                expectedId = await ReignMainThread.InvokeAsync(() => ReignCourtTestControls.FindCourtLifeTestMatter(court, runId)?.MatterId ?? "").ConfigureAwait(false);
                string actualId = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.AutomationCourtLifeMatterId).ConfigureAwait(false);
                if (expectedId.Length == 0 || actualId != expectedId)
                    return LiveCommandResult.Failed("The visible production audience is not the exact fixture matter.");
            }
            // A diagnostic snapshot may show a resolved or busy audience. It proves no provider turn or decision authority.
            if (conversational)
            {
                bool alreadyResolved = await ReignMainThread.InvokeAsync(() =>
                {
                    ReignCourtLifeMatter matter = ReignCourtTestControls.FindCourtLifeTestMatter(court, runId);
                    return matter == null || !matter.IsPending || matter.EffectsCommitted;
                }).ConfigureAwait(false);
                if (alreadyResolved)
                    return LiveCommandResult.Failed("The exact fixture matter is already resolved and cannot accept another natural turn.");
                string providerStatus = await ReignMainThread.InvokeAsync(() =>
                    ReignCourtPetitionScreenManager.AutomationProviderBackedStatus).ConfigureAwait(false);
                if (string.Equals(providerStatus, "technical-failure", StringComparison.OrdinalIgnoreCase))
                {
                    string recoveryError = await ReignMainThread.InvokeAsync(() =>
                    {
                        ReignCourtLifeMatter matter = ReignCourtTestControls.FindCourtLifeTestMatter(court, runId);
                        if (matter == null || !matter.IsPending || matter.EffectsCommitted)
                            return "The exact pending fixture matter is no longer available.";
                        if (ReignCourtPetitionScreenManager.AutomationCourtLifeMatterId != matter.MatterId)
                            return "The visible production audience changed before recovery.";
                        if (!ReignCourtPetitionScreenManager.AutomationCanTechnicalReturn)
                            return "The failed production audience cannot return safely.";
                        if (!ReignCourtPetitionScreenManager.TryExecuteAutomationAction("technical-return", out string returnError))
                            return string.IsNullOrWhiteSpace(returnError) ? "The failed production audience could not close." : returnError;
                        if (!ReignCourtPetitionScreenManager.TryOpenForAutomation(court, matter, out string openError))
                            return string.IsNullOrWhiteSpace(openError) ? "The exact production audience could not reopen." : openError;
                        return ReignCourtPetitionScreenManager.AutomationCourtLifeMatterId == matter.MatterId
                            ? string.Empty
                            : "The reopened production audience is not the exact fixture matter.";
                    }).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(recoveryError))
                        return LiveCommandResult.Failed("The exact technical-failure audience could not be recovered: " + recoveryError);
                    recoveredTechnicalAudience = true;
                }
                int speakers = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.AutomationExpectedReplyCount).ConfigureAwait(false);
                int timeout = Math.Max(120, Math.Min(480, speakers * 120));
                DateTime deadline = DateTime.UtcNow.AddSeconds(timeout);
                bool ready = false;
                while (DateTime.UtcNow < deadline)
                {
                    ready = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.AutomationCanDecide
                        && ReignCourtPetitionScreenManager.AutomationProviderBackedPhaseComplete("opening")).ConfigureAwait(false);
                    if (ready) break;
                    await Task.Delay(250).ConfigureAwait(false);
                }
                if (!ready) return LiveCommandResult.Failed("A complete provider-backed opening was not observed before the bounded deadline.");
                if (phase == "court_life_converse")
                {
                    string text = command.Value<string>("rulerQuestion") ?? "Tell me what brings you to court and what you have in mind.";
                    int before = await ReignMainThread.InvokeAsync(() => ReignCourtTestControls.FindCourtLifeTestMatter(court, runId).CompletedPlayerTurnIds.Count).ConfigureAwait(false);
                    bool sent = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.TryExecuteAutomationAction("send", text, out _)).ConfigureAwait(false);
                    if (!sent) return LiveCommandResult.Failed("The production input and send control did not accept the ruler's words.");
                    bool replied = false;
                    deadline = DateTime.UtcNow.AddSeconds(timeout);
                    while (DateTime.UtcNow < deadline)
                    {
                        replied = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.AutomationCourtLifeMatterId == expectedId
                            && ReignCourtPetitionScreenManager.AutomationNaturalConversationComplete
                            && ReignCourtPetitionScreenManager.AutomationProviderBackedPhaseComplete("conversation")
                            && ReignCourtPetitionScreenManager.AutomationHasPersistedPlayerLine(text)
                            && ReignCourtTestControls.FindCourtLifeTestMatter(court, runId).CompletedPlayerTurnIds.Count == before + 1).ConfigureAwait(false);
                        if (replied) break;
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                    JObject result = await ReignMainThread.InvokeAsync(() => ReignCourtTestControls.RunRulerDocketTestProfile(court, runId,
                        "court_life_observe", command, GameInstanceId)).ConfigureAwait(false);
                    result["phase"] = phase; result["naturalConversationProved"] = replied;
                    result["providerBackedOpeningProved"] = true; result["providerBackedConversationProved"] = replied;
                    result["technicalAudienceRecovered"] = recoveredTechnicalAudience;
                    result["exactPlayerText"] = text; result["usedProductionInputAndSend"] = sent;
                    result["ok"] = result.Value<bool?>("ok") == true && replied;
                    return result.Value<bool?>("ok") == true ? LiveCommandResult.Completed("The natural court-life turn completed.", result)
                        : LiveCommandResult.Failed("The exact natural turn did not meet its persisted provider-backed evidence gate.", result);
                }
            }
            JObject evidence = await ReignMainThread.InvokeAsync(() => ReignCourtTestControls.RunRulerDocketTestProfile(court, runId,
                phase, command, GameInstanceId)).ConfigureAwait(false);
            evidence["technicalAudienceRecovered"] = recoveredTechnicalAudience;
            return evidence.Value<bool?>("ok") == true ? LiveCommandResult.Completed("The court-life acceptance phase completed.", evidence)
                : LiveCommandResult.Failed("The court-life acceptance phase did not meet its evidence gate.", evidence);
        }
    }
}
#endif
