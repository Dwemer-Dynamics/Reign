using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    internal static class ReignBanterPanel
    {
        private static ScreenBase _host;
        private static GauntletLayer _layer;
        private static BanterPanelVM _vm;
        private static ReignBanterWindowState _window;
        private static bool _preferencesDirty, _preferenceWarning;
        private static string PreferencesPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reign", "UI", "party-banter-window.json");
        private static bool _calibration;
        internal static bool IsOpen => _layer != null;
        internal static GauntletLayer InputLayer => _layer;
        internal static bool IsCalibrationActive => _calibration;
        internal static bool ShowCalibrationFixture()
        {
            Close();
            _calibration = true;
            Mount(new JObject { ["state"] = "finished", ["transcript"] = new JArray(
                new JObject { ["speaker"] = "Rhagaea", ["text"] = "Even a crown does little to keep the rain out." },
                new JObject { ["speaker"] = "Jon", ["text"] = "I could hold a cloak over it, Your Majesty." }) }, false, () => { });
            return IsOpen;
        }
        internal static void HideCalibrationFixture() { Close(); }
        internal static void Update(JObject episode, bool visible, bool busy, Action join)
        {
            if (_calibration) return;
            if (!visible || episode == null) { Close(); return; }
            Mount(episode, busy, join);
        }
        private static void Mount(JObject episode, bool busy, Action join)
        {
            ScreenBase host = ScreenManager.TopScreen;
            if (host == null) { Close(); return; }
            if (_host != host) { bool calibration = _calibration; Close(); _calibration = calibration; }
            if (_layer == null)
            {
                bool calibration = _calibration;
                if (!ReignRuntimeSpriteSheets.EnsureCategoryLoaded("ui_reignbeta_banter")) return;
                _host = host;
                _layer = new GauntletLayer("ReignBanterPanel", 9998, false);
                try
                {
                    // Root.Size includes Gauntlet's own root scaling; PageSize is the rendered viewport in pixels.
                    var viewport = _layer.UIContext.EventManager.PageSize;
                    float scale = _layer.UIContext.CustomInverseScale;
                    _window = new ReignBanterWindowState(viewport.X * scale, viewport.Y * scale);
                    _calibration = calibration;
                    if (!_calibration) LoadPreferences();
                    _vm = new BanterPanelVM(_window, ToggleCollapsed, join);
                    _layer.LoadMovie("ReignBanterPanel", _vm);
                    _layer.Input.IsKeysAllowed = false;
                    _layer.Input.IsMouseButtonAllowed = true;
                    _layer.Input.IsMouseWheelAllowed = true;
                    _host.AddLayer(_layer);
                    UpdatePointerInput();
                }
                catch (Exception ex) { ReignLog.Warn("Party banter panel: " + ex.Message); Close(); return; }
            }
            _vm.Update(episode, busy);
            UpdatePointerInput();
        }
        internal static void UpdatePointerInput()
        {
            if (_layer == null || _window == null || _host != ScreenManager.TopScreen) return;
            if (!_calibration && !ReignGovernmentScreenManager.CanPresentOnMap(_layer))
            { EndManipulation(); ReleaseInput(); return; }
            var viewport = _layer.UIContext.EventManager.PageSize;
            float scale = _layer.UIContext.CustomInverseScale;
            if (_window.SetViewport(viewport.X * scale, viewport.Y * scale)) { _vm?.RefreshGeometry(); _preferencesDirty = true; }
            var pointer = _layer.Input.GetMousePositionPixel();
            float x = pointer.X * scale, y = pointer.Y * scale;
            if (_window.IsManipulating)
            {
                if (!_layer.Input.IsKeyDown(InputKey.LeftMouseButton)) EndManipulation();
                else if (_window.MovePointer(x, y)) { _vm?.RefreshGeometry(); _preferencesDirty = true; }
            }
            // Capture the mouse only over this panel (or through a held drag).
            // Keyboard shortcuts, map clicks and map zoom remain available elsewhere.
            bool capture = _window.CapturesPointer(x, y);
            if (capture)
            {
                _layer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.Mouse);
                if (!_layer.IsFocusLayer) { _layer.IsFocusLayer = true; ScreenManager.TrySetFocus(_layer); }
            }
            else ReleaseInput();
        }
        internal static void BeginManipulation(bool resize)
        {
            if (_layer == null || _window == null) return;
            var pointer = _layer.Input.GetMousePositionPixel();
            float scale = _layer.UIContext.CustomInverseScale;
            _window.BeginManipulation(pointer.X * scale, pointer.Y * scale, resize);
        }
        internal static void EndManipulation()
        {
            _window?.EndManipulation(); SavePreferences();
        }
        private static void ToggleCollapsed()
        {
            _window.ToggleCollapsed(); _vm.RefreshGeometry(); _preferencesDirty = true; SavePreferences();
        }
        private static void ReleaseInput()
        {
            if (_layer == null) return;
            _layer.InputRestrictions.ResetInputRestrictions();
            if (_layer.IsFocusLayer) { _layer.IsFocusLayer = false; ScreenManager.TryLoseFocus(_layer); }
        }
        private static void LoadPreferences()
        {
            try
            {
                if (File.Exists(PreferencesPath) && new FileInfo(PreferencesPath).Length <= 4096)
                    _window.Restore(JsonConvert.DeserializeObject<ReignBanterWindowPreferences>(File.ReadAllText(PreferencesPath)));
            }
            catch (Exception ex) { WarnPreferences(ex); }
        }
        private static void SavePreferences()
        {
            if (!_preferencesDirty || _calibration || _window == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath));
                string temporary = PreferencesPath + ".tmp";
                File.WriteAllText(temporary, JsonConvert.SerializeObject(_window.Capture()));
                if (File.Exists(PreferencesPath)) File.Replace(temporary, PreferencesPath, null);
                else File.Move(temporary, PreferencesPath);
                _preferencesDirty = false;
            }
            catch (Exception ex) { _preferencesDirty = false; WarnPreferences(ex); }
        }
        private static void WarnPreferences(Exception ex)
        {
            if (_preferenceWarning) return;
            _preferenceWarning = true; ReignLog.Warn("Party banter window preferences: " + ex.Message);
        }
        internal static void Close()
        {
            EndManipulation(); ReleaseInput();
            if (_layer != null) { try { _host?.RemoveLayer(_layer); } catch { } }
            _vm?.OnFinalize(); _layer = null; _vm = null; _host = null; _window = null;
            _preferencesDirty = false; _calibration = false;
        }
        private sealed class BanterPanelVM : ViewModel
        {
            private readonly Action _collapse, _join;
            private readonly ReignBanterWindowState _window;
            private bool _canJoin;
            private int _scrollVersion;
            private string _transcript = "", _status = "";
            internal BanterPanelVM(ReignBanterWindowState window, Action collapse, Action join) { _window = window; _collapse = collapse; _join = join; }
            [DataSourceProperty] public bool Collapsed => _window.Collapsed;
            [DataSourceProperty] public bool Expanded => !Collapsed;
            [DataSourceProperty] public float PanelLeft => _window.Left;
            [DataSourceProperty] public float PanelTop => _window.Top;
            [DataSourceProperty] public float PanelWidth => _window.Width;
            [DataSourceProperty] public float PanelHeight => _window.Height;
            [DataSourceProperty] public float DragWidth => PanelWidth - 164f;
            [DataSourceProperty] public float TitleWidth => PanelWidth - 200f;
            [DataSourceProperty] public float TranscriptWidth => PanelWidth - 30f;
            [DataSourceProperty] public float TranscriptHeight => Math.Max(1f, PanelHeight - 76f);
            [DataSourceProperty] public float StatusWidth => PanelWidth - 76f;
            [DataSourceProperty] public string CollapseText => Collapsed ? "Expand" : "Minimize";
            [DataSourceProperty] public string Title => "Party banter";
            [DataSourceProperty] public string Transcript => _transcript;
            [DataSourceProperty] public string Status => _status;
            [DataSourceProperty] public bool CanJoin => _canJoin;
            [DataSourceProperty] public int ChatScrollVersion => _scrollVersion;
            internal void RefreshGeometry()
            {
                foreach (string property in new[] { nameof(Collapsed), nameof(Expanded), nameof(PanelLeft), nameof(PanelTop), nameof(PanelWidth), nameof(PanelHeight), nameof(DragWidth), nameof(TitleWidth), nameof(TranscriptWidth), nameof(TranscriptHeight), nameof(StatusWidth), nameof(CollapseText) }) OnPropertyChanged(property);
            }
            internal void Update(JObject e, bool busy)
            {
                string text = string.Join("\n\n", (e["transcript"] as JArray ?? new JArray()).OfType<JObject>()
                    .Select(x => (x.Value<string>("speaker") ?? "Traveler") + ": " + (x.Value<string>("text") ?? "")));
                if (text != _transcript) { _transcript = text; _scrollVersion++; OnPropertyChanged(nameof(Transcript)); OnPropertyChanged(nameof(ChatScrollVersion)); }
                string state = busy ? "Joining…" : e.Value<string>("state") == "generating" && text.Length == 0 ? "Someone is about to speak…" : "";
                if (state != _status) { _status = state; OnPropertyChanged(nameof(Status)); }
                if (_canJoin != !busy) { _canJoin = !busy; OnPropertyChanged(nameof(CanJoin)); }
            }
            public void ExecuteCollapse() => _collapse();
            public void ExecuteJoin() { if (CanJoin) _join(); }
        }
    }
}
