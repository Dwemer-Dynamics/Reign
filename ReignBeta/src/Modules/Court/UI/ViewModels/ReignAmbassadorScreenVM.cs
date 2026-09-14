using System;
using System.Linq;
using AIPortraits;
using ReignBeta.Court;
using ReignBeta.Integration;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignAmbassadorScreenVM : ViewModel
    {
        private readonly ReignCourtCampaignBehavior _court;
        private readonly Action _close;
        private ReignAmbassadorCardVM _selected;
        private bool _busy;

        public ReignAmbassadorScreenVM(ReignCourtCampaignBehavior court, Action close)
        {
            _court = court ?? throw new ArgumentNullException(nameof(court));
            _close = close;
            Ambassadors = new MBBindingList<ReignAmbassadorCardVM>();
            _court.StateChanged += OnStateChanged;
            Refresh();
        }

        [DataSourceProperty] public MBBindingList<ReignAmbassadorCardVM> Ambassadors { get; }
        [DataSourceProperty] public ReignRoyalCouncilSeatVM ForeignAdvisor { get; private set; }
        [DataSourceProperty] public string PlayerBannerId { get; private set; } = string.Empty;
        [DataSourceProperty] public string PlayerBannerArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string PlayerBannerProvider { get; private set; } = string.Empty;
        [DataSourceProperty] public int AmbassadorCount => Ambassadors.Count;
        [DataSourceProperty] public bool ShowEmptyState => Ambassadors.Count == 0;
        [DataSourceProperty] public bool CanRemove => !_busy && _selected?.Posting?.IsActive == true;
        [DataSourceProperty] public bool CanSpeak => !_busy && _selected?.Posting?.IsResident == true;
        public int AutomationCount => Ambassadors.Count;
        public string AutomationPostingId => _selected?.Posting?.PostingId ?? string.Empty;
        public string AutomationOriginKingdomId => _selected?.Posting?.OriginKingdomStringId ?? string.Empty;
        public string AutomationHeroId => _selected?.Posting?.HeroStringId ?? string.Empty;
        public string AutomationStatus => _selected?.StatusText ?? string.Empty;

        public bool TrySelectAutomation(string value, out string error)
        {
            error = string.Empty;
            string query = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                error = "An ambassador posting, hero, or origin-kingdom identifier is required.";
                return false;
            }
            ReignAmbassadorCardVM match = Ambassadors.FirstOrDefault(card =>
                string.Equals(card.Posting?.PostingId, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(card.Posting?.HeroStringId, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(card.Posting?.OriginKingdomStringId, query, StringComparison.OrdinalIgnoreCase));
            if (match == null)
            {
                error = "No active ambassador matched '" + query + "'.";
                return false;
            }
            Select(match);
            return true;
        }

        public void ExecuteClose()
        {
            _close?.Invoke();
        }

        public override void OnFinalize()
        {
            _court.StateChanged -= OnStateChanged;
            base.OnFinalize();
        }

        public void ExecuteEstablishRelations()
        {
            if (_busy) return;
            var kingdoms = _court.GetForeignAmbassadorKingdoms();
            if (kingdoms.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] No other active kingdoms are available."));
                return;
            }
            var choices = kingdoms.Select(item => new InquiryElement(item,
                (item.Kingdom?.Name?.ToString() ?? "Unknown Kingdom")
                    + (item.Eligible ? string.Empty : " — Unavailable: " + item.Reason), null)).ToList();
            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                "Establish Relations",
                "Choose a foreign kingdom. Relations require peace, relation -30 or better with its ruler, and an available lord or lady with at least 50 Charm. The envoy is chosen from the highest available Charm band: 200+, 150–199, 100–149, then 50–99.",
                choices, true, 1, 1, "Select", "Cancel", selected =>
                {
                    ForeignKingdomAmbassadorEligibility choice = selected?.FirstOrDefault()?.Identifier as ForeignKingdomAmbassadorEligibility;
                    if (choice == null) return;
                    if (!choice.Eligible)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + choice.Reason));
                        return;
                    }
                    _ = EstablishAsync(choice.Kingdom);
                }, null, string.Empty, false), true, false);
        }

        public void ExecuteChooseForeignAdvisor()
        {
            ReignResidentAdvisorAppointment.Begin(_court, ReignCourtOffice.ForeignAdvisor, Refresh);
        }

        private async System.Threading.Tasks.Task EstablishAsync(Kingdom kingdom)
        {
            _busy = true;
            NotifyActionStates();
            string error = await _court.EstablishForeignAmbassadorAsync(kingdom).ConfigureAwait(false);
            await ReignMainThread.InvokeAsync(() =>
            {
                _busy = false;
                InformationManager.DisplayMessage(new InformationMessage(string.IsNullOrWhiteSpace(error)
                    ? "[Bannerlord Reign] Relations established. The foreign ruler's ambassador is traveling to your capital."
                    : "[Bannerlord Reign] " + error));
                Refresh();
            }).ConfigureAwait(false);
        }

        public void ExecuteSpeak()
        {
            OpenCard(_selected);
        }

        public void ExecuteRemoveAmbassador()
        {
            ForeignAmbassadorPosting posting = _selected?.Posting;
            if (_busy || posting == null) return;
            InformationManager.ShowInquiry(new InquiryData("Dismiss Ambassador",
                "End this ambassador's assignment and send them back to their kingdom?",
                true, true, "Dismiss", "Cancel", () => _ = DismissAsync(posting), null), true);
        }

        private async System.Threading.Tasks.Task DismissAsync(ForeignAmbassadorPosting posting)
        {
            _busy = true;
            NotifyActionStates();

            string error = await _court.DismissForeignAmbassadorAsync(posting);
            await ReignMainThread.InvokeAsync(() =>
            {
                _busy = false;
                if (!string.IsNullOrWhiteSpace(error))
                {
                    InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + error));
                    NotifyActionStates();
                    return;
                }

                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Ambassador dismissed."));
                Refresh();
            });
        }

        private void Refresh()
        {
            string selectedPostingId = _selected?.Posting?.PostingId ?? string.Empty;
            Ambassadors.Clear();
            foreach (ForeignAmbassadorPosting posting in _court.ForeignAmbassadors
                .Where(item => item?.IsActive == true)
                .OrderBy(item => Kingdom.All.FirstOrDefault(x => x?.StringId == item.OriginKingdomStringId)?.Name?.ToString() ?? item.OriginKingdomStringId))
            {
                Ambassadors.Add(new ReignAmbassadorCardVM(posting, OpenCard, RequestDismiss));
            }

            _selected = Ambassadors.FirstOrDefault(card => string.Equals(card.Posting?.PostingId,
                selectedPostingId, StringComparison.OrdinalIgnoreCase)) ?? Ambassadors.FirstOrDefault();
            foreach (ReignAmbassadorCardVM card in Ambassadors) card.IsSelected = card == _selected;
            CourtOfficeAssignment advisor = _court.Offices.FirstOrDefault(item => item.IsActive && item.Office == ReignCourtOffice.ForeignAdvisor);
            Hero advisorHero = string.IsNullOrWhiteSpace(advisor?.HeroStringId) ? null : Hero.AllAliveHeroes.FirstOrDefault(hero => string.Equals(hero.StringId, advisor.HeroStringId, StringComparison.OrdinalIgnoreCase));
            ForeignAdvisor = new ReignRoyalCouncilSeatVM("foreign", "FOREIGN ADVISOR", advisorHero,
                advisorHero != null && advisorHero.IsAlive && !advisorHero.IsPrisoner, ExecuteChooseForeignAdvisor);
            Banner playerNationBanner = Clan.PlayerClan?.Kingdom?.Banner ?? Clan.PlayerClan?.Banner;
            BannerImageIdentifierVM playerBanner = playerNationBanner == null
                ? null
                : new BannerImageIdentifierVM(playerNationBanner, true);
            PlayerBannerId = playerBanner?.Id ?? string.Empty;
            PlayerBannerArgs = playerBanner?.AdditionalArgs ?? string.Empty;
            PlayerBannerProvider = playerBanner?.TextureProviderName ?? string.Empty;
            OnPropertyChanged(nameof(ForeignAdvisor));
            OnPropertyChanged(nameof(PlayerBannerId));
            OnPropertyChanged(nameof(PlayerBannerArgs));
            OnPropertyChanged(nameof(PlayerBannerProvider));
            OnPropertyChanged(nameof(AmbassadorCount));
            OnPropertyChanged(nameof(ShowEmptyState));
            NotifyActionStates();
        }

        private void OnStateChanged()
        {
            Refresh();
        }

        private void OpenCard(ReignAmbassadorCardVM card)
        {
            if (card == null || _busy) return;
            Select(card);
            ForeignAmbassadorPosting posting = card.Posting;
            if (posting?.IsResident != true)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[Bannerlord Reign] " + (card.StatusText ?? "This ambassador is not available in the capital.")));
                return;
            }
            _court.OpenOfficialAmbassadorConversation(posting);
        }

        private void RequestDismiss(ReignAmbassadorCardVM card)
        {
            if (card == null || _busy) return;
            Select(card);
            ExecuteRemoveAmbassador();
        }

        private void Select(ReignAmbassadorCardVM card)
        {
            if (card == null) return;
            _selected = card;
            foreach (ReignAmbassadorCardVM item in Ambassadors) item.IsSelected = item == card;
            NotifyActionStates();
        }

        private void NotifyActionStates()
        {
            OnPropertyChanged(nameof(CanRemove));
            OnPropertyChanged(nameof(CanSpeak));
        }
    }

    public sealed class ReignAmbassadorCardVM : ViewModel
    {
        private readonly Action<ReignAmbassadorCardVM> _speak;
        private readonly Action<ReignAmbassadorCardVM> _dismiss;
        private bool _isSelected;

        public ReignAmbassadorCardVM(ForeignAmbassadorPosting posting, Action<ReignAmbassadorCardVM> speak,
            Action<ReignAmbassadorCardVM> dismiss)
        {
            Posting = posting;
            _speak = speak;
            _dismiss = dismiss;
            Hero hero = FindHero(posting?.HeroStringId);
            Kingdom target = Kingdom.All.FirstOrDefault(kingdom => kingdom?.StringId == posting?.OriginKingdomStringId);

            KingdomName = target?.Name?.ToString() ?? posting?.OriginKingdomStringId ?? "Unknown Realm";
            Name = hero?.Name?.ToString() ?? "Unknown Ambassador";
            ClanName = hero?.Clan?.Name?.ToString() ?? "House Unknown";
            StatusText = AmbassadorStatus(posting);
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            AmbassadorModel = BuildAmbassadorModel(hero);
            SetPortrait(hero);
            SetBanner(target);
        }

        public ForeignAmbassadorPosting Posting { get; }
        [DataSourceProperty] public string KingdomName { get; }
        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string ClanName { get; }
        [DataSourceProperty] public string StatusText { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public bool HasPortrait => !string.IsNullOrWhiteSpace(PortraitCacheKey);
        [DataSourceProperty] public CharacterViewModel AmbassadorModel { get; }
        [DataSourceProperty] public bool HasCharacterModel => AmbassadorModel != null;
        [DataSourceProperty] public bool CanSpeak => Posting?.IsResident == true;
        [DataSourceProperty] public bool CanDismiss => Posting?.IsActive == true;
        [DataSourceProperty] public string PortraitId { get; private set; } = string.Empty;
        [DataSourceProperty] public string PortraitArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string PortraitProvider { get; private set; } = string.Empty;
        [DataSourceProperty] public string BannerId { get; private set; } = string.Empty;
        [DataSourceProperty] public string BannerArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string BannerProvider { get; private set; } = string.Empty;

        [DataSourceProperty]
        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChangedWithValue(value);
            }
        }

        public void ExecuteSpeak()
        {
            _speak?.Invoke(this);
        }

        public void ExecuteDismiss()
        {
            _dismiss?.Invoke(this);
        }

        private static CharacterViewModel BuildAmbassadorModel(Hero hero)
        {
            if (hero?.CharacterObject == null) return null;
            try
            {
                HeroViewModel model = new HeroViewModel();
                model.FillFrom(hero, 0, useCivilian: true);
                Equipment equipment = hero.CivilianEquipment
                    ?? hero.CharacterObject.FirstCivilianEquipment
                    ?? hero.CharacterObject.Equipment;
                if (equipment != null) model.SetEquipment(equipment);
                model.IsTableauEnabled = true;
                model.HasMount = false;
                model.MountCreationKey = string.Empty;
                return model;
            }
            catch
            {
                return null;
            }
        }

        private void SetPortrait(Hero hero)
        {
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                ImageIdentifierVM portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
                PortraitId = portrait?.Id ?? string.Empty;
                PortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
                PortraitProvider = portrait?.TextureProviderName ?? string.Empty;
            }
            catch
            {
            }
        }

        private void SetBanner(Kingdom kingdom)
        {
            BannerImageIdentifierVM banner = kingdom?.Banner == null ? null : new BannerImageIdentifierVM(kingdom.Banner, true);
            BannerId = banner?.Id ?? string.Empty;
            BannerArgs = banner?.AdditionalArgs ?? string.Empty;
            BannerProvider = banner?.TextureProviderName ?? string.Empty;
        }

        private static Hero FindHero(string id)
        {
            return string.IsNullOrWhiteSpace(id)
                ? null
                : Hero.AllAliveHeroes.FirstOrDefault(hero => string.Equals(hero.StringId, id, StringComparison.OrdinalIgnoreCase));
        }

        private static string AmbassadorStatus(ForeignAmbassadorPosting posting)
        {
            if (posting == null) return "Unavailable";
            if (string.Equals(posting.Status, "traveling", StringComparison.OrdinalIgnoreCase))
                return "Traveling — arrives day " + posting.ArrivalDay.ToString("0.0");
            if (string.Equals(posting.Status, "sheltered", StringComparison.OrdinalIgnoreCase)) return "Sheltered pending a new capital";
            return "Resident ambassador";
        }
    }
}
