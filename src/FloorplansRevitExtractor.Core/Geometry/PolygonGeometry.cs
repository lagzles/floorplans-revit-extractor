using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Geometry
{
    public static class PolygonGeometry
    {
        public static double SignedArea(IReadOnlyList<Point2D> ring)
        {
            if (ring.Count < 3)
            {
                return 0.0;
            }

            double sum = 0.0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                sum += (a.X * b.Y) - (b.X * a.Y);
            }

            return sum / 2.0;
        }

        public static double Area(IReadOnlyList<Point2D> ring) => Math.Abs(SignedArea(ring));

        public static double NetArea(IReadOnlyList<Point2D> outer, IReadOnlyList<IReadOnlyList<Point2D>> holes)
        {
            double area = Area(outer);
            foreach (IReadOnlyList<Point2D> hole in holes)
            {
                area -= Area(hole);
            }

            return area;
        }

        public static Point2D Centroid(IReadOnlyList<Point2D> ring)
        {
            double signedArea = SignedArea(ring);
            if (ring.Count < 3 || signedArea == 0.0)
            {
                return AverageOfVertices(ring);
            }

            double cx = 0.0;
            double cy = 0.0;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                double cross = (a.X * b.Y) - (b.X * a.Y);
                cx += (a.X + b.X) * cross;
                cy += (a.Y + b.Y) * cross;
            }

            double factor = 1.0 / (6.0 * signedArea);
            return new Point2D(cx * factor, cy * factor);
        }

        public static (double MinX, double MinY, double MaxX, double MaxY) BBox(IReadOnlyList<Point2D> ring)
        {
            if (ring.Count == 0)
            {
                return (0.0, 0.0, 0.0, 0.0);
            }

            return (
                ring.Min(p => p.X),
                ring.Min(p => p.Y),
                ring.Max(p => p.X),
                ring.Max(p => p.Y));
        }

        private static Point2D AverageOfVertices(IReadOnlyList<Point2D> ring)
        {
            if (ring.Count == 0)
            {
                return new Point2D(0.0, 0.0);
            }

            return new Point2D(ring.Average(p => p.X), ring.Average(p => p.Y));
        }
    }
}
