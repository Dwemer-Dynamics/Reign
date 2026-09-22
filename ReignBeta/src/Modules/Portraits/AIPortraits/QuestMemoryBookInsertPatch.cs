using System.Xml;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.Prefabs2;
using TaleWorlds.Library;

namespace AIPortraits;

[PrefabExtension("QuestsScreen", "descendant::AnimatedDropdownWidget[@Id='DropdownParent']/..")]
internal sealed class QuestMemoryBookInsertPatch : PrefabExtensionInsertPatch
{
	public const string ButtonId = "AIPortraitsQuestMemoryBookButton";

	private const string ButtonXml =
		"<ButtonWidget Id=\"AIPortraitsQuestMemoryBookButton\" " +
		"WidthSizePolicy=\"Fixed\" HeightSizePolicy=\"Fixed\" " +
		"SuggestedWidth=\"165\" SuggestedHeight=\"42\" " +
		"HorizontalAlignment=\"Right\" VerticalAlignment=\"Top\" " +
		"MarginRight=\"395\" MarginTop=\"9\" Brush=\"ConversationItem.SoundBrush\" " +
		"DoNotPassEventsToChildren=\"true\" UpdateChildrenStates=\"true\" HoveredCursorState=\"RightClickLink\">" +
		"<Children>" +
		"<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"Sprite=\"BlankWhiteSquare\" Color=\"#171717FF\" DoNotAcceptEvents=\"true\" />" +
		"<Widget WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"Sprite=\"gold_frame_9\" Color=\"#7E6A4DFF\" DoNotAcceptEvents=\"true\" />" +
		"<TextWidget Text=\"MEMORY BOOK\" WidthSizePolicy=\"StretchToParent\" HeightSizePolicy=\"StretchToParent\" " +
		"Brush=\"Info.Text\" Brush.Font=\"Galahad\" Brush.FontSize=\"15\" Brush.FontColor=\"#C5AC83FF\" " +
		"Brush.TextHorizontalAlignment=\"Center\" Brush.TextVerticalAlignment=\"Center\" DoNotAcceptEvents=\"true\" />" +
		"</Children></ButtonWidget>";

	private readonly XmlDocument _doc;

	public override InsertType Type => InsertType.Child;

	public override int Index => 0;

	public QuestMemoryBookInsertPatch()
	{
		Debug.Print("[AIPortraits] QuestMemoryBookInsertPatch constructed.");
		_doc = new XmlDocument();
		_doc.LoadXml(ButtonXml);
	}

	[PrefabExtensionXmlNode(false)]
	public XmlNode GetPatchContent()
	{
		return _doc.DocumentElement;
	}
}
