using System;
using Reign.Core.Contracts.Dialogue;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using ReignBeta.Court;
using ReignBeta.CastleChat;
using ReignBeta.Campaign;
using ReignBeta.PartyAgency;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Portraits.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
using ReignBeta.WorldSimulation.PartyAgency;
using ReignBeta.WorldSimulation.World;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
using ReignBeta.Court.UI;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignPartyChatScreenVM
    {
        [DataSourceProperty]
        public string BackgroundImageId
        {
            get { return _backgroundImageId; }
            set
            {
                if (value != _backgroundImageId)
                {
                    _backgroundImageId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BackgroundAdditionalArgs
        {
            get { return _backgroundAdditionalArgs; }
            set
            {
                if (value != _backgroundAdditionalArgs)
                {
                    _backgroundAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string BackgroundTextureProviderName
        {
            get { return _backgroundTextureProviderName; }
            set
            {
                if (value != _backgroundTextureProviderName)
                {
                    _backgroundTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public bool IsOverlayVisible
        {
            get { return _isOverlayVisible; }
            set
            {
                if (value != _isOverlayVisible)
                {
                    _isOverlayVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

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
        public string SelectedSummary
        {
            get { return _selectedSummary; }
            set
            {
                if (value != _selectedSummary)
                {
                    _selectedSummary = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string InputText
        {
            get { return Composer.State.Text; }
            set { Composer.SetText(value); OnPropertyChanged(nameof(SendLabel)); }
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
        public bool IsPortraitPreviewVisible
        {
            get { return _isPortraitPreviewVisible; }
            set
            {
                if (value != _isPortraitPreviewVisible)
                {
                    _isPortraitPreviewVisible = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewName
        {
            get { return _portraitPreviewName; }
            set
            {
                if (value != _portraitPreviewName)
                {
                    _portraitPreviewName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewId
        {
            get { return _portraitPreviewId; }
            set
            {
                if (value != _portraitPreviewId)
                {
                    _portraitPreviewId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewAdditionalArgs
        {
            get { return _portraitPreviewAdditionalArgs; }
            set
            {
                if (value != _portraitPreviewAdditionalArgs)
                {
                    _portraitPreviewAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitPreviewTextureProviderName
        {
            get { return _portraitPreviewTextureProviderName; }
            set
            {
                if (value != _portraitPreviewTextureProviderName)
                {
                    _portraitPreviewTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewImageWidth
        {
            get { return _portraitPreviewImageWidth; }
            set
            {
                if (Math.Abs(value - _portraitPreviewImageWidth) > 0.01f)
                {
                    _portraitPreviewImageWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewImageHeight
        {
            get { return _portraitPreviewImageHeight; }
            set
            {
                if (Math.Abs(value - _portraitPreviewImageHeight) > 0.01f)
                {
                    _portraitPreviewImageHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewFrameWidth
        {
            get { return _portraitPreviewFrameWidth; }
            set
            {
                if (Math.Abs(value - _portraitPreviewFrameWidth) > 0.01f)
                {
                    _portraitPreviewFrameWidth = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public float PortraitPreviewFrameHeight
        {
            get { return _portraitPreviewFrameHeight; }
            set
            {
                if (Math.Abs(value - _portraitPreviewFrameHeight) > 0.01f)
                {
                    _portraitPreviewFrameHeight = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }
    }
}
