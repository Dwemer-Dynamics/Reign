using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtNobleMatterScreenVM : ViewModel
    {
        private const int MaxActiveParticipants = 4;
        private const int MaxCourtAttendees = 25;

        private readonly ReignCourtCampaignBehavior _court;
        private readonly ReignNobleDocketMatter _matter;
        private readonly Action _close;
        private readonly List<Hero> _audienceHeroes = new List<Hero>();
        private readonly ReignCourtCardVM _leadCard;
        private readonly ReignCourtAudienceScenePresentation _scene;
        private string _eventImageId;
        private string _statusText;
        private string _inputText = string.Empty;
        private string _petitionerSpeech;
        private string _selectedSuspectHeroId = string.Empty;
        private string _negotiatedTermsJson = string.Empty;
        private int _acceptanceTier;
        private bool _busy;
        private bool _decisionComplete;
        private int _chatScrollVersion;
        private int _conversationSendRevision;
        private int _completedConversationRevision;
        private int _conversationExpectedReplyCount;
        private int _conversationCompletedReplyCount;
        private readonly Dictionary<string, int> _providerExpectedReplies =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _providerCompletedReplies =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _fallbackCompletedReplies =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public ReignCourtNobleMatterScreenVM(ReignCourtCampaignBehavior court,
            ReignNobleDocketMatter matter, Action close)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _matter = matter ?? throw new ArgumentNullException(nameof(matter));
            _close = close;
            _matter.Normalize();
            Transcript = new MBBindingList<ReignChatLineVM>();
            ActiveParticipants = new MBBindingList<ReignSocialEventAttendeeVM>();
            AvailableAttendees = new MBBindingList<ReignSocialEventAttendeeVM>();
            Hero lead = FindHero(_matter.Participants.FirstOrDefault()?.HeroId);
            string visiblePremise = VisibleMatterPremise();
            _leadCard = new ReignCourtCardVM(_matter.MatterId, "court",
                lead?.Name?.ToString() ?? _matter.Title, visiblePremise,
                _matter.Severity.ToString(), string.Empty, null, _matter, lead);
            _audienceHeroes.AddRange(BuildCourtAudience());
            EnsureParticipantsActive();
            RefreshAudienceLists();
            RestoreConversation();
            EventImageId = ReignEventArtTextureFactory.BuildCourtPetitionReferenceImageId(
                FindCourtCultureId(lead));
            _scene = ReignRulerPetitionSceneClient.Create(court, matter, VisibleMatterPremise,
                imageId => EventImageId = imageId);
            _scene.Refresh();
            PetitionerSpeech = _matter.IsMurder
                ? "A breathless messenger has brought word that " + MurderVictimName()
                    + " was murdered. Those named in the first accounts have been summoned before the throne."
                : _matter.Participants.FirstOrDefault()?.HeroName + " awaits leave to state the matter.";
            StatusText = _matter.DemandsRevealed
                ? "Both positions are now before the throne. Judgment is available."
                : "Hear every principal before giving judgment.";
            if (_matter.ConversationLines.Count == 0)
            {
                AddTranscriptLine("Court Clerk", _matter.IsMurder
                    ? "News has only just arrived: " + visiblePremise
                    : visiblePremise, "system");
                _ = RequestReactionAsync("opening", string.Empty, string.Empty);
            }
        }

        [DataSourceProperty] public string Title => "NOBLE PETITION";
        [DataSourceProperty] public string PetitionerName => _matter.Title;
        [DataSourceProperty] public string PetitionerRole => _matter.Category.ToString() + " matter";
        [DataSourceProperty] public string SeverityText => _matter.Severity.ToString().ToUpperInvariant() + " MATTER";
        [DataSourceProperty] public string ProblemText => VisibleMatterPremise();
        [DataSourceProperty] public string RequestTypeText => _matter.Category.ToString().ToUpperInvariant();
        [DataSourceProperty] public string RequestedResourceText => _matter.DemandsRevealed
            ? "A: " + _matter.DemandA + "\nB: " + _matter.DemandB
                + (string.IsNullOrWhiteSpace(_negotiatedTermsJson)
                    ? string.Empty : "\nImmediate terms: " + _negotiatedTermsJson)
            : "Hear both demands";
        [DataSourceProperty] public string PurposeText => _matter.IsMurder ? "Determine guilt" : "Rule between the parties";
        [DataSourceProperty] public string NeedFacts => _matter.IsMurder
            ? RevealedEvidenceSummary()
            : _matter.Participants.Count(x => x.IsPrincipal).ToString(CultureInfo.InvariantCulture) + " principals • "
                + _matter.Participants.Count.ToString(CultureInfo.InvariantCulture) + " involved nobles";
        [DataSourceProperty] public string RequestTerms => RequestedResourceText;
        [DataSourceProperty] public string BenefitText => ConsequenceSummary();
        [DataSourceProperty] public string DangerText => _matter.IsMurder
            ? "A conviction places the selected suspect in protected capital custody. Execution is a separate irreversible act."
            : string.Empty;
        [DataSourceProperty] public string WarningText => !_matter.DemandsRevealed
            ? "Judgment remains locked until both sides have stated their demands."
            : _matter.IsMurder && string.IsNullOrWhiteSpace(_selectedSuspectHeroId)
                ? "Select the noble you find guilty before convicting."
                : string.Empty;
        [DataSourceProperty] public string EventImageId
        { get => _eventImageId; private set { _eventImageId = value; OnPropertyChangedWithValue(value); } }
        [DataSourceProperty] public string PortraitCacheKey => _leadCard?.PortraitCacheKey ?? string.Empty;
        [DataSourceProperty] public string PortraitId => _leadCard?.PortraitId ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _leadCard?.PortraitAdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _leadCard?.PortraitTextureProviderName ?? string.Empty;
        [DataSourceProperty] public bool HasPortrait => _leadCard?.HasPortrait == true;
        [DataSourceProperty] public MBBindingList<ReignChatLineVM> Transcript { get; }
        [DataSourceProperty] public MBBindingList<ReignSocialEventAttendeeVM> ActiveParticipants { get; }
        [DataSourceProperty] public MBBindingList<ReignSocialEventAttendeeVM> AvailableAttendees { get; }
        [DataSourceProperty] public bool DirectGrantVisible => true;
        [DataSourceProperty] public bool GoldGrantVisible => true;
        [DataSourceProperty] public bool CanGrantDirect => CanJudge
            && (!_matter.IsMurder || !string.IsNullOrWhiteSpace(_selectedSuspectHeroId));
        [DataSourceProperty] public bool CanGrantWithGold => CanJudge;
        [DataSourceProperty] public bool CanRefuse => CanJudge && ChancellorAvailable();
        [DataSourceProperty] public string DirectGrantLabel => _matter.IsMurder
            ? "CONVICT SELECTED"
            : "SIDE WITH " + PrincipalName("principal_a") + " • " + ConsequenceNumbers();
        [DataSourceProperty] public string GoldGrantLabel => _matter.IsMurder
            ? "ACQUIT ALL"
            : "SIDE WITH " + PrincipalName("principal_b") + " • " + ConsequenceNumbers();
        [DataSourceProperty] public string RefuseLabel => "DEFER TO CHANCELLOR";
        [DataSourceProperty] public bool CanSend => !Busy && !DecisionComplete && !string.IsNullOrWhiteSpace(InputText);
        [DataSourceProperty] public bool ConversationEnabled => !Busy && !DecisionComplete;
        [DataSourceProperty] public bool DecisionControlsVisible => !DecisionComplete;
        [DataSourceProperty] public bool PostponeVisible => !DecisionComplete;
        [DataSourceProperty] public bool CanPostpone => PostponeVisible && !Busy;
        [DataSourceProperty] public bool CanContinue => DecisionComplete && !Busy;
        [DataSourceProperty] public bool DecisionComplete
        {
            get { return _decisionComplete; }
            private set
            {
                if (_decisionComplete == value) return;
                _decisionComplete = value;
                OnPropertyChangedWithValue(value);
                NotifyDecisionState();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(ConversationEnabled));
                OnPropertyChanged(nameof(DecisionControlsVisible));
                OnPropertyChanged(nameof(PostponeVisible));
            }
        }
        [DataSourceProperty] public bool Busy
        {
            get { return _busy; }
            private set
            {
                if (_busy == value) return;
                _busy = value;
                OnPropertyChangedWithValue(value);
                NotifyDecisionState();
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(ConversationEnabled));
                OnPropertyChanged(nameof(CanPostpone));
            }
        }
        [DataSourceProperty] public string StatusText
        {
            get { return _statusText; }
            private set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } }
        }
        [DataSourceProperty] public string PetitionerSpeech
        {
            get { return _petitionerSpeech; }
            private set { if (_petitionerSpeech != value) { _petitionerSpeech = value; OnPropertyChangedWithValue(value); } }
        }
        [DataSourceProperty] public string InputText
        {
            get { return _inputText; }
            set
            {
                string next = value ?? string.Empty;
                if (_inputText == next) return;
                _inputText = next;
                OnPropertyChangedWithValue(next);
                OnPropertyChanged(nameof(CanSend));
            }
        }
        [DataSourceProperty] public int ChatScrollVersion
        {
            get { return _chatScrollVersion; }
            private set { if (_chatScrollVersion != value) { _chatScrollVersion = value; OnPropertyChangedWithValue(value); } }
        }

        private bool CanJudge => !DecisionComplete && !Busy && _matter.State == ReignNobleMatterState.Active
            && _matter.DemandsRevealed;
        internal bool ScenePreparationComplete => _scene.Complete;
        internal bool SceneReady => _scene.Ready;
        internal string ScenePreparationError => _scene.Error;
        internal string ScenePreparationStatus => _scene.Status;
        internal JObject ScenePreparationEvidence => _scene.Evidence;
        public override void OnFinalize() { _scene.Close(); base.OnFinalize(); }
        internal string AutomationTranscript => string.Join("\n", Transcript.Select(x => x.Speaker + ": " + x.Text));
        internal int AutomationExpectedReplyCount => Math.Max(1, _matter.Participants.Count);
        internal bool AutomationNaturalConversationComplete => _conversationSendRevision > 0
            && _completedConversationRevision == _conversationSendRevision
            && _conversationExpectedReplyCount > 0
            && _conversationCompletedReplyCount >= _conversationExpectedReplyCount;
        internal int AutomationPersistedConversationLineCount => _matter.ConversationLines.Count;
        internal bool AutomationHasPersistedPlayerLine(string rulerText)
        {
            string expected = (rulerText ?? string.Empty).Trim();
            return expected.Length > 0 && _matter.ConversationLines.Any(x =>
                string.Equals(x.Role, "player", StringComparison.OrdinalIgnoreCase)
                && string.Equals((x.Text ?? string.Empty).Trim(), expected, StringComparison.Ordinal));
        }
        internal bool AutomationProviderBackedPhaseComplete(string phase)
        {
            string key = (phase ?? string.Empty).Trim();
            return _providerExpectedReplies.TryGetValue(key, out int expected) && expected > 0
                && _providerCompletedReplies.TryGetValue(key, out int completed) && completed >= expected
                && (!_fallbackCompletedReplies.TryGetValue(key, out int fallbacks) || fallbacks == 0);
        }
        internal string AutomationProviderBackedStatus => string.Join(";",
            _providerExpectedReplies.Keys.OrderBy(x => x).Select(x => x
                + "=" + (_providerCompletedReplies.TryGetValue(x, out int completed) ? completed : 0)
                + "/" + _providerExpectedReplies[x]
                + ",fallback=" + (_fallbackCompletedReplies.TryGetValue(x, out int fallbacks) ? fallbacks : 0)));
        internal string AutomationNaturalConversationStatus =>
            "sendRevision=" + _conversationSendRevision
            + ";completedRevision=" + _completedConversationRevision
            + ";completedReplies=" + _conversationCompletedReplyCount
            + ";expectedReplies=" + _conversationExpectedReplyCount
            + ";busy=" + Busy
            + ";canJudge=" + CanJudge;

        public void OnFrameTick(float dt) { }
        public void ExecuteGrantDirect() { Decide(_matter.IsMurder ? ReignNobleRuling.ConvictParticipant : ReignNobleRuling.SideA); }
        public void ExecuteGrantWithGold() { Decide(_matter.IsMurder ? ReignNobleRuling.AcquitAll : ReignNobleRuling.SideB); }
        public void ExecuteRefuse() { Decide(ReignNobleRuling.DeferToChancellor); }
        public void ExecutePostpone() { if (CanPostpone) _close?.Invoke(); }
        public void ExecuteContinue() { if (CanContinue) _close?.Invoke(); }
        public void ExecuteSend()
        {
            string text = (InputText ?? string.Empty).Trim();
            if (!CanSend || text.Length == 0) return;
            InputText = string.Empty;
            int conversationRevision = ++_conversationSendRevision;
            _completedConversationRevision = 0;
            _conversationCompletedReplyCount = 0;
            _conversationExpectedReplyCount = Math.Max(1, ActiveAudienceHeroes().Count());
            AddTranscriptLine("You", text, "player");
            _ = RequestReactionAsync("conversation", string.Empty, text, conversationRevision);
        }

        internal bool TryAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            string suppliedAction = (action ?? string.Empty).Trim();
            string normalized = suppliedAction.ToLowerInvariant().Replace('_', '-');
            if (normalized == "send")
            {
                if (!CanSend && string.IsNullOrWhiteSpace(value)) { error = "A ruler message is required."; return false; }
                InputText = value; ExecuteSend(); return true;
            }
            if (normalized.StartsWith("convict:", StringComparison.Ordinal))
            {
                int separator = suppliedAction.IndexOf(':');
                _selectedSuspectHeroId = separator >= 0
                    ? suppliedAction.Substring(separator + 1).Trim() : string.Empty;
                Decide(ReignNobleRuling.ConvictParticipant);
                if (!DecisionComplete) error = StatusText;
                return DecisionComplete;
            }
            switch (normalized)
            {
                case "side-a": ExecuteGrantDirect(); break;
                case "side-b": ExecuteGrantWithGold(); break;
                case "acquit-all": Decide(ReignNobleRuling.AcquitAll); break;
                case "defer": ExecuteRefuse(); break;
                case "continue": ExecuteContinue(); break;
                case "postpone": ExecutePostpone(); break;
                default: error = "Supported noble actions are send, side-a, side-b, convict:<heroId>, acquit-all, defer, continue, and postpone."; return false;
            }
            return DecisionComplete || !ReignCourtPetitionScreenManager.IsOpen;
        }

        public void RegisterTechnicalFailure(string message)
        {
            Busy = false;
            StatusText = string.IsNullOrWhiteSpace(message)
                ? "A technical failure interrupted the hearing. No judgment was applied." : message;
        }

        private void Decide(ReignNobleRuling ruling)
        {
            if (!CanJudge) return;
            Busy = true;
            if (!_court.TryRuleNobleMatter(_matter.MatterId, ruling,
                _selectedSuspectHeroId, _acceptanceTier, _negotiatedTermsJson, out string receipt))
            {
                Busy = false;
                StatusText = receipt;
                return;
            }
            StatusText = receipt;
            DecisionComplete = true;
            Busy = false;
            _ = RequestReactionAsync("closing", _matter.DecisionSummary, string.Empty);
        }

        private async Task RequestReactionAsync(string phase, string outcome, string playerText,
            int conversationRevision = 0)
        {
            Busy = true;
            bool reactionCompleted = false;
            try
            {
                List<Hero> speakers = ActiveAudienceHeroes().ToList();
                if (speakers.Count == 0) throw new InvalidOperationException("No active noble participants remain available.");
                await ReignMainThread.InvokeAsync(() =>
                {
                    _providerExpectedReplies[phase] = speakers.Count;
                    _providerCompletedReplies[phase] = 0;
                    _fallbackCompletedReplies[phase] = 0;
                }).ConfigureAwait(false);
                string turn = phase == "opening"
                    ? "State your knowledge, position, and demand before your ruler."
                    : phase == "closing" ? "The ruler has pronounced judgment: "
                        + CompleteSentence(outcome) + " React to it."
                    : playerText;
                foreach (Hero speaker in speakers)
                {
                    ReignNobleMatterParticipant participant = _matter.Participants.FirstOrDefault(x => x.HeroId == speaker.StringId);
                    JObject context = await ReignMainThread.InvokeAsync(() => BuildConversationContext(
                        phase, outcome, speaker, participant, speakers)).ConfigureAwait(false);
                    ReignDialogueReply response = await ReignServerClient.RequestDialogueResponseAsync(
                        speaker, turn, string.Empty, string.Empty,
                        "noble_docket_" + _matter.MatterId + "_" + speaker.StringId + "_" + Guid.NewGuid().ToString("N"),
                        applyNativeEncyclopediaText: false, conversationContext: context).ConfigureAwait(false);
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        bool providerBacked = response.Ok && !string.IsNullOrWhiteSpace(response.Text);
                        string reply = providerBacked
                            ? ReignRulerDocketRules.SanitizeVisibleNobleReply(
                                response.Text, ResolveCourtLocationName())
                            : FallbackStatement(participant, phase);
                        AddTranscriptLine(speaker.Name?.ToString() ?? "Noble", reply, "npc");
                        PetitionerSpeech = reply;
                        ApplyStructuredTurn(response.RawResponse);
                        if (providerBacked)
                            _providerCompletedReplies[phase] = _providerCompletedReplies[phase] + 1;
                        else
                            _fallbackCompletedReplies[phase] = _fallbackCompletedReplies[phase] + 1;
                        if (phase == "opening" && participant != null)
                            _court.RevealNobleDemand(_matter.MatterId, participant.Role);
                        if (phase == "conversation" && conversationRevision == _conversationSendRevision)
                        {
                            _conversationCompletedReplyCount++;
                            if (_conversationCompletedReplyCount >= _conversationExpectedReplyCount)
                                _completedConversationRevision = conversationRevision;
                        }
                    }).ConfigureAwait(false);
                }
                await ReignMainThread.InvokeAsync(RefreshMatterState).ConfigureAwait(false);
                reactionCompleted = true;
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Noble docket " + phase + " reaction failed: " + ex.Message);
                await ReignMainThread.InvokeAsync(() => AddTranscriptLine("Court Clerk",
                    "The audience response was interrupted. No judgment has been applied.", "system")).ConfigureAwait(false);
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!reactionCompleted && phase == "conversation"
                        && conversationRevision == _conversationSendRevision)
                        _completedConversationRevision = 0;
                    Busy = false;
                }).ConfigureAwait(false);
            }
        }

        private JObject BuildConversationContext(string phase, string outcome, Hero speaker,
            ReignNobleMatterParticipant participant, IReadOnlyList<Hero> activeHeroes)
        {
            string transcript = string.Join("\n", _matter.ConversationLines.Select(x => x.Speaker + ": " + x.Text));
            string ownDemand = participant?.Role == "principal_a" || participant?.Role == "accuser"
                ? _matter.DemandA : participant?.Role == "principal_b" || participant?.Role == "formally_accused"
                    ? _matter.DemandB : string.Empty;
            JArray revealedEvidence = new JArray(_matter.Evidence.Where(x => x.Revealed).Select(x => new JObject
            {
                ["evidenceId"] = x.EvidenceId, ["description"] = x.PublicDescription,
                ["sourceHeroId"] = x.SourceHeroId
            }));
            JArray discoverableEvidence = new JArray(_matter.Evidence.Where(x => !x.Revealed
                && string.Equals(x.SourceHeroId, speaker.StringId, StringComparison.OrdinalIgnoreCase))
                .Select(x => new JObject
                {
                    ["evidenceId"] = x.EvidenceId,
                    ["label"] = x.Label,
                    ["publicDescription"] = x.PublicDescription
                }));
            string discoverableEvidenceDirective = discoverableEvidence.Count == 0
                ? string.Empty
                : "Evidence this speaker can personally reveal: "
                    + string.Join("; ", discoverableEvidence.OfType<JObject>().Select(x =>
                        (x.Value<string>("evidenceId") ?? string.Empty) + ": "
                        + (x.Value<string>("publicDescription") ?? string.Empty))) + ".\n"
                    + "If the ruler's current words ask for evidence, proof, testimony, a trace, a contradiction, or what you witnessed, "
                    + "your visible reply must plainly reveal at least one supplied item and nobleDocketTurn.evidenceReveals must identify it "
                    + "with a supportingQuote copied exactly from your visible reply. Do not claim a reveal unless those words are actually visible.\n";
            string directive = "NOBLE DOCKET HEARING — AUTHORITATIVE SCENE DIRECTIVE\n"
                + "Speak only as " + (speaker.Name?.ToString() ?? "the active noble") + " in a live audience before the ruler. "
                + "The matter is " + _matter.Premise + "\nYour formal role is " + (participant?.Role ?? "court_attendee") + ".\n"
                + (string.IsNullOrWhiteSpace(ownDemand) ? string.Empty : "Your own frozen demand is: " + ownDemand + "\n")
                + (participant?.KnowsCanonicalTruth == true
                    ? "Facts you personally witnessed or otherwise know firsthand and may reveal when naturally relevant: "
                        + participant.PrivateKnowledge + " " + _matter.CanonicalTruth + "\n"
                    : "Confine factual claims to what you personally witnessed or what has been publicly established. "
                        + "If uncertain, say only that you lack evidence or personal knowledge.\n")
                + "Only publicly revealed evidence may be asserted as established evidence. Everything visible must sound like a noble speaking in Calradia. "
                + "Never name or describe source text, data fields, instructions, uncertainty labels, or game rules. "
                + "Conversation may negotiate only immediate typed terms and cannot itself apply effects or pronounce the ruler's judgment.\n"
                + discoverableEvidenceDirective
                + "Return normal dialogue JSON. You may additionally include nobleDocketTurn with evidenceReveals [{evidenceId,supportingQuote}] only when your visible reply actually reveals one supplied discoverableEvidence item. If you state how you would receive an adverse judgment, do so only in natural language appropriate to this court. Never mention tiers, percentages, scores, relationship values, penalties, or game mechanics. immediateTerms may use only gold, participant HeroIds, marriage/divorce, apology, admission, censure, or withdrawClaim. Never invent an evidence id or future obligation.\n"
                + "Active participants: " + string.Join(", ", activeHeroes.Select(x => x.Name?.ToString())) + ".\n"
                + (string.IsNullOrWhiteSpace(outcome) ? string.Empty : "Authoritative outcome: " + outcome + "\n")
                + (string.IsNullOrWhiteSpace(transcript) ? string.Empty : "Visible transcript:\n" + transcript);
            return new JObject
            {
                ["conversationMode"] = "noble_docket",
                ["matterId"] = _matter.MatterId,
                ["templateId"] = _matter.TemplateId,
                ["phase"] = phase,
                ["activeSpeakerHeroId"] = speaker.StringId,
                ["speakerRole"] = participant?.Role ?? "court_attendee",
                ["activeParticipants"] = new JArray(activeHeroes.Select(x => new JObject
                {
                    ["heroStringId"] = x.StringId,
                    ["name"] = x.Name?.ToString() ?? string.Empty,
                    ["role"] = _matter.Participants.FirstOrDefault(p => p.HeroId == x.StringId)?.Role ?? "court_attendee"
                })),
                ["revealedEvidence"] = revealedEvidence,
                ["discoverableEvidence"] = discoverableEvidence,
                ["audienceTranscript"] = transcript,
                ["sceneContextDirective"] = directive,
                ["suppressRelationshipAssessment"] = phase != "conversation"
            };
        }

        private void ApplyStructuredTurn(JObject response)
        {
            JObject turn = response?["nobleDocketTurn"] as JObject;
            if (turn == null || turn.Value<bool?>("accepted") != true) return;
            foreach (string evidenceId in (turn["revealedEvidenceIds"] as JArray
                         ?? new JArray()).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x)))
                _court.RevealNobleEvidence(_matter.MatterId, evidenceId);
            _acceptanceTier = Math.Max(_acceptanceTier,
                Math.Max(0, Math.Min(3, turn.Value<int?>("acceptanceTier") ?? 0)));
            JObject terms = turn["immediateTerms"] as JObject;
            if (terms != null && terms.Properties().Any())
            {
                _negotiatedTermsJson = terms.ToString(Newtonsoft.Json.Formatting.None);
                StatusText = "Immediate negotiated terms are visible in the case summary and will apply only with your judgment.";
                OnPropertyChanged(nameof(RequestedResourceText));
                OnPropertyChanged(nameof(RequestTerms));
            }
        }

        private string FallbackStatement(ReignNobleMatterParticipant participant, string phase)
        {
            if (phase == "closing") return "I have heard the ruler's judgment.";
            if (participant?.Role == "principal_a" || participant?.Role == "accuser") return _matter.DemandA;
            if (participant?.Role == "principal_b" || participant?.Role == "formally_accused") return _matter.DemandB;
            return "I will answer only to what I witnessed and what has been placed before the court.";
        }

        private string ResolveCourtLocationName()
        {
            Settlement court = Settlement.Find(_court.Session?.HostSettlementStringId ?? string.Empty)
                ?? Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            return court?.Name?.ToString() ?? string.Empty;
        }

        private string MurderVictimName()
        {
            Hero victim = string.IsNullOrWhiteSpace(_matter.VictimHeroId) ? null : Hero.FindFirst(x =>
                x != null && string.Equals(x.StringId, _matter.VictimHeroId, StringComparison.OrdinalIgnoreCase));
            return victim?.Name?.ToString() ?? "a protected noble";
        }

        private string VisibleMatterPremise()
        {
            if (!_matter.IsMurder) return _matter.Premise;
            return MurderVictimName()
                + " has been murdered. The first reports name several nobles whose testimony and evidence must be tested before judgment.";
        }

        private static string CompleteSentence(string text)
        {
            string value = (text ?? string.Empty).Trim();
            if (value.Length == 0) return string.Empty;
            char last = value[value.Length - 1];
            return last == '.' || last == '!' || last == '?' ? value : value + ".";
        }

        private void AddTranscriptLine(string speaker, string text, string role)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            ReignDocketConversationLine line = new ReignDocketConversationLine
            { Speaker = speaker ?? string.Empty, Text = text.Trim(), Role = role ?? string.Empty };
            Transcript.Add(new ReignChatLineVM(line.Speaker, line.Text, line.Role));
            _matter.ConversationLines.Add(line);
            while (_matter.ConversationLines.Count > 60) _matter.ConversationLines.RemoveAt(0);
            ChatScrollVersion++;
        }

        private void RestoreConversation()
        {
            foreach (ReignDocketConversationLine line in _matter.ConversationLines)
            {
                Transcript.Add(new ReignChatLineVM(line.Speaker, line.Text, line.Role));
                ChatScrollVersion++;
            }
        }

        private void SelectOrToggleAttendee(ReignSocialEventAttendeeVM attendee)
        {
            Hero hero = attendee?.Hero;
            if (hero == null || Busy || DecisionComplete) return;
            bool involved = _matter.Participants.Any(x => x.HeroId == hero.StringId);
            if (_matter.IsMurder && involved)
            {
                _selectedSuspectHeroId = hero.StringId;
                StatusText = hero.Name + " is selected for possible conviction.";
                NotifyDecisionState();
                return;
            }
            if (involved) return;
            int index = _matter.ActiveAudienceHeroIds.FindIndex(x => x == hero.StringId);
            if (index >= 0) _matter.ActiveAudienceHeroIds.RemoveAt(index);
            else if (_matter.ActiveAudienceHeroIds.Count < MaxActiveParticipants) _matter.ActiveAudienceHeroIds.Add(hero.StringId);
            else { StatusText = "The active court conversation is limited to four people."; return; }
            RefreshAudienceLists();
            _scene.Refresh();
        }

        private void EnsureParticipantsActive()
        {
            HashSet<string> available = new HashSet<string>(_audienceHeroes.Select(x => x.StringId), StringComparer.OrdinalIgnoreCase);
            _matter.ActiveAudienceHeroIds = _matter.Participants.Select(x => x.HeroId)
                .Where(available.Contains).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxActiveParticipants).ToList();
        }

        private void RefreshAudienceLists()
        {
            ActiveParticipants.Clear(); AvailableAttendees.Clear();
            HashSet<string> active = new HashSet<string>(_matter.ActiveAudienceHeroIds, StringComparer.OrdinalIgnoreCase);
            foreach (Hero hero in _audienceHeroes)
            {
                ReignSocialEventAttendeeVM vm = new ReignSocialEventAttendeeVM(hero,
                    active.Contains(hero.StringId), SelectOrToggleAttendee, null, null, null);
                if (vm.IsActive) ActiveParticipants.Add(vm); else AvailableAttendees.Add(vm);
            }
        }

        private IEnumerable<Hero> ActiveAudienceHeroes()
        {
            HashSet<string> active = new HashSet<string>(_matter.ActiveAudienceHeroIds, StringComparer.OrdinalIgnoreCase);
            return _audienceHeroes.Where(x => active.Contains(x.StringId)).Take(MaxActiveParticipants);
        }

        private IEnumerable<Hero> BuildCourtAudience()
        {
            Settlement court = Settlement.Find(_court.Session?.HostSettlementStringId ?? string.Empty)
                ?? Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement;
            List<Hero> heroes = _matter.Participants.Select(x => FindHero(x.HeroId)).Where(x => x != null).ToList();
            if (court != null)
            {
                heroes.AddRange(court.HeroesWithoutParty.Where(x => IsEligibleCourtAttendee(x, court)));
                heroes.AddRange(court.Notables.Where(x => IsEligibleCourtAttendee(x, court)));
                foreach (MobileParty party in court.Parties.Where(x => x?.CurrentSettlement == court))
                {
                    if (IsEligibleCourtAttendee(party.LeaderHero, court)) heroes.Add(party.LeaderHero);
                    heroes.AddRange(party.MemberRoster.GetTroopRoster().Where(x => x.Number > 0)
                        .Select(x => x.Character?.HeroObject).Where(x => IsEligibleCourtAttendee(x, court)));
                }
            }
            HashSet<string> participantIds = new HashSet<string>(_matter.Participants.Select(x => x.HeroId), StringComparer.OrdinalIgnoreCase);
            return heroes.Where(x => x != null && x != Hero.MainHero)
                .GroupBy(x => x.StringId, StringComparer.OrdinalIgnoreCase).Select(x => x.First())
                .OrderBy(x => participantIds.Contains(x.StringId) ? 0 : 1)
                .ThenByDescending(x => x.IsLord).ThenByDescending(x => x.Clan?.Tier ?? 0)
                .ThenBy(x => x.Name?.ToString()).Take(MaxCourtAttendees);
        }

        private static bool IsEligibleCourtAttendee(Hero hero, Settlement court)
        {
            if (!ReignConversationEligibility.IsAdultLivingNpc(hero) || hero.IsPrisoner || hero.IsWounded || court == null) return false;
            if (hero.PartyBelongedTo != null && hero.PartyBelongedTo.CurrentSettlement != court) return false;
            if (hero.PartyBelongedTo == null && hero.CurrentSettlement != court
                && !court.HeroesWithoutParty.Contains(hero) && !court.Notables.Contains(hero)) return false;
            return hero.IsLord || hero.IsNotable || hero.IsWanderer;
        }

        private void RefreshMatterState()
        {
            StatusText = _matter.DemandsRevealed
                ? "Both positions are now before the throne. Judgment is available."
                : "Hear every principal before giving judgment.";
            OnPropertyChanged(nameof(RequestedResourceText));
            OnPropertyChanged(nameof(RequestTerms));
            OnPropertyChanged(nameof(WarningText));
            OnPropertyChanged(nameof(NeedFacts));
            NotifyDecisionState();
        }

        private void NotifyDecisionState()
        {
            OnPropertyChanged(nameof(CanGrantDirect)); OnPropertyChanged(nameof(CanGrantWithGold));
            OnPropertyChanged(nameof(CanRefuse)); OnPropertyChanged(nameof(CanContinue));
            OnPropertyChanged(nameof(CanPostpone)); OnPropertyChanged(nameof(WarningText));
        }

        private bool ChancellorAvailable()
        {
            ReignChancellorOffice office = _court.Chancellor;
            Hero hero = FindHero(office?.HeroId);
            return office != null && !office.IsVacant && hero != null && hero.IsAlive && !hero.IsPrisoner;
        }

        private string PrincipalName(string role)
        {
            string name = _matter.Participants.FirstOrDefault(x => x.Role == role)?.HeroName ?? "PARTY";
            return name.ToUpperInvariant();
        }

        private string ConsequenceNumbers()
        {
            return ReignRulerDocketRules.NobleWinnerRelation(_matter.Severity).ToString("+0;-0;0", CultureInfo.InvariantCulture)
                + " / " + ReignRulerDocketRules.NobleLoserRelation(_matter.Severity).ToString("+0;-0;0", CultureInfo.InvariantCulture);
        }

        private string ConsequenceSummary()
        {
            if (_matter.IsMurder) return "Conviction, acquittal, or a one-day Chancellor investigation; the persisted truth may differ from the public judgment.";
            return "Winner/loser ruler relation: " + ConsequenceNumbers()
                + ". Applicable clan spillover and existing negative reputation tags follow the committed ruling.";
        }

        private string RevealedEvidenceSummary()
        {
            int revealed = _matter.Evidence.Count(x => x.Revealed);
            return revealed.ToString(CultureInfo.InvariantCulture) + " of "
                + _matter.Evidence.Count.ToString(CultureInfo.InvariantCulture) + " evidence items revealed";
        }

        private string FindCourtCultureId(Hero lead)
        {
            return ReignRulerPetitionSceneClient.Host(_court, _matter)?.Culture?.StringId ?? "generic";
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x =>
                string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
