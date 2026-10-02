using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class PolylineChainerTests
    {
        private static IReadOnlyList<Point2D> Seg(params (double X, double Y)[] p) => p.Select(q => new Point2D(q.X, q.Y)).ToArray();

        [Fact]
        public void Chain_ShuffledAndReversedSegments_CloseIntoOneRing()
        {
            // A 4 x 2 rectangle sketched out of order, two edges drawn backwards.
            var pieces = new[]
            {
                Seg((4, 2), (0, 2)),
                Seg((0, 0), (4, 0)),
                Seg((0, 0), (0, 2)), // reversed
                Seg((4, 2), (4, 0)), // reversed
            };

            IReadOnlyList<IReadOnlyList<Point2D>> rings = PolylineChainer.Chain(pieces, 1e-6);

            rings.Should().ContainSingle();
            PolygonGeometry.Area(rings[0]).Should().BeApproximately(8, 1e-9);
            rings[0].Should().HaveCount(4, "shared endpoints appear once");
        }

        [Fact]
        public void Chain_TwoLoopsInOneSketch_GiveTwoRings()
        {
            var pieces = new[]
            {
                Seg((0, 0), (10, 0)), Seg((2, 2), (4, 2)), Seg((10, 0), (10, 10)), Seg((4, 2), (4, 4)),
                Seg((10, 10), (0, 10)), Seg((4, 4), (2, 4)), Seg((0, 10), (0, 0)), Seg((2, 4), (2, 2)),
            };

            IReadOnlyList<IReadOnlyList<Point2D>> rings = PolylineChainer.Chain(pieces, 1e-6);

            rings.Select(PolygonGeometry.Area).OrderBy(a => a).Should().Equal(4, 100);
        }

        [Fact]
        public void Chain_TessellatedArcPieces_KeepTheirInnerPoints()
        {
            var pieces = new[] { Seg((0, 0), (1, -1), (2, 0)), Seg((2, 0), (0, 0)) };

            PolylineChainer.Chain(pieces, 1e-6).Single().Should().HaveCount(3);
        }

        [Fact]
        public void Chain_OpenChain_IsDropped()
        {
            // A sketch that does not close describes no area.
            PolylineChainer.Chain(new[] { Seg((0, 0), (1, 0)), Seg((1, 0), (1, 1)) }, 1e-6).Should().BeEmpty();
        }
    }

    public class ConvexHullTests
    {
        [Fact]
        public void Of_PointsOfABoxSeenFromAbove_IsItsFootprint()
        {
            // 8 corners of a 2 x 1 box tilted so its top and bottom project to the same rectangle.
            var points = new[]
            {
                new Point2D(0, 0), new Point2D(2, 0), new Point2D(2, 1), new Point2D(0, 1),
                new Point2D(0, 0), new Point2D(2, 0), new Point2D(2, 1), new Point2D(1, 0.5),
            };

            IReadOnlyList<Point2D> hull = ConvexHull.Of(points);

            hull.Should().HaveCount(4);
            PolygonGeometry.Area(hull).Should().BeApproximately(2, 1e-9);
            PolygonGeometry.SignedArea(hull).Should().BePositive("counter-clockwise");
        }

        [Fact]
        public void Of_CollinearPoints_IsDegenerate()
        {
            ConvexHull.Of(new[] { new Point2D(0, 0), new Point2D(1, 1), new Point2D(2, 2) }).Count.Should().BeLessThan(3);
        }
    }
}
