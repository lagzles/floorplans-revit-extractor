using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the rooms of a view contribute: label records, mask geometry and review notes.</summary>
    internal sealed record RoomReading(
        IReadOnlyList<RoomPreviewRecord> Records,
        IReadOnlyList<GeomElement> Elements,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Rooms of the view's level and phase, placed and closed (unplaced or
    /// unbounded rooms are dropped and noted), inside the image. Polygons on
    /// the finish face, in the view frame. Rooms are found by level and phase
    /// rather than by visibility, because a plan view often hides the Rooms
    /// category while still being the room's floor plan.
    /// </summary>
    internal static class ViewRoomReader
    {
        internal static RoomReading Read(Document doc, ViewSetup setup, ViewRegion imageRegion)
        {
            (IList<Room> valid, IList<RoomCollector.DiscardedRoom> discarded) = RoomCollector.CollectValidRooms(doc, setup.Level.Id);
            var unmapped = new List<UnmappedRoomName>();
            var areaWarnings = new List<AreaMismatchWarning>();
            var records = new List<RoomPreviewRecord>();
            var elements = new List<GeomElement>();
            int otherPhase = 0;
            int outside = 0;

            foreach (Room room in valid)
            {
                if (!InViewPhase(room, setup.PhaseId))
                {
                    otherPhase++;
                    continue;
                }

                RoomPreviewRecord record = RoomRecordBuilder.Build(room, records.Count, setup.ToFrameMeters, unmapped, areaWarnings);
                if (!Inside(record.BBoxM, imageRegion))
                {
                    outside++;
                    continue;
                }

                records.Add(record);
                elements.Add(GeomElement.Filled(
                    StructureClassNames.ForRoomType(record.Type),
                    room.Id.Value,
                    new[] { record.Polygon },
                    record.Holes));
            }

            var notes = new List<string>();
            notes.AddRange(discarded.Select(d => $"Ambiente {d.ElementId.Value} descartado: {d.Reason}."));
            if (otherPhase > 0)
            {
                notes.Add($"{otherPhase} ambiente(s) do nível em outra fase que a da vista ficaram fora.");
            }

            if (outside > 0)
            {
                notes.Add($"{outside} ambiente(s) do nível fora (ou parcialmente fora) do recorte ficaram fora.");
            }

            return new RoomReading(records, elements, notes);
        }

        private static bool InViewPhase(Room room, ElementId viewPhase)
        {
            if (viewPhase == ElementId.InvalidElementId)
            {
                return true;
            }

            ElementId roomPhase = room.get_Parameter(BuiltInParameter.ROOM_PHASE_ID)?.AsElementId();
            return roomPhase == null || roomPhase == ElementId.InvalidElementId || roomPhase == viewPhase;
        }

        /// <summary>
        /// A room cut by the image edge would carry a polygon larger than its
        /// pixels and fail the centroid roundtrip; it is left out and noted.
        /// </summary>
        private static bool Inside((double MinX, double MinY, double MaxX, double MaxY) box, ViewRegion region) =>
            box.MinX >= region.MinX && box.MaxX <= region.MaxX && box.MinY >= region.MinY && box.MaxY <= region.MaxY;
    }
}
