using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Campaign;
using ReignBeta.UI.EventArt;
using TaleWorlds.CampaignSystem;

namespace ReignBeta.Integration
{
    public static partial class ReignServerClient
    {
        public static Task<JObject> PostChatViewAsync(string action, JObject payload) => PostJsonAsync("/chat-view/" + action, payload);
    }

    internal static class ReignChatViewClient
    {
        private static readonly Dictionary<string, string[]> Archives = new Dictionary<string, string[]>();

        internal static JObject Scope(string session = "") => new JObject
        {
            ["campaignId"] = ReignCampaignIdentity.CurrentCampaignId(),
            ["timelineId"] = ReignWorldHistoryCampaignBehavior.Instance?.TimelineId ?? "main",
            ["playerId"] = Hero.MainHero?.StringId ?? "", ["sessionId"] = session ?? "", ["worldDay"] = CampaignTime.Now.ToDays
        };

        private static string ScopeKey(JObject scope) => Hash(Encoding.UTF8.GetBytes((scope.Value<string>("campaignId") ?? "")
            + "|" + (scope.Value<string>("timelineId") ?? "") + "|" + (scope.Value<string>("playerId") ?? "")));

        internal static string[] ArchiveFiles
        {
            get { lock (Archives) { return Archives.TryGetValue(ScopeKey(Scope()), out string[] files) ? files.ToArray() : new string[0]; } }
        }

        internal static void ClearArchive()
        { lock (Archives) Archives.Remove(ScopeKey(Scope())); }

        internal static async Task RefreshArchiveAsync()
        {
            JObject scope = await ReignMainThread.InvokeAsync(() => Scope()).ConfigureAwait(false);
            var files = new List<string>();
            foreach (JObject entry in await ReadEntriesAsync(scope, "archive").ConfigureAwait(false))
            {
                if (entry.Value<string>("status") != "ready") continue;
                JObject itemScope = (JObject)scope.DeepClone(); itemScope["sessionId"] = entry.Value<string>("sessionId") ?? "";
                JObject ready = await DownloadAsync(itemScope, entry).ConfigureAwait(false);
                files.Add(ready.Value<string>("localPath"));
            }
            await ReignMainThread.InvokeAsync(() =>
            {
                if (ScopeKey(Scope()) != ScopeKey(scope)) return;
                lock (Archives) Archives[ScopeKey(scope)] = files.ToArray();
            }).ConfigureAwait(false);
        }

        internal static async Task<List<JObject>> ReadEntriesAsync(JObject scope, string action = "list")
        {
            var entries = new List<JObject>();
            int offset = 0;
            do
            {
                JObject page = (JObject)scope.DeepClone(); page["offset"] = offset;
                JObject result = await ReignServerClient.PostChatViewAsync(action, page).ConfigureAwait(false);
                Check(result);
                entries.AddRange((result["entries"] as JArray ?? new JArray()).OfType<JObject>());
                int next = result.Value<int?>("nextOffset") ?? -1;
                if (next != -1 && next <= offset) throw new InvalidDataException("The image archive returned an invalid continuation.");
                offset = next;
                if (entries.Count > 10000) throw new InvalidDataException("The image archive exceeds the supported book size.");
            } while (offset >= 0);
            return entries;
        }

        internal static async Task<JObject> DownloadAsync(JObject scope, JObject entry)
        {
            string id = entry.Value<string>("jobId") ?? "", expectedHash = entry.Value<string>("imageSha256") ?? "";
            if (id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c)) || expectedHash.Length != 64)
                throw new InvalidDataException("The scene identity is invalid.");
            string folder = Path.Combine(MemoryService.MemoriesDir, "View", ScopeKey(scope));
            string path = Path.Combine(folder, id + ".png"), inline = Path.Combine(folder, id + "_inline.png");
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            if (bytes == null || Hash(bytes) != expectedHash)
            {
                JObject query = (JObject)scope.DeepClone(); query["jobId"] = id; query["includeImage"] = true;
                JObject result = await ReignServerClient.PostChatViewAsync("state", query).ConfigureAwait(false);
                Check(result);
                string encoded = result.Value<string>("imageBase64") ?? "";
                if (encoded.Length > 90 * 1024 * 1024) throw new InvalidDataException("The image is too large.");
                bytes = Convert.FromBase64String(encoded);
                if (Hash(bytes) != expectedHash) throw new InvalidDataException("The scene did not pass its integrity check.");
                byte[] pixels = PngReencode.DecodeToRgba(bytes, out int width, out int height);
                if (pixels == null || (long)width * 2 != (long)height * 3 || width > 8192 || height > 8192)
                    throw new InvalidDataException("The scene is not a supported 3:2 image.");
                Directory.CreateDirectory(folder);
                WriteAtomic(path, bytes);
            }
            if (!File.Exists(inline))
            {
                byte[] rgba = PngReencode.DecodeToRgba(bytes, out int width, out int height);
                const int tw = 768, th = 512;
                var small = new byte[tw * th * 4];
                for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
                {
                    int sx = Math.Min(width - 1, x * width / tw), sy = Math.Min(height - 1, y * height / th);
                    Buffer.BlockCopy(rgba, (sy * width + sx) * 4, small, (y * tw + x) * 4, 4);
                }
                WriteAtomic(inline, PngEncoder.EncodeRgba(small, tw, th));
            }
            string caption = (entry.Value<string>("caption") ?? "A remembered scene.").Replace('\r', ' ').Replace('\n', ' ');
            WriteAtomic(Path.ChangeExtension(path, ".txt"), Encoding.UTF8.GetBytes("Title: " + caption + "\nDescription: " + caption + "\n"));
            var ready = (JObject)entry.DeepClone(); ready["localPath"] = path;
            await ReignMainThread.InvokeAsync(() =>
            {
                ready["imageId"] = ReignEventArtTextureFactory.BuildMemorySceneImageId(inline);
                ready["fullImageId"] = ReignEventArtTextureFactory.BuildMemorySceneImageId(path);
            }).ConfigureAwait(false);
            return ready;
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            ReignChatViewCacheFile.WriteAtomic(path, bytes);
        }

        private static string Hash(byte[] bytes)
        { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }

        internal static void Check(JObject response)
        { if (response?.Value<bool?>("ok") != true) throw new InvalidOperationException(response?.Value<string>("error") ?? "View could not contact the Reign server."); }
    }
}
