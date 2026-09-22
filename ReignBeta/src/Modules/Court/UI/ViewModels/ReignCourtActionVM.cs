using System;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtActionVM : ViewModel
    {
        private readonly Action _execute;

        public ReignCourtActionVM(string label, string description, bool enabled, string disabledReason, Action execute)
        {
            Label = label ?? string.Empty;
            Description = description ?? string.Empty;
            IsEnabled = enabled;
            DisabledReason = disabledReason ?? string.Empty;
            _execute = execute;
        }

        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public string Description { get; }
        [DataSourceProperty] public bool IsEnabled { get; }
        [DataSourceProperty] public string DisabledReason { get; }
        [DataSourceProperty] public string StateText => IsEnabled ? string.Empty : "LOCKED";

        public void ExecuteAction()
        {
            if (IsEnabled) _execute?.Invoke();
        }
    }
}
