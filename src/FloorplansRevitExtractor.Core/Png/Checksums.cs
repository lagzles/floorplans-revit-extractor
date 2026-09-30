using System;

namespace FloorplansRevitExtractor.Core.Png
{
    /// <summary>The two checksums PNG needs: CRC-32 per chunk, Adler-32 per zlib stream.</summary>
    internal static class Checksums
    {
        private const uint Adler32Modulus = 65521;

        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>CRC-32 over the chunk type bytes followed by the chunk data.</summary>
        internal static uint Crc32(byte[] typeBuffer, int typeOffset, int typeLength, byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            for (int i = typeOffset; i < typeOffset + typeLength; i++)
            {
                crc = CrcTable[(crc ^ typeBuffer[i]) & 0xFF] ^ (crc >> 8);
            }

            foreach (byte b in data)
            {
                crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFF;
        }

        /// <summary>Largest run of bytes before the sums can overflow 32 bits (zlib's NMAX).</summary>
        private const int Adler32BlockSize = 5552;

        internal static uint Adler32(byte[] data)
        {
            uint a = 1;
            uint b = 0;
            for (int blockStart = 0; blockStart < data.Length; blockStart += Adler32BlockSize)
            {
                int blockEnd = Math.Min(data.Length, blockStart + Adler32BlockSize);
                for (int i = blockStart; i < blockEnd; i++)
                {
                    a += data[i];
                    b += a;
                }

                a %= Adler32Modulus;
                b %= Adler32Modulus;
            }

            return (b << 16) | a;
        }

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
