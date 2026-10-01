using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Builds instances_px from the painted instance mask, field by field as
    /// the generator's pixel_instances does, so Revit samples are measured
    /// with the same ruler as the synthetic ones. One pass over the mask
    /// gathers every instance's box, sums and count; the label point then
    /// only looks inside each instance's box.
    /// </summary>
    public static class InstancePixels
    {
        public static IReadOnlyList<InstancePx> Build(InstanceMask mask, PixelTransform transform, IReadOnlyList<InstanceRoom> rooms)
        {
            if (mask.Width != transform.Width || mask.Height != transform.Height)
            {
                throw new ArgumentException(
                    $"Instance mask is {mask.Width}x{mask.Height} but the transform is {transform.Width}x{transform.Height}.");
            }

            InstanceStats stats = InstanceStats.Accumulate(mask);
            var result = new List<InstancePx>();
            foreach (InstanceRoom room in rooms.OrderBy(r => r.RoomId))
            {
                var instanceId = (ushort)(room.RoomId + 1);
                if (stats.Count[instanceId] > 0)
                {
                    result.Add(ToInstancePx(room, instanceId, stats, mask, transform));
                }
            }

            return result;
        }

        private static InstancePx ToInstancePx(
            InstanceRoom room,
            ushort instanceId,
            InstanceStats stats,
            InstanceMask mask,
            PixelTransform transform)
        {
            InstanceStats.Box s = stats.BoxOf(instanceId);
            int w = mask.Width;
            int h = mask.Height;
            int boxWidth = s.MaxX - s.MinX + 1;
            int boxHeight = s.MaxY - s.MinY + 1;
            (int X, int Y) center = CenterInBox(mask, instanceId, s, boxWidth, boxHeight);

            return new InstancePx
            {
                InstanceId = instanceId,
                RoomId = room.RoomId,
                Type = room.Type,
                Name = room.Name,
                ClassId = room.ClassId,
                PolygonPx = room.PolygonM
                    .Select(transform.ToPixel)
                    .Select(p => new Point2D(PythonRounding.Round(p.X, 2), PythonRounding.Round(p.Y, 2)))
                    .ToArray(),
                BBoxPx = (s.MinX, s.MinY, s.MaxX, s.MaxY),
                BBoxCoco = (s.MinX, s.MinY, boxWidth, boxHeight),
                BBoxYolo = (
                    PythonRounding.Round((s.MinX + s.MaxX) / 2.0 / w, 5),
                    PythonRounding.Round((s.MinY + s.MaxY) / 2.0 / h, 5),
                    PythonRounding.Round((double)boxWidth / w, 5),
                    PythonRounding.Round((double)boxHeight / h, 5)),
                CentroidPx = new Point2D(
                    PythonRounding.Round((double)s.SumX / s.Count, 1),
                    PythonRounding.Round((double)s.SumY / s.Count, 1)),
                CenterPx = (s.MinX + center.X, s.MinY + center.Y),
                AreaPx = s.Count,
            };
        }

        /// <summary>
        /// The box crop is equivalent to the full mask here: everything outside
        /// the box is background for this instance, as the image edge is.
        /// </summary>
        private static (int X, int Y) CenterInBox(InstanceMask mask, ushort instanceId, InstanceStats.Box s, int boxWidth, int boxHeight)
        {
            ushort[] pixels = mask.Pixels;
            var crop = new bool[boxWidth * boxHeight];
            for (int y = 0; y < boxHeight; y++)
            {
                int rowStart = ((s.MinY + y) * mask.Width) + s.MinX;
                for (int x = 0; x < boxWidth; x++)
                {
                    crop[(y * boxWidth) + x] = pixels[rowStart + x] == instanceId;
                }
            }

            return LabelPoint.Find(crop, boxWidth, boxHeight, s.MinX, s.MinY);
        }
    }
}
