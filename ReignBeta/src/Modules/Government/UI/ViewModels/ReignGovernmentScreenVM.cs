using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    /// <summary>Public hearing projection. Private scores and current relationships never enter this VM.</summary>
    public sealed class ReignGovernmentScreenVM : ViewModel
    {
        private readonly ReignGovernmentCampaignBehavior _behavior;
        private readonly Kingdom _kingdom;
        private readonly Action _close;
        private readonly BannerImageIdentifierVM _kingdomBanner;
        private ReignGovernmentBusinessRecord _selected;
        private string _view = "hearing", _status = string.Empty, _composer = string.Empty, _hearingBusinessId = string.Empty;
        private bool _sending, _finalized, _refreshPending;
        private int _level;

        public ReignGovernmentScreenVM(ReignGovernmentCampaignBehavior behavior, Kingdom kingdom, bool interactive, Action close)
        {
            _behavior = behavior; _kingdom = kingdom; _close = close;
            _kingdomBanner = kingdom?.Banner == null ? null : new BannerImageIdentifierVM(kingdom.Banner, true);
            IsInteractive = interactive && kingdom?.Leader == Hero.MainHero;
            _behavior.BusinessChanged += OnBusinessChanged;
            RefreshAll("Choose a matter to hear the case.");
        }

        public MBBindingList<ReignGovernmentBusinessCardVM> BusinessItems { get; } = new MBBindingList<ReignGovernmentBusinessCardVM>();
        public MBBindingList<ReignGovernmentBusinessCardVM> ArchiveItems { get; } = new MBBindingList<ReignGovernmentBusinessCardVM>();
        public MBBindingList<ReignGovernmentOptionVM> Options { get; } = new MBBindingList<ReignGovernmentOptionVM>();
        public MBBindingList<ReignGovernmentOptionRowVM> OptionRows { get; } = new MBBindingList<ReignGovernmentOptionRowVM>();
        public MBBindingList<ReignGovernmentPersonVM> Participants { get; } = new MBBindingList<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentPersonVM> Members { get; } = new MBBindingList<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentTranscriptVM> Transcript { get; } = new MBBindingList<ReignGovernmentTranscriptVM>();
        public MBBindingList<ReignGovernmentPartyCardVM> Parties { get; } = new MBBindingList<ReignGovernmentPartyCardVM>();
        public MBBindingList<ReignGovernmentObligationVM> Obligations { get; } = new MBBindingList<ReignGovernmentObligationVM>();
        public MBBindingList<ReignGovernmentCommitmentVM> Commitments { get; } = new MBBindingList<ReignGovernmentCommitmentVM>();
        public MBBindingList<ReignGovernmentPolicyChoiceVM> PolicyChoices { get; } = new MBBindingList<ReignGovernmentPolicyChoiceVM>();
        // Kept for the established automation observation, now counts all pending government cases.
        public MBBindingList<ReignGovernmentBusinessCardVM> Resolutions => BusinessItems;
        internal string ActiveBusinessId => _selected?.BusinessId ?? string.Empty;
        internal string ActivePresentationKey => _selected == null ? string.Empty : _selected.Status + "|" + _selected.Postponed + "|" + _selected.RecessUntilDay;
        internal void SelectBusinessById(string id)
        {
            var card = BusinessItems.Concat(ArchiveItems).FirstOrDefault(x => x.Record.BusinessId == id);
            if (card != null) SelectBusiness(card);
        }

        [DataSourceProperty] public bool IsInteractive { get; }
        [DataSourceProperty] public bool IsReadOnly => !IsInteractive;
        [DataSourceProperty] public string Title { get; private set; }
        [DataSourceProperty] public string KingdomName => _kingdom?.Name?.ToString() ?? "Kingdom";
        [DataSourceProperty] public string KingdomBannerId => _kingdomBanner?.Id ?? string.Empty;
        [DataSourceProperty] public string KingdomBannerArgs => _kingdomBanner?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string KingdomBannerProvider => _kingdomBanner?.TextureProviderName ?? string.Empty;
        [DataSourceProperty] public string GovernmentSceneId => ReignBeta.UI.EventArt.ReignEventArtTextureFactory.BuildCourtPetitionReferenceImageId(_kingdom?.Culture?.StringId);
        [DataSourceProperty] public ReignGovernmentPersonVM Petitioner { get; private set; }
        [DataSourceProperty] public ReignGovernmentPersonVM Sponsor { get; private set; }
        [DataSourceProperty] public bool HasPetitioner => !string.IsNullOrWhiteSpace(_selected?.PetitionerHeroStringId);
        [DataSourceProperty] public bool HasSponsor => !string.IsNullOrWhiteSpace(_selected?.SponsorHeroStringId);
        [DataSourceProperty] public string AuthorityText => "LEVEL " + _level + " · " + (_level == 5 ? "THE ASSEMBLY DECIDES" : "RULER AND GOVERNMENT");
        [DataSourceProperty] public string ModeText => IsInteractive ? "PUBLIC GOVERNMENT RECORD" : "READ-ONLY PUBLIC RECORD";
        [DataSourceProperty] public string BusinessTabText => "Business" + (BusinessItems.Count == 0 ? "" : "  " + BusinessItems.Count);
        [DataSourceProperty] public string BusinessTabSprite => "reign_government_tab_business" + (_view == "business" ? "_selected" : "");
        [DataSourceProperty] public string HearingTabSprite => "reign_government_tab_hearing" + (_view == "hearing" ? "_selected" : "");
        [DataSourceProperty] public string MembersTabSprite => "reign_government_tab_members" + (IsMembersView ? "_selected" : "");
        [DataSourceProperty] public string ArchiveTabSprite => "reign_government_tab_archive" + (IsArchiveView ? "_selected" : "");
        [DataSourceProperty] public string MeetingText { get; private set; }
        [DataSourceProperty] public bool IsHearingView => _view == "hearing" || _view == "business";
        [DataSourceProperty] public bool IsMembersView => _view == "members";
        [DataSourceProperty] public bool IsArchiveView => _view == "archive";
        [DataSourceProperty] public bool IsAuthorityView => _view == "authority";
        [DataSourceProperty] public bool IsPolicyView => _view == "policy";
        [DataSourceProperty] public string LeftHeading => IsArchiveView ? "DECISIONS & OBLIGATIONS" : IsMembersView ? "POLITICAL PARTIES" : IsAuthorityView ? "CONSTITUTION" : "PENDING BUSINESS";
        [DataSourceProperty] public string CenterHeading => IsMembersView ? "MEMBERS & PARTIES" : IsAuthorityView ? "AUTHORITY" : IsArchiveView ? "RECORDED OUTCOME" : _view == "business" ? "BUSINESS" : "PUBLIC HEARING";
        [DataSourceProperty] public string RightHeading => IsMembersView ? "REPRESENTATION" : "PEOPLE & ATTENDANCE";
        [DataSourceProperty] public string StageText => _selected == null ? "No matter selected" : Stage(_selected.Status);
        [DataSourceProperty] public string MatterKind => _selected == null ? "GOVERNMENT BUSINESS" : Friendly(_selected.Kind).ToUpperInvariant();
        [DataSourceProperty] public string MatterTitle => _selected?.Title ?? (_view == "archive" ? "No recorded decision selected" : _view == "business" ? "Government business" : "The floor is open");
        [DataSourceProperty] public string MatterSummary => _selected?.Summary ?? (_view == "business"
            ? "Select a pending matter to enter its hearing, or introduce a new proposal. Event business appears as it occurs; seasonal business follows the regular schedule."
            : "Event decisions enter this record when they occur. Seasonal business follows the government's regular schedule.");
        [DataSourceProperty] public string OutcomeText => _selected?.Outcome ?? string.Empty;
        [DataSourceProperty] public string RecommendationText => string.IsNullOrEmpty(_selected?.RecommendedOptionId) ? "Choose your recommendation below." : "Ruler recommendation: " + OptionLabel(_selected.RecommendedOptionId);
        [DataSourceProperty] public string AuthorityExplanation => _level == 5
            ? "Members weigh your recommendation, their interests, and prior commitments. The assembly's vote is binding."
            : "Hear the government, then respond within the authority granted by the realm's constitution.";
        [DataSourceProperty] public string RecessText => _selected?.Status == "postponed"
            ? "Reconvenes on day " + _selected.RecessUntilDay.ToString("0.0") + ". Attending members can be met privately at the capital."
            : _selected?.IsUrgent == true ? "Urgent business must be decided without a political recess."
            : _selected?.Postponed == true ? "The one permitted recess has been used. Absent members still vote."
            : "Postpone this hearing once to meet attending members at the capital. Absent members still vote.";
        [DataSourceProperty] public string AttendanceText => Members.Count + " seated members" + (_selected == null ? string.Empty : " · " + Members.Count(x => x.IsAbsent) + " absent");
        [DataSourceProperty] public string StatusText { get => _status; private set { _status = value ?? string.Empty; OnPropertyChangedWithValue(_status); } }
        [DataSourceProperty] public string ComposerText { get => _composer; set { _composer = value ?? string.Empty; OnPropertyChangedWithValue(_composer); OnPropertyChanged(nameof(CanSpeak)); } }
        [DataSourceProperty] public bool HasMatter => _selected != null;
        [DataSourceProperty] public bool HasOutcome => !string.IsNullOrWhiteSpace(OutcomeText);
        [DataSourceProperty] public bool HasParticipants => Participants.Count > 0;
        [DataSourceProperty] public bool HasBusiness => BusinessItems.Count > 0;
        [DataSourceProperty] public bool HasArchive => ArchiveItems.Count > 0;
        [DataSourceProperty] public bool CanRecommend => IsInteractive && !_sending && _selected != null && GovernmentBusinessRules.CanRecommend(_selected.Status);
        [DataSourceProperty] public bool CanCallVote => IsInteractive && !_sending && _selected != null && GovernmentBusinessRules.CanVote(_selected.Status);
        [DataSourceProperty] public bool CanPostpone => IsInteractive && !_sending && _selected != null && GovernmentBusinessRules.CanPostpone(_selected.Status, _selected.IsUrgent, _selected.Postponed);
        [DataSourceProperty] public bool CanReconvene => IsInteractive && !_sending && _selected?.Status == "postponed";
        [DataSourceProperty] public bool CanAdoptPetition => IsInteractive && !_sending && _selected?.Status == "awaiting_sponsorship";
        [DataSourceProperty] public bool CanConfirmRecommendation => IsInteractive && !_sending && _level < 5 && _selected != null
            && (_selected.Status == "awaiting_ruler" || _selected.Status == "reconsideration") && !string.IsNullOrWhiteSpace(_selected.RecommendedOptionId);
        [DataSourceProperty] public bool CanOverrideRecommendation => CanConfirmRecommendation && _selected.RecommendedOptionId != _selected.WinningOptionId;
        [DataSourceProperty] public bool CanAcceptGovernmentDecision => IsInteractive && !_sending && _level < 5 && _selected != null
            && (_selected.Status == "awaiting_ruler" || _selected.Status == "reconsideration") && !string.IsNullOrWhiteSpace(_selected.WinningOptionId);
        [DataSourceProperty] public string AcceptGovernmentText => "Accept: " + OptionLabel(_selected?.WinningOptionId);
        [DataSourceProperty] public bool CanSpeak => CanRecommend && !string.IsNullOrWhiteSpace(ComposerText);
        [DataSourceProperty] public bool ShowComposer => CanRecommend;
        [DataSourceProperty] public int BusinessCount => BusinessItems.Count;
        [DataSourceProperty] public int ArchiveCount => ArchiveItems.Count;
        [DataSourceProperty] public int MemberCount => Members.Count;
        [DataSourceProperty] public int ParticipantCount => Participants.Count;
        [DataSourceProperty] public int TranscriptCount => Transcript.Count;
        [DataSourceProperty] public int OptionCount => Options.Count;
        [DataSourceProperty] public int OptionRowCount => OptionRows.Count;
        [DataSourceProperty] public int PartyCount => Parties.Count;
        [DataSourceProperty] public int ObligationCount => Obligations.Count;
        [DataSourceProperty] public int CommitmentCount => Commitments.Count;
        [DataSourceProperty] public int PolicyChoiceCount => PolicyChoices.Count;

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteBusinessTab()
        {
            if (_selected != null) _hearingBusinessId = _selected.BusinessId;
            _view = "business"; _selected = null; RefreshDetails(); RaiseAll();
        }
        public void ExecuteHearingTab()
        {
            var card = BusinessItems.Concat(ArchiveItems).FirstOrDefault(x => x.Record.BusinessId == _hearingBusinessId)
                ?? BusinessItems.FirstOrDefault();
            if (card != null) SelectBusiness(card); else ChangeView("hearing");
        }
        public void ExecuteMembersTab() => ChangeView("members");
        public void ExecuteArchiveTab()
        {
            if (_selected != null && !GovernmentBusinessRules.IsClosed(_selected.Status))
                _hearingBusinessId = _selected.BusinessId;
            _view = "archive";
            _selected = ArchiveItems.FirstOrDefault(x => x.Record == _selected)?.Record
                ?? ArchiveItems.FirstOrDefault()?.Record;
            RefreshDetails(); RaiseAll();
        }
        public void ExecuteAuthorityTab() => ChangeView("authority");
        public void ExecuteNewPolicy()
        {
            if (!IsInteractive) return;
            PolicyChoices.Clear();
            foreach (JObject choice in _behavior.GetGovernmentProposalChoices(_kingdom.StringId).OfType<JObject>())
                PolicyChoices.Add(new ReignGovernmentPolicyChoiceVM(choice, id =>
                {
                    _behavior.CreateGovernmentBusiness(_kingdom.StringId, id, Hero.MainHero, out string businessId, out string result);
                    RefreshAll(result); if (!string.IsNullOrWhiteSpace(businessId)) SelectBusinessById(businessId);
                }));
            ChangeView("policy");
        }
        private void ChangeView(string view) { _view = view; RaiseAll(); }
        public void ExecuteCallVote() { if (!CanCallVote) return; _behavior.VoteBusiness(_selected.BusinessId, Hero.MainHero, out string result); RefreshAll(result); }
        public void ExecutePostpone()
        {
            if (!CanPostpone) return;
            if (_behavior.PostponeBusiness(_selected.BusinessId, Hero.MainHero, out string result)) { RefreshAll(result); _close?.Invoke(); }
            else RefreshAll(result);
        }
        public void ExecuteReconvene() { if (!CanReconvene) return; _behavior.ReconveneBusiness(_selected.BusinessId, Hero.MainHero, out string result); RefreshAll(result); }
        public void ExecuteAdoptPetition() { if (!CanAdoptPetition) return; _behavior.AdoptBusinessPetition(_selected.BusinessId, Hero.MainHero, out string result); RefreshAll(result); }
        public void ExecuteConfirmRecommendation() => Resolve(false);
        public void ExecuteAcceptGovernmentDecision() { if (!CanAcceptGovernmentDecision) return; _behavior.ResolveBusiness(_selected.BusinessId, _selected.WinningOptionId, Hero.MainHero, false, out string result); RefreshAll(result); }
        public void ExecuteOverrideRecommendation()
        {
            if (!CanOverrideRecommendation) return;
            InformationManager.ShowInquiry(new InquiryData("Override the government", "Uphold your recommendation despite the government's recorded position? The constitutional and political consequences will apply.", true, true, "Uphold recommendation", "Return to hearing", () => Resolve(true), null), true);
        }
        private void Resolve(bool force)
        {
            if (!CanConfirmRecommendation || (force && !CanOverrideRecommendation)) return;
            _behavior.ResolveBusiness(_selected.BusinessId, _selected.RecommendedOptionId, Hero.MainHero, force, out string result); RefreshAll(result);
        }
        private void SelectOption(ReignGovernmentOptionVM option)
        {
            if (!CanRecommend) return;
            _behavior.RecommendBusiness(_selected.BusinessId, option.Id, Hero.MainHero, out string result); RefreshAll(result);
        }
        private void SelectBusiness(ReignGovernmentBusinessCardVM card)
        {
            _selected = card.Record; _hearingBusinessId = card.Record.BusinessId; _view = GovernmentBusinessRules.IsClosed(card.Record.Status) ? "archive" : "hearing"; RefreshDetails(); RaiseAll();
        }
        public void ExecuteSpeak() { if (CanSpeak) _ = SpeakAsync(); }
        internal bool TryComposeHearing(string businessId, string message)
        {
            if (_finalized || !IsHearingView || !CanRecommend || _selected?.BusinessId != businessId
                || string.IsNullOrWhiteSpace(message) || message.Length > 1200) return false;
            ComposerText = message;
            return true;
        }
        private async Task SpeakAsync()
        {
            string id = _selected.BusinessId, text = ComposerText.Trim(); _sending = true; RaiseAll();
            try
            {
                JObject response = await _behavior.SubmitHearingMessageAsync(id, text).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => { if (_finalized) return; _sending = false; if (response?.Value<bool>("ok") == true) ComposerText = string.Empty; RefreshAll(response?.Value<string>("message") ?? response?.Value<string>("error") ?? "The public record has been updated."); }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Government hearing message failed: " + ex.Message);
                await ReignMainThread.InvokeAsync(() => { if (_finalized) return; _sending = false; StatusText = "The speaker could not respond. Your message remains available to retry."; RaiseAll(); }).ConfigureAwait(false);
            }
        }
        public void ExecuteHoldMeeting()
        {
            if (!IsInteractive) return;
            var state = _behavior.GetGovernment(_kingdom);
            if (state != null && (float)CampaignTime.Now.ToDays + 0.01f < state.NextMeetingDay) { StatusText = MeetingText; return; }
            var meeting = _behavior.HoldPlayerMeeting(_kingdom); RefreshAll(meeting?.Summary ?? "No meeting could be convened.");
        }
        public void ExecuteIncreaseAuthority() { if (!IsInteractive) return; _behavior.IncreaseGovernmentLevel(_kingdom, out string result); RefreshAll(result); }
        public void ExecuteReduceAuthority()
        {
            if (!IsInteractive || _level <= 1) return;
            InformationManager.ShowInquiry(new InquiryData("Request authority reduction", "Every seated member votes individually. Two thirds must consent to ratify the reduction.", true, true, "Call consent vote", "Cancel", TryRatifiedReduction, null), true);
        }
        private void TryRatifiedReduction()
        {
            if (_behavior.ReduceGovernmentLevel(_kingdom, false, out string result)) { RefreshAll("The constitutional vote was recorded."); return; }
            RefreshAll("The assembly did not consent to the authority reduction.");
            InformationManager.ShowInquiry(new InquiryData("Force authority reduction", "Forcing this reduction harms settlement loyalty and your relationships with the realm's landholding clan leaders. Proceed despite the failed consent vote?", true, true, "Force reduction", "Cancel", () => { _behavior.ReduceGovernmentLevel(_kingdom, true, out string forced); RefreshAll("The authority decision and its consequences were recorded."); }, null), true);
        }

        public void RefreshFromCampaign() => RefreshAll(StatusText);
        private void OnBusinessChanged(ReignGovernmentBusinessRecord record) { if (record?.KingdomStringId == _kingdom.StringId) _refreshPending = true; }
        public void Tick() { if (_refreshPending && !_sending && !_finalized) RefreshAll(StatusText); }
        private void RefreshAll(string status)
        {
            _refreshPending = false;
            string selectedId = _selected?.BusinessId;
            var state = _behavior.EnsureGovernment(_kingdom); _level = state?.Level ?? 1;
            Title = (state?.InstitutionName ?? "Government").ToUpperInvariant();
            MeetingText = state == null ? "No seasonal meeting scheduled." : "Next seasonal meeting: day " + state.NextMeetingDay.ToString("0.0");
            BusinessItems.Clear(); ArchiveItems.Clear();
            foreach (var record in _behavior.GetBusiness(_kingdom.StringId))
            {
                var card = new ReignGovernmentBusinessCardVM(record, SelectBusiness);
                if (GovernmentBusinessRules.IsClosed(record.Status)) ArchiveItems.Add(card); else BusinessItems.Add(card);
            }
            var selectionItems = _view == "archive" ? ArchiveItems.AsEnumerable() : BusinessItems.Concat(ArchiveItems);
            _selected = _view == "business" ? null : selectionItems.FirstOrDefault(x => x.Record.BusinessId == selectedId)?.Record
                ?? (_view == "archive" ? ArchiveItems.FirstOrDefault()?.Record : BusinessItems.FirstOrDefault()?.Record);
            Parties.Clear(); foreach (var party in _behavior.GetParties(_kingdom.StringId)) Parties.Add(new ReignGovernmentPartyCardVM(party));
            Obligations.Clear();
            foreach (var resolution in _behavior.Resolutions.Where(x => x.KingdomStringId == _kingdom.StringId && x.Status == "active"))
                Obligations.Add(new ReignGovernmentObligationVM(resolution, () => { if (!IsInteractive) return; _behavior.ContributeGoods(resolution.ResolutionId, int.MaxValue, out string result); RefreshAll(result); }, IsInteractive));
            Commitments.Clear();
            if (IsInteractive)
                foreach (var commitment in _behavior.GetGovernmentCommitments(_kingdom.StringId).Where(x => x.RulerHeroStringId == Hero.MainHero.StringId))
                    Commitments.Add(new ReignGovernmentCommitmentVM(commitment, _behavior.GetBusiness(_kingdom.StringId).FirstOrDefault(x => x.BusinessId == commitment.BusinessId)?.Title));
            RefreshDetails(); StatusText = status; RaiseAll();
        }
        private void RefreshDetails()
        {
            Options.Clear(); OptionRows.Clear(); Participants.Clear(); Members.Clear(); Transcript.Clear();
            Petitioner = Person(_selected?.PetitionerHeroStringId, "Petitioner", string.Empty);
            Sponsor = Person(_selected?.SponsorHeroStringId, "Sponsor", string.Empty);
            foreach (var card in BusinessItems.Concat(ArchiveItems)) card.IsSelected = card.Record == _selected;
            if (_selected != null)
            {
                foreach (JObject option in Array(_selected.OptionsJson).OfType<JObject>()) Options.Add(new ReignGovernmentOptionVM(option.Value<string>("id"), option.Value<string>("label"), option.Value<string>("description"), option.Value<string>("id") == _selected.RecommendedOptionId, CanRecommend, SelectOption));
                for (int index = 0; index < Options.Count; index += 2)
                    OptionRows.Add(new ReignGovernmentOptionRowVM(Options[index], index + 1 < Options.Count ? Options[index + 1] : null));
                AddParticipant(_selected.PetitionerHeroStringId, "Petitioner");
                AddParticipant(_selected.SponsorHeroStringId, "Sponsor");
                foreach (JObject person in Array(_selected.ParticipantsJson).OfType<JObject>()) AddParticipant(person.Value<string>("heroId"), person.Value<string>("role") ?? "Affected party");
                foreach (JObject entry in Array(_selected.TranscriptJson).OfType<JObject>())
                    foreach (string passage in ReadingPassages(entry.Value<string>("text") ?? entry.Value<string>("message")))
                        Transcript.Add(new ReignGovernmentTranscriptVM(entry.Value<string>("speakerHeroId") ?? entry.Value<string>("heroId"), entry.Value<string>("speaker") ?? entry.Value<string>("speakerName"), entry.Value<string>("role"), passage, entry.Value<float?>("day")));
                foreach (string passage in ReadingPassages(_selected.Outcome))
                    Transcript.Add(new ReignGovernmentTranscriptVM(null, "Clerk's record", "Outcome", passage, _selected.ResolvedDay >= 0 ? (float?)_selected.ResolvedDay : null));
                foreach (JObject ballot in Array(_selected.VotesJson).OfType<JObject>())
                    Transcript.Add(new ReignGovernmentTranscriptVM(ballot.Value<string>("memberHeroId"), null, ballot.Value<bool?>("absentee") == true ? "Absentee ballot" : "Recorded ballot", OptionLabel(ballot.Value<string>("optionId")), null));
            }
            foreach (var seat in _behavior.GetSeats(_kingdom.StringId))
            {
                var party = _behavior.GetParties(_kingdom.StringId).FirstOrDefault(x => x.PartyId == seat.PartyId);
                Hero hero = FindHero(seat.HeroStringId);
                string represented = Settlement.All.FirstOrDefault(x => x.StringId == seat.SettlementStringId)?.Name?.ToString()
                    ?? hero?.Clan?.Name?.ToString() ?? "Government seat";
                Members.Add(Person(seat.HeroStringId, party?.Name ?? "Independent", represented));
            }
        }
        private void AddParticipant(string id, string role)
        {
            if (string.IsNullOrWhiteSpace(id) || Participants.Any(x => x.HeroId == id)) return;
            Participants.Add(Person(id, role, string.Empty));
        }
        private ReignGovernmentPersonVM Person(string id, string role, string constituency)
        {
            var attendance = _selected == null ? null : _behavior.GetBusinessAttendance(_selected.BusinessId).FirstOrDefault(x => x.HeroStringId == id);
            string status = attendance == null ? "Attendance not recorded" : Friendly(attendance.Status) + (string.IsNullOrWhiteSpace(attendance.Reason) ? "" : " · " + attendance.Reason);
            return new ReignGovernmentPersonVM(id, role, constituency, status, attendance != null && attendance.Status != "present");
        }
        private string OptionLabel(string id) => Array(_selected?.OptionsJson).OfType<JObject>().FirstOrDefault(x => x.Value<string>("id") == id)?.Value<string>("label") ?? "No recorded choice";
        internal static JArray Array(string json) { try { return JArray.Parse(json ?? "[]"); } catch { return new JArray(); } }
        internal static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id) ? null : Hero.FindFirst(x => x.StringId == id);
        internal static string Friendly(string text) => (text ?? string.Empty).Replace('_', ' ');
        private static IEnumerable<string> ReadingPassages(string text)
        {
            // Every complete card remains a bounded row; long speeches create additional complete cards.
            string remaining = (text ?? string.Empty).Trim();
            while (remaining.Length > 0)
            {
                int count = Math.Min(150, remaining.Length);
                if (count < remaining.Length) { int boundary = remaining.LastIndexOf(' ', count - 1, count); if (boundary > 0) count = boundary; }
                yield return remaining.Substring(0, count); remaining = remaining.Substring(count).TrimStart();
            }
        }
        private static string Stage(string status)
        {
            switch (status) { case "awaiting_sponsorship": return "Awaiting sponsorship"; case "hearing": return "In discussion"; case "postponed": return "Seven-day recess"; case "awaiting_ruler": return "Ruler response required"; case "reconsideration": return "Reconsider the government's objection"; case "decided": return "Decision recorded"; case "authorized": return "Authorized · Awaiting campaign execution"; case "execution_failed": return "Execution needs recovery"; default: return Friendly(status); }
        }
        private void RaiseAll()
        {
            foreach (var property in GetType().GetProperties().Where(x => Attribute.IsDefined(x, typeof(DataSourceProperty)))) OnPropertyChanged(property.Name);
        }
        public override void OnFinalize() { _finalized = true; _behavior.BusinessChanged -= OnBusinessChanged; base.OnFinalize(); }
    }

    public sealed class ReignGovernmentBusinessCardVM : ViewModel
    {
        private readonly Action<ReignGovernmentBusinessCardVM> _select; private bool _selected;
        public ReignGovernmentBusinessCardVM(ReignGovernmentBusinessRecord record, Action<ReignGovernmentBusinessCardVM> select) { Record = record; _select = select; }
        public ReignGovernmentBusinessRecord Record { get; }
        [DataSourceProperty] public string Title => Record.Title;
        [DataSourceProperty] public string Summary => ReignGovernmentScreenVM.Friendly(Record.Kind) + " · " + (Record.IsUrgent ? "Urgent · " : "") + ReignGovernmentScreenVM.Friendly(Record.Status);
        [DataSourceProperty] public bool IsSelected { get => _selected; set { _selected = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(CardSprite)); } }
        [DataSourceProperty] public string CardSprite
        {
            get
            {
                string kind = Record.Kind == "policy" ? "policy" : Record.Kind.StartsWith("fief") ? "fief"
                    : Record.Kind == "seasonal" || Record.Kind == "world_action" ? "seasonal" : "diplomacy";
                return "reign_government_business_" + kind + (IsSelected ? "_selected" : "");
            }
        }
        public void ExecuteSelect() => _select(this);
    }
    public sealed class ReignGovernmentOptionVM : ViewModel
    {
        private readonly Action<ReignGovernmentOptionVM> _select;
        public ReignGovernmentOptionVM(string id, string label, string detail, bool selected, bool enabled, Action<ReignGovernmentOptionVM> select) { Id = id; Label = label; Detail = detail ?? string.Empty; IsSelected = selected; IsEnabled = enabled; _select = select; }
        public string Id { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool IsSelected { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        [DataSourceProperty] public string SelectionText => IsSelected ? "Recommended" : "";
        [DataSourceProperty] public string CardSprite => IsSelected ? "reign_government_option_selected" : "reign_government_option_card";
        [DataSourceProperty] public string RecommendationSprite => IsSelected ? "reign_government_recommendation_selected" : "reign_government_recommendation";
        public void ExecuteSelect() { if (IsEnabled) _select(this); }
    }
    public sealed class ReignGovernmentOptionRowVM : ViewModel
    {
        public ReignGovernmentOptionRowVM(ReignGovernmentOptionVM left, ReignGovernmentOptionVM right) { Left = left; Right = right; }
        [DataSourceProperty] public ReignGovernmentOptionVM Left { get; }
        [DataSourceProperty] public ReignGovernmentOptionVM Right { get; }
        [DataSourceProperty] public bool HasRight => Right != null;
    }
    public class ReignGovernmentPersonVM : ViewModel
    {
        private readonly ImageIdentifierVM _portrait;
        private readonly BannerImageIdentifierVM _banner;
        public ReignGovernmentPersonVM(string heroId, string role, string constituency, string attendance, bool absent)
        {
            HeroId = heroId ?? string.Empty; Hero hero = ReignGovernmentScreenVM.FindHero(heroId);
            Name = hero?.Name?.ToString() ?? "Former participant"; Role = ReignGovernmentScreenVM.Friendly(role); if (Role.Length > 0) Role = char.ToUpperInvariant(Role[0]) + Role.Substring(1); Constituency = constituency ?? string.Empty; Attendance = attendance ?? string.Empty; IsAbsent = absent;
            PortraitCacheKey = hero == null ? string.Empty : CharacterCacheId.ForHero(hero) ?? string.Empty; HasPortrait = hero != null;
            _banner = hero?.Clan?.Banner == null ? null : new BannerImageIdentifierVM(hero.Clan.Banner, true);
            try { CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject); _portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code); } catch { }
        }
        public string HeroId { get; }
        public bool IsAbsent { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Role { get; }
        [DataSourceProperty] public string Constituency { get; }
        [DataSourceProperty] public string Attendance { get; }
        [DataSourceProperty] public bool HasPortrait { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId => _portrait?.Id ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _portrait?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _portrait?.TextureProviderName ?? string.Empty;
        [DataSourceProperty] public bool HasBanner => _banner != null;
        [DataSourceProperty] public string BannerId => _banner?.Id ?? string.Empty;
        [DataSourceProperty] public string BannerArgs => _banner?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string BannerProvider => _banner?.TextureProviderName ?? string.Empty;
    }
    public sealed class ReignGovernmentTranscriptVM : ReignGovernmentPersonVM
    {
        public ReignGovernmentTranscriptVM(string id, string speaker, string role, string text, float? day) : base(id, role, "", "", false) { Speaker = string.IsNullOrWhiteSpace(speaker) ? Name : speaker; Text = text ?? string.Empty; Date = day.HasValue ? "Day " + day.Value.ToString("0.0") : string.Empty; }
        [DataSourceProperty] public string Speaker { get; }
        [DataSourceProperty] public string Text { get; }
        [DataSourceProperty] public string Date { get; }
    }
    public sealed class ReignGovernmentPartyCardVM : ViewModel
    {
        public ReignGovernmentPartyCardVM(ReignGovernmentPartyRecord party) { Name = party.Name; Planks = string.Join(" · ", ReignGovernmentCampaignBehavior.ParsePlanks(party.PlanksCsv).Select(x => x.ToString())); Speaker = (ReignGovernmentScreenVM.FindHero(party.SpeakerHeroStringId)?.Name?.ToString() ?? "Vacant speaker") + " · " + party.SeatCount + " seats"; Statement = party.LastStatement ?? string.Empty; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Planks { get; }
        [DataSourceProperty] public string Speaker { get; }
        [DataSourceProperty] public string Statement { get; }
    }
    public sealed class ReignGovernmentObligationVM : ViewModel
    {
        private readonly Action _contribute;
        public ReignGovernmentObligationVM(ReignGovernmentResolutionRecord record, Action contribute, bool interactive)
        {
            var template = ReignGovernmentResolutionCatalog.Find(record.TemplateId); Title = template?.Title ?? "Government obligation";
            var action = (ReignGovernmentResolutionAction)record.RouteActionValue;
            bool privateProgress = action == ReignGovernmentResolutionAction.ImproveClanRelation || action == ReignGovernmentResolutionAction.ImproveForeignRelation;
            Detail = (privateProgress ? "Awaiting verified completion" : record.CurrentValue.ToString("0") + " / " + record.RequiredAmount) + " · Due day " + record.DueDay.ToString("0.0");
            CanContribute = interactive && (action == ReignGovernmentResolutionAction.FoodDelivery || action == ReignGovernmentResolutionAction.GoodsDelivery); _contribute = contribute;
        }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool CanContribute { get; }
        public void ExecuteContributeGoods() { if (CanContribute) _contribute(); }
    }
    public sealed class ReignGovernmentCommitmentVM : ViewModel
    {
        public ReignGovernmentCommitmentVM(ReignGovernmentCommitmentRecord record, string matter)
        {
            Name = ReignGovernmentScreenVM.FindHero(record.MemberHeroStringId)?.Name?.ToString() ?? "Former member";
            Matter = matter ?? "Government matter"; Terms = record.Terms ?? "";
            Status = ReignGovernmentScreenVM.Friendly(record.Status) + " · Accepted day " + record.AcceptedDay.ToString("0.0") + " · Expires day " + record.ExpiresDay.ToString("0.0");
        }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Matter { get; }
        [DataSourceProperty] public string Terms { get; }
        [DataSourceProperty] public string Status { get; }
    }
    public sealed class ReignGovernmentPolicyChoiceVM : ViewModel
    {
        private readonly Action<string> _choose; private readonly string _id;
        public ReignGovernmentPolicyChoiceVM(JObject choice, Action<string> choose)
        {
            _id = choice.Value<string>("proposalId"); _choose = choose;
            Title = choice.Value<string>("label") ?? "Policy"; Detail = choice.Value<string>("description") ?? "";
            IsEnabled = choice.Value<bool?>("available") == true;
        }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        public void ExecuteSelect() { if (IsEnabled) _choose(_id); }
    }
}

