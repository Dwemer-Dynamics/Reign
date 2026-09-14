using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using ReignBeta.Integration;
using Reign.Core.Contracts.Platform;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.Runtime
{
    public static class ReignCalendarService
    {
        public const double DaysPerYear = 126d;
        public const double DaysPerSeason = 31.5d;
        public const int SeasonsPerYear = 4;
        public const string PromptRule = "Bannerlord Reign calendar: one year is 126 campaign days; there are four seasons, each lasting 31.5 days. Every character ages at this same 126-day-per-year rate. Never use the native 84-day year or 21-day season.";

        private static readonly object Gate = new object();
        private static ReignCalendarState _state;
        private static string _campaignId;
        private static bool _ready;
        private static bool _dirty;

        public static bool IsReady
        {
            get
            {
                lock (Gate)
                {
                    return _ready && TaleWorlds.CampaignSystem.Campaign.Current != null;
                }
            }
        }

        public static void InitializeForCurrentCampaign()
        {
            lock (Gate)
            {
                _ready = false;
                _campaignId = ReignCampaignIdentity.CurrentCampaignId();
                _state = LoadState() ?? CreateStateFromNativeCalendar();
                _state.AgeAnchors = _state.AgeAnchors ?? new Dictionary<string, ReignAgeAnchor>(StringComparer.OrdinalIgnoreCase);

                double now = CampaignTime.Now.ToDays;
                foreach (Hero hero in Hero.AllAliveHeroes)
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId) || _state.AgeAnchors.ContainsKey(hero.StringId))
                    {
                        continue;
                    }

                    float nativeAge = hero.Age;
                    if (nativeAge >= 0f && nativeAge < 200f)
                    {
                        _state.AgeAnchors[hero.StringId] = new ReignAgeAnchor { Day = now, Age = nativeAge };
                        _dirty = true;
                    }
                }

                _ready = true;
                SaveIfDirty(true);
                ReignLog.Info("Reign calendar initialized campaign=" + _campaignId
                    + " daysPerYear=" + DaysPerYear.ToString("0")
                    + " daysPerSeason=" + DaysPerSeason.ToString("0.0")
                    + " ageAnchors=" + _state.AgeAnchors.Count + ".");
            }
        }

        public static float AdjustHeroAge(Hero hero, float engineAge)
        {
            lock (Gate)
            {
                if (!_ready || _state == null || hero == null || string.IsNullOrWhiteSpace(hero.StringId) || TaleWorlds.CampaignSystem.Campaign.Current == null)
                {
                    return engineAge;
                }

                if (!_state.AgeAnchors.TryGetValue(hero.StringId, out ReignAgeAnchor anchor))
                {
                    if (engineAge < 0f || engineAge >= 200f)
                    {
                        return engineAge;
                    }

                    anchor = new ReignAgeAnchor { Day = CampaignTime.Now.ToDays, Age = engineAge };
                    _state.AgeAnchors[hero.StringId] = anchor;
                    _dirty = true;
                }

                double endDay = CampaignTime.Now.ToDays;
                if (!hero.IsAlive && hero.DeathDay != CampaignTime.Never)
                {
                    endDay = Math.Min(endDay, hero.DeathDay.ToDays);
                }

                double adjusted = anchor.Age + ((endDay - anchor.Day) / DaysPerYear);
                return (float)Math.Max(0d, Math.Min(200d, adjusted));
            }
        }

        public static bool TryGetCalendarPosition(CampaignTime time, out int year, out int dayOfYear, out int season, out int dayOfSeason, out int weekOfSeason)
        {
            lock (Gate)
            {
                year = 0;
                dayOfYear = 0;
                season = 0;
                dayOfSeason = 0;
                weekOfSeason = 0;
                if (!_ready || _state == null)
                {
                    return false;
                }

                double totalDays = _state.CalendarDayAnchor + (time.ToDays - _state.NativeDayAnchor);
                year = FloorDiv(totalDays, DaysPerYear);
                double withinYear = PositiveModulo(totalDays, DaysPerYear);
                dayOfYear = (int)Math.Floor(withinYear);
                season = Math.Max(0, Math.Min(SeasonsPerYear - 1, (int)Math.Floor(withinYear / DaysPerSeason)));
                double withinSeason = withinYear - (season * DaysPerSeason);
                dayOfSeason = Math.Max(0, (int)Math.Floor(withinSeason));
                weekOfSeason = dayOfSeason / 7;
                return true;
            }
        }

        public static string BuildPromptCalendarContext()
        {
            if (TryGetCalendarPosition(CampaignTime.Now, out int year, out _, out int season, out int dayOfSeason, out _))
            {
                return PromptRule + " Current calendar date: " + year + " " + SeasonName(season) + ", day " + (dayOfSeason + 1) + " of the season.";
            }

            return PromptRule;
        }

        public static CampaignTime ConvertYearsValue(float years)
        {
            lock (Gate)
            {
                if (!_ready || _state == null || Math.Abs(years) < 200f)
                {
                    return CampaignTime.Days((float)(years * DaysPerYear));
                }

                double nativeDay = _state.NativeDayAnchor + ((years * DaysPerYear) - _state.CalendarDayAnchor);
                return CampaignTime.Days((float)nativeDay);
            }
        }

        public static void Flush()
        {
            lock (Gate)
            {
                SaveIfDirty(false);
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                SaveIfDirty(false);
                _ready = false;
                _state = null;
                _campaignId = null;
            }
        }

        private static ReignCalendarState CreateStateFromNativeCalendar()
        {
            double nativeDay = CampaignTime.Now.ToDays;
            int nativeYear = CampaignTime.Now.GetYear;
            int nativeDayOfYear = CampaignTime.Now.GetDayOfYear;
            double dayFraction = nativeDay - Math.Floor(nativeDay);
            return new ReignCalendarState
            {
                Version = 1,
                CampaignId = _campaignId,
                NativeDayAnchor = nativeDay,
                CalendarDayAnchor = (nativeYear * DaysPerYear) + (nativeDayOfYear * 1.5d) + dayFraction,
                AgeAnchors = new Dictionary<string, ReignAgeAnchor>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static ReignCalendarState LoadState()
        {
            try
            {
                string path = StatePath();
                if (!File.Exists(path))
                {
                    return null;
                }

                ReignCalendarState state = JsonConvert.DeserializeObject<ReignCalendarState>(File.ReadAllText(path));
                return state != null && state.Version == 1 ? state : null;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign calendar state load failed: " + ex.Message);
                return null;
            }
        }

        private static void SaveIfDirty(bool force)
        {
            if (_state == null || (!force && !_dirty))
            {
                return;
            }

            try
            {
                string path = StatePath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonConvert.SerializeObject(_state, Formatting.Indented));
                _dirty = false;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Reign calendar state save failed: " + ex.Message);
            }
        }

        private static string StatePath()
        {
            string folder = ReignCampaignIdentity.SafePathSegment(_campaignId, "unknown");
            string dataRoot = ReignInstallation.TryLoadCurrent()?.DataRoot
                ?? Path.Combine(BasePath.Name, "Modules", "ReignBeta", "server", "app", "data");
            return Path.Combine(dataRoot, "campaigns", folder, "world", "calendar.json");
        }

        private static int FloorDiv(double value, double divisor)
        {
            return (int)Math.Floor(value / divisor);
        }

        private static double PositiveModulo(double value, double modulus)
        {
            double result = value % modulus;
            return result < 0d ? result + modulus : result;
        }

        private static string SeasonName(int season)
        {
            switch (season)
            {
                case 0: return "Spring";
                case 1: return "Summer";
                case 2: return "Autumn";
                default: return "Winter";
            }
        }

        private sealed class ReignCalendarState
        {
            public int Version { get; set; }
            public string CampaignId { get; set; }
            public double NativeDayAnchor { get; set; }
            public double CalendarDayAnchor { get; set; }
            public Dictionary<string, ReignAgeAnchor> AgeAnchors { get; set; }
        }

        private sealed class ReignAgeAnchor
        {
            public double Day { get; set; }
            public float Age { get; set; }
        }
    }
}
