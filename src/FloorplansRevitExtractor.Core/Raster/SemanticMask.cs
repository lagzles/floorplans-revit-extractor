using System;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>Painted semantic mask: one class id per pixel, row-major.</summary>
    public sealed class SemanticMask
    {
        /// <summary>Pixel value the training pipeline ignores.</summary>
        public const byte IgnoreIndex = 255;

        private readonly byte[] _pixels;
        private readonly int[] _histogram;

        internal SemanticMask(int width, int height, byte[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
            _histogram = new int[256];
            foreach (byte p in pixels)
            {
                _histogram[p]++;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public byte this[int x, int y] => _pixels[PixelSpans.IndexOf(x, y, Width, Height)];

        public int CountPixels(StructureClass cls) => _histogram[(byte)cls];

        /// <summary>Row-major pixels for whole-image passes inside the Core; never mutated.</summary>
        internal byte[] Pixels => _pixels;

        public byte[] CopyPixels()
        {
            var copy = new byte[_pixels.Length];
            Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
            return copy;
        }
    }
}
