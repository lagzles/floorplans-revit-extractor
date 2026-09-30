using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace FloorplansRevitExtractor.RevitInterop
{
    internal static class RoomCollector
    {
        internal readonly struct DiscardedRoom
        {
            internal DiscardedRoom(ElementId elementId, string reason)
            {
                ElementId = elementId;
                Reason = reason;
            }

            internal ElementId ElementId { get; }

            internal string Reason { get; }
        }

        internal static (IList<Room> Valid, IList<DiscardedRoom> Discarded) CollectValidRooms(
            Document doc,
            ElementId levelId)
        {
            var valid = new List<Room>();
            var discarded = new List<DiscardedRoom>();

            IEnumerable<Room> roomsOnLevel = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.LevelId == levelId)
                .OrderBy(r => r.Id.Value);

            foreach (Room room in roomsOnLevel)
            {
                if (room.Location == null)
                {
                    discarded.Add(new DiscardedRoom(room.Id, "sem localização"));
                    continue;
                }

                if (room.Area <= 0)
                {
                    discarded.Add(new DiscardedRoom(room.Id, "área zero (não fechado)"));
                    continue;
                }

                valid.Add(room);
            }

            return (valid, discarded);
        }

        internal static IReadOnlyDictionary<ElementId, int> CountRoomsPerLevel(Document doc)
        {
            var counts = new Dictionary<ElementId, int>();

            IEnumerable<Room> allRooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>();

            foreach (Room room in allRooms)
            {
                if (room.LevelId == null || room.LevelId == ElementId.InvalidElementId)
                {
                    continue;
                }

                counts.TryGetValue(room.LevelId, out int current);
                counts[room.LevelId] = current + 1;
            }

            return counts;
        }
    }
}
