using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>A room as the instance label needs it: ids, names and its polygon in view meters.</summary>
    public sealed record InstanceRoom(int RoomId, string? Type, string Name, int? ClassId, IReadOnlyList<Point2D> PolygonM);

    /// <summary>One entry of the label's instances_px (spec v1 section 3.11).</summary>
    public sealed record InstancePx
    {
        public required int InstanceId { get; init; }

        public required int RoomId { get; init; }

        public string? Type { get; init; }

        public required string Name { get; init; }

        public int? ClassId { get; init; }

        /// <summary>Room polygon in pixels, 2 decimals. Always filled (a struct7k defect left it null).</summary>
        public required IReadOnlyList<Point2D> PolygonPx { get; init; }

        /// <summary>Inclusive pixel corners.</summary>
        public required (int X0, int Y0, int X1, int Y1) BBoxPx { get; init; }

        public required (int X, int Y, int W, int H) BBoxCoco { get; init; }

        /// <summary>Center and size, normalized to 0..1, 5 decimals.</summary>
        public required (double XC, double YC, double W, double H) BBoxYolo { get; init; }

        /// <summary>Mean of the instance's pixels, 1 decimal.</summary>
        public required Point2D CentroidPx { get; init; }

        /// <summary>Label anchor: always a pixel of the room.</summary>
        public required (int X, int Y) CenterPx { get; init; }

        /// <summary>Real pixel count in the instance mask, not the polygon area.</summary>
        public required int AreaPx { get; init; }
    }
}
