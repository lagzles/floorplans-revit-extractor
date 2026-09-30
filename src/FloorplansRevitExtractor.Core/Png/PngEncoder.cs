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

            return Encode(width, height, ColorTypePalette, width, indices, plte);
        }

        public static byte[] EncodeGray16(int width, int height, ushort[] values)
        {
            RequirePixelCount(width, height, values.Length);
            var bigEndian = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                bigEndian[i * 2] = (byte)(values[i] >> 8);
                bigEndian[(i * 2) + 1] = (byte)values[i];
            }

            return Encode(width, height, ColorTypeGrayscale, width * 2, bigEndian, null);
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

        private static byte[] Encode(int width, int height, byte colorType, int stride, byte[] data, byte[]? plte)
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

            WriteChunk(output, "IDAT", ZlibCompress(AddFilterBytes(data, stride, height)));
            WriteChunk(output, "IEND", Array.Empty<byte>());
            return output.ToArray();
        }

        private static byte[] AddFilterBytes(byte[] data, int stride, int height)
        {
            var filtered = new byte[height * (stride + 1)];
            for (int y = 0; y < height; y++)
            {
                Buffer.BlockCopy(data, y * stride, filtered, (y * (stride + 1)) + 1, stride);
            }

            return filtered;
        }

        /// <summary>netstandard2.0 has raw deflate only; zlib adds a 2-byte header and an Adler-32 trailer.</summary>
        private static byte[] ZlibCompress(byte[] raw)
        {
            using var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            var adler = new byte[4];
            WriteInt32BigEndian(adler, 0, (int)Checksums.Adler32(raw));
            output.Write(adler, 0, adler.Length);
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
