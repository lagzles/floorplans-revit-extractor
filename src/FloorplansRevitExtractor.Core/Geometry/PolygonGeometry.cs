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

        /// <summary>
        /// A point guaranteed to be strictly inside the ring, unlike the
        /// centroid (a U shape's centroid sits in its notch): the middle of
        /// the widest inside span among horizontal lines taken halfway
        /// between consecutive vertex heights.
        /// </summary>
        public static Point2D InteriorPoint(IReadOnlyList<Point2D> ring)
        {
            double[] heights = ring.Select(p => p.Y).Distinct().OrderBy(y => y).ToArray();
            Point2D? best = null;
            double bestSpan = 0.0;
            var crossings = new List<double>();

            for (int k = 0; k + 1 < heights.Length; k++)
            {
                double y = (heights[k] + heights[k + 1]) / 2;
                crossings.Clear();
                for (int i = 0; i < ring.Count; i++)
                {
                    Point2D a = ring[i];
                    Point2D b = ring[(i + 1) % ring.Count];
                    if ((a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y))
                    {
                        crossings.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
                    }
                }

                crossings.Sort();
                for (int j = 0; j + 1 < crossings.Count; j += 2)
                {
                    double span = crossings[j + 1] - crossings[j];
                    if (span > bestSpan)
                    {
                        bestSpan = span;
                        best = new Point2D((crossings[j] + crossings[j + 1]) / 2, y);
                    }
                }
            }

            return best ?? throw new ArgumentException("Ring has no interior (fewer than 3 distinct points or zero area).", nameof(ring));
        }

        /// <summary>Even-odd point-in-polygon test; points exactly on an edge may go either way.</summary>
        public static bool Contains(IReadOnlyList<Point2D> ring, Point2D point)
        {
            bool inside = false;
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                bool spansY = (a.Y <= point.Y && point.Y < b.Y) || (b.Y <= point.Y && point.Y < a.Y);
                if (spansY && point.X < a.X + ((point.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)))
                {
                    inside = !inside;
                }
            }

            return inside;
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
