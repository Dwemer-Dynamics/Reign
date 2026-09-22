using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    /// <summary>
    /// Keeps the three-card ambassador rail on complete card boundaries for
    /// wheel input and after the horizontal scrollbar is released.
    /// </summary>
    public sealed class ReignAmbassadorSnapScrollPanel : ScrollablePanel
    {
        private const int VisibleCards = 3;
        private int _itemCount;
        private int _currentCardIndex;

        public ReignAmbassadorSnapScrollPanel(UIContext context) : base(context) { }

        public int ItemCount
        {
            get => _itemCount;
            set
            {
                if (_itemCount == value) return;
                _itemCount = Math.Max(0, value);
                _currentCardIndex = ClampCardIndex(_currentCardIndex);
            }
        }

        protected override void OnMouseScroll()
        {
            float delta = Context.EventManager.DeltaMouseScroll;
            if (HorizontalScrollbar == null) return;
            if (Math.Abs(delta) < float.Epsilon) return;

            // Handle a horizontal wheel action entirely here. Calling the base
            // implementation also starts inertial velocity; at either endpoint
            // that velocity used to fight the snapped value and bounce the rail
            // away from the first or last ambassador.
            AdvanceCard(delta);
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (HorizontalScrollbar == null) return;

            // Preserve continuous dragging, then normalize the resting position.
            // This also makes track clicks and the far-right stop land on a slot.
            if (Input.IsKeyDown(InputKey.LeftMouseButton) || Input.IsMouseScrollChanged) return;
            int restingIndex = CardIndexForOffset(
                HorizontalScrollbar.ValueFloat, HorizontalScrollbar.MaxValue);
            float target = CardOffset(restingIndex, HorizontalScrollbar.MaxValue);
            if (restingIndex == _currentCardIndex
                && Math.Abs(target - HorizontalScrollbar.ValueFloat) < 0.01f) return;
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
            if (HorizontalScrollbar == null || Math.Abs(wheelDelta) < float.Epsilon) return;

            _currentCardIndex = CardIndexForOffset(
                HorizontalScrollbar.ValueFloat, HorizontalScrollbar.MaxValue);
            int direction = wheelDelta > 0f ? -1 : 1;
            int nextIndex = ClampCardIndex(_currentCardIndex + direction);

            // Even a boundary no-op clears any velocity left by an earlier
            // native action. Reapplying a different target is deliberately
            // avoided, so the endpoint cannot rebound.
            ResetTweenSpeed();
            if (nextIndex == _currentCardIndex) return;
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
            float target = CardOffset(_currentCardIndex, HorizontalScrollbar.MaxValue);
            ResetTweenSpeed();
            HorizontalScrollbar.ValueFloat = target;
            SetHorizontalScrollTarget(target, 0f);
        }
    }
}
