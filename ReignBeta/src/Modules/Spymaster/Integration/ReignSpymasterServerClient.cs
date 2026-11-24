using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Characters.Integration;
using ReignBeta.Court.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;

namespace ReignBeta.Spymaster.Integration
{
    public static class ReignSpymasterServerClient
    {
        private static JObject BasePayload()
        {
            ReignCourtCampaignBehavior court = ReignServices.Court;
            return new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["sessionId"] = court?.Session?.SessionId ?? string.Empty,
                ["courtScope"] = (court?.Session?.Scope ?? ReignCourtScope.Local).ToString().ToLowerInvariant(),
                ["hostSettlementStringId"] = court?.Session?.HostSettlementStringId ?? string.Empty,
                ["capitalSettlementStringId"] = court?.CurrentCapital?.StringId ?? string.Empty
            };
        }

        public static Task<JObject> RecordMissionMemoryAsync(ReignSpymasterMission mission, string phase)
            => RecordMissionMemoryAsync(mission, phase, null);

        internal static Task<JObject> RecordMissionMemoryAsync(ReignSpymasterMission mission, string phase,
            Func<bool> requestOwner)
        {
            if (ReignTestHarness.Current.Spymaster.Active)
            {
                ReignTestHarness.Current.Spymaster.RecordNativeIntent("memory_write", new JObject
                {
                    ["missionId"] = mission?.MissionId ?? string.Empty,
                    ["phase"] = phase ?? "updated"
                });
                return Task.FromResult(new JObject { ["ok"] = true, ["testSuppressed"] = true });
            }
            JObject payload = BasePayload();
            payload["phase"] = phase ?? "updated";
            payload["mission"] = JObject.FromObject(mission ?? new ReignSpymasterMission());
            return requestOwner == null ? ReignServerClient.PostJsonAsync("/court/spymaster/memory", payload)
                : ReignServerClient.PostOwnedJsonAsync("/court/spymaster/memory", payload, requestOwner);
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

        internal static Task<JObject> ApplyMitigationAsync(ReignSpymasterMission mission, string restoreGeneration,
            Func<bool> requestOwner)
        {
            JObject payload = BasePayload();
            payload["restoreGeneration"] = restoreGeneration;
            payload["mission"] = JObject.FromObject(mission);
            return ReignServerClient.PostOwnedJsonAsync("/court/spymaster/social/apply", payload, requestOwner);
        }

        internal static Task<JObject> GetMitigationReceiptAsync(ReignSpymasterMission mission, Func<bool> requestOwner)
        {
            JObject payload = BasePayload();
            payload["readMitigationReceipt"] = true;
            payload["mission"] = JObject.FromObject(mission);
            return ReignServerClient.PostOwnedJsonAsync("/court/spymaster/social/status", payload, requestOwner);
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

        internal static Task<JObject> TickForeignAgentsAsync(JObject snapshot, string generation, Func<bool> ownsRequest)
        {
            JObject payload = BasePayload();
            foreach (var property in snapshot ?? new JObject()) payload[property.Key] = property.Value;
            payload["restoreGeneration"] = generation;
            return ReignServerClient.PostOwnedJsonAsync("/court/spymaster/agents/tick", payload, ownsRequest);
        }

        public static Task<JObject> GetForeignAgentStatusAsync(JObject snapshot)
        {
            JObject payload = BasePayload();
            foreach (var property in snapshot ?? new JObject()) payload[property.Key] = property.Value;
            return ReignServerClient.PostJsonAsync("/court/spymaster/agents/status", payload);
        }

        internal static Task<JObject> ExposeForeignAgentAsync(string agentHeroId, string assignmentId,
            float exposedDay, string generation, Func<bool> ownsRequest)
        {
            JObject payload = BasePayload();
            payload["agentHeroId"] = agentHeroId;
            payload["assignmentId"] = assignmentId;
            payload["exposureRevision"] = 0;
            payload["reason"] = "player_counterintelligence";
            payload["worldDay"] = exposedDay;
            payload["restoreGeneration"] = generation;
            return ReignServerClient.PostOwnedJsonAsync("/court/spymaster/agents/expose/v1", payload, ownsRequest);
        }
    }
}
