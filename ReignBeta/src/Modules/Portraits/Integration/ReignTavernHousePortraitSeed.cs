using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.Shared.Characters;
using ReignPortraits;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Library;
using ReignBeta.Characters.Campaign;
using ReignBeta.Characters.Integration;
using ReignBeta.Platform.Integration;
using ReignBeta.Platform.Runtime;
using ReignBeta.Persistence.Integration;

namespace ReignBeta.Portraits.Integration
{
    /// <summary>Imports shipped tavern masters into their saved native Hero cache. It never generates images or touches shared masters.</summary>
    internal static class ReignTavernHousePortraitSeed
    {
        internal sealed class VisitRetries
        {
            private readonly HashSet<string> _handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            internal bool TryHandle(string cacheKey, Func<bool> trySeed)
            {
                if (_handled.Contains(cacheKey)) return true;
                if (!trySeed()) return false;
                _handled.Add(cacheKey);
                return true;
            }
        }

        // False means the visit should check again. True means no more checks are
        // needed during this visit, including when a worker has reserved the import.
        internal static bool TrySeed(Hero hero, TavernHousePerson person)
        {
            if (hero == null || person == null || person.Generation != 0 || !person.Initialized
                || !TavernHouseRules.IsActive(person)) return true;
            if (!ReignCampaignIdentity.HasActiveCampaign() || ReignCampaignInitializationGate.IsPending
                || ReignSaveSyncCoordinator.IsAlignmentPending) return false;
            string cacheKey = CharacterCacheId.ForHero(hero);
            if (string.IsNullOrEmpty(cacheKey) || !Regex.IsMatch(person.CastId ?? "", "^reign_tavern_[A-Za-z0-9_]+$")) return true;
            if (PortraitCache.IsPending(cacheKey)) return false;
            bool hasPortrait = PortraitCache.ExistsOnDisk(cacheKey);
            if (!hasPortrait)
            {
                if (!ReignCampaignPortraitClient.TryGetSettled(cacheKey, out var stored)
                    || stored == ReignCampaignPortraitClient.Lookup.Unavailable) return false;
                if (stored != ReignCampaignPortraitClient.Lookup.Missing) return true;
            }
            string root = Path.GetFullPath(Path.Combine(BasePath.Name, "Modules", "ReignBeta", "TavernHousePortraits"));
            string sourceDirectory = Path.GetFullPath(Path.Combine(root, person.CastId));
            string sourcePath = Path.GetFullPath(Path.Combine(sourceDirectory, "portrait.png"));
            string sourceManifestPath = Path.Combine(sourceDirectory, PortraitDerivativeService.ManifestFileName);
            var info = new FileInfo(sourcePath);
            if (!sourcePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !info.Exists || info.Length < 24 || info.Length > 32L * 1024 * 1024 || !File.Exists(sourceManifestPath)) return true;
            string cacheDirectory = PortraitCache.DirFor(cacheKey);
            string seedReceiptPath = Path.Combine(cacheDirectory, ".tavern_seed.json");
            string masterPath = Path.Combine(cacheDirectory, PortraitCache.PortraitFileName);
            string customPath = Path.Combine(cacheDirectory, PortraitCache.CustomFileName);
            bool hasMaster = File.Exists(masterPath);
            bool repairSeed = hasMaster && RequiresSeedRepair(seedReceiptPath, person.CastId, sourcePath, cacheDirectory);
            if (File.Exists(customPath) || ((hasPortrait || hasMaster) && !repairSeed))
            { person.PortraitComplete = true; return true; }

            var scope = ReignMainThread.CurrentGameScope;
            var fence = ReignServerClient.RequestFence.Capture();
            if (scope == null || !fence.IsCurrentActionPoll()) return false;
            int saveGeneration = ReignSaveSyncCoordinator.CampaignGeneration;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string campaignFolder = ReignCampaignIdentity.CurrentCampaignFolderName();
            string castId = person.CastId, heroId = hero.StringId;
            string appearanceCode = PortraitRequestRegistry.GetCodeFor(hero);
            JObject snapshot = ReignServerClient.BuildNativePortraitSnapshot(hero);
            var behavior = ReignServices.TavernHouse;
            bool OwnsVisit() => ReignServices.TavernHouse == behavior && fence.IsCurrentActionPoll()
                && Settlement.CurrentSettlement?.StringId == person.TownId
                && ReferenceEquals(behavior?.GetPerson(hero), person) && TavernHouseRules.IsActive(person);
            if (!PortraitCache.TryMarkPending(cacheKey)) return false;
            // Resolve every TaleWorlds value above on the requesting game thread.
            Task.Run(async () =>
            {
                using (new PortraitRequestScope(campaignId, campaignFolder, snapshot))
                {
                    try
                    {
                        await ReignRequestOwnership.EnsureAsync(OwnsVisit, "shipped tavern portrait import", scope).ConfigureAwait(false);
                        lock (PortraitCache.ProductCommitLock(cacheDirectory))
                        {
                            // Respect products or recovery work that appeared after the lookup settled.
                            if (ReignSaveSyncCoordinator.CampaignGeneration != saveGeneration
                                || ReignSaveSyncCoordinator.IsAlignmentPending || File.Exists(customPath)
                                || File.Exists(Path.Combine(cacheDirectory, PortraitCache.PendingGenerationFileName))
                                || PortraitProductTransaction.HasRecoveryJournal(cacheDirectory)
                                || File.Exists(masterPath) != repairSeed
                                || (repairSeed && !RequiresSeedRepair(seedReceiptPath, castId, sourcePath, cacheDirectory))) return;
                            string hash = ReignTavernPortraitPack.Install(root, castId, cacheDirectory);
                            JObject receipt = new JObject { ["schema"] = "reign-tavern-portrait-seed-v1", ["castId"] = castId,
                                ["heroStringId"] = heroId, ["campaignId"] = campaignId, ["cacheKey"] = cacheKey,
                                ["sourceSha256"] = hash, ["masterSha256"] = hash, ["derivativesReady"] = true };
                            File.WriteAllText(seedReceiptPath, receipt.ToString(Formatting.Indented));
                        }
                        await ReignRequestOwnership.InvokeScopedAsync(scope, OwnsVisit, () =>
                        {
                            _ = ReignServerClient.RegisterPortraitFileAsync(cacheKey, masterPath, "shipped_tavern");
                            TextureFactory.Invalidate(cacheKey);
                            person.PortraitComplete = PortraitCache.ExistsOnDisk(cacheKey);
                            PortraitIndex.Register(appearanceCode, cacheKey);
                            ReignLog.Info("Tavern shipped portrait ready cast=" + castId + " hero=" + heroId + " campaign=" + campaignId);
                        }, "shipped tavern portrait publication").ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { ReignLog.Exception("Tavern shipped portrait seed " + castId, ex); }
                    finally { PortraitCache.MarkComplete(cacheKey); }
                }
            });
            return true;
        }

        internal static bool RequiresSeedRepair(string receiptPath, string castId, string sourcePath, string cacheDirectory)
        {
            string cachedMasterPath = Path.Combine(cacheDirectory, PortraitCache.PortraitFileName);
            // A crash may leave the shipped master behind before its seed receipt is committed. Match its
            // content before claiming it so unrelated custom or normally generated portraits stay untouched.
            if (!File.Exists(receiptPath)) return FilesHaveSameSha256(sourcePath, cachedMasterPath);
            try
            {
                JObject receipt = JObject.Parse(File.ReadAllText(receiptPath));
                if (!string.Equals(receipt.Value<string>("castId"), castId, StringComparison.Ordinal)) return false;
                string sourceHash = ComputeSha256(sourcePath);
                string cachedHash = ComputeSha256(cachedMasterPath);
                if (!string.Equals(cachedHash, sourceHash, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(cachedHash, receipt.Value<string>("masterSha256"), StringComparison.OrdinalIgnoreCase)) return false;
                if (!string.Equals(receipt.Value<string>("sourceSha256"), sourceHash, StringComparison.OrdinalIgnoreCase)
                    || receipt.Value<bool?>("derivativesReady") != true) return true;
                string manifestPath = Path.Combine(cacheDirectory, PortraitDerivativeService.ManifestFileName);
                if (!File.Exists(manifestPath)) return true;
                PortraitDerivativeManifestV2 manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(File.ReadAllText(manifestPath));
                return !PortraitDerivativeCore.IsCurrentV2(manifest, cachedMasterPath, cacheDirectory);
            }
            catch (JsonException) { return FilesHaveSameSha256(sourcePath, cachedMasterPath); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool FilesHaveSameSha256(string firstPath, string secondPath)
        {
            if (!File.Exists(firstPath) || !File.Exists(secondPath)) return false;
            var first = new FileInfo(firstPath);
            var second = new FileInfo(secondPath);
            return first.Length == second.Length
                && string.Equals(ComputeSha256(firstPath), ComputeSha256(secondPath), StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSha256(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
    }
}
