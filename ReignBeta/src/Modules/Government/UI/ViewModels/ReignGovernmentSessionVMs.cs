using System;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    /// <summary>A published declaration total, never a forecast from private preferences.</summary>
    public sealed class ReignGovernmentShareVM : ViewModel
    {
        public ReignGovernmentShareVM(string label, double weight, int index)
        {
            Label = label ?? "Undecided";
            WeightText = Math.Max(0, Math.Min(100, weight)).ToString("0.##") + "%";
            Width = (float)(Math.Max(0, Math.Min(100, weight)) * 8.36);
            Color = index == 0 ? "#7E6A4DFF" : index == 1 ? "#C35B32FF" : "#4A4945FF";
        }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string WeightText { get; }
        // Keep the exact segment width; never shrink a proposal sentence into it.
        // Named options retain their precise totals on their outcome cards.
        [DataSourceProperty] public string Text => Width < 56 ? "" : Width >= 180 && Label.Length <= 9 ? Label + " " + WeightText : WeightText;
        [DataSourceProperty] public float Width { get; }
        [DataSourceProperty] public string Color { get; }
    }

    public sealed class ReignGovernmentTextRecordVM : ViewModel
    {
        private readonly Action _open;
        public ReignGovernmentTextRecordVM(string title, string detail, string status, Action open = null)
        { Title = title ?? ""; Detail = detail ?? ""; Status = status ?? ""; _open = open; }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Detail { get; }
        [DataSourceProperty] public string Status { get; }
        [DataSourceProperty] public bool CanOpen => _open != null;
        public void ExecuteOpen() => _open?.Invoke();
    }
}
