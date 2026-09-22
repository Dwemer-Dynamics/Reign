#if !REIGN_EXCLUDE_COURT
using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;

namespace ReignBeta.Campaign
{
    public static partial class ReignLiveInteractionTestHost
    {
        private static async Task<LiveCommandResult> ExecuteRulerDocketTestAsync(JObject command)
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            if (court == null) return LiveCommandResult.Failed("The production Court controller is unavailable.");
            string runId = command.Value<string>("fixtureRunId") ?? command.Value<string>("runId") ?? Guid.NewGuid().ToString("N");
            string phase = (command.Value<string>("phase") ?? "preflight").Trim().ToLowerInvariant();
            if (phase.StartsWith("court_life_", StringComparison.Ordinal))
                return await ExecuteCourtLifeTestAsync(command, court, runId, phase).ConfigureAwait(false);
            bool naturalConversationProved = false;
            bool providerBackedDialogueRequired = false;
            bool providerBackedOpeningProved = false;
            bool providerBackedConversationProved = false;
            bool providerBackedClosingProved = false;
            int replyWindowSeconds = 120;
            JObject result;
            try
            {
                bool nobleDecision = phase == "noble_decide";
                if (phase == "decide" || phase == "guardrail_insufficient" || nobleDecision)
                {
                    int expectedReplyCount = await ReignMainThread.InvokeAsync(() =>
                        ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationExpectedReplyCount).ConfigureAwait(false);
                    replyWindowSeconds = Math.Max(120, Math.Min(480,
                        Math.Max(1, expectedReplyCount) * 120));
                    DateTime openingDeadline = DateTime.UtcNow.AddSeconds(replyWindowSeconds);
                    bool canDecide = false;
                    while (DateTime.UtcNow < openingDeadline)
                    {
                        canDecide = await ReignMainThread.InvokeAsync(() =>
                            ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationCanDecide).ConfigureAwait(false);
                        if (canDecide) break;
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                    if (!canDecide)
                        return LiveCommandResult.Failed(
                            "The ruler-docket audience did not become decision-ready before its bounded opening deadline.",
                            new JObject { ["ok"] = false, ["runId"] = runId,
                                ["profile"] = "ruler_docket", ["phase"] = phase,
                                ["error"] = "The bounded AI opening reaction did not complete or the production audience closed." });

                    if (phase == "decide" || nobleDecision)
                    {
                        providerBackedDialogueRequired = nobleDecision
                            && command.Value<int?>("expectedAcceptanceTier").GetValueOrDefault(-1) >= 0;
                        if (providerBackedDialogueRequired)
                        {
                            providerBackedOpeningProved = await ReignMainThread.InvokeAsync(() =>
                                ReignBeta.UI.ReignCourtPetitionScreenManager.
                                    AutomationProviderBackedPhaseComplete("opening")).ConfigureAwait(false);
                            if (!providerBackedOpeningProved)
                            {
                                string providerStatus = await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.
                                        AutomationProviderBackedStatus).ConfigureAwait(false);
                                return LiveCommandResult.Failed(
                                    "The provider-required noble audience used an opening fallback.",
                                    new JObject { ["ok"] = false, ["runId"] = runId,
                                        ["profile"] = "ruler_docket", ["phase"] = phase,
                                        ["providerBackedDialogueRequired"] = true,
                                        ["providerBackedOpeningProved"] = false,
                                        ["providerBackedStatus"] = providerStatus,
                                        ["error"] = "Every opening statement must come from the configured provider for this language case." });
                            }
                        }
                        string rulerQuestion = nobleDecision
                            ? command.Value<string>("rulerQuestion")
                                ?? "Before I rule, each of you state the strongest fact supporting your position and answer the other side's central claim."
                            : "Before I rule, tell me how this aid will address the need you have presented.";
                        bool sent = await ReignMainThread.InvokeAsync(() =>
                            ReignBeta.UI.ReignCourtPetitionScreenManager.TryExecuteAutomationAction(
                                "send", rulerQuestion, out _)).ConfigureAwait(false);
                        if (!sent)
                            return LiveCommandResult.Failed(
                                "The natural ruler-petitioner conversation control did not accept the ruler's question.",
                                new JObject { ["ok"] = false, ["runId"] = runId,
                                    ["profile"] = "ruler_docket", ["phase"] = phase,
                                    ["error"] = "The production petition input/send route was unavailable." });

                        DateTime conversationDeadline = DateTime.UtcNow.AddSeconds(replyWindowSeconds);
                        bool completionReceiptObserved = false;
                        bool exactRulerLinePersisted = false;
                        bool decisionReadyAfterConversation = false;
                        string conversationStatus = string.Empty;
                        int conversationLineCount = 0;
                        while (DateTime.UtcNow < conversationDeadline)
                        {
                            string transcript = await ReignMainThread.InvokeAsync(() =>
                                ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationTranscript).ConfigureAwait(false);
                            conversationLineCount = nobleDecision
                                ? await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.
                                        AutomationPersistedConversationLineCount).ConfigureAwait(false)
                                : transcript.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
                            exactRulerLinePersisted = nobleDecision
                                ? await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.
                                        AutomationHasPersistedPlayerLine(rulerQuestion)).ConfigureAwait(false)
                                : transcript.IndexOf("You: " + rulerQuestion, StringComparison.Ordinal) >= 0;
                            decisionReadyAfterConversation = await ReignMainThread.InvokeAsync(() =>
                                ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationCanDecide).ConfigureAwait(false);
                            completionReceiptObserved = !nobleDecision || await ReignMainThread.InvokeAsync(() =>
                                ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationNaturalConversationComplete).ConfigureAwait(false);
                            if (nobleDecision)
                            {
                                conversationStatus = await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationNaturalConversationStatus).ConfigureAwait(false);
                                providerBackedConversationProved = await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.
                                        AutomationProviderBackedPhaseComplete("conversation")).ConfigureAwait(false);
                            }
                            naturalConversationProved = completionReceiptObserved
                                && decisionReadyAfterConversation
                                && exactRulerLinePersisted
                                && conversationLineCount >= 3
                                && (!providerBackedDialogueRequired || providerBackedConversationProved);
                            if (naturalConversationProved) break;
                            await Task.Delay(250).ConfigureAwait(false);
                        }
                        if (!naturalConversationProved)
                        {
                            bool providerFallback = providerBackedDialogueRequired
                                && !providerBackedConversationProved;
                            return LiveCommandResult.Failed(
                                providerFallback
                                    ? "The provider-required noble conversation used a deterministic fallback."
                                    : "The petitioner did not complete the bounded natural conversation turn before the decision.",
                                new JObject { ["ok"] = false, ["runId"] = runId,
                                    ["profile"] = "ruler_docket", ["phase"] = phase,
                                    ["completionReceiptObserved"] = completionReceiptObserved,
                                    ["decisionReadyAfterConversation"] = decisionReadyAfterConversation,
                                    ["exactRulerLinePersisted"] = exactRulerLinePersisted,
                                    ["conversationLineCount"] = conversationLineCount,
                                    ["conversationStatus"] = conversationStatus,
                                    ["providerBackedDialogueRequired"] = providerBackedDialogueRequired,
                                    ["providerBackedOpeningProved"] = providerBackedOpeningProved,
                                    ["providerBackedConversationProved"] = providerBackedConversationProved,
                                    ["error"] = providerFallback
                                        ? "Every natural reply must come from the configured provider for this language case."
                                        : "No completed petitioner reply was observed after the natural ruler question." });
                        }
                    }
                }
                if (phase == "snapshot" || phase == "noble_snapshot")
                {
                    DateTime artDeadline = DateTime.UtcNow.AddSeconds(240);
                    bool artComplete = false;
                    while (DateTime.UtcNow < artDeadline)
                    {
                        artComplete = await ReignMainThread.InvokeAsync(() =>
                            ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationScenePreparationComplete).ConfigureAwait(false);
                        if (artComplete) break;
                        await Task.Delay(500).ConfigureAwait(false);
                    }
                    bool artReady = await ReignMainThread.InvokeAsync(() =>
                        ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationSceneReady).ConfigureAwait(false);
                    string artError = await ReignMainThread.InvokeAsync(() =>
                        ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationScenePreparationError).ConfigureAwait(false);
                    if (!artComplete || !artReady)
                        return LiveCommandResult.Failed(
                            "The ruler-docket throne-room art did not meet its native readiness gate.",
                            new JObject { ["ok"] = false, ["runId"] = runId,
                                ["profile"] = "ruler_docket", ["phase"] = phase,
                                ["scenePreparationComplete"] = artComplete, ["sceneReady"] = artReady,
                                ["audienceArt"] = await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationSceneEvidence).ConfigureAwait(false),
                                ["error"] = string.IsNullOrWhiteSpace(artError)
                                    ? "The generated throne-room scene did not become ready before the bounded deadline."
                                    : artError });
                }
                result = await ReignMainThread.InvokeAsync(() =>
                    court.RunRulerDocketTestProfile(runId, phase, command, GameInstanceId)).ConfigureAwait(false);
                if (phase == "snapshot" || phase == "noble_snapshot")
                    result["audienceArt"] = await ReignMainThread.InvokeAsync(() =>
                        ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationSceneEvidence).ConfigureAwait(false);
                if (phase == "decide" || nobleDecision)
                {
                    result["naturalConversationProved"] = naturalConversationProved;
                    result["providerBackedDialogueRequired"] = providerBackedDialogueRequired;
                    result["providerBackedOpeningProved"] = providerBackedOpeningProved;
                    result["providerBackedConversationProved"] = providerBackedConversationProved;
                }
                if (phase == "noble_observe" && result.Value<bool?>("ok") == true)
                {
                    result = await VerifyNobleReputationEffectsAsync(result)
                        .ConfigureAwait(false);
                    if (result.Value<bool?>("ok") == true)
                        result = await VerifyNobleDirectionalRelationshipEffectsAsync(
                            court, result).ConfigureAwait(false);
                }
                if (phase == "emergency_observe" && result.Value<bool?>("ok") != true)
                {
                    DateTime deliveryDeadline = DateTime.UtcNow.AddSeconds(90);
                    while (DateTime.UtcNow < deliveryDeadline && result.Value<bool?>("ok") != true)
                    {
                        await Task.Delay(500).ConfigureAwait(false);
                        result = await ReignMainThread.InvokeAsync(() =>
                            court.RunRulerDocketTestProfile(runId, phase, command, GameInstanceId)).ConfigureAwait(false);
                    }
                }
                if ((phase == "decide" || nobleDecision) && result.Value<bool?>("ok") == true)
                {
                    DateTime deadline = DateTime.UtcNow.AddSeconds(replyWindowSeconds);
                    bool ready = false;
                    while (DateTime.UtcNow < deadline)
                    {
                        ready = await ReignMainThread.InvokeAsync(() =>
                            ReignBeta.UI.ReignCourtPetitionScreenManager.AutomationCanContinue).ConfigureAwait(false);
                        if (ready) break;
                        await Task.Delay(250).ConfigureAwait(false);
                    }
                    if (!ready)
                    {
                        result["ok"] = false;
                        result["error"] = "The bounded AI closing reaction did not complete before the production audience deadline.";
                    }
                    else
                    {
                        if (providerBackedDialogueRequired)
                        {
                            providerBackedClosingProved = await ReignMainThread.InvokeAsync(() =>
                                ReignBeta.UI.ReignCourtPetitionScreenManager.
                                    AutomationProviderBackedPhaseComplete("closing")).ConfigureAwait(false);
                            result["providerBackedClosingProved"] = providerBackedClosingProved;
                            if (!providerBackedClosingProved)
                            {
                                result["ok"] = false;
                                result["error"] = "The provider-required noble audience used a closing fallback.";
                                result["providerBackedStatus"] = await ReignMainThread.InvokeAsync(() =>
                                    ReignBeta.UI.ReignCourtPetitionScreenManager.
                                        AutomationProviderBackedStatus).ConfigureAwait(false);
                            }
                        }
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            ReignBeta.UI.ReignCourtPetitionScreenManager.TryExecuteAutomationAction("continue", out _);
                        }).ConfigureAwait(false);
                        result["closingReactionCompleted"] = true;
                        result["productionAudienceClosed"] = true;
                    }
                }
            }
            catch (Exception ex)
            {
                result = new JObject { ["ok"] = false, ["runId"] = runId,
                    ["profile"] = "ruler_docket", ["phase"] = phase, ["error"] = ex.Message };
            }
            return result.Value<bool?>("ok") == true
                ? LiveCommandResult.Completed("The ruler-docket acceptance phase completed.", result)
                : LiveCommandResult.Failed("The ruler-docket acceptance phase did not meet its gate.", result);
        }

        private static async Task<JObject> VerifyNobleDirectionalRelationshipEffectsAsync(
            ReignCourtCampaignBehavior court, JObject result)
        {
            string matterId = (result["matter"] as JObject)?.Value<string>("matterId")
                ?? string.Empty;
            JObject state = await ReignMainThread.InvokeAsync(() =>
                court.ObserveNobleDirectionalRelationshipStateForTest(matterId))
                .ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (state.Value<bool?>("required") == true
                && state.Value<bool?>("consistent") != true
                && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250).ConfigureAwait(false);
                state = await ReignMainThread.InvokeAsync(() =>
                    court.ObserveNobleDirectionalRelationshipStateForTest(matterId))
                    .ConfigureAwait(false);
            }
            result["directionalRelationshipState"] = state;
            if (state.Value<bool?>("consistent") != true)
            {
                result["ok"] = false;
                result["error"] = "The noble judgment's directional relationship effects did not match the rules table or receive durable delivery receipts.";
            }
            return result;
        }

        private static async Task<JObject> VerifyNobleReputationEffectsAsync(
            JObject result)
        {
            JArray expected = result["expectedReputationEffects"] as JArray
                ?? new JArray();
            if (expected.Count == 0)
            {
                result["nativeReputationState"] = new JObject
                {
                    ["required"] = false,
                    ["consistent"] = true,
                    ["worldHistoryFlushed"] = true,
                    ["effects"] = new JArray()
                };
                return result;
            }

            bool timelineReady = false;
            DateTime timelineDeadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < timelineDeadline)
            {
                timelineReady = await ReignMainThread.InvokeAsync(() =>
                {
                    ReignWorldHistoryCampaignBehavior history =
                        TaleWorlds.CampaignSystem.Campaign.Current
                            ?.GetCampaignBehavior<ReignWorldHistoryCampaignBehavior>()
                        ?? ReignWorldHistoryCampaignBehavior.Instance;
                    return history?.TimelineReady == true;
                }).ConfigureAwait(false);
                if (timelineReady) break;
                await Task.Delay(250).ConfigureAwait(false);
            }
            bool flushed = timelineReady && await Task.Run(() =>
                ReignWorldHistoryTransport.FlushAndWait(
                    TimeSpan.FromSeconds(30))).ConfigureAwait(false);
            string timelineId = result.Value<string>("timelineId") ?? "main";
            double worldDay = result.Value<double?>("worldDay") ?? 0d;
            JArray observed = new JArray();
            bool consistent = false;
            int observationAttempts = 0;
            DateTime observationStartedUtc = DateTime.UtcNow;
            DateTime reputationDeadline = observationStartedUtc.AddSeconds(30);
            do
            {
                observationAttempts++;
                observed = new JArray();
                consistent = flushed;
                foreach (JObject expectation in expected.OfType<JObject>())
                {
                    string heroId = expectation.Value<string>("heroId") ?? string.Empty;
                    string tagId = expectation.Value<string>("tagId") ?? string.Empty;
                    string sourceCorrelationId = expectation.Value<string>(
                        "sourceCorrelationId")
                        ?? string.Empty;
                    bool shouldBeActive = expectation.Value<bool?>("active") == true;
                    JObject status = await ReignServerClient.GetSocialCharacterStatusAsync(
                        heroId, timelineId, worldDay).ConfigureAwait(false);
                    JArray reputations = status?["reputations"] as JArray ?? new JArray();
                    JObject matchingSource = reputations.OfType<JObject>().FirstOrDefault(x =>
                        string.Equals(x.Value<string>("tag_id"), tagId,
                            StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.Value<string>(
                                "activation_source_correlation_id"),
                            sourceCorrelationId, StringComparison.Ordinal));
                    bool sourceObserved = matchingSource != null;
                    bool passed = status?.Value<bool?>("ok") == true
                        && (shouldBeActive ? sourceObserved : !sourceObserved);
                    consistent &= passed;
                    observed.Add(new JObject
                    {
                        ["heroId"] = heroId,
                        ["tagId"] = tagId,
                        ["expectedActive"] = shouldBeActive,
                        ["sourceCorrelationId"] = sourceCorrelationId,
                        ["sourceEventId"] = matchingSource?.Value<string>(
                            "activation_source_event_id") ?? string.Empty,
                        ["sourceObserved"] = sourceObserved,
                        ["passed"] = passed,
                        ["reputationValue"] = matchingSource?.Value<int?>(
                            "reputation_value"),
                        ["description"] = matchingSource?.Value<string>("description")
                            ?? string.Empty
                    });
                }
                if (consistent || !flushed || DateTime.UtcNow >= reputationDeadline)
                    break;
                // World History durability and Social Reputation projection are
                // deliberately separate workers. Wait for the authoritative
                // activation receipt instead of racing it with a single read.
                await Task.Delay(250).ConfigureAwait(false);
            }
            while (true);
            result["nativeReputationState"] = new JObject
            {
                ["required"] = true,
                ["consistent"] = consistent,
                ["timelineReadyBeforeFlush"] = timelineReady,
                ["worldHistoryFlushed"] = flushed,
                ["observationAttempts"] = observationAttempts,
                ["observationWaitMs"] = Math.Max(0d,
                    (DateTime.UtcNow - observationStartedUtc).TotalMilliseconds),
                ["effects"] = observed
            };
            if (!consistent)
            {
                result["ok"] = false;
                result["error"] = !timelineReady
                    ? "The judgment reputation events remained behind the timeline-readiness gate."
                    : flushed
                    ? "The authoritative social profile did not match every judgment-tag source."
                    : "The judgment reputation events did not flush before the bounded observer deadline.";
            }
            return result;
        }
    }
}
#endif
