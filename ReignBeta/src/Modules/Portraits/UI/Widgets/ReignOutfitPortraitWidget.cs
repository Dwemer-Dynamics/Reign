using System.Reflection;
using AIPortraits;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Widgets
{
    // Preview-only provider; never publishes into the hero's portrait cache.
    public sealed class ReignOutfitPortraitWidget : TextureWidget
    {
        private static readonly PropertyInfo ProviderProperty = typeof(TextureWidget).GetProperty("TextureProvider", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private string _imagePath;
        public ReignOutfitPortraitWidget(UIContext context) : base(context) { }
        public string ImagePath
        {
            get => _imagePath;
            set { if (_imagePath == value) return; _imagePath = value; ProviderProperty?.SetValue(this, new OutfitFileProvider(value), null); }
        }
        protected override void OnRender(TwoDimensionContext context, TwoDimensionDrawContext draw)
        {
            if (string.IsNullOrWhiteSpace(_imagePath)) return;
            base.OnRender(context, draw);
        }
        private sealed class OutfitFileProvider : TextureProvider
        {
            private readonly string _path;
            public OutfitFileProvider(string path) { _path = path; }
            protected override Texture OnGetTextureForRender(TwoDimensionContext context, string name) => TextureFactory.GetOrBuildFile(_path, _path);
        }
    }
}
