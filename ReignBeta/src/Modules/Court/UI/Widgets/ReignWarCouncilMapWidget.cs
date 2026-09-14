using System;
using System.Collections.Generic;
using ReignBeta.Shared.WarCouncil;
using ReignBeta.UI.EventArt;
using ReignBeta.UI.ViewModels;
using ReignBeta.UI.Widgets;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    /// <summary>Close fixed-scale parchment map with bounded click-drag panning and live pieces.</summary>
    public sealed class ReignWarCouncilMapWidget : Widget
    {
        public const int HighDefinitionTileGridSize = 4;
        public const int HighDefinitionTileCount = HighDefinitionTileGridSize * HighDefinitionTileGridSize;
        private const int HighDefinitionTileWarmupFrames = 2;
        private readonly HashSet<int> _builtTileIndices = new HashSet<int>();
        private int _renderedRevision = -1;
        private int _tileWarmupFrames;
        private bool _mapLayersBuilt;
        private Widget _tileLayer;
        private Widget _markerLayer;

        public ReignWarCouncilMapWidget(UIContext context) : base(context) { }

        public int MarkerRevision { get; set; }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (_renderedRevision != MarkerRevision) RebuildMarkers();
            if (_mapLayersBuilt && vm != null) EnsureVisibleHighDefinitionTile(vm);
        }

        private void RebuildMarkers()
        {
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (vm == null) { _renderedRevision = MarkerRevision; return; }
            if (!_mapLayersBuilt) BuildHighDefinitionMapTiles(vm);
            _markerLayer.RemoveAllChildren();
            foreach (ReignWarCouncilMarker marker in vm.Markers)
            {
                ReignWarCouncilPoint point = ReignWarCouncilRules.WorldToMap(marker.WorldX, marker.WorldY,
                    vm.MapWidth, vm.MapHeight);
                // The closer fixed strategic view gives the party pieces enough
                // room to read as wooden miniatures without obscuring the baked
                // settlement labels and terrain engraving.
                const float tokenSize = 108f;
                ReignEventArtWidget token = new ReignEventArtWidget(Context)
                {
                    WidthSizePolicy = SizePolicy.Fixed, HeightSizePolicy = SizePolicy.Fixed,
                    SuggestedWidth = tokenSize, SuggestedHeight = tokenSize,
                    PositionXOffset = (float)point.X - tokenSize / 2f,
                    PositionYOffset = (float)point.Y - tokenSize * 0.80f,
                    EventImageId = marker.ImageId, DoNotAcceptEvents = true
                };
                _markerLayer.AddChild(token);
                if (marker.IsPlayerRealm)
                {
                    TextWidget number = new TextWidget(Context)
                    {
                        WidthSizePolicy = SizePolicy.Fixed, HeightSizePolicy = SizePolicy.Fixed,
                        SuggestedWidth = 40f, SuggestedHeight = 19f,
                        PositionXOffset = (float)point.X - 20f, PositionYOffset = (float)point.Y + 10f,
                        Text = marker.Roman, Brush = Context.GetBrush("Info.Text"), DoNotAcceptEvents = true
                    };
                    _markerLayer.AddChild(number);
                }
            }
            _renderedRevision = MarkerRevision;
        }

        private void BuildHighDefinitionMapTiles(ReignWarCouncilScreenVM vm)
        {
            // Present the light 2,048-pixel overview first. The old eager path decoded all
            // sixteen 4,096-pixel tiles during opening (more than one GiB of decoded pixels),
            // which could block Bannerlord's UI thread for close to a minute.
            AddChild(new ReignEventArtWidget(Context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = vm.MapWidth,
                SuggestedHeight = vm.MapHeight,
                EventImageId = vm.MapImageId,
                DoNotAcceptEvents = true
            });

            _tileLayer = CreateMapLayer(vm);
            AddChild(_tileLayer);
            _markerLayer = CreateMapLayer(vm);
            AddChild(_markerLayer);
            _tileWarmupFrames = HighDefinitionTileWarmupFrames;
            _mapLayersBuilt = true;
        }

        private Widget CreateMapLayer(ReignWarCouncilScreenVM vm)
        {
            return new Widget(Context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = vm.MapWidth,
                SuggestedHeight = vm.MapHeight,
                DoNotAcceptEvents = true
            };
        }

        private void EnsureVisibleHighDefinitionTile(ReignWarCouncilScreenVM vm)
        {
            if (_tileWarmupFrames > 0)
            {
                _tileWarmupFrames--;
                return;
            }

            float tileWidth = vm.MapWidth / HighDefinitionTileGridSize;
            float tileHeight = vm.MapHeight / HighDefinitionTileGridSize;
            float visibleLeft = -vm.MapOffsetX;
            float visibleTop = -vm.MapOffsetY;
            float visibleRight = visibleLeft + ReignWarCouncilScreenVM.ViewportWidth;
            float visibleBottom = visibleTop + ReignWarCouncilScreenVM.ViewportHeight;
            int centerX = ClampTileIndex((int)Math.Floor((visibleLeft + visibleRight) * 0.5f / tileWidth));
            int centerY = ClampTileIndex((int)Math.Floor((visibleTop + visibleBottom) * 0.5f / tileHeight));

            // The center tile is sufficient almost everywhere because a tile is far
            // larger than the viewport. At a seam, add at most one remaining visible
            // neighbor per frame so no single opening frame can decode all sixteen.
            if (TryBuildHighDefinitionTile(centerX, centerY, tileWidth, tileHeight)) return;
            int startX = ClampTileIndex((int)Math.Floor(visibleLeft / tileWidth));
            int endX = ClampTileIndex((int)Math.Floor(Math.Max(0f, visibleRight - 0.01f) / tileWidth));
            int startY = ClampTileIndex((int)Math.Floor(visibleTop / tileHeight));
            int endY = ClampTileIndex((int)Math.Floor(Math.Max(0f, visibleBottom - 0.01f) / tileHeight));
            for (int y = startY; y <= endY; y++)
                for (int x = startX; x <= endX; x++)
                    if (TryBuildHighDefinitionTile(x, y, tileWidth, tileHeight)) return;
        }

        private bool TryBuildHighDefinitionTile(int x, int y, float tileWidth, float tileHeight)
        {
            int index = y * HighDefinitionTileGridSize + x;
            if (!_builtTileIndices.Add(index)) return false;
            const float seamOverlap = 0.75f;
            _tileLayer.AddChild(new ReignEventArtWidget(Context)
            {
                WidthSizePolicy = SizePolicy.Fixed,
                HeightSizePolicy = SizePolicy.Fixed,
                SuggestedWidth = tileWidth + (x + 1 < HighDefinitionTileGridSize ? seamOverlap : 0f),
                SuggestedHeight = tileHeight + (y + 1 < HighDefinitionTileGridSize ? seamOverlap : 0f),
                PositionXOffset = x * tileWidth,
                PositionYOffset = y * tileHeight,
                EventImageId = ReignEventArtTextureFactory.BuildImageId(
                    "war_council", "map_tile_" + y + "_" + x),
                DoNotAcceptEvents = true
            });
            return true;
        }

        private static int ClampTileIndex(int value)
        {
            return Math.Max(0, Math.Min(HighDefinitionTileGridSize - 1, value));
        }
    }

    /// <summary>
    /// Fixed pointer surface above the clipped map viewport. Keeping input on a
    /// stationary widget prevents the moving 44x map itself from losing Gauntlet
    /// pointer capture while it is being dragged.
    /// </summary>
    public sealed class ReignWarCouncilMapInputWidget : ButtonWidget
    {
        private bool _trackingPointer;
        private bool _leftWasDown;

        public ReignWarCouncilMapInputWidget(UIContext context) : base(context)
        {
            AutomationLateUpdateCount = 0;
            AutomationHeldFrameCount = 0;
            AutomationPressCount = 0;
            AutomationMoveCount = 0;
            AutomationReleaseCount = 0;
            AutomationScrollCount = 0;
            AutomationLastPointerInside = false;
        }

        public static int AutomationLateUpdateCount { get; private set; }
        public static int AutomationHeldFrameCount { get; private set; }
        public static int AutomationPressCount { get; private set; }
        public static int AutomationMoveCount { get; private set; }
        public static int AutomationReleaseCount { get; private set; }
        public static int AutomationScrollCount { get; private set; }
        public static bool AutomationLastPointerInside { get; private set; }
        public static float AutomationLastMouseX { get; private set; }
        public static float AutomationLastMouseY { get; private set; }
        public static float AutomationLogicalX { get; private set; }
        public static float AutomationLogicalY { get; private set; }
        public static float AutomationLogicalWidth { get; private set; }
        public static float AutomationLogicalHeight { get; private set; }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            AutomationLateUpdateCount++;
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (vm == null)
            {
                _trackingPointer = false;
                return;
            }

            var screen = Context.EventManager.MousePositionInReferenceResolution;
            UpdateAutomationGeometry(screen.X, screen.Y);
            var local = ToLogicalLocal(screen.X, screen.Y);
            vm.UpdatePointerLocation(local.X - vm.MapOffsetX, local.Y - vm.MapOffsetY);
            bool leftDown = Input.IsKeyDown(InputKey.LeftMouseButton);
            bool leftPressedThisFrame = leftDown && !_leftWasDown;
            _leftWasDown = leftDown;

            // Gauntlet normally calls OnMousePressed before assigning
            // LatestMouseDownWidget. A visually empty top-level hit surface is not
            // consistently selected by every native Bannerlord input route, so
            // acquire the pointer directly when a held press is inside this fixed
            // viewport. The bounds check keeps global polling confined to the map.
            if (!_trackingPointer)
            {
                if (!leftPressedThisFrame || !AutomationLastPointerInside) return;
                BeginTracking(vm, screen.X, screen.Y);
            }

            // Sample the held pointer once per UI frame. This remains reliable even
            // when the moving parchment or an invisible widget misses mouse-move
            // callbacks, while the stationary viewport preserves bounded capture.
            if (leftDown)
            {
                AutomationHeldFrameCount++;
                vm.PointerMoved(screen.X, screen.Y);
            }
            else
            {
                // A fallback press is not guaranteed to receive Gauntlet's native
                // release callback. Finalize it here so the VM can never retain a
                // stale pointer-down state after the physical button is released.
                CompleteTracking(vm, screen.X, screen.Y);
            }
        }

        protected override void OnMousePressed()
        {
            base.OnMousePressed();
            AutomationPressCount++;
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (vm == null) return;
            var screen = Context.EventManager.MousePositionInReferenceResolution;
            BeginTracking(vm, screen.X, screen.Y);
        }

        protected override void OnMouseMove()
        {
            base.OnMouseMove();
            AutomationMoveCount++;
            var screen = Context.EventManager.MousePositionInReferenceResolution;
            ReignWarCouncilScreenManager.ActiveViewModel?.PointerMoved(screen.X, screen.Y);
        }

        protected override void OnMouseReleased(bool isFromInput)
        {
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (vm != null && _trackingPointer)
            {
                var screen = Context.EventManager.MousePositionInReferenceResolution;
                CompleteTracking(vm, screen.X, screen.Y);
            }
            _trackingPointer = false;
            base.OnMouseReleased(isFromInput);
        }

        protected override void OnMouseAlternateReleased(bool isFromInput)
        {
            ReignWarCouncilScreenVM vm = ReignWarCouncilScreenManager.ActiveViewModel;
            if (vm != null)
            {
                var screen = Context.EventManager.MousePositionInReferenceResolution;
                var local = ToLogicalLocal(screen.X, screen.Y);
                vm.PointerRightReleased(local.X - vm.MapOffsetX, local.Y - vm.MapOffsetY);
            }
            base.OnMouseAlternateReleased(isFromInput);
        }

        protected override void OnMouseScroll()
        {
            AutomationScrollCount++;
            // The map intentionally has one fixed strategic scale. Consuming the
            // wheel here prevents it from leaking into any panel behind the map.
            base.OnMouseScroll();
        }

        private void UpdateAutomationGeometry(float screenX, float screenY)
        {
            float inverseScale = Context.CustomInverseScale;
            AutomationLastMouseX = screenX;
            AutomationLastMouseY = screenY;
            AutomationLogicalX = GlobalPosition.X * inverseScale;
            AutomationLogicalY = GlobalPosition.Y * inverseScale;
            AutomationLogicalWidth = Size.X * inverseScale;
            AutomationLogicalHeight = Size.Y * inverseScale;
            AutomationLastPointerInside = screenX >= AutomationLogicalX
                && screenY >= AutomationLogicalY
                && screenX <= AutomationLogicalX + AutomationLogicalWidth
                && screenY <= AutomationLogicalY + AutomationLogicalHeight;
        }

        private TaleWorlds.Library.Vec2 ToLogicalLocal(float screenX, float screenY)
        {
            float inverseScale = Context.CustomInverseScale;
            return new TaleWorlds.Library.Vec2(
                screenX - GlobalPosition.X * inverseScale,
                screenY - GlobalPosition.Y * inverseScale);
        }

        private void BeginTracking(ReignWarCouncilScreenVM vm, float screenX, float screenY)
        {
            _trackingPointer = true;
            var local = ToLogicalLocal(screenX, screenY);
            vm.PointerPressed(screenX, screenY,
                local.X - vm.MapOffsetX, local.Y - vm.MapOffsetY);
        }

        private void CompleteTracking(ReignWarCouncilScreenVM vm, float screenX, float screenY)
        {
            AutomationReleaseCount++;
            // A native press and release can occur between two UI frames. Apply
            // the final pointer delta before clearing the VM's press state.
            vm.PointerMoved(screenX, screenY);
            var local = ToLogicalLocal(screenX, screenY);
            float mapX = local.X - vm.MapOffsetX;
            float mapY = local.Y - vm.MapOffsetY;
            vm.UpdatePointerLocation(mapX, mapY);
            vm.PointerReleased(mapX, mapY);
            _trackingPointer = false;
        }
    }

    /// <summary>
    /// Uses Bannerlord's native wheel path for ordinary War Council lists.
    /// </summary>
    public class ReignWarCouncilWheelScrollPanel : ScrollablePanel
    {
        public ReignWarCouncilWheelScrollPanel(UIContext context) : base(context) { }
    }

    /// <summary>
    /// Keeps the four-row noble roster on complete 160-pixel card boundaries for
    /// wheel input, scrollbar release, and map-piece selection autoscroll.
    /// </summary>
    public sealed class ReignWarCouncilLordScrollPanel : ReignWarCouncilWheelScrollPanel
    {
        private const int VisibleLordRows = 4;
        private int _itemCount;
        private int _selectedIndex = -1;
        private int _pendingScrollFrames;

        public ReignWarCouncilLordScrollPanel(UIContext context) : base(context) { }

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                if (_selectedIndex == value) return;
                _selectedIndex = value;
                _pendingScrollFrames = 4;
            }
        }

        public int ItemCount
        {
            get => _itemCount;
            set
            {
                if (_itemCount == value) return;
                _itemCount = Math.Max(0, value);
                _pendingScrollFrames = 4;
            }
        }

        protected override void OnMouseScroll()
        {
            float start = VerticalScrollbar?.ValueFloat ?? 0f;
            float delta = Context.EventManager.DeltaMouseScroll;
            if (VerticalScrollbar == null) return;
            if (Math.Abs(delta) < float.Epsilon) return;
            float direction = delta > 0f ? 1f : -1f;
            ResetTweenSpeed();
            ApplyLordOffset(ResolveLordRowTarget(start, direction, VerticalScrollbar.MaxValue));
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (VerticalScrollbar == null) return;

            if (_pendingScrollFrames > 0 && _selectedIndex >= 0)
            {
                _pendingScrollFrames--;
                ApplyLordOffset(ResolveSelectedRowTarget(_selectedIndex, VerticalScrollbar.MaxValue));
                return;
            }

            // Preserve continuous scrollbar dragging, then normalize the released
            // resting position so a partial card can never remain at either edge.
            if (Input.IsKeyDown(InputKey.LeftMouseButton) || Input.IsMouseScrollChanged) return;
            float target = SnapToLordRow(VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            if (Math.Abs(target - VerticalScrollbar.ValueFloat) < 0.01f) return;
            ApplyLordOffset(target);
        }

        private void ApplyLordOffset(float target)
        {
            ResetTweenSpeed();
            VerticalScrollbar.ValueFloat = target;
            SetVerticalScrollTarget(target, 0f);
        }

        private int ScrollableRowCount => Math.Max(0, _itemCount - VisibleLordRows);

        private float LordRowStep(float maximum)
        {
            return ScrollableRowCount <= 0 || maximum <= 0f ? 0f : maximum / ScrollableRowCount;
        }

        private float ResolveLordRowTarget(float current, float direction, float maximum)
        {
            float step = LordRowStep(maximum);
            if (step <= 0f) return 0f;
            float currentRow = (float)Math.Round(current / step, MidpointRounding.AwayFromZero);
            return Math.Max(0f, Math.Min(maximum, (currentRow - direction) * step));
        }

        private float SnapToLordRow(float current, float maximum)
        {
            float step = LordRowStep(maximum);
            if (step <= 0f) return 0f;
            return Math.Max(0f, Math.Min(maximum,
                (float)Math.Round(current / step, MidpointRounding.AwayFromZero) * step));
        }

        private float ResolveSelectedRowTarget(int selectedIndex, float maximum)
        {
            float step = LordRowStep(maximum);
            int topRow = Math.Max(0, Math.Min(ScrollableRowCount, selectedIndex - 2));
            return topRow * step;
        }
    }
}
