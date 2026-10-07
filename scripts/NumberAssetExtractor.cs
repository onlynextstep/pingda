using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Offline build utility, not shipped or executed at application startup.
// No resizing, glyph reconstruction, blur or stochastic generation: every dot stays at its source pixel.
public static class NumberAssetExtractor
{
    public static int[] Extract(string source, string destination)
    {
        using (var input = new Bitmap(source))
        {
            int width = input.Width, height = input.Height;
            var inputRect = new Rectangle(0, 0, width, height);
            var bits = input.LockBits(inputRect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] pixels = new byte[width * height * 4];
            try
            {
                for (int y = 0; y < height; y++)
                    Marshal.Copy(IntPtr.Add(bits.Scan0, y * bits.Stride), pixels, y * width * 4, width * 4);
            }
            finally { input.UnlockBits(bits); }
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                if (Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2])) < 180) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            if (right < left || bottom < top) throw new InvalidOperationException("原图没有可提取的白色数字。");
            // Shared optical padding ratio leaves breathing room for the existing halo.
            int padding = (int)Math.Ceiling((bottom - top + 1) * .08);
            left = Math.Max(0, left - padding); top = Math.Max(0, top - padding);
            right = Math.Min(width - 1, right + padding); bottom = Math.Min(height - 1, bottom + padding);
            int cropWidth = right - left + 1, cropHeight = bottom - top + 1;
            const int blackPoint = 36; // Removes the charcoal texture; keeps continuous, not binary, white-light alpha.
            byte[] rgba = new byte[cropWidth * cropHeight * 4];
            for (int y = 0; y < cropHeight; y++)
            for (int x = 0; x < cropWidth; x++)
            {
                int i = ((top + y) * width + left + x) * 4, o = (y * cropWidth + x) * 4;
                int light = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
                byte alpha = (byte)Math.Max(0, (light - blackPoint) * 255 / (255 - blackPoint));
                rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                rgba[o + 3] = alpha;
            }
            using (var output = new Bitmap(cropWidth, cropHeight, PixelFormat.Format32bppArgb))
            {
                var outputBits = output.LockBits(new Rectangle(0, 0, cropWidth, cropHeight), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    for (int y = 0; y < cropHeight; y++)
                        Marshal.Copy(rgba, y * cropWidth * 4, IntPtr.Add(outputBits.Scan0, y * outputBits.Stride), cropWidth * 4);
                }
                finally { output.UnlockBits(outputBits); }
                output.Save(destination, ImageFormat.Png);
            }
            return new[] { left, top, cropWidth, cropHeight, blackPoint };
        }
    }
}
