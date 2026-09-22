using System;
using ReignBeta.UI.HiddenInformation;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace ReignBeta.Court
{
    public sealed class ReignCourtOfficePerformance
    {
        public int RoleSkill;
        public int Loyalty;
        public bool IsAbsent;
        public bool HasConflictingDuty;
        public float SpeedMultiplier;
        public float CostMultiplier;
        public float ReportReliability;

        public string Summary => "Role skill " + RoleSkill
            + (ReignHiddenInformationPolicy.IsCheatRevealEnabled
                ? " • Loyalty " + Loyalty.ToString("+0;-0;0")
                    + " • Speed x" + SpeedMultiplier.ToString("0.00")
                    + " • Cost x" + CostMultiplier.ToString("0.00")
                    + " • Report reliability " + (ReportReliability * 100f).ToString("0") + "%"
                : string.Empty)
            + (IsAbsent ? " • Absent" : string.Empty) + (HasConflictingDuty ? " • Conflicting native duty" : string.Empty);
    }

    public sealed class ReignCourtIntelligenceQuote
    {
        public int GoldCost;
        public float InfluenceCost;
        public float DurationDays;
        public float Risk;
        public float ReportReliability;
    }

    public static class ReignCourtOfficePerformanceService
    {
        public static ReignCourtOfficePerformance Evaluate(ReignCourtOffice office, Hero hero, CourtSession session)
        {
            if (hero == null) return new ReignCourtOfficePerformance { SpeedMultiplier = 0.5f, CostMultiplier = 1.35f, ReportReliability = 0.25f, IsAbsent = true };
            int skill = RoleSkill(office, hero);
            int loyalty = Hero.MainHero == null || hero == Hero.MainHero ? 100 : hero.GetRelation(Hero.MainHero);
            bool absent = hero.IsPrisoner || hero.CurrentSettlement?.StringId != session?.HostSettlementStringId;
            bool conflict = hero.GovernorOf != null || hero.PartyBelongedTo?.Army != null;
            float skill01 = Math.Max(0f, Math.Min(1f, skill / 300f));
            float loyalty01 = Math.Max(0f, Math.Min(1f, (loyalty + 100f) / 200f));
            float integrity = Math.Max(0.2f, Math.Min(1f, 0.65f + hero.GetTraitLevel(DefaultTraits.Honor) * 0.12f + loyalty01 * 0.2f));
            float dutyPenalty = (absent ? 0.18f : 0f) + (conflict ? 0.12f : 0f) + (hero.IsPrisoner ? 0.4f : 0f);
            float effectiveness = Math.Max(0.1f, Math.Min(1f, skill01 * 0.58f + loyalty01 * 0.22f + integrity * 0.2f - dutyPenalty));
            return new ReignCourtOfficePerformance
            {
                RoleSkill = skill,
                Loyalty = loyalty,
                IsAbsent = absent,
                HasConflictingDuty = conflict,
                SpeedMultiplier = 0.65f + effectiveness * 0.7f,
                CostMultiplier = 1.3f - effectiveness * 0.4f,
                ReportReliability = Math.Max(0.2f, Math.Min(0.95f, 0.25f + effectiveness * 0.7f))
            };
        }

        public static ReignCourtIntelligenceQuote QuoteIntelligence(ReignCourtOfficePerformance performance, int gold, float influence, float duration, float risk)
        {
            performance = performance ?? new ReignCourtOfficePerformance { SpeedMultiplier = 0.5f, CostMultiplier = 1.35f, ReportReliability = 0.25f };
            return new ReignCourtIntelligenceQuote
            {
                GoldCost = (int)Math.Ceiling(Math.Max(0, gold) * performance.CostMultiplier / 10f) * 10,
                InfluenceCost = Math.Max(0f, influence),
                DurationDays = Math.Max(1f, duration / Math.Max(0.25f, performance.SpeedMultiplier)),
                Risk = Math.Max(0f, Math.Min(1f, risk + (1f - performance.ReportReliability) * 0.12f)),
                ReportReliability = performance.ReportReliability
            };
        }

        private static int RoleSkill(ReignCourtOffice office, Hero hero)
        {
            switch (office)
            {
                case ReignCourtOffice.Steward: return hero.GetSkillValue(DefaultSkills.Steward);
                case ReignCourtOffice.EconomicAdvisor: return hero.GetSkillValue(DefaultSkills.Trade);
                case ReignCourtOffice.ForeignAdvisor: return hero.GetSkillValue(DefaultSkills.Charm);
                case ReignCourtOffice.Marshal: return Math.Max(hero.GetSkillValue(DefaultSkills.Leadership), hero.GetSkillValue(DefaultSkills.Tactics));
                case ReignCourtOffice.Spymaster: return hero.GetSkillValue(DefaultSkills.Roguery);
                default: return 0;
            }
        }
    }
}
