using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.Library;

namespace ReignBeta.UI.HiddenInformation
{
    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignRecruitVolunteerOwnerMixin : BaseViewModelMixin<RecruitVolunteerOwnerVM>
    {
        public ReignRecruitVolunteerOwnerMixin(RecruitVolunteerOwnerVM vm) : base(vm) { }

        [DataSourceProperty]
        public string ReignRelationText => string.Empty;

        [DataSourceProperty]
        public bool ReignIsRelationHidden => ReignHiddenInformationPolicy.ShouldMask(ViewModel?.Hero);

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignRelationText));
            ViewModel.OnPropertyChanged(nameof(ReignIsRelationHidden));
        }
    }

    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignMarriageHeroMixin : BaseViewModelMixin<MarriageOfferPopupHeroVM>
    {
        public ReignMarriageHeroMixin(MarriageOfferPopupHeroVM vm) : base(vm) { }

        [DataSourceProperty]
        public bool ReignIsRelationHidden => ReignHiddenInformationPolicy.ShouldMask(ViewModel?.Hero);

        [DataSourceProperty]
        public bool ReignIsRelationVisible => !ReignIsRelationHidden;

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignIsRelationHidden));
            ViewModel.OnPropertyChanged(nameof(ReignIsRelationVisible));
        }
    }
}
