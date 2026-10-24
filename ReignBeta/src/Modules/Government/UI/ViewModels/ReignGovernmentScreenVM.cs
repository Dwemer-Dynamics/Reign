using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Government;
using ReignBeta.Government;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    /// <summary>Public hearing projection. Private scores and current relationships never enter this VM.</summary>
    public sealed partial class ReignGovernmentScreenVM : ViewModel
    {
        private readonly ReignGovernmentCampaignBehavior _behavior;
        private readonly Kingdom _kingdom;
        private readonly Action _close;
        private readonly BannerImageIdentifierVM _kingdomBanner;
        private ReignGovernmentBusinessRecord _selected;
        private string _view = "agenda", _status = string.Empty, _composer = string.Empty, _hearingBusinessId = string.Empty;
        private bool _sending, _finalized, _refreshPending;
        private bool _showProposalTerms, _showOwnPosition;
        private int _memberListRevision, _policyListRevision;
        private int _level;
        private string _pendingOptionId, _ownPositionHearingId;
        private GovernmentMemberBallot _playerBallot;
        private JObject _playerPosition;
        private string _recordsView = "decisions", _memberSearch = "", _partyFilter = "", _positionFilter = "";
        private JObject _forecast = new JObject(), _session = new JObject();
        private ReignGovernmentPersonVM _selectedMember;
        private readonly List<ReignGovernmentPersonVM> _allMembers = new List<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentShareVM> Shares { get; } = new MBBindingList<ReignGovernmentShareVM>();
        public MBBindingList<ReignGovernmentPersonVM> ForMembers { get; } = new MBBindingList<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentPersonVM> AgainstMembers { get; } = new MBBindingList<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentPersonVM> UndecidedMembers { get; } = new MBBindingList<ReignGovernmentPersonVM>();
        public MBBindingList<ReignGovernmentTextRecordVM> ActiveLaws { get; } = new MBBindingList<ReignGovernmentTextRecordVM>();
        public MBBindingList<ReignGovernmentTextRecordVM> NewsEditions { get; } = new MBBindingList<ReignGovernmentTextRecordVM>();
        public MBBindingList<ReignGovernmentTextRecordVM> ProposalSlots { get; } = new MBBindingList<ReignGovernmentTextRecordVM>();

        public ReignGovernmentScreenVM(ReignGovernmentCampaignBehavior behavior, Kingdom kingdom, bool interactive, Action close)
        {
            _behavior = behavior; _kingdom = kingdom; _close = close;
            _kingdomBanner = kingdom?.Banner == null ? null : new BannerImageIdentifierVM(kingdom.Banner, true);
            IsInteractive = interactive && kingdom?.Leader == Hero.MainHero;
            _behavior.BusinessChanged += OnBusinessChanged;
            RefreshAll("Choose a matter to hear the case.");
            if (_behavior.IsCharterPending(kingdom.StringId)) ExecuteFoundingCharter();
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
        public MBBindingList<ReignGovernmentObligationVM> DecisionObligations { get; } = new MBBindingList<ReignGovernmentObligationVM>();
        public MBBindingList<ReignGovernmentCommitmentVM> Commitments { get; } = new MBBindingList<ReignGovernmentCommitmentVM>();
        public MBBindingList<ReignGovernmentPolicyChoiceVM> PolicyChoices { get; } = new MBBindingList<ReignGovernmentPolicyChoiceVM>();
        // Kept for the established automation observation, now counts all pending government cases.
        public MBBindingList<ReignGovernmentBusinessCardVM> Resolutions => BusinessItems;
        internal string ActiveBusinessId => _selected?.BusinessId ?? string.Empty;
        internal string ActivePresentationKey => _selected == null ? string.Empty : _selected.Status + "|" + _selected.PresentationDay + "|" + _selected.DecisionDay;
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
        [DataSourceProperty] public string AuthorityText => "CROWN " + CrownWeight.ToString("0.##") + "% · MEMBERS " + (100 - CrownWeight).ToString("0.##") + "%";
        private double CrownWeight => _selected != null && _selected.FrozenAuthorityLevel > 0 ? (_forecast.Value<double?>("crownWeight") ?? Math.Max(0, 125 - _selected.FrozenAuthorityLevel * 25)) : Math.Max(0, 125 - _level * 25);
        [DataSourceProperty] public string ModeText => ParticipationRoleText + " · " + MeetingText;
        [DataSourceProperty] public string BusinessTabText => "Agenda";
        [DataSourceProperty] public string BusinessTabSprite => "reign_government_v3_tab_agenda" + (IsAgendaView ? "_selected" : "");
        [DataSourceProperty] public string HearingTabSprite => "reign_government_v3_tab_hearing" + (IsHearingView ? "_selected" : "");
        [DataSourceProperty] public string MembersTabSprite => "reign_government_v3_tab_members" + (IsMembersView ? "_selected" : "");
        [DataSourceProperty] public string ArchiveTabSprite => "reign_government_v3_tab_records" + (IsArchiveView ? "_selected" : "");
        [DataSourceProperty] public string ShellSprite => IsMembersView ? "reign_government_v3_members_shell" : IsArchiveView ? "reign_government_v3_records_shell" : IsHearingView ? "reign_government_v3_hearing_shell" : "reign_government_v3_agenda_shell";
        [DataSourceProperty] public string MeetingText { get; private set; }
        [DataSourceProperty] public bool IsAgendaView => _view == "agenda";
        [DataSourceProperty] public bool IsHearingView => _view == "hearing";
        [DataSourceProperty] public bool IsMembersView => _view == "members";
        [DataSourceProperty] public bool IsArchiveView => _view == "archive";
        [DataSourceProperty] public bool IsDecisionsView => IsArchiveView && _recordsView == "decisions";
        [DataSourceProperty] public bool IsLawsView => IsArchiveView && _recordsView == "laws";
        [DataSourceProperty] public bool IsObligationsView => IsArchiveView && _recordsView == "obligations";
        [DataSourceProperty] public bool IsNewsView => IsArchiveView && _recordsView == "news";
        [DataSourceProperty] public string DecisionsTabSprite => "reign_government_v3_record_tab_decisions" + (IsDecisionsView ? "_selected" : "");
        [DataSourceProperty] public string LawsTabSprite => "reign_government_v3_record_tab_laws" + (IsLawsView ? "_selected" : "");
        [DataSourceProperty] public string ObligationsTabSprite => "reign_government_v3_record_tab_obligations" + (IsObligationsView ? "_selected" : "");
        [DataSourceProperty] public string NewsTabSprite => "reign_government_v3_record_tab_news" + (IsNewsView ? "_selected" : "");
        [DataSourceProperty] public bool IsAuthorityView => _view == "authority";
        [DataSourceProperty] public bool IsPolicyView => _view == "policy";
        [DataSourceProperty] public string LeftHeading => IsArchiveView ? "RECORDED DECISIONS" : IsMembersView ? "POLITICAL PARTIES" : IsHearingView ? "THIS SESSION" : IsAuthorityView ? "CONSTITUTION" : "SESSION AGENDA";
        [DataSourceProperty] public string CenterHeading => IsMembersView ? "MEMBER DIRECTORY" : IsAuthorityView ? "AUTHORITY" : IsArchiveView ? "DECISION" : "PUBLIC HEARING";
        [DataSourceProperty] public string RightHeading => IsMembersView ? "MEMBER DETAILS" : IsHearingView ? "PARTICIPANTS" : "MEMBERS ON THIS MATTER";
        [DataSourceProperty] public string StageText => _selected == null ? "No matter selected" : Stage(_selected.Status);
        [DataSourceProperty] public string MatterKind => _selected == null ? "GOVERNMENT BUSINESS" : _selected.IsUrgent ? "EXTRAORDINARY HEARING" : Friendly(_selected.Kind).ToUpperInvariant();
        [DataSourceProperty] public string MatterTitle => _selected?.Title ?? "No matter selected";
        [DataSourceProperty] public string MatterSummary => CompleteProposalText(
            GovernmentBusinessRules.PublicMatterSummary(_selected?.Kind,
                _selected?.Summary ?? "New proposals appear on the agenda. Select a matter to read its public record."), Array(_selected?.OptionsJson));
        internal static string CompleteProposalText(string summary, JArray options)
        {
            return GovernmentBusinessRules.CompleteProposalText(summary, options.OfType<JObject>()
                .Select(option => new KeyValuePair<string, string>(option.Value<string>("label"), option.Value<string>("description"))));
        }
        [DataSourceProperty] public bool HasExtendedSummary => MatterSummary.Length > 85 || MatterSummary.Contains("\n");
        [DataSourceProperty] public string MatterOverview => HasExtendedSummary ? "Read the complete proposal and requirements under Proposal details below." : MatterSummary;
        [DataSourceProperty] public bool ShowProposalTerms => _showProposalTerms && !_showOwnPosition;
        [DataSourceProperty] public bool ShowPublicDiscussion => !_showProposalTerms && !_showOwnPosition;
        [DataSourceProperty] public bool ShowOwnPosition => _showOwnPosition;
        [DataSourceProperty] public bool ShowReadingRecord => ShowProposalTerms || ShowOwnPosition;
        [DataSourceProperty] public bool HideProposalTerms => !ShowProposalTerms;
        [DataSourceProperty] public bool HidePublicDiscussion => !ShowPublicDiscussion;
        [DataSourceProperty] public bool HideOwnPosition => !ShowOwnPosition;
        [DataSourceProperty] public string OwnPositionRecordText => "YOUR PRIVATE VOTE RECORD\n\n" + RecommendationText
            + "\n\nReading this record does not submit or change your vote.";
        [DataSourceProperty] public string ReadingRecordText => ShowOwnPosition ? OwnPositionRecordText : MatterSummary;
        [DataSourceProperty] public bool HasNoDiscussion => Transcript.Count == 0;
        [DataSourceProperty] public string PositionHeading => CanRecommend ? "YOUR DECLARED POSITION" : "POSSIBLE OUTCOMES";
        [DataSourceProperty] public string OutcomeText => _selected?.Outcome ?? "No decision has been recorded.";
        internal static bool ClosedWithoutRecordedVote(ReignGovernmentBusinessRecord record) => record != null
            && GovernmentBusinessRules.IsClosed(record.Status) && Array(record.VotesJson).Count == 0;
        private bool HasUnrecordedClosure => ClosedWithoutRecordedVote(_selected);
        [DataSourceProperty] public string DecisionResultHeading => HasUnrecordedClosure ? "CLOSED MATTER" : "FINAL RESULT";
        [DataSourceProperty] public string DecisionVoteCaption => _selected == null ? "Choose a recorded matter"
            : HasUnrecordedClosure ? "Closed without a recorded vote"
            : _forecast.Value<bool?>("isFinal") == true ? "Final recorded vote" : "No recorded vote available";
        private string OwnRecommendation => IsInteractive && _selected?.RecommendationRulerHeroStringId == Hero.MainHero?.StringId ? _selected.RecommendedOptionId : null;
        private string SelectedOwnOptionId => IsHearingView && CanMemberBallot ? _pendingOptionId ?? _playerBallot?.OptionId
            : _pendingOptionId ?? (_playerPosition?.Value<bool?>("isFinal") == true ? _playerPosition.Value<string>("optionId")
            : _playerBallot != null ? _playerBallot.OptionId : _playerPosition?.Value<string>("optionId") ?? OwnRecommendation);
        [DataSourceProperty] public string RecommendationText
        {
            get
            {
                var lines = new List<string>();
                if (_pendingOptionId != null) lines.Add("Unsaved selection: " + OptionLabel(_pendingOptionId));
                if (_playerPosition?.Value<bool?>("isFinal") == true) lines.Add("Final recorded vote: "
                    + (string.IsNullOrEmpty(_playerPosition.Value<string>("optionId")) ? "Abstain" : OptionLabel(_playerPosition.Value<string>("optionId"))));
                else if (!string.IsNullOrEmpty(_playerPosition?.Value<string>("optionId"))) lines.Add("Public declaration: " + OptionLabel(_playerPosition.Value<string>("optionId")));
                else if (_playerBallot != null || CanMemberBallot) lines.Add("No public declaration filed.");
                if (_playerBallot != null && _playerPosition?.Value<bool?>("isFinal") != true)
                    lines.Add((_playerBallot.Source == "absentee" ? "Filed absentee ballot: " : "Filed member ballot: ") + (_playerBallot.Abstain ? "Abstain" : OptionLabel(_playerBallot.OptionId)));
                else if (!string.IsNullOrEmpty(OwnRecommendation)) lines.Add("Your Crown recommendation: " + OptionLabel(OwnRecommendation));
                if (CanMemberBallot && _playerBallot == null) lines.Add("No member ballot filed.");
                if (lines.Count > 0) return string.Join("\n", lines);
                return !IsInteractive && !IsPlayerMember ? "Public record · Use My participation to seek a mandate and voting rights."
                    : !CanRecommend ? "Voting is not open for you on this matter. The scheduled session and your office determine eligibility."
                    : "No public declaration or ballot filed. Select an outcome.";
            }
        }
        [DataSourceProperty] public string OwnPositionStatus => (_pendingOptionId != null ? "Unsaved selection"
            : _playerPosition?.Value<bool?>("isFinal") == true ? "Final vote recorded"
            : _playerBallot?.Abstain == true ? "Abstention filed"
            : _playerBallot != null ? "Ballot filed"
            : IsHearingView && CanMemberBallot ? "No ballot filed"
            : !string.IsNullOrEmpty(_playerPosition?.Value<string>("optionId")) ? "Declaration filed"
            : !string.IsNullOrEmpty(OwnRecommendation) ? "Recommendation filed"
            : CanMemberBallot ? "No ballot filed" : "Your vote record") + " · Details ›";
        [DataSourceProperty] public bool CanReturnToHearing => IsAgendaView && _selected != null
            && _ownPositionHearingId == _selected.BusinessId
            && BusinessItems.Any(x => x.Record.BusinessId == _ownPositionHearingId);
        [DataSourceProperty] public bool CanRecordPosition => CanReturnToHearing || CanRecommend;
        [DataSourceProperty] public bool CannotRecordPosition => !CanRecordPosition;
        [DataSourceProperty] public string RecordPositionText => CanReturnToHearing ? "Return to hearing" : "Record my position";
        [DataSourceProperty] public string AuthorityExplanation => _level == 1 ? "The ruler's present approval is required. The Crown holds 100% of the vote." : "The largest total voting share wins. All candidates remain on the ballot; there is no runoff or later ruler veto.";
        [DataSourceProperty] public bool CanIncreaseAuthority => IsInteractive && !CharterPending && _level < 5;
        [DataSourceProperty] public bool CanReduceAuthority => IsInteractive && !CharterPending && _level > 1;
        [DataSourceProperty] public bool CannotIncreaseAuthority => !CanIncreaseAuthority;
        [DataSourceProperty] public bool CannotReduceAuthority => !CanReduceAuthority;
        [DataSourceProperty] public string AuthorityActionsExplanation => !IsInteractive
            ? "Only the ruler may change this division of power. Members can participate in a requested constitutional consent vote."
            : CharterPending ? "Confirm the founding charter before changing the division of power."
            : _level >= 5 ? "Members already hold all voting power. Seeking Crown power requests a two-thirds consent vote. Existing matters keep their frozen voting shares."
            : _level <= 1 ? "The Crown already holds all voting power. Delegating transfers 25 percentage points to the chamber. Existing matters keep their frozen voting shares."
            : "Delegating transfers 25 percentage points to the chamber. Seeking Crown power requests a two-thirds consent vote. Existing matters keep their frozen voting shares.";
        [DataSourceProperty] public string RecessText => "Meet members privately between sessions. Public positions may change before the vote.";
        [DataSourceProperty] public string AttendanceText => _selected?.FrozenAuthorityLevel > 0 ? "Frozen voting roll · Vacant shares remain unassigned · Public positions may change" : _allMembers.Count + " current members · " + MemberWeight.ToString("0.##") + "% each before introduction";
        private double MemberWeight => _allMembers.Count == 0 ? 0 : (100 - CrownWeight) / _allMembers.Count;
        [DataSourceProperty] public string TimelineText => _selected == null ? "Sessions on the 15th and 30th" : PresentedDateText + "     ›     " + LobbyingStageText + "     ›     " + DecisionDateText;
        [DataSourceProperty] public bool HasPresentedMatter => _selected != null && _selected.FrozenAuthorityLevel > 0;
        [DataSourceProperty] public string PresentedDateText => (HasPresentedMatter ? "Presented · " : "Presentation · ") + Date(_selected?.PresentationDay ?? -1);
        [DataSourceProperty] public string LobbyingStageText => HasPresentedMatter ? "Lobbying interval" : "Awaiting presentation";
        [DataSourceProperty] public string DecisionDateText => "Decision · " + Date(_selected?.DecisionDay ?? -1);
        [DataSourceProperty] public string DecisionScheduleText => _selected == null ? MeetingText : _selected.IsUrgent ? "Extraordinary decision" : "Decision scheduled for " + Date(_selected.DecisionDay);
        [DataSourceProperty] public string CrownRepresentation => CrownWeight <= 0
            ? "The Crown holds no voting share. The ruler may recommend an outcome; the seated members decide."
            : !HasPresentedMatter ? "The Crown's representative is recorded when this matter is presented."
            : _selected == null ? "At level 2 and above, an active Chancellor may represent an absent ruler. Otherwise the Crown abstains." : _behavior.GetGovernmentCrownRepresentation(_selected.BusinessId);
        [DataSourceProperty] public ReignGovernmentPersonVM CrownActor { get; private set; }
        [DataSourceProperty] public bool HasCrownActor => CrownWeight > 0 && CrownActor?.HasPortrait == true;
        [DataSourceProperty] public bool HasNoCrownActor => !HasCrownActor;
        [DataSourceProperty] public int MemberListRevision => _memberListRevision;
        [DataSourceProperty] public int PolicyListRevision => _policyListRevision;
        [DataSourceProperty] public ReignGovernmentPersonVM SelectedMember => _selectedMember;
        [DataSourceProperty] public bool HasSelectedMember => _selectedMember != null;
        [DataSourceProperty] public string MemberSearch { get => _memberSearch; set { _memberSearch = value ?? ""; FilterMembers(); RaiseAll(); } }
        [DataSourceProperty] public bool ShowMemberSearchHint => string.IsNullOrEmpty(MemberSearch);
        [DataSourceProperty] public string PositionMatterText => _selected?.Title ?? "All public positions";
        [DataSourceProperty] public string MemberFilterText => string.IsNullOrEmpty(_partyFilter) && string.IsNullOrEmpty(_positionFilter) ? "All members" : "Filtered members · Clear filter";
        [DataSourceProperty] public string ForHeading => "FOR · " + ForMembers.Count + " members";
        [DataSourceProperty] public string AgainstHeading => "AGAINST · " + AgainstMembers.Count + " members";
        private int VacantMemberCount => UndecidedMembers.Count(x => x.Position == "Vacant");
        [DataSourceProperty] public string UndecidedHeading => (_forecast.Value<bool?>("isFinal") == true ? "ABSTAINED · " : "UNDECIDED · ")
            + (UndecidedMembers.Count - VacantMemberCount) + (VacantMemberCount == 0 ? " members" : " · VACANT · " + VacantMemberCount);
        [DataSourceProperty] public string EmptyText => IsMembersView ? "No members match this filter." : IsArchiveView ? "No records in this section yet." : "No pending matters. New proposals will appear here.";
        [DataSourceProperty] public bool HasNoMembers => Members.Count == 0;
        [DataSourceProperty] public bool HasNoBusiness => BusinessItems.Count == 0;
        [DataSourceProperty] public bool HasNoLaws => ActiveLaws.Count == 0;
        [DataSourceProperty] public bool HasNoNews => NewsEditions.Count == 0;
        [DataSourceProperty] public bool HasNoObligations => Obligations.Count == 0;
        [DataSourceProperty] public bool HasNoDecisionObligations => DecisionObligations.Count == 0;
        [DataSourceProperty] public int ShareCount => Shares.Count;
        [DataSourceProperty] public int ForCount => ForMembers.Count;
        [DataSourceProperty] public int AgainstCount => AgainstMembers.Count;
        [DataSourceProperty] public int UndecidedCount => UndecidedMembers.Count;
        [DataSourceProperty] public int ActiveLawCount => ActiveLaws.Count;
        [DataSourceProperty] public int NewsCount => NewsEditions.Count;
        [DataSourceProperty] public int ProposalSlotCount => ProposalSlots.Count;
        [DataSourceProperty] public string StatusText { get => _status; private set { _status = value ?? string.Empty; OnPropertyChangedWithValue(_status); } }
        [DataSourceProperty] public string ComposerText { get => _composer; set { _composer = value ?? string.Empty; OnPropertyChangedWithValue(_composer); OnPropertyChanged(nameof(CanSpeak)); } }
        [DataSourceProperty] public bool HasMatter => _selected != null;
        [DataSourceProperty] public bool HasOutcome => !string.IsNullOrWhiteSpace(OutcomeText);
        [DataSourceProperty] public bool HasParticipants => Participants.Count > 0;
        [DataSourceProperty] public bool HasBusiness => BusinessItems.Count > 0;
        [DataSourceProperty] public bool HasArchive => ArchiveItems.Count > 0;
        [DataSourceProperty] public bool CanRecommend => CanMemberBallot || IsInteractive && !_sending && _selected != null && GovernmentBusinessRules.CanRecommend(_selected.Status) && _behavior.IsGovernmentRulerPresent(_selected.BusinessId);
        [DataSourceProperty] public string VoteActionText => CanMemberBallot ? (_playerBallot == null ? "Submit my ballot" : "Revise my ballot") : (_selected?.FrozenAuthorityLevel > 0 ? _selected.FrozenAuthorityLevel : _level) == 5 ? "Record session vote" : "Cast my vote";
        [DataSourceProperty] public bool CannotRecommend => !CanRecommend;
        [DataSourceProperty] public bool CannotCallVote => !CanCallVote;
        [DataSourceProperty] public bool CanCallVote => CanMemberBallot || IsInteractive && !_sending && _selected != null && _behavior.CanPlayerVoteBusiness(_selected.BusinessId);
        [DataSourceProperty] public bool CanAdoptPetition => IsInteractive && !_sending && _selected?.Status == "awaiting_sponsorship";
        [DataSourceProperty] public bool CanSpeak => CanRecommend && (_selected.Status == "presented" || _selected.Status == "hearing") && _behavior.CanPlayerSpeakAtHearing(_selected.BusinessId) && !string.IsNullOrWhiteSpace(ComposerText);
        [DataSourceProperty] public bool ShowComposer => IsHearingView && CanRecommend && (_selected.Status == "presented" || _selected.Status == "hearing") && _behavior.CanPlayerSpeakAtHearing(_selected.BusinessId);
        [DataSourceProperty] public int BusinessCount => BusinessItems.Count;
        [DataSourceProperty] public int ArchiveCount => ArchiveItems.Count;
        [DataSourceProperty] public int MemberCount => Members.Count;
        [DataSourceProperty] public int ParticipantCount => Participants.Count;
        [DataSourceProperty] public int TranscriptCount => Transcript.Count;
        [DataSourceProperty] public int OptionCount => Options.Count;
        [DataSourceProperty] public int OptionRowCount => OptionRows.Count;
        [DataSourceProperty] public int PartyCount => Parties.Count;
        [DataSourceProperty] public int ObligationCount => Obligations.Count;
        [DataSourceProperty] public int DecisionObligationCount => DecisionObligations.Count;
        [DataSourceProperty] public int CommitmentCount => Commitments.Count;
        [DataSourceProperty] public int PolicyChoiceCount => PolicyChoices.Count;

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteBusinessTab()
        {
            _pendingOptionId = null; _view = "agenda";
            _selected = BusinessItems.FirstOrDefault(x => x.Record.BusinessId == _hearingBusinessId)?.Record ?? BusinessItems.FirstOrDefault()?.Record;
            RefreshDetails(); RaiseAll();
        }
        public void ExecuteHearingTab()
        {
            var hearing = BusinessItems.FirstOrDefault(x => x.Record.BusinessId == _hearingBusinessId)?.Record ?? BusinessItems.FirstOrDefault()?.Record;
            bool returning = CanReturnToHearing && hearing?.BusinessId == _ownPositionHearingId;
            if (!returning) _pendingOptionId = null;
            _view = "hearing"; _selected = hearing; RefreshDetails(); RaiseAll();
        }
        public void ExecuteMembersTab() => ChangeView("members");
        public void ExecuteArchiveTab() { _pendingOptionId = null; _view = "archive"; _recordsView = "decisions"; _selected = ArchiveItems.FirstOrDefault()?.Record; RefreshDetails(); RaiseAll(); }
        public void ExecuteDecisionsTab() { _recordsView = "decisions"; RaiseAll(); }
        public void ExecuteLawsTab() { _recordsView = "laws"; RaiseAll(); }
        public void ExecuteObligationsTab() { _view = "archive"; _recordsView = "obligations"; RaiseAll(); }
        public void ExecuteNewsTab() { _view = "archive"; _recordsView = "news"; RaiseAll(); }
        public void ExecuteProposalTerms() { _showOwnPosition = false; _showProposalTerms = true; RaiseAll(); }
        public void ExecutePublicDiscussion() { _showOwnPosition = false; _showProposalTerms = false; RaiseAll(); }
        public void ExecuteOwnPosition()
        {
            if (IsHearingView) _ownPositionHearingId = _selected?.BusinessId;
            _showOwnPosition = true; _view = "agenda"; RaiseAll();
        }
        public void ExecuteForMembers() => ShowPositionMembers("For");
        public void ExecuteAgainstMembers() => ShowPositionMembers("Against");
        public void ExecuteUndecidedMembers() => ShowPositionMembers("Undecided");
        private void ShowPositionMembers(string position) { _positionFilter = position; _partyFilter = ""; FilterMembers(); ChangeView("members"); }
        public void ExecuteClearMemberFilter() { _partyFilter = ""; _positionFilter = ""; MemberSearch = ""; }
        public void ExecuteNextMatter()
        {
            var all = BusinessItems.Concat(ArchiveItems).ToList(); if (all.Count == 0) return;
            int index = all.FindIndex(x => x.Record == _selected); _pendingOptionId = null; _selected = all[(index + 1) % all.Count].Record; RefreshDetails(); RaiseAll();
        }
        public void ExecuteLocateMember()
        {
            Hero hero = FindHero(_selectedMember?.HeroId); if (hero == null) return;
            _close?.Invoke(); TaleWorlds.CampaignSystem.Campaign.Current?.EncyclopediaManager?.GoToLink(hero.EncyclopediaLink);
        }
        public void ExecuteAuthorityTab() => ChangeView("authority");
        public void ExecuteNewPolicy() => ShowPrepareProposal();
        private void ChangeView(string view) { _view = view; RaiseAll(); }
        public void ExecuteCallVote()
        {
            if (!CanCallVote) return;
            if (CanMemberBallot)
            {
                if (string.IsNullOrEmpty(_pendingOptionId)) { StatusText = _playerBallot == null ? "Choose your ballot option, or explicitly abstain." : "Your filed ballot is unchanged. Select an outcome to revise it, or explicitly abstain."; return; }
                _behavior.SubmitMemberBallot(_selected.BusinessId, Hero.MainHero, _pendingOptionId, false, out string memberResult);
                _pendingOptionId = null; RefreshAll(memberResult); return;
            }
            if (!string.IsNullOrEmpty(_pendingOptionId) && !_behavior.RecommendBusiness(_selected.BusinessId, _pendingOptionId, Hero.MainHero, out string recommendationResult))
            { StatusText = recommendationResult; return; }
            _pendingOptionId = null;
            _behavior.VoteBusiness(_selected.BusinessId, Hero.MainHero, out string result); RefreshAll(result);
        }
        public void ExecuteRecordPosition()
        {
            if (CanReturnToHearing) { ExecuteHearingTab(); return; }
            if (!CanRecommend) return;
            string option = SelectedOwnOptionId;
            if (string.IsNullOrWhiteSpace(option)) { StatusText = "Select an outcome first."; return; }
            string result;
            if (CanMemberBallot) _behavior.DeclareBusinessPosition(_selected.BusinessId, Hero.MainHero.StringId, option, out result);
            else _behavior.RecommendBusiness(_selected.BusinessId, option, Hero.MainHero, out result);
            _pendingOptionId = null; RefreshAll(result);
        }
        public void ExecuteAdoptPetition() { if (!CanAdoptPetition) return; _behavior.AdoptBusinessPetition(_selected.BusinessId, Hero.MainHero, out string result); RefreshAll(result); }
        private void SelectOption(ReignGovernmentOptionVM option)
        {
            if (!CanRecommend) return;
            _pendingOptionId = option.Id; RefreshDetails(); RaiseAll();
        }
        private void SelectBusiness(ReignGovernmentBusinessCardVM card)
        {
            _pendingOptionId = null; _selected = card.Record; _hearingBusinessId = card.Record.BusinessId; _view = GovernmentBusinessRules.IsClosed(card.Record.Status) ? "archive" : card.Record.Status == "hearing" ? "hearing" : "agenda"; RefreshDetails(); RaiseAll();
        }
        public void ExecuteSpeak() { if (CanSpeak) _ = SpeakAsync(); }
        internal bool TryComposeHearing(string businessId, string message)
        {
            if (_finalized || !IsHearingView || !CanRecommend || _selected?.BusinessId != businessId
                || string.IsNullOrWhiteSpace(message)) return false;
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
        public void ExecuteIncreaseAuthority() { if (!CanIncreaseAuthority) { StatusText = AuthorityActionsExplanation; return; } _behavior.IncreaseGovernmentLevel(_kingdom, out string result); RefreshAll(result); }
        public void ExecuteReduceAuthority()
        {
            if (!CanReduceAuthority) { StatusText = AuthorityActionsExplanation; return; }
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
            _session = _behavior.GetBusinessSessionSummary(_kingdom.StringId);
            MeetingText = "Next session: " + Date(_session.Value<float?>("nextSessionDay") ?? -1);
            BusinessItems.Clear(); ArchiveItems.Clear();
            var records = _behavior.GetBusiness(_kingdom.StringId).ToList();
            foreach (var record in records.Where(x => !GovernmentBusinessRules.IsClosed(x.Status)))
            {
                var card = new ReignGovernmentBusinessCardVM(record, SelectBusiness, _behavior.DocketLabel(record.BusinessId));
                BusinessItems.Add(card);
            }
            foreach (var record in records.Where(x => GovernmentBusinessRules.IsClosed(x.Status))
                .OrderByDescending(x => x.ResolvedDay >= 0 ? x.ResolvedDay : x.CreatedDay).ThenByDescending(x => x.CreatedDay))
                ArchiveItems.Add(new ReignGovernmentBusinessCardVM(record, SelectBusiness, _behavior.DocketLabel(record.BusinessId)));
            var selectionItems = _view == "archive" ? ArchiveItems.AsEnumerable() : BusinessItems.Concat(ArchiveItems);
            _selected = selectionItems.FirstOrDefault(x => x.Record.BusinessId == selectedId)?.Record
                ?? (_view == "archive" ? ArchiveItems.FirstOrDefault()?.Record : BusinessItems.FirstOrDefault()?.Record);
            Obligations.Clear();
            foreach (var resolution in _behavior.Resolutions.Where(x => x.KingdomStringId == _kingdom.StringId && x.Status == "active"))
                Obligations.Add(new ReignGovernmentObligationVM(resolution, () => { if (!IsInteractive) return; _behavior.ContributeGoods(resolution.ResolutionId, int.MaxValue, out string result); RefreshAll(result); }, IsInteractive));
            Commitments.Clear();
            if (IsInteractive)
                foreach (var commitment in _behavior.GetGovernmentCommitments(_kingdom.StringId).Where(x => x.RulerHeroStringId == Hero.MainHero.StringId))
                    Commitments.Add(new ReignGovernmentCommitmentVM(commitment, _behavior.GetBusiness(_kingdom.StringId).FirstOrDefault(x => x.BusinessId == commitment.BusinessId)?.Title));
            ActiveLaws.Clear();
            foreach (var law in _kingdom.ActivePolicies) ActiveLaws.Add(new ReignGovernmentTextRecordVM(law.Name.ToString(), ReignGovernmentCampaignBehavior.PolicyGovernmentDescription(law), "Active kingdom policy"));
            foreach (JObject edict in _behavior.GetActiveEdicts(_kingdom.StringId).OfType<JObject>())
                ActiveLaws.Add(new ReignGovernmentTextRecordVM(edict.Value<string>("title") ?? edict.Value<string>("label") ?? "Settlement edict", (edict.Value<string>("settlement") ?? "Settlement") + " · " + (edict.Value<string>("terms") ?? "") + " · Budget remaining: " + (edict.Value<int?>("budgetRemaining") ?? 0) + " gold · Grain stock: " + (edict.Value<int?>("grainStock") ?? 0), (edict.Value<bool?>("pendingReturn") == true ? (edict.Value<string>("status") ?? "Ended") + " · " + (edict.Value<string>("returnDetail") ?? "Awaiting conserved resource return") : "Expires " + Date(edict.Value<float?>("expiresDay") ?? -1))));
            NewsEditions.Clear();
            foreach (var edition in _behavior.GetGovernmentNews()) NewsEditions.Add(new ReignGovernmentTextRecordVM(edition.Headline + " · Open bulletin", string.Join(" · ", edition.Kingdoms.Select(x => x.KingdomName)), edition.DateLabel, () => _behavior.OpenGovernmentNews(edition.Id)));
            ProposalSlots.Clear();
            foreach (JObject slot in (_session["slots"] as JArray ?? new JArray()).OfType<JObject>())
                ProposalSlots.Add(new ReignGovernmentTextRecordVM(slot.Value<string>("label"), slot.Value<string>("proposalTitle") ?? "Proposal slot unused", (slot.Value<int?>("used") ?? 0) + " of " + (slot.Value<int?>("limit") ?? 1)));
            RefreshDetails(); StatusText = status; RaiseAll();
        }
        private void RefreshDetails()
        {
            _ownPositionHearingId = null;
            DecisionObligations.Clear();
            if (_selected?.Kind == "seasonal")
                foreach (var obligation in Obligations.Where(x => x.ResolutionId == _selected.TargetId))
                    DecisionObligations.Add(obligation);
            _showOwnPosition = false;
            _showProposalTerms = HasExtendedSummary;
            Options.Clear(); OptionRows.Clear(); Participants.Clear(); Members.Clear(); _allMembers.Clear(); Transcript.Clear(); Shares.Clear(); ForMembers.Clear(); AgainstMembers.Clear(); UndecidedMembers.Clear();
            _forecast = _selected == null ? new JObject() : _behavior.GetBusinessForecast(_selected.BusinessId);
            var positions = _selected == null ? new JArray() : _behavior.GetBusinessPublicPositions(_selected.BusinessId);
            // Only the player's own persisted instruction is read here. Opening a page never submits or revises it.
            var ownBallot = _selected == null || Hero.MainHero == null ? null : _behavior.Participation.Ballots
                .LastOrDefault(x => x.BusinessId == _selected.BusinessId && x.HeroId == Hero.MainHero.StringId);
            _playerBallot = ownBallot == null ? null : new GovernmentMemberBallot { BusinessId = ownBallot.BusinessId,
                HeroId = ownBallot.HeroId, OptionId = ownBallot.OptionId, Abstain = ownBallot.Abstain, Source = ownBallot.Source, SubmittedDay = ownBallot.SubmittedDay };
            _playerPosition = positions.OfType<JObject>().FirstOrDefault(x => x.Value<string>("memberHeroId") == Hero.MainHero?.StringId);
            CrownActor = Person(_selected?.CrownActorHeroId, "Crown representative", "");
            Petitioner = Person(_selected?.PetitionerHeroStringId, "Petitioner", string.Empty);
            Sponsor = Person(_selected?.SponsorHeroStringId, "Sponsor", string.Empty);
            foreach (var card in BusinessItems.Concat(ArchiveItems)) card.IsSelected = card.Record == _selected;
            if (_selected != null)
            {
                foreach (JObject option in Array(_selected.OptionsJson).OfType<JObject>())
                {
                    string id = option.Value<string>("id");
                    JObject share = (_forecast["options"] as JArray)?.OfType<JObject>().FirstOrDefault(x => x.Value<string>("optionId") == id);
                    double weight = share?.Value<double?>("weight") ?? 0;
                    Options.Add(new ReignGovernmentOptionVM(id, option.Value<string>("label"), option.Value<string>("description"), id == SelectedOwnOptionId, CanRecommend, SelectOption, weight,
                        _forecast.Value<bool?>("isFinal") == true, HasUnrecordedClosure));
                    if (!HasUnrecordedClosure) Shares.Add(new ReignGovernmentShareVM(option.Value<string>("label"), weight, Options.Count - 1));
                }
                double undecided = _forecast.Value<double?>("undecidedWeight") ?? 0;
                if (!HasUnrecordedClosure && undecided > 0) Shares.Add(new ReignGovernmentShareVM("Undecided", undecided, 2));
                double abstained = _forecast.Value<double?>("abstainedWeight") ?? 0;
                if (!HasUnrecordedClosure && abstained > 0) Shares.Add(new ReignGovernmentShareVM("Abstained", abstained, 2));
                double vacant = _forecast.Value<double?>("vacantWeight") ?? 0;
                if (!HasUnrecordedClosure && vacant > 0) Shares.Add(new ReignGovernmentShareVM("Vacant", vacant, 2));
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
                {
                    bool crown = ballot.Value<string>("memberHeroId") == "crown";
                    string actorId = crown ? ballot.Value<string>("actorHeroId") : ballot.Value<string>("memberHeroId");
                    string speaker = crown ? (FindHero(actorId)?.Name?.ToString() ?? "Crown") : null;
                    Transcript.Add(new ReignGovernmentTranscriptVM(actorId, speaker, crown ? "Crown ballot" : ballot.Value<bool?>("absentee") == true ? "Absentee ballot" : "Recorded ballot", OptionLabel(ballot.Value<string>("optionId")), null));
                }
            }
            var currentSeats = _behavior.GetSeats(_kingdom.StringId).ToList();
            var frozenSeats = _selected == null ? new List<JObject>() : Array(_selected.FrozenSeatsJson).OfType<JObject>().ToList();
            bool frozen = _selected?.FrozenAuthorityLevel > 0;
            var memberIds = frozenSeats.Select(x => x.Value<string>("memberHeroId")).Concat(positions.OfType<JObject>().Select(x => x.Value<string>("memberHeroId"))).Concat(currentSeats.Select(x => x.HeroStringId)).Distinct().ToList();
            Parties.Clear();
            foreach (var party in _behavior.GetParties(_kingdom.StringId))
            {
                var eligible = frozenSeats.Where(x => x.Value<string>("partyId") == party.PartyId).ToList();
                double share = frozen ? eligible.Sum(x => x.Value<double>("weight")) : currentSeats.Count == 0 ? 0 : (100 - CrownWeight) * party.SeatCount / currentSeats.Count;
                int occupiedCount = frozen ? eligible.Count(x => positions.OfType<JObject>().Any(p => p.Value<string>("memberHeroId") == x.Value<string>("memberHeroId") && p.Value<bool?>("eligible") == true)) : party.SeatCount;
                Parties.Add(new ReignGovernmentPartyCardVM(party, share, occupiedCount, () => { _partyFilter = party.Name; _positionFilter = ""; FilterMembers(); ChangeView("members"); }, frozen ? eligible.Count : -1));
            }
            foreach (string memberId in memberIds.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var seat = currentSeats.FirstOrDefault(x => x.HeroStringId == memberId);
                var frozenSeat = frozenSeats.FirstOrDefault(x => x.Value<string>("memberHeroId") == memberId);
                var party = _behavior.GetParties(_kingdom.StringId).FirstOrDefault(x => x.PartyId == (frozen ? frozenSeat?.Value<string>("partyId") : seat?.PartyId));
                JObject declared = positions.OfType<JObject>().FirstOrDefault(x => x.Value<string>("memberHeroId") == memberId);
                bool vacant = declared?.Value<bool?>("vacant") == true;
                Hero hero = FindHero(memberId);
                string represented = Settlement.All.FirstOrDefault(x => x.StringId == seat?.SettlementStringId)?.Name?.ToString()
                    ?? hero?.Clan?.Name?.ToString() ?? "Government seat";
                var member = Person(memberId, vacant ? "Vacant share" : frozen && frozenSeat == null ? "Not eligible for this matter" : seat == null ? "Recorded seat" : party?.Name ?? "Independent", represented);
                member.FilterPartyName = party?.Name ?? "Independent";
                if (declared?.Value<bool?>("isFinal") == true) member.SetRecordedName(declared.Value<string>("name"));
                double weight = frozen ? declared?.Value<double?>("weight") ?? frozenSeat?.Value<double?>("weight") ?? 0 : seat == null || currentSeats.Count == 0 ? 0 : (100 - CrownWeight) / currentSeats.Count;
                member.SetPublicPosition(vacant ? "Vacant" : declared?.Value<string>("position") ?? "Undecided", weight, declared?.Value<float?>("declaredDay"), vacant ? "This frozen share is vacant and abstains. Its weight is not redistributed." : (declared?.Value<bool?>("isFinal") == true ? (string.IsNullOrEmpty(declared.Value<string>("optionId")) ? "Recorded abstention" : "Recorded vote: " + OptionLabel(declared.Value<string>("optionId"))) : declared?.Value<string>("statement")), () => { _selectedMember = member; ChangeView("members"); });
                _allMembers.Add(member);
                if (frozen && frozenSeat == null) continue;
                if (member.Position == "For") ForMembers.Add(member); else if (member.Position == "Against") AgainstMembers.Add(member); else UndecidedMembers.Add(member);
            }
            FilterMembers();
        }
        private void FilterMembers()
        {
            _memberListRevision++;
            string selected = _selectedMember?.HeroId; Members.Clear();
            foreach (var member in _allMembers.Where(x => (string.IsNullOrEmpty(_partyFilter) || x.FilterPartyName == _partyFilter)
                && (string.IsNullOrEmpty(_positionFilter) || x.Position == _positionFilter
                    || _positionFilter == "Undecided" && (x.Position == "Vacant" || x.Position == "Abstained"))
                && (string.IsNullOrWhiteSpace(_memberSearch) || x.Name.IndexOf(_memberSearch, StringComparison.CurrentCultureIgnoreCase) >= 0))) Members.Add(member);
            _selectedMember = Members.FirstOrDefault(x => x.HeroId == selected) ?? Members.FirstOrDefault();
            OnPropertyChanged(nameof(MemberCount));
        }
        public static string Date(float day)
        {
            return day < 0 ? "the next session" : ReignGovernmentCampaignBehavior.FormatGovernmentDate(day, false);
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
        internal static string Friendly(string text) => System.Text.RegularExpressions.Regex.Replace(
            (text ?? string.Empty).Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ");
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
            switch (status) { case "awaiting_sponsorship": return "Awaiting sponsorship"; case "hearing": return "In discussion"; case "queued": return "Queued for presentation"; case "presented": return "Lobbying interval"; case "decided": return "Decision recorded"; case "authorized": return "Authorized · Awaiting campaign execution"; case "execution_failed": return "Execution needs recovery"; default: return Friendly(status); }
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
        private readonly string _docket;
        public ReignGovernmentBusinessCardVM(ReignGovernmentBusinessRecord record, Action<ReignGovernmentBusinessCardVM> select, string docketLabel = "")
        {
            Record = record; _select = select;
            _docket = docketLabel?.StartsWith("Docket ", StringComparison.Ordinal) == true ? docketLabel.Trim() : "";
        }
        public ReignGovernmentBusinessRecord Record { get; }
        [DataSourceProperty] public string Title => Record.Title;
        [DataSourceProperty] public string Summary => (_docket.Length > 0 ? _docket + "\n" : "") + ReignGovernmentScreenVM.Friendly(Record.Kind) + " · " + (Record.IsUrgent ? "Urgent · " : "") + ReignGovernmentScreenVM.Friendly(Record.Status);
        [DataSourceProperty] public bool IsSelected { get => _selected; set { _selected = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(CardSprite)); } }
        [DataSourceProperty] public string CardSprite
        {
            get
            {
                string kind = Record.Kind == "policy" ? "policy" : Record.Kind.StartsWith("fief") ? "fief"
                    : Record.Kind == "seasonal" || Record.Kind == "world_action" ? "seasonal" : "diplomacy";
                return "reign_government_v3_business" + (IsSelected ? "_selected" : "");
            }
        }
        public void ExecuteSelect() => _select(this);
    }
    public sealed class ReignGovernmentOptionVM : ReignGovernmentPersonVM
    {
        private readonly Action<ReignGovernmentOptionVM> _select;
        public ReignGovernmentOptionVM(string id, string label, string detail, bool selected, bool enabled, Action<ReignGovernmentOptionVM> select, double weight = 0,
            bool finalVote = false, bool closedWithoutVote = false) : base(Candidate(id)?.Leader?.StringId, "Candidate", "", "", false)
        {
            ShareText = closedWithoutVote ? "No vote recorded" : weight.ToString("0.##") + (finalVote ? "% recorded vote" : "% declared support");
            Id = id; Label = label;
            Detail = string.IsNullOrWhiteSpace(detail) ? ShareText : detail.Length > 52 || detail.Contains("\n") ? ShareText + " · Terms below." : detail;
            IsSelected = selected; IsEnabled = enabled; _select = select;
        }
        private static Clan Candidate(string id) => id?.StartsWith("clan:") == true ? Clan.All.FirstOrDefault(x => x.StringId == id.Substring(5)) : null;
        [DataSourceProperty] public string ShareText { get; }
        public string Id { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool IsSelected { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        [DataSourceProperty] public bool IsUnavailable => !IsEnabled;
        [DataSourceProperty] public string SelectionText => IsSelected ? "Selected" : "";
        [DataSourceProperty] public string CardSprite => "reign_government_v3_candidate" + (HasPortrait ? "" : "_text") + (IsSelected ? "_selected" : "");
        [DataSourceProperty] public int LabelLeft => HasPortrait ? 178 : 22;
        [DataSourceProperty] public int LabelWidth => HasPortrait ? 226 : 374;
        [DataSourceProperty] public int ShareLeft => HasPortrait ? 215 : 22;
        [DataSourceProperty] public int ShareWidth => HasPortrait ? 189 : 374;
        [DataSourceProperty] public string RecommendationSprite => IsSelected ? "reign_government_v3_option_selected" : "reign_government_v3_option";
        public new void ExecuteSelect() { if (IsEnabled) _select(this); }
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
        private Action _select;
        [DataSourceProperty] public string Position { get; private set; } = "Undecided";
        [DataSourceProperty] public string VotingShare { get; private set; } = "";
        [DataSourceProperty] public string StatementDate { get; private set; } = "Not yet declared";
        [DataSourceProperty] public string Statement { get; private set; } = "No public statement recorded.";
        [DataSourceProperty] public string Location => ReignGovernmentScreenVM.FindHero(HeroId)?.CurrentSettlement?.Name?.ToString() ?? "Traveling";
        public void SetPublicPosition(string position, double weight, float? day, string statement, Action select)
        { Position = position; VotingShare = weight.ToString("0.##") + "%"; StatementDate = day.HasValue ? ReignGovernmentScreenVM.Date(day.Value) : "Not yet declared"; Statement = string.IsNullOrWhiteSpace(statement) ? "No public statement recorded." : statement; _select = select; }
        public void SetRecordedName(string name) { if (!string.IsNullOrWhiteSpace(name)) Name = name; }
        public void ExecuteSelect() => _select?.Invoke();
        public string HeroId { get; }
        public string FilterPartyName { get; set; } = "";
        public bool IsAbsent { get; }
        [DataSourceProperty] public string Name { get; private set; }
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
        public ReignGovernmentTranscriptVM(string id, string speaker, string role, string text, float? day) : base(id, role, "", "", false) { Speaker = string.IsNullOrWhiteSpace(speaker) ? Name : speaker; Text = text ?? string.Empty; Date = day.HasValue ? ReignGovernmentScreenVM.Date(day.Value) : string.Empty; }
        [DataSourceProperty] public string Speaker { get; }
        [DataSourceProperty] public string Text { get; }
        [DataSourceProperty] public string Date { get; }
    }
    public sealed class ReignGovernmentPartyCardVM : ViewModel
    {
        private readonly Action _select;
        public ReignGovernmentPartyCardVM(ReignGovernmentPartyRecord party, double share, int eligibleCount, Action select, int recordedCount = -1) { _select = select; VotingShare = share.ToString("0.##") + (recordedCount >= 0 ? "% frozen" : "% vote"); Name = party.Name; Planks = string.Join(" · ", ReignGovernmentCampaignBehavior.ParsePlanks(party.PlanksCsv).Select(x => ReignGovernmentScreenVM.Friendly(x.ToString()))); Speaker = (ReignGovernmentScreenVM.FindHero(party.SpeakerHeroStringId)?.Name?.ToString() ?? "Vacant speaker") + " · " + eligibleCount + " eligible seats" + (recordedCount > eligibleCount ? " · " + (recordedCount - eligibleCount) + " vacant" : ""); Statement = party.LastStatement ?? string.Empty; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string VotingShare { get; }
        public void ExecuteSelect() => _select?.Invoke();
        [DataSourceProperty] public string Planks { get; }
        [DataSourceProperty] public string Speaker { get; }
        [DataSourceProperty] public string Statement { get; }
    }
    public sealed class ReignGovernmentObligationVM : ViewModel
    {
        private readonly Action _contribute;
        public ReignGovernmentObligationVM(ReignGovernmentResolutionRecord record, Action contribute, bool interactive)
        {
            ResolutionId = record.ResolutionId;
            var template = ReignGovernmentResolutionCatalog.Find(record.TemplateId); Title = template?.Title ?? "Government obligation";
            var action = (ReignGovernmentResolutionAction)record.RouteActionValue;
            bool privateProgress = action == ReignGovernmentResolutionAction.ImproveClanRelation || action == ReignGovernmentResolutionAction.ImproveForeignRelation;
            Detail = (privateProgress ? "Awaiting verified completion" : record.CurrentValue.ToString("0") + " / " + record.RequiredAmount) + " · Due " + ReignGovernmentScreenVM.Date(record.DueDay);
            CanContribute = interactive && (action == ReignGovernmentResolutionAction.FoodDelivery || action == ReignGovernmentResolutionAction.GoodsDelivery); _contribute = contribute;
        }
        [DataSourceProperty] public string Title { get; }
        internal string ResolutionId { get; }
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

