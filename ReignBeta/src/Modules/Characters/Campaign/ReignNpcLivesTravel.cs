using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Campaign
{
    public sealed class NpcPersonalJourney
    {
        public string Id { get; set; } = "";
        public string HeroId { get; set; } = "";
        public string DestinationId { get; set; } = "";
        public string Status { get; set; } = "away";
        public int Revision { get; set; }
        public double DepartedDay { get; set; }
        public double ArrivalDay { get; set; }
        public double ReturnDay { get; set; }
        public string GuestAgreementId { get; set; } = "";
        public string OriginalClanId { get; set; } = "";
    }
    public sealed class NpcLifeDuty
    {
        public string HeroId { get; set; } = "";
        public string PartyId { get; set; } = "";
        public string SettlementId { get; set; } = "";
        public string TargetSettlementId { get; set; } = "";
        public bool WasLeader { get; set; }
        public bool DecisionLock { get; set; }
        public double? ReturnDueDay { get; set; }
        public string GuestAgreementId { get; set; } = "";
        public string OriginalClanId { get; set; } = "";
        public bool SoloAttendance { get; set; }
        public bool SummonsDeparted { get; set; }
        public NpcSummonsCamp Camp { get; set; }
        public string AttendancePartyId { get; set; } = "";
        public string GovernorSettlementId { get; set; } = "";
    }
    public sealed class NpcMeetingJourney
    {
        public string CommunicationId { get; set; } = "";
        public string CommunicationAcceptanceHash { get; set; } = "";
        public string OperationId { get; set; } = "";
        public string AgreementId { get; set; } = "";
        public NpcSummonedMeeting Meeting { get; set; } = new NpcSummonedMeeting();
        public double CourierDueDay { get; set; }
        public double? PartylessArrivalDay { get; set; }
        public double? IssuerArrivalDay { get; set; }
        public string ControlAgreementId { get; set; } = "";
        public string ControlOperationId { get; set; } = "";
        public bool ReleaseArmy { get; set; }
        public NpcPartyContentDigest TargetRelocationBefore { get; set; }
        public NpcPartyContentDigest TargetRelocationAfter { get; set; }
        public double? TargetRelocationDay { get; set; }
        public bool Terminal => Meeting.Status == "completed" || Meeting.Status == "interrupted" || Meeting.Status == "cancelled";
    }
    public sealed partial class ReignNpcLivesBehavior
    {
        private Dictionary<string, NpcPersonalJourney> _personalTravel = new Dictionary<string, NpcPersonalJourney>(StringComparer.Ordinal);
        private Dictionary<string, NpcMeetingJourney> _meetingTravel = new Dictionary<string, NpcMeetingJourney>(StringComparer.Ordinal);
        private Dictionary<string, NpcLifeDuty> _lifeDuties = new Dictionary<string, NpcLifeDuty>(StringComparer.Ordinal);
        private Dictionary<string, JObject> _storyEncounters = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private double _travelTickDay = -1;
        internal bool IsAwayOnPersonalBusiness(string hero) => _personalTravel.Values.Any(l => l.HeroId == hero && l.Status == "away")
            || _lifeDuties.TryGetValue(hero, out var duty) && !duty.WasLeader;
        private void RestoreLifeTravel(JObject saved)
        {
            _storyBattles.Clear(); _startingStoryEncounter = false;
            _personalTravel = saved["travel"]?.ToObject<Dictionary<string, NpcPersonalJourney>>() ?? new Dictionary<string, NpcPersonalJourney>();
            _meetingTravel = saved["meetings"]?.ToObject<Dictionary<string, NpcMeetingJourney>>() ?? new Dictionary<string, NpcMeetingJourney>();
            _communicationHosts = saved["communicationHosts"]?.ToObject<Dictionary<string, CommunicationHostReservation>>() ?? new Dictionary<string, CommunicationHostReservation>();
            _lifeDuties = saved["duties"]?.ToObject<Dictionary<string, NpcLifeDuty>>() ?? new Dictionary<string, NpcLifeDuty>();
            _storyEncounters = saved["encounters"]?.ToObject<Dictionary<string, JObject>>() ?? new Dictionary<string, JObject>();
            _storyBindings = saved["bindings"]?.ToObject<Dictionary<string, JObject>>() ?? new Dictionary<string, JObject>();
            _travelTickDay = -1;
        }
        private static bool Free(Hero h) => h != null && h.IsAlive && !h.IsPrisoner && h.PartyBelongedTo?.MapEvent == null;
        private static bool TravelSafe() => Mission.Current == null && MobileParty.MainParty?.MapEvent == null
            && TaleWorlds.CampaignSystem.Campaign.Current?.ConversationManager?.IsConversationInProgress != true
            && !ReignBeta.UI.ReignIndividualChatScreenManager.IsOpen && !ReignBeta.UI.ReignPartyChatScreenManager.IsOpenOrPending;
        private static bool SafePlace(Hero h, Settlement s) => h != null && s != null && !s.IsUnderSiege && !s.IsHideout
            && !FactionManager.IsAtWarAgainstFaction(h.MapFaction, s.MapFaction);
        private static string CurrentLifeGuestAgreement(Hero hero) => ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
            .FirstOrDefault(r => r.HeroStringId == hero?.StringId && r.IsLive)?.AgreementId ?? "";
        private static bool MayReturnFromLifeAbsence(Hero hero, string agreement)
        {
            var record = ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance?.Records
                .FirstOrDefault(r => r.HeroStringId == hero?.StringId && r.AgreementId == agreement);
            return NpcLivesPolicy.CanResumeGuestService(agreement, record?.AgreementId ?? "", record?.Phase.ToString() ?? "",
                hero?.Clan == Clan.PlayerClan && hero?.CompanionOf == Clan.PlayerClan);
        }
        private static double TripDays(Hero h, Settlement destination)
        {
            var position = h.PartyBelongedTo?.Position ?? h.CurrentSettlement?.GatePosition ?? MobileParty.MainParty.Position;
            return Math.Max(1d / 24, position.Distance(destination.GatePosition) / (4d * 24d));
        }
        private void AdvanceLifeTravel()
        {
            MaintainSummonsCamps();
            if (!TravelSafe()) return;
            // Presence can change through an accepted invitation or a scene/menu
            // transition while native campaign time is stopped.
            RecordNativeStoryVisits();
            ReconcileStoryBindings();
            double now = CampaignTime.Now.ToDays;
            if (_travelTickDay >= 0 && now < _travelTickDay + 1d / 1440) return;
            _travelTickDay = now;
            if (!_startingStoryEncounter)
                foreach (var entry in _storyEncounters.Where(e => e.Value.Value<string>("status") != "resolved").ToList())
                    if (State.Stories.Any(s => s.Id == entry.Value.Value<string>("storyId") && s.NativeEncounterId == entry.Key))
                        ResolveStoryEncounter(entry.Key, "withdrawn");
                    else if (!_outbox.Any(o => o.Value<string>("operationId") == entry.Key + ":started")) entry.Value["status"] = "resolved";
            foreach (var leave in State.Leaves.Where(l => l.Status == "waiting" && l.DepartureDay.HasValue && now >= l.DepartureDay.Value).ToList())
            {
                if (_personalTravel.ContainsKey(leave.Id)) continue;
                Hero hero = ReignActionService.HeroById(leave.HeroId); Settlement destination = Settlement.Find(leave.DestinationId);
                if (!Free(hero) || !SafePlace(hero, destination) || hero.PartyBelongedTo != MobileParty.MainParty || IsMeetingHeld(hero.StringId)) continue;
                double outbound = TripDays(hero, destination);
                var journey = new NpcPersonalJourney { Id = leave.Id, HeroId = leave.HeroId, DestinationId = leave.DestinationId, Revision = leave.Revision,
                    DepartedDay = now, ArrivalDay = now + outbound, ReturnDay = now + leave.AbsenceDays,
                    GuestAgreementId = CurrentLifeGuestAgreement(hero), OriginalClanId = hero.Clan?.StringId ?? "" };
                _personalTravel.Add(leave.Id, journey); // Ownership exists before the native roster event.
                MobileParty.MainParty.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
                hero.ChangeState(Hero.CharacterStates.Traveling);
                QueueLeaveReceipt(journey, "departed", now);
                InformationManager.DisplayMessage(new InformationMessage(hero.Name + " has set out on personal leave."));
            }
            foreach (var journey in _personalTravel.Values.Where(l => l.Status == "away").ToList())
            {
                Hero hero = ReignActionService.HeroById(journey.HeroId); Settlement destination = Settlement.Find(journey.DestinationId);
                if (hero == null || !hero.IsAlive || hero.IsPrisoner || !MayReturnFromLifeAbsence(hero, journey.GuestAgreementId)
                    || journey.OriginalClanId.Length > 0 && journey.OriginalClanId != (hero.Clan?.StringId ?? "") && hero.CompanionOf != Clan.PlayerClan)
                { journey.Status = "interrupted"; QueueLeaveReceipt(journey, "interrupted", now); continue; }
                if (now >= journey.ReturnDay && Free(hero) && Free(Hero.MainHero))
                {
                    if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
                    hero.ChangeState(Hero.CharacterStates.Active);
                    AddHeroToPartyAction.Apply(hero, MobileParty.MainParty, false);
                    if (hero.PartyBelongedTo != MobileParty.MainParty || !MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject))
                        throw new InvalidOperationException("The returning companion was not restored to the party.");
                    journey.Status = "returned"; QueueLeaveReceipt(journey, "returned", now);
                    InformationManager.DisplayMessage(new InformationMessage(hero.Name + " has returned from personal leave."));
                }
                else if (now >= journey.ArrivalDay && hero.CurrentSettlement == null && SafePlace(hero, destination))
                {
                    hero.ChangeState(Hero.CharacterStates.Active); EnterSettlementAction.ApplyForCharacterOnly(hero, destination);
                }
            }
            foreach (var meeting in _meetingTravel.Values.Where(m => !m.Terminal).ToList()) AdvanceMeeting(meeting, now);
            RestoreReleasedMeetingDuties(now);
        }
        private void QueueLeaveReceipt(NpcPersonalJourney journey, string kind, double day)
        {
            var p = ReignServerClient.CaptureNpcLivesContext(Generation); string id = journey.Id + ":" + kind;
            p["kind"] = kind; p["id"] = journey.Id; p["heroId"] = journey.HeroId; p["revision"] = journey.Revision; p["worldDay"] = day;
            p["nativeReceipt"] = id; QueueLifeOperation("native", p, id);
        }
        private static bool CanReleaseMeetingArmy(Hero issuer, Hero target) => target?.PartyBelongedTo?.Army != null
            && target.PartyBelongedTo.LeaderHero == target
            && (target.PartyBelongedTo.Army.LeaderParty?.LeaderHero == issuer
                || issuer?.Clan?.Kingdom?.Leader == issuer && issuer.Clan.Kingdom == target.Clan?.Kingdom);
        private void CaptureMeetingDuty(Hero hero)
        {
            if (hero == null) return;
            MobileParty party = hero.PartyBelongedTo;
            if (_lifeDuties.TryGetValue(hero.StringId, out var retained))
            {
                // Native daily recruitment can give a previously partyless noble
                // a household party. Keep its actual leader and restore these
                // current duties when the meeting ends, rather than removing
                // the leader as though they were still a passenger.
                if (!retained.SoloAttendance && !retained.WasLeader && hero.IsAlive && !hero.IsPrisoner
                    && party != null && party.IsActive && party != MobileParty.MainParty && party.LeaderHero == hero)
                {
                    retained.WasLeader = true; retained.PartyId = party.StringId;
                    retained.DecisionLock = party.Ai.DoNotMakeNewDecisions;
                    retained.TargetSettlementId = party.TargetSettlement?.StringId ?? "";
                    retained.ReturnDueDay = null;
                }
                return;
            }
            _lifeDuties[hero.StringId] = new NpcLifeDuty { HeroId = hero.StringId, PartyId = party?.StringId ?? "",
                SettlementId = hero.CurrentSettlement?.StringId ?? "", WasLeader = party != null && party != MobileParty.MainParty && party.LeaderHero == hero,
                DecisionLock = party?.Ai.DoNotMakeNewDecisions ?? false, TargetSettlementId = party?.TargetSettlement?.StringId ?? "",
                GovernorSettlementId = hero.GovernorOf?.Settlement?.StringId ?? "",
                GuestAgreementId = CurrentLifeGuestAgreement(hero), OriginalClanId = hero.Clan?.StringId ?? "" };
        }
        private void StartSummonedMeeting(ActionAgreement a, ActionClause c, string operation)
        {
            if (_meetingTravel.ContainsKey(operation)) return;
            if (!ReignNpcLivesPatches.VerifyReady())
                throw new InvalidOperationException("The native meeting safety hooks are unavailable; the summons cannot start.");
            Hero issuer = ReignActionService.HeroById(c.ActorId), target = ReignActionService.HeroById(ReignActionService.Arg(c, "targetHeroId"));
            var state = new NpcSummonedMeeting { Id = "meeting_" + a.Id + "_" + c.Id, ProposalId = a.Id, Revision = a.Revision,
                IssuerId = issuer.StringId, TargetId = target.StringId, DestinationId = ReignActionService.Arg(c, "destinationId"),
                Purpose = ReignActionService.Arg(c, "purpose"), StayDays = LifeNumber(c, "stayDays", 3),
                SoloAttendance = ReignActionService.Arg(c, "attendanceMode") == "solo_camp" };
            if (!state.ScheduleArrival(CampaignTime.Now.ToDays, LifeNumber(c, "arrivalDays", double.NaN)))
                throw new InvalidOperationException("The accepted summons needs a valid fixed arrival wait.");
            var meeting = new NpcMeetingJourney { OperationId = operation, AgreementId = a.Id, Meeting = state,
                ReleaseArmy = ReignActionService.Arg(c, "releaseArmy") == "yes" };
            _meetingTravel.Add(operation, meeting); CaptureMeetingDuty(issuer);
            QueueMeetingReceipt(meeting, "meeting_agreed", a.TermsHash(), operation);
            if (state.SoloAttendance)
            {
                try
                {
                    ReleaseSummonedArmy(meeting, issuer, target);
                    BeginSoloAttendance(meeting, target);
                }
                catch (Exception ex)
                {
                    InterruptMeeting(meeting, "The summoned person could not set out safely: " + ex.Message);
                    RestoreReleasedMeetingDuties(CampaignTime.Now.ToDays);
                    return;
                }
            }
            HoldMeetingHero(issuer, state.DestinationId);
            InformationManager.DisplayMessage(new InformationMessage(issuer.Name + " has agreed: " + target.Name
                + " will arrive in " + state.ArrivalDelayDays.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                + " days; both will stay " + state.StayDays.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " days after arrival."));
        }
        private void AdvanceMeeting(NpcMeetingJourney journey, double now)
        {
            var meeting = journey.Meeting;
            Hero issuer = ReignActionService.HeroById(meeting.IssuerId), target = ReignActionService.HeroById(meeting.TargetId);
            string unavailable = MeetingParticipantProblem(issuer, "The issuer");
            if (unavailable.Length == 0) unavailable = MeetingParticipantProblem(target, "The summoned person");
            if (unavailable.Length > 0) { InterruptMeeting(journey, "The meeting was interrupted: " + unavailable); return; }
            bool field = FieldMeetingPoint(meeting.DestinationId, out _);
            if (!CanSummon(issuer, target) || !SafeMeetingPlace(issuer, meeting.DestinationId) || !SafeMeetingPlace(target, meeting.DestinationId)
                || field && !CanHostFieldMeeting(issuer) && issuer.PartyBelongedTo?.MapEvent == null)
            { InterruptMeeting(journey, "The meeting is no longer possible because its authority or destination changed."); return; }
            if (!ReignNpcLivesPatches.VerifyReady()) return;
            // Never move a participant out of a live battle or native transition.
            if (issuer.PartyBelongedTo?.MapEvent != null || target.PartyBelongedTo?.MapEvent != null
                || issuer.PartyBelongedTo?.IsCurrentlyAtSea == true || target.PartyBelongedTo?.IsCurrentlyAtSea == true
                || issuer.PartyBelongedTo?.IsTransitionInProgress == true || target.PartyBelongedTo?.IsTransitionInProgress == true) return;
            if (issuer.PartyBelongedTo?.Army != null)
            { InterruptMeeting(journey, issuer.Name + " can no longer host the meeting while committed to army duties."); return; }
            if (meeting.Status != "meeting" && !meeting.ScheduledArrivalDay.HasValue)
            {
                InterruptMeeting(journey, "This older summons needs a new agreement with a scheduled arrival date."); return;
            }
            if (meeting.Status == "meeting" && target.PartyBelongedTo?.Army != null)
            {
                if (!journey.ReleaseArmy || !CanReleaseMeetingArmy(issuer, target))
                { InterruptMeeting(journey, target.Name + " cannot remain because army duties prevent an authorized release."); return; }
                target.PartyBelongedTo.Army = null;
            }
            if (meeting.SoloAttendance && _lifeDuties.TryGetValue(target.StringId, out var attendance)
                && attendance.Camp?.Protected == true && LifeParty(attendance.Camp.PartyId)?.MapEvent != null) return;
            if (meeting.Status == "meeting")
            {
                if (meeting.ReleaseDay.HasValue && now < meeting.ReleaseDay.Value)
                {
                    // A native travel flag alone does not mean an attendee left.
                    // Normalize only actual partyless presence at the agreed town;
                    // never teleport an absent attendee back to fabricate a stay.
                    foreach (var participant in new[] { issuer, target })
                        NormalizePresentMeetingAttendee(participant, meeting.DestinationId);
                    if (!AtMeetingPlace(issuer, meeting.DestinationId) || !AtMeetingPlace(target, meeting.DestinationId))
                    {
                        InterruptMeeting(journey, "The meeting was interrupted because a participant left before the agreed stay ended.");
                        return;
                    }
                    HoldMeetingHero(issuer, meeting.DestinationId);
                    HoldMeetingHero(target, meeting.DestinationId);
                }
                if (meeting.Complete(now))
                {
                    QueueMeetingReceipt(journey, "meeting_completed");
                    InformationManager.DisplayMessage(new InformationMessage("The agreed meeting stay has ended: " + meeting.Purpose + "."));
                }
                return;
            }
            CaptureMeetingDuty(issuer);
            // Native governor assignment can set Traveling without moving the
            // waiting host. Restore that flag without replaying settlement entry.
            NormalizePresentMeetingAttendee(issuer, meeting.DestinationId);
            if (!AtMeetingPlace(issuer, meeting.DestinationId)) RelocateMeetingParticipant(issuer, meeting.DestinationId);
            HoldMeetingHero(issuer, meeting.DestinationId);
            if (meeting.SoloAttendance && (!_lifeDuties.TryGetValue(target.StringId, out var departing) || !departing.SummonsDeparted))
            {
                try { BeginSoloAttendance(journey, target); }
                catch (Exception ex) { InterruptMeeting(journey, "The summoned person could not resume safe departure: " + ex.Message); return; }
            }
            if (now + 1e-9 < meeting.ScheduledArrivalDay.Value) return;
            if (target.PartyBelongedTo?.Army != null)
            {
                if (!journey.ReleaseArmy || !CanReleaseMeetingArmy(issuer, target))
                { InterruptMeeting(journey, target.Name + " cannot attend because army duties prevent an authorized release."); return; }
                target.PartyBelongedTo.Army = null;
            }
            CaptureMeetingDuty(target);
            if (!AtMeetingPlace(target, meeting.DestinationId)) RelocateMeetingParticipant(target, meeting.DestinationId, issuer, journey);
            bool available = AtMeetingPlace(target, meeting.DestinationId) && AtMeetingPlace(issuer, meeting.DestinationId);
            if (meeting.Arrive(target.StringId, meeting.DestinationId, now, available))
            {
                HoldMeetingHero(target, meeting.DestinationId); QueueMeetingReceipt(journey, "meeting_arrived");
                InformationManager.DisplayMessage(new InformationMessage(target.Name + " has arrived for the meeting: " + meeting.Purpose + "."));
            }
        }
        private void RelocateMeetingParticipant(Hero hero, string destination, Hero fieldHost = null, NpcMeetingJourney journey = null)
        {
            if (!Free(hero) || MeetingParticipantBlockReason(hero).Length > 0) return;
            bool field = FieldMeetingPoint(destination, out var point);
            Settlement place = field ? null : Settlement.Find(destination);
            if (!SafeMeetingPlace(hero, destination)) return;
            MobileParty party = hero.PartyBelongedTo;
            if (field && (party == null || party == MobileParty.MainParty || party.LeaderHero != hero)
                && (!CanHostFieldMeeting(fieldHost) || !AtMeetingPlace(fieldHost, destination)))
                throw new InvalidOperationException("The fixed field meeting no longer has its agreed host party.");
            bool solo = _lifeDuties.TryGetValue(hero.StringId, out var attendance) && attendance.SoloAttendance;
            if (!solo && party != null && party != MobileParty.MainParty && party.LeaderHero == hero)
            {
                if (party.Army != null || party.IsTransitionInProgress || party.IsCurrentlyAtSea) return;
                var contentsBefore = ObserveLifePartyContents(party).Digest;
                // Keep native presence/quest events, but do not turn a meeting teleport
                // into recruitment, garrison transfers or sales of its prisoners/items.
                using (NpcPartyRelocationScope.Enter(party.StringId))
                {
                    if (party.CurrentSettlement != null) LeaveSettlementAction.ApplyForParty(party);
                    party.SetPositionAfterMapChange(field ? point : place.GatePosition);
                    if (!field) EnterSettlementAction.ApplyForParty(party, place);
                }
                party.Ai.SetDoNotMakeNewDecisions(true); party.SetMoveModeHold();
                if (journey != null)
                {
                    journey.TargetRelocationBefore = contentsBefore;
                    journey.TargetRelocationAfter = ObserveLifePartyContents(party).Digest;
                    journey.TargetRelocationDay = CampaignTime.Now.ToDays;
                }
            }
            else
            {
                if (party != null) party.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
                hero.ChangeState(Hero.CharacterStates.Active);
                if (!field)
                {
                    EnterSettlementAction.ApplyForCharacterOnly(hero, place);
                    if (solo) attendance.AttendancePartyId = "";
                }
                else
                {
                    AddHeroToPartyAction.Apply(hero, fieldHost.PartyBelongedTo, false);
                    if (solo) attendance.AttendancePartyId = fieldHost.PartyBelongedTo.StringId;
                }
            }
        }
        private static NpcPartyContentObservation ObserveLifePartyContents(MobileParty party) => NpcPartyContentObservation.Capture(party.StringId,
            party.MemberRoster.GetTroopRoster().Select(r => new NpcPartyRosterEntry { CharacterId = r.Character?.StringId ?? "",
                Number = r.Number, Wounded = r.WoundedNumber, Xp = r.Xp }),
            party.PrisonRoster.GetTroopRoster().Select(r => new NpcPartyRosterEntry { CharacterId = r.Character?.StringId ?? "",
                Number = r.Number, Wounded = r.WoundedNumber, Xp = r.Xp }),
            party.ItemRoster.Select(r => new NpcPartyItemEntry { ItemId = r.EquipmentElement.Item?.StringId ?? "",
                ModifierId = r.EquipmentElement.ItemModifier?.StringId ?? "", Amount = r.Amount }));
        private void HoldMeetingHero(Hero hero, Settlement place)
        {
            if (!Free(hero) || place == null) return;
            CaptureMeetingDuty(hero); var duty = _lifeDuties[hero.StringId];
            var party = hero.PartyBelongedTo;
            if (duty.WasLeader && !duty.SoloAttendance)
            {
                if (party == null || party == MobileParty.MainParty || party.LeaderHero != hero) return;
                party.Ai.SetDoNotMakeNewDecisions(true);
                if (party.CurrentSettlement == place) party.SetMoveModeHold();
                else party.SetMoveGoToSettlement(place, party.NavigationCapability, false);
            }
            else
            {
                if (party != null) party.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                // Native hourly partyless travel can mark an attendee Traveling
                // without clearing CurrentSettlement. Keep an arrived person
                // available to speak while this meeting still owns their stay.
                // Prisoners/unavailable heroes were rejected before this hold.
                if (hero.CurrentSettlement == place && hero.IsTraveling)
                    hero.ChangeState(Hero.CharacterStates.Active);
                if (hero.CurrentSettlement == null) { hero.ChangeState(Hero.CharacterStates.Active); EnterSettlementAction.ApplyForCharacterOnly(hero, place); }
            }
        }
        internal bool IsMeetingHeld(string hero) => _meetingTravel.Values.Any(j => !j.Terminal
            && j.Meeting.NeedsHold(hero, CampaignTime.Now.ToDays)
            && CanSummon(ReignActionService.HeroById(j.Meeting.IssuerId), ReignActionService.HeroById(j.Meeting.TargetId))
            && SafeMeetingPlace(ReignActionService.HeroById(hero), j.Meeting.DestinationId)) || IsCommunicationHostHeld(hero);
        internal bool MustStayInMeetingSettlement(string hero) => _meetingTravel.Values.Any(j => !j.Terminal
            && j.Meeting.NeedsHold(hero, CampaignTime.Now.ToDays) && AtMeetingPlace(ReignActionService.HeroById(hero), j.Meeting.DestinationId)
            && CanSummon(ReignActionService.HeroById(j.Meeting.IssuerId), ReignActionService.HeroById(j.Meeting.TargetId))
            && Settlement.Find(j.Meeting.DestinationId)?.IsUnderSiege == false) || IsCommunicationHostHeld(hero);
        private void OnMeetingCanMoveToSettlement(Hero hero, ref bool result)
        {
            if (result && hero != Hero.MainHero && Free(hero) && MustStayInMeetingSettlement(hero.StringId))
                result = false;
        }
        internal Settlement MeetingPartySpawnSettlement(Hero hero) => hero != null && hero != Hero.MainHero
            && hero.PartyBelongedTo == null && MustStayInMeetingSettlement(hero.StringId) ? hero.CurrentSettlement : null;
        internal void RetainMeetingPartyAtSpawn(MobileParty party, Settlement place)
        {
            Hero hero = party?.LeaderHero;
            if (place == null || party == MobileParty.MainParty || !Free(hero) || !IsMeetingHeld(hero.StringId) || !SafePlace(hero, place)) return;
            CaptureMeetingDuty(hero);
            if (party.CurrentSettlement != place)
                using (NpcPartyRelocationScope.Enter(party.StringId)) EnterSettlementAction.ApplyForParty(party, place);
            HoldMeetingHero(hero, place);
        }
        internal bool OwnsMeetingParty(MobileParty party) => party != null && party != MobileParty.MainParty && party.LeaderHero != null
            && IsMeetingHeld(party.LeaderHero.StringId) && party.MapEvent == null;
        private void InterruptMeeting(NpcMeetingJourney journey, string reason)
        {
            journey.Meeting.Status = "interrupted"; journey.Meeting.Interruption = reason;
            QueueMeetingReceipt(journey, "meeting_interrupted");
            InformationManager.DisplayMessage(new InformationMessage(reason));
        }
        private void QueueMeetingReceipt(NpcMeetingJourney journey, string kind, string agreementHash = "", string operation = null)
        {
            var p = ReignServerClient.CaptureNpcLivesContext(Generation);
            string receipt = operation ?? journey.Meeting.Id + ":" + journey.Meeting.Revision + ":" + kind;
            p["id"] = journey.Meeting.Id; p["kind"] = kind; p["meeting"] = JObject.FromObject(journey.Meeting);
            p["agreementHash"] = agreementHash; p["nativeReceipt"] = receipt;
            if (journey.CommunicationId.Length > 0)
            {
                p["communicationId"] = journey.CommunicationId;
                p["acceptanceHash"] = journey.CommunicationAcceptanceHash;
            }
            QueueLifeOperation(journey.CommunicationId.Length > 0 ? "communication_receipt" : "native", p, receipt);
        }
        private void RestoreReleasedMeetingDuties(double now)
        {
            foreach (var duty in _lifeDuties.Values.ToList())
            {
                if (_meetingTravel.Values.Any(m => !m.Terminal && (m.Meeting.IssuerId == duty.HeroId || m.Meeting.TargetId == duty.HeroId))) continue;
                if (_communicationHosts.Values.Any(h => h.HostId == duty.HeroId)) continue;
                Hero hero = ReignActionService.HeroById(duty.HeroId);
                if (duty.SoloAttendance)
                {
                    if (RestoreSoloAttendance(duty, hero)) _lifeDuties.Remove(duty.HeroId);
                    continue;
                }
                CaptureMeetingDuty(hero);
                MobileParty party = MobileParty.All.FirstOrDefault(p => p.StringId == duty.PartyId && p.IsActive);
                if (MeetingParticipantBlockReason(hero).Length > 0)
                {
                    if (duty.WasLeader && party != null && party != MobileParty.MainParty && (party.LeaderHero == hero || party.LeaderHero == null))
                        party.Ai.SetDoNotMakeNewDecisions(duty.DecisionLock);
                    _lifeDuties.Remove(duty.HeroId); continue;
                }
                if (hero.PartyBelongedTo?.MapEvent != null || party?.MapEvent != null) continue;
                if (duty.WasLeader)
                {
                    if (party != null && party.LeaderHero == hero && party != MobileParty.MainParty)
                    {
                        bool otherOwner = ReignCampaignCommandBehavior.Instance?.Orders.Any(o => o.CommanderHeroStringId == hero.StringId && !o.IsTerminal && o.Objective == "timed_hold") == true;
                        party.Ai.SetDoNotMakeNewDecisions(duty.DecisionLock || otherOwner
                            || ReignBeta.PartyAgency.ReignTemporaryPartyGuestCampaignBehavior.Instance?.IsProtectedCamp(party) == true);
                        var destination = Settlement.Find(duty.TargetSettlementId);
                        if (SafePlace(hero, destination) && !otherOwner) party.SetMoveGoToSettlement(destination, party.NavigationCapability, false);
                    }
                }
                else
                {
                    if (!MayReturnFromLifeAbsence(hero, duty.GuestAgreementId)) { _lifeDuties.Remove(duty.HeroId); continue; }
                    if (duty.OriginalClanId != (hero.Clan?.StringId ?? "") && hero.CompanionOf != Clan.PlayerClan) party = null;
                    // Cancellation can release an issuer who never left their
                    // recorded duty location. Retire that lease without creating
                    // a new absence, detaching an already restored party member,
                    // or making an already present governor/ruler unavailable.
                    bool alreadyInOriginalParty = party != null && hero.PartyBelongedTo == party
                        && party.MemberRoster.Contains(hero.CharacterObject);
                    bool alreadyAtOriginalSettlement = party == null && hero.PartyBelongedTo == null
                        && !string.IsNullOrEmpty(duty.SettlementId)
                        && hero.CurrentSettlement?.StringId == duty.SettlementId;
                    if (alreadyInOriginalParty || alreadyAtOriginalSettlement)
                    {
                        _lifeDuties.Remove(duty.HeroId);
                        continue;
                    }
                    if (!duty.ReturnDueDay.HasValue)
                    {
                        var origin = hero.PartyBelongedTo?.Position ?? hero.CurrentSettlement?.GatePosition;
                        double distance = party != null && origin.HasValue ? party.Position.Distance(origin.Value) : 0;
                        duty.ReturnDueDay = now + Math.Max(1d / 24, distance / (4d * 24));
                        if (hero.PartyBelongedTo != null) hero.PartyBelongedTo.MemberRoster.AddToCounts(hero.CharacterObject, -1);
                        if (hero.CurrentSettlement != null) LeaveSettlementAction.ApplyForCharacterOnly(hero);
                        hero.ChangeState(Hero.CharacterStates.Traveling);
                    }
                    if (now < duty.ReturnDueDay.Value) continue;
                    hero.ChangeState(Hero.CharacterStates.Active);
                    if (party != null) AddHeroToPartyAction.Apply(hero, party, false);
                    else
                    {
                        var place = Settlement.Find(duty.SettlementId);
                        if (!SafePlace(hero, place)) place = Settlement.All.FirstOrDefault(s => (s.IsTown || s.IsVillage || s.IsCastle) && SafePlace(hero, s));
                        if (place == null) { hero.ChangeState(Hero.CharacterStates.Traveling); continue; }
                        EnterSettlementAction.ApplyForCharacterOnly(hero, place);
                    }
                }
                _lifeDuties.Remove(duty.HeroId);
            }
        }
        private void RecordNativeStoryVisits()
        {
            string location = Settlement.CurrentSettlement?.StringId ?? MobileParty.MainParty.CurrentSettlement?.StringId ?? "";
            if (location.Length == 0) return;
            foreach (var story in State.Stories.Where(s => s.Status == "active" && s.LocationId == location && s.PhaseIndex < s.Definition.Phases.Count))
            {
                Hero hero = ReignActionService.HeroById(story.HeroId);
                if (!Free(hero) || !MobileParty.MainParty.MemberRoster.Contains(hero.CharacterObject)) continue;
                foreach (var objective in story.Definition.Phases[story.PhaseIndex].Objectives.Where(o => o.Kind == "visit" && !story.Evidence.Any(e => e.ObjectiveId == o.Id)))
                {
                    string receipt = story.Id + ":" + objective.Id + ":visit";
                    var p = ReignServerClient.CaptureNpcLivesContext(Generation);
                    p["kind"] = "visit"; p["id"] = story.Id; p["heroId"] = story.HeroId; p["revision"] = story.Revision;
                    p["objectiveId"] = objective.Id; p["nativeReceipt"] = receipt; p["outcome"] = objective.RequiredOutcome;
                    QueueLifeOperation("native", p, receipt);
                }
            }
        }
    }
    internal static class ReignNpcLivesPatches
    {
        private const string Owner = "com.bannerlordreign.reignbeta.npc_lives";
        private const int RequiredHookCount = 12;
        private static Harmony _harmony;
        private static string _lastError = "";
        private sealed class Hook
        {
            internal MethodBase Target;
            internal MethodInfo Method;
            internal bool Prefix;
            internal bool Transpiler;
        }
        private static readonly List<Hook> Hooks = new List<Hook>();

        internal static void Apply()
        {
            if (VerifyReady()) return;
            Unapply();
            try
            {
                _harmony = new Harmony(Owner);
                Install(typeof(LordPartyComponent), nameof(LordPartyComponent.CreateLordParty), typeof(NpcMeetingPartySpawnPatch), "Prefix", true);
                Install(typeof(LordPartyComponent), nameof(LordPartyComponent.CreateLordParty), typeof(NpcMeetingPartySpawnPatch), "Postfix", false);
                Install(typeof(HeroSpawnCampaignBehavior), "GetHeroPartyCommandScore", typeof(NpcSummonsSpawnCandidatePatch), "Prefix", true);
                Install(typeof(LeaveSettlementAction), nameof(LeaveSettlementAction.ApplyForCharacterOnly), typeof(NpcMeetingCharacterStayPatch), "Prefix", true);
                Install(typeof(EnterSettlementAction), nameof(EnterSettlementAction.ApplyForCharacterOnly), typeof(NpcMeetingCharacterEntryObservationPatch), "Prefix", true);
                Install(typeof(TeleportationCampaignBehavior), "HourlyTick", typeof(NpcMeetingQueuedTeleportPatch), "Transpiler", false, true);
                Install(typeof(LeaveSettlementAction), nameof(LeaveSettlementAction.ApplyForParty), typeof(NpcMeetingPartyStayPatch), "Prefix", true);
                Install(typeof(RecruitmentCampaignBehavior), "OnBeforeSettlementEntered", typeof(NpcMeetingEntryCommercePatch), "Prefix", true);
                Install(typeof(PartiesSellPrisonerCampaignBehavior), "OnSettlementEntered", typeof(NpcMeetingEntryCommercePatch), "Prefix", true);
                Install(typeof(PartiesBuyFoodCampaignBehavior), "OnSettlementEntered", typeof(NpcMeetingEntryCommercePatch), "Prefix", true);
                Install(typeof(PartiesBuyHorseCampaignBehavior), "OnSettlementEntered", typeof(NpcMeetingEntryCommercePatch), "Prefix", true);
                Install(typeof(GarrisonTroopsCampaignBehavior), "OnSettlementEntered", typeof(NpcMeetingEntryCommercePatch), "Prefix", true);
                if (!VerifyReady()) throw new InvalidOperationException("The native meeting hook inventory is incomplete or duplicated.");
                _lastError = "";
                ReignLog.Info("NPC Lives native meeting interception ready: " + RequiredHookCount + " verified hooks.");
            }
            catch (Exception ex)
            {
                Unapply();
                _lastError = ex.GetType().Name + ": " + ex.Message;
                ReignLog.Warn("NPC Lives native meeting interception unavailable; new summons and relocation are held: " + ex);
            }
        }
        private static void Install(Type targetType, string targetName, Type patchType, string hookName, bool prefix, bool transpiler = false)
        {
            var target = AccessTools.Method(targetType, targetName) ?? throw new MissingMethodException(targetType.FullName, targetName);
            var method = AccessTools.DeclaredMethod(patchType, hookName) ?? throw new MissingMethodException(patchType.FullName, hookName);
            var hook = new HarmonyMethod(method);
            _harmony.Patch(target, prefix: prefix ? hook : null, postfix: !prefix && !transpiler ? hook : null,
                transpiler: transpiler ? hook : null);
            Hooks.Add(new Hook { Target = target, Method = method, Prefix = prefix, Transpiler = transpiler });
        }
        private static int InstalledCount() => Hooks.Count(h =>
        {
            var info = Harmony.GetPatchInfo(h.Target);
            return (h.Transpiler ? info?.Transpilers : h.Prefix ? info?.Prefixes : info?.Postfixes)?.Count(p => p.owner == Owner && p.PatchMethod == h.Method) == 1;
        });
        internal static bool VerifyReady() => _harmony != null && Hooks.Count == RequiredHookCount && InstalledCount() == RequiredHookCount;
        internal static JObject Snapshot() => new JObject { ["ready"] = VerifyReady(), ["requiredHookCount"] = RequiredHookCount,
            ["installedHookCount"] = InstalledCount(), ["lastError"] = _lastError.Length > 400 ? _lastError.Substring(0, 400) : _lastError };
        internal static void Unapply()
        {
            _harmony?.UnpatchAll(Owner);
            _harmony = null;
            Hooks.Clear();
        }
    }
    internal static class NpcMeetingEntryCommercePatch
    {
        private static bool Prefix(MobileParty __0) => __0 == null || __0 == MobileParty.MainParty
            || !NpcPartyRelocationScope.SuppressesCommerceFor(__0.StringId);
    }
    [HarmonyPatch(typeof(LordPartyComponent), nameof(LordPartyComponent.CreateLordParty))]
    internal static class NpcMeetingPartySpawnPatch
    {
        private static void Prefix(Hero __1, Hero __5, ref CampaignVec2 __2, ref float __3, ref Settlement __4, out Settlement __state)
        {
            __state = ReignNpcLivesBehavior.Instance?.MeetingPartySpawnSettlement(__5 ?? __1);
            if (__state == null) return;
            __2 = __state.GatePosition; __3 = 0; __4 = __state;
        }
        private static void Postfix(MobileParty __result, Settlement __state)
        {
            if (__state != null) ReignNpcLivesBehavior.Instance?.RetainMeetingPartyAtSpawn(__result, __state);
        }
    }
    internal static class NpcSummonsSpawnCandidatePatch
    {
        // Both native commander-selection passes compare this score against zero.
        // Exclude the held hero before creation; never return a null party to native callers.
        private static bool Prefix(Hero __0, ref float __result)
        {
            if (ReignNpcLivesBehavior.Instance?.SuppressSummonedPartySpawn(__0) != true) return true;
            __result = float.MinValue; return false;
        }
    }
    [HarmonyPatch(typeof(LeaveSettlementAction), nameof(LeaveSettlementAction.ApplyForCharacterOnly))]
    internal static class NpcMeetingCharacterStayPatch
    {
        private static bool Prefix(Hero __0)
        {
            bool allowed = __0 == null || __0 == Hero.MainHero || __0.IsPrisoner
            // A native party leader must leave as part of party initialization
            // and movement. The party-level hold owns their settled presence.
            || __0.PartyBelongedTo?.LeaderHero == __0
            || ReignNpcLivesBehavior.Instance?.MustStayInMeetingSettlement(__0.StringId) != true;
            ReignNpcLivesBehavior.Instance?.ObserveMeetingMovement(__0, "leave_settlement", allowed: allowed);
            return allowed;
        }
    }
    [HarmonyPatch(typeof(LeaveSettlementAction), nameof(LeaveSettlementAction.ApplyForParty))]
    internal static class NpcMeetingPartyStayPatch
    {
        private static bool Prefix(MobileParty __0) => __0?.LeaderHero == null || __0 == MobileParty.MainParty
            || NpcPartyRelocationScope.SuppressesCommerceFor(__0.StringId)
            || ReignNpcLivesBehavior.Instance?.MustStayInMeetingSettlement(__0.LeaderHero.StringId) != true;
    }
}
