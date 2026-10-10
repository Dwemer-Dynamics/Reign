using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    internal sealed class ReignOutfitAttempt
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal string CampaignId, CampaignFolder, Timeline, HeroId, PlayerId, CacheKey, SourceHash;
        internal bool ConfirmationRequested;
        internal TaleWorlds.CampaignSystem.Campaign Campaign;
        internal string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reign", "OutfitPreviews", Id);
        internal string BeforePath => Path.Combine(DirectoryPath, "before.png");
        internal string AfterPath => Path.Combine(DirectoryPath, "after.png");
        internal JObject Identity => new JObject { ["campaignId"] = CampaignId, ["timelineId"] = Timeline,
            ["heroStringId"] = HeroId, ["playerHeroId"] = PlayerId, ["operationId"] = Id };
        internal bool IsCurrent => ReferenceEquals(Campaign, TaleWorlds.CampaignSystem.Campaign.Current)
            && CampaignId == ReignCampaignIdentity.CurrentCampaignId()
            && Timeline == (ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main");
        internal void Cleanup()
        {
            TextureFactory.ForgetFile(BeforePath);
            TextureFactory.ForgetFile(AfterPath);
            // Only these two files belong to the preview. Never remove a character cache.
            try { if (File.Exists(BeforePath)) File.Delete(BeforePath); } catch { }
            try { if (File.Exists(AfterPath)) File.Delete(AfterPath); } catch { }
            try { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, false); } catch { }
        }
    }

    internal static class ReignOutfitClient
    {
        internal const int MaxDescriptionLength = 4000;
        internal static bool Eligible(Hero hero)
        {
            if (hero == null || hero == Hero.MainHero || !hero.IsAlive || hero.IsChild || hero.Age < 18 || Hero.MainHero == null) return false;
            Clan clan = Clan.PlayerClan;
            Kingdom kingdom = clan?.Kingdom;
            return (clan != null && (hero.Clan == clan || hero.CompanionOf == clan))
                || (kingdom != null && kingdom.Leader == Hero.MainHero && hero.Clan?.Kingdom == kingdom);
        }
        internal static JObject Profile(Hero hero) => new JObject {
            ["heroStringId"] = hero?.StringId ?? "", ["clanId"] = hero?.Clan?.StringId ?? "",
            ["kingdomId"] = hero?.Clan?.Kingdom?.StringId ?? "", ["isAlive"] = hero?.IsAlive ?? false,
            ["age"] = hero?.Age ?? 0, ["isRuler"] = hero != null && hero.Clan?.Kingdom?.Leader == hero,
            ["sovereignHeroStringId"] = hero?.Clan?.Kingdom?.Leader?.StringId ?? "",
            ["companionOfId"] = hero?.CompanionOf != null && hero.CompanionOf == Clan.PlayerClan ? Hero.MainHero?.StringId ?? "" : "" };
        internal static ReignOutfitAttempt Create(Hero hero) => new ReignOutfitAttempt {
            Campaign = TaleWorlds.CampaignSystem.Campaign.Current, CampaignId = ReignCampaignIdentity.CurrentCampaignId(),
            CampaignFolder = ReignCampaignIdentity.CurrentCampaignFolderName(), Timeline = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
            HeroId = hero.StringId, PlayerId = Hero.MainHero.StringId, CacheKey = CharacterCacheId.ForHero(hero) };
        internal static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        internal static Task<JObject> Request(ReignOutfitAttempt attempt, string action, JObject payload = null)
            => ReignServerClient.PostJsonAsync("/portraits/outfit/" + action, payload ?? attempt.Identity,
                action == "cancel" ? (Func<Task<bool>>)null : () => ReignMainThread.InvokeAsync(() => attempt.IsCurrent));
        internal static async Task Cancel(ReignOutfitAttempt attempt)
        {
            if (attempt == null) return;
            try { await Request(attempt, "cancel").ConfigureAwait(false); }
            catch (Exception ex) { ReignLog.Warn("Outfit cancellation could not reach server: " + ex.Message); }
            finally { attempt.Cleanup(); }
        }
        internal static async Task Install(ReignOutfitAttempt attempt, byte[] bytes, JObject receipt)
        {
            if (!await ReignMainThread.InvokeAsync(() => attempt.IsCurrent).ConfigureAwait(false)) return;
            if (receipt?.Value<string>("schema") != "reign-portrait-product-v1" || receipt.Value<bool?>("accepted") != true
                || receipt["master"]?.Value<string>("sha256") != Hash(bytes))
                throw new InvalidDataException("The confirmed portrait is missing its validated crop receipt.");
            using (new PortraitRequestScope(attempt.CampaignId, attempt.CampaignFolder, new JObject()))
            {
                string path = PortraitCache.CustomPath(attempt.CacheKey);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                WriteAtomic(path, bytes, attempt.Id);
                WriteAtomic(Path.Combine(Path.GetDirectoryName(path), PortraitDerivativeService.OutfitReceiptFileName),
                    Encoding.UTF8.GetBytes(receipt.ToString(Newtonsoft.Json.Formatting.None)), attempt.Id);
                PortraitDerivativeService.Invalidate(attempt.CacheKey);
                if (!await PortraitDerivativeService.EnsureForSourceAsync(attempt.CacheKey, path, highPriority: true, force: true).ConfigureAwait(false)
                    || !PortraitDerivativeService.HasCurrentFullBodyProducts(path, Path.GetDirectoryName(path)))
                    throw new IOException("The new portrait's complete thumbnail, wide thumbnail, chest and zoom set could not be prepared.");
            }
        }
        private static void WriteAtomic(string path, byte[] bytes, string operationId)
        {
            PortraitProductTransaction.WriteAllBytes(path, bytes);
        }
    }
}
