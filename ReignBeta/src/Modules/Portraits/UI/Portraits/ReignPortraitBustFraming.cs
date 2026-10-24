using System;

namespace ReignBeta.UI.Portraits
{
    // A presentation-only crop of the full portrait. Shipped originals and their
    // ordinary chest/thumbnail derivatives are never rewritten for this view.
    public static class ReignPortraitBustFraming
    {
        public struct Crop
        {
            public int X, Y, Width, Height;
        }

        public static Crop Calculate(int sourceWidth, int sourceHeight, double aspect,
            double faceX, double faceY, double faceWidth, double faceHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0 || !Finite(aspect) || aspect <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            bool focused = Finite(faceX) && Finite(faceY) && Finite(faceWidth) && Finite(faceHeight)
                && faceX >= 0 && faceY >= 0 && faceWidth > 0 && faceHeight > 0
                && faceX + faceWidth <= 1.0001 && faceY + faceHeight <= 1.0001;
            double facePixels = focused ? faceHeight * sourceHeight : 0;
            int desiredHeight = focused ? Round(facePixels * 3.6) : sourceHeight;
            int height = Math.Max(1, Math.Min(sourceHeight,
                Math.Min(desiredHeight, Math.Max(1, (int)Math.Floor(sourceWidth / aspect)))));
            int width = Math.Max(1, Math.Min(sourceWidth, Round(height * aspect)));
            double centerX = focused ? (faceX + faceWidth * .5) * sourceWidth : sourceWidth * .5;
            return new Crop
            {
                X = Clamp(Round(centerX - width * .5), 0, sourceWidth - width),
                Y = focused ? Clamp(Round(faceY * sourceHeight - facePixels * .85), 0, sourceHeight - height) : 0,
                Width = width,
                Height = height
            };
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
        private static int Clamp(int value, int minimum, int maximum) => Math.Max(minimum, Math.Min(maximum, value));
    }
}
