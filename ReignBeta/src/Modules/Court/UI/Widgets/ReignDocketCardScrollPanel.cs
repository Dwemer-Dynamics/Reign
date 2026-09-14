using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    /// <summary>
    /// Scrolls the ruler docket by one complete petition card and clamps the
    /// rail at its real first and last entries. Four or fewer petitions do not
    /// create a scroll range and cannot move the list.
    /// </summary>
    public sealed class ReignDocketCardScrollPanel : ScrollablePanel
    {
        private const int VisibleCards = 4;
        private int _itemCount;
        private int _currentCardIndex;
        private bool _pointerDragActive;

        public ReignDocketCardScrollPanel(UIContext context) : base(context) { }

        public int ItemCount
        {
            get => _itemCount;
            set
            {
                int nextCount = Math.Max(0, value);
                if (_itemCount == nextCount) return;
                _itemCount = nextCount;
                // A refreshed docket is a new bounded list. Never inherit a stale
                // offset from yesterday or from a previously longer collection.
                ResetToTop();
            }
        }

        private bool CanScroll => _itemCount > VisibleCards;

        protected override void OnMouseScroll()
        {
            float delta = Context.EventManager.DeltaMouseScroll;
            if (!CanScroll)
            {
                ResetToTop();
                return;
            }
            if (VerticalScrollbar == null || Math.Abs(delta) < float.Epsilon)
            {
                return;
            }
            AdvanceCard(delta);
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (VerticalScrollbar == null) return;
            if (!CanScroll)
            {
                ResetToTop();
                return;
            }

            if (Input.IsKeyDown(InputKey.LeftMouseButton))
            {
                _pointerDragActive = true;
                return;
            }
            if (_pointerDragActive)
            {
                _pointerDragActive = false;
                _currentCardIndex = CardIndexForOffset(
                    VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            }

            if (Input.IsMouseScrollChanged) return;

            // Normalize track clicks and released dragging from the scrollbar's
            // actual position. Never restore a stale pre-input card index.
            int restingIndex = CardIndexForOffset(
                VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            float target = CardOffset(restingIndex, VerticalScrollbar.MaxValue);
            if (restingIndex == _currentCardIndex
                && Math.Abs(target - VerticalScrollbar.ValueFloat) < 0.01f) return;
            ApplyCardIndex(restingIndex);
        }

        private int ScrollableCardCount => Math.Max(0, _itemCount - VisibleCards);

        private float CardStep(float maximum)
        {
            return ScrollableCardCount <= 0 || maximum <= 0f
                ? 0f
                : maximum / ScrollableCardCount;
        }

        private void AdvanceCard(float wheelDelta)
        {
            if (!CanScroll || VerticalScrollbar == null
                || Math.Abs(wheelDelta) < float.Epsilon)
            {
                ResetToTop();
                return;
            }

            int direction = wheelDelta > 0f ? -1 : 1;
            int nextIndex = ClampCardIndex(_currentCardIndex + direction);
            // Apply even at a boundary. This clears native residual velocity and
            // makes repeated upward input hold the true first card at zero.
            ApplyCardIndex(nextIndex);
        }

        private int CardIndexForOffset(float current, float maximum)
        {
            float step = CardStep(maximum);
            if (step <= 0f) return 0;
            return ClampCardIndex((int)Math.Round(
                current / step, MidpointRounding.AwayFromZero));
        }

        private int ClampCardIndex(int index)
        {
            return Math.Max(0, Math.Min(ScrollableCardCount, index));
        }

        private float CardOffset(int index, float maximum)
        {
            return ClampCardIndex(index) * CardStep(maximum);
        }

        private void ApplyCardIndex(int index)
        {
            _currentCardIndex = ClampCardIndex(index);
            float target = CardOffset(_currentCardIndex, VerticalScrollbar.MaxValue);
            ResetTweenSpeed();
            VerticalScrollbar.ValueFloat = target;
            SetVerticalScrollTarget(target, 0f);
        }

        private void ResetToTop()
        {
            _currentCardIndex = 0;
            ResetTweenSpeed();
            if (VerticalScrollbar == null) return;
            VerticalScrollbar.ValueFloat = 0f;
            SetVerticalScrollTarget(0f, 0f);
        }
    }
}
