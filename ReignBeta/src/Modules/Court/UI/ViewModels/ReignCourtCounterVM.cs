using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtCounterVM : ViewModel
    {
        public ReignCourtCounterVM(string symbol, string label, string value, string trend, string iconSprite)
        {
            Symbol = symbol;
            Label = label;
            Value = value;
            Trend = trend;
            IconSprite = iconSprite;
        }

        [DataSourceProperty] public string Symbol { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Value { get; }
        [DataSourceProperty] public string Trend { get; }
        [DataSourceProperty] public string IconSprite { get; }
        [DataSourceProperty] public bool IsPresence => string.Equals(Label, "COURT PRESENCE", System.StringComparison.OrdinalIgnoreCase);
        [DataSourceProperty] public bool IsStrength => string.Equals(Label, "STRENGTH", System.StringComparison.OrdinalIgnoreCase);
        [DataSourceProperty] public bool IsNotStrength => !IsStrength;
    }
}
