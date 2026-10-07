using System.Linq;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;
using static FloorplansRevitExtractor.Core.Tests.Raster.RasterTestShapes;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class ImageRegistrationTests
    {
        private static readonly StructureClass[] Construction = { StructureClass.Wall, StructureClass.Pillar };

        private static readonly (int, int, int, int)[] Plan =
        {
            (5, 5, 75, 9), (5, 5, 9, 45), (71, 5, 75, 45), (5, 41, 75, 45), (35, 9, 38, 30),
        };

        /// <summary>Outline-only walls, as Revit draws them: each rectangle's outline, 1 px, shifted.</summary>
        private static RgbImage Drawing(int width, int height, int shiftX, int shiftY, params (int X0, int Y0, int X1, int Y1)[] walls)
        {
            byte[] pixels = Enumerable.Repeat((byte)255, width * height * 3).ToArray();
            void Dark(int x, int y)
            {
                x += shiftX;
                y += shiftY;
                if (x >= 0 && y >= 0 && x < width && y < height)
                {
                    int o = ((y * width) + x) * 3;
                    pixels[o] = pixels[o + 1] = pixels[o + 2] = 0;
                }
            }

            foreach ((int x0, int y0, int x1, int y1) in walls)
            {
                for (int x = x0; x < x1; x++)
                {
                    Dark(x, y0);
                    Dark(x, y1 - 1);
                }

                for (int y = y0; y < y1; y++)
                {
                    Dark(x0, y);
                    Dark(x1 - 1, y);
                }
            }

            return PngDecoder.DecodeRgb(PngEncoder.EncodeRgb(width, height, pixels));
        }

        private static SemanticMask Provisional() =>
            SemanticMaskPainter.Paint(80, 50, Plan.Select(r => MaskElement.Filled(StructureClass.Wall, new[] { Rect(r.Item1, r.Item2, r.Item3, r.Item4) })).ToList());

        [Theory]
        [InlineData(0, 0)]
        [InlineData(12, 7)]
        [InlineData(25, 19)]
        public void Find_RecoversTheShiftOfTheDrawing(int dx, int dy)
        {
            RgbImage image = Drawing(110, 70, dx, dy, Plan);

            RegistrationFit? fit = ImageRegistration.Find(image, Provisional(), Construction, (0, 30), (0, 20));

            fit!.Dx.Should().Be(dx);
            fit.Dy.Should().Be(dy);
            fit.IsConfident.Should().BeTrue();
        }

        [Fact]
        public void Find_NegativeShift_WhenTheImageStartsInsideTheRegion()
        {
            RgbImage image = Drawing(80, 50, -3, -4, Plan);

            RegistrationFit? fit = ImageRegistration.Find(image, Provisional(), Construction, (-5, 0), (-6, 0));

            fit!.Dx.Should().Be(-3);
            fit.Dy.Should().Be(-4);
        }

        [Fact]
        public void Find_ADrawingWithoutTheWalls_IsNotConfident()
        {
            RgbImage blank = Drawing(110, 70, 0, 0);

            RegistrationFit? fit = ImageRegistration.Find(blank, Provisional(), Construction, (0, 30), (0, 20));

            fit!.IsConfident.Should().BeFalse();
        }
    }
}
