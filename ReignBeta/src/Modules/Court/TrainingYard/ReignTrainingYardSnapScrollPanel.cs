#if !REIGN_EXCLUDE_COURT
using System;
using Reign.Core.Contracts.TrainingYard;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    public sealed class ReignTrainingYardSnapScrollPanel : ScrollablePanel
    {
        private int _itemCount;
        private int _visibleItems = 1;
        private int _currentIndex;

        public ReignTrainingYardSnapScrollPanel(UIContext context) : base(context) { }

        public int ItemCount
        {
            get => _itemCount;
            set { _itemCount = Math.Max(0, value); _currentIndex = ClampIndex(_currentIndex); }
        }

        public int VisibleItems
        {
            get => _visibleItems;
            set { _visibleItems = Math.Max(1, value); _currentIndex = ClampIndex(_currentIndex); }
        }

        protected override void OnMouseScroll()
        {
            float delta = Context.EventManager.DeltaMouseScroll;
            if (VerticalScrollbar == null) return;
            if (Math.Abs(delta) < float.Epsilon) return;

            // This callback is the sole owner of wheel input. A frame-level
            // fallback can observe the same notch again after Gauntlet dispatch
            // and reverse or duplicate a snapped card movement.
            Advance(delta);
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (VerticalScrollbar == null) return;
            if (Input.IsKeyDown(InputKey.LeftMouseButton) || Input.IsMouseScrollChanged) return;
            Apply(IndexForOffset(VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue));
        }

        private int MaximumIndex => Math.Max(0, _itemCount - _visibleItems);
        private int ClampIndex(int value) => Math.Max(0, Math.Min(MaximumIndex, value));

        private int IndexForOffset(float current, float maximum)
        {
            if (MaximumIndex == 0 || maximum <= 0f) return 0;
            return ClampIndex((int)Math.Round(current * MaximumIndex / maximum, MidpointRounding.AwayFromZero));
        }

        private void Advance(float delta)
        {
            if (VerticalScrollbar == null || Math.Abs(delta) < float.Epsilon) return;
            int current = IndexForOffset(VerticalScrollbar.ValueFloat, VerticalScrollbar.MaxValue);
            int next = ReignTrainingYardRules.ResolveScrollIndex(_itemCount, _visibleItems, current, delta > 0f ? -1 : 1);
            ResetTweenSpeed();
            Apply(next);
        }

        private void Apply(int index)
        {
            if (VerticalScrollbar == null) return;
            _currentIndex = ClampIndex(index);
            float target = ReignTrainingYardRules.ResolveScrollOffset(_itemCount, _visibleItems, _currentIndex, VerticalScrollbar.MaxValue);
            if (Math.Abs(target - VerticalScrollbar.ValueFloat) < 0.01f) return;
            ResetTweenSpeed();
            VerticalScrollbar.ValueFloat = target;
            SetVerticalScrollTarget(target, 0f);
        }
    }
}
#endif
