using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.Core.Vocabulary;
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
    /// category while still being the rooms' floor plan. A double-height room
    /// of the floor below also reaches the cut plane and lies under this
    /// floor's rooms: where rooms overlap, the one based highest (closest
    /// below the cut) is this plan's room, and the other is left out.
    /// </summary>
    internal static class ViewRoomReader
    {
        /// <summary>Rooms of one floor only touch along wall faces; more shared area than this is stacking.</summary>
        private const double MaxSharedFraction = 0.2;

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
            int stacked = 0;
            int patios = 0;
            var candidates = new List<(Room Room, RoomPreviewRecord Record, double BaseZ)>();

            IEnumerable<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => CutByPlane(r, setup.CutPlaneZFeet))
                .OrderBy(r => r.Id.Value)
                .ToList();

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

                RoomPreviewRecord record = RoomRecordBuilder.Build(room, 0, setup.ToFrameMeters, unmapped, areaWarnings);
                if (!Inside(record.BBoxM, crop))
                {
                    outside++;
                    continue;
                }

                candidates.Add((room, record, room.get_BoundingBox(null).Min.Z));
            }

            var kept = new List<(Room Room, RoomPreviewRecord Record)>();
            foreach ((Room room, RoomPreviewRecord record, double _) in candidates.OrderByDescending(c => c.BaseZ).ThenBy(c => c.Room.Id.Value))
            {
                if (PolygonOverlap.SharedFraction(record.Polygon, kept.Select(k => k.Record.Polygon)) > MaxSharedFraction)
                {
                    stacked++;
                    continue;
                }

                kept.Add((room, record));
            }

            // Ids follow the Revit element ids, so they do not depend on the overlap pass.
            foreach ((Room room, RoomPreviewRecord candidate) in kept.OrderBy(k => k.Room.Id.Value))
            {
                RoomPreviewRecord record = candidate with { Id = records.Count };
                idByRoom[room.Id.Value] = record.Id;
                records.Add(record);
                StructureClass cls = MaskClass(record);
                if (cls == StructureClass.Background)
                {
                    patios++; // spec v4: an open patio is left background, as the generator does
                    continue;
                }

                elements.Add(GeomElement.Filled(cls, room.Id.Value, new[] { record.Polygon }, record.Holes));
            }

            if (patios > 0)
            {
                notes.Add($"{patios} pátio(s) descoberto(s) no label e fora da máscara (fica fundo, como no gerador).");
            }

            if (stacked > 0)
            {
                notes.Add($"{stacked} ambiente(s) de pavimento inferior (pé-direito duplo) sob ambientes deste pavimento ficaram fora.");
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

        /// <summary>Spec v4: a room named as a ramp is floor even when its type says stairs.</summary>
        private static StructureClass MaskClass(RoomPreviewRecord record)
        {
            string name = RoomTypeVocabulary.Normalize(record.Name);
            return name.Contains("RAMPA") || name.Contains("RAMP") ? StructureClass.Room : StructureClassNames.ForRoomType(record.Type);
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
