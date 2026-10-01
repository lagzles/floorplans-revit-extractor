using System;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class PixelTransformTests
    {
        private const double Tolerance = 1e-9;

        // The example transform from the spec's geometry contract.
        private static readonly PixelTransform SpecExample = new PixelTransform(-2.35, 18.40, 48.0, 1520, 1210);

        [Fact]
        public void ToPixel_AppliesSpecFormula()
        {
            // p_x = (X - x0) * k ; p_y = (y1 - Y) * k
            Point2D px = SpecExample.ToPixel(new Point2D(0, 0));

            px.X.Should().BeApproximately(2.35 * 48, Tolerance);
            px.Y.Should().BeApproximately(18.40 * 48, Tolerance);
        }

        [Fact]
        public void ToPixel_ImageYGrowsDownWhileViewYGrowsUp()
        {
            Point2D lower = SpecExample.ToPixel(new Point2D(1, 2));
            Point2D higher = SpecExample.ToPixel(new Point2D(1, 3));

            (higher.Y - lower.Y).Should().BeApproximately(-48, Tolerance);
        }

        [Fact]
        public void ToPixel_TopLeftCornerOfRegion_IsPixelOrigin()
        {
            Point2D px = SpecExample.ToPixel(new Point2D(-2.35, 18.40));

            px.X.Should().BeApproximately(0, Tolerance);
            px.Y.Should().BeApproximately(0, Tolerance);
        }

        [Fact]
        public void ToView_IsTheInverseOfToPixel()
        {
            var p = new Point2D(4.321, 7.654);

            Point2D back = SpecExample.ToView(SpecExample.ToPixel(p));

            back.X.Should().BeApproximately(p.X, Tolerance);
            back.Y.Should().BeApproximately(p.Y, Tolerance);
        }

        [Fact]
        public void Measure_TakesPxPerMeterFromTheExportedFileNotTheRequest()
        {
            // Requested 48 px/m over 31.5 x 25.2 m, but the exported PNG is 1520 x 1216 px.
            PixelTransform t = PixelTransform.Measure(-2.35, -6.8, 29.15, 18.40, 1520, 1216);

            t.PxPerM.Should().BeApproximately(1520 / 31.5, Tolerance);
            t.X0.Should().Be(-2.35);
            t.Y1.Should().Be(18.40);
            t.Width.Should().Be(1520);
            t.Height.Should().Be(1216);
        }

        [Theory]
        [InlineData(1215)] // rounding: within 1 px of 1216
        [InlineData(1217)]
        public void Measure_HeightWithinOnePixel_IsAccepted(int heightPx)
        {
            PixelTransform.Measure(-2.35, -6.8, 29.15, 18.40, 1520, heightPx).Height.Should().Be(heightPx);
        }

        [Theory]
        [InlineData(1100)] // a different aspect or margin: masks would drift vertically
        [InlineData(1300)]
        public void Measure_HeightThatDisagreesWithTheWidthScale_Throws(int heightPx)
        {
            // Region is 31.5 x 25.2 m; at 1520/31.5 px/m that is 1216 px tall.
            Action act = () => PixelTransform.Measure(-2.35, -6.8, 29.15, 18.40, 1520, heightPx);

            act.Should().Throw<ArgumentException>().WithMessage("*height*");
        }

        [Fact]
        public void Measure_EmptyRegion_Throws()
        {
            Action act = () => PixelTransform.Measure(5, 0, 5, 10, 100, 100);

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(0.0, 100, 100)]
        [InlineData(-3.0, 100, 100)]
        [InlineData(double.NaN, 100, 100)]
        [InlineData(48.0, 0, 100)]
        [InlineData(48.0, 100, -1)]
        public void Constructor_InvalidScaleOrSize_Throws(double pxPerM, int width, int height)
        {
            Action act = () => _ = new PixelTransform(0, 0, pxPerM, width, height);

            act.Should().Throw<ArgumentException>();
        }
    }
}
