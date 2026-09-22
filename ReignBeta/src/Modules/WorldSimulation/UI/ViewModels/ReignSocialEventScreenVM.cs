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

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignSocialEventScreenVM : ViewModel
    {
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
        private string _inputText;
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
            await SendAutomationLineAsync(InputText, string.Empty);
        }

        public async Task<ReignSocialEventTurnReply> SendAutomationLineAsync(string text, string correlationId)
        {
            text = (text ?? string.Empty).Trim();
            ReignSocialEventTurnReply rejected = new ReignSocialEventTurnReply();
            List<Hero> activeHeroes = null;
            bool accepted = await ReignMainThread.InvokeAsync(() =>
            {
                if (string.IsNullOrWhiteSpace(text)) { rejected.Error = "No text was supplied."; return false; }
                if (IsBusy) { rejected.Error = "Social-event conversation is busy."; return false; }
                activeHeroes = _session.ActiveHeroes().ToList();
                if (activeHeroes.Count == 0) { rejected.Error = "No social-event participants are active."; return false; }
                string playerName = Hero.MainHero?.Name?.ToString() ?? "Player";
                AddChatLine(playerName, text, "player");
                _session.RecordTranscriptLine(playerName, text);
                InputText = string.Empty;
                return true;
            }).ConfigureAwait(false);
            if (!accepted) return rejected;
            return await RequestGroupTurnAsync(activeHeroes, text, correlationId).ConfigureAwait(false);
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
            if (IsBusy) return false;
            await ReignServerClient.FinishSocialEventPhaseAsync(_session).ConfigureAwait(false);
            bool advanced = await ReignMainThread.InvokeAsync(() => _session.AdvancePhase()).ConfigureAwait(false);
            if (advanced) await ReignMainThread.InvokeAsync(RefreshFromSession).ConfigureAwait(false);
            return advanced;
        }

        public async Task<JObject> FinishAutomationAsync()
        {
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

            IsBusy = true;
            BusyText = "Waiting to see who approaches...";
            JObject response = new JObject { ["ok"] = false, ["error"] = "The approach roll did not complete." };
            try
            {
                response = await ReignServerClient.RollSocialEventApproachesAsync(_session, candidates);
            }
            finally
            {
                BusyText = string.Empty;
                IsBusy = false;
            }

            if (response.Value<bool?>("ok") != true)
            {
                AddSystemLine("No approach roll was made: " + (response.Value<string>("error") ?? "Bannerlord Reign is unavailable."));
                return;
            }

            HashSet<string> selectedIds = new HashSet<string>(
                (response["approachHeroStringIds"] as JArray ?? new JArray())
                    .Values<string>()
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
            List<Hero> heroes = _session.ActivateApproachHeroes(candidates.Where(x => selectedIds.Contains(x.StringId)));
            if (heroes.Count == 0)
            {
                AddSystemLine("No one approaches you just yet.");
            }
            else
            {
                AddSystemLine(string.Join(", ", heroes.Select(x => x.Name?.ToString())) + (heroes.Count == 1 ? " approaches you." : " approach you."));
            }

            RefreshAttendees();
            if (heroes.Count > 0)
            {
                await RequestRepliesAsync(heroes, string.Empty, true);
            }
        }

        public async void ExecuteNextPhase()
        {
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

            await ReignServerClient.FinishSocialEventPhaseAsync(_session);
            if (!_session.AdvancePhase())
            {
                ExecuteEndEvent();
                return;
            }

            AddSystemLine("The event moves to the next phase: " + _session.CurrentPhase.Title + ".");
            RefreshFromSession();
        }

        public async void ExecuteEndEvent()
        {
            if (_calibrationMode)
            {
                _closeScreen?.Invoke();
                return;
            }
            if (IsBusy)
            {
                return;
            }

            await ReignServerClient.FinishSocialEventPhaseAsync(_session);
            await ReignServerClient.ResolveSocialEventAsync(_session);
            _resolveEvent?.Invoke();
            _closeScreen?.Invoke();
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

        private async Task RequestRepliesAsync(List<Hero> activeHeroes, string playerText, bool approachOpening = false)
        {
            if (!approachOpening)
            {
                await RequestGroupTurnAsync(activeHeroes, playerText);
                return;
            }

            IsBusy = true;
            BusyText = "Waiting for Bannerlord Reign...";
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                foreach (Hero speaker in activeHeroes)
                {
                    ReignEventReply reply = await ReignServerClient.RequestSocialEventResponseAsync(_session, speaker, playerText, approachOpening);
                    if (reply.Ok && !string.IsNullOrWhiteSpace(reply.Text))
                    {
                        AddChatLine(speaker.Name?.ToString() ?? "NPC", reply.Text.Trim(), "npc");
                        _session.RecordTranscriptLine(speaker.Name?.ToString() ?? "NPC", reply.Text.Trim());
                        if (!string.IsNullOrWhiteSpace(reply.TimingSummary))
                        {
                            ReignLog.Info("Social event reply timing hero=" + speaker.StringId + " " + reply.TimingSummary + " clientMs=" + reply.ClientTotalMs);
                        }
                    }
                    else
                    {
                        AddSystemLine("Bannerlord Reign server failed for " + (speaker.Name?.ToString() ?? "NPC") + ": " + (reply.Error ?? "No response."));
                    }
                }
            }
            finally
            {
                timer.Stop();
                BusyText = string.Empty;
                IsBusy = false;
                ReignLog.Info("Social event reply batch clientMs=" + timer.ElapsedMilliseconds
                    + " activeHeroes=" + activeHeroes.Count
                    + " turn=" + (approachOpening ? "approach_opening" : "player_reply"));
                RefreshAttendees();
            }
        }

        private async Task<ReignSocialEventTurnReply> RequestGroupTurnAsync(List<Hero> activeHeroes, string playerText, string correlationId = "")
        {
            if (_calibrationMode) return new ReignSocialEventTurnReply { Error = "Calibration does not send dialogue or award XP." };
            IsBusy = true;
            BusyText = "Waiting for Bannerlord Reign...";
            ReignSocialEventTurnReply turn = null;
            string xpPhaseKey = _session.XpPhaseKey;
            try
            {
                ReignXpInteraction xp = await ReignServerClient.BeginXpInteractionAsync();
                turn = await ReignServerClient.RequestSocialEventTurnAsync(
                    _session,
                    activeHeroes,
                    playerText,
                    string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
                    reply =>
                    {
                        Hero speaker = ReignObjectResolver.FindHero(reply.HeroStringId);
                        if (speaker != null && !string.IsNullOrWhiteSpace(reply.Text))
                        {
                            string speakerName = speaker.Name?.ToString() ?? "NPC";
                            AddChatLine(speakerName, reply.Text.Trim(), "npc");
                            _session.RecordTranscriptLine(speakerName, reply.Text.Trim());
                        }
                    });
            if (turn == null || !turn.Ok)
            {
                AddSystemLine("Bannerlord Reign could not complete the group conversation: " + (turn?.Error ?? "No response."));
                return turn ?? new ReignSocialEventTurnReply { Error = "No response." };
            }

            _session.ApplyTurnResolution(turn.AddressedHeroStringIds, turn.WanderedHeroStringIds, turn.NextIdleCounters);
            bool newExchange = true;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (xp.Owner != null)
                    newExchange = xp.Owner.RecordSocialExchange(xpPhaseKey, turn.TurnId,
                        turn.ParticipantResults.Where(r => r.Ok && !string.IsNullOrWhiteSpace(r.Text)
                            && !string.Equals(r.Participation, "silent", StringComparison.OrdinalIgnoreCase))
                            .Select(r => ReignObjectResolver.FindHero(r.HeroStringId)), xp.Options);
            });
            if (!newExchange) return turn;
            foreach (string heroId in turn.WanderedHeroStringIds)
            {
                Hero wandered = Hero.AllAliveHeroes.FirstOrDefault(x => x != null && string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase));
                AddSystemLine((wandered?.Name?.ToString() ?? "A participant") + " wanders away from the conversation.");
            }

            HashSet<string> joinedIds = new HashSet<string>(turn.JoinedHeroStringIds, StringComparer.OrdinalIgnoreCase);
            List<Hero> joined = _session.ActivateApproachHeroes(_session.Record.GetAttendees().Where(x => x != null && joinedIds.Contains(x.StringId)));
            if (joined.Count > 0)
            {
                AddSystemLine(string.Join(", ", joined.Select(x => x.Name?.ToString())) + (joined.Count == 1 ? " joins the conversation." : " join the conversation."));
                RefreshAttendees();
                await RequestRepliesAsync(joined, string.Empty, true);
            }

            bool advanceAutomatically = _session.RegisterCompletedExchange();
            if (advanceAutomatically)
            {
                await ReignServerClient.FinishSocialEventPhaseAsync(_session);
                if (_session.AdvancePhase())
                {
                    AddSystemLine("The event naturally moves to the next phase: " + _session.CurrentPhase.Title + ".");
                    RefreshFromSession();
                    return turn;
                }
            }

            RefreshFromSession();
            return turn;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    BusyText = string.Empty;
                    IsBusy = false;
                });
            }
        }

        private void ToggleAttendee(ReignSocialEventAttendeeVM attendee)
        {
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
                PhaseStatus = "Phase " + (_session.CurrentPhaseIndex + 1) + " of " + _session.Template.Phases.Count
                    + " | Attendees " + _session.Record.GetAttendees().Count()
                    + " | Conversation " + _session.PhaseExchangeCount + "/" + ReignSocialEventSession.TurnsPerAutomaticPhase;
                EventImageId = ReignEventArtTextureFactory.BuildImageId(_session.Template.TemplateId, _session.CurrentPhase.PhaseId);
                EventImageText = _session.Template.DisplayName + "\n" + _session.CurrentPhase.Title;
                EventBackgroundImageId = string.Empty;
            }
            RefreshAttendees();
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
