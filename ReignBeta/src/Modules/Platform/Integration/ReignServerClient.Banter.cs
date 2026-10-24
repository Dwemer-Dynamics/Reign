using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static readonly HashSet<string> CompletedBanterNativeProjections = new HashSet<string>(StringComparer.Ordinal);
        // All native data is captured on the game thread before starting transport.
        internal static JObject CaptureBanterContext(string generation, string sessionId, int revision, bool traveling, bool includeRoster = false, string[] selectedIds = null)
        {
            List<Hero> heroes = includeRoster ? ReignPartyChatSession.GetMainPartyHeroes()
                .Where(h => selectedIds == null || selectedIds.Contains(h.StringId)).Take(64).ToList() : new List<Hero>();
            var profiles = new JArray(heroes.Select(h => {
                JObject full = BuildHeroProfile(h), profile = new JObject();
                foreach (string key in new[] { "heroStringId", "name", "isFemale", "cultureId", "clanId", "clanName", "kingdomId", "kingdomName", "occupation", "age", "isAlive", "isPrisoner", "isChild", "isLord", "isRuler", "isClanLeader", "isNotable", "isWanderer", "currentPartyId", "partyId", "sovereignHeroStringId", "governorOfSettlementId", "governorOfSettlementName" }) profile[key] = full[key]?.DeepClone();
                profile["banterPartyRole"] = h == MobileParty.MainParty?.LeaderHero ? "party leader" : h.IsPlayerCompanion ? "companion" : "family member or guest";
                profile["isPregnant"] = full["isPregnant"]?.DeepClone();
                profile["pregnancyContexts"] = full["pregnancyContexts"]?.DeepClone();
                return profile;
            }));
            var pairs = new JArray();
            foreach (Hero a in heroes) foreach (Hero b in heroes.Where(h => h != a)) pairs.Add(new JObject {
                ["subjectId"] = a.StringId, ["targetId"] = b.StringId, ["nativeRelation"] = a.GetRelation(b) });
            return new JObject {
                ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CampaignTime.Now.ToDays, ["generation"] = generation ?? "", ["sessionId"] = sessionId ?? "",
                ["expectedRevision"] = revision, ["heroes"] = profiles, ["relationshipPairs"] = pairs,
                ["playerHeroStringId"] = Hero.MainHero?.StringId ?? "", ["playerName"] = Hero.MainHero?.Name?.ToString() ?? "Player",
                ["partyId"] = MobileParty.MainParty?.StringId ?? "", ["locationId"] = MobileParty.MainParty?.CurrentSettlement?.StringId ?? "",
                ["traveling"] = traveling, ["chatOpen"] = UI.ReignPartyChatScreenManager.IsOpenOrPending
            };
        }
        internal static async Task<JObject> BanterRequestAsync(string operation, JObject payload, Func<Task<bool>> current, bool projectNative = true)
        {
            var owner = TaleWorlds.CampaignSystem.Campaign.Current;
            var relationBehavior = ReignRelationshipCampaignBehavior.Instance;
            string timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            JObject response = await PostJsonAsync("/party-banter/" + operation, payload, current).ConfigureAwait(false);
            JObject episode = response?["episode"] as JObject ?? response;
            JObject effects = episode?["relationshipEffects"] as JObject;
            if (!projectNative || response?.Value<bool?>("ok") != true || effects == null) return response;
            foreach (JObject change in (effects["nativeChanges"] as JArray ?? new JArray()).OfType<JObject>())
            {
                JArray ids = change["receiptIds"] as JArray ?? new JArray();
                if (ids.Count == 0) continue;
                string key = payload.Value<string>("campaignId") + "|" + episode.Value<string>("generation") + "|" + string.Join(",", ids.Values<string>());
                JObject nativeReceipt = null;
                bool done = false;
                await ReignMainThread.InvokeAsync(() => done = CompletedBanterNativeProjections.Contains(key)).ConfigureAwait(false);
                if (done) continue;
                if (current != null && !await current().ConfigureAwait(false)) throw new InvalidOperationException("The banter campaign changed before relationship projection.");
                await ReignMainThread.InvokeAsync(() => {
                    if (!ReferenceEquals(owner, TaleWorlds.CampaignSystem.Campaign.Current)
                        || !ReferenceEquals(relationBehavior, ReignRelationshipCampaignBehavior.Instance)
                        || timeline != (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main"))
                        throw new InvalidOperationException("The campaign changed before the banter relationship effect could be applied.");
                    Hero subject = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == change.Value<string>("subjectId"));
                    Hero target = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == change.Value<string>("targetId"));
                    var behavior = ReignRelationshipCampaignBehavior.Instance;
                    if (subject != null && target != null && subject != target && behavior != null)
                        nativeReceipt = behavior.ApplyConversationRelationOnce(ids.Values<string>(), subject, target,
                            change.Value<int?>("delta") ?? 0, change.Value<bool?>("showNotification") == true);
                }).ConfigureAwait(false);
                if (nativeReceipt == null) continue;
                JObject acknowledgement = await PostJsonAsync("/relationships/conversation/native-receipt", new JObject {
                    ["campaignId"] = payload.Value<string>("campaignId"), ["receiptIds"] = ids.DeepClone(), ["nativeReceipt"] = nativeReceipt
                }, current).ConfigureAwait(false);
                if (acknowledgement?.Value<bool?>("ok") != true) throw new InvalidOperationException("The server did not acknowledge the banter relationship receipt.");
                await ReignMainThread.InvokeAsync(() => {
                    if (CompletedBanterNativeProjections.Count >= 128) CompletedBanterNativeProjections.Clear();
                    CompletedBanterNativeProjections.Add(key);
                }).ConfigureAwait(false);
            }
            return response;
        }
    }
}
