using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.UI;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using TaleWorlds.Library;

namespace ReignBeta.Court
{
    public sealed partial class ReignCourtCampaignBehavior : CampaignBehaviorBase
    {
        private List<CourtMatter> _matters = new List<CourtMatter>();
        private List<CourtAgendaItem> _agenda = new List<CourtAgendaItem>();
        private List<CourtOfficeAssignment> _offices = new List<CourtOfficeAssignment>();
        private List<AmbassadorPosting> _ambassadors = new List<AmbassadorPosting>();
        private List<KingdomCapitalDesignation> _capitalDesignations = new List<KingdomCapitalDesignation>();
        private List<ForeignAmbassadorPosting> _foreignAmbassadors = new List<ForeignAmbassadorPosting>();
        private List<CapitalAmbassadorTestLedger> _capitalAmbassadorTestLedgers = new List<CapitalAmbassadorTestLedger>();
        private List<string> _royalCouncilTestMarkers = new List<string>();
        private List<string> _rulerDocketTestMarkers = new List<string>();
        private List<IntelligenceOperation> _operations = new List<IntelligenceOperation>();
        private List<CourtObligation> _obligations = new List<CourtObligation>();
        private List<CourtPlot> _plots = new List<CourtPlot>();
        private List<CourtCounterSample> _counterSamples = new List<CourtCounterSample>();
        private List<CourtRegentAssignment> _regents = new List<CourtRegentAssignment>();
        private List<CastleRoomSessionRecord> _castleRoomSessions = new List<CastleRoomSessionRecord>();
        private List<CastleBathHistoryRecord> _castleBathHistory = new List<CastleBathHistoryRecord>();
        private List<string> _appliedNativeCommandIds = new List<string>();
        private CourtSession _session;
        private long _serverRevision;
        private bool _serverSessionOpened;
        private float _lastServerTickDay = -1000f;
        private bool _serverRequestInFlight;
        private readonly Dictionary<string, long> _serverMatterRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
        private bool _serverAvailable;
        private string _serverStatus = "Court server has not connected.";
        private int _advanceToDocketDay = -1;
        private string _economicReportStateJson = string.Empty;
        private ReignEconomicReportState _economicReportState = new ReignEconomicReportState();
        private string _spymasterStateJson = string.Empty;
        private ReignSpymasterState _spymasterState = new ReignSpymasterState();
        private readonly HashSet<string> _economicLetterRequestsInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _consecutiveAuthorityFailures;
        private string _lastAuthorityFailureReason = string.Empty;

        public static ReignCourtCampaignBehavior Instance { get; private set; }
        public CourtSession Session => _session;
        public IReadOnlyList<CourtMatter> Matters => _matters;
        public IReadOnlyList<CourtAgendaItem> Agenda => _agenda;
        public IReadOnlyList<CourtOfficeAssignment> Offices => _offices;
        public IReadOnlyList<AmbassadorPosting> Ambassadors => _ambassadors;
        public IReadOnlyList<KingdomCapitalDesignation> CapitalDesignations => _capitalDesignations;
        public IReadOnlyList<ForeignAmbassadorPosting> ForeignAmbassadors => _foreignAmbassadors;
        public IReadOnlyList<CapitalAmbassadorTestLedger> CapitalAmbassadorTestLedgers => _capitalAmbassadorTestLedgers;
        public IReadOnlyList<IntelligenceOperation> IntelligenceOperations => _operations;
        public IReadOnlyList<CourtObligation> Obligations => _obligations;
        public IReadOnlyList<CourtPlot> Plots => _plots;
        public IReadOnlyList<CourtCounterSample> CounterSamples => _counterSamples;
        public IReadOnlyList<CourtRegentAssignment> Regents => _regents;
        public IReadOnlyList<CastleRoomSessionRecord> CastleRoomSessions => _castleRoomSessions;
        public CourtRegentAssignment ActiveRegent => _regents.FirstOrDefault(x => x.IsActive);
        private bool HasOpenSession => _session != null && _session.State != ReignCourtSessionState.Inactive && _session.State != ReignCourtSessionState.Closed;
        public bool IsRuleModeActive => HasOpenSession && _session.Authority == ReignCourtAuthority.Royal && IsPlayerKingdomRuler();
        public bool ShouldKeepCourtScreenOpen => HasOpenSession
            && _session.Authority == ReignCourtAuthority.Royal
            && !ReignCourtAuthorityStability.ShouldInterrupt(_consecutiveAuthorityFailures);
        public bool HasRoyalCommandAccess => IsRuleModeActive && _session.Scope == ReignCourtScope.Capital;
        public bool IsCapitalSession => _session?.Scope == ReignCourtScope.Capital;
        public Settlement CurrentCapital => FindCurrentCapital();
        public bool ServerAvailable => _serverAvailable;
        public bool ServerSessionOpened => _serverSessionOpened;
        public bool ServerRequestInFlight => _serverRequestInFlight;
        public string ServerStatus => _serverStatus;
        public ReignEconomicReportState EconomicReportState => EnsureEconomicReportState();
        public ReignSpymasterState SpymasterState => EnsureSpymasterState();

        public event Action StateChanged;

        public void EnsureServerSessionAligned()
        {
            if (!IsRuleModeActive || (_serverAvailable && _serverSessionOpened) || _serverRequestInFlight) return;
            _ = OpenServerSessionAsync();
        }

        public override void RegisterEvents()
        {
            Instance = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnSpymasterPrisonerReleased);
            CampaignEvents.WarDeclared.AddNonSerializedListener(this, OnAmbassadorWarDeclared);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnCapitalSettlementOwnerChanged);
            CampaignEvents.CanHeroLeadPartyEvent.AddNonSerializedListener(this, OnCanForeignAmbassadorLeadParty);
            CampaignEvents.CanHeroLeadPartyEvent.AddNonSerializedListener(this, OnCanNobleVisitorLeadParty);
            CampaignEvents.CanMoveToSettlementEvent.AddNonSerializedListener(this, OnCanNobleVisitorMoveToSettlement);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
            {
                CompactCapitalAmbassadorTestLedgers();
                foreach (CastleRoomSessionRecord session in _castleRoomSessions ?? new List<CastleRoomSessionRecord>())
                {
                    if (session == null || string.IsNullOrWhiteSpace(session.TranscriptJson)) continue;
                    session.WriteTranscriptJson(session.TranscriptJson);
                }
                _economicReportStateJson = JsonConvert.SerializeObject(
                    _economicReportState ?? new ReignEconomicReportState(), Formatting.None);
                _spymasterStateJson = JsonConvert.SerializeObject(
                    EnsureSpymasterState(), Formatting.None);
                PrepareRulerDocketForSave();
            }
            dataStore.SyncData("_reignCourt_session", ref _session);
            dataStore.SyncData("_reignCourt_matters", ref _matters);
            dataStore.SyncData("_reignCourt_agenda", ref _agenda);
            dataStore.SyncData("_reignCourt_offices", ref _offices);
            dataStore.SyncData("_reignCourt_ambassadors", ref _ambassadors);
            dataStore.SyncData("_reignCourt_capitalDesignations", ref _capitalDesignations);
            dataStore.SyncData("_reignCourt_foreignAmbassadors", ref _foreignAmbassadors);
            dataStore.SyncData("_reignCourt_capitalAmbassadorTestLedgers", ref _capitalAmbassadorTestLedgers);
            dataStore.SyncData("_reignCourt_royalCouncilTestMarkers", ref _royalCouncilTestMarkers);
            dataStore.SyncData("_reignCourt_rulerDocketTestMarkers", ref _rulerDocketTestMarkers);
            dataStore.SyncData("_reignCourt_operations", ref _operations);
            dataStore.SyncData("_reignCourt_obligations", ref _obligations);
            dataStore.SyncData("_reignCourt_plots", ref _plots);
            dataStore.SyncData("_reignCourt_counterSamples", ref _counterSamples);
            dataStore.SyncData("_reignCourt_regents", ref _regents);
            dataStore.SyncData("_reignCourt_castleRoomSessions", ref _castleRoomSessions);
            dataStore.SyncData("_reignCourt_castleBathHistory", ref _castleBathHistory);
            dataStore.SyncData("_reignCourt_appliedNativeCommandIds", ref _appliedNativeCommandIds);
            dataStore.SyncData("_reignCourt_serverRevision", ref _serverRevision);
            dataStore.SyncData("_reignCourt_serverSessionOpened", ref _serverSessionOpened);
            dataStore.SyncData("_reignCourt_lastServerTickDay", ref _lastServerTickDay);
            dataStore.SyncData("_reignCourt_advanceToDocketDay", ref _advanceToDocketDay);
            dataStore.SyncData("_reignCourt_economicReportState", ref _economicReportStateJson);
            dataStore.SyncData("_reignCourt_spymasterState", ref _spymasterStateJson);
            dataStore.SyncData("_reignCourt_rulerDocketState", ref _rulerDocketStateJson);
            dataStore.SyncData("_reignCourt_rulerDocketStateChunks", ref _rulerDocketStateChunks);
            if (dataStore.IsLoading)
            {
                _royalCouncilTestMarkers = _royalCouncilTestMarkers ?? new List<string>();
                _rulerDocketTestMarkers = _rulerDocketTestMarkers ?? new List<string>();
                _castleRoomSessions = _castleRoomSessions ?? new List<CastleRoomSessionRecord>();
                _castleBathHistory = _castleBathHistory ?? new List<CastleBathHistoryRecord>();
                try
                {
                    _economicReportState = string.IsNullOrWhiteSpace(_economicReportStateJson)
                        ? new ReignEconomicReportState()
                        : JsonConvert.DeserializeObject<ReignEconomicReportState>(_economicReportStateJson)
                          ?? new ReignEconomicReportState();
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Economic report save state could not be restored: " + ex.Message);
                    _economicReportState = new ReignEconomicReportState();
                }
                try
                {
                    _spymasterState = string.IsNullOrWhiteSpace(_spymasterStateJson)
                        ? new ReignSpymasterState()
                        : JsonConvert.DeserializeObject<ReignSpymasterState>(_spymasterStateJson)
                          ?? new ReignSpymasterState();
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Spymaster save state could not be restored: " + ex.Message);
                    _spymasterState = new ReignSpymasterState();
                }
                RestoreRulerDocketAfterLoad();
            }
            _matters = (_matters ?? new List<CourtMatter>()).Where(x => x != null).ToList();
            _agenda = (_agenda ?? new List<CourtAgendaItem>()).Where(x => x != null).ToList();
            _offices = (_offices ?? new List<CourtOfficeAssignment>()).Where(x => x != null).ToList();
            _ambassadors = (_ambassadors ?? new List<AmbassadorPosting>()).Where(x => x != null).ToList();
            _capitalDesignations = (_capitalDesignations ?? new List<KingdomCapitalDesignation>()).Where(x => x != null).ToList();
            _foreignAmbassadors = (_foreignAmbassadors ?? new List<ForeignAmbassadorPosting>()).Where(x => x != null).ToList();
            _capitalAmbassadorTestLedgers = (_capitalAmbassadorTestLedgers ?? new List<CapitalAmbassadorTestLedger>())
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.RunId))
                .GroupBy(x => x.RunId, StringComparer.OrdinalIgnoreCase).Select(x => x.Last()).Take(32).ToList();
            CompactCapitalAmbassadorTestLedgers();
            _operations = (_operations ?? new List<IntelligenceOperation>()).Where(x => x != null).ToList();
            _obligations = (_obligations ?? new List<CourtObligation>()).Where(x => x != null).ToList();
            _plots = (_plots ?? new List<CourtPlot>()).Where(x => x != null).ToList();
            _counterSamples = (_counterSamples ?? new List<CourtCounterSample>()).Where(x => x != null).OrderBy(x => x.Day).ToList();
            _regents = (_regents ?? new List<CourtRegentAssignment>()).Where(x => x != null).ToList();
            _appliedNativeCommandIds = (_appliedNativeCommandIds ?? new List<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
            if (_appliedNativeCommandIds.Count > 500) _appliedNativeCommandIds = _appliedNativeCommandIds.GetRange(_appliedNativeCommandIds.Count - 500, 500);
        }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town", "reign_designate_capital", "{=!}Designate Capital",
                DesignateCapitalCondition, DesignateCapitalConsequence, false, 2);
            AddRuleModeOption(starter, "town", "reign_enter_rule_mode_town");
            AddRuleModeOption(starter, "castle", "reign_enter_rule_mode_castle");
            // Active-session validation is owned by campaign preparation.
        }

        internal void PrepareInitialState()
        {
            if (HasOpenSession && CurrentCapitalDesignation()?.EverDesignated != true)
                InterruptAndClose("Designate a capital before resuming Rule Mode.");
            else if (HasOpenSession) ValidateActiveSession(true);
            ProcessForeignAmbassadorTick();
        }

        private void AddRuleModeOption(CampaignGameStarter starter, string menuId, string optionId)
        {
            starter.AddGameMenuOption(menuId, optionId, "{=!}Enter Rule Mode", RuleModeCondition, RuleModeConsequence, false, 3);
        }

        private bool RuleModeCondition(MenuCallbackArgs args)
        {
            if (IsRuleModeActive)
            {
                args.optionLeaveType = GameMenuOption.LeaveType.Wait;
                return CurrentSettlement()?.StringId == _session.HostSettlementStringId;
            }
            if (!ReignBetaSettings.IsCourtSystemAvailable) return false;
            KingdomCapitalDesignation capital = CurrentCapitalDesignation();
            if (capital?.EverDesignated != true) return false;
            bool eligible = TryResolveAuthority(CurrentSettlement(), out _, out _);
            args.optionLeaveType = GameMenuOption.LeaveType.Wait;
            args.IsEnabled = eligible;
            return eligible;
        }

        private void RuleModeConsequence(MenuCallbackArgs args)
        {
            if (!IsRuleModeActive && !TryOpenSession(CurrentSettlement(), out string error))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + error));
                return;
            }
            // Rule Mode is hosted directly over the campaign map. Dismiss the
            // settlement menu without leaving the settlement so its buttons do
            // not remain visible or receive clicks behind the Court workspace.
            args.MapState?.ExitMenuMode();
            ReignCourtScreenManager.Open(this);
        }

        public bool TryOpenSession(Settlement settlement, out string error)
        {
            if (!ReignBetaSettings.IsCourtSystemAvailable)
            {
                error = "The Court System MCM option is disabled.";
                return false;
            }
            if (!TryResolveAuthority(settlement, out ReignCourtAuthority authority, out error)) return false;
            KingdomCapitalDesignation capital = CurrentCapitalDesignation();
            if (capital?.EverDesignated != true)
            {
                error = "Designate a capital from one of your kingdom's towns before entering Rule Mode.";
                return false;
            }
            ReignCourtScope scope = ResolveCourtScope(capital.SettlementStringId, settlement.StringId);
            int day = CurrentDay();
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            bool reuseRoyalDocket = authority == ReignCourtAuthority.Royal && _session != null && _session.Authority == ReignCourtAuthority.Royal
                && string.Equals(_session.CampaignId, campaignId, StringComparison.Ordinal) && string.Equals(_session.TimelineId, timelineId, StringComparison.Ordinal);
            if (reuseRoyalDocket)
            {
                _session.State = ReignCourtSessionState.Sitting;
                _session.HostSettlementStringId = settlement.StringId;
                _session.Scope = scope;
                _session.OpenedDay = CurrentDayFloat();
                _session.ClosedDay = -1f;
                _session.InterruptionReason = string.Empty;
                _session.ActiveMatterId = string.Empty;
                _session.TimeMode = 0;
                _session.LastPlayerSittingDay = day;
                _session.Revision++;
            }
            else _session = new CourtSession
            {
                State = ReignCourtSessionState.Sitting,
                Authority = authority,
                Scope = scope,
                HostSettlementStringId = settlement.StringId,
                CampaignId = campaignId,
                TimelineId = timelineId,
                OpenedDay = CurrentDayFloat(),
                LastAgendaDay = -1,
                LastPlayerSittingDay = day,
                Revision = 1,
                TimeMode = 0
            };
            if (CurrentHour() >= 8) BuildDawnAgenda(day);
            RecordCounterSample(day);
            PauseTime();
            error = string.Empty;
            ReignLog.Info("Rule Mode opened authority=" + authority + " scope=" + scope + " settlement=" + settlement.StringId + ".");
            StateChanged?.Invoke();
            _ = OpenServerSessionAsync();
            return true;
        }

        public void CloseSession(string reason)
        {
            if (_session == null) return;
            if (_serverSessionOpened) _ = CloseServerSessionAsync(reason);
            _session.State = ReignCourtSessionState.Closed;
            _session.ClosedDay = CurrentDayFloat();
            _session.InterruptionReason = reason ?? string.Empty;
            _session.ActiveMatterId = string.Empty;
            _session.TimeMode = 0;
            _session.Revision++;
            PauseTime();
            StateChanged?.Invoke();
        }

        public void PauseTime()
        {
            if (TaleWorlds.CampaignSystem.Campaign.Current != null) TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            if (_session != null) { _session.TimeMode = 0; _session.Revision++; }
            StateChanged?.Invoke();
        }

        public void PlayTime()
        {
            if (!CanAdvanceTime()) return;
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppablePlay;
            _session.TimeMode = 1;
            _session.Revision++;
            StateChanged?.Invoke();
        }

        public void FastForwardTime()
        {
            if (!CanAdvanceTime()) return;
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
            _session.TimeMode = 2;
            _session.Revision++;
            StateChanged?.Invoke();
        }

        public bool AdvanceToNextDocket()
        {
            if (!CanAdvanceTime()) return false;
            _advanceToDocketDay = CurrentDay() + 1;
            TaleWorlds.CampaignSystem.Campaign.Current.TimeControlMode = CampaignTimeControlMode.StoppableFastForward;
            _session.TimeMode = 2;
            _session.Revision++;
            StateChanged?.Invoke();
            return true;
        }

        public CourtMatter AdvanceToNextMatter()
        {
            CourtMatter next = _agenda.Where(x => x != null && x.AgendaDay <= CurrentDay() && x.Status != "completed")
                .OrderByDescending(x => x.IsUrgentInterruption).ThenBy(x => x.Slot)
                .Select(x => _matters.FirstOrDefault(m => m.MatterId == x.MatterId))
                .FirstOrDefault(x => x != null && !x.IsTerminal);
            if (next == null) return null;
            PauseTime();
            next.State = ReignCourtMatterState.Active;
            next.Revision++;
            _session.ActiveMatterId = next.MatterId;
            _session.State = ReignCourtSessionState.PausedForMatter;
            _session.Revision++;
            StateChanged?.Invoke();
            _ = StartServerMatterAsync(next);
            return next;
        }

        public async Task<string> BeginAudienceAsync(CourtMatter matter)
        {
            if (!IsRuleModeActive || matter == null || matter.IsTerminal) return "The audience matter is no longer available.";
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            await ReignMainThread.InvokeAsync(() =>
            {
                PauseTime();
                matter.State = ReignCourtMatterState.Active;
                matter.Revision++;
                _session.ActiveMatterId = matter.MatterId;
                _session.State = ReignCourtSessionState.InConversation;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            string startError = await StartServerMatterAsync(matter).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(startError)) return string.Empty;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (!matter.IsTerminal) matter.State = ReignCourtMatterState.Scheduled;
                _session.ActiveMatterId = string.Empty;
                _session.State = ReignCourtSessionState.PausedForMatter;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return startError;
        }

        public async Task<ReignCourtDialogueReply> RespondToAudienceAsync(CourtMatter matter, Hero speaker, string playerText)
        {
            return await RespondToAudienceAsync(
                matter,
                speaker,
                playerText,
                string.Empty).ConfigureAwait(false);
        }

        public async Task<ReignCourtDialogueReply> RespondToAudienceAsync(
            CourtMatter matter,
            Hero speaker,
            string playerText,
            string correlationId)
        {
            if (!_serverAvailable || !_serverSessionOpened)
                return new ReignCourtDialogueReply { Error = MutationUnavailable() };
            if (matter == null || matter.IsTerminal)
                return new ReignCourtDialogueReply { Error = "The audience matter is no longer available." };
            long revision = _serverMatterRevisions.TryGetValue(matter.MatterId, out long known) ? known : matter.Revision;
            string commandId = string.IsNullOrWhiteSpace(correlationId)
                ? "court_respond_" + Guid.NewGuid().ToString("N")
                : correlationId;
            ReignCourtDialogueReply reply = await ReignServerClient.RequestCourtMatterResponseAsync(matter, speaker, playerText,
                revision, commandId).ConfigureAwait(false);
            if (reply.Ok && reply.Revision > 0) _serverMatterRevisions[matter.MatterId] = reply.Revision;
            return reply;
        }

        public void EndAudience(CourtMatter matter)
        {
            if (_session == null) return;
            PauseTime();
            if (matter != null && !matter.IsTerminal && matter.State == ReignCourtMatterState.Active)
            {
                matter.State = ReignCourtMatterState.AwaitingDecision;
                matter.Revision++;
            }
            _session.State = ReignCourtSessionState.PausedForMatter;
            _session.ActiveMatterId = matter?.MatterId ?? _session.ActiveMatterId;
            _session.Revision++;
            StateChanged?.Invoke();
        }

        public CourtMatter CreateLiveTestMatter(Hero speaker, string runId)
        {
            if (!IsRuleModeActive || speaker == null || !speaker.IsAlive || speaker == Hero.MainHero)
                return null;
            string sourceKey = "live_test_court:" + (runId ?? string.Empty).Trim();
            CourtMatter existing = _matters.FirstOrDefault(matter =>
                matter != null
                && !matter.IsTerminal
                && string.Equals(matter.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;
            Settlement host = CurrentSettlement();
            CourtMatter created = new CourtMatter
            {
                SourceKey = sourceKey,
                Kind = ReignCourtMatterKind.PrivateCounsel,
                State = ReignCourtMatterState.Scheduled,
                Title = speaker.Name + " attends a test audience",
                Summary = "A clearly tagged disposable live-test audience. Use the production court prompt, session, dialogue, memory, relationship, and action paths. No native court consequence is implied by the fixture.",
                Confidentiality = "private",
                ParticipantHeroIdsCsv = speaker.StringId,
                WitnessHeroIdsCsv = string.Empty,
                SettlementStringId = host?.StringId ?? _session.HostSettlementStringId,
                KingdomStringId = speaker.Clan?.Kingdom?.StringId ?? string.Empty,
                CreatedDay = CurrentDayFloat(),
                DueDay = CurrentDayFloat() + 1f,
                ScheduledDay = CurrentDayFloat(),
                Priority = 1,
                IsAmbientRoleplay = true,
                RequiresServer = true,
                DecisionOptionsJson = "[]",
                DefaultOutcomeJson = "{}"
            };
            QueueMatter(created);
            CourtMatter queued = _matters.FirstOrDefault(matter =>
                matter != null
                && string.Equals(matter.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase));
            StateChanged?.Invoke();
            return queued;
        }

        public void RemoveLiveTestMatter(CourtMatter matter)
        {
            if (matter == null
                || !(matter.SourceKey ?? string.Empty).StartsWith(
                    "live_test_court:", StringComparison.OrdinalIgnoreCase))
                return;
            if (_session != null
                && string.Equals(_session.ActiveMatterId, matter.MatterId, StringComparison.OrdinalIgnoreCase))
            {
                _session.ActiveMatterId = string.Empty;
                _session.State = ReignCourtSessionState.Sitting;
                _session.Revision++;
            }
            _agenda.RemoveAll(item =>
                item != null
                && string.Equals(item.MatterId, matter.MatterId, StringComparison.OrdinalIgnoreCase));
            _serverMatterRevisions.Remove(matter.MatterId);
            _matters.RemoveAll(candidate =>
                candidate != null
                && string.Equals(candidate.MatterId, matter.MatterId, StringComparison.OrdinalIgnoreCase));
            StateChanged?.Invoke();
        }

        public async Task<string> ResolveDecisionAsync(string matterId, string optionId, string termsHash, bool confirmed, string resolutionMode = "personal")
        {
            if (!confirmed) return "The preview must be confirmed before any effect can be applied.";
            bool remoteRegentReply = string.Equals(resolutionMode, "regent_mail", StringComparison.OrdinalIgnoreCase);
            if (!_serverAvailable || (!_serverSessionOpened && !remoteRegentReply)) return "The court server is unavailable. The decision remains queued and no fallback effect was applied.";
            CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == matterId);
            if (matter == null || matter.IsTerminal) return "The matter is no longer pending.";
            long revision = _serverMatterRevisions.TryGetValue(matterId, out long known) ? known : matter.Revision;
            string commandId = "court_decision_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.ResolveMatterAsync(matterId, optionId, termsHash, revision, commandId).ConfigureAwait(false);
            if (!response.Ok)
            {
                if (response.Raw.Value<string>("error") == "revision_conflict") _serverMatterRevisions[matterId] = response.Raw.Value<long?>("currentRevision") ?? revision;
                return response.Error;
            }
            _serverMatterRevisions[matterId] = response.Revision;
            if (response.NativeActions.Count > 0)
            {
                NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
                string reportCommandId = commandId + "_native_report";
                ReignCourtServerResponse report = await ReignCourtServerClient.ResolveMatterAsync(matterId, optionId, termsHash, response.Revision,
                    reportCommandId, execution.Success ? "completed" : "failed", execution.Receipt).ConfigureAwait(false);
                if (!report.Ok) return "Native execution was " + (execution.Success ? "completed" : "rejected") + ", but the court receipt could not be recorded: " + report.Error;
                _serverMatterRevisions[matterId] = report.Revision;
                if (!execution.Success) return execution.Error;
                response = report;
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                if (matter.IsTerminal) return;
                matter.State = string.Equals(optionId, "refuse", StringComparison.OrdinalIgnoreCase) ? ReignCourtMatterState.Refused : ReignCourtMatterState.Resolved;
                matter.SelectedOptionId = optionId;
                matter.TermsHash = termsHash ?? string.Empty;
                matter.LastCommandId = commandId;
                matter.ResolvedDay = CurrentDayFloat();
                matter.ResolutionReceiptJson = response.Raw["receipt"]?.ToString(Newtonsoft.Json.Formatting.None) ?? "{}";
                matter.ResolutionMode = remoteRegentReply ? "regent_mail" : "personal";
                matter.Revision++;
                foreach (CourtAgendaItem item in _agenda.Where(x => x.MatterId == matter.MatterId)) item.Status = "completed";
                _session.ActiveMatterId = string.Empty;
                _session.State = remoteRegentReply ? ReignCourtSessionState.Closed : ReignCourtSessionState.Sitting;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public bool TryHandleRegentLetter(ReignLetter letter, Action onClosed)
        {
            if (letter == null || !string.Equals(letter.Source, "court_regent", StringComparison.OrdinalIgnoreCase)) return false;
            const string prefix = "court_regent:";
            if (string.IsNullOrWhiteSpace(letter.Reason) || !letter.Reason.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string matterId = letter.Reason.Substring(prefix.Length);
            CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == matterId && !x.IsTerminal);
            if (matter == null) return false;
            JArray options;
            try { options = JArray.Parse(string.IsNullOrWhiteSpace(matter.DecisionOptionsJson) ? "[]" : matter.DecisionOptionsJson); }
            catch { options = new JArray(); }
            List<InquiryElement> elements = options.OfType<JObject>().Select(x => new InquiryElement(
                x.Value<string>("optionId") ?? string.Empty,
                (x.Value<string>("label") ?? "Reply") + (string.IsNullOrWhiteSpace(x.Value<string>("description")) ? string.Empty : " — " + x.Value<string>("description")),
                null)).ToList();
            if (elements.Count == 0) return false;
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "A Major Matter from Your Regent",
                (letter.Body ?? matter.Summary) + "\n\nYour reply directs the outcome, but answering by letter does not count as personal presence at court.",
                elements,true,1,1,"Send Reply","Leave Unanswered",
                selected =>
                {
                    InquiryElement chosen = selected?.FirstOrDefault();
                    JObject option = options.OfType<JObject>().FirstOrDefault(x => string.Equals(x.Value<string>("optionId"), chosen?.Identifier as string, StringComparison.OrdinalIgnoreCase));
                    if (option != null)
                    {
                        _ = ResolveRegentReplyAsync(matter, option);
                    }
                    onClosed?.Invoke();
                },
                selected => onClosed?.Invoke(),string.Empty,false),true,false);
            return true;
        }

        private async Task ResolveRegentReplyAsync(CourtMatter matter, JObject option)
        {
            string error = await ResolveDecisionAsync(matter.MatterId, option.Value<string>("optionId") ?? string.Empty,
                option.Value<string>("termsHash") ?? string.Empty, true, "regent_mail").ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() => InformationManager.DisplayMessage(new InformationMessage(string.IsNullOrWhiteSpace(error)
                ? "[Bannerlord Reign] Your coded reply was sent to the Regent. Presence credit was not granted."
                : "[Bannerlord Reign] The Regent's matter remains pending: " + error))).ConfigureAwait(false);
        }

        public async Task<string> AssignOfficeAsync(ReignCourtOffice office, Hero hero)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            CourtOfficeAssignment active = _offices.FirstOrDefault(x => x.IsActive && x.Office == office);
            if (office == ReignCourtOffice.Spymaster && active != null
                && !string.Equals(active.HeroStringId, hero?.StringId, StringComparison.OrdinalIgnoreCase)
                && !CanChangeSpymasterOffice(out string activeMissionError)) return activeMissionError;
            if (!ReignCourtOfficeService.IsEligible(hero, _session.Authority, _offices.Where(x => x != active), out string eligibilityError)) return eligibilityError;
            long expectedRevision = active?.Revision ?? 0;
            string commandId = "court_office_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignOfficeAsync(office, hero, expectedRevision, commandId,
                NativeHeroEligibility(hero)).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            return await ReignMainThread.InvokeAsync(() =>
            {
                if (IsNobleVisitorReserved(hero)) return "This noble is committed to a court visit. Assign an office after the visit ends.";
                if (!ReignCourtOfficeService.TryAssign(_offices, office, hero, _session.Authority, commandId, expectedRevision, CurrentDayFloat(), out CourtOfficeAssignment assignment, out string error)) return error;
                assignment.AssignmentId = response.Raw.Value<string>("recordId") ?? assignment.AssignmentId;
                assignment.Revision = response.Revision;
                _session.Revision++;
                StateChanged?.Invoke();
                return string.Empty;
            }).ConfigureAwait(false);
        }

        public async Task<string> DismissOfficeAsync(CourtOfficeAssignment assignment)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (assignment == null || !assignment.IsActive) return "That office is already vacant.";
            if (assignment.Office == ReignCourtOffice.Spymaster && !CanChangeSpymasterOffice(out string activeMissionError))
                return activeMissionError;
            string commandId = "court_office_dismiss_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.DismissOfficeAsync(assignment.Office, assignment.Revision, commandId, "dismissed_by_ruler").ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            return await ReignMainThread.InvokeAsync(() =>
            {
                if (!ReignCourtOfficeService.TryDismiss(_offices, assignment.Office, commandId, assignment.Revision, CurrentDayFloat(), "dismissed_by_ruler", out string error)) return error;
                assignment.Revision = response.Revision;
                _session.Revision++;
                StateChanged?.Invoke();
                return string.Empty;
            }).ConfigureAwait(false);
        }

        public async Task<string> AssignRegentAsync(Hero hero)
        {
            if (_session?.Authority != ReignCourtAuthority.Royal) return "A Regent may be appointed only by a kingdom ruler.";
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            JObject eligibility = NativeHeroEligibility(hero);
            if (eligibility.Value<bool?>("isAdult") != true || eligibility.Value<bool?>("isFree") != true || eligibility.Value<bool?>("inCourtScope") != true)
                return "The Regent must be an adult, free, active member of the royal court scope.";
            CourtRegentAssignment active = ActiveRegent;
            long expectedRevision = active?.Revision ?? 0;
            string commandId = "court_regent_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignRegentAsync(hero, expectedRevision, commandId, eligibility).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (active != null)
                {
                    active.IsActive = false;
                    active.DismissedDay = CurrentDayFloat();
                    active.DismissalReason = "replaced";
                    active.Revision++;
                }
                _regents.Add(new CourtRegentAssignment
                {
                    AssignmentId = response.Raw.Value<string>("recordId") ?? "regent_" + Guid.NewGuid().ToString("N"),
                    HeroStringId = hero.StringId,AssignedDay = CurrentDayFloat(),Revision = response.Revision,LastCommandId = commandId
                });
                _session.Revision++; StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> DismissRegentAsync()
        {
            CourtRegentAssignment active = ActiveRegent;
            if (active == null) return "No Regent is assigned.";
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            string commandId = "court_regent_dismiss_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.DismissRegentAsync(active.Revision, commandId, "dismissed_by_ruler").ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                active.IsActive = false;active.DismissedDay = CurrentDayFloat();active.DismissalReason = "dismissed_by_ruler";
                active.Revision = response.Revision;active.LastCommandId = commandId;_session.Revision++;StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> AssignAmbassadorAsync(Hero hero, Kingdom target, string missionType)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (_session.Authority != ReignCourtAuthority.Royal) return "Ambassador postings require royal court authority.";
            if (_ambassadors.Count(x => x.Status != "recalled" && x.Status != "replaced") >= 3) return "All three ambassador postings are occupied.";
            JObject eligibility=NativeHeroEligibility(hero);
            if(eligibility.Value<bool?>("isAdult")!=true||eligibility.Value<bool?>("isFree")!=true||eligibility.Value<bool?>("inCourtScope")!=true)return "The ambassador must be an adult, free, active hero within royal court scope.";
            if (_ambassadors.Any(x => x.HeroStringId == hero.StringId && x.Status != "recalled" && x.Status != "replaced")) return "That hero already holds an ambassador posting.";
            string commandId = "court_ambassador_assign_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.AssignAmbassadorAsync(hero, target, missionType, 3f, 0, commandId, eligibility).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                _ambassadors.Add(new AmbassadorPosting
                {
                    PostingId = response.Raw.Value<string>("recordId") ?? "ambassador_" + Guid.NewGuid().ToString("N"),
                    HeroStringId = hero.StringId,
                    TargetKingdomStringId = target.StringId,
                    MissionType = missionType ?? "public_information",
                    Status = "traveling",
                    AssignedDay = CurrentDayFloat(),
                    ArrivalDay = CurrentDayFloat() + 3f,
                    Revision = response.Revision,
                    LastCommandId = commandId
                });
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SetAmbassadorMissionAsync(AmbassadorPosting posting, string missionType, JObject terms)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (posting == null || posting.Status == "recalled" || posting.Status == "replaced") return "That posting is no longer active.";
            terms = terms ?? new JObject();
            string hash = ReignCourtTerms.Hash(terms.ToString(Newtonsoft.Json.Formatting.None));
            string commandId = "court_ambassador_mission_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.SetAmbassadorMissionAsync(posting, missionType, terms, hash, posting.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                posting.MissionType = missionType;
                posting.MissionTermsJson = terms.ToString(Newtonsoft.Json.Formatting.None);
                posting.Status = "posted";
                posting.Revision = response.Revision;
                posting.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> RecallAmbassadorAsync(AmbassadorPosting posting)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (posting == null || posting.Status == "recalled") return "That ambassador has already been recalled.";
            string commandId = "court_ambassador_recall_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.RecallAmbassadorAsync(posting, posting.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                posting.Status = "recalled";
                posting.RecalledDay = CurrentDayFloat();
                posting.Revision = response.Revision;
                posting.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> DeliverAmbassadorProposalAsync(AmbassadorPosting posting,ReignWorldActionType type,JObject terms)
        {
            if(posting==null)return "An active ambassador posting is required.";
            Kingdom ours=Clan.PlayerClan?.Kingdom,target=Kingdom.All.FirstOrDefault(x=>x.StringId==posting.TargetKingdomStringId);
            if(ours?.Leader==null||target?.Leader==null)return "Both realms must have current rulers.";
            string missionError=await SetAmbassadorMissionAsync(posting,"deliver_proposal",new JObject{{"actionType",type.ToString()},{"terms",terms??new JObject()}}).ConfigureAwait(false);
            if(!string.IsNullOrWhiteSpace(missionError))return missionError;
            return await SubmitDiplomaticProposalAsync(new ReignWorldActionRecord
            {
                Type=type,ActorHeroStringId=ours.Leader.StringId,ActorKingdomStringId=ours.StringId,TargetHeroStringId=target.Leader.StringId,TargetKingdomStringId=target.StringId,
                Reason="The posted ambassador delivers a package authorized by the royal court.",TermsJson=(terms??new JObject()).ToString(Newtonsoft.Json.Formatting.None)
            }).ConfigureAwait(false);
        }

        public async Task<string> StartIntelligenceOperationAsync(string operationType, string targetType, string targetId, int goldCost, float influenceCost, float duration, float risk)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            CourtOfficeAssignment spymasterAssignment=_offices.FirstOrDefault(x=>x.IsActive&&x.Office==ReignCourtOffice.Spymaster);
            if (spymasterAssignment==null) return "Appoint a Spymaster before starting advanced operations.";
            ReignCourtIntelligenceQuote quote=GetIntelligenceQuote(goldCost,influenceCost,duration,risk);
            goldCost=quote.GoldCost;influenceCost=quote.InfluenceCost;duration=quote.DurationDays;risk=quote.Risk;
            if ((Hero.MainHero?.Gold ?? 0) < goldCost) return "The player clan cannot afford this operation.";
            if ((Clan.PlayerClan?.Influence ?? 0f) < influenceCost) return "The player clan lacks the required influence.";
            string commandId = "court_intelligence_start_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.StartIntelligenceAsync(operationType, targetType, targetId, goldCost, influenceCost, duration, risk, quote.ReportReliability, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                if (goldCost > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, goldCost, true);
                if (influenceCost > 0f) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -influenceCost);
                _operations.Add(new IntelligenceOperation
                {
                    OperationId = response.Raw.Value<string>("recordId") ?? "intel_" + Guid.NewGuid().ToString("N"),
                    OperationType = operationType,
                    TargetType = targetType,
                    TargetStringId = targetId,
                    State = ReignIntelligenceOperationState.Active,
                    GoldCost = goldCost,
                    InfluenceCost = influenceCost,
                    StartedDay = CurrentDayFloat(),
                    DueDay = CurrentDayFloat() + duration,
                    Risk = risk,
                    Revision = response.Revision,
                    LastCommandId = commandId
                });
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public ReignCourtIntelligenceQuote GetIntelligenceQuote(int goldCost,float influenceCost,float duration,float risk)
        {
            CourtOfficeAssignment assignment=_offices.FirstOrDefault(x=>x.IsActive&&x.Office==ReignCourtOffice.Spymaster);Hero hero=FindHero(assignment?.HeroStringId);
            return ReignCourtOfficePerformanceService.QuoteIntelligence(ReignCourtOfficePerformanceService.Evaluate(ReignCourtOffice.Spymaster,hero,_session),goldCost,influenceCost,duration,risk);
        }

        public async Task<string> CancelIntelligenceOperationAsync(IntelligenceOperation operation)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (operation == null || operation.State != ReignIntelligenceOperationState.Active) return "Only an active operation may be cancelled.";
            string commandId = "court_intelligence_cancel_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.CancelIntelligenceAsync(operation, operation.Revision, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                operation.State = ReignIntelligenceOperationState.Cancelled;
                operation.Revision = response.Revision;
                operation.LastCommandId = commandId;
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitWorldActionAsync(ReignWorldActionRecord action)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (action == null) return "No court plan was supplied.";
            if(action.TypeValue>0&&action.TypeValue<100)return "Diplomatic packages must be sent through the foreign-ruler proposal path.";
            action.Source = "court_player_plan";
            action.RequiresAcceptance = false;
            if (!ReignActionValidator.Validate(action, out string validationError)) return validationError;
            string commandId = "court_world_action_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.ValidateWorldActionAsync(action, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
            if (!execution.Success) return execution.Error;
            await ReignMainThread.InvokeAsync(() =>
            {
                _session.Revision++;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitDiplomaticProposalAsync(ReignWorldActionRecord action)
        {
            if(!_serverAvailable||!_serverSessionOpened)return MutationUnavailable();
            if(_session.Authority!=ReignCourtAuthority.Royal)return "Diplomatic proposals require royal court authority.";
            if(action==null||action.TypeValue<=0||action.TypeValue>=100)return "A supported diplomatic package is required.";
            action.Source="court_player_diplomacy";action.RequiresAcceptance=true;
            if(!ReignActionValidator.Validate(action,out string validationError))return validationError;
            string commandId="court_diplomacy_"+Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response=await ReignCourtServerClient.ProposeDiplomaticActionAsync(action,commandId).ConfigureAwait(false);
            if(!response.Ok)return response.Error;
            string responseKind=response.Raw.Value<string>("responseKind")??"refuse",reason=response.Raw.Value<string>("publicReason")??string.Empty;
            if(response.NativeActions.Count>0)
            {
                NativeCourtExecution execution=await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
                if(!execution.Success)return execution.Error;
                await ReignMainThread.InvokeAsync(()=>{_session.Revision++;StateChanged?.Invoke();}).ConfigureAwait(false);
                return string.Empty;
            }
            await SyncServerHomeAsync().ConfigureAwait(false);
            return responseKind=="counter"?"A counteroffer was added to the court agenda: "+reason:"The foreign ruler refused: "+reason;
        }

        public async Task<string> TransferSupplyAsync(ReignSupplyTransferRequest request)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            ReignCourtServerResponse response=await ReignCourtServerClient.ValidateSupplyTransferAsync(request).ConfigureAwait(false);
            if(!response.Ok)return response.Error;
            NativeCourtExecution execution=await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
            if(!execution.Success)return execution.Error;
            await ReignMainThread.InvokeAsync(()=>{_session.Revision++;StateChanged?.Invoke();}).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> SubmitNativeActionAsync(JObject action)
        {
            if (!_serverAvailable || !_serverSessionOpened) return MutationUnavailable();
            if (action == null) return "No native court action was supplied.";
            string commandId = "court_native_" + Guid.NewGuid().ToString("N");
            ReignCourtServerResponse response = await ReignCourtServerClient.ValidateNativeActionAsync(action, commandId).ConfigureAwait(false);
            if (!response.Ok) return response.Error;
            NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false);
            if (!execution.Success) return execution.Error;
            await ReignMainThread.InvokeAsync(() => { _session.Revision++; StateChanged?.Invoke(); }).ConfigureAwait(false);
            return string.Empty;
        }

        public async Task<string> RecordGoldPromiseAsync(Hero recipient,int amount,float dueInDays)
        {
            if(!_serverAvailable||!_serverSessionOpened)return MutationUnavailable();
            if(recipient==null||recipient==Hero.MainHero||amount<=0)return "A promise requires another current hero and a positive amount.";
            string id="obligation_"+Guid.NewGuid().ToString("N"),commandId="court_obligation_"+Guid.NewGuid().ToString("N");
            JObject terms=new JObject{{"goldAmount",amount},{"currency","denars"}};
            JObject breach=new JObject{{"effect","obligation_breached"},{"reason","failed_gold_promise"}};
            ReignCourtServerResponse response=await ReignCourtServerClient.UpsertObligationAsync(id,"promise",Hero.MainHero,recipient,
                "Pay "+amount+" denars to "+recipient.Name+".",CurrentDayFloat()+Math.Max(1f,dueInDays),terms,breach,"private",0,commandId).ConfigureAwait(false);
            if(!response.Ok)return response.Error;
            await SyncServerHomeAsync().ConfigureAwait(false);
            return string.Empty;
        }

        public async Task RefreshServerTabAsync(string tab)
        {
            if(!_serverAvailable||!_serverSessionOpened)return;
            ReignCourtServerResponse response=await ReignCourtServerClient.GetTabAsync(tab).ConfigureAwait(false);if(!response.Ok)return;
            await ReignMainThread.InvokeAsync(()=>
            {
                foreach(JObject row in (response.Raw["obligations"] as JArray??new JArray()).OfType<JObject>())UpsertServerObligation(row);
                foreach(JObject row in (response.Raw["plots"] as JArray??new JArray()).OfType<JObject>())UpsertServerPlot(row);
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        public bool TrySelectDecision(string matterId, string optionId, string commandId, long expectedRevision, string termsHash, bool confirmed, out string error)
        {
            error = "Court decisions require the asynchronous server-validated command and native-receipt path.";
            return false;
        }

        public bool TryAssignOffice(ReignCourtOffice office, Hero hero, string commandId, long expectedRevision, out string error)
        {
            error = "Court office mutations require the asynchronous server-validated command path.";
            return false;
        }

        private async Task<NativeCourtExecution> ExecuteNativeActionsAsync(JArray actions)
        {
            return await ReignMainThread.InvokeAsync(() =>
            {
                JArray receipts = new JArray();
                try
                {
                    foreach (JObject action in (actions ?? new JArray()).OfType<JObject>())
                    {
                        string type = action.Value<string>("type") ?? string.Empty;
                        string nativeCommandId = action.Value<string>("commandId") ?? string.Empty;
                        JObject receipt = new JObject { ["type"] = type };
                        if (!string.IsNullOrWhiteSpace(nativeCommandId) && _appliedNativeCommandIds.Contains(nativeCommandId))
                        {
                            receipt["ok"] = true;
                            receipt["idempotentReplay"] = true;
                            receipt["commandId"] = nativeCommandId;
                            receipts.Add(receipt);
                            continue;
                        }
                        switch (type)
                        {
                            case "world_action":
                            {
                                JObject encoded = action["action"] as JObject ?? action["worldAction"] as JObject;
                                ReignWorldActionRecord worldAction = ReignServerClient.ParseCourtWorldAction(encoded);
                                if (worldAction == null) throw new InvalidOperationException("The validated world action could not be decoded.");
                                worldAction.Source = "court_decision";
                                worldAction.RequiresAcceptance = false;
                                if (!ReignActionValidator.Validate(worldAction, out string validationError)) throw new InvalidOperationException(validationError);
                                if (ReignAICampaignBehavior.Instance?.EnqueueAndExecuteServerActions(new[] { worldAction }) != 1) throw new InvalidOperationException("The authoritative world action queue rejected the plan.");
                                ReignActionResult result = ReignAICampaignBehavior.Instance.GetLastActionResult(worldAction.ActionId);
                                if (result != null && !result.Success) throw new InvalidOperationException(result.Message);
                                receipt["actionId"] = worldAction.ActionId;
                                receipt["result"] = result?.ResultCode ?? "queued";
                                break;
                            }
                            case "spend_player_gold":
                            {
                                int amount = Math.Max(0, action.Value<int?>("amount") ?? 0);
                                if ((Hero.MainHero?.Gold ?? 0) < amount) throw new InvalidOperationException("The player can no longer afford the validated cost.");
                                if (amount > 0) GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount, true);
                                receipt["amount"] = amount;
                                break;
                            }
                            case "spend_clan_influence":
                            {
                                float amount = Math.Max(0f, action.Value<float?>("amount") ?? 0f);
                                if ((Clan.PlayerClan?.Influence ?? 0f) < amount) throw new InvalidOperationException("The player clan no longer has the validated influence.");
                                if (amount > 0f) ChangeClanInfluenceAction.Apply(Clan.PlayerClan, -amount);
                                receipt["amount"] = amount;
                                break;
                            }
                            case "transfer_gold":
                            {
                                Hero from = FindHero(action.Value<string>("fromHeroStringId"));
                                Hero to = FindHero(action.Value<string>("toHeroStringId"));
                                int amount = Math.Max(0, action.Value<int?>("amount") ?? 0);
                                if (from == null || to == null || from == to || amount <= 0) throw new InvalidOperationException("A gift requires two distinct current heroes and a positive amount.");
                                if (from != Hero.MainHero) throw new InvalidOperationException("Court gifts may spend only player-clan spendable gold.");
                                if (from.Gold < amount) throw new InvalidOperationException("The player can no longer afford the validated gift.");
                                GiveGoldAction.ApplyBetweenCharacters(from, to, amount, true);
                                receipt["fromHeroStringId"] = from.StringId;
                                receipt["toHeroStringId"] = to.StringId;
                                receipt["amount"] = amount;
                                break;
                            }
                            case "transfer_item":
                            {
                                ReignSupplyTransferResult transfer = ReignCourtSupplyService.ApplyTransfer(new ReignSupplyTransferRequest
                                {
                                    CommandId = action.Value<string>("commandId") ?? "court_transfer_" + Guid.NewGuid().ToString("N"),
                                    SourceSettlementStringId = action.Value<string>("sourceSettlementStringId") ?? string.Empty,
                                    TargetSettlementStringId = action.Value<string>("targetSettlementStringId") ?? string.Empty,
                                    ItemStringId = action.Value<string>("itemStringId") ?? string.Empty,
                                    ItemAmount = action.Value<int?>("amount") ?? 0,
                                    GoldAmount = action.Value<int?>("goldAmount") ?? 0,
                                    VassalConsentGranted = action.Value<bool?>("vassalConsentGranted") == true,
                                    GoldPayerKind = action.Value<string>("goldPayerKind") ?? string.Empty,
                                    GoldPayerStringId = action.Value<string>("goldPayerStringId") ?? string.Empty,
                                    GoldRecipientKind = action.Value<string>("goldRecipientKind") ?? string.Empty,
                                    GoldRecipientStringId = action.Value<string>("goldRecipientStringId") ?? string.Empty
                                }, _session.Authority);
                                if (!transfer.Success) throw new InvalidOperationException(transfer.Error);
                                receipt["receiptId"] = transfer.ReceiptId;
                                break;
                            }
                            case "appoint_governor":
                            {
                                Town town = Settlement.Find(action.Value<string>("settlementStringId") ?? string.Empty)?.Town;
                                Hero hero = FindHero(action.Value<string>("heroStringId"));
                                if (town == null || hero == null || town.OwnerClan != Clan.PlayerClan || hero.Clan != Clan.PlayerClan || hero.IsPrisoner)
                                    throw new InvalidOperationException("The governor appointment no longer satisfies native eligibility.");
                                ChangeGovernorAction.Apply(town, hero);
                                receipt["settlementStringId"] = town.StringId;
                                receipt["heroStringId"] = hero.StringId;
                                break;
                            }
                            case "fund_construction":
                            {
                                Town town = Settlement.Find(action.Value<string>("settlementStringId") ?? string.Empty)?.Town;
                                int amount = Math.Max(0, action.Value<int?>("goldAmount") ?? 0);
                                if (town == null || town.OwnerClan != Clan.PlayerClan || amount <= 0) throw new InvalidOperationException("Construction funding requires a player-clan town or castle and a positive amount.");
                                if ((Hero.MainHero?.Gold ?? 0) < amount) throw new InvalidOperationException("The player can no longer afford the validated construction funding.");
                                int oldBoost = town.BoostBuildingProcess;
                                int oldGold = Hero.MainHero.Gold;
                                try
                                {
                                    GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, null, amount, true);
                                    town.BoostBuildingProcess += amount;
                                }
                                catch
                                {
                                    town.BoostBuildingProcess = oldBoost;
                                    int restore = oldGold - Hero.MainHero.Gold;
                                    if (restore > 0) GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, restore, false);
                                    throw;
                                }
                                receipt["settlementStringId"] = town.StringId;
                                receipt["goldAmount"] = amount;
                                receipt["boostBuildingProcess"] = town.BoostBuildingProcess;
                                break;
                            }
                            case "change_relation":
                            {
                                Hero from = FindHero(action.Value<string>("fromHeroStringId")) ?? Hero.MainHero;
                                Kingdom target = Kingdom.All.FirstOrDefault(x => x.StringId == (action.Value<string>("toKingdomStringId") ?? string.Empty));
                                Hero to = target?.Leader;
                                int amount = action.Value<int?>("amount") ?? 0;
                                if (from != Hero.MainHero || to == null || amount < 1 || amount > 2) throw new InvalidOperationException("The ambassador relation change no longer satisfies its bounded native terms.");
                                ChangeRelationAction.ApplyRelationChangeBetweenHeroes(from, to, amount, true);
                                receipt["fromHeroStringId"] = from.StringId;
                                receipt["toHeroStringId"] = to.StringId;
                                receipt["amount"] = amount;
                                break;
                            }
                            case "rebellion_response":
                            {
                                string result = string.Empty;
                                if (ReignRebellionCampaignBehavior.Instance?.TryResolvePlayerUltimatum(action.Value<string>("movementId"), action.Value<string>("response"), out result) != true)
                                    throw new InvalidOperationException(string.IsNullOrWhiteSpace(result) ? "The rebellion response was rejected." : result);
                                receipt["result"] = result;
                                break;
                            }
                            case "marriage_response":
                            {
                                Hero first = FindHero(action.Value<string>("heroAId"));
                                Hero second = FindHero(action.Value<string>("heroBId"));
                                if (first == null || second == null || TaleWorlds.CampaignSystem.Campaign.Current?.Models?.MarriageModel?.IsCoupleSuitableForMarriage(first, second) != true)
                                    throw new InvalidOperationException("The marriage proposal no longer passes the native marriage model.");
                                MarriageAction.Apply(first, second, true);
                                receipt["heroAId"] = first.StringId;
                                receipt["heroBId"] = second.StringId;
                                break;
                            }
                            default:
                                throw new InvalidOperationException("No authoritative client executor exists for court native action '" + type + "'.");
                        }
                        if (!string.IsNullOrWhiteSpace(nativeCommandId) && !_appliedNativeCommandIds.Contains(nativeCommandId))
                        {
                            _appliedNativeCommandIds.Add(nativeCommandId);
                            if (_appliedNativeCommandIds.Count > 500) _appliedNativeCommandIds.RemoveRange(0, _appliedNativeCommandIds.Count - 500);
                        }
                        receipt["ok"] = true;
                        receipts.Add(receipt);
                    }
                    return new NativeCourtExecution { Success = true, Receipt = new JObject { ["ok"] = true, ["actions"] = receipts } };
                }
                catch (Exception ex)
                {
                    return new NativeCourtExecution { Success = false, Error = ex.Message, Receipt = new JObject { ["ok"] = false, ["error"] = ex.Message, ["actions"] = receipts } };
                }
            }).ConfigureAwait(false);
        }

        private JObject NativeHeroEligibility(Hero hero)
        {
            bool inScope = hero != null && (hero.Clan == Clan.PlayerClan || hero.CompanionOf == Clan.PlayerClan || ReignCourtNobleCampaignBehavior.IsCourtNoble(hero)
                || _session.Authority == ReignCourtAuthority.Royal && hero.Clan?.Kingdom == Clan.PlayerClan?.Kingdom);
            return new JObject
            {
                ["isAdult"] = hero != null && hero.Age >= TaleWorlds.CampaignSystem.Campaign.Current.Models.AgeModel.HeroComesOfAge,
                ["isFree"] = hero != null && hero.IsAlive && hero.IsActive && !hero.IsPrisoner,
                ["inCourtScope"] = inScope,
                ["isAbsent"] = hero?.CurrentSettlement?.StringId != _session.HostSettlementStringId,
                ["hasConflictingNativeDuty"] = hero?.GovernorOf != null || hero?.PartyBelongedTo?.Army != null
            };
        }

        private string MutationUnavailable()
        {
            return "The court server is unavailable. Native reports and time controls remain available, but mutations stay queued.";
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id) ? null : Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private sealed class NativeCourtExecution
        {
            public bool Success;
            public string Error = string.Empty;
            public JObject Receipt = new JObject();
        }

        private void OnDailyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (!ReignBetaSettings.IsCourtSystemAvailable)
            {
                if (HasOpenSession) InterruptAndClose("The Court System option was disabled.");
                return;
            }
            if (!IsPlayerKingdomRuler())
            {
                string reason = "Only the current ruler of a kingdom may hold court.";
                if (HasOpenSession && ObserveAuthorityFailure(reason)) InterruptAndClose(reason);
                return;
            }
            ResetAuthorityFailures();
            int day = CurrentDay();
            bool playerWasSitting = IsRuleModeActive || (_session != null && _session.LastPlayerSittingDay == day - 1);
            if (IsRuleModeActive && !ValidateActiveSession(false)) playerWasSitting = false;
            EnsureRoyalDocket();
            if (IsRuleModeActive) _session.LastPlayerSittingDay = day;
            RecordCounterSample(day);
            if (!_serverSessionOpened && !_serverRequestInFlight) _ = OpenServerSessionAsync();
            if (_serverSessionOpened && !_serverRequestInFlight && day > _lastServerTickDay)
            {
                _lastServerTickDay = day;
                _ = TickServerSessionAsync();
            }
            ProcessSpymasterDailyTick();
            ProcessForeignAmbassadorTick();
            ProcessRulerDocketDailyTick(day);
        }

        private void OnHourlyTick()
        {
            if (ReignCampaignInitializationGate.IsPending) return;
            if (!ReignBetaSettings.IsCourtSystemAvailable)
            {
                if (HasOpenSession) InterruptAndClose("The Court System option was disabled.");
                return;
            }
            if (!IsPlayerKingdomRuler())
            {
                string reason = "Only the current ruler of a kingdom may hold court.";
                if (HasOpenSession && ObserveAuthorityFailure(reason)) InterruptAndClose(reason);
                return;
            }
            ResetAuthorityFailures();
            EnsureRoyalDocket();
            EnsureEconomicReportState();
            ProcessEconomicShipments();
            ProcessForeignAmbassadorTick();
            EnforceResidentAdvisorLocations();
            int day = CurrentDay();
            int hour = CurrentHour();
            ProcessDocketCommitments(day);
            ProcessSoldierExpeditionReturns(day);
            ProcessRulerDocketHourlyTick(day, hour);
            if (hour >= 8 && _session.LastAgendaDay < day)
            {
                bool playerWasSitting = IsRuleModeActive || _session.LastPlayerSittingDay == day - 1;
                BuildDawnAgenda(day, playerWasSitting);
            }
            if (_advanceToDocketDay >= 0 && day >= _advanceToDocketDay && hour >= 8)
            {
                _advanceToDocketDay = -1;
                PauseTime();
            }
        }

        public ReignEconomicReportState EnsureEconomicReportState()
        {
            _economicReportState = _economicReportState ?? new ReignEconomicReportState();
            _economicReportState.Settlements = _economicReportState.Settlements ?? new List<ReignSettlementSupplySnapshot>();
            _economicReportState.Shipments = (_economicReportState.Shipments ?? new List<ReignEconomicShipment>())
                .Where(x => x != null).ToList();
            int day = CurrentDay();
            if (_economicReportState.Settlements.Count == 0 || (CurrentHour() >= 8 && _economicReportState.SnapshotDay < day))
            {
                _economicReportState.SnapshotDay = day;
                _economicReportState.SnapshotCreatedDay = CurrentDayFloat();
                _economicReportState.Settlements = ReignCourtSupplyService
                    .GetScopedSnapshots(_session?.Authority ?? ReignCourtAuthority.Royal)
                    .OrderByDescending(x => string.Equals(x.OwnerClanStringId, Clan.PlayerClan?.StringId, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Name)
                    .ToList();
            }
            return _economicReportState;
        }

        public ReignEconomicShipment SubmitEconomicFoodTransfer(string sourceId, string targetId, int amount, string notes)
        {
            EnsureEconomicReportState();
            Settlement source = FindEconomicSettlement(sourceId);
            Settlement target = FindEconomicSettlement(targetId);
            if (source == null || target == null || source == target || source.Town == null || target.Town == null)
                throw new InvalidOperationException("Choose two different current towns or castles.");
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            if (playerKingdom == null || source.OwnerClan?.Kingdom != playerKingdom)
                throw new InvalidOperationException("The origin must belong to the player's kingdom.");
            if (amount <= 0) throw new InvalidOperationException("Choose a positive food amount.");
            if (source.Town.FoodStocks + 0.001f < amount)
                throw new InvalidOperationException("The origin does not have that much food supply.");

            float now = CurrentDayFloat();
            float distance = (float)Math.Sqrt(source.GetPosition2D.DistanceSquared(target.GetPosition2D));
            float travelDays = Math.Max(0.175f, distance / 50f);
            int spoilagePercent = MBRandom.RandomInt(5, 26);
            float roadSpoilage = amount * spoilagePercent / 100f;
            Clan sourceClan = source.OwnerClan;
            Clan targetClan = target.OwnerClan;
            ReignEconomicShipment shipment = new ReignEconomicShipment
            {
                ShipmentKind = "food_stock_v1",
                ShipmentId = "economic_food_" + Guid.NewGuid().ToString("N"),
                SourceSettlementStringId = source.StringId,
                TargetSettlementStringId = target.StringId,
                SourceSettlementName = source.Name?.ToString() ?? source.StringId,
                TargetSettlementName = target.Name?.ToString() ?? target.StringId,
                ItemName = "Food supply",
                Amount = amount,
                RequestedFood = amount,
                SpoilagePercent = spoilagePercent,
                RoadSpoilage = roadSpoilage,
                SurvivingFood = Math.Max(0f, amount - roadSpoilage),
                SourceFoodBefore = source.Town.FoodStocks,
                Notes = (notes ?? string.Empty).Trim(),
                RequestedDay = now,
                ArrivalDay = now + travelDays,
                Status = "In transit",
                SourceClanStringId = sourceClan?.StringId ?? string.Empty,
                TargetClanStringId = targetClan?.StringId ?? string.Empty,
                SourceLeaderHeroStringId = sourceClan?.Leader?.StringId ?? string.Empty,
                TargetLeaderHeroStringId = targetClan?.Leader?.StringId ?? string.Empty,
                SourceClanKnownHeroIds = LivingClanHeroIds(sourceClan),
                TargetClanKnownHeroIds = LivingClanHeroIds(targetClan),
                SourceLetterStatus = RequiresShipmentLetter(sourceClan?.Leader, sourceClan) ? "Pending" : "NotRequired",
                TargetLetterStatus = RequiresShipmentLetter(targetClan?.Leader, targetClan) ? "Pending" : "NotRequired"
            };
            source.Town.FoodStocks = Math.Max(0f, source.Town.FoodStocks - amount);
            shipment.SourceFoodAfterDispatch = source.Town.FoodStocks;
            _economicReportState.Shipments.Add(shipment);
            RefreshEconomicSnapshots();
            RecordFoodShipmentKnowledge(shipment, "dispatched", "source", source, target);
            RecordFoodShipmentKnowledge(shipment, "dispatched", "destination", source, target);
            QueueEconomicShipmentLetters(shipment);
            StateChanged?.Invoke();
            return shipment;
        }

        private void ProcessEconomicShipments()
        {
            List<ReignEconomicShipment> shipments = (_economicReportState?.Shipments ?? new List<ReignEconomicShipment>())
                .Where(x => x != null).ToList();
            foreach (ReignEconomicShipment shipment in shipments.Where(x => x.IsFoodStockTransfer))
                QueueEconomicShipmentLetters(shipment);

            float now = CurrentDayFloat();
            bool changed = false;
            foreach (ReignEconomicShipment shipment in shipments.Where(x => !x.IsTerminal).ToList())
            {
                if (!shipment.IsFoodStockTransfer)
                {
                    changed |= ProcessLegacyEconomicShipment(shipment, now);
                    continue;
                }
                if (!string.Equals(shipment.Status, "In transit", StringComparison.OrdinalIgnoreCase) || now < shipment.ArrivalDay)
                    continue;
                Settlement source = FindEconomicSettlement(shipment.SourceSettlementStringId);
                Settlement target = FindEconomicSettlement(shipment.TargetSettlementStringId);
                if (target?.Town == null)
                {
                    shipment.Status = "Failed";
                    shipment.Error = "The destination no longer exists; the dispatched food was lost.";
                    RecordFoodShipmentKnowledge(shipment, "failed", "source", source, target);
                    RecordFoodShipmentKnowledge(shipment, "failed", "destination", source, target);
                    changed = true;
                    continue;
                }
                shipment.TargetFoodBeforeDelivery = target.Town.FoodStocks;
                float freeCapacity = Math.Max(0f, target.Town.FoodStocksUpperLimit() - target.Town.FoodStocks);
                shipment.DeliveredFood = Math.Min(Math.Max(0f, shipment.SurvivingFood), freeCapacity);
                shipment.CapacityOverflow = Math.Max(0f, shipment.SurvivingFood - shipment.DeliveredFood);
                target.Town.FoodStocks += shipment.DeliveredFood;
                shipment.TargetFoodAfterDelivery = target.Town.FoodStocks;
                shipment.Status = "Delivered";
                shipment.Error = string.Empty;
                RecordFoodShipmentKnowledge(shipment, "delivered", "source", source, target);
                RecordFoodShipmentKnowledge(shipment, "delivered", "destination", source, target);
                changed = true;
            }
            if (changed)
            {
                RefreshEconomicSnapshots();
                StateChanged?.Invoke();
            }
        }

        private bool ProcessLegacyEconomicShipment(ReignEconomicShipment shipment, float now)
        {
            Settlement source = FindEconomicSettlement(shipment.SourceSettlementStringId);
            Settlement target = FindEconomicSettlement(shipment.TargetSettlementStringId);
            if (source == null || target == null)
            {
                shipment.Status = "Failed";
                shipment.Error = "A settlement no longer exists.";
                return true;
            }
            if (string.Equals(shipment.Status, "Awaiting consent", StringComparison.OrdinalIgnoreCase) && now >= shipment.ConsentDecisionDay)
            {
                ItemObject consentItem = TaleWorlds.CampaignSystem.Campaign.Current?.ObjectManager?.GetObject<ItemObject>(shipment.ItemStringId);
                Hero lord = source.OwnerClan?.Leader;
                int relation = lord?.GetRelation(Hero.MainHero) ?? 0;
                int available = consentItem == null ? 0 : source.ItemRoster.GetItemNumber(consentItem);
                bool accepted = relation + 55 >= Math.Min(90, shipment.Amount * 2) && available >= shipment.Amount;
                shipment.VassalConsentGranted = accepted;
                shipment.Status = accepted ? "In transit" : "Declined";
                if (!accepted) shipment.Error = (lord?.Name?.ToString() ?? "The source clan") + " declined the export request.";
            }
            if (!string.Equals(shipment.Status, "In transit", StringComparison.OrdinalIgnoreCase) || now < shipment.ArrivalDay) return false;
            ReignSupplyTransferResult result = ReignCourtSupplyService.ApplyTransfer(new ReignSupplyTransferRequest
            {
                CommandId = shipment.ShipmentId,
                SourceSettlementStringId = shipment.SourceSettlementStringId,
                TargetSettlementStringId = shipment.TargetSettlementStringId,
                ItemStringId = shipment.ItemStringId,
                ItemAmount = shipment.Amount,
                VassalConsentGranted = shipment.VassalConsentGranted
            }, _session?.Authority ?? ReignCourtAuthority.Royal);
            shipment.Status = result.Success ? "Delivered" : "Failed";
            shipment.Error = result.Error ?? string.Empty;
            RecordLegacyEconomicHistory(shipment, result.Success ? "delivered" : "failed", source, target);
            return true;
        }

        private void RefreshEconomicSnapshots()
        {
            _economicReportState.Settlements = ReignCourtSupplyService
                .GetScopedSnapshots(_session?.Authority ?? ReignCourtAuthority.Royal)
                .OrderByDescending(x => x.OwnerClanStringId == Clan.PlayerClan?.StringId)
                .ThenBy(x => x.Name).ToList();
        }

        private static Settlement FindEconomicSettlement(string id) => Settlement.All.FirstOrDefault(x =>
            string.Equals(x?.StringId, id, StringComparison.OrdinalIgnoreCase) && x.IsFortification);

        private static List<string> LivingClanHeroIds(Clan clan) => clan == null
            ? new List<string>()
            : clan.Heroes.Where(x => x?.IsAlive == true && !string.IsNullOrWhiteSpace(x.StringId))
                .Select(x => x.StringId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        private static bool RequiresShipmentLetter(Hero leader, Clan clan) => leader != null && leader.IsAlive
            && leader != Hero.MainHero && clan != Clan.PlayerClan;

        private void QueueEconomicShipmentLetters(ReignEconomicShipment shipment)
        {
            if (shipment == null || !shipment.IsFoodStockTransfer || !_serverAvailable) return;
            QueueEconomicShipmentLetter(shipment, "source");
            QueueEconomicShipmentLetter(shipment, "destination");
        }

        private void QueueEconomicShipmentLetter(ReignEconomicShipment shipment, string role)
        {
            bool sourceSide = string.Equals(role, "source", StringComparison.OrdinalIgnoreCase);
            string status = sourceSide ? shipment.SourceLetterStatus : shipment.TargetLetterStatus;
            if (string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "NotRequired", StringComparison.OrdinalIgnoreCase)) return;
            string requestKey = shipment.ShipmentId + ":" + role;
            lock (_economicLetterRequestsInFlight)
            {
                if (!_economicLetterRequestsInFlight.Add(requestKey)) return;
            }
            _ = SendEconomicShipmentLetterAsync(shipment, role, requestKey);
        }

        private async Task SendEconomicShipmentLetterAsync(ReignEconomicShipment shipment, string role, string requestKey)
        {
            bool sourceSide = string.Equals(role, "source", StringComparison.OrdinalIgnoreCase);
            try
            {
                string heroId = sourceSide ? shipment.SourceLeaderHeroStringId : shipment.TargetLeaderHeroStringId;
                Hero recipient = Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, heroId, StringComparison.OrdinalIgnoreCase));
                if (recipient == null || recipient == Hero.MainHero)
                {
                    await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role, "NotRequired", string.Empty, string.Empty)).ConfigureAwait(false);
                    return;
                }
                float transit = Math.Max(0.175f, shipment.ArrivalDay - shipment.RequestedDay);
                string body = sourceSide
                    ? "By order of the Crown, " + shipment.RequestedFood.ToString("0.#") + " food supply departed "
                        + shipment.SourceSettlementName + " for " + shipment.TargetSettlementName + ". Estimated travel time: "
                        + transit.ToString("0.0") + " days."
                    : "By order of the Crown, " + shipment.RequestedFood.ToString("0.#") + " food supply has been dispatched from "
                        + shipment.SourceSettlementName + " to " + shipment.TargetSettlementName + ". Estimated travel time: "
                        + transit.ToString("0.0") + " days."
                        + (string.IsNullOrWhiteSpace(shipment.Notes) ? string.Empty : "\n\nA note from the ruler:\n" + shipment.Notes);
                ReignLetterSendResult result = await ReignServerClient.SendEconomicShipmentLetterAsync(
                    recipient, body, shipment.ShipmentId, role, shipment.SourceSettlementStringId,
                    shipment.TargetSettlementStringId, shipment.RequestedDay + 0.25f).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role,
                    result.Ok ? "Sent" : "Error", result.LetterId, result.Error)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() => SetEconomicLetterResult(shipment, role, "Error", string.Empty, ex.Message)).ConfigureAwait(false);
            }
            finally
            {
                lock (_economicLetterRequestsInFlight) _economicLetterRequestsInFlight.Remove(requestKey);
            }
        }

        private void SetEconomicLetterResult(ReignEconomicShipment shipment, string role, string status, string letterId, string error)
        {
            if (shipment == null) return;
            if (string.Equals(role, "source", StringComparison.OrdinalIgnoreCase))
            {
                shipment.SourceLetterStatus = status;
                shipment.SourceLetterId = letterId ?? string.Empty;
                shipment.SourceLetterError = error ?? string.Empty;
            }
            else
            {
                shipment.TargetLetterStatus = status;
                shipment.TargetLetterId = letterId ?? string.Empty;
                shipment.TargetLetterError = error ?? string.Empty;
            }
            StateChanged?.Invoke();
        }

        private static List<string> FindExplicitLordMentions(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return new List<string>();
            return Hero.AllAliveHeroes
                .Where(x => x?.Clan != null && !string.IsNullOrWhiteSpace(x.Name?.ToString())
                    && notes.IndexOf(x.Name.ToString(), StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(x => x.StringId).Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void RecordFoodShipmentKnowledge(ReignEconomicShipment shipment, string phase, string side,
            Settlement source, Settlement target)
        {
            bool sourceSide = string.Equals(side, "source", StringComparison.OrdinalIgnoreCase);
            IEnumerable<string> participantIds = sourceSide ? shipment.SourceClanKnownHeroIds : shipment.TargetClanKnownHeroIds;
            JArray participants = new JArray((participantIds ?? Enumerable.Empty<string>())
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase));
            string sourceName = source?.Name?.ToString() ?? shipment.SourceSettlementName ?? shipment.SourceSettlementStringId;
            string targetName = target?.Name?.ToString() ?? shipment.TargetSettlementName ?? shipment.TargetSettlementStringId;
            string summary = phase == "dispatched"
                ? shipment.RequestedFood.ToString("0.#") + " food supply was dispatched from " + sourceName + " to " + targetName + "."
                : phase == "delivered"
                    ? shipment.DeliveredFood.ToString("0.#") + " food supply reached " + targetName + " from " + sourceName
                        + "; " + shipment.RoadSpoilage.ToString("0.#") + " spoiled on the road and "
                        + shipment.CapacityOverflow.ToString("0.#") + " exceeded storage capacity."
                    : "The food shipment from " + sourceName + " to " + targetName + " was lost.";
            if (!sourceSide && !string.IsNullOrWhiteSpace(shipment.Notes)) summary += " Ruler's note: " + shipment.Notes;
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(
                "court_food_shipment", phase + "_" + side, "economic_transfer", shipment.ShipmentId + ":" + side,
                summary, "private", Hero.MainHero?.StringId ?? string.Empty,
                (sourceSide ? source?.OwnerClan?.Kingdom : target?.OwnerClan?.Kingdom)?.StringId ?? string.Empty,
                new JObject
                {
                    ["knowledgeSide"] = side,
                    ["sourceSettlementId"] = shipment.SourceSettlementStringId ?? string.Empty,
                    ["sourceSettlement"] = sourceName,
                    ["destinationSettlementId"] = shipment.TargetSettlementStringId ?? string.Empty,
                    ["destinationSettlement"] = targetName,
                    ["sourceClanId"] = shipment.SourceClanStringId ?? string.Empty,
                    ["destinationClanId"] = shipment.TargetClanStringId ?? string.Empty,
                    ["knownByHeroIds"] = participants.DeepClone(),
                    ["requestedFood"] = shipment.RequestedFood,
                    ["spoilagePercent"] = shipment.SpoilagePercent,
                    ["roadSpoilage"] = shipment.RoadSpoilage,
                    ["capacityOverflow"] = shipment.CapacityOverflow,
                    ["deliveredFood"] = shipment.DeliveredFood,
                    ["notes"] = sourceSide ? string.Empty : shipment.Notes ?? string.Empty,
                    ["requestedDay"] = shipment.RequestedDay,
                    ["arrivalDay"] = shipment.ArrivalDay
                }, participants);
        }

        private static void RecordLegacyEconomicHistory(ReignEconomicShipment shipment, string phase, Settlement source, Settlement target)
        {
            ReignWorldHistoryCampaignBehavior.Instance?.RecordReignSystemEvent(
                "court_economic_transfer", phase, "economic_transfer", shipment.ShipmentId,
                shipment.Amount + " " + shipment.ItemName + " transferred from " + source.Name + " to " + target.Name + ".",
                "private", Hero.MainHero?.StringId ?? string.Empty, target.OwnerClan?.Kingdom?.StringId ?? string.Empty,
                new JObject { ["legacyItemTransfer"] = true, ["itemId"] = shipment.ItemStringId ?? string.Empty,
                    ["amount"] = shipment.Amount, ["notes"] = shipment.Notes ?? string.Empty },
                new JArray(shipment.MentionedHeroIds ?? new List<string>()));
        }

        public static bool IsPlayerKingdomRuler()
        {
            Hero player = Hero.MainHero;
            Clan playerClan = Clan.PlayerClan;
            Kingdom kingdom = playerClan?.Kingdom;
            return Hero.MainHero != null
                && kingdom != null
                && !kingdom.IsEliminated
                // RulingClan and Kingdom.Leader are the native kingdom-authority
                // signals. Clan.Leader can lag or be replaced independently in
                // modded saves, so requiring it ejects a legitimate player ruler
                // after hourly/fast-forward ticks.
                && (kingdom.RulingClan == playerClan
                    || kingdom.RulingClan?.Leader == player
                    || kingdom.Leader == player);
        }

        private void EnsureRoyalDocket()
        {
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main";
            if (_session != null && _session.Authority == ReignCourtAuthority.Royal
                && string.Equals(_session.CampaignId, campaignId, StringComparison.Ordinal)
                && string.Equals(_session.TimelineId, timelineId, StringComparison.Ordinal)) return;
            _session = new CourtSession
            {
                State = ReignCourtSessionState.Inactive,Authority = ReignCourtAuthority.Royal,HostSettlementStringId = string.Empty,
                CampaignId = campaignId,TimelineId = timelineId,OpenedDay = CurrentDayFloat(),LastAgendaDay = -1,Revision = 1,TimeMode = 0
            };
        }

        private bool ValidateActiveSession(bool loading)
        {
            Settlement host = Settlement.All.FirstOrDefault(x => x.StringId == _session?.HostSettlementStringId);
            if (host == null || !host.IsFortification) { InterruptAndClose("The host fortification no longer exists."); return false; }
            if (host.IsUnderSiege) { QueueHostCrisis(host, "siege"); InterruptAndClose("A siege has interrupted court."); return false; }
            if (Hero.MainHero?.IsPrisoner == true) { InterruptAndClose("The ruler has been taken captive."); return false; }
            if (!TryResolveAuthority(host, out ReignCourtAuthority authority, out string reason) || authority != _session.Authority)
            {
                string authorityReason = string.IsNullOrWhiteSpace(reason)
                    ? "The restored court authority does not match the active session."
                    : reason;
                if (!ObserveAuthorityFailure(authorityReason)) return false;
                InterruptAndClose("Court authority changed: " + authorityReason);
                return false;
            }
            ResetAuthorityFailures();
            if (loading && CurrentHour() >= 8) BuildDawnAgenda(CurrentDay());
            return true;
        }

        private bool ObserveAuthorityFailure(string reason)
        {
            if (!string.Equals(_lastAuthorityFailureReason, reason, StringComparison.Ordinal))
            {
                _consecutiveAuthorityFailures = 0;
                _lastAuthorityFailureReason = reason ?? string.Empty;
            }
            _consecutiveAuthorityFailures = ReignCourtAuthorityStability.RecordFailure(
                _consecutiveAuthorityFailures);
            return ReignCourtAuthorityStability.ShouldInterrupt(_consecutiveAuthorityFailures);
        }

        private void ResetAuthorityFailures()
        {
            _consecutiveAuthorityFailures = 0;
            _lastAuthorityFailureReason = string.Empty;
        }

        private void InterruptAndClose(string reason)
        {
            if (_session == null) return;
            _session.State = ReignCourtSessionState.Interrupted;
            _session.InterruptionReason = reason;
            _session.Revision++;
            ReignCourtScreenManager.Close();
            ReignCastleLayoutScreenManager.Close(false);
            ReignCourtEconomicReportScreenManager.Close(false);
            ReignWarCouncilScreenManager.Close(false);
            ReignAmbassadorScreenManager.Close(false);
            ReignFamilyChambersScreenManager.Close(false);
            CloseSession(reason);
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + reason));
        }

        private void BuildDawnAgenda(int day, bool playerWasSitting = false)
        {
            if (_session == null || _session.LastAgendaDay == day) return;
            ResolvePreviousDailyAgenda(day, playerWasSitting);
            ApplyExpiredNativeOutcomes(day);
            QueueFactualMatters(day);
            _agenda.RemoveAll(x => x == null || x.AgendaDay < day - 7 || x.Status == "completed");
            _agenda.AddRange(ReignCourtScheduler.BuildAgenda(_matters, day));
            _session.LastAgendaDay = day;
            _session.Revision++;
            QueueRegentMailForMajorMatters(day, playerWasSitting);
            StateChanged?.Invoke();
            if (_serverSessionOpened) _ = SyncServerAgendaAsync();
        }

        private void ResolvePreviousDailyAgenda(int day, bool playerWasSitting)
        {
            List<CourtAgendaItem> prior = _agenda.Where(x => x != null && x.AgendaDay < day && x.Status != "completed").OrderBy(x => x.AgendaDay).ThenBy(x => x.Slot).ToList();
            List<CourtAgendaItem> previousDay = _agenda.Where(x => x != null && x.AgendaDay == day - 1).ToList();
            bool personallyCompleted = playerWasSitting && previousDay.All(x =>
            {
                CourtMatter m = _matters.FirstOrDefault(y => y.MatterId == x.MatterId);
                return x.Status == "completed" && m != null && string.Equals(m.ResolutionMode, "personal", StringComparison.OrdinalIgnoreCase);
            });

            Hero regent = FindHero(ActiveRegent?.HeroStringId);
            foreach (CourtAgendaItem item in prior)
            {
                CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == item.MatterId);
                if (matter == null || matter.IsTerminal) { item.Status = "completed"; continue; }
                if (matter.IsMajorRealmEvent)
                {
                    ApplyPredefinedDefaultOutcome(matter, day, regent == null ? "no_regent" : "regent_mail_unanswered");
                    item.Status = "expired";
                    continue;
                }
                if (regent != null)
                {
                    bool positive = RegentPositiveRoll(regent, matter, item.AgendaDay);
                    matter.State = ReignCourtMatterState.Resolved;matter.SelectedOptionId = positive ? "regent_positive" : "regent_negative";
                    matter.ResolutionMode = positive ? "regent_positive" : "regent_negative";matter.ResolvedDay = day;
                    matter.ResolutionReceiptJson = new JObject
                    {
                        ["ok"] = true,["resolver"] = "regent",["heroStringId"] = regent.StringId,["outcome"] = positive ? "positive" : "negative",
                        ["skillRule"] = "charm_plus_steward",["playerPresenceCredit"] = false
                    }.ToString(Newtonsoft.Json.Formatting.None);
                    matter.Revision++;item.Status = positive ? "delegated_positive" : "delegated_negative";
                    if (!positive) ApplyPredefinedDefaultOutcome(matter, day, "regent_negative");
                }
                else
                {
                    ApplyPredefinedDefaultOutcome(matter, day, "no_regent");
                    item.Status = "ignored";
                }
            }
        }

        private bool RegentPositiveRoll(Hero regent, CourtMatter matter, int agendaDay)
        {
            int charm = regent?.GetSkillValue(TaleWorlds.Core.DefaultSkills.Charm) ?? 0;
            int steward = regent?.GetSkillValue(TaleWorlds.Core.DefaultSkills.Steward) ?? 0;
            double normalized = Math.Max(0d, Math.Min(1d, (charm + steward) / 600d));
            double chance = Math.Min(0.70d, 0.20d + 0.50d * normalized);
            return StableUnit((_session?.CampaignId ?? "default") + "|" + (_session?.TimelineId ?? "main") + "|regent|" + (matter?.MatterId ?? "") + "|" + agendaDay + "|" + (regent?.StringId ?? "")) < chance;
        }

        private void ApplyPredefinedDefaultOutcome(CourtMatter matter, int day, string reason)
        {
            JObject outcome;
            try { outcome = JObject.Parse(string.IsNullOrWhiteSpace(matter.DefaultOutcomeJson) ? "{}" : matter.DefaultOutcomeJson); }
            catch { outcome = new JObject(); }
            string effect = outcome.Value<string>("effect") ?? "none";
            bool applied = true;string result = effect == "none" ? "Status quo retained." : string.Empty;
            try
            {
                if (effect == "rebellion_response")
                {
                    string movementId = outcome.Value<string>("movementId") ?? string.Empty;
                    applied = ReignRebellionCampaignBehavior.Instance?.TryResolvePlayerUltimatum(movementId, outcome.Value<string>("response") ?? "deadline_expired", out result) == true;
                }
                else if (effect == "declare_war")
                {
                    Kingdom actor = Kingdom.All.FirstOrDefault(x => x.StringId == (outcome.Value<string>("actorKingdomId") ?? string.Empty));
                    Kingdom target = Kingdom.All.FirstOrDefault(x => x.StringId == (outcome.Value<string>("targetKingdomId") ?? string.Empty));
                    if (actor == null || target == null) { applied = false;result = "The coded war threat no longer identifies two kingdoms."; }
                    else if (!FactionManager.IsAtWarAgainstFaction(actor, target)) { DeclareWarAction.ApplyByKingdomDecision(actor, target);result = "The unprevented threat became war."; }
                    else result = "The kingdoms were already at war.";
                }
                // Marriage and land-trade defaults deliberately perform no mutation.
                else if (effect == "marriage_response" || effect == "land_trade") result = "No agreement occurred; existing state was retained.";
            }
            catch (Exception ex) { applied = false;result = ex.Message; }
            matter.State = applied ? ReignCourtMatterState.Expired : ReignCourtMatterState.Invalidated;
            matter.SelectedOptionId = "daily_deadline_ignored";matter.ResolutionMode = reason;matter.ResolvedDay = day;
            matter.ResolutionReceiptJson = new JObject { ["ok"] = applied,["reason"] = reason,["effect"] = effect,["result"] = result,["defaultOutcome"] = outcome }.ToString(Newtonsoft.Json.Formatting.None);
            matter.InvalidReason = applied ? string.Empty : result;matter.Revision++;
        }

        private void QueueRegentMailForMajorMatters(int day, bool playerWasSitting)
        {
            Hero regent = FindHero(ActiveRegent?.HeroStringId);
            if (regent == null || playerWasSitting) return;
            foreach (CourtMatter matter in _agenda.Where(x => x.AgendaDay == day).Select(x => _matters.FirstOrDefault(m => m.MatterId == x.MatterId))
                .Where(x => x != null && x.IsMajorRealmEvent && !x.IsTerminal && x.RegentMailSentDay < 0f))
            {
                matter.RegentMailSentDay = day;matter.Revision++;
                _ = ReignServerClient.SendRegentCourtLetterAsync(regent, matter);
            }
        }

        private static double StableUnit(string text)
        {
            unchecked { uint hash = 2166136261;foreach (char c in text ?? string.Empty) { hash ^= c;hash *= 16777619; }return hash / (double)uint.MaxValue; }
        }

        private void QueueFactualMatters(int day)
        {
            foreach (ReignSettlementSupplySnapshot land in ReignCourtSupplyService.GetScopedSnapshots(_session.Authority).Where(x => x.HasShortage || x.IsBesieged).Take(3))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "settlement_supply:" + land.SettlementStringId,
                    Kind = ReignCourtMatterKind.SettlementCrisis,
                    Title = land.Name + " requires attention",
                    Summary = land.IsBesieged ? "The settlement is under siege." : "Food supply is deteriorating and the settlement requests aid.",
                    SettlementStringId = land.SettlementStringId,
                    CreatedDay = day,
                    DueDay = day + (land.IsBesieged ? 1 : 3),
                    Priority = land.IsBesieged ? 100 : 75,
                    IsCritical = land.IsBesieged,
                    DecisionOptionsJson = NavigationOptions("Open Lands", "lands", "Postpone", "postpone"),
                    DefaultOutcomeJson = "{\"effect\":\"none\",\"reason\":\"deadline_ignored\"}"
                });
            }

            foreach (ReignCourtOffice office in Enum.GetValues(typeof(ReignCourtOffice)).Cast<ReignCourtOffice>())
            {
                if (_offices.Any(x => x.IsActive && x.Office == office)) continue;
                QueueMatter(new CourtMatter
                {
                    SourceKey = "office_vacancy:" + office.ToString().ToLowerInvariant(),
                    Kind = ReignCourtMatterKind.OfficeBusiness,
                    Title = office + " office is vacant",
                    Summary = "Basic reports remain available, but advanced " + office.ToString().ToLowerInvariant() + " operations require an appointment.",
                    CreatedDay = day,
                    DueDay = -1f,
                    Priority = 35,
                    DecisionOptionsJson = NavigationOptions("Review appointments", "subjects", "Leave vacant", "refuse")
                });
            }

            foreach (CourtObligation obligation in _obligations.Where(x => x.Status == "unresolved" && x.DueDay >= 0f && x.DueDay <= day + 3))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "obligation:" + obligation.ObligationId,
                    Kind = ReignCourtMatterKind.Obligation,
                    Title = "Obligation approaching its deadline",
                    Summary = obligation.Description,
                    CreatedDay = day,
                    DueDay = obligation.DueDay,
                    Priority = 90,
                    IsCritical = obligation.DueDay <= day + 1,
                    TermsHash = obligation.TermsHash,
                    DecisionOptionsJson = NavigationOptions("Review obligation", "court", "Refuse", "refuse"),
                    DefaultOutcomeJson = obligation.BreachRuleJson
                });
            }

            ReignRebellionMovementRecord rebellion = ReignRebellionCampaignBehavior.Instance?.Movements?
                .FirstOrDefault(x => x.IsPlayerKingdom && !x.ResolutionApplied && (x.UltimatumIssued || x.Pressure >= 70f));
            if (rebellion != null)
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "rebellion_ultimatum:" + rebellion.MovementId,
                    Kind = ReignCourtMatterKind.RebellionUltimatum,
                    Title = rebellion.UltimatumIssued ? "A coalition delivers an ultimatum" : "A dangerous coalition is forming",
                    Summary = "Known coalition contacts demand a ruler-facing response. Hidden pressure and undiscovered members are not shown.",
                    KingdomStringId = rebellion.ParentKingdomStringId,
                    ParticipantHeroIdsCsv = rebellion.LeaderHeroStringId,
                    CreatedDay = day,
                    DueDay = day + 7,
                    Priority = 100,
                    IsCritical = rebellion.UltimatumIssued,
                    IsMajorRealmEvent = true,
                    RequiresServer = true,
                    DecisionOptionsJson = RebellionDecisionOptions(rebellion.MovementId),
                    DefaultOutcomeJson = new JObject
                    {
                        ["effect"] = "rebellion_response",
                        ["response"] = "deadline_expired",
                        ["movementId"] = rebellion.MovementId
                    }.ToString(Newtonsoft.Json.Formatting.None)
                });
            }

            Hero prisoner = Hero.AllAliveHeroes.Where(x => x != null && x.IsLord && x.IsPrisoner && x.PartyBelongedToAsPrisoner?.MapFaction == Clan.PlayerClan?.MapFaction)
                .OrderByDescending(x => x.Clan?.Tier ?? 0).FirstOrDefault();
            if (prisoner != null)
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "prisoner_petition:" + prisoner.StringId,
                    Kind = ReignCourtMatterKind.PrisonerPetition,
                    Title = prisoner.Name + " petitions the court",
                    Summary = "A current prisoner asks to be heard. Any pardon, ransom, punishment, or transfer must use a valid coded native action.",
                    ParticipantHeroIdsCsv = prisoner.StringId,
                    CreatedDay = day,
                    DueDay = day + 5,
                    Priority = 58,
                    Confidentiality = "realm",
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Hear petition", "court", "Refuse audience", "refuse")
                });
            }

            if (_session.Authority == ReignCourtAuthority.Royal && Clan.PlayerClan?.Kingdom != null
                && Kingdom.All.Any(x => x != null && x != Clan.PlayerClan.Kingdom && FactionManager.IsAtWarAgainstFaction(Clan.PlayerClan.Kingdom, x)))
            {
                QueueMatter(new CourtMatter
                {
                    SourceKey = "war_council:" + day,
                    Kind = ReignCourtMatterKind.WarCouncil,
                    Title = "The Marshal requests a war council",
                    Summary = "Active wars, commanders, supplies, and readiness require a validated objective. The council may submit plans only through the existing strategy executor.",
                    CreatedDay = day,
                    DueDay = day + 2,
                    Priority = 78,
                    IsMajorRealmEvent = true,
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Open Military", "military", "Postpone", "refuse")
                });
            }

            Hero courtier = Hero.AllAliveHeroes.Where(x => ReignCourtNobleCampaignBehavior.IsCourtNoble(x)
                    && (_session.Authority == ReignCourtAuthority.Royal ? x.Clan?.Kingdom == Clan.PlayerClan?.Kingdom : x.Clan == Clan.PlayerClan))
                .OrderBy(x => StableCourtOrder(x.StringId, day)).FirstOrDefault();
            if (courtier != null)
            {
                ReignCourtMatterKind ambientKind = day % 3 == 0 ? ReignCourtMatterKind.Performer : day % 3 == 1 ? ReignCourtMatterKind.Artisan : ReignCourtMatterKind.PrivateCounsel;
                QueueMatter(new CourtMatter
                {
                    SourceKey = "ambient_court:" + courtier.StringId + ":" + day,
                    Kind = ambientKind,
                    Title = courtier.Name + " requests a brief audience",
                    Summary = "A generated court noble seeks a roleplay audience grounded in current court context. Dialogue may characterize the meeting but coded outcomes remain effect-free unless separately validated.",
                    ParticipantHeroIdsCsv = courtier.StringId,
                    CreatedDay = day,
                    DueDay = day,
                    Priority = 12,
                    IsAmbientRoleplay = true,
                    Confidentiality = ambientKind == ReignCourtMatterKind.PrivateCounsel ? "private" : "public",
                    RequiresServer = true,
                    DecisionOptionsJson = NavigationOptions("Receive", "court", "Dismiss", "refuse")
                });
            }
        }

        private static int StableCourtOrder(string id,int day)
        {
            unchecked{int hash=day;foreach(char c in id??string.Empty)hash=hash*31+c;return hash&int.MaxValue;}
        }

        private void ApplyExpiredNativeOutcomes(int day)
        {
            foreach (CourtMatter matter in _matters.Where(x => x != null && !x.IsTerminal && x.DueDay >= 0f && x.DueDay < day).ToList())
            {
                if (matter.Kind != ReignCourtMatterKind.RebellionUltimatum) continue;
                string movementId = (matter.SourceKey ?? string.Empty).StartsWith("rebellion_ultimatum:", StringComparison.OrdinalIgnoreCase)
                    ? matter.SourceKey.Substring("rebellion_ultimatum:".Length)
                    : string.Empty;
                string result = string.Empty;
                bool applied = ReignRebellionCampaignBehavior.Instance?.TryResolvePlayerUltimatum(movementId, "deadline_expired", out result) == true;
                matter.State = applied ? ReignCourtMatterState.Expired : ReignCourtMatterState.Invalidated;
                matter.SelectedOptionId = "deadline_expired";
                matter.ResolvedDay = day;
                matter.ResolutionReceiptJson = new JObject { ["ok"] = applied, ["result"] = result ?? string.Empty, ["defaultOutcome"] = JObject.Parse(matter.DefaultOutcomeJson ?? "{}") }.ToString(Newtonsoft.Json.Formatting.None);
                matter.InvalidReason = applied ? string.Empty : result ?? "The rebellion ultimatum could not be executed.";
                matter.Revision++;
            }
        }

        private void QueueHostCrisis(Settlement host, string kind)
        {
            ReignCourtScheduler.QueueUrgentInterruption(_matters, _agenda, new CourtMatter
            {
                SourceKey = "host_interruption:" + host.StringId + ":" + kind,
                Kind = ReignCourtMatterKind.SettlementCrisis,
                Title = "Court interrupted at " + host.Name,
                Summary = "A verified native state change has made the court unsafe.",
                SettlementStringId = host.StringId,
                CreatedDay = CurrentDayFloat(),
                DueDay = CurrentDayFloat(),
                Priority = 100,
                DefaultOutcomeJson = "{\"effect\":\"court_closed\"}",
                DecisionOptionsJson = "[]"
            }, CurrentDay());
        }

        private void QueueMatter(CourtMatter matter)
        {
            matter.CampaignId = _session.CampaignId;
            matter.TimelineId = _session.TimelineId;
            ReignCourtScheduler.TryQueueUnique(_matters, matter);
        }

        private void RecordCounterSample(int day)
        {
            CourtCounterSample sample = _counterSamples.FirstOrDefault(x => x.Day == day);
            if (sample == null)
            {
                sample = new CourtCounterSample { Day = day };
                _counterSamples.Add(sample);
            }
            sample.Supply = ReignCourtSupplyService.GetStrategicSupplyUnits(_session.Authority);
            sample.Gold = Hero.MainHero?.Gold ?? 0;
            sample.Influence = Clan.PlayerClan?.Influence ?? 0f;
            sample.Strength = _session.Authority == ReignCourtAuthority.Royal
                ? Clan.PlayerClan?.Kingdom?.CurrentTotalStrength ?? 0f
                : Clan.PlayerClan?.CurrentTotalStrength ?? 0f;
            sample.Renown = Clan.PlayerClan?.Renown ?? 0f;
            _counterSamples.RemoveAll(x => x.Day < day - 7);
        }

        private static string NavigationOptions(string primaryLabel, string primaryTab, string secondaryLabel, string secondaryOption)
        {
            JArray options = new JArray();
            foreach (JObject option in new[]
            {
                new JObject { ["optionId"] = "open_" + primaryTab, ["label"] = primaryLabel, ["effect"] = "none", ["navigateTab"] = primaryTab, ["consequences"] = new JObject() },
                new JObject { ["optionId"] = secondaryOption, ["label"] = secondaryLabel, ["effect"] = "none", ["consequences"] = new JObject() }
            })
            {
                option["termsHash"] = ReignCourtTerms.Hash(option["consequences"].ToString(Newtonsoft.Json.Formatting.None));
                options.Add(option);
            }
            return options.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string RebellionDecisionOptions(string movementId)
        {
            JArray options = new JArray();
            foreach (JObject choice in new[]
            {
                new JObject { ["optionId"] = "accept", ["label"] = "Accept demands", ["description"] = "Apply the coalition's coded demand and avoid civil war.", ["response"] = "accept" },
                new JObject { ["optionId"] = "negotiate", ["label"] = "Negotiate", ["description"] = "Open a time-limited negotiation while the coalition remains active.", ["response"] = "negotiate" },
                new JObject { ["optionId"] = "refuse", ["label"] = "Refuse", ["description"] = "Reject the ultimatum and permit the rebellion executor to begin civil war.", ["response"] = "refuse" }
            })
            {
                JObject consequences = new JObject
                {
                    ["nativeActions"] = new JArray(new JObject
                    {
                        ["type"] = "rebellion_response",
                        ["movementId"] = movementId ?? string.Empty,
                        ["response"] = choice.Value<string>("response")
                    })
                };
                choice.Remove("response");
                choice["consequences"] = consequences;
                choice["termsHash"] = ReignCourtTerms.Hash(consequences.ToString(Newtonsoft.Json.Formatting.None));
                options.Add(choice);
            }
            return options.ToString(Newtonsoft.Json.Formatting.None);
        }

        private bool CanAdvanceTime()
        {
            return IsRuleModeActive && _session.State == ReignCourtSessionState.Sitting && string.IsNullOrWhiteSpace(_session.ActiveMatterId);
        }

        private static bool TryResolveAuthority(Settlement settlement, out ReignCourtAuthority authority, out string reason)
        {
            authority = ReignCourtAuthority.Royal;
            if (settlement == null || !settlement.IsFortification) { reason = "Rule Mode requires a town or castle."; return false; }
            if (settlement.IsUnderSiege) { reason = "Court cannot sit in a besieged fortification."; return false; }
            if (!IsPlayerKingdomRuler()) { reason = "Only the current ruler of a kingdom may hold court."; return false; }
            if (Hero.MainHero.IsPrisoner) { reason = "A captive cannot hold court."; return false; }

            Kingdom kingdom = Clan.PlayerClan.Kingdom;
            if (settlement.OwnerClan?.Kingdom == kingdom)
            {
                reason = string.Empty;
                return true;
            }
            reason = "Royal court may sit only in a town or castle belonging to your kingdom.";
            return false;
        }

        private static Settlement CurrentSettlement() { return Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement; }
        private static int CurrentDay() { return (int)Math.Floor(CampaignTime.Now.ToDays); }
        private static int CurrentHour()
        {
            double day = CampaignTime.Now.ToDays;
            return (int)Math.Floor((day - Math.Floor(day)) * 24d);
        }
        private static float CurrentDayFloat() { return (float)CampaignTime.Now.ToDays; }

        private async Task OpenServerSessionAsync()
        {
            if (_serverRequestInFlight || _session == null) return;
            _serverRequestInFlight = true;
            try
            {
                long expectedRevision = _serverSessionOpened ? _serverRevision : 0;
                string commandId = "court_open_" + _session.SessionId + "_" + Guid.NewGuid().ToString("N");
                JObject nativeSnapshot = BuildNativeSnapshot();
                ReignCourtServerResponse response = await ReignCourtServerClient.OpenSessionAsync(
                    _session, expectedRevision, commandId, nativeSnapshot).ConfigureAwait(false);
                if (!response.Ok
                    && string.Equals(response.Raw?.Value<string>("error"), "revision_conflict", StringComparison.OrdinalIgnoreCase)
                    && response.Raw?["currentRevision"] != null)
                {
                    expectedRevision = response.Revision;
                    response = await ReignCourtServerClient.OpenSessionAsync(
                        _session,
                        expectedRevision,
                        commandId + "_reconcile_" + expectedRevision,
                        nativeSnapshot).ConfigureAwait(false);
                }
                await ReignMainThread.InvokeAsync(() =>
                {
                    _serverAvailable = response.Ok;
                    _serverStatus = response.Ok ? "Court server connected." : "Court server unavailable: " + response.Error;
                    if (response.Ok) { _serverSessionOpened = true; _serverRevision = response.Revision; }
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
                if (response.Ok)
                {
                    await SyncServerAgendaAsync().ConfigureAwait(false);
                    await SyncServerHomeAsync().ConfigureAwait(false);
                }
            }
            finally { _serverRequestInFlight = false; }
        }

        private async Task SyncServerAgendaAsync()
        {
            if (!_serverSessionOpened || _session == null) return;
            JArray candidates = new JArray(_matters.Where(x => !x.IsTerminal).Select(ToServerMatter));
            ReignCourtServerResponse response = await ReignCourtServerClient.BuildAgendaAsync(_session, _serverRevision,
                "court_agenda_" + _session.SessionId + "_" + _session.LastAgendaDay, candidates).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Court server synchronized." : "Court server agenda unavailable: " + response.Error;
                if (response.Ok)
                {
                    _serverRevision = response.Revision;
                    foreach (JObject row in (response.Raw["agenda"] as JArray ?? new JArray()).OfType<JObject>())
                    {
                        string id = row.Value<string>("matter_id");
                        if (!string.IsNullOrWhiteSpace(id)) _serverMatterRevisions[id] = row.Value<long?>("revision") ?? 0;
                    }
                }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            if (response.Ok) await SyncServerHomeAsync().ConfigureAwait(false);
        }

        private async Task TickServerSessionAsync()
        {
            if (_serverRequestInFlight || !_serverSessionOpened || _session == null) return;
            _serverRequestInFlight = true;
            try
            {
                ReignCourtServerResponse response = await ReignCourtServerClient.TickSessionAsync(_session, _serverRevision,
                    "court_tick_" + _session.SessionId + "_" + ((int)(CurrentDayFloat() * 24f)), BuildNativeSnapshot()).ConfigureAwait(false);
                NativeCourtExecution missionExecution = response.Ok && response.NativeActions.Count > 0
                    ? await ExecuteNativeActionsAsync(response.NativeActions).ConfigureAwait(false)
                    : new NativeCourtExecution { Success = true };
                await ReignMainThread.InvokeAsync(() =>
                {
                    _serverAvailable = response.Ok;
                    _serverStatus = response.Ok
                        ? missionExecution.Success ? "Court server synchronized." : "Court synchronized, but an ambassador mission effect failed native validation: " + missionExecution.Error
                        : "Court server unavailable: " + response.Error;
                    if (response.Ok) _serverRevision = response.Revision;
                    StateChanged?.Invoke();
                }).ConfigureAwait(false);
                if (response.Ok) await SyncServerHomeAsync().ConfigureAwait(false);
            }
            finally { _serverRequestInFlight = false; }
        }

        private async Task CloseServerSessionAsync(string reason)
        {
            CourtSession session = _session;
            ReignCourtServerResponse response = await ReignCourtServerClient.CloseSessionAsync(session, _serverRevision,
                "court_close_" + session.SessionId, reason).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Court session closed on server." : "Court server unavailable while closing: " + response.Error;
                if (response.Ok) { _serverRevision = response.Revision; _serverSessionOpened = false; }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private async Task<string> StartServerMatterAsync(CourtMatter matter)
        {
            if (!_serverAvailable || !_serverSessionOpened || matter == null) return MutationUnavailable();
            long revision = _serverMatterRevisions.TryGetValue(matter.MatterId, out long known) ? known : matter.Revision;
            ReignCourtServerResponse response = await ReignCourtServerClient.StartMatterAsync(matter.MatterId, revision,
                "court_start_" + matter.MatterId + "_" + Guid.NewGuid().ToString("N")).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _serverAvailable = response.Ok;
                _serverStatus = response.Ok ? "Matter opened with the court server." : "Matter remains queued: " + response.Error;
                if (response.Ok) _serverMatterRevisions[matter.MatterId] = response.Revision;
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
            return response.Ok ? string.Empty : response.Error;
        }

        private async Task SyncServerHomeAsync()
        {
            ReignCourtServerResponse response = await ReignCourtServerClient.GetHomeAsync().ConfigureAwait(false);
            if (!response.Ok) return;
            await ReignMainThread.InvokeAsync(() =>
            {
                foreach (JObject row in (response.Raw["dailyAgenda"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    CourtMatter matter = UpsertServerMatter(row);
                    if (matter != null && !_agenda.Any(x => x.MatterId == matter.MatterId && x.AgendaDay == CurrentDay()))
                    {
                        _agenda.Add(new CourtAgendaItem
                        {
                            MatterId = matter.MatterId,
                            AgendaDay = CurrentDay(),
                            Slot = _agenda.Count(x => x.AgendaDay == CurrentDay()),
                            IsUrgentInterruption = matter.IsCritical,
                            Status = "scheduled"
                        });
                    }
                }
                foreach (JObject row in (response.Raw["peopleMatters"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerMatter(row);
                foreach (JObject row in (response.Raw["ambassadors"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerAmbassador(row);
                foreach (JObject row in (response.Raw["operations"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerOperation(row);
                foreach (JObject row in (response.Raw["offices"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerOffice(row);
                foreach (JObject row in (response.Raw["obligations"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerObligation(row);
                foreach (JObject row in (response.Raw["plots"] as JArray ?? new JArray()).OfType<JObject>()) UpsertServerPlot(row);
                foreach (JObject rumor in (response.Raw["rumors"] as JArray ?? new JArray()).OfType<JObject>())
                {
                    string rumorId = rumor.Value<string>("rumorId");
                    QueueMatter(new CourtMatter
                    {
                        SourceKey = "rumor:" + rumorId,
                        Kind = ReignCourtMatterKind.Rumor,
                        Title = rumor.Value<string>("claim") ?? "A new rumor reaches court",
                        Summary = "Source: " + (rumor.Value<string>("provenance") ?? "unknown") + ". Confidence " + ((rumor.Value<double?>("confidence") ?? 0.5d) * 100d).ToString("0") + "%.",
                        CreatedDay = (float)(rumor.Value<double?>("createdDay") ?? CurrentDayFloat()),
                        Priority = (int)Math.Round((rumor.Value<double?>("confidence") ?? 0.5d) * 50d),
                        RequiresServer = true,
                        DecisionOptionsJson = "[]"
                    });
                }
                StateChanged?.Invoke();
            }).ConfigureAwait(false);
        }

        private CourtMatter UpsertServerMatter(JObject row)
        {
            string id = row.Value<string>("matter_id") ?? row.Value<string>("matterId") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id)) return null;
            CourtMatter matter = _matters.FirstOrDefault(x => x.MatterId == id);
            if (matter == null)
            {
                matter = new CourtMatter { MatterId = id };
                _matters.Add(matter);
            }
            matter.SourceKey = row.Value<string>("source_key") ?? matter.SourceKey;
            matter.Kind = ParseMatterKind(row.Value<string>("kind"));
            matter.State = ParseMatterState(row.Value<string>("state"));
            matter.Title = row.Value<string>("title") ?? matter.Title;
            matter.Summary = row.Value<string>("summary") ?? matter.Summary;
            matter.Confidentiality = row.Value<string>("confidentiality") ?? matter.Confidentiality;
            matter.ParticipantHeroIdsCsv = JsonIds(row["participants_json"] ?? row["participants"]);
            matter.WitnessHeroIdsCsv = JsonIds(row["witnesses_json"] ?? row["witnesses"]);
            matter.SettlementStringId = row.Value<string>("settlement_id") ?? matter.SettlementStringId;
            matter.KingdomStringId = row.Value<string>("kingdom_id") ?? matter.KingdomStringId;
            matter.CreatedDay = row.Value<float?>("created_day") ?? matter.CreatedDay;
            matter.DueDay = row.Value<float?>("due_day") ?? matter.DueDay;
            matter.ScheduledDay = row.Value<float?>("scheduled_day") ?? matter.ScheduledDay;
            matter.Priority = row.Value<int?>("priority") ?? matter.Priority;
            matter.IsCritical = row.Value<int?>("is_critical") == 1 || row.Value<bool?>("isCritical") == true;
            matter.IsMajorRealmEvent = row.Value<int?>("is_major") == 1 || row.Value<bool?>("isMajorRealmEvent") == true;
            matter.IsAmbientRoleplay = row.Value<int?>("is_ambient") == 1 || row.Value<bool?>("isAmbientRoleplay") == true;
            matter.DecisionOptionsJson = JsonText(row["options_json"], matter.DecisionOptionsJson);
            matter.DefaultOutcomeJson = JsonText(row["default_outcome_json"], matter.DefaultOutcomeJson);
            matter.RequiresServer = row.Value<int?>("requires_server") != 0;
            matter.Revision = row.Value<long?>("revision") ?? matter.Revision;
            matter.CampaignId = _session?.CampaignId ?? matter.CampaignId;
            matter.TimelineId = _session?.TimelineId ?? matter.TimelineId;
            _serverMatterRevisions[id] = matter.Revision;
            return matter;
        }

        private void UpsertServerAmbassador(JObject row)
        {
            string id=row.Value<string>("posting_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            AmbassadorPosting posting=_ambassadors.FirstOrDefault(x=>x.PostingId==id);if(posting==null){posting=new AmbassadorPosting{PostingId=id};_ambassadors.Add(posting);}
            posting.HeroStringId=row.Value<string>("hero_id")??posting.HeroStringId;posting.TargetKingdomStringId=row.Value<string>("target_kingdom_id")??posting.TargetKingdomStringId;
            posting.MissionType=row.Value<string>("mission_type")??posting.MissionType;posting.MissionTermsJson=row.Value<string>("mission_terms_json")??posting.MissionTermsJson;
            posting.Status=row.Value<string>("status")??posting.Status;posting.AssignedDay=row.Value<float?>("assigned_day")??posting.AssignedDay;posting.ArrivalDay=row.Value<float?>("arrival_day")??posting.ArrivalDay;
            posting.LastReportDay=row.Value<float?>("last_report_day")??posting.LastReportDay;posting.RecalledDay=row.Value<float?>("recalled_day")??posting.RecalledDay;posting.Revision=row.Value<long?>("revision")??posting.Revision;
        }

        private void UpsertServerOffice(JObject row)
        {
            string id=row.Value<string>("assignment_id")??string.Empty,heroId=row.Value<string>("hero_id")??string.Empty,officeText=row.Value<string>("office")??string.Empty;if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(heroId))return;
            if(!Enum.TryParse(officeText,true,out ReignCourtOffice office))return;
            CourtOfficeAssignment assignment=_offices.FirstOrDefault(x=>x.AssignmentId==id);
            if(assignment==null)
            {
                foreach(CourtOfficeAssignment old in _offices.Where(x=>x.IsActive&&x.Office==office))old.IsActive=false;
                assignment=new CourtOfficeAssignment{AssignmentId=id,Office=office,HeroStringId=heroId};_offices.Add(assignment);
            }
            assignment.HeroStringId=heroId;assignment.AssignedDay=row.Value<float?>("assigned_day")??assignment.AssignedDay;assignment.DismissedDay=row.Value<float?>("dismissed_day")??assignment.DismissedDay;
            assignment.IsActive=string.Equals(row.Value<string>("status"),"active",StringComparison.OrdinalIgnoreCase);assignment.DismissalReason=row.Value<string>("dismissal_reason")??assignment.DismissalReason;assignment.Revision=row.Value<long?>("revision")??assignment.Revision;
        }

        private void UpsertServerOperation(JObject row)
        {
            string id=row.Value<string>("operation_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            IntelligenceOperation operation=_operations.FirstOrDefault(x=>x.OperationId==id);if(operation==null){operation=new IntelligenceOperation{OperationId=id};_operations.Add(operation);}
            operation.OperationType=row.Value<string>("operation_type")??operation.OperationType;operation.TargetType=row.Value<string>("target_type")??operation.TargetType;operation.TargetStringId=row.Value<string>("target_id")??operation.TargetStringId;
            operation.State=ParseOperationState(row.Value<string>("status"));operation.GoldCost=row.Value<int?>("gold_cost")??operation.GoldCost;operation.InfluenceCost=row.Value<float?>("influence_cost")??operation.InfluenceCost;
            operation.StartedDay=row.Value<float?>("started_day")??operation.StartedDay;operation.DueDay=row.Value<float?>("due_day")??operation.DueDay;operation.Risk=row.Value<float?>("risk")??operation.Risk;operation.Confidence=row.Value<float?>("confidence")??operation.Confidence;
            operation.SourceChainJson=row.Value<string>("source_chain_json")??operation.SourceChainJson;operation.ResultJson=row.Value<string>("result_json")??operation.ResultJson;operation.IsExposed=row.Value<int?>("is_exposed")==1;operation.Revision=row.Value<long?>("revision")??operation.Revision;
        }

        private void UpsertServerObligation(JObject row)
        {
            string id=row.Value<string>("obligation_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            CourtObligation obligation=_obligations.FirstOrDefault(x=>x.ObligationId==id);if(obligation==null){obligation=new CourtObligation{ObligationId=id};_obligations.Add(obligation);}
            obligation.ObligationType=row.Value<string>("obligation_type")??obligation.ObligationType;obligation.OwedByHeroStringId=row.Value<string>("owed_by")??obligation.OwedByHeroStringId;
            obligation.OwedToHeroStringId=row.Value<string>("owed_to")??obligation.OwedToHeroStringId;obligation.Description=row.Value<string>("description")??obligation.Description;
            obligation.CreatedDay=row.Value<float?>("created_day")??obligation.CreatedDay;obligation.DueDay=row.Value<float?>("due_day")??obligation.DueDay;
            obligation.TermsHash=row.Value<string>("terms_hash")??obligation.TermsHash;obligation.BreachRuleJson=row.Value<string>("breach_rule_json")??obligation.BreachRuleJson;
            obligation.Status=row.Value<string>("status")??obligation.Status;obligation.ResolutionJson=row.Value<string>("resolution_json")??obligation.ResolutionJson;obligation.Revision=row.Value<long?>("revision")??obligation.Revision;
            try{JObject payload=JObject.Parse(row.Value<string>("payload_json")??"{}");obligation.TermsJson=JsonText(payload["terms"],obligation.TermsJson);}catch{}
        }

        private void UpsertServerPlot(JObject row)
        {
            string id=row.Value<string>("plot_id")??string.Empty;if(string.IsNullOrWhiteSpace(id))return;
            CourtPlot plot=_plots.FirstOrDefault(x=>x.PlotId==id);if(plot==null){plot=new CourtPlot{PlotId=id};_plots.Add(plot);}
            plot.PlotType=row.Value<string>("plot_type")??plot.PlotType;plot.DirectorHeroStringId=row.Value<string>("director_id")??plot.DirectorHeroStringId;plot.TargetStringId=row.Value<string>("target_id")??plot.TargetStringId;
            plot.ParticipantHeroIdsCsv=JsonIds(row["participants_json"]);plot.Status=row.Value<string>("status")??plot.Status;plot.CreatedDay=row.Value<float?>("created_day")??plot.CreatedDay;plot.DueDay=row.Value<float?>("due_day")??plot.DueDay;
            plot.Progress=row.Value<float?>("progress")??plot.Progress;plot.TermsHash=row.Value<string>("terms_hash")??plot.TermsHash;plot.SecretKnowledgeId=row.Value<string>("secret_knowledge_id")??plot.SecretKnowledgeId;plot.IsDiscoveredByPlayer=true;plot.Revision=row.Value<long?>("revision")??plot.Revision;
        }

        private static ReignIntelligenceOperationState ParseOperationState(string value)
        {
            foreach(ReignIntelligenceOperationState state in Enum.GetValues(typeof(ReignIntelligenceOperationState)))if(string.Equals(state.ToString(),value,StringComparison.OrdinalIgnoreCase))return state;
            return ReignIntelligenceOperationState.Planned;
        }

        private static string JsonText(JToken token, string fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback ?? "{}";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? fallback ?? "{}";
            return token.ToString(Newtonsoft.Json.Formatting.None);
        }

        private static string JsonIds(JToken token)
        {
            string text = JsonText(token, "[]");
            try { return string.Join(",", JArray.Parse(text).Values<string>().Where(x => !string.IsNullOrWhiteSpace(x))); }
            catch { return string.Empty; }
        }

        private static ReignCourtMatterKind ParseMatterKind(string value)
        {
            string normalized = (value ?? string.Empty).Replace("_", string.Empty);
            foreach (ReignCourtMatterKind kind in Enum.GetValues(typeof(ReignCourtMatterKind)))
                if (string.Equals(kind.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) return kind;
            return ReignCourtMatterKind.Petition;
        }

        private static ReignCourtMatterState ParseMatterState(string value)
        {
            string normalized = (value ?? string.Empty).Replace("_", string.Empty);
            foreach (ReignCourtMatterState state in Enum.GetValues(typeof(ReignCourtMatterState)))
                if (string.Equals(state.ToString(), normalized, StringComparison.OrdinalIgnoreCase)) return state;
            return ReignCourtMatterState.Queued;
        }

        private JObject BuildNativeSnapshot()
        {
            return new JObject
            {
                ["playerGold"] = Hero.MainHero?.Gold ?? 0,
                ["playerInfluence"] = Clan.PlayerClan?.Influence ?? 0f,
                ["playerRenown"] = Clan.PlayerClan?.Renown ?? 0f,
                ["militaryStrength"] = _session?.Authority == ReignCourtAuthority.Royal ? Clan.PlayerClan?.Kingdom?.CurrentTotalStrength ?? 0f : Clan.PlayerClan?.CurrentTotalStrength ?? 0f,
                ["strategicSupply"] = _session == null ? 0 : ReignCourtSupplyService.GetStrategicSupplyUnits(_session.Authority),
                ["hostUnderSiege"] = Settlement.All.FirstOrDefault(x => x.StringId == _session?.HostSettlementStringId)?.IsUnderSiege == true
            };
        }

        private static JObject ToServerMatter(CourtMatter matter)
        {
            JArray options;
            try { options = JArray.Parse(matter.DecisionOptionsJson ?? "[]"); } catch { options = new JArray(); }
            JObject defaultOutcome;
            try { defaultOutcome = JObject.Parse(matter.DefaultOutcomeJson ?? "{}"); } catch { defaultOutcome = new JObject(); }
            return new JObject
            {
                ["matterId"] = matter.MatterId, ["sourceKey"] = matter.SourceKey, ["kind"] = matter.Kind.ToString().ToLowerInvariant(),
                ["title"] = matter.Title, ["summary"] = matter.Summary, ["confidentiality"] = matter.Confidentiality,
                ["participants"] = new JArray((matter.ParticipantHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)),
                ["witnesses"] = new JArray((matter.WitnessHeroIdsCsv ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)),
                ["settlementStringId"] = matter.SettlementStringId, ["kingdomStringId"] = matter.KingdomStringId,
                ["createdDay"] = matter.CreatedDay, ["dueDay"] = matter.DueDay, ["priority"] = matter.Priority,
                ["isCritical"] = matter.IsCritical, ["isMajorRealmEvent"] = matter.IsMajorRealmEvent, ["isAmbientRoleplay"] = matter.IsAmbientRoleplay,
                ["options"] = options, ["defaultOutcome"] = defaultOutcome, ["requiresServer"] = matter.RequiresServer
            };
        }
    }
}
