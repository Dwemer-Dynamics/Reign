using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        public static Task<JObject> PollNpcDialogueAuditRequestAsync()
        {
            return PostJsonAsync("/tests/npc-dialogue/request/poll", new JObject
            {
                ["campaignId"] = GetCampaignId()
            });
        }

        public static Task<JObject> AcknowledgeNpcDialogueAuditRequestAsync(
            string requestId,
            string status,
            Hero hero,
            string message)
        {
            return PostJsonAsync("/tests/npc-dialogue/request/ack", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["requestId"] = requestId ?? string.Empty,
                ["status"] = status ?? string.Empty,
                ["heroId"] = hero?.StringId ?? string.Empty,
                ["heroName"] = hero?.Name?.ToString() ?? string.Empty,
                ["message"] = message ?? string.Empty
            });
        }

        public static Task<JObject> StartNpcDialogueAuditAsync(string runId, Hero npc, int seed)
        {
            return PostJsonAsync("/tests/npc-dialogue/start", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["npcId"] = npc?.StringId ?? string.Empty,
                ["npcName"] = npc?.Name?.ToString() ?? string.Empty,
                ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["seed"] = seed,
                ["expectedExchanges"] = 30,
                ["expectedScenes"] = 5
            });
        }

        public static Task<JObject> VerifyNpcDialogueAuditTurnAsync(
            string runId,
            int sceneIndex,
            int turnIndex,
            string playerText,
            ReignDialogueReply reply,
            JArray expectedContextPulls,
            string recallToken)
        {
            return PostJsonAsync("/tests/npc-dialogue/turn", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["sceneIndex"] = sceneIndex,
                ["turnIndex"] = turnIndex,
                ["correlationId"] = reply?.CorrelationId ?? string.Empty,
                ["sessionId"] = reply?.ConversationSessionId ?? string.Empty,
                ["exchangeId"] = reply?.ExchangeId ?? string.Empty,
                ["playerText"] = playerText ?? string.Empty,
                ["npcReply"] = reply?.Text ?? string.Empty,
                ["expectedContextPulls"] = expectedContextPulls ?? new JArray(),
                ["recallToken"] = recallToken ?? string.Empty,
                ["queuedActionCount"] = reply?.QueuedActions?.Count ?? 0
            });
        }

        public static Task<JObject> VerifyNpcDialogueAuditSceneAsync(string runId, int sceneIndex, string sessionId)
        {
            return PostJsonAsync("/tests/npc-dialogue/scene", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["sceneIndex"] = sceneIndex,
                ["sessionId"] = sessionId ?? string.Empty,
                ["expectedTurns"] = 12
            });
        }

        public static Task<JObject> FinishNpcDialogueAuditAsync(string runId, bool cancel)
        {
            return PostJsonAsync(cancel ? "/tests/npc-dialogue/cancel" : "/tests/npc-dialogue/finish", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty
            });
        }

        public static Task<JObject> GetNpcDialogueAuditStatusAsync(string runId = "")
        {
            string route = "/tests/npc-dialogue/status?campaignId=" + Uri.EscapeDataString(GetCampaignId());
            if (!string.IsNullOrWhiteSpace(runId)) route += "&runId=" + Uri.EscapeDataString(runId);
            return GetJsonAsync(route);
        }
    }
}
