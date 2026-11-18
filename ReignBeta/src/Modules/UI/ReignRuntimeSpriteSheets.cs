using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using AIPortraits;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;
using EngineTexture = TaleWorlds.Engine.Texture;
using TwoDimensionTexture = TaleWorlds.TwoDimension.Texture;

namespace ReignBeta.UI
{
    /// <summary>
    /// Loads Reign's generated PNG sprite sheets when the game cannot resolve the
    /// equivalent resource-depot textures. The previewer validates the same manifest,
    /// so a prefab cannot be considered game-ready while its runtime sheet is stale.
    /// </summary>
    internal static class ReignRuntimeSpriteSheets
    {
        private const string CategoryPrefix = "ui_reignbeta_";
        private static readonly object Sync = new object();
        private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
        private static DateTime _nextRetryUtc;
        private static string _moduleRoot;
        private static string _lastFailure;
        private static int _textureSequence;

        public static void EnsureLoaded(bool force = false)
        {
            if (!force && DateTime.UtcNow < _nextRetryUtc) return;

            lock (Sync)
            {
                if (!force && DateTime.UtcNow < _nextRetryUtc) return;

                try
                {
                    SpriteData spriteData = UIResourceManager.SpriteData;
                    if (spriteData == null) return;

                    List<SpriteCategory> categories = spriteData.SpriteCategories.Values
                        .Where(category => category?.Name != null && category.Name.StartsWith(CategoryPrefix, StringComparison.Ordinal))
                        .OrderBy(category => category.Name, StringComparer.Ordinal)
                        .ToList();
                    if (categories.Count == 0) return;
                    if (!force && categories.All(IsReady)) return;

                    string moduleRoot = GetModuleRoot();
                    if (string.IsNullOrWhiteSpace(moduleRoot))
                    {
                        Fail("Could not locate the ReignBeta module root for runtime UI sprite sheets.");
                        return;
                    }

                    string manifestPath = Path.Combine(moduleRoot, "GUI", "RuntimeSpriteSheets", "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        Fail("Runtime UI sprite manifest is missing: " + manifestPath);
                        return;
                    }

                    JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    ValidateSpriteDataHash(moduleRoot, manifest);
                    JObject manifestCategories = manifest["categories"] as JObject
                        ?? throw new InvalidDataException("Runtime UI sprite manifest has no categories object.");

                    var categoryFailures = new List<string>();
                    foreach (SpriteCategory category in categories)
                    {
                        if (!force && IsReady(category)) continue;
                        try
                        {
                            JObject manifestCategory = manifestCategories[category.Name] as JObject
                                ?? throw new InvalidDataException("Runtime UI sprite manifest has no entry for " + category.Name + ".");
                            LoadCategory(moduleRoot, category, manifestCategory);
                        }
                        catch (Exception ex)
                        {
                            categoryFailures.Add(category.Name + ": " + ex.Message);
                        }
                    }

                    if (categoryFailures.Count > 0)
                    {
                        throw new InvalidDataException(string.Join(" | ", categoryFailures));
                    }

                    _lastFailure = null;
                    _nextRetryUtc = DateTime.MinValue;
                }
                catch (Exception ex)
                {
                    Fail("Runtime UI sprite sheet load failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Loads one generated category synchronously. Screens that depend on a
        /// runtime-only atlas must call this before LoadMovie; otherwise Gauntlet can
        /// cache the resource-depot miss and keep blank sprites for that movie.
        /// </summary>
        public static bool EnsureCategoryLoaded(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName) ||
                !categoryName.StartsWith(CategoryPrefix, StringComparison.Ordinal))
            {
                Fail("Invalid Reign runtime UI sprite category: " + (categoryName ?? "<null>") + ".");
                return false;
            }

            lock (Sync)
            {
                try
                {
                    SpriteData spriteData = UIResourceManager.SpriteData;
                    if (spriteData == null)
                    {
                        Fail("Bannerlord UI sprite data is not ready for " + categoryName + ".");
                        return false;
                    }

                    SpriteCategory category;
                    if (!spriteData.SpriteCategories.TryGetValue(categoryName, out category) || category == null)
                    {
                        Fail("Reign runtime UI sprite category is not registered: " + categoryName + ".");
                        return false;
                    }
                    if (IsReady(category)) return true;

                    string moduleRoot = GetModuleRoot();
                    if (string.IsNullOrWhiteSpace(moduleRoot))
                    {
                        Fail("Could not locate the ReignBeta module root for runtime UI sprite sheets.");
                        return false;
                    }

                    string manifestPath = Path.Combine(moduleRoot, "GUI", "RuntimeSpriteSheets", "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        Fail("Runtime UI sprite manifest is missing: " + manifestPath);
                        return false;
                    }

                    JObject manifest = JObject.Parse(File.ReadAllText(manifestPath));
                    ValidateSpriteDataHash(moduleRoot, manifest);
                    JObject manifestCategories = manifest["categories"] as JObject
                        ?? throw new InvalidDataException("Runtime UI sprite manifest has no categories object.");
                    JObject manifestCategory = manifestCategories[categoryName] as JObject
                        ?? throw new InvalidDataException("Runtime UI sprite manifest has no entry for " + categoryName + ".");

                    LoadCategory(moduleRoot, category, manifestCategory);
                    if (!IsReady(category))
                    {
                        throw new InvalidDataException(categoryName + " did not become ready after loading.");
                    }

                    _lastFailure = null;
                    _nextRetryUtc = DateTime.MinValue;
                    return true;
                }
                catch (Exception ex)
                {
                    Fail("Runtime UI sprite category load failed for " + categoryName + ": " + ex.Message);
                    return false;
                }
            }
        }

        private static bool IsReady(SpriteCategory category)
        {
            if (category?.SpriteSheets == null || category.SheetSizes == null) return false;
            if (category.SpriteSheets.Count != category.SpriteSheetCount || category.SheetSizes.Length < category.SpriteSheetCount) return false;

            for (int index = 0; index < category.SpriteSheetCount; index++)
            {
                TwoDimensionTexture sheet = category.SpriteSheets[index];
                Vec2i expected = category.SheetSizes[index];
                if (sheet == null || !sheet.IsValid || sheet.Width != expected.X || sheet.Height != expected.Y) return false;
            }
            return true;
        }

        private static void LoadCategory(string moduleRoot, SpriteCategory category, JObject manifestCategory)
        {
            JArray manifestSheets = manifestCategory["sheets"] as JArray
                ?? throw new InvalidDataException("Runtime UI sprite manifest has no sheets for " + category.Name + ".");
            var builtSheets = new List<TwoDimensionTexture>(category.SpriteSheetCount);
            var nativeTextures = new List<EngineTexture>(category.SpriteSheetCount);

            try
            {
                for (int sheetId = 1; sheetId <= category.SpriteSheetCount; sheetId++)
                {
                    JObject sheetEntry = manifestSheets.OfType<JObject>()
                        .FirstOrDefault(entry => (int?)entry["id"] == sheetId)
                        ?? throw new InvalidDataException(category.Name + " is missing manifest sheet " + sheetId + ".");
                    Vec2i expected = category.SheetSizes[sheetId - 1];
                    if ((int?)sheetEntry["width"] != expected.X || (int?)sheetEntry["height"] != expected.Y)
                    {
                        throw new InvalidDataException(category.Name + " sheet " + sheetId + " dimensions do not match SpriteData.");
                    }

                    string relativePath = ((string)sheetEntry["path"] ?? string.Empty).Replace('/', Path.DirectorySeparatorChar);
                    string sheetPath = ResolveInsideModule(moduleRoot, relativePath);
                    if (!File.Exists(sheetPath)) throw new FileNotFoundException("Generated runtime UI sprite sheet is missing.", sheetPath);

                    byte[] sourceBytes = File.ReadAllBytes(sheetPath);
                    string expectedHash = ((string)sheetEntry["sha256"] ?? string.Empty).Trim();
                    string actualHash = Sha256(sourceBytes);
                    if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(category.Name + " sheet " + sheetId + " is stale; rebuild runtime sprite sheets.");
                    }

                    byte[] engineBytes = PngReencode.ToPngEncoderFormat(sourceBytes) ?? sourceBytes;
                    EngineTexture nativeTexture = EngineTexture.CreateFromMemory(engineBytes);
                    if (nativeTexture == null) throw new InvalidDataException("Bannerlord could not decode " + sheetPath + ".");
                    nativeTextures.Add(nativeTexture);
                    try
                    {
                        nativeTexture.Name = "ReignRuntimeSprite_" + (++_textureSequence) + "_" + category.Name + "_" + sheetId;
                    }
                    catch
                    {
                    }

                    var texture = new TwoDimensionTexture(new TaleWorlds.Engine.GauntletUI.EngineTexture(nativeTexture));
                    if (!texture.IsValid || texture.Width != expected.X || texture.Height != expected.Y)
                    {
                        throw new InvalidDataException(category.Name + " sheet " + sheetId + " decoded at the wrong dimensions.");
                    }
                    builtSheets.Add(texture);
                }

                category.SpriteSheets.Clear();
                category.SpriteSheets.AddRange(builtSheets);
                ReignLog.Info("Loaded " + category.Name + " from generated runtime UI sprite sheets (" + builtSheets.Count + " sheet(s)).");
            }
            catch
            {
                foreach (EngineTexture texture in nativeTextures)
                {
                    try
                    {
                        texture.ReleaseAfterNumberOfFrames(2);
                    }
                    catch
                    {
                    }
                }
                throw;
            }
        }

        private static void ValidateSpriteDataHash(string moduleRoot, JObject manifest)
        {
            string relativePath = ((string)manifest["spriteDataPath"] ?? "GUI/ReignBetaSpriteData.xml")
                .Replace('/', Path.DirectorySeparatorChar);
            string spriteDataPath = ResolveInsideModule(moduleRoot, relativePath);
            if (!File.Exists(spriteDataPath)) throw new FileNotFoundException("Reign sprite data is missing.", spriteDataPath);

            string expectedHash = ((string)manifest["spriteDataSha256"] ?? string.Empty).Trim();
            string actualHash = Sha256(File.ReadAllBytes(spriteDataPath));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("ReignBetaSpriteData.xml changed after the runtime sprite sheets were built.");
            }
        }

        private static string ResolveInsideModule(string moduleRoot, string relativePath)
        {
            string root = Path.GetFullPath(moduleRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(Path.Combine(root, relativePath ?? string.Empty));
            if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Runtime sprite manifest path escapes the module root: " + relativePath);
            }
            return fullPath;
        }

        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
            }
        }

        private static string GetModuleRoot()
        {
            if (!string.IsNullOrWhiteSpace(_moduleRoot)) return _moduleRoot;
            DirectoryInfo directory = new FileInfo(Assembly.GetExecutingAssembly().Location).Directory;
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "SubModule.xml"))) return _moduleRoot = directory.FullName;
                directory = directory.Parent;
            }
            return null;
        }

        private static void Fail(string message)
        {
            _nextRetryUtc = DateTime.UtcNow + RetryDelay;
            if (string.Equals(_lastFailure, message, StringComparison.Ordinal)) return;
            _lastFailure = message;
            ReignLog.Warn(message);
            Debug.Print("[Bannerlord Reign] " + message);
        }
    }
}
