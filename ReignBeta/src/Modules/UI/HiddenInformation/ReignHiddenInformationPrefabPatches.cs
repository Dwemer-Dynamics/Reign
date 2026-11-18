using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace ReignBeta.UI.HiddenInformation
{
    [PrefabExtension("MarriageOfferPopup", "descendant::RelationTextWidget[@Amount='@Relation' and @DataSource='{OffereeClanMember}']")]
    internal sealed class ReignMarriageOffereeRelationPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<Widget DataSource=\"{OffereeClanMember}\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"150\" SuggestedHeight=\"50\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" PositionYOffset=\"10\">" +
            "<Children>" +
            "<RelationTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Clan.MarriagePopup.Paragraph.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5BDAFFF\" Brush.TextHorizontalAlignment=\"Right\" ZeroColor=\"#C5BDAFFF\" PositiveColor=\"#C5AC83FF\" NegativeColor=\"#7E6A4DFF\" Amount=\"@Relation\" IsVisible=\"@ReignIsRelationVisible\"/>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Clan.MarriagePopup.Paragraph.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5BDAFFF\" Brush.TextHorizontalAlignment=\"Right\" Text=\"\" IsVisible=\"@ReignIsRelationHidden\"/>" +
            "</Children></Widget>");
        public override InsertType Type => InsertType.Replace;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("MarriageOfferPopup", "descendant::RelationTextWidget[@Amount='@Relation' and @DataSource='{OffererClanMember}']")]
    internal sealed class ReignMarriageOffererRelationPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<Widget DataSource=\"{OffererClanMember}\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"150\" SuggestedHeight=\"50\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" PositionYOffset=\"10\">" +
            "<Children>" +
            "<RelationTextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Clan.MarriagePopup.Paragraph.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5BDAFFF\" Brush.TextHorizontalAlignment=\"Left\" ZeroColor=\"#C5BDAFFF\" PositiveColor=\"#C5AC83FF\" NegativeColor=\"#7E6A4DFF\" Amount=\"@Relation\" IsVisible=\"@ReignIsRelationVisible\"/>" +
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Brush=\"Clan.MarriagePopup.Paragraph.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5BDAFFF\" Brush.TextHorizontalAlignment=\"Left\" Text=\"\" IsVisible=\"@ReignIsRelationHidden\"/>" +
            "</Children></Widget>");
        public override InsertType Type => InsertType.Replace;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("RecruitVolunteerTuple", "descendant::GameMenuPartyItemButtonWidget[@Id='PartyItemWidget']/Children")]
    internal sealed class ReignRecruitRelationUnknownPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"40\" SuggestedHeight=\"40\" " +
            "HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" MarginRight=\"5\" MarginTop=\"5\" " +
            "Brush=\"EncounterTextBrush\" Brush.Font=\"Galahad\" Brush.FontSize=\"24\" Brush.FontColor=\"#8A8883FF\" Brush.TextHorizontalAlignment=\"Center\" " +
            "Text=\"@ReignRelationText\" IsVisible=\"@ReignIsRelationHidden\" DoNotAcceptEvents=\"true\"/>");
        public override InsertType Type => InsertType.Child;
        public override int Index => 1000;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    internal static class ReignPrefabXml
    {
        public static XmlDocument Load(string xml)
        {
            XmlDocument document = new XmlDocument();
            document.LoadXml(xml);
            return document;
        }
    }
}
