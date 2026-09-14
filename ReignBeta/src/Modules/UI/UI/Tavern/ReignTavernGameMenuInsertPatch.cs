using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace ReignBeta.UI.Tavern
{
    [PrefabExtension("GameMenu", "descendant::Widget[@Id='Overlay']/..")]
    internal sealed class ReignTavernGameMenuInsertPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document;

        public override InsertType Type => InsertType.Child;
        public override int Index => 10000;

        public ReignTavernGameMenuInsertPatch()
        {
            _document = new XmlDocument();
            _document.LoadXml(
                "<Widget Id=\"ReignTavernArtPanel\" " +
                "WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
                "SuggestedWidth=\"780\" SuggestedHeight=\"780\" " +
                "HorizontalAlignment=\"Right\" VerticalAlignment=\"Center\" " +
                "MarginRight=\"95\" IsVisible=\"@IsReignTavernArtVisible\" " +
                "DoNotAcceptEvents=\"true\">" +
                "<Children>" +
                "<ReignTavernArtWidget Id=\"ReignTavernScene\" " +
                "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "MarginLeft=\"55\" MarginRight=\"55\" MarginTop=\"55\" MarginBottom=\"55\" " +
                "TavernImageId=\"@ReignTavernSceneImageId\" DoNotAcceptEvents=\"true\" />" +
                "<ReignTavernArtWidget Id=\"ReignTavernFrame\" " +
                "WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
                "TavernImageId=\"@ReignTavernFrameImageId\" DoNotAcceptEvents=\"true\" RenderLate=\"true\" />" +
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
