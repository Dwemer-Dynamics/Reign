using System.Collections.Generic;
using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;

namespace ReignBeta.UI.HiddenInformation
{
    [PrefabExtension("EncyclopediaHeroPage", "descendant::TextWidget[@IntText='@SkillValue']")]
    internal sealed class ReignEncyclopediaSkillTextPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"20\" " +
            "Text=\"@ReignSkillValueText\" VerticalAlignment=\"Bottom\" HorizontalAlignment=\"Center\" " +
            "Brush=\"Encyclopedia.Skill.Text\" Brush.Font=\"Galahad\" Brush.FontSize=\"20\" Brush.FontColor=\"#C5AC83FF\" PositionYOffset=\"20\" ClipContents=\"false\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("ClanMembers", "descendant::TextWidget[@IntText='@SkillValue']")]
    internal sealed class ReignClanSkillTextPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"20\" " +
            "Text=\"@ReignSkillValueText\" VerticalAlignment=\"Bottom\" Brush=\"Encyclopedia.Skill.Text\" " +
            "Brush.Font=\"Galahad\" Brush.FontSize=\"20\" Brush.FontColor=\"#C5AC83FF\" PositionYOffset=\"25\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("MarriageOfferPopup", "descendant::TextWidget[@IntText='@SkillValue']")]
    [PrefabExtension("HeirSelectionPopup", "descendant::TextWidget[@IntText='@SkillValue']")]
    internal sealed class ReignPopupSkillTextPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"Fixed\" SuggestedHeight=\"20\" " +
            "Text=\"@ReignSkillValueText\" VerticalAlignment=\"Bottom\" HorizontalAlignment=\"Center\" " +
            "Brush=\"Encyclopedia.Skill.Text\" Brush.Font=\"Galahad\" Brush.FontSize=\"15\" Brush.FontColor=\"#C5AC83FF\" PositionYOffset=\"20\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

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

    [PrefabExtension("SkillGridItem", "descendant::TextWidget[@IntText='@Level']")]
    internal sealed class ReignCharacterDeveloperGridSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"80\" SuggestedHeight=\"40\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Bottom\" MarginLeft=\"82\" MarginBottom=\"!LevelText.MarginBottom\" " +
            "Brush=\"CharacterDeveloper.GridSkillLevel.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5AC83FF\" Text=\"@ReignSkillLevelText\" DoNotAcceptEvents=\"true\" IsEnabled=\"@CanLearnSkill\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("CharacterDeveloper", "descendant::TextWidget[@Id='PercentageIndicatorTextWidget']")]
    internal sealed class ReignCharacterDeveloperCurrentSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget Id=\"PercentageIndicatorTextWidget\" WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Top\" PositionYOffset=\"10\" " +
            "Brush=\"CharacterDeveloper.CurrentSkill.Value.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5AC83FF\" Text=\"@ReignSkillLevelText\" IsEnabled=\"@CanLearnSkill\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("CharacterDeveloper", "descendant::TextWidget[@Text='@ProgressText']")]
    internal sealed class ReignCharacterDeveloperProgressTextPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("Text", "@ReignSkillProgressText"),
            new Attribute("Brush.Font", "Galahad"),
            new Attribute("Brush.FontColor", "#C5BDAFFF")
        };
    }

    [PrefabExtension("CharacterDeveloper", "descendant::TextWidget[@Id='CurrentLearningRateTextWidget']")]
    internal sealed class ReignCharacterDeveloperLearningRateTextPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("Text", "@ReignLearningRateText"),
            new Attribute("Brush.Font", "Galahad"),
            new Attribute("Brush.FontColor", "#8A8883FF")
        };
    }

    [PrefabExtension("CharacterDeveloper", "descendant::FillBarWidget[@Id='SkillProgressFillBarWidget']")]
    [PrefabExtension("CharacterDeveloper", "descendant::ValueBasedVisibilityWidget[@Id='LearningLimitIndicator']")]
    internal sealed class ReignCharacterDeveloperDerivedValuePatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute> { new Attribute("IsVisible", "@ReignIsSkillValueVisible") };
    }

    [PrefabExtension("CharacterDeveloper", "descendant::HintWidget[@DataSource='{CurrentSkill\\SkillXPHint}']")]
    internal sealed class ReignCharacterDeveloperSkillXpHintPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute> { new Attribute("IsVisible", "@ReignIsCurrentHeroSkillVisible") };
    }

    [PrefabExtension("CharacterDeveloper", "descendant::HintWidget[@DataSource='{LearningRateTooltip}']")]
    internal sealed class ReignCharacterDeveloperLearningRateHintPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute> { new Attribute("IsVisible", "@ReignIsSkillValueVisible") };
    }

    [PrefabExtension("CraftingHeroPopup", "descendant::TextWidget[@IntText='@SmithySkillLevel']")]
    internal sealed class ReignCraftingHeroPopupSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
            "Brush=\"Crafting.Stamina.Percentage.Text\" Brush.Font=\"Galahad\" Brush.FontSize=\"20\" Brush.FontColor=\"#C5AC83FF\" Text=\"@ReignSmithySkillLevelText\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("Crafting", "descendant::TextWidget[@IntText='@SmithySkillLevel']")]
    internal sealed class ReignCraftingSelectedHeroSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" " +
            "Brush=\"Crafting.Stamina.Percentage.Text\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5AC83FF\" Text=\"@ReignSmithySkillLevelText\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("Crafting", "descendant::TextWidget[@Text='@CurrentCraftingSkillValueText']")]
    internal sealed class ReignCraftingSkillTextPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute>
        {
            new Attribute("Text", "@ReignCraftingSkillText"),
            new Attribute("Brush.Font", "Galahad"),
            new Attribute("Brush.FontColor", "#C5BDAFFF")
        };
    }

    [PrefabExtension("Crafting", "descendant::FillBarVerticalWidget[@Id='CraftingHeroDifficultyBar']")]
    [PrefabExtension("Crafting", "descendant::FillBarVerticalWidget[@Id='CurrentSkillValueBar']")]
    internal sealed class ReignCraftingSkillGaugePatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute> { new Attribute("IsVisible", "@ReignIsCraftingSkillVisible") };
    }

    [PrefabExtension("Crafting", "descendant::HintWidget[@DataSource='{DifficultyExplanationHint}']")]
    internal sealed class ReignCraftingSkillHintPatch : PrefabExtensionSetAttributePatch
    {
        public override List<Attribute> Attributes => new List<Attribute> { new Attribute("IsVisible", "@ReignIsCraftingSkillVisible") };
    }

    [PrefabExtension("Crafting", "descendant::CraftingDifficultyBarParentWidget[@Id='SkillBarParent']/Children")]
    internal sealed class ReignCraftingUnknownSkillInsertPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"100\" SuggestedHeight=\"50\" " +
            "HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" Brush=\"Crafting.Difficulty.Text\" " +
            "Brush.Font=\"Galahad\" Brush.FontSize=\"32\" Brush.FontColor=\"#8A8883FF\" Brush.TextHorizontalAlignment=\"Center\" Text=\"?\" " +
            "IsVisible=\"@ReignIsCraftingSkillHidden\" DoNotAcceptEvents=\"true\"/>");
        public override InsertType Type => InsertType.Child;
        public override int Index => 1000;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("EducationGainedProperties", "descendant::TextWidget[@IntText='@SkillValueInt' and @IsVisible]")]
    internal sealed class ReignEducationIncreasedSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" Brush.Font=\"Galahad\" Brush.FontColor=\"#C5AC83FF\" " +
            "Brush.FontSize=\"25\" IsVisible=\"@HasSkillValueIncreasedInCurrentStage\" Text=\"@ReignSkillValueText\" ClipContents=\"false\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
        [PrefabExtensionXmlNode(false)] public XmlNode Content() => _document.DocumentElement;
    }

    [PrefabExtension("EducationGainedProperties", "descendant::TextWidget[@IntText='@SkillValueInt' and @IsHidden]")]
    internal sealed class ReignEducationNormalSkillPatch : PrefabExtensionInsertPatch
    {
        private readonly XmlDocument _document = ReignPrefabXml.Load(
            "<TextWidget WidthSizePolicy=\"CoverChildren\" HeightSizePolicy=\"CoverChildren\" Brush.Font=\"Galahad\" Brush.FontSize=\"25\" Brush.FontColor=\"#C5BDAFFF\" " +
            "IsHidden=\"@HasSkillValueIncreasedInCurrentStage\" Text=\"@ReignSkillValueText\" ClipContents=\"false\"/>");
        public override InsertType Type => InsertType.ReplaceKeepChildren;
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
