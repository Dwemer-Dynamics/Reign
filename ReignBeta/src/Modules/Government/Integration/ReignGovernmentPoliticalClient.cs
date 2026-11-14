using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static JObject BuildGovernmentPoliticalHero(Hero hero) => BuildHeroProfile(hero);
        internal static JObject BuildPoliticalSyncPayload(IEnumerable<Hero> heroes)
        {
            var rows = new JArray();
            foreach (var hero in heroes) rows.Add(BuildPoliticalSyncHero(hero));
            return BuildPoliticalSyncPayload(rows);
        }
        internal static JObject BuildPoliticalSyncHero(Hero hero)
        {
            var row = BuildHeroProfile(hero);
            row["isRulingClanMember"] = hero.Clan != null && hero.Clan.Kingdom?.RulingClan == hero.Clan;
            row["isLandholdingLord"] = hero.IsLord && hero.Clan?.Fiefs.Count > 0;
            row["isClanLeader"] = hero.Clan?.Leader == hero;
            row["isArmyLeader"] = hero.PartyBelongedTo?.Army?.LeaderParty?.LeaderHero == hero;
            row["isWarPartyLeader"] = hero.PartyBelongedTo?.LeaderHero == hero && hero.IsLord;
            row["isChild"] = hero.IsChild;
            row["politicalEvents"] = ReignBeta.Government.ReignGovernmentCampaignBehavior.Instance?.PoliticalEventsForServer(hero.StringId) ?? new JArray();
            return row;
        }
        internal static JObject BuildPoliticalSyncPayload(JArray rows)
        {
            return new JObject { ["campaignId"] = GetCampaignId(), ["worldDay"] = CampaignTime.Now.ToDays,
                ["timelineId"] = ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", ["heroes"] = rows };
        }
        internal static Task<JObject> SyncPoliticalProfilesAsync(JObject payload) => PostJsonAsync("/government/political-profiles/sync", payload);
    }
}
