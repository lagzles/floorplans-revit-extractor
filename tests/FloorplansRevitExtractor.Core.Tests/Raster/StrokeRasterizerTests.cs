using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class StrokeRasterizerTests
    {
        [Fact]
        public void Stroke_HorizontalSegment_CoversCapsuleOfHalfWidthAroundIt()
        {
            // Width 2 around y=5: rows with centers 4.5 and 5.5. Round caps reach
            // the centers at x=1.5 and x=8.5 (distance 0.707 to the endpoints).
            IReadOnlyList<Point2D> line = new[] { new Point2D(2, 5), new Point2D(8, 5) };

            Coverage coverage = StrokeRasterizer.Stroke(12, 12, new[] { line }, 2.0);

            coverage.Count.Should().Be(16);
            coverage[1, 4].Should().BeTrue();
            coverage[8, 5].Should().BeTrue();
            coverage[0, 4].Should().BeFalse();
            coverage[9, 5].Should().BeFalse();
            coverage[4, 3].Should().BeFalse();
        }

        [Fact]
        public void Stroke_PolylineCorner_HasNoGapAtTheJoin()
        {
            IReadOnlyList<Point2D> corner = new[] { new Point2D(2, 2), new Point2D(10, 2), new Point2D(10, 10) };

            Coverage coverage = StrokeRasterizer.Stroke(14, 14, new[] { corner }, 2.0);

            coverage[9, 1].Should().BeTrue();
            coverage[10, 1].Should().BeTrue();
            coverage[10, 2].Should().BeTrue();
            coverage[9, 2].Should().BeTrue();
        }

        [Fact]
        public void Stroke_SinglePointPolyline_PaintsADot()
        {
            IReadOnlyList<Point2D> dot = new[] { new Point2D(5, 5) };

            StrokeRasterizer.Stroke(10, 10, new[] { dot }, 2.0).Count.Should().Be(4);
        }

        [Fact]
        public void Stroke_FarOutsideImage_CoversNothing()
        {
            IReadOnlyList<Point2D> far = new[] { new Point2D(3e9, 5), new Point2D(4e9, 5) };

            StrokeRasterizer.Stroke(10, 10, new[] { far }, 2.0).Count.Should().Be(0);
        }

        [Fact]
        public void Stroke_NonFiniteCoordinate_Throws()
        {
            IReadOnlyList<Point2D> bad = new[] { new Point2D(1, 1), new Point2D(double.NaN, 5) };

            Action act = () => StrokeRasterizer.Stroke(10, 10, new[] { bad }, 2.0);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Stroke_NonPositiveWidth_Throws()
        {
            Action act = () => StrokeRasterizer.Stroke(10, 10, Array.Empty<IReadOnlyList<Point2D>>(), 0);

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(48.0, 2)] // round(1.92)
        [InlineData(100.0, 4)]
        [InlineData(17.0, 2)] // round(0.68) = 1, floored to the 2 px minimum
        [InlineData(135.0, 5)] // round(5.4)
        [InlineData(62.5, 2)] // round(2.5) = 2: banker's rounding, same as Python round()
        [InlineData(87.5, 4)] // round(3.5) = 4
        public void WidthInPixels_MatchesGeneratorFormula(double pxPerM, int expected)
        {
            StrokeRasterizer.WidthInPixels(0.04, pxPerM).Should().Be(expected);
        }
    }
}
