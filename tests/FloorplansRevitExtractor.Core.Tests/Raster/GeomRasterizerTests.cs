using System;
using System.Collections.Generic;
using System.IO;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Json;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Json.GeomJsonTests;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class GeomRasterizerTests
    {
        // 10 px/m, region x in [0, 10] m, y in [0, 8] m -> a 100 x 80 image.
        private static readonly PixelTransform TenPxPerM = new PixelTransform(0, 8, 10, 100, 80);

        private static GeomDocument Doc(params GeomElement[] elements) => new GeomDocument
        {
            View = new GeomView(1, "V", 100, 1.2),
            Transform = TenPxPerM,
            Elements = elements,
            Warnings = Array.Empty<GeomWarning>(),
        };

        [Fact]
        public void PaintSemantic_WallInMeters_LandsOnTheExpectedPixels()
        {
            // Wall from (1, 7) to (6, 7.2) m -> x in [10, 60) px, y in [8, 10) px.
            SemanticMask mask = GeomRasterizer.PaintSemantic(Doc(
                GeomElement.Filled(StructureClass.Wall, 1, new[] { Ring((1, 7), (6, 7), (6, 7.2), (1, 7.2)) })));

            mask.Width.Should().Be(100);
            mask.Height.Should().Be(80);
            mask.CountPixels(StructureClass.Wall).Should().Be(50 * 2);
            mask[10, 8].Should().Be((byte)StructureClass.Wall);
            mask[59, 9].Should().Be((byte)StructureClass.Wall);
            mask[60, 9].Should().Be((byte)StructureClass.Background);
            mask[10, 10].Should().Be((byte)StructureClass.Background);
        }

        [Fact]
        public void ToMaskElements_SwingStrokeWidth_UsesTheGeneratorFormulaAtThisScale()
        {
            // 0.04 m at 10 px/m = 0.4 px -> raised to the 2 px minimum.
            IReadOnlyList<MaskElement> elements = GeomRasterizer.ToMaskElements(Doc(
                GeomElement.Stroked(StructureClass.Swing, 1, new[] { Ring((1, 1), (2, 2)) }, 0.04)));

            elements[0].StrokeWidthPixels.Should().Be(2);
            elements[0].Polylines[0][1].Should().Be(new Point2D(20, 60));
        }

        [Fact]
        public void ToMaskElements_HoleGoesToThePolygonThatContainsIt()
        {
            // Two floor slabs in one element; the shaft hole is inside the second one only.
            IReadOnlyList<MaskElement> elements = GeomRasterizer.ToMaskElements(Doc(
                GeomElement.Filled(
                    StructureClass.Room,
                    1,
                    new[] { Ring((0, 0), (3, 0), (3, 3), (0, 3)), Ring((5, 0), (9, 0), (9, 4), (5, 4)) },
                    new[] { Ring((6, 1), (7, 1), (7, 2), (6, 2)) })));

            elements[0].Polygons[0].Holes.Should().BeEmpty();
            elements[0].Polygons[1].Holes.Should().HaveCount(1);
        }

        [Fact]
        public void PaintSemantic_IslandInsideAHole_IsTheSameWhateverThePolygonOrder()
        {
            // Outer 10x10 m slab, 6x6 m hole, 2x2 m island inside the hole.
            IReadOnlyList<Point2D> outer = Ring((0, 0), (10, 0), (10, 10), (0, 10));
            IReadOnlyList<Point2D> island = Ring((4, 4), (6, 4), (6, 6), (4, 6));
            IReadOnlyList<Point2D> hole = Ring((2, 2), (8, 2), (8, 8), (2, 8));
            var transform = new PixelTransform(0, 10, 10, 100, 100);

            SemanticMask Paint(params IReadOnlyList<Point2D>[] polygons) => GeomRasterizer.PaintSemantic(Doc(
                GeomElement.Filled(StructureClass.Room, 1, polygons, new[] { hole })) with { Transform = transform });

            SemanticMask outerFirst = Paint(outer, island);
            SemanticMask islandFirst = Paint(island, outer);

            outerFirst.CountPixels(StructureClass.Room).Should().Be(10000 - 3600 + 400);
            islandFirst.CopyPixels().Should().Equal(outerFirst.CopyPixels());
        }

        [Fact]
        public void PaintSemantic_UShapedHoleWithIslandInItsNotch_GoesToTheOuterPolygon()
        {
            // The U-hole's centroid falls in its own notch, where an island sits.
            IReadOnlyList<Point2D> outer = Ring((0, 0), (20, 0), (20, 20), (0, 20));
            IReadOnlyList<Point2D> uHole = Ring((2, 2), (18, 2), (18, 18), (14, 18), (14, 6), (6, 6), (6, 18), (2, 18));
            IReadOnlyList<Point2D> notchIsland = Ring((8, 8), (12, 8), (12, 12), (8, 12));
            var transform = new PixelTransform(0, 20, 5, 100, 100);

            SemanticMask mask = GeomRasterizer.PaintSemantic(Doc(
                GeomElement.Filled(StructureClass.Room, 1, new[] { notchIsland, outer }, new[] { uHole })) with { Transform = transform });

            Point2D insideHole = transform.ToPixel(new Point2D(4, 10));
            Point2D onIsland = transform.ToPixel(new Point2D(10, 10));
            mask[(int)insideHole.X, (int)insideHole.Y].Should().Be((byte)StructureClass.Background);
            mask[(int)onIsland.X, (int)onIsland.Y].Should().Be((byte)StructureClass.Room);
        }

        [Fact]
        public void ToMaskElements_HoleOutsideEveryPolygon_Throws()
        {
            // Attached anyway, the even-odd rule would paint it as solid area.
            Action act = () => GeomRasterizer.ToMaskElements(Doc(
                GeomElement.Filled(
                    StructureClass.Room,
                    42,
                    new[] { Ring((0, 0), (3, 0), (3, 3), (0, 3)) },
                    new[] { Ring((6, 1), (7, 1), (7, 2), (6, 2)) })));

            act.Should().Throw<ArgumentException>().WithMessage("*42*");
        }

        [Fact]
        public void PaintSemantic_FromFile_EqualsPaintSemantic_InMemory()
        {
            GeomDocument original = SpecExample();
            using var stream = new MemoryStream();
            GeomJsonWriter.Write(stream, original);
            stream.Position = 0;

            byte[] fromMemory = GeomRasterizer.PaintSemantic(original).CopyPixels();
            byte[] fromFile = GeomRasterizer.PaintSemantic(GeomJsonReader.Read(stream)).CopyPixels();

            fromFile.Should().Equal(fromMemory);
        }

        [Fact]
        public void PaintSemantic_SpecExample_DoorCutsTheWallAndSwingStaysOnTheFloor()
        {
            SemanticMask mask = GeomRasterizer.PaintSemantic(SpecExample());
            PixelTransform t = SpecExample().Transform;

            Point2D doorCenter = t.ToPixel(new Point2D(1.4, 0.075));
            Point2D wallCenter = t.ToPixel(new Point2D(4.0, 0.075));
            Point2D shaft = t.ToPixel(new Point2D(2.25, 2.25));

            mask[(int)doorCenter.X, (int)doorCenter.Y].Should().Be((byte)StructureClass.Door);
            mask[(int)wallCenter.X, (int)wallCenter.Y].Should().Be((byte)StructureClass.Wall);
            mask[(int)shaft.X, (int)shaft.Y].Should().Be((byte)StructureClass.Background, "the room hole stays unpainted");
            mask.CountPixels(StructureClass.Swing).Should().BePositive();
        }
    }
}
