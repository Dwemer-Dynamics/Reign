using System.Reflection;
using Bannerlord.UIExtenderEx.Attributes;
using Bannerlord.UIExtenderEx.ViewModels;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.CharacterDeveloper;
using TaleWorlds.CampaignSystem.ViewModelCollection.Education;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Items;
using TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.Recruitment;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.MarriageOfferPopup;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting;
using TaleWorlds.CampaignSystem.ViewModelCollection.WeaponCrafting.WeaponDesign;
using TaleWorlds.Library;

namespace ReignBeta.UI.HiddenInformation
{
    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignCharacterDeveloperMixin : BaseViewModelMixin<CharacterDeveloperVM>
    {
        public ReignCharacterDeveloperMixin(CharacterDeveloperVM vm) : base(vm) { }

        [DataSourceProperty]
        public bool ReignIsCurrentHeroSkillHidden => false;

        [DataSourceProperty]
        public bool ReignIsCurrentHeroSkillVisible => !ReignIsCurrentHeroSkillHidden;

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignIsCurrentHeroSkillHidden));
            ViewModel.OnPropertyChanged(nameof(ReignIsCurrentHeroSkillVisible));
        }
    }

    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignEncyclopediaSkillMixin : BaseViewModelMixin<EncyclopediaSkillVM>
    {
        public ReignEncyclopediaSkillMixin(EncyclopediaSkillVM vm) : base(vm) { }

        [DataSourceProperty]
        public string ReignSkillValueText => ViewModel.SkillValue.ToString();

        [DataSourceProperty]
        public bool ReignIsSkillValueHidden => false;

        [DataSourceProperty]
        public bool ReignIsSkillValueVisible => !ReignIsSkillValueHidden;

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignSkillValueText));
            ViewModel.OnPropertyChanged(nameof(ReignIsSkillValueHidden));
            ViewModel.OnPropertyChanged(nameof(ReignIsSkillValueVisible));
        }
    }

    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignCharacterDeveloperSkillMixin : BaseViewModelMixin<SkillVM>
    {
        private static readonly FieldInfo HeroItemField = typeof(SkillVM).GetField("_heroItem", BindingFlags.Instance | BindingFlags.NonPublic);

        public ReignCharacterDeveloperSkillMixin(SkillVM vm) : base(vm) { }

        [DataSourceProperty]
        public bool ReignIsSkillValueHidden => false;

        [DataSourceProperty]
        public bool ReignIsSkillValueVisible => !ReignIsSkillValueHidden;

        [DataSourceProperty]
        public string ReignSkillLevelText => ViewModel.Level.ToString();

        [DataSourceProperty]
        public string ReignSkillProgressText => ViewModel.ProgressText;

        [DataSourceProperty]
        public string ReignLearningRateText => ViewModel.CurrentLearningRateText;

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignIsSkillValueHidden));
            ViewModel.OnPropertyChanged(nameof(ReignIsSkillValueVisible));
            ViewModel.OnPropertyChanged(nameof(ReignSkillLevelText));
            ViewModel.OnPropertyChanged(nameof(ReignSkillProgressText));
            ViewModel.OnPropertyChanged(nameof(ReignLearningRateText));
        }

        private Hero GetHero()
        {
            try
            {
                return (HeroItemField?.GetValue(ViewModel) as CharacterDeveloperHeroItemVM)?.Hero;
            }
            catch (System.Exception ex)
            {
                ReignHiddenInformationPolicy.LogOnce("character-developer-hero", ex);
                return null;
            }
        }
    }

    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignCraftingHeroMixin : BaseViewModelMixin<CraftingAvailableHeroItemVM>
    {
        public ReignCraftingHeroMixin(CraftingAvailableHeroItemVM vm) : base(vm) { }

        [DataSourceProperty]
        public string ReignSmithySkillLevelText => ViewModel.SmithySkillLevel.ToString();

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignSmithySkillLevelText));
        }
    }

    [ViewModelMixin("RefreshValues")]
    internal sealed class ReignWeaponDesignMixin : BaseViewModelMixin<WeaponDesignVM>
    {
        public ReignWeaponDesignMixin(WeaponDesignVM vm) : base(vm) { }

        [DataSourceProperty]
        public bool ReignIsCraftingSkillHidden => false;

        [DataSourceProperty]
        public bool ReignIsCraftingSkillVisible => !ReignIsCraftingSkillHidden;

        [DataSourceProperty]
        public string ReignCraftingSkillText => ViewModel.CurrentCraftingSkillValueText;

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignIsCraftingSkillHidden));
            ViewModel.OnPropertyChanged(nameof(ReignIsCraftingSkillVisible));
            ViewModel.OnPropertyChanged(nameof(ReignCraftingSkillText));
        }
    }

    [ViewModelMixin]
    internal sealed class ReignEducationSkillMixin : BaseViewModelMixin<EducationGainedSkillItemVM>
    {
        public ReignEducationSkillMixin(EducationGainedSkillItemVM vm) : base(vm) { }

        [DataSourceProperty]
        public string ReignSkillValueText => ViewModel.SkillValueInt.ToString();

        public override void OnRefresh()
        {
            ViewModel.OnPropertyChanged(nameof(ReignSkillValueText));
        }
    }

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
