using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public bool CallResolutionVote(string resolutionId, Hero actor, out string result)
        {
            result = string.Empty;
            ImportSeasonalBusiness();
            var hearing = _business.FirstOrDefault(x => x.Kind == "seasonal" && Same(x.TargetId, resolutionId));
            if (hearing != null) return VoteBusiness(hearing.BusinessId, actor, out result);
            ReignGovernmentResolutionRecord record = _resolutions.FirstOrDefault(x => Same(x.ResolutionId, resolutionId));
            ReignGovernmentResolutionTemplate template = ReignGovernmentResolutionCatalog.Find(record?.TemplateId);
            Kingdom kingdom = FindKingdom(record?.KingdomStringId);
            if (record == null || template == null || kingdom == null || !Same(record.Status, "debate"))
            {
                result = "That resolution is not awaiting a government vote.";
                return false;
            }
            if (actor == null || actor != kingdom.Leader)
            {
                result = "Only the ruler may call the recorded final vote.";
                return false;
            }

            ReignGovernmentPartyRecord proposingParty = FindParty(kingdom.StringId, record.PartyId);
            Hero proposingSpeaker = FindHero(record.SpeakerHeroStringId);
            List<ReignGovernmentSeatRecord> seats = GetSeats(kingdom.StringId).ToList();
            List<ReignGovernmentPlank> supported = template.SupportingPlanks.ToList();
            var votes = new JArray();
            int votesFor = 0;
            foreach (ReignGovernmentSeatRecord seat in seats)
            {
                ReignGovernmentPartyRecord ownParty = FindParty(kingdom.StringId, seat.PartyId);
                List<ReignGovernmentPlank> ownPlanks = ParsePlanks(ownParty?.PlanksCsv);
                int shared = ownPlanks.Count(supported.Contains);
                bool ownSpeakerSupports = Same(ownParty?.PartyId, proposingParty?.PartyId) || shared > 0;
                Hero member = FindHero(seat.HeroStringId);
                Hero ownSpeaker = FindHero(ownParty?.SpeakerHeroStringId);
                int lobbyingShift = _lobbyRecords.Where(x => Same(x.ResolutionId, record.ResolutionId)
                        && Same(x.MemberHeroStringId, seat.HeroStringId) && x.Succeeded && x.ExpiresDay >= CurrentDay())
                    .Sum(x => x.PositionShift);
                ReignGovernmentVoteResult vote = ReignGovernmentRules.EvaluateResolutionVote(
                    new ReignGovernmentResolutionVoteInput
                    {
                        IsProposingPartyMember = Same(seat.PartyId, record.PartyId),
                        SharedPlankCount = shared,
                        GovernmentLoyalty = seat.GovernmentLoyalty,
                        PartyLoyalty = seat.PartyLoyalty,
                        OwnSpeakerSupports = ownSpeakerSupports,
                        ProposingSpeakerCharm = proposingSpeaker?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                        OwnSpeakerCharm = ownSpeaker?.GetSkillValue(DefaultSkills.Charm) ?? 0,
                        ProposingSpeakerRelation = member == null || proposingSpeaker == null
                            ? 0 : member.GetRelation(proposingSpeaker),
                        LobbyingShift = Clamp(lobbyingShift, -30, 30),
                        StableVariance = ReignGovernmentRules.StableVariance(seat.HeroStringId, record.ResolutionId)
                    });
                if (vote.Supports) votesFor++;
                votes.Add(new JObject
                {
                    ["memberHeroId"] = seat.HeroStringId,
                    ["partyId"] = seat.PartyId,
                    ["score"] = vote.Score,
                    ["supports"] = vote.Supports,
                    ["lobbyingShift"] = Clamp(lobbyingShift, -30, 30)
                });
            }

            bool adopted = ReignGovernmentRules.ResolutionAdopted(votesFor, seats.Count);
            JObject evidence;
            try { evidence = JObject.Parse(record.EvidenceJson ?? "{}"); }
            catch { evidence = new JObject(); }
            evidence["vote"] = new JObject
            {
                ["votesFor"] = votesFor,
                ["occupiedSeats"] = seats.Count,
                ["adopted"] = adopted,
                ["members"] = votes
            };
            record.EvidenceJson = evidence.ToString(Newtonsoft.Json.Formatting.None);
            record.Status = adopted ? "proposed" : "rejected";
            record.Outcome = adopted
                ? "The government adopted the resolution after individual member voting."
                : "The government rejected the resolution after individual member voting.";
            if (!adopted) record.ResolvedDay = CurrentDay();
            record.Revision++;

            foreach (ReignGovernmentPartyRecord party in GetParties(kingdom.StringId))
            {
                int partyFor = votes.OfType<JObject>().Count(x => Same(x.Value<string>("partyId"), party.PartyId)
                    && x.Value<bool>("supports"));
                int partyTotal = votes.OfType<JObject>().Count(x => Same(x.Value<string>("partyId"), party.PartyId));
                party.LastStatement = party.Name + (partyFor * 2 > partyTotal ? " supports " : " opposes ")
                    + template.Title + " after its members' final vote.";
                party.LastStatementDay = CurrentDay();
                party.Revision++;
            }
            ResolveVoteLobbyRecords(record.ResolutionId);
            result = adopted
                ? "The resolution was adopted " + votesFor + " to " + seats.Count + "."
                : "The resolution was rejected " + votesFor + " to " + seats.Count + ".";
            return adopted;
        }

        public bool AttemptLobbying(
            string resolutionId,
            string memberHeroStringId,
            string method,
            string terms,
            out string result)
        {
            result = "Political persuasion, payments, and personal deals must be discussed in an individual conversation with the member.";
            return false;
        }

        public bool FulfillLobbyDeal(string lobbyId, out string result)
        {
            result = "Private obligations require verified fulfillment through their actual agreed action.";
            return false;
        }

        private void EvaluateLobbyRecords(Kingdom kingdom, ReignGovernmentStateRecord state)
        {
            foreach (ReignGovernmentLobbyRecord lobby in _lobbyRecords.Where(x => Same(x.KingdomStringId, kingdom.StringId)
                && Same(x.Method, "deal") && x.Succeeded && !x.Fulfilled && x.ExpiresDay >= 0f && CurrentDay() > x.ExpiresDay).ToList())
            {
                Hero member = FindHero(lobby.MemberHeroStringId);
                ApplyRelation(kingdom.Leader, member, -12);
                ReignGovernmentSeatRecord seat = _seats.FirstOrDefault(x => Same(x.KingdomStringId, kingdom.StringId)
                    && Same(x.HeroStringId, lobby.MemberHeroStringId));
                if (seat != null)
                {
                    seat.GovernmentLoyalty = Clamp(seat.GovernmentLoyalty - 15, 0, 100);
                    seat.Revision++;
                }
                state.Revision++;
                lobby.PositionShift = 0;
                lobby.Revision++;
            }
        }

        private void ResolveVoteLobbyRecords(string resolutionId)
        {
            foreach (ReignGovernmentLobbyRecord lobby in _lobbyRecords.Where(x => Same(x.ResolutionId, resolutionId)))
            {
                if (!Same(lobby.Method, "deal")) lobby.Fulfilled = true;
                lobby.PositionShift = 0;
                lobby.Revision++;
            }
        }

        private static int BribeCost(ReignGovernmentResolutionScale scale, int governmentLevel)
        {
            int baseCost = scale == ReignGovernmentResolutionScale.Minor ? 5000
                : scale == ReignGovernmentResolutionScale.Standard ? 12500 : 30000;
            return (int)Math.Round(baseCost * (1d + 0.25d * Clamp(governmentLevel - 1, 0, 4)),
                MidpointRounding.AwayFromZero);
        }
    }
}
