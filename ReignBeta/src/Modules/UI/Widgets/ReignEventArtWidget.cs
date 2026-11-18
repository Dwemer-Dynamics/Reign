using System.Reflection;
using ReignBeta.UI.EventArt;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Widgets
{
    public sealed class ReignEventArtWidget : TextureWidget
    {
        private static readonly PropertyInfo TextureProviderProperty =
            typeof(TextureWidget).GetProperty("TextureProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private string _eventImageId;
        private string _loadedEventImageId;

        public ReignEventArtWidget(UIContext context) : base(context)
        {
        }

        public string EventImageId
        {
            get { return _eventImageId; }
            set
            {
                if (value != _eventImageId)
                {
                    _eventImageId = value;
                    _loadedEventImageId = null;
                    RefreshTexture();
                }
            }
        }

        protected override void OnLateUpdate(float dt)
        {
            base.OnLateUpdate(dt);
            if (_loadedEventImageId != _eventImageId)
            {
                RefreshTexture();
            }
        }

        protected override void OnRender(TwoDimensionContext twoDimensionContext, TwoDimensionDrawContext drawContext)
        {
            if (_loadedEventImageId != _eventImageId)
            {
                RefreshTexture();
            }

            base.OnRender(twoDimensionContext, drawContext);
        }

        private void RefreshTexture()
        {
            if (string.IsNullOrWhiteSpace(_eventImageId))
            {
                return;
            }

            TextureProviderProperty?.SetValue(this, new ReignEventArtTextureProvider(_eventImageId), null);
            _loadedEventImageId = _eventImageId;
        }
    }
}
