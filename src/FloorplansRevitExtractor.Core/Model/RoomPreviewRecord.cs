using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Model
{
    /// <summary>
    /// Room fragment of the future struct7k label (spec section 3.3), limited
    /// to what is derivable from Room geometry alone. <see cref="Open"/> and
    /// <see cref="SuiteOf"/> require door/adjacency data from a future
    /// increment, so they always come out as <c>false</c>/<c>null</c> here.
    /// </summary>
    public sealed record RoomPreviewRecord
    {
        public required int Id { get; init; }

        public string? Type { get; init; }

        public required string Name { get; init; }

        public string? Zone { get; init; }

        public int? ClassId { get; init; }

        public bool Open { get; init; } = false;

        public int? SuiteOf { get; init; } = null;

        public required IReadOnlyList<Point2D> Polygon { get; init; }

        public required IReadOnlyList<IReadOnlyList<Point2D>> Holes { get; init; }

        public required IReadOnlyList<(double X, double Y, double W, double H)> AxisRects { get; init; }

        public required double Area { get; init; }

        public required Point2D Centroid { get; init; }

        public required (double MinX, double MinY, double MaxX, double MaxY) BBoxM { get; init; }

        /// <summary>Not written to JSON - only used for the area cross-check warning.</summary>
        public required double RevitAreaM2 { get; init; }
    }
}
