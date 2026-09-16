using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using ReignBeta.Court;
using ReignBeta.CastleChat;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignPartyChatBatchResult
    {
        public bool Ok { get; set; }
        public string Error { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public string ExchangeId { get; set; } = string.Empty;
        public List<ReignPartyChatReply> Replies { get; } = new List<ReignPartyChatReply>();
    }

    public sealed class ReignPartyChatScreenVM : ViewModel
    {
        private readonly ReignPartyChatSession _session;
        private readonly Action _closeScreen;
        private readonly Action<Hero> _openEncyclopediaOverlay;
        private readonly Action<Hero> _requestPortraitOverlay;
        private readonly List<Hero> _conversationHeroes = new List<Hero>();
        private readonly List<string> _automationHeroOrder = new List<string>();
        private Task<string> _conversationSessionTask;
        private Task _activeBatchTask;
        private string _conversationSessionId = string.Empty;
        private bool _conversationFinishRequested;
        private bool _isFinalized;
        private string _title;
        private string _locationText;
        private string _selectedSummary;
        private string _inputText;
        private string _busyText;
        private bool _isBusy;
        private int _chatScrollVersion;
        private bool _isOverlayVisible = true;
        private bool _isPortraitPreviewVisible;
        private string _portraitPreviewName;
        private string _portraitPreviewId;
        private string _portraitPreviewAdditionalArgs;
        private string _portraitPreviewTextureProviderName;
        private string _portraitPreviewCacheKey;
        private string _backgroundImageId;
        private string _backgroundAdditionalArgs;
        private string _backgroundTextureProviderName;
        private string _playerPortraitId;
        private string _playerPortraitAdditionalArgs;
        private string _playerPortraitTextureProviderName;
        private float _portraitPreviewImageWidth = 690f;
        private float _portraitPreviewImageHeight = 920f;
        private float _portraitPreviewFrameWidth = 726f;
        private float _portraitPreviewFrameHeight = 956f;
        private readonly CastleRoomSessionRecord _castleSession;
        private readonly bool _castleMode;
        private string _castleRoomName = string.Empty;
        private string _castleArtImageId = string.Empty;
        private bool _inputEnabled = true;
        private readonly bool _targetedReviewMode;
        private readonly string _targetedReviewContext = string.Empty;
        private readonly Settlement _homesSettlement;
        private readonly Hero _homeResident;
        private readonly string _homeContext = string.Empty;

        [DataSourceProperty] public bool IsHomesMode => _homesSettlement != null;
        [DataSourceProperty] public bool AllowGroupSelection => !IsHomesMode;
        [DataSourceProperty] public bool IsHomeDirectory => IsHomesMode && _homeResident == null;
        [DataSourceProperty] public bool HasChatSession => !IsHomeDirectory;
        [DataSourceProperty] public string HomeDirectoryText => PartyMembers.Count == 0
            ? "You have not met anyone living here yet. Speak with people in the settlement to get to know them."
            : "Select a resident's card to chat at their home, or choose Visit in person to meet them in the settlement.";
        [DataSourceProperty] public string RosterTitle => IsHomesMode ? "Known residents" : "Available Characters";
        [DataSourceProperty] public string ClearSelectionText => IsHomesMode ? "Homes" : "Clear";

        public bool TargetedReviewProviderFailed { get; private set; }

        public ReignPartyChatScreenVM(Action closeScreen, Action<Hero> openEncyclopediaOverlay = null, Action<Hero> requestPortraitOverlay = null)
        {
            _closeScreen = closeScreen;
            _openEncyclopediaOverlay = openEncyclopediaOverlay;
            _requestPortraitOverlay = requestPortraitOverlay;
            _session = new ReignPartyChatSession();
            PartyMembers = new MBBindingList<ReignPartyChatMemberVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();

            Title = "Reign Party Chat";
            SetBackgroundIdentifier();
            RefreshPartyMembers();
            RefreshLabels();
            AddSystemLine("Select characters above, then speak to them as a group.", false);
            ReignLog.Info("Party chat opened characters=" + PartyMembers.Count);
        }

        public ReignPartyChatScreenVM(Action closeScreen, CastleRoomSessionRecord castleSession,
            Action<Hero> openEncyclopediaOverlay = null, Action<Hero> requestPortraitOverlay = null)
        {
            _closeScreen = closeScreen; _openEncyclopediaOverlay = openEncyclopediaOverlay;
            _requestPortraitOverlay = requestPortraitOverlay; _castleSession = castleSession; _castleMode = true;
            _session = new ReignPartyChatSession();
            _session.RestoreIdentity(castleSession?.ServerConversationSessionId);
            PartyMembers = new MBBindingList<ReignPartyChatMemberVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();
            CastleRoom room = castleSession == null ? CastleRoom.MainHall : (CastleRoom)castleSession.Room;
            _castleRoomName = string.IsNullOrWhiteSpace(castleSession?.DisplayName)
                ? ReignCastleLayoutScreenVM.RoomName(room) : castleSession.DisplayName;
            Title = _castleRoomName; LocationText = _castleRoomName;
            SetPlayerPortraitIdentifier();
            _castleArtImageId = castleSession?.ImageStatus != "ready" || string.IsNullOrWhiteSpace(castleSession?.ImageLocalPath)
                ? string.Empty : ReignBeta.UI.EventArt.ReignEventArtTextureFactory.BuildCastleSceneImageId(castleSession.SessionKey);
            RestoreCastleTranscript();
            RefreshCastleMembers();
            RefreshLabels();
            if (PartyMembers.Count == 0)
            {
                AddSystemLine("The " + _castleRoomName + " is quiet.", true);
                InputEnabled = false;
            }
            else if (ChatLines.Count == 0)
            {
                _activeBatchTask = RequestLiveNpcTurns(GetSelectedHeroes(), string.Empty,
                    requireStillSelected: false, castleOpening: true,
                    suppressRelationshipAssessment: true);
            }
        }

        public ReignPartyChatScreenVM(Action closeScreen, Hero targetedHero, string reviewContext,
            Action<Hero> openEncyclopediaOverlay = null, Action<Hero> requestPortraitOverlay = null)
        {
            _closeScreen = closeScreen;
            _openEncyclopediaOverlay = openEncyclopediaOverlay;
            _requestPortraitOverlay = requestPortraitOverlay;
            _targetedReviewMode = true;
            _targetedReviewContext = reviewContext ?? string.Empty;
            TargetedReviewProviderFailed = true;
            _session = new ReignPartyChatSession();
            PartyMembers = new MBBindingList<ReignPartyChatMemberVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();
            Title = "Temporary Guest Review — " + (targetedHero?.Name?.ToString() ?? "Noble");
            SetBackgroundIdentifier();
            if (targetedHero != null)
                PartyMembers.Add(new ReignPartyChatMemberVM(targetedHero, true, _ => { },
                    PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait));
            RefreshLabels();
            AddSystemLine(_targetedReviewContext, false);
            if (targetedHero == null)
            {
                AddSystemLine("The requested guest is no longer available.", false);
                InputEnabled = false;
                return;
            }
            _session.BeginOpeningTurn();
            Task<ReignPartyChatBatchResult> opening = RequestLiveNpcTurns(
                new List<Hero> { targetedHero }, string.Empty, requireStillSelected: false,
                openingTurn: true, suppressRelationshipAssessment: true);
            _activeBatchTask = opening;
            _ = TrackTargetedOpeningAsync(opening);
        }

        public ReignPartyChatScreenVM(Action closeScreen, Settlement settlement, Hero resident,
            Action<Hero> openEncyclopediaOverlay = null, Action<Hero> requestPortraitOverlay = null)
        {
            _closeScreen = closeScreen; _openEncyclopediaOverlay = openEncyclopediaOverlay;
            _requestPortraitOverlay = requestPortraitOverlay;
            _homesSettlement = settlement;
            _session = new ReignPartyChatSession();
            PartyMembers = new MBBindingList<ReignPartyChatMemberVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();
            var residents = ReignEncounteredResidentsCampaignBehavior.Instance?.ResidentsAt(settlement) ?? new List<Hero>();
            _homeResident = residents.Contains(resident) ? resident : null;
            _homeContext = _homeResident == null ? string.Empty
                : "This is a private visit to " + _homeResident.Name + "'s home in " + settlement.Name
                    + ". Only the player and this resident are present and can hear this conversation. "
                    + "The other residents in the Homes directory are not present and cannot witness it. "
                    + "The resident still has their usual work. An invitation or recruitment requires their explicit agreement.";
            Title = "Homes — " + settlement.Name;
            SetBackgroundIdentifier();
            RefreshPartyMembers(); RefreshLabels();
            InputEnabled = _homeResident != null;
            if (_homeResident == null)
            {
                AddSystemLine(residents.Count == 0
                    ? "You have not met anyone who is at home here yet. Speak with people in the settlement to get to know them."
                    : "Select a resident to chat privately at their home, or choose Visit in person to meet them at their usual place.", false);
            }
            else
            {
                AddSystemLine("You visit " + _homeResident.Name + " at home.", false);
                _session.BeginOpeningTurn();
                _activeBatchTask = RequestLiveNpcTurns(new List<Hero> { _homeResident }, string.Empty,
                    requireStillSelected: true, openingTurn: true);
            }
        }

        public MBBindingList<ReignPartyChatMemberVM> PartyMembers { get; }
        public MBBindingList<ReignChatLineVM> ChatLines { get; }

        [DataSourceProperty] public string PlayerPortraitCacheKey => AIPortraits.CharacterCacheId.ForHero(Hero.MainHero) ?? string.Empty;
        [DataSourceProperty] public string PlayerDisplayName => (Hero.MainHero?.Name?.ToString() ?? "Player") + "\n" + (Hero.MainHero?.IsKingdomLeader == true ? "Ruler" : "Regent");
        [DataSourceProperty] public string PlayerPortraitId => _playerPortraitId ?? string.Empty;
        [DataSourceProperty] public string PlayerPortraitAdditionalArgs => _playerPortraitAdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PlayerPortraitTextureProviderName => _playerPortraitTextureProviderName ?? "CharacterImageTextureProvider";
        [DataSourceProperty] public string PortraitPreviewCacheKey => _portraitPreviewCacheKey ?? string.Empty;

        [DataSourceProperty] public bool IsCastleMode => _castleMode;
        [DataSourceProperty] public string CastleRoomName => _castleRoomName;
        [DataSourceProperty] public string CastleArtImageId => _castleArtImageId;
        internal void RefreshCastleArt()
        {
            if (_isFinalized || !_castleMode || _castleSession?.ImageStatus != "ready"
                || string.IsNullOrWhiteSpace(_castleSession.ImageLocalPath) || HasCastleArt) return;
            string imageId = ReignBeta.UI.EventArt.ReignEventArtTextureFactory.BuildCastleSceneImageId(_castleSession.SessionKey);
            if (ReignBeta.UI.EventArt.ReignEventArtTextureFactory.GetOrBuild(imageId) == null) { _castleSession.ImageStatus = "failed"; ReignLog.Warn("Castle scene could not be loaded; retaining the approved fallback."); return; }
            _castleArtImageId = imageId;
            OnPropertyChanged(nameof(CastleArtImageId));
            OnPropertyChanged(nameof(HasCastleArt));
            OnPropertyChanged(nameof(ShowCastleApprovedFallback));
        }
        [DataSourceProperty] public bool HasCastleArt => !string.IsNullOrWhiteSpace(_castleArtImageId);
        [DataSourceProperty] public bool ShowCastleApprovedFallback => !HasCastleArt;
        [DataSourceProperty] public bool InputEnabled { get => _inputEnabled; private set { if (_inputEnabled == value) return; _inputEnabled = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string BackgroundImageId
        {
            get { return _backgroundImageId; }
            set
            {
                if (value != _backgroundImageId)
                {
                    _backgroundImageId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BackgroundAdditionalArgs
        {
            get { return _backgroundAdditionalArgs; }
            set
            {
                if (value != _backgroundAdditionalArgs)
                {
                    _backgroundAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BackgroundTextureProviderName
        {
            get { return _backgroundTextureProviderName; }
            set
            {
                if (value != _backgroundTextureProviderName)
                {
                    _backgroundTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsOverlayVisible
        {
            get { return _isOverlayVisible; }
            set
            {
                if (value != _isOverlayVisible)
                {
                    _isOverlayVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        private void SetBackgroundIdentifier()
        {
            try
            {
                CharacterCode code = Hero.MainHero?.CharacterObject == null ? null : CharacterCode.CreateFrom(Hero.MainHero.CharacterObject);
                CharacterImageIdentifierVM identifier = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
                BackgroundImageId = identifier?.Id ?? string.Empty;
                BackgroundAdditionalArgs = identifier?.AdditionalArgs ?? string.Empty;
                BackgroundTextureProviderName = identifier?.TextureProviderName ?? "CharacterImageTextureProvider";
            }
            catch
            {
                BackgroundImageId = string.Empty;
                BackgroundAdditionalArgs = string.Empty;
                BackgroundTextureProviderName = "CharacterImageTextureProvider";
            }
        }

        private void SetPlayerPortraitIdentifier()
        {
            try
            {
                CharacterCode code = Hero.MainHero?.CharacterObject == null ? null : CharacterCode.CreateFrom(Hero.MainHero.CharacterObject);
                CharacterImageIdentifierVM identifier = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
                _playerPortraitId = identifier?.Id ?? string.Empty;
                _playerPortraitAdditionalArgs = identifier?.AdditionalArgs ?? string.Empty;
                _playerPortraitTextureProviderName = identifier?.TextureProviderName ?? "CharacterImageTextureProvider";
            }
            catch
            {
                _playerPortraitId = string.Empty;
                _playerPortraitAdditionalArgs = string.Empty;
                _playerPortraitTextureProviderName = "CharacterImageTextureProvider";
            }
        }

        [DataSourceProperty]
        public string Title
        {
            get { return _title; }
            set
            {
                if (value != _title)
                {
                    _title = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string LocationText
        {
            get { return _locationText; }
            set
            {
                if (value != _locationText)
                {
                    _locationText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string SelectedSummary
        {
            get { return _selectedSummary; }
            set
            {
                if (value != _selectedSummary)
                {
                    _selectedSummary = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string InputText
        {
            get { return _inputText; }
            set
            {
                if (value != _inputText)
                {
                    _inputText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BusyText
        {
            get { return _busyText; }
            set
            {
                if (value != _busyText)
                {
                    _busyText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsBusy
        {
            get { return _isBusy; }
            set
            {
                if (value != _isBusy)
                {
                    _isBusy = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public int ChatScrollVersion
        {
            get { return _chatScrollVersion; }
            set
            {
                if (value != _chatScrollVersion)
                {
                    _chatScrollVersion = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPortraitPreviewVisible
        {
            get { return _isPortraitPreviewVisible; }
            set
            {
                if (value != _isPortraitPreviewVisible)
                {
                    _isPortraitPreviewVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewName
        {
            get { return _portraitPreviewName; }
            set
            {
                if (value != _portraitPreviewName)
                {
                    _portraitPreviewName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewId
        {
            get { return _portraitPreviewId; }
            set
            {
                if (value != _portraitPreviewId)
                {
                    _portraitPreviewId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewAdditionalArgs
        {
            get { return _portraitPreviewAdditionalArgs; }
            set
            {
                if (value != _portraitPreviewAdditionalArgs)
                {
                    _portraitPreviewAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewTextureProviderName
        {
            get { return _portraitPreviewTextureProviderName; }
            set
            {
                if (value != _portraitPreviewTextureProviderName)
                {
                    _portraitPreviewTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewImageWidth
        {
            get { return _portraitPreviewImageWidth; }
            set
            {
                if (Math.Abs(value - _portraitPreviewImageWidth) > 0.01f)
                {
                    _portraitPreviewImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewImageHeight
        {
            get { return _portraitPreviewImageHeight; }
            set
            {
                if (Math.Abs(value - _portraitPreviewImageHeight) > 0.01f)
                {
                    _portraitPreviewImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewFrameWidth
        {
            get { return _portraitPreviewFrameWidth; }
            set
            {
                if (Math.Abs(value - _portraitPreviewFrameWidth) > 0.01f)
                {
                    _portraitPreviewFrameWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewFrameHeight
        {
            get { return _portraitPreviewFrameHeight; }
            set
            {
                if (Math.Abs(value - _portraitPreviewFrameHeight) > 0.01f)
                {
                    _portraitPreviewFrameHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public async void ExecuteSend()
        {
            string text = (InputText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text) || IsBusy || !InputEnabled)
            {
                return;
            }

            List<Hero> activeHeroes = GetSelectedHeroes();
            string playerName = Hero.MainHero?.Name?.ToString() ?? "Player";
            _session.RegisterPlayerInput();
            AddChatLine(playerName, text, "player");
            _session.RecordTranscriptLine(playerName, text, Hero.MainHero?.StringId ?? string.Empty, "player");
            InputText = string.Empty;

            if (activeHeroes.Count == 0)
            {
                AddSystemLine("Select one or more characters before expecting a reply.", false);
                RefreshLabels();
                return;
            }

            RememberConversationHeroes(activeHeroes);
            Task<ReignPartyChatBatchResult> batch = RequestLiveNpcTurns(activeHeroes, text,
                suppressRelationshipAssessment: _targetedReviewMode);
            _activeBatchTask = batch;
            await batch;
            if (ReferenceEquals(_activeBatchTask, batch))
            {
                _activeBatchTask = null;
            }
            RefreshLabels();
            PersistCastleTranscript();
        }

        public async Task<ReignPartyChatBatchResult> SendAutomationLineAsync(
            string text,
            string auditRunId,
            int sceneIndex,
            int turnIndex,
            string correlationBase = "")
        {
            text = (text ?? string.Empty).Trim();
            ReignPartyChatBatchResult rejected = new ReignPartyChatBatchResult();
            if (!await WaitForAutomationReadyAsync().ConfigureAwait(false))
            {
                rejected.Error = "Timed out waiting for party chat to become ready.";
                return rejected;
            }

            List<Hero> activeHeroes = null;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized)
                {
                    rejected.Error = "Party chat closed.";
                    return false;
                }
                if (string.IsNullOrWhiteSpace(text))
                {
                    rejected.Error = "No text was supplied.";
                    return false;
                }
                if (IsBusy)
                {
                    rejected.Error = "Party chat is busy.";
                    return false;
                }

                activeHeroes = GetAutomationSelectedHeroes();
                if (activeHeroes.Count == 0)
                {
                    rejected.Error = "No party-chat participants are selected.";
                    return false;
                }

                string playerName = Hero.MainHero?.Name?.ToString() ?? "Player";
                _session.RegisterPlayerInput();
                AddChatLine(playerName, text, "player");
                _session.RecordTranscriptLine(playerName, text, Hero.MainHero?.StringId ?? string.Empty, "player");
                InputText = string.Empty;
                RememberConversationHeroes(activeHeroes);
                return true;
            }).ConfigureAwait(false);
            if (!accepted)
            {
                return rejected;
            }

            Task<ReignPartyChatBatchResult> batch = RequestLiveNpcTurns(
                activeHeroes,
                text,
                auditRunId,
                sceneIndex,
                turnIndex,
                false,
                correlationBase);
            _activeBatchTask = batch;
            ReignPartyChatBatchResult result = await batch.ConfigureAwait(false);
            if (ReferenceEquals(_activeBatchTask, batch))
            {
                _activeBatchTask = null;
            }
            await ReignMainThread.InvokeAsync(RefreshLabels).ConfigureAwait(false);
            return result;
        }

        private async Task<bool> WaitForAutomationReadyAsync()
        {
            Stopwatch timer = Stopwatch.StartNew();
            TimeSpan timeout = TimeSpan.FromMinutes(10);
            while (timer.Elapsed < timeout)
            {
                Task activeBatch = _activeBatchTask;
                bool busy = await ReignMainThread.InvokeAsync(() => IsBusy).ConfigureAwait(false);
                if (!busy && (activeBatch == null || activeBatch.IsCompleted))
                {
                    return true;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            return false;
        }

        public bool SelectAutomationHeroes(IEnumerable<Hero> heroes)
        {
            List<string> orderedIds = (heroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null && !string.IsNullOrWhiteSpace(hero.StringId))
                .Select(hero => hero.StringId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            HashSet<string> ids = new HashSet<string>(orderedIds, StringComparer.OrdinalIgnoreCase);
            foreach (ReignPartyChatMemberVM member in PartyMembers)
            {
                member.IsActive = member.Hero != null && ids.Contains(member.Hero.StringId);
            }
            _automationHeroOrder.Clear();
            _automationHeroOrder.AddRange(orderedIds);
            RefreshLabels();
            return ids.Count >= 2 && GetSelectedHeroes().Count == ids.Count;
        }

        public async Task<ReignConversationFinishResult> FinishAutomationSceneAsync(string reason)
        {
            _conversationFinishRequested = true;
            Task batch = _activeBatchTask;
            if (batch != null)
            {
                try { await batch.ConfigureAwait(false); }
                catch { }
            }
            string sessionId = await EnsureConversationSessionAsync(_conversationHeroes).ConfigureAwait(false);
            return await ReignServerClient.FinishPartyChatConversationSessionAsync(
                _session,
                _conversationHeroes,
                reason ?? "party_dialogue_audit_scene").ConfigureAwait(false);
        }

        public void AddAutomationSystemLine(string text)
        {
            AddSystemLine(text, false);
        }

        public void ExecuteSelectAll()
        {
            if (IsHomesMode) return;
            foreach (ReignPartyChatMemberVM member in PartyMembers)
            {
                member.IsActive = true;
            }

            RefreshLabels();
        }

        public void ExecuteClearSelection()
        {
            if (IsHomesMode)
            {
                if (IsBusy) return;
                ExecuteClose();
                ReignPartyChatScreenManager.OpenHomes(_homesSettlement);
                return;
            }
            if (_targetedReviewMode) return;
            foreach (ReignPartyChatMemberVM member in PartyMembers)
            {
                member.IsActive = false;
            }

            RefreshLabels();
        }

        public void ExecuteClose()
        {
            ReignLog.Info("Party chat close clicked.");
            RequestConversationFinish("closed_by_player");
            _closeScreen?.Invoke();
        }

        public void ExecuteClosePortraitPreview()
        {
            IsPortraitPreviewVisible = false;
            _portraitPreviewCacheKey = string.Empty;
            OnPropertyChanged(nameof(PortraitPreviewCacheKey));
        }

        public void ExecutePreviewPlayerPortrait()
        {
            PortraitPreviewName = Hero.MainHero?.Name?.ToString() ?? "Player";
            PortraitPreviewId = PlayerPortraitId;
            PortraitPreviewAdditionalArgs = PlayerPortraitAdditionalArgs;
            PortraitPreviewTextureProviderName = PlayerPortraitTextureProviderName;
            _portraitPreviewCacheKey = PlayerPortraitCacheKey;
            OnPropertyChanged(nameof(PortraitPreviewCacheKey));
            IsPortraitPreviewVisible = !string.IsNullOrWhiteSpace(PortraitPreviewId)
                || !string.IsNullOrWhiteSpace(_portraitPreviewCacheKey);
        }

        public void ExecuteNoOp()
        {
        }

        public void SetOverlayVisible(bool isVisible)
        {
            IsOverlayVisible = isVisible;
        }

        public override void OnFinalize()
        {
            PersistCastleTranscript();
            _isFinalized = true;
            RequestConversationFinish("screen_finalized");
            base.OnFinalize();
            ReignLog.Info("Party chat finalized.");
        }

        private async Task<ReignPartyChatBatchResult> RequestLiveNpcTurns(
            List<Hero> activeHeroes,
            string playerText,
            string auditRunId = "",
            int auditSceneIndex = -1,
            int auditTurnIndex = -1,
            bool requireStillSelected = true,
            string correlationBase = "",
            bool castleOpening = false,
            bool openingTurn = false,
            bool suppressRelationshipAssessment = false)
        {
            ReignPartyChatBatchResult batchResult = new ReignPartyChatBatchResult();
            if (castleOpening && _castleMode && ChatLines.Count == 0
                && !string.IsNullOrWhiteSpace(_session.SessionId))
            {
                try
                {
                    JObject response = await ReignServerClient.PostJsonAsync(
                        "/party-chat/history",
                        new JObject
                        {
                            ["campaignId"] = ReignServerClient.GetCampaignId(),
                            ["conversationSessionId"] = _session.SessionId.Trim(),
                            ["limit"] = 36
                        }).ConfigureAwait(false);
                    JArray rows = response.Value<bool?>("ok") == true
                        ? response["lines"] as JArray
                        : null;
                    if (rows != null)
                    {
                        _pendingRecoveredTranscript = new List<ReignPartyChatTranscriptLine>();
                        foreach (JObject row in rows.OfType<JObject>())
                        {
                            ReignPartyChatTranscriptLine line = row.ToObject<ReignPartyChatTranscriptLine>();
                            if (line != null && !string.IsNullOrWhiteSpace(line.Text))
                            {
                                _pendingRecoveredTranscript.Add(line);
                            }
                        }

                        if (_pendingRecoveredTranscript.Count > 0
                            && await ReignMainThread.InvokeAsync(ApplyPendingRecoveredTranscript).ConfigureAwait(false))
                        {
                            batchResult.SessionId = _session.SessionId;
                            batchResult.Ok = true;
                            return batchResult;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _pendingRecoveredTranscript = null;
                    ReignLog.Warn("Party chat transcript recovery failed: " + ex.Message);
                }
            }

            if (castleOpening)
            {
                _session.BeginOpeningTurn();
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                IsBusy = true;
                BusyText = castleOpening
                    ? "Preparing the room conversation..."
                    : "Waiting for Bannerlord Reign...";
            }).ConfigureAwait(false);
            Stopwatch timer = Stopwatch.StartNew();
            bool anyReply = false;
            bool hadFailure = false;
            ReignXpInteraction xp = await ReignServerClient.BeginXpInteractionAsync().ConfigureAwait(false);
            string xpReceipt = "conversation:party:" + (string.IsNullOrWhiteSpace(correlationBase)
                ? _session.CurrentSceneTurnId : correlationBase);

            try
            {
                string sessionId = await EnsureConversationSessionAsync(activeHeroes);
                if (string.IsNullOrWhiteSpace(sessionId))
                {
                    hadFailure = true;
                    batchResult.Error = "Bannerlord Reign could not start the party conversation session.";
                    await ReignMainThread.InvokeAsync(() => AddSystemLine(batchResult.Error, false)).ConfigureAwait(false);
                    return batchResult;
                }

                batchResult.SessionId = sessionId;
                batchResult.ExchangeId = _session.CurrentSceneTurnId;

                List<Hero> orderedSpeakers = BuildResponseOrder(activeHeroes, playerText);
                for (int speakerIndex = 0; speakerIndex < orderedSpeakers.Count; speakerIndex++)
                {
                    Hero speaker = orderedSpeakers[speakerIndex];
                    bool stillSelected = !requireStillSelected || await ReignMainThread.InvokeAsync(() => IsStillSelected(speaker)).ConfigureAwait(false);
                    if (speaker == null || !stillSelected)
                    {
                        continue;
                    }

                    if (castleOpening)
                    {
                        string progressText = CastleOpeningProgressText(
                            speaker.Name?.ToString(), speakerIndex + 1, orderedSpeakers.Count);
                        await ReignMainThread.InvokeAsync(() => BusyText = progressText).ConfigureAwait(false);
                    }

                    string effectiveCorrelationBase = string.IsNullOrWhiteSpace(
                            correlationBase)
                        ? auditRunId
                        : correlationBase;
                    string correlationId = string.IsNullOrWhiteSpace(
                            effectiveCorrelationBase)
                        ? string.Empty
                        : effectiveCorrelationBase + "-s" + (auditSceneIndex + 1) + "-t" + (auditTurnIndex + 1) + "-npc-" + speaker.StringId;
                    ReignPartyChatReply reply = await ReignServerClient.RequestPartyChatResponseAsync(
                        _session,
                        speaker,
                        orderedSpeakers,
                        playerText,
                        correlationId,
                        auditRunId,
                        auditSceneIndex,
                        auditTurnIndex,
                        _castleMode ? (_castleSession?.DialoguePromptSnapshot ?? string.Empty) : IsHomesMode ? _homeContext : _targetedReviewContext,
                        _castleRoomName,
                        castleOpening || openingTurn,
                        string.Equals(_castleSession?.InteractionMode, "family_chambers", StringComparison.OrdinalIgnoreCase),
                        _castleSession?.ChildHeroIdsCsv ?? string.Empty,
                        _castleSession?.ScenePlanJson ?? "{}",
                        IsHomesMode ? "at " + _homeResident.Name + "'s home in " + _homesSettlement.Name : "").ConfigureAwait(false);
                    batchResult.Replies.Add(reply);
                    if (!reply.Ok)
                    {
                        hadFailure = true;
                        string friendlyError = FriendlyServerError(reply.Error);
                        await ReignMainThread.InvokeAsync(() => AddSystemLine("Bannerlord Reign failed for " + speaker.Name + ": " + friendlyError, false)).ConfigureAwait(false);
                        if (IsBatchFatalError(reply.Error))
                        {
                            break;
                        }
                        continue;
                    }

                    string clean = SanitizeReplyText(reply.Text, speaker, orderedSpeakers);
                    if (string.IsNullOrWhiteSpace(clean) || IsSilence(clean))
                    {
                        ReignLog.Info("Party chat NPC stayed silent speaker=" + speaker.StringId);
                        continue;
                    }

                    string speakerName = speaker.Name?.ToString() ?? "NPC";
                    _session.RecordTranscriptLine(speakerName, clean, speaker.StringId, "npc");
                    await ReignMainThread.InvokeAsync(() => AddChatLine(speakerName, clean, "npc")).ConfigureAwait(false);
                    anyReply = true;

                    if (!string.IsNullOrWhiteSpace(reply.TimingSummary))
                    {
                        ReignLog.Info("Party chat timing hero=" + speaker.StringId + " " + reply.TimingSummary + " clientMs=" + reply.ClientTotalMs);
                    }
                }

                if (!hadFailure && batchResult.Replies.Count == activeHeroes.Count && batchResult.Replies.All(x => x.Ok)
                    && !suppressRelationshipAssessment
                    && !string.Equals(_castleSession?.InteractionMode, "family_chambers", StringComparison.OrdinalIgnoreCase))
                {
                    if (castleOpening)
                    {
                        await ReignMainThread.InvokeAsync(() => BusyText = "Reviewing the opening conversation...").ConfigureAwait(false);
                    }
                    JObject relationshipResult = await ReignServerClient.ApplyConversationRelationshipAssessmentsAsync(
                        batchResult.ExchangeId,
                        "party_chat",
                        activeHeroes,
                        batchResult.Replies.Select(x => x.RawResponse),
                        batchResult.Replies.Select(x => x.CorrelationId).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty).ConfigureAwait(false);
                    if (relationshipResult.Value<bool?>("ok") != true)
                    {
                        hadFailure = true;
                        batchResult.Error = "Conversation relationship evaluation failed: " + (relationshipResult.Value<string>("error") ?? "unknown error");
                        await ReignMainThread.InvokeAsync(() => AddSystemLine(batchResult.Error, false)).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                timer.Stop();
                await ReignMainThread.InvokeAsync(() =>
                {
                    IsBusy = false;
                    BusyText = string.Empty;
                }).ConfigureAwait(false);
                ReignLog.Info("Party chat batch clientMs=" + timer.ElapsedMilliseconds + " activeHeroes=" + activeHeroes.Count);
            }

            if (!anyReply && !hadFailure)
            {
                await ReignMainThread.InvokeAsync(() => AddSystemLine("No one answers just yet.", false)).ConfigureAwait(false);
            }
            await ReignMainThread.InvokeAsync(PersistCastleTranscript).ConfigureAwait(false);
            batchResult.Ok = !hadFailure && batchResult.Replies.Count == activeHeroes.Count && batchResult.Replies.All(reply => reply.Ok);
            if (batchResult.Ok && anyReply && !castleOpening && !openingTurn
                && !string.IsNullOrWhiteSpace(playerText) && string.IsNullOrWhiteSpace(auditRunId))
                await ReignMainThread.InvokeAsync(() => xp.Award(xpReceipt, ReignXpSkill.Charm, ReignXpRules.ConversationXp)).ConfigureAwait(false);
            if (!batchResult.Ok && string.IsNullOrWhiteSpace(batchResult.Error))
            {
                batchResult.Error = string.Join("; ", batchResult.Replies.Where(reply => !reply.Ok)
                    .Select(reply => (reply.SpeakerName ?? reply.SpeakerHeroStringId) + ": " + reply.Error));
            }
            return batchResult;
        }

        private async Task TrackTargetedOpeningAsync(Task<ReignPartyChatBatchResult> opening)
        {
            ReignPartyChatBatchResult result = null;
            try { result = await opening.ConfigureAwait(false); }
            catch { }
            TargetedReviewProviderFailed = result?.Ok != true;
            if (ReferenceEquals(_activeBatchTask, opening)) _activeBatchTask = null;
        }

        private void RefreshCastleMembers()
        {
            PartyMembers.Clear();
            foreach (string id in ReignCourtCampaignBehavior.SplitIds(_castleSession?.OccupantHeroIdsCsv))
            {
                Hero hero = Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
                if (hero != null) PartyMembers.Add(new ReignPartyChatMemberVM(hero, true, _ => { }, PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait));
            }
        }

        private void RestoreCastleTranscript()
        {
            if (_castleSession == null) return;
            try
            {
                List<ReignPartyChatTranscriptLine> lines = JsonConvert.DeserializeObject<List<ReignPartyChatTranscriptLine>>(
                    _castleSession.ReadTranscriptJson())
                    ?? new List<ReignPartyChatTranscriptLine>();
                _session.RestoreTranscript(lines);
                foreach (ReignPartyChatTranscriptLine line in lines) AddChatLine(line.Speaker, line.Text, line.Role);
            }
            catch (Exception ex) { ReignLog.Warn("Castle Chat transcript restore failed: " + ex.Message); }
        }

        private void PersistCastleTranscript()
        {
            if (!_castleMode || _castleSession == null) return;
            _castleSession.WriteTranscriptJson(
                JsonConvert.SerializeObject(_session.RecentTranscriptEntries, Formatting.None));
            _castleSession.OpeningStatus = _session.RecentTranscriptEntries.Any(x => x.Role == "npc") ? "complete" : _castleSession.OpeningStatus;
            _castleSession.Revision++;
        }

        private void RememberConversationHeroes(IEnumerable<Hero> heroes)
        {
            foreach (Hero hero in heroes ?? Enumerable.Empty<Hero>())
            {
                if (hero != null && !_conversationHeroes.Any(existing => existing?.StringId == hero.StringId))
                {
                    _conversationHeroes.Add(hero);
                }
            }
        }

        internal JObject HomesSnapshot() => new JObject {
            ["isHomesMode"] = IsHomesMode, ["isDirectory"] = IsHomeDirectory,
            ["cardHeroIds"] = new JArray(PartyMembers.Select(member => member.Hero.StringId)),
            ["selectedHeroIds"] = new JArray(GetSelectedHeroes().Select(hero => hero.StringId)),
            ["sessionHeroIds"] = new JArray(_conversationHeroes.Select(hero => hero.StringId)),
            ["sessionId"] = _conversationSessionId ?? ""
        };

        private async Task<string> EnsureConversationSessionAsync(IEnumerable<Hero> activeHeroes)
        {
            if (!string.IsNullOrWhiteSpace(_conversationSessionId))
            {
                return _conversationSessionId;
            }

            List<Hero> heroes = (activeHeroes ?? Enumerable.Empty<Hero>())
                .Where(hero => hero != null)
                .Distinct()
                .ToList();
            RememberConversationHeroes(heroes);
            Task<string> pending = _conversationSessionTask
                ?? ReignServerClient.StartPartyChatConversationSessionAsync(_session, heroes);
            _conversationSessionTask = pending;
            _conversationSessionId = await pending ?? string.Empty;
            return _conversationSessionId;
        }

        private void RequestConversationFinish(string reason)
        {
            if (_conversationFinishRequested
                || (string.IsNullOrWhiteSpace(_conversationSessionId) && _conversationSessionTask == null))
            {
                return;
            }

            _conversationFinishRequested = true;
            _ = FinishConversationAsync(reason);
        }

        private async Task FinishConversationAsync(string reason)
        {
            Task batch = _activeBatchTask;
            if (batch != null)
            {
                try
                {
                    await batch;
                }
                catch
                {
                    // The server finish remains useful after a failed response batch.
                }
            }

            string sessionId = await EnsureConversationSessionAsync(_conversationHeroes);
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            await ReignServerClient.FinishPartyChatConversationSessionAsync(
                _session,
                _conversationHeroes,
                reason ?? (_isFinalized ? "screen_finalized" : "closed_by_player"));
        }

        private static string FriendlyServerError(string error)
        {
            string text = (error ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "No response was generated.";
            }

            if (ContainsIgnoreCase(text, "context_length_exceeded") || ContainsIgnoreCase(text, "prompt exceeds") || ContainsIgnoreCase(text, "too much context"))
            {
                return "The AI request contained too much context. The full diagnostic was saved to the server audit log.";
            }

            if (ContainsIgnoreCase(text, "missing_api_key") || ContainsIgnoreCase(text, "Invalid Authentication")
                || ContainsIgnoreCase(text, "invalid_api_key") || ContainsIgnoreCase(text, "unauthorized")
                || ContainsIgnoreCase(text, "api key"))
            {
                return "API key missing or rejected. Open Server Diagnostics, save the key, then run Test LLM Key.";
            }

            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }
            return text.Length <= 220 ? text : text.Substring(0, 220).TrimEnd() + "...";
        }

        private static bool IsBatchFatalError(string error)
        {
            return ContainsIgnoreCase(error, "context_length_exceeded")
                || ContainsIgnoreCase(error, "prompt exceeds")
                || ContainsIgnoreCase(error, "too much context")
                || ContainsIgnoreCase(error, "missing_api_key")
                || ContainsIgnoreCase(error, "Invalid Authentication")
                || ContainsIgnoreCase(error, "invalid_api_key")
                || ContainsIgnoreCase(error, "unauthorized");
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ToggleMember(ReignPartyChatMemberVM attendee)
        {
            if (IsHomesMode)
            {
                if (IsBusy || attendee?.Hero == null) return;
                if (!IsResidentAvailable(attendee.Hero))
                { AddSystemLine("That resident is no longer here.", false); RefreshPartyMembers(); return; }
                ExecuteClose();
                ReignPartyChatScreenManager.OpenHomes(_homesSettlement, attendee.Hero);
                return;
            }
            if (attendee == null)
            {
                return;
            }

            attendee.IsActive = !attendee.IsActive;
            ReignLog.Info("Party chat toggle hero=" + attendee.Hero?.StringId + " active=" + attendee.IsActive);
            RefreshLabels();
        }

        private void PreviewPortrait(ReignPartyChatMemberVM attendee)
        {
            if (attendee == null || string.IsNullOrWhiteSpace(attendee.PortraitId) || string.IsNullOrWhiteSpace(attendee.PortraitTextureProviderName))
            {
                return;
            }

            PortraitPreviewName = attendee.Name;
            PortraitPreviewId = attendee.PortraitId;
            PortraitPreviewAdditionalArgs = attendee.PortraitAdditionalArgs ?? string.Empty;
            PortraitPreviewTextureProviderName = attendee.PortraitTextureProviderName;
            _portraitPreviewCacheKey = attendee.PortraitCacheKey;
            OnPropertyChanged(nameof(PortraitPreviewCacheKey));
            IsPortraitPreviewVisible = true;
        }

        private void OpenEncyclopedia(ReignPartyChatMemberVM attendee)
        {
            if (attendee?.Hero == null)
            {
                return;
            }

            if (_openEncyclopediaOverlay != null)
            {
                _openEncyclopediaOverlay(attendee.Hero);
                return;
            }

            ReignPortraitBridge.OpenHeroEncyclopedia(attendee.Hero);
        }

        private void GenerateAIPortrait(ReignPartyChatMemberVM attendee)
        {
            if (attendee?.Hero == null)
            {
                return;
            }

            if (_requestPortraitOverlay != null)
            {
                _requestPortraitOverlay(attendee.Hero);
                return;
            }

            ReignPortraitBridge.RequestPortrait(attendee.Hero);
        }

        private void RefreshPartyMembers()
        {
            if (IsHomesMode)
            {
                PartyMembers.Clear();
                foreach (Hero hero in ReignEncounteredResidentsCampaignBehavior.Instance.ResidentsAt(_homesSettlement))
                {
                    string role = ReignEncounteredResidentsCampaignBehavior.Instance.Find(hero)?.Role ?? "Resident";
                    role = Regex.Replace(role, "(?<=[a-z])([A-Z])", " $1");
                    PartyMembers.Add(new ReignPartyChatMemberVM(hero, hero == _homeResident, ToggleMember,
                        PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait, VisitResidentInPerson, role));
                }
                return;
            }
            if (_targetedReviewMode) return;
            if (_castleMode) { RefreshCastleMembers(); return; }
            HashSet<string> selected = new HashSet<string>(PartyMembers.Where(x => x.IsActive).Select(x => x.Hero?.StringId));
            PartyMembers.Clear();
            foreach (Hero hero in ReignPartyChatSession.GetAvailableConversationHeroes())
            {
                PartyMembers.Add(new ReignPartyChatMemberVM(hero, selected.Contains(hero.StringId), ToggleMember, PreviewPortrait, OpenEncyclopedia, GenerateAIPortrait));
            }
        }

        private void RefreshLabels()
        {
            if (IsHomesMode)
            {
                LocationText = _homeResident == null ? "People you have met here" : "At home with " + _homeResident.Name;
                SelectedSummary = _homeResident == null ? PartyMembers.Count + " known residents at home"
                    : "Private conversation";
                return;
            }
            if (_castleMode)
            {
                LocationText = _castleRoomName;
                SelectedSummary = _castleRoomName;
                return;
            }
            LocationText = _session.BuildLocationText();
            List<Hero> selected = GetSelectedHeroes();
            SelectedSummary = selected.Count == 0
                ? "No characters selected"
                : "Selected: " + string.Join(", ", selected.Select(x => x.Name?.ToString()).Take(5)) + (selected.Count > 5 ? " +" + (selected.Count - 5) : string.Empty);
        }

        private List<Hero> GetSelectedHeroes()
        {
            if (IsHomesMode) return IsResidentAvailable(_homeResident)
                ? new List<Hero> { _homeResident } : new List<Hero>();
            return PartyMembers
                .Where(x => x.IsActive)
                .Select(x => x.Hero)
                .Where(x => x != null)
                .Distinct()
                .ToList();
        }

        private List<Hero> GetAutomationSelectedHeroes()
        {
            List<Hero> selected = GetSelectedHeroes();
            Dictionary<string, Hero> byId = selected
                .Where(hero => !string.IsNullOrWhiteSpace(hero.StringId))
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            List<Hero> ordered = _automationHeroOrder
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .ToList();
            ordered.AddRange(selected.Where(hero => !ordered.Any(existing =>
                string.Equals(existing.StringId, hero.StringId, StringComparison.OrdinalIgnoreCase))));
            return ordered;
        }

        private bool IsStillSelected(Hero hero)
        {
            if (IsHomesMode) return hero == _homeResident && IsResidentAvailable(hero);
            return hero != null && PartyMembers.Any(x => x.IsActive && x.Hero?.StringId == hero.StringId);
        }

        private bool IsResidentAvailable(Hero hero) => hero != null && Settlement.CurrentSettlement == _homesSettlement
            && ReignEncounteredResidentsCampaignBehavior.Instance?.ResidentsAt(_homesSettlement).Contains(hero) == true;

        private void VisitResidentInPerson(ReignPartyChatMemberVM member)
        {
            if (IsBusy || !IsResidentAvailable(member?.Hero)) return;
            Hero hero = member.Hero;
            ExecuteClose();
            ReignEncounteredResidentsCampaignBehavior.Instance.VisitInPerson(hero);
        }

        private string SanitizeReplyText(string text, Hero speaker, IEnumerable<Hero> activeHeroes)
        {
            string clean = (text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clean))
            {
                return string.Empty;
            }

            List<Hero> knownHeroes = new List<Hero>();
            AddKnownHero(knownHeroes, Hero.MainHero);
            AddKnownHero(knownHeroes, speaker);
            foreach (Hero hero in activeHeroes ?? Enumerable.Empty<Hero>())
            {
                AddKnownHero(knownHeroes, hero);
            }

            foreach (ReignPartyChatMemberVM member in PartyMembers)
            {
                AddKnownHero(knownHeroes, member.Hero);
            }

            foreach (Hero hero in knownHeroes)
            {
                string displayName = GetHeroDisplayName(hero, null);
                clean = ReplaceTechnicalId(clean, hero.StringId, displayName);
                clean = ReplaceTechnicalId(clean, hero.CharacterObject?.StringId, displayName);
            }

            string playerName = GetHeroDisplayName(Hero.MainHero, "the player character");
            clean = Regex.Replace(clean, @"\bthe player character\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bthe player\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bPlayer\b", playerName);
            clean = Regex.Replace(clean, @"\bmain_hero\b", playerName, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\baddress:[A-Za-z0-9_]+\b", string.Empty, RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\bCharacterObject_\d+\b", "someone", RegexOptions.IgnoreCase);
            clean = Regex.Replace(clean, @"\s{2,}", " ").Trim();
            return clean;
        }

        private static bool IsSilence(string text)
        {
            string clean = (text ?? string.Empty).Trim().Trim('*').Trim();
            return string.Equals(clean, "silence", StringComparison.OrdinalIgnoreCase)
                || string.Equals(clean, "silent", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddKnownHero(List<Hero> heroes, Hero hero)
        {
            if (hero == null || heroes.Any(x => x == hero || x.StringId == hero.StringId))
            {
                return;
            }

            heroes.Add(hero);
        }

        private static string ReplaceTechnicalId(string text, string technicalId, string displayName)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(technicalId) || string.IsNullOrWhiteSpace(displayName))
            {
                return text ?? string.Empty;
            }

            string escaped = Regex.Escape(technicalId);
            string clean = Regex.Replace(text, @"\baddress:" + escaped + @"\b", displayName, RegexOptions.IgnoreCase);
            return Regex.Replace(clean, @"\b" + escaped + @"\b", displayName, RegexOptions.IgnoreCase);
        }

        private static string GetHeroDisplayName(Hero hero, string fallback)
        {
            string name = hero?.Name?.ToString();
            return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
        }

        internal static string CastleOpeningProgressText(string speakerName, int current, int total)
        {
            int safeTotal = Math.Max(1, total);
            int safeCurrent = Math.Min(Math.Max(1, current), safeTotal);
            string displayName = string.IsNullOrWhiteSpace(speakerName) ? "the next guest" : speakerName.Trim();
            return "Waiting for " + displayName + " to speak... (" + safeCurrent + " of " + safeTotal + ")";
        }

        private void AddSystemLine(string text, bool recordTranscript)
        {
            AddChatLine("Party", text, "system");
            if (recordTranscript)
            {
                _session.RecordTranscriptLine("Party", text);
            }
        }

        private void AddChatLine(string speaker, string text, string role)
        {
            ChatLines.Add(new ReignChatLineVM(speaker, text, role));
            ChatScrollVersion++;
        }

        private List<Hero> BuildResponseOrder(IEnumerable<Hero> activeHeroes, string playerText)
        {
            List<Hero> speakers = new List<Hero>();
            HashSet<string> seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeHeroes != null)
            {
                foreach (Hero hero in activeHeroes)
                {
                    if (hero != null
                        && !string.IsNullOrWhiteSpace(hero.StringId)
                        && seenIds.Add(hero.StringId))
                    {
                        speakers.Add(hero);
                    }
                }
            }

            Hero directlyAddressed = ResolveDirectlyAddressedSpeaker(speakers, playerText);
            string seed = (_session?.SessionId ?? string.Empty) + "|"
                + (_session?.CurrentSceneTurnId ?? string.Empty) + "|"
                + (_session?.PlayerInputCount.ToString() ?? "0") + "|"
                + (playerText ?? string.Empty);
            List<Hero> ordered = new List<Hero>();
            if (directlyAddressed != null) ordered.Add(directlyAddressed);
            foreach (Hero hero in speakers)
            {
                if (directlyAddressed != null
                    && hero.StringId.Equals(directlyAddressed.StringId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ulong heroKey = StableResponseOrderKey(seed, hero.StringId);
                int insertionIndex = ordered.Count;
                int firstRandomIndex = directlyAddressed == null ? 0 : 1;
                for (int index = firstRandomIndex; index < ordered.Count; index++)
                {
                    Hero existing = ordered[index];
                    ulong existingKey = StableResponseOrderKey(seed, existing.StringId);
                    if (heroKey < existingKey
                        || (heroKey == existingKey
                            && string.Compare(hero.StringId, existing.StringId, StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        insertionIndex = index;
                        break;
                    }
                }

                ordered.Insert(insertionIndex, hero);
            }

            return ordered;
        }

        private static Hero ResolveDirectlyAddressedSpeaker(List<Hero> speakers, string playerText)
        {
            if (speakers == null || speakers.Count == 0 || string.IsNullOrWhiteSpace(playerText)) return null;
            Dictionary<string, int> firstNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Hero hero in speakers)
            {
                string first = FirstName(hero.Name?.ToString());
                if (first.Length < 3) continue;
                firstNameCounts[first] = firstNameCounts.TryGetValue(first, out int count) ? count + 1 : 1;
            }

            Hero bestHero = null;
            int bestIndex = int.MaxValue;
            foreach (Hero hero in speakers)
            {
                string full = hero.Name?.ToString()?.Trim() ?? string.Empty;
                string first = FirstName(full);
                int fullIndex = WholePhraseIndex(playerText, full);
                int firstIndex = first.Length >= 3
                    && firstNameCounts.TryGetValue(first, out int count)
                    && count == 1
                        ? WholePhraseIndex(playerText, first)
                        : -1;
                int index = fullIndex < 0 ? firstIndex : firstIndex < 0 ? fullIndex : Math.Min(fullIndex, firstIndex);
                if (index < 0
                    || index > bestIndex
                    || (index == bestIndex
                        && bestHero != null
                        && string.Compare(hero.StringId, bestHero.StringId, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    continue;
                }

                bestHero = hero;
                bestIndex = index;
            }

            return bestHero;
        }

        private static int WholePhraseIndex(string text, string phrase)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase)) return -1;
            Match match = Regex.Match(text,
                @"(?<![\p{L}\p{N}])" + Regex.Escape(phrase.Trim()) + @"(?![\p{L}\p{N}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return match.Success ? match.Index : -1;
        }

        private static string FirstName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? string.Empty
                : name.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }

        private static ulong StableResponseOrderKey(string seed, string heroId)
        {
            string value = ((seed ?? string.Empty) + "|" + (heroId ?? string.Empty)).ToLowerInvariant();
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }

        private List<ReignPartyChatTranscriptLine> _pendingRecoveredTranscript;

        private bool ApplyPendingRecoveredTranscript()
        {
            List<ReignPartyChatTranscriptLine> recovered = _pendingRecoveredTranscript;
            _pendingRecoveredTranscript = null;
            if (ChatLines.Count > 0 || recovered == null || recovered.Count == 0)
            {
                return ChatLines.Count > 0;
            }

            _session.RestoreTranscript(recovered);
            foreach (ReignPartyChatTranscriptLine line in recovered)
            {
                AddChatLine(line.Speaker, line.Text, line.Role);
            }
            PersistCastleTranscript();
            ReignLog.Info("Castle Chat transcript recovered from server session="
                + _session.SessionId + " lines=" + recovered.Count);
            return true;
        }
    }
}
