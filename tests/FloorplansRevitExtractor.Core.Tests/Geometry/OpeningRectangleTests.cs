using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class OpeningRectangleTests
    {
        private const double Tolerance = 1e-9;

        [Fact]
        public void Build_AxisAlignedDoor_IsWidthByThicknessCenteredOnInsertion()
        {
            IReadOnlyList<Point2D> rect = OpeningRectangle.Build(new Point2D(1.0, 2.0), new Vector2D(1, 0), 0.9, 0.15);

            rect.Should().HaveCount(4);
            rect.Min(p => p.X).Should().BeApproximately(0.55, Tolerance);
            rect.Max(p => p.X).Should().BeApproximately(1.45, Tolerance);
            rect.Min(p => p.Y).Should().BeApproximately(1.925, Tolerance);
            rect.Max(p => p.Y).Should().BeApproximately(2.075, Tolerance);
        }

        [Fact]
        public void Build_WallAt90Degrees_TurnsTheLongSideAlongY()
        {
            IReadOnlyList<Point2D> rect = OpeningRectangle.Build(new Point2D(0, 0), new Vector2D(0, 1), 1.2, 0.2);

            (rect.Max(p => p.X) - rect.Min(p => p.X)).Should().BeApproximately(0.2, Tolerance);
            (rect.Max(p => p.Y) - rect.Min(p => p.Y)).Should().BeApproximately(1.2, Tolerance);
        }

        [Fact]
        public void Build_RotatedWall_LongSideIsExactlyTheOpeningWidth_NoExtendedEnds()
        {
            // Spec: long side of the blob / true width = 0.999 in the synthetic set; an
            // extended end would decalibrate the vectorizer again.
            IReadOnlyList<Point2D> rect = OpeningRectangle.Build(new Point2D(3, 4), new Vector2D(3, 4), 0.8, 0.14);

            Distance(rect[0], rect[1]).Should().BeApproximately(0.8, Tolerance);
            Distance(rect[1], rect[2]).Should().BeApproximately(0.14, Tolerance);
            PolygonGeometry.Area(rect).Should().BeApproximately(0.8 * 0.14, Tolerance);
            PolygonGeometry.Centroid(rect).X.Should().BeApproximately(3, Tolerance);
            PolygonGeometry.Centroid(rect).Y.Should().BeApproximately(4, Tolerance);
        }

        [Fact]
        public void Build_IsCounterClockwise()
        {
            IReadOnlyList<Point2D> rect = OpeningRectangle.Build(new Point2D(0, 0), new Vector2D(-1, 0), 1, 0.1);

            PolygonGeometry.SignedArea(rect).Should().BePositive();
        }

        [Theory]
        [InlineData(0, 0, 0.9, 0.15)]
        [InlineData(1, 0, 0, 0.15)]
        [InlineData(1, 0, 0.9, -0.1)]
        [InlineData(double.NaN, 0, 0.9, 0.15)]
        public void Build_InvalidInput_Throws(double dx, double dy, double width, double thickness)
        {
            Action act = () => OpeningRectangle.Build(new Point2D(0, 0), new Vector2D(dx, dy), width, thickness);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(double.NaN, 0, 0.9, 0.15)]
        [InlineData(0, double.PositiveInfinity, 0.9, 0.15)]
        [InlineData(0, 0, double.PositiveInfinity, 0.15)]
        [InlineData(0, 0, 0.9, double.PositiveInfinity)]
        public void Build_NonFiniteCenterOrSize_Throws(double cx, double cy, double width, double thickness)
        {
            Action act = () => OpeningRectangle.Build(new Point2D(cx, cy), new Vector2D(1, 0), width, thickness);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Build_HugeDirectionVector_IsNormalizedWithoutOverflow()
        {
            // |(1e200, 1e200)| overflows to infinity if squared directly.
            IReadOnlyList<Point2D> rect = OpeningRectangle.Build(new Point2D(0, 0), new Vector2D(1e200, 1e200), 1.0, 0.1);

            PolygonGeometry.Area(rect).Should().BeApproximately(0.1, Tolerance);
        }

        private static double Distance(Point2D a, Point2D b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }
}
