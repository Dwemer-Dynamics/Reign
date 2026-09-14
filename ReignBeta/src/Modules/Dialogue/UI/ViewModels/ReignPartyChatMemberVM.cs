using System;
using AIPortraits;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignPartyChatMemberVM : ViewModel
    {
        private readonly Action<ReignPartyChatMemberVM> _toggle;
        private readonly Action<ReignPartyChatMemberVM> _previewPortrait;
        private readonly Action<ReignPartyChatMemberVM> _openEncyclopedia;
        private readonly Action<ReignPartyChatMemberVM> _generatePortrait;
        private readonly Action<ReignPartyChatMemberVM> _visitInPerson;
        private readonly string _occupation;
        private bool _isActive;
        private string _status;
        private string _portraitId;
        private string _portraitAdditionalArgs;
        private string _portraitTextureProviderName;

        public ReignPartyChatMemberVM(
            Hero hero,
            bool isActive,
            Action<ReignPartyChatMemberVM> toggle,
            Action<ReignPartyChatMemberVM> previewPortrait,
            Action<ReignPartyChatMemberVM> openEncyclopedia,
            Action<ReignPartyChatMemberVM> generatePortrait,
            Action<ReignPartyChatMemberVM> visitInPerson = null, string occupation = null)
        {
            Hero = hero;
            _toggle = toggle;
            _previewPortrait = previewPortrait;
            _openEncyclopedia = openEncyclopedia;
            _generatePortrait = generatePortrait;
            _visitInPerson = visitInPerson;
            _occupation = occupation;
            _status = isActive ? "Active" : "Available";
            IsActive = isActive;
            PortraitCacheKey = hero?.IsChild == true ? string.Empty : CharacterCacheId.ForHero(hero) ?? string.Empty;
            SetPortrait(hero?.IsChild == true ? null : BuildPortrait(hero));
        }

        public Hero Hero { get; }
        [DataSourceProperty] public bool CanVisitInPerson => _visitInPerson != null;
        [DataSourceProperty] public string DisplayStatus => _occupation ?? Status;
        [DataSourceProperty] public float StatusTop => CanVisitInPerson ? 56f : 65f;
        [DataSourceProperty] public float StatusHeight => CanVisitInPerson ? 21f : 28f;
        public void ExecuteVisitInPerson() => _visitInPerson?.Invoke(this);

        [DataSourceProperty]
        public string PortraitCacheKey { get; }

        [DataSourceProperty]
        public bool IsChild => Hero?.IsChild == true;

        [DataSourceProperty]
        public bool HasNativePortrait => !IsChild;

        [DataSourceProperty]
        public string Name => Hero?.Name?.ToString() ?? "Unknown";

        [DataSourceProperty]
        public string Clan => Hero?.Clan?.Name?.ToString() ?? "No clan";

        [DataSourceProperty]
        public string Status
        {
            get { return _status; }
            set
            {
                if (value != _status)
                {
                    _status = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(DisplayStatus));
                }
            }
        }

        [DataSourceProperty]
        public bool IsActive
        {
            get { return _isActive; }
            set
            {
                if (value != _isActive)
                {
                    _isActive = value;
                    Status = value ? "Active" : "Available";
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(IsInactive));
                    OnPropertyChanged(nameof(ActiveCheckText));
                }
            }
        }

        [DataSourceProperty]
        public bool IsInactive => !IsActive;

        [DataSourceProperty]
        public string ActiveCheckText => IsActive ? "X" : string.Empty;

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

        public void ExecuteToggleActive()
        {
            _toggle?.Invoke(this);
        }

        public void ExecutePreviewPortrait()
        {
            if (Hero?.IsChild == true) return;
            _previewPortrait?.Invoke(this);
        }

        public void ExecuteOpenEncyclopedia()
        {
            ReignLog.Info("Party chat encyclopedia click hero=" + Hero?.StringId);
            _openEncyclopedia?.Invoke(this);
        }

        public void ExecuteGenerateAIPortrait()
        {
            if (Hero?.IsChild == true) return;
            ReignLog.Info("Party chat look request hero=" + Hero?.StringId);
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
    }
}
