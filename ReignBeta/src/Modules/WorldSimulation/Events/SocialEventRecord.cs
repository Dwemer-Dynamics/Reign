using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace ReignBeta.Events
{
    public class SocialEventRecord
    {
        [SaveableField(1)]
        public string EventId;

        [SaveableField(2)]
        public string TemplateId;

        [SaveableField(3)]
        public string DisplayName;

        [SaveableField(4)]
        public string SettlementStringId;

        [SaveableField(5)]
        public string HostHeroStringId;

        [SaveableField(6)]
        public List<string> AttendeeHeroStringIds;

        [SaveableField(7)]
        public float AnnouncementDay;

        [SaveableField(8)]
        public float ExpiresDay;

        [SaveableField(9)]
        public int RequiredPlayerClanTier;

        [SaveableField(10)]
        public int StatusValue;

        [SaveableField(11)]
        public bool AnnouncementSent;

        [SaveableField(12)]
        public List<string> MovedHeroStringIds;

        [SaveableField(13)]
        public bool IsGeneratedWildernessEvent;

        [SaveableField(14)]
        public string GeneratedTerrainKey;

        [SaveableField(15)]
        public string GeneratedLocationText;

        [SaveableField(16)]
        public string GeneratedTitle;

        [SaveableField(17)]
        public string GeneratedApproachText;

        [SaveableField(18)]
        public string GeneratedOpeningText;

        [SaveableField(19)]
        public string GeneratedPlayerHook;

        [SaveableField(20)]
        public string GeneratedExternalHeroStringId;

        [SaveableField(21)]
        public string GeneratedExternalContextText;

        [SaveableField(22)]
        public string GeneratedEmotionalPressure;

        [SaveableField(23)]
        public string GeneratedSurfaceClues;

        [SaveableField(24)]
        public string GeneratedHiddenContext;

        [SaveableField(25)]
        public string GeneratedDiscoveryRoutes;

        [SaveableField(26)]
        public string GeneratedTimeOfDayText;

        public SocialEventRecord()
        {
            AttendeeHeroStringIds = new List<string>();
            MovedHeroStringIds = new List<string>();
        }

        public SocialEventStatus Status
        {
            get { return (SocialEventStatus)StatusValue; }
            set { StatusValue = (int)value; }
        }

        public bool IsOpen(float campaignDay)
        {
            return Status == SocialEventStatus.Pending && campaignDay <= ExpiresDay;
        }

        public bool HasTimedOut(float campaignDay)
        {
            return Status == SocialEventStatus.Pending && campaignDay > ExpiresDay;
        }

        public Settlement GetSettlement()
        {
            return Settlement.All.FirstOrDefault(x => x.StringId == SettlementStringId);
        }

        public Hero GetHost()
        {
            return Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == HostHeroStringId);
        }

        public Hero GetGeneratedExternalHero()
        {
            return string.IsNullOrWhiteSpace(GeneratedExternalHeroStringId)
                ? null
                : Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == GeneratedExternalHeroStringId);
        }

        public bool IsGeneratedExternalHero(Hero hero)
        {
            return IsGeneratedWildernessEvent
                && hero != null
                && !string.IsNullOrWhiteSpace(GeneratedExternalHeroStringId)
                && hero.StringId == GeneratedExternalHeroStringId;
        }

        public IEnumerable<Hero> GetAttendees()
        {
            if (AttendeeHeroStringIds == null)
            {
                yield break;
            }

            foreach (string stringId in AttendeeHeroStringIds)
            {
                Hero hero = Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == stringId);
                if (hero != null
                    && hero != Hero.MainHero
                    && !string.Equals(hero.StringId, Hero.MainHero?.StringId, System.StringComparison.OrdinalIgnoreCase))
                {
                    yield return hero;
                }
            }
        }
    }
}
