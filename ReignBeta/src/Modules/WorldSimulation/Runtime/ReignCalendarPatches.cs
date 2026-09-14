using System;
using HarmonyLib;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Runtime
{
    public static class ReignCalendarPatches
    {
        private static bool _applied;

        public static void Apply()
        {
            if (_applied)
            {
                return;
            }

            try
            {
                Harmony harmony = new Harmony("com.bannerlordreign.reignbeta.calendar");
                Type[] patches =
                {
                    typeof(HeroAgePatch),
                    typeof(DaysInYearPatch),
                    typeof(DaysInSeasonPatch),
                    typeof(GetYearPatch),
                    typeof(GetDayOfYearPatch),
                    typeof(GetDayOfSeasonPatch),
                    typeof(GetWeekOfSeasonPatch),
                    typeof(GetSeasonOfYearPatch),
                    typeof(ToYearsPatch),
                    typeof(ElapsedYearsPatch),
                    typeof(RemainingYearsPatch),
                    typeof(YearsPatch),
                    typeof(YearsFromNowPatch)
                };

                foreach (Type patch in patches)
                {
                    harmony.CreateClassProcessor(patch).Patch();
                }

                _applied = true;
                ReignLog.Info("Reign 126-day calendar patches loaded.");
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign calendar Harmony setup failed: " + ex);
            }
        }

        [HarmonyPatch(typeof(Hero), "Age", MethodType.Getter)]
        public static class HeroAgePatch
        {
            public static void Postfix(Hero __instance, ref float __result)
            {
                __result = ReignCalendarService.AdjustHeroAge(__instance, __result);
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "DaysInYear", MethodType.Getter)]
        public static class DaysInYearPatch
        {
            public static void Postfix(ref int __result)
            {
                if (ReignCalendarService.IsReady) __result = 126;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "DaysInSeason", MethodType.Getter)]
        public static class DaysInSeasonPatch
        {
            public static void Postfix(ref int __result)
            {
                if (ReignCalendarService.IsReady) __result = 32;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "GetYear", MethodType.Getter)]
        public static class GetYearPatch
        {
            public static void Postfix(CampaignTime __instance, ref int __result)
            {
                if (ReignCalendarService.TryGetCalendarPosition(__instance, out int year, out _, out _, out _, out _)) __result = year;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "GetDayOfYear", MethodType.Getter)]
        public static class GetDayOfYearPatch
        {
            public static void Postfix(CampaignTime __instance, ref int __result)
            {
                if (ReignCalendarService.TryGetCalendarPosition(__instance, out _, out int day, out _, out _, out _)) __result = day;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "GetDayOfSeason", MethodType.Getter)]
        public static class GetDayOfSeasonPatch
        {
            public static void Postfix(CampaignTime __instance, ref int __result)
            {
                if (ReignCalendarService.TryGetCalendarPosition(__instance, out _, out _, out _, out int day, out _)) __result = day;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "GetWeekOfSeason", MethodType.Getter)]
        public static class GetWeekOfSeasonPatch
        {
            public static void Postfix(CampaignTime __instance, ref int __result)
            {
                if (ReignCalendarService.TryGetCalendarPosition(__instance, out _, out _, out _, out _, out int week)) __result = week;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "GetSeasonOfYear", MethodType.Getter)]
        public static class GetSeasonOfYearPatch
        {
            public static void Postfix(CampaignTime __instance, ref CampaignTime.Seasons __result)
            {
                if (ReignCalendarService.TryGetCalendarPosition(__instance, out _, out _, out int season, out _, out _)) __result = (CampaignTime.Seasons)season;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "ToYears", MethodType.Getter)]
        public static class ToYearsPatch
        {
            public static void Postfix(CampaignTime __instance, ref double __result)
            {
                if (ReignCalendarService.IsReady) __result = __instance.ToDays / ReignCalendarService.DaysPerYear;
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "ElapsedYearsUntilNow", MethodType.Getter)]
        public static class ElapsedYearsPatch
        {
            public static void Postfix(CampaignTime __instance, ref float __result)
            {
                if (ReignCalendarService.IsReady) __result = (float)((CampaignTime.Now.ToDays - __instance.ToDays) / ReignCalendarService.DaysPerYear);
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "RemainingYearsFromNow", MethodType.Getter)]
        public static class RemainingYearsPatch
        {
            public static void Postfix(CampaignTime __instance, ref float __result)
            {
                if (ReignCalendarService.IsReady) __result = (float)((__instance.ToDays - CampaignTime.Now.ToDays) / ReignCalendarService.DaysPerYear);
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "Years", new[] { typeof(float) })]
        public static class YearsPatch
        {
            public static void Postfix(float __0, ref CampaignTime __result)
            {
                if (ReignCalendarService.IsReady) __result = ReignCalendarService.ConvertYearsValue(__0);
            }
        }

        [HarmonyPatch(typeof(CampaignTime), "YearsFromNow", new[] { typeof(float) })]
        public static class YearsFromNowPatch
        {
            public static void Postfix(float __0, ref CampaignTime __result)
            {
                if (ReignCalendarService.IsReady) __result = CampaignTime.Now + CampaignTime.Days((float)(__0 * ReignCalendarService.DaysPerYear));
            }
        }
    }
}
