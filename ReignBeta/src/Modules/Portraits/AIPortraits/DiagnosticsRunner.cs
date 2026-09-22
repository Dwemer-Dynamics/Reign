using System;
using AIEventsAndIntrigue.Settings;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AIPortraits;

public static class DiagnosticsRunner
{
	private const string TestSourceB64 = "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";

	private static volatile bool _testRequested;

	private static volatile bool _testRunning;

	private static volatile bool _playerBuildRequested;

	private static volatile bool _playerClearRequested;

	public static volatile bool CaptureNextRendered;

	public static void RequestTest()
	{
		_testRequested = true;
	}

	public static void RequestPlayerBuild()
	{
		_playerBuildRequested = true;
	}

	public static void RequestPlayerClear()
	{
		_playerClearRequested = true;
	}

	public static byte[] GetNeutralSourceBytes()
	{
		return Convert.FromBase64String("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");
	}

	public static void ReloadPortraitsFromDisk()
	{
		int indexed = 0;
		string campaignId = ReignCampaignIdentity.CurrentCampaignId();
		string campaignFolder = ReignCampaignIdentity.CurrentCampaignFolderName();
		string cacheDir = PortraitCache.CacheDir;
		string currentHeroText = "no active one-to-one hero";
		try
		{
			indexed = PortraitIndex.RegisterCachedCampaignHeroes();
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] ReloadPortraitsFromDisk index rebuild failed: " + ex.Message);
		}
		PortraitCache.ClearMemoryCache();
		TextureFactory.ClearAll();
		try
		{
			Hero hero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
			if (hero != null)
			{
				string cacheKey = CharacterCacheId.ForHero(hero);
				string appearanceKey = PortraitRequestRegistry.NormalizeKey(PortraitRequestRegistry.GetCodeFor(hero));
				string resolved = PortraitIndex.Resolve(appearanceKey);
				bool exists = PortraitCache.ExistsOnDisk(cacheKey);
				bool custom = PortraitCache.HasCustomPortrait(cacheKey);
				bool hasFactory = TextureFactory.Has(cacheKey);
				PortraitCache.TryGetPreferredDiskPathForDiagnostics(cacheKey, out string preferredPath);
				currentHeroText = hero.Name + " cacheKey=" + (cacheKey ?? "<none>")
					+ " resolved=" + (resolved ?? "<none>")
					+ " exists=" + exists
					+ " custom=" + custom
					+ " textureFactoryHas=" + hasFactory
					+ " path=" + (preferredPath ?? "<none>");
			}
		}
		catch (Exception ex)
		{
			currentHeroText = "current hero diagnosis failed: " + ex.Message;
		}
		Debug.Print("[AIP-DIAG] ReloadPortraitsFromDisk campaignId=" + campaignId
			+ " campaignFolder=" + campaignFolder
			+ " cacheDir=" + cacheDir
			+ " indexed=" + indexed
			+ " cachedFolders=" + PortraitCache.CachedCount
			+ " currentHero=" + currentHeroText);
		Msg("Portraits reloaded. Campaign=" + campaignFolder + ", indexed links=" + indexed + ", folders=" + PortraitCache.CachedCount + ". Details written to rgl_log.txt.", 4278246741u);
	}

	public static void DiagnoseCurrentConversationPortrait()
	{
		try
		{
			Hero hero = CharacterObject.OneToOneConversationCharacter?.HeroObject;
			if (hero == null)
			{
				Msg("Start a one-to-one conversation first, then diagnose the portrait.", 4294945280u);
				return;
			}

			int indexed = PortraitIndex.RegisterCachedCampaignHeroes();
			string cacheKey = CharacterCacheId.ForHero(hero);
			string rawAppearance = PortraitRequestRegistry.GetCodeFor(hero);
			string appearanceKey = PortraitRequestRegistry.NormalizeKey(rawAppearance);
			if (!string.IsNullOrWhiteSpace(rawAppearance) && !string.IsNullOrWhiteSpace(cacheKey))
			{
				PortraitIndex.Register(rawAppearance, cacheKey);
			}

			string resolved = PortraitIndex.Resolve(appearanceKey);
			bool cacheExists = PortraitCache.ExistsOnDisk(cacheKey);
			bool customExists = PortraitCache.HasCustomPortrait(cacheKey);
			bool textureFactoryHas = TextureFactory.Has(cacheKey);
			bool preferredExists = PortraitCache.TryGetPreferredDiskPathForDiagnostics(cacheKey, out string preferredPath);
			string message = "[AIP-DIAG] Current portrait diagnosis"
				+ " campaignId=" + ReignCampaignIdentity.CurrentCampaignId()
				+ " campaignFolder=" + ReignCampaignIdentity.CurrentCampaignFolderName()
				+ " cacheDir=" + PortraitCache.CacheDir
				+ " heroId=" + hero.StringId
				+ " characterObjectId=" + (hero.CharacterObject?.StringId ?? "")
				+ " heroName=" + hero.Name
				+ " cacheKey=" + (cacheKey ?? "<none>")
				+ " appearanceTail=" + Tail(appearanceKey)
				+ " resolved=" + (resolved ?? "<none>")
				+ " indexedThisRun=" + indexed
				+ " customExists=" + customExists
				+ " cacheExists=" + cacheExists
				+ " preferredExists=" + preferredExists
				+ " preferredPath=" + (preferredPath ?? "<none>")
				+ " textureFactoryHas=" + textureFactoryHas
				+ " textureCaches=" + TextureFactory.CacheDiagnostics()
				+ " portraitsEnabled=" + (AIEventsSettings.Instance?.ModEnabled ?? false)
				+ " textureTraceEnabled=" + (AIEventsSettings.Instance?.TextureTraceEnabled ?? false);
			Debug.Print(message);
			Msg("Portrait diag for " + hero.Name + ": file=" + cacheExists + ", custom=" + customExists + ", index=" + (!string.IsNullOrWhiteSpace(resolved)) + ", texture=" + textureFactoryHas + ". Full details in rgl_log.txt.", cacheExists ? 4278246741u : 4294945280u);
			_ = DiagnoseCurrentConversationPortraitOnServerAsync(hero, cacheKey, appearanceKey);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIP-DIAG] DiagnoseCurrentConversationPortrait failed: " + ex);
			Msg("Portrait diagnosis failed - see rgl_log.txt.", 4294919168u);
		}
	}

	public static void PrepareSharedPortraitCache()
	{
		if (PortraitCache.PrepareSharedPortraitCache(out int foldersQueued, out string message))
		{
			Msg(message, 4278246741u);
		}
		else
		{
			Msg(message, 4294919168u);
		}
	}

	[Obsolete("Use PrepareSharedPortraitCache.")]
	public static void CopySharedPortraitsToCurrentSave()
	{
		PrepareSharedPortraitCache();
	}

	public static void ClearGeneratedPortraits()
	{
		PortraitCache.ClearGeneratedOnly();
		TextureFactory.ClearAll();
		Msg("Generated portrait cache cleared. Folders with custom.png were kept.", 4294945280u);
	}

	public static void ClearAllPortraitsIncludingCustom()
	{
		PortraitCache.ClearAllIncludingCustom();
		TextureFactory.ClearAll();
		Msg("All portrait cache folders cleared, including custom.png overrides.", 4294919168u);
	}

	public static void Tick()
	{
		if (_playerBuildRequested)
		{
			_playerBuildRequested = false;
			HandlePlayerBuild();
		}
		if (_playerClearRequested)
		{
			_playerClearRequested = false;
			HandlePlayerClear();
		}
		if (_testRequested)
		{
			_testRequested = false;
			if (_testRunning)
			{
				Msg("A test is already running — please wait.", 4294945280u);
			}
			else
			{
				_ = RunTestAsync();
			}
		}
	}

	private static void HandlePlayerBuild()
	{
		if (Hero.MainHero == null) { Msg("Load a campaign before creating a player portrait.", 4294945280u); return; }
		ReignPortraitBridge.RequestPortrait(Hero.MainHero);
	}

	private static async Task DiagnoseCurrentConversationPortraitOnServerAsync(Hero hero, string cacheKey, string appearanceKey)
	{
		try
		{
			JObject response = await ReignServerClient.DiagnosePortraitAsync(hero, cacheKey, appearanceKey).ConfigureAwait(false);
			Debug.Print("[AIP-DIAG] Server portrait diagnosis " + response.ToString(Formatting.None));
		}
		catch (Exception ex)
		{
			Debug.Print("[AIP-DIAG] Server portrait diagnosis failed: " + ex.Message);
		}
	}

	private static string Tail(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return "<none>";
		}
		return text.Length <= 14 ? text : text.Substring(text.Length - 14);
	}



	private static void HandlePlayerClear()
	{
		try
		{
			if (Campaign.Current == null || Hero.MainHero == null)
			{
				Msg("Load a campaign first — there's no player character to clear.", 4294945280u);
				return;
			}
			string codeFor = PortraitRequestRegistry.GetCodeFor(Hero.MainHero);
			string text = CharacterCacheId.ForHero(Hero.MainHero);
			if (string.IsNullOrEmpty(text))
			{
				Msg("Could not resolve the player's portrait key.", 4294919168u);
				return;
			}
			PortraitRequestRegistry.Clear(codeFor);
			bool flag = PortraitCache.Delete(text);
			TextureFactory.Invalidate(text);
			Msg(flag ? "Your character portrait was cleared — vanilla portrait restored." : "No AI portrait was cached for your character.", 4278246741u);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] HandlePlayerClear error: " + ex);
			Msg("Clear errored — see rgl_log.txt.", 4294919168u);
		}
	}

	private static async Task RunTestAsync()
	{
		_testRunning = true;
		try { await ReignMainThread.InvokeAsync(HandlePlayerBuild).ConfigureAwait(false); }
		finally { _testRunning = false; }
	}

	private static void Msg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] " + text, Color.FromUint(color)));
	}
}
