using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Court;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    // Shares the approved court petition movie, including its four portrait slots,
    // transcript scrolling, typography and decision controls.
    public sealed class ReignCourtLifeScreenVM : ViewModel
    {
        private readonly ReignCourtCampaignBehavior _court;
        private readonly ReignCourtLifeMatter _matter;
        private readonly Action _close;
        private readonly ReignCourtCardVM _lead;
        private readonly ReignCourtAudienceScenePresentation _scene;
        private string _eventImageId;
        private bool _busy, _finalized;
        private string _input = "", _status = "Your guests await an audience.";
        private int _scroll;
        private ReignCourtLifeMatterState _lastState;
        public ReignCourtLifeScreenVM(ReignCourtCampaignBehavior court, ReignCourtLifeMatter matter, Action close)
        {
            _court = court; _matter = matter; _close = close; matter.Normalize();
            RecoverPersistedOpeningReceipts(matter);
            if (matter.Source == ReignDocketSource.Patronage && !matter.EffectsCommitted)
                court.RefreshPatronageOptions(matter);
            Transcript = new MBBindingList<ReignChatLineVM>();
            ActiveParticipants = new MBBindingList<ReignCourtLifeAttendeeVM>();
            AvailableAttendees = new MBBindingList<ReignCourtLifeAttendeeVM>();
            foreach (ReignCourtLifeParticipant person in matter.Participants)
                ActiveParticipants.Add(new ReignCourtLifeAttendeeVM(person, matter.SettlementId));
            Hero hero = FindHero(matter.Participants.FirstOrDefault()?.HeroId);
            _lead = new ReignCourtCardVM(matter.MatterId, "court", matter.Title, matter.Summary, "", "", null, matter, hero);
            // The hall belongs to the audience's host town, regardless of who is visiting.
            Settlement audienceSettlement = Settlement.Find(matter.SettlementId ?? string.Empty);
            EventImageId = ReignEventArtTextureFactory.BuildCourtPetitionReferenceImageId(
                audienceSettlement?.Culture?.StringId ?? "generic");
            _scene = ReignRulerPetitionSceneClient.Create(court, matter, () => matter.Summary,
                imageId => EventImageId = imageId);
            _scene.Refresh();
            foreach (ReignDocketConversationLine line in matter.ConversationLines)
                Transcript.Add(new ReignChatLineVM(line.Speaker, line.Text, line.Role));
            if (matter.ConversationLines.Count == 0) AddLine("Court Clerk", matter.Summary, "system");
            if (!matter.EffectsCommitted && (!string.IsNullOrWhiteSpace(matter.PendingPlayerTurnId) || !HasCompleteOpeningReceipts(matter)))
                _ = ConverseAsync(string.IsNullOrWhiteSpace(matter.PendingPlayerTurnId) ? "opening" : "conversation", matter.PendingPlayerText);
        }

        private static bool HasCompleteOpeningReceipts(ReignCourtLifeMatter matter)
        {
            return matter.Participants.All(person =>
                matter.CompletedReplyKeys.Contains("opening|" + person.ActorId));
        }

        private static void RecoverPersistedOpeningReceipts(ReignCourtLifeMatter matter)
        {
            // Older audiences can already contain their visible opening while lacking
            // the per-speaker receipt introduced for idempotent provider recovery.
            // Treat that durable transcript as authoritative so reopening never rolls
            // another greeting or spends another provider request.
            var usedLines = new HashSet<int>();
            foreach (ReignCourtLifeParticipant person in matter.Participants)
            {
                string key = "opening|" + person.ActorId;
                if (matter.CompletedReplyKeys.Contains(key)) continue;
                int index = matter.ConversationLines.FindIndex(line => line != null
                    && !usedLines.Contains(matter.ConversationLines.IndexOf(line))
                    && string.Equals(line.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(line.Speaker, person.Name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) continue;
                usedLines.Add(index);
                matter.CompletedReplyKeys.Add(key);
                matter.ReplyTextsByKey[key] = matter.ConversationLines[index].Text ?? string.Empty;
            }
        }

        [DataSourceProperty] public string Title => _matter.Source.ToString().ToUpperInvariant() + " AUDIENCE";
        [DataSourceProperty] public string PetitionerName => _matter.Title;
        [DataSourceProperty] public string PetitionerRole => _matter.Source.ToString();
        [DataSourceProperty] public string SeverityText => _matter.Source == ReignDocketSource.International ? _matter.Severity.ToString().ToUpperInvariant() : "AT COURT";
        [DataSourceProperty] public string ProblemText => _matter.Summary;
        [DataSourceProperty] public string RequestTypeText => _matter.Source.ToString().ToUpperInvariant();
        [DataSourceProperty] public string RequestedResourceText => OptionLabel(SelectedOption()) ?? "Speak with your guests or review the available decisions.";
        [DataSourceProperty] public string PurposeText => SelectedOption()?.Description ?? "A personal audience with the ruler";
        [DataSourceProperty] public string NeedFacts => string.Join(" • ", _matter.Participants.Select(x => x.Name));
        [DataSourceProperty] public string RequestTerms => RequestedResourceText;
        [DataSourceProperty] public string BenefitText => PurposeText;
        [DataSourceProperty] public string DangerText => "";
        [DataSourceProperty] public string WarningText => "";
        [DataSourceProperty] public string PetitionerSpeech => _matter.ConversationLines.LastOrDefault(x => x.Role == "assistant")?.Text ?? "";
        [DataSourceProperty] public string EventImageId
        { get => _eventImageId; private set { _eventImageId = value; OnPropertyChangedWithValue(value); } }
        [DataSourceProperty] public string PortraitCacheKey => _lead.PortraitCacheKey;
        [DataSourceProperty] public string PortraitId => _lead.PortraitId;
        [DataSourceProperty] public string PortraitAdditionalArgs => _lead.PortraitAdditionalArgs;
        [DataSourceProperty] public string PortraitTextureProviderName => _lead.PortraitTextureProviderName;
        [DataSourceProperty] public bool HasPortrait => _lead.HasPortrait;
        [DataSourceProperty] public MBBindingList<ReignChatLineVM> Transcript { get; }
        [DataSourceProperty] public MBBindingList<ReignCourtLifeAttendeeVM> ActiveParticipants { get; }
        [DataSourceProperty] public MBBindingList<ReignCourtLifeAttendeeVM> AvailableAttendees { get; }
        [DataSourceProperty] public string InputText { get => _input; set { _input = value ?? ""; OnPropertyChangedWithValue(_input); Notify(); } }
        [DataSourceProperty] public string StatusText { get => _status; private set { _status = value ?? ""; OnPropertyChangedWithValue(_status); } }
        [DataSourceProperty] public bool Busy { get => _busy; private set { _busy = value; OnPropertyChangedWithValue(value); Notify(); } }
        [DataSourceProperty] public bool DecisionComplete => _matter.EffectsCommitted || !_matter.IsPending || _matter.State == ReignCourtLifeMatterState.Deferred;
        [DataSourceProperty] public bool CanSend => ConversationEnabled && !string.IsNullOrWhiteSpace(InputText);
        [DataSourceProperty] public bool ConversationEnabled => !Busy && !DecisionComplete;
        [DataSourceProperty] public bool DecisionControlsVisible => !DecisionComplete;
        [DataSourceProperty] public bool DirectGrantVisible => true;
        [DataSourceProperty] public bool GoldGrantVisible => true;
        [DataSourceProperty] public bool CanGrantDirect => ConversationEnabled && _matter.Options.Count > 0;
        [DataSourceProperty] public bool CanGrantWithGold => ConversationEnabled && SelectedOption() != null;
        [DataSourceProperty] public bool CanRefuse => ConversationEnabled;
        [DataSourceProperty] public string DirectGrantLabel => "REVIEW DECISIONS";
        [DataSourceProperty] public string GoldGrantLabel => OptionLabel(SelectedOption())?.ToUpperInvariant() ?? "CONFIRM";
        [DataSourceProperty] public string RefuseLabel => _matter.Source == ReignDocketSource.International ? "REFUSE REQUEST" : "END AUDIENCE";
        [DataSourceProperty] public bool PostponeVisible => !DecisionComplete;
        [DataSourceProperty] public bool CanPostpone => !Busy && !DecisionComplete;
        [DataSourceProperty] public bool CanContinue => !Busy && DecisionComplete;
        [DataSourceProperty] public int ChatScrollVersion => _scroll;
        public bool ScenePreparationComplete => _scene.Complete;
        public string MatterId => _matter.MatterId;
        public bool SceneReady => _scene.Ready;
        public string ScenePreparationError => _scene.Error;
        public string ScenePreparationStatus => _scene.Status;
        internal JObject ScenePreparationEvidence => _scene.Evidence;
        public string AutomationTranscript => string.Join("\n", _matter.ConversationLines.Select(x => x.Speaker + ": " + x.Text));
        public int AutomationExpectedReplyCount => _matter.Participants.Count;
        public int AutomationPersistedConversationLineCount => _matter.ConversationLines.Count;
        public bool AutomationNaturalConversationComplete => !Busy && string.IsNullOrEmpty(_matter.PendingPlayerTurnId) && _matter.CompletedPlayerTurnIds.Count > 0;
        public string AutomationNaturalConversationStatus => Busy ? "awaiting-visible-replies" : _matter.TechnicalFailure ? "technical-failure" : "ready";
        public string AutomationProviderBackedStatus => AutomationNaturalConversationStatus;
        public bool AutomationHasPersistedPlayerLine(string text) => _matter.ConversationLines.Any(x => x.Role == "user" && x.Text == text);
        public bool AutomationProviderBackedPhaseComplete(string phase) => !Busy && !_matter.TechnicalFailure
            && (phase == "opening" ? _matter.Participants.All(x => _matter.CompletedReplyKeys.Contains("opening|" + x.ActorId)) : AutomationNaturalConversationComplete);

        public void ExecuteSend() { if (CanSend) { string text = InputText; InputText = ""; _ = ConverseAsync("conversation", text); } }
        public void ExecuteGrantDirect()
        {
            if (!CanGrantDirect) return;
            var choices = _matter.Options.Select(x => new InquiryElement(x.OptionId, OptionLabel(x) + " — " + x.Description, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Court decision", "Select terms to review before confirming.",
                choices, true, 1, 1, "Review", "Back", selected => {
                    _matter.SelectedOptionId = selected?.FirstOrDefault()?.Identifier as string ?? ""; Notify();
                }, selected => { }, string.Empty, false), true, false);
        }
        public void ExecuteGrantWithGold() { if (CanGrantWithGold) ApplyOption(_matter.SelectedOptionId); }
        public void ExecuteRefuse()
        {
            if (!CanRefuse) return;
            ReignCourtLifeOption end = _matter.Options.FirstOrDefault(x => x.OptionId == "decline" || x.OptionId == "conclude" || x.OptionId == "dismiss" || x.OptionId == "refuse");
            if (end != null) ApplyOption(end.OptionId); else ExecutePostpone();
        }
        public void ExecuteContinue() { if (CanContinue) _close?.Invoke(); }
        public void ExecutePostpone() { if (CanPostpone) { _court.NotifyCourtLifeChanged(); _close?.Invoke(); } }
        public void OnFrameTick(float dt) { if (_lastState != _matter.State) { _lastState = _matter.State; Notify(); } }
        public override void OnFinalize() { _finalized = true; _scene.Close(); base.OnFinalize(); }
        public void RegisterTechnicalFailure(string text) { _matter.TechnicalFailure = true; StatusText = text; Busy = false; }
        public bool TryAutomationAction(string action, string value, out string error)
        {
            error = "";
            switch ((action ?? "").ToLowerInvariant().Replace('_', '-'))
            {
                case "send": if (!ConversationEnabled || string.IsNullOrWhiteSpace(value)) break; InputText = value; ExecuteSend(); return true;
                case "choose": if (_matter.Options.Any(x => x.OptionId == value)) { _matter.SelectedOptionId = value; Notify(); return true; } break;
                case "confirm": if (CanGrantWithGold) return ApplyOption(_matter.SelectedOptionId); break;
                case "decide": if (ConversationEnabled) return ApplyOption(value); break;
                case "refuse": if (CanRefuse) { ExecuteRefuse(); return true; } break;
                case "continue": if (CanContinue) { ExecuteContinue(); return true; } break;
                case "postpone": case "technical-return": if (CanPostpone) { ExecutePostpone(); return true; } break;
            }
            error = "The requested court-life action is unavailable in the current state."; return false;
        }

        private async Task ConverseAsync(string phase, string text)
        {
            if (Busy || DecisionComplete) return;
            Busy = true; _matter.TechnicalFailure = false;
            _court.BeginCourtLifeConversation();
            try
            {
                await EnsureInternationalSceneAsync().ConfigureAwait(false);
                string turnId = "opening";
                if (phase == "conversation")
                {
                    if (string.IsNullOrWhiteSpace(_matter.PendingPlayerTurnId))
                    {
                        _matter.PendingPlayerTurnId = "court_life_turn_" + Guid.NewGuid().ToString("N");
                        _matter.PendingPlayerText = text;
                        AddLine(Hero.MainHero?.Name?.ToString() ?? "Ruler", text, "user");
                    }
                    turnId = _matter.PendingPlayerTurnId; text = _matter.PendingPlayerText;
                }
                foreach (ReignCourtLifeParticipant person in _matter.Participants)
                {
                    string key = turnId + "|" + person.ActorId;
                    bool needsInterpretation = phase == "conversation" && person.Age >= 18 && _matter.Source != ReignDocketSource.Family;
                    if (_matter.CompletedReplyKeys.Contains(key) && (!needsInterpretation || _matter.CompletedInterpretationKeys.Contains(key))) continue;
                    JObject context = null, thinPayload = null;
                    Hero hero = null;
                    await ReignMainThread.InvokeAsync(() => {
                        if (!_court.OwnsCourtLifeMatter(_matter)) throw new OperationCanceledException("The campaign or audience changed.");
                        hero = FindHero(person.HeroId);
                        if (!string.IsNullOrWhiteSpace(person.HeroId) && (hero == null || !hero.IsAlive || hero.IsPrisoner))
                            throw new InvalidOperationException("This guest is no longer available for the audience.");
                        context = BuildSpeakerContext(person, phase, turnId);
                        if (hero == null || hero.IsChild) thinPayload = BuildThinPayload(person, hero, context, phase, turnId, text);
                    }).ConfigureAwait(false);
                    string visible;
                    if (!_matter.ReplyTextsByKey.TryGetValue(key, out visible))
                    {
                    JObject raw;
                    if (hero != null && !hero.IsChild)
                    {
                        ReignDialogueReply reply = await ReignServerClient.RequestDialogueResponseAsync(hero,
                            phase == "opening" ? "[The court grants you leave to speak.]" : text,
                            _matter.TranscriptId, correlationId: _matter.MatterId + "_" + turnId + "_" + person.ActorId,
                            applyNativeEncyclopediaText: false, conversationContext: context).ConfigureAwait(false);
                        if (!reply.Ok || string.IsNullOrWhiteSpace(reply.Text)) throw new InvalidOperationException(reply.Error ?? "The guest did not reply.");
                        raw = reply.RawResponse; raw["reply"] = reply.Text;
                    }
                    else raw = await ReignServerClient.PostJsonAsync(hero?.IsChild == true ? "/party-chat/respond" : "/court/life/respond", thinPayload).ConfigureAwait(false);
                    visible = raw?.Value<string>("reply") ?? "";
                    if (raw?.Value<bool?>("ok") != true || string.IsNullOrWhiteSpace(visible))
                        throw new InvalidOperationException(raw?.Value<string>("error") ?? "No visible response was received.");
                    visible = NormalizeAssistantReply(visible);
                    if (ReignRulerDocketRules.ContainsForbiddenNobleDocketStatistics(visible))
                        throw new InvalidOperationException("The guest returned an invalid response. The audience can be retried.");
                    await ReignMainThread.InvokeAsync(() => {
                        if (!_court.OwnsCourtLifeMatter(_matter)) throw new OperationCanceledException("The campaign or audience changed.");
                        AddLine(person.Name, visible, "assistant");
                        _matter.CompletedReplyKeys.Add(key); _matter.ReplyTextsByKey[key] = visible; _court.NotifyCourtLifeChanged();
                    }).ConfigureAwait(false);
                    }
                    if (needsInterpretation)
                    {
                        JObject interpretationPayload = null;
                        await ReignMainThread.InvokeAsync(() => {
                            interpretationPayload = _court.BuildCourtLifeApiPayload(_matter);
                            interpretationPayload["playerText"] = text; interpretationPayload["visibleReply"] = visible;
                            interpretationPayload["speakerHeroId"] = person.HeroId;
                            interpretationPayload["speakerRole"] = person.Role;
                            interpretationPayload["transcript"] = JArray.FromObject(_matter.ConversationLines.Select(x => new { speaker = x.Speaker, text = x.Text, role = x.Role }));
                        }).ConfigureAwait(false);
                        JObject interpreted = await ReignServerClient.PostJsonAsync("/court/life/interpret", interpretationPayload).ConfigureAwait(false);
                        await ReignMainThread.InvokeAsync(() => {
                            if (!_court.OwnsCourtLifeMatter(_matter)) throw new OperationCanceledException("The campaign or audience changed.");
                            if (interpreted?.Value<bool?>("ok") != true) throw new InvalidOperationException("The reply was saved, but its proposed terms could not be checked. Reopen the audience to retry.");
                            JObject decision = interpreted["decision"] as JObject;
                            if (decision == null) throw new InvalidOperationException("The reply was saved, but its proposed terms were incomplete.");
                            if (_matter.Source == ReignDocketSource.International && decision["proposedTerms"] is JObject proposed && proposed.HasValues)
                            {
                                if (_court.TryCounterInternationalTerms(_matter, proposed, out string quoteSummary))
                                { _matter.AcceptedDecisionJson = "{}"; _matter.SelectedOptionId = ""; StatusText = quoteSummary; Notify(); }
                                _matter.CompletedInterpretationKeys.Add(key); _court.NotifyCourtLifeChanged(); return;
                            }
                            if (_matter.Source == ReignDocketSource.Patronage && decision["regionalSettlementIds"] is JArray destinations && destinations.Count == 3)
                            {
                                _court.RefreshPatronageOptions(_matter, destinations.Values<string>());
                                _matter.AcceptedDecisionJson = "{}"; _matter.SelectedOptionId = "";
                                StatusText = "The three destinations are ready to review with the commission terms.";
                                _matter.CompletedInterpretationKeys.Add(key); _court.NotifyCourtLifeChanged(); Notify(); return;
                            }
                            if (_matter.Source == ReignDocketSource.Visitor && decision.Value<bool?>("privateMeetingAccepted") == true)
                                _court.RecordNobleVisitorInvitation(_matter.MatterId, person.HeroId, turnId,
                                    decision.Value<string>("meetingWords") ?? "", true);
                            if (decision.Value<bool?>("npcAccepted") == true)
                            {
                                decision = RetainPartyAcceptance(decision, person.HeroId);
                                _matter.AcceptedDecisionJson = decision.ToString();
                            }
                            // Player intent is offered for review. The visible confirmation
                            // keeps negotiations from spending money merely by discussing it.
                            if (decision.Value<bool?>("playerCommitted") == true && _matter.Options.Any(x => x.OptionId == decision.Value<string>("optionId")))
                             { _matter.SelectedOptionId = decision.Value<string>("optionId"); StatusText = "Your decision is ready to confirm: " + SelectedOption()?.Label; Notify(); }
                            _matter.CompletedInterpretationKeys.Add(key); _court.NotifyCourtLifeChanged();
                        }).ConfigureAwait(false);
                    }
                }
                await ReignMainThread.InvokeAsync(() => {
                    if (!_court.OwnsCourtLifeMatter(_matter)) return;
                    if (phase == "conversation" && !_matter.CompletedPlayerTurnIds.Contains(turnId))
                    {
                        _matter.CompletedPlayerTurnIds.Add(turnId);
                        if (_matter.Source == ReignDocketSource.Family) _ = _court.RecordFamilyVisitTurnAsync(_matter, turnId);
                        _matter.PendingPlayerTurnId = ""; _matter.PendingPlayerText = "";
                    }
                    if (string.IsNullOrWhiteSpace(_matter.SelectedOptionId)) StatusText = "Your guests await your response.";
                    _court.NotifyCourtLifeChanged();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => RegisterTechnicalFailure("The audience could not continue: " + ex.Message + " You may return and retry.")).ConfigureAwait(false);
            }
            finally { await ReignMainThread.InvokeAsync(() => { _court.EndCourtLifeConversation(); Busy = false; }).ConfigureAwait(false); }
        }

        private async Task EnsureInternationalSceneAsync()
        {
            JObject request = null;
            await ReignMainThread.InvokeAsync(() => {
                if (!_court.OwnsCourtLifeMatter(_matter)) throw new OperationCanceledException("The campaign or audience changed.");
                JObject data = ReignCourtCampaignBehavior.ParseCourtLifeJson(_matter.PayloadJson);
                // Existing conversations keep their recorded accounts; never rewrite a heard case on load.
                if (_matter.Source != ReignDocketSource.International || data["sharedScene"] is JObject
                    || _matter.CompletedReplyKeys.Count > 0 || _matter.ConversationLines.Any(x => x.Role == "assistant")) return;
                request = _court.BuildCourtLifeApiPayload(_matter);
                request["correlationId"] = _matter.MatterId + "_shared_scene";
                request["templateId"] = _matter.TemplateId;
                request["situationTemplate"] = _matter.Summary;
                request["sceneParticipants"] = JArray.FromObject(_matter.Participants.Select(x => new {
                    actorId = x.ActorId, name = x.Name, role = x.Role }));
                request["sceneContext"] = new JObject();
                foreach (string key in new[] { "foreign", "player", "domesticLordId", "foreignLordId", "privateTruth",
                    "constructive", "extortion", "remedy", "captiveId", "captive", "ransom" })
                    if (data[key] != null) request["sceneContext"][key] = data[key].DeepClone();
            }).ConfigureAwait(false);
            if (request == null) return;
            JObject response = await ReignServerClient.PostJsonAsync("/court/life/scene", request).ConfigureAwait(false);
            if (response?.Value<bool?>("ok") != true || !(response["scene"] is JObject scene)
                || scene.Value<string>("schema") != "reign-court-life-scene-v1")
                throw new InvalidOperationException(response?.Value<string>("error") ?? "The hearing's shared account could not be prepared.");
            await ReignMainThread.InvokeAsync(() => {
                if (!_court.OwnsCourtLifeMatter(_matter)) throw new OperationCanceledException("The campaign or audience changed.");
                JObject data = ReignCourtCampaignBehavior.ParseCourtLifeJson(_matter.PayloadJson);
                if (data["sharedScene"] == null)
                {
                    data["sharedScene"] = scene.DeepClone();
                    _matter.PayloadJson = data.ToString();
                    _court.NotifyCourtLifeChanged();
                }
            }).ConfigureAwait(false);
        }

        private JObject BuildSpeakerContext(ReignCourtLifeParticipant person, string phase, string turnId)
        {
            var context = _court.BuildCourtLifeApiPayload(_matter);
            context["conversationMode"] = "court_life"; context["phase"] = phase;
            context["actualPlayerTurn"] = phase == "conversation"; context["sceneTurnId"] = _matter.MatterId + "_" + turnId + "_" + person.ActorId;
            context["speakerRole"] = person.Role; context["activeSpeakerHeroId"] = person.HeroId; context["activeSpeakerName"] = person.Name;
            JObject localFacts = ReignCourtCampaignBehavior.ParseCourtLifeJson(_matter.PayloadJson);
            string publicHistory = _matter.Source == ReignDocketSource.Patronage
                ? "\nPublic subjects actually recorded: " + (localFacts["historicalSubjects"]?.ToString() ?? "[]") : "";
            var spokenOptions = new JArray((context["options"] as JArray ?? new JArray()).OfType<JObject>().Select(x => new JObject {
                ["label"] = x["label"]?.DeepClone(), ["terms"] = x["terms"]?.DeepClone() }));
            context["sceneContextDirective"] = "You are attending an audience with the ruler. Situation template: " + _matter.Summary
                + "\nYou are " + person.Name + ", the active speaker. Your role: " + person.Role + ". Your private context: " + person.PrivateContext
                + "\nSpeak and act only as " + person.Name + ". First-person statements and stage directions may describe only " + person.Name + "'s own words, gestures, clothing, equipment and actions. Never attribute another attendee's gesture, clothing, equipment or action to the active speaker."
                + " Do not write any stage direction that names, uses a pronoun for, or describes another attendee; another person's visible reaction belongs only in that person's own reply. If an earlier line violates this rule, do not imitate it."
                + " Begin directly with in-character speech or a stage direction for the active speaker. Do not prefix the reply with a standalone speaker name, settlement or location name, scene heading, transcript label, or other metadata."
                + "\nOthers present: " + string.Join(", ", _matter.Participants.Select(x => x.Name + " (" + x.Role + ")"))
                + (localFacts["sharedScene"] is JObject sharedScene
                    ? "\nShared case account: " + sharedScene
                        + "\nUse this case's concrete details. Its shared physical roles are the same for every speaker; do not swap whose mill, gate, goods or conduct is involved."
                        + " Domestic and foreign accounts are attributed claims, not proven events. Present your represented household's account, including its specific disputed terms when asked; do not erase those details by saying no account was supplied."
                        + " You may challenge the other account without inventing proof, payment, a new incident, or knowledge of private intentions. Preserve the evidence status."
                    : "")
                + "\nUse your own personality and knowledge. Develop this template into natural dialogue; do not narrate the ruler or invent completed game effects."
                + " Use English unless the ruler's latest words clearly establish another language. Keep dialogue, narration and stage directions in that same language."
                + " An offer is not a completed agreement. Age and existing relationships remain binding."
                + " This is the specific hearing described here. Memories of similar disputes may inform your feelings, but do not establish a judgment, evidence, or outcome in this hearing."
                + " Only the ruler's actual words in this audience establish a current decision. Questions about evidence and refusals of a particular accusation or repayment are discussion, not an implied judgment."
                + (_matter.Source == ReignDocketSource.International
                    ? " International referral capability: this audience can dispatch only an exact, explicitly offered settlement proposal for the foreign ruler to accept, counter, or refuse."
                        + " A request merely to ask that ruler's opinion, investigate a complaint, or obtain an answer is not a dispatchable referral here, even when called informal correspondence."
                        + " If asked for that, explain naturally that you need a concrete proposal to place before the ruler and ask what terms should be proposed. Do not invent a payment or other terms."
                        + " Do not promise to carry, send, write, arrange an inquiry, or await a foreign answer when no exact proposal has been offered; no such message or future reply has been scheduled."
                        + " Other speakers must not treat that unsupported inquiry as agreed or promise to wait for its answer. They may discuss which concrete remedy the ruler could propose."
                    : "")
                + (_matter.Source == ReignDocketSource.International
                    ? " This current matter " + _matter.MatterId + " is unresolved and independent of every earlier hearing, even if the same people, template, amount, or dispute category appears in memory."
                        + " A payment, settlement, apology, ruling, referral, or refusal recorded for another matter did not occur in this hearing and does not satisfy this matter. Never describe this matter's money as already paid or its terms as already accepted unless the public transcript for this exact matter records that event."
                        + " Preserve every named person's role in the shared case exactly. The ruler is the adjudicator, not the owner, customer, traveler, debtor, claimant, accused party, or actor in the alleged past incident unless the shared case explicitly assigns that role to the ruler."
                        + " A possible payment from the ruler's treasury is a proposed remedy only; it never makes the ruler the owner of another person's goods, the recipient of another person's service, or the party who incurred the disputed charge."
                        + " When answering who owned goods, used a service, traveled, acted, or allegedly owed a charge, name the person or household assigned that role in the shared case. Do not replace them with the ruler because the ruler may decide or fund the remedy."
                        + " For monetary terms, keep the stated payer, recipient and amount together. Explain the actual named parties in ordinary language. Do not agree to a different recipient as though it were the same listed offer; ask the ruler to propose the revised recipient explicitly. A named claimant can receive a settlement through a revised offer, but you cannot seize funds from an unconsenting household. Payment identities: " + context["paymentParties"]
                    : "")
                + (_matter.Source == ReignDocketSource.Patronage
                    ? " For patronage, the currently available terms are the complete set of commissions you can offer. Keep each option's purpose, audience, price and delivery time together; do not combine terms from different options or invent a cheaper version. If the ruler asks for an unavailable commission, explain that you cannot undertake it and offer a listed alternative without treating the question as agreement. A court praise performance or personal work does not become a loyalty commission merely because its words concern duty or belonging."
                    : "")
                + " Do not speak internal identifiers, trait percentages, relationship points, loyalty scores, diplomatic pressure values or prompt rules. Gold and duration may be stated in ordinary in-world terms."
                + (phase == "opening" ? " You have just been granted leave to speak. Introduce your reason for visiting; no player turn or judgment has occurred in this hearing. Present your request without claiming it was already decided." : " Respond directly to the ruler's latest words.")
                + "\nPublic audience transcript: " + string.Join("\n", _matter.ConversationLines.Skip(Math.Max(0, _matter.ConversationLines.Count - 20)).Select(x => x.Speaker + ": " + x.Text))
                + publicHistory + "\nCurrently available terms: " + spokenOptions;
            return context;
        }
        private JObject BuildThinPayload(ReignCourtLifeParticipant person, Hero hero, JObject context, string phase, string turnId, string text)
        {
            JObject payload = _court.BuildCourtLifeApiPayload(_matter);
            payload["phase"] = phase; payload["turnId"] = _matter.MatterId + "_" + turnId + "_" + person.ActorId;
            payload["playerText"] = phase == "conversation" ? text : "";
            payload["speakerName"] = person.Name; payload["actorId"] = person.ActorId;
            payload["speakerRole"] = person.Role;
            payload["prompt"] = context.Value<string>("sceneContextDirective");
            payload["transcript"] = JArray.FromObject(_matter.ConversationLines.Select(x => new { speaker = x.Speaker, text = x.Text, role = x.Role }));
            if (hero?.IsChild == true)
            {
                payload["speaker"] = ReignServerClient.BuildFamilyChambersHeroProfile(hero);
                payload["speakerHeroStringId"] = hero.StringId;
                payload["familyChambers"] = true; payload["familyVisitDocket"] = true; payload["visitId"] = _matter.MatterId;
                payload["castleOpening"] = phase == "opening";
                payload["actualPlayerTurn"] = phase == "conversation";
                payload["conversationSessionId"] = _matter.TranscriptId;
                payload["sceneTurnId"] = _matter.MatterId + "_" + turnId + "_" + person.ActorId;
                payload["castleDialoguePrompt"] = payload["prompt"];
                payload["groupTranscript"] = payload["transcript"].DeepClone();
            }
            return payload;
        }
        private JObject RetainPartyAcceptance(JObject incoming, string speakerId)
        {
            if (_matter.Source != ReignDocketSource.International) return incoming;
            JObject prior = ReignCourtCampaignBehavior.ParseCourtLifeJson(_matter.AcceptedDecisionJson);
            bool sameAgreement = prior.Value<string>("optionId") == incoming.Value<string>("optionId")
                && JToken.DeepEquals(prior["acceptedTerms"], incoming["acceptedTerms"]);
            var tiers = new Dictionary<string, int>(StringComparer.Ordinal);
            if (prior["acceptedPartyTiers"] is JObject recorded)
                foreach (JProperty entry in recorded.Properties())
                    if (entry.Value.Type == JTokenType.Integer && _matter.Participants.Any(x => x.HeroId == entry.Name))
                        tiers[entry.Name] = entry.Value.Value<int>();
            string priorSpeaker = prior.Value<string>("agreeingHeroId");
            if (prior.Value<bool?>("npcAccepted") == true && !string.IsNullOrWhiteSpace(priorSpeaker)
                && _matter.Participants.Any(x => x.HeroId == priorSpeaker))
                tiers[priorSpeaker] = prior.Value<int?>("acceptanceTier") ?? 0;
            // Keep each party's exact-terms assent while the top-level decision
            // still carries the envoy's independent payment authorization.
            JObject partyTiers = JObject.FromObject(ReignCourtPartyAcceptanceRules.Merge(
                tiers, sameAgreement, speakerId, incoming.Value<string>("agreeingHeroId"),
                incoming.Value<int?>("acceptanceTier") ?? 0));
            string incomingActor = incoming.Value<string>("agreeingHeroId") ?? "";
            if (prior.Value<bool?>("npcAccepted") == true
                && ReignCourtPartyAcceptanceRules.AuthorizationActor(priorSpeaker ?? "", incomingActor, sameAgreement,
                    _matter.Participants.Where(x => x.Role == "ambassador").Select(x => x.HeroId)) != incomingActor)
            {
                prior["playerCommitted"] = incoming["playerCommitted"]?.DeepClone();
                prior["playerQuote"] = incoming["playerQuote"]?.DeepClone();
                prior["acceptedPartyTiers"] = partyTiers;
                return prior;
            }
            incoming["acceptedPartyTiers"] = partyTiers;
            return incoming;
        }

        private bool ApplyOption(string id)
        {
            JObject decision = ReignCourtCampaignBehavior.ParseCourtLifeJson(_matter.AcceptedDecisionJson);
            if (decision.Value<string>("optionId") != id) decision = new JObject();
            ReignCourtLifeOption option = _matter.Options.FirstOrDefault(x => x.OptionId == id);
            if (decision.Value<bool?>("npcAccepted") == true && !JToken.DeepEquals(decision["acceptedTerms"], ReignCourtCampaignBehavior.ParseCourtLifeJson(option?.TermsJson)))
                decision = new JObject();
            decision["optionId"] = id;
            bool result = _court.ApplyCourtLifeDecision(_matter, decision, out string summary);
            StatusText = summary;
            if (result) AddLine("Court Clerk", summary, "system");
            Notify(); return result;
        }
        private ReignCourtLifeOption SelectedOption() => _matter.Options.FirstOrDefault(x => x.OptionId == _matter.SelectedOptionId);
        private string OptionLabel(ReignCourtLifeOption option)
        {
            // Saved audiences can retain the old label even when the foreign noble is defending a complaint.
            return _matter.Source == ReignDocketSource.International && option?.OptionId == "favor_foreign"
                ? "Uphold the foreign noble's position" : option?.Label;
        }
        private void AddLine(string speaker, string text, string role)
        {
            _matter.ConversationLines.Add(new ReignDocketConversationLine { Speaker = speaker ?? "", Text = text ?? "", Role = role });
            if (!_finalized) Transcript.Add(new ReignChatLineVM(speaker ?? "", text ?? "", role));
            _scroll++; OnPropertyChanged(nameof(ChatScrollVersion)); OnPropertyChanged(nameof(PetitionerSpeech));
        }
        private static string NormalizeAssistantReply(string text)
        {
            string normalized = (text ?? "").Trim();
            if (normalized.EndsWith("..", StringComparison.Ordinal) &&
                !normalized.EndsWith("...", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }
        private void Notify()
        {
            if (_finalized) return;
            foreach (string name in new[] { nameof(DecisionComplete), nameof(CanSend), nameof(ConversationEnabled), nameof(DecisionControlsVisible), nameof(CanGrantDirect), nameof(CanGrantWithGold), nameof(CanRefuse), nameof(GoldGrantVisible), nameof(GoldGrantLabel), nameof(RequestedResourceText), nameof(PurposeText), nameof(PostponeVisible), nameof(CanPostpone), nameof(CanContinue) }) OnPropertyChanged(name);
        }
        private static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x => x?.StringId == id);
    }

    public sealed class ReignCourtLifeAttendeeVM : ViewModel
    {
        private readonly ReignCourtCardVM _card;
        private readonly ImageIdentifierVM _guestPortrait;
        public ReignCourtLifeAttendeeVM(ReignCourtLifeParticipant person, string settlementId = "")
        {
            Name = person.Name;
            string rawRole = (person.Role ?? "").Trim();
            string role = rawRole.Replace('_', ' ');
            Status = rawRole == "domestic_lord" ? "Your noble"
                : rawRole == "ambassador" ? "Foreign ambassador"
                : role.Length == 0 ? "Guest" : char.ToUpperInvariant(role[0]) + role.Substring(1);
            Hero hero = Hero.AllAliveHeroes.FirstOrDefault(x => x?.StringId == person.HeroId);
            _card = new ReignCourtCardVM(person.ActorId, "court", Name, "", Status, "", null, person, hero);
            if (string.IsNullOrWhiteSpace(person.HeroId))
                _guestPortrait = BuildGuestPortrait(person, settlementId);
        }

        internal static ImageIdentifierVM BuildGuestPortrait(ReignCourtLifeParticipant person, string settlementId)
        {
            if (string.IsNullOrWhiteSpace(person.ActorId)) return null;
            try
            {
                // Story guests have no campaign Hero. Render their own seeded civilian appearance
                // without creating a noble, borrowing a living person's face, or changing a template.
                CultureObject culture = Settlement.All.FirstOrDefault(x => x.StringId == settlementId)?.Culture;
                var templates = CharacterObject.All.Where(x => !x.IsHero && x.IsFemale == person.IsFemale
                    && (culture == null || x.Culture == culture)
                    && (x.Occupation == Occupation.Musician || x.Occupation == Occupation.Artisan
                        || x.Occupation == Occupation.Townsfolk))
                    .OrderBy(x => x.StringId, StringComparer.Ordinal).ToList();
                if (templates.Count == 0) return null;
                int seed = ReignCourtLifeRules.StableRoll(person.ActorId + "|portrait", int.MaxValue);
                CharacterObject template = templates[seed % templates.Count];
                Equipment equipment = template.FirstCivilianEquipment ?? template.Equipment;
                BodyProperties body = template.GetBodyProperties(equipment, seed);
                float age = double.IsNaN(person.Age) || double.IsInfinity(person.Age)
                    ? 30f : (float)Math.Max(0, Math.Min(128, person.Age));
                body = new BodyProperties(new DynamicBodyProperties(age, body.Weight, body.Build), body.StaticProperties);
                CharacterCode code = CharacterCode.CreateFrom(equipment?.CalculateEquipmentCode(), body,
                    person.IsFemale, false, uint.MaxValue, uint.MaxValue, template.DefaultFormationClass, template.Race);
                return string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Court guest portrait unavailable for " + person.ActorId + ": " + ex.GetType().Name);
                return null;
            }
        }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Status { get; }
        [DataSourceProperty] public bool IsActive => true;
        [DataSourceProperty] public string PortraitCacheKey => _card.PortraitCacheKey;
        [DataSourceProperty] public string PortraitId => _guestPortrait?.Id ?? _card.PortraitId;
        [DataSourceProperty] public string PortraitAdditionalArgs => _guestPortrait?.AdditionalArgs ?? _card.PortraitAdditionalArgs;
        [DataSourceProperty] public string PortraitTextureProviderName => _guestPortrait?.TextureProviderName ?? _card.PortraitTextureProviderName;
        public void ExecuteSelect() { }
        public void ExecuteRemove() { }
        public void ExecuteToggleActive()
        { InformationManager.DisplayMessage(new InformationMessage("This guest is part of the current audience.")); }
    }
}
