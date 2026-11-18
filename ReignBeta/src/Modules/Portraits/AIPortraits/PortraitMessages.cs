using TaleWorlds.Library;

namespace AIPortraits;

internal static class PortraitMessages
{
	internal static void Msg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] " + text, Color.FromUint(color)));
	}
}
