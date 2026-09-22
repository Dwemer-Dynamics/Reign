using System;
using AIEventsAndIntrigue.Settings;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Conversation;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace AIPortraits;

[ViewModelMixin("Refresh")]
internal sealed class ConversationPortraitMixin : BaseViewModelMixin<MissionConversationVM>
{
	private ImageIdentifierVM _portrait;

	private bool _isVisible;

	private string _lastCode;

	private string _npcCacheKey;

	private string _npcId;

	private string _npcArgs;

	private string _npcProvider;

	private ImageIdentifierVM _playerPortrait;

	private bool _isPlayerVisible;

	private string _lastPlayerCode;

	private string _playerCacheKey;

	private string _playerId;

	private string _playerArgs;

	private string _playerProvider;

	private bool _isPortraitZoomVisible;

	private string _zoomCacheKey;

	private string _zoomId;

	private string _zoomArgs;

	private string _zoomProvider;

	private bool _isAIInfluenceMemoryVisible;

	private string _aiInfluenceMemoryCacheKey;

	private string _aiInfluenceMemoryId;

	private string _aiInfluenceMemoryArgs;

	private string _aiInfluenceMemoryProvider;

	private float _aiInfluenceMemoryImageWidth = 720f;

	private float _aiInfluenceMemoryImageHeight = 540f;

	private float _aiInfluenceMemoryFrameWidth = 746f;

	private float _aiInfluenceMemoryFrameHeight = 566f;

	private float _npcPortraitCropImageWidth = 188f;

	private float _npcPortraitCropImageHeight = 244f;

	private float _playerPortraitCropImageWidth = 188f;

	private float _playerPortraitCropImageHeight = 244f;

	private float _zoomPortraitImageWidth = 540f;

	private float _zoomPortraitImageHeight = 720f;

	private float _zoomFrameWidth = 576f;

	private float _zoomFrameHeight = 756f;

	private bool _isLordSourceExportVisible;

	private string _lordSourceExportCacheKey;

	private string _lordSourceExportId;

	private string _lordSourceExportArgs;

	private string _lordSourceExportProvider;

	private CharacterViewModel _lordSourceExportTableau;

	private static ConversationPortraitMixin _activeMixin;

	private static string _pendingAutomationProbeToken = string.Empty;

	private readonly string _automationProbeToken;

	[DataSourceProperty]
	public float NpcPortraitCropImageWidth
	{
		get
		{
			return _npcPortraitCropImageWidth;
		}
		private set
		{
			if (Math.Abs(_npcPortraitCropImageWidth - value) > 0.01f)
			{
				_npcPortraitCropImageWidth = value;
				base.ViewModel?.OnPropertyChanged("NpcPortraitCropImageWidth");
			}
		}
	}

	[DataSourceProperty]
	public float NpcPortraitCropImageHeight
	{
		get
		{
			return _npcPortraitCropImageHeight;
		}
		private set
		{
			if (Math.Abs(_npcPortraitCropImageHeight - value) > 0.01f)
			{
				_npcPortraitCropImageHeight = value;
				base.ViewModel?.OnPropertyChanged("NpcPortraitCropImageHeight");
			}
		}
	}

	[DataSourceProperty]
	public float PlayerPortraitCropImageWidth
	{
		get
		{
			return _playerPortraitCropImageWidth;
		}
		private set
		{
			if (Math.Abs(_playerPortraitCropImageWidth - value) > 0.01f)
			{
				_playerPortraitCropImageWidth = value;
				base.ViewModel?.OnPropertyChanged("PlayerPortraitCropImageWidth");
			}
		}
	}

	[DataSourceProperty]
	public float PlayerPortraitCropImageHeight
	{
		get
		{
			return _playerPortraitCropImageHeight;
		}
		private set
		{
			if (Math.Abs(_playerPortraitCropImageHeight - value) > 0.01f)
			{
				_playerPortraitCropImageHeight = value;
				base.ViewModel?.OnPropertyChanged("PlayerPortraitCropImageHeight");
			}
		}
	}

	[DataSourceProperty]
	public float ZoomPortraitImageWidth
	{
		get
		{
			return _zoomPortraitImageWidth;
		}
		private set
		{
			if (Math.Abs(_zoomPortraitImageWidth - value) > 0.01f)
			{
				_zoomPortraitImageWidth = value;
				base.ViewModel?.OnPropertyChanged("ZoomPortraitImageWidth");
			}
		}
	}

	[DataSourceProperty]
	public float ZoomPortraitImageHeight
	{
		get
		{
			return _zoomPortraitImageHeight;
		}
		private set
		{
			if (Math.Abs(_zoomPortraitImageHeight - value) > 0.01f)
			{
				_zoomPortraitImageHeight = value;
				base.ViewModel?.OnPropertyChanged("ZoomPortraitImageHeight");
			}
		}
	}

	[DataSourceProperty]
	public float ZoomFrameWidth
	{
		get
		{
			return _zoomFrameWidth;
		}
		private set
		{
			if (Math.Abs(_zoomFrameWidth - value) > 0.01f)
			{
				_zoomFrameWidth = value;
				base.ViewModel?.OnPropertyChanged("ZoomFrameWidth");
			}
		}
	}

	[DataSourceProperty]
	public float ZoomFrameHeight
	{
		get
		{
			return _zoomFrameHeight;
		}
		private set
		{
			if (Math.Abs(_zoomFrameHeight - value) > 0.01f)
			{
				_zoomFrameHeight = value;
				base.ViewModel?.OnPropertyChanged("ZoomFrameHeight");
			}
		}
	}

	[DataSourceProperty]
	public float AIInfluenceMemoryImageWidth
	{
		get
		{
			return _aiInfluenceMemoryImageWidth;
		}
		private set
		{
			if (Math.Abs(_aiInfluenceMemoryImageWidth - value) > 0.01f)
			{
				_aiInfluenceMemoryImageWidth = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryImageWidth");
			}
		}
	}

	[DataSourceProperty]
	public float AIInfluenceMemoryImageHeight
	{
		get
		{
			return _aiInfluenceMemoryImageHeight;
		}
		private set
		{
			if (Math.Abs(_aiInfluenceMemoryImageHeight - value) > 0.01f)
			{
				_aiInfluenceMemoryImageHeight = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryImageHeight");
			}
		}
	}

	[DataSourceProperty]
	public float AIInfluenceMemoryFrameWidth
	{
		get
		{
			return _aiInfluenceMemoryFrameWidth;
		}
		private set
		{
			if (Math.Abs(_aiInfluenceMemoryFrameWidth - value) > 0.01f)
			{
				_aiInfluenceMemoryFrameWidth = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryFrameWidth");
			}
		}
	}

	[DataSourceProperty]
	public float AIInfluenceMemoryFrameHeight
	{
		get
		{
			return _aiInfluenceMemoryFrameHeight;
		}
		private set
		{
			if (Math.Abs(_aiInfluenceMemoryFrameHeight - value) > 0.01f)
			{
				_aiInfluenceMemoryFrameHeight = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryFrameHeight");
			}
		}
	}

	[DataSourceProperty]
	public ImageIdentifierVM ConversationPortrait
	{
		get
		{
			return _portrait;
		}
		set
		{
			if (_portrait != value)
			{
				_portrait = value;
				ConversationPortraitId = value?.Id ?? string.Empty;
				ConversationPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
				ConversationPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
				base.ViewModel?.OnPropertyChanged("ConversationPortrait");
			}
		}
	}

	[DataSourceProperty]
	public string ConversationPortraitId
	{
		get
		{
			return _npcId;
		}
		set
		{
			if (_npcId != value)
			{
				_npcId = value;
				base.ViewModel?.OnPropertyChanged("ConversationPortraitId");
			}
		}
	}

	[DataSourceProperty]
	public string ConversationPortraitAdditionalArgs
	{
		get
		{
			return _npcArgs;
		}
		set
		{
			if (_npcArgs != value)
			{
				_npcArgs = value;
				base.ViewModel?.OnPropertyChanged("ConversationPortraitAdditionalArgs");
			}
		}
	}

	[DataSourceProperty]
	public string ConversationPortraitTextureProviderName
	{
		get
		{
			return _npcProvider;
		}
		set
		{
			if (_npcProvider != value)
			{
				_npcProvider = value;
				base.ViewModel?.OnPropertyChanged("ConversationPortraitTextureProviderName");
			}
		}
	}

	[DataSourceProperty]
	public bool IsConversationPortraitVisible
	{
		get
		{
			return _isVisible;
		}
		set
		{
			if (_isVisible != value)
			{
				_isVisible = value;
				base.ViewModel?.OnPropertyChanged("IsConversationPortraitVisible");
			}
		}
	}

	[DataSourceProperty]
	public ImageIdentifierVM PlayerPortrait
	{
		get
		{
			return _playerPortrait;
		}
		set
		{
			if (_playerPortrait != value)
			{
				_playerPortrait = value;
				PlayerPortraitId = value?.Id ?? string.Empty;
				PlayerPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
				PlayerPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
				base.ViewModel?.OnPropertyChanged("PlayerPortrait");
			}
		}
	}

	[DataSourceProperty]
	public string PlayerPortraitId
	{
		get
		{
			return _playerId;
		}
		set
		{
			if (_playerId != value)
			{
				_playerId = value;
				base.ViewModel?.OnPropertyChanged("PlayerPortraitId");
			}
		}
	}

	[DataSourceProperty]
	public string PlayerPortraitAdditionalArgs
	{
		get
		{
			return _playerArgs;
		}
		set
		{
			if (_playerArgs != value)
			{
				_playerArgs = value;
				base.ViewModel?.OnPropertyChanged("PlayerPortraitAdditionalArgs");
			}
		}
	}

	[DataSourceProperty]
	public string PlayerPortraitTextureProviderName
	{
		get
		{
			return _playerProvider;
		}
		set
		{
			if (_playerProvider != value)
			{
				_playerProvider = value;
				base.ViewModel?.OnPropertyChanged("PlayerPortraitTextureProviderName");
			}
		}
	}

	[DataSourceProperty]
	public bool IsPlayerPortraitVisible
	{
		get
		{
			return _isPlayerVisible;
		}
		set
		{
			if (_isPlayerVisible != value)
			{
				_isPlayerVisible = value;
				base.ViewModel?.OnPropertyChanged("IsPlayerPortraitVisible");
			}
		}
	}

	[DataSourceProperty]
	public string ZoomPortraitId
	{
		get
		{
			return _zoomId;
		}
		set
		{
			if (_zoomId != value)
			{
				_zoomId = value;
				base.ViewModel?.OnPropertyChanged("ZoomPortraitId");
			}
		}
	}

	[DataSourceProperty]
	public string ZoomPortraitAdditionalArgs
	{
		get
		{
			return _zoomArgs;
		}
		set
		{
			if (_zoomArgs != value)
			{
				_zoomArgs = value;
				base.ViewModel?.OnPropertyChanged("ZoomPortraitAdditionalArgs");
			}
		}
	}

	[DataSourceProperty]
	public string ZoomPortraitTextureProviderName
	{
		get
		{
			return _zoomProvider;
		}
		set
		{
			if (_zoomProvider != value)
			{
				_zoomProvider = value;
				base.ViewModel?.OnPropertyChanged("ZoomPortraitTextureProviderName");
			}
		}
	}

	[DataSourceProperty]
	public bool IsPortraitZoomVisible
	{
		get
		{
			return _isPortraitZoomVisible;
		}
		set
		{
			if (_isPortraitZoomVisible != value)
			{
				_isPortraitZoomVisible = value;
				base.ViewModel?.OnPropertyChanged("IsPortraitZoomVisible");
			}
		}
	}

	[DataSourceProperty]
	public string AIInfluenceMemoryId
	{
		get
		{
			return _aiInfluenceMemoryId;
		}
		set
		{
			if (_aiInfluenceMemoryId != value)
			{
				_aiInfluenceMemoryId = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryId");
			}
		}
	}

	[DataSourceProperty]
	public string AIInfluenceMemoryAdditionalArgs
	{
		get
		{
			return _aiInfluenceMemoryArgs;
		}
		set
		{
			if (_aiInfluenceMemoryArgs != value)
			{
				_aiInfluenceMemoryArgs = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryAdditionalArgs");
			}
		}
	}

	[DataSourceProperty]
	public string AIInfluenceMemoryTextureProviderName
	{
		get
		{
			return _aiInfluenceMemoryProvider;
		}
		set
		{
			if (_aiInfluenceMemoryProvider != value)
			{
				_aiInfluenceMemoryProvider = value;
				base.ViewModel?.OnPropertyChanged("AIInfluenceMemoryTextureProviderName");
			}
		}
	}

	[DataSourceProperty]
	public bool IsAIInfluenceMemoryVisible
	{
		get
		{
			return _isAIInfluenceMemoryVisible;
		}
		set
		{
			if (_isAIInfluenceMemoryVisible != value)
			{
				_isAIInfluenceMemoryVisible = value;
				base.ViewModel?.OnPropertyChanged("IsAIInfluenceMemoryVisible");
			}
		}
	}

	[DataSourceProperty]
	public string LordSourceExportId
	{
		get
		{
			return _lordSourceExportId;
		}
		set
		{
			if (_lordSourceExportId != value)
			{
				_lordSourceExportId = value;
				base.ViewModel?.OnPropertyChanged("LordSourceExportId");
			}
		}
	}

	[DataSourceProperty]
	public string LordSourceExportAdditionalArgs
	{
		get
		{
			return _lordSourceExportArgs;
		}
		set
		{
			if (_lordSourceExportArgs != value)
			{
				_lordSourceExportArgs = value;
				base.ViewModel?.OnPropertyChanged("LordSourceExportAdditionalArgs");
			}
		}
	}

	[DataSourceProperty]
	public string LordSourceExportTextureProviderName
	{
		get
		{
			return _lordSourceExportProvider;
		}
		set
		{
			if (_lordSourceExportProvider != value)
			{
				_lordSourceExportProvider = value;
				base.ViewModel?.OnPropertyChanged("LordSourceExportTextureProviderName");
			}
		}
	}

	[DataSourceProperty]
	public bool IsLordSourceExportVisible
	{
		get
		{
			return _isLordSourceExportVisible;
		}
		set
		{
			if (_isLordSourceExportVisible != value)
			{
				_isLordSourceExportVisible = value;
				base.ViewModel?.OnPropertyChanged("IsLordSourceExportVisible");
			}
		}
	}

	[DataSourceProperty]
	public CharacterViewModel LordSourceExportTableau
	{
		get
		{
			return _lordSourceExportTableau;
		}
		set
		{
			if (_lordSourceExportTableau != value)
			{
				_lordSourceExportTableau = value;
				base.ViewModel?.OnPropertyChanged("LordSourceExportTableau");
			}
		}
	}

	public ConversationPortraitMixin(MissionConversationVM vm)
		: base(vm)
	{
		_automationProbeToken = _pendingAutomationProbeToken;
		_pendingAutomationProbeToken = string.Empty;
		_activeMixin = this;
		Debug.Print("[AIPortraits] ConversationPortraitMixin attached to MissionConversationVM.");
		UpdatePortrait();
	}

	public static void ToggleNpcPortraitZoomFromPatch()
	{
		_activeMixin?.ExecuteToggleNpcPortraitZoom();
	}

	public static void TogglePlayerPortraitZoomFromPatch()
	{
		_activeMixin?.ExecuteTogglePlayerPortraitZoom();
	}

	public static void ClosePortraitZoomFromPatch()
	{
		_activeMixin?.ExecuteClosePortraitZoom();
	}

	public static void BeginAutomationProbeFromPatch(string token)
	{
		_activeMixin = null;
		_pendingAutomationProbeToken = token ?? string.Empty;
	}

	public static void ResetAutomationProbeFromPatch(string expectedToken)
	{
		if (string.IsNullOrWhiteSpace(expectedToken)) return;
		if (string.Equals(_pendingAutomationProbeToken, expectedToken, StringComparison.Ordinal))
			_pendingAutomationProbeToken = string.Empty;
		if (string.Equals(_activeMixin?._automationProbeToken, expectedToken, StringComparison.Ordinal))
			_activeMixin = null;
	}

	public static bool AutomationAttached => _activeMixin?.ViewModel != null;

	public static bool AutomationPortraitZoomVisible => _activeMixin?.IsPortraitZoomVisible == true;

	public static string AutomationProbeToken => _activeMixin?._automationProbeToken ?? string.Empty;

	public static string AutomationPortraitZoomSide
	{
		get
		{
			ConversationPortraitMixin active = _activeMixin;
			if (active?.IsPortraitZoomVisible != true) return string.Empty;
			if (string.Equals(active._zoomCacheKey, active._playerCacheKey, StringComparison.Ordinal)) return "player";
			if (string.Equals(active._zoomCacheKey, active._npcCacheKey, StringComparison.Ordinal)) return "npc";
			return "other";
		}
	}

	public static bool TryExecuteAutomationAction(string expectedToken, string action, out string error)
	{
		error = string.Empty;
		ConversationPortraitMixin active = _activeMixin;
		if (active?.ViewModel == null)
		{
			error = "The native conversation portrait mixin is not attached.";
			return false;
		}
		if (string.IsNullOrWhiteSpace(expectedToken)
			|| !string.Equals(active._automationProbeToken, expectedToken, StringComparison.Ordinal))
		{
			error = "The native Conversation screen is not the active calibration fixture.";
			return false;
		}

		switch ((action ?? string.Empty).Trim().Replace('_', '-').ToLowerInvariant())
		{
		case "toggle-player-portrait":
			active.ExecuteTogglePlayerPortraitZoom();
			return true;
		case "toggle-npc-portrait":
			active.ExecuteToggleNpcPortraitZoom();
			return true;
		case "close-portrait":
			active.ExecuteClosePortraitZoom();
			return true;
		default:
			error = "Unsupported native Conversation action '" + action
				+ "'. Use toggle-player-portrait, toggle-npc-portrait, or close-portrait.";
			return false;
		}
	}

	public static void ShowMemoryImageFromPatch(string path)
	{
		_activeMixin?.ShowMemoryImage(path);
	}

	public static void ShowAIInfluenceMemoryFromPatch(string path)
	{
		_activeMixin?.ShowAIInfluenceMemoryImage(path);
	}

	public static void CloseAIInfluenceMemoryFromPatch()
	{
		_activeMixin?.CloseAIInfluenceMemory();
	}

	public static void SetRenderedTextureAspectFromPatch(string widgetId, float aspectRatio)
	{
		_activeMixin?.SetRenderedTextureAspect(widgetId, aspectRatio);
	}

	public static void RefreshLordExportFromPatch()
	{
		_activeMixin?.UpdateLordSourceExport();
	}

	public void ExecuteToggleNpcPortraitZoom()
	{
		if (!IsModEnabled())
		{
			HideAllPortraitUi();
		}
		else
		{
			ShowOrToggleZoom(ConversationPortraitId, ConversationPortraitAdditionalArgs, ConversationPortraitTextureProviderName, _npcCacheKey);
		}
	}

	public void ExecuteTogglePlayerPortraitZoom()
	{
		if (!IsModEnabled())
		{
			HideAllPortraitUi();
		}
		else
		{
			ShowOrToggleZoom(PlayerPortraitId, PlayerPortraitAdditionalArgs, PlayerPortraitTextureProviderName, _playerCacheKey);
		}
	}

	public void ExecuteClosePortraitZoom()
	{
		ClosePortraitZoom();
	}

	public void ExecuteCloseAIInfluenceMemory()
	{
		CloseAIInfluenceMemory();
	}

	public override void OnRefresh()
	{
		UpdatePortrait();
	}

	private void UpdatePortrait()
	{
		try
		{
			RefreshDefaultAspectSizing();
			if (!IsModEnabled())
			{
				HideAllPortraitUi();
				return;
			}
			UpdatePlayerPortrait();
			UpdateLordSourceExport();
			CharacterObject oneToOneConversationCharacter = CharacterObject.OneToOneConversationCharacter;
            Hero knownResident = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.ConversationHero;
            if (knownResident != null) oneToOneConversationCharacter = knownResident.CharacterObject;
			if (oneToOneConversationCharacter == null)
			{
				IsConversationPortraitVisible = false;
				if (string.Equals(_zoomCacheKey, _npcCacheKey, StringComparison.Ordinal))
				{
					ClosePortraitZoom();
				}
				_npcCacheKey = null;
				PortraitPatch.SetNpcIdentity(null);
				return;
			}
			Equipment equipment = knownResident != null
                ? ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance.PortraitEquipment(knownResident)
                : oneToOneConversationCharacter.FirstCivilianEquipment ?? oneToOneConversationCharacter.Equipment;
			CharacterCode characterCode = CharacterCode.CreateFrom(oneToOneConversationCharacter, equipment);
			string text = characterCode?.Code;
			if (string.IsNullOrEmpty(text))
			{
				IsConversationPortraitVisible = false;
				if (string.Equals(_zoomCacheKey, _npcCacheKey, StringComparison.Ordinal))
				{
					ClosePortraitZoom();
				}
				_npcCacheKey = null;
				PortraitPatch.SetNpcIdentity(null);
				return;
			}
			string text2 = CharacterCacheId.ForCharacter(oneToOneConversationCharacter);
			if (!string.Equals(text2, _npcCacheKey, StringComparison.Ordinal) && string.Equals(_zoomCacheKey, _npcCacheKey, StringComparison.Ordinal))
			{
				ClosePortraitZoom();
			}
			_npcCacheKey = text2;
			SetNpcCropAspect(GetAspectRatioFor(text2));
			PortraitPatch.SetNpcIdentity(text2);
			PortraitIndex.Register(PortraitRequestRegistry.NormalizeKey(text), text2);
			if (!string.Equals(text, _lastCode, StringComparison.Ordinal))
			{
				_lastCode = text;
				ConversationPortrait = new CharacterImageIdentifierVM(characterCode);
			}
			IsConversationPortraitVisible = true;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] ConversationPortraitMixin error: " + ex.Message);
			IsConversationPortraitVisible = false;
		}
	}

	private void UpdatePlayerPortrait()
	{
		try
		{
			Hero mainHero = Hero.MainHero;
			if (mainHero?.CharacterObject == null)
			{
				IsPlayerPortraitVisible = false;
				if (string.Equals(_zoomCacheKey, _playerCacheKey, StringComparison.Ordinal))
				{
					ClosePortraitZoom();
				}
				_playerCacheKey = null;
				PortraitPatch.SetPlayerIdentity(null);
				return;
			}
			Equipment equipment = mainHero.CivilianEquipment ?? mainHero.CharacterObject.FirstCivilianEquipment ?? mainHero.CharacterObject.Equipment;
			CharacterCode characterCode = CharacterCode.CreateFrom(mainHero.CharacterObject, equipment);
			string text = characterCode?.Code;
			if (string.IsNullOrEmpty(text))
			{
				IsPlayerPortraitVisible = false;
				if (string.Equals(_zoomCacheKey, _playerCacheKey, StringComparison.Ordinal))
				{
					ClosePortraitZoom();
				}
				_playerCacheKey = null;
				PortraitPatch.SetPlayerIdentity(null);
				return;
			}
			string text2 = CharacterCacheId.ForHero(mainHero);
			if (!string.Equals(text2, _playerCacheKey, StringComparison.Ordinal) && string.Equals(_zoomCacheKey, _playerCacheKey, StringComparison.Ordinal))
			{
				ClosePortraitZoom();
			}
			_playerCacheKey = text2;
			SetPlayerCropAspect(GetAspectRatioFor(text2));
			PortraitPatch.SetPlayerIdentity(text2);
			PortraitIndex.Register(PortraitRequestRegistry.NormalizeKey(text), text2);
			if (!string.Equals(text, _lastPlayerCode, StringComparison.Ordinal))
			{
				_lastPlayerCode = text;
				PlayerPortrait = new CharacterImageIdentifierVM(characterCode);
			}
			IsPlayerPortraitVisible = true;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Player portrait error: " + ex.Message);
			IsPlayerPortraitVisible = false;
		}
	}

	private void UpdateLordSourceExport()
	{
		if (!IsModEnabled() || !LordSourceExportService.TryGetCurrentTableau(out var model, out var cacheKey))
		{
			IsLordSourceExportVisible = false;
			_lordSourceExportCacheKey = null;
			LordSourceExportTableau = null;
			LordSourceExportId = string.Empty;
			LordSourceExportAdditionalArgs = string.Empty;
			LordSourceExportTextureProviderName = string.Empty;
		}
		else
		{
			_lordSourceExportCacheKey = cacheKey;
			LordSourceExportTableau = model;
			IsLordSourceExportVisible = true;
		}
	}

	private static bool IsModEnabled()
	{
		return AIEventsSettings.Instance?.ModEnabled ?? true;
	}

	private void HideAllPortraitUi()
	{
		IsConversationPortraitVisible = false;
		IsPlayerPortraitVisible = false;
		IsLordSourceExportVisible = false;
		_lordSourceExportCacheKey = null;
		LordSourceExportTableau = null;
		LordSourceExportId = string.Empty;
		LordSourceExportAdditionalArgs = string.Empty;
		LordSourceExportTextureProviderName = string.Empty;
		ClosePortraitZoom();
		CloseAIInfluenceMemory();
		_npcCacheKey = null;
		_playerCacheKey = null;
		PortraitPatch.SetNpcIdentity(null);
		PortraitPatch.SetPlayerIdentity(null);
	}

	private void ShowOrToggleZoom(string id, string additionalArgs, string providerName, string cacheKey)
	{
		if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(providerName) && !string.IsNullOrEmpty(cacheKey))
		{
			SetZoomAspect(GetAspectRatioFor(cacheKey, PortraitQualityTier.Zoom));
			if (IsPortraitZoomVisible && string.Equals(_zoomCacheKey, cacheKey, StringComparison.Ordinal))
			{
				ClosePortraitZoom();
				return;
			}
			_zoomCacheKey = cacheKey;
			PortraitPatch.SetZoomIdentity(cacheKey);
			ZoomPortraitId = id;
			ZoomPortraitAdditionalArgs = additionalArgs ?? string.Empty;
			ZoomPortraitTextureProviderName = providerName;
			IsPortraitZoomVisible = true;
		}
	}

	private void ClosePortraitZoom()
	{
		if (IsPortraitZoomVisible || !string.IsNullOrEmpty(_zoomCacheKey))
		{
			IsPortraitZoomVisible = false;
			_zoomCacheKey = null;
			PortraitPatch.SetZoomIdentity(null);
		}
	}

	private void ShowAIInfluenceMemoryImage(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string text = MemoryService.ToMemoryKey(path);
		if (TextureFactory.Has(text))
		{
			string text2 = ((!string.IsNullOrEmpty(ConversationPortraitId)) ? ConversationPortraitId : PlayerPortraitId);
			string text3 = ((!string.IsNullOrEmpty(ConversationPortraitId)) ? ConversationPortraitAdditionalArgs : PlayerPortraitAdditionalArgs);
			string text4 = ((!string.IsNullOrEmpty(ConversationPortraitId)) ? ConversationPortraitTextureProviderName : PlayerPortraitTextureProviderName);
			if (!string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(text4))
			{
				_aiInfluenceMemoryCacheKey = text;
				PortraitPatch.SetAIInfluenceMemoryIdentity(text);
				SetAIInfluenceMemoryAspect(GetAspectRatioFor(text));
				AIInfluenceMemoryId = text2;
				AIInfluenceMemoryAdditionalArgs = text3 ?? string.Empty;
				AIInfluenceMemoryTextureProviderName = text4;
				IsAIInfluenceMemoryVisible = true;
			}
		}
	}

	private void CloseAIInfluenceMemory()
	{
		if (IsAIInfluenceMemoryVisible || !string.IsNullOrEmpty(_aiInfluenceMemoryCacheKey))
		{
			IsAIInfluenceMemoryVisible = false;
			_aiInfluenceMemoryCacheKey = null;
			PortraitPatch.SetAIInfluenceMemoryIdentity(null);
		}
	}

	private void ShowMemoryImage(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string text = MemoryService.ToMemoryKey(path);
		if (TextureFactory.Has(text))
		{
			string text2 = ((!string.IsNullOrEmpty(PlayerPortraitId)) ? PlayerPortraitId : ConversationPortraitId);
			string text3 = ((!string.IsNullOrEmpty(PlayerPortraitId)) ? PlayerPortraitAdditionalArgs : ConversationPortraitAdditionalArgs);
			string text4 = ((!string.IsNullOrEmpty(PlayerPortraitId)) ? PlayerPortraitTextureProviderName : ConversationPortraitTextureProviderName);
			if (!string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(text4))
			{
				_zoomCacheKey = text;
				PortraitPatch.SetZoomIdentity(text);
				SetZoomAspect(GetAspectRatioFor(text, PortraitQualityTier.Zoom));
				ZoomPortraitId = text2;
				ZoomPortraitAdditionalArgs = text3 ?? string.Empty;
				ZoomPortraitTextureProviderName = text4;
				IsPortraitZoomVisible = true;
			}
		}
	}

	private void RefreshDefaultAspectSizing()
	{
		float outputAspectRatio = GetOutputAspectRatio();
		SetNpcCropAspect(string.IsNullOrEmpty(_npcCacheKey) ? outputAspectRatio : GetAspectRatioFor(_npcCacheKey));
		SetPlayerCropAspect(string.IsNullOrEmpty(_playerCacheKey) ? outputAspectRatio : GetAspectRatioFor(_playerCacheKey));
		SetZoomAspect(string.IsNullOrEmpty(_zoomCacheKey) ? outputAspectRatio : GetAspectRatioFor(_zoomCacheKey, PortraitQualityTier.Zoom));
		SetAIInfluenceMemoryAspect(string.IsNullOrEmpty(_aiInfluenceMemoryCacheKey) ? 1.3333334f : GetAspectRatioFor(_aiInfluenceMemoryCacheKey));
	}

	private void SetNpcCropAspect(float ratio)
	{
		GetCoverCropDimensions(ratio, out var width, out var height);
		NpcPortraitCropImageWidth = width;
		NpcPortraitCropImageHeight = height;
	}

	private void SetPlayerCropAspect(float ratio)
	{
		GetCoverCropDimensions(ratio, out var width, out var height);
		PlayerPortraitCropImageWidth = width;
		PlayerPortraitCropImageHeight = height;
	}

	private static void GetCoverCropDimensions(float ratio, out float width, out float height)
	{
		if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
		{
			ratio = 0.75f;
		}

		const float plateWidth = 184f;
		const float plateHeight = 240f;
		const float overscan = 4f;
		const float coverWidth = plateWidth + overscan;
		const float coverHeight = plateHeight + overscan;
		const float coverAspect = coverWidth / coverHeight;

		// Keep the original rectangular texture intact and center-cover the complete
		// 184x240 plate, with a small overscan. The opaque-marble plate is rendered
		// above this source, so every transparent aperture pixel is guaranteed to have
		// portrait content behind it while the outer button clips any overflow.
		if (ratio >= coverAspect)
		{
			height = coverHeight;
			width = Math.Max(coverWidth, height * ratio);
		}
		else
		{
			width = coverWidth;
			height = Math.Max(coverHeight, width / ratio);
		}
	}

	private void SetZoomAspect(float ratio)
	{
		float num = 860f;
		float num2 = num / ratio;
		if (num2 > 920f)
		{
			num2 = 920f;
			num = num2 * ratio;
		}
		ZoomPortraitImageWidth = num;
		ZoomPortraitImageHeight = num2;
		ZoomFrameWidth = num + 36f;
		ZoomFrameHeight = num2 + 36f;
	}

	private void SetAIInfluenceMemoryAspect(float ratio)
	{
		if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
		{
			ratio = 1.3333334f;
		}
		float num = 720f;
		float num2 = num / ratio;
		if (num2 > 620f)
		{
			num2 = 620f;
			num = num2 * ratio;
		}
		AIInfluenceMemoryImageWidth = num;
		AIInfluenceMemoryImageHeight = num2;
		AIInfluenceMemoryFrameWidth = num + 26f;
		AIInfluenceMemoryFrameHeight = num2 + 26f;
	}

	private static float GetAspectRatioFor(string cacheKey, PortraitQualityTier tier = PortraitQualityTier.Thumbnail)
	{
		if (MemoryService.IsMemoryKey(cacheKey) && MemoryService.TryGetAspectRatio(cacheKey, out var ratio))
		{
			return ratio;
		}
		if (!PortraitDerivativeService.TryGetDerivativeAspectRatio(cacheKey, tier, out var aspectRatio)
			&& !PortraitCache.TryGetPortraitAspectRatio(cacheKey, out aspectRatio))
		{
			return GetOutputAspectRatio();
		}
		return aspectRatio;
	}

	private void SetRenderedTextureAspect(string widgetId, float aspectRatio)
	{
		if (!(aspectRatio <= 0f) && !float.IsNaN(aspectRatio) && !float.IsInfinity(aspectRatio))
		{
			switch (widgetId)
			{
			case "AIPortraitsNpcPortrait":
				SetNpcCropAspect(aspectRatio);
				break;
			case "AIPortraitsPlayerPortrait":
				SetPlayerCropAspect(aspectRatio);
				break;
			case "AIPortraitsZoomPortrait":
				SetZoomAspect(aspectRatio);
				break;
			case "AIPortraitsAIInfluenceMemoryImage":
				SetAIInfluenceMemoryAspect(aspectRatio);
				break;
			}
		}
	}

	private static float GetOutputAspectRatio()
	{
		string text = AIEventsSettings.Instance?.OutputSize;
		if (string.IsNullOrWhiteSpace(text))
		{
			return 0.75f;
		}
		string[] array = text.Split(new char[2] { 'x', 'X' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 2)
		{
			return 0.75f;
		}
		if (!int.TryParse(array[0], out var result) || !int.TryParse(array[1], out var result2) || result <= 0 || result2 <= 0)
		{
			return 0.75f;
		}
		return (float)result / (float)result2;
	}
}
