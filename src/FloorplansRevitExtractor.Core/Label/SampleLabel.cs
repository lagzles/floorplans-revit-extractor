using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// labels/NNNNNN.json: the training dataset's schema field by field,
    /// plus source, the one field spec v1 allows adding. seed is always null
    /// for a Revit sample and mask_mode always "structure", so neither is
    /// stored here.
    /// </summary>
    public sealed record SampleLabel
    {
        public const string MaskMode = "structure";

        public required SourceFragment Source { get; init; }

        public required ParamsFragment Params { get; init; }

        public double? Width { get; init; }

        public double? Height { get; init; }

        public double? BuiltArea { get; init; }

        public double? WallExt { get; init; }

        public double? WallInt { get; init; }

        /// <summary>rect, L, U, T, or null when not classifiable.</summary>
        public string? Envelope { get; init; }

        public required IReadOnlyList<Point2D> Outline { get; init; }

        public required IReadOnlyList<RoomPreviewRecord> Rooms { get; init; }

        public required IReadOnlyList<DoorRecord> Doors { get; init; }

        public required IReadOnlyList<WindowRecord> Windows { get; init; }

        public required IReadOnlyList<FurnitureRecord> Furniture { get; init; }

        public required ConnectivityGraph ConnectivityGraph { get; init; }

        public required string Caption { get; init; }

        public required RenderStyle RenderStyle { get; init; }

        /// <summary>[width, height] of the PNG, equal to the transform's.</summary>
        public required (int Width, int Height) ImageSize { get; init; }

        public required PixelTransform Transform { get; init; }

        public required IReadOnlyList<InstancePx> InstancesPx { get; init; }

        public required string File { get; init; }

        /// <summary>
        /// Rooms with no pixel in the instance mask (hidden under another room
        /// or thinner than a pixel), so missing from instances_px. Not written
        /// to the label: it goes to review.json.
        /// </summary>
        public required IReadOnlyList<int> RoomsWithoutPixels { get; init; }
    }
}
