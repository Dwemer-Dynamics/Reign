using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    // Presentation boundary: callers supply committed active agreements only.
    public sealed class ReignClanAccordsScreenData
    {
        public string ClanName { get; set; } = string.Empty;
        public int Tier { get; set; }
        public List<ReignClanAccordDisplayData> Records { get; set; } = new List<ReignClanAccordDisplayData>();
    }

    public sealed class ReignClanAccordDisplayData
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string PartnerClanId { get; set; } = string.Empty;
        public string PartnerClanName { get; set; } = string.Empty;
        public string KingdomName { get; set; } = string.Empty;
        public string BannerCode { get; set; } = string.Empty;
        public string PlayerArrangerName { get; set; } = string.Empty;
        public string NpcArrangerName { get; set; } = string.Empty;
        public string StartedText { get; set; } = string.Empty;
        public string BenefitText { get; set; } = string.Empty;
        public string ApplicabilityText { get; set; } = string.Empty;
    }

    public sealed class ReignClanAccordsVM : ViewModel
    {
        public static readonly string[] Types = { "Trade", "MutualWatch", "Agricultural", "Artisan", "Garrison" };
        private readonly Func<ReignClanAccordsScreenData> _snapshot;
        private readonly Func<string, string> _cancel;
        private readonly Action _close;
        private ReignClanAccordsScreenData _data;
        private string _filter = string.Empty;
        private ReignClanAccordDisplayData _pending;
        private bool _submitting;
        private string _fingerprint = string.Empty;

        public ReignClanAccordsVM(Func<ReignClanAccordsScreenData> snapshot, Func<string, string> cancel, Action close)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
            _close = close;
            Refresh();
        }

        [DataSourceProperty] public MBBindingList<ReignClanAccordCardVM> Agreements { get; } = new MBBindingList<ReignClanAccordCardVM>();
        [DataSourceProperty] public MBBindingList<ReignClanAccordFilterVM> Filters { get; } = new MBBindingList<ReignClanAccordFilterVM>();
        [DataSourceProperty] public string ClanText { get; private set; } = string.Empty;
        [DataSourceProperty] public string TradeTotal { get; private set; } = string.Empty;
        [DataSourceProperty] public string SecurityTotal { get; private set; } = string.Empty;
        [DataSourceProperty] public string HearthTotal { get; private set; } = string.Empty;
        [DataSourceProperty] public string ProsperityTotal { get; private set; } = string.Empty;
        [DataSourceProperty] public string GarrisonTotal { get; private set; } = string.Empty;
        [DataSourceProperty] public string CountText { get; private set; } = string.Empty;
        [DataSourceProperty] public string StatusText { get; private set; } = string.Empty;
        [DataSourceProperty] public string FooterText => string.IsNullOrWhiteSpace(StatusText) ? "War ends accords with opposing clans." : StatusText;
        [DataSourceProperty] public bool IsEmpty => Agreements.Count == 0;
        [DataSourceProperty] public int AgreementCount => Agreements.Count;
        [DataSourceProperty] public bool IsConfirmationOpen => _pending != null;
        [DataSourceProperty] public bool IsListEnabled => _pending == null && !_submitting;
        [DataSourceProperty] public bool CanConfirm => _pending != null && !_submitting;
        [DataSourceProperty] public string ConfirmationTitle => _pending == null ? string.Empty : "End " + TypeName(_pending.Type) + " with " + _pending.PartnerClanName + "?";
        [DataSourceProperty] public string ConfirmationText => _pending == null ? string.Empty : "Both clans lose: " + Benefit(_pending) + "\nRelation with each living adult member of " + _pending.PartnerClanName + " decreases by 10.";

        public void RefreshIfChanged()
        {
            if (_submitting) return;
            var next = _snapshot() ?? new ReignClanAccordsScreenData();
            if (!string.Equals(Fingerprint(next), _fingerprint, StringComparison.Ordinal)) Refresh(next);
        }

        private static string Fingerprint(ReignClanAccordsScreenData data)
        {
            return data.ClanName + "|" + data.Tier + "|" + string.Join("\n", (data.Records ?? new List<ReignClanAccordDisplayData>())
                .Where(r => r != null).Select(r => string.Join("|", r.Id, r.Type, r.PartnerClanId, r.PartnerClanName, r.KingdomName, r.BannerCode, r.PlayerArrangerName, r.NpcArrangerName, r.StartedText, r.BenefitText, r.ApplicabilityText)));
        }

        public void Refresh() => Refresh(_snapshot() ?? new ReignClanAccordsScreenData());

        private void Refresh(ReignClanAccordsScreenData data)
        {
            _data = data;
            _fingerprint = Fingerprint(data);
            _data.Records = (_data.Records ?? new List<ReignClanAccordDisplayData>()).Where(r => r != null).ToList();
            if (_pending != null && !_data.Records.Any(r => r.Id == _pending.Id))
            {
                _pending = null;
                StatusText = "This agreement is no longer active.";
            }
            ClanText = _data.ClanName + " · Tier " + _data.Tier.ToString(CultureInfo.InvariantCulture);
            TradeTotal = "+" + (Count("Trade") * 50).ToString(CultureInfo.InvariantCulture) + " / day";
            SecurityTotal = Daily(Count("MutualWatch") * .1);
            HearthTotal = Daily(Count("Agricultural") * .2);
            ProsperityTotal = Daily(Count("Artisan") * .1);
            GarrisonTotal = "-" + (Count("Garrison") * 2).ToString(CultureInfo.InvariantCulture) + "%";
            CountText = _data.Records.Count + " agreements · " + _data.Records.Select(r => r.PartnerClanId).Distinct(StringComparer.Ordinal).Count() + " partner clans";
            foreach (var filter in Filters) filter.OnFinalize();
            Filters.Clear();
            Filters.Add(new ReignClanAccordFilterVM("", "All Accords", _data.Records.Count.ToString(), _filter.Length == 0, SelectFilter));
            foreach (string type in Types)
                Filters.Add(new ReignClanAccordFilterVM(type, FilterName(type), Count(type) + " / " + _data.Tier, _filter == type, SelectFilter));
            foreach (var card in Agreements) card.OnFinalize();
            Agreements.Clear();
            foreach (var row in _data.Records.Where(r => _filter.Length == 0 || r.Type == _filter)
                .OrderBy(r => r.PartnerClanName, StringComparer.OrdinalIgnoreCase).ThenBy(r => Array.IndexOf(Types, r.Type)).ThenBy(r => r.Id, StringComparer.Ordinal))
                Agreements.Add(new ReignClanAccordCardVM(row, RequestCancellation));
            Notify();
        }

        public void SelectFilter(string type)
        {
            if (!IsListEnabled || (type.Length != 0 && !Types.Contains(type))) return;
            _filter = type;
            Refresh();
        }

        private void RequestCancellation(ReignClanAccordDisplayData row)
        {
            if (!IsListEnabled) return;
            _pending = row;
            StatusText = string.Empty;
            Notify();
        }

        public void ExecuteKeepAgreement()
        {
            if (_submitting) return;
            _pending = null;
            Notify();
        }

        public void ExecuteConfirmCancellation()
        {
            if (!CanConfirm) return;
            _submitting = true;
            Notify();
            try
            {
                string error = _cancel(_pending.Id);
                if (!string.IsNullOrEmpty(error)) { StatusText = error; return; }
                _pending = null;
                StatusText = "Agreement ended. Benefits and capacity have been updated.";
                Refresh();
            }
            catch (Exception ex) { StatusText = "The agreement could not be ended: " + ex.Message; }
            finally { _submitting = false; Notify(); }
        }

        public void ExecuteClose() { if (_pending != null) ExecuteKeepAgreement(); else _close?.Invoke(); }
        private int Count(string type) => _data.Records.Count(r => r.Type == type);
        private static string Daily(double amount) => "+" + amount.ToString("0.#", CultureInfo.InvariantCulture) + " / day";
        public static string FilterName(string type) => type == "MutualWatch" ? "Mutual Watch" : type;
        public static string TypeName(string type)
        {
            switch (type)
            {
                case "Trade": return "Trade Cooperation";
                case "MutualWatch": return "Mutual Watch";
                case "Agricultural": return "Agricultural Exchange";
                case "Artisan": return "Artisan Exchange";
                case "Garrison": return "Garrison Cooperation";
                default: return type ?? string.Empty;
            }
        }
        public static string Benefit(ReignClanAccordDisplayData row)
        {
            if (!string.IsNullOrWhiteSpace(row.BenefitText)) return row.BenefitText;
            switch (row.Type)
            {
                case "Trade": return "+50 denars/day to each clan";
                case "MutualWatch": return "+0.1 security/day in both clans’ towns and castles";
                case "Agricultural": return "+0.2 hearth growth/day in both clans’ villages";
                case "Artisan": return "+0.1 prosperity/day in both clans’ towns";
                case "Garrison": return "2% lower garrison wages for each clan";
                default: return string.Empty;
            }
        }
        private void Notify()
        {
            foreach (string name in new[] { nameof(ClanText), nameof(TradeTotal), nameof(SecurityTotal), nameof(HearthTotal), nameof(ProsperityTotal), nameof(GarrisonTotal), nameof(CountText), nameof(StatusText), nameof(FooterText), nameof(IsEmpty), nameof(AgreementCount), nameof(IsConfirmationOpen), nameof(IsListEnabled), nameof(CanConfirm), nameof(ConfirmationTitle), nameof(ConfirmationText) }) OnPropertyChanged(name);
        }
        public override void OnFinalize()
        {
            foreach (var card in Agreements) card.OnFinalize();
            foreach (var filter in Filters) filter.OnFinalize();
            base.OnFinalize();
        }
    }

    public sealed class ReignClanAccordFilterVM : ViewModel
    {
        private readonly string _type;
        private readonly Action<string> _select;
        public ReignClanAccordFilterVM(string type, string name, string capacity, bool selected, Action<string> select)
        { _type = type; Name = name; Capacity = capacity; IsSelected = selected; _select = select; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Capacity { get; }
        [DataSourceProperty] public bool IsSelected { get; }
        [DataSourceProperty] public string Sprite => IsSelected ? "reign_clan_accords_filter" : "reign_clan_accords_filter_idle";
        public void ExecuteSelect() => _select(_type);
    }

    public sealed class ReignClanAccordCardVM : ViewModel
    {
        private readonly ReignClanAccordDisplayData _row;
        private readonly Action<ReignClanAccordDisplayData> _request;
        public ReignClanAccordCardVM(ReignClanAccordDisplayData row, Action<ReignClanAccordDisplayData> request)
        {
            _row = row; _request = request;
            try
            {
                if (!string.IsNullOrWhiteSpace(row.BannerCode))
                {
                    var banner = new BannerImageIdentifierVM(new Banner(row.BannerCode), true);
                    BannerId = banner.Id; BannerArgs = banner.AdditionalArgs; BannerProvider = banner.TextureProviderName;
                }
            }
            catch { /* Neutral card-owned banner opening remains when a banner cannot resolve. */ }
        }
        [DataSourceProperty] public string PartnerClanName => _row.PartnerClanName;
        [DataSourceProperty] public string KingdomName => string.IsNullOrWhiteSpace(_row.KingdomName) ? "Independent" : _row.KingdomName;
        [DataSourceProperty] public string Title => ReignClanAccordsVM.TypeName(_row.Type).ToUpperInvariant();
        [DataSourceProperty] public string BenefitText => ReignClanAccordsVM.Benefit(_row) + (string.IsNullOrWhiteSpace(_row.ApplicabilityText) ? string.Empty : "\n" + _row.ApplicabilityText);
        [DataSourceProperty] public string Arrangers => "Arranged by: " + _row.PlayerArrangerName + " & " + _row.NpcArrangerName;
        [DataSourceProperty] public string StartedText => _row.StartedText;
        [DataSourceProperty] public string ApplicabilityText => _row.ApplicabilityText;
        [DataSourceProperty] public string BannerId { get; } = string.Empty;
        [DataSourceProperty] public string BannerArgs { get; } = string.Empty;
        [DataSourceProperty] public string BannerProvider { get; } = string.Empty;
        public void ExecuteCancel() => _request(_row);
    }
}
