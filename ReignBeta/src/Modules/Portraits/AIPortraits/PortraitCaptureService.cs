using System;
using System.IO;
using System.Threading;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.TwoDimension;

namespace AIPortraits;

public static class PortraitCaptureService
{
	public static byte[] CaptureFromTwoDTexture(Texture twoDTex)
	{
		string text = null;
		try
		{
			if (twoDTex == null || !twoDTex.IsValid)
			{
				return null;
			}
			if (!(twoDTex.PlatformTexture is EngineTexture { Texture: var texture }))
			{
				return null;
			}
			if (texture == null || !texture.IsValid)
			{
				return null;
			}
			PortraitCache.EnsureDirectory();
			text = Path.Combine(PortraitCache.CacheDir, "_capture_" + Guid.NewGuid().ToString("N") + ".png");
			texture.SaveToFile(text, isRelativePath: false);
			if (!WaitForFile(text, 1500))
			{
				Debug.Print("[AIPortraits] SaveToFile produced no file in time");
				return null;
			}
			byte[] array = File.ReadAllBytes(text);
			if (array == null || array.Length == 0)
			{
				return null;
			}
			return FixSavedTextureColors(array);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] CaptureFromTwoDTexture (SaveToFile) failed: " + ex.Message);
			return null;
		}
		finally
		{
			try
			{
				if (!string.IsNullOrEmpty(text) && File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch
			{
			}
		}
	}

	private static byte[] FixSavedTextureColors(byte[] savedPngBytes)
	{
		byte[] colorFixed = PngReencode.SwapRedBlueToPngEncoderFormat(savedPngBytes);
		if (colorFixed == null)
		{
			Debug.Print("[AIPortraits] Source color fix skipped: saved texture PNG could not be decoded.");
			return null;
		}
		byte[] compatible = PngReencode.NormalizeImageEditSource(colorFixed);
		if (compatible == null)
		{
			Debug.Print("[AIPortraits] Source normalization failed: image-edit sources require opaque PNG, dimensions 384-5000, and file size under 10 MB.");
			return null;
		}
		return compatible;
	}

	private static bool WaitForFile(string path, int timeoutMs)
	{
		DateTime utcNow = DateTime.UtcNow;
		while ((DateTime.UtcNow - utcNow).TotalMilliseconds < (double)timeoutMs)
		{
			try
			{
				if (File.Exists(path))
				{
					FileInfo fileInfo = new FileInfo(path);
					if (fileInfo.Length > 0)
					{
						long length = fileInfo.Length;
						Thread.Sleep(50);
						fileInfo.Refresh();
						if (fileInfo.Length == length)
						{
							return true;
						}
					}
				}
			}
			catch
			{
			}
			Thread.Sleep(50);
		}
		return false;
	}
}
