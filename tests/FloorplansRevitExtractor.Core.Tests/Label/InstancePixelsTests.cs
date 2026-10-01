using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class InstancePixelsTests
    {
        // 10 px/m; view region x in [0, 20] m, y in [0, 10] m -> 200 x 100 px.
        private static readonly PixelTransform Transform = new PixelTransform(0, 10, 10, 200, 100);

        private static InstanceRoom Room(int id, string? type, params (double X, double Y)[] meters) =>
            new InstanceRoom(id, type, $"ROOM {id}", type == null ? (int?)null : 6, meters.Select(p => new Point2D(p.X, p.Y)).ToArray());

        private static InstanceMask PaintRooms(params InstanceRoom[] rooms) => InstanceMaskPainter.Paint(
            Transform.Width,
            Transform.Height,
            rooms.Select(r => new RoomInstance(r.RoomId, new[]
            {
                new PolygonShape(r.PolygonM.Select(Transform.ToPixel).ToArray(), Array.Empty<IReadOnlyList<Point2D>>()),
            })).ToArray());

        [Fact]
        public void Build_RectangularRoom_FillsEveryFieldLikeTheGenerator()
        {
            // Room x in [2, 6] m, y in [3, 7] m -> pixels x 20..59, y 30..69.
            InstanceRoom room = Room(0, "bedroom", (2, 3), (6, 3), (6, 7), (2, 7));
            InstanceMask mask = PaintRooms(room);

            InstancePx px = InstancePixels.Build(mask, Transform, new[] { room }).Single();

            px.InstanceId.Should().Be(1);
            px.RoomId.Should().Be(0);
            px.Type.Should().Be("bedroom");
            px.Name.Should().Be("ROOM 0");
            px.ClassId.Should().Be(6);
            px.BBoxPx.Should().Be((20, 30, 59, 69));
            px.BBoxCoco.Should().Be((20, 30, 40, 40), "width and height are inclusive: x1 - x0 + 1");
            px.BBoxYolo.Should().Be((0.1975, 0.495, 0.2, 0.4));
            px.CentroidPx.Should().Be(new Point2D(39.5, 49.5));
            px.AreaPx.Should().Be(1600);
            px.PolygonPx.Should().Equal(new Point2D(20, 70), new Point2D(60, 70), new Point2D(60, 30), new Point2D(20, 30));
        }

        [Fact]
        public void Build_CenterOfASquare_IsItsRoundedCentroid()
        {
            InstanceRoom room = Room(0, "bedroom", (2, 3), (6, 3), (6, 7), (2, 7));

            InstancePx px = InstancePixels.Build(PaintRooms(room), Transform, new[] { room }).Single();

            // mean 39.5 / 49.5 rounds half to even, like Python round().
            px.CenterPx.Should().Be((40, 50));
        }

        [Fact]
        public void Build_CenterOfAnLShapedRoom_FallsBackToTheInnermostPixel()
        {
            // The centroid of this L sits in the corner region near the walls.
            InstanceRoom room = Room(0, "living_room", (0, 0), (10, 0), (10, 2), (2, 2), (2, 10), (0, 10));
            InstanceMask mask = PaintRooms(room);

            InstancePx px = InstancePixels.Build(mask, Transform, new[] { room }).Single();

            mask[px.CenterPx.X, px.CenterPx.Y].Should().Be(1, "the label anchor is always inside the room");
            px.CenterPx.Should().NotBe(((int)Math.Round(px.CentroidPx.X), (int)Math.Round(px.CentroidPx.Y)));
        }

        [Fact]
        public void Build_RoomWithNoPixels_IsSkippedLikeTheGenerator()
        {
            InstanceRoom painted = Room(0, "bedroom", (2, 3), (6, 3), (6, 7), (2, 7));
            InstanceRoom sliver = Room(1, "closet", (10, 3), (10.01, 3), (10.01, 7), (10, 7));

            IReadOnlyList<InstancePx> result = InstancePixels.Build(PaintRooms(painted, sliver), Transform, new[] { painted, sliver });

            result.Select(i => i.RoomId).Should().Equal(0);
        }

        [Fact]
        public void Build_ResultIsOrderedByInstanceId()
        {
            InstanceRoom a = Room(3, "bedroom", (2, 3), (6, 3), (6, 7), (2, 7));
            InstanceRoom b = Room(1, "bathroom", (8, 3), (12, 3), (12, 7), (8, 7));

            IReadOnlyList<InstancePx> result = InstancePixels.Build(PaintRooms(a, b), Transform, new[] { a, b });

            result.Select(i => i.InstanceId).Should().Equal(2, 4);
        }

        [Fact]
        public void Build_PolygonPx_IsRoundedToTwoDecimals()
        {
            InstanceRoom room = Room(0, "bedroom", (2.12345, 3), (6, 3), (6, 7), (2.12345, 7));

            InstancePx px = InstancePixels.Build(PaintRooms(room), Transform, new[] { room }).Single();

            px.PolygonPx[0].X.Should().Be(21.23);
        }
    }

    public class LabelPointTests
    {
        [Fact]
        public void Find_CentroidOnTheEdgeOfAThinArm_UsesTheInnermostPixelInstead()
        {
            // A U shape: the pixel centroid lands in the gap between the arms.
            int w = 30;
            int h = 20;
            var mask = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool leftArm = x < 6;
                    bool rightArm = x >= 24;
                    bool bottom = y >= 14;
                    mask[(y * w) + x] = leftArm || rightArm || bottom;
                }
            }

            (int X, int Y) p = LabelPoint.Find(mask, w, h);

            mask[(p.Y * w) + p.X].Should().BeTrue();
        }

        [Fact]
        public void Find_CropOffset_RoundsTheCentroidInImageCoordinates()
        {
            // Found against the generator: crop mean 3.5 rounds to 4, but the image
            // mean 310.5 rounds to 310. Half-to-even depends on the offset's parity.
            var mask = new bool[8 * 25];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = true;
            }

            (int X, int Y) p = LabelPoint.Find(mask, 8, 25, originX: 307, originY: 302);

            (p.X + 307).Should().Be(310);
            (p.Y + 302).Should().Be(314);
        }

        [Fact]
        public void Find_HalfPixelCentroid_RoundsHalfToEvenLikePython()
        {
            // A 2x4 bar whose centroid (1.5 -> 2, 0.5 -> 0) is inside with clearance.
            bool[] mask = { true, true, true, true, true, true, true, true };

            LabelPoint.Find(mask, 4, 2).Should().Be((2, 0));
        }
    }
}
