using AIPortraits;
using TaleWorlds.GauntletUI;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Portraits
{
    public sealed class ReignPortraitTextureProvider : TextureProvider
    {
        private readonly string _cacheKey;
        private readonly float _targetAspect;
        private readonly PortraitQualityTier _qualityTier;

        public ReignPortraitTextureProvider(string cacheKey, float targetAspect, bool useZoomQuality)
            : this(cacheKey, targetAspect, useZoomQuality ? PortraitQualityTier.Portrait : PortraitQualityTier.Thumbnail)
        {
        }

        public ReignPortraitTextureProvider(string cacheKey, float targetAspect, PortraitQualityTier qualityTier)
        {
            _cacheKey = cacheKey;
            _targetAspect = targetAspect;
            _qualityTier = qualityTier;
        }

        protected override Texture OnGetTextureForRender(TwoDimensionContext twoDimensionContext, string name)
        {
            if (!TextureFactory.Has(_cacheKey))
            {
                return null;
            }

            // Normalize the rectangular source to the destination slot before
            // Gauntlet renders it so wide and square native thumbnails cannot
            // stretch the portrait. Zoom/full-body portraits use contain mode to
            // preserve the entire composition; aperture plates still own the
            // visible circle or oval edge.
            return TextureFactory.GetOrBuildForAspect(
                _cacheKey,
                _targetAspect,
                contain: _qualityTier == PortraitQualityTier.Zoom,
                _qualityTier);
        }
    }
}
