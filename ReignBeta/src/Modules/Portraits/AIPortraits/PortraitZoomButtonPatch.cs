using HarmonyLib;
using TaleWorlds.GauntletUI.BaseTypes;

namespace AIPortraits;

[HarmonyPatch(typeof(ButtonWidget), "HandleClick")]
public static class PortraitZoomButtonPatch
{
	private static void Postfix(ButtonWidget __instance)
	{
		if (__instance != null)
		{
			switch (__instance.Id)
			{
			case "AIPortraitsNpcPortraitButton":
				ConversationPortraitMixin.ToggleNpcPortraitZoomFromPatch();
				break;
			case "AIPortraitsPlayerPortraitButton":
				ConversationPortraitMixin.TogglePlayerPortraitZoomFromPatch();
				break;
			case "AIPortraitsZoomBackdrop":
				ConversationPortraitMixin.ClosePortraitZoomFromPatch();
				break;
			case "AIPortraitsAIInfluenceMemoryButton":
				ConversationPortraitMixin.CloseAIInfluenceMemoryFromPatch();
				break;
			case "AIPortraitsQuestMemoryBookButton":
				MemoryService.OpenMemoriesBook();
				break;
			}
		}
	}
}
