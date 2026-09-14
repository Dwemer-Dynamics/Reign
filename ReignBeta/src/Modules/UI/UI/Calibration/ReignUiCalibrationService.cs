extern alias NumericsVectors;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignBeta.Settings;
using ReignBeta.UI.ViewModels;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using Vector2 = NumericsVectors::System.Numerics.Vector2;

namespace ReignBeta.UI.Calibration
{
    public static class ReignUiCalibrationService
    {
        internal const string OverlayMovieName = "ReignUiCalibrationOverlay";
        private static readonly ConditionalWeakTable<UIContext, ReignUiCalibrationSession> Sessions = new ConditionalWeakTable<UIContext, ReignUiCalibrationSession>();
        private static readonly Dictionary<string, WeakReference<ReignUiCalibrationSession>> SessionsByMovie = new Dictionary<string, WeakReference<ReignUiCalibrationSession>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> HeadlessNativeMovieNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "GameMenu",
            "EncyclopediaHeroPage",
            "ClanMembers",
            "MarriageOfferPopup",
            "HeirSelectionPopup",
            "SkillGridItem",
            "CharacterDeveloper",
            "CraftingHeroPopup",
            "Crafting",
            "EducationGainedProperties",
            "RecruitVolunteerTuple",
            "GameMenuPartyItem",
            "SPConversation",
            "QuestsScreen"
        };
        private static readonly object Sync = new object();
        private static readonly HashSet<string> AutomationLeaseOwners =
            new HashSet<string>(StringComparer.Ordinal);
        private static string _authorizedHeadlessNativeMovie = string.Empty;
        private static bool _automationEnabled;

        public static bool AutomationEnabled
        {
            get
            {
                lock (Sync) return _automationEnabled || AutomationLeaseOwners.Count > 0;
            }
            set
            {
                lock (Sync) _automationEnabled = value;
            }
        }

        internal static void AcquireAutomationLease(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner))
                throw new ArgumentException("A calibration automation lease owner is required.", nameof(owner));
            lock (Sync) AutomationLeaseOwners.Add(owner);
        }

        internal static void ReleaseAutomationLease(string owner)
        {
            if (string.IsNullOrWhiteSpace(owner)) return;
            lock (Sync) AutomationLeaseOwners.Remove(owner);
        }

        internal static bool TryAuthorizeHeadlessNativeMovie(string movieName, out string error)
        {
            error = string.Empty;
            if (!HeadlessNativeMovieNames.Contains(movieName ?? string.Empty))
            {
                error = "The requested movie is not an allowlisted native Reign augmentation target.";
                return false;
            }
            lock (Sync) _authorizedHeadlessNativeMovie = movieName;
            return true;
        }

        internal static void ClearAuthorizedHeadlessNativeMovie()
        {
            lock (Sync) _authorizedHeadlessNativeMovie = string.Empty;
        }

        public static void TryAttach(GauntletLayer layer, string movieName)
        {
            Widget contextRoot = layer?.UIContext?.Root;
            Widget targetRoot = contextRoot != null && contextRoot.ChildCount > 0 ? contextRoot.GetChild(contextRoot.ChildCount - 1) : null;
            if (layer?.UIContext == null || targetRoot == null
                || !IsCalibratableMovie(movieName)
                || string.Equals(movieName, OverlayMovieName, StringComparison.Ordinal)
                || (!AutomationEnabled && ReignBetaSettings.Instance?.McmTestModeEnabled != true))
            {
                return;
            }

            lock (Sync)
            {
                if (Sessions.TryGetValue(layer.UIContext, out ReignUiCalibrationSession existing))
                {
                    existing.SetTarget(movieName, targetRoot);
                    SessionsByMovie[movieName] = new WeakReference<ReignUiCalibrationSession>(existing);
                    return;
                }

                var session = new ReignUiCalibrationSession(layer.UIContext, movieName, targetRoot);
                Sessions.Add(layer.UIContext, session);
                SessionsByMovie[movieName] = new WeakReference<ReignUiCalibrationSession>(session);
                // Native Bannerlord keeps only this headless target registration.
                // ReignUiCalibrationOverlay is a browser-previewer asset and must
                // never be loaded into the live Gauntlet widget tree.
            }
        }

        public static bool TrySaveSnapshot(string movieName, out string path, out string error)
        {
            path = string.Empty;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(movieName))
            {
                error = "An owned Reign UI movie name is required.";
                return false;
            }
            lock (Sync)
            {
                if (!SessionsByMovie.TryGetValue(movieName, out WeakReference<ReignUiCalibrationSession> reference)
                    || !reference.TryGetTarget(out ReignUiCalibrationSession session)
                    || !session.IsConnected)
                {
                    error = "No connected calibration session exists for " + movieName + ".";
                    return false;
                }
                path = session.SaveSnapshot(false, out error);
                return !string.IsNullOrWhiteSpace(path);
            }
        }

        internal static void PointerPressed(UIContext context)
        {
            if (context != null && Sessions.TryGetValue(context, out ReignUiCalibrationSession session)) session.PointerPressed();
        }

        internal static void PointerMoved(UIContext context)
        {
            if (context != null && Sessions.TryGetValue(context, out ReignUiCalibrationSession session)) session.PointerMoved();
        }

        internal static void PointerReleased(UIContext context)
        {
            if (context != null && Sessions.TryGetValue(context, out ReignUiCalibrationSession session)) session.PointerReleased();
        }

        private static bool IsCalibratableMovie(string movieName)
        {
            return !string.IsNullOrWhiteSpace(movieName)
                && (movieName.StartsWith("Reign", StringComparison.Ordinal)
                    || string.Equals(movieName, "AIPortraitsMemoriesBook", StringComparison.Ordinal)
                    || string.Equals(movieName, _authorizedHeadlessNativeMovie, StringComparison.Ordinal));
        }
    }

    public sealed class ReignUiCalibrationSession
    {
        private readonly UIContext _context;
        private readonly Dictionary<Widget, WidgetGeometry> _originalStates = new Dictionary<Widget, WidgetGeometry>();
        private readonly Stack<WidgetChange> _undo = new Stack<WidgetChange>();
        private List<Widget> _lastCandidates = new List<Widget>();
        private int _candidateIndex;
        private bool _pointerDown;
        private bool _pointerChanged;
        private float _pointerStartX;
        private float _pointerStartY;
        private WidgetGeometry _dragStart;
        private string _loadedPrefabSha256;

        internal ReignUiCalibrationSession(UIContext context, string movieName, Widget targetRoot)
        {
            _context = context;
            MovieName = movieName;
            TargetRoot = targetRoot;
            _loadedPrefabSha256 = PrefabSha256(movieName);
            ViewModel = new ReignUiCalibrationVM(this);
            Refresh();
        }

        internal ReignUiCalibrationVM ViewModel { get; }
        internal Widget TargetRoot { get; private set; }
        internal Widget SelectedWidget { get; private set; }
        internal string MovieName { get; private set; }
        internal bool IsExpanded { get; private set; }
        internal bool IsResizeMode { get; private set; }
        internal string StatusText { get; private set; } = "Click a widget, drag it, then export a snapshot for the XML previewer.";
        internal bool IsConnected => TargetRoot?.ConnectedToRoot == true;

        internal string SelectionLabel => SelectedWidget == null
            ? "Click an element to select it"
            : SelectedWidget.GetType().Name + (string.IsNullOrWhiteSpace(SelectedWidget.Id) ? "" : " #" + SelectedWidget.Id);

        internal string GeometryLabel => SelectedWidget == null
            ? "No live widget selected"
            : string.Format("x {0:0.#}  y {1:0.#}  |  {2:0.#} x {3:0.#}  |  {4}/{5}",
                SelectionX, SelectionY, SelectionWidth, SelectionHeight,
                SelectedWidget.WidthSizePolicy, SelectedWidget.HeightSizePolicy);

        internal float SelectionX => SelectedWidget == null ? 0f : SelectedWidget.GlobalPosition.X * _context.CustomInverseScale;
        internal float SelectionY => SelectedWidget == null ? 0f : SelectedWidget.GlobalPosition.Y * _context.CustomInverseScale;
        internal float SelectionWidth => SelectedWidget == null ? 1f : Math.Max(1f, SelectedWidget.Size.X * _context.CustomInverseScale);
        internal float SelectionHeight => SelectedWidget == null ? 1f : Math.Max(1f, SelectedWidget.Size.Y * _context.CustomInverseScale);

        internal void SetTarget(string movieName, Widget targetRoot)
        {
            MovieName = movieName;
            TargetRoot = targetRoot;
            _loadedPrefabSha256 = PrefabSha256(movieName);
            SelectedWidget = null;
            _lastCandidates.Clear();
            _candidateIndex = 0;
            StatusText = "Target changed. Click a widget to begin live calibration.";
            Refresh();
        }

        internal void ToggleExpanded() { SetExpanded(!IsExpanded); }

        internal void SetExpanded(bool expanded)
        {
            IsExpanded = expanded;
            _pointerDown = false;
            StatusText = expanded
                ? "Move mode: drag a selected widget. Toggle mode for bottom-right resize."
                : "Calibration collapsed; live changes remain active in this screen.";
            Refresh();
        }

        internal void ToggleMode()
        {
            IsResizeMode = !IsResizeMode;
            StatusText = IsResizeMode
                ? "Resize mode: drag right/down from the selected widget; fixed size policies are used while resizing."
                : "Move mode: drag the selected widget or use one-pixel nudge buttons.";
            Refresh();
        }

        internal void PointerPressed()
        {
            if (!IsExpanded || TargetRoot == null) return;
            _lastCandidates = HitCandidates(TargetRoot);
            _candidateIndex = 0;
            SelectedWidget = _lastCandidates.FirstOrDefault();
            if (SelectedWidget == null)
            {
                StatusText = "No target widget was found beneath the pointer.";
                Refresh();
                return;
            }

            _pointerDown = true;
            _pointerChanged = false;
            _pointerStartX = _context.EventManager.MousePositionInReferenceResolution.X;
            _pointerStartY = _context.EventManager.MousePositionInReferenceResolution.Y;
            _dragStart = WidgetGeometry.Capture(SelectedWidget);
            StatusText = "Selected " + SelectionLabel + ".";
            Refresh();
        }

        internal void PointerMoved()
        {
            if (!_pointerDown || SelectedWidget == null) return;
            float dx = _context.EventManager.MousePositionInReferenceResolution.X - _pointerStartX;
            float dy = _context.EventManager.MousePositionInReferenceResolution.Y - _pointerStartY;
            if (!_pointerChanged && dx * dx + dy * dy < 0.25f) return;
            if (!_pointerChanged)
            {
                BeginChange(SelectedWidget, _dragStart);
                _pointerChanged = true;
            }

            if (IsResizeMode)
            {
                EnsureFixedSize(SelectedWidget, _dragStart);
                SelectedWidget.SuggestedWidth = Math.Max(1f, _dragStart.LogicalWidth + dx);
                SelectedWidget.SuggestedHeight = Math.Max(1f, _dragStart.LogicalHeight + dy);
            }
            else
            {
                SelectedWidget.PositionXOffset = _dragStart.PositionXOffset + dx;
                SelectedWidget.PositionYOffset = _dragStart.PositionYOffset + dy;
            }
            Refresh();
        }

        internal void PointerReleased()
        {
            if (!_pointerDown) return;
            _pointerDown = false;
            StatusText = _pointerChanged
                ? "Live geometry changed. Export a snapshot when the fit is correct."
                : "Selected " + SelectionLabel + ".";
            Refresh();
        }

        internal void Nudge(float dx, float dy)
        {
            if (SelectedWidget == null) return;
            WidgetGeometry before = WidgetGeometry.Capture(SelectedWidget);
            BeginChange(SelectedWidget, before);
            SelectedWidget.PositionXOffset += dx;
            SelectedWidget.PositionYOffset += dy;
            StatusText = "Moved selected widget by one logical pixel.";
            Refresh();
        }

        internal void Resize(float dw, float dh)
        {
            if (SelectedWidget == null) return;
            WidgetGeometry before = WidgetGeometry.Capture(SelectedWidget);
            BeginChange(SelectedWidget, before);
            EnsureFixedSize(SelectedWidget, before);
            SelectedWidget.SuggestedWidth = Math.Max(1f, before.LogicalWidth + dw);
            SelectedWidget.SuggestedHeight = Math.Max(1f, before.LogicalHeight + dh);
            StatusText = "Resized selected widget by one logical pixel.";
            Refresh();
        }

        internal void Undo()
        {
            if (_undo.Count == 0)
            {
                StatusText = "There are no live changes to undo.";
                Refresh();
                return;
            }
            WidgetChange change = _undo.Pop();
            if (change.Widget?.ConnectedToRoot == true)
            {
                change.Before.Apply(change.Widget);
                SelectedWidget = change.Widget;
                StatusText = "Undid the last live geometry change.";
            }
            Refresh();
        }

        internal void SelectParent()
        {
            if (SelectedWidget?.ParentWidget == null || SelectedWidget == TargetRoot) return;
            SelectedWidget = SelectedWidget.ParentWidget;
            StatusText = "Selected parent " + SelectionLabel + ".";
            Refresh();
        }

        internal void CycleSelection()
        {
            if (_lastCandidates.Count < 2) return;
            _candidateIndex = (_candidateIndex + 1) % _lastCandidates.Count;
            SelectedWidget = _lastCandidates[_candidateIndex];
            StatusText = string.Format("Layer {0} of {1}: {2}", _candidateIndex + 1, _lastCandidates.Count, SelectionLabel);
            Refresh();
        }

        internal bool TrySelectWidgetForCapture(string widgetId, out string error)
        {
            error = string.Empty;
            List<Widget> matches = Flatten(TargetRoot)
                .Where(widget => widget.ConnectedToRoot
                    && widget.IsVisible
                    && widget.IsRecursivelyVisible()
                    && string.Equals(widget.Id, widgetId, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1)
            {
                error = string.Format("Expected exactly one visible connected widget with id '{0}', found {1}.", widgetId, matches.Count);
                return false;
            }

            SelectedWidget = matches[0];
            _lastCandidates = new List<Widget> { SelectedWidget };
            _candidateIndex = 0;
            _pointerDown = false;
            _pointerChanged = false;
            StatusText = "Selected " + SelectionLabel + " for deterministic capture.";
            Refresh();
            return true;
        }

        internal void SaveSnapshot()
        {
            SaveSnapshot(true, out _);
        }

        internal string SaveSnapshot(bool displayMessage, out string error)
        {
            error = string.Empty;
            try
            {
                string directory = Path.Combine(BasePath.Name, "Modules", "ReignBeta", "UiCalibration", "snapshots");
                Directory.CreateDirectory(directory);
                string safeMovie = new string((MovieName ?? "ReignUI").Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
                string path = Path.Combine(directory, safeMovie + "-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
                var widgets = Flatten(TargetRoot).Select(widget => BuildSnapshotRow(widget)).ToArray();
                var payload = new
                {
                    schema = "reign-ui-runtime-snapshot-v1",
                    createdUtc = DateTime.UtcNow.ToString("o"),
                    movieName = MovieName,
                    physicalWidth = TaleWorlds.Engine.Screen.RealScreenResolutionWidth,
                    physicalHeight = TaleWorlds.Engine.Screen.RealScreenResolutionHeight,
                    uiScale = _context.CustomScale,
                    supportUiInjected = false,
                    widgets
                };
                JObject snapshot = JObject.FromObject(payload);
                snapshot.Add("prefabSha256", _loadedPrefabSha256 ?? string.Empty);
                snapshot.Add("installedPrefabSha256AtCapture", PrefabSha256(MovieName));
                File.WriteAllText(path, snapshot.ToString(Formatting.Indented));
                StatusText = "Snapshot saved: " + path;
                if (displayMessage) InformationManager.DisplayMessage(new InformationMessage("Reign UI calibration snapshot saved.", Color.FromUint(0xFF65E1FF)));
                ReignLog.Info("UI calibration snapshot saved to " + path);
                Refresh();
                return path;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                StatusText = "Snapshot failed: " + ex.Message;
                ReignLog.Warn("UI calibration snapshot failed: " + ex);
                Refresh();
                return string.Empty;
            }
        }

        private static string PrefabSha256(string movieName)
        {
            string safeMovie = new string((movieName ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_').ToArray());
            if (string.IsNullOrWhiteSpace(safeMovie)) return string.Empty;
            string prefabPath = Path.Combine(BasePath.Name, "Modules", "ReignBeta", "GUI", "Prefabs", safeMovie + ".xml");
            if (!File.Exists(prefabPath)) return string.Empty;
            using (FileStream stream = File.OpenRead(prefabPath))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private object BuildSnapshotRow(Widget widget)
        {
            WidgetGeometry current = WidgetGeometry.Capture(widget);
            Dictionary<string, object> changes = new Dictionary<string, object>();
            if (_originalStates.TryGetValue(widget, out WidgetGeometry original)) changes = current.ChangesFrom(original);
            TextWidget textWidget = widget as TextWidget;
            return new
            {
                id = widget.Id ?? string.Empty,
                path = RuntimePath(widget),
                type = widget.GetType().Name,
                logicalX = widget.GlobalPosition.X * _context.CustomInverseScale,
                logicalY = widget.GlobalPosition.Y * _context.CustomInverseScale,
                logicalWidth = widget.Size.X * _context.CustomInverseScale,
                logicalHeight = widget.Size.Y * _context.CustomInverseScale,
                widthPolicy = widget.WidthSizePolicy.ToString(),
                heightPolicy = widget.HeightSizePolicy.ToString(),
                clipContents = widget.ClipContents,
                isVisible = widget.IsVisible,
                isEnabled = widget.IsEnabled,
                isConnected = widget.ConnectedToRoot,
                textureProvider = (widget as TextureWidget)?.TextureProviderName ?? string.Empty,
                text = textWidget?.Text ?? string.Empty,
                brush = textWidget?.Brush?.Name ?? string.Empty,
                fontSize = textWidget?.Brush?.FontSize ?? 0,
                positionXOffset = textWidget?.PositionXOffset ?? 0f,
                positionYOffset = textWidget?.PositionYOffset ?? 0f,
                changes
            };
        }

        private void BeginChange(Widget widget, WidgetGeometry before)
        {
            if (!_originalStates.ContainsKey(widget)) _originalStates[widget] = before;
            _undo.Push(new WidgetChange(widget, before));
        }

        private void EnsureFixedSize(Widget widget, WidgetGeometry start)
        {
            if (widget.WidthSizePolicy != SizePolicy.Fixed)
            {
                widget.WidthSizePolicy = SizePolicy.Fixed;
                widget.SuggestedWidth = start.LogicalWidth;
            }
            if (widget.HeightSizePolicy != SizePolicy.Fixed)
            {
                widget.HeightSizePolicy = SizePolicy.Fixed;
                widget.SuggestedHeight = start.LogicalHeight;
            }
        }

        private List<Widget> HitCandidates(Widget root)
        {
            var result = new List<Widget>();
            CollectHits(root, result);
            return result;
        }

        private void CollectHits(Widget widget, List<Widget> result)
        {
            if (widget == null || !widget.IsVisible || !widget.IsRecursivelyVisible()) return;
            for (int index = widget.ChildCount - 1; index >= 0; index--) CollectHits(widget.GetChild(index), result);
            if (widget != TargetRoot && widget.IsPointInsideMeasuredArea(_context.EventManager.MousePosition)) result.Add(widget);
        }

        private static IEnumerable<Widget> Flatten(Widget root)
        {
            if (root == null) yield break;
            yield return root;
            for (int index = 0; index < root.ChildCount; index++)
            {
                foreach (Widget child in Flatten(root.GetChild(index))) yield return child;
            }
        }

        private static string RuntimePath(Widget widget)
        {
            var segments = new Stack<string>();
            for (Widget current = widget; current != null; current = current.ParentWidget)
            {
                int sameTypeIndex = 1;
                if (current.ParentWidget != null)
                {
                    for (int index = 0; index < current.GetSiblingIndex(); index++)
                    {
                        if (current.ParentWidget.GetChild(index)?.GetType() == current.GetType()) sameTypeIndex++;
                    }
                }
                segments.Push(current.GetType().Name + "[" + sameTypeIndex + "]");
            }
            return "/" + string.Join("/", segments);
        }

        private void Refresh()
        {
            ViewModel.UpdateFromSession(this);
        }

        private readonly struct WidgetChange
        {
            internal WidgetChange(Widget widget, WidgetGeometry before) { Widget = widget; Before = before; }
            internal Widget Widget { get; }
            internal WidgetGeometry Before { get; }
        }

        private readonly struct WidgetGeometry
        {
            private WidgetGeometry(Widget widget)
            {
                PositionXOffset = widget.PositionXOffset;
                PositionYOffset = widget.PositionYOffset;
                SuggestedWidth = widget.SuggestedWidth;
                SuggestedHeight = widget.SuggestedHeight;
                MarginLeft = widget.MarginLeft;
                MarginTop = widget.MarginTop;
                MarginRight = widget.MarginRight;
                MarginBottom = widget.MarginBottom;
                WidthSizePolicy = widget.WidthSizePolicy;
                HeightSizePolicy = widget.HeightSizePolicy;
                LogicalWidth = widget.Size.X * widget.Context.CustomInverseScale;
                LogicalHeight = widget.Size.Y * widget.Context.CustomInverseScale;
            }

            internal float PositionXOffset { get; }
            internal float PositionYOffset { get; }
            internal float SuggestedWidth { get; }
            internal float SuggestedHeight { get; }
            internal float MarginLeft { get; }
            internal float MarginTop { get; }
            internal float MarginRight { get; }
            internal float MarginBottom { get; }
            internal SizePolicy WidthSizePolicy { get; }
            internal SizePolicy HeightSizePolicy { get; }
            internal float LogicalWidth { get; }
            internal float LogicalHeight { get; }

            internal static WidgetGeometry Capture(Widget widget) { return new WidgetGeometry(widget); }

            internal void Apply(Widget widget)
            {
                widget.WidthSizePolicy = WidthSizePolicy;
                widget.HeightSizePolicy = HeightSizePolicy;
                widget.SuggestedWidth = SuggestedWidth;
                widget.SuggestedHeight = SuggestedHeight;
                widget.PositionXOffset = PositionXOffset;
                widget.PositionYOffset = PositionYOffset;
                widget.MarginLeft = MarginLeft;
                widget.MarginTop = MarginTop;
                widget.MarginRight = MarginRight;
                widget.MarginBottom = MarginBottom;
            }

            internal Dictionary<string, object> ChangesFrom(WidgetGeometry original)
            {
                var changes = new Dictionary<string, object>();
                Add(changes, "PositionXOffset", PositionXOffset, original.PositionXOffset);
                Add(changes, "PositionYOffset", PositionYOffset, original.PositionYOffset);
                Add(changes, "SuggestedWidth", SuggestedWidth, original.SuggestedWidth);
                Add(changes, "SuggestedHeight", SuggestedHeight, original.SuggestedHeight);
                Add(changes, "MarginLeft", MarginLeft, original.MarginLeft);
                Add(changes, "MarginTop", MarginTop, original.MarginTop);
                Add(changes, "MarginRight", MarginRight, original.MarginRight);
                Add(changes, "MarginBottom", MarginBottom, original.MarginBottom);
                if (WidthSizePolicy != original.WidthSizePolicy) changes["WidthSizePolicy"] = WidthSizePolicy.ToString();
                if (HeightSizePolicy != original.HeightSizePolicy) changes["HeightSizePolicy"] = HeightSizePolicy.ToString();
                return changes;
            }

            private static void Add(IDictionary<string, object> changes, string name, float current, float original)
            {
                if (Math.Abs(current - original) > 0.001f) changes[name] = Math.Round(current, 3);
            }
        }
    }
}
