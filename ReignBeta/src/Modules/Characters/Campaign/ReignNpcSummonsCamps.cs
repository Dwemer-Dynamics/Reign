using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.PartyAgency;
using ReignBeta.Shared.Characters;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace ReignBeta.Campaign
{
    // Owned by the saved life duty, independently of temporary guest agreements.
    public sealed class NpcSummonsCamp
    {
        public string PartyId { get; set; } = "";
        public string ClanId { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public bool OnLand { get; set; }
        public float Morale { get; set; }
        public bool Visible { get; set; }
        public bool UsedByQuest { get; set; }
        public bool HadCustomName { get; set; }
        public string OriginalName { get; set; } = "";
        public bool Protected { get; set; }
    }

    public sealed partial class ReignNpcLivesBehavior
    {
        internal bool IsSummonsCamp(MobileParty party) => party != null && party != MobileParty.MainParty
            && _lifeDuties.Values.Any(d => d.Camp?.Protected == true && d.Camp.PartyId == party.StringId);
        internal bool IsSummonedClanLeader(Clan clan) => clan?.Leader != null
            && _lifeDuties.TryGetValue(clan.Leader.StringId, out var duty) && duty.Camp?.Protected == true;
        internal bool SuppressSummonedPartySpawn(Hero hero) => hero != null && hero != Hero.MainHero
            && _lifeDuties.TryGetValue(hero.StringId, out var duty) && duty.SoloAttendance && duty.SummonsDeparted;
        private static MobileParty LifeParty(string id) => string.IsNullOrEmpty(id) ? null : MobileParty.All.FirstOrDefault(p => p.StringId == id && p.IsActive);
        private static void ReleaseSummonedArmy(NpcMeetingJourney journey, Hero issuer, Hero target)
        {
            var party = target.PartyBelongedTo;
            if (party?.Army == null) return;
            if (!journey.ReleaseArmy || !CanReleaseMeetingArmy(issuer, target))
                throw new InvalidOperationException("The summoned person's army duties have not been released within the issuer's authority.");
            if (party.Army.LeaderParty == party) DisbandArmyAction.ApplyByUnknownReason(party.Army);
            else party.Army = null;
        }
        private string SoloDepartureProblem(Hero hero)
        {
            if (!ReignTemporaryPartyGuestPatches.VerifyReady()) return "The waiting-camp safety hooks are unavailable.";
            if (!Free(hero)) return "The summoned person cannot safely set out now.";
            if (_lifeDuties.TryGetValue(hero.StringId, out var duty) && !duty.SoloAttendance
                && _meetingTravel.Values.Any(j => !j.Terminal && (j.Meeting.TargetId == hero.StringId || j.Meeting.IssuerId == hero.StringId)))
                return "Finish or cancel this person's existing party-attendance meeting before arranging a solo summons.";
            var party = hero.PartyBelongedTo;
            if (party == null || party == MobileParty.MainParty) return "";
            if (party.MapEvent != null || party.IsCurrentlyAtSea || party.IsTransitionInProgress || party.SiegeEvent != null || party.BesiegerCamp != null)
                return "The summoned person's battle, siege or travel transition must finish before departure.";
            if (party.LeaderHero != hero) return ""; // A passenger never reserves somebody else's party.
            if (!IsSummonsCamp(party) && (party.IsCurrentlyUsedByAQuest || party.ShouldBeIgnored
                || ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(party) == true))
                return "The person's party is already reserved by another protected duty.";
            if (ReignCampaignCommandBehavior.Instance?.Orders.Any(o => !o.IsTerminal && o.CommanderHeroStringId == hero.StringId) == true)
                return "The person must finish or cancel their current campaign order before camping their party.";
            return "";
        }
        private void BeginSoloAttendance(NpcMeetingJourney journey, Hero hero)
        {
            string problem = SoloDepartureProblem(hero);
            if (problem.Length > 0) throw new InvalidOperationException(problem);
            CaptureMeetingDuty(hero);
            var duty = _lifeDuties[hero.StringId];
            duty.SoloAttendance = true;
            if (duty.SummonsDeparted && AtMeetingPlace(hero, journey.Meeting.DestinationId)
                && journey.Meeting.ArrivalDelayDays == 0) return;
            var party = hero.PartyBelongedTo;
            if (duty.Camp == null && party != null && party != MobileParty.MainParty && party.LeaderHero == hero)
            {
                duty.Camp = new NpcSummonsCamp { PartyId = party.StringId, ClanId = party.ActualClan?.StringId ?? "",
                    X = party.Position.X, Y = party.Position.Y, OnLand = party.Position.IsOnLand, Morale = party.Morale,
                    Visible = party.IsVisible, UsedByQuest = party.IsCurrentlyUsedByAQuest,
                    HadCustomName = !TextObject.IsNullOrEmpty(party.Party.CustomName), OriginalName = party.Party.CustomName?.ToString() ?? "",
                    Protected = true };
                // Ownership precedes native settlement and roster callbacks.
                using (NpcPartyRelocationScope.Enter(party.StringId))
                    if (party.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(party);
                EnforceSummonsCamp(duty.Camp);
                party.Party.SetCustomName(new TextObject(hero.Name + "'s Waiting Camp"));
            }
            if (party != null && party.MemberRoster.Contains(hero.CharacterObject)) party.MemberRoster.AddToCounts(hero.CharacterObject, -1);
            if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
            hero.ChangeState(Hero.CharacterStates.Traveling);
            duty.SummonsDeparted = true;
        }
        private void EnforceSummonsCamp(NpcSummonsCamp camp)
        {
            var party = LifeParty(camp.PartyId);
            if (!camp.Protected || party == null || party == MobileParty.MainParty || party.MapEvent != null
                || party.IsCurrentlyAtSea || party.IsTransitionInProgress) return;
            ReignTemporaryPartyGuestCampaignBehavior.ProtectWaitingCamp(party,
                new CampaignVec2(new Vec2(camp.X, camp.Y), camp.OnLand), camp.Morale);
        }
        private void MaintainSummonsCamps()
        {
            foreach (var duty in _lifeDuties.Values.Where(d => d.Camp?.Protected == true).ToList())
            {
                var party = LifeParty(duty.Camp.PartyId);
                if (party != null && (duty.Camp.ClanId != (party.ActualClan?.StringId ?? "")
                    || party.LeaderHero != null && party.LeaderHero.StringId != duty.HeroId))
                {
                    ReleaseSummonsCamp(duty, false);
                    foreach (var journey in _meetingTravel.Values.Where(j => !j.Terminal && j.Meeting.TargetId == duty.HeroId).ToList())
                        InterruptMeeting(journey, "The waiting party has acquired a different owner or leader.");
                }
                else EnforceSummonsCamp(duty.Camp);
            }
        }
        private void ReleaseSummonsCamp(NpcLifeDuty duty, bool leaderUnavailable)
        {
            var camp = duty.Camp;
            if (camp == null || !camp.Protected) return;
            var party = LifeParty(camp.PartyId);
            camp.Protected = false;
            if (party == null || party == MobileParty.MainParty
                || ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(party) == true) return;
            party.IgnoreByOtherPartiesTill(CampaignTime.Now);
            party.SetPartyUsedByQuest(camp.UsedByQuest);
            party.IsVisible = camp.Visible;
            bool otherOwner = ReignCampaignCommandBehavior.Instance?.Orders.Any(o => !o.IsTerminal
                && o.CommanderHeroStringId == (party.LeaderHero?.StringId ?? duty.HeroId) && o.Objective == "timed_hold") == true;
            party.Ai.SetDoNotMakeNewDecisions(duty.DecisionLock || otherOwner);
            party.Party.SetCustomName(camp.HadCustomName ? new TextObject(camp.OriginalName) : TextObject.GetEmpty());
            if (leaderUnavailable && (party.LeaderHero == null || party.LeaderHero.StringId == duty.HeroId)) DisbandPartyAction.StartDisband(party);
            else if (!otherOwner && party.LeaderHero?.StringId == duty.HeroId)
            {
                var destination = Settlement.Find(duty.TargetSettlementId);
                if (SafePlace(party.LeaderHero, destination)) party.SetMoveGoToSettlement(destination, party.NavigationCapability, false);
            }
        }
        // Called only after all meeting owners have released this saved duty.
        private bool RestoreSoloAttendance(NpcLifeDuty duty, Hero hero)
        {
            var party = LifeParty(duty.PartyId);
            if (hero?.PartyBelongedTo?.MapEvent != null || party?.MapEvent != null
                || hero?.PartyBelongedTo?.IsTransitionInProgress == true || party?.IsTransitionInProgress == true
                || hero?.PartyBelongedTo?.IsCurrentlyAtSea == true || party?.IsCurrentlyAtSea == true) return false;
            if (MeetingParticipantBlockReason(hero).Length > 0 || !MayReturnFromLifeAbsence(hero, duty.GuestAgreementId))
            { ReleaseSummonsCamp(duty, true); return true; }
            bool sameClan = duty.OriginalClanId == (hero.Clan?.StringId ?? "");
            bool ownParty = party != null && (duty.Camp == null || duty.Camp.ClanId == (party.ActualClan?.StringId ?? ""))
                && (party.LeaderHero == hero || party.LeaderHero == null);
            if (!sameClan || duty.WasLeader && !ownParty) party = null;
            // Do not steal a hero who has acquired a different native commitment.
            if (hero.PartyBelongedTo != null && hero.PartyBelongedTo != party
                && hero.PartyBelongedTo.StringId != duty.AttendancePartyId)
            { ReleaseSummonsCamp(duty, true); return true; }
            if (party != null && (party.IsCurrentlyAtSea || party.IsDisbanding)) party = null;
            if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
            if (party != null)
            {
                if (duty.WasLeader) TeleportHeroAction.ApplyImmediateTeleportToPartyAsPartyLeader(hero, party);
                else { hero.ChangeState(Hero.CharacterStates.Active); AddHeroToPartyAction.Apply(hero, party, false); }
                if (hero.PartyBelongedTo != party || !party.MemberRoster.Contains(hero.CharacterObject)) return false;
            }
            else
            {
                var place = Settlement.Find(duty.SettlementId);
                if (!SafePlace(hero, place)) place = hero.HomeSettlement;
                if (!SafePlace(hero, place)) place = Settlement.All.FirstOrDefault(s => (s.IsTown || s.IsVillage || s.IsCastle) && SafePlace(hero, s));
                if (place == null) return false;
                TeleportHeroAction.ApplyImmediateTeleportToSettlement(hero, place);
                if (hero.CurrentSettlement != place) return false;
            }
            ReleaseSummonsCamp(duty, party == null);
            var governorTown = Settlement.Find(duty.GovernorSettlementId)?.Town;
            if (hero.GovernorOf == null && governorTown != null && governorTown.Governor == null && governorTown.OwnerClan == hero.Clan)
                ChangeGovernorAction.Apply(governorTown, hero);
            return true;
        }
        private JObject SummonsCampEvidence(string heroId)
        {
            if (!_lifeDuties.TryGetValue(heroId, out var duty) || !duty.SoloAttendance) return null;
            var camp = duty.Camp;
            var party = camp == null ? null : LifeParty(camp.PartyId);
            return new JObject { ["soloAttendance"] = true, ["departed"] = duty.SummonsDeparted,
                ["record"] = camp == null ? null : JObject.FromObject(camp),
                ["safetyHooks"] = ReignTemporaryPartyGuestPatches.BuildDiagnostics(),
                ["nativeParty"] = party == null ? null : new JObject { ["id"] = party.StringId,
                    ["active"] = party.IsActive, ["protectedCamp"] = IsSummonsCamp(party), ["leaderId"] = party.LeaderHero?.StringId ?? "",
                    ["position"] = new JObject { ["x"] = party.Position.X, ["y"] = party.Position.Y, ["onLand"] = party.Position.IsOnLand },
                    ["decisionLock"] = party.Ai.DoNotMakeNewDecisions, ["questUsed"] = party.IsCurrentlyUsedByAQuest,
                    ["ignored"] = party.ShouldBeIgnored, ["morale"] = party.Morale, ["partyContents"] = JObject.FromObject(ObserveLifePartyContents(party)) } };
        }
    }
}
