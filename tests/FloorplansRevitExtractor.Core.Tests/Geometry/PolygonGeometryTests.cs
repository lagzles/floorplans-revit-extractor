using System;
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

        [Theory]
        [InlineData(2.0, 1.5, true)]
        [InlineData(5.0, 1.5, false)]
        [InlineData(-0.1, 1.5, false)]
        [InlineData(3.9, 2.9, true)]
        public void Contains_PointInsideOrOutsideRectangle(double x, double y, bool expected)
        {
            PolygonGeometry.Contains(Rectangle4x3, new Point2D(x, y)).Should().Be(expected);
        }

        [Fact]
        public void Contains_PointInTheNotchOfAnLShape_IsOutside()
        {
            IReadOnlyList<Point2D> lShape = new[]
            {
                new Point2D(0, 0), new Point2D(5, 0), new Point2D(5, 2),
                new Point2D(2, 2), new Point2D(2, 4), new Point2D(0, 4),
            };

            PolygonGeometry.Contains(lShape, new Point2D(4, 3)).Should().BeFalse();
            PolygonGeometry.Contains(lShape, new Point2D(1, 3)).Should().BeTrue();
        }

        [Fact]
        public void InteriorPoint_UShape_IsInsideEvenThoughTheCentroidIsNot()
        {
            IReadOnlyList<Point2D> u = new[]
            {
                new Point2D(2, 2), new Point2D(18, 2), new Point2D(18, 18), new Point2D(14, 18),
                new Point2D(14, 6), new Point2D(6, 6), new Point2D(6, 18), new Point2D(2, 18),
            };

            PolygonGeometry.Contains(u, PolygonGeometry.Centroid(u)).Should().BeFalse("the premise of this test");
            PolygonGeometry.Contains(u, PolygonGeometry.InteriorPoint(u)).Should().BeTrue();
        }

        [Fact]
        public void InteriorPoint_ThinRotatedSliver_IsInside()
        {
            IReadOnlyList<Point2D> sliver = new[] { new Point2D(0, 0), new Point2D(10, 7), new Point2D(10.01, 7.02) };

            PolygonGeometry.Contains(sliver, PolygonGeometry.InteriorPoint(sliver)).Should().BeTrue();
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

        [Fact]
        public void NetCentroid_MovesAwayFromAHole()
        {
            // 10 x 10 square with a 2 x 2 pillar hole in its left half.
            Point2D[] outer = { new Point2D(0, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(0, 10) };
            Point2D[] hole = { new Point2D(2, 4), new Point2D(4, 4), new Point2D(4, 6), new Point2D(2, 6) };

            Point2D c = PolygonGeometry.NetCentroid(outer, new[] { (IReadOnlyList<Point2D>)hole });

            // (100 * 5 - 4 * 3) / 96 on X; Y stays 5 by symmetry.
            c.X.Should().BeApproximately(488.0 / 96.0, 1e-12);
            c.Y.Should().BeApproximately(5.0, 1e-12);
        }

        [Fact]
        public void NetCentroid_WithoutHoles_IsTheCentroid()
        {
            Point2D[] outer = { new Point2D(0, 0), new Point2D(4, 0), new Point2D(4, 2), new Point2D(0, 2) };

            PolygonGeometry.NetCentroid(outer, Array.Empty<IReadOnlyList<Point2D>>()).Should().Be(PolygonGeometry.Centroid(outer));
        }
    }
}
