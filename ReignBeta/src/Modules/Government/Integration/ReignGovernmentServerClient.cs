using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Platform.Integration.ReignServerClient;
using ReignBeta.Government.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Government.Integration
{
    internal static class ReignGovernmentServerClient
    {
        internal static Task<JObject> RequestGovernmentSpeakerStatementAsync(
            Kingdom kingdom,
            ReignGovernmentPartyRecord party,
            JArray rows)
        {
            ReignGovernmentCampaignBehavior behavior = ReignServices.Government;
            Hero speaker = string.IsNullOrWhiteSpace(party?.SpeakerHeroStringId) ? null
                : Hero.FindFirst(x => string.Equals(x.StringId, party.SpeakerHeroStringId,
                    System.StringComparison.OrdinalIgnoreCase));
            JObject payload = new JObject
            {
                ["campaignId"] = GetCampaignId(),
                ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                ["correlationId"] = "government_meeting_" + (kingdom?.StringId ?? "unknown")
                    + "_" + (party?.PartyId ?? "party"),
                ["government"] = behavior?.BuildPublicSnapshot(kingdom) ?? new JObject(),
                ["party"] = new JObject
                {
                    ["partyId"] = party?.PartyId ?? string.Empty,
                    ["name"] = party?.Name ?? string.Empty,
                    ["planks"] = new JArray(ReignGovernmentCampaignBehavior.ParsePlanks(party?.PlanksCsv)
                        .Select(x => x.ToString())),
                    ["seatCount"] = party?.SeatCount ?? 0
                },
                ["speaker"] = speaker == null ? null : BuildHeroProfile(speaker),
                ["resolutions"] = rows.DeepClone()
            };
            return PostJsonAsync("/government/meeting/speaker-statement", payload);
        }

        internal static string GovernmentResolutionTargetName(ReignGovernmentResolutionRecord record)
        {
            Settlement settlement = Settlement.Find(record?.TargetSettlementStringId);
            if (settlement != null) return settlement.Name?.ToString() ?? settlement.StringId;
            Hero hero = string.IsNullOrWhiteSpace(record?.TargetHeroStringId) ? null
                : Hero.FindFirst(x => string.Equals(x.StringId, record.TargetHeroStringId,
                    System.StringComparison.OrdinalIgnoreCase));
            if (hero != null) return hero.Name?.ToString() ?? hero.StringId;
            Clan clan = string.IsNullOrWhiteSpace(record?.TargetClanStringId) ? null
                : Clan.FindFirst(x => string.Equals(x.StringId, record.TargetClanStringId,
                    System.StringComparison.OrdinalIgnoreCase));
            if (clan != null) return clan.Name?.ToString() ?? clan.StringId;
            Kingdom kingdom = string.IsNullOrWhiteSpace(record?.TargetKingdomStringId) ? null
                : Kingdom.All.FirstOrDefault(x => string.Equals(x.StringId, record.TargetKingdomStringId,
                    System.StringComparison.OrdinalIgnoreCase));
            return kingdom?.Name?.ToString() ?? kingdom?.StringId ?? string.Empty;
        }
    }
}
