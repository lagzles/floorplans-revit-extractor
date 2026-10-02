using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// Convex hull (Andrew's monotone chain), counter-clockwise, collinear
    /// points dropped. The plan footprint of a box whatever way it is turned
    /// in 3D: a wall-hung fixture's local Z is horizontal, so its box corners
    /// must all be projected, not just the four at its local base.
    /// </summary>
    public static class ConvexHull
    {
        public static IReadOnlyList<Point2D> Of(IEnumerable<Point2D> points)
        {
            Point2D[] sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
            if (sorted.Length < 3)
            {
                return sorted;
            }

            var lower = new List<Point2D>();
            foreach (Point2D p in sorted)
            {
                AddTurningLeft(lower, p);
            }

            var upper = new List<Point2D>();
            foreach (Point2D p in sorted.Reverse())
            {
                AddTurningLeft(upper, p);
            }

            lower.RemoveAt(lower.Count - 1);
            upper.RemoveAt(upper.Count - 1);
            return lower.Concat(upper).ToArray();
        }

        private static void AddTurningLeft(List<Point2D> chain, Point2D p)
        {
            while (chain.Count >= 2 && Cross(chain[chain.Count - 2], chain[chain.Count - 1], p) <= 0)
            {
                chain.RemoveAt(chain.Count - 1);
            }

            chain.Add(p);
        }

        private static double Cross(Point2D o, Point2D a, Point2D b) => ((a.X - o.X) * (b.Y - o.Y)) - ((a.Y - o.Y) * (b.X - o.X));
    }
}
