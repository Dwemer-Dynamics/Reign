#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReignBeta.WorldSimulation.Runtime
{
    internal sealed class ReignCalendarStore
    {
        private readonly string _path;
        internal ReignCalendarState State { get; }
        internal string? PublishedSha256 { get; private set; }

        private ReignCalendarStore(string path, ReignCalendarState state, string? digest)
        {
            _path = path;
            State = state;
            PublishedSha256 = digest;
        }

        internal static bool CanCreateForLoadedSave(bool hasPoint, bool registered, string? digest, bool? prepared)
        {
            // A save taken before native foundations can already have an unregistered point.
            // Pre-Reign saves have neither a preparation journal nor a native point.
            if (registered || !string.IsNullOrEmpty(digest)) return false;
            return prepared == false || (!prepared.HasValue && !hasPoint);
        }

        // Save Sync aligns the active campaign directory with the loaded native save before
        // this read. Never search adjacent campaigns, other save points or temporary files.
        internal static ReignCalendarStore Open(string path, string campaignId, bool firstUse,
            string? checkpointSha256, Func<ReignCalendarState> create)
        {
            byte[]? bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { bytes = null; }
            catch (DirectoryNotFoundException) { bytes = null; }
            if (bytes == null)
            {
                if (!firstUse || !string.IsNullOrEmpty(checkpointSha256))
                    throw new FileNotFoundException("The loaded save is missing its Reign calendar. Restore its matching Save Sync checkpoint.", path);
                return new ReignCalendarStore(path, create(), null);
            }

            ReignCalendarState state;
            try
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true))
                {
                    var json = JObject.Parse(reader.ReadToEnd(), new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    state = ReadState(json, campaignId);
                }
            }
            catch (Exception ex) when (ex is JsonException || ex is DecoderFallbackException || ex is OverflowException)
            {
                throw new InvalidDataException("Invalid Reign calendar data. Restore its matching Save Sync checkpoint.", ex);
            }
            string digest = Digest(bytes);
            if (!string.IsNullOrEmpty(checkpointSha256) && !string.Equals(digest, checkpointSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The Reign calendar does not match the registered native save point.");
            return new ReignCalendarStore(path, state, digest);
        }

        internal void VerifyPublished()
        {
            if (Digest(File.ReadAllBytes(_path)) != PublishedSha256)
                throw new IOException("The Reign calendar changed on disk since it was loaded. Reload the matching save before writing.");
        }

        internal void Publish()
        {
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(State, Formatting.Indented));
            string digest = Digest(bytes);
            string directory = Path.GetDirectoryName(_path) ?? throw new IOException("The calendar needs a campaign directory.");
            if (PublishedSha256 == null) Directory.CreateDirectory(directory);
            string temporary = Path.Combine(directory, Path.GetRandomFileName());
            bool ownsTemporary = false;
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    ownsTemporary = true;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (PublishedSha256 == null)
                    File.Move(temporary, _path);
                else
                {
                    VerifyPublished();
                    ReplaceExisting(temporary, _path);
                }
                ownsTemporary = false;
                PublishedSha256 = digest;
            }
            finally
            {
                if (ownsTemporary) File.Delete(temporary);
            }
        }

        private static void ReplaceExisting(string temporary, string destination)
        {
            // .NET Framework lacks File.Move(overwrite: true). Use one same-volume rename.
            // Windows ReplaceFile without a backup can remove the original on a failed move.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                const uint replaceExisting = 1;
                const uint writeThrough = 8;
                if (!MoveFileExW(temporary, destination, replaceExisting | writeThrough))
                    throw new IOException("Could not publish the Reign calendar.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            else
            {
                File.Replace(temporary, destination, null);
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);

        private static ReignCalendarState ReadState(JObject json, string campaignId)
        {
            var version = Field(json, "Version");
            if (version?.Type != JTokenType.Integer) throw new InvalidDataException("Invalid Reign calendar version field.");
            if (!JToken.DeepEquals(version, new JValue(1))) throw new InvalidDataException("Unsupported Reign calendar version " + version + ".");
            var id = Field(json, "CampaignId");
            string? storedCampaign = id?.Type == JTokenType.String ? (string?)id : null;
            if (string.IsNullOrWhiteSpace(storedCampaign))
                throw new InvalidDataException("Invalid Reign calendar campaign ID.");
            if (!string.Equals(storedCampaign, campaignId, StringComparison.Ordinal))
                throw new InvalidDataException("The Reign calendar belongs to a different campaign.");
            var heroes = Field(json, "AgeAnchors") as JObject;
            if (heroes == null) throw new InvalidDataException("Invalid Reign calendar age anchors.");
            var state = new ReignCalendarState
            {
                Version = 1,
                CampaignId = campaignId,
                NativeDayAnchor = Number(json, "NativeDayAnchor"),
                CalendarDayAnchor = Number(json, "CalendarDayAnchor"),
                AgeAnchors = new Dictionary<string, ReignAgeAnchor>(StringComparer.Ordinal)
            };
            foreach (var entry in heroes.Properties())
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || !(entry.Value is JObject hero))
                    throw new InvalidDataException("Invalid Reign calendar hero age anchor.");
                float age = (float)Number(hero, "Age");
                if (age < 0f || age >= 200f) throw new InvalidDataException("Invalid Reign calendar hero age.");
                state.AgeAnchors.Add(entry.Name, new ReignAgeAnchor { Day = Number(hero, "Day"), Age = age });
            }
            return state;
        }

        private static double Number(JObject json, string name)
        {
            var value = Field(json, name);
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float))
                throw new InvalidDataException("Invalid Reign calendar " + name + ".");
            double number = (double)value;
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new InvalidDataException("Invalid Reign calendar " + name + ".");
            return number;
        }

        private static JToken? Field(JObject json, string name)
        {
            JToken? value = null;
            foreach (var field in json.Properties())
            {
                if (!field.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                if (value != null) throw new InvalidDataException("Ambiguous Reign calendar " + name + ".");
                value = field.Value;
            }
            return value;
        }

        private static string Digest(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }

    // All v1 writers persist both day anchors and the hero dictionary. A missing number
    // must not deserialize as zero, which would silently change dates or hero ages.
    internal sealed class ReignCalendarState
    {
        public int Version { get; set; }
        public string CampaignId { get; set; } = string.Empty;
        public double NativeDayAnchor { get; set; }
        public double CalendarDayAnchor { get; set; }
        public Dictionary<string, ReignAgeAnchor> AgeAnchors { get; set; } = new Dictionary<string, ReignAgeAnchor>();

        internal double CalendarDayAt(double nativeDay) => CalendarDayAnchor + (nativeDay - NativeDayAnchor);
        internal double NativeDayAt(double calendarDay) => NativeDayAnchor + (calendarDay - CalendarDayAnchor);
    }

    internal sealed class ReignAgeAnchor
    {
        public double Day { get; set; }
        public float Age { get; set; }

        internal float AgeAt(double nativeDay, double daysPerYear) =>
            (float)Math.Max(0d, Math.Min(200d, Age + (nativeDay - Day) / daysPerYear));
    }
}
