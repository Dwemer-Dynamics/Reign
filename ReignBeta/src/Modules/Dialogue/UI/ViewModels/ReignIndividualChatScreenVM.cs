using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using Reign.Core.Contracts.Dialogue;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed partial class ReignIndividualChatScreenVM : ViewModel, ReignBeta.UI.IReignActionHost
    {
        public IReadOnlyList<Hero> ActionParticipants => new[] { _hero };
        public bool ActionBusy => _presentationInitializing || IsBusy || IsPregnancyWarningVisible || IsOutfitVisible || _calibrationMode;
        private ReignActionComposerVM _composer;
        public ReignActionComposerVM Composer => _composer ?? (_composer = new ReignActionComposerVM(() => { _recoveredDraftReview.InputChanged(_composer?.State.Text); OnPropertyChanged(nameof(InputText)); OnPropertyChanged(nameof(SendButtonText)); }));
        [DataSourceProperty] public string ActionComposerId => Composer.Id;
        public Reign.Core.Contracts.Actions.ActionAgreement ActionDraft => Composer.State.HasValidAttachment ? Composer.State.Draft : null;
        private readonly Hero _hero;
        internal Hero ConversationHero => _hero;
        private readonly Action _closeScreen;
        private readonly Action<Hero> _lookAtHero;
        private string _title;
        private string _heroSubtitle;
        private string _locationText;
        private string _playerName;
        private string _playerSubtitle;
        private string _playerInfo;
        private string _npcName;
        private string _npcSubtitle;
        private string _npcInfo;
        private ImageIdentifierVM _playerPortrait;
        private ImageIdentifierVM _npcPortrait;
        private string _playerPortraitCacheKey;
        private string _npcPortraitCacheKey;
        private string _zoomPortraitCacheKey;
        private string _playerPortraitId;
        private string _playerPortraitAdditionalArgs;
        private string _playerPortraitTextureProviderName;
        private string _npcPortraitId;
        private string _npcPortraitAdditionalArgs;
        private string _npcPortraitTextureProviderName;
        private string _zoomPortraitId;
        private string _zoomPortraitAdditionalArgs;
        private string _zoomPortraitTextureProviderName;
        private string _busyText;
        private bool _isBusy;
        private bool _isPortraitZoomVisible;
        private bool _isPregnancyWarningVisible;
        private bool _pregnancyDecisionBusy;
        private bool _calibrationPregnancyFixtureActive;
        private ReignConceptionAttemptResult _pendingConceptionAttempt;
        private string _pendingConceptionActionText;
        private TaskCompletionSource<PregnancyDecisionReceipt> _pregnancyDecisionCompletion;
        private PregnancyDecisionReceipt _lastPregnancyDecisionReceipt;
        private bool _isFinalized;
        private bool _presentationInitializing;
        private Task<bool> _presentationInitializationTask = Task.FromResult(true);
        internal Task<bool> PresentationInitialization => _presentationInitializationTask;
        private Task<ConversationSessionStart> _conversationSessionTask;
        private readonly ConversationSessionStartGate _conversationStartGate;
        private Task<ReignConversationFinishResult> _conversationFinishTask;
        private TaskCompletionSource<bool> _conversationTurnCompletion;
        private string _finalizationReason = "screen_finalized";
        internal string ConversationGeneration { get; } = Guid.NewGuid().ToString("N");
        private string _conversationSessionId;
        private bool _conversationFinishRequested;
        private readonly bool _initializePresentation;
        private readonly JObject _conversationContext;
        private readonly bool _isOfficialAmbassadorMode;
        private readonly bool _calibrationMode;
        private int _chatScrollVersion;
        private const float HeaderPortraitApertureWidth = 210f;
        private const float HeaderPortraitApertureHeight = 310f;
        private const float HeaderPortraitCoverOverscan = 1.04f;
        private float _playerPortraitCropImageWidth = 241.8f;
        private float _playerPortraitCropImageHeight = 322.4f;
        private float _npcPortraitCropImageWidth = 241.8f;
        private float _npcPortraitCropImageHeight = 322.4f;
        private float _zoomPortraitImageWidth = 540f;
        private float _zoomPortraitImageHeight = 720f;
        private float _zoomFrameWidth = 576f;
        private float _zoomFrameHeight = 756f;

        public ReignIndividualChatScreenVM(
            Hero hero,
            Action closeScreen,
            Action<Hero> lookAtHero,
            bool initializePresentation = true,
            JObject conversationContext = null,
            bool calibrationMode = false)
        {
            _hero = hero;
            _closeScreen = closeScreen;
            _lookAtHero = lookAtHero;
            _initializePresentation = initializePresentation;
            _conversationContext = conversationContext == null ? new JObject() : (JObject)conversationContext.DeepClone();
            // Stable request identity survives a transport retry. A caller carrying
            // an explicit scene continuation also carries its logical/parent IDs.
            if (string.IsNullOrWhiteSpace(_conversationContext.Value<string>("sessionId")))
                _conversationContext["sessionId"] = "conversation_" + Guid.NewGuid().ToString("N");
            _conversationContext["campaignId"] = ReignServerClient.GetCampaignId();
            _conversationContext["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            _conversationContext["playerId"] = Hero.MainHero?.StringId ?? string.Empty;
            _conversationStartGate = new ConversationSessionStartGate(_conversationContext.Value<string>("sessionId"));
            _isOfficialAmbassadorMode = string.Equals(_conversationContext.Value<string>("conversationMode"), "ambassador_official", StringComparison.OrdinalIgnoreCase);
            _calibrationMode = calibrationMode;
            if (!_calibrationMode) ReignHeroKnowledge.MarkMet(hero);
            ChatLines = new MBBindingList<ReignChatLineVM>();
            Title = _isOfficialAmbassadorMode
                ? "AMBASSADOR MODE - " + (hero?.Name?.ToString() ?? "Envoy")
                : hero?.Name?.ToString() ?? "Conversation";
            HeroSubtitle = BuildHeroSubtitle(hero);
            LocationText = BuildLocationText(hero);
            PlayerName = Hero.MainHero?.Name?.ToString() ?? "Player";
            PlayerSubtitle = BuildHeroSubtitle(Hero.MainHero);
            PlayerInfo = BuildLocationText(Hero.MainHero);
            NpcName = Title;
            NpcSubtitle = HeroSubtitle;
            NpcInfo = LocationText;
            if (_initializePresentation)
            {
                _playerPortraitCacheKey = CharacterCacheId.ForHero(Hero.MainHero);
                _npcPortraitCacheKey = CharacterCacheId.ForHero(hero);
                PortraitPatch.SetChatPlayerIdentity(_playerPortraitCacheKey);
                PortraitPatch.SetChatNpcIdentity(_npcPortraitCacheKey);
                SetPlayerCropAspect(GetAspectRatioFor(_playerPortraitCacheKey, PortraitQualityTier.Portrait));
                SetNpcCropAspect(GetAspectRatioFor(_npcPortraitCacheKey, PortraitQualityTier.Portrait));
                PlayerPortrait = BuildPortrait(Hero.MainHero, _playerPortraitCacheKey);
                NpcPortrait = BuildPortrait(hero, _npcPortraitCacheKey);
            }
            BusyText = string.Empty;
            if (_calibrationMode)
            {
                AddSystemLine("Provider-free UI calibration conversation.");
                AddChatLine(hero?.Name?.ToString() ?? "Councillor",
                    "I am ready to review the matter when you are.", "npc");
            }
            else
            {
                _conversationSessionTask = _conversationStartGate.EnsureAsync(
                    StartConversationAsync, false);
                if (_initializePresentation)
                {
                    AddSystemLine("Loading previous conversation...");
                    _presentationInitializing = true;
                    _presentationInitializationTask = InitializePresentationAsync();
                }
            }
        }

        public MBBindingList<ReignChatLineVM> ChatLines { get; }

        public bool IsCalibrationMode => _calibrationMode;

        internal sealed class PregnancyDecisionReceipt
        {
            public string Decision { get; set; } = string.Empty;
            public bool Ok { get; set; }
            public bool ProviderFreeFixture { get; set; }
            public bool WarningVisibleAfter { get; set; }
            public bool InputEnabledAfter { get; set; }
            public bool BusyAfter { get; set; }
            public string Error { get; set; } = string.Empty;

            public JObject ToJson()
            {
                return new JObject
                {
                    ["decision"] = Decision ?? string.Empty,
                    ["ok"] = Ok,
                    ["providerFreeFixture"] = ProviderFreeFixture,
                    ["warningVisibleAfter"] = WarningVisibleAfter,
                    ["inputEnabledAfter"] = InputEnabledAfter,
                    ["busyAfter"] = BusyAfter,
                    ["error"] = Error ?? string.Empty
                };
            }
        }

        internal bool PregnancyDecisionBusy => _pregnancyDecisionBusy;
        internal bool CalibrationPregnancyFixtureActive => _calibrationPregnancyFixtureActive;

        internal JObject PregnancyAutomationStateJson()
        {
            return new JObject
            {
                ["warningVisible"] = IsPregnancyWarningVisible,
                ["inputEnabled"] = InputEnabled,
                ["decisionBusy"] = _pregnancyDecisionBusy,
                ["providerFreeFixture"] = _calibrationPregnancyFixtureActive,
                ["lastDecision"] = _lastPregnancyDecisionReceipt?.ToJson() ?? new JObject()
            };
        }

        internal bool TryShowPregnancyWarningFixture(out string error)
        {
            error = string.Empty;
            if (!_calibrationMode)
            {
                error = "The pregnancy-warning fixture is restricted to provider-free UI calibration.";
                return false;
            }
            if (_isFinalized)
            {
                error = "The Individual Chat calibration view model is finalized.";
                return false;
            }
            if (IsPregnancyWarningVisible || _pregnancyDecisionBusy)
            {
                error = "A pregnancy-warning decision is already pending.";
                return false;
            }

            _calibrationPregnancyFixtureActive = true;
            _pendingConceptionAttempt = new ReignConceptionAttemptResult
            {
                Ok = true,
                Verified = true,
                Eligible = true,
                AttemptId = "provider-free-ui-calibration-pregnancy-warning"
            };
            _pendingConceptionActionText = "Provider-free pregnancy warning calibration fixture.";
            PreparePregnancyDecisionCompletion();
            IsPregnancyWarningVisible = true;
            IsBusy = true;
            BusyText = "Choose whether to proceed.";
            return true;
        }

        internal Task<PregnancyDecisionReceipt> ObservePregnancyDecisionCompletionAsync()
        {
            if (!IsPregnancyWarningVisible)
            {
                return Task.FromResult(new PregnancyDecisionReceipt
                {
                    Ok = false,
                    Error = "No pregnancy-warning decision is pending.",
                    WarningVisibleAfter = false,
                    InputEnabledAfter = InputEnabled,
                    BusyAfter = IsBusy
                });
            }
            if (_pregnancyDecisionCompletion == null || _pregnancyDecisionCompletion.Task.IsCompleted)
            {
                PreparePregnancyDecisionCompletion();
            }
            return _pregnancyDecisionCompletion.Task;
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
        public string HeroSubtitle
        {
            get { return _heroSubtitle; }
            set
            {
                if (value != _heroSubtitle)
                {
                    _heroSubtitle = value;
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
        public string PlayerName
        {
            get { return _playerName; }
            set
            {
                if (value != _playerName)
                {
                    _playerName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerSubtitle
        {
            get { return _playerSubtitle; }
            set
            {
                if (value != _playerSubtitle)
                {
                    _playerSubtitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerInfo
        {
            get { return _playerInfo; }
            set
            {
                if (value != _playerInfo)
                {
                    _playerInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcName
        {
            get { return _npcName; }
            set
            {
                if (value != _npcName)
                {
                    _npcName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcSubtitle
        {
            get { return _npcSubtitle; }
            set
            {
                if (value != _npcSubtitle)
                {
                    _npcSubtitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcInfo
        {
            get { return _npcInfo; }
            set
            {
                if (value != _npcInfo)
                {
                    _npcInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ImageIdentifierVM PlayerPortrait
        {
            get { return _playerPortrait; }
            set
            {
                if (value != _playerPortrait)
                {
                    _playerPortrait = value;
                    PlayerPortraitId = value?.Id ?? string.Empty;
                    PlayerPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
                    PlayerPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ImageIdentifierVM NpcPortrait
        {
            get { return _npcPortrait; }
            set
            {
                if (value != _npcPortrait)
                {
                    _npcPortrait = value;
                    NpcPortraitId = value?.Id ?? string.Empty;
                    NpcPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
                    NpcPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitId
        {
            get { return _playerPortraitId; }
            set
            {
                if (value != _playerPortraitId)
                {
                    _playerPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitAdditionalArgs
        {
            get { return _playerPortraitAdditionalArgs; }
            set
            {
                if (value != _playerPortraitAdditionalArgs)
                {
                    _playerPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitTextureProviderName
        {
            get { return _playerPortraitTextureProviderName; }
            set
            {
                if (value != _playerPortraitTextureProviderName)
                {
                    _playerPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitId
        {
            get { return _npcPortraitId; }
            set
            {
                if (value != _npcPortraitId)
                {
                    _npcPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitAdditionalArgs
        {
            get { return _npcPortraitAdditionalArgs; }
            set
            {
                if (value != _npcPortraitAdditionalArgs)
                {
                    _npcPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitTextureProviderName
        {
            get { return _npcPortraitTextureProviderName; }
            set
            {
                if (value != _npcPortraitTextureProviderName)
                {
                    _npcPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitId
        {
            get { return _zoomPortraitId; }
            set
            {
                if (value != _zoomPortraitId)
                {
                    _zoomPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitAdditionalArgs
        {
            get { return _zoomPortraitAdditionalArgs; }
            set
            {
                if (value != _zoomPortraitAdditionalArgs)
                {
                    _zoomPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitTextureProviderName
        {
            get { return _zoomPortraitTextureProviderName; }
            set
            {
                if (value != _zoomPortraitTextureProviderName)
                {
                    _zoomPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string InputText
        {
            get { return Composer.State.Text; }
            set { Composer.SetText(value); }
        }

        [DataSourceProperty]
        public bool InputEnabled
        {
            get { return !_isFinalized && !_presentationInitializing && !IsBusy && !IsPregnancyWarningVisible && !IsOutfitVisible; }
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
                    OnPropertyChanged(nameof(InputEnabled));
                    OnPropertyChanged(nameof(SendButtonText));
                }
            }
        }

        [DataSourceProperty]
        public bool IsPortraitZoomVisible
        {
            get { return _isPortraitZoomVisible; }
            set
            {
                if (value != _isPortraitZoomVisible)
                {
                    _isPortraitZoomVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPregnancyWarningVisible
        {
            get { return _isPregnancyWarningVisible; }
            set
            {
                if (value != _isPregnancyWarningVisible)
                {
                    _isPregnancyWarningVisible = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(InputEnabled));
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
        public float PlayerPortraitCropImageWidth
        {
            get { return _playerPortraitCropImageWidth; }
            set
            {
                if (Math.Abs(value - _playerPortraitCropImageWidth) > 0.01f)
                {
                    _playerPortraitCropImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PlayerPortraitCropImageHeight
        {
            get { return _playerPortraitCropImageHeight; }
            set
            {
                if (Math.Abs(value - _playerPortraitCropImageHeight) > 0.01f)
                {
                    _playerPortraitCropImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float NpcPortraitCropImageWidth
        {
            get { return _npcPortraitCropImageWidth; }
            set
            {
                if (Math.Abs(value - _npcPortraitCropImageWidth) > 0.01f)
                {
                    _npcPortraitCropImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float NpcPortraitCropImageHeight
        {
            get { return _npcPortraitCropImageHeight; }
            set
            {
                if (Math.Abs(value - _npcPortraitCropImageHeight) > 0.01f)
                {
                    _npcPortraitCropImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomPortraitImageWidth
        {
            get { return _zoomPortraitImageWidth; }
            set
            {
                if (Math.Abs(value - _zoomPortraitImageWidth) > 0.01f)
                {
                    _zoomPortraitImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomPortraitImageHeight
        {
            get { return _zoomPortraitImageHeight; }
            set
            {
                if (Math.Abs(value - _zoomPortraitImageHeight) > 0.01f)
                {
                    _zoomPortraitImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomFrameWidth
        {
            get { return _zoomFrameWidth; }
            set
            {
                if (Math.Abs(value - _zoomFrameWidth) > 0.01f)
                {
                    _zoomFrameWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomFrameHeight
        {
            get { return _zoomFrameHeight; }
            set
            {
                if (Math.Abs(value - _zoomFrameHeight) > 0.01f)
                {
                    _zoomFrameHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public async void ExecuteSend()
        {
            if (_presentationInitializing || _isFinalized) return;
            if (IsOutfitVisible) { if (IsOutfitInputVisible) ExecuteSubmitOutfit(); return; }
            if (_calibrationMode)
            {
                BusyText = "Calibration mode does not send dialogue.";
                return;
            }
            if (TryRecoverSavedInput() || !_recoveredDraftReview.CanSubmit(DateTime.UtcNow)) return;
            string text = InputText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(text) && _failedTurn.HasPending && !IsBusy && !IsPregnancyWarningVisible)
            {
                var turn = BeginFailedConversationRetry();
                if (turn != null) await SendTextAsync(turn.Text, false, correlationId: turn.CorrelationId,
                    actionDraft: _failedActionDraft, retrySceneTurnId: turn.SceneTurnId, retrySavedTurn: true, recoveredInput: _failedRecoveredInput);
                return;
            }
            if ((string.IsNullOrWhiteSpace(text) && _conversationContext.Value<bool?>("approachOpening") != true) || IsBusy || IsPregnancyWarningVisible)
            {
                return;
            }

            await SendTextAsync(text, true, actionDraft: ActionDraft == null ? null : ReignBeta.Actions.ReignActionService.Copy(ActionDraft), recoveredInput: _recoveredDraftReview.SelectedDraft);
        }

        public Task<ReignDialogueReply> SendAutomationLineAsync(string text)
        {
            return SendTextAsync(text, true);
        }

        public Task<ReignDialogueReply> SendAutomationLineAsync(string text, string correlationId, string auditRunId, int sceneIndex, int turnIndex)
        {
            return SendTextAsync(text, true, string.Empty, correlationId, auditRunId, sceneIndex, turnIndex);
        }

        public Task<ReignDialogueReply> SendAutomationLineAsync(
            string text, string correlationId, string auditRunId, int sceneIndex,
            int turnIndex, bool guardedPromptOverride,
            string guardedPromptOverrideDirective,
            string guardedPromptOverrideOwnerCommandId,
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null)
        {
            return SendTextAsync(text, true, string.Empty, correlationId, auditRunId,
                sceneIndex, turnIndex, guardedPromptOverride,
                guardedPromptOverrideDirective,
                guardedPromptOverrideOwnerCommandId, actionDraft);
        }

        internal string PrepareAutomationActionLine(
            Reign.Core.Contracts.Actions.ActionAgreement draft, string template)
        {
            int marker = (template ?? "").IndexOf("{action}", StringComparison.Ordinal);
            if (draft == null || marker < 0
                || marker != template.LastIndexOf("{action}", StringComparison.Ordinal))
                throw new InvalidOperationException("Supply one action insertion marker.");
            ReignBeta.Actions.ReignActionWording.Render(draft);
            Composer.SetText(template.Substring(0, marker));
            Composer.Insert(draft);
            Composer.Replace(Composer.State.Text.Length, 0, template.Substring(marker + 8));
            if (!Composer.State.HasValidAttachment)
                throw new InvalidOperationException("The production composer rejected the protected action attachment.");
            return Composer.State.Text;
        }

        public async Task<ReignConversationFinishResult> FinishAutomationSceneAsync(string reason)
        {
            RequestConversationFinish(reason ?? "npc_dialogue_audit_scene");
            return _conversationFinishTask == null ? new ReignConversationFinishResult() : await _conversationFinishTask.ConfigureAwait(false);
        }

        public void AddAutomationSystemLine(string text)
        {
            AddSystemLine(text);
        }

        private async Task<ReignDialogueReply> SendTextAsync(
            string text,
            bool clearInput,
            string playerPromptLabel = "",
            string correlationId = "",
            string auditRunId = "",
            int auditSceneIndex = -1,
            int auditTurnIndex = -1,
            bool guardedPromptOverride = false,
            string guardedPromptOverrideDirective = "",
            string guardedPromptOverrideOwnerCommandId = "",
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null,
            string retrySceneTurnId = "", bool retrySavedTurn = false, JObject recoveredInput = null)
        {
            ReignDialogueReply reply = new ReignDialogueReply();
            if (_calibrationMode) return reply;
            if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("N");
            text = text ?? string.Empty;
            // An accepted invitation has no fabricated player turn, including retries.
            if (_conversationContext.Value<bool?>("approachOpening") == true) { text = string.Empty; actionDraft = null; clearInput = false; }
            bool npcOpening = _conversationContext.Value<bool?>("approachOpening") == true && string.IsNullOrWhiteSpace(text);
            string retryScope = "";
            object requestCampaign = null;
            if (string.IsNullOrWhiteSpace(text) && !npcOpening)
            {
                reply.Error = "No text was supplied.";
                return reply;
            }

            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized)
                {
                    reply.Error = "Chat closed.";
                    return false;
                }

                if ((_presentationInitializing && !npcOpening) || IsBusy || IsPregnancyWarningVisible || IsOutfitVisible)
                {
                    reply.Error = "Chat is busy.";
                    return false;
                }

                string playerName = Hero.MainHero?.Name?.ToString() ?? "Player";
                retryScope = ConversationRetryScope;
                requestCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (!retrySavedTurn) ClearFailedConversationTurn();
                if (!npcOpening)
                {
                    try { ReignConversationInputJournal.Save(ReignServerClient.GetCampaignId(),
                        ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
                        Hero.MainHero?.StringId ?? "", correlationId, text, _hero?.StringId ?? "",
                        _conversationContext.Value<string>("sessionId") ?? ""); }
                    catch (Exception ex) { reply.Error = "Your message could not be saved locally: " + ex.Message; AddSystemLine(reply.Error); return false; }
                }
                if (!npcOpening && !retrySavedTurn) AddChatLine(playerName, text);
                if (clearInput)
                {
                    InputText = string.Empty;
                }

                IsBusy = true;
                BusyText = "Preparing response...";
                _conversationTurnCompletion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return true;
            }).ConfigureAwait(false);
            if (!accepted)
            {
                return reply;
            }

            Stopwatch timer = Stopwatch.StartNew();

            try
            {
                string sessionId = await EnsureConversationSessionAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(sessionId))
                    throw new InvalidOperationException("The conversation could not reconnect. Close and reopen chat to try again.");
                ReignXpInteraction xp = await ReignServerClient.BeginXpInteractionAsync().ConfigureAwait(false);
                reply = await ReignServerClient.RequestDialogueResponseAsync(
                    _hero,
                    text,
                    sessionId,
                    playerPromptLabel,
                    correlationId,
                    auditRunId,
                    auditSceneIndex,
                    auditTurnIndex,
                    _initializePresentation,
                    _conversationContext,
                    guardedPromptOverride,
                    guardedPromptOverrideDirective,
                    guardedPromptOverrideOwnerCommandId, actionDraft, retrySavedTurn ? correlationId : "", retrySceneTurnId);
                timer.Stop();

                if (reply.Ok && recoveredInput != null)
                    ReignConversationInputJournal.ResolveReintroducedDraft(recoveredInput,
                        _conversationContext.Value<string>("contributionId") ?? correlationId, _hero?.StringId ?? "");
                JArray recoveredAfterReply = reply.Ok && recoveredInput != null ? ReignConversationInputJournal.ReadRecoverable(
                    _conversationContext.Value<string>("campaignId"), _conversationContext.Value<string>("timelineId"),
                    _conversationContext.Value<string>("playerId"), _hero?.StringId ?? "") : null;

                if (reply.RawResponse?.Value<bool?>("reloadConversationHistory") == true)
                {
                    await ReignMainThread.InvokeAsync(ClearFailedConversationTurn).ConfigureAwait(false);
                    await LoadHistoryAsync().ConfigureAwait(false);
                    return reply;
                }

                await ReignMainThread.InvokeAsync(() =>
                {
                    ReignLog.Info("Individual chat response totalMs=" + timer.ElapsedMilliseconds
                        + " hero=" + (_hero?.StringId ?? "")
                        + " ok=" + reply.Ok
                        + " serverTiming=" + (reply.TimingSummary ?? ""));
                    if (reply.Ok && reply.QueuedActions != null && reply.QueuedActions.Count > 0)
                    {
                        int queued = ReignAICampaignBehavior.Instance?.EnqueueAndExecuteServerActions(reply.QueuedActions) ?? 0;
                        ReignLog.Info("Immediate dialogue action import hero=" + (_hero?.StringId ?? "") + " actions=" + reply.QueuedActions.Count + " queued=" + queued);
                    }

                    if (reply.Ok && reply.ChancellorDecision?.Value<bool?>("accepted") == true)
                    {
                        string kind = reply.ChancellorDecision.Value<string>("kind") ?? string.Empty;
                        string decisionHeroId = reply.ChancellorDecision.Value<string>("heroId") ?? string.Empty;
                        bool heroMatches = _hero != null && string.Equals(_hero.StringId,
                            decisionHeroId, StringComparison.OrdinalIgnoreCase);
                        string officeReceipt = "The Chancellor service is not available.";
                        bool officeChanged = false;
                        if (heroMatches && string.Equals(kind, "appointment_agreement",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            int salary = Math.Max(0,
                                reply.ChancellorDecision.Value<int?>("activeDailySalary") ??
                                Reign.Core.Contracts.Court.ReignRulerDocketRules.DefaultActiveChancellorSalary);
                            officeChanged = ReignCourtCampaignBehavior.Instance
                                ?.TryAppointChancellor(_hero, salary, true, out officeReceipt) == true;
                        }
                        else if (heroMatches && string.Equals(kind, "dismissal_acknowledged",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            officeChanged = ReignCourtCampaignBehavior.Instance
                                ?.TryDismissChancellor(true, out officeReceipt) == true;
                        }
                        else
                        {
                            officeReceipt = "The Chancellor decision did not match this conversation and was not applied.";
                        }
                        AddSystemLine(officeChanged
                            ? "[Chancellor office: " + officeReceipt + "]"
                            : "[Chancellor office unchanged: " + officeReceipt + "]");
                    }

                    if (_isFinalized)
                    {
                        return;
                    }

                    if (recoveredAfterReply != null)
                    {
                        _recoverableInputs = recoveredAfterReply;
                        _recoveredDraftReview.Clear();
                        OnPropertyChanged(nameof(SendButtonText));
                    }

                    if (reply.Ok && !string.IsNullOrWhiteSpace(reply.Text))
                    {
                        ClearFailedConversationTurn();
                        if (_conversationContext.Value<bool?>("rescueOpening") == true)
                            ReignRescueConversationBehavior.Instance?.HeardRescue(_conversationContext["rescueContext"]?.Value<string>("eventId"), _hero);
                        _conversationContext["approachOpening"] = false;
                        _conversationContext["rescueOpening"] = false;
                        if (string.IsNullOrWhiteSpace(auditRunId))
                            xp.Award("conversation:individual:" + correlationId, ReignXpSkill.Charm, ReignXpRules.ConversationXp, ReignXpReason.Conversation);
                        AddChatLine(_hero?.Name?.ToString() ?? "NPC", reply.Text, "npc");
                        if (!string.IsNullOrWhiteSpace(reply.ActionShadowPreview) && ReignBetaSettings.Instance?.DebugMessagesEnabled == true)
                        {
                            AddSystemLine(reply.ActionShadowPreview);
                        }
                        if (!_isOfficialAmbassadorMode && reply.ConceptionGateNeeded && reply.ConceptionAttempt != null)
                        {
                            _calibrationPregnancyFixtureActive = false;
                            _pendingConceptionAttempt = reply.ConceptionAttempt;
                            _pendingConceptionActionText = reply.Text;
                            PreparePregnancyDecisionCompletion();
                            IsPregnancyWarningVisible = true;
                            BusyText = "Choose whether to proceed.";
                        }
                    }
                    else
                    {
                        RetainFailedConversationTurn(text, correlationId, reply.RequestSceneTurnId, retryScope,
                            requestCampaign, actionDraft, reply.Error, recoveredInput);
                    }
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                timer.Stop();
                reply.Error = ex.Message;
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!_isFinalized)
                    {
                        RetainFailedConversationTurn(text, correlationId, reply.RequestSceneTurnId, retryScope,
                            requestCampaign, actionDraft, ex.Message, recoveredInput);
                    }

                    ReignLog.Warn("Individual chat send failed: " + ex.Message);
                }).ConfigureAwait(false);
            }
            finally
            {
                _conversationTurnCompletion?.TrySetResult(true);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!_isFinalized)
                    {
                        IsBusy = IsPregnancyWarningVisible;
                        BusyText = IsPregnancyWarningVisible ? "Choose whether to proceed." : _generationErrorStatus;
                    }
                }).ConfigureAwait(false);
            }

            return reply;
        }

        public async void ExecuteProceedPregnancy()
        {
            ReignConceptionAttemptResult pending = null;
            string actionText = string.Empty;
            bool providerFreeFixture = false;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized || !IsPregnancyWarningVisible || _pregnancyDecisionBusy || _pendingConceptionAttempt == null)
                {
                    return false;
                }
                _pregnancyDecisionBusy = true;
                pending = _pendingConceptionAttempt;
                actionText = _pendingConceptionActionText ?? string.Empty;
                providerFreeFixture = _calibrationPregnancyFixtureActive;
                BusyText = "Resolving pregnancy chance...";
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return;

            if (providerFreeFixture)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    FinishPregnancyDecision("proceed", true, true, string.Empty);
                }).ConfigureAwait(false);
                return;
            }

            ReignConceptionAttemptResult result = await ReignServerClient.ResolvePlayerConceptionAttemptAsync(
                _hero, actionText, pending.AttemptId).ConfigureAwait(false);
            if (!result.Ok)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _pregnancyDecisionBusy = false;
                    BusyText = FriendlyServerError(result.Error);
                    CompletePregnancyDecision("proceed", false, false, result.Error);
                }).ConfigureAwait(false);
                return;
            }

            await ReignServerClient.ApplyResolvedPlayerConceptionAsync(result).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                FinishPregnancyDecision("proceed", true, false, string.Empty);
            }).ConfigureAwait(false);
        }

        public async void ExecutePullOut()
        {
            ReignConceptionAttemptResult pending = null;
            string actionText = string.Empty;
            bool providerFreeFixture = false;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized || !IsPregnancyWarningVisible || _pregnancyDecisionBusy || _pendingConceptionAttempt == null)
                {
                    return false;
                }
                _pregnancyDecisionBusy = true;
                pending = _pendingConceptionAttempt;
                actionText = _pendingConceptionActionText ?? string.Empty;
                providerFreeFixture = _calibrationPregnancyFixtureActive;
                BusyText = "Preventing pregnancy...";
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return;

            if (providerFreeFixture)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    FinishPregnancyDecision("pull_out", true, true, string.Empty);
                }).ConfigureAwait(false);
                return;
            }

            ReignConceptionAttemptResult result = await ReignServerClient.CancelPlayerConceptionAttemptAsync(
                _hero, actionText, pending.AttemptId).ConfigureAwait(false);
            if (!result.Ok || !result.Cancelled)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _pregnancyDecisionBusy = false;
                    string error = string.IsNullOrWhiteSpace(result.Error)
                        ? "The pull-out decision could not be recorded."
                        : result.Error;
                    BusyText = FriendlyServerError(error);
                    CompletePregnancyDecision("pull_out", false, false, error);
                }).ConfigureAwait(false);
                return;
            }

            await ReignMainThread.InvokeAsync(() =>
            {
                FinishPregnancyDecision("pull_out", true, false, string.Empty);
            }).ConfigureAwait(false);

            await SendTextAsync("Pull Out", false, "pull_out").ConfigureAwait(false);
        }

        public void ExecuteClose()
        {
            if (IsOutfitVisible) { HandleOutfitEscape(); return; }
            _pendingConceptionAttempt = null;
            _pendingConceptionActionText = string.Empty;
            _calibrationPregnancyFixtureActive = false;
            IsPregnancyWarningVisible = false;
            CompletePregnancyDecision("closed", false, false, "The pregnancy-warning screen was closed before a decision completed.");
            RequestConversationFinish("closed_by_player");
            ClosePortraitZoom();
            PortraitPatch.SetChatPlayerIdentity(null);
            PortraitPatch.SetChatNpcIdentity(null);
            _closeScreen?.Invoke();
        }

        public void ExecuteTogglePlayerPortraitZoom()
        {
            ShowOrToggleZoom(PlayerPortraitId, PlayerPortraitAdditionalArgs, PlayerPortraitTextureProviderName, _playerPortraitCacheKey);
        }

        public void ExecuteToggleNpcPortraitZoom()
        {
            ShowOrToggleZoom(NpcPortraitId, NpcPortraitAdditionalArgs, NpcPortraitTextureProviderName, _npcPortraitCacheKey);
        }

        public void ExecuteClosePortraitZoom()
        {
            ClosePortraitZoom();
        }

        public void ExecuteLookAtThem()
        {
            if (_hero == null || IsBusy)
            {
                return;
            }

            AddSystemLine("Studying " + (_hero.Name?.ToString() ?? "them") + " closely...");
            _lookAtHero?.Invoke(_hero);
        }

        public void ExecuteNoOp()
        {
        }

        public override void OnFinalize()
        {
            _recoveredDraftReview.Clear();
            _failedRecoveredInput = null;
            ReignActionBuilder.CloseFor(this);
            _composer?.Dispose();
            _outfitCancellation?.Cancel();
            _ = ReignOutfitClient.Cancel(_outfitAttempt);
            FinishOutfit();
            _isFinalized = true;
            CompletePregnancyDecision("finalized", false, _calibrationPregnancyFixtureActive,
                "The Individual Chat screen finalized before the pregnancy decision completed.");
            RequestConversationFinish(_finalizationReason);
            ChatLines.Clear();
            PlayerPortrait = null;
            NpcPortrait = null;
            ClosePortraitZoom();
            base.OnFinalize();
        }

        private void PreparePregnancyDecisionCompletion()
        {
            _pregnancyDecisionCompletion = new TaskCompletionSource<PregnancyDecisionReceipt>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _lastPregnancyDecisionReceipt = null;
        }

        private void FinishPregnancyDecision(
            string decision,
            bool ok,
            bool providerFreeFixture,
            string error)
        {
            _pregnancyDecisionBusy = false;
            _pendingConceptionAttempt = null;
            _pendingConceptionActionText = string.Empty;
            _calibrationPregnancyFixtureActive = false;
            IsPregnancyWarningVisible = false;
            IsBusy = false;
            BusyText = string.Empty;
            CompletePregnancyDecision(decision, ok, providerFreeFixture, error);
        }

        private void CompletePregnancyDecision(
            string decision,
            bool ok,
            bool providerFreeFixture,
            string error)
        {
            if (_pregnancyDecisionCompletion == null || _pregnancyDecisionCompletion.Task.IsCompleted)
            {
                return;
            }
            PregnancyDecisionReceipt receipt = new PregnancyDecisionReceipt
            {
                Decision = decision ?? string.Empty,
                Ok = ok,
                ProviderFreeFixture = providerFreeFixture,
                WarningVisibleAfter = IsPregnancyWarningVisible,
                InputEnabledAfter = InputEnabled,
                BusyAfter = IsBusy,
                Error = error ?? string.Empty
            };
            _lastPregnancyDecisionReceipt = receipt;
            _pregnancyDecisionCompletion.TrySetResult(receipt);
        }

        private async Task<string> EnsureConversationSessionAsync()
        {
            if (!string.IsNullOrWhiteSpace(_conversationSessionId))
            {
                return _conversationSessionId;
            }
            Task<ConversationSessionStart> pending = _conversationStartGate.EnsureAsync(
                StartConversationAsync, true);
            _conversationSessionTask = pending;
            ConversationSessionStart result = await pending.ConfigureAwait(false);
            _conversationSessionId = result.Ok ? result.SessionId : string.Empty;
            return _conversationSessionId;
        }

        private async Task<ConversationSessionStart> StartConversationAsync()
        {
            var owner = ReignIndividualConversationBehavior.Instance;
            if (owner != null && !await owner.WaitForRecoveryAsync().ConfigureAwait(false))
                return new ConversationSessionStart { Error = "Conversation recovery has not completed." };
            return await ReignServerClient.StartConversationSessionWithIdentityAsync(_hero, _conversationContext).ConfigureAwait(false);
        }

        internal void SetFinalizationReason(string reason) { _finalizationReason = reason; }

        internal ConversationContinuationToken CaptureContinuation(string reason)
        {
            if (_calibrationMode || _isOfficialAmbassadorMode || _conversationSessionTask?.Status != TaskStatus.RanToCompletion)
                return null;
            var session = _conversationSessionTask.Result;
            if (!session.Ok || (_conversationFinishRequested && _finalizationReason != reason)) return null;
            var token = new ConversationContinuationToken {
                CampaignId = _conversationContext.Value<string>("campaignId"), TimelineId = _conversationContext.Value<string>("timelineId"),
                PlayerId = _conversationContext.Value<string>("playerId"), NpcId = _hero?.StringId ?? string.Empty,
                SessionId = session.SessionId, LogicalConversationId = session.LogicalConversationId,
                Generation = ConversationGeneration, Reason = reason
            };
            return token.IsValid ? token : null;
        }

        internal async Task<ConversationContinuationToken> SuspendForContinuationAsync(string reason)
        {
            _finalizationReason = reason;
            await EnsureConversationSessionAsync().ConfigureAwait(false);
            var token = CaptureContinuation(reason);
            RequestConversationFinish(reason);
            var result = _conversationFinishTask == null ? null : await _conversationFinishTask.ConfigureAwait(false);
            return result?.Ok == true ? token : null;
        }

        private void RequestConversationFinish(string reason)
        {
            if (_calibrationMode || _conversationFinishRequested)
            {
                return;
            }
            _conversationFinishRequested = true;
            _conversationFinishTask = FinishConversationAsync(reason);
        }

        private async Task<ReignConversationFinishResult> FinishConversationAsync(string reason)
        {
            var turn = _conversationTurnCompletion;
            if (turn != null) await turn.Task.ConfigureAwait(false);
            string sessionId = await EnsureConversationSessionAsync().ConfigureAwait(false);
            return await ReignServerClient.FinishConversationSessionAsync(_hero, sessionId, reason, _conversationContext).ConfigureAwait(false);
        }

        private async Task LoadHistoryAsync()
        {
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized)
                {
                    return false;
                }

                IsBusy = true;
                BusyText = "Loading history...";
                return true;
            }).ConfigureAwait(false);
            if (!accepted)
            {
                return;
            }

            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                List<ReignDialogueLine> history = await ReignServerClient.GetDialogueHistoryAsync(_hero, 80, _conversationContext).ConfigureAwait(false);
                JArray recoverable = _isOfficialAmbassadorMode ? new JArray() : ReignConversationInputJournal.ReadRecoverable(
                    _conversationContext.Value<string>("campaignId"), _conversationContext.Value<string>("timelineId"),
                    _conversationContext.Value<string>("playerId"), _hero?.StringId ?? "");
                timer.Stop();

                await ReignMainThread.InvokeAsync(() =>
                {
                    if (_isFinalized)
                    {
                        return;
                    }

                    ReignLog.Info("Individual chat history totalMs=" + timer.ElapsedMilliseconds + " hero=" + (_hero?.StringId ?? "") + " lines=" + history.Count);
                    _recoverableInputs = recoverable;
                    OnPropertyChanged(nameof(SendButtonText));
                    ChatLines.Clear();
                    if (history.Count == 0)
                    {
                        AddSystemLine("No previous conversation with " + (_hero?.Name?.ToString() ?? "this character") + ".");
                    }
                    else
                    {
                        foreach (ReignDialogueLine line in history)
                        {
                            AddChatLine(line.DisplaySpeaker, line.Text, line.Role);
                        }
                    }
                    if (_isOfficialAmbassadorMode) AddOfficialOpeningNotice();
                    if (_recoverableInputs.Count > 0)
                        AddSystemLine(_recoverableInputs.Count + " saved message(s) have unconfirmed delivery. With an empty message box, select Recover to review the newest draft. Nothing is sent automatically.");
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                timer.Stop();
                ReignLog.Warn("Individual chat history failed: " + ex.Message);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!_isFinalized)
                    {
                        ChatLines.Clear();
                        AddSystemLine(FriendlyServerError(ex.Message));
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!_isFinalized)
                    {
                        IsBusy = false;
                        BusyText = string.Empty;
                    }
                }).ConfigureAwait(false);
            }
        }

        private async Task<bool> InitializePresentationAsync()
        {
            try
            {
                await OpenNpcInitiatedConversationAsync().ConfigureAwait(false);
                return !_isFinalized;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Conversation presentation unavailable: " + ex.Message);
                return false;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _presentationInitializing = false;
                    OnPropertyChanged(nameof(InputEnabled));
                }).ConfigureAwait(false);
            }
        }

        private async Task OpenNpcInitiatedConversationAsync()
        {
            await LoadHistoryAsync().ConfigureAwait(false);
            // Only unfinished input in this exact session defers its opener. Older
            // recovery-only drafts cannot indefinitely suppress fresh NPC approaches.
            if (ReignRecoveredDraftReview.HasPendingInSession(_recoverableInputs, _conversationContext.Value<string>("sessionId"))
                && _conversationContext.Value<bool?>("approachOpening") != true) return;
            if (_conversationContext.Value<bool?>("approachOpening") == true && !_isFinalized)
            {
                await SendTextAsync(string.Empty, false).ConfigureAwait(false);
                return;
            }
            string mode = _conversationContext.Value<string>("conversationMode") ?? "";
            if (_isFinalized || _isOfficialAmbassadorMode || _conversationContext.Value<bool?>("rescueOpening") == true
                || (!string.IsNullOrEmpty(mode) && mode != "in_person" && mode != "npc_story")
                || !string.IsNullOrEmpty(_conversationContext.Value<string>("continuationReason"))) return;
            await ReignMainThread.InvokeAsync(() => { if (!_isFinalized) { IsBusy = true; BusyText = "Approaching..."; } }).ConfigureAwait(false);
            try
            {
                string session = await EnsureConversationSessionAsync().ConfigureAwait(false);
                if (session.Length == 0 || _isFinalized) return;
                var lives = ReignNpcLivesBehavior.Instance;
                if (lives != null)
                    await ReignMainThread.InvokeAsync(() => lives.PrepareLifeConversationAsync(_hero, session, _conversationContext)).Unwrap().ConfigureAwait(false);
                await ReignServerClient.OpenNpcLifeApproachAsync(_hero, session, _conversationContext, () => !_isFinalized).ConfigureAwait(false);
                // Reload accepted history once. This also recovers an opener which
                // finished after the previous screen closed, without speaking twice.
                if (!_isFinalized) await LoadHistoryAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { ReignLog.Warn("NPC Approach unavailable: " + ex.Message); }
            finally { await ReignMainThread.InvokeAsync(() => { if (!_isFinalized) { IsBusy = false; BusyText = ""; } }).ConfigureAwait(false); }
        }

        private void AddSystemLine(string text)
        {
            AddChatLine("Bannerlord Reign", text, "system");
        }

        private void AddOfficialOpeningNotice()
        {
            string ruler = _conversationContext.Value<string>("representedRulerName") ?? "the represented ruler";
            AddSystemLine("OFFICIAL RELAY: This entire audience is an official diplomatic record delivered to " + ruler + ". There is no off-the-record portion in Ambassador Mode.");
            AddChatLine(_hero?.Name?.ToString() ?? "Ambassador",
                "I acknowledge my charge. I will faithfully convey everything said in this audience to " + ruler + ", and I cannot promise secrecy here.", "npc");
        }

        private void AddChatLine(string speaker, string text, string role = "player")
        {
            ChatLines.Add(new ReignChatLineVM(speaker, text, role));
            ChatScrollVersion++;
        }

        private static string FriendlyServerError(string error)
        {
            string text = (error ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return "Bannerlord Reign server failed: no response was generated.";
            }

            if (ContainsIgnoreCase(text, "missing_api_key")
                || ContainsIgnoreCase(text, "Invalid Authentication")
                || ContainsIgnoreCase(text, "invalid_api_key")
                || ContainsIgnoreCase(text, "unauthorized")
                || ContainsIgnoreCase(text, "401")
                || ContainsIgnoreCase(text, "api key"))
            {
                return "API key missing or rejected. Open Server Diagnostics, save the key, then run Test LLM Key.";
            }

            if (ContainsIgnoreCase(text, "character generation has failed"))
            {
                return "Character generation failed. Open Server Diagnostics, run Test LLM Key, then retry the failed character build.";
            }

            text = text.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ");
            while (text.Contains("  "))
            {
                text = text.Replace("  ", " ");
            }

            const int maxLength = 260;
            if (text.Length > maxLength)
            {
                text = text.Substring(0, maxLength).TrimEnd() + "...";
            }

            return "Bannerlord Reign server failed: " + text;
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text)
                && !string.IsNullOrEmpty(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ImageIdentifierVM BuildPortrait(Hero hero, string cacheKey)
        {
            try
            {
                if (hero?.CharacterObject == null)
                {
                    return null;
                }

                Equipment equipment = hero == Hero.MainHero
                    ? hero.CivilianEquipment ?? hero.CharacterObject.FirstCivilianEquipment ?? hero.CharacterObject.Equipment
                    : hero.CharacterObject.FirstCivilianEquipment ?? hero.CharacterObject.Equipment;
                CharacterCode code = CharacterCode.CreateFrom(hero.CharacterObject, equipment);
                if (string.IsNullOrWhiteSpace(code?.Code))
                {
                    return null;
                }

                PortraitIndex.Register(PortraitRequestRegistry.NormalizeKey(code.Code), cacheKey);
                return new CharacterImageIdentifierVM(code);
            }
            catch
            {
                return null;
            }
        }

        private void ShowOrToggleZoom(string id, string additionalArgs, string providerName, string cacheKey)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(providerName) || string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            if (IsPortraitZoomVisible && string.Equals(_zoomPortraitCacheKey, cacheKey, StringComparison.Ordinal))
            {
                ClosePortraitZoom();
                return;
            }

            _zoomPortraitCacheKey = cacheKey;
            PortraitPatch.SetChatZoomIdentity(cacheKey);
            SetZoomAspect(GetAspectRatioFor(cacheKey, PortraitQualityTier.Zoom));
            ZoomPortraitId = id;
            ZoomPortraitAdditionalArgs = additionalArgs ?? string.Empty;
            ZoomPortraitTextureProviderName = providerName;
            IsPortraitZoomVisible = true;
        }

        private void ClosePortraitZoom()
        {
            IsPortraitZoomVisible = false;
            _zoomPortraitCacheKey = null;
            PortraitPatch.SetChatZoomIdentity(null);
        }

        private void SetPlayerCropAspect(float ratio)
        {
            GetTopToBottomCropDimensions(ratio, out var width, out var height);
            PlayerPortraitCropImageWidth = width;
            PlayerPortraitCropImageHeight = height;
        }

        private void SetNpcCropAspect(float ratio)
        {
            GetTopToBottomCropDimensions(ratio, out var width, out var height);
            NpcPortraitCropImageWidth = width;
            NpcPortraitCropImageHeight = height;
        }

        private static void GetTopToBottomCropDimensions(float ratio, out float width, out float height)
        {
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                ratio = 0.75f;
            }

            float apertureAspect = HeaderPortraitApertureWidth / HeaderPortraitApertureHeight;
            if (ratio >= apertureAspect)
            {
                height = HeaderPortraitApertureHeight * HeaderPortraitCoverOverscan;
                width = height * ratio;
            }
            else
            {
                width = HeaderPortraitApertureWidth * HeaderPortraitCoverOverscan;
                height = width / ratio;
            }
        }

        private void SetZoomAspect(float ratio)
        {
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                ratio = 0.75f;
            }

            float width = 860f;
            float height = width / ratio;
            if (height > 920f)
            {
                height = 920f;
                width = height * ratio;
            }

            ZoomPortraitImageWidth = width;
            ZoomPortraitImageHeight = height;
            ZoomFrameWidth = width + 36f;
            ZoomFrameHeight = height + 36f;
        }

        private static float GetAspectRatioFor(string cacheKey, PortraitQualityTier tier)
        {
            if (!PortraitDerivativeService.TryGetDerivativeAspectRatio(cacheKey, tier, out var ratio)
                && !PortraitCache.TryGetPortraitAspectRatio(cacheKey, out ratio))
            {
                return 0.75f;
            }

            return ratio;
        }

        private static string BuildHeroSubtitle(Hero hero)
        {
            if (hero == null)
            {
                return string.Empty;
            }

            string clan = hero.Clan?.Name?.ToString();
            string kingdom = hero.Clan?.Kingdom?.InformalName?.ToString() ?? hero.MapFaction?.Name?.ToString();
            if (!string.IsNullOrWhiteSpace(clan) && !string.IsNullOrWhiteSpace(kingdom))
            {
                return clan + " - " + kingdom;
            }

            if (!string.IsNullOrWhiteSpace(clan))
            {
                return clan;
            }

            return !string.IsNullOrWhiteSpace(kingdom) ? kingdom : hero.Occupation.ToString();
        }

        private static string BuildLocationText(Hero hero)
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? hero?.CurrentSettlement;
            if (settlement != null)
            {
                return "Location: " + settlement.Name;
            }

            return "Location: with your party";
        }
    }
}
