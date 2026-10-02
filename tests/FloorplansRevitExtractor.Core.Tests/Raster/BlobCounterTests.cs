using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class BlobCounterTests
    {
        private static MaskElement Filled(StructureClass cls, double x0, double y0, double x1, double y1) =>
            MaskElement.Filled(cls, new[] { Rect(x0, y0, x1, y1) });

        [Fact]
        public void Count_SeparateRectangles_AreSeparateBlobs()
        {
            SemanticMask mask = SemanticMaskPainter.Paint(20, 10, new[]
            {
                Filled(StructureClass.Pillar, 1, 1, 3, 3),
                Filled(StructureClass.Pillar, 6, 1, 8, 3),
                Filled(StructureClass.Pillar, 12, 5, 14, 9),
            });

            BlobCounter.Count(mask, StructureClass.Pillar).Should().Be(3);
            BlobCounter.Count(mask, StructureClass.Door).Should().Be(0);
        }

        [Fact]
        public void Count_DiagonalNeighbours_AreOneBlob()
        {
            // 8-connectivity: pixels touching at a corner belong together, as a rotated door does.
            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[]
            {
                Filled(StructureClass.Door, 1, 1, 2, 2),
                Filled(StructureClass.Door, 2, 2, 3, 3),
            });

            BlobCounter.Count(mask, StructureClass.Door).Should().Be(1);
        }

        [Fact]
        public void Count_RingShape_IsOneBlob()
        {
            var ring = new PolygonShape(RectRing(1, 1, 9, 9), new[] { RectRing(3, 3, 7, 7) });

            SemanticMask mask = SemanticMaskPainter.Paint(10, 10, new[] { MaskElement.Filled(StructureClass.Pillar, new[] { ring }) });

            BlobCounter.Count(mask, StructureClass.Pillar).Should().Be(1);
        }
    }
}
