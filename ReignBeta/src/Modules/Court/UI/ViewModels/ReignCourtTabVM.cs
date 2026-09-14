using System;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtTabVM : ViewModel
    {
        private readonly Action<ReignCourtTabVM> _select;
        private bool _isActive;

        public ReignCourtTabVM(string id, string label, bool locked, string lockReason, Action<ReignCourtTabVM> select)
        {
            Id = id;
            Label = label;
            IsLocked = locked;
            LockReason = lockReason ?? string.Empty;
            _select = select;
        }

        public string Id { get; }
        [DataSourceProperty] public string Label { get; }
        [DataSourceProperty] public bool IsLocked { get; }
        [DataSourceProperty] public string LockReason { get; }
        [DataSourceProperty] public string LockText => IsLocked ? "LOCKED" : string.Empty;

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set { if (_isActive != value) { _isActive = value; OnPropertyChangedWithValue(value); } }
        }

        public void ExecuteSelect() { _select?.Invoke(this); }
    }
}
