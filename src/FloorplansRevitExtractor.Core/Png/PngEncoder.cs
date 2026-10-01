using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FloorplansRevitExtractor.Core.Png
{
    /// <summary>
    /// Writes the two lossless mask formats the dataset needs, with no image
    /// library: 8-bit palette (PIL mode P, semantic mask) and 16-bit grayscale
    /// (PIL mode I;16, instance mask). Every scanline uses filter 0 and the
    /// output is deterministic for the same input on the same runtime.
    /// </summary>
    public static class PngEncoder
    {
        private const int PaletteSize = 256;
        private const byte ColorTypeGrayscale = 0;
        private const byte ColorTypeRgb = 2;
        private const byte ColorTypePalette = 3;

        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>
        /// The palette is padded to 256 entries with black, so every index,
        /// including 255 (ignore), is valid PNG.
        /// </summary>
        public static byte[] EncodePalette(int width, int height, byte[] indices, IReadOnlyList<Rgb> palette)
        {
            RequirePixelCount(width, height, indices.Length);
            if (palette.Count > PaletteSize)
            {
                throw new ArgumentException($"A PNG palette holds at most {PaletteSize} colors.", nameof(palette));
            }

            var plte = new byte[PaletteSize * 3];
            for (int i = 0; i < palette.Count; i++)
            {
                plte[i * 3] = palette[i].R;
                plte[(i * 3) + 1] = palette[i].G;
                plte[(i * 3) + 2] = palette[i].B;
            }

            return Encode(width, height, ColorTypePalette, width, plte, (y, row) => Buffer.BlockCopy(indices, y * width, row, 0, width));
        }

        /// <summary>8-bit RGB, three bytes per pixel row-major: the dataset's image format.</summary>
        public static byte[] EncodeRgb(int width, int height, byte[] rgb)
        {
            RequirePixelCount(width, height, rgb.Length / 3);
            if (rgb.Length % 3 != 0)
            {
                throw new ArgumentException($"RGB data length {rgb.Length} is not a multiple of 3.", nameof(rgb));
            }

            return Encode(width, height, ColorTypeRgb, width * 3, null, (y, row) => Buffer.BlockCopy(rgb, y * width * 3, row, 0, width * 3));
        }

        public static byte[] EncodeGray16(int width, int height, ushort[] values)
        {
            RequirePixelCount(width, height, values.Length);
            return Encode(width, height, ColorTypeGrayscale, width * 2, null, (y, row) =>
            {
                int start = y * width;
                for (int x = 0; x < width; x++)
                {
                    row[x * 2] = (byte)(values[start + x] >> 8);
                    row[(x * 2) + 1] = (byte)values[start + x];
                }
            });
        }

        private static void RequirePixelCount(int width, int height, int actual)
        {
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentException($"Image size must be positive, got {width}x{height}.");
            }

            if (actual != (long)width * height)
            {
                throw new ArgumentException($"Expected {(long)width * height} pixels for {width}x{height}, got {actual}.");
            }
        }

        /// <summary>
        /// Rows are filled one at a time and streamed through deflate with the
        /// filter byte in front, so no full-image copy is made.
        /// </summary>
        private static byte[] Encode(int width, int height, byte colorType, int stride, byte[]? plte, Action<int, byte[]> fillRow)
        {
            byte bitDepth = colorType == ColorTypeGrayscale ? (byte)16 : (byte)8;
            var ihdr = new byte[13];
            WriteInt32BigEndian(ihdr, 0, width);
            WriteInt32BigEndian(ihdr, 4, height);
            ihdr[8] = bitDepth;
            ihdr[9] = colorType;

            using var output = new MemoryStream();
            output.Write(Signature, 0, Signature.Length);
            WriteChunk(output, "IHDR", ihdr);
            if (plte != null)
            {
                WriteChunk(output, "PLTE", plte);
            }

            WriteChunk(output, "IDAT", ZlibCompressRows(height, stride, fillRow));
            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        /// <summary>netstandard2.0 has raw deflate only; zlib adds a 2-byte header and an Adler-32 trailer.</summary>
        private static byte[] ZlibCompressRows(int height, int stride, Action<int, byte[]> fillRow)
        {
            using var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            var adler = new Checksums.Adler32Accumulator();
            var line = new byte[stride + 1]; // filter byte 0, then the row
            var row = new byte[stride];
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                for (int y = 0; y < height; y++)
                {
                    fillRow(y, row);
                    Buffer.BlockCopy(row, 0, line, 1, stride);
                    deflate.Write(line, 0, line.Length);
                    adler.Update(line, 0, line.Length);
                }
            }

            var trailer = new byte[4];
            WriteInt32BigEndian(trailer, 0, (int)adler.Value);
            output.Write(trailer, 0, trailer.Length);
            return output.ToArray();
        }

        private static void WriteChunk(Stream output, string type, byte[] data)
        {
            var header = new byte[8];
            WriteInt32BigEndian(header, 0, data.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, header, 4);

            var crc = new byte[4];
            WriteInt32BigEndian(crc, 0, (int)Checksums.Crc32(header, 4, 4, data));

            output.Write(header, 0, header.Length);
            output.Write(data, 0, data.Length);
            output.Write(crc, 0, crc.Length);
        }

        private static void WriteInt32BigEndian(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
