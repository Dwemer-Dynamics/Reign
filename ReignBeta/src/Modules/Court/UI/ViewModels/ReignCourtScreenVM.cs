using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Court;
using ReignBeta.UI.HiddenInformation;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using ReignBeta.Court.Campaign;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.UI;
using ReignBeta.Government.UI;
using ReignBeta.Platform.Integration;
using ReignBeta.Spymaster.UI;
using ReignBeta.WorldSimulation.World;

namespace ReignBeta.Court.UI
{
    public sealed partial class ReignCourtScreenVM : ViewModel
    {
        private const string OpeningAudienceStatus = "Opening the audience...";
        private readonly ReignCourtCampaignBehavior _court;
        private readonly Action _close;
        private string _activeTabId = "court";
        private float _refreshClock;
        private bool _finalized;
        private bool _homeVisible = true;
        private bool _tabContentVisible;
        private bool _detailVisible;
        private string _screenTitle = "Court";
        private string _screenSubtitle = string.Empty;
        private string _dateText = string.Empty;
        private string _timeText = string.Empty;
        private string _statusText = string.Empty;
        private string _detailTitle = string.Empty;
        private string _detailBody = string.Empty;
        private string _detailMeta = string.Empty;
        private bool _audienceVisible;
        private bool _audienceBusy;
        private string _audienceSpeaker = string.Empty;
        private string _audienceText = string.Empty;
        private string _audienceEmotion = string.Empty;
        private string _audienceInput = string.Empty;
        private CourtMatter _audienceMatter;
        private Hero _audienceHero;
        private long _audienceViewRevision;
        // The sending view stays busy until its reply is applied on the main thread.
        private long? _sendingAudienceRevision;
        private ReignCourtCardVM _selectedCard;
        private string _playerBannerId = string.Empty;
        private string _playerBannerArgs = string.Empty;
        private string _playerBannerProvider = string.Empty;
        private string _nextDocketText = "Next docket: 8:00 AM";
        private bool _useLargeCourtLayout;
        private ReignCourtCardVM _chancellorCard;

        public ReignCourtScreenVM(ReignCourtCampaignBehavior court, Action close)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _close = close;
            Tabs = new MBBindingList<ReignCourtTabVM>();
            Counters = new MBBindingList<ReignCourtCounterVM>();
            Lands = new MBBindingList<ReignCourtCardVM>();
            DailyAgenda = new MBBindingList<ReignCourtCardVM>();
            People = new MBBindingList<ReignCourtCardVM>();
            DiplomaticStatus = new MBBindingList<ReignCourtCardVM>();
            AmbassadorCards = new MBBindingList<ReignCourtCardVM>();
            Rumors = new MBBindingList<ReignCourtCardVM>();
            TabRows = new MBBindingList<ReignCourtCardVM>();
            Actions = new MBBindingList<ReignCourtActionVM>();
            _court.StateChanged += OnCourtStateChanged;
            _court.AudienceReplyPendingChanged += OnAudienceReplyPendingChanged;
            BuildTabs();
            RefreshAll();
        }

        public MBBindingList<ReignCourtTabVM> Tabs { get; }
        public MBBindingList<ReignCourtCounterVM> Counters { get; }
        public MBBindingList<ReignCourtCardVM> Lands { get; }
        public MBBindingList<ReignCourtCardVM> DailyAgenda { get; }
        public MBBindingList<ReignCourtCardVM> People { get; }
        public MBBindingList<ReignCourtCardVM> DiplomaticStatus { get; }
        public MBBindingList<ReignCourtCardVM> AmbassadorCards { get; }
        public MBBindingList<ReignCourtCardVM> Rumors { get; }
        public MBBindingList<ReignCourtCardVM> TabRows { get; }
        public MBBindingList<ReignCourtActionVM> Actions { get; }

        [DataSourceProperty] public int DocketItemCount => DailyAgenda.Count;
        [DataSourceProperty] public bool DocketScrollVisible => DocketItemCount > 4;

        [DataSourceProperty] public bool IsRoyalCourt => _court.Session?.Authority == ReignCourtAuthority.Royal && _court.HasRoyalCommandAccess;
        [DataSourceProperty] public bool RoyalCommandsVisible => _court.HasRoyalCommandAccess;
        [DataSourceProperty] public bool LocalModeNoticeVisible => !_court.HasRoyalCommandAccess;
        [DataSourceProperty] public string DocketTitle => _court.HasRoyalCommandAccess ? "ROYAL DOCKET" : "LOCAL DOCKET";
        [DataSourceProperty] public string AuthorityText => _court.HasRoyalCommandAccess ? "ROYAL COURT" : "LOCAL RULE MODE";
        [DataSourceProperty] public string PlayerName => Hero.MainHero?.Name?.ToString() ?? "Ruler";
        [DataSourceProperty] public string PlayerClan
        {
            get
            {
                string clanName = Clan.PlayerClan?.Name?.ToString();
                return string.IsNullOrWhiteSpace(clanName) ? string.Empty : "Clan " + clanName;
            }
        }
        [DataSourceProperty] public string CultureTint => BuildCultureTint(Hero.MainHero?.Culture?.StringId);
        [DataSourceProperty] public string CourtTitle
        {
            get
            {
                string realmName = (Clan.PlayerClan?.Kingdom?.Name ?? Clan.PlayerClan?.Name)?.ToString();
                if (!_court.HasRoyalCommandAccess)
                {
                    Settlement host = Settlement.All.FirstOrDefault(x => x.StringId == _court.Session?.HostSettlementStringId);
                    return "LOCAL RULE MODE" + (host == null ? string.Empty : " AT " + host.Name.ToString().ToUpperInvariant());
                }
                return string.IsNullOrWhiteSpace(realmName) ? "THE ROYAL COURT" : "THE ROYAL COURT OF " + realmName.ToUpperInvariant();
            }
        }
        [DataSourceProperty] public string PlayerBannerId => _playerBannerId;
        [DataSourceProperty] public string PlayerBannerArgs => _playerBannerArgs;
        [DataSourceProperty] public string PlayerBannerProvider => _playerBannerProvider;
        [DataSourceProperty] public string NextDocketText { get { return _nextDocketText; } set { if (_nextDocketText != value) { _nextDocketText = value; OnPropertyChangedWithValue(value); } } }
        private float _courtWorkspaceTopInset = ReignCourtLayout.TopHudInset;
        [DataSourceProperty]
        public float CourtWorkspaceTopInset
        {
            get => _courtWorkspaceTopInset;
            set
            {
                if (Math.Abs(_courtWorkspaceTopInset - value) < 0.001f) return;
                _courtWorkspaceTopInset = value;
                OnPropertyChangedWithValue(value);
            }
        }
        [DataSourceProperty] public bool UseLargeCourtLayout => _useLargeCourtLayout;
        [DataSourceProperty] public bool HasChancellor => _chancellorCard?.HasPortrait == true;
        [DataSourceProperty] public string ChancellorName => _court.Chancellor?.IsVacant == false ? _court.Chancellor.HeroName : "Office vacant";
        [DataSourceProperty] public string ChancellorPortraitCacheKey => _chancellorCard?.PortraitCacheKey ?? string.Empty;
        [DataSourceProperty] public string ChancellorStateText
        {
            get
            {
                ReignChancellorOffice office = _court.Chancellor;
                if (office == null || office.IsVacant) return "VACANT";
                string current = office.State.ToString().Replace('_', ' ').ToUpperInvariant();
                if (office.DesiredActiveEffectiveDay >= 0
                    && office.DesiredActive != (office.State == ReignChancellorOfficeState.Active))
                    return current + "  â€¢  " + (office.DesiredActive ? "ACTIVE" : "INACTIVE") + " NEXT 8 AM";
                return current;
            }
        }
        [DataSourceProperty] public string ChancellorSalaryText
        {
            get
            {
                ReignChancellorOffice office = _court.Chancellor;
                if (office == null || office.IsVacant) return "Appoint through direct conversation";
                if (office.Emergency || office.State == ReignChancellorOfficeState.CaptiveContinuity
                    || office.State == ReignChancellorOfficeState.Handoff)
                    return "Emergency service â€¢ unpaid";
                return office.Salary.ToString("N0", CultureInfo.InvariantCulture) + " denars per Active day";
            }
        }
        [DataSourceProperty] public bool ChancellorToggleVisible
        {
            get
            {
                ReignChancellorOffice office = _court.Chancellor;
                return office != null && !office.IsVacant && !office.Emergency
                    && office.State != ReignChancellorOfficeState.CaptiveContinuity
                    && office.State != ReignChancellorOfficeState.Handoff;
            }
        }
        [DataSourceProperty] public bool LargeHomeVisible => HomeVisible && UseLargeCourtLayout;
        [DataSourceProperty] public bool ReferenceHomeVisible => HomeVisible && !UseLargeCourtLayout;

        [DataSourceProperty]
        public bool HomeVisible
        {
            get { return _homeVisible; }
            set
            {
                if (_homeVisible != value)
                {
                    _homeVisible = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(LargeHomeVisible));
                    OnPropertyChanged(nameof(ReferenceHomeVisible));
                }
            }
        }
        [DataSourceProperty]
        public bool TabContentVisible { get { return _tabContentVisible; } set { if (_tabContentVisible != value) { _tabContentVisible = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public bool DetailVisible { get { return _detailVisible; } set { if (_detailVisible != value) { _detailVisible = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(ActionsVisible)); } } }
        [DataSourceProperty]
        public string ScreenTitle { get { return _screenTitle; } set { if (_screenTitle != value) { _screenTitle = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string ScreenSubtitle { get { return _screenSubtitle; } set { if (_screenSubtitle != value) { _screenSubtitle = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string DateText { get { return _dateText; } set { if (_dateText != value) { _dateText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string TimeText { get { return _timeText; } set { if (_timeText != value) { _timeText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string StatusText { get { return _statusText; } set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string DetailTitle { get { return _detailTitle; } set { if (_detailTitle != value) { _detailTitle = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string DetailBody { get { return _detailBody; } set { if (_detailBody != value) { _detailBody = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string DetailMeta { get { return _detailMeta; } set { if (_detailMeta != value) { _detailMeta = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public bool AudienceVisible { get { return _audienceVisible; } set { if (_audienceVisible != value) { _audienceVisible = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(ActionsVisible)); } } }
        [DataSourceProperty] public bool ActionsVisible => DetailVisible && !AudienceVisible;
        [DataSourceProperty]
        public bool AudienceBusy { get { return _audienceBusy; } set { if (_audienceBusy != value) { _audienceBusy = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string AudienceSpeaker { get { return _audienceSpeaker; } set { if (_audienceSpeaker != value) { _audienceSpeaker = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string AudienceText { get { return _audienceText; } set { if (_audienceText != value) { _audienceText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string AudienceEmotion { get { return _audienceEmotion; } set { if (_audienceEmotion != value) { _audienceEmotion = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty]
        public string AudienceInput { get { return _audienceInput; } set { if (_audienceInput != value) { _audienceInput = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool AudienceHasPortrait => _selectedCard?.HasPortrait == true;
        [DataSourceProperty] public string AudiencePortraitId => _selectedCard?.PortraitId ?? string.Empty;
        [DataSourceProperty] public string AudiencePortraitAdditionalArgs => _selectedCard?.PortraitAdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string AudiencePortraitTextureProviderName => _selectedCard?.PortraitTextureProviderName ?? string.Empty;
        [DataSourceProperty] public string AudiencePortraitCacheKey => _selectedCard?.PortraitCacheKey ?? string.Empty;
        [DataSourceProperty] public bool IsPaused => _court.Session?.TimeMode == 0;
        [DataSourceProperty] public bool IsPlaying => _court.Session?.TimeMode == 1;
        [DataSourceProperty] public bool IsFastForwarding => _court.Session?.TimeMode == 2;

        public void SetLargeCourtLayout(bool useLargeLayout)
        {
            if (_useLargeCourtLayout == useLargeLayout) return;
            _useLargeCourtLayout = useLargeLayout;
            OnPropertyChanged(nameof(UseLargeCourtLayout));
            OnPropertyChanged(nameof(LargeHomeVisible));
            OnPropertyChanged(nameof(ReferenceHomeVisible));
        }

        public void OnFrameTick(float dt)
        {
            _refreshClock += dt;
            if (_refreshClock < 0.5f) return;
            _refreshClock = 0f;
            RefreshClock();
            RefreshTimeStates();
        }

        public void ExecuteClose() { _close?.Invoke(); }
        public void ExecuteEndCourt()
        {
            InformationManager.ShowInquiry(new InquiryData("End Rule Mode", "Close this sitting and return to ordinary campaign play? Unresolved matters remain recorded.", true, true,
                "End court", "Continue ruling", () => { _court.CloseSession("The ruler ended court."); _close?.Invoke(); }, null), true);
        }
        public void ExecutePause() { _court.PauseTime(); }
        public void ExecutePlay() { _court.PlayTime(); }
        public void ExecuteFastForward() { _court.FastForwardTime(); }
        public void ExecuteAdvanceToDocket()
        {
            if (!_court.AdvanceToNextDocket()) StatusText = "Court cannot advance while another matter is active.";
            else StatusText = "Advancing to the next docket at 8:00 AM.";
        }
        public void ExecuteAdvanceMatter()
        {
            CourtMatter matter = _court.AdvanceToNextMatter();
            if (matter == null) { StatusText = "No scheduled matter is waiting."; return; }
            SelectCard(new ReignCourtCardVM(matter.MatterId, "court", matter.Title, matter.Summary, FormatMatterState(matter), DueText(matter), SelectCard, matter));
        }
        public void ExecuteChancellorActive() { ScheduleChancellor(true); }
        public void ExecuteChancellorInactive() { ScheduleChancellor(false); }
        public void ExecuteOpenCourtHistory()
        {
            ReignCourtTabVM history = Tabs.FirstOrDefault(x => x.Id == "history");
            if (history == null)
            {
                StatusText = "Court history is unavailable.";
                return;
            }
            SelectTab(history);
        }
        public void ExecuteBackToHome() { SelectTab(Tabs.First(x => x.Id == "court")); }
        public void ExecuteOpenWarCouncil()
        {
            if (!RequireRoyalCommand()) return;
            ReignWarCouncilScreenManager.Open(_court);
        }
        public void ExecuteOpenSpymaster()
        {
            if (!RequireRoyalCommand()) return;
            _close?.Invoke();
            ReignSpymasterScreenManager.Open(_court);
        }
        public void ExecuteOpenAmbassadors()
        {
            if (!RequireRoyalCommand()) return;
            _close?.Invoke();
            ReignAmbassadorScreenManager.Open(_court);
        }
        public void ExecuteOpenEconomicReport()
        {
            if (!RequireRoyalCommand()) return;
            _close?.Invoke();
            ReignCourtEconomicReportScreenManager.Open(_court);
        }
        public void ExecuteOpenKeep()
        {
            if (!RequireRoyalCommand()) return;
            _close?.Invoke();
            ReignCastleLayoutScreenManager.Open(_court);
        }
        public void ExecuteOpenFamilyChambers()
        {
            _close?.Invoke();
            ReignFamilyChambersScreenManager.Open(_court);
        }
        public void ExecuteRoyalProclamation()
        {
            if (!RequireRoyalCommand()) return;
            InformationManager.ShowTextInquiry(new TextInquiryData(
                "Royal Proclamation",
                "Write the proclamation exactly as it should enter globally known history. It creates no direct mechanical effect.",
                true, true, "Preview", "Cancel", PreviewRoyalProclamation, null, false,
                text => string.IsNullOrWhiteSpace(text)
                    ? new Tuple<bool, string>(false, "A proclamation requires text.")
                    : new Tuple<bool, string>(true, string.Empty),
                string.Empty, string.Empty), true);
        }

        private void PreviewRoyalProclamation(string exactText)
        {
            InformationManager.ShowInquiry(new InquiryData(
                "Confirm Royal Proclamation",
                "The following exact text will become immutable, globally known history with no direct mechanics:\n\n" + exactText,
                true, true, "Proclaim", "Edit", () =>
                {
                    bool ok = _court.TryIssueRoyalProclamation(exactText, out string receipt);
                    StatusText = receipt;
                    if (ok) RefreshAll();
                }, () => PreviewRoyalProclamationEdit(exactText)), true);
        }

        private void PreviewRoyalProclamationEdit(string exactText)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(
                "Royal Proclamation", "Edit the exact proclamation text.",
                true, true, "Preview", "Cancel", PreviewRoyalProclamation, null, false,
                text => string.IsNullOrWhiteSpace(text)
                    ? new Tuple<bool, string>(false, "A proclamation requires text.")
                    : new Tuple<bool, string>(true, string.Empty),
                exactText, exactText), true);
        }
        public void ExecuteOpenRoyalCouncil()
        {
            if (!RequireRoyalCommand()) return;
            _close?.Invoke();
            ReignRoyalCouncilScreenManager.Open(_court);
        }
        public void ExecuteOpenGovernment()
        {
            if (!RequireRoyalCommand()) return;
            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (kingdom == null)
            {
                StatusText = "There is no kingdom government to convene.";
                return;
            }
            _close?.Invoke();
            ReignGovernmentScreenManager.Open(kingdom, kingdom.Leader == Hero.MainHero,
                () => { if (_court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(_court); });
        }
        public void ExecutePrimaryDetailAction()
        {
            if (_selectedCard?.Hero != null)
            {
                Hero hero = _selectedCard.Hero;
                _close?.Invoke();
                ReignCorrespondenceScreenManager.Open(hero);
                return;
            }
            if (_selectedCard?.Payload is CourtMatter matter) ShowMatterDecision(matter);
            else if (_selectedCard?.Payload is ReignSettlementSupplySnapshot land) TrySendEmergencyGrain(land);
            else StatusText = "This report is informational; choose one of its listed related records for an available action.";
        }

        public void ExecuteEndAudience()
        {
            CourtMatter matter = _audienceMatter;
            HideAudience();
            if (matter != null) _court.EndAudience(matter);
        }

        public async void ExecuteSendAudience()
        {
            CourtMatter matter = _audienceMatter;
            CourtSession session = _court.Session;
            Hero speaker = _audienceHero;
            string text = (AudienceInput ?? string.Empty).Trim();
            if (_finalized || !AudienceVisible || AudienceBusy || matter == null || speaker == null
                || string.IsNullOrWhiteSpace(text)) return;

            long viewRevision = _audienceViewRevision;
            _sendingAudienceRevision = viewRevision;
            RefreshAudienceBusy();
            AudienceInput = string.Empty;
            AudienceText = AudienceText + "\n\n" + PlayerName + ": " + text;

            ReignCourtDialogueReply reply;
            try
            {
                reply = await _court.RespondToAudienceAsync(matter, speaker, text).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Court audience response failed: " + ex.Message);
                reply = new ReignCourtDialogueReply { Error = "The audience response failed." };
            }

            await ReignMainThread.InvokeAsync(() =>
            {
                if (_sendingAudienceRevision == viewRevision) _sendingAudienceRevision = null;
                if (_finalized) return;
                RefreshAudienceBusy();
                if (viewRevision != _audienceViewRevision || session != _court.Session
                    || !AudienceVisible || matter != _audienceMatter || matter.IsTerminal)
                    return;

                if (!reply.Ok)
                {
                    StatusText = string.IsNullOrWhiteSpace(reply.Error) ? "The audience response failed." : reply.Error;
                    return;
                }
                AudienceEmotion = string.IsNullOrWhiteSpace(reply.Emotion) ? "measured" : reply.Emotion;
                AudienceText = AudienceText + "\n\n" + speaker.Name + ": " + reply.Text;
                matter.State = ReignCourtMatterState.AwaitingDecision;
                matter.Revision = Math.Max(matter.Revision, reply.Revision);
                StatusText = "The audience is awaiting your coded decision; dialogue cannot apply mechanical effects.";
                RefreshAll();
            }).ConfigureAwait(false);
        }

        private void HideAudience()
        {
            _audienceViewRevision++;
            _audienceMatter = null;
            _audienceHero = null;
            AudienceVisible = false;
            if (StatusText == OpeningAudienceStatus) StatusText = string.Empty;
            RefreshAudienceBusy();
        }

        private void OnAudienceReplyPendingChanged(string matterId)
        {
            if (_audienceMatter?.MatterId == matterId) RefreshAudienceBusy();
        }

        private void RefreshAudienceBusy()
        {
            AudienceBusy = AudienceVisible && (_sendingAudienceRevision == _audienceViewRevision
                || (_audienceMatter != null && _court.IsAudienceReplyPending(_audienceMatter.MatterId)));
        }

        public override void OnFinalize()
        {
            _finalized = true;
            _court.StateChanged -= OnCourtStateChanged;
            _court.AudienceReplyPendingChanged -= OnAudienceReplyPendingChanged;
            base.OnFinalize();
        }

        private void BuildTabs()
        {
            Tabs.Clear();
            if (!_court.HasRoyalCommandAccess)
            {
                Tabs.Add(new ReignCourtTabVM("court", "LOCAL DOCKET", false, "", SelectTab));
                Tabs.Add(new ReignCourtTabVM("history", "COURT HISTORY", false, "", SelectTab));
                Tabs[0].IsActive = true;
                return;
            }
            bool locked = _court.Session?.Authority != ReignCourtAuthority.Royal;
            Tabs.Add(new ReignCourtTabVM("court", "COURT", false, "", SelectTab));
            Tabs.Add(new ReignCourtTabVM("history", "COURT HISTORY", false, "", SelectTab));
            Tabs.Add(new ReignCourtTabVM("lands", "LANDS", false, "", SelectTab));
            Tabs.Add(new ReignCourtTabVM("subjects", "SUBJECTS", false, "", SelectTab));
            Tabs.Add(new ReignCourtTabVM("diplomacy", "DIPLOMACY", locked, "Royal authority is required.", SelectTab));
            Tabs.Add(new ReignCourtTabVM("ambassadors", "AMBASSADORS", locked, "Royal authority is required.", SelectTab));
            Tabs.Add(new ReignCourtTabVM("spymaster", "SPYMASTER", false, "", SelectTab));
            Tabs.Add(new ReignCourtTabVM("military", "MILITARY", locked, "Royal authority is required.", SelectTab));
            Tabs[0].IsActive = true;
        }

        private void SelectTab(ReignCourtTabVM tab)
        {
            if (tab == null) return;
            if (tab.IsLocked)
            {
                StatusText = tab.LockReason;
                return;
            }
            foreach (ReignCourtTabVM item in Tabs) item.IsActive = item == tab;
            _activeTabId = tab.Id;
            HomeVisible = tab.Id == "court";
            TabContentVisible = !HomeVisible;
            DetailVisible = false;
            HideAudience();
            Actions.Clear();
            ScreenTitle = tab.Label;
            ScreenSubtitle = BuildTabSubtitle(tab.Id);
            if (!HomeVisible) { BuildTabRows(tab.Id); _ = _court.RefreshServerTabAsync(tab.Id); }
        }

        private bool RequireRoyalCommand()
        {
            if (_court.HasRoyalCommandAccess) return true;
            StatusText = "Royal commands are available only while ruling from the designated capital.";
            InformationManager.DisplayMessage(new InformationMessage(StatusText));
            return false;
        }

        private void SelectCard(ReignCourtCardVM card)
        {
            if (card == null) return;
            if (card.Payload is ReignCourtLifeMatter courtLife)
            {
                ReignCourtCampaignBehavior court = _court;
                _close?.Invoke();
                ReignCourtPetitionScreenManager.Open(court, courtLife,
                    () => { if (court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court); });
                return;
            }
            if (card.Payload is ReignDocketPetition petition)
            {
                ReignCourtCampaignBehavior court = _court;
                _close?.Invoke();
                ReignCourtPetitionScreenManager.Open(court, petition,
                    () => { if (court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court); });
                return;
            }
            if (card.Payload is ReignNobleDocketMatter nobleMatter)
            {
                ReignCourtCampaignBehavior court = _court;
                _close?.Invoke();
                ReignCourtPetitionScreenManager.Open(court, nobleMatter,
                    () => { if (court?.IsRuleModeActive == true) ReignCourtScreenManager.Open(court); });
                return;
            }
            ReignCourtTabVM tab = Tabs.FirstOrDefault(x => x.Id == card.TabId);
            if (tab?.IsLocked == true) { StatusText = tab.LockReason; return; }
            _selectedCard = card;
            _activeTabId = card.TabId;
            foreach (ReignCourtTabVM item in Tabs) item.IsActive = item.Id == card.TabId;
            HomeVisible = false;
            TabContentVisible = true;
            DetailVisible = true;
            HideAudience();
            ScreenTitle = tab?.Label ?? "COURT";
            ScreenSubtitle = BuildTabSubtitle(card.TabId);
            BuildTabRows(card.TabId);
            DetailTitle = card.Title;
            DetailBody = card.Payload is ReignDocketHistoryRecord selectedHistory
                ? HistoryBody(selectedHistory) : card.Subtitle;
            DetailMeta = string.Join("   ", new[] { card.Status, card.Meta }.Where(x => !string.IsNullOrWhiteSpace(x)));
            BuildActions(card);
            OnPropertyChanged(nameof(AudienceHasPortrait));
            OnPropertyChanged(nameof(AudiencePortraitId));
            OnPropertyChanged(nameof(AudiencePortraitAdditionalArgs));
            OnPropertyChanged(nameof(AudiencePortraitTextureProviderName));
            OnPropertyChanged(nameof(AudiencePortraitCacheKey));
            OnPropertyChanged(nameof(ActionsVisible));
        }

        private void RefreshAll()
        {
            if (_finalized) return;
            OnPropertyChanged(nameof(IsRoyalCourt));
            OnPropertyChanged(nameof(RoyalCommandsVisible));
            OnPropertyChanged(nameof(LocalModeNoticeVisible));
            OnPropertyChanged(nameof(DocketTitle));
            OnPropertyChanged(nameof(AuthorityText));
            OnPropertyChanged(nameof(CourtTitle));
            RefreshChancellor();
            RefreshClock();
            RefreshCounters();
            RefreshPlayerBanner();
            RefreshHome();
            if (_activeTabId != "court") BuildTabRows(_activeTabId);
            RefreshTimeStates();
            Settlement host = Settlement.All.FirstOrDefault(x => x.StringId == _court.Session?.HostSettlementStringId);
            ScreenSubtitle = AuthorityText + (host == null ? string.Empty : " AT " + host.Name.ToString().ToUpperInvariant());
        }

        private void RefreshHome()
        {
            Lands.Clear();
            if (_court.HasRoyalCommandAccess)
            foreach (ReignSettlementSupplySnapshot land in ReignCourtSupplyService.GetScopedSnapshots(_court.Session.Authority).Take(5))
                Lands.Add(new ReignCourtCardVM(land.SettlementStringId, "lands", land.Name,
                    "Food " + land.FoodStocks.ToString("0") + "/" + land.FoodCapacity + "  â€¢  Grain " + land.GrainCount,
                    land.IsBesieged ? "BESIEGED" : land.HasShortage ? "SHORTAGE" : land.Loyalty < 40 ? "UNREST" : "STABLE",
                    "Prosperity " + land.Prosperity.ToString("0") + "  " + land.FoodChange.ToString("+0.0;-0.0;0.0") + "/day", SelectCard, land));

            DailyAgenda.Clear();
            if (_court.HasRoyalCommandAccess && Clan.PlayerClan?.Kingdom?.Leader == Hero.MainHero)
            {
                var standingKingdom = Clan.PlayerClan.Kingdom;
                DailyAgenda.Add(new ReignCourtCardVM("military_standing", "military", "Kingdom Military Standing",
                    "Peace standing keeps smaller field forces and releases a proportional share from each troop tier. "
                        + "Hostile political pressure raises readiness on the relevant frontier. The sovereign's selected peace target remains in force during war.\n"
                        + "Garrison targets remain full. Quartered regulars cost less in wages and supplies. A fixed share of discharged manpower returns to functioning villages.",
                    ReignBeta.WorldSimulation.Economy.ReignMilitaryReadiness.EffectiveMode(standingKingdom).ToUpperInvariant() + " STANDING",
                    "Review the kingdom's military readiness", SelectCard, standingKingdom, iconSprite: "reign_court_docket_military"));
            }
            foreach (ReignCourtLifeMatter matter in _court.DocketCourtLifeMatters)
            {
                Hero lead = FindHero(matter.Participants.FirstOrDefault()?.HeroId);
                DailyAgenda.Add(new ReignCourtCardVM(matter.MatterId, "court", matter.Title,
                    matter.Summary, matter.Source.ToString().ToUpperInvariant(),
                    matter.ExpiresDay < 0 ? "AWAITING AUDIENCE" : "VISIT UNTIL DAY " + matter.ExpiresDay.ToString("0.0"),
                    SelectCard, matter, lead));
            }
            var displayedPetitioners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ReignNobleDocketMatter matter in _court.DocketNobleMatters
                .Where(x => x?.IsPending == true)
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.MatterId))
            {
                ReignNobleMatterParticipant leadParticipant = matter.Participants.FirstOrDefault();
                Hero lead = FindHero(leadParticipant?.HeroId);
                DailyAgenda.Add(new ReignCourtCardVM(matter.MatterId, "court",
                    matter.Title, matter.Premise, matter.Severity.ToString().ToUpperInvariant(),
                    matter.Category.ToString().ToUpperInvariant(), SelectCard, matter, lead,
                    NobleMatterIcon(matter)));
            }
            foreach (ReignDocketPetition petition in _court.DocketPetitions
                .Where(x => x?.IsPending == true)
                .OrderByDescending(x => x.Severity)
                .ThenByDescending(x => x.NormalizedNeed)
                .ThenBy(x => x.PetitionId))
            {
                string petitionerKey = string.IsNullOrWhiteSpace(petition.PetitionerHeroId)
                    ? "petition:" + petition.PetitionId
                    : "hero:" + petition.PetitionerHeroId;
                if (!displayedPetitioners.Add(petitionerKey)) continue;
                Hero petitioner = FindHero(petition.PetitionerHeroId);
                DailyAgenda.Add(new ReignCourtCardVM(petition.PetitionId, "court",
                    petition.PetitionerName, petition.ProblemSummary,
                    petition.Severity.ToString().ToUpperInvariant(), PetitionRequestMeta(petition),
                    SelectCard, petition, petitioner, PetitionIcon(petition.Kind)));
                // New arrivals are bounded during generation. Existing pending
                // matters remain visible through this movie's finite scroll area.
            }
            OnPropertyChanged(nameof(DocketItemCount));
            OnPropertyChanged(nameof(DocketScrollVisible));

            People.Clear();
            if (!_court.HasRoyalCommandAccess)
            {
                DiplomaticStatus.Clear();
                AmbassadorCards.Clear();
                Rumors.Clear();
                return;
            }
            IEnumerable<CourtMatter> personal = _court.Matters.Where(x => !x.IsTerminal && (x.Kind == ReignCourtMatterKind.Obligation || x.Kind == ReignCourtMatterKind.RelationshipMatter || x.Kind == ReignCourtMatterKind.MarriageProposal || x.Kind == ReignCourtMatterKind.PrivateCounsel));
            foreach (CourtMatter matter in personal.OrderByDescending(x => x.IsCritical).ThenBy(x => x.DueDay < 0 ? float.MaxValue : x.DueDay).Take(6))
            {
                Hero hero = FindHero((matter.ParticipantHeroIdsCsv ?? string.Empty).Split(',').FirstOrDefault());
                People.Add(new ReignCourtCardVM(matter.MatterId, "subjects", hero?.Name?.ToString() ?? matter.Title, matter.Summary, FormatMatterState(matter), DueText(matter), SelectCard, matter, hero));
            }

            DiplomaticStatus.Clear();
            if (IsRoyalCourt)
            {
                Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
                foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated && x != playerKingdom)
                    .OrderByDescending(x => FactionManager.IsAtWarAgainstFaction(playerKingdom, x)).ThenByDescending(x => x.CurrentTotalStrength).Take(5))
                {
                    bool atWar = FactionManager.IsAtWarAgainstFaction(playerKingdom, kingdom);
                    DiplomaticStatus.Add(new ReignCourtCardVM(kingdom.StringId, "diplomacy", kingdom.Name.ToString(),
                        kingdom.Leader?.Name?.ToString() ?? "No recognized ruler", atWar ? "AT WAR" : "PEACE", "Strength " + kingdom.CurrentTotalStrength.ToString("0"), SelectCard, kingdom, kingdom.Leader));
                }
            }

            AmbassadorCards.Clear();
            foreach (AmbassadorPosting posting in _court.Ambassadors.Where(x => x.Status != "recalled").Take(3))
            {
                Hero hero = FindHero(posting.HeroStringId);
                Kingdom target = Kingdom.All.FirstOrDefault(x => x.StringId == posting.TargetKingdomStringId);
                AmbassadorCards.Add(new ReignCourtCardVM(posting.PostingId, "ambassadors", hero?.Name?.ToString() ?? "Unknown ambassador",
                    "In " + (target?.Name?.ToString() ?? posting.TargetKingdomStringId), posting.Status.ToUpperInvariant(), posting.MissionType.Replace('_', ' '), SelectCard, posting, hero));
            }

            Rumors.Clear();
            foreach (CourtMatter rumor in _court.Matters.Where(x => x.Kind == ReignCourtMatterKind.Rumor && !x.IsTerminal).OrderByDescending(x => x.CreatedDay).Take(4))
                Rumors.Add(new ReignCourtCardVM(rumor.MatterId, "spymaster", rumor.Title, rumor.Summary, "UNVERIFIED", "Source-limited", SelectCard, rumor));
        }

        private void RefreshChancellor()
        {
            ReignChancellorOffice office = _court.Chancellor;
            Hero hero = FindHero(office?.HeroId);
            _chancellorCard = hero == null ? null : new ReignCourtCardVM(
                office.TermId, "court", office.HeroName, ChancellorSalaryText,
                ChancellorStateText, string.Empty, null, office, hero);
            OnPropertyChanged(nameof(HasChancellor));
            OnPropertyChanged(nameof(ChancellorName));
            OnPropertyChanged(nameof(ChancellorPortraitCacheKey));
            OnPropertyChanged(nameof(ChancellorStateText));
            OnPropertyChanged(nameof(ChancellorSalaryText));
            OnPropertyChanged(nameof(ChancellorToggleVisible));
        }

        private void ScheduleChancellor(bool active)
        {
            if (_court.TryScheduleChancellorActive(active, out string error))
                StatusText = (active ? "Active" : "Inactive") + " Chancellor service is scheduled for the next 8:00 AM docket boundary.";
            else
                StatusText = error;
            RefreshChancellor();
        }

        private ReignPatronageCommission HistoryCommission(ReignDocketHistoryRecord history)
        {
            if (history == null || string.IsNullOrWhiteSpace(history.PetitionId)) return null;
            return _court.RulerDocketState.PatronageCommissions?.FirstOrDefault(x =>
                x != null && x.MatterId == history.PetitionId);
        }

        private string HistoryBody(ReignDocketHistoryRecord history)
        {
            if (history.Type == "patronage_completion")
            {
                // Keep the original audit snapshot intact; show the preserved work as readable prose.
                ReignPatronageCommission commission = HistoryCommission(history);
                ReignPatronageWork work = commission == null ? null
                    : _court.RulerDocketState.PatronageWorks?.FirstOrDefault(x =>
                        x != null && x.CommissionId == commission.CommissionId);
                string description = !string.IsNullOrWhiteSpace(work?.Description)
                    ? work.Description : commission?.Description;
                return string.Join("\n\n", new[] {
                    history.Summary,
                    commission == null ? null : "Patronage paid: "
                        + commission.GoldPaid.ToString("N0", CultureInfo.InvariantCulture) + " denars.",
                    string.IsNullOrWhiteSpace(description)
                        ? "The text of this work is not available in the surviving court record." : description
                }.Where(x => !string.IsNullOrWhiteSpace(x)));
            }
            if (history.Type == "court_life_patronage") return history.Summary ?? string.Empty;
            return string.Join("\n", new[] { history.Summary, history.Detail }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private void BuildTabRows(string tabId)
        {
            TabRows.Clear();
            switch (tabId)
            {
                case "history":
                    foreach (ReignDocketHistoryRecord history in _court.RulerDocketState.History
                        .Where(x => x != null)
                        .OrderByDescending(x => x.Day)
                        .ThenByDescending(x => x.RecordId)
                        .Take(250))
                    {
                        string heroId = !string.IsNullOrWhiteSpace(history.PetitionerHeroId)
                            ? history.PetitionerHeroId
                            : history.ChancellorHeroId;
                        Hero historyHero = FindHero(heroId);
                        bool patronageHistory = history.Type == "patronage_completion"
                            || history.Type == "court_life_patronage";
                        ReignPatronageCommission historyCommission = patronageHistory ? HistoryCommission(history) : null;
                        string historyBody = patronageHistory ? history.Summary : HistoryBody(history);
                        TabRows.Add(new ReignCourtCardVM(history.RecordId, tabId,
                            historyCommission?.Title ?? DocketHistoryTitle(history), historyBody,
                            (history.Outcome ?? string.Empty).Replace('_', ' ').ToUpperInvariant(),
                            "DAY " + history.Day.ToString(CultureInfo.InvariantCulture),
                            SelectCard, history, historyHero, DocketHistoryIcon(history)));
                    }
                    if (TabRows.Count == 0)
                        TabRows.Add(new ReignCourtCardVM("court_history_empty", tabId,
                            "No court history yet",
                            "Resolved petitions, completed commitments, expeditions, and Chancellor service will be recorded here.",
                            "EMPTY", string.Empty, SelectCard));
                    break;
                case "lands":
                    foreach (ReignSettlementSupplySnapshot land in ReignCourtSupplyService.GetScopedSnapshots(_court.Session.Authority))
                        TabRows.Add(new ReignCourtCardVM(land.SettlementStringId, tabId, land.Name,
                            "Owner " + (Settlement.Find(land.SettlementStringId)?.OwnerClan?.Name?.ToString() ?? "Unknown") + "  â€¢  Governor " + (FindHero(land.GovernorHeroStringId)?.Name?.ToString() ?? "Vacant")
                            + "\nProsperity " + land.Prosperity.ToString("0") + "  Loyalty " + land.Loyalty.ToString("0") + "  Security " + land.Security.ToString("0")
                            + "\nFood " + land.FoodStocks.ToString("0") + "/" + land.FoodCapacity + " (" + land.FoodChange.ToString("+0.0;-0.0;0.0") + "/day), market stock " + land.StrategicSupplyUnits + " supply units, market gold " + land.MarketGold
                            + "\nTax " + land.DailyTaxIncome + "/day  â€¢  Construction " + land.CurrentConstruction + " " + land.CurrentConstructionProgress.ToString("0") + " (" + land.Construction.ToString("0.0") + "/day)"
                            + "\nGarrison " + land.GarrisonCount + " (" + land.GarrisonWounded + " wounded)  â€¢  Militia " + land.Militia.ToString("0") + " (" + land.MilitiaChange.ToString("+0.0;-0.0;0.0") + "/day)  â€¢  Villages " + land.VillageCount + (land.RaidedVillageCount > 0 ? " (" + land.RaidedVillageCount + " raided)" : string.Empty),
                            land.IsBesieged ? "BESIEGED" : land.HasShortage ? "SHORTAGE" : "STABLE", "Select for an atomic 25-grain emergency transfer", SelectCard, land));
                    break;
                case "subjects":
                    CourtRegentAssignment regent=_court.ActiveRegent;Hero regentHero=FindHero(regent?.HeroStringId);
                    TabRows.Add(new ReignCourtCardVM(regent?.AssignmentId??"regent_vacant",tabId,"Regent: "+(regentHero?.Name?.ToString()??"Vacant"),
                        regentHero==null?"Without a Regent, ordinary unattended work defaults and threats progress. Major marriages and land trades do not occur.":"Ordinary unattended work uses capped Charm + Steward. Major matters are sent by mail and require the ruler's coded reply.",
                        regentHero==null?"VACANT":"ACTIVE","Regent mail never grants Presence credit.",SelectCard,regent,regentHero));
                    foreach(CourtOfficeAssignment assignment in _court.Offices.Where(x=>x.IsActive).OrderBy(x=>x.Office))
                    {
                        Hero holder=FindHero(assignment.HeroStringId);ReignCourtOfficePerformance performance=ReignCourtOfficePerformanceService.Evaluate(assignment.Office,holder,_court.Session);
                        TabRows.Add(new ReignCourtCardVM(assignment.AssignmentId,tabId,assignment.Office+": "+(holder?.Name?.ToString()??"Unknown officeholder"),performance.Summary,"ACTIVE","Absence, captivity, loyalty, traits, skill, and conflicting native duties affect this office.",SelectCard,assignment,holder));
                    }
                    foreach (CourtMatter pending in _court.Matters.Where(x => !x.IsTerminal && (x.Kind == ReignCourtMatterKind.MarriageProposal || x.Kind == ReignCourtMatterKind.RelationshipMatter || x.Kind == ReignCourtMatterKind.Obligation)))
                        TabRows.Add(MatterCard(pending, tabId));
                    foreach (CourtObligation obligation in _court.Obligations.Where(x => x.Status == "unresolved"))
                        TabRows.Add(new ReignCourtCardVM(obligation.ObligationId,tabId,"Obligation: "+obligation.ObligationType,obligation.Description,"DUE "+(obligation.DueDay<0?"NONE":obligation.DueDay.ToString("0.0")),"Terms are revision- and hash-bound",SelectCard,obligation));
                    foreach (CourtPlot plot in _court.Plots.Where(x => x.IsDiscoveredByPlayer && x.Status == "active"))
                        TabRows.Add(new ReignCourtCardVM(plot.PlotId,tabId,"Known "+plot.PlotType,plot.TargetStringId,"ACTIVE",(plot.Progress*100f).ToString("0")+"% â€¢ source-limited",SelectCard,plot));
                    foreach (Hero hero in SubjectHeroes().Take(200))
                        TabRows.Add(new ReignCourtCardVM(hero.StringId, tabId, hero.Name.ToString(), SubjectSubtitle(hero),
                            hero.IsPrisoner ? "CAPTIVE" : hero.CurrentSettlement != null ? "AT " + hero.CurrentSettlement.Name : "AWAY",
                            ReignHiddenInformationPolicy.IsCheatRevealEnabled
                                ? "Relation " + (Hero.MainHero?.GetRelation(hero) ?? 0).ToString("+0;-0;0")
                                : string.Empty, SelectCard, hero, hero));
                    break;
                case "diplomacy":
                    Kingdom ours = Clan.PlayerClan?.Kingdom;
                    foreach (Kingdom kingdom in Kingdom.All.Where(x => x != null && !x.IsEliminated && x != ours).OrderByDescending(x => x.CurrentTotalStrength))
                    {
                        bool war = FactionManager.IsAtWarAgainstFaction(ours, kingdom);
                        TabRows.Add(new ReignCourtCardVM(kingdom.StringId, tabId, kingdom.Name.ToString(), "Ruler " + (kingdom.Leader?.Name?.ToString() ?? "Unknown")
                            + "  â€¢  Fiefs " + kingdom.Fiefs.Count + "  â€¢  Clans " + kingdom.Clans.Count,
                            war ? "AT WAR" : "AT PEACE", "Comparative strength " + kingdom.CurrentTotalStrength.ToString("0"), SelectCard, kingdom, kingdom.Leader));
                    }
                    break;
                case "ambassadors":
                    foreach (AmbassadorPosting posting in _court.Ambassadors.OrderByDescending(x => x.AssignedDay))
                    {
                        Hero hero = FindHero(posting.HeroStringId);
                        TabRows.Add(new ReignCourtCardVM(posting.PostingId, tabId, hero?.Name?.ToString() ?? "Unknown ambassador",
                            "Mission " + posting.MissionType.Replace('_', ' ') + "  â€¢  Target " + (Kingdom.All.FirstOrDefault(x => x.StringId == posting.TargetKingdomStringId)?.Name?.ToString() ?? posting.TargetKingdomStringId),
                            posting.Status.ToUpperInvariant(), "Assigned day " + posting.AssignedDay.ToString("0.0"), SelectCard, posting, hero));
                    }
                    if (TabRows.Count == 0) TabRows.Add(new ReignCourtCardVM("vacant", tabId, "No active postings", "Up to three persistent royal ambassador postings may be assigned.", "VACANT", "Appoint a Foreign Advisor to improve negotiations.", SelectCard));
                    break;
                case "spymaster":
                    CourtOfficeAssignment spymaster = _court.Offices.FirstOrDefault(x => x.IsActive && x.Office == ReignCourtOffice.Spymaster);
                    Hero spymasterHero=FindHero(spymaster?.HeroStringId);string spymasterReport=spymaster==null?"Vacant":spymasterHero?.Name+"\n"+ReignCourtOfficePerformanceService.Evaluate(ReignCourtOffice.Spymaster,spymasterHero,_court.Session).Summary;
                    TabRows.Add(new ReignCourtCardVM("spymaster_office", tabId, "Spymaster", spymasterReport,
                        spymaster == null ? "ADVANCED OPERATIONS LOCKED" : "ACTIVE", "Exact native reports remain available; uncertain claims require sources and confidence.", SelectCard, spymaster, FindHero(spymaster?.HeroStringId)));
                    foreach (IntelligenceOperation operation in _court.IntelligenceOperations.OrderByDescending(x => x.StartedDay))
                        TabRows.Add(new ReignCourtCardVM(operation.OperationId, tabId, operation.OperationType.Replace('_', ' '), operation.TargetType + " " + operation.TargetStringId,
                            operation.State.ToString().ToUpperInvariant(), "Risk " + (operation.Risk * 100f).ToString("0") + "%  â€¢  Confidence " + (operation.Confidence * 100f).ToString("0") + "%", SelectCard, operation));
                    foreach (CourtMatter rumor in _court.Matters.Where(x => x.Kind == ReignCourtMatterKind.Rumor && !x.IsTerminal)) TabRows.Add(MatterCard(rumor, tabId));
                    break;
                case "military":
                    Kingdom realm = Clan.PlayerClan?.Kingdom;
                    if (realm != null)
                        TabRows.Add(new ReignCourtCardVM("military_standing", tabId, "KINGDOM MILITARY STANDING",
                            "Peace standing keeps smaller field retinues and raises frontier readiness as hostile political pressure grows. "
                                + "War standing permits full field forces. The player sovereign may retain Peace Standing during war.\n"
                                + "Garrison targets stay full in both standings. Quartered regulars receive lower wages and use fewer supplies through reduced spoilage and handling losses.",
                            ReignBeta.WorldSimulation.Economy.ReignMilitaryReadiness.EffectiveMode(realm).ToUpperInvariant() + " STANDING",
                            "Mobilization requires actual recruits, provisions and clan funding.", SelectCard, realm));
                    foreach (MobileParty party in MobileParty.AllLordParties.Where(x => x?.ActualClan?.Kingdom == realm).OrderByDescending(x => x.Party.EstimatedStrength))
                        TabRows.Add(new ReignCourtCardVM(party.StringId, tabId, party.Name.ToString(), "Commander " + (party.LeaderHero?.Name?.ToString() ?? "Vacant")
                            + "  â€¢  Men " + party.MemberRoster.TotalManCount + "  â€¢  Wounded " + party.MemberRoster.TotalWounded
                            + "\nMorale " + party.Morale.ToString("0") + (party.Army == null ? string.Empty : "  â€¢  Army cohesion " + party.Army.Cohesion.ToString("0"))
                            + "  â€¢  Food " + party.Food.ToString("0.0") + " days  â€¢  Prisoners " + party.PrisonRoster.TotalManCount,
                            party.Army == null ? "INDEPENDENT" : "IN ARMY", "Strength " + party.Party.EstimatedStrength.ToString("0") + "  â€¢  Objective " + party.DefaultBehavior, SelectCard, party, party.LeaderHero));
                    break;
                default:
                    foreach (CourtMatter matter in _court.Matters.Where(x => !x.IsTerminal).OrderByDescending(x => x.IsCritical).ThenBy(x => x.DueDay)) TabRows.Add(MatterCard(matter));
                    break;
            }
        }

        private bool CheckNobleJudgmentOwner(Func<bool> ownsJudgment)
        {
            if (ownsJudgment()) return true;
            StatusText = "The hearing or Court session changed. Reopen the judgment in history.";
            return false;
        }

        private void ConfirmNobleReversal(ReignNobleDocketMatter matter, Func<bool> ownsJudgment)
        {
            if (!CheckNobleJudgmentOwner(ownsJudgment)) return;
            InformationManager.ShowInquiry(new InquiryData("Reverse noble judgment",
                "Reverse this formal judgment? Safe social and relation effects will be compensated, native family or gold changes will remain, and your credibility will suffer.",
                true, true, "Reverse", "Cancel", () =>
                {
                    if (!CheckNobleJudgmentOwner(ownsJudgment)) return;
                    bool ok = _court.TryReverseNobleJudgment(matter.MatterId, out string receipt);
                    StatusText = receipt;
                    if (ok) RefreshAll();
                }, null), true);
        }

        private void ConfirmMurderExecution(ReignNobleDocketMatter matter, Func<bool> ownsJudgment)
        {
            if (!CheckNobleJudgmentOwner(ownsJudgment)) return;
            InformationManager.ShowInquiry(new InquiryData("Irreversible execution",
                "Execute the convicted prisoner now? The character will actually die. This cannot be reversed, even if later evidence proves the judgment wrong.",
                true, true, "Execute", "Cancel", () =>
                {
                    if (!CheckNobleJudgmentOwner(ownsJudgment)) return;
                    bool ok = _court.TryExecuteMurderSentence(matter.MatterId, out string receipt);
                    StatusText = receipt;
                    if (ok) RefreshAll();
                }, null), true);
        }

        private async void BeginAudience(CourtMatter matter, Hero speaker)
        {
            if (_finalized) return;
            CourtSession session = _court.Session;
            long viewRevision = ++_audienceViewRevision;
            StatusText = OpeningAudienceStatus;
            // A reopened view can show the pending turn without starting the same server matter again.
            string error = _court.IsAudienceReplyPending(matter.MatterId)
                ? string.Empty
                : await _court.BeginAudienceAsync(matter).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (_finalized || viewRevision != _audienceViewRevision || session != _court.Session) return;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    StatusText = error;
                    return;
                }
                _audienceMatter = matter;
                _audienceHero = speaker;
                AudienceSpeaker = speaker?.Name?.ToString() ?? "Petitioner";
                AudienceEmotion = "awaiting your words";
                AudienceText = matter.Summary;
                AudienceInput = string.Empty;
                AudienceVisible = true;
                if (StatusText == OpeningAudienceStatus) StatusText = string.Empty;
                RefreshAudienceBusy();
            }).ConfigureAwait(false);
        }

        private void ConfirmMatterDecision(CourtMatter matter, JObject option)
        {
            string label = option.Value<string>("label") ?? "Confirm";
            JObject consequences = option["consequences"] as JObject ?? new JObject();
            string termsHash = option.Value<string>("termsHash") ?? ReignCourtTerms.Hash(consequences.ToString(Newtonsoft.Json.Formatting.None));
            InformationManager.ShowInquiry(new InquiryData(matter.Title, matter.Summary + "\n\nPreview: " + consequences.ToString(Newtonsoft.Json.Formatting.Indented), true, true,
                label, "Cancel", () => RunCourtAction(() => _court.ResolveDecisionAsync(matter.MatterId, option.Value<string>("optionId"), termsHash, true), "Decision recorded with a validated receipt."), null), true);
        }

        private async void RunCourtAction(Func<System.Threading.Tasks.Task<string>> action, string success)
        {
            StatusText = "Validating command, revision, and exact terms...";
            string error = await action();
            await ReignMainThread.InvokeAsync(() =>
            {
                StatusText = string.IsNullOrWhiteSpace(error) ? success : error;
                RefreshAll();
                if (_selectedCard != null) BuildActions(_selectedCard);
            });
        }

        private void SubmitWorldPlan(ReignWorldActionRecord action, string success)
        {
            InformationManager.ShowInquiry(new InquiryData("Confirm court plan", action.Reason + "\n\nType: " + action.Type + "\nTarget: " + action.TargetSettlementStringId + action.TargetKingdomStringId,
                true, true, "Submit", "Cancel", () => RunCourtAction(() => _court.SubmitWorldActionAsync(action), success), null), true);
        }

        private void SubmitDiplomaticPlan(ReignWorldActionRecord action,string success)
        {
            InformationManager.ShowInquiry(new InquiryData("Send diplomatic package",action.Reason+"\n\nType: "+action.Type+"\nTarget: "+action.TargetKingdomStringId+"\nTerms: "+action.TermsJson,
                true,true,"Send","Cancel",()=>RunCourtAction(()=>_court.SubmitDiplomaticProposalAsync(action),success),null),true);
        }

        private string IntelTerms(string description,int gold,float influence,float days,float risk)
        {
            ReignCourtIntelligenceQuote quote=_court.GetIntelligenceQuote(gold,influence,days,risk);
            return description+" Exact office-adjusted quote: "+quote.GoldCost+" gold, "+quote.InfluenceCost.ToString("0")+" influence, "+quote.DurationDays.ToString("0.0")+" days, "+(quote.Risk*100f).ToString("0")+"% exposure risk.";
        }

        private static ReignWorldActionRecord StrategyPlan(ReignWorldActionType type, MobileParty party, Settlement target)
        {
            return new ReignWorldActionRecord
            {
                Type = type,
                ActorHeroStringId = party?.LeaderHero?.StringId ?? string.Empty,
                ActorKingdomStringId = party?.MapFaction?.StringId ?? string.Empty,
                TargetSettlementStringId = target?.StringId ?? string.Empty,
                Reason = "A validated objective submitted by the royal war council.",
                TermsJson = "{}",
                MinimumTroops = 40,
                DesiredStrength = 350,
                MaxAttempts = type == ReignWorldActionType.StrategyCaptureSettlement ? 24 : 3
            };
        }

        private void RefreshCounters()
        {
            Counters.Clear();
            CourtCounterSample latest = _court.CounterSamples.OrderByDescending(x => x.Day).FirstOrDefault();
            int supply = ReignCourtSupplyService.GetStrategicSupplyUnits(_court.Session.Authority);
            int gold = Hero.MainHero?.Gold ?? 0;
            float influence = Clan.PlayerClan?.Influence ?? 0f;
            float strength = _court.Session.Authority == ReignCourtAuthority.Royal ? Clan.PlayerClan?.Kingdom?.CurrentTotalStrength ?? 0f : Clan.PlayerClan?.CurrentTotalStrength ?? 0f;
            float renown = Clan.PlayerClan?.Renown ?? 0f;
            Counters.Add(new ReignCourtCounterVM("G", "FUNDS", FormatCompact(gold), Trend(x => x.Gold, gold, latest), "reign_court_status_funds"));
            Counters.Add(new ReignCourtCounterVM("I", "INFLUENCE", FormatCompact(influence), Trend(x => x.Influence, influence, latest), "reign_court_status_influence"));
            Counters.Add(new ReignCourtCounterVM("R", "RENOWN", FormatCompact(renown), Trend(x => x.Renown, renown, latest), "reign_court_status_renown"));
            Counters.Add(new ReignCourtCounterVM("S", "SUPPLY", FormatCompact(supply), Trend(x => x.Supply, supply, latest), "reign_court_status_supply"));
            Counters.Add(new ReignCourtCounterVM("M", "STRENGTH", FormatCompact(strength), Trend(x => x.Strength, strength, latest), "reign_court_status_strength"));
        }

        private void RefreshPlayerBanner()
        {
            BannerImageIdentifierVM banner = Clan.PlayerClan?.Banner == null ? null : new BannerImageIdentifierVM(Clan.PlayerClan.Banner, true);
            _playerBannerId = banner?.Id ?? string.Empty;
            _playerBannerArgs = banner?.AdditionalArgs ?? string.Empty;
            _playerBannerProvider = banner?.TextureProviderName ?? string.Empty;
            OnPropertyChanged(nameof(PlayerBannerId));
            OnPropertyChanged(nameof(PlayerBannerArgs));
            OnPropertyChanged(nameof(PlayerBannerProvider));
            OnPropertyChanged(nameof(CourtTitle));
        }

        private string Trend(Func<CourtCounterSample, float> selector, float current, CourtCounterSample latest)
        {
            List<CourtCounterSample> samples = _court.CounterSamples.OrderBy(x => x.Day).ToList();
            if (samples.Count < 2) return "0 /day";
            CourtCounterSample first = samples.First();
            CourtCounterSample last = samples.Last();
            int days = Math.Max(1, last.Day - first.Day);
            float change = (selector(last) - selector(first)) / days;
            return change.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + " /day";
        }

        private void RefreshClock()
        {
            CampaignTime now = CampaignTime.Now;
            DateText = now.GetSeasonOfYear + " " + (now.GetDayOfSeason + 1) + ", " + now.GetYear;
            double fraction = now.ToDays - Math.Floor(now.ToDays);
            int hour = (int)Math.Floor(fraction * 24d);
            int minute = (int)Math.Floor((fraction * 24d - hour) * 60d);
            string suffix = hour >= 12 ? "PM" : "AM";
            int displayHour = hour % 12;
            if (displayHour == 0) displayHour = 12;
            TimeText = displayHour + ":" + minute.ToString("00", CultureInfo.InvariantCulture) + " " + suffix;
            NextDocketText = "Next docket: 8:00 AM";
        }

        private void RefreshTimeStates()
        {
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsFastForwarding));
            if (_court.Session?.State == ReignCourtSessionState.PausedForMatter) StatusText = "Court is paused for a matter.";
            else if (!string.IsNullOrWhiteSpace(_court.Session?.InterruptionReason)) StatusText = _court.Session.InterruptionReason;
            else if (!_court.ServerAvailable) StatusText = _court.ServerStatus;
        }

        private void OnCourtStateChanged()
        {
            RefreshAudienceBusy();
            RefreshAll();
        }

        private void ShowMatterDecision(CourtMatter matter)
        {
            JArray options;
            try { options = JArray.Parse(matter.DecisionOptionsJson ?? "[]"); } catch { options = new JArray(); }
            JObject option = options.OfType<JObject>().FirstOrDefault();
            if (option == null) { StatusText = "This report has no direct decision."; return; }
            string label = option.Value<string>("label") ?? "Confirm";
            string termsHash = option.Value<string>("termsHash") ?? ReignCourtTerms.Hash((option["consequences"] ?? new JObject()).ToString());
            InformationManager.ShowInquiry(new InquiryData(matter.Title, matter.Summary + "\n\nPreview: " + (option["consequences"] ?? new JObject()).ToString(Newtonsoft.Json.Formatting.None), true, true,
                label, "Cancel", async () =>
                {
                    StatusText = "Validating the decision and canonical terms...";
                    string error = await _court.ResolveDecisionAsync(matter.MatterId, option.Value<string>("optionId"), termsHash, true);
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        StatusText = string.IsNullOrWhiteSpace(error) ? "Decision recorded with a validated receipt." : error;
                        RefreshAll();
                    });
                }, null), true);
        }

        private void TrySendEmergencyGrain(ReignSettlementSupplySnapshot target)
        {
            if (!_court.ServerAvailable)
            {
                StatusText = "The court server is unavailable. Native reports and time controls remain available, but mutations are disabled.";
                return;
            }
            ItemObject grain = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>("grain");
            Settlement source = Clan.PlayerClan?.Fiefs.Select(x => x.Settlement).FirstOrDefault(x => x != null && x != Settlement.Find(target.SettlementStringId) && grain != null && x.ItemRoster.GetItemNumber(grain) >= 25);
            if (source == null || grain == null) { StatusText = "No player-clan fief has 25 grain available for an atomic transfer."; return; }
            Settlement destination = Settlement.Find(target.SettlementStringId);
            InformationManager.ShowInquiry(new InquiryData("Emergency supply transfer", "Transfer 25 real grain from " + source.Name + " to " + destination.Name + "? Both rosters are revalidated before execution.", true, true,
                "Transfer", "Cancel", () =>
                {
                    ReignSupplyTransferRequest request = new ReignSupplyTransferRequest
                    {
                        CommandId = Guid.NewGuid().ToString("N"), SourceSettlementStringId = source.StringId, TargetSettlementStringId = destination.StringId,
                        ItemStringId = grain.StringId, ItemAmount = 25, GoldAmount = 0, VassalConsentGranted = source.OwnerClan == Clan.PlayerClan
                    };
                    RunCourtAction(() => _court.TransferSupplyAsync(request), "Transferred 25 real grain with a validated court receipt.");
                }, null), true);
        }

        private ReignCourtCardVM MatterCard(CourtMatter matter, string tab = "court")
        {
            Hero hero = FindHero((matter.ParticipantHeroIdsCsv ?? string.Empty).Split(',').FirstOrDefault());
            return new ReignCourtCardVM(matter.MatterId, tab, matter.Title, matter.Summary, FormatMatterState(matter), DueText(matter), SelectCard, matter, hero, DocketIcon(matter));
        }

        private static string PetitionRequestMeta(ReignDocketPetition petition)
        {
            if (petition == null) return string.Empty;
            switch (petition.Kind)
            {
                case ReignPetitionKind.Food:
                    return petition.FoodStockCost.ToString("N0", CultureInfo.InvariantCulture) + " food â€¢ " + petition.DurationDays + " days";
                case ReignPetitionKind.Soldiers:
                    return petition.SoldierCount.ToString("N0", CultureInfo.InvariantCulture) + " soldiers â€¢ " + petition.DurationDays + " days";
                default:
                    return petition.GoldCost.ToString("N0", CultureInfo.InvariantCulture) + " denars â€¢ " + petition.DurationDays + " days";
            }
        }

        private static string PetitionIcon(ReignPetitionKind kind)
        {
            switch (kind)
            {
                case ReignPetitionKind.Soldiers: return "reign_court_docket_military";
                case ReignPetitionKind.Food: return "reign_court_docket_accounts";
                case ReignPetitionKind.TownGold:
                case ReignPetitionKind.VillageGold: return "reign_court_docket_accounts";
                default: return "reign_court_docket_justice";
            }
        }

        private static string NobleMatterIcon(ReignNobleDocketMatter matter)
        {
            if (matter?.IsMurder == true || matter?.Severity == ReignNobleMatterSeverity.Exceptional)
                return "reign_court_docket_urgent";
            switch (matter?.Category)
            {
                case ReignNobleMatterCategory.Marriage:
                case ReignNobleMatterCategory.Divorce:
                case ReignNobleMatterCategory.Family:
                    return "reign_court_docket_social";
                case ReignNobleMatterCategory.Military:
                    return "reign_court_docket_military";
                case ReignNobleMatterCategory.Property:
                case ReignNobleMatterCategory.Finance:
                    return "reign_court_docket_accounts";
                default:
                    return "reign_court_docket_justice";
            }
        }

        private static string DocketHistoryTitle(ReignDocketHistoryRecord history)
        {
            if (history == null) return "Court record";
            if (string.Equals(history.Type, "petition", StringComparison.OrdinalIgnoreCase))
                return (string.IsNullOrWhiteSpace(history.PetitionerName)
                    ? "Petition"
                    : history.PetitionerName) + " â€” "
                    + (history.PetitionKind?.ToString() ?? "petition");
            if (string.Equals(history.Type, "chancellor_service", StringComparison.OrdinalIgnoreCase))
                return (string.IsNullOrWhiteSpace(history.ChancellorName)
                    ? "Chancellor"
                    : history.ChancellorName) + " â€” Chancellor service";
            if (string.Equals(history.Type, "soldier_expedition", StringComparison.OrdinalIgnoreCase))
                return (string.IsNullOrWhiteSpace(history.SettlementName)
                    ? "Garrison"
                    : history.SettlementName) + " â€” Soldier expedition";
            if (string.Equals(history.Type, "commitment", StringComparison.OrdinalIgnoreCase))
                return (string.IsNullOrWhiteSpace(history.SettlementName)
                    ? "Settlement"
                    : history.SettlementName) + " â€” "
                    + (history.PetitionKind?.ToString() ?? "court") + " commitment";
            string type = string.IsNullOrWhiteSpace(history.Type) ? "Court record" : history.Type;
            return type.Replace('_', ' ');
        }

        private static string DocketHistoryIcon(ReignDocketHistoryRecord history)
        {
            if (history?.PetitionKind != null) return PetitionIcon(history.PetitionKind.Value);
            if (string.Equals(history?.Type, "chancellor_service", StringComparison.OrdinalIgnoreCase))
                return "reign_court_docket_correspondence";
            return "reign_court_docket_justice";
        }

        private static string DocketIcon(CourtMatter matter)
        {
            if (matter?.IsCritical == true) return "reign_court_docket_urgent";
            string text = ((matter?.Title ?? string.Empty) + " " + (matter?.Summary ?? string.Empty)).ToLowerInvariant();
            if (text.Contains("delegate") || text.Contains("treaty") || text.Contains("compact") || text.Contains("ambassador")) return "reign_court_docket_diplomacy";
            if (text.Contains("account") || text.Contains("tax") || text.Contains("ledger") || text.Contains("supply") || text.Contains("grain")) return "reign_court_docket_accounts";
            if (text.Contains("dinner") || text.Contains("feast") || text.Contains("marriage") || text.Contains("audience")) return "reign_court_docket_social";
            if (text.Contains("levy") || text.Contains("garrison") || text.Contains("war") || text.Contains("army") || text.Contains("military")) return "reign_court_docket_military";
            if (text.Contains("message") || text.Contains("letter") || text.Contains("correspondence") || text.Contains("sealed")) return "reign_court_docket_correspondence";
            return "reign_court_docket_justice";
        }

        private IEnumerable<Hero> SubjectHeroes()
        {
            Kingdom realm = Clan.PlayerClan?.Kingdom;
            return Hero.AllAliveHeroes.Where(x => x != Hero.MainHero && (x.Clan == Clan.PlayerClan || x.CompanionOf == Clan.PlayerClan
                || ReignCourtNobleCampaignBehavior.IsCourtNoble(x) && (_court.Session.Authority == ReignCourtAuthority.Royal ? x.Clan?.Kingdom == realm : x.Clan == Clan.PlayerClan)
                || _court.Session.Authority == ReignCourtAuthority.Royal && x.Clan?.Kingdom == realm))
                .OrderByDescending(x => x.Clan == Clan.PlayerClan).ThenBy(x => x.Name?.ToString()).ThenBy(x => x.StringId);
        }

        private static Hero FindHero(string id) { return string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x => x.StringId == id.Trim()); }
        private static string SubjectSubtitle(Hero hero) { return (hero.Clan?.Name?.ToString() ?? hero.Occupation.ToString()) + "  â€¢  " + (hero.Clan?.Kingdom?.Name?.ToString() ?? "No kingdom"); }
        private static string FormatMatterState(CourtMatter matter) { return matter.IsCritical ? "URGENT" : matter.State.ToString().Replace('_', ' ').ToUpperInvariant(); }
        private static string DueText(CourtMatter matter) { return matter.DueDay < 0 ? "No deadline" : "Due day " + matter.DueDay.ToString("0.#"); }
        private static string FormatCompact(float value) { return Math.Abs(value) >= 10000f ? (value / 1000f).ToString("0.#") + "K" : value.ToString("0"); }
        private static string BuildTabSubtitle(string tabId)
        {
            switch (tabId)
            {
                case "lands": return "SETTLEMENT REPORTS, AID, GOVERNORS, AND SUPPLY";
                case "subjects": return "HOUSEHOLD, VASSALS, COURTIERS, OBLIGATIONS, AND OFFICES";
                case "diplomacy": return "REALM STANCES, WARS, TREATIES, AND PROPOSALS";
                case "ambassadors": return "PERSISTENT ROYAL POSTINGS AND MISSIONS";
                case "spymaster": return "SOURCE-LIMITED INTELLIGENCE AND OPERATIONS";
                case "military": return "ARMIES, PARTIES, READINESS, SUPPLIES, AND WAR COUNCILS";
                case "history": return "RESOLVED PETITIONS, COMMITMENTS, EXPEDITIONS, AND CHANCELLOR SERVICE";
                default: return "AGENDA, AUDIENCES, JUDGMENTS, AND COURT HISTORY";
            }
        }

        private static string BuildCultureTint(string cultureId)
        {
            switch ((cultureId ?? string.Empty).ToLowerInvariant())
            {
                case "vlandia": return "#7A1E1628";
                case "sturgia": return "#274B6828";
                case "battania": return "#36552A28";
                case "aserai": return "#8A5B1E24";
                case "khuzait": return "#2A625B28";
                case "empire": return "#5A396324";
                default: return "#9A6B2220";
            }
        }
    }
}
