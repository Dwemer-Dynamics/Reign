using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    internal static class ReignPartyChatSessionRequest
    {
        internal static JObject Create(string campaignId, string timelineId, string sessionId,
            string npcId, string playerId, IEnumerable<string> participantIds)
        {
            if (string.IsNullOrWhiteSpace(timelineId))
                throw new ArgumentException("A party conversation requires its current timeline.", nameof(timelineId));

            // Approach validates the saved session against this exact timeline and
            // the complete audience, including the player and the anchor NPC.
            return new JObject
            {
                ["campaignId"] = campaignId,
                ["timelineId"] = timelineId,
                ["sessionId"] = sessionId,
                ["conversationSessionId"] = sessionId,
                ["npcId"] = npcId,
                ["playerId"] = playerId,
                ["channel"] = "party_chat",
                ["participants"] = new JArray((participantIds ?? Enumerable.Empty<string>())
                    .Concat(new[] { npcId, playerId })
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase))
            };
        }
    }
}
