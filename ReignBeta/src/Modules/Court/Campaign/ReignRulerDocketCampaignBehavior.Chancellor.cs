using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Integration;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.Economy;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior
    {
        private static bool DocketIdentityKnownDead(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            Hero known = Hero.FindFirst(x => string.Equals(x?.StringId, id, StringComparison.OrdinalIgnoreCase));
            return known != null && !known.IsAlive;
        }

        internal void ProcessRulerDocketOfficeTick(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            if (state.LastOfficeTickDay >= day) return;
            state.LastOfficeTickDay = day;
            ReignChancellorOffice office = state.Chancellor;
            Hero chancellor = FindHero(office.HeroId);
            if (!office.IsVacant && !IsCurrentChancellorEligible(chancellor))
            {
                FinishChancellorTerm("became_ineligible", day, false);
                MaintainChancellorCaptivityLifecycle(day);
                office = state.Chancellor;
                chancellor = FindHero(office.HeroId);
            }

            // The 8 AM attempt settles the Active service day that just ended.
            // Inactive, emergency, captivity-continuity, and handoff days are unpaid.
            if (!office.IsVacant && !office.Emergency
                && office.State == ReignChancellorOfficeState.Active
                && state.ActiveChancellorDays.Contains(day - 1)
                && office.LastPaidDay < day - 1)
            {
                if (Hero.MainHero != null && chancellor != null
                    && office.Salary >= 0 && Hero.MainHero.Gold >= office.Salary)
                {
                    if (office.Salary > 0)
                        GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, chancellor,
                            office.Salary, true);
                    office.LastPaidDay = day - 1;
                    office.TotalPaidGold = SaturatingAdd(office.TotalPaidGold, office.Salary);
                    office.PaidActiveDays++;
                    AddChancellorServiceHistory("salary_paid", day,
                        office.Salary + " denars were paid for the preceding Active service day.");
                }
                else
                {
                    office.FailedPaymentDays++;
                    office.State = ReignChancellorOfficeState.Inactive;
                    office.DesiredActive = false;
                    office.DesiredActiveEffectiveDay = -1;
                    AddChancellorServiceHistory("salary_unpaid_inactive", day,
                        "The full Active-day salary could not be paid, so the Chancellor became Inactive before petitions were drawn.");
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[Bannerlord Reign] The Chancellor could not be paid in full and is now Inactive."));
                }
            }

            if (!office.IsVacant && !office.Emergency
                && office.State != ReignChancellorOfficeState.CaptiveContinuity
                && office.State != ReignChancellorOfficeState.Handoff
                && office.DesiredActiveEffectiveDay >= 0
                && office.DesiredActiveEffectiveDay <= day)
            {
                office.State = office.DesiredActive
                    ? ReignChancellorOfficeState.Active
                    : ReignChancellorOfficeState.Inactive;
                office.DesiredActiveEffectiveDay = -1;
                AddChancellorServiceHistory(office.State == ReignChancellorOfficeState.Active
                        ? "activated" : "deactivated",
                    day, "The scheduled Chancellor state change took effect at the 8 AM docket boundary.");
            }
            if (state.Chancellor.SuppressesPetitions && !state.ActiveChancellorDays.Contains(day))
                state.ActiveChancellorDays.Add(day);
            MaintainChancellorResidence();
            ProcessChancellorAbsentLordReputation(day);
        }

        private void MaintainChancellorResidence()
        {
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            Hero hero = office.IsVacant ? null : FindHero(office.HeroId);
            if (hero == null || hero.IsPrisoner) return;
            RelinquishChancellorDuties(hero);
            TeleportPartylessHero(hero, FindCurrentCapital());
        }

        private void ProcessChancellorAbsentLordReputation(int day)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            List<int> activeDays = state.ActiveChancellorDays
                .Where(x => x >= day - ReignRulerDocketRules.AbsentLordWindowDays + 1
                    && x <= day).Distinct().OrderBy(x => x).ToList();
            bool thresholdMet = activeDays.Count >= ReignRulerDocketRules.AbsentLordThresholdDays;
            if (!thresholdMet)
            {
                state.ChancellorAbsentLordThresholdActive = false;
                return;
            }
            if (state.ChancellorAbsentLordThresholdActive) return;

            state.ChancellorAbsentLordThresholdActive = true;
            state.LastChancellorAbsentLordOccurrenceDay = day;
            Hero ruler = Hero.MainHero;
            if (ruler == null) return;
            ReignServices.WorldHistory?.RecordSocialOutcome(
                "absent_lord", ruler, "ruler",
                "chancellor_coverage|" + CurrentReignId() + "|window_end|" + day,
                ruler.Name + " delegated the morning docket to an Active Chancellor on "
                    + activeDays.Count + " of the last "
                    + ReignRulerDocketRules.AbsentLordWindowDays + " days.",
                new JObject
                {
                    ["windowEndDay"] = day,
                    ["windowDays"] = ReignRulerDocketRules.AbsentLordWindowDays,
                    ["activeChancellorDays"] = activeDays.Count,
                    ["thresholdDays"] = ReignRulerDocketRules.AbsentLordThresholdDays,
                    ["coveredDays"] = new JArray(activeDays),
                    ["sourceSystem"] = "ruler_docket_chancellor"
                }, null, false, false);
        }

        public bool TryScheduleChancellorActive(bool active, out string error)
        {
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            if (office.IsVacant)
            {
                error = "No Chancellor is appointed.";
                return false;
            }
            if (office.Emergency || office.State == ReignChancellorOfficeState.CaptiveContinuity
                || office.State == ReignChancellorOfficeState.Handoff)
            {
                error = "Emergency and handoff coverage remains Active until its lifecycle ends.";
                return false;
            }
            if (active && (Hero.MainHero == null || Hero.MainHero.Gold < office.Salary))
            {
                error = "Activation requires the ruler to hold at least one full Active-day salary ("
                    + office.Salary + " denars).";
                return false;
            }
            office.DesiredActive = active;
            office.DesiredActiveEffectiveDay = CurrentDay() + 1;
            error = string.Empty;
            StateChanged?.Invoke();
            return true;
        }

        public bool TryAppointChancellor(Hero candidate, int activeDailySalary,
            bool explicitValidatedAgreement, out string receipt)
        {
            receipt = string.Empty;
            if (!explicitValidatedAgreement)
            {
                receipt = "Appointment requires the candidate's explicit validated conversational agreement.";
                return false;
            }
            if (!EnsureRulerDocketState().Chancellor.IsVacant)
            {
                receipt = "The current Chancellor must be dismissed in direct conversation before another is appointed.";
                return false;
            }
            if (activeDailySalary < 0)
            {
                receipt = "The agreed Active-day salary must be a non-negative whole-denar amount.";
                return false;
            }
            if (!IsNormalChancellorCandidate(candidate, out string eligibilityError))
            {
                receipt = eligibilityError;
                return false;
            }
            Settlement capital = FindCurrentCapital();
            MobileParty party = candidate.PartyBelongedTo;
            if (party != null && party.LeaderHero == candidate
                && (party.MapEvent != null || party.Army != null))
            {
                receipt = "The candidate cannot safely relinquish an active battle or army command.";
                return false;
            }

            string duties = DescribeChancellorDuties(candidate);
            ReignChancellorOffice office = new ReignChancellorOffice
            {
                TermId = "chancellor_" + Guid.NewGuid().ToString("N"),
                HeroId = candidate.StringId,
                HeroName = candidate.Name?.ToString() ?? candidate.StringId,
                State = ReignChancellorOfficeState.Inactive,
                Salary = activeDailySalary,
                DesiredActive = false,
                AppointedDay = CurrentDay(),
                ServiceStartedDay = CurrentDay(),
                OriginalHomeSettlementId = candidate.HomeSettlement?.StringId ?? string.Empty,
                RelinquishedDuties = duties
            };
            EnsureRulerDocketState().Chancellor = office;
            RelinquishChancellorDuties(candidate);
            TeleportPartylessHero(candidate, capital);
            AddChancellorServiceHistory("appointed_inactive", CurrentDay(),
                "The candidate explicitly accepted appointment at " + activeDailySalary
                + " denars per Active day and began Inactive. Relinquished duties: " + duties + ".");
            receipt = office.HeroName + " is appointed Chancellor, initially Inactive, at "
                + activeDailySalary + " denars per Active day. Inactive days are unpaid.";
            StateChanged?.Invoke();
            return true;
        }

        public bool TryDismissChancellor(bool validatedRulerConversation, out string receipt)
        {
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            if (office.IsVacant) { receipt = "No Chancellor is appointed."; return false; }
            if (!validatedRulerConversation)
            {
                receipt = "Normal dismissal is available only through validated direct conversation.";
                return false;
            }
            FinishChancellorTerm("dismissed_by_ruler", CurrentDay(), office.Emergency);
            receipt = "The Chancellor has been dismissed.";
            return true;
        }

        internal void MaintainChancellorCaptivityLifecycle(int day, bool? rulerCaptiveOverride = null)
        {
            ReignRulerDocketState state = EnsureRulerDocketState();
            ReignChancellorOffice office = state.Chancellor;
            bool rulerCaptive = rulerCaptiveOverride ?? Hero.MainHero?.IsPrisoner == true;
            if (rulerCaptive)
            {
                if (!office.IsVacant && !IsCurrentChancellorEligible(FindHero(office.HeroId)))
                {
                    FinishChancellorTerm("emergency_holder_ineligible", day,
                        office.Emergency || office.State == ReignChancellorOfficeState.CaptiveContinuity
                        || office.State == ReignChancellorOfficeState.Handoff);
                    office = state.Chancellor;
                }
                if (office.IsVacant)
                {
                    Hero selected = SelectEmergencyChancellorCandidate();
                    if (selected != null) StartEmergencyChancellor(selected, day);
                    return;
                }
                if (office.State != ReignChancellorOfficeState.EmergencyActive
                    && office.State != ReignChancellorOfficeState.CaptiveContinuity)
                {
                    office.PreCaptureState = office.State;
                    office.PreCaptureDesiredActive = office.DesiredActive;
                    office.State = office.Emergency
                        ? ReignChancellorOfficeState.EmergencyActive
                        : ReignChancellorOfficeState.CaptiveContinuity;
                    office.EmergencyStartedDay = day;
                    office.RulerReleasedDay = -1;
                    office.HandoffDueDay = -1;
                    if (string.IsNullOrWhiteSpace(office.EmergencyCorrelationId))
                        office.EmergencyCorrelationId = "emergency_chancellor_" + office.TermId;
                    QueueEmergencyWorldHistory(office, "started", day, string.Empty);
                    AddChancellorServiceHistory("captivity_authority_assumed", day,
                        office.HeroName + " assumed authority while the ruler was captive.");
                }
                return;
            }

            if (office.IsVacant) return;
            if ((office.State == ReignChancellorOfficeState.EmergencyActive
                    || office.State == ReignChancellorOfficeState.CaptiveContinuity)
                && office.RulerReleasedDay < 0)
            {
                office.RulerReleasedDay = day;
                office.HandoffDueDay = day + 3;
                office.State = ReignChancellorOfficeState.Handoff;
                AddChancellorServiceHistory("handoff_started", day,
                    "The ruler was released; the unpaid Active three-day handoff began.");
            }
            if (office.State != ReignChancellorOfficeState.Handoff
                || office.HandoffDueDay < 0 || day < office.HandoffDueDay) return;

            if (office.Emergency)
            {
                FinishChancellorTerm("emergency_handoff_completed", day, true);
            }
            else
            {
                string outcome = "permanent_office_resumed";
                QueueEmergencyWorldHistory(office, "completed", day, outcome);
                QueueEmergencyChancellorMemory(office, day, outcome);
                office.State = office.PreCaptureState == ReignChancellorOfficeState.Active
                    ? ReignChancellorOfficeState.Active
                    : ReignChancellorOfficeState.Inactive;
                office.DesiredActive = office.PreCaptureDesiredActive;
                office.RulerReleasedDay = -1;
                office.HandoffDueDay = -1;
                AddChancellorServiceHistory(outcome, day,
                    "The temporary captivity handoff ended and the negotiated permanent office resumed its prior state.");
                StateChanged?.Invoke();
            }
        }

        internal Hero SelectEmergencyChancellorCandidate()
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null) return null;
            return Hero.AllAliveHeroes.Where(x => IsEmergencyChancellorCandidate(x, kingdom))
                .OrderByDescending(EmergencyChancellorScore)
                .ThenBy(x => x.StringId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static double EmergencyChancellorScore(Hero hero)
        {
            if (hero == null) return double.MinValue;
            return (hero.GetSkillValue(DefaultSkills.Steward)
                + hero.GetSkillValue(DefaultSkills.Charm)
                + hero.GetSkillValue(DefaultSkills.Leadership)) / 3d;
        }

        private static bool IsEmergencyChancellorCandidate(Hero hero, Kingdom kingdom)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsActive
                || hero.IsChild || hero.IsPrisoner || hero.Occupation != Occupation.Lord
                || hero.Clan?.Kingdom != kingdom) return false;
            MobileParty party = hero.PartyBelongedTo;
            return party == null || party.LeaderHero != hero
                || party.MapEvent == null && party.Army == null;
        }

        private void StartEmergencyChancellor(Hero hero, int day)
        {
            string duties = DescribeChancellorDuties(hero);
            ReignChancellorOffice office = new ReignChancellorOffice
            {
                TermId = "emergency_chancellor_" + Guid.NewGuid().ToString("N"),
                HeroId = hero.StringId,
                HeroName = hero.Name?.ToString() ?? hero.StringId,
                State = ReignChancellorOfficeState.EmergencyActive,
                Salary = 0,
                DesiredActive = true,
                AppointedDay = day,
                ServiceStartedDay = day,
                Emergency = true,
                EmergencyReason = "ruler_captivity",
                EmergencyStartedDay = day,
                EmergencyCorrelationId = "emergency_chancellor_" + Guid.NewGuid().ToString("N"),
                OriginalHomeSettlementId = hero.HomeSettlement?.StringId ?? string.Empty,
                RelinquishedDuties = duties
            };
            EnsureRulerDocketState().Chancellor = office;
            RelinquishChancellorDuties(hero);
            TeleportPartylessHero(hero, FindCurrentCapital());
            QueueEmergencyWorldHistory(office, "started", day, string.Empty);
            AddChancellorServiceHistory("emergency_appointed", day,
                office.HeroName + " automatically assumed the unpaid Active office because the ruler was captive."
                + " Relinquished duties: " + duties + ".");
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] "
                + office.HeroName + " has assumed authority as emergency Chancellor during your captivity."));
            StateChanged?.Invoke();
        }

        private bool IsNormalChancellorCandidate(Hero hero, out string error)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            bool sameKingdom = hero?.Clan?.Kingdom == kingdom
                || hero?.CurrentSettlement?.OwnerClan?.Kingdom == kingdom
                || hero?.HomeSettlement?.OwnerClan?.Kingdom == kingdom;
            bool supportedRole = hero?.Occupation == Occupation.Lord
                || hero?.Clan == Clan.PlayerClan || hero?.IsNotable == true;
            if (kingdom == null || hero == null || hero == Hero.MainHero || !hero.IsAlive
                || !hero.IsActive || hero.IsChild || hero.IsPrisoner
                || !sameKingdom || !supportedRole)
            {
                error = "The candidate must be an adult, free, active lord, clan hero, or lawful settlement notable of the ruler's kingdom.";
                return false;
            }
            error = string.Empty;
            return true;
        }

        private bool IsCurrentChancellorEligible(Hero hero)
        {
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            return hero != null && hero.IsAlive && hero.IsActive && !hero.IsChild
                && !hero.IsPrisoner && kingdom != null
                && (hero.Clan?.Kingdom == kingdom
                    || hero.HomeSettlement?.OwnerClan?.Kingdom == kingdom
                    || hero.CurrentSettlement?.OwnerClan?.Kingdom == kingdom);
        }

        private static string DescribeChancellorDuties(Hero hero)
        {
            List<string> duties = new List<string>();
            if (hero?.GovernorOf != null) duties.Add("governorship of " + hero.GovernorOf.Name);
            if (hero?.PartyBelongedTo?.LeaderHero == hero) duties.Add("party command");
            if (hero?.IsNotable == true) duties.Add("local notable residence");
            return duties.Count == 0 ? "none" : string.Join(", ", duties);
        }

        private static void RelinquishChancellorDuties(Hero hero)
        {
            if (hero == null) return;
            if (hero.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(hero);
            MobileParty party = hero.PartyBelongedTo;
            if (party != null && party.LeaderHero == hero && party.MapEvent == null
                && party.Army == null)
                DisbandPartyAction.StartDisband(party);
        }

        private void FinishChancellorTerm(string outcome, int day, bool emergencyService)
        {
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            if (office.IsVacant) return;
            bool rememberedEmergency = emergencyService || office.Emergency
                || office.EmergencyStartedDay >= 0;
            if (rememberedEmergency)
            {
                QueueEmergencyWorldHistory(office, "completed", day, outcome);
                QueueEmergencyChancellorMemory(office, day, outcome);
            }
            office.EndedDay = day;
            office.EndReason = outcome ?? string.Empty;
            AddChancellorServiceHistory(outcome, day,
                office.HeroName + " ended service after "
                + Math.Max(0, day - Math.Max(0, office.ServiceStartedDay))
                + " days; paid " + office.TotalPaidGold + " denars across "
                + office.PaidActiveDays + " Active days.");
            Hero hero = FindHero(office.HeroId);
            Settlement returnTo = Settlement.Find(office.OriginalHomeSettlementId)
                ?? hero?.HomeSettlement
                ?? ClosestFriendlyFortification(hero?.CurrentSettlement, Clan.PlayerClan?.Kingdom);
            TeleportPartylessHero(hero, returnTo);
            EnsureRulerDocketState().Chancellor = new ReignChancellorOffice();
            StateChanged?.Invoke();
        }

        private void AddChancellorServiceHistory(string outcome, int day, string detail)
        {
            ReignChancellorOffice office = EnsureRulerDocketState().Chancellor;
            EnsureRulerDocketState().History.Add(new ReignDocketHistoryRecord
            {
                RecordId = "docket_history_" + Guid.NewGuid().ToString("N"),
                ReignId = CurrentReignId(), Type = "chancellor_service",
                Outcome = outcome ?? string.Empty, Day = day,
                ChancellorHeroId = office.HeroId, ChancellorName = office.HeroName,
                Summary = "Chancellor service: " + (outcome ?? "updated"),
                Detail = detail ?? string.Empty
            });
        }

        private void QueueEmergencyWorldHistory(ReignChancellorOffice office,
            string phase, int day, string outcome)
        {
            if (office == null || string.IsNullOrWhiteSpace(office.HeroId)) return;
            string id = office.TermId + "|world_history|" + phase;
            if (EnsureRulerDocketState().PendingWorldHistoryJobs.Any(x => x.JobId == id)) return;
            EnsureRulerDocketState().PendingWorldHistoryJobs.Add(new ReignDocketWorldHistoryJob
            {
                JobId = id, CorrelationId = office.EmergencyCorrelationId,
                Phase = phase, ChancellorHeroId = office.HeroId,
                RulerHeroId = Hero.MainHero?.StringId ?? string.Empty,
                KingdomId = Clan.PlayerClan?.Kingdom?.StringId ?? string.Empty,
                StartDay = office.EmergencyStartedDay >= 0 ? office.EmergencyStartedDay : day,
                EndDay = phase == "completed" ? day : -1, Outcome = outcome ?? string.Empty,
                Summary = phase == "started"
                    ? office.HeroName + " assumed authority over "
                        + (Clan.PlayerClan?.Kingdom?.Name?.ToString() ?? "the realm")
                        + " while " + (Hero.MainHero?.Name?.ToString() ?? "the ruler") + " was captive."
                    : office.HeroName + " concluded emergency authority after the ruler's release and handoff; outcome: "
                        + (outcome ?? "completed") + "."
            });
        }

        private void QueueEmergencyChancellorMemory(ReignChancellorOffice office,
            int day, string outcome)
        {
            if (office == null || string.IsNullOrWhiteSpace(office.HeroId)) return;
            string jobId = office.TermId + "|full_emergency_memory";
            if (EnsureRulerDocketState().PendingMemoryJobs.Any(x => x.JobId == jobId)) return;
            int start = office.EmergencyStartedDay >= 0 ? office.EmergencyStartedDay : office.ServiceStartedDay;
            EnsureRulerDocketState().PendingMemoryJobs.Add(new ReignDocketMemoryJob
            {
                JobId = jobId, HeroId = office.HeroId,
                EventType = "emergency_chancellor_service",
                WorldDay = day,
                Summary = "I assumed authority as Chancellor of "
                    + (Clan.PlayerClan?.Kingdom?.Name?.ToString() ?? "the realm")
                    + " when " + (Hero.MainHero?.Name?.ToString() ?? "the ruler")
                    + " was taken captive. I served from day " + start + " through day " + day
                    + ", remained in authority through the ruler's release and three-day handoff where applicable, and my service ended as "
                    + (outcome ?? "completed") + ". No unrecorded petitions or deeds are implied."
            });
        }

        internal string CurrentReignId()
        {
            string kingdomId = TaleWorlds.CampaignSystem.Clan.PlayerClan?.Kingdom?.StringId ?? "no_kingdom";
            string rulerId = TaleWorlds.CampaignSystem.Hero.MainHero?.StringId ?? "no_ruler";
            return kingdomId + ":" + rulerId;
        }
    }
}
