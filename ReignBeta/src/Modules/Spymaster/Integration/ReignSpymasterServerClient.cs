using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;

namespace ReignBeta.Integration
{
    public static class ReignSpymasterServerClient
    {
        private static JObject BasePayload()
        {
            ReignCourtCampaignBehavior court = ReignCourtCampaignBehavior.Instance;
            return new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["sessionId"] = court?.Session?.SessionId ?? string.Empty,
                ["courtScope"] = (court?.Session?.Scope ?? ReignCourtScope.Local).ToString().ToLowerInvariant(),
                ["hostSettlementStringId"] = court?.Session?.HostSettlementStringId ?? string.Empty,
                ["capitalSettlementStringId"] = court?.CurrentCapital?.StringId ?? string.Empty
            };
        }

        public static Task<JObject> RecordMissionMemoryAsync(ReignSpymasterMission mission, string phase)
        {
            if (ReignSpymasterTestRuntime.Active)
            {
                ReignSpymasterTestRuntime.RecordNativeIntent("memory_write", new JObject
                {
                    ["missionId"] = mission?.MissionId ?? string.Empty,
                    ["phase"] = phase ?? "updated"
                });
                return Task.FromResult(new JObject { ["ok"] = true, ["testSuppressed"] = true });
            }
            JObject payload = BasePayload();
            payload["phase"] = phase ?? "updated";
            payload["mission"] = JObject.FromObject(mission ?? new ReignSpymasterMission());
            return ReignServerClient.PostJsonAsync("/court/spymaster/memory", payload);
        }

        public static Task<JObject> GetSocialStatusAsync(string subjectId)
        {
            JObject payload = BasePayload();
            payload["subjectId"] = subjectId ?? string.Empty;
            payload["worldDay"] = TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays;
            return ReignServerClient.PostJsonAsync("/court/spymaster/social/status", payload);
        }

        public static Task<JObject> ApplySocialOperationAsync(ReignSpymasterMission mission)
        {
            JObject payload = BasePayload();
            payload["mission"] = JObject.FromObject(mission ?? new ReignSpymasterMission());
            return ReignServerClient.PostJsonAsync("/court/spymaster/social/apply", payload);
        }

        public static Task<JObject> ApplyExposurePressureAsync(ReignSpymasterMission mission, int relationLoss)
        {
            JObject payload = BasePayload();
            payload["mission"] = JObject.FromObject(mission ?? new ReignSpymasterMission());
            payload["relationLoss"] = relationLoss;
            payload["playerKingdomId"] = TaleWorlds.CampaignSystem.Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty;
            payload["worldDay"] = TaleWorlds.CampaignSystem.CampaignTime.Now.ToDays;
            return ReignServerClient.PostJsonAsync("/court/spymaster/exposure", payload);
        }

        public static Task<JObject> TickForeignAgentsAsync(JObject snapshot)
        {
            JObject payload = BasePayload();
            foreach (var property in snapshot ?? new JObject()) payload[property.Key] = property.Value;
            return ReignServerClient.PostJsonAsync("/court/spymaster/agents/tick", payload);
        }

        public static Task<JObject> GetForeignAgentStatusAsync(JObject snapshot)
        {
            JObject payload = BasePayload();
            foreach (var property in snapshot ?? new JObject()) payload[property.Key] = property.Value;
            return ReignServerClient.PostJsonAsync("/court/spymaster/agents/status", payload);
        }

        public static Task<JObject> ExposeForeignAgentAsync(string agentHeroId, string reason)
        {
            JObject payload = BasePayload();
            payload["agentHeroId"] = agentHeroId ?? string.Empty;
            payload["reason"] = reason ?? "exposed";
            return ReignServerClient.PostJsonAsync("/court/spymaster/agents/expose", payload);
        }
    }
}
