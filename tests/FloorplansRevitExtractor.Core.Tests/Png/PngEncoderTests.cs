using System;
using System.Linq;
using FloorplansRevitExtractor.Core.Png;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Png
{
    public class PngEncoderTests
    {
        private static readonly Rgb[] ThreeColors =
        {
            new Rgb(0, 0, 0),
            new Rgb(255, 200, 100),
            new Rgb(10, 20, 30),
        };

        [Fact]
        public void EncodePalette_RoundTrips_AsColorType3With256EntryPalette()
        {
            byte[] indices = { 0, 1, 2, 255, 1, 0 };

            PngTestDecoder png = PngTestDecoder.Decode(PngEncoder.EncodePalette(3, 2, indices, ThreeColors));

            png.Width.Should().Be(3);
            png.Height.Should().Be(2);
            png.ColorType.Should().Be(3);
            png.BitDepth.Should().Be(8);
            png.Pixels.Should().Equal(indices);
            png.Palette.Length.Should().Be(256 * 3, "index 255 (ignore) must have a palette entry");
            png.Palette.Take(9).Should().Equal(new byte[] { 0, 0, 0, 255, 200, 100, 10, 20, 30 });
            png.ChunkTypes.Should().Equal("IHDR", "PLTE", "IDAT", "IEND");
        }

        [Fact]
        public void EncodeGray16_RoundTrips_BigEndian()
        {
            ushort[] values = { 0, 1, 300, 65534 };

            PngTestDecoder png = PngTestDecoder.Decode(PngEncoder.EncodeGray16(2, 2, values));

            png.ColorType.Should().Be(0);
            png.BitDepth.Should().Be(16);
            png.Pixels.Should().Equal(new byte[] { 0, 0, 0, 1, 1, 44, 255, 254 });
        }

        [Fact]
        public void Encode_SameInput_ProducesIdenticalBytes()
        {
            var indices = Enumerable.Range(0, 64 * 48).Select(i => (byte)(i % 10)).ToArray();

            byte[] first = PngEncoder.EncodePalette(64, 48, indices, ThreeColors);
            byte[] second = PngEncoder.EncodePalette(64, 48, indices, ThreeColors);

            second.Should().Equal(first);
        }

        [Fact]
        public void Encode_LargeImage_SurvivesMultiBlockCompression()
        {
            var indices = Enumerable.Range(0, 1600 * 1200).Select(i => (byte)((i / 7) % 10)).ToArray();

            PngTestDecoder png = PngTestDecoder.Decode(PngEncoder.EncodePalette(1600, 1200, indices, ThreeColors));

            png.Pixels.Should().Equal(indices);
        }

        [Fact]
        public void EncodePalette_PixelCountMismatch_Throws()
        {
            Action act = () => PngEncoder.EncodePalette(3, 3, new byte[8], ThreeColors);

            act.Should().Throw<ArgumentException>();
        }

        [Fact]
        public void EncodePalette_MoreThan256Colors_Throws()
        {
            Action act = () => PngEncoder.EncodePalette(1, 1, new byte[1], new Rgb[257]);

            act.Should().Throw<ArgumentException>();
        }
    }
}
