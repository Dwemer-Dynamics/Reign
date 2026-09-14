using System;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.InputSystem;

namespace ReignBeta.UI.Widgets
{
    public class ReignAutoScrollPanel : ScrollablePanel
    {
        // Bannerlord may report a full platform wheel delta (for example 120)
        // rather than a normalized notch. Apply exactly one small text-line
        // step per callback so chat can be positioned precisely.
        private const float MouseWheelStep = 18f;
        private int _chatScrollVersion;
        private int _pendingScrollFrames;

        public ReignAutoScrollPanel(UIContext context) : base(context)
        {
        }

        protected override void OnMouseScroll()
        {
            if (VerticalScrollbar == null)
            {
                base.OnMouseScroll();
                return;
            }

            float rawDelta = Context.EventManager.DeltaMouseScroll;
            if (Math.Abs(rawDelta) < float.Epsilon) return;
            float current = VerticalScrollbar.ValueFloat;
            float direction = rawDelta > 0f ? 1f : -1f;
            float target = Math.Max(0f,
                Math.Min(VerticalScrollbar.MaxValue,
                    current - direction * MouseWheelStep));
            VerticalScrollbar.ValueFloat = target;
            SetVerticalScrollTarget(target, 0f);
        }

        public int ChatScrollVersion
        {
            get { return _chatScrollVersion; }
            set
            {
                if (value != _chatScrollVersion)
                {
                    _chatScrollVersion = value;
                    _pendingScrollFrames = 4;
                }
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);

            if (_pendingScrollFrames <= 0 || VerticalScrollbar == null)
            {
                return;
            }

            _pendingScrollFrames--;
            float maxValue = VerticalScrollbar.MaxValue;
            VerticalScrollbar.ValueFloat = maxValue;
            SetVerticalScrollTarget(maxValue, 0f);
        }
    }

}
