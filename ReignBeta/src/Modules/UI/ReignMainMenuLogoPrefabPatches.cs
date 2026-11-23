using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace ReignBeta.UI
{
    [PrefabExtension("InitialScreen", "descendant::BrushWidget[@Brush='InitialMenu.Logo.WarSails']")]
    internal sealed class ReignHideWarSailsMainMenuLogoPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("IsVisible", "false"),
            new Attribute("IsHidden", "true")
        };
    }

    [PrefabExtension("InitialScreen", "descendant::BrushWidget[@Brush='InitialMenu.Logo']")]
    internal sealed class ReignMainMenuLogoReplacePatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document;

        public override InsertType Type => InsertType.Replace;

        public ReignMainMenuLogoReplacePatch()
        {
            _document = new XmlDocument();
            _document.LoadXml(
                "<ReignEventArtWidget Id=\"ReignMainMenuLogo\" " +
                "WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
                "SuggestedWidth=\"360\" SuggestedHeight=\"221\" " +
                "HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" " +
                "MarginTop=\"32\" EventImageId=\"reigneventart|main_menu|logo\" " +
                "DoNotAcceptEvents=\"true\" RenderLate=\"true\" " +
                "ForcePixelPerfectRenderPlacement=\"true\" />");
        }

        [PrefabExtensionXmlNode(false)]
        public XmlNode GetPatchContent()
        {
            return _document.DocumentElement;
        }
    }
}
