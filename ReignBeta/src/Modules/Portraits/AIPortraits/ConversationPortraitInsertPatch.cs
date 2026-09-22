using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using TaleWorlds.Library;

namespace AIPortraits;

[PrefabExtension("SPConversation", "descendant::ConversationScreenButtonWidget/Children")]
internal sealed class ConversationPortraitInsertPatch : PrefabExtensionInsertPatch
{
	private const string PortraitWidgetXml =
		"<Widget DoNotAcceptEvents=\"true\" RenderLate=\"true\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\">" +
		"<Children>" +
		"<ButtonWidget Id=\"AIPortraitsNpcPortraitButton\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"184\" SuggestedHeight=\"240\" HorizontalAlignment=\"Left\" VerticalAlignment=\"Bottom\" " +
		"MarginLeft=\"20\" MarginBottom=\"60\" IsVisible=\"@IsConversationPortraitVisible\" " +
		"ClipContents=\"true\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" HoveredCursorState=\"RightClickLink\">" +
		"<Children>" +
		"<ImageIdentifierWidget Id=\"AIPortraitsNpcPortrait\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"@NpcPortraitCropImageWidth\" SuggestedHeight=\"@NpcPortraitCropImageHeight\" " +
		"HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" TextureProviderName=\"@ConversationPortraitTextureProviderName\" " +
		"AdditionalArgs=\"@ConversationPortraitAdditionalArgs\" ImageId=\"@ConversationPortraitId\" DoNotAcceptEvents=\"true\" />" +
		"<ImageWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"Sprite=\"reign_modern_portrait_rectangle_overlay\" DoNotAcceptEvents=\"true\" />" +
		"</Children></ButtonWidget>" +
		"<ButtonWidget Id=\"AIPortraitsPlayerPortraitButton\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"184\" SuggestedHeight=\"240\" HorizontalAlignment=\"Right\" VerticalAlignment=\"Bottom\" " +
		"MarginRight=\"16\" MarginBottom=\"60\" IsVisible=\"@IsPlayerPortraitVisible\" " +
		"ClipContents=\"true\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" HoveredCursorState=\"RightClickLink\">" +
		"<Children>" +
		"<ImageIdentifierWidget Id=\"AIPortraitsPlayerPortrait\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"@PlayerPortraitCropImageWidth\" SuggestedHeight=\"@PlayerPortraitCropImageHeight\" " +
		"HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" TextureProviderName=\"@PlayerPortraitTextureProviderName\" " +
		"AdditionalArgs=\"@PlayerPortraitAdditionalArgs\" ImageId=\"@PlayerPortraitId\" DoNotAcceptEvents=\"true\" />" +
		"<ImageWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"Sprite=\"reign_modern_portrait_rectangle_overlay\" DoNotAcceptEvents=\"true\" />" +
		"</Children></ButtonWidget>" +
		"<ButtonWidget Id=\"AIPortraitsAIInfluenceMemoryButton\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"@AIInfluenceMemoryFrameWidth\" SuggestedHeight=\"@AIInfluenceMemoryFrameHeight\" " +
		"HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" MarginRight=\"94\" MarginTop=\"118\" " +
		"IsVisible=\"@IsAIInfluenceMemoryVisible\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" " +
		"RenderLate=\"true\" HoveredCursorState=\"RightClickLink\"><Children>" +
		"<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Sprite=\"BlankWhiteSquare\" Color=\"#10100FFF\" DoNotAcceptEvents=\"true\" />" +
		"<ImageIdentifierWidget Id=\"AIPortraitsAIInfluenceMemoryImage\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"@AIInfluenceMemoryImageWidth\" SuggestedHeight=\"@AIInfluenceMemoryImageHeight\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" " +
		"TextureProviderName=\"@AIInfluenceMemoryTextureProviderName\" AdditionalArgs=\"@AIInfluenceMemoryAdditionalArgs\" ImageId=\"@AIInfluenceMemoryId\" />" +
		"<ImageWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Sprite=\"reign_modern_portrait_rectangle_overlay\" DoNotAcceptEvents=\"true\" />" +
		"</Children></ButtonWidget>" +
		"<ButtonWidget Id=\"AIPortraitsZoomBackdrop\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"IsVisible=\"@IsPortraitZoomVisible\" DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" RenderLate=\"true\" " +
		"Sprite=\"BlankWhiteSquare\" Brush.Color=\"#03030399\"><Children>" +
		"<Widget WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" SuggestedWidth=\"@ZoomFrameWidth\" SuggestedHeight=\"@ZoomFrameHeight\" " +
		"HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" ClipContents=\"true\" DoNotAcceptEvents=\"true\" RenderLate=\"true\"><Children>" +
		"<ImageIdentifierWidget Id=\"AIPortraitsZoomPortrait\" WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"@ZoomPortraitImageWidth\" SuggestedHeight=\"@ZoomPortraitImageHeight\" HorizontalAlignment=\"Center\" VerticalAlignment=\"Center\" " +
		"TextureProviderName=\"@ZoomPortraitTextureProviderName\" AdditionalArgs=\"@ZoomPortraitAdditionalArgs\" ImageId=\"@ZoomPortraitId\" DoNotAcceptEvents=\"true\" />" +
		"<ImageWidget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" Sprite=\"reign_modern_portrait_rectangle_overlay\" DoNotAcceptEvents=\"true\" />" +
		"</Children></Widget></Children></ButtonWidget>" +
		"</Children></Widget>";

	private readonly XmlDocument _doc;

	public override InsertType Type => InsertType.Child;

	public override int Index => 10000;

	public ConversationPortraitInsertPatch()
	{
		Debug.Print("[AIPortraits] ConversationPortraitInsertPatch constructed (clipped square-corner portrait patch registered).");
		_doc = new XmlDocument();
		_doc.LoadXml(PortraitWidgetXml);
	}

	[PrefabExtensionXmlNode(false)]
	public XmlNode GetPatchContent()
	{
		return _doc.DocumentElement;
	}
}
