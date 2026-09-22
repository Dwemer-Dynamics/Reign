using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Localization;

namespace ReignBeta.ClanAccords
{
    internal static class ReignClanAccordModelMath
    {
        // ExplainedNumber.Add changes its base and is multiplied by every existing factor.
        // Freeze the completed native calculation into its effective explanation lines first,
        // so fixed accord benefits remain exact even with -100% or other native factors.
        // This must be the final modifier in a model; retain native floors and ceilings.
        internal static void AddFinalDelta(ref ExplainedNumber result, float delta, TextObject explanation)
        {
            if (delta == 0f) return;
            float original = result.ResultNumber;
            var final = new ExplainedNumber(result.IncludeDescriptions ? 0f : original, result.IncludeDescriptions);
            if (result.IncludeDescriptions)
            {
                foreach (var line in result.GetLines())
                    final.Add(line.number, new TextObject("{=!}" + line.name));
                float remainder = original - final.ResultNumber;
                if (remainder != 0f)
                    final.Add(remainder, new TextObject("{=!}Other native adjustments"));
            }
            final.Add(delta, explanation);
            if (result.LimitMinValue != float.MinValue) final.LimitMin(result.LimitMinValue);
            if (result.LimitMaxValue != float.MaxValue) final.LimitMax(result.LimitMaxValue);
            result = final;
        }

        internal static void ApplyWageReduction(ref ExplainedNumber result, double reduction)
        {
            if (reduction <= 0d) return;
            AddFinalDelta(ref result, -(float)(System.Math.Max(0f, result.ResultNumber) * System.Math.Min(1d, reduction)),
                new TextObject("{=!}Clan Accords: garrison cooperation"));
        }
    }

    internal sealed class ReignClanAccordsFinanceModel : DefaultClanFinanceModel
    {
        public override ExplainedNumber CalculateClanGoldChange(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
        {
            var result = base.CalculateClanGoldChange(clan, includeDescriptions, applyWithdrawals, includeDetails);
            AddAccordIncome(clan, ref result);
            return result;
        }

        public override ExplainedNumber CalculateClanIncome(Clan clan, bool includeDescriptions = false, bool applyWithdrawals = false, bool includeDetails = false)
        {
            var result = base.CalculateClanIncome(clan, includeDescriptions, applyWithdrawals, includeDetails);
            AddAccordIncome(clan, ref result);
            return result;
        }

        private static void AddAccordIncome(Clan clan, ref ExplainedNumber result)
        {
            int income = ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(clan).TradeIncome ?? 0;
            ReignClanAccordModelMath.AddFinalDelta(ref result, income, new TextObject("{=!}Clan Accords: trade cooperation"));
        }
    }

    internal sealed class ReignClanAccordsWageModel : DefaultPartyWageModel
    {
        public override ExplainedNumber GetTotalWage(MobileParty mobileParty, TroopRoster troopRoster, bool includeDescriptions = false)
        {
            var result = base.GetTotalWage(mobileParty, troopRoster, includeDescriptions);
            if (mobileParty?.IsGarrison != true) return result;
            double reduction = ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(mobileParty.CurrentSettlement?.OwnerClan).GarrisonWageReduction ?? 0d;
            ReignClanAccordModelMath.ApplyWageReduction(ref result, reduction);
            return result;
        }
    }
}
