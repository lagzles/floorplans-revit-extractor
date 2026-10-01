using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace FloorplansRevitExtractor.Core.Tests.Png
{
    /// <summary>Hand-built PNG files for edge cases no encoder in the repo produces.</summary>
    internal static class PngBytes
    {
        internal static byte[] Build(int width, int height, byte colorType, byte[][] rows, params (string Type, byte[] Data)[] extra)
        {
            var ihdr = new byte[13];
            WriteInt(ihdr, 0, width);
            WriteInt(ihdr, 4, height);
            ihdr[8] = 8;
            ihdr[9] = colorType;
            byte[] raw = rows.SelectMany(r => new byte[] { 0 }.Concat(r)).ToArray();
            return Assemble(new[] { ("IHDR", ihdr) }.Concat(extra).Concat(new[] { ("IDAT", Zlib(raw)), ("IEND", new byte[0]) }));
        }

        internal static byte[] WithRawIhdr(byte[] ihdr) =>
            Assemble(new[] { ("IHDR", ihdr), ("IDAT", Zlib(new byte[] { 0, 0 })), ("IEND", new byte[0]) });

        private static byte[] Assemble(IEnumerable<(string Type, byte[] Data)> chunks)
        {
            var output = new MemoryStream();
            output.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            foreach ((string type, byte[] data) in chunks)
            {
                var header = new byte[8];
                WriteInt(header, 0, data.Length);
                Encoding.ASCII.GetBytes(type, 0, 4, header, 4);
                var crcInput = header.Skip(4).Concat(data).ToArray();
                var crc = new byte[4];
                WriteInt(crc, 0, (int)Crc32(crcInput));
                output.Write(header, 0, 8);
                output.Write(data, 0, data.Length);
                output.Write(crc, 0, 4);
            }

            return output.ToArray();
        }

        private static byte[] Zlib(byte[] raw)
        {
            var output = new MemoryStream();
            output.WriteByte(0x78);
            output.WriteByte(0x9C);
            using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            uint a = 1;
            uint b = 0;
            foreach (byte x in raw)
            {
                a = (a + x) % 65521;
                b = (b + a) % 65521;
            }

            var adler = new byte[4];
            WriteInt(adler, 0, (int)((b << 16) | a));
            output.Write(adler, 0, 4);
            return output.ToArray();
        }

        private static uint Crc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte x in data)
            {
                crc ^= x;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
                }
            }

            return crc ^ 0xFFFFFFFF;
        }

        private static void WriteInt(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }
    }
}
