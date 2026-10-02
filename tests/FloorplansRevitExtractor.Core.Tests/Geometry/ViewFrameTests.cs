using System;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class ViewFrameTests
    {
        private const double Tolerance = 1e-9;

        [Fact]
        public void ToView_IdentityFrame_KeepsCoordinates()
        {
            var frame = new ViewFrame(new Point2D(0, 0), new Vector2D(1, 0), new Vector2D(0, 1));

            Point2D v = frame.ToView(new Point2D(3.5, -2));

            v.X.Should().BeApproximately(3.5, Tolerance);
            v.Y.Should().BeApproximately(-2, Tolerance);
        }

        [Fact]
        public void ToView_TranslatedFrame_SubtractsOrigin()
        {
            var frame = new ViewFrame(new Point2D(10, 20), new Vector2D(1, 0), new Vector2D(0, 1));

            Point2D v = frame.ToView(new Point2D(12, 25));

            v.X.Should().BeApproximately(2, Tolerance);
            v.Y.Should().BeApproximately(5, Tolerance);
        }

        [Fact]
        public void ToView_FrameRotated30Degrees_AlignsRotatedWallWithViewAxes()
        {
            // A wall running at 30 degrees in the model runs along the view's X axis.
            double c = Math.Cos(Math.PI / 6);
            double s = Math.Sin(Math.PI / 6);
            var frame = new ViewFrame(new Point2D(1, 1), new Vector2D(c, s), new Vector2D(-s, c));

            Point2D wallEnd = frame.ToView(new Point2D(1 + (4 * c), 1 + (4 * s)));

            wallEnd.X.Should().BeApproximately(4, Tolerance);
            wallEnd.Y.Should().BeApproximately(0, Tolerance);
        }

        [Fact]
        public void ToViewDirection_RotatesWithoutTheOriginShift()
        {
            double c = Math.Cos(Math.PI / 6);
            double s = Math.Sin(Math.PI / 6);
            var frame = new ViewFrame(new Point2D(100, -50), new Vector2D(c, s), new Vector2D(-s, c));

            Vector2D along = frame.ToViewDirection(new Vector2D(c, s));

            along.X.Should().BeApproximately(1, Tolerance);
            along.Y.Should().BeApproximately(0, Tolerance);
        }

        [Fact]
        public void ToModel_IsTheInverseOfToView()
        {
            double c = Math.Cos(1.1);
            double s = Math.Sin(1.1);
            var frame = new ViewFrame(new Point2D(-7.2, 3.3), new Vector2D(c, s), new Vector2D(-s, c));
            var p = new Point2D(12.345, -6.789);

            Point2D back = frame.ToModel(frame.ToView(p));

            back.X.Should().BeApproximately(p.X, Tolerance);
            back.Y.Should().BeApproximately(p.Y, Tolerance);
        }

        [Fact]
        public void Constructor_MirroredBasis_Throws()
        {
            // BasisX x BasisY pointing down would flip the plan left-right, silently.
            Action act = () => _ = new ViewFrame(new Point2D(0, 0), new Vector2D(1, 0), new Vector2D(0, -1));

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Constructor_NonFiniteOrigin_Throws()
        {
            Action act = () => _ = new ViewFrame(new Point2D(double.NaN, 0), new Vector2D(1, 0), new Vector2D(0, 1));

            act.Should().Throw<ArgumentException>();
        }

        [Theory]
        [InlineData(2, 0, 0, 1)] // not unit length
        [InlineData(1, 0, 0.3, 0.95)] // not perpendicular
        [InlineData(double.NaN, 0, 0, 1)]
        public void Constructor_NonOrthonormalOrNonFiniteBasis_Throws(double xx, double xy, double yx, double yy)
        {
            Action act = () => _ = new ViewFrame(new Point2D(0, 0), new Vector2D(xx, xy), new Vector2D(yx, yy));

            act.Should().Throw<ArgumentException>();
        }
    }
}
