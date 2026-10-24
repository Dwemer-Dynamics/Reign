using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Government
{
    public sealed partial class ReignGovernmentCampaignBehavior
    {
        private GovernmentPetition _filingPetition;
        public JArray GetParticipationProposalChoices(string kingdomId, Hero author)
        {
            if (!GetParticipationAccess(kingdomId, author).Prepare) return new JArray();
            var result = new JArray(GetGovernmentProposalChoices(kingdomId).OfType<JObject>().Where(x =>
                x.Value<string>("proposalId").StartsWith("policy:", StringComparison.Ordinal) || x.Value<string>("category") == "edict"));
            foreach (var row in _participation.Constituencies.Where(x => x.KingdomId == kingdomId && x.Status == "active"
                && x.Source != ReignGovernmentSeatSource.LandholdingClanLeader))
            {
                string location = ConstituencyLabel(row), incumbent = FindHero(row.IncumbentId)?.Name?.ToString() ?? "Vacant";
                result.Add(new JObject { ["proposalId"] = "admission:" + row.Id, ["label"] = "Civic mandate for " + author.Name + " — " + location,
                    ["description"] = "Represent " + location + ". Requires a majority of its eligible local notables, the incumbent's transfer consent if occupied, sponsorship and a regular government decision. Current member: " + incumbent + ". No new seat is created.",
                    ["available"] = !_seats.Any(x => x.HeroStringId == author.StringId) && author != FindKingdom(kingdomId)?.Leader });
                if (row.IncumbentId.Length > 0) result.Add(new JObject {
                    ["proposalId"] = "recall:" + row.Id, ["label"] = "Recall " + incumbent + " — " + location,
                    ["description"] = "A separate recall requires two-thirds of eligible local notables and a regular government vote. It creates a vacancy, not an automatic appointment.", ["available"] = true });
            }
            return result;
        }

        public IReadOnlyList<GovernmentPetition> GetPetitions(string kingdomId, Hero viewer) => _participation.Petitions
            .Where(x => x.KingdomId == kingdomId && viewer != null && GovernmentParticipationRules.CanRead(x, viewer.StringId))
            .OrderByDescending(x => x.CreatedInterval).ThenBy(x => x.Title, StringComparer.CurrentCulture).ToList();

        public bool PrepareGovernmentPetition(string kingdomId, string proposalId, string argument, Hero author, out string petitionId, out string result)
        {
            petitionId = ""; result = "You cannot prepare a proposal for this kingdom.";
            if (!GetParticipationAccess(kingdomId, author).Prepare) return false;
            var choice = GetParticipationProposalChoices(kingdomId, author).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == proposalId);
            if (choice?.Value<bool>("available") != true) { result = "The measure is unavailable or conflicts with current law."; return false; }
            var duplicate = _participation.Petitions.FirstOrDefault(x => x.KingdomId == kingdomId && x.AuthorId == author.StringId && x.ProposalId == proposalId && !GovernmentParticipationRules.Closed(x.Status));
            if (duplicate != null) { petitionId = duplicate.Id; result = "Your existing petition has been selected."; return true; }
            bool admission = proposalId.StartsWith("admission:", StringComparison.Ordinal), recall = proposalId.StartsWith("recall:", StringComparison.Ordinal);
            var petition = new GovernmentPetition { KingdomId = kingdomId, AuthorId = author.StringId, ProposalId = proposalId,
                Kind = admission ? "admission" : recall ? "recall" : proposalId.StartsWith("policy:", StringComparison.Ordinal) ? "policy" : "edict",
                Title = choice.Value<string>("label"), Terms = choice.Value<string>("description"), Argument = (argument ?? "").Substring(0, Math.Min(2000, (argument ?? "").Length)),
                ExactPayloadJson = choice["payload"]?.ToString(Formatting.None) ?? "",
                CreatedInterval = GovernmentInterval(CurrentDay()) };
            if (admission || recall)
            {
                petition.ConstituencyId = proposalId.Substring(admission ? 10 : 7);
                var constituency = _participation.Constituencies.Single(x => x.KingdomId == kingdomId && x.Id == petition.ConstituencyId);
                petition.CandidateId = author.StringId; petition.PreviousIncumbentId = constituency.IncumbentId;
            }
            _participation.Petitions.Add(petition); petitionId = petition.Id;
            result = "Private draft saved. It has not consumed a public slot, spent funds or changed the law."; return true;
        }
        public bool ReviseGovernmentPetition(string petitionId, string argument, Hero author, out string result)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId && x.AuthorId == author?.StringId);
            result = "Only an unpresented, unfiled petition can be revised. Withdraw a filed matter and prepare its replacement.";
            if (petition == null || petition.BusinessId.Length > 0 || GovernmentParticipationRules.Closed(petition.Status)) return false;
            var row = GetParticipationProposalChoices(petition.KingdomId, author).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == petition.ProposalId);
            if (row?.Value<bool>("available") != true) { result = "This target is no longer eligible. Prepare a new proposal."; return false; }
            // Deliberate revision renews the exact request, including a changed endorsement
            // roll. Former consents remain in agreement journals, never carried into the new terms.
            petition.Revision++; petition.Consents.Clear(); petition.FrozenEndorsers.Clear(); petition.DisclosedTo.Clear();
            petition.Title = row.Value<string>("label"); petition.Terms = row.Value<string>("description");
            petition.ExactPayloadJson = row["payload"]?.ToString(Formatting.None) ?? "";
            petition.Argument = (argument ?? "").Substring(0, Math.Min(2000, (argument ?? "").Length));
            petition.Status = "draft"; petition.AdoptedPartyId = ""; petition.BlockReason = "";
            var constituency = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == petition.KingdomId && x.Id == petition.ConstituencyId);
            if (constituency != null) petition.PreviousIncumbentId = constituency.IncumbentId;
            foreach (var caucus in _participation.Caucuses.Where(x => !x.Closed))
            {
                caucus.Nominations.Remove(petitionId);
                foreach (var key in caucus.Endorsements.Where(x => x.Value == petitionId).Select(x => x.Key).ToList()) caucus.Endorsements.Remove(key);
            }
            result = "A new private revision is saved. Submit it and obtain fresh consent."; return true;
        }

        public bool SubmitGovernmentPetition(string petitionId, Hero author, out string result)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId);
            result = "Only the author may submit this draft.";
            if (petition == null || author?.StringId != petition.AuthorId || !GetParticipationAccess(petition.KingdomId, author).Prepare) return false;
            int interval = GovernmentInterval(CurrentDay());
            if (!GovernmentParticipationRules.CanSubmit(petition, _participation.Petitions, interval, out result) || !ValidatePetitionTerms(petition, out result)) return false;
            petition.Status = "seeking_sponsorship"; petition.SubmittedInterval = interval;
            result = "Petition submitted. Approach a seated member or party speaker privately to request support for this exact petition."; return true;
        }

        public bool WithdrawGovernmentPetition(string petitionId, Hero author, out string result)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId);
            result = "This petition cannot be withdrawn.";
            if (petition == null || petition.AuthorId != author?.StringId || GovernmentParticipationRules.Closed(petition.Status)) return false;
            var business = _business.FirstOrDefault(x => x.BusinessId == petition.BusinessId);
            if (business != null && (business.ExecutionApplied || !string.IsNullOrEmpty(business.WinningOptionId))) return false;
            if (business != null) CloseInvalidBusiness(business, "The author withdrew the petition before decision.");
            petition.Status = "withdrawn"; petition.ResolvedInterval = GovernmentInterval(CurrentDay());
            result = "Petition withdrawn. Existing agreement cancellation and refund rules apply."; return true;
        }

        private bool ValidatePetitionTerms(GovernmentPetition petition, out string reason)
        {
            reason = "The petition's named parties or government are no longer eligible.";
            var kingdom = FindKingdom(petition.KingdomId); var author = FindHero(petition.AuthorId);
            if (!GetParticipationAccess(petition.KingdomId, author).Prepare) return false;
            if (petition.Kind == "seasonal")
            {
                var template = ReignGovernmentResolutionCatalog.Find(petition.ProposalId.Substring(9));
                if (template == null) { reason = "The resolution template is unavailable."; return false; }
                reason = ""; return true;
            }
            if (petition.Kind == "admission" || petition.Kind == "recall")
            {
                var seat = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == petition.KingdomId && x.Id == petition.ConstituencyId && x.Status == "active");
                if (seat == null || seat.Source == ReignGovernmentSeatSource.LandholdingClanLeader || seat.IncumbentId != petition.PreviousIncumbentId)
                { reason = "The constituency or incumbent changed; prepare a fresh mandate petition."; return false; }
                if (petition.Kind == "admission" && (!EligibleOfficeHolder(author, kingdom) || _seats.Any(x => x.HeroStringId == author.StringId))) return false;
                if (petition.Kind == "recall" && seat.IncumbentId.Length == 0) { reason = "This constituency is already vacant."; return false; }
                reason = ""; return true;
            }
            var choice = GetGovernmentProposalChoices(petition.KingdomId).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == petition.ProposalId);
            // Exact reviewed terms include active/repeal state, beneficiary and project. No
            // newly convenient target, payer or opposite policy operation may be substituted.
            if (choice == null || choice.Value<string>("label") != petition.Title || choice.Value<string>("description") != petition.Terms
                || (choice["payload"]?.ToString(Formatting.None) ?? "") != petition.ExactPayloadJson
                || choice.Value<bool>("available") != true && petition.BusinessId.Length == 0)
            { reason = "The reviewed terms or availability changed. Revise the petition and obtain fresh consent."; return false; }
            reason = ""; return true;
        }

        private List<string> EligibleMandateEndorsers(GovernmentPetition petition)
        {
            var seat = _participation.Constituencies.FirstOrDefault(x => x.KingdomId == petition.KingdomId && x.Id == petition.ConstituencyId);
            var settlement = FindSettlement(seat?.SettlementId); var kingdom = FindKingdom(petition.KingdomId);
            var current = settlement?.Notables.Where(x => EligibleOfficeHolder(x, kingdom) && OccupationMatches(x, seat.Source))
                .Select(x => x.StringId).OrderBy(x => x, StringComparer.Ordinal).ToList() ?? new List<string>();
            return petition.FrozenEndorsers.Count == 0 ? current : petition.FrozenEndorsers.Where(current.Contains).ToList();
        }

        public bool CheckPetitionAction(string operation, string petitionId, long revision, Hero actor, Hero counterpart, out string reason)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId);
            reason = "This exact petition revision is unavailable.";
            if (petition == null || petition.Revision != revision || actor == null || !actor.IsAlive || actor.IsChild
                || GovernmentParticipationRules.Closed(petition.Status) || petition.Status == "draft" || petition.Status == "presented") return false;
            if (!GovernmentParticipationRules.CanRead(petition, actor.StringId) && counterpart?.StringId != petition.AuthorId) return false;
            if (!ValidatePetitionTerms(petition, out reason)) return false;
            var access = GetParticipationAccess(petition.KingdomId, actor);
            switch (operation)
            {
                case "endorse":
                    if (access.Member || EligibleMandateEndorsers(petition).Contains(actor.StringId)) { reason = ""; return true; }
                    reason = "Endorsement requires a seated member or an eligible local notable for this mandate."; return false;
                case "transfer":
                    if (petition.Kind == "admission" && petition.PreviousIncumbentId == actor.StringId && access.Member) { reason = ""; return true; }
                    reason = "Only the current incumbent can consent to transfer this exact seat."; return false;
                case "sponsor":
                    if (access.Member) { reason = ""; return true; }
                    reason = "Personal sponsorship requires a current seat."; return false;
                case "seek_adoption":
                case "present":
                    if (!access.Speaker && !access.Crown) { reason = "A current party speaker or the Crown must carry the petition to selection."; return false; }
                    if (operation == "present" && petition.Status != "scheduled") { reason = "The party must adopt this petition before anyone promises to present it."; return false; }
                    if (!access.Crown && !petition.Consents.Any(c => c.Operation == "sponsor"
                        && GovernmentParticipationRules.HasConsent(petition, c.ActorId, "sponsor", GovernmentInterval(CurrentDay()))
                        && GetParticipationAccess(petition.KingdomId, FindHero(c.ActorId)).Member))
                    { reason = "A current member must personally sponsor this exact petition before party adoption."; return false; }
                    if (petition.Kind == "admission" || petition.Kind == "recall")
                    {
                        var eligible = EligibleMandateEndorsers(petition);
                        if (petition.FrozenEndorsers.Count > 0 && eligible.Count != petition.FrozenEndorsers.Count)
                        { reason = "An endorser lost eligibility. Revise the unpresented application and collect current consent."; return false; }
                        if (!GovernmentParticipationRules.MandateSupported(petition, eligible, GovernmentInterval(CurrentDay()), petition.Kind == "recall"))
                        { reason = "Collect the required local endorsements and incumbent transfer consent first."; return false; }
                    }
                    reason = ""; return true;
                default: reason = "Unknown petition operation."; return false;
            }
        }

        public bool ApplyPetitionAction(string operation, string petitionId, long revision, Hero actor, Hero counterpart, string receiptId, out string reason)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == petitionId);
            int interval = GovernmentInterval(CurrentDay());
            // These three receipts belong only to free, player-owned screen actions.
            // A later renewal must not reuse an expired interval's receipt. Signed
            // conversation/agreement receipts retain their original identity.
            string localPrefix = operation == "sponsor" ? "member:" : operation == "seek_adoption"
                ? (receiptId?.StartsWith("crown:", StringComparison.Ordinal) == true ? "crown:" : "speaker:") : "";
            if (petition != null && actor != null && actor == Hero.MainHero && counterpart == actor && localPrefix.Length > 0
                && receiptId == localPrefix + petitionId + ":" + revision)
            {
                if (petition.Revision == revision && LocalConsentCoversNextSelection(petition, actor.StringId, operation, interval))
                { reason = "Your consent for this revision already covers the next selection."; return true; }
                receiptId = LocalPetitionReceiptId(receiptId, interval);
            }
            if (petition != null && petition.Consents.Any(x => x.ReceiptId == receiptId && x.ActorId == actor?.StringId && x.Operation == operation))
            { reason = "The original agreement receipt is already recorded."; return true; }
            if (!CheckPetitionAction(operation, petitionId, revision, actor, counterpart, out reason)) return false;
            if (!petition.DisclosedTo.Contains(actor.StringId)) petition.DisclosedTo.Add(actor.StringId);
            var seat = GetSeats(petition.KingdomId).FirstOrDefault(x => x.HeroStringId == actor.StringId);
            if (!GovernmentParticipationRules.AddConsent(petition, actor.StringId, operation, revision, receiptId, interval, seat?.PartyId ?? "crown", out reason)) return false;
            if (operation == "sponsor") petition.Status = "party_consideration";
            if (operation == "seek_adoption")
            {
                if (petition.Kind == "admission" || petition.Kind == "recall") petition.FrozenEndorsers = EligibleMandateEndorsers(petition);
                if (GetParticipationAccess(petition.KingdomId, actor).Crown)
                { petition.AdoptedPartyId = "crown"; return FileAdoptedPetition(petition, actor, out reason); }
                var caucus = EnsurePartyCaucus(petition.KingdomId, seat.PartyId, interval + 1);
                if (!caucus.Nominations.Contains(petition.Id)) caucus.Nominations.Add(petition.Id);
                foreach (var member in caucus.Roll.Where(x => !petition.DisclosedTo.Contains(x))) petition.DisclosedTo.Add(member);
                petition.Status = "party_consideration";
                ReviewNpcNominations(caucus, CurrentCaucusMembers(caucus));
                reason = "The petition is entered for party selection. It requires a majority of the frozen caucus; no party votes are guaranteed.";
            }
            return true;
        }

        private static string LocalPetitionReceiptId(string receiptId, int interval) => receiptId + ":interval:" + interval;

        // A consent that expires at the coming selection cannot support its nomination.
        // Only an explicit free screen action renews it; signed agreement receipts above
        // keep their original identity and cannot silently extend an NPC's promise.
        private static bool LocalConsentCoversNextSelection(GovernmentPetition petition, string actorId, string operation, int interval) =>
            petition != null && GovernmentParticipationRules.HasConsent(petition, actorId, operation, interval + 1);

        public string PetitionConsentValidity(GovernmentPetition petition, GovernmentPetitionConsent consent) =>
            DescribePetitionConsent(petition, consent, GovernmentInterval(CurrentDay()),
                CaucusSessionLabel(new GovernmentPartyCaucus { TargetInterval = consent.ExpiresInterval }));

        private static string DescribePetitionConsent(GovernmentPetition petition, GovernmentPetitionConsent consent, int interval, string expiry)
        {
            if (consent.Revision != petition.Revision) return "Earlier revision; fresh consent is required for the current terms.";
            if (interval >= consent.ExpiresInterval) return "Consent expired at " + expiry + ". Recorded services remain in the history.";
            if (consent.Operation == "sponsor" && consent.ExpiresInterval <= interval + 1 && petition.BusinessId.Length == 0)
                return "Expires at " + expiry + ". Renew sponsorship before seeking votes for the next selection.";
            return "Consent current; expires at " + expiry + ".";
        }

        private static GovernmentPetitionConsent PersonalPetitionSponsor(GovernmentPetition petition, int interval, IEnumerable<string> members) =>
            petition.Consents.FirstOrDefault(c => c.Operation == "sponsor" && c.Revision == petition.Revision
                && interval < c.ExpiresInterval && members.Contains(c.ActorId));

        private static JArray PetitionParticipants(JArray original, string petitioner, string sponsor, string filingSpeaker)
        {
            var participants = new JArray();
            foreach (var person in new[] { new[] { petitioner, "petitioner" }, new[] { sponsor, "sponsor" }, new[] { filingSpeaker, "filing_speaker" } })
                if (!string.IsNullOrWhiteSpace(person[0])) participants.Add(new JObject { ["heroId"] = person[0], ["role"] = person[1] });
            foreach (var participant in original)
            {
                string role = participant.Value<string>("role");
                if (role != "petitioner" && role != "sponsor" && role != "filing_speaker") participants.Add(participant.DeepClone());
            }
            return participants;
        }

        private void RepairPendingPetitionSponsor(GovernmentPetition petition, ReignGovernmentBusinessRecord record)
        {
            // Repair attribution only before a decision or consequence. The filing
            // speaker comes from this record, never from today's office holder.
            if (record.FrozenAuthorityLevel <= 0 || record.ExecutionApplied || record.ConsequencesApplied
                || (record.Status != "presented" && record.Status != "hearing") || CurrentDay() >= record.DecisionDay) return;
            int interval = GovernmentInterval(record.PresentationDay);
            var old = petition.Consents.Where(c => c.Operation == "sponsor" && c.Revision == petition.Revision
                && c.ActorId == record.SponsorHeroStringId).ToList();
            string sponsorId = record.SponsorHeroStringId;
            if (old.Any(c => c.ExpiresInterval <= interval) && !old.Any(c => interval < c.ExpiresInterval))
            {
                var members = JArray.Parse(record.FrozenSeatsJson).Select(x => x.Value<string>("memberHeroId")).ToList();
                sponsorId = PersonalPetitionSponsor(petition, interval, members)?.ActorId ?? sponsorId;
            }
            var original = JArray.Parse(record.ParticipantsJson);
            string filingSpeaker = original.OfType<JObject>().FirstOrDefault(x => x.Value<string>("role") == "filing_speaker")?.Value<string>("heroId");
            string participants = PetitionParticipants(original, petition.AuthorId, sponsorId, filingSpeaker).ToString(Formatting.None);
            if (record.PetitionerHeroStringId != petition.AuthorId || record.SponsorHeroStringId != sponsorId || record.ParticipantsJson != participants)
            {
                record.PetitionerHeroStringId = petition.AuthorId;
                record.SponsorHeroStringId = sponsorId;
                record.ParticipantsJson = participants;
                ChangedBusiness(record);
            }
            // Refresh only a presentation already awaiting publication. The ledger
            // never rewrites an archived edition or creates missing historical news.
            if (_governmentNews.Pending.SelectMany(x => x.Kingdoms).SelectMany(x => x.Items)
                .Any(x => x.ReceiptId == record.BusinessId + ":presented")) RecordGovernmentPresentationNews(record);
        }

        private bool FileAdoptedPetition(GovernmentPetition petition, Hero filer, out string result)
        {
            result = "The adopted petition is already filed.";
            if (petition.BusinessId.Length > 0) return true;
            if (!ValidatePetitionTerms(petition, out result)) { petition.Status = "blocked"; petition.BlockReason = result; return false; }
            string businessId = "";
            _filingPetition = petition;
            try
            {
                bool created = petition.Kind == "seasonal" ? CreatePartyResolutionBusiness(petition, filer, out businessId, out result)
                    : petition.Kind == "admission" || petition.Kind == "recall"
                    ? CreateMandateBusiness(petition, filer, out businessId, out result)
                    : CreateGovernmentBusiness(petition.KingdomId, petition.ProposalId, filer, out businessId, out result);
                if (!created) { petition.Status = "blocked"; petition.BlockReason = result; return false; }
            }
            finally { _filingPetition = null; }
            var record = _business.Single(x => x.BusinessId == businessId);
            int sponsorshipInterval = GovernmentInterval(_participationSessionDay >= 0 ? _participationSessionDay : CurrentDay());
            var personalSponsor = PersonalPetitionSponsor(petition, sponsorshipInterval, GetSeats(petition.KingdomId)
                .Where(x => GetParticipationAccess(petition.KingdomId, FindHero(x.HeroStringId)).Member).Select(x => x.HeroStringId));
            record.PetitionerHeroStringId = petition.AuthorId; record.SponsorHeroStringId = personalSponsor?.ActorId ?? filer.StringId;
            record.IntroductionPartyId = petition.AdoptedPartyId;
            var participants = PetitionParticipants(JArray.Parse(record.ParticipantsJson), petition.AuthorId, record.SponsorHeroStringId, filer.StringId);
            record.ParticipantsJson = participants.ToString(Formatting.None); ChangedBusiness(record);
            petition.BusinessId = businessId; petition.Status = "scheduled";
            InitializeBusinessSchedule(record);
            if (_participationSessionDay >= 0)
            { record.PresentationDay = _participationSessionDay; record.DecisionDay = NextGovernmentSessionDay(_participationSessionDay); ChangedBusiness(record); }
            AssignDocket(record);
            result = "The adopted petition is scheduled for presentation, then decision at the following regular session."; return true;
        }

        private bool CanFileGovernmentPetition(string kingdomId, Hero actor) => actor != null && (actor == FindKingdom(kingdomId)?.Leader
            || _filingPetition != null && _filingPetition.KingdomId == kingdomId && _filingPetition.AdoptedPartyId.Length > 0
                && GetParticipationAccess(kingdomId, actor).Speaker && GetSeats(kingdomId).Any(x => x.HeroStringId == actor.StringId && x.PartyId == _filingPetition.AdoptedPartyId));

        private bool CreatePartyResolutionBusiness(GovernmentPetition petition, Hero filer, out string businessId, out string result)
        {
            businessId = ""; result = "The adopted resolution is no longer eligible.";
            var kingdom = FindKingdom(petition.KingdomId); var party = FindParty(petition.KingdomId, petition.AdoptedPartyId);
            var template = ReignGovernmentResolutionCatalog.Find(petition.ProposalId.Substring(9));
            if (party == null || template == null || !ReignGovernmentResolutionCatalog.IsEligible(template, CollectEvidenceTags(kingdom), ParsePlanks(party.PlanksCsv))) return false;
            if (!CanPrepareSeasonalPrisonerTarget(kingdom, template))
            { result = "This remedy requires an existing prisoner. No replacement target has been selected."; return false; }
            var resolution = CreateResolution(kingdom, GetGovernment(kingdom), party, template, CurrentDay());
            _resolutions.Add(resolution); ImportSeasonalBusiness();
            var record = _business.FirstOrDefault(x => x.Kind == "seasonal" && x.TargetId == resolution.ResolutionId);
            if (record == null) return false;
            businessId = record.BusinessId; result = "The party's adopted resolution is filed."; return true;
        }

        private int MandateOptionInterest(ReignGovernmentBusinessRecord record, Hero member)
        {
            var petition = _participation.Petitions.FirstOrDefault(x => x.BusinessId == record.BusinessId);
            var candidate = FindHero(record.Kind == "admission" ? petition?.CandidateId : petition?.PreviousIncumbentId);
            if (candidate == null) return 0;
            int interest = KnownCandidateAlignment(record.KingdomStringId, member, candidate.StringId) / 2
                + Math.Max(-15, Math.Min(15, member.GetRelation(candidate) / 5));
            return record.Kind == "admission" ? interest : -interest;
        }

        private bool CreateMandateBusiness(GovernmentPetition petition, Hero filer, out string businessId, out string result)
        {
            businessId = "";
            if (!ValidatePetitionTerms(petition, out result)) return false;
            var record = new ReignGovernmentBusinessRecord { KingdomStringId = petition.KingdomId, Kind = petition.Kind,
                Title = petition.Title, Summary = petition.Terms, PetitionerHeroStringId = petition.AuthorId, SponsorHeroStringId = filer.StringId,
                IntroductionPartyId = petition.AdoptedPartyId, TargetId = petition.ConstituencyId, Status = "queued", CreatedDay = CurrentDay(),
                RequestedOptionId = "accept", StatusQuoOptionId = "reject", ActionPayloadJson = new JObject { ["petitionId"] = petition.Id, ["revision"] = petition.Revision }.ToString(Formatting.None),
                OptionsJson = new JArray(new JObject { ["id"] = "accept", ["label"] = petition.Kind == "admission" ? "Grant the civic mandate" : "Recall the representative", ["affirmative"] = true },
                    new JObject { ["id"] = "reject", ["label"] = "Retain current representation", ["affirmative"] = false }).ToString(Formatting.None),
                ParticipantsJson = new JArray(new JObject { ["heroId"] = petition.AuthorId, ["role"] = "petitioner" },
                    new JObject { ["heroId"] = petition.PreviousIncumbentId, ["role"] = "affected_member" }).ToString(Formatting.None) };
            _business.Add(record); ChangedBusiness(record); businessId = record.BusinessId; result = "Mandate petition filed."; return true;
        }
        private bool ValidateMandateBusiness(ReignGovernmentBusinessRecord record, out string result)
        {
            var payload = JObject.Parse(record.ActionPayloadJson);
            var petition = _participation.Petitions.FirstOrDefault(x => x.Id == payload.Value<string>("petitionId") && x.Revision == payload.Value<long>("revision"));
            result = "The original mandate petition is unavailable.";
            if (petition == null || !ValidatePetitionTerms(petition, out result)) return false;
            if (record.FrozenAuthorityLevel == 0)
            {
                var eligible = EligibleMandateEndorsers(petition);
                if (eligible.Count != petition.FrozenEndorsers.Count || !GovernmentParticipationRules.MandateSupported(petition, eligible, GovernmentInterval(CurrentDay()), petition.Kind == "recall"))
                { petition.Status = "blocked"; petition.BlockReason = result = "Local eligibility or required consent changed before presentation. Withdraw and revise this application."; return false; }
            }
            return true;
        }
        private bool ExecuteMandateBusiness(ReignGovernmentBusinessRecord record, string optionId, out string result)
        {
            if (!ValidateMandateBusiness(record, out result)) return false;
            var petition = _participation.Petitions.Single(x => x.BusinessId == record.BusinessId);
            if (optionId == "accept")
            {
                var seat = _participation.Constituencies.Single(x => x.KingdomId == record.KingdomStringId && x.Id == record.TargetId);
                _seats.RemoveAll(x => x.KingdomStringId == record.KingdomStringId && x.HeroStringId == seat.IncumbentId);
                seat.IncumbentId = record.Kind == "admission" ? petition.CandidateId : "";
                seat.Mandate = record.Kind == "admission" ? "civic" : "vacant";
                seat.AdmissionPetitionId = record.Kind == "admission" ? petition.Id : "";
                seat.AccreditedDay = CurrentDay(); seat.AccreditedInterval = GovernmentInterval(CurrentDay());
                ReconcileMembership(FindKingdom(record.KingdomStringId), GetGovernment(record.KingdomStringId), false);
            }
            FinishBusiness(record, optionId, false); result = record.Outcome; return true;
        }
    }
}
