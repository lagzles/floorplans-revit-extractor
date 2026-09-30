using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    internal static class RasterTestShapes
    {
        internal static PolygonShape Rect(double x0, double y0, double x1, double y1) =>
            new PolygonShape(RectRing(x0, y0, x1, y1), Array.Empty<IReadOnlyList<Point2D>>());

        internal static IReadOnlyList<Point2D> RectRing(double x0, double y0, double x1, double y1) => new[]
        {
            new Point2D(x0, y0),
            new Point2D(x1, y0),
            new Point2D(x1, y1),
            new Point2D(x0, y1),
        };

        /// <summary>
        /// Independent oracle: even-odd ray casting evaluated at every pixel
        /// center, with no scanline logic shared with the code under test.
        /// </summary>
        internal static bool[] BruteForceCoverage(int width, int height, PolygonShape shape)
        {
            IReadOnlyList<Point2D>[] rings = new[] { shape.Outer }.Concat(shape.Holes).ToArray();
            var result = new bool[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int crossings = rings.Sum(ring => CountCrossings(ring, x + 0.5, y + 0.5));
                    result[(y * width) + x] = crossings % 2 == 1;
                }
            }

            return result;
        }

        private static int CountCrossings(IReadOnlyList<Point2D> ring, double px, double py)
        {
            int count = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                bool spansRow = (a.Y <= py && py < b.Y) || (b.Y <= py && py < a.Y);
                if (spansRow && px < a.X + ((py - a.Y) * (b.X - a.X) / (b.Y - a.Y)))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
