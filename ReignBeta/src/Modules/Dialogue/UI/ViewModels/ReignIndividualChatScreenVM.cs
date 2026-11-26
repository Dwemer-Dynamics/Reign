using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using Reign.Core.Contracts.Dialogue;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Campaign;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignIndividualChatScreenVM : ViewModel, ReignBeta.Dialogue.UI.IReignActionHost
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
        private object _pregnancyDecisionOperation;
        private bool _calibrationPregnancyFixtureActive;
        private ReignConceptionAttemptResult _pendingConceptionAttempt;
        private string _pendingConceptionActionText;
        private TaskCompletionSource<PregnancyDecisionReceipt> _pregnancyDecisionCompletion;
        private PregnancyDecisionReceipt _lastPregnancyDecisionReceipt;
        private bool _isFinalized;
        private bool _presentationInitializing;
        private bool _firstMoveChosen;
        private Task<bool> _presentationInitializationTask = Task.FromResult(true);
        internal Task<bool> PresentationInitialization => _presentationInitializationTask;
        private Task<ConversationSessionStart> _conversationSessionTask;
        private readonly ConversationSessionStartGate _conversationStartGate;
        private Task<ReignConversationFinishResult> _conversationFinishTask;
        private TaskCompletionSource<bool> _conversationTurnCompletion;
        private ConversationRequestOwner _activeTurn;
        // The load which opened this screen (captured on the Bannerlord thread).
        // Session start, its install, finish payload and close work are fenced to it.
        private readonly ReignServerClient.RequestFence _loadFence;
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
            _conversationContext["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main";
            _conversationContext["playerId"] = Hero.MainHero?.StringId ?? string.Empty;
            _loadFence = ReignServerClient.RequestFence.Capture();
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
                    RefreshInputAvailability();
                    if (!value && _view != null) _ = _view.RestoreAsync();
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
                    RefreshInputAvailability();
                }
            }
        }

        private void RefreshInputAvailability()
        {
            _view?.RefreshAvailability();
            OnPropertyChanged(nameof(InputEnabled));
            OnPropertyChanged(nameof(SendButtonText));
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
            if (string.IsNullOrWhiteSpace(text) && CanOfferApproach)
            {
                await RequestLifeOpeningAsync();
                return;
            }
            if ((string.IsNullOrWhiteSpace(text) && !HasAcceptedNpcInvitation) || IsBusy || IsPregnancyWarningVisible)
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
            ReignBeta.WorldSimulation.Actions.ReignActionWording.Render(draft);
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
            string retrySceneTurnId = "", bool retrySavedTurn = false, JObject recoveredInput = null,
            bool endsOnCloseRequest = false)
        {
            ReignDialogueReply reply = new ReignDialogueReply();
            if (_calibrationMode) return reply;
            if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("N");
            text = text ?? string.Empty;
            // An accepted invitation has no fabricated player turn, including retries.
            bool npcOpening = HasAcceptedNpcInvitation;
            if (npcOpening) { text = string.Empty; actionDraft = null; clearInput = false; }
            bool prepareLifeContext = false;
            string retryScope = "";
            object requestCampaign = null;
            ConversationRequestOwner owner = null;
            // Immutable reintroduced-draft inputs, captured on the Bannerlord thread.
            JObject reintroducedOriginal = null;
            string reintroducedContribution = string.Empty, reintroducedHero = string.Empty;
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
                        ReignServices.WorldHistory?.TimelineId ?? "main",
                        Hero.MainHero?.StringId ?? "", correlationId, text, _hero?.StringId ?? "",
                        _conversationContext.Value<string>("sessionId") ?? ""); }
                    catch (Exception ex) { reply.Error = "Your message could not be saved locally: " + ex.Message; AddSystemLine(reply.Error); return false; }
                }
                prepareLifeContext = !npcOpening && CanStartLifeOpening;
                _firstMoveChosen = true;
                if (!npcOpening && !retrySavedTurn) AddChatLine(playerName, text);
                if (clearInput)
                {
                    InputText = string.Empty;
                }

                IsBusy = true;
                BusyText = "Preparing response...";
                owner = new ConversationRequestOwner(ReignServerClient.RequestFence.Capture(), Hero.MainHero,
                    new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
                    { EndsOnCloseRequest = endsOnCloseRequest };
                _activeTurn = owner;
                _conversationTurnCompletion = owner.Completion;
                if (recoveredInput != null)
                {
                    reintroducedOriginal = (JObject)recoveredInput.DeepClone();
                    reintroducedContribution = _conversationContext.Value<string>("contributionId") ?? correlationId;
                    reintroducedHero = _hero?.StringId ?? string.Empty;
                }
                return true;
            }).ConfigureAwait(false);
            if (!accepted)
            {
                return reply;
            }

            Stopwatch timer = Stopwatch.StartNew();
            Func<bool> ownerCurrent = () => IsRequestOwnerCurrent(owner);

            try
            {
                string sessionId = await EnsureConversationSessionAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(sessionId))
                    throw new InvalidOperationException("The conversation could not reconnect. Close and reopen chat to try again.");
                // Bind the confirmed server session to this accepted turn. An
                // unbound owner (no session yet) is a normal start, not stale.
                if (!await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ownerCurrent()) return false;
                        owner.SessionId = sessionId;
                        return true;
                    }).ConfigureAwait(false))
                    throw new ReignRequestOwnerAbandonedException("The conversation closed before its session was bound.");
                if (prepareLifeContext) await PrepareLifeOpeningAsync(sessionId, ownerCurrent).ConfigureAwait(false);
                ReignXpInteraction xp = await ReignXpClient.BeginXpInteractionAsync().ConfigureAwait(false);
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
                    guardedPromptOverrideOwnerCommandId, actionDraft, retrySavedTurn ? correlationId : "", retrySceneTurnId,
                    ownerCurrent, requestViewInsertionReceipt: !_isOfficialAmbassadorMode);
                timer.Stop();

                // Durable original-payload reconciliation is not presentation: it
                // runs for any accepted reply, including one whose view went stale.
                // The journal itself verifies the original scope, the old hash and
                // the confirmed new contribution. Only the recoverable list and
                // retry/UI state below are screen-gated.
                if (reintroducedOriginal != null && (reply.Ok || reply.RawResponse?["acceptedResponse"] is JObject))
                {
                    try { ReignConversationInputJournal.ResolveReintroducedDraft(reintroducedOriginal, reintroducedContribution, reintroducedHero); }
                    catch (Exception ex) { ReignLog.Warn("Reintroduced draft reconciliation deferred: " + ex.GetType().Name); }
                }

                if (reply.RawResponse?.Value<bool?>("reloadConversationHistory") == true)
                {
                    bool reload = await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!ownerCurrent()) return false;
                        ClearFailedConversationTurn();
                        return true;
                    }).ConfigureAwait(false);
                    if (reload) await LoadHistoryAsync(ownerCurrent).ConfigureAwait(false);
                    return reply;
                }

                await ReignMainThread.InvokeAsync(() =>
                {
                    ReignLog.Info("Individual chat response totalMs=" + timer.ElapsedMilliseconds
                        + " hero=" + (_hero?.StringId ?? "")
                        + " ok=" + reply.Ok
                        + " serverTiming=" + (reply.TimingSummary ?? ""));
                    // Tested in this same game-thread callback before any presentation,
                    // journal, XP, office or immediate native-action mutation. The
                    // durable journal acknowledgement already ran in the transport;
                    // undelivered queued actions stay in the server outbox.
                    if (!ownerCurrent())
                    {
                        ReignLog.Info("Individual chat reply skipped by a stale view hero=" + (_hero?.StringId ?? ""));
                        return;
                    }
                    JArray recoveredAfterReply = reply.Ok && recoveredInput != null
                        ? ReadRecoverableInputs() : null;
                    // Dialogue actions were imported once by the conversation wrapper; only
                    // test directives remain here. An interrupted import never discards the
                    // accepted reply and never becomes a failed turn or a retry.
                    string heldDirectiveId = null;
                    // QueuedActions also carries the already-imported dialogue rows as evidence.
                    if (reply.Ok && reply.TestDirectiveActions != null && reply.TestDirectiveActions.Count > 0)
                    {
                        int queued = 0;
                        if (ReignServices.AI?.TryImportForPresentation(reply.TestDirectiveActions, out queued, out heldDirectiveId) == false)
                            ReignLog.Warn("Immediate test directive import interrupted hero=" + (_hero?.StringId ?? ""));
                        ReignLog.Info("Immediate test directive import hero=" + (_hero?.StringId ?? "") + " actions=" + reply.TestDirectiveActions.Count + " queued=" + queued);
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
                            officeChanged = ReignServices.Court
                                ?.TryAppointChancellor(_hero, salary, true, out officeReceipt) == true;
                        }
                        else if (heroMatches && string.Equals(kind, "dismissal_acknowledged",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            officeChanged = ReignServices.Court
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

                    if (recoveredAfterReply != null)
                    {
                        _recoverableInputs = recoveredAfterReply;
                        _recoveredDraftReview.Clear();
                        OnPropertyChanged(nameof(SendButtonText));
                    }

                    // Existing receipts/outcomes are separate system lines after the exact NPC line.
                    List<string> statusLines = reply.Ok ? ReignStructuredActionClient.NativeStatusLines(reply.RawResponse) : new List<string>();
                    if (heldDirectiveId != null)
                        statusLines.Add("[unknown: " + ReignBeta.WorldSimulation.Actions.ReignCompletedOrderPolicy.UncertainExecutionMarker + "]");
                    if (reply.Ok && (!string.IsNullOrWhiteSpace(reply.Text) || statusLines.Count > 0))
                    {
                        ClearFailedConversationTurn();
                        if (_conversationContext.Value<bool?>("rescueOpening") == true)
                        {
                            ReignServices.RescueConversation?.HeardRescue(_conversationContext["rescueContext"]?.Value<string>("eventId"), _hero);
                            // The opening instruction ends only after a usable response.
                            // Retain the dated rescue facts and unrelated directives.
                            _conversationContext.Remove("sceneContextDirective");
                        }
                        _conversationContext["approachOpening"] = false;
                        _conversationContext["rescueOpening"] = false;
                        if (string.IsNullOrWhiteSpace(auditRunId))
                            xp.Award("conversation:individual:" + correlationId, ReignXpSkill.Charm, ReignXpRules.ConversationXp, ReignXpReason.Conversation);
                        string viewExchange = reply.ExchangeId ?? "";
                        var viewPlayerLine = ChatLines.LastOrDefault(line => line.IsPlayerLine && string.IsNullOrEmpty(line.ExchangeId));
                        if (viewPlayerLine != null) viewPlayerLine.ExchangeId = viewExchange;
                        if (!string.IsNullOrWhiteSpace(reply.Text))
                        {
                            // The exact accepted line is inserted once; its insertion receipt
                            // is derived only from that raw line, never from status text.
                            AddChatLine(_hero?.Name?.ToString() ?? "NPC", reply.Text, "npc", viewExchange);
                            if (!_isOfficialAmbassadorMode) AcknowledgeViewInsertion(owner, reply);
                        }
                        foreach (string statusLine in statusLines) AddSystemLine(statusLine);
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
                    // Failed-draft state belongs only to a still-current owner.
                    if (ownerCurrent())
                    {
                        RetainFailedConversationTurn(text, correlationId, reply.RequestSceneTurnId, retryScope,
                            requestCampaign, actionDraft, ex.Message, recoveredInput);
                    }

                    if (ex is ReignRequestOwnerAbandonedException) ReignLog.Info("Individual chat send abandoned: " + ex.Message);
                    else ReignLog.Warn("Individual chat send failed: " + ex.Message);
                }).ConfigureAwait(false);
            }
            finally
            {
                // Settle this turn's own completion source even when stale, so a
                // pending finish never waits on it and never settles a newer turn.
                owner.Completion.TrySetResult(true);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!ReferenceEquals(_activeTurn, owner)) return;
                    _activeTurn = null;
                    // A changed load never receives this turn's busy/presentation reset.
                    if (!_isFinalized && owner.Fence.IsCurrent())
                    {
                        IsBusy = IsPregnancyWarningVisible;
                        BusyText = IsPregnancyWarningVisible ? "Choose whether to proceed." : _generationErrorStatus;
                    }
                }).ConfigureAwait(false);
            }

            return reply;
        }

        // One accepted Individual Chat turn: the original load fence, player, this
        // screen and active-turn ownership, plus its own completion source. The
        // server session is bound once confirmed.
        private sealed class ConversationRequestOwner
        {
            internal ConversationRequestOwner(ReignServerClient.RequestFence fence, Hero player, TaskCompletionSource<bool> completion)
            {
                Fence = fence; Player = player; Completion = completion;
            }
            internal ReignServerClient.RequestFence Fence { get; }
            internal Hero Player { get; }
            internal TaskCompletionSource<bool> Completion { get; }
            internal string SessionId { get; set; } = string.Empty;
            // A system follow-up turn (Pull Out) also ends once close requests finish.
            internal bool EndsOnCloseRequest { get; set; }
        }

        // Bannerlord thread only.
        private bool IsRequestOwnerCurrent(ConversationRequestOwner owner)
        {
            return owner != null && !_isFinalized && ReferenceEquals(_activeTurn, owner)
                && !(owner.EndsOnCloseRequest && _conversationFinishRequested)
                && owner.Fence.IsCurrent() && ReferenceEquals(owner.Player, Hero.MainHero)
                && (owner.SessionId.Length == 0 || string.Equals(owner.SessionId, _conversationSessionId, StringComparison.Ordinal));
        }

        // Bannerlord thread, immediately after AddChatLine inserted reply.Text for this owner.
        // Captures the exact acknowledgment from the original request scope. Delivery is
        // detached: the insertion already happened, so it may finish after this view closes
        // while the original load, player and shared generation still match. Any failure
        // leaves presentation unknown and never touches the accepted reply or the UI.
        private void AcknowledgeViewInsertion(ConversationRequestOwner owner, ReignDialogueReply reply)
        {
            try
            {
                string generation = owner?.Fence.SharedActivityGeneration ?? string.Empty;
                if (!IsRequestOwnerCurrent(owner) || generation.Length == 0
                    || generation != (ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty)) return;
                string campaign = ReignServerClient.GetCampaignId();
                string timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
                string player = owner.Player?.StringId ?? string.Empty;
                if (campaign != _conversationContext.Value<string>("campaignId") || timeline != _conversationContext.Value<string>("timelineId")
                    || player != _conversationContext.Value<string>("playerId")) return;
                var ack = ReignServerClient.CaptureIndividualViewInsertion(reply.RawResponse, reply.Text,
                    campaign, timeline, owner.SessionId, player, _hero?.StringId ?? string.Empty, generation);
                if (ack == null) return;
                ReignServerClient.RequestFence fence = owner.Fence;
                Hero originalPlayer = owner.Player;
                Func<bool> originalLoad = () => fence.IsCurrent() && ReferenceEquals(originalPlayer, Hero.MainHero)
                    && generation == (ReignServices.WorldHistory?.SharedActivityGeneration ?? string.Empty);
                _ = Task.Run(() => ReignServerClient.SendIndividualViewInsertionAsync(ack, originalLoad));
            }
            catch (Exception ex) { ReignLog.Info("View insertion receipt skipped: " + ex.GetType().Name); }
        }

        // Bannerlord thread only, for a current owner whose reply was accepted.
        private JArray ReadRecoverableInputs()
        {
            return ReignConversationInputJournal.ReadRecoverable(
                _conversationContext.Value<string>("campaignId"), _conversationContext.Value<string>("timelineId"),
                _conversationContext.Value<string>("playerId"), _hero?.StringId ?? "");
        }

        // Bannerlord thread only, at decision admission. The returned predicate
        // (Bannerlord thread only) holds while this screen is open, close has not
        // requested finish, and this exact decision operation and warning attempt
        // are pending; a real attempt also needs its original load, player and session.
        private Func<bool> CapturePregnancyDecisionOwner(object operation, ReignConceptionAttemptResult attempt, bool providerFreeFixture)
        {
            Hero player = providerFreeFixture ? null : Hero.MainHero;
            string sessionId = _conversationSessionId ?? string.Empty;
            return () => !_isFinalized && !_conversationFinishRequested
                && ReferenceEquals(_pregnancyDecisionOperation, operation)
                && ReferenceEquals(_pendingConceptionAttempt, attempt)
                && (providerFreeFixture || (_loadFence.IsCurrent() && ReferenceEquals(player, Hero.MainHero)
                    && string.Equals(sessionId, _conversationSessionId ?? string.Empty, StringComparison.Ordinal)));
        }

        public async void ExecuteProceedPregnancy()
        {
            ReignConceptionAttemptResult pending = null;
            string actionText = string.Empty;
            bool providerFreeFixture = false;
            Func<bool> owner = null;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized || _conversationFinishRequested || !IsPregnancyWarningVisible || _pregnancyDecisionBusy || _pendingConceptionAttempt == null
                    || (!_calibrationPregnancyFixtureActive && !_loadFence.IsCurrent()))
                {
                    return false;
                }
                _pregnancyDecisionBusy = true;
                _pregnancyDecisionOperation = new object();
                pending = _pendingConceptionAttempt;
                actionText = _pendingConceptionActionText ?? string.Empty;
                providerFreeFixture = _calibrationPregnancyFixtureActive;
                owner = CapturePregnancyDecisionOwner(_pregnancyDecisionOperation, pending, providerFreeFixture);
                BusyText = "Resolving pregnancy chance...";
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return;

            if (providerFreeFixture)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (owner()) FinishPregnancyDecision("proceed", true, true, string.Empty);
                }).ConfigureAwait(false);
                return;
            }

            ReignConceptionAttemptResult result = await ReignRelationshipDirectorClient.ResolvePlayerConceptionAttemptAsync(
                _hero, actionText, pending.AttemptId, owner).ConfigureAwait(false);
            if (!result.Ok)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!owner()) return;
                    _pregnancyDecisionBusy = false;
                    _pregnancyDecisionOperation = null;
                    BusyText = FriendlyServerError(result.Error);
                    CompletePregnancyDecision("proceed", false, false, result.Error);
                }).ConfigureAwait(false);
                return;
            }

            await ReignRelationshipDirectorClient.ApplyResolvedPlayerConceptionAsync(result, owner).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (owner()) FinishPregnancyDecision("proceed", true, false, string.Empty);
            }).ConfigureAwait(false);
        }

        public async void ExecutePullOut()
        {
            ReignConceptionAttemptResult pending = null;
            string actionText = string.Empty;
            bool providerFreeFixture = false;
            Func<bool> owner = null;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized || _conversationFinishRequested || !IsPregnancyWarningVisible || _pregnancyDecisionBusy || _pendingConceptionAttempt == null
                    || (!_calibrationPregnancyFixtureActive && !_loadFence.IsCurrent()))
                {
                    return false;
                }
                _pregnancyDecisionBusy = true;
                _pregnancyDecisionOperation = new object();
                pending = _pendingConceptionAttempt;
                actionText = _pendingConceptionActionText ?? string.Empty;
                providerFreeFixture = _calibrationPregnancyFixtureActive;
                owner = CapturePregnancyDecisionOwner(_pregnancyDecisionOperation, pending, providerFreeFixture);
                BusyText = "Preventing pregnancy...";
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return;

            if (providerFreeFixture)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (owner()) FinishPregnancyDecision("pull_out", true, true, string.Empty);
                }).ConfigureAwait(false);
                return;
            }

            ReignConceptionAttemptResult result = await ReignRelationshipDirectorClient.CancelPlayerConceptionAttemptAsync(
                _hero, actionText, pending.AttemptId, owner).ConfigureAwait(false);
            if (!result.Ok || !result.Cancelled)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!owner()) return;
                    _pregnancyDecisionBusy = false;
                    _pregnancyDecisionOperation = null;
                    string error = string.IsNullOrWhiteSpace(result.Error)
                        ? "The pull-out decision could not be recorded."
                        : result.Error;
                    BusyText = FriendlyServerError(error);
                    CompletePregnancyDecision("pull_out", false, false, error);
                }).ConfigureAwait(false);
                return;
            }

            // The follow-up turn is admitted inline (already on the Bannerlord
            // thread) in the same guarded callback that finishes the warning.
            Task<ReignDialogueReply> followUp = await ReignMainThread.InvokeAsync(() =>
            {
                if (!owner()) return null;
                FinishPregnancyDecision("pull_out", true, false, string.Empty);
                return SendTextAsync("Pull Out", false, "pull_out", endsOnCloseRequest: true);
            }).ConfigureAwait(false);
            if (followUp != null) await followUp.ConfigureAwait(false);
        }

        public void ExecuteClose()
        {
            if (IsOutfitVisible) { HandleOutfitEscape(); return; }
            _pendingConceptionAttempt = null;
            _pendingConceptionActionText = string.Empty;
            _calibrationPregnancyFixtureActive = false;
            IsPregnancyWarningVisible = false;
            _pregnancyDecisionBusy = false; // Releases this screen's token; stale callbacks then fail owner().
            _pregnancyDecisionOperation = null;
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
            _view?.Close();
            _pregnancyDecisionBusy = false;
            _pregnancyDecisionOperation = null;
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
            _pregnancyDecisionOperation = null;
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
            // Gate selection, the session-task field and the result install run on
            // the Bannerlord thread, fenced to this screen's original load. A
            // same-load close still joins an in-flight start; it never reopens one.
            string known = string.Empty;
            Task<ConversationSessionStart> pending = await ReignMainThread.InvokeAsync(() =>
            {
                if (!_loadFence.IsCurrent()) return null;
                if (!string.IsNullOrWhiteSpace(_conversationSessionId)) { known = _conversationSessionId; return null; }
                // After close, never reopen a failed start: reuse only the existing attempt.
                Task<ConversationSessionStart> attempt = _conversationStartGate.EnsureAsync(
                    StartConversationAsync, !_conversationFinishRequested);
                _conversationSessionTask = attempt;
                return attempt;
            }).ConfigureAwait(false);
            if (pending == null) return known;
            ConversationSessionStart result = await pending.ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                // A success that arrives after a reload is never installed.
                if (!_loadFence.IsCurrent()) return string.Empty;
                // A stale failed attempt can never erase a session confirmed meanwhile.
                if (result.Ok && string.IsNullOrWhiteSpace(_conversationSessionId)) _conversationSessionId = result.SessionId;
                return result.Ok ? result.SessionId : _conversationSessionId ?? string.Empty;
            }).ConfigureAwait(false);
        }

        private async Task<ConversationSessionStart> StartConversationAsync()
        {
            var owner = ReignServices.IndividualConversation;
            if (owner != null && !await owner.WaitForRecoveryAsync().ConfigureAwait(false))
                return new ConversationSessionStart { Error = "Conversation recovery has not completed." };
            // The fence is rechecked after the recovery await, inside the
            // game-thread callback that captures the native start payload.
            return await ReignServerClient.StartConversationSessionWithIdentityAsync(_hero, _conversationContext,
                _loadFence.IsCurrent).ConfigureAwait(false);
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
            // Payload capture and close-work apply are each fenced to the original load.
            return await ReignServerClient.FinishConversationSessionAsync(_hero, sessionId, reason, _conversationContext,
                _loadFence.IsCurrent).ConfigureAwait(false);
        }

        private async Task LoadHistoryAsync(Func<bool> requestOwner = null)
        {
            ReignServerClient.RequestFence fence = null;
            Func<bool> historyCurrent = () => !_isFinalized && fence != null && fence.IsCurrent()
                && (requestOwner == null || requestOwner());
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_isFinalized || (requestOwner != null && !requestOwner()))
                {
                    return false;
                }

                fence = ReignServerClient.RequestFence.Capture();
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
                List<ReignDialogueLine> history = await ReignServerClient.GetDialogueHistoryAsync(_hero, 80, _conversationContext, historyCurrent).ConfigureAwait(false);
                JArray recoverable = _isOfficialAmbassadorMode ? new JArray() : ReignConversationInputJournal.ReadRecoverable(
                    _conversationContext.Value<string>("campaignId"), _conversationContext.Value<string>("timelineId"),
                    _conversationContext.Value<string>("playerId"), _hero?.StringId ?? "");
                timer.Stop();

                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!historyCurrent())
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
                            AddChatLine(line.DisplaySpeaker, line.Text, line.Role, line.ExchangeId);
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
                    if (historyCurrent())
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
                await LoadHistoryAsync().ConfigureAwait(false);
                // Accepted invitations already represent the player's decision to hear the NPC.
                if (HasAcceptedNpcInvitation && !_isFinalized)
                    await SendTextAsync(string.Empty, false).ConfigureAwait(false);
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
                    RefreshInputAvailability();
                }).ConfigureAwait(false);
            }
        }

        private bool HasAcceptedNpcInvitation => _conversationContext.Value<bool?>("approachOpening") == true;

        private bool CanStartLifeOpening
        {
            get
            {
                string mode = _conversationContext.Value<string>("conversationMode") ?? "";
                return _initializePresentation && !_firstMoveChosen && !_calibrationMode && !_isOfficialAmbassadorMode
                    && !HasAcceptedNpcInvitation && _conversationContext.Value<bool?>("rescueOpening") != true
                    && (mode.Length == 0 || mode == "in_person" || mode == "npc_story")
                    && string.IsNullOrEmpty(_conversationContext.Value<string>("continuationReason"))
                    && !ReignRecoveredDraftReview.HasPendingInSession(_recoverableInputs, _conversationContext.Value<string>("sessionId"));
            }
        }

        private bool CanOfferApproach => InputEnabled && CanStartLifeOpening
            && ReignNpcLivesClient.NpcLivesOptions.Allows("approach");

        private async Task PrepareLifeOpeningAsync(string session, Func<bool> requestOwner)
        {
            try
            {
                await ReignMainThread.InvokeAsync(() => requestOwner()
                    ? ReignServices.NpcLives?.PrepareLifeConversationAsync(_hero, session, _conversationContext) ?? Task.CompletedTask
                    : Task.CompletedTask).Unwrap().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("NPC life conversation context unavailable: " + ex.Message);
            }
        }

        private async Task RequestLifeOpeningAsync()
        {
            Func<bool> current = () => !_isFinalized && !_conversationFinishRequested && _loadFence.IsCurrent();
            if (!current()) return;
            IsBusy = true;
            BusyText = "Approaching...";
            string error = null;
            try
            {
                string session = await EnsureConversationSessionAsync().ConfigureAwait(false);
                if (session.Length == 0)
                    throw new InvalidOperationException("The conversation could not reconnect. Close and reopen chat to try again.");

                await PrepareLifeOpeningAsync(session, current).ConfigureAwait(false);
                var response = await ReignNpcLivesClient.OpenNpcLifeApproachAsync(_hero, session, _conversationContext,
                    requestOwner: current).ConfigureAwait(false);
                string status = response.Value<string>("status");
                if (response.Value<bool?>("ok") != true || (status != "opened" && status != "resume" && status != "disabled"))
                {
                    error = FriendlyServerError(response.Value<string>("error"));
                    return;
                }

                bool reload = await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return false;
                    _firstMoveChosen = true;
                    return status != "disabled";
                }).ConfigureAwait(false);
                if (reload) await LoadHistoryAsync(current).ConfigureAwait(false);
            }
            catch (ReignRequestOwnerAbandonedException) { }
            catch (Exception ex)
            {
                ReignLog.Warn("NPC Approach unavailable: " + ex.Message);
                error = FriendlyServerError(ex.Message);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return;
                    IsBusy = false;
                    BusyText = error ?? "";
                    if (error != null) AddSystemLine(error + " Select Approach to try again, or write a message.");
                }).ConfigureAwait(false);
            }
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

        private void AddChatLine(string speaker, string text, string role = "player", string exchangeId = "")
        {
            ChatLines.Add(new ReignChatLineVM(speaker, text, role) { ExchangeId = exchangeId });
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
    }
}
