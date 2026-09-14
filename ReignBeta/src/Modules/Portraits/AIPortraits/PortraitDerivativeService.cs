using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReignPortraits;
using TaleWorlds.Library;

namespace AIPortraits;

public enum PortraitQualityTier
{
	Thumbnail,
	PartyThumbnail,
	Portrait,
	Zoom
}

public static class PortraitDerivativeService
{
	private sealed class LegacyDerivativeManifest
	{
		public int Version;
		public string SourceFileName;
		public string SourcePath;
		public long SourceLength;
		public long SourceLastWriteUtcTicks;
		public int ThumbnailWidth;
		public int ThumbnailHeight;
		public int ZoomWidth;
		public int ZoomHeight;
	}

	private sealed class ReadySet
	{
		public bool IsLegacy;
		public string SourcePath;
		public long SourceLength;
		public long SourceLastWriteUtcTicks;
		public string ThumbnailPath;
		public string PartyThumbnailPath;
		public string PortraitPath;
		public string ZoomPath;
		public long ThumbnailLength;
		public long ThumbnailLastWriteUtcTicks;
		public long PartyThumbnailLength;
		public long PartyThumbnailLastWriteUtcTicks;
		public long PortraitLength;
		public long PortraitLastWriteUtcTicks;
		public long ZoomLength;
		public long ZoomLastWriteUtcTicks;
	}

	private sealed class WorkItem
	{
		public string Key;
		public string Id;
		public string SourcePath;
		public string OutputDirectory;
		public bool Force;
		public int Started;
		public readonly TaskCompletionSource<bool> Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
	}

	public const string ThumbnailFileName = PortraitDerivativeCore.ThumbnailFileName;
	public const string PartyThumbnailFileName = PortraitDerivativeCore.PartyThumbnailFileName;
	public const string PortraitFileName = PortraitDerivativeCore.PortraitFileName;
	public const string ZoomFileName = PortraitDerivativeCore.ZoomFileName;
	public const string ManifestFileName = PortraitDerivativeCore.ManifestFileName;
	public const int CurrentVersion = PortraitDerivativeCore.CurrentVersion;

	private const int LegacyVersion = 1;
	private const int LegacyThumbnailMaxWidth = 256;
	private const int LegacyThumbnailMaxHeight = 384;
	private const int LegacyZoomMaxWidth = 768;
	private const int LegacyZoomMaxHeight = 1152;

	private static readonly ConcurrentDictionary<string, WorkItem> _jobs = new ConcurrentDictionary<string, WorkItem>(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentDictionary<string, ReadySet> _ready = new ConcurrentDictionary<string, ReadySet>(StringComparer.OrdinalIgnoreCase);
	private static readonly ConcurrentQueue<WorkItem> _highPriority = new ConcurrentQueue<WorkItem>();
	private static readonly ConcurrentQueue<WorkItem> _background = new ConcurrentQueue<WorkItem>();
	private static readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
	private static int _workerStarted;
	private static int _backgroundScanStarted;

	public static int PendingCount => _jobs.Count;

	public static bool TryGetDerivativePath(string id, PortraitQualityTier tier, out string path)
	{
		path = null;
		if (!TryResolvePaths(id, out string sourcePath, out string outputDirectory))
		{
			return false;
		}

		if (TryGetReadySet(sourcePath, outputDirectory, out ReadySet ready))
		{
			path = tier == PortraitQualityTier.Zoom
				? ready.ZoomPath
				: tier == PortraitQualityTier.Portrait
					? ready.PortraitPath
					: tier == PortraitQualityTier.PartyThumbnail ? ready.PartyThumbnailPath : ready.ThumbnailPath;
			return File.Exists(path);
		}

		_ = QueueSource(id, sourcePath, outputDirectory, highPriority: true, force: false);
		return false;
	}

	public static bool TryGetDerivativeAspectRatio(string id, PortraitQualityTier tier, out float aspectRatio)
	{
		aspectRatio = 0f;
		if (!TryGetDerivativePath(id, tier, out string path)
			|| !PortraitDerivativeCore.TryReadPngDimensions(path, out int width, out int height)
			|| width <= 0 || height <= 0)
		{
			return false;
		}

		aspectRatio = (float)width / height;
		return aspectRatio > 0f;
	}

	public static Task<bool> EnsureForIdAsync(string id, bool highPriority = true, bool force = false)
	{
		if (!TryResolvePaths(id, out string sourcePath, out string outputDirectory))
		{
			return Task.FromResult(false);
		}

		return QueueSource(id, sourcePath, outputDirectory, highPriority, force);
	}

	public static Task<bool> EnsureForSourceAsync(string id, string sourcePath, bool highPriority = true, bool force = false)
	{
		if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
		{
			return Task.FromResult(false);
		}

		return QueueSource(id, sourcePath, PortraitCache.GetDerivativeOutputDirectory(id, sourcePath), highPriority, force);
	}

	public static int QueueSharedValidation(bool force = false)
	{
		string root = PortraitCache.SeedCacheDir;
		if (!Directory.Exists(root))
		{
			return 0;
		}

		int queued = 0;
		foreach (string directory in Directory.GetDirectories(root))
		{
			if (!TryFindSourceInDirectory(directory, out string sourcePath))
			{
				continue;
			}

			string id = Path.GetFileName(directory);
			_ = QueueSource(id, sourcePath, directory, highPriority: false, force);
			queued++;
		}

		return queued;
	}

	public static void StartBackgroundSharedValidation()
	{
		if (Interlocked.Exchange(ref _backgroundScanStarted, 1) != 0)
		{
			return;
		}

		Task.Run(delegate
		{
			try
			{
				int count = QueueSharedValidation();
				Debug.Print("[AIPortraits] Shared portrait derivative validation queued " + count + " folders.");
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] Shared derivative scan failed: " + ex.Message);
			}
		});
	}

	public static void Invalidate(string id)
	{
		_ready.Clear();
	}

	public static void ClearReadyCache()
	{
		_ready.Clear();
	}

	private static Task<bool> QueueSource(string id, string sourcePath, string outputDirectory, bool highPriority, bool force)
	{
		if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(outputDirectory) || !File.Exists(sourcePath))
		{
			return Task.FromResult(false);
		}

		string key = BuildKey(sourcePath, outputDirectory);
		if (!force && TryGetReadySet(sourcePath, outputDirectory, out var _))
		{
			return Task.FromResult(true);
		}

		WorkItem candidate = new WorkItem
		{
			Key = key,
			Id = id ?? string.Empty,
			SourcePath = sourcePath,
			OutputDirectory = outputDirectory,
			Force = force
		};
		WorkItem item = _jobs.GetOrAdd(key, candidate);
		if (force)
		{
			item.Force = true;
		}

		if (ReferenceEquals(item, candidate) || highPriority)
		{
			if (highPriority)
			{
				_highPriority.Enqueue(item);
			}
			else
			{
				_background.Enqueue(item);
			}
			_signal.Release();
		}

		StartWorker();
		return item.Completion.Task;
	}

	private static void StartWorker()
	{
		if (Interlocked.Exchange(ref _workerStarted, 1) != 0)
		{
			return;
		}

		Task.Run(WorkerLoopAsync);
	}

	private static async Task WorkerLoopAsync()
	{
		while (true)
		{
			await _signal.WaitAsync().ConfigureAwait(false);
			if (!_highPriority.TryDequeue(out WorkItem item) && !_background.TryDequeue(out item))
			{
				continue;
			}
			if (Interlocked.Exchange(ref item.Started, 1) != 0)
			{
				continue;
			}

			try
			{
				item.Completion.TrySetResult(Prepare(item));
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] Derivative preparation failed for " + item.Id + ": " + ex.Message);
				item.Completion.TrySetResult(false);
			}
			finally
			{
				_jobs.TryRemove(item.Key, out var _);
			}
		}
	}

	private static bool Prepare(WorkItem item)
	{
		if (!item.Force && TryGetReadySet(item.SourcePath, item.OutputDirectory, out var _))
		{
			return true;
		}

		return string.Equals(Path.GetFileName(item.SourcePath), PortraitCache.PortraitFileName, StringComparison.OrdinalIgnoreCase)
			? PrepareV2(item)
			: PrepareLegacy(item);
	}

	private static bool PrepareV2(WorkItem item)
	{
		FileInfo sourceInfo = new FileInfo(item.SourcePath);
		byte[] sourceBytes = File.ReadAllBytes(item.SourcePath);
		if (IsJpeg(sourceBytes))
		{
			sourceBytes = JpegToPng.Convert(sourceBytes);
		}
		byte[] rgba = PngReencode.DecodeToRgba(sourceBytes, out int sourceWidth, out int sourceHeight);
		if (rgba == null || sourceWidth <= 0 || sourceHeight <= 0)
		{
			return false;
		}

		if (!TryReadPersistedFaceFocus(item.OutputDirectory, out PortraitFaceFocus focus))
		{
			Debug.Print("[AIPortraits] Refusing legacy fallback crop for full-body portrait " + item.Id
				+ "; a server-validated face-focus receipt or prior focused manifest is required.");
			return false;
		}

		PortraitDerivativeBuild build = PortraitDerivativeCore.BuildV2(
			rgba,
			sourceWidth,
			sourceHeight,
			sourceInfo,
			PngEncoder.EncodeRgba,
			focus);
		byte[] manifestBytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(build.Manifest, Formatting.Indented));
		if (!PortraitDerivativeCore.CommitV2(item.SourcePath, item.OutputDirectory, build, manifestBytes, out string error))
		{
			Debug.Print("[AIPortraits] V2 derivative commit failed for " + item.Id + ": " + error);
			return false;
		}

		_ready[item.Key] = ToReadySet(build.Manifest, item.SourcePath, item.OutputDirectory);
		return true;
	}

	private static bool TryReadPersistedFaceFocus(string outputDirectory, out PortraitFaceFocus focus)
	{
		focus = null;
		try
		{
			string receiptPath = Path.Combine(outputDirectory, PortraitCache.GenerationReceiptFileName);
			if (File.Exists(receiptPath))
			{
				JObject root = JObject.Parse(File.ReadAllText(receiptPath, Encoding.UTF8));
				JObject receipt = string.Equals(root.Value<string>("schema"), "reign-portrait-product-v1", StringComparison.Ordinal)
					? root
					: root["productReceipt"] as JObject;
				if (TryReadFaceFocus(receipt?["focus"] as JObject, out focus))
				{
					return true;
				}
			}

			string manifestPath = Path.Combine(outputDirectory, ManifestFileName);
			if (!File.Exists(manifestPath)) return false;
			PortraitDerivativeManifestV2 manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(
				File.ReadAllText(manifestPath, Encoding.UTF8));
			PortraitFaceFocus prior = manifest == null ? null : new PortraitFaceFocus
			{
				Method = manifest.FocusMethod,
				Model = manifest.FocusModel,
				Confidence = manifest.FocusConfidence,
				CandidateCount = manifest.FaceCandidateCount,
				X = manifest.FaceX,
				Y = manifest.FaceY,
				Width = manifest.FaceWidth,
				Height = manifest.FaceHeight
			};
			if (PortraitDerivativeCore.IsValidFocus(prior)
				&& !string.Equals(prior.Method, "legacy_fallback", StringComparison.OrdinalIgnoreCase))
			{
				focus = prior;
				return true;
			}
		}
		catch
		{
		}
		focus = null;
		return false;
	}

	private static bool TryReadFaceFocus(JObject value, out PortraitFaceFocus focus)
	{
		focus = null;
		if (value == null) return false;
		PortraitFaceFocus candidate = new PortraitFaceFocus
		{
			Method = value.Value<string>("method") ?? string.Empty,
			Model = value.Value<string>("model") ?? string.Empty,
			Confidence = value.Value<double?>("confidence") ?? 0d,
			CandidateCount = value.Value<int?>("candidateCount") ?? 0,
			X = value.Value<double?>("x") ?? 0d,
			Y = value.Value<double?>("y") ?? 0d,
			Width = value.Value<double?>("width") ?? 0d,
			Height = value.Value<double?>("height") ?? 0d
		};
		if (!PortraitDerivativeCore.IsValidFocus(candidate)
			|| string.Equals(candidate.Method, "legacy_fallback", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		focus = candidate;
		return true;
	}

	private static bool PrepareLegacy(WorkItem item)
	{
		byte[] sourceBytes = File.ReadAllBytes(item.SourcePath);
		if (IsJpeg(sourceBytes))
		{
			sourceBytes = JpegToPng.Convert(sourceBytes);
		}
		byte[] rgba = PngReencode.DecodeToRgba(sourceBytes, out int sourceWidth, out int sourceHeight);
		if (rgba == null || sourceWidth <= 0 || sourceHeight <= 0)
		{
			return false;
		}

		Directory.CreateDirectory(item.OutputDirectory);
		PortraitDerivativeCore.CalculateContainedDimensions(sourceWidth, sourceHeight, LegacyThumbnailMaxWidth, LegacyThumbnailMaxHeight, out int thumbnailWidth, out int thumbnailHeight);
		PortraitDerivativeCore.CalculateContainedDimensions(sourceWidth, sourceHeight, LegacyZoomMaxWidth, LegacyZoomMaxHeight, out int zoomWidth, out int zoomHeight);
		byte[] thumbnailPng = PngEncoder.EncodeRgba(PortraitDerivativeCore.ResizeBilinear(rgba, sourceWidth, sourceHeight, thumbnailWidth, thumbnailHeight), thumbnailWidth, thumbnailHeight);
		byte[] zoomPng = PngEncoder.EncodeRgba(PortraitDerivativeCore.ResizeBilinear(rgba, sourceWidth, sourceHeight, zoomWidth, zoomHeight), zoomWidth, zoomHeight);
		WriteAtomic(Path.Combine(item.OutputDirectory, ThumbnailFileName), thumbnailPng);
		WriteAtomic(Path.Combine(item.OutputDirectory, ZoomFileName), zoomPng);

		FileInfo sourceInfo = new FileInfo(item.SourcePath);
		LegacyDerivativeManifest manifest = new LegacyDerivativeManifest
		{
			Version = LegacyVersion,
			SourceFileName = sourceInfo.Name,
			SourcePath = sourceInfo.FullName,
			SourceLength = sourceInfo.Length,
			SourceLastWriteUtcTicks = sourceInfo.LastWriteTimeUtc.Ticks,
			ThumbnailWidth = thumbnailWidth,
			ThumbnailHeight = thumbnailHeight,
			ZoomWidth = zoomWidth,
			ZoomHeight = zoomHeight
		};
		WriteAtomic(Path.Combine(item.OutputDirectory, ManifestFileName), Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest, Formatting.Indented)));
		_ready[item.Key] = ToLegacyReadySet(manifest, item.SourcePath, item.OutputDirectory);
		return true;
	}

	private static bool TryGetReadySet(string sourcePath, string outputDirectory, out ReadySet ready)
	{
		ready = null;
		string key = BuildKey(sourcePath, outputDirectory);
		FileInfo sourceInfo;
		try
		{
			sourceInfo = new FileInfo(sourcePath);
			if (!sourceInfo.Exists)
			{
				return false;
			}
		}
		catch
		{
			return false;
		}

		if (_ready.TryGetValue(key, out ReadySet cached)
			&& cached.SourceLength == sourceInfo.Length
			&& cached.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks
			&& FileMatches(cached.ThumbnailPath, cached.ThumbnailLength, cached.ThumbnailLastWriteUtcTicks)
			&& FileMatches(cached.PartyThumbnailPath, cached.PartyThumbnailLength, cached.PartyThumbnailLastWriteUtcTicks)
			&& FileMatches(cached.PortraitPath, cached.PortraitLength, cached.PortraitLastWriteUtcTicks)
			&& FileMatches(cached.ZoomPath, cached.ZoomLength, cached.ZoomLastWriteUtcTicks))
		{
			ready = cached;
			return true;
		}

		string manifestPath = Path.Combine(outputDirectory, ManifestFileName);
		if (!File.Exists(manifestPath))
		{
			return false;
		}

		try
		{
			string json = File.ReadAllText(manifestPath);
			PortraitDerivativeManifestV2 manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(json);
			if (PortraitDerivativeCore.IsCurrentV2(manifest, sourcePath, outputDirectory))
			{
				ready = ToReadySet(manifest, sourcePath, outputDirectory);
				_ready[key] = ready;
				return true;
			}

			if (!string.Equals(sourceInfo.Name, PortraitCache.PortraitFileName, StringComparison.OrdinalIgnoreCase))
			{
				LegacyDerivativeManifest legacy = JsonConvert.DeserializeObject<LegacyDerivativeManifest>(json);
				if (IsCurrentLegacy(legacy, sourceInfo, outputDirectory))
				{
					ready = ToLegacyReadySet(legacy, sourcePath, outputDirectory);
					_ready[key] = ready;
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static bool TryReadCurrentV2(string sourcePath, string outputDirectory, out PortraitDerivativeManifestV2 manifest)
	{
		manifest = null;
		try
		{
			string path = Path.Combine(outputDirectory, ManifestFileName);
			if (!File.Exists(path))
			{
				return false;
			}
			manifest = JsonConvert.DeserializeObject<PortraitDerivativeManifestV2>(File.ReadAllText(path));
			return PortraitDerivativeCore.IsCurrentV2(manifest, sourcePath, outputDirectory);
		}
		catch
		{
			manifest = null;
			return false;
		}
	}

	private static bool IsCurrentLegacy(LegacyDerivativeManifest manifest, FileInfo sourceInfo, string outputDirectory)
	{
		return manifest != null
			&& manifest.Version == LegacyVersion
			&& string.Equals(manifest.SourceFileName, sourceInfo.Name, StringComparison.OrdinalIgnoreCase)
			&& manifest.SourceLength == sourceInfo.Length
			&& manifest.SourceLastWriteUtcTicks == sourceInfo.LastWriteTimeUtc.Ticks
			&& PortraitDerivativeCore.HasPngDimensions(Path.Combine(outputDirectory, ThumbnailFileName), manifest.ThumbnailWidth, manifest.ThumbnailHeight)
			&& PortraitDerivativeCore.HasPngDimensions(Path.Combine(outputDirectory, ZoomFileName), manifest.ZoomWidth, manifest.ZoomHeight);
	}

	private static ReadySet ToReadySet(PortraitDerivativeManifestV2 manifest, string sourcePath, string outputDirectory)
	{
		return CreateReadySet(false, manifest.SourceLength, manifest.SourceLastWriteUtcTicks, sourcePath,
			Path.Combine(outputDirectory, ThumbnailFileName),
			Path.Combine(outputDirectory, PartyThumbnailFileName),
			Path.Combine(outputDirectory, PortraitFileName),
			Path.Combine(outputDirectory, ZoomFileName));
	}

	private static ReadySet ToLegacyReadySet(LegacyDerivativeManifest manifest, string sourcePath, string outputDirectory)
	{
		string zoom = Path.Combine(outputDirectory, ZoomFileName);
		return CreateReadySet(true, manifest.SourceLength, manifest.SourceLastWriteUtcTicks, sourcePath,
			Path.Combine(outputDirectory, ThumbnailFileName), Path.Combine(outputDirectory, ThumbnailFileName), zoom, zoom);
	}

	private static ReadySet CreateReadySet(bool legacy, long sourceLength, long sourceTicks, string sourcePath, string thumbnailPath, string partyThumbnailPath, string portraitPath, string zoomPath)
	{
		FileInfo thumbnail = new FileInfo(thumbnailPath);
		FileInfo partyThumbnail = new FileInfo(partyThumbnailPath);
		FileInfo portrait = new FileInfo(portraitPath);
		FileInfo zoom = new FileInfo(zoomPath);
		return new ReadySet
		{
			IsLegacy = legacy,
			SourcePath = sourcePath,
			SourceLength = sourceLength,
			SourceLastWriteUtcTicks = sourceTicks,
			ThumbnailPath = thumbnailPath,
			PartyThumbnailPath = partyThumbnailPath,
			PortraitPath = portraitPath,
			ZoomPath = zoomPath,
			ThumbnailLength = thumbnail.Length,
			ThumbnailLastWriteUtcTicks = thumbnail.LastWriteTimeUtc.Ticks,
			PartyThumbnailLength = partyThumbnail.Length,
			PartyThumbnailLastWriteUtcTicks = partyThumbnail.LastWriteTimeUtc.Ticks,
			PortraitLength = portrait.Length,
			PortraitLastWriteUtcTicks = portrait.LastWriteTimeUtc.Ticks,
			ZoomLength = zoom.Length,
			ZoomLastWriteUtcTicks = zoom.LastWriteTimeUtc.Ticks
		};
	}

	private static bool FileMatches(string path, long length, long lastWriteUtcTicks)
	{
		try
		{
			FileInfo info = new FileInfo(path);
			return info.Exists && info.Length == length && info.LastWriteTimeUtc.Ticks == lastWriteUtcTicks;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryResolvePaths(string id, out string sourcePath, out string outputDirectory)
	{
		sourcePath = null;
		outputDirectory = null;
		if (!PortraitCache.TryGetPreferredDiskPathForDerivatives(id, out sourcePath))
		{
			return false;
		}

		outputDirectory = PortraitCache.GetDerivativeOutputDirectory(id, sourcePath);
		if (string.IsNullOrWhiteSpace(outputDirectory))
		{
			return false;
		}

		string portrait = Path.Combine(outputDirectory, PortraitCache.PortraitFileName);
		if (File.Exists(portrait) && TryReadCurrentV2(portrait, outputDirectory, out var _))
		{
			sourcePath = portrait;
		}
		return true;
	}

	private static bool TryFindSourceInDirectory(string directory, out string sourcePath)
	{
		string portrait = Path.Combine(directory, PortraitCache.PortraitFileName);
		if (File.Exists(portrait) && TryReadCurrentV2(portrait, directory, out var _))
		{
			sourcePath = portrait;
			return true;
		}
		string custom = Path.Combine(directory, PortraitCache.CustomFileName);
		if (File.Exists(custom))
		{
			sourcePath = custom;
			return true;
		}
		if (File.Exists(portrait))
		{
			sourcePath = portrait;
			return true;
		}
		sourcePath = null;
		return false;
	}

	private static string BuildKey(string sourcePath, string outputDirectory)
	{
		return Path.GetFullPath(sourcePath) + "|" + Path.GetFullPath(outputDirectory);
	}

	private static void WriteAtomic(string path, byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0)
		{
			throw new InvalidDataException("Cannot write an empty portrait derivative.");
		}
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

	private static bool IsJpeg(byte[] bytes)
	{
		return bytes != null && bytes.Length >= 2 && bytes[0] == byte.MaxValue && bytes[1] == 216;
	}
}
