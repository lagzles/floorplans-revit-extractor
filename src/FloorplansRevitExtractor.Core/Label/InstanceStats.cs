using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Per-instance box, coordinate sums and pixel count from one pass over
    /// the instance mask, in arrays indexed by instance id (about 2.4 MB for
    /// all 65536 ids), so a 64-megapixel mask costs no per-pixel hashing.
    /// </summary>
    internal sealed class InstanceStats
    {
        private const int IdCount = ushort.MaxValue + 1;

        private InstanceStats()
        {
        }

        internal int[] Count { get; } = new int[IdCount];

        private int[] MinX { get; } = new int[IdCount];

        private int[] MinY { get; } = new int[IdCount];

        private int[] MaxX { get; } = new int[IdCount];

        private int[] MaxY { get; } = new int[IdCount];

        private long[] SumX { get; } = new long[IdCount];

        private long[] SumY { get; } = new long[IdCount];

        internal static InstanceStats Accumulate(InstanceMask mask)
        {
            var stats = new InstanceStats();
            ushort[] pixels = mask.Pixels;
            for (int y = 0; y < mask.Height; y++)
            {
                int rowStart = y * mask.Width;
                for (int x = 0; x < mask.Width; x++)
                {
                    ushort id = pixels[rowStart + x];
                    if (id != 0)
                    {
                        stats.Add(id, x, y);
                    }
                }
            }

            return stats;
        }

        internal Box BoxOf(ushort id) => new Box(MinX[id], MinY[id], MaxX[id], MaxY[id], SumX[id], SumY[id], Count[id]);

        private void Add(ushort id, int x, int y)
        {
            if (Count[id] == 0)
            {
                MinX[id] = x;
                MinY[id] = y;
                MaxX[id] = x;
                MaxY[id] = y;
            }
            else
            {
                MinX[id] = x < MinX[id] ? x : MinX[id];
                MaxX[id] = x > MaxX[id] ? x : MaxX[id];
                MaxY[id] = y; // rows are visited in increasing order
            }

            SumX[id] += x;
            SumY[id] += y;
            Count[id]++;
        }

        internal readonly record struct Box(int MinX, int MinY, int MaxX, int MaxY, long SumX, long SumY, int Count);
    }
}
