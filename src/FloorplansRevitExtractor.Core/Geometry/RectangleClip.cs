using System;
using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// Clips a ring to an axis-aligned rectangle (Sutherland-Hodgman). Revit
    /// clips model graphics at the view crop, so the mask must too: a wall
    /// running past the crop edge stops there in the image and in the mask.
    /// A concave ring may come back with zero-width bridges along the edge;
    /// they cover no pixel centers, so rasterizing is unaffected.
    /// </summary>
    public static class RectangleClip
    {
        /// <returns>The clipped ring, or an empty list when nothing is inside.</returns>
        public static IReadOnlyList<Point2D> Clip(IReadOnlyList<Point2D> ring, double minX, double minY, double maxX, double maxY)
        {
            List<Point2D> points = new List<Point2D>(ring);
            points = ClipEdge(points, p => p.X >= minX, (a, b) => AtX(a, b, minX));
            points = ClipEdge(points, p => p.X <= maxX, (a, b) => AtX(a, b, maxX));
            points = ClipEdge(points, p => p.Y >= minY, (a, b) => AtY(a, b, minY));
            points = ClipEdge(points, p => p.Y <= maxY, (a, b) => AtY(a, b, maxY));
            return points.Count >= 3 ? points : (IReadOnlyList<Point2D>)Array.Empty<Point2D>();
        }

        private static List<Point2D> ClipEdge(List<Point2D> points, Func<Point2D, bool> inside, Func<Point2D, Point2D, Point2D> cross)
        {
            var result = new List<Point2D>();
            for (int i = 0; i < points.Count; i++)
            {
                Point2D current = points[i];
                Point2D previous = points[(i - 1 + points.Count) % points.Count];
                bool currentIn = inside(current);
                if (currentIn != inside(previous))
                {
                    result.Add(cross(previous, current));
                }

                if (currentIn)
                {
                    result.Add(current);
                }
            }

            return result;
        }

        private static Point2D AtX(Point2D a, Point2D b, double x) =>
            new Point2D(x, a.Y + ((b.Y - a.Y) * (x - a.X) / (b.X - a.X)));

        private static Point2D AtY(Point2D a, Point2D b, double y) =>
            new Point2D(a.X + ((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)), y);
    }
}
