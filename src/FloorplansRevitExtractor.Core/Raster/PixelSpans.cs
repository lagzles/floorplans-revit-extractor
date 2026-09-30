using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>Receives the covered pixels [first, last] of one row.</summary>
    internal delegate void SpanWriter(int row, int first, int last);

    /// <summary>Shared guards for turning double coordinates into pixel indices.</summary>
    internal static class PixelSpans
    {
        /// <summary>
        /// Clamps in double before casting: a plain (int) cast of a coordinate
        /// beyond the int range wraps around and fills whole rows.
        /// </summary>
        internal static int Clamp(double value, int min, int max) =>
            value <= min ? min : value >= max ? max : (int)value;

        /// <summary>A NaN or infinite vertex would put pixels outside the shape, silently.</summary>
        internal static void RequireFinite(IEnumerable<Point2D> points, string paramName)
        {
            foreach (Point2D p in points)
            {
                if (!IsFinite(p.X) || !IsFinite(p.Y))
                {
                    throw new ArgumentException($"Non-finite coordinate ({p.X}, {p.Y}) in shape.", paramName);
                }
            }
        }

        internal static int RequireArea(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), width, "Image width must be positive.");
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height), height, "Image height must be positive.");
            }

            return checked(width * height);
        }

        internal static int IndexOf(int x, int y, int width, int height)
        {
            if (x < 0 || x >= width)
            {
                throw new ArgumentOutOfRangeException(nameof(x), x, $"Must be in [0, {width}).");
            }

            if (y < 0 || y >= height)
            {
                throw new ArgumentOutOfRangeException(nameof(y), y, $"Must be in [0, {height}).");
            }

            return (y * width) + x;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
