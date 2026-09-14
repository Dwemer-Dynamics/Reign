using System;
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
using TaleWorlds.Library;

namespace ReignBeta.Integration
{
    /// <summary>Imports shipped tavern masters into their saved native Hero cache. It never generates images or touches shared masters.</summary>
    internal static class ReignTavernHousePortraitSeed
    {
        internal static bool TrySeed(Hero hero, TavernHousePerson person)
        {
            if (hero == null || person == null || person.Generation != 0 || !person.Initialized
                || !ReignCampaignIdentity.HasActiveCampaign() || ReignCampaignInitializationGate.IsPending) return false;
            string cacheKey = CharacterCacheId.ForHero(hero);
            if (!Regex.IsMatch(person.CastId ?? "", "^reign_tavern_[A-Za-z0-9_]+$")) return false;
            string root = Path.GetFullPath(Path.Combine(BasePath.Name, "Modules", "ReignBeta", "TavernHousePortraits"));
            string sourceDirectory = Path.GetFullPath(Path.Combine(root, person.CastId));
            string sourcePath = Path.GetFullPath(Path.Combine(sourceDirectory, "portrait.png"));
            string sourceManifestPath = Path.Combine(sourceDirectory, PortraitDerivativeService.ManifestFileName);
            if (!sourcePath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(sourcePath)) return false;
            string cacheDirectory = PortraitCache.DirFor(cacheKey);
            string seedReceiptPath = Path.Combine(cacheDirectory, ".tavern_seed.json");
            bool portraitExists = PortraitCache.ExistsOnDisk(cacheKey);
            bool repairSeed = portraitExists && RequiresSeedRepair(seedReceiptPath, person.CastId, sourcePath, cacheDirectory);
            if (portraitExists && !repairSeed) { person.PortraitComplete = true; return true; }
            if (PortraitCache.IsPending(cacheKey)) return false;
            var info = new FileInfo(sourcePath);
            if (info.Length < 24 || info.Length > 32L * 1024 * 1024 || !File.Exists(sourceManifestPath)) return false;
            if (!PortraitCache.TryMarkPending(cacheKey)) return false;
            string campaignId = ReignCampaignIdentity.CurrentCampaignId();
            string campaignFolder = ReignCampaignIdentity.CurrentCampaignFolderName();
            string castId = person.CastId, heroId = hero.StringId;
            string appearanceCode = PortraitRequestRegistry.GetCodeFor(hero);
            JObject snapshot = ReignServerClient.BuildNativePortraitSnapshot(hero);
            var behavior = ReignTavernHouseCampaignBehavior.Instance;
            // Resolve every TaleWorlds value above on the requesting game thread.
            Task.Run(async () =>
            {
                using (new PortraitRequestScope(campaignId, campaignFolder, snapshot))
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(sourcePath);
                        if (bytes.Length < 24 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71)
                            throw new InvalidDataException("The shipped tavern portrait is not a PNG.");
                        // A normal portrait request may have completed before this worker began.
                        if (PortraitCache.ExistsOnDisk(cacheKey) && !repairSeed) return;
                        Directory.CreateDirectory(cacheDirectory);
                        // The shipped V2 manifest carries the validated face-focus receipt. SaveAndPrepareAsync
                        // uses it to rebuild cache-local derivatives whose source path and timestamps match this save.
                        File.Copy(sourceManifestPath, Path.Combine(cacheDirectory, PortraitDerivativeService.ManifestFileName), true);
                        bool prepared = await PortraitCache.SaveAndPrepareAsync(cacheKey, bytes).ConfigureAwait(false);
                        string hash;
                        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                        JObject receipt = new JObject { ["schema"] = "reign-tavern-portrait-seed-v1", ["castId"] = castId,
                            ["heroStringId"] = heroId, ["campaignId"] = campaignId, ["cacheKey"] = cacheKey,
                            ["sourceSha256"] = hash, ["masterSha256"] = hash, ["derivativesReady"] = prepared };
                        File.WriteAllText(Path.Combine(PortraitCache.DirFor(cacheKey), ".tavern_seed.json"), receipt.ToString(Formatting.Indented));
                        await ReignMainThread.InvokeAsync(() =>
                        {
                            if (ReignTavernHouseCampaignBehavior.Instance != behavior || ReignCampaignIdentity.CurrentCampaignId() != campaignId) return;
                            person.PortraitComplete = prepared && PortraitCache.ExistsOnDisk(cacheKey);
                            PortraitIndex.Register(appearanceCode, cacheKey);
                            TextureFactory.Invalidate(cacheKey);
                        });
                    }
                    catch (Exception ex) { ReignLog.Exception("Tavern shipped portrait seed " + castId, ex); }
                    finally { PortraitCache.MarkComplete(cacheKey); }
                }
            });
            return true;
        }

        private static bool RequiresSeedRepair(string receiptPath, string castId, string sourcePath, string cacheDirectory)
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
                if (!string.Equals(receipt.Value<string>("sourceSha256"), sourceHash, StringComparison.OrdinalIgnoreCase)
                    || receipt.Value<bool?>("derivativesReady") != true) return true;
                string manifestPath = Path.Combine(cacheDirectory, PortraitDerivativeService.ManifestFileName);
                if (!File.Exists(manifestPath)) return true;
                PortraitDerivativeManifestV2 manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(File.ReadAllText(manifestPath));
                return !PortraitDerivativeCore.IsCurrentV2(manifest, cachedMasterPath, cacheDirectory);
            }
            catch { return true; }
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
