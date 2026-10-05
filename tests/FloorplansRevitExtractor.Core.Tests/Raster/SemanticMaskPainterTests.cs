using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class SemanticMaskPainterTests
    {
        private static MaskElement Filled(StructureClass cls, double x0, double y0, double x1, double y1) =>
            MaskElement.Filled(cls, new[] { Rect(x0, y0, x1, y1) });

        private static byte At(SemanticMask mask, int x, int y) => mask[x, y];

        [Fact]
        public void Paint_NoElements_IsAllBackground()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(6, 4, Array.Empty<MaskElement>());

            mask.CountPixels(StructureClass.Background).Should().Be(24);
        }

        [Fact]
        public void Paint_WallOverlappingRoom_WallWins()
        {
            // Revit room boundaries can bleed a few cm into the wall.
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.Room, 0, 1, 10, 10),
            });

            At(mask, 5, 1).Should().Be((byte)StructureClass.Wall);
            At(mask, 5, 2).Should().Be((byte)StructureClass.Room);
        }

        [Fact]
        public void Paint_Furniture_OnlyOverRoomPixels()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 2, 8, 10),
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.Furniture, 6, 1, 10, 4), // cabinet pushed into the wall, past the room
            });

            At(mask, 7, 3).Should().Be((byte)StructureClass.Furniture);
            At(mask, 7, 1).Should().Be((byte)StructureClass.Wall);
            At(mask, 9, 3).Should().Be((byte)StructureClass.Background);
        }

        [Fact]
        public void Paint_Swing_DoesNotOverwriteFurnitureOrWall()
        {
            IReadOnlyList<Point2D> leaf = new[] { new Point2D(2, 0.5), new Point2D(2, 6.5) };

            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 2, 10, 10),
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.Furniture, 0, 5, 10, 7),
                MaskElement.Stroked(StructureClass.Swing, new[] { leaf }, 2.0),
            });

            At(mask, 1, 3).Should().Be((byte)StructureClass.Swing);
            At(mask, 1, 5).Should().Be((byte)StructureClass.Furniture);
            At(mask, 1, 1).Should().Be((byte)StructureClass.Wall);
        }

        [Fact]
        public void Paint_Pillar_IsPaintedOverTheWallItIsEmbeddedIn()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Pillar, 3, 0, 5, 3),
                Filled(StructureClass.Wall, 0, 0, 10, 2),
            });

            At(mask, 4, 1).Should().Be((byte)StructureClass.Pillar);
            At(mask, 6, 1).Should().Be((byte)StructureClass.Wall);
        }

        [Fact]
        public void Paint_DoorAndWindow_CutTheWall_DoorOverWindow()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(12, 4, new[]
            {
                Filled(StructureClass.Door, 5, 0, 8, 2),
                Filled(StructureClass.Window, 1, 0, 6, 2),
                Filled(StructureClass.Wall, 0, 0, 12, 2),
            });

            At(mask, 2, 1).Should().Be((byte)StructureClass.Window);
            At(mask, 5, 1).Should().Be((byte)StructureClass.Door);
            At(mask, 10, 1).Should().Be((byte)StructureClass.Wall);
        }

        [Fact]
        public void Paint_StairAndVoid_SitOverRoomAndUnderWall()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Wall, 0, 0, 10, 1),
                Filled(StructureClass.Void, 5, 0, 8, 5),
                Filled(StructureClass.Stair, 2, 0, 7, 5),
                Filled(StructureClass.Room, 0, 0, 10, 10),
            });

            At(mask, 3, 3).Should().Be((byte)StructureClass.Stair);
            At(mask, 6, 3).Should().Be((byte)StructureClass.Void);
            At(mask, 6, 0).Should().Be((byte)StructureClass.Wall);
            At(mask, 9, 3).Should().Be((byte)StructureClass.Room);
        }

        [Fact]
        public void Paint_ElementOrder_DoesNotChangeTheMask()
        {
            MaskElement[] elements =
            {
                Filled(StructureClass.Room, 0, 1, 20, 20),
                Filled(StructureClass.Wall, 0, 0, 20, 2),
                Filled(StructureClass.Door, 8, 0, 12, 2),
                Filled(StructureClass.Furniture, 3, 1, 7, 6),
                Filled(StructureClass.Pillar, 15, 0, 18, 3),
            };

            byte[] forward = SemanticMaskPainter.Paint(20, 20, elements).CopyPixels();
            byte[] reversed = SemanticMaskPainter.Paint(20, 20, elements.Reverse().ToArray()).CopyPixels();

            reversed.Should().Equal(forward);
        }

        [Fact]
        public void Paint_OverlappingFurniture_StaysFurnitureAndStillRespectsTheWall()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 2, 10, 10),
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.Furniture, 1, 1, 6, 5),
                Filled(StructureClass.Furniture, 4, 1, 9, 5),
            });

            mask.CountPixels(StructureClass.Furniture).Should().Be(8 * 3);
            At(mask, 5, 1).Should().Be((byte)StructureClass.Wall);
        }

        [Fact]
        public void Indexer_OutsideTheImage_Throws()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(4, 4, Array.Empty<MaskElement>());

            Action act = () => _ = mask[0, 4];

            act.Should().Throw<ArgumentOutOfRangeException>();
        }

        private static MaskElement Line(StructureClass cls, double x0, double y0, double x1, double y1) =>
            MaskElement.Stroked(cls, new[] { (IReadOnlyList<Point2D>)new[] { new Point2D(x0, y0), new Point2D(x1, y1) } }, 2.0);

        [Fact]
        public void Paint_GridCrossingAWall_LeavesTheWallIntact()
        {
            // Spec v3: painting the wall with the grid would teach the model to open the wall under every grid line.
            SemanticMask mask = SemanticMaskPainter.Paint(20, 10, new[]
            {
                Filled(StructureClass.Room, 0, 0, 20, 10),
                Filled(StructureClass.Wall, 8, 0, 10, 10),
                Line(StructureClass.Grid, 0, 5, 20, 5),
            });

            At(mask, 9, 5).Should().Be((byte)StructureClass.Wall);
            At(mask, 3, 5).Should().Be((byte)StructureClass.Grid);
        }

        [Fact]
        public void Paint_Annotation_GoesOverBackgroundAndRoomOnly()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(30, 10, new[]
            {
                Filled(StructureClass.Room, 10, 0, 30, 10),
                Filled(StructureClass.Stair, 12, 0, 14, 10),
                Filled(StructureClass.Void, 15, 0, 16, 10),
                Filled(StructureClass.Pillar, 17, 0, 18, 10),
                Filled(StructureClass.Furniture, 19, 0, 20, 10),
                Filled(StructureClass.Door, 21, 0, 22, 10),
                Line(StructureClass.Dimension, 0, 5, 30, 5),
            });

            At(mask, 3, 5).Should().Be((byte)StructureClass.Dimension, "over background");
            At(mask, 11, 5).Should().Be((byte)StructureClass.Dimension, "over room");
            new[] { 13, 15, 17, 19, 21 }.Select(x => At(mask, x, 5))
                .Should().Equal((byte)StructureClass.Stair, (byte)StructureClass.Void, (byte)StructureClass.Pillar, (byte)StructureClass.Furniture, (byte)StructureClass.Door);
        }

        [Fact]
        public void Paint_DimensionCrossingAGrid_StaysGrid()
        {
            // Spec v4: annotation in the generator's order (grid, dimension, text), first painted wins.
            SemanticMask mask = SemanticMaskPainter.Paint(20, 20, new[]
            {
                Line(StructureClass.Grid, 10, 0, 10, 20),
                Line(StructureClass.Dimension, 0, 10, 20, 10),
            });

            At(mask, 9, 9).Should().Be((byte)StructureClass.Grid);
            At(mask, 3, 9).Should().Be((byte)StructureClass.Dimension);
        }

        [Fact]
        public void Paint_TextTouchingADimension_StaysDimension()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(20, 20, new[]
            {
                Line(StructureClass.Dimension, 0, 10, 20, 10),
                Filled(StructureClass.Text, 4, 6, 12, 14),
            });

            At(mask, 8, 9).Should().Be((byte)StructureClass.Dimension);
            At(mask, 8, 7).Should().Be((byte)StructureClass.Text);
        }

        [Fact]
        public void Paint_TextOverFurniture_LeavesTheFurniture()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 0, 10, 10),
                Filled(StructureClass.Furniture, 0, 0, 5, 10),
                Filled(StructureClass.Text, 3, 4, 8, 6),
            });

            At(mask, 4, 5).Should().Be((byte)StructureClass.Furniture);
            At(mask, 6, 5).Should().Be((byte)StructureClass.Text);
        }

        [Fact]
        public void Paint_GuardrailAgainstAWall_LeavesTheWall()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 2, 10, 10),
                Filled(StructureClass.Wall, 0, 0, 4, 10),
                Line(StructureClass.LowWall, 0, 5, 10, 5),
            });

            At(mask, 2, 5).Should().Be((byte)StructureClass.Wall);
            At(mask, 7, 5).Should().Be((byte)StructureClass.LowWall);
        }

        [Fact]
        public void Paint_SinkOnACounter_IsFixture_AndFixtureStaysOffTheWall()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Room, 0, 2, 10, 10),
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.Furniture, 0, 1, 10, 5), // counter drawn as loose furniture
                Filled(StructureClass.Fixture, 3, 1, 6, 4),
            });

            At(mask, 4, 3).Should().Be((byte)StructureClass.Fixture);
            At(mask, 8, 3).Should().Be((byte)StructureClass.Furniture);
            At(mask, 4, 1).Should().Be((byte)StructureClass.Wall);
        }

        [Fact]
        public void Paint_PassageCutsTheWall_AndCurtainWallReplacesIt()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Wall, 0, 0, 10, 2),
                Filled(StructureClass.CurtainWall, 6, 0, 10, 2),
                Filled(StructureClass.Passage, 1, 0, 3, 2),
            });

            At(mask, 2, 1).Should().Be((byte)StructureClass.Passage);
            At(mask, 4, 1).Should().Be((byte)StructureClass.Wall);
            At(mask, 8, 1).Should().Be((byte)StructureClass.CurtainWall);
        }

        [Fact]
        public void Paint_BackgroundElement_IsRejected()
        {
            Action act = () => SemanticMaskPainter.Paint(4, 4, new[] { Filled(StructureClass.Background, 0, 0, 2, 2) });

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void CopyPixels_ReturnsADetachedCopy()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(4, 4, new[] { Filled(StructureClass.Room, 0, 0, 4, 4) });

            byte[] copy = mask.CopyPixels();
            copy[0] = 0;

            At(mask, 0, 0).Should().Be((byte)StructureClass.Room);
        }
    }
}
