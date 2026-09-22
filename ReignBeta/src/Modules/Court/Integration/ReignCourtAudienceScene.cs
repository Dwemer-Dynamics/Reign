#nullable disable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIPortraits;
using Newtonsoft.Json.Linq;

namespace ReignBeta.Integration
{
    internal sealed class CourtSceneViewState
    {
        private int _revision;
        private bool _closed;
        internal string Status { get; private set; } = "pending";
        internal string Error { get; private set; } = "";
        internal string Path { get; private set; } = "";
        internal int Begin() { Status = "pending"; Error = ""; Path = ""; return ++_revision; }
        internal void Close() { _closed = true; }
        internal void Disable(int revision) { if (revision == _revision) Status = "disabled"; }
        internal void Fail(int revision, string error)
        { if (revision == _revision) { Status = "failed"; Error = error; } }
        internal bool Publish(int revision, string path, bool owns, Action<string> persist, Action<string> show)
        {
            if (!owns || revision != _revision) return false;
            persist(path); Path = path; Status = "ready";
            if (!_closed) show(path);
            return true;
        }
    }

    // No campaign/native objects: the complete audience is snapshotted before any await.
    internal sealed class CourtScenePerson
    {
        public string Id, Name, ReferenceStamp;
        public string CharacterCode = "";
        public double Age;
        public byte[] Portrait;
    }

    internal sealed class CourtSceneSnapshot
    {
        public string CampaignId, TimelineId, AudienceId, HostId, CultureId, PublicContext, Folder;
        public string RosterStamp = "";
        public byte[] Hall;
        public List<CourtScenePerson> People = new List<CourtScenePerson>();

        public string Key => ReignCourtAudienceScene.Hash(Encoding.UTF8.GetBytes(new JObject
        {
            ["contract"] = ReignCourtAudienceScene.RenderContract,
            ["campaign"] = CampaignId, ["timeline"] = TimelineId, ["audience"] = AudienceId,
            ["host"] = HostId, ["culture"] = CultureId, ["context"] = PublicContext,
            ["hall"] = ReignCourtAudienceScene.Hash(Hall),
            ["cast"] = new JArray(People.Select(p => new JObject
            { ["id"] = p.Id, ["name"] = p.Name, ["age"] = ReignCourtAudienceScene.ReferenceAge(p.Age), ["reference"] = p.ReferenceStamp }))
        }.ToString(Newtonsoft.Json.Formatting.None)));
    }

    internal static class ReignCourtAudienceScene
    {
        internal const string RenderContract = Reign.Core.Contracts.Court.ReignCourtAudienceArtRules.RenderContract;
        private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Jobs =
            new ConcurrentDictionary<string, Lazy<Task<string>>>(StringComparer.Ordinal);

        internal static List<CourtScenePerson> CompleteCast(IEnumerable<CourtScenePerson> people)
        {
            var result = new List<CourtScenePerson>();
            foreach (CourtScenePerson person in people ?? Enumerable.Empty<CourtScenePerson>())
            {
                if (person == null || string.IsNullOrWhiteSpace(person.Id))
                    throw new InvalidOperationException("An audience participant has no stable identity.");
                CourtScenePerson previous = result.FirstOrDefault(p => string.Equals(p.Id, person.Id, StringComparison.OrdinalIgnoreCase));
                if (previous == null) result.Add(person);
                else if (previous.ReferenceStamp != person.ReferenceStamp)
                    throw new InvalidOperationException("Conflicting references for one audience participant.");
            }
            // Fail explicitly on unreasonable input; never silently take the first four.
            if (result.Count == 0 || result.Count > 48)
                throw new InvalidOperationException("A court scene requires one to 48 involved courtiers.");
            return result;
        }

        internal static string CachedPath(CourtSceneSnapshot scene)
        {
            string path = Path.Combine(scene.Folder, scene.Key + ".png");
            try
            {
                if (!File.Exists(path) || !File.Exists(path + ".json")) return string.Empty;
                JObject receipt = JObject.Parse(File.ReadAllText(path + ".json"));
                if (receipt.Value<string>("snapshotKey") != scene.Key
                    || receipt.Value<string>("imageSha256") != Hash(File.ReadAllBytes(path))) return string.Empty;
                return path;
            }
            catch (Exception) { return string.Empty; }
        }

        internal static async Task<string> PrepareAsync(CourtSceneSnapshot scene,
            Func<CourtScenePerson, Task<byte[]>> reference, Func<JObject, Task<JObject>> provider,
            Func<Task> ensureCurrent)
        {
            scene.People = CompleteCast(scene.People);
            string key = scene.Key;
            Lazy<Task<string>> job = Jobs.GetOrAdd(key, _ => new Lazy<Task<string>>(() =>
                GenerateAsync(scene, reference, provider, ensureCurrent), true));
            try { return await job.Value.ConfigureAwait(false); }
            finally
            {
                // Remove only this job; a later retry must not be removed by another waiter.
                ((ICollection<KeyValuePair<string, Lazy<Task<string>>>>)Jobs)
                    .Remove(new KeyValuePair<string, Lazy<Task<string>>>(key, job));
            }
        }

        private static async Task<string> GenerateAsync(CourtSceneSnapshot scene,
            Func<CourtScenePerson, Task<byte[]>> reference, Func<JObject, Task<JObject>> provider,
            Func<Task> ensureCurrent)
        {
            await ensureCurrent().ConfigureAwait(false);
            string cached = CachedPath(scene);
            if (!string.IsNullOrEmpty(cached)) return cached;
            var portraits = new List<byte[]>();
            foreach (CourtScenePerson person in scene.People)
            {
                await ensureCurrent().ConfigureAwait(false);
                byte[] bytes = await reference(person).ConfigureAwait(false);
                if (bytes == null || bytes.Length == 0)
                    throw new InvalidOperationException("No reference is available for " + person.Name + ". No partial-cast scene was requested.");
                portraits.Add(bytes);
            }
            byte[] composite = Compose(portraits, scene.Hall, out int columns);
            JObject request = BuildRequest(scene, composite, columns);
            await ensureCurrent().ConfigureAwait(false);
            // Exactly one provider invocation and one image-bearing field for the whole audience.
            JObject response = await provider(request).ConfigureAwait(false);
            if (response?.Value<bool?>("ok") != true)
                throw new InvalidOperationException(response?.Value<string>("error") ?? "Court scene generation failed.");
            byte[] image = Convert.FromBase64String(response.Value<string>("imageBase64") ?? "");
            if (PngReencode.DecodeToRgba(image, out int width, out int height) == null || width < 1 || height < 1)
                throw new InvalidOperationException("Court scene response contains no readable image.");
            await ensureCurrent().ConfigureAwait(false);
            Directory.CreateDirectory(scene.Folder);
            string path = Path.Combine(scene.Folder, scene.Key + ".png");
            // Receipt is written last: an interrupted write is never considered a valid cache hit.
            File.WriteAllBytes(path, image);
            File.WriteAllText(path + ".json", new JObject
            {
                ["schema"] = "reign-court-audience-art-v1", ["snapshotKey"] = scene.Key,
                ["renderContract"] = RenderContract, ["imageSha256"] = Hash(image),
                ["compositeSha256"] = Hash(composite), ["inputImageCount"] = 1,
                ["sceneRequestCount"] = 1, ["providerCached"] = response.Value<bool?>("cached") == true,
                ["hostSettlementId"] = scene.HostId, ["hostCultureId"] = scene.CultureId,
                ["orderedParticipants"] = new JArray(scene.People.Select(p => p.Id)),
                ["referenceSha256"] = new JArray(portraits.Select(Hash)), ["slotColumns"] = columns,
                ["providerCacheKey"] = request.Value<string>("cacheKey")
            }.ToString());
            return path;
        }

        internal static JObject BuildRequest(CourtSceneSnapshot scene, byte[] composite, int columns)
        {
            string slots = string.Join("; ", scene.People.Select((p, i) =>
                "row " + (i / columns + 1) + ", column " + (i % columns + 1) + " = " + p.Name
                + " (" + p.Id + ", age " + p.Age.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ")"));
            string prompt = "Create one ultra-photorealistic historical court audience from the ruler's seated viewpoint on an elevated dais, looking slightly downward into the hall. "
                + "The single supplied composite has a LEFT portrait grid and a RIGHT approved hall panel. LEFT identity slots: " + slots + ". "
                + "Show exactly all " + scene.People.Count + " referenced courtiers together, each once, at the foot of the dais, looking upward toward the camera. Preserve every face, age and identity; preserve clothing colors and details only when they are complete and suitable for formal court. Do not merge people. "
                + Reign.Core.Contracts.Court.ReignCourtAudienceArtRules.PromptContract + " "
                + "Only the RIGHT panel controls architecture, materials, lighting, entrance behind the courtiers, open central approach and dais-step corners. Do not rotate, reverse, replace the architecture or look back toward the ruler's throne. This is the host town's " + scene.CultureId + " hall. A visitor's culture must never change the hall. "
                + "The ruler must never appear as a body, face, reflection, silhouette, hand, shadow or portrait. No visible throne, ceremonial chair, ruler's seat or throne back anywhere. No table, desk, railing, lectern or furniture across the foreground. "
                + "Public audience context: " + scene.PublicContext + ". "
                + "Use natural skin, real cloth, plausible optics and restrained court lighting. No illustration, painting, CGI, waxy skin, repetitive microtexture, text, labels, borders, logos or interface elements. "
                + (scene.People.Any(p => p.Age < 18) ? "This is a safe family audience. Preserve children's exact developmental ages and child proportions with modest ordinary clothing and calm age-appropriate behavior. No adult traits, romance, sexuality, danger, punishment, weapons or combat. " : "");
            return new JObject
            {
                ["campaignId"] = scene.CampaignId, ["timelineId"] = scene.TimelineId,
                ["settlementId"] = scene.HostId, ["room"] = "throne_petition", ["timeBlock"] = "audience",
                ["cacheKey"] = Hash(Encoding.UTF8.GetBytes(scene.Key + "|" + Hash(composite))),
                ["renderContract"] = RenderContract, ["promptRevision"] = RenderContract,
                ["prompt"] = prompt, ["contactSheetBase64"] = Convert.ToBase64String(composite),
                ["orderedParticipants"] = new JArray(scene.People.Select(p => p.Id))
            };
        }

        internal static byte[] Compose(IReadOnlyList<byte[]> portraits, byte[] hall, out int columns)
        {
            if (portraits == null || portraits.Count < 1 || portraits.Count > 48)
                throw new InvalidOperationException("Invalid full-cast reference count.");
            const int width = 3072, height = 1536, gridWidth = 1280, gap = 16;
            columns = (int)Math.Ceiling(Math.Sqrt(portraits.Count));
            int rows = (portraits.Count + columns - 1) / columns;
            byte[] canvas = new byte[width * height * 4];
            for (int i = 0; i < canvas.Length; i += 4)
            { canvas[i] = 18; canvas[i + 1] = 16; canvas[i + 2] = 14; canvas[i + 3] = 255; }
            for (int i = 0; i < portraits.Count; i++)
                DrawFitted(canvas, width, portraits[i], i % columns * (gridWidth / columns),
                    i / columns * (height / rows), gridWidth / columns - gap, height / rows - gap);
            DrawFitted(canvas, width, hall, gridWidth + gap, 0, width - gridWidth - gap, height);
            return PngEncoder.EncodeRgba(canvas, width, height);
        }

        private static void DrawFitted(byte[] canvas, int canvasWidth, byte[] source,
            int left, int top, int boxWidth, int boxHeight)
        {
            byte[] rgba = PngReencode.DecodeToRgba(source, out int sw, out int sh);
            if (rgba == null || sw < 1 || sh < 1) throw new InvalidOperationException("An audience reference could not be decoded; no scene was requested.");
            double scale = Math.Min((double)boxWidth / sw, (double)boxHeight / sh);
            int dw = Math.Max(1, (int)(sw * scale)), dh = Math.Max(1, (int)(sh * scale));
            int ox = left + (boxWidth - dw) / 2, oy = top + (boxHeight - dh) / 2;
            for (int y = 0; y < dh; y++) for (int x = 0; x < dw; x++)
            {
                double sx = Math.Max(0, Math.Min(sw - 1, (x + 0.5) / scale - 0.5));
                double sy = Math.Max(0, Math.Min(sh - 1, (y + 0.5) / scale - 0.5));
                int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(sw - 1, x0 + 1), y1 = Math.Min(sh - 1, y0 + 1);
                double wx = sx - x0, wy = sy - y0;
                int dst = ((oy + y) * canvasWidth + ox + x) * 4;
                // Bilinear, alpha-composited sampling preserves portrait reference detail.
                for (int c = 0; c < 3; c++)
                {
                    double Sample(int px, int py)
                    {
                        int src = (py * sw + px) * 4;
                        return (rgba[src + c] * rgba[src + 3] + canvas[dst + c] * (255 - rgba[src + 3])) / 255d;
                    }
                    double topValue = Sample(x0, y0) * (1 - wx) + Sample(x1, y0) * wx;
                    double bottomValue = Sample(x0, y1) * (1 - wx) + Sample(x1, y1) * wx;
                    canvas[dst + c] = (byte)Math.Round(topValue * (1 - wy) + bottomValue * wy);
                }
            }
        }

        internal static string HallReference(string culture)
        {
            switch ((culture ?? "").Trim().ToLowerInvariant())
            {
                case "empire": case "battania": case "vlandia": case "sturgia": case "aserai": case "khuzait":
                    return "ruler-petition-throne-viewpoint-" + culture.Trim().ToLowerInvariant() + ".png";
                default: return "ruler-petition-throne-viewpoint-reference.png";
            }
        }

        internal static double ReferenceAge(double age) => double.IsNaN(age) || double.IsInfinity(age)
            ? 30 : Math.Floor(Math.Max(0, Math.Min(128, age)));

        internal static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes ?? new byte[0])).Replace("-", "").ToLowerInvariant();
        }
    }
}
