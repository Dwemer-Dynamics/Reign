using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignBeta.Integration;
using ReignPortraits;
using Reign.Core.Contracts.Platform;
using TaleWorlds.Library;

namespace AIPortraits;

public sealed class PortraitSeedResult
{
	public bool Ok;

	public bool AlreadySeeded;

	public int FoldersCopied;

	public int FilesCopied;

	public int Indexed;

	public string Message = string.Empty;

	public string Error = string.Empty;
}

public static class PortraitCache
{
	private static readonly ConcurrentDictionary<string, byte> _pending = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> _migrationChecked = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> _seedChecked = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, byte> _normalizationChecked = new ConcurrentDictionary<string, byte>();

	private static readonly ConcurrentDictionary<string, string> _characterPortraitPathCache = new ConcurrentDictionary<string, string>();

	private static readonly ConcurrentDictionary<string, string> _seedPortraitPathByIdentity = new ConcurrentDictionary<string, string>();

	private static readonly ConcurrentDictionary<string, object> _productCommitLocks = new ConcurrentDictionary<string, object>();

	public const string PortraitFileName = "portrait.png";

	public const string CustomFileName = "custom.png";

	public const string SourceFileName = "source.png";

	public const string PromptFileName = "prompt.txt";

	public const string CustomPromptFileName = "custom_prompt.txt";

	public const string PortraitInputFileName = "portrait_input.json";

	public const string GenerationReceiptFileName = ".ai_generation.json";
	public const string PendingGenerationFileName = ".portrait_generation_pending.json";

	private const string SeedMarkerFileName = ".shared_seed_complete.json";

	private static string[] LegacyCacheDirs => new[]
	{
		Path.Combine(BasePath.Name, "Modules", "AIPortraits", "PortraitCache"),
		Path.Combine(BasePath.Name, "Modules", "AIEventsAndIntrigue", "PortraitCache")
	};

	private static readonly Lazy<ReignInstallation> Installation = new Lazy<ReignInstallation>(ReignInstallation.TryLoadCurrent);
	private static string BaseCacheRoot => Installation.Value?.PortraitCacheRoot ?? Path.Combine(BasePath.Name, "Modules", "ReignBeta", "PortraitCache");

	public static string BaseCacheRootForDerivatives => BaseCacheRoot;

	private static string[] ServerCharactersDirs
	{
		get
		{
			string campaignFolder = RequestCampaignFolder;
			if (Installation.Value != null)
				return new[] { Path.Combine(Installation.Value.DataRoot, "campaigns", campaignFolder, "characters") };
			string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
			return new[]
			{
				Path.Combine(BasePath.Name, "Modules", "ReignBeta", "server", "app", "data", "campaigns", campaignFolder, "characters"),
				Path.Combine(desktop, "ReignBeta", "server", "app", "data", "campaigns", campaignFolder, "characters"),
				Path.Combine(desktop, "ReignServer", "app", "data", "campaigns", campaignFolder, "characters")
			};
		}
	}

	public static string SeedCacheDir => Installation.Value?.SharedPortraitRoot ?? Path.Combine(BaseCacheRoot, "_shared");

	internal static string RequestCampaignFolder => PortraitRequestScope.Current?.CampaignFolder ?? ReignCampaignIdentity.CurrentCampaignFolderName();

	public static string CacheDir => Path.Combine(BaseCacheRoot, RequestCampaignFolder);

	private static string SeedMarkerPath => Path.Combine(CacheDir, SeedMarkerFileName);

	public static int CachedCount
	{
		get
		{
			EnsureDirectory();
			if (!Directory.Exists(CacheDir))
			{
				return 0;
			}
			return Directory.GetDirectories(CacheDir).Length;
		}
	}

	public static void EnsureRootDirectory()
	{
		Directory.CreateDirectory(BaseCacheRoot);
	}

	public static void EnsureDirectory()
	{
		EnsureRootDirectory();
		if (!ReignCampaignIdentity.HasActiveCampaign())
		{
			return;
		}
		Directory.CreateDirectory(CacheDir);
	}

	public static bool IsCampaignSeeded()
	{
		EnsureDirectory();
		return File.Exists(SeedMarkerPath);
	}

	public static PortraitSeedResult EnsureSeedCacheForCampaign()
	{
		PortraitSeedResult result = new PortraitSeedResult();
		if (!ReignCampaignIdentity.HasActiveCampaign())
		{
			result.Message = "Load a campaign before preparing portrait cache.";
			return result;
		}

		EnsureDirectory();
		if (File.Exists(SeedMarkerPath))
		{
			result.Ok = true;
			result.AlreadySeeded = true;
			result.FoldersCopied = 0;
			result.FilesCopied = 0;
			result.Indexed = PortraitIndex.EnsureCollisionSafetyForCampaign();
			result.Message = "Portrait cache already prepared for this save.";
			return result;
		}

		if (!Directory.Exists(SeedCacheDir))
		{
			result.Message = "Shared portrait source folder was not found: " + SeedCacheDir;
			return result;
		}

		try
		{
			_characterPortraitPathCache.Clear();
			result.Indexed = PortraitIndex.EnsureCollisionSafetyForCampaign();
			WriteSeedMarker(result);
			result.Ok = true;
			result.Message = "Shared portrait cache linked for this save; indexed links=" + result.Indexed + ".";
			return result;
		}
		catch (Exception ex)
		{
			result.Error = ex.Message;
			result.Message = "Portrait cache preparation failed: " + ex.Message;
			Debug.Print("[AIPortraits] " + result.Message);
			return result;
		}
	}

	public static string DirFor(string id)
	{
		return Path.Combine(CacheDir, id ?? "");
	}

	public static string DiskPath(string id)
	{
		return Path.Combine(DirFor(id), PortraitFileName);
	}

	public static string CustomPath(string id)
	{
		return Path.Combine(DirFor(id), CustomFileName);
	}

	public static string SourcePath(string id)
	{
		return Path.Combine(DirFor(id), SourceFileName);
	}

	public static string PromptPath(string id)
	{
		return Path.Combine(DirFor(id), PromptFileName);
	}

	public static byte[] TryGetFromMemory(string id)
	{
		return null;
	}

	public static bool ExistsOnDisk(string id)
	{
		EnsureDirectory();
		if (!string.IsNullOrEmpty(id))
		{
			return TryGetPreferredDiskPath(id, out var _);
		}
		return false;
	}

	public static bool HasCustomPortrait(string id)
	{
		EnsureDirectory();
		if (!string.IsNullOrEmpty(id))
		{
			if (File.Exists(CustomPath(id)))
			{
				return true;
			}
			return TryGetCharacterPortraitPath(id, requireCustom: true, out var _);
		}
		return false;
	}

	public static async Task<bool> SaveAndPrepareAsync(string id, byte[] pngBytes)
	{
		if (string.IsNullOrEmpty(id) || pngBytes == null || pngBytes.Length == 0)
		{
			return false;
		}

		Directory.CreateDirectory(DirFor(id));
		WriteAtomic(DiskPath(id), pngBytes);
		_characterPortraitPathCache.Clear();
		PortraitDerivativeService.Invalidate(id);
		bool prepared = await PortraitDerivativeService.EnsureForSourceAsync(id, DiskPath(id), highPriority: true, force: true).ConfigureAwait(false);
		if (prepared)
		{
			_ = ReignServerClient.RegisterPortraitFileAsync(id, DiskPath(id), "generated");
		}
		return prepared;
	}

	public static async Task<bool> SavePortraitProductAsync(string id, ReignPortraitGenerationResult product)
	{
		if (string.IsNullOrEmpty(id) || product == null)
		{
			NanoGptClient.ReportPortraitProductError("A portrait cache key and generated product are required.");
			return false;
		}

		try
		{
			ValidatePortraitProduct(product);
		}
		catch (Exception ex) when (ex is InvalidDataException || ex is JsonException || ex is FormatException || ex is ArgumentException)
		{
			// A completed but invalid product cannot improve by recovering it again.
			// Retire only its matching marker; the next explicit Look may start a new job.
			if (!string.IsNullOrWhiteSpace(product.GenerationOperationId))
				ClearPendingGenerationOperation(id, product.GenerationOperationId);
			NanoGptClient.ReportPortraitProductError("The generated portrait was rejected and the existing portrait was kept. " + ex.Message);
			return false;
		}
		catch (Exception ex)
		{
			// Keep delivery recovery available for local resource failures.
			NanoGptClient.ReportPortraitProductError("The generated portrait could not be checked and the existing portrait was kept. " + ex.Message);
			return false;
		}

		try
		{
			string outputDirectory = DirFor(id);
			if (PortraitRequestScope.Current != null && !Directory.Exists(outputDirectory))
				throw new InvalidDataException("The originating campaign portrait request was removed before completion.");
			Directory.CreateDirectory(outputDirectory);
			object commitLock = _productCommitLocks.GetOrAdd(outputDirectory, _ => new object());
			lock (commitLock)
			{
				CommitPortraitProduct(outputDirectory, product);
			}

			_characterPortraitPathCache.Clear();
			PortraitDerivativeService.Invalidate(id);
			bool registered = await ReignServerClient.RegisterPortraitFileAsync(id, DiskPath(id), "generated_product_v1").ConfigureAwait(false);
			if (!registered)
			{
				Debug.Print("[AIPortraits] Portrait product committed but server registration will retry through normal cache discovery: " + id);
			}
			ClearPendingGenerationOperation(id, product.GenerationOperationId);
			return true;
		}
		catch (Exception ex)
		{
			NanoGptClient.ReportPortraitProductError("The generated portrait was rejected and the existing portrait was kept. " + ex.Message);
			return false;
		}
	}

	public static void MarkPendingGenerationOperation(string id, string heroStringId, string operationId)
	{
		if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(operationId)) return;
		try
		{
			Directory.CreateDirectory(DirFor(id));
			JObject marker = new JObject
			{
				["schema"] = "reign-portrait-generation-pending-v1",
				["campaignId"] = RequestCampaignFolder,
				["cacheKey"] = id,
				["heroStringId"] = heroStringId ?? string.Empty,
				["generationOperationId"] = operationId,
				["startedUtc"] = DateTime.UtcNow.ToString("o")
			};
			WriteAtomic(Path.Combine(DirFor(id), PendingGenerationFileName),
				Encoding.UTF8.GetBytes(marker.ToString(Formatting.Indented)));
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Could not persist portrait operation " + operationId + ": " + ex.Message);
		}
	}

	public static bool TryGetPendingGenerationOperation(string id, out string heroStringId, out string operationId)
	{
		heroStringId = string.Empty;
		operationId = string.Empty;
		if (string.IsNullOrWhiteSpace(id)) return false;
		try
		{
			string path = Path.Combine(DirFor(id), PendingGenerationFileName);
			if (!File.Exists(path)) return false;
			JObject marker = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
			if (!string.Equals(marker.Value<string>("schema"), "reign-portrait-generation-pending-v1", StringComparison.Ordinal)
				|| !string.Equals(marker.Value<string>("campaignId"), RequestCampaignFolder, StringComparison.OrdinalIgnoreCase)
				|| !string.Equals(marker.Value<string>("cacheKey"), id, StringComparison.Ordinal))
			{
				return false;
			}
			heroStringId = marker.Value<string>("heroStringId") ?? string.Empty;
			operationId = marker.Value<string>("generationOperationId") ?? string.Empty;
			return operationId.Length == 32;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Could not read pending portrait operation for " + id + ": " + ex.Message);
			return false;
		}
	}

	public static void ClearPendingGenerationOperation(string id, string operationId)
	{
		if (string.IsNullOrWhiteSpace(id)) return;
		try
		{
			string path = Path.Combine(DirFor(id), PendingGenerationFileName);
			if (!File.Exists(path)) return;
			if (!string.IsNullOrWhiteSpace(operationId)
				&& TryGetPendingGenerationOperation(id, out string _, out string current)
				&& !string.Equals(current, operationId, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
			File.Delete(path);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Could not clear pending portrait operation for " + id + ": " + ex.Message);
		}
	}

	private static void ValidatePortraitProduct(ReignPortraitGenerationResult product)
	{
		if (!product.Ok || !product.ProductAccepted
			|| product.ProductVersion != 1
			|| !string.Equals(product.ProductSchema, "reign-portrait-product-v1", StringComparison.Ordinal))
		{
			throw new InvalidDataException("The server did not return an accepted reign-portrait-product-v1 receipt.");
		}
		if (product.ImageBytes == null || product.ImageBytes.Length == 0
			|| product.SourceImageBytes == null || product.SourceImageBytes.Length == 0
			|| string.IsNullOrWhiteSpace(product.EffectivePrompt)
			|| string.IsNullOrWhiteSpace(product.PortraitProductReceiptJson)
			|| string.IsNullOrWhiteSpace(product.PortraitInputJson))
		{
			throw new InvalidDataException("The portrait master, canonical source, effective prompt, or audit receipt was missing.");
		}
		string compositionError = PortraitDerivativeCore.PortraitCompositionError(product.FaceFocus);
		if (compositionError != null)
		{
			throw new InvalidDataException(compositionError);
		}

		byte[] portraitRgba = PngReencode.DecodeToRgba(product.ImageBytes, out int portraitWidth, out int portraitHeight);
		byte[] sourceRgba = PngReencode.DecodeToRgba(product.SourceImageBytes, out int sourceWidth, out int sourceHeight);
		if (portraitRgba == null || portraitWidth != product.PortraitWidth || portraitHeight != product.PortraitHeight)
		{
			throw new InvalidDataException("The portrait master dimensions did not match its signed product receipt.");
		}
		if (sourceRgba == null || sourceWidth != 768 || sourceHeight != 1024
			|| sourceWidth != product.SourceWidth || sourceHeight != product.SourceHeight)
		{
			throw new InvalidDataException("The canonical source was not the required 768x1024 native portrait input.");
		}
		if (!HashEquals(product.PortraitSha256, product.ImageBytes)
			|| !HashEquals(product.SourceSha256, product.SourceImageBytes)
			|| !HashEquals(product.PromptSha256, Encoding.UTF8.GetBytes(product.EffectivePrompt)))
		{
			throw new InvalidDataException("The portrait product hashes did not match the server receipt.");
		}

		JObject receipt = JObject.Parse(product.PortraitProductReceiptJson);
		JObject input = JObject.Parse(product.PortraitInputJson);
		if (!string.Equals(receipt.Value<string>("schema"), "reign-portrait-product-v1", StringComparison.Ordinal)
			|| !string.Equals(input.Value<string>("schema"), "reign-portrait-input-v1", StringComparison.Ordinal))
		{
			throw new InvalidDataException("The portrait product audit documents used an unsupported schema.");
		}
	}

	private static void CommitPortraitProduct(string outputDirectory, ReignPortraitGenerationResult product)
	{
		string token = Guid.NewGuid().ToString("N");
		string masterPath = Path.Combine(outputDirectory, PortraitFileName);
		var master = new ProductCommitItem(masterPath, product.ImageBytes, product.PortraitWidth, product.PortraitHeight, true, token);
		var items = new List<ProductCommitItem> { master };
		try
		{
			File.WriteAllBytes(master.TemporaryPath, master.Bytes);
			master.TemporaryWritten = true;
			if (!PortraitDerivativeCore.HasPngDimensions(master.TemporaryPath, master.Width, master.Height))
			{
				throw new InvalidDataException("The staged portrait master failed PNG validation.");
			}

			byte[] rgba = PngReencode.DecodeToRgba(product.ImageBytes, out int width, out int height);
			FileInfo temporaryMaster = new FileInfo(master.TemporaryPath);
			PortraitDerivativeBuild build = PortraitDerivativeCore.BuildRequiredFocusedV2(
				rgba,
				width,
				height,
				temporaryMaster,
				PngEncoder.EncodeRgba,
				product.FaceFocus);
			build.Manifest.SourceFileName = PortraitFileName;
			build.Manifest.SourcePath = Path.GetFullPath(masterPath);
			build.Manifest.SourceLength = temporaryMaster.Length;
			build.Manifest.SourceLastWriteUtcTicks = temporaryMaster.LastWriteTimeUtc.Ticks;

			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, SourceFileName), product.SourceImageBytes, 768, 1024, true, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PromptFileName), Encoding.UTF8.GetBytes(product.EffectivePrompt), 0, 0, false, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PortraitInputFileName), Encoding.UTF8.GetBytes(product.PortraitInputJson), 0, 0, false, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, GenerationReceiptFileName), Encoding.UTF8.GetBytes(product.PortraitProductReceiptJson), 0, 0, false, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PortraitDerivativeCore.ThumbnailFileName), build.ThumbnailPng, build.Manifest.ThumbnailWidth, build.Manifest.ThumbnailHeight, true, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PortraitDerivativeCore.PartyThumbnailFileName), build.PartyThumbnailPng, build.Manifest.PartyThumbnailWidth, build.Manifest.PartyThumbnailHeight, true, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PortraitDerivativeCore.PortraitFileName), build.PortraitPng, build.Manifest.PortraitWidth, build.Manifest.PortraitHeight, true, token));
			items.Add(new ProductCommitItem(Path.Combine(outputDirectory, PortraitDerivativeCore.ZoomFileName), build.ZoomPng, build.Manifest.ZoomWidth, build.Manifest.ZoomHeight, true, token));
			items.Add(new ProductCommitItem(
				Path.Combine(outputDirectory, PortraitDerivativeCore.ManifestFileName),
				Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(build.Manifest, Formatting.Indented)),
				0,
				0,
				false,
				token));

			foreach (ProductCommitItem item in items)
			{
				if (!item.TemporaryWritten)
				{
					File.WriteAllBytes(item.TemporaryPath, item.Bytes);
					item.TemporaryWritten = true;
				}
				if (item.IsPng && !PortraitDerivativeCore.HasPngDimensions(item.TemporaryPath, item.Width, item.Height))
				{
					throw new InvalidDataException("A staged portrait product file failed PNG validation: " + Path.GetFileName(item.Path));
				}
			}

			foreach (ProductCommitItem item in items)
			{
				item.HadOriginal = File.Exists(item.Path);
				if (item.HadOriginal)
				{
					File.Replace(item.TemporaryPath, item.Path, item.BackupPath);
				}
				else
				{
					File.Move(item.TemporaryPath, item.Path);
				}
				item.Committed = true;
				if (item.IsPng && !PortraitDerivativeCore.HasPngDimensions(item.Path, item.Width, item.Height))
				{
					throw new InvalidDataException("A committed portrait product file failed PNG validation: " + Path.GetFileName(item.Path));
				}
			}
			if (!PortraitDerivativeCore.IsCurrentV2(build.Manifest, masterPath, outputDirectory))
			{
				throw new InvalidDataException("The complete committed portrait product failed derivative-manifest validation.");
			}
		}
		catch
		{
			for (int index = items.Count - 1; index >= 0; index--)
			{
				ProductCommitItem item = items[index];
				try
				{
					if (item.Committed)
					{
						if (File.Exists(item.Path)) File.Delete(item.Path);
						if (item.HadOriginal && File.Exists(item.BackupPath)) File.Move(item.BackupPath, item.Path);
					}
				}
				catch
				{
				}
			}
			throw;
		}
		finally
		{
			foreach (ProductCommitItem item in items)
			{
				try { if (File.Exists(item.TemporaryPath)) File.Delete(item.TemporaryPath); } catch { }
				try { if (File.Exists(item.BackupPath)) File.Delete(item.BackupPath); } catch { }
			}
		}
	}

	private static bool HashEquals(string expected, byte[] bytes)
	{
		if (string.IsNullOrWhiteSpace(expected) || bytes == null) return false;
		using (SHA256 sha = SHA256.Create())
		{
			string actual = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
			return string.Equals(expected.Trim(), actual, StringComparison.OrdinalIgnoreCase);
		}
	}

	private sealed class ProductCommitItem
	{
		public readonly string Path;
		public readonly string TemporaryPath;
		public readonly string BackupPath;
		public readonly byte[] Bytes;
		public readonly int Width;
		public readonly int Height;
		public readonly bool IsPng;
		public bool TemporaryWritten;
		public bool HadOriginal;
		public bool Committed;

		public ProductCommitItem(string path, byte[] bytes, int width, int height, bool isPng, string token)
		{
			Path = path;
			TemporaryPath = path + ".tmp." + token;
			BackupPath = path + ".bak." + token;
			Bytes = bytes ?? throw new InvalidDataException("A portrait product artifact was empty.");
			Width = width;
			Height = height;
			IsPng = isPng;
		}
	}

	[Obsolete("Use SaveAndPrepareAsync so derivatives are ready before completion.")]
	public static void SaveToDisk(string id, byte[] pngBytes)
	{
		SaveAndPrepareAsync(id, pngBytes).GetAwaiter().GetResult();
	}

	public static void SaveSource(string id, byte[] pngBytes)
	{
		if (string.IsNullOrEmpty(id) || pngBytes == null)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(DirFor(id));
			File.WriteAllBytes(SourcePath(id), pngBytes);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] SaveSource failed for " + id + ": " + ex.Message);
		}
	}

	public static void SavePrompt(string id, string prompt)
	{
		if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(prompt))
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(DirFor(id));
			File.WriteAllText(PromptPath(id), prompt, Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] SavePrompt failed for " + id + ": " + ex.Message);
		}
	}

	public static byte[] GetDiskBytes(string id)
	{
		EnsureDirectory();
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		if (!TryGetPreferredDiskPath(id, out var path))
		{
			return null;
		}
		try
		{
			return File.ReadAllBytes(path);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] GetDiskBytes failed for " + id + ": " + ex.Message);
			return null;
		}
	}

	public static bool TryGetPortraitAspectRatio(string id, out float aspectRatio)
	{
		aspectRatio = 0f;
		byte[] diskBytes = GetDiskBytes(id);
		if (!TryGetImageDimensions(diskBytes, out var width, out var height))
		{
			return false;
		}
		aspectRatio = (float)width / (float)height;
		return aspectRatio > 0f;
	}

	private static bool TryGetImageDimensions(byte[] bytes, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (bytes == null || bytes.Length < 10)
		{
			return false;
		}
		if (IsPng(bytes) && bytes.Length >= 24)
		{
			width = ReadBigEndianInt32(bytes, 16);
			height = ReadBigEndianInt32(bytes, 20);
			if (width > 0)
			{
				return height > 0;
			}
			return false;
		}
		if (IsJpeg(bytes))
		{
			return TryGetJpegDimensions(bytes, out width, out height);
		}
		if (IsWebP(bytes))
		{
			return TryGetWebPDimensions(bytes, out width, out height);
		}
		return false;
	}

	private static bool IsPng(byte[] bytes)
	{
		if (bytes.Length >= 8 && bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 && bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26)
		{
			return bytes[7] == 10;
		}
		return false;
	}

	private static bool IsJpeg(byte[] bytes)
	{
		if (bytes.Length >= 2 && bytes[0] == byte.MaxValue)
		{
			return bytes[1] == 216;
		}
		return false;
	}

	private static bool IsWebP(byte[] bytes)
	{
		if (bytes.Length >= 16 && bytes[0] == 82 && bytes[1] == 73 && bytes[2] == 70 && bytes[3] == 70 && bytes[8] == 87 && bytes[9] == 69 && bytes[10] == 66)
		{
			return bytes[11] == 80;
		}
		return false;
	}

	private static int ReadBigEndianInt32(byte[] bytes, int offset)
	{
		return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
	}

	private static int ReadBigEndianInt16(byte[] bytes, int offset)
	{
		return (bytes[offset] << 8) | bytes[offset + 1];
	}

	private static int ReadLittleEndianInt16(byte[] bytes, int offset)
	{
		return bytes[offset] | (bytes[offset + 1] << 8);
	}

	private static int ReadLittleEndianInt24(byte[] bytes, int offset)
	{
		return bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
	}

	private static bool TryGetJpegDimensions(byte[] bytes, out int width, out int height)
	{
		width = 0;
		height = 0;
		int i = 2;
		while (i < bytes.Length - 1)
		{
			if (bytes[i] != byte.MaxValue)
			{
				i++;
				continue;
			}
			for (; i < bytes.Length && bytes[i] == byte.MaxValue; i++)
			{
			}
			if (i >= bytes.Length)
			{
				return false;
			}
			byte b = bytes[i++];
			switch (b)
			{
			case 217:
			case 218:
				return false;
			case 1:
			case 208:
			case 209:
			case 210:
			case 211:
			case 212:
			case 213:
			case 214:
			case 215:
				continue;
			}
			if (i + 1 >= bytes.Length)
			{
				return false;
			}
			int num = ReadBigEndianInt16(bytes, i);
			if (num < 2 || i + num > bytes.Length)
			{
				return false;
			}
			if (IsJpegStartOfFrame(b) && num >= 7)
			{
				height = ReadBigEndianInt16(bytes, i + 3);
				width = ReadBigEndianInt16(bytes, i + 5);
				if (width > 0)
				{
					return height > 0;
				}
				return false;
			}
			i += num;
		}
		return false;
	}

	private static bool IsJpegStartOfFrame(byte marker)
	{
		if ((marker < 192 || marker > 195) && (marker < 197 || marker > 199) && (marker < 201 || marker > 203))
		{
			if (marker >= 205)
			{
				return marker <= 207;
			}
			return false;
		}
		return true;
	}

	private static bool TryGetWebPDimensions(byte[] bytes, out int width, out int height)
	{
		width = 0;
		height = 0;
		if (bytes.Length < 30)
		{
			return false;
		}
		switch (Encoding.ASCII.GetString(bytes, 12, 4))
		{
		case "VP8X":
			if (bytes.Length < 30)
			{
				return false;
			}
			width = ReadLittleEndianInt24(bytes, 24) + 1;
			height = ReadLittleEndianInt24(bytes, 27) + 1;
			if (width > 0)
			{
				return height > 0;
			}
			return false;
		case "VP8L":
		{
			if (bytes.Length < 25 || bytes[20] != 47)
			{
				return false;
			}
			int num = bytes[21];
			int num2 = bytes[22];
			int num3 = bytes[23];
			int num4 = bytes[24];
			width = 1 + (((num2 & 0x3F) << 8) | num);
			height = 1 + (((num4 & 0xF) << 10) | (num3 << 2) | ((num2 & 0xC0) >> 6));
			if (width > 0)
			{
				return height > 0;
			}
			return false;
		}
		case "VP8 ":
			if (bytes.Length < 30 || bytes[23] != 157 || bytes[24] != 1 || bytes[25] != 42)
			{
				return false;
			}
			width = ReadLittleEndianInt16(bytes, 26) & 0x3FFF;
			height = ReadLittleEndianInt16(bytes, 28) & 0x3FFF;
			if (width > 0)
			{
				return height > 0;
			}
			return false;
		default:
			return false;
		}
	}

	public static bool Delete(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return false;
		}
		_pending.TryRemove(RequestCampaignFolder + "|" + id, out var _);
		try
		{
			string path = DirFor(id);
			if (Directory.Exists(path))
			{
				Directory.Delete(path, recursive: true);
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Delete failed for " + id + ": " + ex.Message);
		}
		return false;
	}

	public static void ClearMemoryCache()
	{
		_characterPortraitPathCache.Clear();
		_seedPortraitPathByIdentity.Clear();
		PortraitDerivativeService.ClearReadyCache();
	}

	public static bool TryGetPreferredDiskPathForDiagnostics(string id, out string path)
	{
		return TryGetPreferredDiskPath(id, out path);
	}

	public static bool TryGetPreferredDiskPathForDerivatives(string id, out string path)
	{
		return TryGetPreferredDiskPath(id, out path);
	}

	public static string GetDerivativeOutputDirectory(string id, string sourcePath)
	{
		if (string.IsNullOrWhiteSpace(sourcePath))
		{
			return null;
		}

		string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
		string cacheRoot = Path.GetFullPath(BaseCacheRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!string.IsNullOrWhiteSpace(sourceDirectory)
			&& (sourceDirectory + Path.DirectorySeparatorChar).StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase))
		{
			return sourceDirectory;
		}

		string seedRoot = Path.GetFullPath(SeedCacheDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		if (!string.IsNullOrWhiteSpace(sourceDirectory)
			&& (sourceDirectory + Path.DirectorySeparatorChar).StartsWith(seedRoot, StringComparison.OrdinalIgnoreCase))
			return sourceDirectory;
		if (ReignCampaignIdentity.HasActiveCampaign()) return DirFor(id);
		return sourceDirectory;
	}

	public static bool PrepareSharedPortraitCache(out int foldersQueued, out string message)
	{
		foldersQueued = PortraitDerivativeService.QueueSharedValidation(force: false);
		message = foldersQueued > 0
			? "Queued derivative validation for " + foldersQueued + " shared portraits."
			: "Shared portrait source folder was not found or contains no portraits: " + SeedCacheDir;
		return foldersQueued > 0;
	}

	[Obsolete("Use PrepareSharedPortraitCache.")]
	public static bool CopySharedPortraitsToCurrentSave(out int foldersCopied, out int filesCopied, out string message)
	{
		filesCopied = 0;
		return PrepareSharedPortraitCache(out foldersCopied, out message);
	}

	public static void ClearGeneratedOnly()
	{
		_pending.Clear();
		if (Directory.Exists(CacheDir))
		{
			string[] directories = Directory.GetDirectories(CacheDir);
			foreach (string text in directories)
			{
				try
				{
					if (!File.Exists(Path.Combine(text, "custom.png")))
					{
						Directory.Delete(text, recursive: true);
					}
				}
				catch
				{
				}
			}
			string[] files = Directory.GetFiles(CacheDir, "*.png");
			foreach (string path in files)
			{
				try
				{
					File.Delete(path);
				}
				catch
				{
				}
			}
		}
		Debug.Print("[AIPortraits] Generated portrait cache cleared; folders with custom.png were preserved.");
	}

	public static void ClearAllIncludingCustom()
	{
		_pending.Clear();
		if (Directory.Exists(CacheDir))
		{
			string[] directories = Directory.GetDirectories(CacheDir);
			foreach (string path in directories)
			{
				try
				{
					Directory.Delete(path, recursive: true);
				}
				catch
				{
				}
			}
			string[] files = Directory.GetFiles(CacheDir, "*.png");
			foreach (string path2 in files)
			{
				try
				{
					File.Delete(path2);
				}
				catch
				{
				}
			}
		}
		Debug.Print("[AIPortraits] All portrait cache folders cleared, including custom.png overrides.");
	}

	public static bool IsPending(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			return _pending.ContainsKey(RequestCampaignFolder + "|" + id);
		}
		return false;
	}

	public static bool TryMarkPending(string id) => !string.IsNullOrWhiteSpace(id)
		&& _pending.TryAdd(RequestCampaignFolder + "|" + id, 0);

	public static void MarkPending(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			_pending.TryAdd(RequestCampaignFolder + "|" + id, 0);
		}
	}

	public static void MarkComplete(string id)
	{
		if (!string.IsNullOrEmpty(id))
		{
			_pending.TryRemove(RequestCampaignFolder + "|" + id, out var _);
		}
	}

	private static void MigrateLegacyCacheIfNeeded()
	{
		string migrationKey = CacheDir;
		if (!_migrationChecked.TryAdd(migrationKey, 0))
		{
			return;
		}

		try
		{
			bool migrated = false;
			foreach (string legacyCacheDir in LegacyCacheDirs)
			{
				if (!Directory.Exists(legacyCacheDir))
				{
					continue;
				}

				CopyDirectory(legacyCacheDir, CacheDir);
				migrated = true;
			}

			if (migrated)
			{
				Debug.Print("[AIPortraits] Migrated portrait cache path to Bannerlord Reign campaign folder.");
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Portrait cache migration skipped: " + ex.Message);
		}
	}

	private static void CopySeedCacheIfNeeded()
	{
		string seedKey = CacheDir;
		if (!_seedChecked.TryAdd(seedKey, 0))
		{
			return;
		}

		PortraitSeedResult result = EnsureSeedCacheForCampaign();
		if (!result.Ok && !string.IsNullOrWhiteSpace(result.Message))
		{
			Debug.Print("[AIPortraits] " + result.Message);
		}
	}

	private static void NormalizePromptFilesIfNeeded()
	{
		string normalizationKey = CacheDir;
		if (!_normalizationChecked.TryAdd(normalizationKey, 0))
		{
			return;
		}

		try
		{
			if (!Directory.Exists(CacheDir))
			{
				return;
			}

			int normalized = 0;
			foreach (string customPromptFile in Directory.GetFiles(CacheDir, CustomPromptFileName, SearchOption.AllDirectories))
			{
				string directoryName = Path.GetDirectoryName(customPromptFile);
				if (string.IsNullOrEmpty(directoryName))
				{
					continue;
				}

				string promptFile = Path.Combine(directoryName, PromptFileName);
				if (File.Exists(promptFile))
				{
					continue;
				}

				File.Copy(customPromptFile, promptFile);
				normalized++;
			}

			if (normalized > 0)
			{
				Debug.Print("[AIPortraits] Normalized " + normalized + " custom_prompt.txt files into prompt.txt.");
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Portrait prompt normalization skipped: " + ex.Message);
		}
	}

	private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
	{
		Directory.CreateDirectory(destinationDirectory);
		foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
		{
			string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
			if (!File.Exists(destinationFile))
			{
				File.Copy(sourceFile, destinationFile);
			}
		}

		foreach (string sourceChild in Directory.GetDirectories(sourceDirectory))
		{
			string destinationChild = Path.Combine(destinationDirectory, Path.GetFileName(sourceChild));
			CopyDirectory(sourceChild, destinationChild);
		}
	}

	private static void CopyDirectoryCounted(string sourceDirectory, string destinationDirectory, ref int foldersCopied, ref int filesCopied)
	{
		if (!Directory.Exists(destinationDirectory))
		{
			Directory.CreateDirectory(destinationDirectory);
			foldersCopied++;
		}

		foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
		{
			string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
			if (!File.Exists(destinationFile))
			{
				File.Copy(sourceFile, destinationFile);
				filesCopied++;
			}
		}

		foreach (string sourceChild in Directory.GetDirectories(sourceDirectory))
		{
			string destinationChild = Path.Combine(destinationDirectory, Path.GetFileName(sourceChild));
			CopyDirectoryCounted(sourceChild, destinationChild, ref foldersCopied, ref filesCopied);
		}
	}

	private static void WriteSeedMarker(PortraitSeedResult result)
	{
		try
		{
			Directory.CreateDirectory(CacheDir);
			JObject marker = new JObject
			{
				["campaignId"] = RequestCampaignFolder,
				["completedUtc"] = DateTime.UtcNow.ToString("O"),
				["foldersCopied"] = result?.FoldersCopied ?? 0,
				["filesCopied"] = result?.FilesCopied ?? 0,
				["indexed"] = result?.Indexed ?? 0
			};
			File.WriteAllText(SeedMarkerPath, marker.ToString(), Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Failed to write portrait seed marker: " + ex.Message);
		}
	}

	private static string MemoryKey(string id)
	{
		return RequestCampaignFolder + "|" + (id ?? string.Empty);
	}

	private static void WriteAtomic(string path, byte[] bytes)
	{
		string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
		File.WriteAllBytes(temporary, bytes);
		try
		{
			if (File.Exists(path))
			{
				File.Replace(temporary, path, null);
			}
			else
			{
				File.Move(temporary, path);
			}
		}
		finally
		{
			if (File.Exists(temporary))
			{
				File.Delete(temporary);
			}
		}
	}

	private static bool TryGetPreferredDiskPath(string id, out string path)
	{
		path = null;
		if (string.IsNullOrEmpty(id))
		{
			return false;
		}

		string customPath = CustomPath(id);
		if (File.Exists(customPath))
		{
			path = customPath;
			return true;
		}

		string diskPath = DiskPath(id);
		if (File.Exists(diskPath))
		{
			path = diskPath;
			return true;
		}

		// The player has the stable native id main_hero in every campaign. A
		// shared seed keyed by that id therefore belongs to a different person
		// as soon as a new campaign is created. Player portraits may still come
		// from this campaign's cache or character record, but never from _shared.
		if (!IsCampaignSpecificPlayerIdentity(id)
			&& TryGetSeedPortraitPath(id, requireCustom: false, out path))
		{
			return true;
		}

		return TryGetCharacterPortraitPath(id, requireCustom: false, out path);
	}

	private static bool IsCampaignSpecificPlayerIdentity(string id)
	{
		string identity = ExtractParenthesizedId(id);
		return string.Equals(identity, "main_hero", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(identity, "player_hero", StringComparison.OrdinalIgnoreCase);
	}

	private static bool TryGetCharacterPortraitPath(string id, bool requireCustom, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(id) || !HasAnyServerCharactersDir())
		{
			return false;
		}

		string cacheKey = MemoryKey(id) + (requireCustom ? "|custom" : "|any");
		if (_characterPortraitPathCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
		{
			if (!requireCustom || string.Equals(Path.GetFileName(cachedPath), CustomFileName, StringComparison.OrdinalIgnoreCase))
			{
				path = cachedPath;
				return true;
			}
		}

		if (TryGetCharacterPortraitPathDirect(id, requireCustom, out path))
		{
			_characterPortraitPathCache[cacheKey] = path;
			return true;
		}

		return false;
	}

	private static bool TryGetSeedPortraitPath(string id, bool requireCustom, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(id) || !Directory.Exists(SeedCacheDir))
		{
			return false;
		}

		string extractedId = ExtractParenthesizedId(id);
		string[] candidates = new[]
		{
			id,
			CharacterCacheId.Sanitize(id),
			extractedId,
			CharacterCacheId.Sanitize(extractedId)
		};

		foreach (string candidate in candidates)
		{
			if (string.IsNullOrWhiteSpace(candidate))
			{
				continue;
			}

			string folder = Path.Combine(SeedCacheDir, ReignCampaignIdentity.SafePathSegment(candidate, "unknown"));
			if (TryGetPortraitInSeedFolder(folder, requireCustom, out path))
			{
				return true;
			}
		}

		return TryGetSeedPortraitPathByIdentity(extractedId, requireCustom, out path);
	}

	private static bool TryGetSeedPortraitPathByIdentity(string heroId, bool requireCustom, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(heroId) || !Directory.Exists(SeedCacheDir))
		{
			return false;
		}

		string lookupKey = heroId.ToLowerInvariant() + (requireCustom ? "|custom" : "|any");
		if (_seedPortraitPathByIdentity.TryGetValue(lookupKey, out string cachedPath) && File.Exists(cachedPath))
		{
			path = cachedPath;
			return true;
		}

		try
		{
			string[] folders = Directory.GetDirectories(SeedCacheDir);
			Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
			string[] identityFolders = Array.FindAll(folders, folder =>
				StringEquals(ExtractParenthesizedId(Path.GetFileName(folder)), heroId));

			// A custom portrait remains authoritative even if more than one historical
			// display-name folder exists for the same stable Bannerlord hero id.
			foreach (string folder in identityFolders)
			{
				if (TryGetPortraitInSeedFolder(folder, requireCustom: true, out path))
				{
					_seedPortraitPathByIdentity[lookupKey] = path;
					return true;
				}
			}

			if (!requireCustom)
			{
				foreach (string folder in identityFolders)
				{
					if (TryGetPortraitInSeedFolder(folder, requireCustom: false, out path))
					{
						_seedPortraitPathByIdentity[lookupKey] = path;
						return true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Stable-id shared portrait lookup failed for " + heroId + ": " + ex.Message);
		}

		path = null;
		return false;
	}

	private static bool TryGetPortraitInSeedFolder(string folder, bool requireCustom, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
		{
			return false;
		}

		string customPath = Path.Combine(folder, CustomFileName);
		if (File.Exists(customPath))
		{
			path = customPath;
			return true;
		}

		if (requireCustom)
		{
			return false;
		}

		string generatedPath = Path.Combine(folder, PortraitFileName);
		if (File.Exists(generatedPath))
		{
			path = generatedPath;
			return true;
		}

		return false;
	}

	private static bool TryGetCharacterPortraitPathDirect(string id, bool requireCustom, out string path)
	{
		path = null;
		string extractedId = ExtractParenthesizedId(id);
		string[] candidates = new[]
		{
			id,
			CharacterCacheId.Sanitize(id),
			extractedId,
			CharacterCacheId.Sanitize(extractedId)
		};

		foreach (string serverCharactersDir in ServerCharactersDirs)
		{
			if (!Directory.Exists(serverCharactersDir))
			{
				continue;
			}

			foreach (string candidate in candidates)
			{
				if (string.IsNullOrWhiteSpace(candidate))
				{
					continue;
				}

				string folder = Path.Combine(serverCharactersDir, ReignCampaignIdentity.SafePathSegment(candidate, "unknown"));
				if (TryGetPortraitInCharacterFolder(folder, requireCustom, out path))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static bool TryGetCharacterPortraitPathByProfileScan(string id, bool requireCustom, out string path)
	{
		path = null;
		string extractedId = ExtractParenthesizedId(id);
		if (string.IsNullOrWhiteSpace(extractedId))
		{
			return false;
		}

		try
		{
			foreach (string serverCharactersDir in ServerCharactersDirs)
			{
				if (!Directory.Exists(serverCharactersDir))
				{
					continue;
				}

				foreach (string characterDir in Directory.GetDirectories(serverCharactersDir))
				{
					string profilePath = Path.Combine(characterDir, "profile.json");
					if (!File.Exists(profilePath))
					{
						continue;
					}

					JObject profile = JObject.Parse(File.ReadAllText(profilePath, Encoding.UTF8));
					if (!ProfileMatches(profile, extractedId))
					{
						continue;
					}

					if (TryGetPortraitInCharacterFolder(characterDir, requireCustom, out path))
					{
						return true;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Character portrait profile scan failed for " + id + ": " + ex.Message);
		}

		return false;
	}

	private static bool TryGetPortraitInCharacterFolder(string characterDir, bool requireCustom, out string path)
	{
		path = null;
		if (string.IsNullOrWhiteSpace(characterDir) || !Directory.Exists(characterDir))
		{
			return false;
		}

		string portraitDir = Path.Combine(characterDir, "portraits");
		if (!Directory.Exists(portraitDir))
		{
			return false;
		}

		string customPath = Path.Combine(portraitDir, CustomFileName);
		if (File.Exists(customPath))
		{
			path = customPath;
			return true;
		}

		if (requireCustom)
		{
			return false;
		}

		string generatedPath = Path.Combine(portraitDir, PortraitFileName);
		if (File.Exists(generatedPath))
		{
			path = generatedPath;
			return true;
		}

		return false;
	}

	private static bool ProfileMatches(JObject profile, string extractedId)
	{
		if (profile == null || string.IsNullOrWhiteSpace(extractedId))
		{
			return false;
		}

		return StringEquals(profile.Value<string>("heroStringId"), extractedId)
			|| StringEquals(profile.Value<string>("characterObjectId"), extractedId)
			|| StringEquals(profile.Value<string>("stringId"), extractedId)
			|| StringEquals(profile.Value<string>("id"), extractedId);
	}

	private static string ExtractParenthesizedId(string cacheKey)
	{
		if (string.IsNullOrWhiteSpace(cacheKey))
		{
			return string.Empty;
		}

		int close = cacheKey.LastIndexOf(')');
		if (close <= 0)
		{
			return cacheKey.Trim();
		}

		int open = cacheKey.LastIndexOf('(', close);
		if (open < 0 || close <= open + 1)
		{
			return cacheKey.Trim();
		}

		return cacheKey.Substring(open + 1, close - open - 1).Trim();
	}

	private static bool StringEquals(string left, string right)
	{
		return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
	}

	private static bool HasAnyServerCharactersDir()
	{
		foreach (string serverCharactersDir in ServerCharactersDirs)
		{
			if (Directory.Exists(serverCharactersDir))
			{
				return true;
			}
		}

		return false;
	}
}
