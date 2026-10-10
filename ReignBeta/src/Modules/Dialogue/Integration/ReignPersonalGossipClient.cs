using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        private static int _personalGossipTickInFlight;

        // Capture on the campaign thread, before the first await. Social-pool army
        // precedence and the partial room roster cannot establish physical co-presence.
        internal static JObject BuildPersonalGossipPresence()
        {
            var heroes = Hero.AllAliveHeroes.Where(h => h != null && !string.IsNullOrWhiteSpace(h.StringId))
                .OrderBy(h => h.StringId, StringComparer.Ordinal).Take(8193).ToList();
            var people = new JArray();
            foreach (Hero hero in heroes.Take(8192))
            {
                MobileParty party = hero.PartyBelongedTo;
                Settlement settlement = hero.CurrentSettlement ?? party?.CurrentSettlement;
                if (hero == Hero.MainHero)
                {
                    party = party ?? MobileParty.MainParty;
                    settlement = settlement ?? party?.CurrentSettlement ?? Settlement.CurrentSettlement;
                }
                var position = party?.GetPosition2D ?? settlement?.GetPosition2D;
                people.Add(new JObject
                {
                    ["heroStringId"] = hero.StringId, ["name"] = hero.Name?.ToString() ?? hero.StringId,
                    ["isAlive"] = hero.IsAlive, ["isPrisoner"] = hero.IsPrisoner,
                    ["available"] = hero.IsAlive && hero.IsActive && !hero.IsPrisoner,
                    ["settlementId"] = settlement?.StringId ?? string.Empty,
                    ["partyId"] = party?.StringId ?? string.Empty,
                    ["kingdomId"] = hero.Clan?.Kingdom?.StringId ?? string.Empty,
                    ["fatherId"] = hero.Father?.StringId ?? string.Empty,
                    ["motherId"] = hero.Mother?.StringId ?? string.Empty,
                    ["spouseId"] = hero.Spouse?.StringId ?? string.Empty,
                    ["positionKnown"] = position.HasValue,
                    ["positionX"] = position.HasValue ? (double)position.Value.X : 0d,
                    ["positionY"] = position.HasValue ? (double)position.Value.Y : 0d
                });
            }
            return new JObject
            {
                ["schema"] = "reign-gossip-presence-v1",
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                ["worldDay"] = CampaignTime.Now.ToDays,
                ["rosterComplete"] = heroes.Count <= 8192,
                ["people"] = people
            };
        }

        public static async Task TickPersonalGossipAsync()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current == null || Interlocked.CompareExchange(ref _personalGossipTickInFlight, 1, 0) != 0) return;
            try
            {
                JObject presence = BuildPersonalGossipPresence();
                await PostJsonAsync("/gossip/tick", new JObject
                {
                    ["campaignId"] = GetCampaignId(), ["timelineId"] = presence["timelineId"]?.DeepClone(),
                    ["worldDay"] = presence["worldDay"]?.DeepClone(), ["gossipPresence"] = presence
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("Personal gossip tick failed: " + ex.Message); }
            finally { Interlocked.Exchange(ref _personalGossipTickInFlight, 0); }
        }
    }
}
