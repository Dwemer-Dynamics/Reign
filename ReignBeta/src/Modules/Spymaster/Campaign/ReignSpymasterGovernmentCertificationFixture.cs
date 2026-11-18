using System;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        internal string AddGovernmentCertificationSubterfugeEffect(Town town)
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
                AgentHeroStringId = ActiveSpymaster?.StringId ?? string.Empty,
                StartDay = day,
                EndDay = day + 30f,
                Magnitude = -5f
            };
            EnsureSpymasterState().Effects.Add(effect);
            return effect.EffectId;
        }

        internal bool RemoveGovernmentCertificationSubterfugeEffect(string effectId)
        {
            if (string.IsNullOrWhiteSpace(effectId)) return false;
            return EnsureSpymasterState().Effects.RemoveAll(item =>
                string.Equals(item?.EffectId, effectId, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }
}
