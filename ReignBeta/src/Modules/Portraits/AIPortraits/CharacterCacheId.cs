using System.IO;
using TaleWorlds.CampaignSystem;

namespace AIPortraits;

public static class CharacterCacheId
{
	public static string ForHero(Hero hero)
	{
		if (hero?.CharacterObject == null)
		{
			return null;
		}
		return Build(CacheDisplayName(hero), hero.CharacterObject.StringId);
	}

	public static string ForCharacter(CharacterObject character)
	{
		if (character == null)
		{
			return null;
		}
		string name = character.HeroObject != null
			? CacheDisplayName(character.HeroObject)
			: character.Name?.ToString();
		return Build(name, character.StringId);
	}

	private static string CacheDisplayName(Hero hero)
	{
		string name = hero?.Name?.ToString();
		string heroId = hero?.StringId ?? string.Empty;
		string clanId = hero?.Clan?.StringId ?? string.Empty;
		string clanName = hero?.Clan?.Name?.ToString();
		if (heroId.StartsWith("reign_court_")
			&& clanId.StartsWith("reign_house_")
			&& !string.IsNullOrWhiteSpace(name)
			&& !string.IsNullOrWhiteSpace(clanName)
			&& !name.EndsWith(" " + clanName, System.StringComparison.OrdinalIgnoreCase))
		{
			return name + " " + clanName;
		}
		return name;
	}

	private static string Build(string name, string stringId)
	{
		if (string.IsNullOrEmpty(stringId))
		{
			return null;
		}
		return Sanitize(name ?? "Unknown") + " (" + Sanitize(stringId) + ")";
	}

	public static string Fallback(string anyKey)
	{
		return "Unknown_" + Sanitize(anyKey ?? "portrait");
	}

	public static string Sanitize(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return "Unknown";
		}
		char[] invalidFileNameChars = Path.GetInvalidFileNameChars();
		foreach (char c in invalidFileNameChars)
		{
			s = s.Replace(c.ToString(), "");
		}
		return s.Replace(' ', '_');
	}
}
