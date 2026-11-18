using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using Reign.Core.Contracts.Dialogue;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Campaign;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignIndividualChatScreenVM
    {
        [DataSourceProperty]
        public string Title
        {
            get { return _title; }
            set
            {
                if (value != _title)
                {
                    _title = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string HeroSubtitle
        {
            get { return _heroSubtitle; }
            set
            {
                if (value != _heroSubtitle)
                {
                    _heroSubtitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string LocationText
        {
            get { return _locationText; }
            set
            {
                if (value != _locationText)
                {
                    _locationText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerName
        {
            get { return _playerName; }
            set
            {
                if (value != _playerName)
                {
                    _playerName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerSubtitle
        {
            get { return _playerSubtitle; }
            set
            {
                if (value != _playerSubtitle)
                {
                    _playerSubtitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerInfo
        {
            get { return _playerInfo; }
            set
            {
                if (value != _playerInfo)
                {
                    _playerInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcName
        {
            get { return _npcName; }
            set
            {
                if (value != _npcName)
                {
                    _npcName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcSubtitle
        {
            get { return _npcSubtitle; }
            set
            {
                if (value != _npcSubtitle)
                {
                    _npcSubtitle = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcInfo
        {
            get { return _npcInfo; }
            set
            {
                if (value != _npcInfo)
                {
                    _npcInfo = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ImageIdentifierVM PlayerPortrait
        {
            get { return _playerPortrait; }
            set
            {
                if (value != _playerPortrait)
                {
                    _playerPortrait = value;
                    PlayerPortraitId = value?.Id ?? string.Empty;
                    PlayerPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
                    PlayerPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public ImageIdentifierVM NpcPortrait
        {
            get { return _npcPortrait; }
            set
            {
                if (value != _npcPortrait)
                {
                    _npcPortrait = value;
                    NpcPortraitId = value?.Id ?? string.Empty;
                    NpcPortraitAdditionalArgs = value?.AdditionalArgs ?? string.Empty;
                    NpcPortraitTextureProviderName = value?.TextureProviderName ?? string.Empty;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitId
        {
            get { return _playerPortraitId; }
            set
            {
                if (value != _playerPortraitId)
                {
                    _playerPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitAdditionalArgs
        {
            get { return _playerPortraitAdditionalArgs; }
            set
            {
                if (value != _playerPortraitAdditionalArgs)
                {
                    _playerPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PlayerPortraitTextureProviderName
        {
            get { return _playerPortraitTextureProviderName; }
            set
            {
                if (value != _playerPortraitTextureProviderName)
                {
                    _playerPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitId
        {
            get { return _npcPortraitId; }
            set
            {
                if (value != _npcPortraitId)
                {
                    _npcPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitAdditionalArgs
        {
            get { return _npcPortraitAdditionalArgs; }
            set
            {
                if (value != _npcPortraitAdditionalArgs)
                {
                    _npcPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string NpcPortraitTextureProviderName
        {
            get { return _npcPortraitTextureProviderName; }
            set
            {
                if (value != _npcPortraitTextureProviderName)
                {
                    _npcPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitId
        {
            get { return _zoomPortraitId; }
            set
            {
                if (value != _zoomPortraitId)
                {
                    _zoomPortraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitAdditionalArgs
        {
            get { return _zoomPortraitAdditionalArgs; }
            set
            {
                if (value != _zoomPortraitAdditionalArgs)
                {
                    _zoomPortraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string ZoomPortraitTextureProviderName
        {
            get { return _zoomPortraitTextureProviderName; }
            set
            {
                if (value != _zoomPortraitTextureProviderName)
                {
                    _zoomPortraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsPortraitZoomVisible
        {
            get { return _isPortraitZoomVisible; }
            set
            {
                if (value != _isPortraitZoomVisible)
                {
                    _isPortraitZoomVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BusyText
        {
            get { return _busyText; }
            set
            {
                if (value != _busyText)
                {
                    _busyText = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public int ChatScrollVersion
        {
            get { return _chatScrollVersion; }
            set
            {
                if (value != _chatScrollVersion)
                {
                    _chatScrollVersion = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PlayerPortraitCropImageWidth
        {
            get { return _playerPortraitCropImageWidth; }
            set
            {
                if (Math.Abs(value - _playerPortraitCropImageWidth) > 0.01f)
                {
                    _playerPortraitCropImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PlayerPortraitCropImageHeight
        {
            get { return _playerPortraitCropImageHeight; }
            set
            {
                if (Math.Abs(value - _playerPortraitCropImageHeight) > 0.01f)
                {
                    _playerPortraitCropImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float NpcPortraitCropImageWidth
        {
            get { return _npcPortraitCropImageWidth; }
            set
            {
                if (Math.Abs(value - _npcPortraitCropImageWidth) > 0.01f)
                {
                    _npcPortraitCropImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float NpcPortraitCropImageHeight
        {
            get { return _npcPortraitCropImageHeight; }
            set
            {
                if (Math.Abs(value - _npcPortraitCropImageHeight) > 0.01f)
                {
                    _npcPortraitCropImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomPortraitImageWidth
        {
            get { return _zoomPortraitImageWidth; }
            set
            {
                if (Math.Abs(value - _zoomPortraitImageWidth) > 0.01f)
                {
                    _zoomPortraitImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomPortraitImageHeight
        {
            get { return _zoomPortraitImageHeight; }
            set
            {
                if (Math.Abs(value - _zoomPortraitImageHeight) > 0.01f)
                {
                    _zoomPortraitImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomFrameWidth
        {
            get { return _zoomFrameWidth; }
            set
            {
                if (Math.Abs(value - _zoomFrameWidth) > 0.01f)
                {
                    _zoomFrameWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float ZoomFrameHeight
        {
            get { return _zoomFrameHeight; }
            set
            {
                if (Math.Abs(value - _zoomFrameHeight) > 0.01f)
                {
                    _zoomFrameHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }
}
