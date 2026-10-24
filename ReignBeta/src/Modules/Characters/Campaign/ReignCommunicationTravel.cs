using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using ReignBeta.Actions;
using ReignBeta.Integration;
using ReignBeta.Shared.Characters;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace ReignBeta.Campaign
{
    public sealed partial class ReignNpcLivesBehavior
    {
        private sealed class CommunicationHostReservation
        {
            public string Id = "", HostId = "", TargetId = "", DestinationId = "", Hash = "";
            public double ExpiresDay;
        }
        private Dictionary<string, CommunicationHostReservation> _communicationHosts = new Dictionary<string, CommunicationHostReservation>();
        internal bool HasPendingCommunicationReceipts => _outbox.Any(row => row.Value<string>("route") == "communication_receipt");

        private string CommunicationHostProblem(CommunicationHostReservation hold)
        {
            Hero host = ReignActionService.HeroById(hold.HostId), target = ReignActionService.HeroById(hold.TargetId);
            // Native delayed teleport scheduling can mark a partyless resident
            // Traveling before it attempts to leave their current settlement.
            // An existing lease still owns that physically present resident;
            // otherwise the leave hook would release itself on the new flag.
            bool retainedResident = _communicationHosts.TryGetValue(hold.Id, out var owned) && owned.Hash == hold.Hash
                && Free(host) && host.IsTraveling && host.PartyBelongedTo == null
                && host.CurrentSettlement != null && host.CurrentSettlement.StringId == hold.DestinationId;
            return !ReignNpcLivesPatches.VerifyReady() ? "The native movement safety hooks are unavailable."
                : !ReignServerClient.NpcLivesOptions.Allows("meeting") ? "Summoned meetings are disabled."
                : host == Hero.MainHero || target == Hero.MainHero ? "A background invitation cannot reserve the player."
                : !CanSummon(host, target) ? "The host lacks authority or a participant is unavailable."
                : !AtMeetingPlace(host, hold.DestinationId) && !retainedResident || host.PartyBelongedTo?.Army != null ? "The host is no longer safely at the agreed meeting place."
                : !SafeMeetingPlace(host, hold.DestinationId) || !SafeMeetingPlace(target, hold.DestinationId) ? "The meeting place is unsafe."
                : _meetingTravel.Values.Any(m => !m.Terminal && (m.Meeting.TargetId == hold.HostId || m.Meeting.TargetId == hold.TargetId
                    || m.Meeting.IssuerId == hold.TargetId || m.Meeting.IssuerId == hold.HostId && m.Meeting.DestinationId != hold.DestinationId))
                    ? "An existing meeting conflicts with this invitation." : "";
        }

        private void QueueCommunicationHostReceipt(CommunicationHostReservation hold, string kind, string reason = "")
        {
            var payload = ReignServerClient.CaptureNpcLivesContext(Generation);
            payload["communicationId"] = hold.Id; payload["reservationHash"] = hold.Hash;
            payload["hostId"] = hold.HostId; payload["destinationId"] = hold.DestinationId;
            payload["kind"] = kind; payload["reason"] = reason;
            QueueLifeOperation("communication_receipt", payload, hold.Id + ":" + kind + ":" + hold.Hash);
        }

        internal void ReconcileCommunicationHosts(JArray work)
        {
            if (!_initialized) return;
            var wanted = work.Children<JObject>().ToDictionary(w => w.Value<string>("id") ?? "", StringComparer.Ordinal);
            foreach (var hold in _communicationHosts.Values.ToList())
                if (!wanted.TryGetValue(hold.Id, out var match) || match.Value<string>("reservationHash") != hold.Hash)
                    _communicationHosts.Remove(hold.Id);
            foreach (var row in wanted.Values)
            {
                string id = row.Value<string>("id") ?? "", hash = row.Value<string>("reservationHash") ?? "";
                if (id.Length == 0 || hash.Length == 0 || _meetingTravel.ContainsKey(id)) continue;
                if (_communicationHosts.ContainsKey(id)) continue;
                var hold = new CommunicationHostReservation { Id = id, Hash = hash, HostId = row.Value<string>("speakerId") ?? "",
                    TargetId = row.Value<string>("recipientId") ?? "", DestinationId = row.Value<string>("destinationId") ?? "",
                    ExpiresDay = row.Value<double?>("expiresDay") ?? 0 };
                string problem = CommunicationHostProblem(hold);
                if (!NpcLivesPolicy.Finite(hold.ExpiresDay) || CampaignTime.Now.ToDays >= hold.ExpiresDay)
                    problem = "The invitation wait has expired.";
                if (_communicationHosts.Values.Any(h => h.HostId == hold.HostId && h.DestinationId != hold.DestinationId
                    || h.TargetId == hold.HostId || h.HostId == hold.TargetId)) problem = "An invitation already reserves a participant elsewhere.";
                if (problem.Length > 0) { QueueCommunicationHostReceipt(hold, "host_blocked", problem); continue; }
                CaptureMeetingDuty(ReignActionService.HeroById(hold.HostId));
                _communicationHosts.Add(id, hold);
                HoldMeetingHero(ReignActionService.HeroById(hold.HostId), hold.DestinationId);
                QueueCommunicationHostReceipt(hold, "host_reserved");
            }
            AdvanceCommunicationHosts();
            RestoreReleasedMeetingDuties(CampaignTime.Now.ToDays);
        }

        private void AdvanceCommunicationHosts()
        {
            foreach (var hold in _communicationHosts.Values.ToList())
            {
                bool expired = CampaignTime.Now.ToDays >= hold.ExpiresDay;
                if (!expired && TravelSafe()) NormalizePresentMeetingAttendee(ReignActionService.HeroById(hold.HostId), hold.DestinationId);
                string problem = expired ? "The fourteen-day invitation wait expired." : CommunicationHostProblem(hold);
                if (problem.Length > 0)
                {
                    _communicationHosts.Remove(hold.Id);
                    QueueCommunicationHostReceipt(hold, expired ? "host_expired" : "host_lost", problem);
                }
                else if (TravelSafe()) HoldMeetingHero(ReignActionService.HeroById(hold.HostId), hold.DestinationId);
            }
        }

        private bool IsCommunicationHostHeld(string hero) => _communicationHosts.Values.Any(h => h.HostId == hero
            && CampaignTime.Now.ToDays < h.ExpiresDay && CommunicationHostProblem(h).Length == 0);

        internal void StartCommunicationSummons(JObject work)
        {
            string id = work.Value<string>("id") ?? "", acceptance = work.Value<string>("acceptanceHash") ?? "";
            if (!_initialized || id.Length == 0 || acceptance.Length == 0 || _meetingTravel.ContainsKey(id)) return;
            Hero issuer = ReignActionService.HeroById(work.Value<string>("speakerId"));
            Hero target = ReignActionService.HeroById(work.Value<string>("recipientId"));
            string destination = work.Value<string>("destinationId") ?? "";
            double stay = work.Value<double?>("stayDays") ?? 0;
            string reservation = work.Value<string>("hostReservationHash") ?? "";
            string problem = reservation.Length > 0 && (!_communicationHosts.TryGetValue(id, out var held) || held.Hash != reservation
                    || held.HostId != issuer?.StringId || held.TargetId != target?.StringId || held.DestinationId != destination)
                ? "The accepted invitation has no matching native host reservation."
                : !ReignNpcLivesPatches.VerifyReady() ? "The native movement safety hooks are unavailable."
                : !ReignServerClient.NpcLivesOptions.Allows("meeting") ? "Summoned meetings are disabled."
                : issuer == Hero.MainHero || target == Hero.MainHero ? "Background NPC summons cannot control the player hero."
                : !CanSummon(issuer, target) ? "The issuer lacks current authority or a participant is unavailable."
                : !AtMeetingPlace(issuer, destination) || issuer.PartyBelongedTo?.Army != null ? "The host must be safely waiting at the agreed fixed meeting place."
                : SoloDepartureProblem(target).Length > 0 ? SoloDepartureProblem(target)
                : target.PartyBelongedTo?.Army != null ? "The attendee's army duties require a separately agreed release."
                : !SafeMeetingPlace(issuer, destination) || !SafeMeetingPlace(target, destination) ? "The fixed meeting place is unsafe."
                : stay < .25 || stay > 31.5 || double.IsNaN(stay) || double.IsInfinity(stay) ? "The agreed meeting duration is invalid."
                : _meetingTravel.Values.Any(m => !m.Terminal && (m.Meeting.TargetId == issuer.StringId
                    || m.Meeting.IssuerId == target.StringId || m.Meeting.TargetId == target.StringId
                    || m.Meeting.IssuerId == issuer.StringId && m.Meeting.DestinationId != destination))
                    ? "A participant already has an incompatible meeting commitment." : "";
            if (problem.Length > 0)
            {
                var blocked = ReignServerClient.CaptureNpcLivesContext(Generation);
                blocked["communicationId"] = id; blocked["acceptanceHash"] = acceptance;
                blocked["kind"] = "summons_blocked"; blocked["reason"] = problem;
                QueueLifeOperation("communication_receipt", blocked, id + ":blocked"); return;
            }
            Settlement place = Settlement.Find(destination);
            // The original native meeting owns elapsed solo travel, protected party
            // camps, real arrival, the full agreed stay, and restoration afterward.
            var origin = target.PartyBelongedTo?.Position ?? target.CurrentSettlement?.GatePosition
                ?? target.HomeSettlement?.GatePosition ?? MobileParty.MainParty.Position;
            double speed = target.PartyBelongedTo?.Speed ?? 4d;
            if (!NpcLivesPolicy.Finite(speed) || speed <= 0) speed = 4d;
            double normal = AtMeetingPlace(target, destination) ? 0
                : (FieldMeetingPoint(destination, out var point) ? origin.Distance(point) : origin.Distance(place.GatePosition)) / (speed * 24d);
            double delay = NpcLivesPolicy.SummonsArrivalDelay(normal, ReignServerClient.NpcLivesOptions);
            var state = new NpcSummonedMeeting {
                Id = "meeting_" + id, ProposalId = id, Revision = work.Value<int?>("revision") ?? 1,
                IssuerId = issuer.StringId, TargetId = target.StringId, DestinationId = destination,
                Purpose = work.Value<string>("purpose") ?? "Meeting", StayDays = stay, SoloAttendance = true
            };
            if (!state.ScheduleArrival(CampaignTime.Now.ToDays, delay)) return;
            var journey = new NpcMeetingJourney { OperationId = id, AgreementId = id, Meeting = state,
                CommunicationId = id, CommunicationAcceptanceHash = acceptance };
            _meetingTravel.Add(id, journey); CaptureMeetingDuty(issuer);
            // The accepted journey takes over the original duty lease without
            // briefly restoring native movement between invitation and meeting.
            _communicationHosts.Remove(id);
            QueueMeetingReceipt(journey, "meeting_agreed", acceptance, id + ":agreed");
            try { BeginSoloAttendance(journey, target); HoldMeetingHero(issuer, destination); }
            catch (Exception ex) { InterruptMeeting(journey, "The attendee could not depart safely: " + ex.Message); RestoreReleasedMeetingDuties(CampaignTime.Now.ToDays); }
        }
    }
}
