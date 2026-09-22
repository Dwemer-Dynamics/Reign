using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI
{
    /// <summary>Four complete cards; wheel and resting drag positions never leave a half card.</summary>
    public sealed class ReignClanAccordsSnapScrollPanel : ScrollablePanel
    {
        public ReignClanAccordsSnapScrollPanel(UIContext context) : base(context) { }
        public int ItemCount { get; set; }
        private int MaximumIndex => Math.Max(0, ItemCount - 4);
        private int Index => VerticalScrollbar == null || VerticalScrollbar.MaxValue <= 0 || MaximumIndex == 0 ? 0
            : Math.Max(0, Math.Min(MaximumIndex, (int)Math.Round(VerticalScrollbar.ValueFloat / VerticalScrollbar.MaxValue * MaximumIndex, MidpointRounding.AwayFromZero)));
        private void Apply(int index)
        {
            if (VerticalScrollbar == null) return;
            float value = MaximumIndex == 0 ? 0 : Math.Max(0, Math.Min(MaximumIndex, index)) * VerticalScrollbar.MaxValue / MaximumIndex;
            ResetTweenSpeed();
            VerticalScrollbar.ValueFloat = value;
            SetVerticalScrollTarget(value, 0f);
        }
        protected override void OnMouseScroll()
        {
            float delta = Context.EventManager.DeltaMouseScroll;
            if (Math.Abs(delta) > float.Epsilon) Apply(Index + (delta > 0 ? -1 : 1));
        }
        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (!Input.IsKeyDown(InputKey.LeftMouseButton) && !Input.IsMouseScrollChanged) Apply(Index);
        }
    }
}
