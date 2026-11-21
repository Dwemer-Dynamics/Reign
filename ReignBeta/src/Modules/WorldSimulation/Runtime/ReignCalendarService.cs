using System;
using System.Collections.Generic;
using System.IO;
using Reign.Core.Contracts.Platform;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;

namespace ReignBeta.WorldSimulation.Runtime
{
    public static class ReignCalendarService
    {
        public const double DaysPerYear = 126d;
        public const double DaysPerSeason = 31.5d;
        public const int SeasonsPerYear = 4;
        public const string PromptRule = "Bannerlord Reign calendar: one year is 126 campaign days; there are four seasons, each lasting 31.5 days. Every character ages at this same 126-day-per-year rate. Never use the native 84-day year or 21-day season.";

        private static readonly object Gate = new object();
        private static ReignCalendarStore _store;
        private static WeakReference<TaleWorlds.CampaignSystem.Campaign> _owner;
        private static bool _dirty;

        private static ReignCalendarState CurrentState =>
            _store != null && _owner.TryGetTarget(out var owner) && ReferenceEquals(owner, TaleWorlds.CampaignSystem.Campaign.Current)
                ? _store.State : null;

        public static bool IsReady
        {
            get
            {
                lock (Gate)
                {
                    return CurrentState != null;
                }
            }
        }

        public static void InitializeForCurrentCampaign()
        {
            lock (Gate)
            {
                if (CurrentState != null) return;
                Reset();
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                string campaignId = campaign?.UniqueGameId?.Trim();
                var save = ReignSaveSyncCampaignBehavior.Instance;
                if (!ReignSaveSyncCampaignBehavior.IsDurableCampaignId(campaignId) || save == null
                    || !ReignSaveSyncCoordinator.IsReadyForCampaign(campaignId))
                    throw new InvalidOperationException("The native campaign identity and Save Sync alignment must be ready before loading its calendar.");
                var store = ReignCalendarStore.Open(StatePath(campaignId), campaignId, save.CanCreateCalendar,
                    save.RegisteredCalendarSha256, () => CreateStateFromNativeCalendar(campaignId));
                var state = store.State;
                bool needsPublish = store.PublishedSha256 == null;

                double now = CampaignTime.Now.ToDays;
                state.ValidateTimeline(CampaignTime.Never.ToDays, now, DaysPerYear);
                foreach (Hero hero in Hero.AllAliveHeroes)
                {
                    if (hero == null || string.IsNullOrWhiteSpace(hero.StringId) || state.AgeAnchors.ContainsKey(hero.StringId))
                    {
                        continue;
                    }

                    float nativeAge = hero.Age;
                    if (nativeAge >= 0f && nativeAge < 200f)
                    {
                        state.AgeAnchors[hero.StringId] = new ReignAgeAnchor { Day = now, Age = nativeAge };
                        needsPublish = true;
                    }
                }

                if (needsPublish) store.Publish();
                _owner = new WeakReference<TaleWorlds.CampaignSystem.Campaign>(campaign);
                _store = store;
                ReignLog.Info("Reign calendar initialized campaign=" + campaignId
                    + " daysPerYear=" + DaysPerYear.ToString("0")
                    + " daysPerSeason=" + DaysPerSeason.ToString("0.0")
                    + " ageAnchors=" + state.AgeAnchors.Count + ".");
            }
        }

        public static float AdjustHeroAge(Hero hero, float engineAge)
        {
            lock (Gate)
            {
                var state = CurrentState;
                if (state == null || hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                {
                    return engineAge;
                }

                if (!state.AgeAnchors.TryGetValue(hero.StringId, out ReignAgeAnchor anchor))
                {
                    if (!(engineAge >= 0f && engineAge < 200f))
                    {
                        return engineAge;
                    }

                    anchor = new ReignAgeAnchor { Day = CampaignTime.Now.ToDays, Age = engineAge };
                    state.AgeAnchors[hero.StringId] = anchor;
                    _dirty = true;
                }

                double endDay = CampaignTime.Now.ToDays;
                if (!hero.IsAlive && hero.DeathDay != CampaignTime.Never)
                {
                    endDay = Math.Min(endDay, hero.DeathDay.ToDays);
                }

                return anchor.AgeAt(endDay, DaysPerYear);
            }
        }

        public static void OnHeroBirthDayChanged(Hero hero, CampaignTime birthDay)
        {
            lock (Gate)
            {
                var state = CurrentState;
                if (state == null || hero == null || string.IsNullOrWhiteSpace(hero.StringId))
                    return;

                // Explicit native age edits must replace the migration anchor as well as the
                // birthday. Ordinary aging still advances from this point at 126 days per year.
                float age = birthDay.ElapsedYearsUntilNow;
                if (float.IsNaN(age) || float.IsInfinity(age) || age < 0f || age >= 200f)
                    return;
                state.AgeAnchors[hero.StringId] = new ReignAgeAnchor { Day = CampaignTime.Now.ToDays, Age = age };
                _dirty = true;
                Flush();
            }
        }

        public static bool TryGetAbsoluteCalendarDay(CampaignTime time, out double day)
        {
            lock (Gate)
            {
                day = 0;
                var state = CurrentState;
                if (state == null) return false;
                day = state.CalendarDayAt(time.ToDays);
                return ReignCalendarState.HasRepresentableYear(day, DaysPerYear);
            }
        }

        public static bool TryFromAbsoluteCalendarDay(double day, out CampaignTime time)
        {
            lock (Gate)
            {
                time = CampaignTime.Never;
                var state = CurrentState;
                if (state == null || double.IsNaN(day) || double.IsInfinity(day)) return false;
                double nativeDay = state.NativeDayAt(day);
                if (!(Math.Abs(nativeDay) < CampaignTime.Never.ToDays)) return false;
                // Days(float) also multiplies in single precision. Preserve the anchor through the
                // native millisecond factory so half-day season boundaries do not drift a day in UI.
                time = CampaignTime.Milliseconds((long)Math.Round(nativeDay * CampaignTime.Days(1f).ToMilliseconds));
                return true;
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
                var state = CurrentState;
                if (state == null)
                {
                    return false;
                }

                double totalDays = state.CalendarDayAt(time.ToDays);
                if (!ReignCalendarState.HasRepresentableYear(totalDays, DaysPerYear)) return false;
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
                var state = CurrentState;
                if (state == null || Math.Abs(years) < 200f)
                {
                    return CampaignTime.Days((float)(years * DaysPerYear));
                }

                double nativeDay = state.NativeDayAt(years * DaysPerYear);
                return CampaignTime.Days((float)nativeDay);
            }
        }

        public static void Flush()
        {
            lock (Gate)
            {
                if (CurrentState == null || !_dirty) return;
                try { PublishDirty(); }
                catch (Exception ex) { ReignLog.Warn("Reign calendar state save failed: " + ex.Message); }
            }
        }

        internal static string FlushForSavePoint(string campaignId)
        {
            lock (Gate)
            {
                if (CurrentState == null || !string.Equals(campaignId, _store.State.CampaignId, StringComparison.Ordinal))
                    throw new InvalidOperationException("The current campaign calendar is unavailable for Save Sync.");
                if (_dirty) PublishDirty();
                else _store.VerifyPublished();
                return _store.PublishedSha256;
            }
        }

        public static void Reset()
        {
            lock (Gate)
            {
                // Both native load and Save Sync rollback may have just replaced the sidecar.
                // The previous in-memory state no longer owns that file.
                _store = null;
                _owner = null;
                _dirty = false;
            }
        }

        private static ReignCalendarState CreateStateFromNativeCalendar(string campaignId)
        {
            double nativeDay = CampaignTime.Now.ToDays;
            int nativeYear = CampaignTime.Now.GetYear;
            int nativeDayOfYear = CampaignTime.Now.GetDayOfYear;
            double dayFraction = nativeDay - Math.Floor(nativeDay);
            return new ReignCalendarState
            {
                Version = 1,
                CampaignId = campaignId,
                NativeDayAnchor = nativeDay,
                CalendarDayAnchor = (nativeYear * DaysPerYear) + (nativeDayOfYear * 1.5d) + dayFraction,
                AgeAnchors = new Dictionary<string, ReignAgeAnchor>(StringComparer.OrdinalIgnoreCase)
            };
        }

        private static void PublishDirty()
        {
            _store.Publish();
            _dirty = false;
        }

        private static string StatePath(string campaignId)
        {
            string folder = ReignCampaignIdentity.SafePathSegment(campaignId, "unknown");
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
    }
}
