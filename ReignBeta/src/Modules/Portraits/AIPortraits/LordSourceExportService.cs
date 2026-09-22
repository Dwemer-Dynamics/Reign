using System;
using AIEventsAndIntrigue.Settings;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIPortraits;

public static class LordSourceExportService
{
	private sealed class ExportItem
	{
		public string HeroName;

		public string CharacterId;

		public string CacheKey;

		public string AppearanceKey;

		public string ImageId;

		public string AdditionalArgs;

		public string TextureProviderName;

		public CharacterViewModel TableauModel;

		public Hero Hero;

		public int? AgeYears;

		public string CultureId;

		public int? ClanTier;

		public string SocialStation;
	}

	private static readonly object _lock = new object();

	private static List<ExportItem> _items = new List<ExportItem>();

	private static Dictionary<string, ExportItem> _itemsByCharacterId = new Dictionary<string, ExportItem>(StringComparer.OrdinalIgnoreCase);

	private static HashSet<string> _capturedCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static HashSet<string> _generateAfterCaptureCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static int _index;

	private static int _captured;

	private static bool _active;

	private const double SettleSeconds = 1.2;

	private const double RetryThrottleSeconds = 0.35;

	private const double FreshOpenGapSeconds = 0.5;

	private const double GiveUpWarnSeconds = 12.0;

	private static readonly Stopwatch _clock = Stopwatch.StartNew();

	private static Dictionary<string, double> _firstSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, double> _lastSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, double> _lastAttemptTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

	private static HashSet<string> _gaveUpWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static HashSet<string> _unmatchedTableauWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	public static void Start()
	{
		try
		{
			AIEventsSettings instance = AIEventsSettings.Instance;
			if (instance != null && !instance.ModEnabled)
			{
				Msg("Cannot export lord sources: AI Portraits is disabled.", 4294945280u);
				return;
			}
			if (Campaign.Current == null)
			{
				Msg("Load a campaign first; lord data is not available at the main menu.", 4294945280u);
				return;
			}
			List<ExportItem> list = CollectLordItems();
			if (list.Count == 0)
			{
				Msg("No lord-tagged heroes found in this campaign.", 4294945280u);
				return;
			}
			PortraitCache.EnsureDirectory();
			foreach (ExportItem item in list)
			{
				Directory.CreateDirectory(PortraitCache.DirFor(item.CacheKey));
			}
			WriteManifest(list);
			int num = RevealToPlayer(list);
			lock (_lock)
			{
				_items = list;
				_itemsByCharacterId = BuildCharacterIdIndex(list);
				_capturedCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_generateAfterCaptureCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_firstSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_lastSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_lastAttemptTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_gaveUpWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_index = 0;
				_captured = 0;
				_active = true;
			}
			ConversationPortraitMixin.RefreshLordExportFromPatch();
			Msg("Prepared " + list.Count + " lord folders and revealed " + num + " encyclopedia pages. Open each lord's encyclopedia page to save its full-body source.png.", 4278233855u);
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Lord source export start failed: " + ex);
			Msg("Lord source export failed to start; see rgl_log.txt.", 4294919168u);
		}
	}

	public static bool PrepareSingleHeroCapture(Hero hero, bool generateAfterCapture, out string appearanceKey, out string cacheKey, out string message)
	{
		appearanceKey = null;
		cacheKey = null;
		message = string.Empty;
		try
		{
			AIEventsSettings instance = AIEventsSettings.Instance;
			if (instance != null && !instance.ModEnabled)
			{
				message = "AI portraits are disabled in Bannerlord Reign options.";
				return false;
			}
			if (hero == null)
			{
				message = "No hero selected.";
				return false;
			}
			if (Campaign.Current == null)
			{
				message = "Load a campaign before requesting a portrait.";
				return false;
			}

			ExportItem item = BuildItem(hero);
			if (item == null || string.IsNullOrWhiteSpace(item.AppearanceKey) || string.IsNullOrWhiteSpace(item.CacheKey))
			{
				message = "Could not build a 3D source capture record for " + hero.Name + ".";
				return false;
			}

			PortraitCache.EnsureDirectory();
			Directory.CreateDirectory(PortraitCache.DirFor(item.CacheKey));
			PortraitIndex.Register(item.AppearanceKey, item.CacheKey);
			try
			{
				if (!hero.IsKnownToPlayer)
				{
					hero.IsKnownToPlayer = true;
				}
				if (!hero.HasMet)
				{
					hero.SetHasMet();
				}
			}
			catch (Exception ex)
			{
				TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to reveal single source hero " + item.CacheKey + ": " + ex.Message);
			}

			List<ExportItem> list = new List<ExportItem> { item };
			lock (_lock)
			{
				_items = list;
				_itemsByCharacterId = BuildCharacterIdIndex(list);
				_capturedCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_generateAfterCaptureCacheKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				if (generateAfterCapture)
				{
					_generateAfterCaptureCacheKeys.Add(item.CacheKey);
				}
				_firstSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_lastSeenTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_lastAttemptTick = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
				_gaveUpWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_index = 0;
				_captured = 0;
				_active = true;
			}

			ConversationPortraitMixin.RefreshLordExportFromPatch();
			appearanceKey = item.AppearanceKey;
			cacheKey = item.CacheKey;
			message = "3D source capture armed for " + item.HeroName + ".";
			return true;
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Single hero source capture failed to start: " + ex);
			message = "3D source capture failed to start; see rgl_log.txt.";
			return false;
		}
	}

	public static bool TryGetCurrent(out string imageId, out string additionalArgs, out string providerName, out string cacheKey)
	{
		lock (_lock)
		{
			if (!_active || _index < 0 || _index >= _items.Count)
			{
				imageId = (additionalArgs = (providerName = (cacheKey = null)));
				return false;
			}
			ExportItem exportItem = _items[_index];
			imageId = exportItem.ImageId;
			additionalArgs = exportItem.AdditionalArgs;
			providerName = exportItem.TextureProviderName;
			cacheKey = exportItem.CacheKey;
			return true;
		}
	}

	public static bool TryGetCurrentTableau(out CharacterViewModel model, out string cacheKey)
	{
		lock (_lock)
		{
			if (!_active || _index < 0 || _index >= _items.Count)
			{
				model = null;
				cacheKey = null;
				return false;
			}
			ExportItem exportItem = _items[_index];
			model = exportItem.TableauModel;
			cacheKey = exportItem.CacheKey;
			return model != null;
		}
	}

	public static bool TryCaptureRendered(TextureWidget widget, string rawId)
	{
		ExportItem exportItem;
		lock (_lock)
		{
			if (!_active || _index < 0 || _index >= _items.Count)
			{
				return false;
			}
			exportItem = _items[_index];
		}
		string a = PortraitRequestRegistry.NormalizeKey(rawId);
		if (!string.Equals(a, exportItem.AppearanceKey, StringComparison.Ordinal))
		{
			return false;
		}
		Texture texture = widget.Texture;
		if (texture == null || !texture.IsValid)
		{
			return false;
		}
		byte[] array = PortraitCaptureService.CaptureFromTwoDTexture(texture);
		if (array == null || array.Length == 0)
		{
			return false;
		}
		return CompleteCapture(exportItem, array, "Lord source export complete. Saved source.png files in lord cache folders.");
	}

	public static bool TryCaptureTableauRendered(TextureWidget widget)
	{
		ExportItem item;
		lock (_lock)
		{
			if (!_active || _index < 0 || _index >= _items.Count)
			{
				return false;
			}
			item = _items[_index];
		}
		Texture texture = widget.Texture;
		if (texture == null || !texture.IsValid)
		{
			return false;
		}
		byte[] array = PortraitCaptureService.CaptureFromTwoDTexture(texture);
		if (array == null || array.Length == 0)
		{
			return false;
		}
		return CompleteCapture(item, array, "Lord source export complete. Saved full-body civilian source.png files in lord cache folders.");
	}

	public static bool TryCaptureEncyclopediaTableauRendered(TextureWidget widget, string characterId)
	{
		if (string.IsNullOrWhiteSpace(characterId))
		{
			return false;
		}
		ExportItem value;
		string lookupId = NormalizeLookupId(characterId);
		lock (_lock)
		{
			if (!_active)
			{
				return false;
			}
			if (!_itemsByCharacterId.TryGetValue(lookupId, out value))
			{
				if (_unmatchedTableauWarned.Add(lookupId))
				{
					TaleWorlds.Library.Debug.Print("[AIPortraits] Lord source export saw encyclopedia tableau '" + lookupId + "' but it is not in the prepared capture list.");
				}
				return false;
			}
			if (_capturedCacheKeys.Contains(value.CacheKey))
			{
				return false;
			}
		}
		Texture texture = widget.Texture;
		if (texture == null || !texture.IsValid)
		{
			return false;
		}
		double totalSeconds = _clock.Elapsed.TotalSeconds;
		string cacheKey = value.CacheKey;
		double num;
		lock (_lock)
		{
			if (!_lastSeenTick.TryGetValue(cacheKey, out var value2) || totalSeconds - value2 > 0.5)
			{
				_firstSeenTick[cacheKey] = totalSeconds;
			}
			_lastSeenTick[cacheKey] = totalSeconds;
			num = _firstSeenTick[cacheKey];
			if (totalSeconds - num < 1.2)
			{
				return false;
			}
			if (_lastAttemptTick.TryGetValue(cacheKey, out var value3) && totalSeconds - value3 < 0.35)
			{
				return false;
			}
			_lastAttemptTick[cacheKey] = totalSeconds;
		}
		byte[] array = PortraitCaptureService.CaptureFromTwoDTexture(texture);
		if (array == null || array.Length == 0)
		{
			return false;
		}
		if (!LooksRendered(array))
		{
			if (totalSeconds - num > 12.0)
			{
				lock (_lock)
				{
					if (_gaveUpWarned.Add(cacheKey))
					{
						Msg("Still capturing black for " + value.HeroName + "; leave the page and reopen it to retry.", 4294945280u);
					}
				}
			}
			return false;
		}
		return CompleteCapture(value, array, "Lord source export complete. Saved full-body encyclopedia source.png files in lord cache folders.");
	}

	private static bool LooksRendered(byte[] png)
	{
		try
		{
			int width;
			int height;
			byte[] array = PngReencode.DecodeToRgba(png, out width, out height);
			if (array == null || width <= 0 || height <= 0)
			{
				return false;
			}
			long lit = 0L;
			long alphaVisible = 0L;
			long sampled = 0L;
			for (int i = 0; i + 3 < array.Length; i += 16)
			{
				byte b = array[i];
				byte b2 = array[i + 1];
				byte b3 = array[i + 2];
				byte b4 = array[i + 3];
				sampled++;
				if (b4 > 16)
				{
					alphaVisible++;
					if (b > 14 || b2 > 14 || b3 > 14)
					{
						lit++;
					}
				}
			}
			if (sampled == 0L)
			{
				return false;
			}
			double alphaRatio = (double)alphaVisible / (double)sampled;
			double litRatio = (double)lit / (double)sampled;
			return litRatio >= 0.004 || (alphaRatio >= 0.01 && alphaRatio <= 0.95 && litRatio >= 0.001);
		}
		catch
		{
			return false;
		}
	}

	private static bool CompleteCapture(ExportItem item, byte[] sourceBytes, string completeMessage)
	{
		if (item == null || sourceBytes == null || sourceBytes.Length == 0)
		{
			return false;
		}
		PortraitCache.SaveSource(item.CacheKey, sourceBytes);
		WriteItemInfo(item);
		MaybeGeneratePortraitFromCapturedSource(item, sourceBytes);
		int captured;
		int count;
		lock (_lock)
		{
			if (!_capturedCacheKeys.Add(item.CacheKey))
			{
				return false;
			}
			_captured++;
			captured = _captured;
			count = _items.Count;
			while (_index < _items.Count && _capturedCacheKeys.Contains(_items[_index].CacheKey))
			{
				_index++;
			}
			if (_captured >= _items.Count || _index >= _items.Count)
			{
				_active = false;
			}
		}
		if (captured == 1 || captured % 10 == 0 || captured == count)
		{
			Msg("Exported lord source " + captured + "/" + count + ": " + item.HeroName, 4278246741u);
		}
		if (captured == count)
		{
			Msg(completeMessage, 4278246741u);
		}
		ConversationPortraitMixin.RefreshLordExportFromPatch();
		return true;
	}

	private static void MaybeGeneratePortraitFromCapturedSource(ExportItem item, byte[] sourceBytes)
	{
		if (item == null) return;
		bool requested;
		lock (_lock) requested = _generateAfterCaptureCacheKeys.Remove(item.CacheKey);
		if (requested) ReignPortraitBridge.RequestPortrait(item.Hero);
	}

	private static List<ExportItem> CollectLordItems()
	{
		List<ExportItem> list = new List<ExportItem>();
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Hero allAliveHero in Hero.AllAliveHeroes)
		{
			if (IsLordTagged(allAliveHero))
			{
				ExportItem exportItem = BuildItem(allAliveHero);
				if (exportItem != null && !string.IsNullOrWhiteSpace(exportItem.CacheKey) && hashSet.Add(exportItem.CacheKey))
				{
					list.Add(exportItem);
				}
			}
		}
		list.Sort((ExportItem a, ExportItem b) => string.Compare(a.CacheKey, b.CacheKey, StringComparison.OrdinalIgnoreCase));
		return list;
	}

	private static ExportItem BuildItem(Hero hero)
	{
		try
		{
			CharacterObject characterObject = hero?.CharacterObject;
			if (characterObject == null)
			{
				return null;
			}
			Equipment equipment = hero.CivilianEquipment ?? characterObject.FirstCivilianEquipment ?? characterObject.Equipment;
			CharacterCode characterCode = CharacterCode.CreateFrom(characterObject, equipment);
			if (characterCode == null || string.IsNullOrWhiteSpace(characterCode.Code))
			{
				return null;
			}
			CharacterImageIdentifierVM characterImageIdentifierVM = new CharacterImageIdentifierVM(characterCode);
			CharacterViewModel tableauModel = BuildTableauViewModel(hero, characterObject, equipment);
			PortraitPromptContext portraitPromptContext = PortraitPromptContext.FromHero(hero);
			return new ExportItem
			{
				HeroName = (hero.Name?.ToString() ?? characterObject.Name?.ToString() ?? characterObject.StringId),
				CharacterId = characterObject.StringId,
				CacheKey = CharacterCacheId.ForHero(hero),
				AppearanceKey = PortraitRequestRegistry.NormalizeKey(characterCode.Code),
				ImageId = (characterImageIdentifierVM.Id ?? characterCode.Code),
				AdditionalArgs = (characterImageIdentifierVM.AdditionalArgs ?? string.Empty),
				TextureProviderName = (characterImageIdentifierVM.TextureProviderName ?? "CharacterImageTextureProvider"),
				TableauModel = tableauModel,
				Hero = hero,
				AgeYears = portraitPromptContext?.AgeYears,
				CultureId = portraitPromptContext?.CultureId,
				ClanTier = portraitPromptContext?.ClanTier,
				SocialStation = portraitPromptContext?.SocialStation
			};
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to build lord export item: " + ex.Message);
			return null;
		}
	}

	private static CharacterViewModel BuildTableauViewModel(Hero hero, CharacterObject character, Equipment equipment)
	{
		try
		{
			HeroViewModel heroViewModel = new HeroViewModel();
			heroViewModel.FillFrom(hero, 0, useCivilian: true);
			if (equipment != null)
			{
				heroViewModel.SetEquipment(equipment);
			}
			heroViewModel.IsTableauEnabled = true;
			heroViewModel.HasMount = false;
			heroViewModel.MountCreationKey = string.Empty;
			return heroViewModel;
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to build lord tableau model: " + ex.Message);
			return null;
		}
	}

	private static bool IsLordTagged(Hero hero)
	{
		CharacterObject characterObject = hero?.CharacterObject;
		if (characterObject == null)
		{
			return false;
		}
		if (!string.IsNullOrWhiteSpace(characterObject.StringId) && characterObject.StringId.IndexOf("lord", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return true;
		}
		try
		{
			return characterObject.Occupation.ToString().IndexOf("Lord", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static int RevealToPlayer(List<ExportItem> items)
	{
		int num = 0;
		foreach (ExportItem item in items)
		{
			Hero hero = item.Hero;
			if (hero == null)
			{
				continue;
			}
			bool flag = false;
			try
			{
				if (!hero.IsKnownToPlayer)
				{
					hero.IsKnownToPlayer = true;
					flag = true;
				}
				if (!hero.HasMet)
				{
					hero.SetHasMet();
					flag = true;
				}
			}
			catch (Exception ex)
			{
				TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to reveal lord " + item.CacheKey + ": " + ex.Message);
			}
			if (flag)
			{
				num++;
			}
		}
		return num;
	}

	private static Dictionary<string, ExportItem> BuildCharacterIdIndex(List<ExportItem> items)
	{
		Dictionary<string, ExportItem> dictionary = new Dictionary<string, ExportItem>(StringComparer.OrdinalIgnoreCase);
		foreach (ExportItem item in items)
		{
			AddCharacterIdIndex(dictionary, item.CharacterId, item);
			AddCharacterIdIndex(dictionary, item.Hero?.StringId, item);
			AddCharacterIdIndex(dictionary, item.Hero?.CharacterObject?.StringId, item);
			AddCharacterIdIndex(dictionary, item.CacheKey, item);
		}
		return dictionary;
	}

	private static void AddCharacterIdIndex(Dictionary<string, ExportItem> map, string id, ExportItem item)
	{
		string key = NormalizeLookupId(id);
		if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
		{
			map.Add(key, item);
		}
	}

	private static string NormalizeLookupId(string id)
	{
		return (id ?? string.Empty).Trim();
	}

	private static void WriteManifest(List<ExportItem> items)
	{
		try
		{
			string path = Path.Combine(PortraitCache.CacheDir, "lord_source_export.tsv");
			using StreamWriter streamWriter = new StreamWriter(path, append: false);
			streamWriter.WriteLine("Name\tCharacterId\tCacheFolder\tAge\tCulture\tClanTier\tSocialStation\tSourcePath");
			foreach (ExportItem item in items)
			{
				streamWriter.WriteLine(SafeField(item.HeroName) + "\t" + SafeField(item.CharacterId) + "\t" + SafeField(item.CacheKey) + "\t" + item.AgeYears + "\t" + SafeField(item.CultureId) + "\t" + item.ClanTier + "\t" + SafeField(item.SocialStation) + "\t" + SafeField(PortraitCache.SourcePath(item.CacheKey)));
			}
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to write lord source manifest: " + ex.Message);
		}
	}

	private static void WriteItemInfo(ExportItem item)
	{
		try
		{
			string path = Path.Combine(PortraitCache.DirFor(item.CacheKey), "lord_export_info.txt");
			File.WriteAllText(path, "Name: " + item.HeroName + Environment.NewLine + "CharacterId: " + item.CharacterId + Environment.NewLine + "Age: " + item.AgeYears + Environment.NewLine + "Culture: " + item.CultureId + Environment.NewLine + "Clan tier: " + item.ClanTier + Environment.NewLine + "Social station: " + item.SocialStation + Environment.NewLine + "Source: " + PortraitCache.SourcePath(item.CacheKey) + Environment.NewLine);
		}
		catch (Exception ex)
		{
			TaleWorlds.Library.Debug.Print("[AIPortraits] Failed to write lord export info: " + ex.Message);
		}
	}

	private static string SafeField(string value)
	{
		return (value ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
	}

	private static void Msg(string text, uint color)
	{
		InformationManager.DisplayMessage(new InformationMessage("[AIPortraits] " + text, Color.FromUint(color)));
	}
}
