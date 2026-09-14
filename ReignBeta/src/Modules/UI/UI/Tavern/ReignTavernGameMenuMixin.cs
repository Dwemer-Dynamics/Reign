using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu;
using TaleWorlds.Library;

namespace ReignBeta.UI.Tavern
{
    [ViewModelMixin("Refresh")]
    internal sealed class ReignTavernGameMenuMixin : BaseViewModelMixin<GameMenuVM>
    {
        private const string TavernMenuId = "town_backstreet";
        private bool _isTavernArtVisible;
        private bool _wasInTavernMenu;
        private string _entryKey;
        private string _sceneImageId;
        private string _frameImageId;

        public ReignTavernGameMenuMixin(GameMenuVM vm) : base(vm)
        {
            UpdateTavernArt();
        }

        [DataSourceProperty]
        public bool IsReignTavernArtVisible
        {
            get { return _isTavernArtVisible; }
            private set
            {
                if (value != _isTavernArtVisible)
                {
                    _isTavernArtVisible = value;
                    ViewModel?.OnPropertyChanged(nameof(IsReignTavernArtVisible));
                }
            }
        }

        [DataSourceProperty]
        public string ReignTavernSceneImageId
        {
            get { return _sceneImageId; }
            private set
            {
                if (value != _sceneImageId)
                {
                    _sceneImageId = value;
                    ViewModel?.OnPropertyChanged(nameof(ReignTavernSceneImageId));
                }
            }
        }

        [DataSourceProperty]
        public string ReignTavernFrameImageId
        {
            get { return _frameImageId; }
            private set
            {
                if (value != _frameImageId)
                {
                    _frameImageId = value;
                    ViewModel?.OnPropertyChanged(nameof(ReignTavernFrameImageId));
                }
            }
        }

        public override void OnRefresh()
        {
            UpdateTavernArt();
        }

        private void UpdateTavernArt()
        {
            bool isTavernMenu = string.Equals(ViewModel?.MenuId, TavernMenuId, System.StringComparison.Ordinal);
            if (!isTavernMenu)
            {
                _wasInTavernMenu = false;
                _entryKey = null;
                ReignTavernSceneImageId = string.Empty;
                ReignTavernFrameImageId = string.Empty;
                IsReignTavernArtVisible = false;
                return;
            }

            Settlement settlement = Settlement.CurrentSettlement;
            string cultureId = settlement?.Culture?.StringId ?? string.Empty;
            string entryKey = (settlement?.StringId ?? string.Empty) + "|" + cultureId;
            if (!_wasInTavernMenu || _entryKey != entryKey || string.IsNullOrWhiteSpace(ReignTavernSceneImageId))
            {
                ReignTavernSceneImageId = ReignTavernArtTextureFactory.SelectRandomSceneImageId(cultureId);
                ReignTavernFrameImageId = ReignTavernArtTextureFactory.BuildFrameImageId();
                _entryKey = entryKey;
            }

            _wasInTavernMenu = true;
            IsReignTavernArtVisible =
                settlement?.IsTown == true
                && !string.IsNullOrWhiteSpace(ReignTavernSceneImageId)
                && !string.IsNullOrWhiteSpace(ReignTavernFrameImageId);
        }
    }
}
