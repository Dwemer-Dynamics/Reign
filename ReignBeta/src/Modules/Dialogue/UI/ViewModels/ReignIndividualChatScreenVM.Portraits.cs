using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Court;
using Reign.Core.Contracts.Dialogue;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using static ReignBeta.Dialogue.UI.ReignChatText;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Dialogue.Campaign;
using ReignBeta.Dialogue.Integration;
using ReignBeta.Dialogue.Runtime;
using ReignBeta.Platform.Campaign;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Settings;
using ReignBeta.Portraits.Integration;
using ReignBeta.Relationships.Integration;
using ReignBeta.UI.ViewModels;
using ReignBeta.WorldSimulation.Campaign;
#if !REIGN_EXCLUDE_COURT
using ReignBeta.Court.Campaign;
#endif
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Dialogue.UI
{
    public sealed partial class ReignIndividualChatScreenVM
    {
        private static ImageIdentifierVM BuildPortrait(Hero hero, string cacheKey)
        {
            try
            {
                if (hero?.CharacterObject == null)
                {
                    return null;
                }

                Equipment equipment = hero == Hero.MainHero
                    ? hero.CivilianEquipment ?? hero.CharacterObject.FirstCivilianEquipment ?? hero.CharacterObject.Equipment
                    : hero.CharacterObject.FirstCivilianEquipment ?? hero.CharacterObject.Equipment;
                CharacterCode code = CharacterCode.CreateFrom(hero.CharacterObject, equipment);
                if (string.IsNullOrWhiteSpace(code?.Code))
                {
                    return null;
                }

                PortraitIndex.Register(PortraitRequestRegistry.NormalizeKey(code.Code), cacheKey);
                return new CharacterImageIdentifierVM(code);
            }
            catch
            {
                return null;
            }
        }

        private void ShowOrToggleZoom(string id, string additionalArgs, string providerName, string cacheKey)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(providerName) || string.IsNullOrEmpty(cacheKey))
            {
                return;
            }

            if (IsPortraitZoomVisible && string.Equals(_zoomPortraitCacheKey, cacheKey, StringComparison.Ordinal))
            {
                ClosePortraitZoom();
                return;
            }

            _zoomPortraitCacheKey = cacheKey;
            PortraitPatch.SetChatZoomIdentity(cacheKey);
            SetZoomAspect(GetAspectRatioFor(cacheKey, PortraitQualityTier.Zoom));
            ZoomPortraitId = id;
            ZoomPortraitAdditionalArgs = additionalArgs ?? string.Empty;
            ZoomPortraitTextureProviderName = providerName;
            IsPortraitZoomVisible = true;
        }

        private void ClosePortraitZoom()
        {
            IsPortraitZoomVisible = false;
            _zoomPortraitCacheKey = null;
            PortraitPatch.SetChatZoomIdentity(null);
        }

        private void SetPlayerCropAspect(float ratio)
        {
            GetTopToBottomCropDimensions(ratio, out var width, out var height);
            PlayerPortraitCropImageWidth = width;
            PlayerPortraitCropImageHeight = height;
        }

        private void SetNpcCropAspect(float ratio)
        {
            GetTopToBottomCropDimensions(ratio, out var width, out var height);
            NpcPortraitCropImageWidth = width;
            NpcPortraitCropImageHeight = height;
        }

        private static void GetTopToBottomCropDimensions(float ratio, out float width, out float height)
        {
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                ratio = 0.75f;
            }

            float apertureAspect = HeaderPortraitApertureWidth / HeaderPortraitApertureHeight;
            if (ratio >= apertureAspect)
            {
                height = HeaderPortraitApertureHeight * HeaderPortraitCoverOverscan;
                width = height * ratio;
            }
            else
            {
                width = HeaderPortraitApertureWidth * HeaderPortraitCoverOverscan;
                height = width / ratio;
            }
        }

        private void SetZoomAspect(float ratio)
        {
            if (ratio <= 0f || float.IsNaN(ratio) || float.IsInfinity(ratio))
            {
                ratio = 0.75f;
            }

            float width = 860f;
            float height = width / ratio;
            if (height > 920f)
            {
                height = 920f;
                width = height * ratio;
            }

            ZoomPortraitImageWidth = width;
            ZoomPortraitImageHeight = height;
            ZoomFrameWidth = width + 36f;
            ZoomFrameHeight = height + 36f;
        }

        private static float GetAspectRatioFor(string cacheKey, PortraitQualityTier tier)
        {
            if (!PortraitDerivativeService.TryGetDerivativeAspectRatio(cacheKey, tier, out var ratio)
                && !PortraitCache.TryGetPortraitAspectRatio(cacheKey, out ratio))
            {
                return 0.75f;
            }

            return ratio;
        }

        private static string BuildHeroSubtitle(Hero hero)
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

        private static string BuildLocationText(Hero hero)
        {
            Settlement settlement = Settlement.CurrentSettlement ?? MobileParty.MainParty?.CurrentSettlement ?? hero?.CurrentSettlement;
            if (settlement != null)
            {
                return "Location: " + settlement.Name;
            }

            return "Location: with your party";
        }
    }
}
