using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    /// <summary>
    /// Keeps the government member rail on complete 76-pixel card boundaries.
    /// Interactive card descendants can consume the native wheel callback, so
    /// the panel also recovers wheel input while the pointer is inside the rail.
    /// </summary>
    public sealed class ReignGovernmentMemberScrollPanel : ScrollablePanel
    {
        // The 141-pixel rail displays both 65-pixel rows plus their 11-pixel
        // separation. Treating only one row as visible left a false scrollable
        // range for two-member governments and allowed the scrollbar to move
        // both cards completely outside the clip.
        private int _visibleRows = 2;
        private int _itemCount;
        private int _currentRowIndex;

        public ReignGovernmentMemberScrollPanel(UIContext context) : base(context) { }

        public int VisibleRows
        {
            get => _visibleRows;
            set { _visibleRows = Math.Max(1, value); _currentRowIndex = ClampRowIndex(_currentRowIndex); }
        }

        public int ItemCount
        {
            get => _itemCount;
            set
            {
                _itemCount = Math.Max(0, value);
                _currentRowIndex = ClampRowIndex(_currentRowIndex);
            }
        }

        protected override void OnMouseScroll()
        {
            float delta = Context.EventManager.DeltaMouseScroll;
            if (VerticalScrollbar == null) return;
            if (Math.Abs(delta) < float.Epsilon) return;

            // Do not also start ScrollablePanel's inertial velocity. Combining
            // native momentum with a snapped row target causes the last members
            // to rebound or rest between the two card apertures.
            AdvanceRow(delta);
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (VerticalScrollbar == null) return;

            // Preserve continuous dragging, then normalize the released resting
            // position so a partially shifted member card cannot remain visible.
            if (Input.IsKeyDown(InputKey.LeftMouseButton) || Input.IsMouseScrollChanged) return;
            int restingIndex = RowIndexForOffset(
                VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            float target = RowOffset(restingIndex, VerticalScrollbar.MaxValue);
            if (restingIndex == _currentRowIndex
                && Math.Abs(target - VerticalScrollbar.ValueFloat) < 0.01f) return;
            ApplyRowIndex(restingIndex);
        }

        private int ScrollableRowCount => Math.Max(0, _itemCount - _visibleRows);

        private float RowStep(float maximum)
        {
            return ScrollableRowCount <= 0 || maximum <= 0f
                ? 0f
                : maximum / ScrollableRowCount;
        }

        private void AdvanceRow(float wheelDelta)
        {
            if (VerticalScrollbar == null || Math.Abs(wheelDelta) < float.Epsilon) return;

            _currentRowIndex = RowIndexForOffset(
                VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            int direction = wheelDelta > 0f ? -1 : 1;
            int nextIndex = ClampRowIndex(_currentRowIndex + direction);

            ResetTweenSpeed();
            if (nextIndex == _currentRowIndex) return;
            ApplyRowIndex(nextIndex);
        }

        private int RowIndexForOffset(float current, float maximum)
        {
            float step = RowStep(maximum);
            if (step <= 0f) return 0;
            return ClampRowIndex((int)Math.Round(
                current / step, MidpointRounding.AwayFromZero));
        }

        private int ClampRowIndex(int index)
        {
            return Math.Max(0, Math.Min(ScrollableRowCount, index));
        }

        private float RowOffset(int index, float maximum)
        {
            return ClampRowIndex(index) * RowStep(maximum);
        }

        private void ApplyRowIndex(int index)
        {
            _currentRowIndex = ClampRowIndex(index);
            float target = RowOffset(_currentRowIndex, VerticalScrollbar.MaxValue);
            ResetTweenSpeed();
            VerticalScrollbar.ValueFloat = target;
            SetVerticalScrollTarget(target, 0f);
        }
    }
}
