using System;
using System.IO;
using System.Linq;
using FloorplansRevitExtractor.Core.Png;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Png
{
    public class PngDecoderTests
    {
        private static string Fixture(string name) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fixtures", "png", name);

        // Written by PIL; the .rgb files hold PIL's own RGB pixels (RGBA composited over white).
        [Theory]
        [InlineData("rgb")]
        [InlineData("rgba")]
        [InlineData("gray")]
        [InlineData("palette")]
        [InlineData("all_filters")] // one row per filter type 0..4, built by hand and checked with PIL
        public void DecodeRgb_PngsWrittenByOtherTools_MatchTheirPixels(string name)
        {
            RgbImage image = PngDecoder.DecodeRgb(File.ReadAllBytes(Fixture(name + ".png")));

            image.Width.Should().Be(37);
            image.Height.Should().Be(23);
            image.CopyPixels().Should().Equal(File.ReadAllBytes(Fixture(name + ".rgb")));
        }

        [Fact]
        public void EncodeRgb_RoundTripsThroughTheDecoder()
        {
            byte[] pixels = Enumerable.Range(0, 5 * 4 * 3).Select(i => (byte)(i * 7)).ToArray();

            RgbImage image = PngDecoder.DecodeRgb(PngEncoder.EncodeRgb(5, 4, pixels));

            image.CopyPixels().Should().Equal(pixels);
        }

        [Fact]
        public void EncodeRgb_IsColorType2_TrueColorWithoutAlpha()
        {
            PngTestDecoder png = PngTestDecoder.Decode(PngEncoder.EncodeRgb(2, 1, new byte[6]));

            png.ColorType.Should().Be(2);
            png.BitDepth.Should().Be(8);
        }

        [Fact]
        public void DecodeRgb_PaletteMask_ExpandsToItsColors()
        {
            var palette = new[] { new Rgb(0, 0, 0), new Rgb(230, 60, 60) };

            RgbImage image = PngDecoder.DecodeRgb(PngEncoder.EncodePalette(2, 1, new byte[] { 1, 0 }, palette));

            image.CopyPixels().Should().Equal(230, 60, 60, 0, 0, 0);
        }

        [Fact]
        public void DecodeRgb_SixteenBitImage_IsRejectedClearly()
        {
            byte[] png = PngEncoder.EncodeGray16(2, 2, new ushort[4]);

            Action act = () => PngDecoder.DecodeRgb(png);

            act.Should().Throw<InvalidDataException>().WithMessage("*16*");
        }

        [Theory]
        [InlineData("not a png")]
        [InlineData("")]
        public void DecodeRgb_NotAPng_Throws(string text)
        {
            Action act = () => PngDecoder.DecodeRgb(System.Text.Encoding.ASCII.GetBytes(text));

            act.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void DecodeRgb_CorruptedChunk_FailsTheCrcCheck()
        {
            byte[] png = File.ReadAllBytes(Fixture("rgb.png"));
            png[40] ^= 0xFF; // inside the first IDAT

            Action act = () => PngDecoder.DecodeRgb(png);

            act.Should().Throw<InvalidDataException>().WithMessage("*CRC*");
        }

        [Fact]
        public void DecodeRgb_TransparentPaletteEntry_IsPaperNotBlack()
        {
            // A palette PNG whose background entry is black but fully transparent (tRNS).
            byte[] png = PngBytes.Build(1, 1, colorType: 3, rows: new[] { new byte[] { 0 } },
                ("PLTE", new byte[] { 0, 0, 0 }), ("tRNS", new byte[] { 0 }));

            PngDecoder.DecodeRgb(png).CopyPixels().Should().Equal(255, 255, 255);
        }

        [Fact]
        public void DecodeRgb_TransparentColorKeyOnRgb_IsPaper()
        {
            // tRNS on a true-color image names one RGB value (16-bit samples) that is transparent.
            byte[] png = PngBytes.Build(2, 1, colorType: 2, rows: new[] { new byte[] { 0, 0, 0, 10, 20, 30 } },
                ("tRNS", new byte[] { 0, 0, 0, 0, 0, 0 }));

            PngDecoder.DecodeRgb(png).CopyPixels().Should().Equal(255, 255, 255, 10, 20, 30);
        }

        [Fact]
        public void DecodeRgb_ShortHeader_IsInvalidDataNotACrash()
        {
            byte[] png = PngBytes.WithRawIhdr(new byte[] { 0, 0, 0, 1, 0, 0, 0, 1, 8 });

            Action act = () => PngDecoder.DecodeRgb(png);

            act.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void DecodeRgb_HugeDeclaredSize_IsRejectedBeforeInflating()
        {
            // 2^29 x 1 gray would inflate to half a gigabyte from a tiny file.
            byte[] png = PngBytes.Build(1 << 29, 1, colorType: 0, rows: new byte[0][]);

            Action act = () => PngDecoder.DecodeRgb(png);

            act.Should().Throw<InvalidDataException>().WithMessage("*pixels*");
        }

        [Fact]
        public void Gray_UsesPillowsLuminanceFormula()
        {
            // PIL convert("L"): (R*19595 + G*38470 + B*7471 + 0x8000) >> 16.
            RgbImage image = PngDecoder.DecodeRgb(PngEncoder.EncodeRgb(2, 1, new byte[] { 255, 0, 0, 10, 200, 30 }));

            image.Gray(0, 0).Should().Be(76);
            image.Gray(1, 0).Should().Be(124);
        }
    }
}
