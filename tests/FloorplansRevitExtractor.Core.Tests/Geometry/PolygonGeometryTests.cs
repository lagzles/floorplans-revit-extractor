using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class PolygonGeometryTests
    {
        private static readonly IReadOnlyList<Point2D> Rectangle4x3 = new[]
        {
            new Point2D(0, 0),
            new Point2D(4, 0),
            new Point2D(4, 3),
            new Point2D(0, 3),
        };

        private static readonly IReadOnlyList<Point2D> Rectangle4x3Clockwise = new[]
        {
            new Point2D(0, 0),
            new Point2D(0, 3),
            new Point2D(4, 3),
            new Point2D(4, 0),
        };

        [Fact]
        public void Area_SimpleRectangle_ReturnsWidthTimesHeight()
        {
            PolygonGeometry.Area(Rectangle4x3).Should().BeApproximately(12.0, 1e-9);
        }

        [Fact]
        public void Area_SameForClockwiseAndCounterClockwiseWinding()
        {
            PolygonGeometry.Area(Rectangle4x3).Should().Be(PolygonGeometry.Area(Rectangle4x3Clockwise));
        }

        [Fact]
        public void NetArea_SubtractsHoleArea()
        {
            // outer 10x8 = 80, hole (shaft) 2x2 = 4 fully inside -> net 76.
            IReadOnlyList<Point2D> outer = new[]
            {
                new Point2D(0, 0),
                new Point2D(10, 0),
                new Point2D(10, 8),
                new Point2D(0, 8),
            };
            IReadOnlyList<Point2D> hole = new[]
            {
                new Point2D(4, 3),
                new Point2D(6, 3),
                new Point2D(6, 5),
                new Point2D(4, 5),
            };

            PolygonGeometry.NetArea(outer, new[] { hole }).Should().BeApproximately(76.0, 1e-9);
        }

        [Fact]
        public void Centroid_Rectangle_ReturnsCenterPoint()
        {
            Point2D centroid = PolygonGeometry.Centroid(Rectangle4x3);

            centroid.X.Should().BeApproximately(2.0, 1e-9);
            centroid.Y.Should().BeApproximately(1.5, 1e-9);
        }

        [Fact]
        public void BBox_ArbitraryPolygon_ReturnsCorrectMinMax()
        {
            IReadOnlyList<Point2D> lShape = new[]
            {
                new Point2D(0, 0),
                new Point2D(5, 0),
                new Point2D(5, 2),
                new Point2D(2, 2),
                new Point2D(2, 4),
                new Point2D(0, 4),
            };

            (double minX, double minY, double maxX, double maxY) = PolygonGeometry.BBox(lShape);

            minX.Should().Be(0);
            minY.Should().Be(0);
            maxX.Should().Be(5);
            maxY.Should().Be(4);
        }
    }
}
