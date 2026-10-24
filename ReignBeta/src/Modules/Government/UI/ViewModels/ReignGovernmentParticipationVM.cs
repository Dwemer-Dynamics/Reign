using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignGovernmentScreenVM
    {
        private string _participationMode = "", _proposalId = "", _proposalArgument = "";
        private string _participationHeading = "", _participationExplanation = "", _participationListHeading = "", _participationPrimary = "";
        private Action _participationAction;
        [DataSourceProperty] public string ParticipationHeading => _participationHeading;
        [DataSourceProperty] public string ParticipationExplanation => _participationExplanation;
        [DataSourceProperty] public string ParticipationListHeading => _participationListHeading;
        [DataSourceProperty] public string ParticipationPrimaryText => _participationPrimary;
        [DataSourceProperty] public bool HasParticipationPrimary => _participationAction != null;
        [DataSourceProperty] public string ProposalArgument { get => _proposalArgument; set { _proposalArgument = value ?? ""; OnPropertyChangedWithValue(_proposalArgument); } }
        [DataSourceProperty] public bool ShowProposalArgument => IsPolicyView && (_participationMode == "prepare_review" || _participationMode == "revise_review");
        [DataSourceProperty] public bool CanPrepareProposal => _behavior.GetParticipationAccess(_kingdom.StringId, Hero.MainHero).Prepare;
        [DataSourceProperty] public string ParticipationRoleText => _behavior.IsCharterPending(_kingdom.StringId) ? "FOUNDING CHARTER REQUIRED"
            : IsInteractive ? "RULER" : IsPlayerMember ? _behavior.GovernmentOfficeTitle(Hero.MainHero?.StringId).ToUpperInvariant() : "PETITIONER / PUBLIC RECORD";
        [DataSourceProperty] public bool CanMemberBallot => !_sending && _selected != null && _behavior.GetParticipationAccess(_kingdom.StringId, Hero.MainHero, _selected.BusinessId).Ballot;
        [DataSourceProperty] public bool IsPlayerMember => _behavior.GetParticipationAccess(_kingdom.StringId, Hero.MainHero).Member;
        [DataSourceProperty] public bool CharterPending => _behavior.IsCharterPending(_kingdom.StringId);
        public bool ConfirmCloseForMemberBallot(Action closeAfterAbstain)
        {
            if (_selected == null || !_behavior.NeedsPlayerMemberBallot(_selected.BusinessId)) return true;
            string businessId = _selected.BusinessId;
            InformationManager.ShowInquiry(new InquiryData("Leave the decision hearing?",
                "Your ballot has not been recorded. You can abstain and close, or return to cast your vote.",
                true, true, "Abstain and close", "Return to hearing", () =>
                {
                    bool submitted = _behavior.SubmitMemberBallot(businessId, Hero.MainHero, "", true, out string result);
                    RefreshAll(result);
                    if (submitted) closeAfterAbstain?.Invoke();
                }, null), true);
            return false;
        }
        public void ExecuteParticipationPrimary() { var action = _participationAction; action?.Invoke(); }
        public void ExecuteAbstain()
        {
            if (!CanMemberBallot) return;
            _behavior.SubmitMemberBallot(_selected.BusinessId, Hero.MainHero, "", true, out string result); RefreshAll(result);
        }
        private void ParticipationPage(string mode, string heading, string explanation, string listHeading, string primary = "", Action action = null)
        {
            _participationMode = mode; _participationHeading = heading; _participationExplanation = explanation;
            _participationListHeading = listHeading; _participationPrimary = primary; _participationAction = action;
            PolicyChoices.Clear(); _policyListRevision++; _view = "policy";
        }
        private void ParticipationChoice(string id, string title, string detail, Action<string> choose = null, bool enabled = true)
        {
            PolicyChoices.Add(new ReignGovernmentPolicyChoiceVM(new JObject { ["proposalId"] = id, ["label"] = title,
                ["description"] = detail, ["available"] = enabled }, choose ?? (_ => { })));
        }
        public void ExecuteFoundingCharter()
        {
            var charter = _behavior.GetConstitution(_kingdom.StringId);
            if (charter?.Confirmed != false || _kingdom.Leader != Hero.MainHero) return;
            ParticipationPage("charter_institution", "FOUNDING CHARTER", "Choose who represents your new kingdom. Culture recommends a system; it does not restrict your choice. Your unfinished charter is saved.", "CHOOSE AN INSTITUTION");
            foreach (var choice in _behavior.GetFoundingInstitutionChoices(_kingdom.StringId).OfType<JObject>())
                ParticipationChoice(choice.Value<string>("id"), choice.Value<string>("label"), choice.Value<string>("description") + "\n"
                    + choice.Value<int>("memberCount") + " eligible founding members. Title: " + choice.Value<string>("title"), id => {
                        if (!_behavior.SetCharterDraft(_kingdom.StringId, id, charter.DraftAuthority, Hero.MainHero, false, out string result))
                        { StatusText = result; return; }
                        ShowCharterAuthority(); });
            RaiseAll();
        }
        public void ExecuteResumeFoundingCharter()
        {
            var charter = _behavior.GetConstitution(_kingdom.StringId);
            if (charter?.Confirmed != false || _kingdom.Leader != Hero.MainHero || charter.Source != "player_foundation") return;
            var institution = _behavior.GetFoundingInstitutionChoices(_kingdom.StringId).OfType<JObject>()
                .FirstOrDefault(x => x.Value<string>("id") == charter.InstitutionId);
            if (institution == null) { ExecuteFoundingCharter(); return; }
            if (!GovernmentParticipationRules.CanConfirmCharter(charter.DraftAuthority, institution.Value<int>("memberCount")))
            {
                ShowCharterAuthority();
                StatusText = "A members-only decision requires at least one eligible member. Choose another institution or division of authority.";
                return;
            }
            // Resume only reads the saved draft. Choosing or confirming is a separate action.
            ShowCharterReview();
        }
        private void ShowCharterAuthority()
        {
            var charter = _behavior.GetConstitution(_kingdom.StringId);
            var institution = _behavior.GetFoundingInstitutionChoices(_kingdom.StringId).OfType<JObject>().Single(x => x.Value<string>("id") == charter.InstitutionId);
            int members = institution.Value<int>("memberCount");
            ParticipationPage("charter_authority", "FOUNDING AUTHORITY", institution.Value<string>("label") + ". Review the Crown share and the share divided among actual members. Empty shares are never added to the Crown.", "CHOOSE AUTHORITY", "Change institution", ExecuteFoundingCharter);
            for (int level = 1; level <= 5; level++)
            {
                int chosen = level, crown = GovernmentSessionRules.CrownWeight(level);
                bool available = GovernmentParticipationRules.CanConfirmCharter(level, members);
                ParticipationChoice(level.ToString(), CharterDecisionRule(level) + (available ? "" : " — unavailable"),
                    "Crown " + crown + "% / members " + (100 - crown) + "%. " + members + " eligible members. " + (members == 0 ? (100 - crown) + "% remains unallocated." : GovernmentSessionRules.MemberWeight(level, members).ToString("0.###") + "% per member on new matters.")
                    + (level == 5 ? " The ruler can recommend and campaign; only members vote." : " Ordinary decisions follow the weighted vote.")
                    + (available ? "" : " Requires at least one eligible member. Choose another institution or division of authority."), ignored => {
                        if (!_behavior.SetCharterDraft(_kingdom.StringId, charter.InstitutionId, chosen, Hero.MainHero, false, out string result))
                        { StatusText = result; return; }
                        ShowCharterReview(); }, available);
            }
            RaiseAll();
        }
        private void ShowCharterReview()
        {
            var charter = _behavior.GetConstitution(_kingdom.StringId);
            var institution = _behavior.GetFoundingInstitutionChoices(_kingdom.StringId).OfType<JObject>().Single(x => x.Value<string>("id") == charter.InstitutionId);
            ParticipationPage("charter_review", "REVIEW THE CHARTER", institution.Value<string>("label") + "\n" + CharterDecisionRule(charter.DraftAuthority)
                + ". Crown " + GovernmentSessionRules.CrownWeight(charter.DraftAuthority) + "%; members " + (100 - GovernmentSessionRules.CrownWeight(charter.DraftAuthority)) + "%. " + MeetingText + ".\nConfirm once to establish your constitution. Later authority changes follow the constitutional process.",
                "FOUNDING REPRESENTATIVES", "Confirm charter", () => {
                    _behavior.SetCharterDraft(_kingdom.StringId, charter.InstitutionId, charter.DraftAuthority, Hero.MainHero, true, out string result);
                    RefreshAll(result); if (!_behavior.IsCharterPending(_kingdom.StringId)) ExecuteBusinessTab(); });
            foreach (var member in (institution["members"] as JArray ?? new JArray()).OfType<JObject>())
                ParticipationChoice(member.Value<string>("heroId"), member.Value<string>("name"), member.Value<string>("constituency") + ". Holds one constitutional seat. Party placement follows personal political beliefs.", enabled: false);
            if (PolicyChoices.Count == 0) ParticipationChoice("empty", "No eligible founding members", "The constitution creates no fictitious voters. Its member share remains unallocated until constituencies become eligible.", enabled: false);
            RaiseAll();
        }
        private void ShowPrepareProposal()
        {
            if (!CanPrepareProposal) { StatusText = _behavior.GetParticipationAccess(_kingdom.StringId, Hero.MainHero).Reason; return; }
            ParticipationPage("prepare_kind", "PREPARE A PROPOSAL", "Choose an actual law or civic mandate. Review its exact scope, costs and effects before saving a private draft. Sponsorship and party selection come afterward.", "WHAT DO YOU WANT TO PROPOSE?");
            foreach (var kind in new[] { new[] { "edict", "Settlement edict", "The eight Reign edicts, with their real costs, durations and tradeoffs." },
                new[] { "policy", "Kingdom policy", "Enact or repeal an actual native policy with its existing effects." }, new[] { "admission", "Civic membership", "Apply to represent an existing constituency." },
                new[] { "recall", "Recall a representative", "Seek a separate lawful vote on an occupied local constituency." } })
                ParticipationChoice(kind[0], kind[1], kind[2], ShowProposalTargets);
            if (IsInteractive) ParticipationChoice("crown", "Crown matters", "Existing fief reviews and clan expulsion recommendations use the ordinary Crown slot.", ShowProposalTargets);
            RaiseAll();
        }
        private void ShowProposalTargets(string kind)
        {
            ParticipationPage("prepare_target", "SELECT EXACT TERMS", "Choose the named target and enactment or repeal. Each card shows the actual effects and restrictions. Free text can explain your case; it cannot alter the law's mechanics.", "AVAILABLE " + kind.ToUpperInvariant() + " PROPOSALS", "Change proposal type", ShowPrepareProposal);
            var choices = kind == "crown" && IsInteractive ? _behavior.GetGovernmentProposalChoices(_kingdom.StringId) : _behavior.GetParticipationProposalChoices(_kingdom.StringId, Hero.MainHero);
            foreach (var row in choices.OfType<JObject>().Where(x => kind == "crown" ? x.Value<string>("proposalId").StartsWith("fief:", StringComparison.Ordinal) || x.Value<string>("proposalId").StartsWith("expel:", StringComparison.Ordinal) :
                kind == "edict" ? x.Value<string>("category") == "edict" : x.Value<string>("proposalId").StartsWith(kind + ":", StringComparison.Ordinal)))
                PolicyChoices.Add(new ReignGovernmentPolicyChoiceVM(row, ShowProposalReview));
            if (PolicyChoices.Count == 0) ParticipationChoice("empty", "No eligible targets", "Your government, territory and current law do not offer this route. Clan representation requires qualifying landholding leadership.", enabled: false);
            RaiseAll();
        }
        private void ShowProposalReview(string proposalId)
        {
            _proposalId = proposalId;
            if (IsInteractive && (proposalId.StartsWith("fief:", StringComparison.Ordinal) || proposalId.StartsWith("expel:", StringComparison.Ordinal)))
            {
                var crownChoice = _behavior.GetGovernmentProposalChoices(_kingdom.StringId).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == proposalId);
                if (crownChoice == null) return;
                ParticipationPage("crown_review", "REVIEW CROWN MOTION", "File this named recommendation through the ordinary Crown slot. The weighted vote will decide it at the following session.", "EXACT PROPOSED TERMS", "File Crown motion", () => {
                    _behavior.CreateGovernmentBusiness(_kingdom.StringId, proposalId, Hero.MainHero, out string businessId, out string result); RefreshAll(result); if (businessId.Length > 0) SelectBusinessById(businessId); });
                ParticipationChoice(proposalId, crownChoice.Value<string>("label"), crownChoice.Value<string>("description"), enabled:false); RaiseAll(); return;
            }
            var row = _behavior.GetParticipationProposalChoices(_kingdom.StringId, Hero.MainHero).OfType<JObject>().FirstOrDefault(x => x.Value<string>("proposalId") == proposalId);
            if (row == null) { ShowPrepareProposal(); StatusText = "That proposal is no longer available."; return; }
            ParticipationPage("prepare_review", "REVIEW YOUR PETITION", "Saving preserves a private draft. It spends no money or influence and reserves no agenda slot. Add an optional argument below.", "EXACT PROPOSED TERMS", "Save private draft", () => {
                bool saved = _behavior.PrepareGovernmentPetition(_kingdom.StringId, _proposalId, ProposalArgument, Hero.MainHero, out string id, out string result);
                StatusText = result; if (saved) ShowPetition(id); });
            ParticipationChoice(proposalId, row.Value<string>("label"), row.Value<string>("description"), enabled: false);
            RaiseAll();
        }
        public void ExecuteMyPetitions()
        {
            ParticipationPage("petitions", "MY PROPOSALS", "Track your drafts, sponsors, party selection and public decisions. Private promises do not guarantee any vote.", "YOUR PETITIONS", "Prepare a proposal", CanPrepareProposal ? (Action)ShowPrepareProposal : null);
            ParticipationChoice("representation", "My office and representation", "Review your constituency, party, current and selected-matter voting shares, and territorial accreditation.", ignored => ExecuteRepresentation());
            foreach (var petition in _behavior.GetPetitions(_kingdom.StringId, Hero.MainHero).Where(x => x.AuthorId == Hero.MainHero.StringId))
                ParticipationChoice(petition.Id, petition.Title, PetitionStatusText(petition) + " · revision " + petition.Revision + "\n" + petition.BlockReason, ShowPetition);
            if (PolicyChoices.Count == 1) ParticipationChoice("empty", "No petitions yet", "Prepare a measure, then approach a member privately for sponsorship.", enabled: false);
            RaiseAll();
        }
        public void ExecuteRepresentation()
        {
            bool clanSeats = _behavior.RecognizesClanSeats(_kingdom.StringId);
            Action request = clanSeats ? (Action)(() => {
                _behavior.RecognizeClanSeat(_kingdom.StringId, Hero.MainHero, out string result);
                ExecuteRepresentation(); StatusText = result;
            }) : () => ShowProposalTargets("admission");
            ParticipationPage("representation", "OFFICE & REPRESENTATION", "Constituencies create seats. New territory must remain eligible for a day, then accredit at the next regular session. Existing matters keep their frozen roll.",
                "CURRENT AND PENDING SEATS", clanSeats ? "Recognize my clan seat" : "Apply for a mandate", CanPrepareProposal ? request : null);
            ParticipationChoice("my_office", "Your current role", _behavior.ParticipationSummary(_kingdom.StringId, Hero.MainHero, _selected?.BusinessId), enabled: false);
            foreach (var row in _behavior.Participation.Constituencies.Where(x => x.KingdomId == _kingdom.StringId)
                .OrderBy(x => x.Status == "pending" ? 0 : x.Status == "active" ? 1 : 2).ThenBy(x => _behavior.ConstituencyLabel(x)))
                ParticipationChoice(row.Id, _behavior.ConstituencyLabel(row) + " · " + Friendly(row.Status),
                    (FindHero(row.IncumbentId)?.Name?.ToString() ?? "Vacant") + " · " + Friendly(row.Mandate) + "\n"
                    + (row.Status == "pending" ? "Awaiting a full stable day and the next session. This seat adds no vote to existing matters."
                        : row.Status == "suspended" ? "The old government's entitlement is suspended. Frozen shares abstain."
                        : "One constitutional seat. Its current holder does not inherit another member's past ballots."), enabled: false);
            RaiseAll();
        }
        private static string CharterDecisionRule(int authority)
        {
            switch (authority)
            {
                case 1: return "Crown decides";
                case 2: return "Crown leads the vote";
                case 3: return "Equal Crown and chamber shares";
                case 4: return "Chamber leads the vote";
                default: return "Members decide";
            }
        }
        private string PetitionStatusText(GovernmentPetition petition)
        {
            string edictOutcome = _behavior.PetitionEdictOutcomeText(petition);
            return edictOutcome.Length > 0 ? edictOutcome : Friendly(petition.Status);
        }
        private void ShowPetition(string id)
        {
            var petition = _behavior.GetPetitions(_kingdom.StringId, Hero.MainHero).FirstOrDefault(x => x.Id == id); if (petition == null) return;
            bool draft = petition.Status == "draft" || petition.Status == "blocked";
            ParticipationPage("petition_detail", "PETITION RECORD", PetitionStatusText(petition) + " · Revision " + petition.Revision + "\n" + petition.BlockReason
                + "\nRequest sponsorship in an individual conversation using Actions → Support. No negotiation occurs during a session.", "TERMS AND RECORDED SUPPORT", draft ? "Submit for sponsorship" : "Back to my proposals", draft ? (Action)(() => {
                    _behavior.SubmitGovernmentPetition(id, Hero.MainHero, out string result); StatusText = result; ShowPetition(id); }) : ExecuteMyPetitions);
            ParticipationChoice("terms", petition.Title, petition.Terms + "\nArgument: " + petition.Argument, enabled: false);
            foreach (var consent in petition.Consents)
                ParticipationChoice(consent.ReceiptId, FindHero(consent.ActorId)?.Name?.ToString() ?? "Former participant", Friendly(consent.Operation) + " for revision " + consent.Revision + ". "
                    + _behavior.PetitionConsentValidity(petition, consent) + "\nThis records the specific service, not a successful vote.", enabled: false);
            var access = _behavior.GetParticipationAccess(_kingdom.StringId, Hero.MainHero);
            if (access.Member && !draft && !GovernmentParticipationRules.Closed(petition.Status) && petition.BusinessId.Length == 0)
            {
                ParticipationChoice("personal_sponsor", "Record or renew my sponsorship", "Record your own sponsorship through the next selection. It does not consume the party slot or guarantee adoption.", ignored => {
                    _behavior.ApplyPetitionAction("sponsor", id, petition.Revision, Hero.MainHero, Hero.MainHero, "member:" + id + ":" + petition.Revision, out string result); StatusText = result; ShowPetition(id); });
                if (access.Speaker) ParticipationChoice("nomination", "Enter this petition for party selection", "Your party must give it majority support before it receives the next public slot.", ignored => {
                    _behavior.ApplyPetitionAction("seek_adoption", id, petition.Revision, Hero.MainHero, Hero.MainHero, "speaker:" + id + ":" + petition.Revision, out string result); StatusText = result; ShowPetition(id); });
            }
            if (IsInteractive && !GovernmentParticipationRules.Closed(petition.Status) && !draft && petition.BusinessId.Length == 0)
                ParticipationChoice("crown", "Adopt through the Crown slot", "The ruler may use the ordinary Crown introduction. The matter is decided the following session.", ignored => {
                    _behavior.ApplyPetitionAction("seek_adoption", id, petition.Revision, Hero.MainHero, Hero.MainHero, "crown:" + id + ":" + petition.Revision, out string result); StatusText = result; ShowPetition(id); });
            if (!GovernmentParticipationRules.Closed(petition.Status)) ParticipationChoice("withdraw", "Withdraw this petition", "Withdraw before a recorded decision. Agreements use their existing cancellation and refund rules.", ignored => {
                _behavior.WithdrawGovernmentPetition(id, Hero.MainHero, out string result); StatusText = result; ShowPetition(id); });
            if (petition.BusinessId.Length == 0 && !GovernmentParticipationRules.Closed(petition.Status))
                ParticipationChoice("revise", "Review current terms as a new revision", "Refresh eligibility and terms, invalidate old consents, and save a new private draft for fresh sponsorship.", ignored => {
                    ProposalArgument = petition.Argument;
                    ParticipationPage("revise_review", "REVISE YOUR PETITION", "Edit your argument below. Saving refreshes current terms and clears all earlier consents. Fresh sponsorship is required.", "REVIEW BEFORE REVISING", "Save new revision", () => {
                        _behavior.ReviseGovernmentPetition(id, ProposalArgument, Hero.MainHero, out string result); StatusText = result; ShowPetition(id); });
                    ParticipationChoice("revision_terms", petition.Title, petition.Terms, enabled: false); RaiseAll(); });
            RaiseAll();
        }
        public void ExecutePartyBusiness()
        {
            ParticipationPage("party", "PARTY BUSINESS", "A speaker coordinates and files. A majority of the frozen caucus chooses the party's public nomination. Leadership elections use members' votes without any Crown share.", "NOMINATIONS AND LEADERSHIP");
            var seat = _behavior.GetSeats(_kingdom.StringId).FirstOrDefault(x => x.HeroStringId == Hero.MainHero?.StringId);
            if (seat == null) ParticipationChoice("none", "A government seat is required", "A civic mandate or qualifying clan seat grants participation. Prepare a civic application or review your clan's constitutional entitlement.", enabled: false);
            else
            {
                var constituency = _behavior.Participation.Constituencies.FirstOrDefault(x => x.KingdomId == _kingdom.StringId && x.IncumbentId == Hero.MainHero.StringId && x.Status == "active");
                if (constituency?.Mandate == "civic") ParticipationChoice("resign_review", "Review resignation", "Resignation vacates your civic mandate; it never passes to your heir.", ignored => {
                    ParticipationPage("resign", "RESIGN YOUR MANDATE", "You will lose this office and its future voting rights. Prior votes remain in the record. Reappointment requires the normal admission process.", "REVIEW RESIGNATION", "Keep my mandate", ExecutePartyBusiness);
                    ParticipationChoice("resign_confirm", "Confirm resignation", "Vacate your own civic mandate.", unused => { _behavior.ResignCivicMandate(_kingdom.StringId, Hero.MainHero, out string result); StatusText = result; RefreshAll(result); ExecutePartyBusiness(); }); RaiseAll(); });
                foreach (var caucus in _behavior.Participation.Caucuses.Where(x => x.KingdomId == _kingdom.StringId && x.PartyId == seat.PartyId && !x.Closed))
                foreach (string id in caucus.Nominations)
                {
                    var petition = _behavior.Participation.Petitions.First(x => x.Id == id);
                    bool selected = caucus.Endorsements.TryGetValue(Hero.MainHero.StringId, out string endorsed) && endorsed == id
                        && caucus.EndorsementRevisions.TryGetValue(Hero.MainHero.StringId, out long ownRevision) && ownRevision == petition.Revision;
                    bool eligible = _behavior.CheckNominationSupport(Hero.MainHero.StringId, id, petition.Revision,
                        GovernmentParticipationRules.CaucusId(caucus), out string reason);
                    int supporters = caucus.Endorsements.Count(x => x.Value == id && caucus.Roll.Contains(x.Key)
                        && caucus.EndorsementRevisions.TryGetValue(x.Key, out long revision) && revision == petition.Revision
                        && _behavior.CheckNominationSupport(x.Key, id, revision, GovernmentParticipationRules.CaucusId(caucus), out _));
                    ParticipationChoice(id, (selected ? "Your endorsement · " : "Endorse · ") + petition.Title,
                        petition.Terms + "\nSelection: " + ReignGovernmentCampaignBehavior.CaucusSessionLabel(caucus)
                        + ". Recorded support: " + supporters + " of " + caucus.Roll.Count + " frozen members; " + (caucus.Roll.Count / 2 + 1) + " required."
                        + (selected ? " Your choice is recorded and may change before selection." : "")
                        + (eligible ? "\nSeek other members' nomination votes privately through Actions → Support." : "\n" + reason), ignored => {
                            _behavior.SelectPartyNomination(id, Hero.MainHero, out string result); StatusText = result; ExecutePartyBusiness(); }, enabled: eligible);
                }
                foreach (var caucus in _behavior.Participation.Caucuses.Where(x => x.KingdomId == _kingdom.StringId && x.PartyId == seat.PartyId && x.Closed)
                    .OrderByDescending(x => x.TargetInterval).Take(3))
                {
                    string selection = ReignGovernmentCampaignBehavior.CaucusSessionLabel(caucus);
                    bool recorded = caucus.SelectionBallots.Count > 0;
                    foreach (string id in caucus.Nominations)
                    {
                        var petition = _behavior.Participation.Petitions.FirstOrDefault(x => x.Id == id);
                        bool adopted = caucus.AdoptedPetitionId == id;
                        string tally = recorded ? caucus.SelectionBallots.Count(x => x.Eligible && x.PetitionId == id) + " of " + caucus.Roll.Count + " frozen votes."
                            : "The ballot breakdown was not recorded for this older selection.";
                        string next = adopted ? string.IsNullOrWhiteSpace(petition?.BusinessId)
                            ? petition == null ? "The selected petition's filing record is unavailable. No public docket is linked to this older selection."
                                : petition.AuthorId == Hero.MainHero.StringId ? "No public docket is linked. Review My proposals for this petition's recorded status."
                                : "No public docket is linked to this petition. Its filing outcome is not recorded here."
                            : "Public matter: " + _behavior.DocketLabel(petition.BusinessId) + ". Review the Agenda or Decisions."
                            : "Not selected for the party's public slot.";
                        ParticipationChoice("selection:" + GovernmentParticipationRules.CaucusId(caucus) + ":" + id,
                            (adopted ? "Selected · " : "Not selected · ") + (petition?.Title ?? "Former petition"),
                            "Completed party selection: " + selection + ". " + tally + "\n" + next, enabled: false);
                    }
                    if (recorded)
                    {
                        int abstentions = caucus.SelectionBallots.Count(x => x.Eligible && string.IsNullOrWhiteSpace(x.PetitionId));
                        ParticipationChoice("selection:" + GovernmentParticipationRules.CaucusId(caucus) + ":abstentions", "Selection attendance · " + selection,
                            abstentions + (abstentions == 1 ? " abstention; " : " abstentions; ")
                            + caucus.SelectionBallots.Count(x => !x.Eligible) + " ineligible frozen members. The selection record is final.", enabled: false);
                    }
                }
                ParticipationChoice("nominate", "Stand for party speaker", "Requires a completed membership interval and two nominations, or your own in a one-person party. Seek other nominations privately.", ignored => {
                    _behavior.NominatePartyLeader(_kingdom.StringId, Hero.MainHero.StringId, Hero.MainHero, out string result); StatusText = result; ExecutePartyBusiness(); });
                foreach (var election in _behavior.Participation.Elections.Where(x => x.KingdomId == _kingdom.StringId && x.PartyId == seat.PartyId && !x.Closed))
                foreach (var nominee in election.Nominations)
                {
                    string candidate = nominee.Key;
                    ParticipationChoice(candidate, "Vote for " + FindHero(candidate)?.Name, nominee.Value.Distinct().Count() + " nominations. A majority replaces the incumbent; a tied challenge retains them.", ignored => {
                        _behavior.VotePartyLeader(_kingdom.StringId, candidate, Hero.MainHero, out string result); StatusText = result; ExecutePartyBusiness(); });
                }
                foreach (var party in _behavior.GetParties(_kingdom.StringId).Where(x => x.PartyId != seat.PartyId))
                    ParticipationChoice("join:" + party.PartyId, "Join " + party.Name, party.PlanksCsv.Replace(',', ' ') + ". Existing binding commitments must still be honored.", ignored => {
                        _behavior.ChangePlayerParty(_kingdom.StringId, party.PartyId, out string result); StatusText = result; ExecutePartyBusiness(); });
            }
            RaiseAll();
        }
        public void ExecutePoliticalPriorities()
        {
            ParticipationPage("priorities", "MY POLITICAL PRIORITIES", "These are voluntary declarations. They never force your ballot or automatically become known to other characters. You can change them as your roleplay develops.", "YOUR DECLARED VALUES");
            foreach (ReignGovernmentPlank value in Enum.GetValues(typeof(ReignGovernmentPlank)))
            {
                var declaration = _behavior.Participation.PlayerDeclarations.FirstOrDefault(x => x.Value == value);
                ParticipationChoice(value.ToString(), FriendlyPoliticalValue(value), declaration == null ? "Undeclared" : "Stance " + declaration.Stance + " · importance " + declaration.Importance,
                    ignored => ShowDeclaration(value));
            }
            RaiseAll();
        }
        private static string FriendlyPoliticalValue(ReignGovernmentPlank value) => System.Text.RegularExpressions.Regex.Replace(value.ToString(), "([a-z])([A-Z])", "$1 $2");
        private void ShowDeclaration(ReignGovernmentPlank value)
        {
            ParticipationPage("declaration", "DECLARE A PRIORITY", FriendlyPoliticalValue(value) + ". Choose your stance, then its importance to your character.", "YOUR POSITION", "Back to priorities", ExecutePoliticalPriorities);
            foreach (int stance in new[] { -100, -50, 0, 50, 100 })
                ParticipationChoice(stance.ToString(), stance == 0 ? "Neutral" : stance < 0 ? (stance == -100 ? "Strongly oppose" : "Oppose") : (stance == 100 ? "Strongly support" : "Support"), FriendlyPoliticalValue(value), ignored => {
                    ParticipationPage("importance", "SET IMPORTANCE", "How important is " + FriendlyPoliticalValue(value).ToLowerInvariant() + " to your character?", "YOUR PRIORITY ORDER");
                    foreach (int importance in new[] { 25, 50, 75, 100 }) ParticipationChoice(importance.ToString(), importance == 100 ? "Defining priority" : importance == 75 ? "High priority" : importance == 50 ? "Moderate priority" : "Low priority", "Personal declaration only", key => {
                        _behavior.SetPlayerPoliticalDeclaration(value, stance, int.Parse(key)); ExecutePoliticalPriorities(); });
                    RaiseAll(); });
            RaiseAll();
        }
    }
}
