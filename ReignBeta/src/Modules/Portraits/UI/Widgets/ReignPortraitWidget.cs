using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AIPortraits;
using ReignBeta.UI.Portraits;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Widgets
{
    /// <summary>
    /// Renders an AI portrait by stable hero cache key. The ordinary character
    /// image widget remains underneath it as a native fallback.
    /// </summary>
    public sealed class ReignPortraitWidget : TextureWidget
    {
        private static readonly PropertyInfo TextureProviderProperty =
            typeof(TextureWidget).GetProperty("TextureProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private string _portraitCacheKey;
        private string _loadedCacheKey;
        private float _targetAspect = 1f;
        private float _loadedAspect;
        private bool _useZoomQuality;
        private bool _usePartyThumbnail;
        private bool _useFullBody;
        private bool _useFramedBust;
        private bool _loadedUseZoomQuality;
        private bool _loadedUsePartyThumbnail;
        private bool _loadedUseFullBody;
        private bool _loadedUseFramedBust;
        private static readonly ConditionalWeakTable<UIContext, CancellationTokenSource> ThumbnailOwners
            = new ConditionalWeakTable<UIContext, CancellationTokenSource>();
        private Task<Texture> _thumbnailPending;
        private Texture _thumbnailTexture;
        private string _thumbnailKey;
        private int _thumbnailRevision = -1;
        private float _thumbnailRetrySeconds;

        // Correspondence opts into a cached AI-only, render-visible path. Other
        // portrait surfaces retain their existing quality and fallback behavior.
        public bool VisibleThumbnailOnly { get; set; }

        public static void CancelThumbnailLoads(UIContext context)
        {
            if (context != null && ThumbnailOwners.TryGetValue(context, out var cancellation))
                cancellation.Cancel();
        }

        public ReignPortraitWidget(UIContext context) : base(context)
        {
        }

        public string PortraitCacheKey
        {
            get { return _portraitCacheKey; }
            set
            {
                if (value != _portraitCacheKey)
                {
                    _portraitCacheKey = value;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        public float TargetAspect
        {
            get { return _targetAspect; }
            set
            {
                float normalized = value > 0.01f ? value : 1f;
                if (normalized != _targetAspect)
                {
                    _targetAspect = normalized;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        public bool UseZoomQuality
        {
            get { return _useZoomQuality; }
            set
            {
                if (value != _useZoomQuality)
                {
                    _useZoomQuality = value;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        public bool UsePartyThumbnail
        {
            get { return _usePartyThumbnail; }
            set
            {
                if (value != _usePartyThumbnail)
                {
                    _usePartyThumbnail = value;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        public bool UseFullBody
        {
            get { return _useFullBody; }
            set
            {
                if (value != _useFullBody)
                {
                    _useFullBody = value;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        public bool UseFramedBust
        {
            get { return _useFramedBust; }
            set
            {
                if (value != _useFramedBust)
                {
                    _useFramedBust = value;
                    _loadedCacheKey = null;
                    InvalidateThumbnail();
                }
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (VisibleThumbnailOnly)
            {
                _thumbnailRetrySeconds = Math.Max(0f, _thumbnailRetrySeconds - dt);
                return;
            }
            if (_loadedRevision != TextureFactory.PortraitRevision || _loadedCacheKey != _portraitCacheKey || _loadedAspect != _targetAspect
                || _loadedUseZoomQuality != _useZoomQuality || _loadedUsePartyThumbnail != _usePartyThumbnail
                || _loadedUseFullBody != _useFullBody || _loadedUseFramedBust != _useFramedBust || Texture == null)
            {
                RefreshTexture();
            }
        }

        protected override void OnRender(TwoDimensionContext twoDimensionContext, TwoDimensionDrawContext drawContext)
        {
            if (VisibleThumbnailOnly)
            {
                RenderVisibleThumbnail(twoDimensionContext, drawContext);
                return;
            }
            // TextureWidget asks the engine resource depot to load its texture name
            // during base rendering. A visible portrait widget with no available AI
            // portrait has neither a sprite name nor a valid provider texture, and
            // Bannerlord's native renderer does not tolerate that empty state.
            // Leave the native ImageIdentifierWidget underneath as the fallback.
            if (string.IsNullOrWhiteSpace(_portraitCacheKey) || !TextureFactory.Has(_portraitCacheKey))
            {
                return;
            }

            if (_loadedRevision != TextureFactory.PortraitRevision || _loadedCacheKey != _portraitCacheKey || _loadedAspect != _targetAspect
                || _loadedUseZoomQuality != _useZoomQuality || _loadedUsePartyThumbnail != _usePartyThumbnail
                || _loadedUseFullBody != _useFullBody || _loadedUseFramedBust != _useFramedBust || Texture == null)
            {
                RefreshTexture();
            }

            base.OnRender(twoDimensionContext, drawContext);
        }

        private int _loadedRevision = -1;

        private void InvalidateThumbnail()
        {
            _thumbnailPending = null;
            _thumbnailTexture = null;
            _thumbnailKey = null;
            _thumbnailRetrySeconds = 0f;
        }

        private void RenderVisibleThumbnail(TwoDimensionContext context, TwoDimensionDrawContext drawContext)
        {
            if (string.IsNullOrWhiteSpace(_portraitCacheKey) || !IsInsideClipViewport()) return;
            var owner = ThumbnailOwners.GetValue(Context, _ => new CancellationTokenSource());
            if (owner.IsCancellationRequested) return;
            if (_thumbnailKey != _portraitCacheKey || _thumbnailRevision != TextureFactory.PortraitRevision)
            {
                InvalidateThumbnail();
                _thumbnailKey = _portraitCacheKey;
                _thumbnailRevision = TextureFactory.PortraitRevision;
            }
            if (_thumbnailPending != null && _thumbnailPending.IsCompleted)
            {
                _thumbnailTexture = _thumbnailPending.GetAwaiter().GetResult();
                _thumbnailPending = null;
                _thumbnailRetrySeconds = 1f;
                if (_thumbnailTexture != null)
                    TextureProviderProperty?.SetValue(this, new CachedThumbnailProvider(_thumbnailTexture), null);
            }
            // An old off-screen row may outlive the bounded texture LRU. Do not
            // render a released texture when that row returns to the viewport.
            if (!TextureFactory.TryGetThumbnail(_portraitCacheKey, _targetAspect, out var current))
                _thumbnailTexture = null;
            else if (!ReferenceEquals(current, _thumbnailTexture))
            {
                _thumbnailTexture = current;
                TextureProviderProperty?.SetValue(this, new CachedThumbnailProvider(current), null);
            }
            if (_thumbnailTexture == null)
            {
                if (_thumbnailPending == null && _thumbnailRetrySeconds <= 0f)
                    _thumbnailPending = TextureFactory.GetOrBuildThumbnailAsync(_portraitCacheKey, _targetAspect, owner.Token);
                return;
            }
            base.OnRender(context, drawContext);
        }

        private bool IsInsideClipViewport()
        {
            if (!IsVisible || !IsRecursivelyVisible() || ParentWidget == null || Size.X <= 0f || Size.Y <= 0f) return false;
            for (Widget ancestor = ParentWidget; ancestor != null; ancestor = ancestor.ParentWidget)
                if (ancestor.ClipContents && !Intersects(GlobalPosition.X, GlobalPosition.Y, Size.X, Size.Y,
                    ancestor.GlobalPosition.X, ancestor.GlobalPosition.Y, ancestor.Size.X, ancestor.Size.Y)) return false;
            return true;
        }

        internal static bool Intersects(float x, float y, float width, float height,
            float clipX, float clipY, float clipWidth, float clipHeight)
        {
            return width > 0f && height > 0f && clipWidth > 0f && clipHeight > 0f
                && x < clipX + clipWidth && x + width > clipX
                && y < clipY + clipHeight && y + height > clipY;
        }

        private sealed class CachedThumbnailProvider : TextureProvider
        {
            private readonly Texture _texture;
            public CachedThumbnailProvider(Texture texture) { _texture = texture; }
            protected override Texture OnGetTextureForRender(TwoDimensionContext context, string name) => _texture;
        }

        private void RefreshTexture()
        {
            if (string.IsNullOrWhiteSpace(_portraitCacheKey) || !TextureFactory.Has(_portraitCacheKey))
            {
                return;
            }

            TextureProviderProperty?.SetValue(
                this,
                new ReignPortraitTextureProvider(
                    _portraitCacheKey,
                    _targetAspect,
                    _useFullBody ? AIPortraits.PortraitQualityTier.Zoom
                        : _usePartyThumbnail ? AIPortraits.PortraitQualityTier.PartyThumbnail
                        : _useZoomQuality ? AIPortraits.PortraitQualityTier.Portrait : AIPortraits.PortraitQualityTier.Thumbnail,
                    _useFramedBust),
                null);
            _loadedCacheKey = _portraitCacheKey;
            _loadedRevision = TextureFactory.PortraitRevision;
            _loadedAspect = _targetAspect;
            _loadedUseZoomQuality = _useZoomQuality;
            _loadedUsePartyThumbnail = _usePartyThumbnail;
            _loadedUseFullBody = _useFullBody;
            _loadedUseFramedBust = _useFramedBust;
        }
    }
}
