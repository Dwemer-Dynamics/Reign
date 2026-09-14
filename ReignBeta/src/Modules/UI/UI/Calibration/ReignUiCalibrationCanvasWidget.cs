using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;

namespace ReignBeta.UI.Calibration
{
    /// <summary>
    /// Full-surface pointer receiver for the debug-only live calibration overlay.
    /// It selects widgets from the separately loaded target movie, so the overlay
    /// never has to modify production prefab structure to inspect it.
    /// </summary>
    public sealed class ReignUiCalibrationCanvasWidget : Widget
    {
        public ReignUiCalibrationCanvasWidget(UIContext context) : base(context)
        {
        }

        protected override void OnMousePressed()
        {
            base.OnMousePressed();
            ReignUiCalibrationService.PointerPressed(Context);
        }

        protected override void OnMouseMove()
        {
            base.OnMouseMove();
            ReignUiCalibrationService.PointerMoved(Context);
        }

        protected override void OnMouseReleased(bool isFromInput)
        {
            base.OnMouseReleased(isFromInput);
            ReignUiCalibrationService.PointerReleased(Context);
        }
    }
}
