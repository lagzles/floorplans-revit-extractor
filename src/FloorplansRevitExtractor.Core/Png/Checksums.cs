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
            var accumulator = new Adler32Accumulator();
            accumulator.Update(data, 0, data.Length);
            return accumulator.Value;
        }

        /// <summary>Adler-32 over data that arrives in pieces (zlib's NMAX batching).</summary>
        internal sealed class Adler32Accumulator
        {
            private uint _a = 1;
            private uint _b;

            internal uint Value => (_b << 16) | _a;

            internal void Update(byte[] data, int offset, int count)
            {
                int end = offset + count;
                for (int blockStart = offset; blockStart < end; blockStart += Adler32BlockSize)
                {
                    int blockEnd = Math.Min(end, blockStart + Adler32BlockSize);
                    for (int i = blockStart; i < blockEnd; i++)
                    {
                        _a += data[i];
                        _b += _a;
                    }

                    _a %= Adler32Modulus;
                    _b %= Adler32Modulus;
                }
            }
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
