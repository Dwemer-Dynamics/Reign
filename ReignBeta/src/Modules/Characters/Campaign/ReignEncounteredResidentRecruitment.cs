using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.World;
using ReignBeta.Integration;
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

        internal bool UsesCompanionMembership(Hero hero, Clan targetClan)
            => targetClan != null && targetClan == Clan.PlayerClan && Find(hero) != null && !hero.IsLord;

        internal bool ValidateCompanionMembership(Hero hero, out string reason)
        {
            reason = string.Empty;
            var resident = Find(hero);
            if (resident == null || !resident.Initialized || resident.Retired || hero == null
                || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.IsNotable || hero.IsLord)
                reason = "Companion registration requires a living adult encountered resident.";
            else if (Clan.PlayerClan == null || hero.CompanionOf != null && hero.CompanionOf != Clan.PlayerClan
                || hero.Clan != null && hero.Clan != Clan.PlayerClan)
                reason = "This resident belongs to another clan.";
            else if (resident.Military && !resident.ReleasedFromDuty)
                reason = "This person must be released from duty before becoming a companion.";
            else if (hero.CompanionOf != Clan.PlayerClan && Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit)
                reason = "Your clan has reached its native companion limit.";
            return reason.Length == 0;
        }

        internal bool RegisterResidentClanMember(Hero hero, out string reason)
        {
            if (!ValidateCompanionMembership(hero, out reason)) return false;
            if (hero.Clan != Clan.PlayerClan)
            { reason = "This resident has not joined your clan."; return false; }
            var resident = Find(hero);
            var party = hero.PartyBelongedTo;
            try
            {
                ReignResidentCompanionRegistration.Ensure(Clan.PlayerClan, () => hero.Clan,
                    clan => hero.Clan = clan, () => hero.CompanionOf,
                    () => AddCompanionAction.Apply(Clan.PlayerClan, hero),
                    () => Clan.PlayerClan.Companions.Contains(hero),
                    () => Clan.PlayerClan.Heroes.Contains(hero),
                    () => { hero.CompanionOf = null; hero.CompanionOf = Clan.PlayerClan; },
                    () => hero.IsWanderer,
                    () => hero.SetNewOccupation(Occupation.Wanderer));
                if (hero.PartyBelongedTo != party)
                    throw new InvalidOperationException("Native companion registration changed the resident's party.");
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompleteResidentRecruitment(hero);
                DetachDepartingRole(hero);
                resident.Recruited = true;
                return true;
            }
            catch (Exception ex)
            {
                reason = "Resident companion registration is incomplete: " + ex.Message;
                ReignLog.Exception("Resident clan membership repair hero=" + hero.StringId, ex);
                return false;
            }
        }

        private void RepairResidentClanMembers()
        {
            if (Clan.PlayerClan == null) return;
            foreach (var resident in _residents.Where(r => r.Initialized && !r.Retired).ToList())
            {
                Hero hero = Resolve(resident);
                // Native clan membership is the authority. A saved Recruited flag,
                // caravan ownership or a temporary party invitation cannot recruit anyone.
                if (hero == null || hero.Clan != Clan.PlayerClan || hero.IsLord || hero.IsNotable
                    || !hero.IsAlive || hero.IsChild || hero.CompanionOf == Clan.PlayerClan && hero.IsWanderer
                        && resident.Recruited && Clan.PlayerClan.Companions.Contains(hero)
                        && Clan.PlayerClan.Heroes.Contains(hero)) continue;
                if (RegisterResidentClanMember(hero, out string reason))
                    ReignLog.Info("Repaired resident companion registration hero=" + hero.StringId
                        + " party=" + (hero.PartyBelongedTo?.StringId ?? ""));
                else ReignLog.Warn("Resident companion registration pending hero=" + hero.StringId + ": " + reason);
            }
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
            if (resident == null || hero?.IsNotable == true)
            {
                Settlement here = Settlement.CurrentSettlement ?? Hero.MainHero?.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
                bool present = hero != null && (CharacterObject.OneToOneConversationCharacter?.HeroObject == hero
                    || MobileParty.MainParty != null && hero.PartyBelongedTo == MobileParty.MainParty
                    || here != null && hero.CurrentSettlement == here);
                var house = ReignTavernHouseCampaignBehavior.Instance;
                if (hero == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.IsPrisoner
                    || hero.IsLord || hero.Clan != null || hero.CompanionOf != null || !present
                    || !hero.HasMet && CharacterObject.OneToOneConversationCharacter?.HeroObject != hero)
                    reason = "Only a known, living, adult, free civilian here can join you.";
                else if (CivilianHome(hero) == null)
                    reason = "This person's home is unavailable for a safe departure and return.";
                else if (house?.IsActiveStaff(hero) == true && !house.IsAvailable(hero, here))
                    reason = "This tavern worker is no longer available here.";
                return reason.Length == 0;
            }
            if (!resident.Initialized || hero == null || !hero.IsAlive || hero.IsChild || hero.IsPrisoner)
                reason = "Only a known, living, adult, free resident can join you.";
            else if (resident.Military && !resident.ReleasedFromDuty)
                reason = "This person must be released from duty by their commander or home-owning clan leader first.";
            else if (!ResidentsAt(Settlement.CurrentSettlement).Contains(hero)
                && hero.PartyBelongedTo != MobileParty.MainParty)
                reason = "This resident is no longer here.";
            return reason.Length == 0;
        }

        private static Settlement CivilianHome(Hero hero)
        {
            var person = ReignTavernHouseCampaignBehavior.Instance?.GetPerson(hero);
            return (person == null ? null : Settlement.Find(person.TownId)) ?? hero?.HomeSettlement ?? hero?.CurrentSettlement;
        }

        internal EncounteredResident RegisterCivilianDeparture(Hero hero)
        {
            var existing = Find(hero);
            if (existing != null) return existing;
            Settlement home = CivilianHome(hero) ?? throw new InvalidOperationException("The departing civilian's home is unavailable.");
            CharacterObject source = (hero.IsFemale ? hero.Culture?.Townswoman : hero.Culture?.Townsman) ?? hero.CharacterObject;
            var colors = PortraitColors(hero);
            var record = new EncounteredResident {
                Id = Guid.NewGuid().ToString("N"), HeroId = hero.StringId, HomeId = home.StringId,
                LocationId = home.IsVillage && home.LocationComplex?.GetLocationWithId("village_center") != null
                    ? "village_center" : "center", SlotKey = "departed_civilian:" + hero.StringId,
                SourceTemplateId = source.StringId, SkillsTemplateId = hero.CharacterObject.StringId,
                Name = hero.Name.ToString(), Role = "resident", BodyProperties = hero.BodyProperties.ToString(),
                NativeSpawnTag = "npc_common", NativeActionSet = hero.IsFemale ? "as_human_female_villager" : "as_human_villager",
                ClothingColor1 = colors.Item1, ClothingColor2 = colors.Item2,
                Initialized = true, InitialPortraitCompleted = true, MetDay = CampaignTime.Now.ToDays,
                Equipment = Enumerable.Range(0, 12).Where(i => hero.CivilianEquipment[(EquipmentIndex)i].Item != null)
                    .Select(i => new ResidentEquipmentPart { Slot = i,
                        ItemId = hero.CivilianEquipment[(EquipmentIndex)i].Item.StringId,
                        ModifierId = hero.CivilianEquipment[(EquipmentIndex)i].ItemModifier?.StringId }).ToList()
            };
            // Bind the existing Hero; never create, rename or reroll this person's identity.
            _residents.Add(record);
            return record;
        }

        internal bool ValidateResidentAction(ReignWorldActionRecord action, out string reason, bool proposal = false)
        {
            reason = string.Empty;
            JObject terms = ResidentTerms(action);
            Hero actor = Hero.Find(action.ActorHeroStringId);
            var house = ReignTavernHouseCampaignBehavior.Instance;
            if (action.Type == ReignWorldActionType.RegularRecruitEncounteredResident && house?.GetPerson(actor) != null && Find(actor) == null)
            {
                if (terms.Value<string>("agreementKind") != "permanent" || terms["agreedGold"]?.Type != JTokenType.Integer
                    || terms.Value<long>("agreedGold") < 0 || terms.Value<long>("agreedGold") > int.MaxValue)
                { reason = "Permanent recruitment requires an exact nonnegative whole-denar payment."; return false; }
                return house.ValidateRecruitment(actor, terms.Value<int>("agreedGold"), action.ActionId,
                    proposal || ReignActionValidator.HasOperatorAuthorization(action) || terms.Value<bool?>("consentConfirmed") == true, out reason);
            }
            if (action.Type == ReignWorldActionType.RegularReleaseResidentFromDuty)
            {
                Hero target = Hero.Find(action.TargetHeroStringId);
                EncounteredResident resident = Find(target);
                if (resident == null || !resident.Military || !target.IsAlive || target.IsPrisoner)
                    reason = "Release requires a known, living, free resident with military duties.";
                else if (resident.ReleasedFromDuty) return true;
                else if (!proposal && !ReignActionValidator.HasOperatorAuthorization(action) && terms.Value<bool?>("releaseConfirmed") != true)
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
            bool resuming = recruit != null && recruit.RecruitmentReceipt == action.ActionId && actor?.CompanionOf == Clan.PlayerClan;
            if (!resuming && !ValidateDeparture(actor, out reason)) return false;
            if (actor == null || !actor.IsAlive || actor.IsPrisoner || actor.IsChild)
            { reason = "This resident is unavailable to complete recruitment."; return false; }
            if ((!proposal && !ReignActionValidator.HasOperatorAuthorization(action) && terms.Value<bool?>("consentConfirmed") != true) || terms.Value<string>("agreementKind") != "permanent")
                reason = "Permanent recruitment requires this resident's explicit agreement to become your companion.";
            else if (terms["agreedGold"]?.Type != JTokenType.Integer || terms.Value<long>("agreedGold") < 0
                || terms.Value<long>("agreedGold") > int.MaxValue)
                reason = "Recruitment requires the exact agreed gold amount, including zero for a free agreement.";
            else if (actor.CompanionOf != null && actor.CompanionOf != Clan.PlayerClan || actor.Clan != null && actor.Clan != Clan.PlayerClan)
                reason = "A resident already committed to another clan cannot be recruited through this agreement.";
            else if (actor.CompanionOf != Clan.PlayerClan && Clan.PlayerClan.Companions.Count >= Clan.PlayerClan.CompanionLimit)
                reason = "Your clan has reached its native companion limit.";
            else if (!string.IsNullOrEmpty(recruit?.RecruitmentReceipt) && recruit.RecruitmentReceipt != action.ActionId)
                reason = "This resident already has a recruitment transaction in progress.";
            else if (ReignGoldTransferService.Instance == null)
                reason = "The payment receipt service is unavailable.";
            else if (recruit?.RecruitmentReceipt == action.ActionId && recruit.RecruitmentGold != terms.Value<int>("agreedGold"))
                reason = "The recruitment price changed during execution.";
            else if (recruit?.RecruitmentGoldPaid != true && !ReignGoldTransferService.Instance.HasReceipt(action.ActionId + ":payment")
                && Hero.MainHero.Gold < Math.Max(0, terms.Value<int>("agreedGold") - Math.Max(recruit?.RecruitmentCredit ?? 0,
                    ReignGoldTransferService.Instance.Credit(actor, AgreementId(actor)))))
                reason = "You do not have the agreed recruitment payment.";
            return reason.Length == 0;
        }

        internal ReignActionResult ExecuteResidentAction(ReignWorldActionRecord action)
        {
            var payment = ReignGoldTransferService.Instance;
            var dependencies = ResidentTerms(action)["paymentDependencies"] as JArray;
            if (dependencies != null && dependencies.Count > 0)
            {
                if (payment == null) return ReignActionResult.ValidationFailed("The payment service is unavailable.");
                foreach (string id in dependencies.Values<string>())
                {
                    string status = payment.ReceiptStatus(id);
                    if (status == "pending")
                    {
                        var prior = ReignAICampaignBehavior.Instance?.GetLastActionResult(id);
                        if (prior != null && !prior.Success) return ReignActionResult.ValidationFailed("The required advance failed: " + prior.Message);
                        return ReignActionResult.Progress("Recruitment is waiting for the advance's native result.");
                    }
                    if (status != "applied") return ReignActionResult.ValidationFailed("The recruitment advance did not complete; confirm the payment before recruiting.");
                }
            }
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
            var house = ReignTavernHouseCampaignBehavior.Instance;
            if (house?.GetPerson(hero) != null && Find(hero) == null)
            {
                if (house.TryRecruit(hero, terms.Value<int>("agreedGold"), action.ActionId,
                    ReignActionValidator.HasOperatorAuthorization(action) || terms.Value<bool?>("consentConfirmed") == true, out string houseReason))
                    return ReignActionResult.Done(hero.Name + " has joined your clan and party.")
                        .WithEffect("tavern_house_recruited", "hero", hero.StringId, hero.Name.ToString(), "agreement=" + action.ActionId);
                return ReignActionResult.Progress(houseReason);
            }
            var record = Find(hero) ?? RegisterCivilianDeparture(hero);
            if (record.Recruited && hero.CompanionOf == Clan.PlayerClan)
                return ReignActionResult.Done(hero.Name + " is already your companion.");
            // This saved receipt resumes a partially completed native transaction without paying twice.
            record.RecruitmentReceipt = action.ActionId;
            record.RecruitmentGold = terms.Value<int>("agreedGold");
            if (!record.RecruitmentGoldPaid)
            {
                record.RecruitmentCredit = payment.ConsumeCredit(hero, AgreementId(hero), action.ActionId, record.RecruitmentGold);
                var paid = payment.Pay(action.ActionId + ":payment", Hero.MainHero, hero,
                    Math.Max(0, record.RecruitmentGold - record.RecruitmentCredit), "recruitment_balance", AgreementId(hero));
                if (!paid.Success || !paid.Completed)
                {
                    if (payment.ReceiptStatus(action.ActionId + ":payment") == "failed")
                    { payment.ReleaseCredit(action.ActionId); record.RecruitmentReceipt = null; record.RecruitmentCredit = 0; }
                    return paid;
                }
                record.RecruitmentGoldPaid = true;
            }
            try
            {
                if (hero.CompanionOf != Clan.PlayerClan) AddCompanionAction.Apply(Clan.PlayerClan, hero);
                if (hero.IsNotable)
                {
                    ReignRecruitedNotableCampaignBehavior.ClearRecruitedRole(hero);
                    if (hero.IsNotable) return ReignActionResult.Progress("Payment is recorded; notable role cleanup must finish before joining your party.");
                }
                if (hero.PartyBelongedTo != MobileParty.MainParty) AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                if (hero.CompanionOf != Clan.PlayerClan || hero.PartyBelongedTo != MobileParty.MainParty)
                    return ReignActionResult.Progress("Payment is recorded; native companion/party transfer is incomplete and will resume without another charge.");
                hero.SetNewOccupation(Occupation.Wanderer);
                ReignTemporaryPartyGuestCampaignBehavior.Instance?.CompleteResidentRecruitment(hero);
                DetachDepartingRole(hero);
                record.Recruited = true;
            }
            catch (Exception ex)
            {
                ReignLog.Exception("Resident recruitment will resume from its saved payment receipt.", ex);
                return ReignActionResult.Progress("Payment is recorded; recruitment cleanup is pending and will resume without another charge.");
            }
            return ReignActionResult.Done(hero.Name + " joined your clan as a companion.")
                    .WithEffect("encountered_resident_recruited", "hero", hero.StringId, hero.Name.ToString(), "gold=" + record.RecruitmentGold + ";advanceCredit=" + record.RecruitmentCredit)
                .WithChangedEntity("hero", hero.StringId, hero.Name.ToString(), "companion_recruited");
        }

        internal void DetachDepartingRole(Hero hero)
        {
            // The next native scene supplies its ordinary service replacement. Until it closes,
            // remove the old role body so a recruited resident cannot remain behind as a duplicate.
            var agents = _agents.Where(p => p.Value.HeroId == hero.StringId).Select(p => p.Key)
                .Concat(Mission.Current?.Agents.Where(a => a.Origin != null && ResolveAgent(a) == hero)
                    ?? Enumerable.Empty<Agent>()).Distinct().ToList();
            foreach (var agent in agents)
            {
                _agents.Remove(agent);
                var location = CampaignMission.Current?.Location;
                var role = location?.GetLocationCharacter(agent.Origin);
                if (role != null)
                {
                    if (_personalRoles.ContainsKey(role)) RestoreRolePrototype(role);
                    else location.RemoveLocationCharacter(role);
                }
                _originSlots.Remove(agent.Origin);
                _colors.Remove(agent.Origin);
                if (agent.IsActive()) agent.FadeOut(true, false);
            }
        }
    }
}
