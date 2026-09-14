using System;
using AIEventsAndIntrigue.Settings;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIPortraits;

public class ConversationBehavior : CampaignBehaviorBase
{
	private const string LookOutputToken = "aiportraits_look_done";

	private const string MemoryOutputToken = "aiportraits_memory_done";

	public override void RegisterEvents()
	{
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
	}

	public override void SyncData(IDataStore dataStore)
	{
	}

	private void OnSessionLaunched(CampaignGameStarter starter)
	{
		starter.AddPlayerLine("aiportraits_look", "hero_main_options", "aiportraits_look_done", "You take a look at them.", LookCondition, LookConsequence, 110);
		starter.AddPlayerLine("aiportraits_memory", "hero_main_options", "aiportraits_memory_done", "You reflect on your time with them.", MemoryCondition, MemoryConsequence, 109);
		starter.AddDialogLine("aiportraits_look_reaction", "aiportraits_look_done", "hero_main_options", "{=*}...", null, null);
		starter.AddDialogLine("aiportraits_memory_reaction", "aiportraits_memory_done", "hero_main_options", "{=*}...", null, null);
	}

	private bool LookCondition()
	{
		if (ReignCampaignInitializationGate.IsPending) return false;
		Hero conversationHero = GetConversationHero();
		if (conversationHero == null)
		{
			return false;
		}
		AIEventsSettings instance = AIEventsSettings.Instance;
		if (instance != null && !instance.ModEnabled)
		{
			return false;
		}
		return true;
	}

	private bool MemoryCondition()
	{
		if (ReignCampaignInitializationGate.IsPending) return false;
		Hero conversationHero = GetConversationHero();
		if (conversationHero == null || Hero.MainHero == null)
		{
			return false;
		}
		AIEventsSettings instance = AIEventsSettings.Instance;
		if (instance != null && !instance.ModEnabled)
		{
			return false;
		}
		if (PortraitCache.ExistsOnDisk(CharacterCacheId.ForHero(Hero.MainHero)))
		{
			return PortraitCache.ExistsOnDisk(CharacterCacheId.ForHero(conversationHero));
		}
		return false;
	}

	private void LookConsequence()
	{
		Hero conversationHero = GetConversationHero();
		if (conversationHero == null)
		{
			return;
		}
		ReignPortraitBridge.RequestPortrait(conversationHero);
	}

	private void MemoryConsequence()
	{
		Hero hero = GetConversationHero();
		if (hero != null)
		{
			TextInquiryData textData = new TextInquiryData("Create a Memory", "Describe what you and " + hero.Name?.ToString() + " did together.", isAffirmativeOptionShown: true, isNegativeOptionShown: true, "Create", "Cancel", delegate(string text)
			{
				MemoryService.CreateMemory(hero, text);
			}, null, shouldInputBeObfuscated: false, delegate(string text)
			{
				string text2 = (text ?? string.Empty).Trim();
				return (text2.Length < 3) ? new Tuple<bool, string>(item1: false, "Write a short description first.") : new Tuple<bool, string>(item1: true, string.Empty);
			}, string.Empty, string.Empty);
			InformationManager.ShowTextInquiry(textData, pauseGameActiveState: true);
		}
	}

	private static Hero GetConversationHero()
	{
		return ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.ConversationHero
            ?? CharacterObject.OneToOneConversationCharacter?.HeroObject;
	}

	private static void ShowMsg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(color)));
	}
}
