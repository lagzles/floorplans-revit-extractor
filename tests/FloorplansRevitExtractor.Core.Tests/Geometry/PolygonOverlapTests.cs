using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class PolygonOverlapTests
    {
        private static IReadOnlyList<Point2D> Rect(double x0, double y0, double x1, double y1) =>
            new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };

        [Fact]
        public void SharedFraction_DisjointRooms_IsZero()
        {
            PolygonOverlap.SharedFraction(Rect(0, 0, 4, 4), new[] { Rect(5, 0, 9, 4) }).Should().Be(0);
        }

        [Fact]
        public void SharedFraction_RoomsSharingAWallFace_IsZero()
        {
            // Neighbours on the same floor touch along a line; that is not overlap.
            PolygonOverlap.SharedFraction(Rect(0, 0, 4, 4), new[] { Rect(4, 0, 8, 4) }).Should().Be(0);
        }

        [Fact]
        public void SharedFraction_DoubleHeightRoomUnderUpperRooms_IsTheCoveredShare()
        {
            // A 10 x 10 m double-height cafe; the upper floor has two 5 x 10 m rooms over half of it... and beyond.
            double shared = PolygonOverlap.SharedFraction(Rect(0, 0, 10, 10), new[] { Rect(5, 0, 10, 10), Rect(10, 0, 15, 10) });

            shared.Should().BeApproximately(0.5, 0.02);
        }

        [Fact]
        public void SharedFraction_NoOtherRooms_IsZero()
        {
            PolygonOverlap.SharedFraction(Rect(0, 0, 4, 4), new IReadOnlyList<Point2D>[0]).Should().Be(0);
        }
    }
}
