using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using static ReignBeta.Integration.ReignServerClient;

namespace ReignBeta.Integration
{
    internal static class ReignStructuredActionClient
    {
        // Prepare and complete on the campaign thread. Alternate conversation hosts
        // use this same boundary without granting a response from an older load authority.
        // inlineReceipt keeps the established reply-appended receipt for unaffected hosts
        // (Tavern House, court audience). Hosts that present receipts as separate system
        // lines pass false and read structuredActionReceipt through NativeStatusLines.
        internal static Action<JObject> BeginStructuredActionExchange(JObject payload, Hero npc, ActionAgreement draft = null, bool tavernActions = false,
            bool inlineReceipt = true)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var behavior = ReignAgreementBehavior.Instance;
            string campaignId = GetCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            if (TavernHouseVisitAction.Contains(draft) && !tavernActions)
                throw new InvalidOperationException("This visit action belongs only to the Tavern House conversation.");
            ConfigureStructuredActions(payload, npc, draft, AgreementChannel.InPerson);
            return response =>
            {
                if (campaign != TaleWorlds.CampaignSystem.Campaign.Current || behavior != ReignAgreementBehavior.Instance
                    || campaignId != GetCampaignId() || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                { response["ok"] = false; response["error"] = "The campaign changed before the action response arrived."; return; }
                ProcessStructuredActionDecision(payload, response, npc,
                    payload.Value<string>("requestId") ?? payload.Value<string>("turnId") ?? payload.Value<string>("commandId") ?? "", tavernActions,
                    inlineReceipt);
            };
        }
        // Called on the campaign thread. Only the locally attached composer draft
        // supplies executable terms. Speech may rank discovery assets, never authorize them.
        internal static void ConfigureStructuredActions(JObject payload, Hero npc, ActionAgreement draft,
            AgreementChannel channel)
        {
            if (!Enum.IsDefined(typeof(AgreementChannel), channel)) throw new InvalidOperationException("Unknown native conversation channel.");
            string playerId = Hero.MainHero?.StringId ?? "", npcId = npc?.StringId ?? "";
            payload["playerHeroStringId"] = playerId;
            payload["speakerHeroStringId"] = npcId;
            string contextId = ReignActionConversationPolicy.ConversationContextId(GetCampaignId(),
                ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", npcId);
            if (draft != null && npc?.IsChild == true) throw new InvalidOperationException("Gameplay bargaining requires an adult participant.");
            var behavior = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("The explicit action service is unavailable.");
            var offer = behavior.PrepareOffer(draft, playerId, npcId, contextId, out var receipt, channel);
            var context = new JObject { ["playerId"] = playerId, ["npcId"] = npcId, ["conversationId"] = contextId,
                ["worldDay"] = ReignActionService.Now, ["adultParticipant"] = npc?.IsChild != true, ["channel"] = (int)channel };
            if (offer != null) { context["proposal"] = JObject.FromObject(offer); context["termsHash"] = offer.TermsHash(); }
            if (receipt != null) context["nativeReceipt"] = JObject.FromObject(receipt);
            context["fulfilledPaymentHistory"] = CaptureFulfilledPaymentHistory(playerId, npcId);
            // Actual saved journals, distinct from model proposals and queue rows.
            // Bound the prompt view; the native journals themselves remain intact.
            var journals = System.Linq.Enumerable.Where(behavior.Agreements, j => j.Agreement.PlayerId == playerId && j.Agreement.NpcId == npcId
                && j.Agreement.Id != receipt?.OperationId && (j.Agreement.State == AgreementState.Reserved || j.Agreement.State == AgreementState.Waiting
                    || j.Agreement.State == AgreementState.InTransit || j.Agreement.State == AgreementState.RecoveryRequired));
            if (System.Linq.Enumerable.Any(journals)) context["agreementJournal"] = new JObject {
                ["schema"] = "reign-agreement-journal-view-v1", ["total"] = System.Linq.Enumerable.Count(journals),
                ["truncated"] = System.Linq.Enumerable.Count(journals) > 16,
                ["rows"] = JArray.FromObject(System.Linq.Enumerable.Select(System.Linq.Enumerable.Take(
                    System.Linq.Enumerable.ThenBy(System.Linq.Enumerable.OrderByDescending(journals, j => j.Agreement.CreatedDay),
                        j => j.Agreement.Id, StringComparer.Ordinal), 16), j => behavior.Status(j.Agreement.Id)))
            };
            if (ReignSpouseSeparationAction.Available(npc)) context["currentMarriage"] = new JObject {
                ["actorId"] = npcId, ["spouseId"] = npc.Spouse.StringId,
                ["spouseName"] = npc.Spouse.Name.ToString(), ["spouseClanId"] = npc.Spouse.Clan?.StringId ?? "",
                ["spouseClanName"] = npc.Spouse.Clan?.Name.ToString() ?? "", ["relationLoss"] = ReignSpouseSeparation.RelationLoss
            };
            if (draft != null && AgreementRules.IsPlayerDeclaration(draft) && receipt != null)
                context["playerDeclaration"] = draft.Sentence;
            if (npc?.IsChild != true) context["appraisals"] = ReignActionQuotes.Context(playerId, npcId, offer,
                payload.Value<string>("playerText") ?? payload.Value<string>("text") ?? payload.Value<string>("message") ?? "");
            if (npc?.IsChild != true)
                context["governmentPetitions"] = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance
                    ?.PetitionNegotiationContext(npc, Hero.MainHero, offer);
            payload["structuredActionProtocolVersion"] = ActionNegotiation.ProtocolVersion;
            payload["structuredActionContext"] = context;
        }

        // Arrival is observation, not a bargaining turn. Supply the same verified
        // payment facts without preparing offers or enabling action decisions.
        internal static void AttachReadOnlyAgreementPaymentHistory(JObject payload, Hero npc)
        {
            string playerId = Hero.MainHero?.StringId ?? "", npcId = npc?.StringId ?? "";
            payload["structuredActionContext"] = new JObject {
                ["playerId"] = playerId, ["npcId"] = npcId,
                ["fulfilledPaymentHistory"] = CaptureFulfilledPaymentHistory(playerId, npcId)
            };
        }

        private static JObject CaptureFulfilledPaymentHistory(string playerId, string npcId)
        {
            var payments = ReignAgreementPaymentFacts.Capture(ReignAgreementBehavior.Instance?.Agreements, playerId, npcId);
            return new JObject {
                ["schema"] = AgreementPaymentHistory.Schema, ["source"] = "native_agreement_journal",
                ["total"] = payments.Total, ["truncated"] = payments.Truncated,
                ["rows"] = new JArray(System.Linq.Enumerable.Select(payments.Rows, payment => new JObject {
                    ["agreementId"] = payment.AgreementId, ["clauseId"] = payment.ClauseId,
                    ["termsHash"] = payment.TermsHash, ["status"] = "applied",
                    ["fromHeroStringId"] = payment.FromHeroId, ["toHeroStringId"] = payment.ToHeroId,
                    ["amount"] = payment.Amount, ["quantityPerInstallment"] = payment.QuantityPerInstallment,
                    ["fulfilledInstallments"] = payment.FulfilledInstallments,
                    ["scheduledInstallments"] = payment.ScheduledInstallments,
                    ["stepStartedDay"] = payment.StepStartedDay,
                    ["unusedReservationReturned"] = payment.UnusedReservationReturned,
                    ["associatedActionIds"] = new JArray(payment.AssociatedActionIds)
                }))
            };
        }

        internal static void ProcessStructuredActionDecision(JObject request, JObject response, Hero npc, string responseId = "", bool tavernActions = false,
            bool inlineReceipt = true)
        {
            if (response == null || response.Value<bool?>("ok") != true || response.Value<bool?>("historicalReplay") == true) return;
            var context = request["structuredActionContext"] as JObject;
            if (context == null) return;
            string message;
            try
            {
                var envelope = response["structuredActionDecision"] as JObject;
                if (envelope?.Value<bool?>("valid") != true)
                {
                    if (context["proposal"] == null) return;
                    throw new InvalidOperationException(envelope?.Value<string>("error") ?? "No verified action decision was returned.");
                }
                var decision = envelope["decision"]?.ToObject<ActionDecision>() ?? throw new InvalidOperationException("The typed decision is missing.");
                if (!Enum.IsDefined(typeof(AgreementChannel), context.Value<int?>("channel") ?? 0))
                    throw new InvalidOperationException("Unknown retained native conversation channel.");
                if (npc?.IsChild == true && decision.Kind != "none") throw new InvalidOperationException("This participant cannot authorize gameplay bargains.");
                if (context.Value<string>("playerId") != Hero.MainHero?.StringId || context.Value<string>("npcId") != npc?.StringId)
                    throw new InvalidOperationException("The conversation participants changed.");
                var pendingTavern = context["proposal"]?.ToObject<ActionAgreement>();
                var candidate = decision.Kind == "propose" || decision.Kind == "counter"
                    ? ActionNegotiation.CreateProposal(pendingTavern, decision, context.Value<string>("playerId"), context.Value<string>("npcId"), context.Value<string>("conversationId"), ReignActionService.Now)
                    : pendingTavern;
                if ((TavernHouseVisitAction.Contains(candidate) || TavernHouseVisitAction.Contains(pendingTavern)) && !tavernActions)
                    throw new InvalidOperationException("Visit actions are unavailable outside the dedicated Tavern House conversation.");
                if (response.Value<string>("preparedVisitError") is string preparationError && preparationError.Length > 0)
                {
                    ReignAgreementBehavior.Instance.InvalidateUnpaidTavernOffer(pendingTavern);
                    throw new InvalidOperationException(preparationError);
                }
                if (TavernHouseVisitAction.Contains(pendingTavern) && pendingTavern.HasBothConsents && (decision.Kind == "none" || decision.Kind == "accept"))
                {
                    message = ReignAgreementBehavior.Instance.CommitPreparedTavern(pendingTavern).Message;
                }
                else message = ReignAgreementBehavior.Instance.ReceiveDecision(context["proposal"]?.ToObject<ActionAgreement>(), decision,
                    context.Value<string>("playerId"), context.Value<string>("npcId"), context.Value<string>("conversationId"),
                    string.IsNullOrEmpty(responseId) ? request.Value<string>("correlationId") ?? request.Value<string>("sceneTurnId") : responseId,
                    (AgreementChannel)(context.Value<int?>("channel") ?? 0));
            }
            catch (Exception ex) { message = "Action not applied: " + ex.Message; }
            if (string.IsNullOrWhiteSpace(message)) return;
            response["structuredActionReceipt"] = message;
            if (inlineReceipt) response["reply"] = (response.Value<string>("reply") ?? "") + "\n\n[" + message + "]";
        }

        // Existing structured receipt and native action outcomes as separate system lines.
        // The NPC reply text is never modified; only executor-supplied messages are shown.
        internal static List<string> NativeStatusLines(JObject response)
        {
            var lines = new List<string>();
            string receipt = response?.Value<string>("structuredActionReceipt");
            if (!string.IsNullOrWhiteSpace(receipt)) lines.Add("[" + receipt + "]");
            foreach (JObject outcome in (response?["nativeActionOutcomes"] as JArray ?? new JArray()).OfType<JObject>())
                lines.Add("[" + (outcome.Value<string>("status") ?? "pending") + ": " + (outcome.Value<string>("message") ?? "") + "]");
            return lines;
        }

        // An accepted reply whose only content is an action receipt or outcome is still a
        // presented exchange, as it was when those receipts were appended to the reply text.
        internal static bool HasNativeStatus(JObject response) => NativeStatusLines(response).Count > 0;
    }
}
