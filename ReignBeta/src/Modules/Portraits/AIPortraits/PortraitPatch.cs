using System;
using AIEventsAndIntrigue.Settings;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using ReignBeta.Campaign;
using ReignBeta.Integration;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets;
using TaleWorlds.MountAndBlade.GauntletUI.Widgets.Encyclopedia;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace AIPortraits;

public static class PortraitPatch
{
	[HarmonyPatch(typeof(TextureWidget), "Texture", MethodType.Setter)]
	public static class TextureSetterPatch
	{
		internal static void Prefix(TextureWidget __instance, ref Texture value)
		{
			try
			{
				if (TryApplyChatBackground(__instance, ref value))
				{
					return;
				}
				bool flag = IsOurBox(__instance);
				if (__instance.Id == "AIPortraitsLordExportPortrait")
				{
					TraceTexture(__instance, "skip_export", null, null, value);
					return;
				}
				if (ShouldSuppressDuringLiveGauntlet())
				{
					if (flag)
					{
						UpdateRenderedAspect(__instance.Id, value);
					}
					return;
				}
				if (__instance.TextureProviderName != "CharacterImageTextureProvider" && !flag)
				{
					TraceTexture(__instance, "skip_provider", null, null, value);
					return;
				}
				AIEventsSettings instance = AIEventsSettings.Instance;
				if (instance != null && !instance.ModEnabled)
				{
					TraceTexture(__instance, "skip_portraits_disabled", null, null, value);
					if (flag)
					{
						UpdateRenderedAspect(__instance.Id, value);
					}
					return;
				}
				string value2 = Traverse.Create(__instance).Property("ImageId").GetValue<string>();
				string text = PortraitRequestRegistry.NormalizeKey(value2);
				string text2 = ResolveCacheKey(__instance, text);
				if (instance != null && instance.LogUIMovies && !string.IsNullOrEmpty(value2) && _seenDisplay.TryAdd(value2, 0))
				{
					string text3 = (string.IsNullOrEmpty(__instance.Id) ? "vanilla" : __instance.Id);
					ShowMsg("[DISP] " + text3 + " app…" + Tail(text) + " -> " + (string.IsNullOrEmpty(text2) ? "<none>" : text2), 4294967040u);
				}
				if (string.IsNullOrEmpty(text2))
				{
					TraceTexture(__instance, "skip_no_cache_key", text, text2, value);
					if (flag)
					{
						UpdateRenderedAspect(__instance.Id, value);
					}
					return;
				}
				if (!TextureFactory.Has(text2))
				{
					TraceTexture(__instance, "skip_texture_factory_missing", text, text2, value);
					if (flag)
					{
						UpdateRenderedAspect(__instance.Id, value);
					}
					return;
				}
				float num = (flag ? 0f : GetWidgetAspect(__instance, value));
				PortraitQualityTier tier = SelectTier(__instance, num);
				Texture texture = (flag ? TextureFactory.GetOrBuild(text2, tier) : TextureFactory.GetOrBuildForAspect(text2, num, ShouldContainPortrait(__instance, num, tier), tier));
				if (texture != null)
				{
					value = texture;
					TraceTexture(__instance, "applied_replacement", text, text2, texture);
					if (flag)
					{
						UpdateRenderedAspect(__instance.Id, texture);
					}
				}
				else
				{
					TraceTexture(__instance, "skip_texture_build_null", text, text2, value);
				}
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] Texture setter error: " + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(CharacterTableauWidget), "OnRender")]
	public static class CharacterTableauRenderPatch
	{
		private static void Postfix(CharacterTableauWidget __instance)
		{
			try
			{
				if (ShouldSuppressDuringLiveGauntlet())
				{
					return;
				}
				AIEventsSettings instance = AIEventsSettings.Instance;
				if (instance == null || instance.ModEnabled)
				{
					if (__instance.Id == "AIPortraitsLordExportTableau")
					{
						LordSourceExportService.TryCaptureTableauRendered(__instance);
					}
					else if (__instance is EncyclopediaCharacterTableauWidget)
					{
						LordSourceExportService.TryCaptureEncyclopediaTableauRendered(__instance, __instance.CharStringId);
					}
				}
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] Tableau render capture error: " + ex.Message);
			}
		}
	}

	[HarmonyPatch(typeof(TextureWidget), "OnRender")]
	public static class OnRenderPatch
	{
		private sealed class RefreshState { public long Revision = -1; public int LayoutAspect = -1; }
		private static readonly ConditionalWeakTable<TextureWidget, RefreshState> RefreshStates = new ConditionalWeakTable<TextureWidget, RefreshState>();
		private static void Postfix(TextureWidget __instance)
		{
			// Refresh existing native widgets after a background commit without reopening the screen.
			var state = RefreshStates.GetValue(__instance, widget => new RefreshState());
			long revision = TextureFactory.PortraitRevision;
			int layoutAspect = GetGameMenuPortraitOwner(__instance) == null ? 0
				: (int)Math.Round(GetWidgetAspect(__instance, null) * 1000f);
			if (state.Revision != revision || state.LayoutAspect != layoutAspect)
			{
				state.Revision = revision;
				state.LayoutAspect = layoutAspect;
				Texture replacement = __instance.Texture;
				TextureSetterPatch.Prefix(__instance, ref replacement);
				if (replacement != null && !ReferenceEquals(replacement, __instance.Texture))
				{
					try { Traverse.Create(__instance).Property("Texture").SetValue(replacement); }
					catch (Exception ex) { Debug.Print("[AIPortraits] Texture refresh failed: " + ex.Message); }
				}
			}
			// Rendering can export a reference for diagnostics, but never starts paid generation.
			if (__instance.Id != "AIPortraitsLordExportPortrait"
				|| __instance.TextureProviderName != "CharacterImageTextureProvider") return;
			try
			{
				string imageId = Traverse.Create(__instance).Property("ImageId").GetValue<string>();
				LordSourceExportService.TryCaptureRendered(__instance, imageId);
			}
			catch (Exception ex) { Debug.Print("[AIPortraits] Reference export failed: " + ex.Message); }
		}
	}

	public const string NpcBoxId = "AIPortraitsNpcPortrait";

	public const string PlayerBoxId = "AIPortraitsPlayerPortrait";

	public const string ZoomBoxId = "AIPortraitsZoomPortrait";

	public const string ChatPlayerBoxId = "ReignChatPlayerPortrait";

	public const string ChatNpcBoxId = "ReignChatNpcPortrait";

	public const string ChatZoomBoxId = "ReignChatZoomPortrait";

	public const string PartyChatPreviewBoxId = "ReignPartyChatPortraitPreview";

	public const string SocialEventPreviewBoxId = "AIEventsPortraitPreview";

	public const string IndividualChatBackgroundId = "ReignIndividualChatBackground";

	public const string PartyChatBackgroundId = "ReignPartyChatBackground";

	public const string AIInfluenceMemoryBoxId = "AIPortraitsAIInfluenceMemoryImage";

	public const string LordExportBoxId = "AIPortraitsLordExportPortrait";

	public const string LordExportTableauId = "AIPortraitsLordExportTableau";

	public const string MemoryBookBoxId = "AIPortraitsMemoryBookImage";

	public const string CourtPetitionNativePortraitId = "ReignCourtPetitionNativePortrait";

	private static volatile string _convNpcCacheKey;

	private static volatile string _convPlayerCacheKey;

	private static volatile string _zoomCacheKey;

	private static volatile string _chatPlayerCacheKey;

	private static volatile string _chatNpcCacheKey;

	private static volatile string _chatZoomCacheKey;

	private static volatile string _aiInfluenceMemoryCacheKey;

	private static volatile string _memoryBookCacheKey;

	private static bool _announcedNoMatch = false;

	private static readonly ConcurrentDictionary<string, byte> _seenDisplay = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> _waitingAnnounced = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> _readFailAnnounced = new ConcurrentDictionary<string, byte>();

	public static void SetNpcIdentity(string cacheKey)
	{
		_convNpcCacheKey = cacheKey;
	}

	public static void SetPlayerIdentity(string cacheKey)
	{
		_convPlayerCacheKey = cacheKey;
	}

	public static void SetZoomIdentity(string cacheKey)
	{
		_zoomCacheKey = cacheKey;
	}

	public static void SetChatPlayerIdentity(string cacheKey)
	{
		_chatPlayerCacheKey = cacheKey;
	}

	public static void SetChatNpcIdentity(string cacheKey)
	{
		_chatNpcCacheKey = cacheKey;
	}

	public static void SetChatZoomIdentity(string cacheKey)
	{
		_chatZoomCacheKey = cacheKey;
	}

	public static void SetAIInfluenceMemoryIdentity(string cacheKey)
	{
		_aiInfluenceMemoryCacheKey = cacheKey;
	}

	public static void SetMemoryBookIdentity(string cacheKey)
	{
		_memoryBookCacheKey = cacheKey;
	}

	private static string ResolveCacheKey(TextureWidget widget, string appearanceKey)
	{
		return widget.Id switch
		{
			"AIPortraitsNpcPortrait" => _convNpcCacheKey, 
			"AIPortraitsPlayerPortrait" => _convPlayerCacheKey, 
			"AIPortraitsZoomPortrait" => _zoomCacheKey, 
			"ReignChatPlayerPortrait" => _chatPlayerCacheKey,
			"ReignChatNpcPortrait" => _chatNpcCacheKey,
			"ReignChatZoomPortrait" => _chatZoomCacheKey,
			"AIPortraitsAIInfluenceMemoryImage" => _aiInfluenceMemoryCacheKey, 
			"AIPortraitsMemoryBookImage" => _memoryBookCacheKey, 
			_ => PortraitIndex.Resolve(appearanceKey), 
		};
	}

	private static bool IsOurBox(TextureWidget widget)
	{
		if (!(widget.Id == "AIPortraitsNpcPortrait") && !(widget.Id == "AIPortraitsPlayerPortrait") && !(widget.Id == "AIPortraitsZoomPortrait") && !(widget.Id == "ReignChatPlayerPortrait") && !(widget.Id == "ReignChatNpcPortrait") && !(widget.Id == "ReignChatZoomPortrait"))
		{
			return widget.Id == "AIPortraitsAIInfluenceMemoryImage";
		}
		return true;
	}

	private static bool TryApplyChatBackground(TextureWidget widget, ref Texture value)
	{
		if (widget == null)
		{
			return false;
		}

		string relativePath;
		string cacheKey;
		if (widget.Id == IndividualChatBackgroundId)
		{
			cacheKey = "individual_chat_background";
			relativePath = Path.Combine("GUI", "SpriteParts", "ui_reignbeta_individual", "reign_individual_chat_background.png");
		}
		else if (widget.Id == PartyChatBackgroundId)
		{
			cacheKey = "party_chat_background";
			relativePath = Path.Combine("GUI", "SpriteParts", "ui_reignbeta_party", "reign_party_chat_background.png");
		}
		else
		{
			return false;
		}

		string path = Path.Combine(BasePath.Name, "Modules", "ReignBeta", relativePath);
		Texture background = TextureFactory.GetOrBuildFile(cacheKey, path);
		if (background != null)
		{
			value = background;
		}
		return true;
	}

	private static bool ShouldSuppressDuringLiveGauntlet()
	{
		try
		{
			return ReignActionGauntlet.IsLiveDialogueGauntletActive;
		}
		catch
		{
			return false;
		}
	}

	private static bool ShouldTrace(TextureWidget widget)
	{
		try
		{
			if (AIEventsSettings.Instance?.TextureTraceEnabled != true || widget == null)
			{
				return false;
			}

			if (IsOurBox(widget))
			{
				return true;
			}

			string id = widget.Id ?? string.Empty;
			return id == "AIPortraitsMemoryBookImage" || id == "AIPortraitsLordExportPortrait";
		}
		catch
		{
			return false;
		}
	}

	private static void TraceTexture(TextureWidget widget, string reason, string appearanceKey, string cacheKey, Texture texture)
	{
		if (!ShouldTrace(widget))
		{
			return;
		}

		try
		{
			string preferredPath = null;
			bool preferredExists = false;
			bool customExists = false;
			bool factoryHas = false;
			if (!string.IsNullOrWhiteSpace(cacheKey))
			{
				preferredExists = PortraitCache.TryGetPreferredDiskPathForDiagnostics(cacheKey, out preferredPath);
				customExists = PortraitCache.HasCustomPortrait(cacheKey);
				factoryHas = TextureFactory.Has(cacheKey);
			}

			Debug.Print("[AIP-DIAG] texture " + reason
				+ " widget=" + (widget.Id ?? "<none>")
				+ " provider=" + (widget.TextureProviderName ?? "<none>")
				+ " imageTail=" + Tail(appearanceKey)
				+ " cacheKey=" + (cacheKey ?? "<none>")
				+ " preferredExists=" + preferredExists
				+ " customExists=" + customExists
				+ " textureFactoryHas=" + factoryHas
				+ " texture=" + DescribeTexture(texture)
				+ " path=" + (preferredPath ?? "<none>"));
		}
		catch (Exception ex)
		{
			Debug.Print("[AIP-DIAG] texture trace failed: " + ex.Message);
		}
	}

	private static string DescribeTexture(Texture texture)
	{
		if (texture == null)
		{
			return "<null>";
		}

		return "valid=" + texture.IsValid + " size=" + texture.Width + "x" + texture.Height;
	}

	private static float GetWidgetAspect(TextureWidget widget, Texture fallbackTexture)
	{
		float vectorAspect = GetVectorAspect(widget, "Size");
		if (vectorAspect > 0f)
		{
			return vectorAspect;
		}
		vectorAspect = GetVectorAspect(widget, "MeasuredSize");
		if (vectorAspect > 0f)
		{
			return vectorAspect;
		}
		if (TryGetFloatProperty(widget, "SuggestedWidth", out var value) && TryGetFloatProperty(widget, "SuggestedHeight", out var value2) && value > 1f && value2 > 1f)
		{
			return value / value2;
		}
		if (fallbackTexture != null && fallbackTexture.IsValid && fallbackTexture.Width > 0 && fallbackTexture.Height > 0)
		{
			return (float)fallbackTexture.Width / (float)fallbackTexture.Height;
		}
		return 0f;
	}

	private static PortraitQualityTier SelectTier(TextureWidget widget, float aspect)
	{
		if (ReignPortraits.PortraitDerivativeCore.UsesNativeWideThumbnail(
			widget?.Id, GetGameMenuPortraitOwner(widget), IsPartyWindowPortrait(aspect)))
		{
			return PortraitQualityTier.PartyThumbnail;
		}
		if (widget != null && (widget.Id == ZoomBoxId
			|| widget.Id == ChatZoomBoxId
			|| widget.Id == PartyChatPreviewBoxId
			|| widget.Id == SocialEventPreviewBoxId))
		{
			return PortraitQualityTier.Zoom;
		}
		if (widget != null && (widget.Id == ChatPlayerBoxId || widget.Id == ChatNpcBoxId))
		{
			return PortraitQualityTier.Portrait;
		}

		return GetWidgetMaximumDimension(widget) > 384f ? PortraitQualityTier.Portrait : PortraitQualityTier.Thumbnail;
	}

	private static float GetWidgetMaximumDimension(TextureWidget widget)
	{
		if (widget == null)
		{
			return 0f;
		}
		float maximum = GetVectorMaximum(widget, "Size");
		if (maximum > 1f)
		{
			return maximum;
		}
		maximum = GetVectorMaximum(widget, "MeasuredSize");
		if (maximum > 1f)
		{
			return maximum;
		}
		if (TryGetFloatProperty(widget, "SuggestedWidth", out var width) && TryGetFloatProperty(widget, "SuggestedHeight", out var height))
		{
			return Math.Max(width, height);
		}
		return 0f;
	}

	private static string GetGameMenuPortraitOwner(TextureWidget widget)
	{
		// Bind to the native row, not MapScreen globally or an ambiguous image id.
		// This also works before layout and on background-generation hot refresh.
		if (widget == null || widget.Id != "CharacterImage") return null;
		Widget parent = widget.ParentWidget;
		for (int depth = 0; parent != null && depth < 12; depth++, parent = parent.ParentWidget)
		{
			if (parent.GetType().Name == "GameMenuPartyItemButtonWidget")
				return "GameMenuPartyItemButtonWidget";
		}
		return null;
	}

	private static float GetVectorMaximum(TextureWidget widget, string propertyName)
	{
		try
		{
			object value = Traverse.Create(widget).Property(propertyName).GetValue<object>();
			if (TryReadFloatMember(value, "X", out var width) && TryReadFloatMember(value, "Y", out var height))
			{
				return Math.Max(width, height);
			}
		}
		catch
		{
		}
		return 0f;
	}

	private static bool IsPartyWindowPortrait(float aspect)
	{
		if (aspect < 1.25f)
		{
			return false;
		}
		try
		{
			string text = ScreenManager.TopScreen?.GetType().FullName ?? string.Empty;
			return text.IndexOf("GauntletPartyScreen", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		catch
		{
			return false;
		}
	}

	private static float GetVectorAspect(TextureWidget widget, string propertyName)
	{
		try
		{
			object value = Traverse.Create(widget).Property(propertyName).GetValue<object>();
			if (TryReadFloatMember(value, "X", out var value2) && TryReadFloatMember(value, "Y", out var value3) && value2 > 1f && value3 > 1f)
			{
				return value2 / value3;
			}
		}
		catch
		{
		}
		return 0f;
	}

	private static bool TryGetFloatProperty(object target, string propertyName, out float value)
	{
		value = 0f;
		try
		{
			PropertyInfo property = target.GetType().GetProperty(propertyName);
			if (property == null)
			{
				return false;
			}
			return TryConvertToFloat(property.GetValue(target, null), out value);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryReadFloatMember(object target, string memberName, out float value)
	{
		value = 0f;
		if (target == null)
		{
			return false;
		}
		try
		{
			Type type = target.GetType();
			PropertyInfo property = type.GetProperty(memberName);
			if (property != null)
			{
				return TryConvertToFloat(property.GetValue(target, null), out value);
			}
			FieldInfo field = type.GetField(memberName);
			if (field != null)
			{
				return TryConvertToFloat(field.GetValue(target), out value);
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryConvertToFloat(object raw, out float value)
	{
		value = 0f;
		if (raw == null)
		{
			return false;
		}
		try
		{
			value = Convert.ToSingle(raw);
			return !float.IsNaN(value) && !float.IsInfinity(value);
		}
		catch
		{
			return false;
		}
	}

	private static void UpdateRenderedAspect(string widgetId, Texture texture)
	{
		if (texture != null && texture.IsValid && texture.Width > 0 && texture.Height > 0)
		{
			ConversationPortraitMixin.SetRenderedTextureAspectFromPatch(widgetId, (float)texture.Width / (float)texture.Height);
		}
	}

	private static void ShowMsg(string text, uint color)
	{
		_ = ReignBeta.Integration.ReignMainThread.InvokeAsync(delegate
		{
			InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(color)));
		});
	}

	private static bool ShouldContainPortrait(TextureWidget widget, float aspect, PortraitQualityTier tier)
	{
		if (tier == PortraitQualityTier.Zoom)
		{
			return true;
		}
		if (widget != null && (widget.Id == "AIEventsActivePortrait" || widget.Id == "AIEventsAvailablePortrait"))
		{
			return true;
		}

		return IsPartyWindowPortrait(aspect);
	}

	private static string Tail(string s)
	{
		if (!string.IsNullOrEmpty(s))
		{
			if (s.Length > 10)
			{
				return s.Substring(s.Length - 10);
			}
			return s;
		}
		return "(none)";
	}
}
