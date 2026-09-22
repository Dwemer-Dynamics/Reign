using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ReignBeta.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtEconomicReportVM : ViewModel
    {
        public void ExecuteClanAccords()
        {
            var accords = ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.Instance;
            if (accords != null && ReignBeta.ClanAccords.ReignClanAccordsCampaignBehavior.IsPlayerRuler)
                ReignClanAccordsScreenManager.Open(_court, ReignClanAccordsPresentation.Capture, accords.CancelFromUi);
        }

        private readonly ReignCourtCampaignBehavior _court;
        private readonly Action _close;
        private readonly List<ReignSettlementSupplySnapshot> _settlements = new List<ReignSettlementSupplySnapshot>();
        private readonly List<ReignSettlementSupplySnapshot> _sources = new List<ReignSettlementSupplySnapshot>();
        private readonly List<ReignSettlementSupplySnapshot> _targets = new List<ReignSettlementSupplySnapshot>();
        private int _sourceIndex;
        private int _targetIndex = 1;
        private int _amount = 10;
        private float _clock;
        private float _travelDays;
        private string _sourceName = string.Empty;
        private string _targetName = string.Empty;
        private string _goodsName = string.Empty;
        private string _availableText = "0";
        private string _amountText = "10";
        private string _arrivalText = "ARRIVES IN: 0.0 DAYS";
        private string _notes = string.Empty;
        private string _reportDateText = string.Empty;
        private string _statusText = string.Empty;
        private string _treasuryText = "0";
        private string _dailyIncomeText = "0";
        private string _dailyExpensesText = "0";
        private string _netDailyChangeText = "0";
        private string _tariffsTradeText = "0";
        private string _villageManorText = "0";
        private string _tradeBalanceText = "0";
        private string _outstandingDebtsText = "0";
        private string _grainTotalText = "0";
        private string _meatTotalText = "0";
        private string _timberTotalText = "0";
        private string _ironTotalText = "0";
        private string _totalMilitiaText = "0";
        private string _totalGarrisonText = "0";
        private string _garrisonUpkeepText = "0";
        private string _warInventoryText = "Low";
        private string _overallStabilityText = "0";
        private string _averageLoyaltyText = "0";
        private string _averageSecurityText = "0";
        private string _corruptionRiskText = "Low";
        private bool _netDailyPositive;
        private bool _netDailyNegative;
        private bool _tradeBalancePositive;
        private bool _tradeBalanceNegative;
        private bool _canSubmit;
        private bool _isSourceDropdownOpen;
        private bool _isTargetDropdownOpen;
        private ReignRoyalCouncilSeatVM _economicAdvisor;

        public ReignCourtEconomicReportVM(ReignCourtCampaignBehavior court, Action close)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _close = close;
            Settlements = new MBBindingList<ReignEconomicSettlementVM>();
            SurplusRows = new MBBindingList<ReignEconomicSurplusVM>();
            SourceOptions = new MBBindingList<ReignEconomicSettlementOptionVM>();
            TargetOptions = new MBBindingList<ReignEconomicSettlementOptionVM>();
            _court.StateChanged += OnStateChanged;
            RefreshAll();
        }

        public MBBindingList<ReignEconomicSettlementVM> Settlements { get; }
        public MBBindingList<ReignEconomicSurplusVM> SurplusRows { get; }
        public MBBindingList<ReignEconomicSettlementOptionVM> SourceOptions { get; }
        public MBBindingList<ReignEconomicSettlementOptionVM> TargetOptions { get; }
        [DataSourceProperty] public bool HasSettlementOverflow => Settlements.Count > 3;

        [DataSourceProperty] public string SourceName { get => _sourceName; set { if (_sourceName != value) { _sourceName = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TargetName { get => _targetName; set { if (_targetName != value) { _targetName = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string GoodsName { get => _goodsName; set { if (_goodsName != value) { _goodsName = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string AvailableText { get => _availableText; set { if (_availableText != value) { _availableText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string AmountText { get => _amountText; set { if (_amountText != value) { _amountText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string ArrivalText { get => _arrivalText; set { if (_arrivalText != value) { _arrivalText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string Notes { get => _notes; set { if (_notes != value) { _notes = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string ReportDateText { get => _reportDateText; set { if (_reportDateText != value) { _reportDateText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TreasuryText { get => _treasuryText; set { if (_treasuryText != value) { _treasuryText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string DailyIncomeText { get => _dailyIncomeText; set { if (_dailyIncomeText != value) { _dailyIncomeText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string DailyExpensesText { get => _dailyExpensesText; set { if (_dailyExpensesText != value) { _dailyExpensesText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string NetDailyChangeText { get => _netDailyChangeText; set { if (_netDailyChangeText != value) { _netDailyChangeText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TariffsTradeText { get => _tariffsTradeText; set { if (_tariffsTradeText != value) { _tariffsTradeText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string VillageManorText { get => _villageManorText; set { if (_villageManorText != value) { _villageManorText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TradeBalanceText { get => _tradeBalanceText; set { if (_tradeBalanceText != value) { _tradeBalanceText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string OutstandingDebtsText { get => _outstandingDebtsText; set { if (_outstandingDebtsText != value) { _outstandingDebtsText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string GrainTotalText { get => _grainTotalText; set { if (_grainTotalText != value) { _grainTotalText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string MeatTotalText { get => _meatTotalText; set { if (_meatTotalText != value) { _meatTotalText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TimberTotalText { get => _timberTotalText; set { if (_timberTotalText != value) { _timberTotalText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string IronTotalText { get => _ironTotalText; set { if (_ironTotalText != value) { _ironTotalText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TotalMilitiaText { get => _totalMilitiaText; set { if (_totalMilitiaText != value) { _totalMilitiaText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string TotalGarrisonText { get => _totalGarrisonText; set { if (_totalGarrisonText != value) { _totalGarrisonText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string GarrisonUpkeepText { get => _garrisonUpkeepText; set { if (_garrisonUpkeepText != value) { _garrisonUpkeepText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string WarInventoryText { get => _warInventoryText; set { if (_warInventoryText != value) { _warInventoryText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string OverallStabilityText { get => _overallStabilityText; set { if (_overallStabilityText != value) { _overallStabilityText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string AverageLoyaltyText { get => _averageLoyaltyText; set { if (_averageLoyaltyText != value) { _averageLoyaltyText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string AverageSecurityText { get => _averageSecurityText; set { if (_averageSecurityText != value) { _averageSecurityText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public string CorruptionRiskText { get => _corruptionRiskText; set { if (_corruptionRiskText != value) { _corruptionRiskText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool NetDailyPositive { get => _netDailyPositive; set { if (_netDailyPositive != value) { _netDailyPositive = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool NetDailyNegative { get => _netDailyNegative; set { if (_netDailyNegative != value) { _netDailyNegative = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool TradeBalancePositive { get => _tradeBalancePositive; set { if (_tradeBalancePositive != value) { _tradeBalancePositive = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool TradeBalanceNegative { get => _tradeBalanceNegative; set { if (_tradeBalanceNegative != value) { _tradeBalanceNegative = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool CanSubmit { get => _canSubmit; set { if (_canSubmit != value) { _canSubmit = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsSourceDropdownOpen { get => _isSourceDropdownOpen; set { if (_isSourceDropdownOpen != value) { _isSourceDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsTargetDropdownOpen { get => _isTargetDropdownOpen; set { if (_isTargetDropdownOpen != value) { _isTargetDropdownOpen = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public ReignRoyalCouncilSeatVM EconomicAdvisor { get => _economicAdvisor; private set { if (_economicAdvisor != value) { _economicAdvisor = value; OnPropertyChangedWithValue(value); } } }

        public void OnFrameTick(float dt)
        {
            _clock += dt;
            if (_clock < 1f) return;
            _clock = 0f;
            RefreshShipmentStatus();
        }

        public void ExecuteClose() => _close?.Invoke();
        public void ExecuteToggleSourceDropdown()
        {
            IsSourceDropdownOpen = !IsSourceDropdownOpen;
            if (IsSourceDropdownOpen) IsTargetDropdownOpen = false;
        }
        public void ExecuteToggleTargetDropdown()
        {
            IsTargetDropdownOpen = !IsTargetDropdownOpen;
            if (IsTargetDropdownOpen) IsSourceDropdownOpen = false;
        }
        public void ExecutePreviousSource() { CycleSource(-1); }
        public void ExecuteNextSource() { CycleSource(1); }
        public void ExecutePreviousTarget() { CycleTarget(-1); }
        public void ExecuteNextTarget() { CycleTarget(1); }
        public void ExecutePreviousGoods() { }
        public void ExecuteNextGoods() { }
        public void ExecuteDecreaseAmount() { _amount = Math.Max(1, _amount - 10); RefreshTransfer(); }
        public void ExecuteIncreaseAmount() { _amount = Math.Min(MaximumFood(), _amount + 10); RefreshTransfer(); }

        public void ExecuteSubmit()
        {
            if (!CanSubmit) { StatusText = "Choose different settlements and an amount within the origin's available food supply."; return; }
            ReignSettlementSupplySnapshot source = CurrentSource();
            ReignSettlementSupplySnapshot target = CurrentTarget();
            try
            {
                ReignEconomicShipment shipment = _court.SubmitEconomicFoodTransfer(
                    source.SettlementStringId, target.SettlementStringId, _amount, Notes);
                StatusText = shipment.Status + " - " + shipment.RequestedFood.ToString("0.#")
                    + " food dispatched; expected in "
                    + Math.Max(0.1f, shipment.ArrivalDay - CampaignTime.Now.ToDays).ToString("0.0") + " days.";
                Notes = string.Empty;
            }
            catch (Exception ex) { StatusText = ex.Message; }
        }

        public bool SelectSourceById(string id) => SelectSettlement(_sources, id, true);
        public bool SelectTargetById(string id) => SelectSettlement(_targets, id, false);
        public void SetAmountForAutomation(int amount) { _amount = Math.Max(1, Math.Min(MaximumFood(), amount)); RefreshTransfer(); }
        public void SetNotesForAutomation(string notes) => Notes = notes ?? string.Empty;
        public string SelectedSourceId => CurrentSource()?.SettlementStringId ?? string.Empty;
        public string SelectedTargetId => CurrentTarget()?.SettlementStringId ?? string.Empty;
        public float TravelDays => _travelDays;
        public int SourceOptionCount => _sources.Count;
        public int TargetOptionCount => _targets.Count;
        public ReignEconomicShipment LatestShipment => _court.EconomicReportState?.Shipments?.LastOrDefault();

        private void OnStateChanged() => RefreshAll();

        private void RefreshAll()
        {
            string sourceId = CurrentSource()?.SettlementStringId;
            string targetId = CurrentTarget()?.SettlementStringId;
            ReignEconomicReportState state = _court.EnsureEconomicReportState();
            _settlements.Clear();
            _settlements.AddRange(state.Settlements ?? new List<ReignSettlementSupplySnapshot>());
            List<ReignSettlementSupplySnapshot> all = ReignCourtSupplyService.GetAllFortificationSnapshots();
            Kingdom playerKingdom = Clan.PlayerClan?.Kingdom;
            _sources.Clear();
            _sources.AddRange(all.Where(x => Settlement.All.FirstOrDefault(s => s.StringId == x.SettlementStringId)?.OwnerClan?.Kingdom == playerKingdom));
            _targets.Clear();
            _targets.AddRange(all);
            _sourceIndex = FindIndex(_sources, sourceId, 0);
            _targetIndex = FindIndex(_targets, targetId, 0);
            EnsureDifferentEndpoints();
            Settlements.Clear();
            foreach (ReignSettlementSupplySnapshot x in _settlements) Settlements.Add(new ReignEconomicSettlementVM(x));
            OnPropertyChanged(nameof(HasSettlementOverflow));
            SourceOptions.Clear();
            foreach (ReignSettlementSupplySnapshot x in _sources)
                SourceOptions.Add(new ReignEconomicSettlementOptionVM(x, id => SelectSourceById(id)));
            TargetOptions.Clear();
            foreach (ReignSettlementSupplySnapshot x in _targets)
                TargetOptions.Add(new ReignEconomicSettlementOptionVM(x, id => SelectTargetById(id)));
            RefreshEconomicAdvisor();
            BuildKingdomBook();
            RefreshTransfer();
            RefreshShipmentStatus();
            ReportDateText = CampaignTime.Now.GetDayOfSeason + Ordinal(CampaignTime.Now.GetDayOfSeason)
                + " of " + CampaignTime.Now.GetSeasonOfYear + ", " + CampaignTime.Now.GetYear;
        }

        private void CycleSource(int delta)
        {
            if (_sources.Count == 0) return;
            _sourceIndex = Wrap(_sourceIndex + delta, _sources.Count);
            EnsureDifferentEndpoints();
            RefreshTransfer();
        }

        private void CycleTarget(int delta)
        {
            if (_targets.Count == 0) return;
            _targetIndex = Wrap(_targetIndex + delta, _targets.Count);
            EnsureDifferentEndpoints();
            RefreshTransfer();
        }

        private void EnsureDifferentEndpoints()
        {
            if (!SameEndpoint()) return;
            int alternate = _targets.FindIndex(x => !string.Equals(
                x.SettlementStringId, CurrentSource()?.SettlementStringId, StringComparison.OrdinalIgnoreCase));
            if (alternate >= 0) _targetIndex = alternate;
        }

        private bool SelectSettlement(List<ReignSettlementSupplySnapshot> list, string id, bool source)
        {
            int index = list.FindIndex(x => string.Equals(x.SettlementStringId, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return false;
            if (source) { _sourceIndex = index; IsSourceDropdownOpen = false; }
            else { _targetIndex = index; IsTargetDropdownOpen = false; }
            EnsureDifferentEndpoints();
            RefreshTransfer();
            return true;
        }

        private void RefreshTransfer()
        {
            if (_sources.Count == 0 || _targets.Count == 0)
            {
                SourceName = TargetName = GoodsName = "None";
                AvailableText = "0";
                ArrivalText = "ARRIVES IN: 0.0 DAYS";
                CanSubmit = false;
                return;
            }

            ReignSettlementSupplySnapshot source = CurrentSource();
            ReignSettlementSupplySnapshot target = CurrentTarget();
            SourceName = source.Name;
            TargetName = target.Name;
            GoodsName = "Food Supply";
            int available = MaximumFood();
            AvailableText = available.ToString("N0");
            if (available > 0) _amount = Math.Max(1, Math.Min(_amount, available));
            RefreshAmount();
            Settlement sourceSettlement = Settlement.All.FirstOrDefault(x => x.StringId == source.SettlementStringId);
            Settlement targetSettlement = Settlement.All.FirstOrDefault(x => x.StringId == target.SettlementStringId);
            _travelDays = sourceSettlement == null || targetSettlement == null ? 0f
                : Math.Max(0.175f, (float)Math.Sqrt(sourceSettlement.GetPosition2D.DistanceSquared(targetSettlement.GetPosition2D)) / 50f);
            ArrivalText = "ARRIVES IN: " + _travelDays.ToString("0.0") + " DAYS";
            CanSubmit = !SameEndpoint() && available >= _amount && _amount > 0;
        }

        private void RefreshAmount() => AmountText = _amount.ToString(CultureInfo.InvariantCulture);

        private int MaximumFood() => Math.Max(0, (int)Math.Floor(CurrentSource()?.FoodStocks ?? 0f));
        private ReignSettlementSupplySnapshot CurrentSource() => _sources.Count == 0 ? null : _sources[Wrap(_sourceIndex, _sources.Count)];
        private ReignSettlementSupplySnapshot CurrentTarget() => _targets.Count == 0 ? null : _targets[Wrap(_targetIndex, _targets.Count)];
        private bool SameEndpoint() => string.Equals(CurrentSource()?.SettlementStringId, CurrentTarget()?.SettlementStringId, StringComparison.OrdinalIgnoreCase);

        private void BuildKingdomBook()
        {
            int treasury = Hero.MainHero?.Gold ?? 0;
            int income = _settlements.Sum(x => x.DailyTaxIncome);
            int expense = _settlements.Sum(x => x.GarrisonCount * 6);
            int balance = income - expense;
            int militia = (int)_settlements.Sum(x => x.Militia);
            int garrison = _settlements.Sum(x => x.GarrisonCount);
            int food = _settlements.Sum(x => x.StrategicSupplyUnits);
            float loyalty = _settlements.Count == 0 ? 0 : _settlements.Average(x => x.Loyalty);
            float security = _settlements.Count == 0 ? 0 : _settlements.Average(x => x.Security);

            TreasuryText = Format(treasury);
            DailyIncomeText = FormatSigned(income);
            DailyExpensesText = FormatSigned(-expense);
            NetDailyChangeText = FormatSigned(balance);
            NetDailyPositive = balance >= 0;
            NetDailyNegative = balance < 0;
            TariffsTradeText = Format(income / 3);
            VillageManorText = Format(income * 2 / 3);
            TradeBalanceText = FormatSigned(balance);
            TradeBalancePositive = balance >= 0;
            TradeBalanceNegative = balance < 0;
            OutstandingDebtsText = "0";
            GrainTotalText = Format(SumItem("grain"));
            MeatTotalText = Format(SumItem("meat"));
            TimberTotalText = Format(SumItem("hardwood"));
            IronTotalText = Format(SumItem("iron"));
            TotalMilitiaText = Format(militia);
            TotalGarrisonText = Format(garrison);
            GarrisonUpkeepText = Format(expense);
            WarInventoryText = food > garrison * 5 ? "Adequate" : "Low";
            OverallStabilityText = ((loyalty + security) / 2f).ToString("0");
            AverageLoyaltyText = loyalty.ToString("0");
            AverageSecurityText = security.ToString("0");
            CorruptionRiskText = loyalty < 45 ? "High" : loyalty < 65 ? "Moderate" : "Low";

            SurplusRows.Clear();
            foreach (ReignSettlementSupplySnapshot settlement in _settlements
                .Where(x => Settlement.Find(x.SettlementStringId)?.IsTown == true)
                .Take(8))
                SurplusRows.Add(ReignEconomicSurplusVM.From(settlement));
        }

        private void RefreshEconomicAdvisor()
        {
            CourtOfficeAssignment assignment = _court.Offices.FirstOrDefault(x => x.IsActive && x.Office == ReignCourtOffice.EconomicAdvisor);
            Hero advisor = string.IsNullOrWhiteSpace(assignment?.HeroStringId)
                ? null
                : Hero.AllAliveHeroes.FirstOrDefault(x => string.Equals(x.StringId, assignment.HeroStringId, StringComparison.OrdinalIgnoreCase));
            bool available = advisor != null && advisor.IsAlive && !advisor.IsPrisoner;
            EconomicAdvisor = new ReignRoyalCouncilSeatVM(
                "economic",
                "ECONOMIC ADVISOR",
                advisor,
                available,
                () => ReignResidentAdvisorAppointment.Begin(_court, ReignCourtOffice.EconomicAdvisor, RefreshAll));
        }

        private int SumItem(string token) => _settlements.Sum(s => (s.Items ?? new List<ReignSettlementSupplyItem>())
            .Where(x => (x.ItemStringId ?? string.Empty).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0
                || (x.Name ?? string.Empty).IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0).Sum(x => x.Count));

        private void RefreshShipmentStatus()
        {
            ReignEconomicShipment latest = _court.EconomicReportState?.Shipments?.LastOrDefault();
            if (latest == null) return;
            if (!latest.IsFoodStockTransfer)
            {
                StatusText = latest.Status + ": " + latest.Amount + " " + latest.ItemName
                    + (string.IsNullOrWhiteSpace(latest.Error) ? string.Empty : " - " + latest.Error);
                return;
            }

            if (string.Equals(latest.Status, "Delivered", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "DELIVERED: requested " + latest.RequestedFood.ToString("0.#")
                    + ", spoiled " + latest.RoadSpoilage.ToString("0.#")
                    + ", overflow " + latest.CapacityOverflow.ToString("0.#")
                    + ", received " + latest.DeliveredFood.ToString("0.#") + ".";
            }
            else if (string.Equals(latest.Status, "In transit", StringComparison.OrdinalIgnoreCase))
            {
                StatusText = "IN TRANSIT: " + latest.RequestedFood.ToString("0.#") + " food, "
                    + latest.SpoilagePercent + "% road spoilage, "
                    + Math.Max(0f, latest.ArrivalDay - CampaignTime.Now.ToDays).ToString("0.0") + " days remaining.";
            }
            else
            {
                StatusText = latest.Status.ToUpperInvariant() + ": "
                    + (string.IsNullOrWhiteSpace(latest.Error) ? "shipment could not be completed." : latest.Error);
            }
        }

        public override void OnFinalize()
        {
            _court.StateChanged -= OnStateChanged;
            base.OnFinalize();
        }

        private static string Format(int value) => value.ToString("N0");
        private static string FormatSigned(int value) => value.ToString("+#,0;-#,0;0");
        private static int FindIndex(List<ReignSettlementSupplySnapshot> list, string id, int fallback)
        {
            if (list == null || list.Count == 0) return 0;
            int index = string.IsNullOrWhiteSpace(id) ? -1 : list.FindIndex(x =>
                string.Equals(x.SettlementStringId, id, StringComparison.OrdinalIgnoreCase));
            return index >= 0 ? index : Math.Max(0, Math.Min(fallback, list.Count - 1));
        }
        private static int Wrap(int value, int count) { if (count <= 0) return 0; value %= count; return value < 0 ? value + count : value; }
        private static string Ordinal(int day) { int m = day % 100; if (m >= 11 && m <= 13) return "th"; return day % 10 == 1 ? "st" : day % 10 == 2 ? "nd" : day % 10 == 3 ? "rd" : "th"; }
    }

    public sealed class ReignEconomicSettlementOptionVM : ViewModel
    {
        private readonly Action<string> _select;

        public ReignEconomicSettlementOptionVM(ReignSettlementSupplySnapshot snapshot, Action<string> select)
        {
            StringId = snapshot?.SettlementStringId ?? string.Empty;
            Name = snapshot?.Name ?? "Settlement";
            Settlement settlement = Settlement.All.FirstOrDefault(x => x.StringId == StringId);
            string type = settlement?.IsTown == true ? "Town" : "Castle";
            string realm = settlement?.OwnerClan?.Kingdom?.Name?.ToString() ?? "Independent";
            DetailText = type + " - " + realm;
            _select = select;
        }

        [DataSourceProperty] public string StringId { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string DetailText { get; }
        public void ExecuteSelect() => _select?.Invoke(StringId);
    }

    public sealed class ReignEconomicSettlementVM : ViewModel
    {
        public ReignEconomicSettlementVM(ReignSettlementSupplySnapshot snapshot)
        {
            Name = snapshot.Name ?? "Settlement";
            Hero governor = Hero.AllAliveHeroes.FirstOrDefault(h => h.StringId == snapshot.GovernorHeroStringId);
            GovernorName = governor?.Name?.ToString() ?? "Unappointed";
            ProsperityText = snapshot.Prosperity.ToString("0");
            FoodText = snapshot.FoodStocks.ToString("0");
            SecurityText = snapshot.Security.ToString("0");
            LoyaltyText = snapshot.Loyalty.ToString("0");
            MilitiaText = snapshot.Militia.ToString("0");
            GarrisonText = snapshot.GarrisonCount.ToString("N0");
            GarrisonFoodText = Math.Max(1, snapshot.GarrisonCount / 20).ToString("N0");
            GarrisonWageText = (snapshot.GarrisonCount * 6).ToString("N0");
            GrainText = CountItem(snapshot, "Grain");
            FishText = CountItem(snapshot, "Fish");
            MeatText = CountItem(snapshot, "Meat");
            OlivesText = CountItem(snapshot, "Olives");
            BeerText = CountItem(snapshot, "Beer");
            ButterText = CountItem(snapshot, "Butter");
            GrapesText = CountItem(snapshot, "Grapes");
            DatesText = CountItem(snapshot, "Dates");
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string GovernorName { get; }
        [DataSourceProperty] public string ProsperityText { get; }
        [DataSourceProperty] public string FoodText { get; }
        [DataSourceProperty] public string SecurityText { get; }
        [DataSourceProperty] public string LoyaltyText { get; }
        [DataSourceProperty] public string MilitiaText { get; }
        [DataSourceProperty] public string GarrisonText { get; }
        [DataSourceProperty] public string GarrisonFoodText { get; }
        [DataSourceProperty] public string GarrisonWageText { get; }
        [DataSourceProperty] public string GrainText { get; }
        [DataSourceProperty] public string FishText { get; }
        [DataSourceProperty] public string MeatText { get; }
        [DataSourceProperty] public string OlivesText { get; }
        [DataSourceProperty] public string BeerText { get; }
        [DataSourceProperty] public string ButterText { get; }
        [DataSourceProperty] public string GrapesText { get; }
        [DataSourceProperty] public string DatesText { get; }

        private static string CountItem(ReignSettlementSupplySnapshot snapshot, string name)
        {
            int count = (snapshot.Items ?? new List<ReignSettlementSupplyItem>())
                .Where(item => (item.Name ?? string.Empty).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    || (item.ItemStringId ?? string.Empty).IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                .Sum(item => item.Count);
            return count.ToString("N0");
        }
    }

    public sealed class ReignEconomicSurplusVM : ViewModel
    {
        private ReignEconomicSurplusVM(string settlementName, string itemName, string amountText, bool isSurplus)
        {
            SettlementName = settlementName;
            ItemName = itemName;
            AmountText = amountText;
            IsSurplus = isSurplus;
            IsShortage = !isSurplus;
        }

        [DataSourceProperty] public string SettlementName { get; }
        [DataSourceProperty] public string ItemName { get; }
        [DataSourceProperty] public string AmountText { get; }
        [DataSourceProperty] public bool IsSurplus { get; }
        [DataSourceProperty] public bool IsShortage { get; }

        public static ReignEconomicSurplusVM From(ReignSettlementSupplySnapshot settlement)
        {
            List<ReignSettlementSupplyItem> items = settlement.Items ?? new List<ReignSettlementSupplyItem>();
            if (settlement.HasShortage)
            {
                ReignSettlementSupplyItem shortageItem = items
                    .Where(x => x.IsFood)
                    .OrderBy(x => x.Count)
                    .FirstOrDefault();
                string itemName = shortageItem?.Name ?? "Grain";
                int missing = Math.Max(1, 100 - (shortageItem?.Count ?? settlement.GrainCount));
                return new ReignEconomicSurplusVM(settlement.Name, itemName, "-" + missing.ToString("N0"), false);
            }

            ReignSettlementSupplyItem surplusItem = items.OrderByDescending(x => x.Count).FirstOrDefault();
            return surplusItem == null
                ? new ReignEconomicSurplusVM(settlement.Name, "Food", "-1", false)
                : new ReignEconomicSurplusVM(settlement.Name, surplusItem.Name, "+" + surplusItem.Count.ToString("N0"), true);
        }
    }
}
