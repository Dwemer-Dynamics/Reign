using System.Reflection;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Tavern
{
    public sealed class ReignTavernArtWidget : TextureWidget
    {
        private static readonly PropertyInfo TextureProviderProperty =
            typeof(TextureWidget).GetProperty(
                "TextureProvider",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private string _tavernImageId;
        private string _loadedTavernImageId;

        public ReignTavernArtWidget(UIContext context) : base(context)
        {
        }

        public string TavernImageId
        {
            get { return _tavernImageId; }
            set
            {
                if (value != _tavernImageId)
                {
                    _tavernImageId = value;
                    _loadedTavernImageId = null;
                    RefreshTexture();
                }
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (_loadedTavernImageId != _tavernImageId || Texture == null)
            {
                RefreshTexture();
            }
        }

        protected override void OnRender(TwoDimensionContext twoDimensionContext, TwoDimensionDrawContext drawContext)
        {
            if (_loadedTavernImageId != _tavernImageId || Texture == null)
            {
                RefreshTexture();
            }

            base.OnRender(twoDimensionContext, drawContext);
        }

        private void RefreshTexture()
        {
            if (string.IsNullOrWhiteSpace(_tavernImageId))
            {
                return;
            }

            TextureProviderProperty?.SetValue(this, new ReignTavernArtTextureProvider(_tavernImageId), null);
            _loadedTavernImageId = _tavernImageId;
        }
    }
}
