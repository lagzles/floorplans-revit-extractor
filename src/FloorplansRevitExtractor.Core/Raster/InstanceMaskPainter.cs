using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Paints rooms only, underneath door and window openings too (the
    /// instance mask ignores the semantic layer order). Rooms are painted in
    /// ascending id, so where boundaries overlap the higher id wins no matter
    /// how the input was ordered.
    /// </summary>
    public static class InstanceMaskPainter
    {
        /// <summary>room_id + 1 must fit in 16 bits.</summary>
        public const int MaxRoomId = ushort.MaxValue - 1;

        public static InstanceMask Paint(int width, int height, IReadOnlyList<RoomInstance> rooms)
        {
            RoomInstance? outOfRange = rooms.FirstOrDefault(r => r.RoomId < 0 || r.RoomId > MaxRoomId);
            if (outOfRange != null)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rooms),
                    outOfRange.RoomId,
                    $"Room ids must be between 0 and {MaxRoomId}.");
            }

            var pixels = new ushort[PixelSpans.RequireArea(width, height)];
            foreach (RoomInstance room in rooms.OrderBy(r => r.RoomId))
            {
                var instanceId = (ushort)(room.RoomId + 1);
                SpanWriter write = (row, first, last) =>
                {
                    int rowStart = row * width;
                    for (int x = first; x <= last; x++)
                    {
                        pixels[rowStart + x] = instanceId;
                    }
                };

                foreach (PolygonShape polygon in room.Polygons)
                {
                    ScanlineFiller.FillSpans(width, height, polygon, write);
                }
            }

            return new InstanceMask(width, height, pixels);
        }
    }
}
