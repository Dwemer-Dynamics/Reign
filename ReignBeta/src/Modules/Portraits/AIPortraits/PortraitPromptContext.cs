using System;
using TaleWorlds.CampaignSystem;

namespace AIPortraits;

public sealed class PortraitPromptContext
{
	public string HeroStringId { get; }

	public string CharacterName { get; }

	public int? AgeYears { get; }

	public string CultureId { get; }

	public string CultureName { get; }

	public string Gender { get; }

	public int? ClanTier { get; }

	public string SocialStation { get; }

	public string Occupation { get; }

	public bool IsNotable { get; }

	public bool HasMetadata
	{
		get
		{
			if (!AgeYears.HasValue && string.IsNullOrWhiteSpace(CharacterName) && string.IsNullOrWhiteSpace(CultureId))
			{
				return !string.IsNullOrWhiteSpace(CultureName);
			}
			return true;
		}
	}

	private PortraitPromptContext(string heroStringId, string characterName, int? ageYears, string cultureId, string cultureName, string gender, int? clanTier, string socialStation, string occupation, bool isNotable)
	{
		HeroStringId = heroStringId;
		CharacterName = characterName;
		AgeYears = ageYears;
		CultureId = cultureId;
		CultureName = cultureName;
		Gender = gender;
		ClanTier = clanTier;
		SocialStation = socialStation;
		Occupation = occupation;
		IsNotable = isNotable;
	}

	public static PortraitPromptContext FromHero(Hero hero)
	{
		if (hero == null)
		{
			return null;
		}
		int? clanTier = GetClanTier(hero);
		return new PortraitPromptContext(
			hero.StringId,
			hero.Name?.ToString(),
			GetAgeYears(hero),
			GetCultureId(hero),
			GetCultureName(hero),
			GetGender(hero),
			clanTier,
			GetSocialStation(clanTier),
			GetOccupation(hero),
			hero.IsNotable);
	}

	private static string GetOccupation(Hero hero)
	{
		try
		{
			return hero.CharacterObject?.Occupation.ToString();
		}
		catch
		{
			return null;
		}
	}

	private static int? GetClanTier(Hero hero)
	{
		try
		{
			int tier = hero?.Clan?.Tier ?? 0;
			return tier > 0 ? tier : (int?)null;
		}
		catch
		{
			return null;
		}
	}

	public static string GetSocialStation(int? clanTier)
	{
		if (!clanTier.HasValue || clanTier.Value < 1)
		{
			return "unranked";
		}
		if (clanTier.Value <= 2)
		{
			return "landowner";
		}
		if (clanTier.Value <= 4)
		{
			return "lesser lord";
		}
		return "high noble";
	}

	private static string GetGender(Hero hero)
	{
		try
		{
			return hero.IsFemale ? "woman" : "man";
		}
		catch
		{
			return null;
		}
	}

	private static int? GetAgeYears(Hero hero)
	{
		try
		{
			float age = hero.Age;
			if (float.IsNaN(age) || float.IsInfinity(age) || age <= 0f)
			{
				return null;
			}
			int num = (int)Math.Round(age, MidpointRounding.AwayFromZero);
			if (num < 1 || num > 130)
			{
				return null;
			}
			return num;
		}
		catch
		{
			return null;
		}
	}

	private static string GetCultureId(Hero hero)
	{
		try
		{
			return (hero.Culture ?? hero.CharacterObject?.Culture)?.StringId;
		}
		catch
		{
			return null;
		}
	}

	private static string GetCultureName(Hero hero)
	{
		try
		{
			return (hero.Culture ?? hero.CharacterObject?.Culture)?.Name?.ToString();
		}
		catch
		{
			return null;
		}
	}
}
