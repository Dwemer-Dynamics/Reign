using System;
using System.Collections.Generic;
using System.Linq;
using AIPortraits;
using ReignBeta.Court;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignFamilyChambersScreenVM : ViewModel
    {
        private readonly Action _close;
        private readonly Action<FamilyChambersSessionRecord> _enter;
        private string _statusText;
        private bool _isBusy;
        private float _refreshClock;

        public ReignFamilyChambersScreenVM(Action close, Action<FamilyChambersSessionRecord> enter)
        {
            _close = close;
            _enter = enter;
            Adults = new MBBindingList<ReignFamilyAdultCardVM>();
            Children = new MBBindingList<ReignFamilyChildCardVM>();
            RefreshRoster();
        }

        public MBBindingList<ReignFamilyAdultCardVM> Adults { get; }
        public MBBindingList<ReignFamilyChildCardVM> Children { get; }
        [DataSourceProperty] public string Title => "REIGN FAMILY CHAMBERS";
        [DataSourceProperty] public string Subtitle => "Choose up to four members of the household";
        [DataSourceProperty] public string StatusText { get => _statusText; set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsBusy { get => _isBusy; set { if (_isBusy != value) { _isBusy = value; OnPropertyChangedWithValue(value); OnPropertyChanged(nameof(CanEnterScene)); } } }
        [DataSourceProperty] public bool CanEnterScene => !IsBusy && SelectedCount >= 1 && SelectedCount <= 4;
        [DataSourceProperty] public int SelectedCount => Adults.Count(card => card.IsSelected) + Children.Count(card => card.IsSelected);
        [DataSourceProperty] public string SelectionText => SelectedCount + " / 4 SELECTED";

        public void OnFrameTick(float dt)
        {
            _refreshClock += dt;
            if (_refreshClock < 1f || IsBusy) return;
            _refreshClock = 0f;
        }

        public void ExecuteClose() => _close?.Invoke();

        public void ExecuteEnterScene()
        {
            if (!CanEnterScene)
            {
                StatusText = SelectedCount > 4 ? "Deselect family members until no more than four remain." : "Select at least one family member.";
                return;
            }
            List<Hero> selected = Adults.Where(card => card.IsSelected).Select(card => card.Hero)
                .Concat(Children.Where(card => card.IsSelected).Select(card => card.Hero)).Where(hero => hero != null).ToList();
            FamilyChambersSessionRecord session = ReignFamilyChambersCampaignBehavior.Instance?.CreateSession(selected);
            if (session == null)
            {
                StatusText = "The Family Chambers cannot be prepared here.";
                return;
            }
            IsBusy = true;
            InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] You make your way to the Family Chambers...", Color.FromUint(0xFFFFD36A)));
            _enter?.Invoke(session);
        }

        private void RefreshRoster()
        {
            IReadOnlyList<Hero> adults = ReignFamilyChambersCampaignBehavior.Instance?.GetPresentAdults() ?? Array.Empty<Hero>();
            IReadOnlyList<Hero> children = ReignFamilyChambersCampaignBehavior.Instance?.GetQualifiedChildren(adults) ?? Array.Empty<Hero>();
            var adultIds = new HashSet<string>(adults.Select(hero => hero.StringId), StringComparer.OrdinalIgnoreCase);
            Adults.Clear();
            Children.Clear();
            foreach (Hero adult in adults) Adults.Add(new ReignFamilyAdultCardVM(adult, ToggleAdult));
            foreach (Hero child in children) Children.Add(new ReignFamilyChildCardVM(child, adultIds, ToggleChild));
            RefreshSelectionState();
        }

        private void ToggleAdult(ReignFamilyAdultCardVM card)
        {
            if (card == null) return;
            if (card.IsSelected) card.IsSelected = false;
            else
            {
                card.IsSelected = true;
                foreach (ReignFamilyChildCardVM child in Children.Where(item => item.HasParent(card.Hero?.StringId))) child.IsSelected = true;
            }
            RefreshSelectionState();
        }

        private void ToggleChild(ReignFamilyChildCardVM card)
        {
            if (card == null) return;
            if (card.IsSelected) card.IsSelected = false;
            else
            {
                card.IsSelected = true;
                foreach (ReignFamilyAdultCardVM adult in Adults.Where(item => card.HasParent(item.Hero?.StringId))) adult.IsSelected = true;
            }
            RefreshSelectionState();
        }

        private void RefreshSelectionState()
        {
            int count = SelectedCount;
            StatusText = count == 0 ? "Select a noble or child to gather a family group."
                : count > 4 ? "The whole family is highlighted. Deselect individuals until four remain."
                : "The selected group is ready to enter the scene.";
            OnPropertyChanged(nameof(SelectedCount));
            OnPropertyChanged(nameof(SelectionText));
            OnPropertyChanged(nameof(CanEnterScene));
        }

        internal bool TryExecuteAutomationAction(string action, string value, out string error)
        {
            error = string.Empty;
            string normalized = (action ?? string.Empty).Trim().ToLowerInvariant().Replace('_', '-');
            if (normalized == "select-adult" || normalized == "toggle-adult")
            {
                ReignFamilyAdultCardVM card = Adults.FirstOrDefault(item => string.Equals(item.Hero?.StringId, value, StringComparison.OrdinalIgnoreCase));
                if (card == null) { error = "The requested adult is not in the Family Chambers roster."; return false; }
                ToggleAdult(card); return true;
            }
            if (normalized == "select-child" || normalized == "toggle-child")
            {
                ReignFamilyChildCardVM card = Children.FirstOrDefault(item => string.Equals(item.Hero?.StringId, value, StringComparison.OrdinalIgnoreCase));
                if (card == null) { error = "The requested child is not in the Family Chambers roster."; return false; }
                ToggleChild(card); return true;
            }
            if (normalized == "deselect")
            {
                ReignFamilyAdultCardVM adult = Adults.FirstOrDefault(item => string.Equals(item.Hero?.StringId, value, StringComparison.OrdinalIgnoreCase));
                ReignFamilyChildCardVM child = Children.FirstOrDefault(item => string.Equals(item.Hero?.StringId, value, StringComparison.OrdinalIgnoreCase));
                if (adult == null && child == null) { error = "The requested family member is not in the roster."; return false; }
                if (adult != null) adult.IsSelected = false; if (child != null) child.IsSelected = false; RefreshSelectionState(); return true;
            }
            error = "Supported Family Chambers actions are select-adult, select-child, toggle-adult, toggle-child, and deselect.";
            return false;
        }
    }

    public sealed class ReignFamilyAdultCardVM : ViewModel
    {
        private readonly Action<ReignFamilyAdultCardVM> _toggle;
        private bool _isSelected;
        private readonly ImageIdentifierVM _portrait;

        public ReignFamilyAdultCardVM(Hero hero, Action<ReignFamilyAdultCardVM> toggle)
        {
            Hero = hero;
            _toggle = toggle;
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                _portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch { }
        }

        public Hero Hero { get; }
        [DataSourceProperty] public string Name => Hero?.Name?.ToString() ?? "Unknown";
        [DataSourceProperty] public string Detail => (Hero?.Clan?.Name?.ToString() ?? "No clan") + "  •  Age " + Math.Max(0, (int)(Hero?.Age ?? 0f));
        [DataSourceProperty] public string Relation => Hero == null || TaleWorlds.CampaignSystem.Hero.MainHero == null ? string.Empty : "Relation " + Hero.GetRelation(TaleWorlds.CampaignSystem.Hero.MainHero);
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId => _portrait?.Id ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _portrait?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _portrait?.TextureProviderName ?? string.Empty;
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }
        public void ExecuteToggle() => _toggle?.Invoke(this);
    }

    public sealed class ReignFamilyChildCardVM : ViewModel
    {
        private readonly Action<ReignFamilyChildCardVM> _toggle;
        private bool _isSelected;
        private readonly HashSet<string> _parentIds;
        private readonly ImageIdentifierVM _portrait;

        public ReignFamilyChildCardVM(Hero hero, ISet<string> availableAdultIds, Action<ReignFamilyChildCardVM> toggle)
        {
            Hero = hero;
            _toggle = toggle;
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                _portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch { }
            _parentIds = new HashSet<string>(new[] { hero?.Father?.StringId, hero?.Mother?.StringId }
                .Where(id => !string.IsNullOrWhiteSpace(id) && availableAdultIds.Contains(id)), StringComparer.OrdinalIgnoreCase);
            List<string> names = new List<string>();
            if (hero?.Father != null) names.Add(hero.Father.Name?.ToString() ?? hero.Father.StringId);
            if (hero?.Mother != null) names.Add(hero.Mother.Name?.ToString() ?? hero.Mother.StringId);
            Parents = names.Count == 0 ? "Parents unknown" : "Parents: " + string.Join(" & ", names);
        }

        public Hero Hero { get; }
        [DataSourceProperty] public string Name => Hero?.Name?.ToString() ?? "Unknown child";
        [DataSourceProperty] public string AgeText => "Age " + Math.Max(0, (int)(Hero?.Age ?? 0f));
        [DataSourceProperty] public string Parents { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId => _portrait?.Id ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _portrait?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _portrait?.TextureProviderName ?? string.Empty;
        [DataSourceProperty] public bool IsSelected { get => _isSelected; set { if (_isSelected != value) { _isSelected = value; OnPropertyChangedWithValue(value); } } }
        public bool HasParent(string heroId) => !string.IsNullOrWhiteSpace(heroId) && _parentIds.Contains(heroId);
        public void ExecuteToggle() => _toggle?.Invoke(this);
    }
}
