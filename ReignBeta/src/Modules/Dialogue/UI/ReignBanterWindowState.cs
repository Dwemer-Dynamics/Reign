using System;

namespace ReignBeta.UI
{
    // Logical Gauntlet coordinates; no game, provider or campaign state.
    internal sealed class ReignBanterWindowState
    {
        internal const float BarHeight = 42f;
        private float _viewportWidth, _viewportHeight, _startX, _startY, _startLeft, _startTop, _startWidth, _startHeight;
        private bool _resize, _positionCustomized;
        internal float Left { get; private set; }
        internal float Top { get; private set; }
        internal float Width { get; private set; } = 420f;
        internal float ExpandedHeight { get; private set; } = 220f;
        internal bool Collapsed { get; private set; }
        internal bool IsManipulating { get; private set; }
        internal float Height => Collapsed ? BarHeight : ExpandedHeight;

        internal ReignBanterWindowState(float viewportWidth, float viewportHeight)
        {
            _viewportWidth = Positive(viewportWidth, 1672f);
            _viewportHeight = Positive(viewportHeight, 941f);
            Left = _viewportWidth - Width - 18f;
            Top = _viewportHeight - ExpandedHeight - 122f;
            Clamp();
        }

        internal bool SetViewport(float width, float height)
        {
            width = Positive(width, _viewportWidth); height = Positive(height, _viewportHeight);
            if (width == _viewportWidth && height == _viewportHeight) return false;
            EndManipulation();
            if (_positionCustomized) { Left *= width / _viewportWidth; Top *= height / _viewportHeight; }
            else { Left = width - Width - 18f; Top = height - Height - 122f; }
            _viewportWidth = width; _viewportHeight = height;
            Clamp(); return true;
        }

        internal void Restore(ReignBanterWindowPreferences preferences)
        {
            if (preferences == null || preferences.Schema != 1) return;
            Width = Positive(preferences.Width, 420f);
            ExpandedHeight = Positive(preferences.Height, 220f);
            Collapsed = preferences.Collapsed;
            _positionCustomized = Finite(preferences.LeftRatio) && Finite(preferences.TopRatio);
            if (Finite(preferences.LeftRatio)) Left = Math.Max(0f, Math.Min(1f, preferences.LeftRatio)) * _viewportWidth;
            if (Finite(preferences.TopRatio)) Top = Math.Max(0f, Math.Min(1f, preferences.TopRatio)) * _viewportHeight;
            Clamp();
        }

        internal ReignBanterWindowPreferences Capture() => new ReignBanterWindowPreferences {
            Schema = 1, LeftRatio = Left / _viewportWidth, TopRatio = Top / _viewportHeight,
            Width = Width, Height = ExpandedHeight, Collapsed = Collapsed
        };

        internal void ToggleCollapsed() { EndManipulation(); Collapsed = !Collapsed; _positionCustomized = true; Clamp(); }

        internal bool BeginManipulation(float x, float y, bool resize)
        {
            if (!Finite(x) || !Finite(y) || (resize && Collapsed)) return false;
            _startX = x; _startY = y; _startLeft = Left; _startTop = Top;
            _startWidth = Width; _startHeight = ExpandedHeight;
            _resize = resize; IsManipulating = true; return true;
        }

        internal bool MovePointer(float x, float y)
        {
            if (!IsManipulating || !Finite(x) || !Finite(y)) return false;
            float oldLeft = Left, oldTop = Top, oldWidth = Width, oldHeight = ExpandedHeight;
            if (_resize)
            {
                Width = Math.Min(_viewportWidth - Left - 8f, _startWidth + x - _startX);
                ExpandedHeight = Math.Min(_viewportHeight - Top - 8f, _startHeight + y - _startY);
            }
            else { Left = _startLeft + x - _startX; Top = _startTop + y - _startY; }
            Clamp();
            bool changed = oldLeft != Left || oldTop != Top || oldWidth != Width || oldHeight != ExpandedHeight;
            if (changed) _positionCustomized = true;
            return changed;
        }

        internal void EndManipulation() { IsManipulating = false; }
        internal bool CapturesPointer(float x, float y) => IsManipulating ||
            (Finite(x) && Finite(y) && x >= Left && y >= Top && x < Left + Width && y < Top + Height);

        private void Clamp()
        {
            float availableWidth = Math.Max(1f, _viewportWidth - 16f), availableHeight = Math.Max(1f, _viewportHeight - 16f);
            Width = Bound(Width, Math.Min(320f, availableWidth), Math.Min(1000f, availableWidth));
            ExpandedHeight = Bound(ExpandedHeight, Math.Min(200f, availableHeight), Math.Min(700f, availableHeight));
            Left = Bound(Left, Math.Min(8f, _viewportWidth - Width), Math.Max(0f, _viewportWidth - Width - 8f));
            Top = Bound(Top, Math.Min(8f, _viewportHeight - Height), Math.Max(0f, _viewportHeight - Height - 8f));
        }
        private static float Bound(float value, float min, float max) => Math.Max(min, Math.Min(Math.Max(min, max), value));
        private static float Positive(float value, float fallback) => Finite(value) && value > 0f ? value : fallback;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    internal sealed class ReignBanterWindowPreferences
    {
        public int Schema { get; set; } = 1;
        public float LeftRatio { get; set; } = float.NaN;
        public float TopRatio { get; set; } = float.NaN;
        public float Width { get; set; } = 420f;
        public float Height { get; set; } = 220f;
        public bool Collapsed { get; set; }
    }
}
