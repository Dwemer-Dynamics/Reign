#if !REIGN_EXCLUDE_COURT
using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteCourtLifeTestAsync(JObject command,
            ReignCourtCampaignBehavior court, string runId, string phase)
        {
            JObject gate = await ReignMainThread.InvokeAsync(() => court.RunRulerDocketTestProfile(runId,
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
                expectedId = await ReignMainThread.InvokeAsync(() => court.FindCourtLifeTestMatter(runId)?.MatterId ?? "").ConfigureAwait(false);
                string actualId = await ReignMainThread.InvokeAsync(() => ReignCourtPetitionScreenManager.AutomationCourtLifeMatterId).ConfigureAwait(false);
                if (expectedId.Length == 0 || actualId != expectedId)
                    return LiveCommandResult.Failed("The visible production audience is not the exact fixture matter.");
            }
            // A diagnostic snapshot may show a resolved or busy audience. It proves no provider turn or decision authority.
            if (conversational)
            {
                bool alreadyResolved = await ReignMainThread.InvokeAsync(() =>
                {
                    ReignCourtLifeMatter matter = court.FindCourtLifeTestMatter(runId);
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
                        ReignCourtLifeMatter matter = court.FindCourtLifeTestMatter(runId);
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
                    int before = await ReignMainThread.InvokeAsync(() => court.FindCourtLifeTestMatter(runId).CompletedPlayerTurnIds.Count).ConfigureAwait(false);
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
                            && court.FindCourtLifeTestMatter(runId).CompletedPlayerTurnIds.Count == before + 1).ConfigureAwait(false);
                        if (replied) break;
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                    JObject result = await ReignMainThread.InvokeAsync(() => court.RunRulerDocketTestProfile(runId,
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
            JObject evidence = await ReignMainThread.InvokeAsync(() => court.RunRulerDocketTestProfile(runId,
                phase, command, GameInstanceId)).ConfigureAwait(false);
            evidence["technicalAudienceRecovered"] = recoveredTechnicalAudience;
            return evidence.Value<bool?>("ok") == true ? LiveCommandResult.Completed("The court-life acceptance phase completed.", evidence)
                : LiveCommandResult.Failed("The court-life acceptance phase did not meet its evidence gate.", evidence);
        }
    }
}
#endif
