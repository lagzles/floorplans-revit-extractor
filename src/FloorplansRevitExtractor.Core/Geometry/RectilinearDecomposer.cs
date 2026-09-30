using System;
using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// Deliberately minimal: only recognizes a single axis-aligned rectangle.
    /// Any other shape (L/T/U, non-orthogonal, with holes) yields no rects,
    /// per spec guidance to not force a decomposition ("não force").
    /// </summary>
    public static class RectilinearDecomposer
    {
        private const double Tolerance = 0.001;

        public static IReadOnlyList<(double X, double Y, double W, double H)> TryDecompose(
            IReadOnlyList<Point2D>? outerRing)
        {
            if (outerRing is null || outerRing.Count != 4 || !IsAxisAlignedRectangle(outerRing))
            {
                return Array.Empty<(double, double, double, double)>();
            }

            (double minX, double minY, double maxX, double maxY) = PolygonGeometry.BBox(outerRing);
            return new[] { (minX, minY, maxX - minX, maxY - minY) };
        }

        private static bool IsAxisAlignedRectangle(IReadOnlyList<Point2D> ring)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                bool sameX = Math.Abs(a.X - b.X) < Tolerance;
                bool sameY = Math.Abs(a.Y - b.Y) < Tolerance;
                if (!sameX && !sameY)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
