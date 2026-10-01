using System;

namespace FloorplansRevitExtractor.Core.Png
{
    /// <summary>An 8-bit RGB image, row-major, three bytes per pixel. Read-only.</summary>
    public sealed class RgbImage
    {
        private readonly byte[] _pixels;

        internal RgbImage(int width, int height, byte[] pixels)
        {
            if (pixels.Length != (long)width * height * 3)
            {
                throw new ArgumentException($"Expected {(long)width * height * 3} bytes for {width}x{height} RGB, got {pixels.Length}.");
            }

            Width = width;
            Height = height;
            _pixels = pixels;
        }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// Luminance as Pillow's convert("L") computes it, so ink measurements
        /// match the training repo's scripts: (R*19595 + G*38470 + B*7471 + 0x8000) >> 16.
        /// </summary>
        public byte Gray(int x, int y)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside {Width}x{Height}.");
            }

            return GrayAt((y * Width) + x);
        }

        public byte[] CopyPixels()
        {
            var copy = new byte[_pixels.Length];
            Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
            return copy;
        }

        internal byte GrayAt(int pixelIndex)
        {
            int i = pixelIndex * 3;
            return (byte)(((_pixels[i] * 19595) + (_pixels[i + 1] * 38470) + (_pixels[i + 2] * 7471) + 0x8000) >> 16);
        }

        internal byte[] Pixels => _pixels;
    }
}
