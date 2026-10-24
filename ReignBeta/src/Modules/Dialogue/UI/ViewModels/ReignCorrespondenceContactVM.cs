using System;
using AIPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCorrespondenceContactVM : ViewModel
    {
        private readonly Action<ReignCorrespondenceContactVM> _select;
        private bool _isSelected;
        private int _unreadCount;
        private string _status = string.Empty;

        public ReignCorrespondenceContactVM(Hero hero, Action<ReignCorrespondenceContactVM> select)
        {
            Hero = hero;
            _select = select;
            Name = hero?.Name?.ToString() ?? "Unknown";
            Subtitle = BuildSubtitle(hero);
            // An introduction by letter does not reveal an unmet person's face.
            // Never construct a native character tableau for the contact rail.
            PortraitCacheKey = hero?.HasMet == true ? CharacterCacheId.ForHero(hero) ?? string.Empty : string.Empty;
        }

        public Hero Hero { get; }
        public bool HasCorrespondence { get; set; }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string Subtitle { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public bool IsUnselected => !IsSelected;

        [DataSourceProperty]
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (value != _isSelected)
                {
                    _isSelected = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(IsUnselected));
                }
            }
        }

        [DataSourceProperty]
        public int UnreadCount
        {
            get { return _unreadCount; }
            set
            {
                if (value != _unreadCount)
                {
                    _unreadCount = value;
                    OnPropertyChangedWithValue(value);
                    OnPropertyChanged(nameof(HasUnread));
                    OnPropertyChanged(nameof(UnreadText));
                }
            }
        }

        [DataSourceProperty] public bool HasUnread => UnreadCount > 0;
        [DataSourceProperty] public string UnreadText => UnreadCount > 0 ? UnreadCount + " new" : string.Empty;

        [DataSourceProperty]
        public string Status
        {
            get { return _status; }
            set
            {
                value = value ?? string.Empty;
                if (value != _status)
                {
                    _status = value;
                    OnPropertyChangedWithValue(value);
                }
            }
        }

        public void ExecuteSelect()
        {
            _select?.Invoke(this);
        }

        private static string BuildSubtitle(Hero hero)
        {
            string clan = hero?.Clan?.Name?.ToString();
            string kingdom = hero?.Clan?.Kingdom?.InformalName?.ToString() ?? hero?.MapFaction?.Name?.ToString();
            if (!string.IsNullOrWhiteSpace(clan) && !string.IsNullOrWhiteSpace(kingdom)) return clan + " - " + kingdom;
            if (!string.IsNullOrWhiteSpace(clan)) return clan;
            return !string.IsNullOrWhiteSpace(kingdom) ? kingdom : hero?.Occupation.ToString() ?? string.Empty;
        }
    }
}
