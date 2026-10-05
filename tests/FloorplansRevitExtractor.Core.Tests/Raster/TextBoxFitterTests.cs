using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class TextBoxFitterTests
    {
        private static RgbImage WhiteWithInk(int width, int height, params (int X, int Y)[] ink)
        {
            byte[] pixels = Enumerable.Repeat((byte)255, width * height * 3).ToArray();
            foreach ((int x, int y) in ink)
            {
                int o = ((y * width) + x) * 3;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = 0;
            }

            return PngDecoder.DecodeRgb(PngEncoder.EncodeRgb(width, height, pixels));
        }

        [Fact]
        public void Fit_HorizontalText_IsTheInkBoxPlusOnePixel()
        {
            RgbImage image = WhiteWithInk(40, 20, (10, 8), (14, 8), (17, 11));

            IReadOnlyList<Point2D>? box = TextBoxFitter.Fit(image, new Point2D(15, 10), new Vector2D(1, 0), 12, 6);

            box.Should().NotBeNull();
            box!.Min(p => p.X).Should().BeApproximately(9, 1e-9); // pixel 10 starts at 10, minus the 1 px pad
            box.Max(p => p.X).Should().BeApproximately(19, 1e-9); // pixel 17 ends at 18, plus the pad
            box.Min(p => p.Y).Should().BeApproximately(7, 1e-9);
            box.Max(p => p.Y).Should().BeApproximately(13, 1e-9);
        }

        [Fact]
        public void Fit_IgnoresInkOutsideTheLooseBox()
        {
            RgbImage image = WhiteWithInk(40, 20, (10, 8), (35, 8));

            IReadOnlyList<Point2D>? box = TextBoxFitter.Fit(image, new Point2D(12, 8), new Vector2D(1, 0), 5, 3);

            box!.Max(p => p.X).Should().BeApproximately(12, 1e-9);
        }

        [Fact]
        public void Fit_VerticalText_KeepsTheTextAngle()
        {
            RgbImage image = WhiteWithInk(20, 40, (8, 10), (8, 20));

            IReadOnlyList<Point2D>? box = TextBoxFitter.Fit(image, new Point2D(8.5, 15), new Vector2D(0, -1), 10, 3);

            box!.Min(p => p.Y).Should().BeApproximately(9, 1e-9);
            box.Max(p => p.Y).Should().BeApproximately(22, 1e-9);
            box.Min(p => p.X).Should().BeApproximately(7, 1e-9);
            box.Max(p => p.X).Should().BeApproximately(10, 1e-9);
        }

        [Fact]
        public void Fit_NoInk_ReturnsNull()
        {
            RgbImage image = WhiteWithInk(20, 20);

            TextBoxFitter.Fit(image, new Point2D(10, 10), new Vector2D(1, 0), 5, 3).Should().BeNull();
        }
    }
}
