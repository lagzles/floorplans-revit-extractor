using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class StructureClassFoldingTests
    {
        private static IReadOnlyList<Point2D> Line(double x0, double y0, double x1, double y1) =>
            new[] { new Point2D(x0, y0), new Point2D(x1, y1) };

        [Theory]
        [InlineData(StructureClass.Passage, StructureClass.Door)]
        [InlineData(StructureClass.Text, StructureClass.Dimension)]
        [InlineData(StructureClass.LowWall, StructureClass.Wall)]
        [InlineData(StructureClass.CurtainWall, StructureClass.Window)]
        [InlineData(StructureClass.Fixture, StructureClass.Furniture)]
        [InlineData(StructureClass.Grid, StructureClass.Grid)]
        [InlineData(StructureClass.Wall, StructureClass.Wall)]
        public void ToLegacy_FoldsEachNewClassIntoItsParent(StructureClass cls, StructureClass expected)
        {
            StructureClassFolding.ToLegacy((byte)cls).Should().Be((byte)expected);
        }

        [Fact]
        public void ToLegacy_KeepsIgnore()
        {
            StructureClassFolding.ToLegacy(SemanticMask.IgnoreIndex).Should().Be(SemanticMask.IgnoreIndex);
        }

        [Fact]
        public void Folds_OfAPaintedMask_LandIn12And10Classes()
        {
            // Every class, overlapping, painted in the spec v4 order.
            MaskElement[] construction =
            {
                MaskElement.Filled(StructureClass.Room, new[] { Rect(0, 0, 30, 30) }),
                MaskElement.Filled(StructureClass.Wall, new[] { Rect(0, 0, 30, 3) }),
                MaskElement.Filled(StructureClass.CurtainWall, new[] { Rect(20, 0, 30, 3) }),
                MaskElement.Filled(StructureClass.Passage, new[] { Rect(5, 0, 8, 3) }),
                MaskElement.Filled(StructureClass.Fixture, new[] { Rect(2, 4, 8, 8) }),
                MaskElement.Stroked(StructureClass.LowWall, new[] { Line(0, 20, 30, 20) }, 2),
            };
            MaskElement[] annotation =
            {
                MaskElement.Stroked(StructureClass.Grid, new[] { Line(15, 0, 15, 30) }, 2),
                MaskElement.Stroked(StructureClass.Dimension, new[] { Line(0, 12, 30, 12) }, 2),
                MaskElement.Filled(StructureClass.Text, new[] { Rect(4, 2, 12, 14) }),
            };

            SemanticMask full = SemanticMaskPainter.Paint(30, 30, construction.Concat(annotation).ToList());
            SemanticMask constructionOnly = SemanticMaskPainter.Paint(30, 30, construction);

            byte[] legacy = StructureClassFolding.ToLegacy(full.CopyPixels());
            byte[] tenClasses = StructureClassFolding.StripAnnotation(full.CopyPixels(), constructionOnly.CopyPixels());

            legacy.Should().OnlyContain(v => v <= 11);
            tenClasses.Should().OnlyContain(v => v <= 9);
            tenClasses.Should().Equal(StructureClassFolding.ToLegacy(constructionOnly.CopyPixels()), "annotation sits on background or room only");
            full.CountPixels(StructureClass.Text).Should().BeGreaterThan(0);
        }
    }
}
