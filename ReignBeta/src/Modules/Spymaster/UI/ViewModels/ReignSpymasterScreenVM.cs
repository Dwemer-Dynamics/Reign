using System;
using System.Collections.Generic;
using System.Linq;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Court;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignSpymasterScreenVM : ViewModel
    {
        private readonly ReignCourtCampaignBehavior _court;
        private readonly Action _close;
        private ReignSpymasterPage _page;
        private string _peopleGroup = "own_nobles";
        private string _socialScope = "self";
        private bool _lands = true;
        private ReignSpymasterOptionVM _target;
        private ReignSpymasterOptionVM _action;
        private ReignSpymasterOptionVM _socialItem;
        private ReignSpymasterMissionVM _selectedReport;
        private bool _targetDropdownOpen;
        private bool _actionDropdownOpen;
        private bool _socialDropdownOpen;
        private string _statusText = string.Empty;
        private string _reportText = "Completed intelligence reports and mission outcomes appear here.";
        private string _spymasterName = "Appoint Spymaster";
        private string _portraitCacheKey = string.Empty;
        private string _portraitId = string.Empty;
        private string _portraitArgs = string.Empty;
        private string _portraitProvider = string.Empty;
        private CharacterViewModel _spymasterModel;
        private bool _hasSpymaster;
        private bool _appointmentPending;
        private float _clock;
        private int _socialRefreshGeneration;

        public ReignSpymasterScreenVM(ReignCourtCampaignBehavior court, Action close)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _close = close;
            TargetOptions = new MBBindingList<ReignSpymasterOptionVM>();
            ActionOptions = new MBBindingList<ReignSpymasterOptionVM>();
            SocialItems = new MBBindingList<ReignSpymasterOptionVM>();
            Reports = new MBBindingList<ReignSpymasterMissionVM>();
            _court.StateChanged += OnStateChanged;
            RefreshAll();
        }

        [DataSourceProperty] public MBBindingList<ReignSpymasterOptionVM> TargetOptions { get; }
        [DataSourceProperty] public MBBindingList<ReignSpymasterOptionVM> ActionOptions { get; }
        [DataSourceProperty] public MBBindingList<ReignSpymasterOptionVM> SocialItems { get; }
        [DataSourceProperty] public MBBindingList<ReignSpymasterMissionVM> Reports { get; }
        [DataSourceProperty] public string SpymasterName { get => _spymasterName; private set { if (_spymasterName != value) { _spymasterName = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string PortraitCacheKey { get => _portraitCacheKey; private set { if (_portraitCacheKey != value) { _portraitCacheKey = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(HasPortrait)); OnPropertyChanged(nameof(ShowSpymasterAiPortrait)); OnPropertyChanged(nameof(ShowSpymasterTableauFallback)); } } }
        [DataSourceProperty] public string PortraitId { get => _portraitId; private set { if (_portraitId != value) { _portraitId = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string PortraitArgs { get => _portraitArgs; private set { if (_portraitArgs != value) { _portraitArgs = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string PortraitProvider { get => _portraitProvider; private set { if (_portraitProvider != value) { _portraitProvider = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public CharacterViewModel SpymasterModel { get => _spymasterModel; private set { if (_spymasterModel != value) { _spymasterModel = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(ShowSpymasterTableauFallback)); } } }
        [DataSourceProperty] public bool HasSpymaster { get => _hasSpymaster; private set { if (_hasSpymaster != value) { _hasSpymaster = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(ShowAppointmentPrompt)); OnPropertyChanged(nameof(ShowSpymasterAiPortrait)); OnPropertyChanged(nameof(ShowSpymasterTableauFallback)); } } }
        [DataSourceProperty] public bool ShowAppointmentPrompt => !HasSpymaster;
        [DataSourceProperty] public bool AppointmentPending { get => _appointmentPending; private set { if (_appointmentPending != value) { _appointmentPending = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool HasPortrait => !string.IsNullOrWhiteSpace(PortraitCacheKey);
        [DataSourceProperty] public bool ShowSpymasterAiPortrait => HasSpymaster && HasPortrait;
        [DataSourceProperty] public bool ShowSpymasterTableauFallback => HasSpymaster && !HasPortrait && SpymasterModel != null;
        [DataSourceProperty] public bool IsIntelligence => _page == ReignSpymasterPage.Intelligence;
        [DataSourceProperty] public bool IsSubterfuge => _page == ReignSpymasterPage.Subterfuge;
        [DataSourceProperty] public bool IsReputation => _page == ReignSpymasterPage.Reputation;
        [DataSourceProperty] public bool IsArchive => _page == ReignSpymasterPage.Archive;
        [DataSourceProperty] public bool ShowOperationPanel => !IsArchive;
        [DataSourceProperty] public bool IsLands => IsIntelligence && _lands;
        [DataSourceProperty] public bool IsPeople => IsIntelligence && !_lands;
        [DataSourceProperty] public bool ShowPeopleGroups => IsPeople;
        [DataSourceProperty] public bool ShowSocialItem => IsReputation && _action != null && (_action.Id.Contains("mitigate"));
        [DataSourceProperty] public string PeopleGroupText => _peopleGroup == "own_nobles" ? "OWN NOBLES" : _peopleGroup == "notables" ? "YOUR NOTABLES" : "FOREIGN NOBLES";
        [DataSourceProperty] public string SocialScopeText => _socialScope == "self" ? "YOUR REPUTATION" : _socialScope == "kingdom" ? "YOUR KINGDOM" : "FOREIGN LORDS";
        [DataSourceProperty] public string TargetName => _target?.Label ?? "Select target";
        [DataSourceProperty] public string TargetDetail => _target?.Detail ?? string.Empty;
        [DataSourceProperty] public string ActionName => _action?.Label ?? "Select operation";
        [DataSourceProperty] public string OperationDetail => QuoteText + "  •  Capacity " + _court.ActiveSpymasterMissionCount + "/" + _court.SpymasterMissionCapacity;
        [DataSourceProperty] public string SocialItemName => _socialItem?.Label ?? "Select rumor or reputation";
        [DataSourceProperty] public bool TargetDropdownOpen { get => _targetDropdownOpen; set { if (_targetDropdownOpen != value) { _targetDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool ActionDropdownOpen { get => _actionDropdownOpen; set { if (_actionDropdownOpen != value) { _actionDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool SocialDropdownOpen { get => _socialDropdownOpen; set { if (_socialDropdownOpen != value) { _socialDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string ReportText { get => _reportText; set { if (_reportText != value) { _reportText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string QuoteText
        {
            get
            {
                if (_action == null || _court.ActiveSpymaster == null) return "Appoint a Spymaster and choose an operation.";
                ReignSpymasterMissionQuote quote =
                    _court.GetSpymasterQuote(_action.Id, CurrentTargetType(), _target?.Id);
                return quote.GoldCost.ToString("N0") + " denars  •  " + quote.DurationDays.ToString("0.0") + " days  •  Success "
                    + quote.SuccessChance.ToString("0") + "%  •  Detection " + quote.DetectionChance.ToString("0") + "%  •  " + quote.DifficultyLabel;
            }
        }
        [DataSourceProperty] public string AssessmentText
        {
            get
            {
                if (_action == null || _court.ActiveSpymaster == null) return "UNKNOWN";
                return (_court.GetSpymasterQuote(_action.Id, CurrentTargetType(), _target?.Id)?.DifficultyLabel
                    ?? "UNKNOWN").ToUpperInvariant();
            }
        }
        [DataSourceProperty] public bool IsChallengingAssessment =>
            string.Equals(AssessmentText, "CHALLENGING", StringComparison.OrdinalIgnoreCase);
        [DataSourceProperty] public bool IsOrdinaryAssessment => !IsChallengingAssessment;
        [DataSourceProperty] public bool CanBegin => _court.ActiveSpymaster?.IsAlive == true && _court.ActiveSpymaster.IsActive
            && !_court.ActiveSpymaster.IsPrisoner && _court.ActiveSpymasterMissionCount < _court.SpymasterMissionCapacity
            && _target != null && _action != null && (!ShowSocialItem || _socialItem != null);
        [DataSourceProperty] public bool CanBreakout => _court.CanAttemptSpymasterBreakout;
        [DataSourceProperty] public string BreakoutText => "BREAK OUT SPYMASTER — " + _court.GetSpymasterBreakoutChance().ToString("0") + "%";

        public void OnFrameTick(float dt)
        {
            _clock += dt;
            if (_clock < 1f) return;
            _clock = 0f;
            RefreshReports(false);
        }

        public override void OnFinalize()
        {
            _court.StateChanged -= OnStateChanged;
            base.OnFinalize();
        }

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteIntelligence() { _page = ReignSpymasterPage.Intelligence; RefreshPage(); }
        public void ExecuteSubterfuge() { _page = ReignSpymasterPage.Subterfuge; RefreshPage(); }
        public void ExecuteReputation() { _page = ReignSpymasterPage.Reputation; RefreshPage(); }
        public void ExecuteArchive() { _page = ReignSpymasterPage.Archive; RefreshPage(); }
        public void ExecuteLands() { _lands = true; RefreshPage(); }
        public void ExecutePeople() { _lands = false; RefreshPage(); }
        public void ExecuteNextPeopleGroup()
        {
            _peopleGroup = _peopleGroup == "own_nobles" ? "notables" : _peopleGroup == "notables" ? "foreign" : "own_nobles";
            OnPropertyChanged(nameof(PeopleGroupText));
            RefreshPage();
        }
        public void ExecuteNextSocialScope()
        {
            _socialScope = _socialScope == "self" ? "kingdom" : _socialScope == "kingdom" ? "foreign" : "self";
            OnPropertyChanged(nameof(SocialScopeText));
            RefreshPage();
        }
        public void ExecuteToggleTargetDropdown() { TargetDropdownOpen = !TargetDropdownOpen; ActionDropdownOpen = false; SocialDropdownOpen = false; }
        public void ExecuteToggleActionDropdown() { ActionDropdownOpen = !ActionDropdownOpen; TargetDropdownOpen = false; SocialDropdownOpen = false; }
        public void ExecuteToggleSocialDropdown() { SocialDropdownOpen = !SocialDropdownOpen; TargetDropdownOpen = false; ActionDropdownOpen = false; }
        public void ExecuteBreakout()
        {
            if (!CanBreakout) return;
            InformationManager.ShowInquiry(new InquiryData("Break Out the Spymaster",
                "The ruler will personally attempt the rescue. Success frees the Spymaster without exposing your involvement. Failure imprisons both characters and exposes the entire operation. Assessed success: "
                + _court.GetSpymasterBreakoutChance().ToString("0") + "%.", true, true, "Attempt Breakout", "Cancel", () =>
                {
                    string error = _court.AttemptSpymasterBreakout();
                    StatusText = string.IsNullOrWhiteSpace(error) ? "The breakout succeeded. Your involvement remains concealed." : error;
                    RefreshAll();
                }, null), true);
        }

        public void ExecuteChooseSpymaster()
        {
            if (AppointmentPending)
            {
                StatusText = "The court is still recording the current appointment.";
                return;
            }
            List<Hero> heroes = Hero.AllAliveHeroes.Where(hero => ReignCourtOfficeService.IsEligible(hero,
                _court.Session?.Authority ?? ReignCourtAuthority.Royal,
                _court.Offices.Where(x => x.Office != ReignCourtOffice.Spymaster || !x.IsActive), out _))
                .OrderBy(x => x.Name?.ToString()).ToList();
            if (heroes.Count == 0)
            {
                StatusText = "No eligible adult courtier is available for appointment.";
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + StatusText));
                return;
            }
            List<InquiryElement> choices = heroes.Select(hero => new InquiryElement(hero,
                hero.Name?.ToString() ?? hero.StringId, null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData("Choose Spymaster",
                "Choose a trusted courtier to direct intelligence and covert operations. Candidate skills remain private.", choices,
                true, 1, 1, "Appoint", "Cancel", selected =>
                {
                    Hero hero = selected?.FirstOrDefault()?.Identifier as Hero;
                    if (hero == null) return;
                    AppointmentPending = true;
                    StatusText = "Appointing " + (hero.Name?.ToString() ?? hero.StringId) + " as Spymaster...";
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + StatusText));
                    _ = AssignSpymasterAsync(hero);
                }, null, string.Empty, false), true, false);
        }

        private async System.Threading.Tasks.Task AssignSpymasterAsync(Hero hero)
        {
            try
            {
                string error = await _court.AssignOfficeAsync(ReignCourtOffice.Spymaster, hero).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    AppointmentPending = false;
                    StatusText = string.IsNullOrWhiteSpace(error)
                        ? (hero.Name?.ToString() ?? hero.StringId) + " is now the Spymaster."
                        : "Appointment failed: " + error;
                    RefreshAll();
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + StatusText));
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    AppointmentPending = false;
                    StatusText = "Appointment failed: " + ex.Message;
                    RefreshAll();
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + StatusText));
                }).ConfigureAwait(false);
            }
        }

        internal async System.Threading.Tasks.Task<string> TryExecuteAutomationAppointmentAsync(string value)
        {
            Hero incumbent = _court.ActiveSpymaster;
            if (incumbent?.IsAlive == true && incumbent.IsActive && !incumbent.IsPrisoner)
            {
                RefreshAll();
                return string.Empty;
            }
            if (AppointmentPending) return "The court is still recording the current appointment.";

            DateTime serverDeadline = DateTime.UtcNow.AddSeconds(60);
            while ((!_court.ServerAvailable || !_court.ServerSessionOpened || _court.ServerRequestInFlight)
                && DateTime.UtcNow < serverDeadline)
            {
                await ReignMainThread.InvokeAsync(_court.EnsureServerSessionAligned).ConfigureAwait(false);
                if (_court.ServerAvailable && _court.ServerSessionOpened && !_court.ServerRequestInFlight) break;
                await System.Threading.Tasks.Task.Delay(250).ConfigureAwait(false);
            }
            if (!_court.ServerAvailable || !_court.ServerSessionOpened || _court.ServerRequestInFlight)
                return string.IsNullOrWhiteSpace(_court.ServerStatus)
                    ? "The court server did not become ready for appointment within 60 seconds."
                    : _court.ServerStatus;

            List<Hero> heroes = Hero.AllAliveHeroes.Where(hero => ReignCourtOfficeService.IsEligible(hero,
                _court.Session?.Authority ?? ReignCourtAuthority.Royal,
                _court.Offices.Where(x => x.Office != ReignCourtOffice.Spymaster || !x.IsActive), out _))
                .OrderBy(x => x.Name?.ToString()).ThenBy(x => x.StringId).ToList();
            string selector = (value ?? string.Empty).Trim();
            Hero selected = string.IsNullOrWhiteSpace(selector)
                ? heroes.FirstOrDefault()
                : heroes.FirstOrDefault(hero => string.Equals(hero.StringId, selector, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(hero.Name?.ToString(), selector, StringComparison.OrdinalIgnoreCase));
            if (selected == null) return string.IsNullOrWhiteSpace(selector)
                ? "No eligible adult courtier is available for appointment."
                : "No eligible Spymaster candidate matches '" + selector + "'.";

            AppointmentPending = true;
            StatusText = "Appointing " + (selected.Name?.ToString() ?? selected.StringId) + " as Spymaster...";
            string error;
            try
            {
                error = await _court.AssignOfficeAsync(ReignCourtOffice.Spymaster, selected).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                AppointmentPending = false;
                StatusText = string.IsNullOrWhiteSpace(error)
                    ? (selected.Name?.ToString() ?? selected.StringId) + " is now the Spymaster."
                    : "Appointment failed: " + error;
                RefreshAll();
            }).ConfigureAwait(false);
            return error ?? string.Empty;
        }

        public void ExecuteBegin()
        {
            if (!CanBegin) { StatusText = "Complete the operation selections first."; return; }
            if (_action.Id.StartsWith("fabricate_", StringComparison.OrdinalIgnoreCase))
            {
                InformationManager.ShowTextInquiry(new TextInquiryData("Fabricate Rumor or Reputation",
                    "Describe the story your Spymaster should circulate. The server will store it as an unverified social claim.",
                    true, true, "Begin", "Cancel", text => StartSelected(text), null), true);
                return;
            }
            StartSelected(string.Empty);
        }

        internal bool TryExecuteAutomationSelection(string kind, string value, out string error)
        {
            error = string.Empty;
            string normalizedKind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            string selector = (value ?? string.Empty).Trim();
            if (normalizedKind == "target")
            {
                ReignSpymasterOptionVM target = SelectAutomationTarget(selector);
                if (target == null) { error = "No Spymaster target matches '" + selector + "'."; return false; }
                SelectTarget(target);
                return true;
            }
            if (normalizedKind == "action")
            {
                ReignSpymasterOptionVM action = ActionOptions.FirstOrDefault(x => string.Equals(x.Id, selector, StringComparison.OrdinalIgnoreCase));
                if (action == null) { error = "No Spymaster action matches '" + selector + "'."; return false; }
                SelectAction(action);
                return true;
            }
            if (normalizedKind == "social")
            {
                ReignSpymasterOptionVM social = string.IsNullOrWhiteSpace(selector) || string.Equals(selector, "first", StringComparison.OrdinalIgnoreCase)
                    ? SocialItems.FirstOrDefault() : SocialItems.FirstOrDefault(x => string.Equals(x.Id, selector, StringComparison.OrdinalIgnoreCase));
                if (social == null) { error = "No applicable Spymaster rumor or reputation item is currently loaded."; return false; }
                SelectSocialItem(social);
                return true;
            }
            error = "Unsupported Spymaster selection kind '" + kind + "'.";
            return false;
        }

        internal async System.Threading.Tasks.Task<string> TryExecuteAutomationSocialSelectionAsync(string value)
        {
            await RefreshSocialItemsAsync().ConfigureAwait(false);
            return await ReignMainThread.InvokeAsync(() =>
            {
                TryExecuteAutomationSelection("social", value, out string error);
                return error;
            }).ConfigureAwait(false);
        }

        internal async System.Threading.Tasks.Task<string> TryExecuteAutomationSocialTargetSelectionAsync(string value)
        {
            string selector = (value ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            bool reputation = string.Equals(selector, "foreign-noble-with-reputation", StringComparison.OrdinalIgnoreCase);
            bool rumor = string.Equals(selector, "foreign-noble-with-rumor", StringComparison.OrdinalIgnoreCase);
            if (!reputation && !rumor)
                return "Unsupported Spymaster social target selector '" + value + "'.";

            string missionType = reputation ? "fabricate_severe" : "fabricate_moderate";
            List<string> candidateIds = await ReignMainThread.InvokeAsync(() =>
                _court.EnsureSpymasterState().Missions
                    .Where(mission => mission != null
                        && mission.State == ReignSpymasterMissionState.Succeeded
                        && string.Equals(mission.MissionType, missionType, StringComparison.OrdinalIgnoreCase)
                        && TargetOptions.Any(option => string.Equals(option.Id, mission.TargetStringId, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(mission => mission.ResolvedDay)
                    .Select(mission => mission.TargetStringId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()).ConfigureAwait(false);

            foreach (string candidateId in candidateIds)
            {
                JObject status;
                try { status = await ReignSpymasterServerClient.GetSocialStatusAsync(candidateId).ConfigureAwait(false); }
                catch { continue; }
                JArray rows = status?[reputation ? "reputations" : "rumors"] as JArray ?? new JArray();
                if (rows.Count == 0) continue;
                return await ReignMainThread.InvokeAsync(() =>
                {
                    ReignSpymasterOptionVM option = TargetOptions.FirstOrDefault(x =>
                        string.Equals(x.Id, candidateId, StringComparison.OrdinalIgnoreCase));
                    if (option == null) return "The matching Spymaster social target is no longer eligible.";
                    SelectTarget(option);
                    return string.Empty;
                }).ConfigureAwait(false);
            }

            return reputation
                ? "No active foreign noble with an established Spymaster reputation is currently available."
                : "No active foreign noble with a Spymaster rumor is currently available.";
        }

        internal bool TryExecuteAutomationBegin(string fabricatedText, out string error)
        {
            error = string.Empty;
            if (!CanBegin) { error = "Complete the operation selections first."; StatusText = error; return false; }
            if (_action.Id.StartsWith("fabricate_", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(fabricatedText))
            {
                error = "An organic fabrication test must supply the text that the normal inquiry would collect.";
                StatusText = error;
                return false;
            }
            error = _court.StartSpymasterMission(_action.Id, IsSubterfuge || IsLands ? "settlement" : "person",
                _target.Id, _target.Label, _socialItem?.Id, fabricatedText ?? string.Empty);
            StatusText = string.IsNullOrWhiteSpace(error)
                ? "Operation dispatched. The assigned Spymaster has committed every detail to memory." : error;
            RefreshReports(true);
            OnPropertyChanged(nameof(QuoteText));
            OnPropertyChanged(nameof(OperationDetail));
            OnPropertyChanged(nameof(AssessmentText));
            OnPropertyChanged(nameof(IsChallengingAssessment));
            OnPropertyChanged(nameof(IsOrdinaryAssessment));
            return string.IsNullOrWhiteSpace(error);
        }

        private ReignSpymasterOptionVM SelectAutomationTarget(string selector)
        {
            if (TargetOptions.Count == 0) return null;
            if (string.IsNullOrWhiteSpace(selector) || string.Equals(selector, "first", StringComparison.OrdinalIgnoreCase))
                return TargetOptions.FirstOrDefault();
            ReignSpymasterOptionVM exact = TargetOptions.FirstOrDefault(x => string.Equals(x.Id, selector, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            Kingdom player = Clan.PlayerClan?.Kingdom;
            if (string.Equals(selector, "fresh-foreign-noble", StringComparison.OrdinalIgnoreCase))
            {
                List<ReignSpymasterMission> missions = _court.EnsureSpymasterState().Missions;
                return TargetOptions
                    .Where(option =>
                    {
                        Hero hero = FindHero(option.Id);
                        return hero?.Clan?.Kingdom != null && hero.Clan.Kingdom != player;
                    })
                    .OrderBy(option => missions.Any(mission => string.Equals(
                        mission.TargetStringId, option.Id, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
                    .ThenBy(option => missions.Where(mission => string.Equals(
                            mission.TargetStringId, option.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(mission => mission.StartedDay).DefaultIfEmpty(-1f).Max())
                    .ThenBy(option => option.Label)
                    .FirstOrDefault();
            }
            if (string.Equals(selector, "stable-fresh-foreign-noble", StringComparison.OrdinalIgnoreCase))
            {
                List<ReignSpymasterMission> missions = _court.EnsureSpymasterState().Missions;
                return TargetOptions
                    .Select(option => new { Option = option, Hero = FindHero(option.Id) })
                    .Where(candidate => candidate.Hero?.Clan?.Kingdom != null
                        && candidate.Hero.Clan.Kingdom != player
                        && candidate.Hero.IsAlive
                        && candidate.Hero.IsActive
                        && !candidate.Hero.IsPrisoner
                        && candidate.Hero.Age >= 18f)
                    .OrderBy(candidate => missions.Any(mission => string.Equals(
                        mission.TargetStringId, candidate.Option.Id, StringComparison.OrdinalIgnoreCase)) ? 1 : 0)
                    .ThenBy(candidate => candidate.Hero.PartyBelongedTo == null ? 0 : 1)
                    .ThenBy(candidate => candidate.Hero.Clan?.Kingdom?.Leader == candidate.Hero ? 1 : 0)
                    .ThenBy(candidate => candidate.Hero.Age)
                    .ThenBy(candidate => candidate.Option.Label)
                    .Select(candidate => candidate.Option)
                    .FirstOrDefault();
            }
            if (string.Equals(selector, "foreign", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selector, "foreign-noble", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selector, "foreign-ruler", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selector, "foreign-governed", StringComparison.OrdinalIgnoreCase))
            {
                foreach (ReignSpymasterOptionVM option in TargetOptions)
                {
                    Settlement settlement = Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, option.Id, StringComparison.OrdinalIgnoreCase));
                    if (string.Equals(selector, "foreign-governed", StringComparison.OrdinalIgnoreCase))
                    {
                        if (settlement?.Town?.OwnerClan?.Kingdom != null && settlement.Town.OwnerClan.Kingdom != player
                            && settlement.Town.Governor?.IsAlive == true) return option;
                        continue;
                    }
                    if (settlement?.Town?.OwnerClan?.Kingdom != null && settlement.Town.OwnerClan.Kingdom != player) return option;
                    Hero hero = FindHero(option.Id);
                    if (hero?.Clan?.Kingdom == null || hero.Clan.Kingdom == player) continue;
                    if (string.Equals(selector, "foreign-ruler", StringComparison.OrdinalIgnoreCase) && hero.Clan.Kingdom.Leader != hero) continue;
                    return option;
                }
            }
            if (string.Equals(selector, "player", StringComparison.OrdinalIgnoreCase)
                || string.Equals(selector, "player-holding", StringComparison.OrdinalIgnoreCase))
            {
                return TargetOptions.FirstOrDefault(option =>
                {
                    Settlement settlement = Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, option.Id, StringComparison.OrdinalIgnoreCase));
                    Hero hero = FindHero(option.Id);
                    return settlement?.Town?.OwnerClan?.Kingdom == player || hero == Hero.MainHero || hero?.Clan?.Kingdom == player;
                });
            }
            return null;
        }

        private void StartSelected(string fabricatedText)
        {
            TryExecuteAutomationBegin(fabricatedText, out _);
        }

        private void RefreshAll()
        {
            RefreshSpymaster();
            RefreshPage();
            RefreshReports(true);
        }

        private void OnStateChanged()
        {
            // Mission and court updates can arrive while a social-status request is in flight.
            // Rebuilding the page here clears the selected rumor/reputation between the user's
            // selection and Begin. Keep the current controls stable and refresh only live state.
            RefreshSpymaster();
            RefreshReports(true);
            RefreshSelectionProperties();
            OnPropertyChanged(nameof(CanBreakout));
            OnPropertyChanged(nameof(BreakoutText));
        }

        private void RefreshSpymaster()
        {
            Hero hero = _court.ActiveSpymaster;
            HasSpymaster = hero != null;
            SpymasterName = hero?.Name?.ToString() ?? "Appoint Spymaster";
            SpymasterModel = BuildSpymasterModel(hero);
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                ImageIdentifierVM portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
                PortraitId = portrait?.Id ?? string.Empty;
                PortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
                PortraitProvider = portrait?.TextureProviderName ?? string.Empty;
            }
            catch { PortraitId = PortraitArgs = PortraitProvider = string.Empty; }
            OnPropertyChanged(nameof(CanBegin));
            OnPropertyChanged(nameof(QuoteText));
            OnPropertyChanged(nameof(OperationDetail));
            OnPropertyChanged(nameof(AssessmentText));
            OnPropertyChanged(nameof(IsChallengingAssessment));
            OnPropertyChanged(nameof(IsOrdinaryAssessment));
            OnPropertyChanged(nameof(CanBreakout));
            OnPropertyChanged(nameof(BreakoutText));
        }

        private static CharacterViewModel BuildSpymasterModel(Hero hero)
        {
            if (hero?.CharacterObject == null) return null;
            try
            {
                HeroViewModel model = new HeroViewModel();
                model.FillFrom(hero, 0, useCivilian: true);
                Equipment equipment = hero.CivilianEquipment
                    ?? hero.CharacterObject.FirstCivilianEquipment
                    ?? hero.CharacterObject.Equipment;
                if (equipment != null) model.SetEquipment(equipment);
                model.IsTableauEnabled = true;
                model.HasMount = false;
                model.MountCreationKey = string.Empty;
                return model;
            }
            catch
            {
                return null;
            }
        }

        private void RefreshPage()
        {
            string preferredTargetId = _target?.Id;
            string preferredActionId = _action?.Id;
            string preferredSocialItemId = _socialItem?.Id;
            OnPropertyChanged(nameof(IsIntelligence)); OnPropertyChanged(nameof(IsSubterfuge));
            OnPropertyChanged(nameof(IsReputation)); OnPropertyChanged(nameof(IsArchive));
            OnPropertyChanged(nameof(ShowOperationPanel));
            OnPropertyChanged(nameof(IsLands)); OnPropertyChanged(nameof(IsPeople)); OnPropertyChanged(nameof(ShowPeopleGroups));
            TargetDropdownOpen = ActionDropdownOpen = SocialDropdownOpen = false;
            TargetOptions.Clear(); ActionOptions.Clear(); SocialItems.Clear();

            if (IsArchive) { _target = _action = _socialItem = null; RefreshSelectionProperties(); return; }
            if (IsIntelligence && _lands)
            {
                AddSettlementTargets(false);
                AddActions(new[] { Pair("land_intelligence", "Gather settlement intelligence"), Pair("counterintelligence", "Counterintelligence sweep") });
            }
            else if (IsIntelligence)
            {
                IEnumerable<Hero> heroes = PeopleForGroup();
                AddHeroTargets(heroes);
                AddActions(new[] { Pair("person_skills", "Investigate skills"), Pair("person_relationships", "Investigate relationships"),
                    Pair("person_rumors", "Investigate rumors & reputation"), Pair("assassinate_person", "Assassinate target") });
            }
            else if (IsSubterfuge)
            {
                AddSettlementTargets(true);
                AddActions(new[] { Pair("disrupt_food", "Disrupt food"), Pair("disrupt_construction", "Disrupt construction"),
                    Pair("disrupt_security", "Disrupt security"), Pair("disrupt_loyalty", "Disrupt loyalty"), Pair("assassinate_governor", "Assassinate governor") });
            }
            else
            {
                AddHeroTargets(PeopleForSocialScope());
                AddActions(_socialScope == "self"
                    ? new[] { Pair("mitigate_own_rumor", "Mitigate one of your rumors"), Pair("mitigate_own_reputation", "Mitigate one of your reputations") }
                    : new[] { Pair("mitigate_target_rumor", "Mitigate target rumor"), Pair("mitigate_target_reputation", "Mitigate target reputation"),
                        Pair("fabricate_positive", "Promote positive reputation"), Pair("fabricate_moderate", "Fabricate harmful rumor"), Pair("fabricate_severe", "Fabricate severe reputation") });
            }
            _target = TargetOptions.FirstOrDefault(x => string.Equals(x.Id, preferredTargetId, StringComparison.OrdinalIgnoreCase))
                ?? TargetOptions.FirstOrDefault();
            _action = ActionOptions.FirstOrDefault(x => string.Equals(x.Id, preferredActionId, StringComparison.OrdinalIgnoreCase))
                ?? ActionOptions.FirstOrDefault();
            SetSelected(TargetOptions, _target); SetSelected(ActionOptions, _action);
            RefreshSelectionProperties();
            if (IsReputation) _ = RefreshSocialItemsAsync(preferredSocialItemId);
        }

        private void AddSettlementTargets(bool foreignOnly)
        {
            Kingdom player = Clan.PlayerClan?.Kingdom;
            foreach (var snapshot in ReignCourtSupplyService.GetAllFortificationSnapshots()
                .Where(x => !foreignOnly || !string.Equals(x.OwnerKingdomStringId, player?.StringId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => string.Equals(x.OwnerKingdomStringId, player?.StringId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(x => Settlement.All.FirstOrDefault(s => string.Equals(s.StringId, x.SettlementStringId, StringComparison.OrdinalIgnoreCase))?.IsTown == true ? 0 : 1)
                .ThenBy(x => x.Name))
            {
                Settlement settlement = Settlement.All.FirstOrDefault(x => string.Equals(x.StringId, snapshot.SettlementStringId, StringComparison.OrdinalIgnoreCase));
                string group = settlement?.MapFaction == player ? "YOUR REALM" : settlement?.MapFaction == null ? "INDEPENDENT" : "FOREIGN";
                string kind = settlement?.IsTown == true ? "TOWN" : "CASTLE";
                string detail = group + " — " + kind + ". " + snapshot.OwnerClanName + "; governor " + snapshot.GovernorName + ".";
                TargetOptions.Add(new ReignSpymasterOptionVM(snapshot.SettlementStringId, snapshot.Name, detail, SelectTarget));
            }
        }

        internal string AutomationPage => _page.ToString();
        internal string AutomationTargetId => _target?.Id ?? string.Empty;
        internal string AutomationActionId => _action?.Id ?? string.Empty;
        internal int AutomationTargetCount => TargetOptions.Count;
        internal int AutomationActionCount => ActionOptions.Count;
        internal int AutomationReportCount => Reports.Count;
        internal bool AutomationCanBegin => CanBegin;
        internal string AutomationStatus => StatusText ?? string.Empty;
        internal string AutomationOperationDetail => OperationDetail ?? string.Empty;
        internal string AutomationPeopleGroup => _peopleGroup;
        internal string AutomationSocialScope => _socialScope;

        private void AddHeroTargets(IEnumerable<Hero> heroes)
        {
            foreach (Hero hero in (heroes ?? Enumerable.Empty<Hero>())
                .Where(x => x != null && x.IsAlive && x.IsActive)
                .Distinct().OrderBy(x => x.Name?.ToString()))
                TargetOptions.Add(new ReignSpymasterOptionVM(hero.StringId, hero.Name?.ToString() ?? hero.StringId,
                    hero.Clan == null ? (hero.HomeSettlement?.Name?.ToString() ?? "Notable") : "Clan tier " + hero.Clan.Tier, SelectTarget));
        }

        private IEnumerable<Hero> PeopleForGroup()
        {
            Kingdom player = Clan.PlayerClan?.Kingdom;
            if (_peopleGroup == "own_nobles") return Hero.AllAliveHeroes.Where(x => x.IsLord && x.Clan?.Kingdom == player);
            if (_peopleGroup == "notables") return Settlement.All.Where(x => x.MapFaction == player).SelectMany(x => x.Notables).Where(x => x.IsAlive);
            return Hero.AllAliveHeroes.Where(x => x.IsLord && x.Clan?.Kingdom != null && x.Clan.Kingdom != player);
        }

        private IEnumerable<Hero> PeopleForSocialScope()
        {
            Kingdom player = Clan.PlayerClan?.Kingdom;
            if (_socialScope == "self") return new[] { Hero.MainHero };
            if (_socialScope == "kingdom") return Hero.AllAliveHeroes.Where(x => x.IsLord && x.Clan?.Kingdom == player && x != Hero.MainHero);
            return Hero.AllAliveHeroes.Where(x => x.IsLord && x.Clan?.Kingdom != null && x.Clan.Kingdom != player);
        }

        private void AddActions(IEnumerable<Tuple<string, string>> actions)
        {
            foreach (var action in actions) ActionOptions.Add(new ReignSpymasterOptionVM(action.Item1, action.Item2, string.Empty, SelectAction));
        }

        private static Tuple<string, string> Pair(string id, string label) => Tuple.Create(id, label);
        private void SelectTarget(ReignSpymasterOptionVM option)
        {
            _target = option; SetSelected(TargetOptions, option); TargetDropdownOpen = false; RefreshSelectionProperties();
            if (IsReputation) _ = RefreshSocialItemsAsync();
        }
        private void SelectAction(ReignSpymasterOptionVM option)
        {
            _action = option; SetSelected(ActionOptions, option); ActionDropdownOpen = false; _socialItem = null; RefreshSelectionProperties();
            if (IsReputation) _ = RefreshSocialItemsAsync();
        }
        private void SelectSocialItem(ReignSpymasterOptionVM option)
        {
            _socialItem = option; SetSelected(SocialItems, option); SocialDropdownOpen = false; RefreshSelectionProperties();
        }

        private async System.Threading.Tasks.Task RefreshSocialItemsAsync(string preferredSocialItemId = null)
        {
            int refreshGeneration = ++_socialRefreshGeneration;
            string targetId = _target?.Id;
            string actionId = _action?.Id;
            SocialItems.Clear(); _socialItem = null;
            if (_target == null || _action == null || !_action.Id.Contains("mitigate")) { RefreshSelectionProperties(); return; }
            try
            {
                JObject status = await ReignSpymasterServerClient.GetSocialStatusAsync(_target.Id).ConfigureAwait(false);
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (refreshGeneration != _socialRefreshGeneration
                        || !string.Equals(_target?.Id, targetId, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(_action?.Id, actionId, StringComparison.OrdinalIgnoreCase)) return;
                    bool reputation = _action.Id.Contains("reputation");
                    JArray rows = status?[reputation ? "reputations" : "rumors"] as JArray ?? new JArray();
                    foreach (JToken row in rows)
                    {
                        string id = (string)row[reputation ? "tag_id" : "tag_id"] ?? (string)row["occurrence_id"] ?? string.Empty;
                        string label = (string)row["description"] ?? id;
                        SocialItems.Add(new ReignSpymasterOptionVM(id, label, reputation ? "Reputation" : "Rumor", SelectSocialItem));
                    }
                    _socialItem = SocialItems.FirstOrDefault(x => string.Equals(x.Id, preferredSocialItemId, StringComparison.OrdinalIgnoreCase))
                        ?? SocialItems.FirstOrDefault();
                    SetSelected(SocialItems, _socialItem); RefreshSelectionProperties();
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await ReignMainThread.InvokeAsync(() =>
                {
                    if (refreshGeneration != _socialRefreshGeneration) return;
                    StatusText = "Social records unavailable: " + ex.Message;
                    RefreshSelectionProperties();
                }).ConfigureAwait(false);
            }
        }

        private void RefreshSelectionProperties()
        {
            OnPropertyChanged(nameof(TargetName)); OnPropertyChanged(nameof(TargetDetail));
            OnPropertyChanged(nameof(ActionName)); OnPropertyChanged(nameof(OperationDetail));
            OnPropertyChanged(nameof(SocialItemName)); OnPropertyChanged(nameof(ShowSocialItem));
            OnPropertyChanged(nameof(QuoteText)); OnPropertyChanged(nameof(AssessmentText));
            OnPropertyChanged(nameof(IsChallengingAssessment)); OnPropertyChanged(nameof(IsOrdinaryAssessment));
            OnPropertyChanged(nameof(CanBegin));
        }

        private static void SetSelected(IEnumerable<ReignSpymasterOptionVM> rows, ReignSpymasterOptionVM selected)
        {
            foreach (ReignSpymasterOptionVM row in rows) row.IsSelected = row == selected;
        }

        private void RefreshReports(bool force)
        {
            string selectedId = _selectedReport?.Mission?.MissionId;
            List<ReignSpymasterMission> rows = _court.EnsureSpymasterState().Missions.OrderByDescending(x => x.StartedDay).ToList();
            if (!force && Reports.Count == rows.Count && Reports.Zip(rows, (a, b) => a.Mission?.MissionId == b.MissionId && a.Mission?.State == b.State).All(x => x)) return;
            Reports.Clear();
            foreach (ReignSpymasterMission mission in rows) Reports.Add(new ReignSpymasterMissionVM(mission, SelectReport));
            _selectedReport = Reports.FirstOrDefault(x => x.Mission.MissionId == selectedId) ?? Reports.FirstOrDefault();
            foreach (ReignSpymasterMissionVM row in Reports) row.IsSelected = row == _selectedReport;
            ReportText = _selectedReport?.Mission?.ResultSummary;
            if (string.IsNullOrWhiteSpace(ReportText)) ReportText = _selectedReport?.Mission?.RequestSummary ?? "No intelligence operations have been recorded.";
        }

        private void SelectReport(ReignSpymasterMissionVM report)
        {
            _selectedReport = report;
            foreach (ReignSpymasterMissionVM row in Reports) row.IsSelected = row == report;
            ReportText = string.IsNullOrWhiteSpace(report?.Mission?.ResultSummary) ? report?.Mission?.RequestSummary : report.Mission.ResultSummary;
        }

        private static Hero FindHero(string id) => string.IsNullOrWhiteSpace(id) ? null
            : Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase));

        private string CurrentTargetType() => IsSubterfuge || IsLands ? "settlement" : "person";
    }

    public sealed class ReignSpymasterOptionVM : ViewModel
    {
        private readonly Action<ReignSpymasterOptionVM> _select;
        private bool _isSelected;
        public ReignSpymasterOptionVM(string id, string label, string detail, Action<ReignSpymasterOptionVM> select)
        { Id = id ?? string.Empty; Label = label ?? id ?? string.Empty; Detail = detail ?? string.Empty; _select = select; }
        [DataSourceProperty] public string Id { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }
        public void ExecuteSelect() => _select?.Invoke(this);
    }

    public sealed class ReignSpymasterMissionVM : ViewModel
    {
        private readonly Action<ReignSpymasterMissionVM> _select;
        private bool _isSelected;
        public ReignSpymasterMissionVM(ReignSpymasterMission mission, Action<ReignSpymasterMissionVM> select) { Mission = mission; _select = select; }
        public ReignSpymasterMission Mission { get; }
        [DataSourceProperty] public string Title => Mission.TargetName + " — " + Mission.MissionType.Replace('_', ' ');
        [DataSourceProperty] public string StateText => Mission.State.ToString().ToUpperInvariant();
        [DataSourceProperty] public string DateText => Mission.State == ReignSpymasterMissionState.Active
            ? "Due day " + Mission.DueDay.ToString("0.0") : "Resolved day " + Mission.ResolvedDay.ToString("0.0");
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }
        public void ExecuteSelect() => _select?.Invoke(this);
    }
}
