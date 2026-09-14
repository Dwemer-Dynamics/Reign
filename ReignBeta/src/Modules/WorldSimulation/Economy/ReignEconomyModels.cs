using System;
using ReignBeta.Campaign;
using ReignBeta.Government;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court;
#endif
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Economy
{
    internal sealed class ReignVillageProductionCalculatorModel : DefaultVillageProductionCalculatorModel
    {
        public override ExplainedNumber CalculateDailyProductionAmount(Village village, ItemObject item)
        {
            ExplainedNumber result = base.CalculateDailyProductionAmount(village, item);
#if !REIGN_EXCLUDE_COURT
            float factor = ReignCourtCampaignBehavior.Instance?.GetDocketVillageProductionFactor(village) ?? 0f;
            if (factor != 0f)
                result.AddFactor(factor, new TextObject("{=!}Royal petition production support"));
#endif
            return result;
        }
    }

    internal sealed class ReignSettlementFoodModel : DefaultSettlementFoodModel
    {
        public override ExplainedNumber CalculateTownFoodStocksChange(
            Town town,
            bool includeMarketStocks = true,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateTownFoodStocksChange(town, includeMarketStocks, includeDescriptions);
            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            if (town?.Settlement == null || town.IsUnderSiege)
            {
                return result;
            }

            float effectiveVillageContribution = 0f;
            foreach (Village village in town.Settlement.BoundVillages)
            {
                if (village.VillageState != Village.VillageStates.Normal)
                {
                    continue;
                }

                float modifier = behavior?.GetRecoveryModifier(village) ?? 1f;
                float nativeContribution = (village.GetHearthLevel() + 1) * 6f;
                if (modifier < 1f)
                {
                    TextObject recoveryExplanation = new TextObject("{=!}{VILLAGE} agricultural recovery");
                    recoveryExplanation.SetTextVariable("VILLAGE", village.Name);
                    result.Add(-nativeContribution * (1f - modifier), recoveryExplanation);
                }

                float mobilizationPenalty = MathF.Min(
                    behavior?.GetMobilizationFoodPenalty(village) ?? 0f, nativeContribution * modifier);
                if (mobilizationPenalty > 0f)
                {
                    TextObject mobilizationExplanation = new TextObject("{=!}{VILLAGE} manpower shortage");
                    mobilizationExplanation.SetTextVariable("VILLAGE", village.Name);
                    result.Add(-mobilizationPenalty, mobilizationExplanation);
                }
                effectiveVillageContribution += MathF.Max(0f,
                    nativeContribution * modifier - mobilizationPenalty);
            }

            float eventFactor = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetFoodProductionFactor(town) ?? 0f;
            if (eventFactor != 0f && effectiveVillageContribution > 0f)
            {
                result.Add(effectiveVillageContribution * eventFactor,
                    new TextObject(eventFactor < 0f
                        ? "{=!}Kingdom-wide famine"
                        : "{=!}Kingdom-wide bountiful harvest"));
            }

#if !REIGN_EXCLUDE_COURT
            float spyFactor = ReignCourtCampaignBehavior.Instance?.GetSpymasterFoodFactor(town) ?? 0f;
            if (spyFactor != 0f && effectiveVillageContribution > 0f)
                result.Add(effectiveVillageContribution * spyFactor,
                    new TextObject("{=!}Covert disruption of food distribution"));
#endif

            return result;
        }
    }

    internal sealed class ReignSettlementSecurityModel : DefaultSettlementSecurityModel
    {
        public override ExplainedNumber CalculateSecurityChange(Town town, bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateSecurityChange(town, includeDescriptions);
            float accordSecurity = (float)(ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(town?.Settlement?.OwnerClan).Security ?? 0d);
            ReignEconomyCampaignBehavior behavior = ReignEconomyCampaignBehavior.Instance;
            if (town?.Settlement == null)
            {
                return result;
            }

            if (behavior != null)
            {
                bool nativeLootedPenaltyApplied = false;
                foreach (Village village in town.Settlement.BoundVillages)
                {
                    if (village.VillageState == Village.VillageStates.Looted)
                    {
                        nativeLootedPenaltyApplied = true;
                        break;
                    }
                }

                // Remove Bannerlord's one-time non-stacking -2 so every village can
                // contribute its own persistent, recovery-scaled penalty below.
                if (nativeLootedPenaltyApplied)
                {
                    result.Add(2f, new TextObject("{=!}Reign stacked village security"));
                }

                foreach (Village village in town.Settlement.BoundVillages)
                {
                    float modifier = behavior.GetRecoveryModifier(village);
                    if (modifier >= 1f)
                    {
                        continue;
                    }

                    TextObject explanation = new TextObject("{=!}{VILLAGE} devastation");
                    explanation.SetTextVariable("VILLAGE", village.Name);
                    result.Add(-2f * (1f - modifier), explanation);
                }
            }

            float eventDelta = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetSecurityDelta(town) ?? 0f;
            if (eventDelta != 0f)
                result.Add(eventDelta, new TextObject(eventDelta < 0f
                    ? "{=!}Kingdom-wide crime wave"
                    : "{=!}Kingdom-wide law and order"));

#if !REIGN_EXCLUDE_COURT
            float spyDelta = ReignCourtCampaignBehavior.Instance?.GetSpymasterSecurityDelta(town) ?? 0f;
            if (spyDelta != 0f)
                result.Add(spyDelta, new TextObject("{=!}Covert security disruption"));
#endif
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result, accordSecurity,
                new TextObject("{=!}Clan Accords: mutual watch"));
            return result;
        }
    }

    internal sealed class ReignSettlementLoyaltyModel : DefaultSettlementLoyaltyModel
    {
        public override ExplainedNumber CalculateLoyaltyChange(Town town, bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateLoyaltyChange(town, includeDescriptions);
            float modifier = ReignEconomyCampaignBehavior.Instance?.GetReliefLoyaltyModifier(town) ?? 0f;
            if (modifier > 0f)
            {
                result.Add(modifier, new TextObject("{=!}Emergency provisions received"));
            }
            else if (modifier < 0f)
            {
                result.Add(modifier, new TextObject("{=!}Unmet emergency provisions"));
            }
            float eventDelta = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetLoyaltyDelta(town) ?? 0f;
            if (eventDelta != 0f)
                result.Add(eventDelta, new TextObject(eventDelta < 0f
                    ? "{=!}Kingdom-wide political unrest"
                    : "{=!}Kingdom-wide national unity"));
            float governmentDelta = ReignGovernmentCampaignBehavior.Instance?.GetLoyaltyDelta(town) ?? 0f;
            if (governmentDelta != 0f)
                result.Add(governmentDelta, new TextObject("{=!}Representative government authority"));
#if !REIGN_EXCLUDE_COURT
            float spyDelta = ReignCourtCampaignBehavior.Instance?.GetSpymasterLoyaltyDelta(town) ?? 0f;
            if (spyDelta != 0f)
                result.Add(spyDelta, new TextObject("{=!}Covert agitation"));
#endif
            return result;
        }
    }

    internal sealed class ReignSettlementProsperityModel : DefaultSettlementProsperityModel
    {
        public override ExplainedNumber CalculateProsperityChange(
            Town fortification,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateProsperityChange(
                fortification, includeDescriptions);
            float accordProsperity = fortification?.Settlement?.IsTown == true
                ? (float)(ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(fortification.Settlement.OwnerClan).Prosperity ?? 0d) : 0f;
            float eventDelta = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetProsperityDelta(fortification) ?? 0f;
            if (eventDelta != 0f)
                result.Add(eventDelta, new TextObject(eventDelta < 0f
                    ? "{=!}Kingdom-wide trade collapse"
                    : "{=!}Kingdom-wide trade boom"));
            float governmentDelta = ReignGovernmentCampaignBehavior.Instance?.GetProsperityDelta(fortification) ?? 0f;
            if (governmentDelta != 0f)
                result.Add(governmentDelta, new TextObject("{=!}Representative government authority"));
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result, accordProsperity,
                new TextObject("{=!}Clan Accords: artisan exchange"));
            return result;
        }

        public override ExplainedNumber CalculateHearthChange(
            Village village,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateHearthChange(village, includeDescriptions);
            float accordHearths = (float)(ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance?.GetBonuses(village?.Settlement?.OwnerClan).HearthGrowth ?? 0d);
            float eventDelta = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetHearthDelta(village) ?? 0f;
            if (eventDelta != 0f)
            {
                if (eventDelta < 0f)
                {
                    // Pestilence is an actual loss, not merely a reduction to
                    // native growth. Remove positive native growth before adding
                    // the event loss, then keep the event from pushing a village
                    // beneath Reign's established hearth floor.
                    float positiveNativeGrowth = MathF.Max(0f, result.ResultNumber);
                    result.Add(eventDelta - positiveNativeGrowth,
                        new TextObject("{=!}Kingdom-wide pestilence"));
                    result.LimitMin(MathF.Min(0f,
                        village.Hearth - ReignEconomyCampaignBehavior.MinimumVillageHearth <= 0f
                            ? 0f
                            : ReignEconomyCampaignBehavior.MinimumVillageHearth - village.Hearth));
                }
                else
                {
                    result.Add(eventDelta,
                        new TextObject("{=!}Kingdom-wide population boom"));
                }
            }
            float governmentDelta = ReignGovernmentCampaignBehavior.Instance?.GetHearthDelta(village) ?? 0f;
            if (governmentDelta != 0f)
                result.Add(governmentDelta, new TextObject("{=!}Representative government authority"));
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result, accordHearths,
                new TextObject("{=!}Clan Accords: agricultural exchange"));
            return result;
        }
    }

    internal sealed class ReignBuildingConstructionModel : DefaultBuildingConstructionModel
    {
        public override ExplainedNumber CalculateDailyConstructionPower(
            Town town,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateDailyConstructionPower(town, includeDescriptions);
            float eventFactor = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetConstructionFactor(town) ?? 0f;
            if (eventFactor != 0f)
                result.AddFactor(eventFactor, new TextObject(eventFactor < 0f
                    ? "{=!}Kingdom-wide construction stagnation"
                    : "{=!}Kingdom-wide golden age"));
#if !REIGN_EXCLUDE_COURT
            float spyFactor = ReignCourtCampaignBehavior.Instance?.GetSpymasterConstructionFactor(town) ?? 0f;
            if (spyFactor != 0f)
                result.AddFactor(spyFactor, new TextObject("{=!}Covert construction disruption"));
#endif
            result.LimitMin(0f);
            return result;
        }

        public override int CalculateDailyConstructionPowerWithoutBoost(Town town)
        {
            int native = base.CalculateDailyConstructionPowerWithoutBoost(town);
            float eventFactor = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetConstructionFactor(town) ?? 0f;
#if !REIGN_EXCLUDE_COURT
            float spyFactor = ReignCourtCampaignBehavior.Instance?.GetSpymasterConstructionFactor(town) ?? 0f;
#else
            float spyFactor = 0f;
#endif
            return Math.Max(0, (int)Math.Floor(native * (1f + eventFactor + spyFactor)));
        }
    }
}
