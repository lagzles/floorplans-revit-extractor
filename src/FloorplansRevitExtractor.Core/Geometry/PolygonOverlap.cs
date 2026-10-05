using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// How much of a polygon other polygons cover, measured on a grid of
    /// sample points (10 cm by default). Rooms of one floor only touch along
    /// wall faces, so any real overlap means two floors' rooms are stacked in
    /// the same plan, as a double-height room under the floor above.
    /// </summary>
    public static class PolygonOverlap
    {
        public const double DefaultCellM = 0.1;

        /// <returns>Share of the candidate's sample points inside any of the others, 0 to 1.</returns>
        public static double SharedFraction(IReadOnlyList<Point2D> candidate, IEnumerable<IReadOnlyList<Point2D>> others, double cellM = DefaultCellM)
        {
            List<IReadOnlyList<Point2D>> covering = others.ToList();
            (double minX, double minY, double maxX, double maxY) = PolygonGeometry.BBox(candidate);
            if (covering.Count == 0 || maxX <= minX || maxY <= minY)
            {
                return 0;
            }

            covering = covering.Where(o => Overlaps(PolygonGeometry.BBox(o), minX, minY, maxX, maxY)).ToList();
            int inside = 0;
            int shared = 0;
            for (double y = minY + (cellM / 2); y < maxY; y += cellM)
            {
                for (double x = minX + (cellM / 2); x < maxX; x += cellM)
                {
                    var p = new Point2D(x, y);
                    if (!PolygonGeometry.Contains(candidate, p))
                    {
                        continue;
                    }

                    inside++;
                    shared += covering.Any(o => PolygonGeometry.Contains(o, p)) ? 1 : 0;
                }
            }

            return inside == 0 ? 0 : (double)shared / inside;
        }

        private static bool Overlaps((double MinX, double MinY, double MaxX, double MaxY) box, double minX, double minY, double maxX, double maxY) =>
            box.MinX < maxX && box.MaxX > minX && box.MinY < maxY && box.MaxY > minY;
    }
}
