using AIEventsAndIntrigue.Settings;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIPortraits;

public class PreloadCampaignBehavior : CampaignBehaviorBase
{
	public override void RegisterEvents()
	{
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
	}

	public override void SyncData(IDataStore dataStore)
	{
	}

	private void OnSessionLaunched(CampaignGameStarter starter)
	{
		if (ReignCampaignInitializationGate.IsPending)
		{
			ReignCampaignInitializationGate.RunWhenReadyOnMainThread(InitializePortraitCache);
			return;
		}
		InitializePortraitCache();
	}

	private static void InitializePortraitCache()
	{
		AIEventsSettings instance = AIEventsSettings.Instance;
		if (instance?.ModEnabled ?? true)
		{
			bool alreadySeeded = PortraitCache.IsCampaignSeeded();
			if (!alreadySeeded)
			{
				InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Linking this campaign to the shared portrait library.", Color.FromUint(4278246775u)));
			}

			PortraitSeedResult seedResult = PortraitCache.EnsureSeedCacheForCampaign();
			if (seedResult.Ok)
			{
				string detail = seedResult.AlreadySeeded
					? "shared portraits already linked."
					: "shared portraits linked; indexed " + seedResult.Indexed + " appearance entries.";
				InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Portraits ready: " + detail, Color.FromUint(4278246775u)));
			}
			else if (!string.IsNullOrWhiteSpace(seedResult.Message))
			{
				InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + seedResult.Message, Color.FromUint(4294945280u)));
			}
		}
	}
}
