using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ReignBeta.Court;
using ReignBeta.Court.WarCouncil;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    internal static class ReignResidentAdvisorAppointment
    {
        internal static void Begin(ReignCourtCampaignBehavior court, ReignCourtOffice office, Action completed)
        {
            List<Hero> candidates = Candidates(court).ToList();
            if (candidates.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] No eligible free adult lord is present in the capital."));
                return;
            }
            List<InquiryElement> choices = candidates.Select(hero => new InquiryElement(hero,
                (hero.Name?.ToString() ?? hero.StringId) + DutyWarning(hero), null)).ToList();
            string title = office == ReignCourtOffice.EconomicAdvisor ? "Economic Advisor" : "Foreign Advisor";
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Appoint " + title,
                "Choose a distinct adult, active, free lord physically present in the capital. Listed governorship or safely retireable party command duties will be removed.",
                choices, true, 1, 1, "Appoint", "Cancel", selected =>
                {
                    Hero hero = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (hero != null) _ = AppointAsync(court, office, hero, completed);
                }, null, string.Empty, false), true, false);
        }

        internal static IEnumerable<Hero> Candidates(ReignCourtCampaignBehavior court)
        {
            if (court?.CurrentCapital == null) return Enumerable.Empty<Hero>();
            var used = new HashSet<string>(court.Offices.Where(a => a.IsActive).Select(a => a.HeroStringId), StringComparer.OrdinalIgnoreCase);
            string war = ReignWarCouncilCampaignBehavior.Instance?.SelectedCouncilorHeroId;
            if (!string.IsNullOrWhiteSpace(war)) used.Add(war);
            return Hero.AllAliveHeroes.Where(hero => hero != null && hero.Occupation == Occupation.Lord && !hero.IsChild && !hero.IsPrisoner
                && hero.CurrentSettlement == court.CurrentCapital && !used.Contains(hero.StringId)).ToList();
        }

        private static async Task AppointAsync(ReignCourtCampaignBehavior court, ReignCourtOffice office, Hero hero, Action completed)
        {
            if (!Candidates(court).Contains(hero)) { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Native conditions changed; no appointment was made.")); return; }
            MobileParty party = hero.PartyBelongedTo;
            if (party != null && party.LeaderHero == hero && (party.CurrentSettlement != court.CurrentCapital || party.MapEvent != null || party.Army != null))
            { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] That party cannot be safely retired at the capital.")); return; }
            CourtOfficeAssignment previous = court.Offices.FirstOrDefault(a => a.IsActive && a.Office == office);
            Hero previousHero = string.IsNullOrWhiteSpace(previous?.HeroStringId) ? null : Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == previous.HeroStringId);
            string error = await court.AssignOfficeAsync(office, hero).ConfigureAwait(false);
            Exception transitionFailure = null;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(error)) { InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + error)); return; }
                try
                {
                    if (hero.GovernorOf != null) ChangeGovernorAction.RemoveGovernorOf(hero);
                    if (party != null && party.LeaderHero == hero) DisbandPartyAction.StartDisband(party);
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + hero.Name + " appointed " + (office == ReignCourtOffice.EconomicAdvisor ? "Economic Advisor" : "Foreign Advisor") + "."));
                    completed?.Invoke();
                }
                catch (Exception ex) { transitionFailure = ex; }
            });
            if (transitionFailure == null || !string.IsNullOrWhiteSpace(error)) return;
            CourtOfficeAssignment active = court.Offices.FirstOrDefault(a => a.IsActive && a.Office == office && a.HeroStringId == hero.StringId);
            if (active != null) await court.DismissOfficeAsync(active).ConfigureAwait(false);
            if (previousHero != null) await court.AssignOfficeAsync(office, previousHero).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Appointment rolled back: " + transitionFailure.Message)));
        }

        private static string DutyWarning(Hero hero)
        {
            List<string> duties = new List<string>();
            if (hero?.GovernorOf != null) duties.Add("governorship of " + hero.GovernorOf.Name);
            if (hero?.PartyBelongedTo?.LeaderHero == hero) duties.Add("party command");
            return duties.Count == 0 ? string.Empty : " — removes " + string.Join(" and ", duties);
        }
    }
}
