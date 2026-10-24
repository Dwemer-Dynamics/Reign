using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using ReignBeta.ClanAccords;
using ReignBeta.Economy;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Settlements.Workshops;
using TaleWorlds.Localization;

namespace ReignBeta.Government
{
    // Patch final Reign model overrides, not both their base and override. Every native
    // effect is applied once and remains inside Bannerlord's actual economy calculation.
    [HarmonyPatch]
    internal static class ReignGovernmentEdictModelPatch
    {
        private static JObject _observation;
        private static int _observationThreadId;

        // Called only by the exact-save guarded snapshot harness. This observes a
        // real model call; it never substitutes a result or suppresses an edict.
        internal static JObject ObserveNativeModel(string method, Settlement settlement, Func<ExplainedNumber> calculate)
        {
            if (_observation != null) throw new InvalidOperationException("An edict model observation is already active.");
            var observation = new JObject { ["method"] = method, ["settlementId"] = settlement.StringId, ["calls"] = 0 };
            _observationThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            _observation = observation;
            try
            {
                observation["returnedResult"] = calculate().ResultNumber;
                return observation;
            }
            finally { _observation = null; _observationThreadId = 0; }
        }

        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ReignSettlementLoyaltyModel), "CalculateLoyaltyChange");
            yield return AccessTools.Method(typeof(ReignSettlementProsperityModel), "CalculateProsperityChange");
            yield return AccessTools.Method(typeof(ReignVillageProductionCalculatorModel), "CalculateDailyProductionAmount");
            yield return AccessTools.Method(typeof(ReignBuildingConstructionModel), "CalculateDailyConstructionPower");
            yield return AccessTools.Method(typeof(DefaultSettlementTaxModel), "CalculateTownTax");
            yield return AccessTools.Method(typeof(DefaultClanFinanceModel), "CalculateTownIncomeFromTariffs");
            yield return AccessTools.Method(typeof(ReignClanAccordsWageModel), "GetTotalWage");
            yield return AccessTools.Method(typeof(DefaultWorkshopModel), "GetEffectiveConversionSpeedOfProduction");
        }
        private static void Postfix(MethodBase __originalMethod, object[] __args, ref ExplainedNumber __result)
        {
            var behavior = ReignGovernmentCampaignBehavior.Instance;
            if (behavior == null) return;
            Settlement settlement = __args.OfType<Town>().FirstOrDefault()?.Settlement
                ?? __args.OfType<Village>().FirstOrDefault()?.Settlement
                ?? __args.OfType<Workshop>().FirstOrDefault()?.Settlement;
            var party = __args.OfType<MobileParty>().FirstOrDefault();
            if (__originalMethod.Name == "GetTotalWage")
            {
                if (party?.IsGarrison != true) return;
                settlement = party.CurrentSettlement;
            }
            var effects = behavior.EdictEffects(settlement);
            double delta = 0, factor = 0;
            switch (__originalMethod.Name)
            {
                case "CalculateLoyaltyChange": delta = effects.Loyalty; break;
                case "CalculateProsperityChange": delta = effects.Prosperity; break;
                case "CalculateDailyProductionAmount": factor = effects.Production; break;
                case "CalculateDailyConstructionPower": factor = effects.Construction; break;
                case "CalculateTownTax": factor = effects.Tax; break;
                case "CalculateTownIncomeFromTariffs": factor = effects.Tariffs; break;
                case "GetTotalWage": factor = effects.Wages; break;
                case "GetEffectiveConversionSpeedOfProduction": factor = effects.WorkshopProduction; break;
            }
            bool observe = _observation != null
                && _observationThreadId == System.Threading.Thread.CurrentThread.ManagedThreadId
                && _observation.Value<string>("method") == __originalMethod.Name
                && _observation.Value<string>("settlementId") == settlement?.StringId;
            float before = observe ? __result.ResultNumber : 0f;
            double flatDelta = delta;
            float limitMin = observe ? __result.LimitMinValue : float.MinValue;
            float limitMax = observe ? __result.LimitMaxValue : float.MaxValue;
            if (factor != 0) delta += Math.Max(0, __result.ResultNumber) * factor;
            ReignClanAccordModelMath.AddFinalDelta(ref __result, (float)delta, new TextObject("{=!}Voted government edicts"));
            if (observe)
            {
                _observation["calls"] = _observation.Value<int>("calls") + 1;
                _observation["beforeEdicts"] = before;
                _observation["factor"] = factor;
                _observation["flatDelta"] = flatDelta;
                _observation["afterEdicts"] = __result.ResultNumber;
                _observation["limitMin"] = limitMin;
                _observation["limitMax"] = limitMax;
            }
        }
    }

    [HarmonyPatch]
    internal static class ReignGovernmentEdictVolunteerPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        { yield return AccessTools.Method(typeof(DefaultVolunteerModel), "GetDailyVolunteerProductionProbability"); }
        private static void Postfix(Settlement settlement, ref float __result)
        {
            var effects = ReignGovernmentCampaignBehavior.Instance?.EdictEffects(settlement);
            if (effects != null) __result = Math.Max(0, __result * (1f + (float)effects.RecruitReplenishment));
        }
    }

}
