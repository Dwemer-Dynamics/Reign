using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Reign.Core.Contracts.Court;
using ReignBeta.Campaign;
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
using ReignBeta.Characters.Integration;
using ReignBeta.Characters.World;
using ReignBeta.Court.FamilyChambers;
using ReignBeta.Court.Integration;
using ReignBeta.Court.UI;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Persistence.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Persistence.Save;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Rebellion.Campaign;
using ReignBeta.Relationships.Campaign;
using ReignBeta.Relationships.Integration;
using ReignBeta.Spymaster.Campaign;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.World;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Court.Campaign
{
    public sealed partial class ReignCourtCampaignBehavior : CampaignBehaviorBase
    {
        internal List<CourtMatter> _matters = new List<CourtMatter>();
        internal List<CourtAgendaItem> _agenda = new List<CourtAgendaItem>();
        internal List<CourtOfficeAssignment> _offices = new List<CourtOfficeAssignment>();
        internal List<AmbassadorPosting> _ambassadors = new List<AmbassadorPosting>();
        private List<KingdomCapitalDesignation> _capitalDesignations = new List<KingdomCapitalDesignation>();
        internal List<ForeignAmbassadorPosting> _foreignAmbassadors = new List<ForeignAmbassadorPosting>();
        internal List<CapitalAmbassadorTestLedger> _capitalAmbassadorTestLedgers = new List<CapitalAmbassadorTestLedger>();
        internal List<string> _royalCouncilTestMarkers = new List<string>();
        internal List<string> _rulerDocketTestMarkers = new List<string>();
        private List<IntelligenceOperation> _operations = new List<IntelligenceOperation>();
        private List<CourtObligation> _obligations = new List<CourtObligation>();
        private List<CourtPlot> _plots = new List<CourtPlot>();
        private List<CourtCounterSample> _counterSamples = new List<CourtCounterSample>();
        internal List<CourtRegentAssignment> _regents = new List<CourtRegentAssignment>();
        private List<CastleRoomSessionRecord> _castleRoomSessions = new List<CastleRoomSessionRecord>();
        private List<CastleBathHistoryRecord> _castleBathHistory = new List<CastleBathHistoryRecord>();
        private List<string> _appliedNativeCommandIds = new List<string>();
        private List<string> _courtWeddingReceipts = new List<string>();
        private bool _courtWeddingReportInFlight;
        internal CourtSession _session;
        private long _serverRevision;
        private bool _serverSessionOpened;
        private float _lastServerTickDay = -1000f;
        internal bool _serverRequestInFlight;
        private readonly ConcurrentDictionary<string, long> _serverMatterRevisions = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
        // Admission and removal run on the main thread and outlive any individual Court screen.
        private readonly HashSet<(string CampaignId, string TimelineId, string MatterId)> _audienceRepliesInFlight
            = new HashSet<(string, string, string)>();
        private bool _serverAvailable;
        private string _serverStatus = "Court server has not connected.";
        private int _advanceToDocketDay = -1;
        private string _economicReportStateJson = string.Empty;
        private List<string> _economicReportStateChunks = new List<string>();
        private ReignEconomicReportState _economicReportState = new ReignEconomicReportState();
        private string _spymasterStateJson = string.Empty;
        private List<string> _spymasterStateChunks = new List<string>();
        internal ReignSpymasterState _spymasterState = new ReignSpymasterState();
        private readonly HashSet<string> _economicLetterRequestsInFlight = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int _consecutiveAuthorityFailures;
        private string _lastAuthorityFailureReason = string.Empty;

        public static ReignCourtCampaignBehavior Instance => ReignServices.Court;
        // SyncData bounds the saved capital/ambassador acceptance ledgers. Product-owned because it runs on
        // every save and load whether or not the optional test harness assembly is installed.
        internal const int CapitalAmbassadorLedgerMaxCount = 16;
        internal const int CapitalAmbassadorLedgerMaxObservations = 16;
        internal const int CapitalAmbassadorLedgerMaxStoredJsonChars = 2048;

        private void CompactCapitalAmbassadorTestLedgers()
        {
            _capitalAmbassadorTestLedgers = (_capitalAmbassadorTestLedgers ?? new List<CapitalAmbassadorTestLedger>())
                .Where(x=>x!=null&&!string.IsNullOrWhiteSpace(x.RunId))
                .GroupBy(x=>x.RunId,StringComparer.OrdinalIgnoreCase).Select(x=>x.Last())
                .Take(CapitalAmbassadorLedgerMaxCount).ToList();
            foreach(CapitalAmbassadorTestLedger ledger in _capitalAmbassadorTestLedgers)
                CompactCapitalAmbassadorTestLedger(ledger);
        }

        internal static void CompactCapitalAmbassadorTestLedger(CapitalAmbassadorTestLedger ledger)
        {
            if(ledger==null)return;
            ledger.ObservationJson=(ledger.ObservationJson??new List<string>())
                .Where(x=>!string.IsNullOrWhiteSpace(x))
                .Select(x=>CompactCapitalAmbassadorStoredJson(x,"observation"))
                .ToList();
            if(ledger.ObservationJson.Count>CapitalAmbassadorLedgerMaxObservations)
                ledger.ObservationJson=ledger.ObservationJson.Skip(ledger.ObservationJson.Count-CapitalAmbassadorLedgerMaxObservations).ToList();
        }

        private static string CompactCapitalAmbassadorStoredJson(string json,string kind)
        {
            string value=json??string.Empty;
            if(value.Length<=CapitalAmbassadorLedgerMaxStoredJsonChars)return value;
            return new JObject{{"compacted",true},{"kind",kind??string.Empty},{"originalLength",value.Length}}
                .ToString(Formatting.None);
        }

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
        internal event Action<string> AudienceReplyPendingChanged;
        // Lets harness controls raise the event; C# allows raising a field-like event only inside its type.
        internal void RaiseStateChanged() => StateChanged?.Invoke();

        public void EnsureServerSessionAligned()
        {
            if (!IsRuleModeActive || (_serverAvailable && _serverSessionOpened) || _serverRequestInFlight) return;
            _ = OpenServerSessionAsync();
        }

        public override void RegisterEvents()
        {
            ReignServices.Court = this;
            ReignServices.CourtWorld = this;
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
                _economicReportStateJson = string.Empty;
                _economicReportStateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(
                    _economicReportState ?? new ReignEconomicReportState(), Formatting.None));
                _spymasterStateJson = string.Empty;
                _spymasterStateChunks = ReignSavePayloadCodec.Encode(JsonConvert.SerializeObject(
                    EnsureSpymasterState(), Formatting.None));
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
            dataStore.SyncData("_reignCourt_weddingReceiptsV1", ref _courtWeddingReceipts);
            if (dataStore.IsLoading && _courtWeddingReceipts == null) _courtWeddingReceipts = new List<string>();
            dataStore.SyncData("_reignCourt_serverRevision", ref _serverRevision);
            dataStore.SyncData("_reignCourt_serverSessionOpened", ref _serverSessionOpened);
            dataStore.SyncData("_reignCourt_lastServerTickDay", ref _lastServerTickDay);
            dataStore.SyncData("_reignCourt_advanceToDocketDay", ref _advanceToDocketDay);
            dataStore.SyncData("_reignCourt_economicReportState", ref _economicReportStateJson);
            dataStore.SyncData("_reignCourt_economicReportStateChunks_v2", ref _economicReportStateChunks);
            dataStore.SyncData("_reignCourt_spymasterState", ref _spymasterStateJson);
            dataStore.SyncData("_reignCourt_spymasterStateChunks_v2", ref _spymasterStateChunks);
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
                    string economicReportJson = _economicReportStateJson;
                    if (_economicReportStateChunks != null && _economicReportStateChunks.Count > 0)
                        economicReportJson = ReignSavePayloadCodec.Decode(_economicReportStateChunks);
                    else if (economicReportJson?.StartsWith("reign-gzip-v1:", StringComparison.Ordinal) == true)
                        economicReportJson = ReignSavePayloadCodec.DecodeLegacyArchiveEntry(economicReportJson);
                    _economicReportState = string.IsNullOrWhiteSpace(economicReportJson)
                        ? new ReignEconomicReportState()
                        : JsonConvert.DeserializeObject<ReignEconomicReportState>(economicReportJson)
                          ?? throw new InvalidOperationException("Economic report save payload is null.");
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Economic report save state could not be restored: " + ex.Message);
                    // Pending and delivered shipment records must not disappear on load.
                    throw new InvalidOperationException("Economic report save state could not be restored safely.", ex);
                }
                try
                {
                    string spymasterJson = _spymasterStateJson;
                    if (_spymasterStateChunks != null && _spymasterStateChunks.Count > 0)
                        spymasterJson = ReignSavePayloadCodec.Decode(_spymasterStateChunks);
                    else if (spymasterJson?.StartsWith("reign-gzip-v1:", StringComparison.Ordinal) == true)
                        spymasterJson = ReignSavePayloadCodec.DecodeLegacyArchiveEntry(spymasterJson);
                    _spymasterState = string.IsNullOrWhiteSpace(spymasterJson)
                        ? new ReignSpymasterState()
                        : JsonConvert.DeserializeObject<ReignSpymasterState>(spymasterJson)
                          ?? new ReignSpymasterState();
                }
                catch (Exception ex)
                {
                    ReignLog.Warn("Spymaster save state could not be restored: " + ex.Message);
                    // Losing mission and effect receipts could replay irreversible actions.
                    throw new InvalidOperationException("Spymaster save state could not be restored safely.", ex);
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
            string timelineId = ReignServices.WorldHistory?.TimelineId ?? "main";
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
            CourtSession session = _session;
            if (session == null || matter == null || matter.IsTerminal || string.IsNullOrWhiteSpace(matter.MatterId))
                return new ReignCourtDialogueReply { Error = "The audience matter is no longer available." };
            string matterId = matter.MatterId;
            var pendingKey = (session.CampaignId, session.TimelineId, MatterId: matterId);
            if (!await ReignMainThread.InvokeAsync(() => _audienceRepliesInFlight.Add(pendingKey)).ConfigureAwait(false))
                return new ReignCourtDialogueReply { Error = "A reply is already pending for this audience." };

            try
            {
                Task<ReignCourtDialogueReply> request = await ReignMainThread.InvokeAsync(() =>
                {
                    if (_session != session || !_serverSessionOpened || matter.IsTerminal) return null;
                    AudienceReplyPendingChanged?.Invoke(matterId);
                    long revision = _serverMatterRevisions.TryGetValue(matterId, out long known) ? known : matter.Revision;
                    string commandId = string.IsNullOrWhiteSpace(correlationId)
                        ? "court_respond_" + Guid.NewGuid().ToString("N")
                        : correlationId;
                    return ReignCourtDialogueClient.RequestCourtMatterResponseAsync(matter, speaker, playerText, revision, commandId);
                }).ConfigureAwait(false);
                if (request == null)
                    return new ReignCourtDialogueReply { Error = "The audience matter is no longer available." };

                ReignCourtDialogueReply reply = await request.ConfigureAwait(false);
                if (reply.Ok && reply.Revision > 0)
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        if (_session?.CampaignId == pendingKey.CampaignId && _session?.TimelineId == pendingKey.TimelineId)
                            _serverMatterRevisions.AddOrUpdate(matterId, reply.Revision, (_, current) => Math.Max(current, reply.Revision));
                    }).ConfigureAwait(false);
                return reply;
            }
            finally
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    _audienceRepliesInFlight.Remove(pendingKey);
                    AudienceReplyPendingChanged?.Invoke(matterId);
                }).ConfigureAwait(false);
            }
        }

        internal bool IsAudienceReplyPending(string matterId) =>
            _session != null && _audienceRepliesInFlight.Contains((_session.CampaignId, _session.TimelineId, matterId));

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
            _serverMatterRevisions.TryRemove(matter.MatterId, out _);
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
                string reportCommandId = commandId + "_native_report";
                var weddingReceipt = new JObject
                {
                    ["matterId"] = matterId, ["optionId"] = optionId, ["termsHash"] = termsHash,
                    ["revision"] = response.Revision, ["commandId"] = reportCommandId,
                    ["timelineId"] = ReignServices.WorldHistory?.TimelineId ?? "main",
                    ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId()
                };
                NativeCourtExecution execution = await ExecuteNativeActionsAsync(response.NativeActions, weddingReceipt).ConfigureAwait(false);
                if (execution.PendingWedding)
                {
                    await ReignMainThread.InvokeAsync(() =>
                    {
                        matter.State = ReignCourtMatterState.Resolving;
                        matter.SelectedOptionId = optionId;
                        matter.TermsHash = termsHash ?? string.Empty;
                        matter.LastCommandId = commandId;
                        StateChanged?.Invoke();
                    }).ConfigureAwait(false);
                    return string.Empty;
                }
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
            PumpCourtWeddingReports();
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
            // Shipment arrivals are processed hourly by the economy coordinator, including after a ruler change.
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

        private void EnsureRoyalDocket()
        {
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string timelineId = ReignServices.WorldHistory?.TimelineId ?? "main";
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
        internal static int CurrentDay() { return (int)Math.Floor(CampaignTime.Now.ToDays); }
        private static int CurrentHour()
        {
            double day = CampaignTime.Now.ToDays;
            return (int)Math.Floor((day - Math.Floor(day)) * 24d);
        }
        internal static float CurrentDayFloat() { return (float)CampaignTime.Now.ToDays; }

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
    }
}
