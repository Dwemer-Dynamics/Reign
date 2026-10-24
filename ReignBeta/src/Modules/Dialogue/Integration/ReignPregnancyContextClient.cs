using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static async Task SynchronizePregnancyContextAsync(string route, JObject request)
        {
            if (request == null) return;
            var capture = await ReignMainThread.InvokeAsync(() =>
            {
                var family = ReignFamilyCampaignBehavior.Instance;
                if (family == null || TaleWorlds.CampaignSystem.Campaign.Current == null) return null;
                string campaignId = GetCampaignId(), timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
                string requestedCampaign = request.Value<string>("campaignId"), requestedTimeline = request.Value<string>("timelineId");
                if (!string.IsNullOrEmpty(requestedCampaign) && requestedCampaign != campaignId
                    || !string.IsNullOrEmpty(requestedTimeline) && requestedTimeline != timeline)
                    throw new OperationCanceledException("Pregnancy request belongs to a previous loaded save.");
                var snapshots = family.CapturePregnancyContexts();
                var profiles = new List<JObject>();
                foreach (string key in new[] { "hero", "profile", "speaker" })
                    if (request[key] is JObject profile) profiles.Add(profile);
                foreach (string key in new[] { "heroes", "attendees" })
                    profiles.AddRange((request[key] as JArray ?? new JArray()).OfType<JObject>());
                var people = profiles.Select(o => o.Value<string>("heroStringId"))
                    .Concat(new[] { request.Value<string>("heroStringId"), request.Value<string>("speakerHeroStringId"), Hero.MainHero?.StringId })
                    .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                // These IDs come from the native conversation roster, never prose.
                foreach (JObject person in (request["sceneParticipants"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string id = person.Value<string>("heroStringId") ?? person.Value<string>("heroId");
                    if (!string.IsNullOrEmpty(id) && !people.Contains(id)) people.Add(id);
                }
                bool physical = !route.StartsWith("/correspondence", StringComparison.Ordinal)
                    && route != "/memory/conversation/start" // An opened remote thread is not an observation.
                    && !route.EndsWith("/scene/generate", StringComparison.Ordinal);
                return new PregnancySyncCapture { Campaign = TaleWorlds.CampaignSystem.Campaign.Current, Family = family,
                    CampaignId = campaignId, Timeline = timeline,
                    Payload = new JObject { ["campaignId"] = campaignId,
                        ["timelineId"] = timeline,
                        ["worldDay"] = CampaignTime.Now.ToDays, ["pregnancies"] = snapshots,
                        ["observers"] = new JArray(people), ["physicalConversation"] = physical,
                        ["presentMotherIds"] = new JArray(physical ? people.Where(id => snapshots.OfType<JObject>().Any(p => p.Value<string>("motherId") == id))
                            .Where(id => {
                                Hero mother = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id);
                                return mother != null && (mother == Hero.OneToOneConversationHero
                                    || mother.PartyBelongedTo != null && mother.PartyBelongedTo == Hero.MainHero?.PartyBelongedTo
                                    || mother.CurrentSettlement != null && mother.CurrentSettlement == Hero.MainHero?.CurrentSettlement);
                            }) : Enumerable.Empty<string>()) } };
            }).ConfigureAwait(false);
            if (capture == null) return;
            JObject response = await PostJsonAsync("/family/pregnancy/sync", capture.Payload).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true) throw new InvalidOperationException(response.Value<string>("error") ?? "Pregnancy context could not be reconciled.");
            await ReignMainThread.InvokeAsync(() =>
            {
                if (capture.Campaign != TaleWorlds.CampaignSystem.Campaign.Current || capture.Family != ReignFamilyCampaignBehavior.Instance
                    || capture.CampaignId != GetCampaignId() || capture.Timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                    throw new OperationCanceledException("Pregnancy context belongs to another loaded save.");
                capture.Family.AcceptPregnancyKnowledge(response["knowledge"] as JArray);
                JObject views = response["observerViews"] as JObject ?? new JObject();
                foreach (JObject profile in request.DescendantsAndSelf().OfType<JObject>().ToList())
                {
                    string id = profile.Value<string>("heroStringId");
                    if (id == null) continue;
                    if (views[id] != null) profile["pregnancyPrompt"] = views[id].DeepClone();
                    if (profile["isPregnant"] != null) profile["pregnancyContexts"] = capture.Family.PregnancyContextsFor(
                        Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == id));
                }
            }).ConfigureAwait(false);
        }
        private sealed class PregnancySyncCapture
        {
            internal TaleWorlds.CampaignSystem.Campaign Campaign;
            internal ReignFamilyCampaignBehavior Family;
            internal string CampaignId, Timeline;
            internal JObject Payload;
        }
    }
}
