using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.RevitInterop
{
    internal static class RoomBoundaryExtractor
    {
        internal static (IReadOnlyList<Point2D> Outer, IReadOnlyList<IReadOnlyList<Point2D>> Holes) GetFinishBoundary(
            Room room)
        {
            IList<IReadOnlyList<Point2D>> loops = GetLoops(room, SpatialElementBoundaryLocation.Finish);
            return SplitOuterFromHoles(loops);
        }

        internal static IReadOnlyList<Point2D> GetCenterOuterLoopOrNull(Room room)
        {
            IList<IReadOnlyList<Point2D>> loops = GetLoops(room, SpatialElementBoundaryLocation.Center);
            return loops.Count == 1 ? loops[0] : null;
        }

        private static IList<IReadOnlyList<Point2D>> GetLoops(Room room, SpatialElementBoundaryLocation location)
        {
            var options = new SpatialElementBoundaryOptions { SpatialElementBoundaryLocation = location };
            IList<IList<BoundarySegment>> boundaryLoops = room.GetBoundarySegments(options);

            var loops = new List<IReadOnlyList<Point2D>>();
            foreach (IList<BoundarySegment> loop in boundaryLoops)
            {
                loops.Add(TessellateLoop(loop));
            }

            return loops;
        }

        private static IReadOnlyList<Point2D> TessellateLoop(IList<BoundarySegment> loop)
        {
            var points = new List<Point2D>();
            foreach (BoundarySegment segment in loop)
            {
                foreach (XYZ p in segment.GetCurve().Tessellate())
                {
                    points.Add(UnitsConverter.ToMetersPoint(p));
                }
            }

            return RemoveClosingDuplicate(points);
        }

        private static IReadOnlyList<Point2D> RemoveClosingDuplicate(List<Point2D> points)
        {
            const double ToleranceMeters = 0.001;
            while (points.Count > 1 && IsWithinTolerance(points[0], points[points.Count - 1], ToleranceMeters))
            {
                points.RemoveAt(points.Count - 1);
            }

            return points;
        }

        private static bool IsWithinTolerance(Point2D a, Point2D b, double tolerance) =>
            System.Math.Abs(a.X - b.X) < tolerance && System.Math.Abs(a.Y - b.Y) < tolerance;

        private static (IReadOnlyList<Point2D> Outer, IReadOnlyList<IReadOnlyList<Point2D>> Holes) SplitOuterFromHoles(
            IList<IReadOnlyList<Point2D>> loops)
        {
            if (loops.Count == 0)
            {
                return (new List<Point2D>(), new List<IReadOnlyList<Point2D>>());
            }

            IReadOnlyList<Point2D> outer = loops.OrderByDescending(PolygonGeometry.Area).First();
            List<IReadOnlyList<Point2D>> holes = loops.Where(l => !ReferenceEquals(l, outer)).ToList();
            return (outer, holes);
        }
    }
}
