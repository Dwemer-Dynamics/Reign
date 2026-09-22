using System;
using AIPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace ReignBeta.UI.ViewModels
{
    public sealed class ReignCourtCardVM : ViewModel
    {
        private readonly Action<ReignCourtCardVM> _select;

        public ReignCourtCardVM(string id, string tabId, string title, string subtitle, string status, string meta,
            Action<ReignCourtCardVM> select, object payload = null, Hero hero = null, string iconSprite = "")
        {
            Id = id ?? string.Empty;
            TabId = tabId ?? "court";
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            Status = status ?? string.Empty;
            Meta = meta ?? string.Empty;
            IconSprite = iconSprite ?? string.Empty;
            Payload = payload;
            Hero = hero;
            _select = select;
            HasPortrait = hero != null;
            PortraitCacheKey = CharacterCacheId.ForHero(hero) ?? string.Empty;
            ImageIdentifierVM portrait = BuildPortrait(hero);
            PortraitId = portrait?.Id ?? string.Empty;
            PortraitAdditionalArgs = portrait?.AdditionalArgs ?? string.Empty;
            PortraitTextureProviderName = portrait?.TextureProviderName ?? string.Empty;
        }

        public string Id { get; }
        public string TabId { get; }
        public object Payload { get; }
        public Hero Hero { get; }
        [DataSourceProperty] public string Title { get; }
        [DataSourceProperty] public string Subtitle { get; }
        [DataSourceProperty] public string Status { get; }
        [DataSourceProperty] public string Meta { get; }
        [DataSourceProperty] public string IconSprite { get; }
        [DataSourceProperty] public bool HasPortrait { get; }
        [DataSourceProperty] public string PortraitCacheKey { get; }
        [DataSourceProperty] public string PortraitId { get; }
        [DataSourceProperty] public string PortraitAdditionalArgs { get; }
        [DataSourceProperty] public string PortraitTextureProviderName { get; }

        public void ExecuteSelect() { _select?.Invoke(this); }

        private static ImageIdentifierVM BuildPortrait(Hero hero)
        {
            try
            {
                CharacterCode code = hero?.CharacterObject == null ? null : CharacterCode.CreateFrom(hero.CharacterObject);
                return string.IsNullOrWhiteSpace(code?.Code) ? null : new CharacterImageIdentifierVM(code);
            }
            catch { return null; }
        }
    }
}
