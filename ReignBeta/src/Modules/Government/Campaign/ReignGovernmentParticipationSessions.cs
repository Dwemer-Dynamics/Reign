using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.MountAndBlade;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private float _participationSessionDay = -1;
        private void TickParticipation()
        {
            QueuePoliticalProfileSync();
            int interval = GovernmentInterval(CurrentDay());
            if (interval < 0) return;
            foreach (var kingdom in Kingdom.All.Where(IsEligibleKingdom).Where(x => !IsCharterPending(x.StringId)
                && !_participation.Caucuses.Any(c => c.KingdomId == x.StringId)))
            {
                if (GetGovernment(kingdom) == null) continue;
                foreach (var party in GetParties(kingdom.StringId).Where(x => x.SeatCount > 0))
                    EnsurePartyCaucus(kingdom.StringId, party.PartyId, interval + 1);
                SeedPartyPetitions(kingdom, GetGovernment(kingdom), CurrentDay(), interval + 1);
            }
            foreach (var petition in _participation.Petitions.Where(x => !GovernmentParticipationRules.Closed(x.Status)).ToList())
            {
                var business = _business.FirstOrDefault(x => x.BusinessId == petition.BusinessId);
                if (business != null)
                {
                    RepairPendingPetitionSponsor(petition, business);
                    if (business.FrozenAuthorityLevel > 0 && !business.ExecutionApplied) petition.Status = "presented";
                    if (business.ExecutionApplied) CompleteParticipationPetition(business);
                    else if (GovernmentBusinessRules.IsClosed(business.Status) || business.Status == "execution_failed")
                    { petition.Status = "failed"; petition.BlockReason = business.Outcome; petition.ResolvedInterval = interval; }
                }
                else if (petition.SubmittedInterval >= 0 && interval >= petition.SubmittedInterval + GovernmentParticipationBalance.SponsorshipExpiryIntervals
                    && !petition.Consents.Any(x => x.Operation == "sponsor" && interval < x.ExpiresInterval))
                { petition.Status = "expired"; petition.ResolvedInterval = interval; petition.BlockReason = "Sponsorship expired after two regular intervals."; }
            }
        }

        private GovernmentPartyCaucus EnsurePartyCaucus(string kingdomId, string partyId, int targetInterval)
        {
            var caucus = _participation.Caucuses.FirstOrDefault(x => x.KingdomId == kingdomId && x.PartyId == partyId && x.TargetInterval == targetInterval);
            if (caucus != null) return caucus;
            caucus = new GovernmentPartyCaucus { KingdomId = kingdomId, PartyId = partyId, TargetInterval = targetInterval,
                Roll = GetSeats(kingdomId).Where(x => x.PartyId == partyId).Select(x => x.HeroStringId).ToList() };
            _participation.Caucuses.Add(caucus); return caucus;
        }

        private void ProcessParticipationSession(Kingdom kingdom, ReignGovernmentStateRecord state, float day)
        {
            int interval = GovernmentInterval(day);
            _accreditingSession = true;
            try { ReconcileMembership(kingdom, state, false); }
            finally { _accreditingSession = false; }
            foreach (var election in _participation.Elections.Where(x => x.KingdomId == kingdom.StringId && x.Opened && !x.Closed && x.CloseInterval <= interval))
            {
                var party = FindParty(kingdom.StringId, election.PartyId); if (party == null) continue;
                var current = GetSeats(kingdom.StringId).Where(x => x.PartyId == election.PartyId).Select(x => x.HeroStringId).ToList();
                foreach (var member in election.Roll.Where(x => x != Hero.MainHero?.StringId && current.Contains(x)))
                {
                    if (election.Ballots.ContainsKey(member)) continue;
                    var voter = FindHero(member); var profile = AppliedPoliticalProfile(member); if (profile == null || voter == null || voter.IsPrisoner) continue;
                    string preferred = election.Nominations.Where(x => current.Contains(x.Key) && x.Value.Count(current.Contains) >= (election.Roll.Count == 1 ? 1 : 2)).Select(x => x.Key)
                        .OrderByDescending(id => KnownCandidateAlignment(kingdom.StringId, voter, id)
                            + Math.Max(-15, Math.Min(15, voter.GetRelation(FindHero(id)) / 5)) + (id == election.IncumbentId ? 10 : 0))
                        .ThenBy(id => id, StringComparer.Ordinal).FirstOrDefault();
                    if (preferred != null) election.Ballots[member] = preferred;
                }
                party.SpeakerHeroStringId = GovernmentParticipationRules.CloseElection(election, current); party.Revision++;
                RecordParticipationNews(kingdom.StringId, "party_office", party.Name + " selected " + (FindHero(party.SpeakerHeroStringId)?.Name?.ToString() ?? "an acting speaker") + " as its speaker.");
            }
            foreach (var caucus in _participation.Caucuses.Where(x => x.KingdomId == kingdom.StringId && !x.Closed && x.TargetInterval <= interval).ToList())
            {
                var current = CurrentCaucusMembers(caucus);
                ReviewNpcNominations(caucus, current);
                var valid = caucus.Nominations.Where(id => {
                    var petition = _participation.Petitions.FirstOrDefault(x => x.Id == id);
                    return petition != null && petition.BusinessId.Length == 0 && !GovernmentParticipationRules.Closed(petition.Status)
                        && petition.Consents.Any(c => c.Operation == "sponsor" && c.Revision == petition.Revision && interval < c.ExpiresInterval
                            && GetParticipationAccess(petition.KingdomId, FindHero(c.ActorId)).Member)
                        && ValidatePetitionTerms(petition, out _);
                }).ToList();
                var revisions = _participation.Petitions.Where(p => valid.Contains(p.Id)).ToDictionary(p => p.Id, p => p.Revision);
                string selected = GovernmentParticipationRules.SelectNomination(caucus, current, valid, revisions);
                if (selected.Length == 0) continue;
                var adopted = _participation.Petitions.Single(x => x.Id == selected);
                adopted.AdoptedPartyId = caucus.PartyId;
                var speaker = FindHero(FindParty(kingdom.StringId, caucus.PartyId)?.SpeakerHeroStringId);
                if (speaker != null)
                {
                    _participationSessionDay = day;
                    try { FileAdoptedPetition(adopted, speaker, out _); }
                    finally { _participationSessionDay = -1; }
                }
            }
            ReviewPoliticalAffiliations(kingdom, interval);
            foreach (var party in GetParties(kingdom.StringId).Where(x => x.SeatCount > 0)) EnsurePartyCaucus(kingdom.StringId, party.PartyId, interval + 1);
            SeedPartyPetitions(kingdom, state, day, interval + 1);
        }

        private void SeedPartyPetitions(Kingdom kingdom, ReignGovernmentStateRecord state, float day, int interval)
        {
            var evidence = CollectEvidenceTags(kingdom);
            foreach (var party in GetParties(kingdom.StringId).Where(x => x.SeatCount > 0 && x.SpeakerHeroStringId != Hero.MainHero?.StringId))
            {
                var caucus = EnsurePartyCaucus(kingdom.StringId, party.PartyId, interval);
                if (caucus.Nominations.Any()) continue;
                var template = ReignGovernmentResolutionCatalog.Templates.Where(x => ReignGovernmentResolutionCatalog.IsEligible(x, evidence, ParsePlanks(party.PlanksCsv))
                    && CanPrepareSeasonalPrisonerTarget(kingdom, x)
                    && !_resolutions.Any(r => r.KingdomStringId == kingdom.StringId && r.TemplateId == x.Id && (r.Status == "active" || r.Status == "proposed" || r.Status == "debate")))
                    .OrderBy(x => ReignGovernmentRules.StableHash(kingdom.StringId + "|" + party.PartyId + "|" + interval + "|" + x.Id)).FirstOrDefault();
                if (template == null || string.IsNullOrEmpty(party.SpeakerHeroStringId)) continue;
                var petition = new GovernmentPetition { KingdomId = kingdom.StringId, AuthorId = party.SpeakerHeroStringId,
                    ProposalId = "seasonal:" + template.Id, Kind = "seasonal", Title = template.Title,
                    Terms = "Evidence-backed " + template.Title + ". Review the measured objectives and routes at presentation.",
                    CreatedInterval = interval - 1, SubmittedInterval = interval - 1, Status = "party_consideration" };
                petition.DisclosedTo.AddRange(caucus.Roll);
                petition.Consents.Add(new GovernmentPetitionConsent { ActorId = party.SpeakerHeroStringId, Operation = "sponsor", Revision = 1,
                    PartyId = party.PartyId, ExpiresInterval = interval + 1, ReceiptId = "npc_nomination:" + petition.Id });
                _participation.Petitions.Add(petition); caucus.Nominations.Add(petition.Id);
                ReviewNpcNominations(caucus, caucus.Roll);
            }
        }

        private void ReviewNpcNominations(GovernmentPartyCaucus caucus, IReadOnlyCollection<string> current)
        {
            foreach (string id in caucus.Roll.Where(x => current.Contains(x) && x != Hero.MainHero?.StringId))
            {
                var hero = FindHero(id); var beliefs = AppliedPoliticalProfile(id); if (hero == null || hero.IsPrisoner || beliefs == null) continue;
                var promise = AgreedCaucusNomination(caucus, id);
                if (promise != null)
                {
                    // An invalidated exact promise cannot silently become a vote for
                    // another revision or proposal. Its observer handles impossibility.
                    long.TryParse(ReignBeta.Actions.ReignActionService.Arg(promise, "petitionRevision"), out long promisedRevision);
                    string promised = ReignBeta.Actions.ReignActionService.Arg(promise, "petitionId");
                    var exact = _participation.Petitions.FirstOrDefault(p => p.Id == promised && p.Revision == promisedRevision
                        && p.Status == "party_consideration" && p.BusinessId.Length == 0);
                    if (exact != null) GovernmentParticipationRules.EndorseNomination(caucus, id, promised, current, promisedRevision);
                    else { caucus.Endorsements.Remove(id); caucus.EndorsementRevisions.Remove(id); }
                    continue;
                }
                var ranked = caucus.Nominations.Select(p => _participation.Petitions.FirstOrDefault(x => x.Id == p))
                    .Where(p => p != null && p.Status == "party_consideration" && p.BusinessId.Length == 0
                        && p.Consents.Any(c => c.Operation == "sponsor" && c.Revision == p.Revision && caucus.TargetInterval < c.ExpiresInterval
                            && GetParticipationAccess(p.KingdomId, FindHero(c.ActorId)).Member) && ValidatePetitionTerms(p, out _))
                    .Select(p => new { Petition = p, Score = PetitionAlignment(p, beliefs) + (p.AuthorId == id ? 15 : 0)
                        + (FindHero(p.AuthorId) == null ? 0 : hero.GetRelation(FindHero(p.AuthorId)) / 10) })
                    .OrderByDescending(x => x.Score).ThenBy(x => x.Petition.Id, StringComparer.Ordinal).FirstOrDefault();
                if (ranked != null && ranked.Score >= 0)
                {
                    if (!ranked.Petition.DisclosedTo.Contains(id)) ranked.Petition.DisclosedTo.Add(id);
                    GovernmentParticipationRules.EndorseNomination(caucus, id, ranked.Petition.Id, current, ranked.Petition.Revision);
                }
                else { caucus.Endorsements.Remove(id); caucus.EndorsementRevisions.Remove(id); }
            }
        }

        private int PetitionAlignment(GovernmentPetition petition, GovernmentPoliticalProfile profile)
        {
            if (petition.Kind == "seasonal")
            {
                var template = ReignGovernmentResolutionCatalog.Find(petition.ProposalId.Substring(9));
                return template == null ? -40 : GovernmentPoliticalBeliefs.Alignment(profile, template.SupportingPlanks.ToDictionary(x => x, x => 75));
            }
            if (petition.Kind == "edict")
            {
                string id = petition.ProposalId.StartsWith("edict_repeal:", StringComparison.Ordinal)
                    ? _edicts.FirstOrDefault(x => x.Id == petition.ProposalId.Substring(13))?.EdictId : petition.ProposalId.Split(':')[1];
                return id == null ? -40 : GovernmentPoliticalBeliefs.Alignment(profile, GovernmentPoliticalBeliefs.EdictImpact(id, petition.ProposalId.StartsWith("edict_repeal:", StringComparison.Ordinal)));
            }
            if (petition.Kind == "policy")
            {
                var policy = PolicyObject.All.FirstOrDefault(x => x.StringId == petition.ProposalId.Substring(7));
                return policy == null ? -40 : GovernmentPoliticalBeliefs.Alignment(profile, GovernmentPoliticalBeliefs.NativePolicyImpact(policy.AuthoritarianWeight, policy.OligarchicWeight,
                    policy.EgalitarianWeight, FindKingdom(petition.KingdomId).HasPolicy(policy)));
            }
            return GovernmentPoliticalBeliefs.Alignment(profile, new Dictionary<ReignGovernmentPlank, int> { [ReignGovernmentPlank.RepresentativeAuthority] = petition.Kind == "admission" ? 25 : -25 });
        }

        public bool SelectPartyNomination(string petitionId, Hero actor, out string reason)
        {
            reason = "Your current seat must belong to the frozen caucus for this petition.";
            var caucus = _participation.Caucuses.FirstOrDefault(x => !x.Closed && x.Nominations.Contains(petitionId) && x.Roll.Contains(actor?.StringId));
            if (caucus == null) return false;
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId);
            if (petition == null || !CheckNominationSupport(actor?.StringId, petitionId, petition.Revision, GovernmentParticipationRules.CaucusId(caucus), out reason)) return false;
            var current = CurrentCaucusMembers(caucus);
            if (!GovernmentParticipationRules.EndorseNomination(caucus, actor.StringId, petitionId, current, petition.Revision)) return false;
            reason = "Your party endorsement is recorded. You may change it before selection closes."; return true;
        }

        private string LeadershipElectionId(string kingdomId, string partyId)
        {
            var rounds = _participation.Elections.Where(x => x.KingdomId == kingdomId && x.PartyId == partyId).ToList();
            return rounds.LastOrDefault(x => !x.Closed)?.Id ?? kingdomId + "|" + partyId + "|" + (rounds.Count + 1);
        }

        private int KnownCandidateAlignment(string kingdomId, Hero voter, string candidateId)
        {
            if (AppliedPoliticalProfile(voter?.StringId) == null) return 0;
            var assessments = new List<int>();
            foreach (var business in _business.Where(x => x.KingdomStringId == kingdomId && x.FrozenAuthorityLevel > 0)
                .OrderByDescending(x => x.PresentationDay).Take(12))
            {
                var declared = GetBusinessPublicPositions(business.BusinessId).OfType<JObject>()
                    .FirstOrDefault(x => x.Value<string>("memberHeroId") == candidateId && !string.IsNullOrEmpty(x.Value<string>("optionId")));
                var option = BusinessOptions(business).FirstOrDefault(x => x.Value<string>("id") == declared?.Value<string>("optionId"));
                if (option != null) assessments.Add(PersonalBusinessAlignment(business, voter, option));
            }
            // Compare only actual public positions/ballots. Another candidate's private
            // profile or an undisclosed player declaration is not observer knowledge.
            return assessments.Count == 0 ? 0 : (int)Math.Round(assessments.Average());
        }

        public bool NominatePartyLeader(string kingdomId, string candidateId, Hero nominator, out string reason)
        {
            if (!CanNominatePartyLeader(kingdomId, candidateId, nominator, out reason)) return false;
            var seat = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == nominator?.StringId);
            var candidate = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == candidateId && x.PartyId == seat?.PartyId);
            if (seat == null || candidate == null || string.IsNullOrEmpty(seat.PartyId)) return false;
            int interval = GovernmentInterval(CurrentDay());
            var office = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == kingdomId && x.IncumbentId == candidateId && x.Status == "active");
            if (office == null || interval <= office.AccreditedInterval) { reason = "The candidate must complete one regular interval as a member."; return false; }
            var election = _participation.Elections.LastOrDefault(x => x.KingdomId == kingdomId && x.PartyId == seat.PartyId);
            if (election == null || election.Closed)
            {
                if (election?.Opened == true && interval < election.OpenInterval + GovernmentParticipationBalance.LeadershipChallengeIntervals) { reason = "This party may hold one leadership challenge per two sessions."; return false; }
                election = new GovernmentPartyElection { Id = LeadershipElectionId(kingdomId, seat.PartyId), KingdomId = kingdomId, PartyId = seat.PartyId, OpenInterval = interval, CloseInterval = interval + 1,
                    IncumbentId = FindParty(kingdomId, seat.PartyId)?.SpeakerHeroStringId ?? "", Roll = GetSeats(kingdomId).Where(x => x.PartyId == seat.PartyId).Select(x => x.HeroStringId).ToList() };
                _participation.Elections.Add(election);
            }
            if (!election.Roll.Contains(nominator.StringId)) { reason = "You joined after this election roll was frozen."; return false; }
            if (!election.Nominations.ContainsKey(candidateId)) election.Nominations[candidateId] = new List<string>();
            if (!election.Nominations[candidateId].Contains(nominator.StringId)) election.Nominations[candidateId].Add(nominator.StringId);
            if (!election.Opened && election.Nominations[candidateId].Count >= (election.Roll.Count == 1 ? 1 : 2))
            { election.Opened = true; election.OpenInterval = interval; election.CloseInterval = interval + 1; }
            reason = election.Opened ? "The nominated challenge is open for one regular interval. Seek members' explicit votes."
                : "Nomination recorded. A second eligible nomination is needed to open an election; no challenge cooldown is spent yet."; return true;
        }
        public bool CanNominatePartyLeader(string kingdomId, string candidateId, Hero nominator, out string reason)
        {
            reason = "Leadership nominations require adult living members of the same party.";
            if (nominator == null || !GetParticipationAccess(kingdomId, nominator).Member
                || !GetParticipationAccess(kingdomId, FindHero(candidateId)).Member) return false;
            var seat = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == nominator.StringId);
            var candidate = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == candidateId && x.PartyId == seat?.PartyId);
            if (seat == null || candidate == null || string.IsNullOrEmpty(seat.PartyId)) return false;
            int interval = GovernmentInterval(CurrentDay());
            var office = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == kingdomId && x.IncumbentId == candidateId && x.Status == "active");
            if (office == null || interval <= office.AccreditedInterval) { reason = "The candidate must complete one regular interval as a member."; return false; }
            var election = _participation.Elections.LastOrDefault(x => x.KingdomId == kingdomId && x.PartyId == seat.PartyId);
            if (election?.Closed == false && !election.Roll.Contains(nominator.StringId))
            { reason = "You joined after this election roll was frozen."; return false; }
            if (election?.Closed == true && election.Opened && interval < election.OpenInterval + GovernmentParticipationBalance.LeadershipChallengeIntervals)
            { reason = "This party may hold one leadership challenge per two sessions."; return false; }
            reason = ""; return true;
        }
        private void OpenVacantSpeakerElection(ReignGovernmentPartyRecord party, string previousSpeaker)
        {
            if (string.IsNullOrEmpty(previousSpeaker) || party.SeatCount == 0
                || _participation.Elections.Any(x => x.KingdomId == party.KingdomStringId && x.PartyId == party.PartyId && !x.Closed)) return;
            int interval = GovernmentInterval(CurrentDay());
            var roll = GetSeats(party.KingdomStringId).Where(x => x.PartyId == party.PartyId).Select(x => x.HeroStringId).ToList();
            var election = new GovernmentPartyElection { Id = LeadershipElectionId(party.KingdomStringId, party.PartyId), KingdomId = party.KingdomStringId, PartyId = party.PartyId,
                Roll = roll, Opened = true, OpenInterval = interval, CloseInterval = interval + 1, IncumbentId = "" };
            var candidates = roll.Where(x => x != Hero.MainHero?.StringId && _participation.Constituencies.Any(c =>
                c.KingdomId == party.KingdomStringId && c.Status == "active" && c.IncumbentId == x && interval > c.AccreditedInterval)).ToList();
            foreach (string nominator in roll.Where(x => x != Hero.MainHero?.StringId && AppliedPoliticalProfile(x) != null))
            {
                var member = FindHero(nominator); if (member == null || member.IsPrisoner) continue;
                string candidate = candidates.OrderByDescending(x => KnownCandidateAlignment(party.KingdomStringId, member, x)
                    + Math.Max(-15, Math.Min(15, member.GetRelation(FindHero(x)) / 5))).ThenBy(x => x, StringComparer.Ordinal).FirstOrDefault();
                if (candidate == null) continue;
                if (!election.Nominations.ContainsKey(candidate)) election.Nominations[candidate] = new List<string>();
                election.Nominations[candidate].Add(nominator);
            }
            _participation.Elections.Add(election);
            RecordParticipationNews(party.KingdomStringId, "party_office", party.Name + " has appointed an acting speaker while its members elect a successor at the next session.");
        }
        public bool VotePartyLeader(string kingdomId, string candidateId, Hero voter, out string reason)
        {
            reason = "An eligible candidate and your frozen party election seat are required.";
            var seat = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == voter?.StringId);
            var election = _participation.Elections.FirstOrDefault(x => x.KingdomId == kingdomId && x.PartyId == seat?.PartyId && !x.Closed);
            if (election == null || !election.Opened || !election.Roll.Contains(voter.StringId) || !election.Nominations.ContainsKey(candidateId)
                || election.Nominations[candidateId].Distinct().Count() < (election.Roll.Count == 1 ? 1 : 2)) return false;
            election.Ballots[voter.StringId] = candidateId; reason = "Your party leadership ballot is recorded."; return true;
        }

        private void ReviewPoliticalAffiliations(Kingdom kingdom, int interval)
        {
            foreach (var seat in GetSeats(kingdom.StringId).Where(x => x.HeroStringId != Hero.MainHero?.StringId))
            {
                var beliefs = AppliedPoliticalProfile(seat.HeroStringId); if (beliefs == null || AgreedPlatformParty(seat.HeroStringId).Length > 0) continue;
                var parties = GetParties(kingdom.StringId);
                var current = parties.FirstOrDefault(x => x.PartyId == seat.PartyId);
                var best = parties.OrderByDescending(x => GovernmentPoliticalBeliefs.PartyCompatibility(beliefs, ParsePlanks(x.PlanksCsv)))
                    .ThenBy(x => x.PartyId, StringComparer.Ordinal).FirstOrDefault();
                if (best == null || best == current || current != null && GovernmentPoliticalBeliefs.PartyCompatibility(beliefs, ParsePlanks(best.PlanksCsv))
                    - GovernmentPoliticalBeliefs.PartyCompatibility(beliefs, ParsePlanks(current.PlanksCsv)) < 15)
                { _participation.SwitchCandidates.Remove(seat.HeroStringId); continue; }
                string key = best.PartyId + "|" + (interval - 1);
                if (_participation.SwitchCandidates.TryGetValue(seat.HeroStringId, out string prior) && prior == key)
                { seat.PartyId = best.PartyId; seat.Revision++; _participation.SwitchCandidates.Remove(seat.HeroStringId); }
                else _participation.SwitchCandidates[seat.HeroStringId] = best.PartyId + "|" + interval;
            }
            RecalculatePartyState(kingdom, GetGovernment(kingdom));
        }

        private void AssignDocket(ReignGovernmentBusinessRecord record)
        {
            var prior = _participation.Dockets.LastOrDefault(x => x.BusinessId == record.BusinessId);
            if (prior != null && (record.FrozenAuthorityLevel > 0 || Math.Abs(prior.PresentationDay - record.PresentationDay) < .01)) return;
            int index = _participation.Dockets.Count(x => x.KingdomId == record.KingdomStringId && Math.Abs(x.PresentationDay - record.PresentationDay) < .01);
            _participation.Dockets.Add(new GovernmentDocketReference { KingdomId = record.KingdomStringId, BusinessId = record.BusinessId,
                PresentationDay = record.PresentationDay, Index = index, Alias = GovernmentParticipationRules.DocketAlias(index) });
        }
        public string DocketLabel(string businessId)
        {
            var docket = _participation.Dockets.LastOrDefault(x => x.BusinessId == businessId);
            return docket == null ? "Awaiting presentation" : "Docket " + docket.Alias + " · " + FormatGovernmentDate((float)docket.PresentationDay);
        }
        private void CompleteParticipationPetition(ReignGovernmentBusinessRecord record)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.BusinessId == record.BusinessId); if (petition == null) return;
            petition.Status = record.ExecutedOptionId == record.StatusQuoOptionId ? "rejected" : record.Kind == "edict" && !record.IsRepeal ? "active" : "completed";
            petition.ResolvedInterval = GovernmentInterval(CurrentDay());
        }

        public bool SubmitMemberBallot(string businessId, Hero voter, string optionId, bool abstain, out string reason)
        {
            reason = "Your current seat must be on the matter's frozen voting roll, and the vote must still be open.";
            var record = _business.FirstOrDefault(x => x.BusinessId == businessId);
            if (record == null || !GetParticipationAccess(record.KingdomStringId, voter, businessId).Ballot || voter.IsPrisoner
                || !abstain && !BusinessOptions(record).Any(x => x.Value<string>("id") == optionId)) return false;
            bool present = GetBusinessAttendance(businessId).Any(x => x.HeroStringId == voter.StringId && x.Status == "present");
            if (!present && (Mission.Current != null || voter.PartyBelongedTo?.MapEvent != null || voter.PartyBelongedTo?.SiegeEvent != null))
            { reason = "Absentee instructions require normal communication outside combat, captivity and a siege."; return false; }
            if (!present && CurrentDay() >= record.DecisionDay) { reason = "An absentee ballot must be filed before the decision session."; return false; }
            _participation.Ballots.RemoveAll(x => x.BusinessId == businessId && x.HeroId == voter.StringId);
            _participation.Ballots.Add(new GovernmentMemberBallot { BusinessId = businessId, HeroId = voter.StringId, OptionId = abstain ? "" : optionId,
                Abstain = abstain, Source = present ? "member" : "absentee", SubmittedDay = CurrentDay() });
            reason = abstain ? "Your explicit abstention is recorded." : present ? "Your member ballot is recorded." : "Your absentee ballot is recorded. You may revise it before the decision session.";
            if (CurrentDay() >= record.DecisionDay) TickGovernmentBusiness();
            return true;
        }
        private bool WaitForPlayerMemberBallot(ReignGovernmentBusinessRecord record) => Hero.MainHero != null && Mission.Current == null
            && (ReignBeta.UI.ReignGovernmentScreenManager.IsOpen || ReignBeta.UI.ReignGovernmentScreenManager.CanPresentOnMap())
            && GetParticipationAccess(record.KingdomStringId, Hero.MainHero, record.BusinessId).Ballot
            && GetBusinessAttendance(record.BusinessId).Any(x => x.HeroStringId == Hero.MainHero.StringId && x.Status == "present")
            && !_participation.Ballots.Any(x => x.BusinessId == record.BusinessId && x.HeroId == Hero.MainHero.StringId);
        public bool NeedsPlayerMemberBallot(string businessId) => _business.Any(x => x.BusinessId == businessId && CurrentDay() >= x.DecisionDay && WaitForPlayerMemberBallot(x));
        public bool ChangePlayerParty(string kingdomId, string partyId, out string reason)
        {
            reason = "A current seat and a party in this government are required.";
            var seat = GetSeats(kingdomId).FirstOrDefault(x => x.HeroStringId == Hero.MainHero?.StringId);
            if (seat == null || !GetParties(kingdomId).Any(x => x.PartyId == partyId)) return false;
            if (AgreedPlatformParty(seat.HeroStringId).Length > 0 && AgreedPlatformParty(seat.HeroStringId) != partyId)
            { reason = "Your binding platform commitment must finish first."; return false; }
            seat.PartyId = partyId; seat.Revision++; RecalculatePartyState(FindKingdom(kingdomId), GetGovernment(kingdomId));
            reason = "Your party affiliation is changed. Existing frozen rolls and commitments remain binding."; return true;
        }
        public void SetPlayerPoliticalDeclaration(ReignGovernmentPlank value, int stance, int importance)
        {
            if (!Enum.IsDefined(typeof(ReignGovernmentPlank), value) || stance < -100 || stance > 100 || importance < 1 || importance > 100)
                throw new ArgumentException("Invalid political declaration.");
            _participation.PlayerDeclarations.RemoveAll(x => x.Value == value);
            _participation.PlayerDeclarations.Add(new GovernmentPoliticalValue { Value = value, Stance = stance, Importance = importance });
        }
    }
}
