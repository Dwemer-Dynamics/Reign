using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using AIEventsAndIntrigue.Settings;
using AIPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Portraits.Integration
{
    public static class ReignPortraitBridge
    {
        private static Hero _queuedEncyclopediaHero;
        private static int _queuedEncyclopediaDelayTicks;

        public static bool IsEncyclopediaOpenQueued => _queuedEncyclopediaHero != null;

        public static void RequestPortrait(Hero hero) => RequestPortrait(hero, allowLookup: true);

        private static void RequestPortrait(Hero hero, bool allowLookup)
        {
            string key = hero == null ? null : CharacterCacheId.ForHero(hero);
            if (hero != null && !PortraitCache.IsPending(key)
                && (TextureFactory.Has(key) || PortraitCache.ExistsOnDisk(key)))
            {
                if (ReignBeta.Portraits.UI.ReignPortraitRefreshPopup.IsOpen) return;
                string campaign = ReignCampaignIdentity.CurrentCampaignId();
                TaleWorlds.ScreenSystem.ScreenManager.PushScreen(new ReignBeta.Portraits.UI.ReignPortraitRefreshPopup(
                    hero.Name?.ToString() ?? "them", hero.Age >= 18 && hero.Age <= 150, mode =>
                    {
                        if (ReignCampaignIdentity.CurrentCampaignId() == campaign) RequestPortraitChoice(hero, mode);
                    }));
                return;
            }
            if (allowLookup && hero != null && !PortraitCache.IsPending(key)
                && (!ReignCampaignPortraitClient.TryGetSettled(key, out var stored)
                    || stored == ReignCampaignPortraitClient.Lookup.Unavailable))
            {
                _ = ResumePortraitRequestAsync(hero, key);
                return;
            }
            RequestPortraitChoice(hero, "");
        }

        private static async Task ResumePortraitRequestAsync(Hero hero, string key)
        {
            var scope = ReignMainThread.CurrentGameScope;
            if (scope == null) return;
            var fence = ReignServerClient.RequestFence.Capture();
            try
            {
                await ReignCampaignPortraitClient.FindAsync(key).ConfigureAwait(false);
                await ReignRequestOwnership.InvokeScopedAsync(scope, fence.IsCurrentActionPoll,
                    () => RequestPortrait(hero, allowLookup: false), "stored portrait choice").ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReignLog.Exception("Stored portrait choice", ex); }
        }

        private static void RequestPortraitChoice(Hero hero, string mode)
        {
            bool armed = TryRequestPortrait(hero, out string message, out uint color, mode);
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

        public static bool TryRequestPortrait(Hero hero, out string message, out uint color, string refreshMode = "")
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

            if (refreshMode.Length == 0 && (TextureFactory.Has(cacheKey) || PortraitCache.ExistsOnDisk(cacheKey)))
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
            if (refreshMode.Length == 0)
            {
                bool settled = ReignCampaignPortraitClient.TryGetSettled(cacheKey, out var stored);
                if (settled && (stored == ReignCampaignPortraitClient.Lookup.Generated
                    || stored == ReignCampaignPortraitClient.Lookup.Custom))
                {
                    color = 0xFF00DD55;
                    message = "You already have a vivid image of " + hero.Name + ".";
                    return false;
                }
                if (!settled || stored != ReignCampaignPortraitClient.Lookup.Missing)
                {
                    message = settled ? "Couldn't check for " + hero.Name + "'s stored portrait. Please try again."
                        : "Checking for " + hero.Name + "'s stored portrait.";
                    return false;
                }
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
            PortraitCache.RefreshApplication refresh = null;
            if (refreshMode.Length != 0)
            {
                try { refresh = PortraitCache.CaptureRefreshApplication(campaignId, cacheKey, heroId, refreshMode); }
                catch (Exception ex)
                {
                    message = "The current portrait could not be claimed for refresh. " + ex.Message;
                    return false;
                }
            }
            if (!(refresh == null ? PortraitCache.TryMarkPending(cacheKey) : refresh.TryMarkPending()))
            {
                message = "You're still picturing " + heroName + "...";
                return false;
            }
            // Pin the exact displayed master before leaving the game thread. Never use a native
            // source for the age-only edit; Look Again always renders the fresh snapshot below.
            byte[] reference = refreshMode == "reflect_years" ? PortraitCache.GetDiskBytes(cacheKey) : null;
            if (refreshMode == "reflect_years" && (hero.Age < 18 || reference == null))
            {
                refresh.Complete();
                message = "An existing adult portrait is required to reflect on the years.";
                return false;
            }
            Task.Run(async () =>
            {
                using (new PortraitRequestScope(campaignId, campaignFolder, snapshot))
                {
                    try
                    {
                        var product = await NanoGptClient.GeneratePortraitProductAsync(
                            NanoGptClient.BuildPrompt(context), reference, null, cacheKey, heroId, context, refreshMode: refreshMode,
                            refreshOperationId: refresh?.OperationId,
                            requestOwner: refresh == null ? (Func<bool>)null : refresh.IsCurrentOwner,
                            requestScope: refresh?.GameScope).ConfigureAwait(false);
                        bool saved = product != null && await PortraitCache.SavePortraitProductAsync(cacheKey, product,
                            replaceExisting: refresh != null, refresh: refresh).ConfigureAwait(false);
                        string error = NanoGptClient.LastErrorForDisplay;
                        void ShowResult()
                        {
                            if (ReignCampaignIdentity.CurrentCampaignId() != campaignId) return;
                            if (saved)
                            {
                                TextureFactory.Invalidate(cacheKey);
                                var resident = ReignServices.EncounteredResidents?.Find(hero);
                                if (resident != null) resident.InitialPortraitCompleted = true;
                            }
                            InformationManager.DisplayMessage(new InformationMessage(saved
                                ? "[AIPortraits] Portrait ready for " + heroName + "."
                                : "[AIPortraits] Portrait generation failed: " + (error ?? "See the server image-generation log."),
                                Color.FromUint(saved ? 0xFF00DD55u : 0xFFFF5500u)));
                        }
                        if (refresh == null) await ReignMainThread.InvokeAsync(ShowResult).ConfigureAwait(false);
                        else await ReignRequestOwnership.InvokeScopedAsync(refresh.GameScope, refresh.IsCurrentOwner,
                            ShowResult, "portrait result notification").ConfigureAwait(false);
                    }
                    catch (Exception ex) { ReignLog.Warn("Background portrait failed: " + ex.Message); }
                    finally
                    {
                        if (refresh == null) PortraitCache.MarkComplete(cacheKey);
                        else refresh.Complete();
                    }
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
    }
}
