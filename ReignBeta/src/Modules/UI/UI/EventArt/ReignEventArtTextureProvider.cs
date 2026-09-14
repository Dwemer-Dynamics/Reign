using TaleWorlds.GauntletUI;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.EventArt
{
    public sealed class ReignEventArtTextureProvider : TextureProvider
    {
        private readonly string _eventImageId;

        public ReignEventArtTextureProvider(string eventImageId)
        {
            _eventImageId = eventImageId;
        }

        protected override Texture OnGetTextureForRender(TwoDimensionContext twoDimensionContext, string name)
        {
            return ReignEventArtTextureFactory.GetOrBuild(_eventImageId);
        }
    }
}
