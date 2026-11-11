using System;
using System.Reflection;
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
        [ThreadStatic] private static int _potentialDepth;
        internal static bool IsPotential => _potentialDepth > 0;
        internal static float PotentialProduction(Village village, ItemObject item)
        {
            _potentialDepth++;
            try { return ObserveWithoutRandomAdvance(() => TaleWorlds.CampaignSystem.Campaign.Current.Models.VillageProductionCalculatorModel.CalculateDailyProductionAmount(village, item).ResultNumber); }
            finally { _potentialDepth--; }
        }
        private static readonly PropertyInfo NativeRandom = typeof(MBRandom).GetProperty("Random", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo[] NativeRandomState = Array.ConvertAll(new[] { "_x", "_y", "_z", "_w" },
            name => typeof(MBFastRandom).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic));
        // Native Shepherd production draws from the campaign RNG. Model quotations must only peek.
        // Calls and restoration are synchronous on the campaign thread, including nested quotations.
        internal static float ObserveWithoutRandomAdvance(Func<float> observe)
        {
            var random = NativeRandom?.GetValue(null, null) as MBFastRandom
                ?? throw new InvalidOperationException("Native production observation RNG unavailable.");
            if (Array.Exists(NativeRandomState, field => field == null || field.FieldType != typeof(uint)))
                throw new InvalidOperationException("Native production observation RNG state changed.");
            var before = Array.ConvertAll(NativeRandomState, field => field.GetValue(random));
            try { return observe(); }
            finally { for (int i = 0; i < NativeRandomState.Length; i++) NativeRandomState[i].SetValue(random, before[i]); }
        }
        internal static float CommodityBaseline(Village village) => (float)ReignWarEconomyPolicy.HealthyFood(village.Hearth) / 12f;

        public override float CalculateDailyFoodProductionAmount(Village village)
        {
            if (village == null || village.VillageState != Village.VillageStates.Normal) return 0f;
            // The supplementary native channel has only a hearth baseline and multiplicative issue effects.
            float native = base.CalculateDailyFoodProductionAmount(village);
            float recovery = IsPotential ? 1f : ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village) ?? 1f;
            return Math.Max(0, native / (village.GetHearthLevel() + 1) * (float)ReignWarEconomyPolicy.HealthyFood(village.Hearth) / 6f * recovery);
        }
        public override ExplainedNumber CalculateDailyProductionAmount(Village village, ItemObject item)
        {
            if (village == null || village.VillageState != Village.VillageStates.Normal)
                return new ExplainedNumber(0f);
            ExplainedNumber result = base.CalculateDailyProductionAmount(village, item);
#if !REIGN_EXCLUDE_COURT
            float factor = ReignCourtCampaignBehavior.Instance?.GetDocketVillageProductionFactor(village) ?? 0f;
            if (factor != 0f)
                result.AddFactor(factor, new TextObject("{=!}Royal petition production support"));
#endif
            float recovery = IsPotential ? 1f : ReignEconomyCampaignBehavior.Instance?.GetRecoveryModifier(village) ?? 1f;
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                -Math.Max(0, result.ResultNumber) * (1 - recovery), new TextObject("{=!}Agricultural recovery"));
            result.LimitMin(0);
            return result;
        }
    }

    internal sealed class ReignSettlementFoodModel : DefaultSettlementFoodModel
    {
        // Native consumption still applies siege, governor and building modifiers to this baseline.
        public override int NumberOfMenOnGarrisonToEatOneFood => ReignEconomyPatches.QuarteringCounterfactual ? 20 : ReignWarEconomyPolicy.GarrisonMenPerFood;
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
                float actual = (float)ReignWarEconomyPolicy.HealthyFood(village.Hearth) * modifier;
                result.Add(actual - nativeContribution,
                    new TextObject("{=!}{VILLAGE}: households and agricultural recovery").SetTextVariable("VILLAGE", village.Name));
                effectiveVillageContribution += actual;
            }

            float eventFactor = ReignKingdomEventsCampaignBehavior.Instance
                ?.GetFoodProductionFactor(town) ?? 0f;
#if !REIGN_EXCLUDE_COURT
            float spyFactor = ReignCourtCampaignBehavior.Instance?.GetSpymasterFoodFactor(town) ?? 0f;
#else
            float spyFactor = 0f;
#endif
            // Clamp the affected production, never the final production-minus-consumption total.
            float combined = Math.Max(-1f, eventFactor + spyFactor);
            if (combined != 0)
                result.Add(effectiveVillageContribution * combined, new TextObject("{=!}Harvest conditions and food disruption"));

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
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                (float)(ReignRulerCampaignBehavior.Instance?.Modifiers(town).Security ?? 0),
                new TextObject("{=!}Ruler judgment consequences"));
            return result;
        }
    }

    internal sealed class ReignSettlementLoyaltyModel : DefaultSettlementLoyaltyModel
    {
        public override float GovernorDifferentCultureLoyaltyEffect => ReignRulerCampaignBehavior.Enabled(ReignBeta.Shared.Rulers.RulerOptions.GovernorCulture) ? 0f : base.GovernorDifferentCultureLoyaltyEffect;
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
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                (float)(ReignRulerCampaignBehavior.Instance?.Modifiers(town).Loyalty ?? 0),
                new TextObject("{=!}Ruler judgment consequences"));
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
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result,
                (float)(ReignRulerCampaignBehavior.Instance?.Modifiers(fortification).Prosperity ?? 0),
                new TextObject("{=!}Ruler judgment consequences"));
            return result;
        }

        public override ExplainedNumber CalculateHearthChange(
            Village village,
            bool includeDescriptions = false)
        {
            ExplainedNumber result = base.CalculateHearthChange(village, includeDescriptions);
            if (village.VillageState == Village.VillageStates.Normal)
            {
                float nativeBaseline = village.Hearth < 300 ? 4f : village.Hearth < 600 ? 1.2f : .2f;
                float natural = ReignEconomyCampaignBehavior.Instance?.GetNaturalGrowth(village)
                    ?? (float)ReignWarEconomyPolicy.NaturalGrowth(village.Hearth, 0, ReignEconomyCampaignBehavior.IsAgricultureStressed(village));
                // Add changes the baseline under existing native factors; unrelated perk/building flats remain intact.
                result.Add(natural - nativeBaseline, new TextObject("{=!}Household recovery after mobilization"));
            }
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
            ReignBeta.ClanAccords.ReignClanAccordModelMath.AddFinalDelta(ref result, governmentDelta,
                new TextObject("{=!}Representative government authority"));
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
