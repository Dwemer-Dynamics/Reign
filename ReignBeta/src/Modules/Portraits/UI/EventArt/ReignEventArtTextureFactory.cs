using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using AIPortraits;
using ReignBeta.Integration;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace ReignBeta.UI.EventArt
{
    public static class ReignEventArtTextureFactory
    {
        private const string ImageIdPrefix = "reigneventart|";
        private const string MemorySceneTemplateId = "memory_scene";
        private const string CourtPetitionSceneTemplateId = "court_petition_scene";
        private static readonly ConcurrentDictionary<string, Texture> BuiltTextures = new ConcurrentDictionary<string, Texture>();
        private static readonly ConcurrentDictionary<string, string> MemoryScenePaths = new ConcurrentDictionary<string, string>();
        private static readonly ConcurrentDictionary<string, string> CourtPetitionScenePaths = new ConcurrentDictionary<string, string>();
        private static string _eventArtRoot;

        public static string BuildImageId(string templateId, string phaseId)
        {
            return ImageIdPrefix + CleanPart(templateId) + "|" + CleanPart(phaseId);
        }

        public static string BuildGeneratedWildernessImageId(string terrainKey)
        {
            return BuildImageId("generated_wilderness", "terrain_" + CleanPart(terrainKey));
        }

        public static string BuildWildernessBackgroundImageId()
        {
            return BuildImageId("generated_wilderness", "background");
        }

        public static string BuildCastleMapImageId(string cultureId)
        {
            return BuildImageId("castle_map", string.IsNullOrWhiteSpace(cultureId) ? "generic" : cultureId);
        }

        public static string StoreTavernHouseScene(string cacheKey, byte[] png)
        {
            if (string.IsNullOrWhiteSpace(cacheKey) || cacheKey.Length != 64 || cacheKey.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("A scene content hash is required.", nameof(cacheKey));
            if (png == null || png.Length == 0 || png.Length > 25 * 1024 * 1024)
                throw new ArgumentException("The scene image is empty or too large.", nameof(png));
            string moduleRoot = GetModuleRoot();
            if (string.IsNullOrWhiteSpace(moduleRoot)) throw new IOException("The module image directory is unavailable.");
            string directory = Path.Combine(moduleRoot, "TavernHouseArt");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, cacheKey + ".png");
            if (!File.Exists(path)) File.WriteAllBytes(path, png);
            return BuildImageId("tavern_house_scene", cacheKey);
        }

        public static string BuildCastleSceneImageId(string sessionKey)
        {
            return BuildImageId("castle_scene", sessionKey);
        }

        public static string BuildMemorySceneImageId(string imagePath)
        {
            if (!TryResolveMemoryScenePath(imagePath, out string fullPath)) return string.Empty;

            string phaseId = BuildStableMemorySceneKey(fullPath);
            MemoryScenePaths[MemorySceneTemplateId + "|" + phaseId] = fullPath;
            return BuildImageId(MemorySceneTemplateId, phaseId);
        }

        public static string BuildCourtPetitionSceneImageId(string imagePath)
        {
            if (!TryResolveCourtPetitionScenePath(imagePath, out string fullPath))
                return string.Empty;

            string phaseId = BuildStableSceneKey(fullPath);
            CourtPetitionScenePaths[CourtPetitionSceneTemplateId + "|" + phaseId] = fullPath;
            return BuildImageId(CourtPetitionSceneTemplateId, phaseId);
        }

        public static string BuildCourtPetitionReferenceImageId(string cultureId)
        {
            string culture = CleanPart(cultureId);
            switch (culture)
            {
                case "empire":
                case "vlandia":
                case "sturgia":
                case "battania":
                case "aserai":
                case "khuzait":
                    return BuildImageId("court_petition_reference", culture);
                default:
                    return BuildImageId("court_petition_reference", "generic");
            }
        }

        public static Texture GetOrBuild(string imageId)
        {
            if (!TryParseImageId(imageId, out string templateId, out string phaseId)) return null;
            string cacheKey = templateId + "|" + phaseId;
            if (BuiltTextures.TryGetValue(cacheKey, out Texture existing)) return existing;

            string imagePath = ResolveImagePath(templateId, phaseId);
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            {
                Debug.Print("[Bannerlord Reign] No event art found for '" + cacheKey + "'.");
                return null;
            }

            // The protected War Council map and its tiles intentionally keep their
            // authored source bytes. Correct their display channels only in memory;
            // other War Council artwork and the main-menu logo retain their existing
            // file-backed route.
            bool isWarCouncilRuntimeAsset = string.Equals(templateId, "war_council", StringComparison.Ordinal);
            bool isWarCouncilMapAsset = isWarCouncilRuntimeAsset
                && (string.Equals(phaseId, "map", StringComparison.Ordinal)
                    || phaseId.StartsWith("map_tile_", StringComparison.Ordinal));
            bool isMainMenuLogo = string.Equals(cacheKey, "main_menu|logo", StringComparison.Ordinal);
            bool isEngineReadyRuntimeAsset = (isWarCouncilRuntimeAsset && !isWarCouncilMapAsset) || isMainMenuLogo;
            string textureKey = "reign_event_art_" + cacheKey.Replace('|', '_');
            Texture texture = isWarCouncilMapAsset
                ? TextureFactory.GetOrBuildFileRedBlueSwapped(textureKey, imagePath)
                : isEngineReadyRuntimeAsset
                    ? TextureFactory.GetOrBuildFileBacked(textureKey, imagePath)
                        ?? TextureFactory.GetOrBuildFile(textureKey, imagePath)
                    : TextureFactory.GetOrBuildFile(textureKey, imagePath);
            if (texture != null)
            {
                BuiltTextures[cacheKey] = texture;
                if (string.Equals(cacheKey, "main_menu|logo", StringComparison.Ordinal))
                {
                    ReignLog.Info("Loaded the Reign main-menu logo directly from " + imagePath + ".");
                }
            }
            return texture;
        }

        public static string ResolveImagePath(string templateId, string phaseId)
        {
            if (string.Equals(templateId, MemorySceneTemplateId, StringComparison.Ordinal))
            {
                string key = MemorySceneTemplateId + "|" + CleanPart(phaseId);
                if (MemoryScenePaths.TryGetValue(key, out string memoryScenePath)
                    && File.Exists(memoryScenePath))
                    return memoryScenePath;
                return null;
            }

            if (string.Equals(templateId, CourtPetitionSceneTemplateId, StringComparison.Ordinal))
            {
                string key = CourtPetitionSceneTemplateId + "|" + CleanPart(phaseId);
                if (CourtPetitionScenePaths.TryGetValue(key, out string courtScenePath)
                    && File.Exists(courtScenePath))
                    return courtScenePath;
                return null;
            }

            if (string.Equals(templateId, "court_petition_reference", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
                string culture = CleanPart(phaseId);
                string file = culture == "generic"
                    ? "ruler-petition-throne-viewpoint-reference.png"
                    : "ruler-petition-throne-viewpoint-" + culture + ".png";
                string path = Path.Combine(moduleRoot, "GUI", "UiCalibration",
                    "reference-scenes", file);
                if (File.Exists(path)) return path;
                string fallback = Path.Combine(moduleRoot, "GUI", "UiCalibration",
                    "reference-scenes", "ruler-petition-throne-viewpoint-reference.png");
                return File.Exists(fallback) ? fallback : null;
            }

            if (string.Equals(templateId, "war_council", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
                string file;
                string cleanPhase = CleanPart(phaseId);
                if (cleanPhase.StartsWith("map_tile_", StringComparison.Ordinal))
                {
                    file = "reign_war_council_tile_" + cleanPhase.Substring("map_tile_".Length) + ".png";
                }
                else switch (cleanPhase)
                {
                    case "map": file = "reign_war_council_calradia.png"; break;
                    case "outer_frame": file = "reign_war_council_outer_frame.png"; break;
                    case "panel_frame": file = "reign_war_council_panel_frame.png"; break;
                    case "panel_frame_tall": file = "reign_war_council_panel_frame_tall.png"; break;
                    case "panel_frame_overlay": file = "reign_war_council_panel_frame_overlay.png"; break;
                    case "token_infantry": file = "reign_war_token_infantry.png"; break;
                    case "token_archer": file = "reign_war_token_archer.png"; break;
                    case "token_cavalry": file = "reign_war_token_cavalry.png"; break;
                    case "token_ship": file = "reign_war_token_ship.png"; break;
                    case "token_infantry_black": file = "reign_war_token_infantry_black.png"; break;
                    case "token_archer_black": file = "reign_war_token_archer_black.png"; break;
                    case "token_cavalry_black": file = "reign_war_token_cavalry_black.png"; break;
                    case "token_ship_black": file = "reign_war_token_ship_black.png"; break;
                    case "raven_scroll": file = "reign_war_raven_scroll.png"; break;
                    default: return null;
                }
                string path = Path.Combine(moduleRoot, "GUI", "SpriteParts",
                    "ui_reignbeta_war_council", file);
                return File.Exists(path) ? path : null;
            }

            if (string.Equals(templateId, "castle_map", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                string culture = CleanPart(phaseId);
                string map = string.IsNullOrWhiteSpace(moduleRoot) ? null : Path.Combine(moduleRoot, "GUI", "SpriteParts",
                    "ui_reignbeta_castle_layout", "reign_castle_map_" + culture + ".png");
                if (!string.IsNullOrWhiteSpace(map) && File.Exists(map)) return map;
                string generic = string.IsNullOrWhiteSpace(moduleRoot) ? null : Path.Combine(moduleRoot, "GUI", "SpriteParts",
                    "ui_reignbeta_castle_layout", "reign_castle_layout_generic.png");
                return !string.IsNullOrWhiteSpace(generic) && File.Exists(generic) ? generic : null;
            }

            if (string.Equals(templateId, "tavern_house_scene", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
                string path = Path.Combine(moduleRoot, "TavernHouseArt", CleanPart(phaseId) + ".png");
                return File.Exists(path) ? path : null;
            }

            if (string.Equals(templateId, "castle_scene", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
                string session = CleanPart(phaseId);
                string path = Path.Combine(moduleRoot, "CastleChatArt", session + ".png");
                return File.Exists(path) ? path : null;
            }

            if (string.Equals(templateId, "main_menu", StringComparison.Ordinal)
                && string.Equals(phaseId, "logo", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                string logo = string.IsNullOrWhiteSpace(moduleRoot)
                    ? null
                    : Path.Combine(
                        moduleRoot,
                        "GUI",
                        "SpriteParts",
                        "ui_reignbeta_main_menu",
                        "reign_main_menu_logo.png");
                return !string.IsNullOrWhiteSpace(logo) && File.Exists(logo) ? logo : null;
            }

            if (string.Equals(templateId, "generated_wilderness", StringComparison.Ordinal)
                && string.Equals(phaseId, "background", StringComparison.Ordinal))
            {
                string moduleRoot = GetModuleRoot();
                string background = string.IsNullOrWhiteSpace(moduleRoot) ? null : Path.Combine(moduleRoot, "GUI", "SpriteParts", "ui_reignbeta_event", "reign_wilderness_event_background.png");
                return !string.IsNullOrWhiteSpace(background) && File.Exists(background) ? background : null;
            }

            string root = GetEventArtRoot();
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;

            if (string.Equals(templateId, "generated_wilderness", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(phaseId)
                && phaseId.StartsWith("terrain_", StringComparison.Ordinal))
            {
                string terrainKey = CleanPart(phaseId.Substring("terrain_".Length));
                string generatedRoot = Path.Combine(root, "generated_wilderness");
                string terrainImage = FindPreferredPngInFolder(Path.Combine(generatedRoot, terrainKey));
                if (!string.IsNullOrWhiteSpace(terrainImage)) return terrainImage;
                string plainImage = FindPreferredPngInFolder(Path.Combine(generatedRoot, "plain"));
                return !string.IsNullOrWhiteSpace(plainImage) ? plainImage : FindPreferredPngUnderRoot(generatedRoot);
            }

            string exact = Path.Combine(root, templateId, phaseId + ".png");
            if (File.Exists(exact)) return exact;

            string eventKind = GetEventKind(templateId);
            string samePhase = Directory.GetDirectories(root, eventKind + "_*")
                .Select(folder => Path.Combine(folder, phaseId + ".png"))
                .FirstOrDefault(File.Exists);
            if (!string.IsNullOrWhiteSpace(samePhase)) return samePhase;

            string sameTemplateFolder = Path.Combine(root, templateId);
            if (Directory.Exists(sameTemplateFolder))
            {
                string sameTemplate = Directory.GetFiles(sameTemplateFolder, "*.png").OrderBy(path => path).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(sameTemplate)) return sameTemplate;
            }

            return Directory.GetFiles(root, "*.png", SearchOption.AllDirectories)
                .Where(path => path.IndexOf(Path.DirectorySeparatorChar + "_reference" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) < 0)
                .OrderBy(path => path)
                .FirstOrDefault();
        }

        public static string ResolveWildernessImagePath(string terrainKey)
        {
            return ResolveImagePath("generated_wilderness", "terrain_" + CleanPart(terrainKey));
        }

        public static void Clear()
        {
            BuiltTextures.Clear();
        }

        private static string FindPreferredPngInFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return null;
            string generic = Path.Combine(folder, "generic.png");
            if (File.Exists(generic)) return generic;
            return Directory.GetFiles(folder, "*.png")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => path)
                .FirstOrDefault();
        }

        private static string FindPreferredPngUnderRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
            return Directory.GetFiles(root, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ThenBy(path => path)
                .FirstOrDefault();
        }

        private static bool TryParseImageId(string imageId, out string templateId, out string phaseId)
        {
            templateId = null;
            phaseId = null;
            if (string.IsNullOrWhiteSpace(imageId) || !imageId.StartsWith(ImageIdPrefix, StringComparison.Ordinal)) return false;
            string[] parts = imageId.Substring(ImageIdPrefix.Length).Split('|');
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) return false;
            templateId = CleanPart(parts[0]);
            phaseId = CleanPart(parts[1]);
            return true;
        }

        private static string GetEventArtRoot()
        {
            if (!string.IsNullOrWhiteSpace(_eventArtRoot)) return _eventArtRoot;
            string configured = Environment.GetEnvironmentVariable("BANNERLORD_REIGN_EVENT_ART_ROOT");
            if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured)) return _eventArtRoot = configured;
            string moduleRoot = GetModuleRoot();
            return string.IsNullOrWhiteSpace(moduleRoot) ? null : _eventArtRoot = Path.Combine(moduleRoot, "EventArt");
        }

        private static string GetModuleRoot()
        {
            DirectoryInfo directory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SubModule.xml"))) return directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static string GetEventKind(string templateId)
        {
            int index = (templateId ?? string.Empty).IndexOf("_", StringComparison.Ordinal);
            return index > 0 ? templateId.Substring(0, index) : templateId;
        }

        private static bool TryResolveMemoryScenePath(string imagePath, out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(imagePath)) return false;

            try
            {
                string candidate = Path.GetFullPath(imagePath);
                string memoriesRoot = Path.GetFullPath(MemoryService.MemoriesDir)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string extension = Path.GetExtension(candidate);
                if (!candidate.StartsWith(memoriesRoot, StringComparison.OrdinalIgnoreCase)
                    || (!string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase))
                    || !File.Exists(candidate))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveCourtPetitionScenePath(string imagePath,
            out string fullPath)
        {
            fullPath = null;
            if (string.IsNullOrWhiteSpace(imagePath)) return false;

            try
            {
                string moduleRoot = GetModuleRoot();
                if (string.IsNullOrWhiteSpace(moduleRoot)) return false;
                string petitionArtRoot = Path.GetFullPath(Path.Combine(moduleRoot,
                        "CourtPetitionArt"))
                    .TrimEnd(Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(imagePath);
                string extension = Path.GetExtension(candidate);
                if (!candidate.StartsWith(petitionArtRoot,
                        StringComparison.OrdinalIgnoreCase)
                    || (!string.Equals(extension, ".png",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".jpg",
                            StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(extension, ".jpeg",
                            StringComparison.OrdinalIgnoreCase))
                    || !File.Exists(candidate))
                    return false;

                fullPath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildStableMemorySceneKey(string fullPath)
        {
            return BuildStableSceneKey(fullPath);
        }

        private static string BuildStableSceneKey(string fullPath)
        {
            // FNV-1a keeps a short deterministic key after each caller confines
            // the registered path to its own Reign-owned image directory. The
            // last-write time changes the id when a generated image is replaced.
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                string normalized = fullPath.ToUpperInvariant();
                for (int i = 0; i < normalized.Length; i++)
                {
                    hash ^= normalized[i];
                    hash *= 1099511628211UL;
                }

                return hash.ToString("x16") + "_" + File.GetLastWriteTimeUtc(fullPath).Ticks.ToString("x");
            }
        }

        private static string CleanPart(string value)
        {
            string clean = (value ?? string.Empty).Trim().ToLowerInvariant()
                .Replace("\\", string.Empty).Replace("/", string.Empty).Replace("..", string.Empty);
            foreach (char invalid in Path.GetInvalidFileNameChars()) clean = clean.Replace(invalid, '_');
            // The image-id contract itself uses a pipe delimiter, so a persisted
            // session identity must never be allowed to introduce another one.
            return clean.Replace('|', '_').Replace(':', '_');
        }
    }
}
