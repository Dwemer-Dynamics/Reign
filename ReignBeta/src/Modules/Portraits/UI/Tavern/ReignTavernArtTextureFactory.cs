using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using AIPortraits;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.Tavern
{
    public static class ReignTavernArtTextureFactory
    {
        private const string ImageIdPrefix = "reigntavern|";
        private const string FrameImageId = ImageIdPrefix + "frame";
        private static readonly ConcurrentDictionary<string, Texture> BuiltTextures =
            new ConcurrentDictionary<string, Texture>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> CultureAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["imperial"] = "empire"
            };

        private static string _tavernArtRoot;

        public static string BuildFrameImageId()
        {
            return FrameImageId;
        }

        public static string SelectRandomSceneImageId(string cultureId)
        {
            string root = GetTavernArtRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return string.Empty;
            }

            string culture = CleanPart(cultureId);
            if (CultureAliases.TryGetValue(culture, out string alias))
            {
                culture = alias;
            }

            string[] files = FindSceneFiles(Path.Combine(root, culture));
            if (files.Length == 0)
            {
                files = Directory.GetDirectories(root)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(FindSceneFiles)
                    .ToArray();
            }

            if (files.Length == 0)
            {
                return string.Empty;
            }

            string selected = files[MBRandom.RandomInt(files.Length)];
            return BuildSceneImageId(selected);
        }

        public static string SelectDeterministicSceneImageId(string cultureId, string stableKey)
        {
            string root = GetTavernArtRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                return string.Empty;
            }

            string culture = CleanPart(cultureId);
            if (CultureAliases.TryGetValue(culture, out string alias))
            {
                culture = alias;
            }

            string[] files = FindSceneFiles(Path.Combine(root, culture));
            if (files.Length == 0)
            {
                files = Directory.GetDirectories(root)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .SelectMany(FindSceneFiles)
                    .ToArray();
            }

            if (files.Length == 0)
            {
                return string.Empty;
            }

            ulong selectionKey = StableSceneKey(culture + "|" + (stableKey ?? string.Empty));
            return BuildSceneImageId(files[(int)(selectionKey % (ulong)files.Length)]);
        }

        public static Texture GetOrBuild(string imageId)
        {
            string path = ResolveImagePath(imageId);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            string cacheKey = imageId.ToLowerInvariant();
            if (BuiltTextures.TryGetValue(cacheKey, out Texture existing))
            {
                return existing;
            }

            Texture texture = TextureFactory.GetOrBuildFile(
                "reign_tavern_" + CharacterCacheId.Sanitize(cacheKey),
                path);
            if (texture != null)
            {
                BuiltTextures[cacheKey] = texture;
            }

            return texture;
        }

        public static string ResolveImagePath(string imageId)
        {
            string root = GetTavernArtRoot();
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(imageId))
            {
                return null;
            }

            if (string.Equals(imageId, FrameImageId, StringComparison.Ordinal))
            {
                return Path.Combine(root, "tavern_frame_modern.png");
            }

            if (!imageId.StartsWith(ImageIdPrefix + "scene|", StringComparison.Ordinal))
            {
                return null;
            }

            string[] parts = imageId.Substring((ImageIdPrefix + "scene|").Length).Split('|');
            if (parts.Length != 2)
            {
                return null;
            }

            string culture = CleanPart(parts[0]);
            string fileName = Path.GetFileName(parts[1]);
            if (string.IsNullOrWhiteSpace(culture) || string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string candidate = Path.GetFullPath(Path.Combine(root, culture, fileName));
            string expectedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return candidate.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase) ? candidate : null;
        }

        private static string[] FindSceneFiles(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(folder)
                .Where(path =>
                {
                    string extension = Path.GetExtension(path);
                    return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
                })
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string BuildSceneImageId(string selectedPath)
        {
            string selectedCulture = new DirectoryInfo(Path.GetDirectoryName(selectedPath)).Name;
            return ImageIdPrefix + "scene|" + CleanPart(selectedCulture) + "|" + Path.GetFileName(selectedPath);
        }

        private static ulong StableSceneKey(string value)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char character in (value ?? string.Empty).ToLowerInvariant())
                {
                    hash ^= character;
                    hash *= 1099511628211UL;
                }

                return hash;
            }
        }

        private static string GetTavernArtRoot()
        {
            if (!string.IsNullOrWhiteSpace(_tavernArtRoot))
            {
                return _tavernArtRoot;
            }

            DirectoryInfo directory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SubModule.xml")))
                {
                    return _tavernArtRoot = Path.Combine(directory.FullName, "TavernArt");
                }

                directory = directory.Parent;
            }

            return null;
        }

        private static string CleanPart(string value)
        {
            return new string((value ?? string.Empty)
                .Trim()
                .ToLowerInvariant()
                .Where(character => char.IsLetterOrDigit(character) || character == '_' || character == '-')
                .ToArray());
        }
    }
}
