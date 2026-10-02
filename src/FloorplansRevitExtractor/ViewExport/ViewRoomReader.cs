using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the rooms of a view contribute: label records, mask geometry and review notes.</summary>
    /// <param name="RecordIdByRoom">Revit room element id to label room id, for doors and windows.</param>
    internal sealed record RoomReading(
        IReadOnlyList<RoomPreviewRecord> Records,
        IReadOnlyList<GeomElement> Elements,
        IReadOnlyDictionary<long, int> RecordIdByRoom,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// The rooms the view's cut plane passes through, whatever their level:
    /// a plan often shows several buildings or blocks whose levels differ
    /// (Snowdon Towers' L1 shows four blocks). Rooms must be placed, closed,
    /// in the view's phase and inside the crop. They are found by geometry
    /// rather than visibility, because a plan view often hides the Rooms
    /// category while still being the rooms' floor plan.
    /// </summary>
    internal static class ViewRoomReader
    {
        internal static RoomReading Read(Document doc, ViewSetup setup, ViewRegion crop)
        {
            var unmapped = new List<UnmappedRoomName>();
            var areaWarnings = new List<AreaMismatchWarning>();
            var records = new List<RoomPreviewRecord>();
            var elements = new List<GeomElement>();
            var idByRoom = new Dictionary<long, int>();
            var notes = new List<string>();
            int otherPhase = 0;
            int outside = 0;

            IEnumerable<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => CutByPlane(r, setup.CutPlaneZFeet))
                .OrderBy(r => r.Id.Value);

            foreach (Room room in rooms)
            {
                if (room.Location == null || room.Area <= 0)
                {
                    notes.Add($"Ambiente {room.Id.Value} descartado: {(room.Location == null ? "sem localização" : "área zero (não fechado)")}.");
                    continue;
                }

                if (!InViewPhase(room, setup.PhaseId))
                {
                    otherPhase++;
                    continue;
                }

                RoomPreviewRecord record = RoomRecordBuilder.Build(room, records.Count, setup.ToFrameMeters, unmapped, areaWarnings);
                if (!Inside(record.BBoxM, crop))
                {
                    outside++;
                    continue;
                }

                idByRoom[room.Id.Value] = record.Id;
                records.Add(record);
                elements.Add(GeomElement.Filled(
                    StructureClassNames.ForRoomType(record.Type),
                    room.Id.Value,
                    new[] { record.Polygon },
                    record.Holes));
            }

            if (otherPhase > 0)
            {
                notes.Add($"{otherPhase} ambiente(s) cortado(s) pelo plano em outra fase que a da vista ficaram fora.");
            }

            if (outside > 0)
            {
                notes.Add($"{outside} ambiente(s) cortado(s) pelo plano fora (ou parcialmente fora) do recorte ficaram fora.");
            }

            return new RoomReading(records, elements, idByRoom, notes);
        }

        /// <summary>A room's bounding box spans its computed volume, from its base to its upper limit.</summary>
        private static bool CutByPlane(Room room, double cutZFeet)
        {
            BoundingBoxXYZ box = room.get_BoundingBox(null);
            return box != null && box.Min.Z <= cutZFeet && cutZFeet <= box.Max.Z;
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
        /// A room cut by the crop edge would carry a polygon larger than its
        /// pixels and fail the centroid roundtrip; it is left out and noted.
        /// </summary>
        private static bool Inside((double MinX, double MinY, double MaxX, double MaxY) box, ViewRegion region) =>
            box.MinX >= region.MinX && box.MaxX <= region.MaxX && box.MinY >= region.MinY && box.MaxY <= region.MaxY;
    }
}
