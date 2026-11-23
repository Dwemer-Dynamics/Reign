using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using static ReignBeta.Court.Campaign.ReignCourtCampaignBehavior;
using ReignBeta.Court.Campaign;

namespace ReignBeta.Spymaster.Campaign
{
    internal static partial class ReignSpymasterTestControls
    {
        internal static string AddGovernmentCertificationSubterfugeEffect(ReignCourtCampaignBehavior court, Town town)
        {
            if (town?.Settlement == null) return string.Empty;
            float day = CurrentDayFloat();
            Kingdom foreign = Kingdom.All.Where(item => item != null && !item.IsEliminated
                    && item != Clan.PlayerClan?.Kingdom)
                .OrderBy(item => item.StringId, StringComparer.Ordinal).FirstOrDefault();
            var effect = new ReignSpymasterSettlementEffect
            {
                SettlementStringId = town.Settlement.StringId,
                EffectType = "loyalty",
                SourceKingdomStringId = foreign?.StringId ?? "government_certification_foreign_network",
                AgentHeroStringId = court.ActiveSpymaster?.StringId ?? string.Empty,
                StartDay = day,
                EndDay = day + 30f,
                Magnitude = -5f
            };
            court.EnsureSpymasterState().Effects.Add(effect);
            return effect.EffectId;
        }

        internal static bool RemoveGovernmentCertificationSubterfugeEffect(ReignCourtCampaignBehavior court, string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId)) return false;
            return court.EnsureSpymasterState().Effects.RemoveAll(item =>
                string.Equals(item?.EffectId, effectId, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }
}
