using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class ScanlineFillerTests
    {
        [Fact]
        public void Fill_AxisAlignedRectangle_CoversExactlyItsPixels()
        {
            Coverage coverage = ScanlineFiller.Fill(10, 10, new[] { Rect(2, 3, 6, 6) });

            coverage.Count.Should().Be(12);
            coverage[2, 3].Should().BeTrue();
            coverage[5, 5].Should().BeTrue();
            coverage[6, 5].Should().BeFalse();
            coverage[5, 6].Should().BeFalse();
            coverage[1, 3].Should().BeFalse();
        }

        [Fact]
        public void Fill_EdgeExactlyOnPixelCenter_IncludesLeftTopExcludesRightBottom()
        {
            // Pixel centers at 0.5 and 1.5 are inside [0.5, 2.5); 2.5 is not.
            Coverage coverage = ScanlineFiller.Fill(5, 5, new[] { Rect(0.5, 0.5, 2.5, 2.5) });

            coverage.Count.Should().Be(4);
            coverage[0, 0].Should().BeTrue();
            coverage[1, 1].Should().BeTrue();
            coverage[2, 1].Should().BeFalse();
            coverage[1, 2].Should().BeFalse();
        }

        [Fact]
        public void Fill_AdjacentPolygonsSharingAnEdge_NeitherOverlapNorLeaveAGap()
        {
            // A diagonal shared edge is the case where naive rules double-paint or skip pixels.
            IReadOnlyList<Point2D> left = new[] { new Point2D(0, 0), new Point2D(7.3, 0), new Point2D(3.1, 10), new Point2D(0, 10) };
            IReadOnlyList<Point2D> right = new[] { new Point2D(7.3, 0), new Point2D(10, 0), new Point2D(10, 10), new Point2D(3.1, 10) };
            var none = Array.Empty<IReadOnlyList<Point2D>>();

            Coverage a = ScanlineFiller.Fill(10, 10, new[] { new PolygonShape(left, none) });
            Coverage b = ScanlineFiller.Fill(10, 10, new[] { new PolygonShape(right, none) });

            for (int y = 0; y < 10; y++)
            {
                for (int x = 0; x < 10; x++)
                {
                    (a[x, y] ^ b[x, y]).Should().BeTrue($"pixel ({x},{y}) must belong to exactly one polygon");
                }
            }
        }

        [Fact]
        public void Fill_PolygonWithHole_LeavesTheHoleEmpty()
        {
            var shape = new PolygonShape(RectRing(0, 0, 10, 8), new[] { RectRing(4, 3, 6, 5) });

            Coverage coverage = ScanlineFiller.Fill(12, 12, new[] { shape });

            coverage.Count.Should().Be(76);
            coverage[4, 3].Should().BeFalse();
            coverage[5, 4].Should().BeFalse();
            coverage[3, 3].Should().BeTrue();
        }

        [Fact]
        public void Fill_RotatedAndConcavePolygons_MatchPixelCenterOracle()
        {
            var none = Array.Empty<IReadOnlyList<Point2D>>();
            var shapes = new[]
            {
                // 30-degree rotated rectangle, like a wall in a rotated view.
                new PolygonShape(new[] { new Point2D(3.2, 1.1), new Point2D(17.9, 9.6), new Point2D(15.4, 13.9), new Point2D(0.7, 5.4) }, none),

                // L-shaped pillar.
                new PolygonShape(new[] { new Point2D(2, 2), new Point2D(14.5, 2), new Point2D(14.5, 6.2), new Point2D(6.7, 6.2), new Point2D(6.7, 17), new Point2D(2, 17) }, none),

                // Vertices exactly on pixel centers, joined by diagonal edges.
                new PolygonShape(new[] { new Point2D(10.5, 1.5), new Point2D(18.5, 9.5), new Point2D(10.5, 17.5), new Point2D(2.5, 9.5) }, none),

                // Duplicate and collinear vertices, as Revit boundary tessellation produces.
                new PolygonShape(new[] { new Point2D(1, 1), new Point2D(1, 1), new Point2D(6, 1), new Point2D(12.3, 1), new Point2D(12.3, 8.8), new Point2D(12.3, 8.8), new Point2D(1, 8.8) }, none),

                // Hole touching the outer ring.
                new PolygonShape(RectRing(0, 0, 16, 12), new[] { RectRing(0, 4, 5, 8) }),
            };

            foreach (PolygonShape shape in shapes)
            {
                Coverage coverage = ScanlineFiller.Fill(20, 20, new[] { shape });
                bool[] expected = BruteForceCoverage(20, 20, shape);

                for (int y = 0; y < 20; y++)
                {
                    for (int x = 0; x < 20; x++)
                    {
                        coverage[x, y].Should().Be(expected[(y * 20) + x], $"pixel ({x},{y})");
                    }
                }
            }
        }

        [Fact]
        public void Fill_WindingDirection_DoesNotChangeResult()
        {
            var none = Array.Empty<IReadOnlyList<Point2D>>();
            IReadOnlyList<Point2D> ccw = RectRing(1, 1, 5, 4);
            IReadOnlyList<Point2D> cw = new[] { ccw[0], ccw[3], ccw[2], ccw[1] };

            Coverage a = ScanlineFiller.Fill(8, 8, new[] { new PolygonShape(ccw, none) });
            Coverage b = ScanlineFiller.Fill(8, 8, new[] { new PolygonShape(cw, none) });

            b.Count.Should().Be(a.Count).And.Be(12);
        }

        [Fact]
        public void Fill_OverlappingSeparateShapes_AreUnitedNotCancelled()
        {
            Coverage coverage = ScanlineFiller.Fill(10, 10, new[] { Rect(0, 0, 4, 4), Rect(2, 2, 6, 6) });

            coverage.Count.Should().Be(16 + 16 - 4);
            coverage[3, 3].Should().BeTrue();
        }

        [Fact]
        public void Fill_PolygonPartlyOutsideImage_IsClippedWithoutError()
        {
            Coverage coverage = ScanlineFiller.Fill(4, 4, new[] { Rect(-3, -3, 2, 10) });

            coverage.Count.Should().Be(8);
        }

        [Fact]
        public void Fill_PolygonEntirelyOutsideImage_CoversNothing()
        {
            ScanlineFiller.Fill(10, 10, new[] { Rect(12, 12, 20, 20), Rect(-9, -9, -1, -1) }).Count.Should().Be(0);
        }

        [Theory]
        [InlineData(3e9, 4e9)]
        [InlineData(-4e9, -3e9)]
        public void Fill_CoordinatesBeyondIntRange_CoverNothing(double from, double to)
        {
            // Casting these to int without clamping first wraps around and fills whole rows.
            ScanlineFiller.Fill(10, 10, new[] { Rect(from, 2, to, 5), Rect(2, from, 5, to) }).Count.Should().Be(0);
        }

        [Fact]
        public void Fill_PolygonSpanningFarBeyondTheImage_CoversEveryPixel()
        {
            ScanlineFiller.Fill(10, 10, new[] { Rect(-1e12, -1e12, 1e12, 1e12) }).Count.Should().Be(100);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Fill_NonFiniteCoordinate_Throws(double bad)
        {
            // Filling anyway would put pixels outside the polygon, silently.
            var none = Array.Empty<IReadOnlyList<Point2D>>();
            var shape = new PolygonShape(new[] { new Point2D(2, 2), new Point2D(4, 2), new Point2D(4, 4), new Point2D(bad, 4) }, none);

            Action act = () => ScanlineFiller.Fill(10, 10, new[] { shape });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void Indexer_OutsideTheImage_Throws()
        {
            Coverage coverage = ScanlineFiller.Fill(4, 4, new[] { Rect(0, 0, 4, 4) });

            Action act = () => _ = coverage[4, 0];

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void Fill_DegenerateRing_CoversNothing()
        {
            var none = Array.Empty<IReadOnlyList<Point2D>>();
            var flat = new PolygonShape(new[] { new Point2D(1, 1), new Point2D(5, 1), new Point2D(3, 1) }, none);

            ScanlineFiller.Fill(8, 8, new[] { flat }).Count.Should().Be(0);
        }

        [Fact]
        public void Fill_NonPositiveSize_Throws()
        {
            Action act = () => ScanlineFiller.Fill(0, 5, Array.Empty<PolygonShape>());

            act.Should().Throw<ArgumentOutOfRangeException>();
        }
    }
}
