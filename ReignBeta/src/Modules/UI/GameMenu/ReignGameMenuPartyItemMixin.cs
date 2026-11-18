using AIPortraits;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Overlay;
using TaleWorlds.Library;

namespace ReignBeta.UI.GameMenu
{
    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignGameMenuPartyItemMixin : BaseViewModelMixin<GameMenuPartyItemVM>
    {
        private string _portraitCacheKey = string.Empty;
        private bool _isPortraitAvailable;

        [DataSourceProperty]
        public bool ReignIsRelationHidden
        {
            get
            {
                Hero hero = ViewModel?.Character?.HeroObject ?? ViewModel?.Party?.LeaderHero;
                return HiddenInformation.ReignHiddenInformationPolicy.ShouldMask(hero);
            }
        }

        [DataSourceProperty]
        public string ReignRelationText => string.Empty;

        public ReignGameMenuPartyItemMixin(GameMenuPartyItemVM vm) : base(vm)
        {
            RefreshPortraitIdentity();
        }

        [DataSourceProperty]
        public string ReignPortraitCacheKey
        {
            get { return _portraitCacheKey; }
            private set
            {
                value = value ?? string.Empty;
                if (value == _portraitCacheKey)
                {
                    return;
                }

                _portraitCacheKey = value;
                ViewModel?.OnPropertyChanged(nameof(ReignPortraitCacheKey));
            }
        }

        [DataSourceProperty]
        public bool IsReignPortraitAvailable
        {
            get { return _isPortraitAvailable; }
            private set
            {
                if (value == _isPortraitAvailable)
                {
                    return;
                }

                _isPortraitAvailable = value;
                ViewModel?.OnPropertyChanged(nameof(IsReignPortraitAvailable));
            }
        }

        public override void OnRefresh()
        {
            RefreshPortraitIdentity();
            ViewModel?.OnPropertyChanged(nameof(ReignIsRelationHidden));
            ViewModel?.OnPropertyChanged(nameof(ReignRelationText));
        }

        private void RefreshPortraitIdentity()
        {
            Hero hero = ViewModel?.Character?.HeroObject ?? ViewModel?.Party?.LeaderHero;
            string cacheKey = hero == null ? string.Empty : CharacterCacheId.ForHero(hero);
            ReignPortraitCacheKey = cacheKey;
            IsReignPortraitAvailable =
                !string.IsNullOrWhiteSpace(cacheKey)
                && PortraitCache.ExistsOnDisk(cacheKey);
        }
    }
}
