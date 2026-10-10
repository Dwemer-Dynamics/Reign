using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignPortraits;

namespace ReignBeta.Integration
{
    /// <summary>Installs an inventory-verified shipped portrait without decoding or regenerating its images.</summary>
    internal static class ReignTavernPortraitPack
    {
        private const string MasterFileName = "portrait.png";
        private static readonly string[] FileNames = { MasterFileName, PortraitDerivativeCore.ThumbnailFileName,
            PortraitDerivativeCore.PartyThumbnailFileName, PortraitDerivativeCore.PortraitFileName,
            PortraitDerivativeCore.ZoomFileName, PortraitDerivativeCore.ManifestFileName };

        // Kept independent of native game state so canonical tests exercise the actual client implementation.
        internal static string Install(string packRoot, string castId, string cacheDirectory)
        {
            if (!Regex.IsMatch(castId ?? "", "^reign_tavern_[A-Za-z0-9_]+$"))
                throw new InvalidDataException("Invalid tavern cast ID.");
            packRoot = Path.GetFullPath(packRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            cacheDirectory = Path.GetFullPath(cacheDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(cacheDirectory, packRoot, StringComparison.OrdinalIgnoreCase)
                || cacheDirectory.StartsWith(packRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The campaign cache must be outside the shipped portrait pack.");

            JObject inventory = JObject.Parse(File.ReadAllText(Path.Combine(packRoot, "portrait-inventory.json")));
            if (inventory.Value<string>("schema") != "reign-tavern-portrait-inventory-v1" || !(inventory["files"] is JArray entries))
                throw new InvalidDataException("The shipped tavern portrait inventory is invalid.");
            var verified = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string sourceDirectory = Path.Combine(packRoot, castId);
            string masterHash = null;
            foreach (string name in FileNames)
            {
                string relative = castId + "/" + name;
                JObject entry = entries.OfType<JObject>().SingleOrDefault(row => row.Value<string>("path") == relative);
                string path = Path.Combine(sourceDirectory, name);
                var info = new FileInfo(path);
                if (entry == null || !info.Exists || info.Length < 1 || info.Length > 32L * 1024 * 1024
                    || entry.Value<long?>("bytes") != info.Length)
                    throw new InvalidDataException("Missing or invalid shipped tavern file: " + relative);
                byte[] bytes = File.ReadAllBytes(path);
                string hash;
                using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                if (!string.Equals(hash, entry.Value<string>("sha256"), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Shipped tavern file failed its inventory hash: " + relative);
                verified.Add(name, bytes);
                if (name == MasterFileName) masterHash = hash;
            }

            PortraitDerivativeManifestV2 manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(
                Encoding.UTF8.GetString(verified[PortraitDerivativeCore.ManifestFileName]));
            string sourcePath = Path.Combine(sourceDirectory, MasterFileName);
            var source = new FileInfo(sourcePath);
            if (manifest == null || manifest.SourceLength != verified[MasterFileName].Length
                || !PortraitDerivativeCore.HasPngDimensions(sourcePath, manifest.SourceWidth, manifest.SourceHeight))
                throw new InvalidDataException("The shipped tavern manifest does not describe its master: " + castId);

            // Packaging/Git/copy operations legitimately change paths and mtimes. The inventory hashes above
            // bind the complete product to its bytes; only the campaign's working manifest uses local mtimes.
            manifest.SourcePath = source.FullName;
            manifest.SourceLastWriteUtcTicks = source.LastWriteTimeUtc.Ticks;
            if (!PortraitDerivativeCore.IsCurrentV2(manifest, sourcePath, sourceDirectory))
                throw new InvalidDataException("The shipped tavern derivative set is invalid: " + castId);

            Directory.CreateDirectory(cacheDirectory);
            foreach (string name in FileNames.Where(name => name != PortraitDerivativeCore.ManifestFileName))
                WriteAtomic(Path.Combine(cacheDirectory, name), verified[name]);
            var cachedMaster = new FileInfo(Path.Combine(cacheDirectory, MasterFileName));
            manifest.SourcePath = cachedMaster.FullName;
            manifest.SourceLength = cachedMaster.Length;
            manifest.SourceLastWriteUtcTicks = cachedMaster.LastWriteTimeUtc.Ticks;
            WriteAtomic(Path.Combine(cacheDirectory, PortraitDerivativeCore.ManifestFileName),
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.Indented)));
            if (!PortraitDerivativeCore.IsCurrentV2(manifest, cachedMaster.FullName, cacheDirectory))
                throw new InvalidDataException("The installed tavern derivative set is incomplete: " + castId);
            return masterHash;
        }

        private static void WriteAtomic(string path, byte[] bytes)
        {
            string temporary = path + ".tavern-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
