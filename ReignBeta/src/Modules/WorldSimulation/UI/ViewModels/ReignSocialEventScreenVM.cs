using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Runtime;
using ReignBeta.Settings;
using ReignBeta.UI.EventArt;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignSocialEventScreenVM : ViewModel, ReignBeta.UI.IReignActionHost
    {
        public IReadOnlyList<Hero> ActionParticipants => _session.ActiveHeroes().ToList();
        public bool ActionBusy => IsBusy;
        internal string ActiveWeddingId => _session.Record.WeddingId;
        [DataSourceProperty] public string ApproachLabel => _session.Record.IsWedding ? "Wedding actions" : "Wait Approach";
        [DataSourceProperty] public string NextPhaseLabel => _session.Record.IsWedding && _session.CurrentPhaseIndex == 2 ? "Conclude ceremony" : "Next Phase";
        [DataSourceProperty] public string LeaveEventLabel => _session.Record.IsWedding ? "Leave ceremony" : "End Event";
        private ReignActionComposerVM _composer;
        private bool _personalSceneClosed;
        public ReignActionComposerVM Composer => _composer ?? (_composer = new ReignActionComposerVM(() => OnPropertyChanged(nameof(InputText))));
        public override void OnFinalize()
        {
            _personalSceneClosed = true;
            ReignActionBuilder.CloseFor(this);
            _composer?.Dispose();
            base.OnFinalize();
        }

        [DataSourceProperty] public string ActionComposerId => Composer.Id;
        public Reign.Core.Contracts.Actions.ActionAgreement ActionDraft => Composer.State.HasValidAttachment ? Composer.State.Draft : null;
        private readonly ReignSocialEventSession _session;
        private readonly Action _closeScreen;
        private readonly Action _resolveEvent;
        private readonly Action<Hero> _openEncyclopediaOverlay;
        private readonly Action<Hero> _requestPortraitOverlay;
        private readonly bool _calibrationMode;
        private string _eventTitle;
        private string _phaseTitle;
        private string _phaseDescription;
        private string _phaseStatus;
        private string _eventImageId;
        private string _eventImageText;
        private string _eventBackgroundImageId;
        private string _busyText;
        private bool _isBusy;
        private bool _isOverlayVisible = true;
        private int _chatScrollVersion;
        private bool _isPortraitPreviewVisible;
        private string _portraitPreviewName;
        private string _portraitPreviewId;
        private string _portraitPreviewAdditionalArgs;
        private string _portraitPreviewTextureProviderName;
        private string _backgroundImageId;
        private string _backgroundAdditionalArgs;
        private string _backgroundTextureProviderName;
        private float _eventArtSize = 876f;
        private float _eventArtRowHeight = 896f;

        public ReignSocialEventScreenVM(
            ReignSocialEventSession session,
            Action closeScreen,
            Action resolveEvent,
            Action<Hero> openEncyclopediaOverlay = null,
            Action<Hero> requestPortraitOverlay = null,
            bool calibrationMode = false)
        {
            _session = session;
            _closeScreen = closeScreen;
            _resolveEvent = resolveEvent;
            _openEncyclopediaOverlay = openEncyclopediaOverlay;
            _requestPortraitOverlay = requestPortraitOverlay;
            _calibrationMode = calibrationMode;
            Attendees = new MBBindingList<ReignSocialEventAttendeeVM>();
            ActiveParticipants = new MBBindingList<ReignSocialEventAttendeeVM>();
            AvailableAttendees = new MBBindingList<ReignSocialEventAttendeeVM>();
            ChatLines = new MBBindingList<ReignChatLineVM>();
            SetBackgroundIdentifier();
            RefreshFromSession();
            AddOpeningLines();
            if (_calibrationMode)
                AddSystemLine("Provider-free UI calibration fixture.");
            else if (_session.PersonalStory != null)
            {
                _session.PersonalStoryReady = ReignNpcStoryClient.StartNpcStorySceneAsync(_session);
                _ = RestorePersonalStoryAsync();
            }
            else
                _ = ReignServerClient.StartSocialEventAsync(_session);
        }

        public MBBindingList<ReignSocialEventAttendeeVM> Attendees { get; }
        public MBBindingList<ReignSocialEventAttendeeVM> ActiveParticipants { get; }
        public MBBindingList<ReignSocialEventAttendeeVM> AvailableAttendees { get; }
        public MBBindingList<ReignChatLineVM> ChatLines { get; }

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
        public float PortraitPreviewImageWidth => 690f;

        [DataSourceProperty]
        public float PortraitPreviewImageHeight => 920f;

        [DataSourceProperty]
        public float PortraitPreviewFrameWidth => 726f;

        [DataSourceProperty]
        public float PortraitPreviewFrameHeight => 956f;

        [DataSourceProperty]
        public string EventTitle
        {
            get { return _eventTitle; }
            set
            {
                if (value != _eventTitle)
                {
                    _eventTitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float EventArtSize
        {
            get { return _eventArtSize; }
            private set
            {
                if (Math.Abs(value - _eventArtSize) > 0.01f)
                {
                    _eventArtSize = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float EventArtRowHeight
        {
            get { return _eventArtRowHeight; }
            private set
            {
                if (Math.Abs(value - _eventArtRowHeight) > 0.01f)
                {
                    _eventArtRowHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public void UpdateViewportLayout(float logicalHeight)
        {
            // Reserve the outer margins, header, phase banner, and at least 220
            // logical pixels for chat. The square then consumes every remaining
            // pixel up to the approved 876px live-game maximum.
            float artSize = Math.Min(876f, Math.Max(236f, logicalHeight - 484f));
            artSize = (float)Math.Round(artSize);
            EventArtSize = artSize;
            EventArtRowHeight = artSize + 20f;
        }

        [DataSourceProperty]
        public string PhaseTitle
        {
            get { return _phaseTitle; }
            set
            {
                if (value != _phaseTitle)
                {
                    _phaseTitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PhaseDescription
        {
            get { return _phaseDescription; }
            set
            {
                if (value != _phaseDescription)
                {
                    _phaseDescription = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PhaseStatus
        {
            get { return _phaseStatus; }
            set
            {
                if (value != _phaseStatus)
                {
                    _phaseStatus = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string EventImageId
        {
            get { return _eventImageId; }
            set
            {
                if (value != _eventImageId)
                {
                    _eventImageId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string EventImageText
        {
            get { return _eventImageText; }
            set
            {
                if (value != _eventImageText)
                {
                    _eventImageText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string EventBackgroundImageId
        {
            get { return _eventBackgroundImageId; }
            set
            {
                if (value != _eventBackgroundImageId)
                {
                    _eventBackgroundImageId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool ShowPhaseControls => !_session.Record.IsGeneratedWildernessEvent;

        [DataSourceProperty]
        public bool ShowWildernessArt => _session.Record.IsGeneratedWildernessEvent;

        [DataSourceProperty]
        public float AttendeeListTopMargin => _session.Record.IsGeneratedWildernessEvent ? 492f : 48f;

        [DataSourceProperty]
        public float AttendeeListBottomMargin => _session.Record.IsGeneratedWildernessEvent ? 16f : 74f;

        [DataSourceProperty]
        public float AttendeeTitleTopMargin => _session.Record.IsGeneratedWildernessEvent ? 452f : 12f;

        [DataSourceProperty]
        public float EventArtTopMargin => _session.Record.IsGeneratedWildernessEvent ? 12f : 48f;

        [DataSourceProperty]
        public string InputText
        {
            get { return Composer.State.Text; }
            set { Composer.SetText(value); }
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

        public void OnFrameTick(float dt)
        {
            // Event approaches are an explicit player action. There is no automatic
            // phase timer: Wait Approach is the only way an attendee can initiate.
        }

        public async void ExecuteSend()
        {
            if (_calibrationMode)
            {
                BusyText = "Calibration mode does not send dialogue.";
                return;
            }
            await SendLineAsync(InputText, string.Empty, ActionDraft == null ? null : ReignBeta.Actions.ReignActionService.Copy(ActionDraft));
        }

        public Task<ReignSocialEventTurnReply> SendAutomationLineAsync(string text, string correlationId) => SendLineAsync(text, correlationId, null);

        private async Task<ReignSocialEventTurnReply> SendLineAsync(string text, string correlationId, Reign.Core.Contracts.Actions.ActionAgreement actionDraft)
        {
            text = text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(correlationId)) correlationId = Guid.NewGuid().ToString("N");
            ReignSocialEventTurnReply rejected = new ReignSocialEventTurnReply();
            List<Hero> activeHeroes = null;
            SocialTurnOwner owner = null;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (_personalSceneClosed) { rejected.Error = "The social event closed."; return false; }
                if (string.IsNullOrWhiteSpace(text)) { rejected.Error = "No text was supplied."; return false; }
                if (IsBusy) { rejected.Error = "Social-event conversation is busy."; return false; }
                activeHeroes = _session.ActiveHeroes().ToList();
                if (activeHeroes.Count == 0) { rejected.Error = "No social-event participants are active."; return false; }
                if (actionDraft != null && !activeHeroes.Any(h => h.StringId == actionDraft.NpcId)) { rejected.Error = "Activate the character addressed by your action."; return false; }
                try { ReignConversationInputJournal.Save(ReignServerClient.GetCampaignId(),
                    ReignBeta.Campaign.ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main", Hero.MainHero?.StringId ?? "",
                    correlationId, text, session: _session.PersonalStory == null ? "social_event_" + _session.Record.EventId : ReignNpcStoryClient.StorySceneSessionId(_session)); }
                catch (Exception ex) { rejected.Error = "Your message could not be saved locally: " + ex.Message; AddSystemLine(rejected.Error); return false; }
                string playerName = Hero.MainHero?.Name?.ToString() ?? "Player";
                AddChatLine(playerName, text, "player");
                _session.RecordTranscriptLine(playerName, text);
                InputText = string.Empty;
                owner = CaptureSocialOwner();
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return rejected;
            return await RequestGroupTurnAsync(activeHeroes, text, correlationId, actionDraft, owner).ConfigureAwait(false);
        }

        public bool SelectAutomationHeroes(IEnumerable<Hero> heroes)
        {
            List<Hero> requested = (heroes ?? Enumerable.Empty<Hero>()).Where(hero => hero != null && hero != Hero.MainHero)
                .GroupBy(hero => hero.StringId, StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .Take(ReignSocialEventSession.MaxActiveParticipants).ToList();
            foreach (Hero active in _session.ActiveHeroes().ToList()) _session.DeactivateHero(active);
            foreach (Hero hero in requested) _session.ActivateHero(hero, true);
            RefreshAttendees();
            return requested.Count > 0 && _session.ActiveHeroes().Count() == requested.Count;
        }

        public async Task<bool> AdvanceAutomationPhaseAsync()
        {
            if (_session.PersonalStory != null) return false;
            if (IsBusy) return false;
            await ReignServerClient.FinishSocialEventPhaseAsync(_session).ConfigureAwait(false);
            bool advanced = await ReignMainThread.InvokeAsync(() => _session.AdvancePhase()).ConfigureAwait(false);
            if (advanced) await ReignMainThread.InvokeAsync(RefreshFromSession).ConfigureAwait(false);
            return advanced;
        }

        public async Task<JObject> FinishAutomationAsync()
        {
            if (_session.PersonalStory != null) return new JObject { ["ok"] = true, ["status"] = "closed_without_advancing_story" };
            if (IsBusy) return new JObject { ["ok"] = false, ["error"] = "Social-event conversation is busy." };
            bool phase = await ReignServerClient.FinishSocialEventPhaseAsync(_session).ConfigureAwait(false);
            JObject resolution = await ReignServerClient.ResolveSocialEventDetailedAsync(_session).ConfigureAwait(false);
            bool resolved = resolution.Value<bool?>("ok") == true;
            if (resolved) await ReignMainThread.InvokeAsync(() => _resolveEvent?.Invoke()).ConfigureAwait(false);
            return new JObject
            {
                ["ok"] = phase && resolved,
                ["phaseFinalized"] = phase,
                ["eventResolved"] = resolved,
                ["resolution"] = resolution
            };
        }

        public async void ExecuteWaitApproach()
        {
            if (_calibrationMode)
            {
                AddSystemLine("Approach rolls are disabled in calibration mode.");
                return;
            }
            if (IsBusy)
            {
                return;
            }

            if (_session.Record.IsWedding)
            {
                ReignActionBuilder.RequestOpenForHost(ScreenManager.TopScreen, this);
                return;
            }

            if (_session.RemainingActiveSlots <= 0)
            {
                AddSystemLine("The current conversation already has five participants.");
                return;
            }

            List<Hero> candidates = _session.GetApproachCandidates();
            if (candidates.Count == 0)
            {
                AddSystemLine("No one approaches you just yet.");
                return;
            }

            SocialTurnOwner owner = CaptureSocialOwner();
            IsBusy = true;
            BusyText = "Waiting to see who approaches...";
            JObject response = new JObject { ["ok"] = false, ["error"] = "The approach roll did not complete." };
            try
            {
                response = await ReignServerClient.RollSocialEventApproachesAsync(_session, candidates);
            }
            catch (Exception ex)
            {
                response = new JObject { ["ok"] = false, ["error"] = ex.Message };
            }

            // The roll resumes off-thread: activation and presentation are owner-checked
            // on the game thread, and a stale owner starts no approach replies.
            List<Hero> heroes = await ReignMainThread.InvokeAsync(() =>
            {
                if (!IsSocialTurnCurrent(owner)) return null;
                BusyText = string.Empty;
                IsBusy = false;
                if (response.Value<bool?>("ok") != true)
                {
                    AddSystemLine("No approach roll was made: " + (response.Value<string>("error") ?? "Bannerlord Reign is unavailable."));
                    return null;
                }

                HashSet<string> selectedIds = new HashSet<string>(
                    (response["approachHeroStringIds"] as JArray ?? new JArray())
                        .Values<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x)),
                    StringComparer.OrdinalIgnoreCase);
                List<Hero> approached = _session.ActivateApproachHeroes(candidates.Where(x => selectedIds.Contains(x.StringId)));
                if (approached.Count == 0)
                {
                    AddSystemLine("No one approaches you just yet.");
                }
                else
                {
                    AddSystemLine(string.Join(", ", approached.Select(x => x.Name?.ToString())) + (approached.Count == 1 ? " approaches you." : " approach you."));
                }

                RefreshAttendees();
                return approached;
            });
            if (heroes != null && heroes.Count > 0)
            {
                await RequestRepliesAsync(heroes, string.Empty, true, owner);
            }
        }

        public async void ExecuteNextPhase()
        {
            if (_session.PersonalStory != null) return;
            if (_calibrationMode)
            {
                if (_session.AdvancePhase())
                {
                    AddSystemLine("The calibration fixture moves to: " + _session.CurrentPhase.Title + ".");
                    RefreshFromSession();
                }
                return;
            }
            if (IsBusy)
            {
                return;
            }

            if (_session.Record.IsWedding)
            {
                if (_session.AdvancePhase())
                {
                    RefreshFromSession();
                    AddSystemLine(_session.CurrentPhase.SettingSummary);
                }
                else if (ReignBeta.Campaign.ReignRelationshipCampaignBehavior.Instance?.ConcludeWedding(ActiveWeddingId, _session.CurrentPhaseIndex) == true)
                    _closeScreen?.Invoke();
                return;
            }
            SocialTurnOwner owner = CaptureSocialOwner();
            await ReignServerClient.FinishSocialEventPhaseAsync(_session);
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!IsSocialTurnCurrent(owner)) return;
                if (!_session.AdvancePhase())
                {
                    ExecuteEndEvent();
                    return;
                }

                AddSystemLine("The event moves to the next phase: " + _session.CurrentPhase.Title + ".");
                RefreshFromSession();
            });
        }

        public async void ExecuteEndEvent()
        {
            if (_session.Record.IsWedding)
            {
                if (!IsBusy) _closeScreen?.Invoke();
                return;
            }
            if (_session.PersonalStory != null)
            {
                if (!IsBusy) _closeScreen?.Invoke();
                return;
            }
            if (_calibrationMode)
            {
                _closeScreen?.Invoke();
                return;
            }
            if (IsBusy)
            {
                return;
            }

            // Ending is same-load cleanup: it is owned by the original load, and only a
            // still-open screen is closed. Native payload reads stay on the game thread.
            ReignServerClient.RequestFence loadFence = ReignServerClient.RequestFence.Capture();
            await ReignServerClient.FinishSocialEventPhaseAsync(_session);
            Task<bool> resolve = await ReignMainThread.InvokeAsync(() =>
                loadFence.IsCurrent() ? ReignServerClient.ResolveSocialEventAsync(_session) : null);
            if (resolve == null) return;
            await resolve;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!loadFence.IsCurrent()) return;
                _resolveEvent?.Invoke();
                if (!_personalSceneClosed) _closeScreen?.Invoke();
            });
        }

        public void ExecuteClose()
        {
            ExecuteEndEvent();
        }

        public void ExecuteClosePortraitPreview()
        {
            IsPortraitPreviewVisible = false;
        }

        public void ExecuteNoOp()
        {
        }

        public void SetOverlayVisible(bool isVisible)
        {
            IsOverlayVisible = isVisible;
        }

        // Game thread only. A closed screen, a changed load or a changed player owns no
        // later dispatch, presentation or native effect of an in-flight turn.
        private sealed class SocialTurnOwner
        {
            internal SocialTurnOwner(ReignServerClient.RequestFence fence, Hero player) { Fence = fence; Player = player; }
            internal ReignServerClient.RequestFence Fence { get; }
            internal Hero Player { get; }
        }

        private SocialTurnOwner CaptureSocialOwner() => new SocialTurnOwner(ReignServerClient.RequestFence.Capture(), Hero.MainHero);

        private bool IsSocialTurnCurrent(SocialTurnOwner owner) => owner != null && !_personalSceneClosed
            && owner.Fence.IsCurrent() && ReferenceEquals(owner.Player, Hero.MainHero);

        // Existing action receipts/outcomes as separate system lines; never NPC transcript text.
        private void AddNativeStatusLines(JObject response)
        {
            foreach (string line in ReignStructuredActionClient.NativeStatusLines(response)) AddChatLine("Event", line, "system");
        }

        private async Task RequestRepliesAsync(List<Hero> activeHeroes, string playerText, bool approachOpening, SocialTurnOwner owner)
        {
            if (!approachOpening)
            {
                await RequestGroupTurnAsync(activeHeroes, playerText, owner: owner).ConfigureAwait(false);
                return;
            }

            Func<bool> current = () => IsSocialTurnCurrent(owner);
            if (!await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return false;
                    IsBusy = true;
                    BusyText = "Waiting for Bannerlord Reign...";
                    return true;
                }).ConfigureAwait(false)) return;
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                foreach (Hero speaker in activeHeroes)
                {
                    // Each approach dispatch is admitted only for the still-current owner.
                    ReignEventReply reply = await ReignServerClient.RequestSocialEventResponseAsync(_session, speaker, playerText, approachOpening, current).ConfigureAwait(false);
                    bool stillCurrent = await ReignMainThread.InvokeAsync(() =>
                    {
                        if (!current()) return false;
                        string speakerName = speaker.Name?.ToString() ?? "NPC";
                        bool hasStatus = reply.Ok && ReignStructuredActionClient.HasNativeStatus(reply.RawResponse);
                        if (reply.Ok && (!string.IsNullOrWhiteSpace(reply.Text) || hasStatus))
                        {
                            if (!string.IsNullOrWhiteSpace(reply.Text))
                            {
                                AddChatLine(speakerName, reply.Text.Trim(), "npc");
                                _session.RecordTranscriptLine(speakerName, reply.Text.Trim());
                            }
                            AddNativeStatusLines(reply.RawResponse);
                            if (!string.IsNullOrWhiteSpace(reply.TimingSummary))
                            {
                                ReignLog.Info("Social event reply timing hero=" + speaker.StringId + " " + reply.TimingSummary + " clientMs=" + reply.ClientTotalMs);
                            }
                        }
                        else
                        {
                            AddSystemLine("Bannerlord Reign server failed for " + speakerName + ": " + (reply.Error ?? "No response."));
                        }
                        return true;
                    }).ConfigureAwait(false);
                    if (!stillCurrent) break;
                }
            }
            finally
            {
                timer.Stop();
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return;
                    BusyText = string.Empty;
                    IsBusy = false;
                    RefreshAttendees();
                }).ConfigureAwait(false);
                ReignLog.Info("Social event reply batch clientMs=" + timer.ElapsedMilliseconds
                    + " activeHeroes=" + activeHeroes.Count
                    + " turn=" + (approachOpening ? "approach_opening" : "player_reply"));
            }
        }

        private async Task<ReignSocialEventTurnReply> RequestGroupTurnAsync(List<Hero> activeHeroes, string playerText, string correlationId = "",
            Reign.Core.Contracts.Actions.ActionAgreement actionDraft = null, SocialTurnOwner owner = null)
        {
            if (_session.PersonalStory != null) return await RequestPersonalStoryAsync(playerText, correlationId, actionDraft, owner).ConfigureAwait(false);
            if (_calibrationMode) return new ReignSocialEventTurnReply { Error = "Calibration does not send dialogue or award XP." };
            Func<bool> current = () => IsSocialTurnCurrent(owner);
            string xpPhaseKey = await ReignMainThread.InvokeAsync(() =>
            {
                if (!current()) return null;
                IsBusy = true;
                BusyText = "Waiting for Bannerlord Reign...";
                return _session.XpPhaseKey;
            }).ConfigureAwait(false);
            if (xpPhaseKey == null) return new ReignSocialEventTurnReply { Error = "The social event closed." };
            ReignSocialEventTurnReply turn = null;
            try
            {
                ReignXpInteraction xp = await ReignXpClient.BeginXpInteractionAsync().ConfigureAwait(false);
                turn = await ReignServerClient.RequestSocialEventTurnAsync(
                    _session,
                    activeHeroes,
                    playerText,
                    string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
                    reply =>
                    {
                        // Invoked on the game thread after the client's owner check.
                        Hero speaker = ReignObjectResolver.FindHero(reply.HeroStringId);
                        if (speaker == null) return;
                        if (!string.IsNullOrWhiteSpace(reply.Text))
                        {
                            string speakerName = speaker.Name?.ToString() ?? "NPC";
                            AddChatLine(speakerName, reply.Text.Trim(), "npc");
                            _session.RecordTranscriptLine(speakerName, reply.Text.Trim());
                        }
                        AddNativeStatusLines(reply.RawResponse);
                    }, actionDraft, current).ConfigureAwait(false);

                // Resolution, XP, departures and joins share one owner-checked callback.
                List<Hero> joined = await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return null;
                    if (turn == null || !turn.Ok)
                    {
                        AddSystemLine("Bannerlord Reign could not complete the group conversation: " + (turn?.Error ?? "No response."));
                        return null;
                    }

                    _session.ApplyTurnResolution(turn.AddressedHeroStringIds, turn.WanderedHeroStringIds, turn.NextIdleCounters);
                    if (xp.Owner != null && !xp.Owner.RecordSocialExchange(xpPhaseKey, turn.TurnId,
                            turn.ParticipantResults.Where(r => r.Ok && !string.IsNullOrWhiteSpace(r.Text)
                                && !string.Equals(r.Participation, "silent", StringComparison.OrdinalIgnoreCase))
                                .Select(r => ReignObjectResolver.FindHero(r.HeroStringId)), xp.Options))
                        return null;
                    foreach (string heroId in turn.WanderedHeroStringIds)
                    {
                        Hero wandered = Hero.AllAliveHeroes.FirstOrDefault(x => x != null && string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase));
                        AddSystemLine((wandered?.Name?.ToString() ?? "A participant") + " wanders away from the conversation.");
                    }

                    HashSet<string> joinedIds = new HashSet<string>(turn.JoinedHeroStringIds, StringComparer.OrdinalIgnoreCase);
                    List<Hero> arrivals = _session.ActivateApproachHeroes(_session.Record.GetAttendees().Where(x => x != null && joinedIds.Contains(x.StringId)));
                    if (arrivals.Count > 0)
                    {
                        AddSystemLine(string.Join(", ", arrivals.Select(x => x.Name?.ToString())) + (arrivals.Count == 1 ? " joins the conversation." : " join the conversation."));
                        RefreshAttendees();
                    }
                    return arrivals;
                }).ConfigureAwait(false);
                if (joined == null) return turn ?? new ReignSocialEventTurnReply { Error = "No response." };
                if (joined.Count > 0)
                {
                    await RequestRepliesAsync(joined, string.Empty, true, owner).ConfigureAwait(false);
                }

                bool? advanceAutomatically = await ReignMainThread.InvokeAsync(() =>
                    current() ? _session.RegisterCompletedExchange() : (bool?)null).ConfigureAwait(false);
                if (advanceAutomatically == null) return turn;
                if (advanceAutomatically == true)
                {
                    await ReignServerClient.FinishSocialEventPhaseAsync(_session).ConfigureAwait(false);
                }
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return;
                    if (advanceAutomatically == true && _session.AdvancePhase())
                    {
                        AddSystemLine("The event naturally moves to the next phase: " + _session.CurrentPhase.Title + ".");
                    }
                    RefreshFromSession();
                }).ConfigureAwait(false);
                return turn;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (!current()) return;
                    BusyText = string.Empty;
                    IsBusy = false;
                }).ConfigureAwait(false);
            }
        }

        private void ToggleAttendee(ReignSocialEventAttendeeVM attendee)
        {
            if (_session.PersonalStory != null) return;
            if (attendee?.Hero == null)
            {
                return;
            }

            if (attendee.IsActive)
            {
                _session.DeactivateHero(attendee.Hero);
            }
            else
            {
                if (!_session.ActivateHero(attendee.Hero, true))
                {
                    AddSystemLine("The current conversation already has five participants.");
                }
            }

            RefreshAttendees();
        }

        private void RefreshFromSession()
        {
            EventTitle = _session.Record.DisplayName;
            OnPropertyChanged(nameof(NextPhaseLabel));
            PhaseTitle = _session.CurrentPhase.Title;
            PhaseDescription = _session.CurrentPhase.SettingSummary;
            if (_session.Record.IsGeneratedWildernessEvent)
            {
                string terrain = string.IsNullOrWhiteSpace(_session.Record.GeneratedTerrainKey)
                    ? "plain"
                    : _session.Record.GeneratedTerrainKey;
                PhaseStatus = string.Empty;
                PhaseDescription = !string.IsNullOrWhiteSpace(_session.Record.GeneratedPlayerHook)
                    ? _session.Record.GeneratedPlayerHook
                    : _session.Record.GeneratedOpeningText ?? "A moment on the road draws your attention.";
                EventImageId = ReignEventArtTextureFactory.BuildGeneratedWildernessImageId(terrain);
                EventImageText = BuildWildernessImageCaption(terrain);
                EventBackgroundImageId = ReignEventArtTextureFactory.BuildWildernessBackgroundImageId();
            }
            else
            {
                bool wedding = _session.Record.IsWedding;
                int count = wedding ? _session.Record.WeddingRosterCount : _session.Record.GetAttendees().Count();
                PhaseStatus = "Phase " + (_session.CurrentPhaseIndex + 1) + " of " + _session.Template.Phases.Count + " | Attendees " + count;
                if (!wedding) PhaseStatus += " | Conversation " + _session.PhaseExchangeCount + "/" + ReignSocialEventSession.TurnsPerAutomaticPhase;
                string artTemplate = _session.Template.TemplateId;
                string artPhase = _session.CurrentPhase.PhaseId;
                if (wedding)
                {
                    string culture = artTemplate.Substring("wedding_".Length);
                    artTemplate = "feast_" + (culture == "neutral" ? "empire" : culture);
                    artPhase = new[] { "arrival_and_seating", "toasts_and_table_talk", "farewells" }[_session.CurrentPhaseIndex];
                    if (_session.CurrentPhaseIndex == 1 && !string.IsNullOrEmpty(_session.Record.WeddingCustoms))
                        PhaseDescription += " " + _session.Record.WeddingCustoms;
                }
                EventImageId = ReignEventArtTextureFactory.BuildImageId(artTemplate, artPhase);
                EventImageText = _session.Template.DisplayName + "\n" + _session.CurrentPhase.Title;
                EventBackgroundImageId = string.Empty;
            }
            RefreshAttendees();
        }

        private async Task RestorePersonalStoryAsync()
        {
            IsBusy = true; BusyText = "Restoring this story...";
            try
            {
                var history = await _session.PersonalStoryReady.ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => {
                    if (_personalSceneClosed) return;
                    foreach (var line in (history["lines"] as JArray ?? new JArray()).OfType<JObject>())
                        AddChatLine(line.Value<string>("speaker") ?? "", line.Value<string>("text") ?? "", line.Value<string>("role") ?? "npc");
                }).ConfigureAwait(false);
            }
            catch (Exception ex) { await ReignMainThread.InvokeAsync(() => AddSystemLine(ex.Message)).ConfigureAwait(false); }
            finally { await ReignMainThread.InvokeAsync(() => { IsBusy = false; BusyText = ""; }).ConfigureAwait(false); }
        }
        private async Task<ReignSocialEventTurnReply> RequestPersonalStoryAsync(string text, string correlation, Reign.Core.Contracts.Actions.ActionAgreement action,
            SocialTurnOwner owner)
        {
            Func<bool> ownerCurrent = () => IsSocialTurnCurrent(owner);
            if (!await ReignMainThread.InvokeAsync(() =>
                {
                    if (!ownerCurrent()) return false;
                    IsBusy = true; BusyText = "Listening...";
                    return true;
                }).ConfigureAwait(false))
                return new ReignSocialEventTurnReply { Error = "The story scene closed." };
            try
            {
                var reply = await ReignNpcStoryClient.RequestNpcStoryTurnAsync(_session, text,
                    string.IsNullOrWhiteSpace(correlation) ? Guid.NewGuid().ToString("N") : correlation, action, ownerCurrent).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => {
                    if (!ownerCurrent()) return;
                    if (!reply.Ok) { AddSystemLine(reply.Error); return; }
                    foreach (var line in reply.ParticipantResults)
                    {
                        string name = ReignObjectResolver.FindHero(line.HeroStringId)?.Name?.ToString() ?? "Companion";
                        // The exact story reply is projected unchanged; action receipts follow it
                        // as separate system lines (an action-only reply shows only those).
                        if (!string.IsNullOrWhiteSpace(line.Text) || !ReignStructuredActionClient.HasNativeStatus(line.RawResponse))
                        {
                            AddChatLine(name, line.Text, "npc"); _session.RecordTranscriptLine(name, line.Text);
                        }
                        AddNativeStatusLines(line.RawResponse);
                    }
                    var current = ReignBeta.Campaign.ReignNpcLivesBehavior.Instance?.State.Stories.FirstOrDefault(s => s.Id == _session.PersonalStory.Id);
                    if (current != null && current.Revision != _session.PersonalStory.Revision)
                        AddSystemLine("This chapter is complete. Your shared experience has been recorded.");
                }).ConfigureAwait(false);
                return reply;
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => { if (ownerCurrent()) AddSystemLine(ex.Message); }).ConfigureAwait(false);
                return new ReignSocialEventTurnReply { Error = ex.Message };
            }
            finally { await ReignMainThread.InvokeAsync(() => { if (ownerCurrent()) { IsBusy = false; BusyText = ""; } }).ConfigureAwait(false); }
        }

        private string BuildWildernessImageCaption(string terrain)
        {
            string location = (_session.Record.GeneratedLocationText ?? string.Empty).Trim();
            string time = (_session.Record.GeneratedTimeOfDayText ?? string.Empty).Trim();
            string caption = string.Join(" - ", new[] { location, time }
                .Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrWhiteSpace(caption) ? terrain : caption;
        }

        private void AddOpeningLines()
        {
            if (_session.Record.IsGeneratedWildernessEvent)
            {
                if (!string.IsNullOrWhiteSpace(_session.Record.GeneratedApproachText))
                {
                    AddSystemLine(_session.Record.GeneratedApproachText.Trim());
                }

                if (!string.IsNullOrWhiteSpace(_session.Record.GeneratedOpeningText))
                {
                    AddSystemLine(_session.Record.GeneratedOpeningText.Trim());
                }

                if (!string.IsNullOrWhiteSpace(_session.Record.GeneratedPlayerHook))
                {
                    AddSystemLine(_session.Record.GeneratedPlayerHook.Trim());
                }

                return;
            }

            AddSystemLine("The " + _session.Record.DisplayName + " begins.");
            if (!string.IsNullOrWhiteSpace(_session.Record.GeneratedOpeningText))
            {
                AddSystemLine(_session.Record.GeneratedOpeningText.Trim());
            }
        }

        private void RefreshAttendees()
        {
            List<string> activeIds = _session.ActiveHeroStringIds.ToList();
            Attendees.Clear();
            ActiveParticipants.Clear();
            AvailableAttendees.Clear();
            foreach (Hero hero in _session.Record.GetAttendees().Where(x => x != null && x != Hero.MainHero))
            {
                ReignSocialEventAttendeeVM attendee = new ReignSocialEventAttendeeVM(
                    hero,
                    activeIds.Contains(hero.StringId),
                    ToggleAttendee,
                    PreviewPortrait,
                    OpenEncyclopedia,
                    GenerateAIPortrait);
                Attendees.Add(attendee);
                if (attendee.IsActive)
                {
                    ActiveParticipants.Add(attendee);
                }
                else
                {
                    AvailableAttendees.Add(attendee);
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

        private void OpenEncyclopedia(ReignSocialEventAttendeeVM attendee)
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

        private void GenerateAIPortrait(ReignSocialEventAttendeeVM attendee)
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

        private void PreviewPortrait(ReignSocialEventAttendeeVM attendee)
        {
            if (attendee == null
                || string.IsNullOrWhiteSpace(attendee.PortraitId)
                || string.IsNullOrWhiteSpace(attendee.PortraitTextureProviderName))
            {
                return;
            }

            PortraitPreviewName = attendee.Name;
            PortraitPreviewId = attendee.PortraitId;
            PortraitPreviewAdditionalArgs = attendee.PortraitAdditionalArgs ?? string.Empty;
            PortraitPreviewTextureProviderName = attendee.PortraitTextureProviderName;
            IsPortraitPreviewVisible = true;
        }

        private void AddSystemLine(string text)
        {
            AddChatLine("Event", text, "system");
            _session.RecordTranscriptLine("Event", text);
        }

        private void AddChatLine(string speaker, string text, string role)
        {
            ChatLines.Add(new ReignChatLineVM(speaker, text, role));
            ChatScrollVersion++;
        }
    }
}
