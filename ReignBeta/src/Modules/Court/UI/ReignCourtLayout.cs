using System;

namespace ReignBeta.UI
{
    // Uniform authored-space fit; keep these constants in parity with court-layout.js.
    public static class ReignCourtLayout
    {
        public const float Width = 1518f;
        public const float Height = 854f;
        public const float TopHudInset = 150f;
        public const float BottomHudInset = 52f;
        public const float SideInset = 24f;

        public static (float ScaleModifier, float TopInset) ComputeNativeLayout(
            float width, float height, float nativeBaseScale)
        {
            var fit = Compute(width, height);
            // UIContext.CustomScale is UIContext.Scale * ScaleModifier. Its
            // actual base includes Gauntlet's narrow-aspect correction; neither
            // height/1080 nor the player's layer scale is that base.
            float modifier = fit.Scale / nativeBaseScale;
            if (float.IsNaN(modifier) || float.IsInfinity(modifier)
                || nativeBaseScale <= 0f || modifier <= 0f)
                modifier = 1f;
            return (modifier, fit.TopInset);
        }

        public static (float Scale, float TopInset) Compute(float width, float height)
        {
            if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0f
                || float.IsNaN(height) || float.IsInfinity(height) || height <= 0f)
                return (1f, TopHudInset);

            // Same narrow-aspect base scale as Gauntlet; the HUD reservations
            // remain in base units while the entire court grows into free space.
            float hudScale = Math.Min(height / 1080f, width / (1920f * 0.98f));
            float scale = Math.Min((width - 2f * SideInset * hudScale) / Width,
                (height - (TopHudInset + BottomHudInset) * hudScale) / Height);
            return (scale, TopHudInset * hudScale / scale);
        }
    }
}
