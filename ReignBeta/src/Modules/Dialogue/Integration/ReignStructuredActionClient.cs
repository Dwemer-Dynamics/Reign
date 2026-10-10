using System;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        // Prepare and complete on the campaign thread. Alternate conversation hosts
        // use this same boundary without granting a response from an older load authority.
        internal static Action<JObject> BeginStructuredActionExchange(JObject payload, Hero npc, ActionAgreement draft = null)
        {
            var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
            var behavior = ReignAgreementBehavior.Instance;
            string campaignId = GetCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            ConfigureStructuredActions(payload, npc, draft);
            return response =>
            {
                if (campaign != TaleWorlds.CampaignSystem.Campaign.Current || behavior != ReignAgreementBehavior.Instance
                    || campaignId != GetCampaignId() || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                { response["ok"] = false; response["error"] = "The campaign changed before the action response arrived."; return; }
                ProcessStructuredActionDecision(payload, response, npc,
                    payload.Value<string>("requestId") ?? payload.Value<string>("turnId") ?? payload.Value<string>("commandId") ?? "");
            };
        }
        // Called on the campaign thread. Only the locally attached composer draft
        // supplies executable terms. Speech may rank discovery assets, never authorize them.
        private static void ConfigureStructuredActions(JObject payload, Hero npc, ActionAgreement draft)
        {
            string playerId = Hero.MainHero?.StringId ?? "", npcId = npc?.StringId ?? "";
            payload["playerHeroStringId"] = playerId;
            payload["speakerHeroStringId"] = npcId;
            string contextId = GetCampaignId() + ":" + (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main") + ":" + npcId;
            if (draft != null && npc?.IsChild == true) throw new InvalidOperationException("Gameplay bargaining requires an adult participant.");
            var behavior = ReignAgreementBehavior.Instance ?? throw new InvalidOperationException("The explicit action service is unavailable.");
            var offer = behavior.PrepareOffer(draft, playerId, npcId, contextId, out var receipt);
            var context = new JObject { ["playerId"] = playerId, ["npcId"] = npcId, ["conversationId"] = contextId,
                ["worldDay"] = ReignActionService.Now, ["adultParticipant"] = npc?.IsChild != true };
            if (offer != null) { context["proposal"] = JObject.FromObject(offer); context["termsHash"] = offer.TermsHash(); }
            if (receipt != null) context["nativeReceipt"] = JObject.FromObject(receipt);
            if (draft != null && AgreementRules.IsPlayerDeclaration(draft) && receipt != null)
                context["playerDeclaration"] = draft.Sentence;
            if (npc?.IsChild != true) context["appraisals"] = ReignActionQuotes.Context(playerId, npcId, offer,
                payload.Value<string>("playerText") ?? payload.Value<string>("text") ?? payload.Value<string>("message") ?? "");
            payload["structuredActionProtocolVersion"] = ActionNegotiation.ProtocolVersion;
            payload["structuredActionContext"] = context;
        }

        private static void ProcessStructuredActionDecision(JObject request, JObject response, Hero npc, string responseId = "")
        {
            if (response == null || response.Value<bool?>("ok") != true) return;
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
                if (npc?.IsChild == true && decision.Kind != "none") throw new InvalidOperationException("This participant cannot authorize gameplay bargains.");
                if (context.Value<string>("playerId") != Hero.MainHero?.StringId || context.Value<string>("npcId") != npc?.StringId)
                    throw new InvalidOperationException("The conversation participants changed.");
                message = ReignAgreementBehavior.Instance.ReceiveDecision(context["proposal"]?.ToObject<ActionAgreement>(), decision,
                    context.Value<string>("playerId"), context.Value<string>("npcId"), context.Value<string>("conversationId"),
                    string.IsNullOrEmpty(responseId) ? request.Value<string>("correlationId") ?? request.Value<string>("sceneTurnId") : responseId);
            }
            catch (Exception ex) { message = "Action not applied: " + ex.Message; }
            if (string.IsNullOrWhiteSpace(message)) return;
            response["structuredActionReceipt"] = message;
            response["reply"] = (response.Value<string>("reply") ?? "") + "\n\n[" + message + "]";
        }
    }
}
