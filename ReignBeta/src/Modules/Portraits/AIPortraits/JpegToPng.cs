using System;
using System.Runtime.CompilerServices;
using JpegLibrary;
using TaleWorlds.Library;

namespace AIPortraits;

public static class JpegToPng
{
	private class BlockWriter : JpegBlockOutputWriter
	{
		private readonly short[][] _planes;

		private readonly int[] _strides;

		private readonly int _ncomp;

		private readonly int _w;

		private readonly int _h;

		public BlockWriter(short[][] planes, int[] strides, int ncomp, int w, int h)
		{
			_planes = planes;
			_strides = strides;
			_ncomp = ncomp;
			_w = w;
			_h = h;
		}

		public override void WriteBlock(ref short blockRef, int componentIndex, int x, int y)
		{
			if (componentIndex >= _ncomp)
			{
				return;
			}
			short[] array = _planes[componentIndex];
			int num = _strides[componentIndex];
			for (int i = 0; i < 8; i++)
			{
				int num2 = y + i;
				if (num2 >= _h)
				{
					break;
				}
				for (int j = 0; j < 8; j++)
				{
					int num3 = x + j;
					if (num3 >= _w)
					{
						break;
					}
					array[num2 * num + num3] = Unsafe.Add(ref blockRef, i * 8 + j);
				}
			}
		}
	}

	public static byte[] Convert(byte[] jpeg)
	{
		try
		{
			JpegDecoder jpegDecoder = new JpegDecoder();
			jpegDecoder.SetInput(new ReadOnlyMemory<byte>(jpeg));
			jpegDecoder.Identify();
			int width = jpegDecoder.Width;
			int height = jpegDecoder.Height;
			int numberOfComponents = jpegDecoder.NumberOfComponents;
			if (width <= 0 || height <= 0)
			{
				return null;
			}
			if (numberOfComponents != 1 && numberOfComponents != 3)
			{
				return null;
			}
			int num = (width + 7) / 8;
			int num2 = (height + 7) / 8;
			int[] array = new int[numberOfComponents];
			int[] array2 = new int[numberOfComponents];
			int num3 = 1;
			int num4 = 1;
			for (int i = 0; i < numberOfComponents; i++)
			{
				array[i] = jpegDecoder.GetHorizontalSampling(i);
				array2[i] = jpegDecoder.GetVerticalSampling(i);
				if (array[i] > num3)
				{
					num3 = array[i];
				}
				if (array2[i] > num4)
				{
					num4 = array2[i];
				}
			}
			int num5 = num3 * 8;
			int num6 = num4 * 8;
			int num7 = (width + num5 - 1) / num5;
			int num8 = (height + num6 - 1) / num6;
			int num9 = num7 * num5;
			int num10 = num8 * num6;
			short[][] array3 = new short[numberOfComponents][];
			int[] array4 = new int[numberOfComponents];
			for (int j = 0; j < numberOfComponents; j++)
			{
				array4[j] = num9;
				array3[j] = new short[num9 * num10];
			}
			BlockWriter outputWriter = new BlockWriter(array3, array4, numberOfComponents, num9, num10);
			jpegDecoder.SetOutputWriter(outputWriter);
			jpegDecoder.Decode();
			byte[] array5 = new byte[width * height * 4];
			if (numberOfComponents == 1)
			{
				int num11 = array4[0];
				for (int k = 0; k < height; k++)
				{
					for (int l = 0; l < width; l++)
					{
						byte b = ClampByte(array3[0][k * num11 + l]);
						int num12 = (k * width + l) * 4;
						array5[num12] = b;
						array5[num12 + 1] = b;
						array5[num12 + 2] = b;
						array5[num12 + 3] = byte.MaxValue;
					}
				}
			}
			else
			{
				int num13 = array4[0];
				int num14 = array4[1];
				int num15 = array4[2];
				for (int m = 0; m < height; m++)
				{
					for (int n = 0; n < width; n++)
					{
						int num16 = array3[0][m * num13 + n];
						int num17 = array3[1][m * num14 + n] - 128;
						int num18 = array3[2][m * num15 + n] - 128;
						int num19 = (m * width + n) * 4;
						array5[num19] = ClampByte(num16 + (91881 * num18 >> 16));
						array5[num19 + 1] = ClampByte(num16 - (22554 * num17 + 46802 * num18 >> 16));
						array5[num19 + 2] = ClampByte(num16 + (116130 * num17 >> 16));
						array5[num19 + 3] = byte.MaxValue;
					}
				}
			}
			return PngEncoder.EncodeRgba(array5, width, height);
		}
		catch (Exception ex)
		{
			Debug.Print("[AIPortraits] JpegToPng failed: " + ex.Message);
			return null;
		}
	}

	private static byte ClampByte(int v)
	{
		return (byte)((v >= 0) ? ((v > 255) ? 255u : ((uint)v)) : 0u);
	}
}
