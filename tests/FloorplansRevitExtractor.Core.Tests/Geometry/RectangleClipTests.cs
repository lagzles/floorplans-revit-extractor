using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class RectangleClipTests
    {
        private static IReadOnlyList<Point2D> Ring(params (double X, double Y)[] p) => p.Select(q => new Point2D(q.X, q.Y)).ToArray();

        [Fact]
        public void Clip_RingInside_IsUnchanged()
        {
            IReadOnlyList<Point2D> ring = Ring((1, 1), (3, 1), (3, 2), (1, 2));

            RectangleClip.Clip(ring, 0, 0, 10, 10).Should().Equal(ring);
        }

        [Fact]
        public void Clip_WallCrossingTheCropEdge_IsCutAtTheEdge()
        {
            // A wall from x = -2 to x = 6 m, cropped at x = 0.
            IReadOnlyList<Point2D> clipped = RectangleClip.Clip(Ring((-2, 1), (6, 1), (6, 1.2), (-2, 1.2)), 0, 0, 10, 10);

            clipped.Min(p => p.X).Should().Be(0);
            PolygonGeometry.Area(clipped).Should().BeApproximately(6 * 0.2, 1e-9);
        }

        [Fact]
        public void Clip_RingEntirelyOutside_IsEmpty()
        {
            RectangleClip.Clip(Ring((11, 1), (13, 1), (13, 2), (11, 2)), 0, 0, 10, 10).Should().BeEmpty();
        }

        [Fact]
        public void Clip_RotatedSquareOverACorner_KeepsOnlyTheInsideArea()
        {
            // Diamond centered on the corner (0, 0): a quarter of it is inside.
            IReadOnlyList<Point2D> clipped = RectangleClip.Clip(Ring((0, -2), (2, 0), (0, 2), (-2, 0)), 0, 0, 10, 10);

            PolygonGeometry.Area(clipped).Should().BeApproximately(2.0, 1e-9);
        }
    }
}
