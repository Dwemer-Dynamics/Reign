using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.ScreenSystem;

namespace ReignBeta.UI
{
    internal static class ReignScreenLayers
    {
        internal static bool SafeHasLayer(ScreenBase host, GauntletLayer layer)
        {
            try { return host?.HasLayer(layer) == true; }
            catch { return false; }
        }
    }
}
