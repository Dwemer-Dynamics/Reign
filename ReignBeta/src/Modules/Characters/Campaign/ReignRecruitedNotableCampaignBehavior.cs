using System;
using System.Collections.Generic;
using System.Linq;
using ReignBeta.Integration;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignRecruitedNotableCampaignBehavior : CampaignBehaviorBase
    {
        private static readonly HashSet<Hero> ClearingRoles = new HashSet<Hero>();

        public override void RegisterEvents()
        {
            CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnClanChanged);
            CampaignEvents.NewCompanionAdded.AddNonSerializedListener(this, OnCompanionAdded);
            CampaignEvents.OnGameLoadFinishedEvent.AddNonSerializedListener(this, RepairRecruitedNotables);
            CampaignEvents.DailyTickHeroEvent.AddNonSerializedListener(this, RetryIncompleteRecruitment);
        }

        public override void SyncData(IDataStore dataStore) { }

        private static void OnClanChanged(Hero hero, Clan oldClan)
        {
            if (hero?.Clan == Clan.PlayerClan && oldClan != hero?.Clan)
            {
                ClearRecruitedRole(hero);
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompletePermanentRecruitment(hero);
            }
        }

        private static void OnCompanionAdded(Hero hero)
        {
            if (hero?.CompanionOf == Clan.PlayerClan)
            {
                ClearRecruitedRole(hero);
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompletePermanentRecruitment(hero);
            }
        }

        internal static bool ClearRecruitedRole(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsNotable || hero.Clan == null)
                return false;
            return ClearNotableRole(hero, hero.CompanionOf != null ? Occupation.Wanderer : Occupation.Lord);
        }

        internal static bool ClearDepartingNotableRole(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsNotable) return false;
            return ClearNotableRole(hero, Occupation.Special);
        }

        private static bool ClearNotableRole(Hero hero, Occupation occupation)
        {
            if (!ClearingRoles.Add(hero)) return false;
            try
            {
                // Finish disposal before clearing the occupation. If a native
                // transfer fails, IsNotable remains the save-backed retry marker;
                // completed transfers are absent from the next ownership snapshot.
                ReleaseBusinessOwnership(hero);
                foreach (var alley in hero.OwnedAlleys.ToList())
                    if (alley.Owner == hero) alley.SetOwner(null);
                if (hero.OwnedAlleys.Any(alley => alley.Owner == hero))
                    throw new InvalidOperationException("The departing notable retained an alley.");
                if (hero.Issue != null)
                {
                    hero.Issue.CompleteIssueWithCancel();
                    if (hero.Issue != null)
                        throw new InvalidOperationException("The departing notable retained a native issue.");
                }
                hero.SupporterOf = null;
                if (hero.VolunteerTypes != null) Array.Clear(hero.VolunteerTypes, 0, hero.VolunteerTypes.Length);
                hero.SetNewOccupation(occupation);
                if (hero.IsNotable) throw new InvalidOperationException("Recruited hero retained the native notable occupation.");
                ReignLog.Info("Cleared recruited notable role and businesses hero=" + hero.StringId + " clan=" + (hero.Clan?.StringId ?? "") + " occupation=" + occupation);
                return true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Recruited notable cleanup incomplete hero=" + hero.StringId + ": " + ex.Message);
                return false;
            }
            finally { ClearingRoles.Remove(hero); }
        }

        private static void RetryIncompleteRecruitment(Hero hero)
        {
            if (Clan.PlayerClan != null && hero?.Clan == Clan.PlayerClan && hero.IsNotable)
                ClearRecruitedRole(hero);
        }

        private static void RepairRecruitedNotables()
        {
            // Native campaign saves retain Occupation. Repair the already-recruited
            // player-clan members from older Reign versions exactly once by state.
            if (Clan.PlayerClan == null) return;
            foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x.Clan == Clan.PlayerClan).ToList())
            {
                ClearRecruitedRole(hero);
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompletePermanentRecruitment(hero);
            }
        }
    }
}
