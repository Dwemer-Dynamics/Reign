using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Actions;
using Reign.Core.Contracts.Government;
using ReignBeta.Actions;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        public static string PetitionOperation(string actionId)
        {
            switch (actionId)
            {
                case "government.endorse_petition": return "endorse";
                case "government.sponsor_petition": return "sponsor";
                case "government.seek_petition_adoption": return "seek_adoption";
                case "government.present_petition": return "present";
                case "government.transfer_mandate": return "transfer";
                default: return "";
            }
        }
        internal bool IsParticipationAgreement(ActionClause clause) => PetitionOperation(clause.ActionId).Length > 0
            || clause.ActionId == "government.nominate_leader" || clause.ActionId == "government.vote_leader";

        internal List<string> CheckParticipationAgreement(ActionClause clause)
        {
            string operation = PetitionOperation(clause.ActionId), reason;
            var actor = FindHero(clause.ActorId); var counterpart = FindHero(clause.RecipientId);
            if (operation.Length > 0)
            {
                if (!long.TryParse(ReignActionService.Arg(clause, "petitionRevision"), out long revision)) return new List<string> { "The exact petition revision is required." };
                return CheckPetitionAction(operation, ReignActionService.Arg(clause, "petitionId"), revision, actor, counterpart, out reason) ? new List<string>() : new List<string> { reason };
            }
            var seat = _seats.FirstOrDefault(x => x.HeroStringId == clause.ActorId);
            string candidateId = ReignActionService.Arg(clause, "candidateId");
            if (seat == null || !_seats.Any(x => x.KingdomStringId == seat.KingdomStringId && x.PartyId == seat.PartyId && x.HeroStringId == candidateId))
                return new List<string> { "The actor and candidate must be members of the same party." };
            if (ReignActionService.Arg(clause, "electionId") != LeadershipElectionId(seat.KingdomStringId, seat.PartyId))
                return new List<string> { "This promise belongs to a different party election. Select the current contest again." };
            if (clause.ActionId == "government.nominate_leader" && !CanNominatePartyLeader(seat.KingdomStringId, candidateId, actor, out reason))
                return new List<string> { reason };
            if (clause.ActionId == "government.vote_leader")
            {
                var election = _participation.Elections.FirstOrDefault(x => x.KingdomId == seat.KingdomStringId && x.PartyId == seat.PartyId && !x.Closed);
                if (election == null || !election.Opened || !election.Roll.Contains(clause.ActorId) || !election.Nominations.ContainsKey(candidateId)
                    || election.Nominations[candidateId].Distinct().Count() < (election.Roll.Count == 1 ? 1 : 2))
                    return new List<string> { "The candidate needs valid nominations in an open election whose roll includes this member." };
            }
            return new List<string>();
        }
        private void ApplyParticipationAgreement(string agreementId, ActionClause clause)
        {
            if (_participation.Petitions.Any(p => p.Consents.Any(c => c.ReceiptId == agreementId + ":" + clause.Id
                && c.ActorId == clause.ActorId && c.Operation == PetitionOperation(clause.ActionId)))) return;
            string receipt = agreementId + ":" + clause.Id;
            string service = clause.ActionId + "|" + clause.ActorId + "|" + ReignActionService.Arg(clause, "candidateId");
            if (_participation.Elections.Any(x => x.Id == ReignActionService.Arg(clause, "electionId")
                && x.ActionReceipts.TryGetValue(receipt, out string prior) && prior == service)) return;
            var errors = CheckParticipationAgreement(clause); if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
            string operation = PetitionOperation(clause.ActionId), reason;
            var actor = FindHero(clause.ActorId); bool success;
            if (operation.Length > 0) success = ApplyPetitionAction(operation, ReignActionService.Arg(clause, "petitionId"),
                long.Parse(ReignActionService.Arg(clause, "petitionRevision")), actor, FindHero(clause.RecipientId), agreementId + ":" + clause.Id, out reason);
            else
            {
                var seat = _seats.First(x => x.HeroStringId == clause.ActorId);
                success = clause.ActionId == "government.nominate_leader"
                    ? NominatePartyLeader(seat.KingdomStringId, ReignActionService.Arg(clause, "candidateId"), actor, out reason)
                    : VotePartyLeader(seat.KingdomStringId, ReignActionService.Arg(clause, "candidateId"), actor, out reason);
            }
            if (!success) throw new InvalidOperationException(reason);
            if (operation.Length == 0)
                _participation.Elections.Single(x => x.Id == ReignActionService.Arg(clause, "electionId")).ActionReceipts[receipt] = service;
        }
        private bool ObserveParticipationAgreement(ActionClause clause)
        {
            string operation = PetitionOperation(clause.ActionId);
            if (operation.Length > 0)
            {
                var petition = _participation.Petitions.FirstOrDefault(x => x.Id == ReignActionService.Arg(clause, "petitionId"));
                if (petition == null || petition.Revision.ToString() != ReignActionService.Arg(clause, "petitionRevision")) throw new InvalidOperationException("The agreed petition revision no longer exists.");
                bool consent = petition.Consents.Any(x => x.ActorId == clause.ActorId && x.Operation == operation && x.Revision == petition.Revision);
                if (operation == "present")
                {
                    var business = _business.FirstOrDefault(x => x.BusinessId == petition.BusinessId);
                    if (GovernmentParticipationRules.Closed(petition.Status) && (business == null || business.FrozenAuthorityLevel <= 0)) throw new InvalidOperationException("The petition closed without presentation.");
                    return consent && business?.FrozenAuthorityLevel > 0;
                }
                // Sponsorship and seeking adoption are their own verifiable service. They
                // never promise admission, caucus endorsement or a successful public vote.
                return consent;
            }
            var election = _participation.Elections.SingleOrDefault(x => x.Id == ReignActionService.Arg(clause, "electionId")
                && x.Roll.Contains(clause.ActorId) && x.Nominations.ContainsKey(ReignActionService.Arg(clause, "candidateId")));
            return election != null && (clause.ActionId == "government.nominate_leader"
                ? election.Nominations[ReignActionService.Arg(clause, "candidateId")].Contains(clause.ActorId)
                : election.Ballots.TryGetValue(clause.ActorId, out string vote) && vote == ReignActionService.Arg(clause, "candidateId"));
        }

        public void BindPetitionRevisions(ActionAgreement agreement)
        {
            foreach (var clause in agreement.Clauses.Where(x => PetitionOperation(x.ActionId).Length > 0 || x.ActionId == "government.support_nomination"))
            {
                if (clause.Arguments.ContainsKey("petitionRevision")) continue;
                var petition = _participation.Petitions.FirstOrDefault(x => x.Id == ReignActionService.Arg(clause, "petitionId"));
                if (petition != null) clause.Arguments["petitionRevision"] = petition.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            foreach (var clause in agreement.Clauses.Where(x => x.ActionId == "government.support_nomination" && !x.Arguments.ContainsKey("caucusId")))
            {
                var caucus = _participation.Caucuses.FirstOrDefault(x => !x.Closed && x.Roll.Contains(clause.ActorId)
                    && x.Nominations.Contains(ReignActionService.Arg(clause, "petitionId")) && CurrentCaucusMembers(x).Contains(clause.ActorId));
                if (caucus != null) clause.Arguments["caucusId"] = GovernmentParticipationRules.CaucusId(caucus);
            }
            foreach (var clause in agreement.Clauses.Where(x => x.ActionId == "government.nominate_leader" || x.ActionId == "government.vote_leader"))
            {
                if (clause.Arguments.ContainsKey("electionId")) continue;
                var seat = _seats.FirstOrDefault(x => x.HeroStringId == clause.ActorId);
                if (seat != null && !string.IsNullOrEmpty(seat.PartyId))
                    clause.Arguments["electionId"] = LeadershipElectionId(seat.KingdomStringId, seat.PartyId);
            }
        }
        public string GovernmentActionSentence(ActionClause clause, string subject)
        {
            string operation = PetitionOperation(clause.ActionId);
            if (operation.Length > 0)
            {
                var petition = _participation.Petitions.FirstOrDefault(x => x.Id == ReignActionService.Arg(clause, "petitionId"));
                string verb = operation == "endorse" ? "endorse " : operation == "sponsor" ? "personally sponsor " : operation == "seek_adoption" ? "seek government adoption of "
                    : operation == "present" ? "present the adopted petition for " : "consent to the civic mandate transfer described in ";
                return subject + verb + (petition?.Title ?? "the unavailable petition") + " in " + (FindKingdom(petition?.KingdomId)?.Name?.ToString() ?? "its named kingdom");
            }
            if (clause.ActionId == "government.support_vote")
            {
                var business = _business.FirstOrDefault(x => x.BusinessId == ReignActionService.Arg(clause, "businessId"));
                string option = business == null ? "the recorded option" : BusinessOptions(business).FirstOrDefault(x => x.Value<string>("id") == ReignActionService.Arg(clause, "optionId"))?.Value<string>("label");
                return subject + "vote for “" + option + "” on “" + business?.Title + "” in " + FindKingdom(business?.KingdomStringId)?.Name;
            }
            if (clause.ActionId == "government.support_nomination")
            {
                var petition = _participation.Petitions.FirstOrDefault(x => x.Id == ReignActionService.Arg(clause, "petitionId"));
                var caucus = _participation.Caucuses.FirstOrDefault(x => GovernmentParticipationRules.CaucusId(x) == ReignActionService.Arg(clause, "caucusId"));
                return subject + "vote to nominate “" + (petition?.Title ?? "the unavailable nomination") + "” for "
                    + (FindParty(caucus?.KingdomId, caucus?.PartyId)?.Name ?? "the named party") + " at its "
                    + (caucus == null ? "unavailable selection" : CaucusSessionLabel(caucus) + " selection");
            }
            if (clause.ActionId == "government.nominate_leader" || clause.ActionId == "government.vote_leader")
                return subject + (clause.ActionId == "government.nominate_leader" ? "nominate " : "vote for ")
                    + FindHero(ReignActionService.Arg(clause, "candidateId"))?.Name + " as our party speaker";
            return "";
        }
        public int PetitionServiceReference(Hero actor, ActionClause clause)
        {
            var petition = _participation.Petitions.FirstOrDefault(p => p.Id == ReignActionService.Arg(clause, "petitionId"));
            return ReignActionQuotes.PoliticalServiceReference(actor, clause.ActionId, FindKingdom(petition?.KingdomId));
        }

        public JObject PetitionNegotiationContext(Hero speaker, Hero counterpart, ActionAgreement offer)
        {
            var beliefs = AppliedPoliticalProfile(speaker?.StringId);
            // Opening a selector is read-only. Only an actually submitted exact offer
            // discloses its petition to this recipient; unrelated drafts stay private.
            foreach (var clause in offer?.Clauses ?? new List<ActionClause>())
            {
                var petition = _participation.Petitions.FirstOrDefault(p => p.Id == ReignActionService.Arg(clause, "petitionId")
                    && p.Revision.ToString() == ReignActionService.Arg(clause, "petitionRevision"));
                if (petition != null && petition.AuthorId == counterpart?.StringId && petition.Status != "draft"
                    && !GovernmentParticipationRules.Closed(petition.Status) && !petition.DisclosedTo.Contains(speaker.StringId))
                    petition.DisclosedTo.Add(speaker.StringId);
            }
            var requestedServices = new JArray();
            foreach (var clause in offer?.Clauses ?? new List<ActionClause>())
            {
                if (clause.ActorId != speaker?.StringId || PetitionOperation(clause.ActionId).Length == 0 && clause.ActionId != "government.support_nomination") continue;
                int reference = PetitionServiceReference(speaker, clause);
                requestedServices.Add(new JObject { ["actionId"] = clause.ActionId,
                    ["petitionId"] = ReignActionService.Arg(clause, "petitionId"),
                    ["petitionRevision"] = ReignActionService.Arg(clause, "petitionRevision"),
                    ["caucusId"] = ReignActionService.Arg(clause, "caucusId"),
                    ["referenceGold"] = reference, ["minimumGold"] = (reference + 1) / 2,
                    ["maximumGold"] = reference * 2 });
            }
            int totalReference = requestedServices.OfType<JObject>().Sum(x => x.Value<int>("referenceGold"));
            var requestedQuote = totalReference == 0 ? null : new JObject {
                ["services"] = requestedServices, ["referenceTotalGold"] = totalReference,
                ["minimumConsiderationGold"] = (totalReference + 1) / 2,
                ["maximumConsiderationGold"] = totalReference * 2,
                ["politicalOnlyNpcSide"] = offer.Clauses.Where(c => c.ActorId == speaker.StringId)
                    .All(c => PetitionOperation(c.ActionId).Length > 0 || c.ActionId == "government.support_nomination") };
            var petitions = _participation.Petitions.Where(x => !GovernmentParticipationRules.Closed(x.Status)
                && GovernmentParticipationRules.CanRead(x, speaker?.StringId) && x.Status != "draft"
                && (x.AuthorId == counterpart?.StringId || offer?.Clauses.Any(c => ReignActionService.Arg(c, "petitionId") == x.Id) == true)).ToList();
            return new JObject { ["instructions"] = "These exact petitions have been disclosed to you. Your private willingness uses alignment +/-40, source-backed conduct +/-15, directional relationship +/-10, new credible argument 0..10, material benefit 0..15 TOTAL per actor/petition, and political cost 0..25. Total >=25 is credible grounds to accept, 5..24 calls for better reasons or terms, below 5 normally means refusal. These are consistency bounds, never automatic consent or chance. Interpret them through your court traits. With unchanged evidence and terms retain your previous assessment; repeated requests or gifts add nothing. Sponsorship is personal support; seeking adoption promises entering selection, not a majority or an appointment. Use explicit structured acceptance. A principled service may be free. Token gifts are not major political benefits: assess the total actual personal benefit against the relevant service reference, never award the full material contribution for a token payment, and never stack repeated gifts. A reference does not authorize an ineligible service or guarantee a seat, adoption or a vote. Initial monetary quotes must be between half and twice the supplied reference. Payments use the named actual recipient and normal verified action completion; there is no party treasury. Never publish scores, wealth or private ideological context.",
                ["requestedPersonalServiceQuote"] = requestedQuote,
                ["nominationVotingRule"] = "Supporting a nomination is a promise to cast your own exact revision vote in the named frozen party selection. It is distinct from personal endorsement, sponsorship, entering selection, and the later public chamber vote. It guarantees no majority. Payment for a final selection vote waits for the actual selection ballot, including when the nomination loses. Resolve a conflicting existing commitment before promising another nomination. Private persuasion occurs between sessions.",
                ["openPartySelections"] = new JArray(_participation.Caucuses.Where(c => !c.Closed && c.Roll.Contains(speaker?.StringId))
                    .SelectMany(c => c.Nominations.Select(id => new { Caucus = c, Petition = _participation.Petitions.FirstOrDefault(p => p.Id == id) }))
                    .Where(x => x.Petition != null && GovernmentParticipationRules.CanRead(x.Petition, speaker?.StringId))
                    .Select(x => new JObject { ["caucusId"] = GovernmentParticipationRules.CaucusId(x.Caucus),
                        ["party"] = FindParty(x.Caucus.KingdomId, x.Caucus.PartyId)?.Name, ["selectionDate"] = CaucusSessionLabel(x.Caucus),
                        ["petitionId"] = x.Petition.Id, ["petitionRevision"] = x.Petition.Revision, ["title"] = x.Petition.Title,
                        ["eligible"] = CheckNominationSupport(speaker?.StringId, x.Petition.Id, x.Petition.Revision, GovernmentParticipationRules.CaucusId(x.Caucus), out _),
                        ["recordedEndorsement"] = x.Caucus.Endorsements.TryGetValue(speaker.StringId, out string choice) && choice == x.Petition.Id
                            && x.Caucus.EndorsementRevisions.TryGetValue(speaker.StringId, out long endorsedRevision) && endorsedRevision == x.Petition.Revision,
                        ["frozenMembers"] = x.Caucus.Roll.Count, ["requiredVotes"] = x.Caucus.Roll.Count / 2 + 1 })),
                ["petitions"] = new JArray(petitions.Select(p => new JObject { ["petitionId"] = p.Id, ["revision"] = p.Revision, ["title"] = p.Title,
                    ["terms"] = p.Terms, ["argument"] = p.Argument, ["status"] = p.Status,
                    ["evidenceIdentity"] = p.Id + ":" + p.Revision + ":" + ReignGovernmentRules.StableHash(p.Argument),
                    ["role"] = GetParticipationAccess(p.KingdomId, speaker).Crown ? "Crown" : GetParticipationAccess(p.KingdomId, speaker).Speaker ? "party speaker" : GetParticipationAccess(p.KingdomId, speaker).Member ? "member" : "constituent",
                    ["privateBeliefAlignment"] = beliefs == null ? (JToken)JValue.CreateNull() : PetitionAlignment(p, beliefs),
                    ["privateRelationshipContribution"] = Math.Max(-10, Math.Min(10, speaker.GetRelation(counterpart) / 10)),
                    ["personalEndorsementReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.endorse_petition", FindKingdom(p.KingdomId)),
                    ["personalSponsorshipReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.sponsor_petition", FindKingdom(p.KingdomId)),
                    ["personalAdoptionReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.seek_petition_adoption", FindKingdom(p.KingdomId)),
                    ["nominationVoteReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.support_nomination", FindKingdom(p.KingdomId)),
                    ["personalPresentationReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.present_petition", FindKingdom(p.KingdomId)),
                    ["personalMandateTransferReferenceGold"] = ReignActionQuotes.PoliticalServiceReference(speaker, "government.transfer_mandate", FindKingdom(p.KingdomId)),
                    ["referencePolicyVersion"] = GovernmentParticipationBalance.Version })) };
        }
    }
}
