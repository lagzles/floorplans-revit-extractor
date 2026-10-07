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
    /// below the cut) is this plan's room, and the other is left out. Where
    /// nothing of this floor covers it, it is open to below: a void (9).
    /// </summary>
    internal static class ViewRoomReader
    {
        /// <summary>Rooms of one floor only touch along wall faces; more shared area than this is stacking.</summary>
        private const double MaxSharedFraction = 0.2;

        internal static RoomReading Read(Document doc, ViewSetup setup, ViewRegion crop, SlabCoverage slabs)
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
            int doubleHeight = 0;
            int risingThrough = 0;
            int underSlab = 0;
            var candidates = new List<(Room Room, RoomPreviewRecord Record, double BaseZ)>();

            IEnumerable<Room> rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => CutByPlane(r, setup.CutZFor(r)))
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
                if (FromFloorBelow(room, setup))
                {
                    // Spec v4: the upper half of a double-height room is a void of this plan
                    // (vazio de pé-direito), not one of its rooms. A stair or shaft room rising
                    // through every floor keeps its own class: painted as void, it would cover
                    // the stair (the void layer comes after the stair layer).
                    StructureClass below = MaskClass(candidate);
                    bool keepsClass = below == StructureClass.Stair || below == StructureClass.Void;
                    if (keepsClass)
                    {
                        elements.Add(GeomElement.Filled(below, room.Id.Value, new[] { candidate.Polygon }, candidate.Holes));
                        risingThrough++;
                        continue;
                    }

                    // Only the part with no floor of this plan over it is open to below (a
                    // mezzanine covers the rest); a room all under the slab has its upper
                    // limit modelled high and is not a double height.
                    IReadOnlyList<PolygonWithHoles> open = slabs.OpenPart(candidate.Polygon, candidate.Holes);
                    if (open.Count == 0)
                    {
                        underSlab++;
                        continue;
                    }

                    elements.Add(GeomElement.Filled(
                        StructureClass.Void,
                        room.Id.Value,
                        open.Select(p => p.Outer).ToList(),
                        open.SelectMany(p => p.Holes).ToList()));
                    doubleHeight++;
                    continue;
                }

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

            if (doubleHeight > 0)
            {
                notes.Add($"{doubleHeight} ambiente(s) de pé-direito duplo do pavimento inferior: a parte sem laje deste pavimento por cima pintada como vazio (9), fora do label.");
            }

            if (underSlab > 0)
            {
                notes.Add($"{underSlab} ambiente(s) de pavimento inferior que atravessa(m) o corte sob laje deste pavimento ficaram fora (limite superior alto, não pé-direito duplo).");
            }

            if (risingThrough > 0)
            {
                notes.Add($"{risingThrough} escada(s)/poço(s) de pavimento inferior que atravessa(m) o corte: classe própria na máscara, fora do label.");
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

        /// <summary>A room whose level is below the view's: it reaches this cut plane only by its double height.</summary>
        private static bool FromFloorBelow(Room room, ViewSetup setup)
        {
            const double ToleranceFeet = 0.01;
            return room.Level != null && room.Level.ProjectElevation < setup.Level.ProjectElevation - ToleranceFeet;
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
