using System;
using AIPortraits;
using ReignBeta.World;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignDiplomacyAnnouncementVM : ViewModel
    {
        private readonly Action _acknowledge;

        public ReignDiplomacyAnnouncementVM(ReignDiplomacyAnnouncement announcement, Action acknowledge)
        {
            _acknowledge = acknowledge;
            announcement = announcement ?? new ReignDiplomacyAnnouncement();
            Title = announcement.Title;
            Outcome = announcement.Outcome;
            Summary = announcement.Summary;
            Terms = string.IsNullOrWhiteSpace(announcement.Terms) ? "No additional terms were announced." : announcement.Terms;
            ActorName = announcement.ActorName;
            ActorKingdom = announcement.ActorKingdomName;
            ActorReason = announcement.ActorPublicReason;
            TargetName = announcement.TargetName;
            TargetKingdom = announcement.TargetKingdomName;
            TargetReason = announcement.TargetPublicReason;
            DateText = "Day " + announcement.WorldDay.ToString("0");
            ShowTarget = !string.IsNullOrWhiteSpace(TargetName) || !string.IsNullOrWhiteSpace(TargetKingdom);
            SetPortrait(ReignObjectResolver.FindHero(announcement.ActorHeroStringId), true);
            SetPortrait(ReignObjectResolver.FindHero(announcement.TargetHeroStringId), false);
            SetBanner(ReignObjectResolver.FindKingdom(announcement.ActorKingdomStringId), true);
            SetBanner(ReignObjectResolver.FindKingdom(announcement.TargetKingdomStringId), false);
        }

        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Outcome { get; }
        [DataSourceProperty] public string Summary { get; }
        [DataSourceProperty] public string Terms { get; }
        [DataSourceProperty] public string ActorName { get; }
        [DataSourceProperty] public string ActorKingdom { get; }
        [DataSourceProperty] public string ActorReason { get; }
        [DataSourceProperty] public string TargetName { get; }
        [DataSourceProperty] public string TargetKingdom { get; }
        [DataSourceProperty] public string TargetReason { get; }
        [DataSourceProperty] public string DateText { get; }
        [DataSourceProperty] public bool ShowTarget { get; }
        [DataSourceProperty] public string ActorPortraitId { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorPortraitArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorPortraitProvider { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorPortraitCacheKey { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetPortraitId { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetPortraitArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetPortraitProvider { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetPortraitCacheKey { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorBannerId { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorBannerArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string ActorBannerProvider { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetBannerId { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetBannerArgs { get; private set; } = string.Empty;
        [DataSourceProperty] public string TargetBannerProvider { get; private set; } = string.Empty;

        public void ExecuteAcknowledge()
        {
            _acknowledge?.Invoke();
        }

        private void SetPortrait(Hero hero, bool actor)
        {
            ImageIdentifierVM portrait = BuildPortrait(hero);
            if (actor)
            {
                ActorPortraitId = portrait?.Id ?? string.Empty;
                ActorPortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
                ActorPortraitProvider = portrait?.TextureProviderName ?? string.Empty;
                ActorPortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            }
            else
            {
                TargetPortraitId = portrait?.Id ?? string.Empty;
                TargetPortraitArgs = portrait?.AdditionalArgs ?? string.Empty;
                TargetPortraitProvider = portrait?.TextureProviderName ?? string.Empty;
                TargetPortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            }
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

        private void SetBanner(Kingdom kingdom, bool actor)
        {
            BannerImageIdentifierVM banner = kingdom?.Banner == null ? null : new BannerImageIdentifierVM(kingdom.Banner, true);
            if (actor)
            {
                ActorBannerId = banner?.Id ?? string.Empty;
                ActorBannerArgs = banner?.AdditionalArgs ?? string.Empty;
                ActorBannerProvider = banner?.TextureProviderName ?? string.Empty;
            }
            else
            {
                TargetBannerId = banner?.Id ?? string.Empty;
                TargetBannerArgs = banner?.AdditionalArgs ?? string.Empty;
                TargetBannerProvider = banner?.TextureProviderName ?? string.Empty;
            }
        }
    }
}
