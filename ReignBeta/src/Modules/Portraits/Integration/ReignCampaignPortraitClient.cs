using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Persistence.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;

namespace ReignBeta.Portraits.Integration
{
    internal static class ReignCampaignPortraitClient
    {
        private const int MaxBase64Characters = 32 * 1024 * 1024;
        private static readonly SemaphoreSlim Requests = new SemaphoreSlim(2, 2);
        private static readonly HttpClient Client = ReignHttpTransport.CreateLocalServerHttpClient(2, timeoutSeconds: 12);
        private static readonly Dictionary<string, string> HeroIds = new Dictionary<string, string>(StringComparer.Ordinal);
        internal enum Lookup { Unavailable, Missing, Generated, Custom }
        private static readonly Dictionary<string, (string Owner, DateTime Expires, Task<Lookup> Pending)> Reads
            = new Dictionary<string, (string Owner, DateTime Expires, Task<Lookup> Pending)>(StringComparer.Ordinal);
        private static readonly Task<Lookup> Unavailable = Task.FromResult(Lookup.Unavailable);
        private static ReignMainThread.GameScope _scope;

        static ReignCampaignPortraitClient() => Client.MaxResponseContentBufferSize = MaxBase64Characters + 1024 * 1024;
        private static int _saveGeneration;

        private static void ResetBindingsIfNeeded(ReignMainThread.GameScope scope, int generation)
        {
            if (ReferenceEquals(_scope, scope) && _saveGeneration == generation) return;
            HeroIds.Clear();
            Reads.Clear();
            _scope = scope;
            _saveGeneration = generation;
        }

        internal static void RememberHero(string cacheKey, string heroId)
        {
            var scope = ReignMainThread.CurrentGameScope;
            if (scope == null || string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(heroId)) return;
            lock (HeroIds)
            {
                ResetBindingsIfNeeded(scope, ReignSaveSyncCoordinator.CampaignGeneration);
                if (HeroIds.TryGetValue(cacheKey, out string previous))
                {
                    if (previous != heroId) HeroIds[cacheKey] = ""; // An ambiguous native id cannot select an archive.
                }
                else HeroIds.Add(cacheKey, heroId);
            }
        }

        // Returning a disk path used to expose files in the server's mutable
        // character directory directly. Warm the normal cache from a confirmed
        // immutable response instead, and render a placeholder meanwhile.
        internal static void Queue(string cacheKey) => _ = Find(cacheKey, waitForSlot: false);

        internal static bool TryGetSettled(string cacheKey, out Lookup lookup)
        {
            Task<Lookup> task = Find(cacheKey, waitForSlot: false);
            lookup = task.Status == TaskStatus.RanToCompletion ? task.Result : Lookup.Unavailable;
            return task.Status == TaskStatus.RanToCompletion;
        }

        internal static Task<Lookup> FindAsync(string cacheKey) => Find(cacheKey, waitForSlot: true);

        private static Task<Lookup> Find(string cacheKey, bool waitForSlot)
        {
            var scope = ReignMainThread.CurrentGameScope;
            if (scope == null || string.IsNullOrEmpty(cacheKey) || ReignSaveSyncCoordinator.IsAlignmentPending
                || ReignCampaignInitializationGate.IsPending) return Unavailable;
            int saveGeneration = ReignSaveSyncCoordinator.CampaignGeneration;
            string expectedScope = PortraitCache.ScopedTextureKey(cacheKey);
            string owner = expectedScope + "|" + ReignServices.WorldHistory?.TimelineId + "|"
                + ReignServices.WorldHistory?.SharedActivityGeneration;
            lock (HeroIds)
            {
                ResetBindingsIfNeeded(scope, saveGeneration);
                if (!HeroIds.TryGetValue(cacheKey, out string heroId) || string.IsNullOrEmpty(heroId)) return Unavailable;
                DateTime now = DateTime.UtcNow;
                if (Reads.TryGetValue(cacheKey, out var read) && read.Owner == owner
                    && (!read.Pending.IsCompleted || read.Expires > now)) return read.Pending;
                bool hasSlot = Requests.Wait(0);
                if (!hasSlot && !waitForSlot) return Unavailable;
                var pending = ReadAsync(cacheKey, heroId, expectedScope, scope, saveGeneration, hasSlot);
                Reads[cacheKey] = (owner, now.AddSeconds(20), pending);
                return pending;
            }
        }

        private static async Task<Lookup> ReadAsync(string cacheKey, string hero, string expectedScope,
            ReignMainThread.GameScope scope, int saveGeneration, bool hasSlot)
        {
            try
            {
                if (!hasSlot)
                {
                    hasSlot = await Requests.WaitAsync(TimeSpan.FromSeconds(12)).ConfigureAwait(false);
                    if (!hasSlot) return Lookup.Unavailable;
                }
                string campaign = null, timeline = null, folder = null;
                ReignServerClient.RequestFence fence = null;
                bool LiveScope()
                {
                    lock (HeroIds)
                        return ReferenceEquals(ReignMainThread.CurrentGameScope, scope)
                            && ReignSaveSyncCoordinator.CampaignGeneration == saveGeneration
                            && HeroIds.TryGetValue(cacheKey, out string activeHero) && activeHero == hero;
                }
                bool OwnsRequest() => LiveScope() && fence?.IsCurrentActionPoll() == true;
                await ReignRequestOwnership.InvokeScopedAsync(scope, LiveScope, () =>
                {
                    fence = ReignServerClient.RequestFence.Capture();
                    if (!fence.IsCurrentActionPoll() || ReignCampaignInitializationGate.IsPending
                        || expectedScope != PortraitCache.ScopedTextureKey(cacheKey))
                        throw new OperationCanceledException();
                    campaign = ReignServerClient.GetCampaignId();
                    timeline = ReignServices.WorldHistory?.TimelineId ?? "main";
                    folder = PortraitCache.RequestCampaignFolder;
                }, "campaign portrait scope").ConfigureAwait(false);

                var body = new JObject
                {
                    ["campaignId"] = campaign,
                    ["heroStringId"] = hero,
                    ["timelineId"] = timeline,
                    ["sharedActivityGeneration"] = fence.SharedActivityGeneration
                };
                await ReignRequestOwnership.EnsureAsync(OwnsRequest, "campaign portrait dispatch", scope).ConfigureAwait(false);
                var response = (await ReignHttpTransport.PostDirectJsonAsync(Client, "/portraits/current-product",
                    body.ToString(Formatting.None), checkAvailability: true).ConfigureAwait(false)).Json;

                if (response?.Value<bool?>("ok") != true || response.Value<string>("campaignId") != campaign
                    || response.Value<string>("heroStringId") != hero || response.Value<string>("timelineId") != timeline
                    || response.Value<string>("sharedActivityGeneration") != fence.SharedActivityGeneration) return Lookup.Unavailable;
                if (response.Value<string>("status") == "missing")
                {
                    await ReignRequestOwnership.EnsureAsync(OwnsRequest, "campaign portrait absence", scope).ConfigureAwait(false);
                    return Lookup.Missing;
                }
                if (response.Value<string>("status") != "current" || response["custom"]?.Type != JTokenType.Boolean)
                    return Lookup.Unavailable;
                string data = response.Value<string>("imageBase64");
                if (string.IsNullOrEmpty(data) || data.Length > MaxBase64Characters) return Lookup.Unavailable;
                byte[] image = Convert.FromBase64String(data);
                string sha = ReignOutfitClient.Hash(image);
                if (sha != response.Value<string>("sha256") || image.Length < 24
                    || image[0] != 137 || image[1] != 80 || image[2] != 78 || image[3] != 71) return Lookup.Unavailable;
                var receipt = response["portraitReceipt"] as JObject;
                if (receipt != null && (receipt.Value<string>("schema") != "reign-portrait-product-v1"
                    || receipt.Value<bool?>("accepted") != true || receipt["master"]?.Value<string>("sha256") != sha))
                    return Lookup.Unavailable;

                bool installed = false;
                bool custom = response.Value<bool>("custom");
                await ReignRequestOwnership.InvokeScopedAsync(scope, OwnsRequest, () =>
                {
                    // Fence the publication itself. A same-world timeline change can
                    // happen between a separately scheduled preflight and a disk write.
                    using (new PortraitRequestScope(campaign, folder, new JObject()))
                    {
                        installed = PortraitCache.InstallMissingCampaignPortrait(cacheKey, image, custom, receipt,
                            OwnsRequest);
                        if (installed) PortraitDerivativeService.Invalidate(cacheKey);
                    }
                }, "campaign portrait install").ConfigureAwait(false);
                if (!installed) return custom ? Lookup.Custom : Lookup.Generated;

                using (new PortraitRequestScope(campaign, folder, new JObject()))
                    await PortraitDerivativeService.EnsureForIdAsync(cacheKey, highPriority: true).ConfigureAwait(false);
                await ReignRequestOwnership.InvokeScopedAsync(scope, OwnsRequest, () =>
                {
                    using (new PortraitRequestScope(campaign, folder, new JObject()))
                    {
                        PortraitCache.InvalidateAvailability(cacheKey);
                        TextureFactory.NotifyPortraitAvailabilityChanged();
                    }
                }, "campaign portrait update").ConfigureAwait(false);
                return custom ? Lookup.Custom : Lookup.Generated;
            }
            catch (OperationCanceledException) { return Lookup.Unavailable; }
            catch (Exception failure) when (failure is IOException || failure is UnauthorizedAccessException
                || failure is InvalidOperationException || failure is FormatException || failure is JsonException
                || failure is HttpRequestException)
            {
                ReignLog.Warn("Campaign portrait cache warm-up failed: " + failure.Message);
                return Lookup.Unavailable;
            }
            finally { if (hasSlot) Requests.Release(); }
        }
    }
}