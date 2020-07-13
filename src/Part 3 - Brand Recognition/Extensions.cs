using Microsoft.Azure.Kinect.Sensor;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ManCaveCoding.KinectDK.Part3
{
	public static class Extensions
	{
		public static BitmapSource CreateBitmapSource(this Image image, double dpiX = 300, double dpiY = 300)
		{
			PixelFormat pixelFormat;

			using (Image reference = image.Reference())
			{

				switch (reference.Format)
				{
					case ImageFormat.ColorBGRA32:
						pixelFormat = PixelFormats.Bgra32;
						break;
					case ImageFormat.Depth16:
					case ImageFormat.IR16:
						pixelFormat = PixelFormats.Gray16;
						break;
					case ImageFormat.Custom8:
						pixelFormat = PixelFormats.Gray8;
						break;
					default:
						throw new AzureKinectException($"Pixel format {reference.Format} cannot be converted to a BitmapSource");
				}

				// BitmapSource.Create copies the unmanaged memory, so there is no need to keep
				// a reference after we have created the BitmapSource objects
				unsafe
				{
					using (var pin = reference.Memory.Pin())
					{
						BitmapSource source = BitmapSource.Create(
									reference.WidthPixels,
									reference.HeightPixels,
									dpiX,
									dpiY,
									pixelFormat,
									/* palette: */ null,
									(IntPtr)pin.Pointer,
									checked((int)reference.Size),
									reference.StrideBytes);
						return source;
					}
				}
			}
		}

		public static Stream CreateStream(this BitmapSource writeBmp, double dpiX = 96, double dpiY = 300)
		{

			Stream bmp = new MemoryStream();
			BitmapEncoder enc = new BmpBitmapEncoder();
			enc.Frames.Add(BitmapFrame.Create(writeBmp));
			enc.Save(bmp);
			return bmp;
		}

		public static System.Drawing.Bitmap CreateBitmap(this Image image)
		{
			unsafe
			{
				using (var pin = image.Memory.Pin())
				{
					var bitmap = new System.Drawing.Bitmap(image.WidthPixels, image.HeightPixels, image.StrideBytes, System.Drawing.Imaging.PixelFormat.Format32bppArgb, (IntPtr)pin.Pointer);
					return bitmap;
				}
			}
		}

		public static System.Drawing.Bitmap ReduceSize(this System.Drawing.Bitmap bitmap, double scaleFactor = 0.5)
		{
			var width = (int)(bitmap.Width * scaleFactor);
			var height = (int)(bitmap.Height * scaleFactor);
			System.Drawing.Bitmap result = new System.Drawing.Bitmap(width, height);
			using (var g = System.Drawing.Graphics.FromImage(result))
			{
				g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
				g.DrawImage(bitmap, 0, 0, width, height);
			}

			return result;
		}

		public static void DrawRectangle(this WriteableBitmap writeableBitmap, int left, int top, int width, int height, Color color, int lineStroke = 3)
		{
			// Compute the pixel's color
			int colorData = color.A << 24; // A
			colorData |= color.R << 16; // R
			colorData |= color.G << 8; // G
			colorData |= color.B << 0; // B
			int bpp = writeableBitmap.Format.BitsPerPixel / 8;

			unsafe
			{
				for (int y = 0; y < height; y++)
				{
					// Get a pointer to the back buffer
					Int64 pBackBuffer = (Int64)writeableBitmap.BackBuffer;

					// Find the address of the pixel to draw
					pBackBuffer += (top + y) * writeableBitmap.BackBufferStride;
					pBackBuffer += left * bpp;

					for (int x = 0; x < width; x++)
					{
						if (x < lineStroke || x > (width - lineStroke) || y < lineStroke || y > (height - lineStroke))
						{
							// Assign the color data to the pixel
							*((int*)pBackBuffer) = colorData;

							// Increment the address of the pixel to draw
							pBackBuffer += bpp;
						}
					}
				}
			}

			writeableBitmap.AddDirtyRect(new System.Windows.Int32Rect(left, top, width, height));
		}
	}
}