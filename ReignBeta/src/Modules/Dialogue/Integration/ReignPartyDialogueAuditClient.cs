using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.UI.ViewModels;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        public static Task<JObject> PollPartyDialogueAuditRequestAsync()
        {
            return PostJsonAsync("/tests/party-dialogue/request/poll", new JObject
            {
                ["campaignId"] = GetCampaignId()
            });
        }

        public static Task<JObject> AcknowledgePartyDialogueAuditRequestAsync(
            string requestId,
            string status,
            IEnumerable<Hero> heroes,
            string message)
        {
            JArray selected = new JArray((heroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null)
                .Select(hero => new JObject
                {
                    ["heroId"] = hero.StringId ?? string.Empty,
                    ["heroName"] = hero.Name?.ToString() ?? string.Empty
                }));
            return PostJsonAsync("/tests/party-dialogue/request/ack", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["requestId"] = requestId ?? string.Empty,
                ["status"] = status ?? string.Empty,
                ["heroes"] = selected,
                ["message"] = message ?? string.Empty
            });
        }

        public static Task<JObject> StartPartyDialogueAuditAsync(string runId, IEnumerable<Hero> heroes, int seed)
        {
            JArray selected = new JArray((heroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null)
                .Select(hero => new JObject
                {
                    ["heroId"] = hero.StringId ?? string.Empty,
                    ["heroName"] = hero.Name?.ToString() ?? string.Empty
                }));
            return PostJsonAsync("/tests/party-dialogue/start", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["heroes"] = selected,
                ["playerId"] = Hero.MainHero?.StringId ?? string.Empty,
                ["seed"] = seed,
                ["expectedExchanges"] = 15,
                ["expectedScenes"] = 5,
                ["expectedNpcReplies"] = selected.Count * 15
            });
        }

        public static Task<JObject> VerifyPartyDialogueAuditTurnAsync(
            string runId,
            int sceneIndex,
            int turnIndex,
            string playerText,
            ReignPartyChatBatchResult batch,
            IEnumerable<string> recallTokens,
            bool requireEverySpeakerRecall)
        {
            JArray replies = new JArray((batch?.Replies ?? new List<ReignPartyChatReply>()).Select(reply => new JObject
            {
                ["heroId"] = reply?.SpeakerHeroStringId ?? string.Empty,
                ["heroName"] = reply?.SpeakerName ?? string.Empty,
                ["text"] = reply?.Text ?? string.Empty,
                ["correlationId"] = reply?.CorrelationId ?? string.Empty,
                ["sessionId"] = reply?.ConversationSessionId ?? string.Empty,
                ["exchangeId"] = reply?.ExchangeId ?? string.Empty,
                ["selectedContextPulls"] = reply?.SelectedContextPulls ?? new JArray(),
                ["participation"] = reply?.Participation ?? "speak",
                ["reactionTargetHeroStringId"] = reply?.ReactionTargetHeroStringId ?? string.Empty,
                ["queuedActionCount"] = reply?.QueuedActions?.Count ?? 0,
                ["ok"] = reply?.Ok == true,
                ["error"] = reply?.Error ?? string.Empty
            }));
            return PostJsonAsync("/tests/party-dialogue/turn", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["sceneIndex"] = sceneIndex,
                ["turnIndex"] = turnIndex,
                ["sessionId"] = batch?.SessionId ?? string.Empty,
                ["exchangeId"] = batch?.ExchangeId ?? string.Empty,
                ["playerText"] = playerText ?? string.Empty,
                ["replies"] = replies,
                ["recallTokens"] = new JArray(recallTokens ?? Enumerable.Empty<string>()),
                ["requireEverySpeakerRecall"] = requireEverySpeakerRecall
            });
        }

        public static Task<JObject> VerifyPartyDialogueAuditSceneAsync(
            string runId,
            int sceneIndex,
            string sessionId,
            string sceneSummaryId)
        {
            return PostJsonAsync("/tests/party-dialogue/scene", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty,
                ["sceneIndex"] = sceneIndex,
                ["sessionId"] = sessionId ?? string.Empty,
                ["sceneSummaryId"] = sceneSummaryId ?? string.Empty,
                ["expectedTurns"] = 12
            });
        }

        public static Task<JObject> FinishPartyDialogueAuditAsync(string runId, bool cancel)
        {
            return PostJsonAsync(cancel ? "/tests/party-dialogue/cancel" : "/tests/party-dialogue/finish", new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["runId"] = runId ?? string.Empty
            });
        }

        public static Task<JObject> GetPartyDialogueAuditStatusAsync(string runId = "")
        {
            string route = "/tests/party-dialogue/status?campaignId=" + Uri.EscapeDataString(GetCampaignId());
            if (!string.IsNullOrWhiteSpace(runId)) route += "&runId=" + Uri.EscapeDataString(runId);
            return GetJsonAsync(route);
        }
    }
}
