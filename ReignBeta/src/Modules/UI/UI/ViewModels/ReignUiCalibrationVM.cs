using ReignBeta.UI.Calibration;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignUiCalibrationVM : ViewModel
    {
        private readonly ReignUiCalibrationSession _session;
        private bool _isExpanded;
        private bool _isLauncherVisible = true;
        private bool _selectionVisible;
        private float _selectionX;
        private float _selectionY;
        private float _selectionWidth = 1f;
        private float _selectionHeight = 1f;
        private string _movieText = "Reign UI";
        private string _selectedText = "Click an element to select it";
        private string _geometryText = "No live widget selected";
        private string _modeText = "MOVE MODE";
        private string _statusText = "Live edits are temporary until a snapshot is imported into the XML previewer.";

        internal ReignUiCalibrationVM(ReignUiCalibrationSession session)
        {
            _session = session;
        }

        [DataSourceProperty]
        public bool IsExpanded { get => _isExpanded; private set { if (_isExpanded == value) return; _isExpanded = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public bool IsLauncherVisible { get => _isLauncherVisible; private set { if (_isLauncherVisible == value) return; _isLauncherVisible = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public bool SelectionVisible { get => _selectionVisible; private set { if (_selectionVisible == value) return; _selectionVisible = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public float SelectionX { get => _selectionX; private set { if (_selectionX == value) return; _selectionX = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public float SelectionY { get => _selectionY; private set { if (_selectionY == value) return; _selectionY = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public float SelectionWidth { get => _selectionWidth; private set { if (_selectionWidth == value) return; _selectionWidth = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public float SelectionHeight { get => _selectionHeight; private set { if (_selectionHeight == value) return; _selectionHeight = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string MovieText { get => _movieText; private set { if (_movieText == value) return; _movieText = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string SelectedText { get => _selectedText; private set { if (_selectedText == value) return; _selectedText = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string GeometryText { get => _geometryText; private set { if (_geometryText == value) return; _geometryText = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string ModeText { get => _modeText; private set { if (_modeText == value) return; _modeText = value; OnPropertyChangedWithValue(value); } }

        [DataSourceProperty]
        public string StatusText { get => _statusText; private set { if (_statusText == value) return; _statusText = value; OnPropertyChangedWithValue(value); } }

        public void ExecuteToggle() { _session.ToggleExpanded(); }
        public void ExecuteClose() { _session.SetExpanded(false); }
        public void ExecuteToggleMode() { _session.ToggleMode(); }
        public void ExecuteUndo() { _session.Undo(); }
        public void ExecuteParent() { _session.SelectParent(); }
        public void ExecuteCycle() { _session.CycleSelection(); }
        public void ExecuteSaveSnapshot() { _session.SaveSnapshot(); }
        public void ExecuteLeft() { _session.Nudge(-1f, 0f); }
        public void ExecuteRight() { _session.Nudge(1f, 0f); }
        public void ExecuteUp() { _session.Nudge(0f, -1f); }
        public void ExecuteDown() { _session.Nudge(0f, 1f); }
        public void ExecuteNarrower() { _session.Resize(-1f, 0f); }
        public void ExecuteWider() { _session.Resize(1f, 0f); }
        public void ExecuteShorter() { _session.Resize(0f, -1f); }
        public void ExecuteTaller() { _session.Resize(0f, 1f); }

        internal void UpdateFromSession(ReignUiCalibrationSession session)
        {
            IsExpanded = session.IsExpanded;
            IsLauncherVisible = !session.IsExpanded;
            SelectionVisible = session.IsExpanded && session.SelectedWidget != null;
            MovieText = session.MovieName;
            SelectedText = session.SelectionLabel;
            GeometryText = session.GeometryLabel;
            ModeText = session.IsResizeMode ? "RESIZE MODE" : "MOVE MODE";
            StatusText = session.StatusText;
            SelectionX = session.SelectionX;
            SelectionY = session.SelectionY;
            SelectionWidth = session.SelectionWidth;
            SelectionHeight = session.SelectionHeight;
        }
    }
}
