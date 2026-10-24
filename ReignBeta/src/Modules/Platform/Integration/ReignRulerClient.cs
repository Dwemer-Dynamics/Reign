using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Runtime;
using ReignBeta.Shared.Rulers;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        internal static RulerOptions RulerOptions { get; private set; } = new RulerOptions(_ => false);
        internal static bool RulerOptionsConfirmed { get; private set; }
        internal static void SetRulerOptions(JObject values)
        {
            if (values == null) return;
            foreach (string key in Shared.Rulers.RulerOptions.Switches.Keys) if (values[key]?.Type != JTokenType.Boolean) return;
            var copy = (JObject)values.DeepClone();
            RulerOptions = new RulerOptions(k => copy.Value<bool>(k)); RulerOptionsConfirmed = true;
            ReignRulerCampaignBehavior.Instance?.OptionsChanged();
        }
        internal static JObject RulerRequestPayload()
        {
            return new JObject { ["campaignId"] = GetCampaignId(), ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", ["worldDay"] = CampaignTime.Now.ToDays };
        }
        internal static Task<JObject> RulerRequestAsync(string operation, JObject payload) => PostJsonAsync("/rulers/" + operation, payload);
        internal static JObject RulerPersonalitySnapshot(Hero hero) => new JObject
        {
            ["heroStringId"] = hero?.StringId ?? "", ["name"] = hero?.Name?.ToString() ?? "",
            ["age"] = hero?.Age ?? 0, ["isLord"] = hero?.IsLord ?? false,
            ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
            ["isClanLeader"] = hero != null && hero.Clan?.Leader == hero,
            ["clanTier"] = hero?.Clan?.Tier ?? 0,
            ["nativeEncyclopediaText"] = GetNativeEncyclopediaText(hero),
            ["traits"] = BuildTraitProfile(hero), ["skills"] = BuildSkillProfile(hero)
        };
    }
}
