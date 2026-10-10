using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ReignBeta.UI.Widgets
{
    public sealed class ReignBanterWindowWidget : Widget
    {
        public ReignBanterWindowWidget(UIContext context) : base(context) { }
        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            ReignBanterPanel.UpdatePointerInput();
        }
    }

    public sealed class ReignBanterWindowHandleWidget : Widget
    {
        public ReignBanterWindowHandleWidget(UIContext context) : base(context) { }
        public bool Resize { get; set; }
        protected override void OnMousePressed()
        {
            base.OnMousePressed(); ReignBanterPanel.BeginManipulation(Resize);
        }
        protected override void OnMouseReleased(bool isFromInput)
        {
            base.OnMouseReleased(isFromInput); ReignBanterPanel.EndManipulation();
        }
    }
}
