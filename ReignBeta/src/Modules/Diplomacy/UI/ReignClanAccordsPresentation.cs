using System.Linq;
using Reign.Core.Contracts.ClanAccords;
using ReignBeta.ClanAccords;
using ReignBeta.UI.ViewModels;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.UI
{
    internal static class ReignClanAccordsPresentation
    {
        internal static ReignClanAccordsScreenData Capture()
        {
            var snapshot = new ReignClanAccordsScreenData { ClanName = Clan.PlayerClan?.Name.ToString() ?? "Your clan", Tier = Clan.PlayerClan?.Tier ?? 0 };
            var behavior = ReignClanAccordsCampaignBehavior.Instance;
            if (behavior == null) return snapshot;
            foreach (var record in behavior.Records.Where(x => x.IsActive && x.PlayerClanId == Clan.PlayerClan?.StringId))
            {
                Clan partner = ReignObjectResolver.FindClan(record.PartnerClanId);
                bool playerLand = HasApplicableHoldings(Clan.PlayerClan, record.Type);
                bool partnerLand = HasApplicableHoldings(partner, record.Type);
                snapshot.Records.Add(new ReignClanAccordDisplayData
                {
                    Id = record.Id, Type = record.Type.ToString(), PartnerClanId = record.PartnerClanId,
                    PartnerClanName = partner?.Name.ToString() ?? record.PartnerClanName,
                    KingdomName = partner?.Kingdom?.Name.ToString() ?? "Independent", BannerCode = partner?.Banner?.Serialize() ?? "",
                    PlayerArrangerName = record.PlayerArrangerName, NpcArrangerName = record.NpcArrangerName,
                    StartedText = CampaignTime.Days((float)record.StartDay).ToString(), BenefitText = Benefit(record.Type),
                    ApplicabilityText = playerLand && partnerLand ? "" : !playerLand && !partnerLand
                        ? "Neither clan currently has applicable holdings." : !playerLand
                        ? "Your clan currently has no applicable holdings." : "Partner clan currently has no applicable holdings."
                });
            }
            return snapshot;
        }

        private static bool HasApplicableHoldings(Clan clan, ClanAccordType type)
        {
            if (type == ClanAccordType.Trade) return true;
            return clan != null && Settlement.All.Any(s => s.OwnerClan == clan
                && (type == ClanAccordType.Agricultural ? s.IsVillage : type == ClanAccordType.Artisan ? s.IsTown : s.IsFortification));
        }

        internal static string Benefit(ClanAccordType type)
        {
            switch (type)
            {
                case ClanAccordType.Trade: return "+50 denars/day to each clan";
                case ClanAccordType.MutualWatch: return "+0.1 security/day in both clans' towns & castles";
                case ClanAccordType.Agricultural: return "+0.2 hearth growth/day in both clans' villages";
                case ClanAccordType.Artisan: return "+0.1 prosperity/day in both clans' towns";
                case ClanAccordType.Garrison: return "2% lower garrison wages for both clans";
                default: return "Unsupported accord";
            }
        }
    }
}
