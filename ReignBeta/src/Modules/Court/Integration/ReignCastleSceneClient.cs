using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.CastleChat;
using ReignBeta.Court;
using ReignBeta.Settings;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static class ReignCastleSceneClient
    {
        private const string CastleSceneRenderContract = "castle_scene_16x9_room_attire_v3";

        public static async Task<string> PrepareAsync(CastleRoomSessionRecord session, IEnumerable<Hero> heroes)
        {
            if (session == null) return string.Empty;
            // Reopening preserves the established scene; explicit regeneration
            // clears ImageStatus before requesting a new render.
            if (session.ImageStatus == "ready" && File.Exists(session.ImageLocalPath)) return session.ImageLocalPath;
            if (ReignBetaSettings.Instance?.CastleChatImageGeneration == false)
            {
                session.ImageStatus = "disabled";
                ReignLog.Info("Castle scene image generation is disabled in MCM for session=" + session.SessionKey);
                return string.Empty;
            }
            List<Hero> ordered = (heroes ?? Enumerable.Empty<Hero>()).Where(x => x != null).Take(4).ToList();
            if (ordered.Count == 0) return string.Empty;
            if (ordered.Any(hero => hero.IsChild))
            {
                return await PrepareFamilyAsync(new FamilyChambersSessionRecord
                {
                    SessionId = session.SessionKey, CampaignId = session.CampaignId, TimelineId = session.TimelineId,
                    SettlementStringId = session.SettlementStringId, CreatedDay = session.CampaignDay,
                    AdultHeroIdsCsv = string.Join(",", ordered.Where(hero => !hero.IsChild).Select(hero => hero.StringId)),
                    ChildHeroIdsCsv = string.Join(",", ordered.Where(hero => hero.IsChild).Select(hero => hero.StringId)),
                    ChatRecord = session
                }).ConfigureAwait(false);
            }
            if (string.IsNullOrWhiteSpace(session.ImagePromptSnapshot))
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Castle scene generation skipped because the snapshotted image prompt is empty. session=" + session.SessionKey);
                return string.Empty;
            }
            List<byte[]> portraits = new List<byte[]>();
            foreach (Hero hero in ordered)
            {
                string portraitKey = CharacterCacheId.ForHero(hero);
                byte[] portrait = PortraitCache.GetDiskBytes(portraitKey);
                if (portrait == null || portrait.Length == 0)
                {
                    ReignLog.Info("Castle scene is preparing a missing portrait master for hero=" + hero.StringId);
                    JObject portraitResponse = await ReignServerClient.GenerateSharedPortraitAsync(portraitKey).ConfigureAwait(false);
                    if (portraitResponse.Value<bool?>("ok") == true)
                    {
                        PortraitCache.ClearMemoryCache();
                        portrait = PortraitCache.GetDiskBytes(portraitKey);
                    }
                    if (portrait == null || portrait.Length == 0)
                    {
                        session.ImageStatus = "failed";
                        ReignLog.Warn("Castle scene portrait preparation failed for hero=" + hero.StringId
                            + ": " + (portraitResponse.Value<string>("error") ?? "portrait master is unavailable"));
                        return string.Empty;
                    }
                }
                portraits.Add(portrait);
            }
            byte[] sheet = ComposeContactSheet(portraits);
            if (sheet == null)
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Castle scene contact sheet could not be composed. session=" + session.SessionKey);
                return string.Empty;
            }
            string orderedIds = string.Join(",", ordered.Select(x => x.StringId));
            string fingerprints = string.Join(",", portraits.Select(Hash));
            string cacheKey = Hash(System.Text.Encoding.UTF8.GetBytes(CastleSceneRenderContract + "|" + session.SessionKey + "|" + orderedIds + "|" + fingerprints + "|" + session.PromptRevision));
            session.ImageCacheKey = cacheKey; session.ImageStatus = "generating";
            JObject response = await ReignServerClient.PostCastleSceneAsync(new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(), ["timelineId"] = session.TimelineId,
                ["settlementId"] = session.SettlementStringId, ["room"] = ((CastleRoom)session.Room).ToString(),
                ["timeBlock"] = ((CastleTimeBlock)session.TimeBlock).ToString(), ["cacheKey"] = cacheKey,
                ["renderContract"] = CastleSceneRenderContract,
                ["sceneInterface"] = "castle_keep_location",
                ["prompt"] = session.ImagePromptSnapshot, ["promptRevision"] = session.PromptRevision,
                ["contactSheetBase64"] = Convert.ToBase64String(sheet), ["orderedParticipants"] = new JArray(ordered.Select(x => x.StringId))
            }).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true)
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Castle scene provider failed for session=" + session.SessionKey
                    + ": " + (response.Value<string>("error") ?? "unknown image-provider failure"));
                return string.Empty;
            }
            byte[] image = Convert.FromBase64String(response.Value<string>("imageBase64") ?? string.Empty);
            string root = FindModuleRoot();
            if (image.Length == 0 || string.IsNullOrWhiteSpace(root))
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Castle scene response contained no usable PNG or module root. session=" + session.SessionKey);
                return string.Empty;
            }
            string folder = Path.Combine(root, "CastleChatArt"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, ReignEventArtTextureFactory.BuildImageId("key", session.SessionKey).Split('|').Last() + ".png");
            File.WriteAllBytes(path, image);
            await PublishSceneAsync(session, path).ConfigureAwait(false);
            ReignLog.Info("Castle scene ready session=" + session.SessionKey + " path=" + path);
            return path;
        }

        public static async Task<string> PrepareFamilyAsync(FamilyChambersSessionRecord family)
        {
            CastleRoomSessionRecord session = family?.ChatRecord;
            if (session == null) return string.Empty;
            if (session.ImageStatus == "ready" && File.Exists(session.ImageLocalPath)) return session.ImageLocalPath;
            if (ReignBetaSettings.Instance?.CastleChatImageGeneration == false)
            {
                session.ImageStatus = "disabled";
                return string.Empty;
            }
            List<Hero> ordered = ReignCourtCampaignBehavior.SplitIds(session.OccupantHeroIdsCsv)
                .Select(id => Hero.FindFirst(x => string.Equals(x.StringId, id, StringComparison.OrdinalIgnoreCase)))
                .Where(hero => hero != null).Take(4).ToList();
            if (ordered.Count == 0) return string.Empty;
            JArray participantReferences = new JArray();
            foreach (Hero hero in ordered.Where(x => !x.IsChild))
            {
                string key = CharacterCacheId.ForHero(hero);
                byte[] portrait = PortraitCache.GetDiskBytes(key);
                if (portrait == null || portrait.Length == 0)
                {
                    JObject generated = await ReignServerClient.GenerateSharedPortraitAsync(key).ConfigureAwait(false);
                    if (generated.Value<bool?>("ok") == true) { PortraitCache.ClearMemoryCache(); portrait = PortraitCache.GetDiskBytes(key); }
                }
                if (portrait != null && portrait.Length > 0)
                {
                    participantReferences.Add(new JObject
                    {
                        ["heroId"] = hero.StringId,
                        ["imageBase64"] = Convert.ToBase64String(portrait)
                    });
                }
            }
            JArray profiles = new JArray(ordered.Select(ReignServerClient.BuildFamilyChambersHeroProfile));
            string cacheKey = Hash(System.Text.Encoding.UTF8.GetBytes("family_chambers_scene_v3|" + session.SessionKey + "|"
                + string.Join(",", ordered.Select(x => x.StringId + ":" + x.Age.ToString("0.00"))) + "|" + session.PromptRevision));
            session.ImageCacheKey = cacheKey;
            session.ImageStatus = "generating";
            JObject payload = new JObject
            {
                ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(), ["timelineId"] = session.TimelineId,
                ["settlementId"] = session.SettlementStringId, ["timeBlock"] = ((CastleTimeBlock)session.TimeBlock).ToString(),
                ["cacheKey"] = cacheKey, ["prompt"] = session.DialoguePromptSnapshot,
                ["orderedParticipants"] = new JArray(ordered.Select(x => x.StringId)), ["participantProfiles"] = profiles,
                ["participantReferences"] = participantReferences,
                ["worldDay"] = family.CreatedDay
            };
            JObject response = await ReignServerClient.PostFamilyChambersSceneAsync(payload).ConfigureAwait(false);
            if (response.Value<bool?>("ok") != true)
            {
                session.ImageStatus = "failed";
                ReignLog.Warn("Family Chambers image generation failed: " + (response.Value<string>("error") ?? "unknown error"));
                return string.Empty;
            }
            byte[] image = Convert.FromBase64String(response.Value<string>("imageBase64") ?? string.Empty);
            string root = FindModuleRoot();
            if (image.Length == 0 || string.IsNullOrWhiteSpace(root)) { session.ImageStatus = "failed"; return string.Empty; }
            string folder = Path.Combine(root, "CastleChatArt"); Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, ReignEventArtTextureFactory.BuildImageId("key", session.SessionKey).Split('|').Last() + ".png");
            File.WriteAllBytes(path, image);
            await PublishSceneAsync(session, path).ConfigureAwait(false);
            return path;
        }

        private static Task PublishSceneAsync(CastleRoomSessionRecord session, string path)
        {
            // Clear the previous texture before publishing ready on the UI thread.
            // A live conversation must never observe ready with a stale cached image.
            return ReignMainThread.InvokeAsync(() =>
            {
                ReignEventArtTextureFactory.Clear();
                session.ImageLocalPath = path;
                session.ImageStatus = "ready";
                session.Revision++;
            });
        }

        private static byte[] ComposeContactSheet(IReadOnlyList<byte[]> images)
        {
            const int cell = 384, height = 512; int width = cell * images.Count;
            byte[] canvas = new byte[width * height * 4];
            for (int i = 0; i < canvas.Length; i += 4) { canvas[i] = 18; canvas[i + 1] = 16; canvas[i + 2] = 14; canvas[i + 3] = 255; }
            for (int index = 0; index < images.Count; index++)
            {
                byte[] source = images[index];
                if (IsJpeg(source)) source = JpegToPng.Convert(source);
                byte[] rgba = PngReencode.DecodeToRgba(source, out int sw, out int sh);
                if (rgba == null) return null;
                float scale = Math.Min((float)cell / sw, (float)height / sh); int dw = Math.Max(1, (int)(sw * scale)), dh = Math.Max(1, (int)(sh * scale));
                int ox = index * cell + (cell - dw) / 2, oy = (height - dh) / 2;
                for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++)
                {
                    int sx = Math.Min(sw - 1, (int)(x / scale)), sy = Math.Min(sh - 1, (int)(y / scale));
                    int src = (sy * sw + sx) * 4, dst = ((oy + y) * width + ox + x) * 4;
                    Buffer.BlockCopy(rgba, src, canvas, dst, 4); canvas[dst + 3] = 255;
                }
            }
            return PngEncoder.EncodeRgba(canvas, width, height);
        }
        private static bool IsJpeg(byte[] bytes) => bytes != null && bytes.Length >= 3
            && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
        private static string Hash(byte[] bytes) { using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        private static string FindModuleRoot()
        {
            DirectoryInfo dir = new FileInfo(typeof(ReignCastleSceneClient).Assembly.Location).Directory;
            while (dir != null) { if (File.Exists(Path.Combine(dir.FullName, "SubModule.xml"))) return dir.FullName; dir = dir.Parent; }
            return null;
        }
    }
}
