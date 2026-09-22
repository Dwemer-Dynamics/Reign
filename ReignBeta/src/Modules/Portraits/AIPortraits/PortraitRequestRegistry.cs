using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace AIPortraits;

public static class PortraitRequestRegistry
{
	private static readonly ConcurrentDictionary<string, byte> _requested = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, PortraitPromptContext> _promptContexts = new ConcurrentDictionary<string, PortraitPromptContext>();

	public static string GetCodeFor(Hero hero)
	{
		if (hero?.CharacterObject == null)
		{
			return null;
		}
		return NormalizeKey(CharacterCode.CreateFrom(hero.CharacterObject)?.Code);
	}

	public static string NormalizeKey(string code)
	{
		if (string.IsNullOrEmpty(code))
		{
			return code;
		}
		Match match = Regex.Match(code, "key=\"([^\"]+)\"", RegexOptions.IgnoreCase);
		if (match.Success && match.Groups[1].Value.Length > 0)
		{
			return "bpkey:" + match.Groups[1].Value;
		}
		string[] array = code.Split(new string[1] { "@---@" }, StringSplitOptions.None);
		if (array.Length < 4)
		{
			return ScrubVolatile(code);
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 2; i < array.Length; i++)
		{
			stringBuilder.Append(array[i]);
			stringBuilder.Append('|');
		}
		return ScrubVolatile(stringBuilder.ToString());
	}

	private static string ScrubVolatile(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return s;
		}
		RegexOptions options = RegexOptions.IgnoreCase;
		s = Regex.Replace(s, "age=\"[^\"]*\"", "", options);
		s = Regex.Replace(s, "weight=\"[^\"]*\"", "", options);
		s = Regex.Replace(s, "build=\"[^\"]*\"", "", options);
		return s;
	}

	public static void Request(string code)
	{
		Request(code, null);
	}

	public static void Request(string code, PortraitPromptContext promptContext)
	{
		if (!string.IsNullOrEmpty(code))
		{
			_requested[code] = 0;
			if (promptContext != null && promptContext.HasMetadata)
			{
				_promptContexts[code] = promptContext;
			}
			else
			{
				_promptContexts.TryRemove(code, out var _);
			}
		}
	}

	public static bool IsRequested(string code)
	{
		if (!string.IsNullOrEmpty(code))
		{
			return _requested.ContainsKey(code);
		}
		return false;
	}

	public static PortraitPromptContext GetPromptContext(string code)
	{
		if (string.IsNullOrEmpty(code))
		{
			return null;
		}
		if (!_promptContexts.TryGetValue(code, out var value))
		{
			return null;
		}
		return value;
	}

	public static bool HasAnyRequest()
	{
		return !_requested.IsEmpty;
	}

	public static void Clear(string code)
	{
		if (!string.IsNullOrEmpty(code))
		{
			_requested.TryRemove(code, out var _);
			_promptContexts.TryRemove(code, out var _);
		}
	}
}
