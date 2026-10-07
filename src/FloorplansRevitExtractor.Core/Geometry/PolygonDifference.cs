using System;
using System.Collections.Generic;
using System.Linq;
using Clipper2Lib;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>A polygon with its holes, as Clipper2 returns them nested.</summary>
    public sealed record PolygonWithHoles(IReadOnlyList<Point2D> Outer, IReadOnlyList<IReadOnlyList<Point2D>> Holes);

    /// <summary>
    /// Region minus region (Clipper2), for what part of a room of the floor
    /// below is open to below: the room's area minus the slab of this plan,
    /// where a slab opening counts as open. Pieces smaller than
    /// <see cref="MinPieceM2"/> are dropped: a room's finish line and a slab's
    /// edge never coincide to the millimetre, and the slivers they leave are
    /// not openings.
    /// </summary>
    public static class PolygonDifference
    {
        public const double MinPieceM2 = 0.25;

        /// <summary>Decimal places Clipper2 keeps (micrometres, on coordinates in meters).</summary>
        private const int Precision = 6;

        public static IReadOnlyList<PolygonWithHoles> Subtract(
            IReadOnlyList<Point2D> outer,
            IReadOnlyList<IReadOnlyList<Point2D>> holes,
            IReadOnlyList<IReadOnlyList<Point2D>> cutOuters,
            IReadOnlyList<IReadOnlyList<Point2D>> cutHoles)
        {
            PathsD region = Clipper.Difference(Paths(new[] { outer }), Paths(holes), FillRule.NonZero, Precision);
            PathsD cut = Clipper.Difference(
                Clipper.Union(Paths(cutOuters), new PathsD(), FillRule.NonZero, Precision),
                Clipper.Union(Paths(cutHoles), new PathsD(), FillRule.NonZero, Precision),
                FillRule.NonZero,
                Precision);

            var tree = new PolyTreeD();
            Clipper.BooleanOp(ClipType.Difference, region, cut, tree, FillRule.NonZero, Precision);
            var pieces = new List<PolygonWithHoles>();
            Collect(tree, pieces);
            return pieces.Where(p => PolygonGeometry.NetArea(p.Outer, p.Holes) >= MinPieceM2).ToList();
        }

        private static void Collect(PolyPathD node, List<PolygonWithHoles> pieces)
        {
            for (int i = 0; i < node.Count; i++)
            {
                PolyPathD outer = node[i];
                var holes = new List<IReadOnlyList<Point2D>>();
                for (int j = 0; j < outer.Count; j++)
                {
                    PolyPathD hole = outer[j];
                    holes.Add(Points(hole.Polygon));
                    Collect(hole, pieces); // islands inside a hole are pieces of their own
                }

                pieces.Add(new PolygonWithHoles(Points(outer.Polygon), holes));
            }
        }

        private static PathsD Paths(IEnumerable<IReadOnlyList<Point2D>> rings)
        {
            var paths = new PathsD();
            foreach (IReadOnlyList<Point2D> ring in rings)
            {
                var path = new PathD(ring.Count);
                path.AddRange(ring.Select(p => new PointD(p.X, p.Y)));
                paths.Add(path);
            }

            return paths;
        }

        private static IReadOnlyList<Point2D> Points(PathD? path) =>
            path == null ? Array.Empty<Point2D>() : path.Select(p => new Point2D(p.x, p.y)).ToArray();
    }
}
