using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.PartyAgency;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    internal sealed partial class ReignEncounteredResidentsCampaignBehavior
    {
        private static JObject ResidentTerms(ReignWorldActionRecord action)
        {
            try { return JObject.Parse(action?.TermsJson ?? "{}"); }
            catch { return new JObject(); }
        }

        private static void CaptureDutySource(EncounteredResident record, Agent agent, Settlement home)
        {
            if (!record.Military) return;
            var origin = agent.Origin.BattleCombatant as PartyBase;
            var parties = new[] { origin?.MobileParty, home.Town?.GarrisonParty,
                home.MilitiaPartyComponent?.MobileParty }.Where(p => p != null && p.IsActive).Distinct().ToList();
            CharacterObject source = (CharacterObject)agent.Character;
            foreach (var party in parties)
            {
                var troops = party.MemberRoster.GetTroopRoster().Where(t => !t.Character.IsHero && t.Number > t.WoundedNumber)
                    .OrderByDescending(t => t.Character == source)
                    .ThenBy(t => Math.Abs(t.Character.Level - source.Level)).ToList();
                if (troops.Count == 0) continue;
                // A native scenery guard may use a role template rather than a roster troop.
                // Bind one real garrison/militia member now; departure must consume that member.
                record.SourcePartyId = party.StringId;
                record.ManpowerTemplateId = troops[0].Character.StringId;
                return;
            }
        }

        private static MobileParty DutyParty(EncounteredResident record) => MobileParty.All
            .FirstOrDefault(p => p.StringId == record.SourcePartyId && p.IsActive);

        internal bool ValidateDeparture(Hero hero, out string reason)
        {
            reason = string.Empty;
            var resident = Find(hero);
            if (resident == null || !resident.Initialized || hero == null || !hero.IsAlive || hero.IsChild || hero.IsPrisoner)
                reason = "Only a known, living, adult, free resident can join you.";
            else if (resident.Military && !resident.ReleasedFromDuty)
                reason = "This person must be released from duty by their commander or home-owning clan leader first.";
            else if (!ResidentsAt(Settlement.CurrentSettlement).Contains(hero)
                && hero.PartyBelongedTo != MobileParty.MainParty)
                reason = "This resident is no longer here.";
            return reason.Length == 0;
        }

        internal bool ValidateResidentAction(ReignWorldActionRecord action, out string reason)
        {
            reason = string.Empty;
            JObject terms = ResidentTerms(action);
            Hero actor = Hero.Find(action.ActorHeroStringId);
            if (action.Type == ReignWorldActionType.RegularReleaseResidentFromDuty)
            {
                Hero target = Hero.Find(action.TargetHeroStringId);
                EncounteredResident resident = Find(target);
                if (resident == null || !resident.Military || !target.IsAlive || target.IsPrisoner)
                    reason = "Release requires a known, living, free resident with military duties.";
                else if (resident.ReleasedFromDuty) return true;
                else if (terms.Value<bool?>("releaseConfirmed") != true)
                    reason = "The authorized commander must explicitly release this person from duty.";
                else
                {
                    Settlement home = Settlement.Find(resident.HomeId);
                    MobileParty source = DutyParty(resident);
                    Hero authority = actor == target ? Hero.MainHero : actor;
                    bool authorized = authority != null && (home?.OwnerClan?.Leader == authority
                        || source?.LeaderHero == authority);
                    if (!authorized) reason = "Only the source commander or home-owning clan leader can grant this release.";
                    else if (actor?.CurrentSettlement != Hero.MainHero.CurrentSettlement
                        && actor?.PartyBelongedTo != MobileParty.MainParty)
                        reason = "Duty release must be agreed in person.";
                    else if (source == null || source.MapEvent != null || source.SiegeEvent != null || home?.IsUnderSiege == true)
                        reason = "This person's source force is unavailable or engaged in battle.";
                    else
                    {
                        CharacterObject troop = CharacterObject.Find(resident.ManpowerTemplateId);
                        var entry = source.MemberRoster.GetTroopRoster().FirstOrDefault(t => t.Character == troop);
                        if (troop == null || entry.Number <= entry.WoundedNumber)
                            reason = "The saved source troop is no longer available for release.";
                    }
                }
                return reason.Length == 0;
            }
            EncounteredResident recruit = Find(actor);
            if (recruit != null && recruit.Recruited && actor.CompanionOf == Clan.PlayerClan) return true;
            if (!ValidateDeparture(actor, out reason)) return false;
            if (terms.Value<bool?>("consentConfirmed") != true || terms.Value<string>("agreementKind") != "permanent")
                reason = "Permanent recruitment requires this resident's explicit agreement to become your companion.";
            else if (terms["agreedGold"]?.Type != JTokenType.Integer || terms.Value<long>("agreedGold") < 0
                || terms.Value<long>("agreedGold") > int.MaxValue)
                reason = "Recruitment requires the exact agreed gold amount, including zero for a free agreement.";
            else if (actor.CompanionOf != null && actor.CompanionOf != Clan.PlayerClan || actor.Clan != null && actor.Clan != Clan.PlayerClan)
                reason = "A resident already committed to another clan cannot be recruited through this agreement.";
            else if (actor.CompanionOf != Clan.PlayerClan && Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit)
                reason = "Your clan has reached its native companion limit.";
            else if (!string.IsNullOrEmpty(recruit.RecruitmentReceipt) && recruit.RecruitmentReceipt != action.ActionId)
                reason = "This resident already has a recruitment transaction in progress.";
            else if (!recruit.RecruitmentGoldPaid && Hero.MainHero.Gold < terms.Value<int>("agreedGold"))
                reason = "You do not have the agreed recruitment payment.";
            return reason.Length == 0;
        }

        internal ReignActionResult ExecuteResidentAction(ReignWorldActionRecord action)
        {
            if (!ValidateResidentAction(action, out string reason)) return ReignActionResult.ValidationFailed(reason);
            JObject terms = ResidentTerms(action);
            if (action.Type == ReignWorldActionType.RegularReleaseResidentFromDuty)
            {
                Hero target = Hero.Find(action.TargetHeroStringId);
                var resident = Find(target);
                if (!resident.ReleasedFromDuty)
                {
                    DutyParty(resident).MemberRoster.AddToCounts(CharacterObject.Find(resident.ManpowerTemplateId), -1);
                    resident.ReleasedFromDuty = true;
                    resident.DutyReleaseReceipt = action.ActionId;
                }
                return ReignActionResult.Done(target.Name + " is released from duty.")
                    .WithEffect("resident_released_from_duty", "hero", target.StringId, target.Name.ToString(),
                        "sourceParty=" + resident.SourcePartyId + ";troop=" + resident.ManpowerTemplateId + ";removed=1");
            }
            Hero hero = Hero.Find(action.ActorHeroStringId);
            var record = Find(hero);
            if (record.Recruited && hero.CompanionOf == Clan.PlayerClan)
                return ReignActionResult.Done(hero.Name + " is already your companion.");
            // This saved receipt resumes a partially completed native transaction without paying twice.
            record.RecruitmentReceipt = action.ActionId;
            record.RecruitmentGold = terms.Value<int>("agreedGold");
            if (hero.CompanionOf != Clan.PlayerClan) AddCompanionAction.Apply(Clan.PlayerClan, hero);
            if (hero.PartyBelongedTo != MobileParty.MainParty) AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
            if (hero.CompanionOf != Clan.PlayerClan || hero.PartyBelongedTo != MobileParty.MainParty)
                return ReignActionResult.FailTerminal("The native companion transfer did not complete.", "resident_recruitment_transfer", "native_party_transfer");
            if (!record.RecruitmentGoldPaid)
            {
                if (record.RecruitmentGold > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, hero, record.RecruitmentGold, true);
                record.RecruitmentGoldPaid = true;
            }
            record.Recruited = true;
            hero.SetNewOccupation(Occupation.Wanderer);
            ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompleteResidentRecruitment(hero);
            DetachDepartingRole(hero);
            return ReignActionResult.Done(hero.Name + " joined your clan as a companion.")
                .WithEffect("encountered_resident_recruited", "hero", hero.StringId, hero.Name.ToString(), "gold=" + record.RecruitmentGold)
                .WithChangedEntity("hero", hero.StringId, hero.Name.ToString(), "companion_recruited");
        }

        internal void DetachDepartingRole(Hero hero)
        {
            // The next native scene supplies its ordinary service replacement. Until it closes,
            // remove the old role body so a recruited resident cannot remain behind as a duplicate.
            foreach (var pair in _agents.Where(p => p.Value.HeroId == hero.StringId).ToList())
            {
                _agents.Remove(pair.Key);
                var location = CampaignMission.Current?.Location;
                var role = location?.GetLocationCharacter(pair.Key.Origin);
                if (role != null)
                {
                    if (_personalRoles.ContainsKey(role)) RestoreRolePrototype(role);
                    else location.RemoveLocationCharacter(role);
                }
                _originSlots.Remove(pair.Key.Origin);
                _colors.Remove(pair.Key.Origin);
                if (pair.Key.IsActive()) pair.Key.FadeOut(true, false);
            }
        }
    }
}
