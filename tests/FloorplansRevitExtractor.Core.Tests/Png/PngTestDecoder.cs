using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FloorplansRevitExtractor.Core.Tests.Png
{
    /// <summary>
    /// Minimal strict PNG reader for tests: validates signature, chunk CRCs,
    /// zlib header and Adler-32, and undoes filter type 0 only (the only one
    /// the encoder writes). Anything else fails loudly.
    /// </summary>
    internal sealed class PngTestDecoder
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        internal int Width { get; private set; }

        internal int Height { get; private set; }

        internal int BitDepth { get; private set; }

        internal int ColorType { get; private set; }

        internal byte[] Palette { get; private set; } = Array.Empty<byte>();

        internal byte[] Pixels { get; private set; } = Array.Empty<byte>();

        internal List<string> ChunkTypes { get; } = new List<string>();

        internal static PngTestDecoder Decode(byte[] png)
        {
            var decoder = new PngTestDecoder();
            for (int i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                {
                    throw new InvalidDataException("bad signature");
                }
            }

            var idat = new MemoryStream();
            int offset = Signature.Length;
            while (offset < png.Length)
            {
                int length = ReadInt32BigEndian(png, offset);
                string type = Encoding.ASCII.GetString(png, offset + 4, 4);
                var typeAndData = new byte[4 + length];
                Buffer.BlockCopy(png, offset + 4, typeAndData, 0, typeAndData.Length);
                uint storedCrc = (uint)ReadInt32BigEndian(png, offset + 8 + length);
                if (Crc32(typeAndData) != storedCrc)
                {
                    throw new InvalidDataException($"bad CRC in {type}");
                }

                decoder.ChunkTypes.Add(type);
                var data = new byte[length];
                Buffer.BlockCopy(png, offset + 8, data, 0, length);
                decoder.ReadChunk(type, data, idat);
                offset += 12 + length;
            }

            decoder.Pixels = decoder.Unfilter(Inflate(idat.ToArray()));
            return decoder;
        }

        private void ReadChunk(string type, byte[] data, MemoryStream idat)
        {
            switch (type)
            {
                case "IHDR":
                    Width = ReadInt32BigEndian(data, 0);
                    Height = ReadInt32BigEndian(data, 4);
                    BitDepth = data[8];
                    ColorType = data[9];
                    if (data[10] != 0 || data[11] != 0 || data[12] != 0)
                    {
                        throw new InvalidDataException("compression, filter and interlace methods must all be 0");
                    }

                    break;
                case "PLTE":
                    Palette = data;
                    break;
                case "IDAT":
                    idat.Write(data, 0, data.Length);
                    break;
            }
        }

        private byte[] Unfilter(byte[] raw)
        {
            int bytesPerPixel = ColorType == 3 ? 1 : BitDepth / 8;
            int stride = Width * bytesPerPixel;
            if (raw.Length != Height * (stride + 1))
            {
                throw new InvalidDataException("unexpected image data length");
            }

            var pixels = new byte[Height * stride];
            for (int y = 0; y < Height; y++)
            {
                if (raw[y * (stride + 1)] != 0)
                {
                    throw new InvalidDataException("only filter type 0 is expected");
                }

                Buffer.BlockCopy(raw, (y * (stride + 1)) + 1, pixels, y * stride, stride);
            }

            return pixels;
        }

        private static byte[] Inflate(byte[] zlib)
        {
            if (((zlib[0] << 8) | zlib[1]) % 31 != 0 || (zlib[0] & 0x0F) != 8)
            {
                throw new InvalidDataException("bad zlib header");
            }

            var output = new MemoryStream();
            using (var deflate = new DeflateStream(new MemoryStream(zlib, 2, zlib.Length - 6), CompressionMode.Decompress))
            {
                deflate.CopyTo(output);
            }

            byte[] result = output.ToArray();
            if ((uint)ReadInt32BigEndian(zlib, zlib.Length - 4) != Adler32(result))
            {
                throw new InvalidDataException("bad Adler-32");
            }

            return result;
        }

        private static int ReadInt32BigEndian(byte[] b, int i) =>
            (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFF;
        }

        private static uint Adler32(byte[] data)
        {
            uint a = 1;
            uint b = 0;
            foreach (byte x in data)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }

            return (b << 16) | a;
        }
    }
}
