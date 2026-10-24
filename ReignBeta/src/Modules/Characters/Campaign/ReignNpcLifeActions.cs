using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
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
        internal bool CanBuildLifeAction(string action, Hero actor, Hero counterpart) => actor != null && actor != Hero.MainHero
            && (MeetingControl(action) ? MeetingOptions(actor.StringId).Count > 0
                : action == "npc_life.summon_meeting" && ReignServerClient.NpcLivesOptions.Allows("meeting")
                && (actor.Clan?.Leader == actor || actor.Clan?.Kingdom?.Leader == actor) && TogetherForMeeting(actor, counterpart));
        private static bool HasMeetingAuthority(Hero issuer, Hero target) => issuer != null && target != null && issuer != target
            && (issuer.Clan?.Leader == issuer && target.Clan == issuer.Clan
                || issuer.Clan?.Kingdom?.Leader == issuer && target.Clan?.Kingdom == issuer.Clan.Kingdom);
        internal static bool CanSummon(Hero issuer, Hero target) => HasMeetingAuthority(issuer, target)
            && MeetingParticipantBlockReason(issuer).Length == 0 && MeetingParticipantBlockReason(target).Length == 0;
        private static string MeetingParticipantBlockReason(Hero hero) => NpcLivesPolicy.MeetingParticipantBlockReason(
            hero != null, hero?.IsAlive == true, hero?.IsChild == false, hero?.IsPrisoner == true,
            hero?.IsActive == true || hero?.IsTraveling == true,
            hero?.PartyBelongedTo == null || hero.PartyBelongedTo.IsActive && !hero.PartyBelongedTo.IsDisbanding);
        private static string MeetingParticipantProblem(Hero hero, string role)
        {
            string reason = MeetingParticipantBlockReason(hero);
            return reason.Length == 0 ? "" : (hero?.Name?.ToString() ?? role) + " " + reason + ".";
        }
        private static double LifeNumber(ActionClause c, string key, double fallback) => double.TryParse(ReignActionService.Arg(c, key),
            NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : fallback;
        internal void BindMeetingArrival(ActionAgreement agreement)
        {
            if (agreement == null || agreement.PlayerAcceptedHash.Length > 0 || agreement.NpcAcceptedHash.Length > 0) return;
            foreach (var clause in agreement.Clauses.Where(c => c.ActionId == "npc_life.summon_meeting" || c.ActionId == "npc_life.change_meeting"))
            {
                var retained = clause.ActionId == "npc_life.change_meeting" ? _meetingTravel.Values.FirstOrDefault(j => !j.Terminal
                    && j.Meeting.Id == ReignActionService.Arg(clause, "meetingId")) : null;
                clause.Arguments["attendanceMode"] = retained == null || retained.Meeting.SoloAttendance ? "solo_camp" : "legacy_party";
                if (clause.Arguments.ContainsKey("arrivalDays")) continue;
                string targetId = ReignActionService.Arg(clause, "targetHeroId");
                if (clause.ActionId == "npc_life.change_meeting") targetId = _meetingTravel.Values.FirstOrDefault(j => !j.Terminal
                    && j.Meeting.Id == ReignActionService.Arg(clause, "meetingId"))?.Meeting.TargetId;
                Hero target = ReignActionService.HeroById(targetId);
                string destination = ReignActionService.Arg(clause, "destinationId");
                Settlement place = Settlement.Find(destination);
                if (target == null || place == null && !FieldMeetingPoint(destination, out _)) continue;
                double normal = 0;
                if (!AtMeetingPlace(target, destination))
                {
                    var origin = target.PartyBelongedTo?.Position ?? target.CurrentSettlement?.GatePosition ?? target.HomeSettlement?.GatePosition ?? MobileParty.MainParty.Position;
                    double speed = target.PartyBelongedTo?.Speed ?? 4d;
                    if (!NpcLivesPolicy.Finite(speed) || speed <= 0) speed = 4d;
                    normal = (FieldMeetingPoint(destination, out var point) ? origin.Distance(point) : origin.Distance(place.GatePosition)) / (speed * 24d);
                }
                clause.Arguments["arrivalDays"] = NpcLivesPolicy.SummonsArrivalDelay(normal, ReignServerClient.NpcLivesOptions).ToString("R", CultureInfo.InvariantCulture);
            }
        }
        internal IReadOnlyList<string> CheckLifeAction(ActionAgreement a, ActionClause c)
        {
            var errors = new List<string>();
            if (!_initialized || Generation.Length == 0) { errors.Add("NPC Lives information is still loading."); return errors; }
            if (c.ActorId != a.NpcId || c.RecipientId != a.PlayerId) errors.Add("This request belongs to the addressed NPC and player.");
            if (c.Timing != TermTiming.Immediate || c.Installments != 1 || c.DurationDays != 0 || c.DueDay != 0)
                errors.Add("Personal matters use their own explicit departure, arrival and duration terms.");
            if ((c.ActionId == "npc_life.summon_meeting" || MeetingControl(c.ActionId)) && a.Clauses.Count != 1)
                errors.Add("A meeting and its revisions need a separate agreement so movement holds can be released precisely.");
            if (c.ActionId == "npc_life.summon_meeting" || c.ActionId == "npc_life.change_meeting")
            {
                double arrival = LifeNumber(c, "arrivalDays", double.NaN);
                if (!NpcLivesPolicy.Finite(arrival) || arrival < 0 || arrival > 126) errors.Add("Review a finite arrival wait from zero to 126 days before agreeing.");
            }
            if (MeetingControl(c.ActionId)) { CheckMeetingControl(a, c, errors); return errors; }
            if (c.ActionId == "npc_life.summon_meeting")
            {
                Hero issuer = ReignActionService.HeroById(c.ActorId), target = ReignActionService.HeroById(ReignActionService.Arg(c, "targetHeroId"));
                Settlement destination = Settlement.Find(ReignActionService.Arg(c, "destinationId"));
                foreach (string problem in new[] { MeetingParticipantProblem(issuer, "The issuer"), MeetingParticipantProblem(target, "The summoned person") })
                    if (problem.Length > 0) errors.Add(problem);
                if (!HasMeetingAuthority(issuer, target)) errors.Add("Only the target's own clan leader or ruler can summon them.");
                if (!MeetingPlaceOptions(c).Any(o => o.Id == ReignActionService.Arg(c, "destinationId")))
                    errors.Add("Choose the current settlement or fixed field rendezvous where you and the leader are meeting.");
                if (destination?.IsUnderSiege == true) errors.Add("This meeting place is currently under siege.");
                double duration = LifeNumber(c, "stayDays", 3);
                if (!NpcLivesPolicy.Finite(duration) || duration < .25 || duration > 31.5) errors.Add("Choose a stay of 0.25–31.5 days after arrival.");
                if (issuer?.PartyBelongedTo?.Army != null) errors.Add("The issuer must first leave army duties to promise this stay.");
                if (target?.PartyBelongedTo?.Army != null && (ReignActionService.Arg(c, "releaseArmy") != "yes"
                    || !CanReleaseMeetingArmy(issuer, target))) errors.Add("This person needs an explicitly authorized release from army duties.");
                if (ReignActionService.Arg(c, "attendanceMode") == "solo_camp")
                {
                    string departure = SoloDepartureProblem(target);
                    if (departure.Length > 0) errors.Add(departure);
                }
                foreach (string hero in new[] { c.ActorId, target?.StringId ?? "" })
                    if (_meetingTravel.Values.Any(m => !m.Terminal && (m.Meeting.IssuerId == hero || m.Meeting.TargetId == hero)
                        && m.Meeting.DestinationId != ReignActionService.Arg(c, "destinationId"))) errors.Add("This person already has a meeting at a different destination.");
                if (ReignActionService.Arg(c, "purpose").Trim().Length == 0) errors.Add("Specify the purpose of the meeting.");
            }
            else
            {
                string id = ReignActionService.Arg(c, "matterId");
                int revision = (int)LifeNumber(c, "revision", -1);
                if (c.ActionId == "npc_life.accept_story")
                {
                    var story = State.Stories.FirstOrDefault(s => s.Id == id && s.HeroId == c.ActorId && s.Status == "invited");
                    if (story == null || story.Revision != revision) errors.Add("The personal-story offer has changed.");
                    if (story != null && !ReignServerClient.NpcLivesOptions.Allows(story.Definition.Category)) errors.Add("New personal stories of this kind are disabled.");
                }
                else if (c.ActionId == "npc_life.personal_leave")
                {
                    var leave = State.Leaves.FirstOrDefault(l => l.Id == id && l.HeroId == c.ActorId && l.Status == "discussing");
                    if (leave == null || leave.Revision != revision) errors.Add("The personal-leave offer has changed.");
                    double wait = LifeNumber(c, "waitDays", double.NaN), away = LifeNumber(c, "awayDays", double.NaN);
                    if (!NpcLivesPolicy.Finite(wait) || wait < 0 || wait > 31.5 || !NpcLivesPolicy.Finite(away) || away < .25 || away > 31.5)
                        errors.Add("Choose separate finite terms for the wait and time away.");
                }
                else errors.Add("Unknown personal action.");
            }
            return errors;
        }
        internal string LifeActionSentence(ActionAgreement a, ActionClause c)
        {
            string actor = ReignActionService.HeroById(c.ActorId)?.Name?.ToString() ?? "the NPC";
            if (MeetingControl(c.ActionId))
            {
                var meeting = _meetingTravel.Values.FirstOrDefault(j => !j.Terminal && j.Meeting.Id == ReignActionService.Arg(c, "meetingId"))?.Meeting;
                string purpose = meeting?.Purpose ?? ReignActionService.Arg(c, "meetingId");
                if (c.ActionId == "npc_life.cancel_meeting")
                    return "We agree to cancel the meeting about " + purpose + " and release its remaining commitments";
                string destination = ReignActionService.Arg(c, "destinationId");
                bool samePlace = meeting != null && meeting.DestinationId == destination;
                if (samePlace && meeting.Status == "meeting" && meeting.ArrivalDay.HasValue
                    && AtMeetingPlace(ReignActionService.HeroById(meeting.IssuerId), destination)
                    && AtMeetingPlace(ReignActionService.HeroById(meeting.TargetId), destination))
                    return "We agree to revise the meeting about " + purpose + " at " + MeetingPlaceName(destination)
                        + ", keeping the agreed attendance arrangement; both stay "
                        + LifeNumber(c, "stayDays", 3).ToString("0.##", CultureInfo.InvariantCulture)
                        + " days in total from their original actual arrival";
                return "We agree to " + (samePlace ? "revise" : "move") + " the meeting about " + purpose
                        + (samePlace ? " at " : " to ") + MeetingPlaceName(destination)
                        + "; the summoned person will arrive in " + LifeNumber(c, "arrivalDays", 0).ToString("0.##", CultureInfo.InvariantCulture)
                        + (ReignActionService.Arg(c, "attendanceMode") == "solo_camp"
                            ? " days, attending alone; any accompanying party stays at its original camp, and the summoned person returns to their starting place afterward; both stay "
                            : " days with their party if they have one, and both stay ")
                        + LifeNumber(c, "stayDays", 3).ToString("0.##", CultureInfo.InvariantCulture) + " days from their actual arrival";
            }
            if (c.ActionId == "npc_life.summon_meeting")
            {
                string target = ReignActionService.HeroById(ReignActionService.Arg(c, "targetHeroId"))?.Name?.ToString() ?? "the summoned person";
                string place = MeetingPlaceName(ReignActionService.Arg(c, "destinationId"));
                return "You will summon " + target + " to " + place + " for " + ReignActionService.Arg(c, "purpose")
                    + "; they will arrive in " + LifeNumber(c, "arrivalDays", 0).ToString("0.##", CultureInfo.InvariantCulture)
                    + (ReignActionService.Arg(c, "attendanceMode") == "solo_camp"
                        ? " days, attending alone; any accompanying party stays at its original camp, and the summoned person returns to their starting place afterward; you will wait here, and both remain for "
                        : " days with their party if they have one, you will wait here, and both remain for ")
                    + LifeNumber(c, "stayDays", 3).ToString("0.##", CultureInfo.InvariantCulture)
                    + " days after their actual arrival" + (ReignActionService.Arg(c, "releaseArmy") == "yes" ? ", releasing them from army duties within your authority" : "");
            }
            string id = ReignActionService.Arg(c, "matterId");
            if (c.ActionId == "npc_life.personal_leave")
                return actor + " will wait " + LifeNumber(c, "waitDays", 0).ToString("0.##", CultureInfo.InvariantCulture)
                    + " days before taking personal leave, then spend " + LifeNumber(c, "awayDays", 5).ToString("0.##", CultureInfo.InvariantCulture)
                    + " days away before returning to this party; the absence begins on actual departure";
            var story = State.Stories.FirstOrDefault(s => s.Id == id);
            return "We agree to share this personal experience: " + NpcLivesPolicy.StoryActionTitle(story, id);
        }
        internal async Task PrepareLifeConversationAsync(Hero hero, string session, JObject context)
        {
            JObject p = null; TaleWorlds.CampaignSystem.Campaign owner = null; int epoch = 0;
            await ReignMainThread.InvokeAsync(() => { p = ReignServerClient.CaptureNpcLivesContext(Generation);
                owner = TaleWorlds.CampaignSystem.Campaign.Current; epoch = _epoch; }).ConfigureAwait(false);
            var response = await ReignServerClient.NpcLivesRequestAsync("state", p).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => {
                if (!Current(owner, epoch) || response.Value<bool?>("ok") != true) return;
                var snapshot = response["state"]?.ToObject<NpcLivesState>();
                if (snapshot == null || snapshot.TimelineId != _timeline) return;
                string generation = response.Value<string>("generation") ?? "";
                if (generation != Generation || snapshot.Revision >= State.Revision) State = snapshot;
                Generation = generation; _initialized = true;
                var story = State.Stories.FirstOrDefault(s => s.HeroId == hero.StringId && s.Status == "invited");
                var leave = State.Leaves.FirstOrDefault(l => l.HeroId == hero.StringId && l.Status == "discussing");
                if (story == null && leave == null) return;
                string id = story?.Id ?? leave.Id, proposal = story?.ProposalId ?? leave.ProposalId;
                int revision = story?.Revision ?? leave.Revision;
                var clause = new ActionClause { Id = proposal + "_term", ActionId = story != null ? "npc_life.accept_story" : "npc_life.personal_leave",
                    ActorId = hero.StringId, RecipientId = Hero.MainHero.StringId };
                clause.Arguments["matterId"] = id; clause.Arguments["revision"] = revision.ToString(CultureInfo.InvariantCulture);
                if (leave != null) { clause.Arguments["waitDays"] = "0"; clause.Arguments["awayDays"] = leave.AbsenceDays.ToString(CultureInfo.InvariantCulture); }
                var offer = new ActionAgreement { Id = proposal, Revision = revision, PlayerId = Hero.MainHero.StringId, NpcId = hero.StringId,
                    ConversationId = ReignActionConversationPolicy.ConversationContextId(_campaign, _timeline, hero.StringId),
                    ProposerId = hero.StringId, Origin = ActionOrigin.Conversation, CreatedDay = CampaignTime.Now.ToDays,
                    ExpiresDay = 0, Clauses = new List<ActionClause> { clause } };
                ReignAgreementBehavior.Instance?.RegisterNpcLifeOffer(offer);
                context["npcLifeRequestId"] = id; context["npcLivesRevision"] = revision;
                if (story != null) context["npcStoryId"] = story.Id;
            }).ConfigureAwait(false);
        }
        internal void DeclineLifeOffer(ActionAgreement a)
        {
            foreach (var c in a.Clauses.Where(c => c.ActionId == "npc_life.accept_story" || c.ActionId == "npc_life.personal_leave"))
                QueueLifeChoice(a, c, a.Id + ":decline:" + a.Revision, "decline");
        }
        private void QueueLifeChoice(ActionAgreement a, ActionClause c, string key, string choice)
        {
            string id = ReignActionService.Arg(c, "matterId");
            string proposal = State.Stories.FirstOrDefault(s => s.Id == id)?.ProposalId ?? State.Leaves.FirstOrDefault(l => l.Id == id)?.ProposalId ?? "";
            var p = ReignServerClient.CaptureNpcLivesContext(Generation);
            p["id"] = id; p["heroId"] = c.ActorId; p["revision"] = (int)LifeNumber(c, "revision", -1); p["choice"] = choice;
            p["proposalId"] = proposal; p["agreementHash"] = a.TermsHash(); p["waitDays"] = LifeNumber(c, "waitDays", 0);
            p["awayDays"] = LifeNumber(c, "awayDays", 5);
            QueueLifeOperation("decision", p, key);
        }
        internal void ApplyLifeAction(ActionAgreement a, ActionClause c, string key)
        {
            if (!a.HasRequiredConsents) throw new InvalidOperationException("Both participants must accept the exact personal-action terms.");
            var errors = CheckLifeAction(a, c); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            if (MeetingControl(c.ActionId)) ApplyMeetingControl(a, c, key);
            else if (c.ActionId == "npc_life.summon_meeting") StartSummonedMeeting(a, c, key);
            else QueueLifeChoice(a, c, key, "accept");
        }
        internal bool ObserveLifeAction(ActionAgreement a, ActionClause c, string key)
        {
            if (_failedOperations.TryGetValue(key, out string error)) throw new InvalidOperationException(error);
            if (c.ActionId != "npc_life.summon_meeting" && c.ActionId != "npc_life.change_meeting") return _acceptedOperations.Contains(key);
            if (!_meetingTravel.TryGetValue(key, out var journey)) return false;
            if (journey.ControlAgreementId.Length > 0) return false;
            if (journey.Terminal && journey.Meeting.Status != "completed")
                throw new InvalidOperationException("The meeting was interrupted: " + journey.Meeting.Interruption);
            return journey.Meeting.Status == "completed";
        }
    }
}
