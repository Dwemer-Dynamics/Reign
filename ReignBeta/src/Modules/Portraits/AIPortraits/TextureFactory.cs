using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ReignBeta.Integration;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIPortraits;

public static class TextureFactory
{
	private sealed class TextureCacheEntry
	{
		public TaleWorlds.TwoDimension.Texture Texture;
		public int Bytes;
		public LinkedListNode<string> Node;
	}

	private sealed class TextureLruCache
	{
		private readonly object _lock = new object();
		private readonly long _budgetBytes;
		private readonly Dictionary<string, TextureCacheEntry> _entries = new Dictionary<string, TextureCacheEntry>(StringComparer.Ordinal);
		private readonly LinkedList<string> _recent = new LinkedList<string>();
		private long _usedBytes;

		public TextureLruCache(long budgetBytes)
		{
			_budgetBytes = budgetBytes;
		}

		public long UsedBytes
		{
			get { lock (_lock) return _usedBytes; }
		}

		public int Count
		{
			get { lock (_lock) return _entries.Count; }
		}

		public bool TryGet(string key, out TaleWorlds.TwoDimension.Texture texture)
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out TextureCacheEntry entry))
				{
					_recent.Remove(entry.Node);
					_recent.AddFirst(entry.Node);
					texture = entry.Texture;
					return true;
				}
			}
			texture = null;
			return false;
		}

		public void Add(string key, TaleWorlds.TwoDimension.Texture texture)
		{
			if (texture == null)
			{
				return;
			}
			List<TaleWorlds.TwoDimension.Texture> release = new List<TaleWorlds.TwoDimension.Texture>();
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out TextureCacheEntry old))
				{
					_usedBytes -= old.Bytes;
					_recent.Remove(old.Node);
					release.Add(old.Texture);
				}
				int bytes = EstimateBytes(texture);
				LinkedListNode<string> node = _recent.AddFirst(key);
				_entries[key] = new TextureCacheEntry { Texture = texture, Bytes = bytes, Node = node };
				_usedBytes += bytes;
				while (_usedBytes > _budgetBytes && _recent.Last != null && _entries.Count > 1)
				{
					string evictKey = _recent.Last.Value;
					_recent.RemoveLast();
					TextureCacheEntry evicted = _entries[evictKey];
					_entries.Remove(evictKey);
					_usedBytes -= evicted.Bytes;
					release.Add(evicted.Texture);
				}
			}
			foreach (TaleWorlds.TwoDimension.Texture oldTexture in release)
			{
				ReleaseTexture(oldTexture);
			}
		}

		public void RemoveWhere(Func<string, bool> predicate)
		{
			List<TaleWorlds.TwoDimension.Texture> release = new List<TaleWorlds.TwoDimension.Texture>();
			lock (_lock)
			{
				foreach (string key in new List<string>(_entries.Keys))
				{
					if (!predicate(key))
					{
						continue;
					}
					TextureCacheEntry entry = _entries[key];
					_entries.Remove(key);
					_recent.Remove(entry.Node);
					_usedBytes -= entry.Bytes;
					release.Add(entry.Texture);
				}
			}
			foreach (TaleWorlds.TwoDimension.Texture texture in release)
			{
				ReleaseTexture(texture);
			}
		}

		public void Clear()
		{
			RemoveWhere(_ => true);
		}
	}

	private static readonly TextureLruCache _thumbnailCache = new TextureLruCache(64L * 1024L * 1024L);

	private static readonly TextureLruCache _portraitCache = new TextureLruCache(32L * 1024L * 1024L);

	private static readonly TextureLruCache _zoomCache = new TextureLruCache(24L * 1024L * 1024L);

	private static readonly ConcurrentDictionary<string, TaleWorlds.TwoDimension.Texture> _builtFiles = new ConcurrentDictionary<string, TaleWorlds.TwoDimension.Texture>();

	private static int _nameCounter;

	public static TaleWorlds.TwoDimension.Texture GetOrBuildFile(string key, string path)
	{
		return GetOrBuildFile(key, path, true);
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuildFileDirect(string key, string path)
	{
		return GetOrBuildFile(key, path, false);
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuildFileBacked(string key, string path)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		if (_builtFiles.TryGetValue(key, out var cached))
		{
			return cached;
		}
		try
		{
			if (!File.Exists(path))
			{
				return null;
			}
			string marker = System.IO.Path.DirectorySeparatorChar + "Modules" + System.IO.Path.DirectorySeparatorChar
				+ "ReignBeta" + System.IO.Path.DirectorySeparatorChar;
			int markerIndex = path.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
			string moduleRelativePath = markerIndex >= 0
				? path.Substring(markerIndex + marker.Length)
				: System.IO.Path.GetFileName(path);
			TaleWorlds.Engine.Texture nativeTexture = TaleWorlds.Engine.Texture.LoadTextureFromPath(moduleRelativePath, "ReignBeta");
			if (nativeTexture == null)
			{
				return null;
			}
			nativeTexture.PreloadTexture(false);
			var texture = new TaleWorlds.TwoDimension.Texture(new EngineTexture(nativeTexture));
			if (!texture.IsValid)
			{
				nativeTexture.ReleaseAfterNumberOfFrames(2);
				return null;
			}
			_builtFiles[key] = texture;
			Debug.Print("[Bannerlord Reign] Loaded file-backed UI texture " + key + " from " + path);
			return texture;
		}
		catch (Exception ex)
		{
			Debug.Print("[Bannerlord Reign] File-backed UI texture load failed for " + key + ": " + ex.Message);
			return null;
		}
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuildFileRedBlueSwapped(string key, string path)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		string swappedKey = key + "|red-blue-swapped";
		if (_builtFiles.TryGetValue(swappedKey, out var cached))
		{
			return cached;
		}
		try
		{
			if (!File.Exists(path))
			{
				Debug.Print("[Bannerlord Reign] Red/blue-swapped UI texture file is missing: " + path);
				return null;
			}
			byte[] bytes = PngReencode.SwapRedBlueToPngEncoderFormat(File.ReadAllBytes(path));
			if (bytes == null)
			{
				Debug.Print("[Bannerlord Reign] Red/blue-swapped UI texture conversion failed for " + key + ".");
				return null;
			}
			TaleWorlds.TwoDimension.Texture texture = BuildTexture("ui_" + swappedKey, bytes, "GetOrBuildFileRedBlueSwapped");
			if (texture != null)
			{
				_builtFiles[swappedKey] = texture;
				Debug.Print("[Bannerlord Reign] Loaded red/blue-swapped UI texture " + key + " from " + path);
			}
			return texture;
		}
		catch (Exception ex)
		{
			Debug.Print("[Bannerlord Reign] Red/blue-swapped UI texture load failed for " + key + ": " + ex.Message);
			return null;
		}
	}

	private static TaleWorlds.TwoDimension.Texture GetOrBuildFile(string key, string path, bool normalizeForEngine)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(path))
		{
			return null;
		}
		if (_builtFiles.TryGetValue(key, out var cached))
		{
			return cached;
		}
		try
		{
			if (!File.Exists(path))
			{
				Debug.Print("[Bannerlord Reign] UI texture file is missing: " + path);
				return null;
			}
			byte[] sourceBytes = File.ReadAllBytes(path);
			// Full-screen event, castle-map, and conversation artwork must retain its
			// authored resolution. Portrait-memory textures use the bounded 448-pixel
			// path below, but applying that thumbnail cap here makes large UI images
			// visibly blocky when Gauntlet expands them to their presentation frame.
			byte[] bytes = normalizeForEngine ? NormalizeForEngine(sourceBytes) : sourceBytes;
			TaleWorlds.TwoDimension.Texture texture = BuildTexture("ui_" + key, bytes, "GetOrBuildFile");
			if (texture != null)
			{
				_builtFiles[key] = texture;
				Debug.Print("[Bannerlord Reign] Loaded UI texture " + key + " from " + path);
			}
			return texture;
		}
		catch (Exception ex)
		{
			Debug.Print("[Bannerlord Reign] UI texture load failed for " + key + ": " + ex.Message);
			return null;
		}
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuild(string id)
	{
		return GetOrBuild(id, PortraitQualityTier.Thumbnail);
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuild(string id, PortraitQualityTier tier)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		TextureLruCache cache = CacheFor(tier);
		string cacheKey = id + "|tier:" + tier;
		if (cache.TryGet(cacheKey, out var value))
		{
			return value;
		}
		byte[] array = GetBytes(id, tier);
		if (array == null || array.Length == 0)
		{
			return null;
		}
		try
		{
			// Portrait derivatives keep their authored rectangular composition here.
			// Circular and oval presentation is owned by an opaque UI aperture plate;
			// never pre-crop, recolor, or alpha-mask portrait bytes on this path.
			if (MemoryService.IsMemoryKey(id))
			{
				array = NormalizeForEngine(array, 448);
			}
			TaleWorlds.TwoDimension.Texture texture = BuildTexture(cacheKey, array, "GetOrBuild");
			if (texture == null)
			{
				QueueRepair(id);
				return null;
			}
			cache.Add(cacheKey, texture);
			return texture;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] TextureFactory.GetOrBuild failed for " + id + ": " + ex.Message);
			return null;
		}
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuildForAspect(string id, float targetAspect, bool contain = false)
	{
		return GetOrBuildForAspect(id, targetAspect, contain, PortraitQualityTier.Thumbnail);
	}

	public static TaleWorlds.TwoDimension.Texture GetOrBuildForAspect(string id, float targetAspect, bool contain, PortraitQualityTier tier)
	{
		if (string.IsNullOrEmpty(id))
		{
			return null;
		}
		if (targetAspect <= 0.01f || float.IsNaN(targetAspect) || float.IsInfinity(targetAspect))
		{
			return GetOrBuild(id, tier);
		}
		int num = (int)Math.Round(targetAspect * 1000f);
		string text = id + "|tier:" + tier + (contain ? "|contain:" : "|aspect:") + num;
		TextureLruCache cache = CacheFor(tier);
		if (cache.TryGet(text, out var value))
		{
			return value;
		}
		byte[] array = GetBytes(id, tier);
		if (array == null || array.Length == 0)
		{
			return null;
		}
		try
		{
			if (MemoryService.IsMemoryKey(id))
			{
				array = NormalizeForEngine(array, 448);
			}
			byte[] array2 = (contain ? ContainToAspect(array, targetAspect, 0.5f) : CenterCropToAspect(array, targetAspect));
			if (array2 != null)
			{
				array = array2;
			}
			TaleWorlds.TwoDimension.Texture texture = BuildTexture(text, array, "GetOrBuildForAspect");
			if (texture == null)
			{
				QueueRepair(id);
				return null;
			}
			cache.Add(text, texture);
			return texture;
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] TextureFactory.GetOrBuildForAspect failed for " + id + ": " + ex.Message);
			return GetOrBuild(id, tier);
		}
	}

	private static byte[] ContainToAspect(byte[] pngBytes, float targetAspect, float borderReduction)
	{
		int width;
		int height;
		byte[] array = PngReencode.DecodeToRgba(pngBytes, out width, out height);
		if (array == null || width <= 0 || height <= 0)
		{
			return null;
		}
		float num = (float)width / (float)height;
		if (Math.Abs(num - targetAspect) < 0.02f)
		{
			return pngBytes;
		}
		int num2 = width;
		int num3 = height;
		if (num < targetAspect)
		{
			num2 = Math.Max(width, (int)Math.Round((float)height * targetAspect));
		}
		else
		{
			num3 = Math.Max(height, (int)Math.Round((float)width / targetAspect));
		}
		byte[] array2 = new byte[num2 * num3 * 4];
		for (int i = 0; i < array2.Length; i += 4)
		{
			array2[i] = 0;
			array2[i + 1] = 0;
			array2[i + 2] = 0;
			array2[i + 3] = byte.MaxValue;
		}
		borderReduction = Math.Max(0f, Math.Min(1f, borderReduction));
		float num4 = 1f;
		if (num < targetAspect)
		{
			num4 = 1f + borderReduction * ((float)num2 / (float)width - 1f);
		}
		else if (num > targetAspect)
		{
			num4 = 1f + borderReduction * ((float)num3 / (float)height - 1f);
		}
		int num5 = Math.Max(1, (int)Math.Round((float)width * num4));
		int num6 = Math.Max(1, (int)Math.Round((float)height * num4));
		int num7 = (num2 - num5) / 2;
		int num8 = (int)Math.Round((float)(num3 - num6) * 0.3f);
		for (int j = 0; j < num3; j++)
		{
			int num9 = (int)Math.Floor((float)(j - num8) / num4);
			if (num9 < 0 || num9 >= height)
			{
				continue;
			}
			for (int k = 0; k < num2; k++)
			{
				int num10 = (int)Math.Floor((float)(k - num7) / num4);
				if (num10 >= 0 && num10 < width)
				{
					int num11 = (num9 * width + num10) * 4;
					int num12 = (j * num2 + k) * 4;
					array2[num12] = array[num11];
					array2[num12 + 1] = array[num11 + 1];
					array2[num12 + 2] = array[num11 + 2];
					array2[num12 + 3] = array[num11 + 3];
				}
			}
		}
		return PngEncoder.EncodeRgba(array2, num2, num3);
	}

	private static byte[] NormalizeForEngine(byte[] imageBytes, int maximumDimension = 0)
	{
		if (imageBytes == null || imageBytes.Length == 0)
		{
			return imageBytes;
		}
		bool flag = imageBytes.Length > 3 && imageBytes[0] == byte.MaxValue && imageBytes[1] == 216 && imageBytes[2] == byte.MaxValue;
		bool flag2 = imageBytes.Length > 7 && imageBytes[0] == 137 && imageBytes[1] == 80 && imageBytes[2] == 78 && imageBytes[3] == 71;
		if (flag)
		{
			byte[] array = JpegToPng.Convert(imageBytes);
			if (array != null)
			{
				byte[] normalized = PngReencode.ToEngineTextureFormat(array, maximumDimension);
				return normalized ?? array;
			}
		}
		else if (flag2)
		{
			byte[] array2 = PngReencode.ToEngineTextureFormat(imageBytes, maximumDimension);
			if (array2 != null)
			{
				return array2;
			}
		}
		return imageBytes;
	}

	private static TaleWorlds.TwoDimension.Texture BuildTexture(string textureKey, byte[] imageBytes, string caller)
	{
		if (imageBytes == null || imageBytes.Length == 0)
		{
			return null;
		}
		TaleWorlds.Engine.Texture texture = TaleWorlds.Engine.Texture.CreateFromMemory(imageBytes);
		if (texture == null)
		{
			Debug.Print("[AIPortraits] " + caller + ": CreateFromMemory returned null for " + textureKey + " (bytes=" + imageBytes.Length + ")");
			return null;
		}
		try
		{
			string safeKey = CharacterCacheId.Sanitize(textureKey);
			if (safeKey.Length > 72)
			{
				safeKey = safeKey.Substring(safeKey.Length - 72);
			}
			texture.Name = "AIPortrait_" + Interlocked.Increment(ref _nameCounter) + "_" + safeKey;
		}
		catch (Exception ex)
		{
			texture.ReleaseAfterNumberOfFrames(2);
			Debug.Print("[AIPortraits] " + caller + ": could not assign an engine texture name for " + textureKey + ": " + ex.Message);
			return null;
		}
		EngineTexture platformTexture = new EngineTexture(texture);
		TaleWorlds.TwoDimension.Texture result = new TaleWorlds.TwoDimension.Texture(platformTexture);
		if (!result.IsValid)
		{
			texture.ReleaseAfterNumberOfFrames(2);
			Debug.Print("[AIPortraits] " + caller + ": invalid engine texture for " + textureKey);
			return null;
		}
		return result;
	}

	private static TextureLruCache CacheFor(PortraitQualityTier tier)
	{
		return tier == PortraitQualityTier.Zoom
			? _zoomCache
			: tier == PortraitQualityTier.Portrait ? _portraitCache : _thumbnailCache;
	}

	private static byte[] GetBytes(string id, PortraitQualityTier tier)
	{
		if (MemoryService.IsMemoryKey(id))
		{
			return MemoryService.GetMemoryBytes(id);
		}
		if (!PortraitDerivativeService.TryGetDerivativePath(id, tier, out string path))
		{
			return null;
		}
		try
		{
			return File.ReadAllBytes(path);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] Derivative read failed for " + id + ": " + ex.Message);
			QueueRepair(id);
			return null;
		}
	}

	private static void QueueRepair(string id)
	{
		if (!MemoryService.IsMemoryKey(id))
		{
			_ = PortraitDerivativeService.EnsureForIdAsync(id, highPriority: true, force: true);
		}
	}

	private static int EstimateBytes(TaleWorlds.TwoDimension.Texture texture)
	{
		try
		{
			if (texture?.PlatformTexture is EngineTexture engineTexture && engineTexture.Texture != null && engineTexture.Texture.MemorySize > 0)
			{
				return engineTexture.Texture.MemorySize;
			}
		}
		catch
		{
		}
		long estimate = (long)Math.Max(1, texture?.Width ?? 1) * Math.Max(1, texture?.Height ?? 1) * 4L;
		return (int)Math.Min(int.MaxValue, estimate);
	}

	private static void ReleaseTexture(TaleWorlds.TwoDimension.Texture texture)
	{
		if (texture == null)
		{
			return;
		}
		Action release = delegate
		{
			try
			{
				if (texture.PlatformTexture is EngineTexture engineTexture && engineTexture.Texture != null)
				{
					engineTexture.Texture.ReleaseAfterNumberOfFrames(2);
				}
			}
			catch (Exception ex)
			{
				Debug.Print("[AIPortraits] Texture release failed: " + ex.Message);
			}
		};
		if (ReignMainThread.IsMainThread)
		{
			release();
		}
		else
		{
			_ = ReignMainThread.InvokeAsync(release);
		}
	}

	private static byte[] CenterCropToAspect(byte[] pngBytes, float targetAspect)
	{
		int width;
		int height;
		byte[] array = PngReencode.DecodeToRgba(pngBytes, out width, out height);
		if (array == null || width <= 0 || height <= 0)
		{
			return null;
		}
		float num = (float)width / (float)height;
		if (Math.Abs(num - targetAspect) < 0.02f)
		{
			return pngBytes;
		}
		int val = width;
		int val2 = height;
		if (num > targetAspect)
		{
			val = Math.Max(1, (int)Math.Round((float)height * targetAspect));
		}
		else
		{
			val2 = Math.Max(1, (int)Math.Round((float)width / targetAspect));
		}
		val = Math.Min(val, width);
		val2 = Math.Min(val2, height);
		int num2 = (width - val) / 2;
		int num3 = (height - val2) / 2;
		if (num < targetAspect && targetAspect >= 1.15f && num < 0.95f)
		{
			num3 = (int)Math.Round((float)(height - val2) * 0.08f);
		}
		byte[] array2 = new byte[val * val2 * 4];
		for (int i = 0; i < val2; i++)
		{
			int srcOffset = ((num3 + i) * width + num2) * 4;
			int dstOffset = i * val * 4;
			Buffer.BlockCopy(array, srcOffset, array2, dstOffset, val * 4);
		}
		return PngEncoder.EncodeRgba(array2, val, val2);
	}

	public static bool Has(string id)
	{
		return !string.IsNullOrEmpty(id) && (MemoryService.IsMemoryKey(id) ? MemoryService.Exists(id) : PortraitCache.ExistsOnDisk(id));
	}

	public static string CacheDiagnostics()
	{
		return "thumbnail=" + _thumbnailCache.Count + "/" + (_thumbnailCache.UsedBytes / 1048576d).ToString("0.0") + "MB"
			+ " portrait=" + _portraitCache.Count + "/" + (_portraitCache.UsedBytes / 1048576d).ToString("0.0") + "MB"
			+ " zoom=" + _zoomCache.Count + "/" + (_zoomCache.UsedBytes / 1048576d).ToString("0.0") + "MB"
			+ " derivativeJobs=" + PortraitDerivativeService.PendingCount;
	}

	public static void Invalidate(string id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return;
		}
		_thumbnailCache.RemoveWhere(key => key.StartsWith(id + "|", StringComparison.Ordinal));
		_portraitCache.RemoveWhere(key => key.StartsWith(id + "|", StringComparison.Ordinal));
		_zoomCache.RemoveWhere(key => key.StartsWith(id + "|", StringComparison.Ordinal));
		PortraitDerivativeService.Invalidate(id);
		System.Threading.Interlocked.Increment(ref _portraitRevision);
	}

	private static int _portraitRevision;
	public static int PortraitRevision => _portraitRevision;

	public static void ClearAll()
	{
		_thumbnailCache.Clear();
		_portraitCache.Clear();
		_zoomCache.Clear();
		_builtFiles.Clear();
		PortraitDerivativeService.ClearReadyCache();
	}
}
