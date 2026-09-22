#if !REIGN_EXCLUDE_COURT
using System;
using System.Collections.Generic;
using System.Linq;
using AIPortraits;
using Helpers;
using Reign.Core.Contracts.TrainingYard;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignTrainingYardScreenVM : ViewModel
    {
        private readonly Settlement _settlement;
        private readonly Action _close;
        private readonly Func<bool> _start;
        private readonly Action _stop;
        private string _selectedTrainerId = string.Empty;
        private string _statusText;
        private bool _isTraining;
        private int _elapsedHours;
        private long _totalXpDelivered;

        public ReignTrainingYardScreenVM(Settlement settlement, Action close, Func<bool> start, Action stop)
        {
            _settlement = settlement;
            _close = close;
            _start = start;
            _stop = stop;
            Trainers = new MBBindingList<ReignTrainingYardTrainerVM>();
            Troops = new MBBindingList<ReignTrainingYardTroopVM>();
            RefreshAll();
            StatusText = Trainers.Count == 0 ? "No eligible trainer is available."
                : Troops.Any(x => x.CanGainXp) ? "Choose a trainer to begin." : "Every troop is ready to upgrade.";
        }

        public MBBindingList<ReignTrainingYardTrainerVM> Trainers { get; }
        public MBBindingList<ReignTrainingYardTroopVM> Troops { get; }
        [DataSourceProperty] public int TrainerCount => Trainers.Count;
        [DataSourceProperty] public int TroopCount => Troops.Count;

        [DataSourceProperty] public string Title => "TRAINING YARD";
        [DataSourceProperty] public string TrainerHeading => "TRAINERS";
        [DataSourceProperty] public string TroopHeading => "PARTY TROOPS";
        [DataSourceProperty] public string SelectedTrainerName => SelectedTrainer?.Name ?? "NO TRAINER SELECTED";
        [DataSourceProperty] public string SelectedTrainerDetail => SelectedTrainer?.Detail ?? "Choose an eligible adult travelling with your party.";
        [DataSourceProperty] public string LeadershipText => SelectedTrainer == null ? "LEADERSHIP —" : "LEADERSHIP " + SelectedTrainer.Leadership;
        [DataSourceProperty] public string WeaponText => SelectedTrainer == null ? "BEST WEAPON —" : SelectedTrainer.BestWeaponName.ToUpperInvariant() + " " + SelectedTrainer.BestWeaponSkill;
        [DataSourceProperty] public string RateText => SelectedTrainer == null ? "0 XP PER TROOP / HOUR" : SelectedTrainer.HourlyXp + " XP PER TROOP / HOUR";
        [DataSourceProperty] public string ElapsedText => "ELAPSED  " + _elapsedHours + " HOURS";
        [DataSourceProperty] public string DeliveredText => "DELIVERED  " + _totalXpDelivered + " XP";
        [DataSourceProperty] public string StatusText { get => _statusText; private set { if (_statusText != value) { _statusText = value; OnPropertyChangedWithValue(value); } } }
        [DataSourceProperty] public bool IsTraining { get => _isTraining; private set { if (_isTraining != value) { _isTraining = value; OnPropertyChangedWithValue(value); NotifyActionState(); } } }
        [DataSourceProperty] public bool CanTrain => !IsTraining && SelectedTrainer?.HourlyXp > 0 && Troops.Any(x => x.CanGainXp);
        [DataSourceProperty] public bool CanStop => IsTraining;
        [DataSourceProperty] public bool HasSelectedTrainer => SelectedTrainer != null;
        [DataSourceProperty] public bool HasSelectedGeneratedPortrait => SelectedTrainer?.HasGeneratedPortrait == true;
        internal long TotalXpDelivered => _totalXpDelivered;
        [DataSourceProperty] public string SelectedPortraitCacheKey => SelectedTrainer?.PortraitCacheKey ?? string.Empty;
        [DataSourceProperty] public string SelectedPortraitId => SelectedTrainer?.PortraitId ?? string.Empty;
        [DataSourceProperty] public string SelectedPortraitAdditionalArgs => SelectedTrainer?.PortraitAdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string SelectedPortraitTextureProviderName => SelectedTrainer?.PortraitTextureProviderName ?? string.Empty;

        private ReignTrainingYardTrainerVM SelectedTrainer => Trainers.FirstOrDefault(x => x.Hero?.StringId == _selectedTrainerId);

        public void ExecuteClose() => _close?.Invoke();

        public void ExecuteTrain()
        {
            if (!CanTrain)
            {
                StatusText = SelectedTrainer == null ? "Choose a trainer first."
                    : SelectedTrainer.HourlyXp <= 0 ? "That trainer cannot provide any hourly XP."
                    : "Every troop is ready to upgrade.";
                return;
            }
            if (_start?.Invoke() != true)
            {
                StatusText = "Training could not begin because campaign time is unavailable.";
                return;
            }
            IsTraining = true;
            StatusText = SelectedTrainerName + " is drilling the party.";
        }

        public void ExecuteStop()
        {
            _stop?.Invoke();
            SetStopped("Training stopped. Campaign time is paused.");
        }

        internal bool ApplyHourlyTraining(out string stopReason)
        {
            stopReason = string.Empty;
            if (!IsTraining) return true;
            Hero trainer = EligibleTrainers(_settlement).FirstOrDefault(x => x.StringId == _selectedTrainerId);
            if (trainer == null)
            {
                stopReason = "Training stopped because the selected trainer is no longer eligible.";
                return false;
            }

            int hourlyRate = ReignTrainingYardTrainerVM.CalculateRate(trainer, out _, out _);
            if (hourlyRate <= 0)
            {
                stopReason = "Training stopped because the selected trainer cannot provide XP.";
                return false;
            }

            MobileParty party = MobileParty.MainParty;
            if (party?.MemberRoster == null)
            {
                stopReason = "Training stopped because the player party is unavailable.";
                return false;
            }

            int delivered = 0;
            foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster().ToList())
            {
                CharacterObject troop = element.Character;
                if (troop == null || troop.IsHero || element.Number <= 0) continue;
                if (!MobilePartyHelper.CanTroopGainXp(party.Party, troop, out int gainableXp)) continue;
                int award = ReignTrainingYardRules.CalculateStackAward(hourlyRate, element.Number, gainableXp);
                if (award <= 0) continue;
                party.MemberRoster.AddXpToTroop(troop, award);
                delivered += award;
            }

            _elapsedHours++;
            _totalXpDelivered += delivered;
            OnPropertyChanged(nameof(ElapsedText));
            OnPropertyChanged(nameof(DeliveredText));
            RefreshTroops();
            StatusText = delivered > 0
                ? SelectedTrainerName + " delivered " + delivered + " troop XP this hour."
                : "All eligible troops are ready to upgrade; time continues until you stop it.";
            return true;
        }

        internal void SetStopped(string reason)
        {
            IsTraining = false;
            if (!string.IsNullOrWhiteSpace(reason)) StatusText = reason;
        }

        internal void OnFrameTick(float dt)
        {
            // Campaign-hour events own XP delivery; frame ticks intentionally do not mutate troop state.
        }

        private void SelectTrainer(ReignTrainingYardTrainerVM trainer)
        {
            if (trainer == null || IsTraining) return;
            _selectedTrainerId = trainer.Hero?.StringId ?? string.Empty;
            foreach (ReignTrainingYardTrainerVM item in Trainers) item.SetSelected(item == trainer);
            NotifySelection();
            StatusText = trainer.HourlyXp > 0
                ? trainer.Name + " will grant " + trainer.HourlyXp + " XP to each eligible troop every hour."
                : trainer.Name + " cannot provide any hourly XP.";
        }

        internal bool TrySelectTrainer(string value, out string error)
        {
            error = string.Empty;
            string requested = (value ?? string.Empty).Trim();
            ReignTrainingYardTrainerVM trainer = string.IsNullOrWhiteSpace(requested)
                ? Trainers.FirstOrDefault()
                : Trainers.FirstOrDefault(x => string.Equals(x.Hero?.StringId, requested, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(x.Name, requested, StringComparison.OrdinalIgnoreCase));
            if (trainer == null)
            {
                error = "No matching eligible Training Yard trainer was found.";
                return false;
            }
            if (IsTraining)
            {
                error = "Stop training before changing trainers.";
                return false;
            }
            SelectTrainer(trainer);
            return true;
        }

        private void RefreshAll()
        {
            string selected = _selectedTrainerId;
            Trainers.Clear();
            foreach (Hero hero in EligibleTrainers(_settlement))
                Trainers.Add(new ReignTrainingYardTrainerVM(hero, SelectTrainer));
            ReignTrainingYardTrainerVM retained = Trainers.FirstOrDefault(x => x.Hero?.StringId == selected);
            if (retained != null)
            {
                _selectedTrainerId = selected;
                retained.SetSelected(true);
            }
            else _selectedTrainerId = string.Empty;
            RefreshTroops();
            NotifySelection();
        }

        private void RefreshTroops()
        {
            Troops.Clear();
            MobileParty party = MobileParty.MainParty;
            if (party?.MemberRoster != null)
            {
                foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster()
                    .Where(x => x.Character != null && !x.Character.IsHero && x.Number > 0)
                    .OrderBy(x => x.Character.Tier).ThenBy(x => x.Character.Name?.ToString()))
                    Troops.Add(new ReignTrainingYardTroopVM(party, element));
            }
            NotifyActionState();
        }

        private void NotifySelection()
        {
            OnPropertyChanged(nameof(SelectedTrainerName));
            OnPropertyChanged(nameof(SelectedTrainerDetail));
            OnPropertyChanged(nameof(LeadershipText));
            OnPropertyChanged(nameof(WeaponText));
            OnPropertyChanged(nameof(RateText));
            OnPropertyChanged(nameof(HasSelectedTrainer));
            OnPropertyChanged(nameof(HasSelectedGeneratedPortrait));
            OnPropertyChanged(nameof(SelectedPortraitCacheKey));
            OnPropertyChanged(nameof(SelectedPortraitId));
            OnPropertyChanged(nameof(SelectedPortraitAdditionalArgs));
            OnPropertyChanged(nameof(SelectedPortraitTextureProviderName));
            NotifyActionState();
        }

        private void NotifyActionState()
        {
            OnPropertyChanged(nameof(CanTrain));
            OnPropertyChanged(nameof(CanStop));
            foreach (ReignTrainingYardTrainerVM item in Trainers) item.NotifyInteractionState(IsTraining);
        }

        internal static IReadOnlyList<Hero> EligibleTrainers(Settlement settlement)
        {
            var rows = new Dictionary<string, Hero>(StringComparer.OrdinalIgnoreCase);
            MobileParty mainParty = MobileParty.MainParty;
            if (mainParty?.MemberRoster != null)
            {
                foreach (TroopRosterElement element in mainParty.MemberRoster.GetTroopRoster())
                    if (element.Character?.IsHero == true) AddIfEligible(rows, element.Character.HeroObject);
            }

            Kingdom kingdom = Clan.PlayerClan?.Kingdom;
            if (settlement != null && kingdom?.Leader == Hero.MainHero)
            {
                foreach (Hero hero in Hero.AllAliveHeroes.Where(x => x?.CurrentSettlement == settlement
                    && (x.Clan == Clan.PlayerClan || x.Clan?.Kingdom == kingdom)))
                    AddIfEligible(rows, hero);
            }
            return rows.Values.OrderByDescending(x => x.Clan == Clan.PlayerClan)
                .ThenByDescending(x => x.GetSkillValue(DefaultSkills.Leadership))
                .ThenBy(x => x.Name?.ToString()).ThenBy(x => x.StringId).ToList();
        }

        private static void AddIfEligible(IDictionary<string, Hero> rows, Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || !hero.IsActive || hero.IsChild || hero.IsPrisoner
                || hero.HeroState == Hero.CharacterStates.Disabled || string.IsNullOrWhiteSpace(hero.StringId)) return;
            rows[hero.StringId] = hero;
        }
    }

    public sealed class ReignTrainingYardTrainerVM : ViewModel
    {
        private readonly Action<ReignTrainingYardTrainerVM> _select;
        private readonly ImageIdentifierVM _portrait;
        private bool _isSelected;
        private bool _selectionLocked;

        public ReignTrainingYardTrainerVM(Hero hero, Action<ReignTrainingYardTrainerVM> select)
        {
            Hero = hero;
            _select = select;
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                _portrait = string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch { }
            HourlyXp = CalculateRate(hero, out int best, out string bestName);
            BestWeaponSkill = best;
            BestWeaponName = bestName;
        }

        public Hero Hero { get; }
        [DataSourceProperty] public string Name => Hero?.Name?.ToString() ?? "Unknown";
        [DataSourceProperty] public string Detail => "Leadership " + Leadership + "  •  " + BestWeaponName + " " + BestWeaponSkill;
        [DataSourceProperty] public int Leadership => Hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0;
        [DataSourceProperty] public int BestWeaponSkill { get; }
        [DataSourceProperty] public string BestWeaponName { get; }
        [DataSourceProperty] public int HourlyXp { get; }
        [DataSourceProperty] public string Rate => HourlyXp + " XP / TROOP / HOUR";
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId => _portrait?.Id ?? string.Empty;
        [DataSourceProperty] public string PortraitAdditionalArgs => _portrait?.AdditionalArgs ?? string.Empty;
        [DataSourceProperty] public string PortraitTextureProviderName => _portrait?.TextureProviderName ?? string.Empty;
        [DataSourceProperty] public bool HasGeneratedPortrait => TextureFactory.Has(PortraitCacheKey);
        [DataSourceProperty] public bool IsSelected => _isSelected;
        [DataSourceProperty] public bool CanSelect => !_selectionLocked;

        public void ExecuteSelect() { if (CanSelect) _select?.Invoke(this); }
        internal void SetSelected(bool value) { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        internal void NotifyInteractionState(bool locked) { if (_selectionLocked == locked) return; _selectionLocked = locked; OnPropertyChanged(nameof(CanSelect)); }

        internal static int CalculateRate(Hero hero, out int bestValue, out string bestName)
        {
            var skills = new[]
            {
                Tuple.Create("One-Handed", DefaultSkills.OneHanded), Tuple.Create("Two-Handed", DefaultSkills.TwoHanded),
                Tuple.Create("Polearm", DefaultSkills.Polearm), Tuple.Create("Bow", DefaultSkills.Bow),
                Tuple.Create("Crossbow", DefaultSkills.Crossbow), Tuple.Create("Throwing", DefaultSkills.Throwing)
            };
            Tuple<string, SkillObject> best = skills.OrderByDescending(x => hero?.GetSkillValue(x.Item2) ?? 0).First();
            bestName = best.Item1;
            bestValue = hero?.GetSkillValue(best.Item2) ?? 0;
            return ReignTrainingYardRules.CalculateHourlyXp(hero?.GetSkillValue(DefaultSkills.Leadership) ?? 0, bestValue);
        }
    }

    public sealed class ReignTrainingYardTroopVM : ViewModel
    {
        public ReignTrainingYardTroopVM(MobileParty party, TroopRosterElement element)
        {
            CharacterObject troop = element.Character;
            Name = troop?.Name?.ToString() ?? "Unknown troop";
            TierText = "TIER " + (troop?.Tier ?? 0);
            CountText = element.Number + " TROOPS";
            int index = party.MemberRoster.FindIndexOfTroop(troop);
            int current = index < 0 ? 0 : party.MemberRoster.GetElementXp(index);
            int gainable = 0;
            CanGainXp = troop != null && MobilePartyHelper.CanTroopGainXp(party.Party, troop, out gainable);
            int cap = current + (CanGainXp ? gainable : 0);
            ProgressText = CanGainXp ? current + " / " + cap + " XP" : "READY TO UPGRADE";
            StateText = CanGainXp ? "RECEIVING TRAINING" : "XP CAPPED";
        }

        [DataSourceProperty] public string Name { get; }
        [DataSourceProperty] public string TierText { get; }
        [DataSourceProperty] public string CountText { get; }
        [DataSourceProperty] public string ProgressText { get; }
        [DataSourceProperty] public string StateText { get; }
        [DataSourceProperty] public bool CanGainXp { get; }
    }
}
#endif
