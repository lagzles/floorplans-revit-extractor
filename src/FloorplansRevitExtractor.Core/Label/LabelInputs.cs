using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>Everything the label is assembled from, all in the view frame.</summary>
    public sealed record LabelInputs
    {
        public required SourceFragment Source { get; init; }

        public required BuildingMeasures Building { get; init; }

        /// <summary>Ids sequential from 0, polygons on the finish face (spec v1 3.3).</summary>
        public required IReadOnlyList<RoomPreviewRecord> Rooms { get; init; }

        public required IReadOnlyList<DoorRecord> Doors { get; init; }

        public required IReadOnlyList<WindowRecord> Windows { get; init; }

        public required IReadOnlyList<FurnitureRecord> Furniture { get; init; }

        public required RenderStyle RenderStyle { get; init; }

        /// <summary>The transform measured on the exported image.</summary>
        public required PixelTransform Transform { get; init; }

        public required InstanceMask InstanceMask { get; init; }

        /// <summary>Image file name: six digits and .png, from 900001 up.</summary>
        public required string File { get; init; }
    }
}
