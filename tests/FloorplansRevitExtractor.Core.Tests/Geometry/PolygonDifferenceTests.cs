using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Geometry
{
    public class PolygonDifferenceTests
    {
        private static readonly IReadOnlyList<IReadOnlyList<Point2D>> None = Array.Empty<IReadOnlyList<Point2D>>();

        private static IReadOnlyList<Point2D> Rect(double x0, double y0, double x1, double y1) =>
            new[] { new Point2D(x0, y0), new Point2D(x1, y0), new Point2D(x1, y1), new Point2D(x0, y1) };

        private static double Area(IReadOnlyList<PolygonWithHoles> pieces)
        {
            double total = 0;
            foreach (PolygonWithHoles p in pieces)
            {
                total += PolygonGeometry.NetArea(p.Outer, p.Holes);
            }

            return total;
        }

        [Fact]
        public void Subtract_SlabOverPartOfTheRoom_LeavesTheOpenPart()
        {
            // A two-storey room with a mezzanine over its left 6 m.
            IReadOnlyList<PolygonWithHoles> open = PolygonDifference.Subtract(Rect(0, 0, 10, 10), None, new[] { Rect(-1, -1, 6, 11) }, None);

            open.Should().ContainSingle();
            Area(open).Should().BeApproximately(40, 1e-6);
        }

        [Fact]
        public void Subtract_SlabOpeningOverTheRoom_IsOpen()
        {
            // The floor above covers everything but an opening over the room's middle.
            IReadOnlyList<PolygonWithHoles> open = PolygonDifference.Subtract(
                Rect(0, 0, 10, 10), None, new[] { Rect(-5, -5, 15, 15) }, new[] { Rect(2, 2, 8, 6) });

            Area(open).Should().BeApproximately(24, 1e-6);
        }

        [Fact]
        public void Subtract_RoomUnderASlab_LeavesNothing_SliversIncluded()
        {
            // The slab stops 2 cm short of the room's finish line: a sliver, not an opening.
            IReadOnlyList<PolygonWithHoles> open = PolygonDifference.Subtract(Rect(0, 0, 10, 10), None, new[] { Rect(0, 0, 10, 9.98) }, None);

            open.Should().BeEmpty();
        }

        [Fact]
        public void Subtract_RoomHolesStayHoles()
        {
            // A pillar hole in the room, no slab: the open part keeps the hole.
            IReadOnlyList<PolygonWithHoles> open = PolygonDifference.Subtract(Rect(0, 0, 10, 10), new[] { Rect(4, 4, 5, 5) }, None, None);

            open.Should().ContainSingle().Which.Holes.Should().ContainSingle();
            Area(open).Should().BeApproximately(99, 1e-6);
        }
    }
}
