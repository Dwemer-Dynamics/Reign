using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIEventsAndIntrigue.Settings;
using AIPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    public static class ReignPortraitBridge
    {
        private static Hero _queuedEncyclopediaHero;
        private static int _queuedEncyclopediaDelayTicks;

        public static bool IsEncyclopediaOpenQueued => _queuedEncyclopediaHero != null;

        public static void RequestPortrait(Hero hero)
        {
            bool armed = TryRequestPortrait(hero, out string message, out uint color);
            if (!string.IsNullOrWhiteSpace(message))
            {
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] " + message, Color.FromUint(color)));
            }

            ReignLog.Info("Portrait look request hero=" + (hero?.StringId ?? "") + " armed=" + armed + " message=" + message);
        }



        public static void QueueOpenHeroEncyclopedia(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            _queuedEncyclopediaHero = hero;
            _queuedEncyclopediaDelayTicks = 2;
            ReignLog.Info("Queued encyclopedia open hero=" + hero.StringId);
        }

        public static void ProcessQueuedEncyclopediaOpen()
        {
            if (_queuedEncyclopediaHero == null)
            {
                return;
            }

            if (_queuedEncyclopediaDelayTicks > 0)
            {
                _queuedEncyclopediaDelayTicks--;
                return;
            }

            Hero hero = _queuedEncyclopediaHero;
            _queuedEncyclopediaHero = null;
            _queuedEncyclopediaDelayTicks = 0;
            OpenHeroEncyclopedia(hero);
        }

        public static void CancelQueuedEncyclopediaOpen()
        {
            _queuedEncyclopediaHero = null;
            _queuedEncyclopediaDelayTicks = 0;
        }

        public static bool TryRequestPortrait(Hero hero, out string message, out uint color)
        {
            color = 0xFFFFAA00;
            message = string.Empty;

            if (hero == null)
            {
                message = "No hero selected.";
                return false;
            }

            AIEventsSettings settings = AIEventsSettings.Instance;
            if (settings != null && !settings.ModEnabled)
            {
                message = "AI portraits are disabled in Bannerlord Reign options.";
                return false;
            }

            string cacheKey = CharacterCacheId.ForHero(hero);
            if (string.IsNullOrWhiteSpace(cacheKey))
            {
                message = "Could not build a portrait cache key for " + hero.Name + ".";
                return false;
            }

            if (TextureFactory.Has(cacheKey) || PortraitCache.ExistsOnDisk(cacheKey))
            {
                color = 0xFF00DD55;
                message = "You already have a vivid image of " + hero.Name + ".";
                return false;
            }

            if (PortraitCache.IsPending(cacheKey))
            {
                message = "You're still picturing " + hero.Name + "...";
                return false;
            }

            if (!ReignCampaignIdentity.HasActiveCampaign() || ReignCampaignInitializationGate.IsPending)
            {
                message = "Wait for campaign preparation before requesting a portrait.";
                return false;
            }

            // Read TaleWorlds objects once, on the requesting game thread.
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string campaignFolder = ReignCampaignIdentity.CurrentCampaignFolderName();
            string heroId = hero.StringId;
            string heroName = hero.Name.ToString();
            JObject snapshot = ReignServerClient.BuildNativePortraitSnapshot(hero);
            PortraitPromptContext context = PortraitPromptContext.FromHero(hero);
            PortraitIndex.Register(PortraitRequestRegistry.GetCodeFor(hero), cacheKey);
            if (!PortraitCache.TryMarkPending(cacheKey))
            {
                message = "You're still picturing " + heroName + "...";
                return false;
            }
            Task.Run(async () =>
            {
                using (new PortraitRequestScope(campaignId, campaignFolder, snapshot))
                {
                    try
                    {
                        var product = await NanoGptClient.GeneratePortraitProductAsync(
                            NanoGptClient.BuildPrompt(context), null, null, cacheKey, heroId, context).ConfigureAwait(false);
                        bool saved = product != null && await PortraitCache.SavePortraitProductAsync(cacheKey, product).ConfigureAwait(false);
                        string error = NanoGptClient.LastErrorForDisplay;
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            if (ReignCampaignIdentity.CurrentCampaignId() != campaignId) return;
                            if (saved)
                            {
                                TextureFactory.Invalidate(cacheKey);
                                var resident = ReignBeta.Campaign.ReignEncounteredResidentsCampaignBehavior.Instance?.Find(hero);
                                if (resident != null) resident.InitialPortraitCompleted = true;
                            }
                            InformationManager.DisplayMessage(new InformationMessage(saved
                                ? "[AIPortraits] Portrait ready for " + heroName + "."
                                : "[AIPortraits] Portrait generation failed: " + (error ?? "See the server image-generation log."),
                                Color.FromUint(saved ? 0xFF00DD55u : 0xFFFF5500u)));
                        }).ConfigureAwait(false);
                    }
                    catch (Exception ex) { ReignLog.Warn("Background portrait failed: " + ex.Message); }
                    finally { PortraitCache.MarkComplete(cacheKey); }
                }
            });
            color = 0xFF00AAFF;
            message = "Preparing a portrait of " + heroName + " in the background...";
            ReignLog.Info("Background portrait queued campaign=" + campaignId + " hero=" + heroId + " cacheKey=" + cacheKey);
            return true;
        }

        public static void OpenHeroEncyclopedia(Hero hero)
        {
            if (hero == null)
            {
                return;
            }

            try
            {
                string link = hero.EncyclopediaLink;
                if (!string.IsNullOrWhiteSpace(link))
                {
                    TaleWorlds.CampaignSystem.Campaign.Current?.EncyclopediaManager?.GoToLink(link);
                    ReignLog.Info("Opened encyclopedia for portrait capture hero=" + hero.StringId + " link=" + link);
                }
            }
            catch (Exception ex)
            {
                ReignLog.Warn("Could not open encyclopedia for " + hero.StringId + ": " + ex.Message);
                InformationManager.DisplayMessage(new InformationMessage("[Bannerlord Reign] Could not open encyclopedia for " + hero.Name + ".", Color.FromUint(0xFFFFAA00)));
            }
        }

        private static string ShortKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return key.Length <= 24 ? key : key.Substring(0, 24) + "...";
        }
    }
}
