using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AIPortraits;

public static class PortraitIndex
{
	private const string AmbiguousMapping = "__AMBIGUOUS__";

	private static Dictionary<string, string> _map;

	private static string _loadedIndexPath;

	private static readonly object _lock = new object();

	private static string IndexPath => Path.Combine(PortraitCache.CacheDir, "index.json");

	private static string CollisionSafetyMarkerPath => Path.Combine(PortraitCache.CacheDir, ".portrait_index_collision_v2");

	public static string IndexPathForDiagnostics => IndexPath;

	private static void EnsureLoaded()
	{
		string currentIndexPath = IndexPath;
		if (_map != null && string.Equals(_loadedIndexPath, currentIndexPath, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		lock (_lock)
		{
			currentIndexPath = IndexPath;
			if (_map != null && string.Equals(_loadedIndexPath, currentIndexPath, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			try
			{
				PortraitCache.EnsureDirectory();
				if (File.Exists(currentIndexPath))
				{
					_map = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(currentIndexPath)) ?? new Dictionary<string, string>();
				}
				else
				{
					_map = new Dictionary<string, string>();
				}
				_loadedIndexPath = currentIndexPath;
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] PortraitIndex load failed: " + ex.Message);
				_map = new Dictionary<string, string>();
				_loadedIndexPath = currentIndexPath;
			}
		}
	}

	public static void Register(string appearanceKey, string cacheKey)
	{
		if (string.IsNullOrEmpty(appearanceKey) || string.IsNullOrEmpty(cacheKey))
		{
			return;
		}
		EnsureLoaded();
		lock (_lock)
		{
			if (RegisterMappingLocked(appearanceKey, cacheKey))
			{
				Save();
			}
		}
	}

	public static string Resolve(string appearanceKey)
	{
		if (string.IsNullOrEmpty(appearanceKey))
		{
			return null;
		}
		EnsureLoaded();
		lock (_lock)
		{
			string value;
			return _map.TryGetValue(appearanceKey, out value)
				&& !string.Equals(value, AmbiguousMapping, StringComparison.Ordinal)
				? value
				: null;
		}
	}

	public static int RegisterCachedCampaignHeroes()
	{
		EnsureLoaded();
		if (Campaign.Current == null)
		{
			return 0;
		}

		int changed = 0;
		try
		{
			foreach (Hero hero in Hero.AllAliveHeroes)
			{
				CharacterObject character = hero?.CharacterObject;
				if (character == null)
				{
					continue;
				}

				string cacheKey = CharacterCacheId.ForHero(hero);
				if (string.IsNullOrEmpty(cacheKey))
				{
					continue;
				}

				changed += RegisterHeroKeys(hero, character, cacheKey);
			}

			if (changed > 0)
			{
				Save();
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] RegisterCachedCampaignHeroes failed: " + ex.Message);
		}

		return changed;
	}

	public static int EnsureCollisionSafetyForCampaign()
	{
		EnsureLoaded();
		if (Campaign.Current == null || File.Exists(CollisionSafetyMarkerPath))
		{
			return 0;
		}

		int changed = RegisterCachedCampaignHeroes();
		try
		{
			File.WriteAllText(CollisionSafetyMarkerPath, DateTime.UtcNow.ToString("O"));
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Could not write collision-safety marker: " + ex.Message);
		}

		Debug.Print("[AIPortraits] Collision-safe portrait index prepared; changed entries=" + changed + ".");
		return changed;
	}

	private static int RegisterHeroKeys(Hero hero, CharacterObject character, string cacheKey)
	{
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		AddKey(keys, CharacterCode.CreateFrom(character)?.Code);

		Equipment heroCivilian = hero?.CivilianEquipment;
		if (heroCivilian != null)
		{
			AddKey(keys, CharacterCode.CreateFrom(character, heroCivilian)?.Code);
		}

		Equipment firstCivilian = character.FirstCivilianEquipment;
		if (firstCivilian != null)
		{
			AddKey(keys, CharacterCode.CreateFrom(character, firstCivilian)?.Code);
		}

		Equipment equipment = character.Equipment;
		if (equipment != null)
		{
			AddKey(keys, CharacterCode.CreateFrom(character, equipment)?.Code);
		}

		int changed = 0;
		lock (_lock)
		{
			foreach (string key in keys)
			{
				changed += RegisterMappingLocked(key, cacheKey) ? 1 : 0;
			}
		}

		return changed;
	}

	private static bool RegisterMappingLocked(string appearanceKey, string cacheKey)
	{
		if (!_map.TryGetValue(appearanceKey, out string existing))
		{
			_map[appearanceKey] = cacheKey;
			return true;
		}

		if (string.Equals(existing, AmbiguousMapping, StringComparison.Ordinal)
			|| string.Equals(existing, cacheKey, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		// A renamed hero may have a new display-name cache folder while retaining the
		// same stable Bannerlord id. That is still one identity and is safe to update.
		if (SameHeroIdentity(existing, cacheKey))
		{
			_map[appearanceKey] = cacheKey;
			return true;
		}

		// CharacterCode keys are not guaranteed unique. Procedural heroes and close
		// relatives can share one, so never let the last rendered hero steal it.
		_map[appearanceKey] = AmbiguousMapping;
		Debug.Print("[AIPortraits] Ambiguous appearance key disabled for "
			+ CacheIdentity(existing) + " and " + CacheIdentity(cacheKey) + ".");
		return true;
	}

	private static bool SameHeroIdentity(string left, string right)
	{
		string leftId = CacheIdentity(left);
		string rightId = CacheIdentity(right);
		return !string.IsNullOrWhiteSpace(leftId)
			&& string.Equals(leftId, rightId, StringComparison.OrdinalIgnoreCase);
	}

	private static string CacheIdentity(string cacheKey)
	{
		if (string.IsNullOrWhiteSpace(cacheKey))
		{
			return string.Empty;
		}

		int close = cacheKey.LastIndexOf(')');
		int open = close > 0 ? cacheKey.LastIndexOf('(', close) : -1;
		return open >= 0 && close > open + 1
			? cacheKey.Substring(open + 1, close - open - 1).Trim()
			: cacheKey.Trim();
	}

	private static void AddKey(HashSet<string> keys, string code)
	{
		string key = PortraitRequestRegistry.NormalizeKey(code);
		if (!string.IsNullOrEmpty(key))
		{
			keys.Add(key);
		}
	}

	private static void Save()
	{
		try
		{
			PortraitCache.EnsureDirectory();
			File.WriteAllText(IndexPath, JsonConvert.SerializeObject(_map, Formatting.Indented));
			_loadedIndexPath = IndexPath;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] PortraitIndex save failed: " + ex.Message);
		}
	}
}
