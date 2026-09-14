using System;

namespace AIPortraits;

public static class MemoryReferenceComposer
{
	public static byte[] ComposeTwoPortraitReference(byte[] playerImage, byte[] npcImage)
	{
		if (!TryDecodeAny(playerImage, out var rgba, out var width, out var height))
		{
			return null;
		}
		if (!TryDecodeAny(npcImage, out var rgba2, out var width2, out var height2))
		{
			return null;
		}
		byte[] array = new byte[3145728];
		Fill(array, 24, 22, 20, byte.MaxValue);
		int num = 464;
		int boxHeight = 704;
		BlitContain(rgba, width, height, array, 1024, 768, 32, 32, num, boxHeight);
		BlitContain(rgba2, width2, height2, array, 1024, 768, 32 + num + 32, 32, num, boxHeight);
		return PngEncoder.EncodeRgba(array, 1024, 768);
	}

	private static bool TryDecodeAny(byte[] imageBytes, out byte[] rgba, out int width, out int height)
	{
		rgba = null;
		width = 0;
		height = 0;
		if (imageBytes == null || imageBytes.Length == 0)
		{
			return false;
		}
		byte[] array = imageBytes;
		if (IsJpeg(imageBytes))
		{
			array = JpegToPng.Convert(imageBytes);
			if (array == null)
			{
				return false;
			}
		}
		rgba = PngReencode.DecodeToRgba(array, out width, out height);
		if (rgba != null && width > 0)
		{
			return height > 0;
		}
		return false;
	}

	private static bool IsJpeg(byte[] bytes)
	{
		if (bytes.Length >= 3 && bytes[0] == byte.MaxValue && bytes[1] == 216)
		{
			return bytes[2] == byte.MaxValue;
		}
		return false;
	}

	private static void Fill(byte[] rgba, byte r, byte g, byte b, byte a)
	{
		for (int i = 0; i + 3 < rgba.Length; i += 4)
		{
			rgba[i] = r;
			rgba[i + 1] = g;
			rgba[i + 2] = b;
			rgba[i + 3] = a;
		}
	}

	private static void BlitContain(byte[] source, int sourceWidth, int sourceHeight, byte[] destination, int destinationWidth, int destinationHeight, int boxX, int boxY, int boxWidth, int boxHeight)
	{
		if (source == null || destination == null || sourceWidth <= 0 || sourceHeight <= 0)
		{
			return;
		}
		float num = Math.Min((float)boxWidth / (float)sourceWidth, (float)boxHeight / (float)sourceHeight);
		int num2 = Math.Max(1, (int)Math.Round((float)sourceWidth * num));
		int num3 = Math.Max(1, (int)Math.Round((float)sourceHeight * num));
		int num4 = boxX + (boxWidth - num2) / 2;
		int num5 = boxY + (boxHeight - num3) / 2;
		for (int i = 0; i < num3; i++)
		{
			int num6 = Math.Min(sourceHeight - 1, (int)((float)i / num));
			int num7 = num5 + i;
			if (num7 < 0 || num7 >= destinationHeight)
			{
				continue;
			}
			for (int j = 0; j < num2; j++)
			{
				int num8 = Math.Min(sourceWidth - 1, (int)((float)j / num));
				int num9 = num4 + j;
				if (num9 >= 0 && num9 < destinationWidth)
				{
					int num10 = (num6 * sourceWidth + num8) * 4;
					int num11 = (num7 * destinationWidth + num9) * 4;
					destination[num11] = source[num10];
					destination[num11 + 1] = source[num10 + 1];
					destination[num11 + 2] = source[num10 + 2];
					destination[num11 + 3] = source[num10 + 3];
				}
			}
		}
	}
}
