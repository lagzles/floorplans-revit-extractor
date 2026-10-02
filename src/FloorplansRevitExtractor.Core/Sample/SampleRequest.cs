using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Sample
{
    /// <summary>
    /// Everything the Revit side hands over for one view: the exported image,
    /// the geometry records and the label facts. The Core does the rest:
    /// sample id, masks, label, gate, files.
    /// </summary>
    public sealed record SampleRequest
    {
        public required SampleKey Key { get; init; }

        /// <summary>The PNG exactly as Revit exported it; it is re-encoded as RGB 8-bit.</summary>
        public required byte[] ImagePng { get; init; }

        /// <summary>Geometry in view meters; its transform is the one measured on the exported image.</summary>
        public required GeomDocument Geom { get; init; }

        public required SourceFragment Source { get; init; }

        public required BuildingMeasures Building { get; init; }

        public required IReadOnlyList<RoomPreviewRecord> Rooms { get; init; }

        public required IReadOnlyList<DoorRecord> Doors { get; init; }

        public required IReadOnlyList<WindowRecord> Windows { get; init; }

        public required IReadOnlyList<FurnitureRecord> Furniture { get; init; }

        public required RenderStyle RenderStyle { get; init; }

        /// <summary>Things a person should look at, from the extraction (links ignored, curtain walls skipped...).</summary>
        public required IReadOnlyList<string> ReviewNotes { get; init; }

        /// <summary>
        /// How many doors, windows and pillars were extracted (spec v2 count
        /// check: against the blobs in the mask, within 5%). Classes left out
        /// are not checked.
        /// </summary>
        public IReadOnlyDictionary<StructureClass, int> ExpectedCounts { get; init; } = new Dictionary<StructureClass, int>();
    }
}
