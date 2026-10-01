using System;
using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>A room to paint into the instance mask, in pixel coordinates.</summary>
    public sealed record RoomInstance(int RoomId, IReadOnlyList<PolygonShape> Polygons);

    /// <summary>
    /// Painted instance mask: 0 outside rooms, room_id + 1 inside, row-major.
    /// Written as a 16-bit PNG (PIL mode I;16).
    /// </summary>
    public sealed class InstanceMask
    {
        private readonly ushort[] _pixels;
        private readonly int[] _histogram;

        internal InstanceMask(int width, int height, ushort[] pixels)
        {
            Width = width;
            Height = height;
            _pixels = pixels;
            _histogram = new int[ushort.MaxValue + 1];
            foreach (ushort p in pixels)
            {
                _histogram[p]++;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public ushort this[int x, int y] => _pixels[PixelSpans.IndexOf(x, y, Width, Height)];

        /// <summary>Row-major pixels for whole-image passes inside the Core; never mutated.</summary>
        internal ushort[] Pixels => _pixels;

        /// <summary>The label's area_px: real pixel count, not polygon area.</summary>
        public int CountPixels(ushort instanceId) => _histogram[instanceId];

        public ushort[] CopyPixels()
        {
            var copy = new ushort[_pixels.Length];
            Array.Copy(_pixels, copy, _pixels.Length);
            return copy;
        }
    }
}
