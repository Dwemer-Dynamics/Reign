using System;

namespace ReignBeta.UI
{
    // Campaign preparation can finish before Bannerlord has rendered its map.
    // Taking focus earlier prevents the native map from clearing global loading.
    internal static class ReignPlayerApproachPresentationPolicy
    {
        internal static bool CanPresent(bool nativeMapActive, bool nativeMapReady, bool globalLoadingActive)
            => nativeMapActive && nativeMapReady && !globalLoadingActive;

        internal static bool TryPresent(bool nativeMapActive, bool nativeMapReady, bool globalLoadingActive, Action present)
        {
            if (!CanPresent(nativeMapActive, nativeMapReady, globalLoadingActive)) return false;
            present();
            return true;
        }
    }
}
