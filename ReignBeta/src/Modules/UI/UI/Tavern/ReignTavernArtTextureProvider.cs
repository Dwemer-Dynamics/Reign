using TaleWorlds.GauntletUI;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Tavern
{
    public sealed class ReignTavernArtTextureProvider : TextureProvider
    {
        private readonly string _imageId;

        public ReignTavernArtTextureProvider(string imageId)
        {
            _imageId = imageId;
        }

        protected override Texture OnGetTextureForRender(TwoDimensionContext twoDimensionContext, string name)
        {
            return ReignTavernArtTextureFactory.GetOrBuild(_imageId);
        }
    }
}
