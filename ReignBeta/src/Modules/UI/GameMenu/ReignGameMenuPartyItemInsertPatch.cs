using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace ReignBeta.UI.GameMenu
{
    [PrefabExtension("GameMenuPartyItem", "descendant::ImageIdentifierWidget[@Id='CharacterImage']/..")]
    internal sealed class ReignGameMenuPartyItemInsertPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document;

        public override InsertType Type => InsertType.Child;

        // Native children are background, banner, portrait, then quest markers.
        // Keep the native button and its event ownership intact. Use the dedicated
        // party-wide derivative at the native 117x85 portrait aspect so the head and
        // upper chest retain their authored scale beneath the banner and aperture.
        public override int Index => 3;

        public ReignGameMenuPartyItemInsertPatch()
        {
            _document = new XmlDocument();
            _document.LoadXml(
                "<Widget Id=\"ReignGameMenuPartyPortraitLayer\" " +
                "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "ClipContents=\"true\" DoNotAcceptEvents=\"true\">" +
                "<Children>" +
                "<ReignPortraitWidget Id=\"ReignGameMenuPartyPortrait\" Type=\"ReignBeta.UI.Widgets.ReignPortraitWidget\" " +
                "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "PortraitCacheKey=\"@ReignPortraitCacheKey\" TargetAspect=\"1.3764706\" UsePartyThumbnail=\"true\" " +
                "IsVisible=\"@IsReignPortraitAvailable\" DoNotAcceptEvents=\"true\" />" +
                "<ImageWidget Id=\"ReignGameMenuPartyPortraitAperturePlate\" " +
                "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "Sprite=\"reign_modern_portrait_rectangle_overlay\" DoNotAcceptEvents=\"true\" />" +
                "<MaskedTextureWidget DataSource=\"{Banner_9}\" " +
                "WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
                "SuggestedWidth=\"!Banner.Width.Scaled\" SuggestedHeight=\"!Banner.Height.Scaled\" " +
                "HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" " +
                "MarginTop=\"1\" MarginRight=\"3\" Brush=\"Flat.Tuple.Banner.Small.Hero\" " +
                "AdditionalArgs=\"@AdditionalArgs\" ImageId=\"@Id\" " +
                "TextureProviderName=\"@TextureProviderName\" IsDisabled=\"true\" />" +
                "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
                "SuggestedWidth=\"36\" SuggestedHeight=\"36\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Top\" " +
                "MarginLeft=\"5\" MarginTop=\"5\" Brush=\"Info.Text\" Brush.Font=\"Galahad\" Brush.FontSize=\"20\" " +
                "Brush.FontColor=\"#C5AC83FF\" " +
                "Brush.TextHorizontalAlignment=\"Center\" Text=\"@ReignRelationText\" " +
                "IsVisible=\"@ReignIsRelationHidden\" DoNotAcceptEvents=\"true\" />" +
                "</Children>" +
                "</Widget>");
        }

        [PrefabExtensionXmlNode(false)]
        public XmlNode GetPatchContent()
        {
            return _document.DocumentElement;
        }
    }
}
