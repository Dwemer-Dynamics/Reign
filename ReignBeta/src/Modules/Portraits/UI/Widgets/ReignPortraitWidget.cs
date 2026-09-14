using System.Reflection;
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
        private bool _loadedUseZoomQuality;
        private bool _loadedUsePartyThumbnail;
        private bool _loadedUseFullBody;

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
                    RefreshTexture();
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
                    RefreshTexture();
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
                    RefreshTexture();
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
                    RefreshTexture();
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
                    RefreshTexture();
                }
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (_loadedRevision != TextureFactory.PortraitRevision || _loadedCacheKey != _portraitCacheKey || _loadedAspect != _targetAspect
                || _loadedUseZoomQuality != _useZoomQuality || _loadedUsePartyThumbnail != _usePartyThumbnail
                || _loadedUseFullBody != _useFullBody || Texture == null)
            {
                RefreshTexture();
            }
        }

        protected override void OnRender(TwoDimensionContext twoDimensionContext, TwoDimensionDrawContext drawContext)
        {
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
                || _loadedUseFullBody != _useFullBody || Texture == null)
            {
                RefreshTexture();
            }

            base.OnRender(twoDimensionContext, drawContext);
        }

        private int _loadedRevision = -1;

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
                        : _useZoomQuality ? AIPortraits.PortraitQualityTier.Portrait : AIPortraits.PortraitQualityTier.Thumbnail),
                null);
            _loadedCacheKey = _portraitCacheKey;
            _loadedRevision = TextureFactory.PortraitRevision;
            _loadedAspect = _targetAspect;
            _loadedUseZoomQuality = _useZoomQuality;
            _loadedUsePartyThumbnail = _usePartyThumbnail;
            _loadedUseFullBody = _useFullBody;
        }
    }
}
