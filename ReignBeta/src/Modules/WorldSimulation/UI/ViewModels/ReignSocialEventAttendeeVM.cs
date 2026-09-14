using System;
using AIPortraits;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignSocialEventAttendeeVM : ViewModel
    {
        private readonly Action<ReignSocialEventAttendeeVM> _toggle;
        private readonly Action<ReignSocialEventAttendeeVM> _previewPortrait;
        private readonly Action<ReignSocialEventAttendeeVM> _openEncyclopedia;
        private readonly Action<ReignSocialEventAttendeeVM> _generatePortrait;
        private bool _isActive;
        private string _portraitId;
        private string _portraitAdditionalArgs;
        private string _portraitTextureProviderName;

        public ReignSocialEventAttendeeVM(
            Hero hero,
            bool isActive,
            Action<ReignSocialEventAttendeeVM> toggle,
            Action<ReignSocialEventAttendeeVM> previewPortrait,
            Action<ReignSocialEventAttendeeVM> openEncyclopedia,
            Action<ReignSocialEventAttendeeVM> generatePortrait)
        {
            Hero = hero;
            _toggle = toggle;
            _previewPortrait = previewPortrait;
            _openEncyclopedia = openEncyclopedia;
            _generatePortrait = generatePortrait;
            Name = hero?.Name?.ToString() ?? "Unknown";
            Subtitle = BuildSubtitle(hero);
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            IsActive = isActive;
            SetPortrait(BuildPortrait(hero));
        }

        public Hero Hero { get; }

        [DataSourceProperty]
        public string PortraitCacheKey { get; }

        [DataSourceProperty]
        public string Name { get; private set; }

        [DataSourceProperty]
        public string Subtitle { get; private set; }

        [DataSourceProperty]
        public string Status => IsActive ? "Active" : "Available";

        [DataSourceProperty]
        public float PortraitCropImageWidth => 104f;

        [DataSourceProperty]
        public float PortraitCropImageHeight => 104f;

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (value != _isActive)
                {
                    _isActive = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(Status));
                }
            }
        }

        [DataSourceProperty]
        public string PortraitId
        {
            get { return _portraitId; }
            set
            {
                if (value != _portraitId)
                {
                    _portraitId = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitAdditionalArgs
        {
            get { return _portraitAdditionalArgs; }
            set
            {
                if (value != _portraitAdditionalArgs)
                {
                    _portraitAdditionalArgs = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        [DataSourceProperty]
        public string PortraitTextureProviderName
        {
            get { return _portraitTextureProviderName; }
            set
            {
                if (value != _portraitTextureProviderName)
                {
                    _portraitTextureProviderName = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public void ExecuteToggle()
        {
            _toggle?.Invoke(this);
        }

        public void ExecuteToggleActive()
        {
            ExecuteToggle();
        }

        public void ExecutePreviewPortrait()
        {
            _previewPortrait?.Invoke(this);
        }

        public void ExecuteOpenEncyclopedia()
        {
            ReignLog.Info("Social event encyclopedia click hero=" + Hero?.StringId);
            _openEncyclopedia?.Invoke(this);
        }

        public void ExecuteGenerateAIPortrait()
        {
            ReignLog.Info("Social event portrait request hero=" + Hero?.StringId);
            _generatePortrait?.Invoke(this);
        }

        private void SetPortrait(ImageIdentifierVM portrait)
        {
            PortraitId = portrait?.Id ?? string.Empty;
            PortraitAdditionalArgs = portrait?.AdditionalArgs ?? string.Empty;
            PortraitTextureProviderName = portrait?.TextureProviderName ?? string.Empty;
        }

        private static ImageIdentifierVM BuildPortrait(Hero hero)
        {
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                return string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildSubtitle(Hero hero)
        {
            if (hero == null)
            {
                return string.Empty;
            }

            string clan = hero.Clan?.Name?.ToString();
            string kingdom = hero.Clan?.Kingdom?.InformalName?.ToString() ?? hero.MapFaction?.Name?.ToString();
            if (!string.IsNullOrWhiteSpace(clan) && !string.IsNullOrWhiteSpace(kingdom))
            {
                return clan + " - " + kingdom;
            }

            if (!string.IsNullOrWhiteSpace(clan))
            {
                return clan;
            }

            return !string.IsNullOrWhiteSpace(kingdom) ? kingdom : hero.Occupation.ToString();
        }
    }
}
