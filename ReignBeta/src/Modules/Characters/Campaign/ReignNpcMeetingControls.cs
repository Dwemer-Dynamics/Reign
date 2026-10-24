using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private static bool MeetingControl(string action) => action == "npc_life.change_meeting" || action == "npc_life.cancel_meeting";
        private static string FieldMeetingId(CampaignVec2 point) => "field:" + point.ToVec2().X.ToString("R", CultureInfo.InvariantCulture)
            + ":" + point.ToVec2().Y.ToString("R", CultureInfo.InvariantCulture) + ":" + (point.IsOnLand ? "land" : "sea");
        private static bool FieldMeetingPoint(string id, out CampaignVec2 point)
        {
            point = default; var parts = (id ?? "").Split(':');
            if (parts.Length != 4 || parts[0] != "field" || (parts[3] != "land" && parts[3] != "sea")
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !NpcLivesPolicy.Finite(x) || !NpcLivesPolicy.Finite(y) || Math.Abs(x) > 100000 || Math.Abs(y) > 100000) return false;
            point = new CampaignVec2(new Vec2(x, y), parts[3] == "land"); return true;
        }
        private static string MeetingPlaceName(string id) => Settlement.Find(id)?.Name?.ToString()
            ?? (FieldMeetingPoint(id, out _) ? "the fixed field rendezvous (" + id.Substring(6).Replace(':', ',') + ")" : id);
        private static bool CanHostFieldMeeting(Hero hero) => Free(hero) && hero.PartyBelongedTo != null
            && hero.PartyBelongedTo != MobileParty.MainParty && hero.PartyBelongedTo.LeaderHero == hero;
        private static bool AtMeetingPlace(Hero hero, string id)
        {
            if (!Free(hero) || !hero.IsActive) return false;
            if (FieldMeetingPoint(id, out var point)) return hero.CurrentSettlement == null && hero.PartyBelongedTo != null
                && hero.PartyBelongedTo.Position.IsOnLand == point.IsOnLand && hero.PartyBelongedTo.Position.Distance(point) <= 1.5f;
            return hero.CurrentSettlement?.StringId == id;
        }
        private static bool SafeMeetingPlace(Hero hero, string id) => FieldMeetingPoint(id, out var point)
            ? point.IsOnLand && hero != null && hero.IsAlive && !hero.IsPrisoner : SafePlace(hero, Settlement.Find(id));
        private static bool TogetherForMeeting(Hero issuer, Hero player) => issuer != null && player != null
            && (issuer.CurrentSettlement != null && issuer.CurrentSettlement == player.CurrentSettlement
                || issuer.CurrentSettlement == null && player.CurrentSettlement == null && CanHostFieldMeeting(issuer)
                && player.PartyBelongedTo != null && issuer.PartyBelongedTo.Position.Distance(player.PartyBelongedTo.Position) <= 4);
        internal IReadOnlyList<ReignActionOption> MeetingOptions(string issuer) => _meetingTravel.Values
            .Where(j => !j.Terminal && j.Meeting.IssuerId == issuer).Select(j => new ReignActionOption {
                Id = j.Meeting.Id, Label = j.Meeting.Purpose + " — " + MeetingPlaceName(j.Meeting.DestinationId),
                Arguments = new Dictionary<string, string> { ["meetingRevision"] = j.Meeting.Revision.ToString(CultureInfo.InvariantCulture) }
            }).ToList();
        internal IReadOnlyList<ReignActionOption> MeetingPlaceOptions(ActionClause clause)
        {
            var result = new List<ReignActionOption>(); if (clause == null) return result;
            Hero issuer = ReignActionService.HeroById(clause.ActorId), player = Hero.MainHero;
            bool changing = clause.ActionId == "npc_life.change_meeting";
            var active = _meetingTravel.Values.FirstOrDefault(j => !j.Terminal && j.Meeting.Id == ReignActionService.Arg(clause, "meetingId"));
            Hero target = ReignActionService.HeroById(changing ? active?.Meeting.TargetId : ReignActionService.Arg(clause, "targetHeroId"));
            if (issuer == null || player == null) return result;
            foreach (var place in Settlement.All.Where(s => (s.IsTown || s.IsVillage || s.IsCastle)
                && SafePlace(issuer, s) && (target == null || SafePlace(target, s))
                && (changing || s == issuer.CurrentSettlement && s == player.CurrentSettlement)))
                result.Add(new ReignActionOption { Id = place.StringId, Label = place.Name.ToString() });
            if (CanHostFieldMeeting(issuer) && player.CurrentSettlement == null && MobileParty.MainParty != null && MobileParty.MainParty.Position.IsOnLand
                && (changing || TogetherForMeeting(issuer, player)))
                result.Add(new ReignActionOption { Id = FieldMeetingId(MobileParty.MainParty.Position), Label = "This fixed field position" });
            return result;
        }
        private void CheckMeetingControl(ActionAgreement agreement, ActionClause clause, List<string> errors)
        {
            var journey = _meetingTravel.Values.FirstOrDefault(j => !j.Terminal && j.Meeting.Id == ReignActionService.Arg(clause, "meetingId"));
            if (journey == null || journey.Meeting.IssuerId != clause.ActorId
                || journey.Meeting.Revision != LifeNumber(clause, "meetingRevision", -1))
            { errors.Add("This meeting has changed or ended; review its current terms."); return; }
            if (clause.ActionId == "npc_life.change_meeting")
            {
                foreach (string problem in new[] { MeetingParticipantProblem(ReignActionService.HeroById(journey.Meeting.IssuerId), "The issuer"),
                    MeetingParticipantProblem(ReignActionService.HeroById(journey.Meeting.TargetId), "The summoned person") })
                    if (problem.Length > 0) errors.Add(problem);
                if (!HasMeetingAuthority(ReignActionService.HeroById(journey.Meeting.IssuerId), ReignActionService.HeroById(journey.Meeting.TargetId)))
                    errors.Add("The leader no longer has authority to reschedule this summons.");
                if (journey.Meeting.SoloAttendance)
                {
                    string departure = SoloDepartureProblem(ReignActionService.HeroById(journey.Meeting.TargetId));
                    if (departure.Length > 0) errors.Add(departure);
                }
                if ((ReignActionService.Arg(clause, "attendanceMode") == "solo_camp") != journey.Meeting.SoloAttendance)
                    errors.Add("The revised meeting must preserve its accepted attendance arrangement.");
                string place = ReignActionService.Arg(clause, "destinationId");
                if (!MeetingPlaceOptions(clause).Any(o => o.Id == place)) errors.Add("Select an available fixed meeting destination.");
                double days = LifeNumber(clause, "stayDays", double.NaN);
                if (!NpcLivesPolicy.Finite(days) || days < .25 || days > 31.5) errors.Add("Choose a stay of 0.25–31.5 days after arrival.");
                if (_meetingTravel.Values.Any(j => !j.Terminal && j != journey && j.Meeting.DestinationId != place
                    && new[] { j.Meeting.IssuerId, j.Meeting.TargetId }.Intersect(new[] { journey.Meeting.IssuerId, journey.Meeting.TargetId }).Any()))
                    errors.Add("Another meeting still holds a participant at a different place.");
            }
        }
        private void ApplyMeetingControl(ActionAgreement agreement, ActionClause clause, string operation)
        {
            var old = _meetingTravel.Values.Single(j => !j.Terminal && j.Meeting.Id == ReignActionService.Arg(clause, "meetingId"));
            old.ControlAgreementId = agreement.Id; old.ControlOperationId = operation;
            bool change = clause.ActionId == "npc_life.change_meeting";
            if (change)
            {
                string destination = ReignActionService.Arg(clause, "destinationId");
                bool bothPresent = AtMeetingPlace(ReignActionService.HeroById(old.Meeting.IssuerId), destination)
                    && AtMeetingPlace(ReignActionService.HeroById(old.Meeting.TargetId), destination);
                var revised = old.Meeting.Revise(agreement.Id, destination, LifeNumber(clause, "stayDays", 3),
                    CampaignTime.Now.ToDays, LifeNumber(clause, "arrivalDays", double.NaN), bothPresent);
                var next = new NpcMeetingJourney { OperationId = operation, AgreementId = agreement.Id, Meeting = revised,
                    ReleaseArmy = old.ReleaseArmy };
                _meetingTravel.Add(operation, next);
                old.Meeting.Status = "cancelled"; old.Meeting.Interruption = "Superseded by accepted meeting revision " + revised.Revision + ".";
                QueueMeetingReceipt(next, "meeting_revised", agreement.TermsHash(), operation);
                if (revised.SoloAttendance && revised.Status != "meeting")
                {
                    try { BeginSoloAttendance(next, ReignActionService.HeroById(revised.TargetId)); }
                    catch (Exception ex)
                    {
                        InterruptMeeting(next, "The revised summons could not set out safely: " + ex.Message);
                        RestoreReleasedMeetingDuties(CampaignTime.Now.ToDays);
                    }
                }
            }
            else
            {
                old.Meeting.Status = "cancelled"; old.Meeting.Interruption = "Cancelled by mutual agreement " + agreement.Id + ".";
                QueueMeetingReceipt(old, "meeting_cancelled", agreement.TermsHash(), operation);
            }
        }
        internal void ReconcileMeetingAgreements()
        {
            foreach (var journey in _meetingTravel.Values.Where(j => j.ControlAgreementId.Length > 0 && _acceptedOperations.Contains(j.ControlOperationId)))
                ReignAgreementBehavior.Instance?.RetireMeetingAgreement(journey.AgreementId, journey.ControlAgreementId, journey.Meeting.Id, journey.Meeting.Revision);
        }
        private void HoldMeetingHero(Hero hero, string destination)
        {
            if (!FieldMeetingPoint(destination, out var point)) { HoldMeetingHero(hero, Settlement.Find(destination)); return; }
            if (!Free(hero)) return;
            CaptureMeetingDuty(hero); var party = hero.PartyBelongedTo;
            if (party == null || party == MobileParty.MainParty || party.LeaderHero != hero) return;
            party.Ai.SetDoNotMakeNewDecisions(true);
            if (AtMeetingPlace(hero, destination)) party.SetMoveModeHold();
            else party.SetMoveGoToPoint(point, party.NavigationCapability);
        }
    }
}
