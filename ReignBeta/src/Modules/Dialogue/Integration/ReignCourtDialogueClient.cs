using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public sealed class ReignCourtDialogueReply
    {
        public bool Ok;
        public string Text = string.Empty;
        public string Emotion = string.Empty;
        public string Error = string.Empty;
        public long Revision;
    }

    public static partial class ReignServerClient
    {
        public static async Task<ReignCourtDialogueReply> RequestCourtMatterResponseAsync(CourtMatter matter, Hero speaker, string playerText,
            long expectedRevision, string commandId)
        {
            ReignCourtDialogueReply reply = new ReignCourtDialogueReply();
            try
            {
                if (matter == null || speaker == null)
                {
                    reply.Error = "A pending matter and known speaker are required for a court audience.";
                    return reply;
                }

                JObject payload = new JObject
                {
                    ["campaignId"] = GetCampaignId(),
                    ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                    ["commandId"] = commandId ?? string.Empty,
                    ["expectedRevision"] = expectedRevision,
                    ["matterId"] = matter.MatterId,
                    ["heroStringId"] = speaker.StringId,
                    ["hero"] = BuildHeroProfile(speaker),
                    ["playerHeroStringId"] = Hero.MainHero?.StringId ?? string.Empty,
                    ["playerIdentity"] = BuildPlayerIdentityContext(),
                    ["playerClanId"] = Clan.PlayerClan?.StringId ?? string.Empty,
                    ["playerKingdomId"] = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                    ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                    ["playerText"] = playerText ?? string.Empty,
                    ["conversationSessionId"] = "court_" + matter.MatterId,
                    ["sceneTurnId"] = "court_" + matter.MatterId + "_" + Guid.NewGuid().ToString("N"),
                    ["sceneParticipants"] = BuildSceneParticipants(new List<Hero> { speaker }),
                    ["channel"] = "court_audience",
                    ["worldDay"] = CampaignTime.Now.ToDays,
                    ["locationId"] = Settlement.CurrentSettlement?.StringId ?? string.Empty,
                    ["sceneContext"] = "An in-screen court audience about: " + matter.Title + ". " + matter.Summary,
                    ["nativePoliticalContext"] = BuildNativePoliticalContext(speaker),
                    ["actionResolutionIndex"] = BuildActionResolutionIndex(speaker, new List<Hero> { speaker })
                };
                await AttachContextBundlesAsync(payload, "dialogue", speaker, new List<Hero> { speaker }).ConfigureAwait(false);
                JObject response = await PostJsonAsync("/court/matter/respond", payload).ConfigureAwait(false);
                reply.Ok = response.Value<bool?>("ok") == true;
                reply.Text = response.Value<string>("reply") ?? string.Empty;
                reply.Emotion = response.Value<string>("emotion") ?? string.Empty;
                reply.Error = response.Value<string>("error") ?? string.Empty;
                reply.Revision = response.Value<long?>("revision") ?? 0;
            }
            catch (Exception ex)
            {
                reply.Error = ex.Message;
                ReignLog.Warn("Court audience response failed: " + ex.Message);
            }
            return reply;
        }

        public static ReignBeta.World.ReignWorldActionRecord ParseCourtWorldAction(JObject action)
        {
            return action == null ? null : ParseActionRecord(action);
        }
    }
}
