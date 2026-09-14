using System;
using System.IO;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static class ReignCampaignIdentity
    {
        public static bool HasActiveCampaign()
        {
            try
            {
                return TaleWorlds.CampaignSystem.Campaign.Current != null;
            }
            catch
            {
                return false;
            }
        }

        public static string CurrentCampaignId()
        {
            try
            {
                string id = TaleWorlds.CampaignSystem.Campaign.Current?.UniqueGameId;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id.Trim();
                }
            }
            catch
            {
            }

            try
            {
                string mainHeroId = Hero.MainHero?.StringId;
                if (!string.IsNullOrWhiteSpace(mainHeroId))
                {
                    return "unsaved_" + mainHeroId.Trim();
                }
            }
            catch
            {
            }

            return "unknown";
        }

        public static string CurrentCampaignFolderName()
        {
            return SafePathSegment(CurrentCampaignId(), "unknown");
        }

        public static string CurrentCampaignLabel()
        {
            string mainHeroName = Hero.MainHero?.Name?.ToString();
            string clanName = Clan.PlayerClan?.Name?.ToString();
            string kingdomName = Clan.PlayerClan?.Kingdom?.InformalName?.ToString();
            string day = TaleWorlds.CampaignSystem.Campaign.Current == null ? "unknown day" : "day " + CampaignTime.Now.ToDays.ToString("0.##");

            string label = "";
            if (!string.IsNullOrWhiteSpace(mainHeroName))
            {
                label = mainHeroName;
            }
            if (!string.IsNullOrWhiteSpace(clanName))
            {
                label = string.IsNullOrWhiteSpace(label) ? clanName : label + " / " + clanName;
            }
            if (!string.IsNullOrWhiteSpace(kingdomName))
            {
                label += " / " + kingdomName;
            }

            return string.IsNullOrWhiteSpace(label) ? "Unknown campaign (" + day + ")" : label + " (" + day + ")";
        }

        public static string SafePathSegment(string value, string fallback)
        {
            string clean = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                clean = clean.Replace(invalid, '_');
            }

            return string.IsNullOrWhiteSpace(clean) ? fallback : clean;
        }
    }
}
