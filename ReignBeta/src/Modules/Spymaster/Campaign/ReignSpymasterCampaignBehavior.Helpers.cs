using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private static float NextSpymasterRoll(string purpose)
        {
            if (ReignSpymasterTestRuntime.TryNext(purpose, out float testRoll)) return testRoll;
            return MBRandom.RandomFloat;
        }

        private static void AppendMissionHistory(ReignSpymasterMission mission, string text, float day)
        {
            if (mission == null || string.IsNullOrWhiteSpace(text)) return;
            mission.History = mission.History ?? new System.Collections.Generic.List<string>();
            mission.History.Add("Day " + day.ToString("0.00") + ": " + text.Trim());
            if (mission.History.Count > 64)
                mission.History = mission.History.Skip(mission.History.Count - 64).ToList();
        }

        private static string ValidateSpymasterMissionTarget(ReignSpymasterMission mission)
        {
            if (mission == null) return "The operation record was invalidated.";
            string type = mission.MissionType ?? string.Empty;
            if (string.Equals(type, "counterintelligence", StringComparison.OrdinalIgnoreCase)) return string.Empty;
            if (string.Equals(mission.TargetType, "settlement", StringComparison.OrdinalIgnoreCase))
            {
                Settlement settlement = FindSettlement(mission.TargetStringId);
                if (settlement?.Town == null || !settlement.IsFortification) return "The target settlement is no longer a valid fortification.";
                if (string.Equals(type, "assassinate_governor", StringComparison.OrdinalIgnoreCase)
                    && settlement.Town.Governor?.IsAlive != true)
                    return "The target settlement no longer has a living governor.";
                return string.Empty;
            }

            if (string.Equals(mission.TargetType, "person", StringComparison.OrdinalIgnoreCase))
            {
                Hero hero = FindHero(mission.TargetStringId);
                if (hero?.IsAlive != true || !hero.IsActive) return "The target person is no longer available.";
                if (type.StartsWith("assassinate", StringComparison.OrdinalIgnoreCase) && hero.IsNotable && hero.Issue != null)
                    return "The target became essential to an active issue.";
            }
            return string.Empty;
        }

        private static Hero ResolveSpymasterConsequenceTarget(ReignSpymasterMission mission)
        {
            if (mission == null) return null;
            if (string.Equals(mission.TargetType, "person", StringComparison.OrdinalIgnoreCase))
                return FindHero(mission.TargetStringId);
            Settlement settlement = FindSettlement(mission.TargetStringId);
            return settlement?.Town?.Governor ?? settlement?.OwnerClan?.Leader ?? settlement?.MapFaction?.Leader;
        }

        private static Settlement ResolveNobleFortification(Hero hero, Kingdom player)
        {
            Settlement current = hero?.CurrentSettlement;
            if (current?.IsVillage == true) current = current.Village?.Bound;
            if (current?.IsFortification == true && current.MapFaction == player) return current;
            Settlement home = hero?.HomeSettlement;
            if (home?.IsVillage == true) home = home.Village?.Bound;
            if (home?.IsFortification == true && home.MapFaction == player) return home;
            return Town.AllFiefs.Where(x => x?.Settlement?.IsFortification == true && x.OwnerClan?.Kingdom == player)
                .OrderBy(x => x.Settlement.StringId).Select(x => x.Settlement).FirstOrDefault();
        }
    }
}
