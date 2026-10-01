using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Vocabulary;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Assembles the label from what the Revit side measured, deriving what
    /// the dataset derives (params counts, connectivity graph, caption,
    /// envelope, instances_px). It first checks referential consistency
    /// (spec v1 gate item 7): a label that points at a room that does not
    /// exist is rejected here, not discovered in training.
    /// </summary>
    public static class LabelAssembler
    {
        private static readonly Regex FileName = new Regex(@"^\d{6}\.png$", RegexOptions.CultureInvariant);

        public static SampleLabel Build(LabelInputs inputs)
        {
            RequireNotNull(inputs);
            Validate(inputs);
            IReadOnlyList<RoomPreviewRecord> rooms = inputs.Rooms;
            BuildingMeasures building = inputs.Building;
            IReadOnlyList<InstancePx> instances = InstancePixels.Build(
                inputs.InstanceMask,
                inputs.Transform,
                rooms.Select(r => new InstanceRoom(r.Id, r.Type, r.Name, r.ClassId, r.Polygon)).ToList());

            return new SampleLabel
            {
                Source = inputs.Source,
                Params = BuildParams(rooms, building),
                Width = building.Width,
                Height = building.Height,
                BuiltArea = building.BuiltArea,
                WallExt = building.WallExt,
                WallInt = building.WallInt,
                Envelope = EnvelopeClassifier.Classify(building.Outline),
                Outline = building.Outline,
                Rooms = rooms,
                Doors = inputs.Doors,
                Windows = inputs.Windows,
                Furniture = inputs.Furniture,
                ConnectivityGraph = ConnectivityGraph.Build(rooms, inputs.Doors),
                Caption = CaptionBuilder.Build(rooms.Select(r => r.Type), building.BuiltArea),
                RenderStyle = inputs.RenderStyle,
                ImageSize = (inputs.Transform.Width, inputs.Transform.Height),
                Transform = inputs.Transform,
                InstancesPx = instances,
                File = inputs.File,
                RoomsWithoutPixels = rooms.Select(r => r.Id).Where(id => !instances.Any(i => i.RoomId == id)).ToList(),
            };
        }

        /// <summary>A null list or name fails here with its name, not as a NullReferenceException deep inside.</summary>
        private static void RequireNotNull(LabelInputs inputs)
        {
            var required = new (object? Value, string Name)[]
            {
                (inputs, nameof(inputs)),
                (inputs.Source, nameof(inputs.Source)),
                (inputs.Building, nameof(inputs.Building)),
                (inputs.Building?.Outline, "Building.Outline"),
                (inputs.Rooms, nameof(inputs.Rooms)),
                (inputs.Doors, nameof(inputs.Doors)),
                (inputs.Windows, nameof(inputs.Windows)),
                (inputs.Furniture, nameof(inputs.Furniture)),
                (inputs.RenderStyle, nameof(inputs.RenderStyle)),
                (inputs.Transform, nameof(inputs.Transform)),
                (inputs.InstanceMask, nameof(inputs.InstanceMask)),
            };

            string? missing = required.Where(r => r.Value == null).Select(r => r.Name).FirstOrDefault();
            if (missing != null)
            {
                throw new ArgumentNullException(missing);
            }

            RoomPreviewRecord? unnamed = inputs.Rooms.FirstOrDefault(r => r?.Name == null);
            if (unnamed != null || inputs.Rooms.Any(r => r == null))
            {
                throw new ArgumentException($"Every room needs a name; room {unnamed?.Id.ToString() ?? "null"} has none.", nameof(inputs));
            }
        }

        private static ParamsFragment BuildParams(IReadOnlyList<RoomPreviewRecord> rooms, BuildingMeasures building) => new ParamsFragment
        {
            NBedrooms = rooms.Count(r => r.Type == "bedroom"),
            NBathrooms = rooms.Count(r => r.Type == "bathroom"),
            TotalArea = building.TotalArea,
            Garage = rooms.Any(r => r.Type == "garage" || r.Type == "parking"),
            Building = building.BuildingType,
            ThinPartitions = building.ThinPartitions,
            WallIntMin = building.WallIntMin,
        };

        private static void Validate(LabelInputs inputs)
        {
            if (!FileName.IsMatch(inputs.File ?? string.Empty))
            {
                throw new ArgumentException($"Sample file must be six digits and .png, got \"{inputs.File}\".", nameof(inputs));
            }

            if (inputs.Building.BuildingType != null)
            {
                LabelVocabularies.RequireBuildingType(inputs.Building.BuildingType);
            }

            ValidateRooms(inputs.Rooms);
            var roomIds = new HashSet<int>(inputs.Rooms.Select(r => r.Id));
            RequireUniqueIds(inputs.Doors.Select(d => d.Id), "door");
            RequireUniqueIds(inputs.Windows.Select(w => w.Id), "window");
            RequireUniqueIds(inputs.Furniture.Select(f => f.Id), "furniture");

            foreach (RoomPreviewRecord room in inputs.Rooms)
            {
                RequireRoom(roomIds, room.SuiteOf, $"room {room.Id} suite_of");
            }

            foreach (DoorRecord door in inputs.Doors)
            {
                RequireRoom(roomIds, door.RoomA, $"door {door.Id} room_a");
                RequireRoom(roomIds, door.RoomB, $"door {door.Id} room_b");
                RequireRoom(roomIds, door.SwingInto, $"door {door.Id} swing_into");
            }

            foreach (WindowRecord window in inputs.Windows)
            {
                RequireRoom(roomIds, window.Room, $"window {window.Id} room");
            }

            foreach (FurnitureRecord item in inputs.Furniture)
            {
                RequireRoom(roomIds, item.Room, $"furniture {item.Id} room");
            }
        }

        private static void ValidateRooms(IReadOnlyList<RoomPreviewRecord> rooms)
        {
            for (int i = 0; i < rooms.Count; i++)
            {
                RoomPreviewRecord room = rooms[i];
                if (room.Id != i)
                {
                    throw new ArgumentException($"Room ids must be sequential from 0 in order; position {i} has id {room.Id}.");
                }

                int? expectedClassId = room.Type == null ? (int?)null : RoomTypeCatalog.GetClassId(room.Type);
                string? expectedZone = room.Type == null ? null : RoomZoneTable.GetZone(room.Type);
                if (room.ClassId != expectedClassId || room.Zone != expectedZone)
                {
                    throw new ArgumentException(
                        $"Room {room.Id} of type {room.Type ?? "null"} has class_id {room.ClassId?.ToString() ?? "null"} and zone {room.Zone ?? "null"}; "
                        + $"the vocabulary says class_id {expectedClassId?.ToString() ?? "null"} and zone {expectedZone ?? "null"}.");
                }
            }
        }

        private static void RequireRoom(HashSet<int> roomIds, int? roomId, string where)
        {
            if (roomId.HasValue && !roomIds.Contains(roomId.Value))
            {
                throw new ArgumentException($"{where} points at room {roomId.Value}, which does not exist.");
            }
        }

        private static void RequireUniqueIds(IEnumerable<int> ids, string what)
        {
            int? duplicate = ids.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => (int?)g.Key).FirstOrDefault();
            if (duplicate.HasValue)
            {
                throw new ArgumentException($"Duplicate {what} id {duplicate.Value}.");
            }
        }
    }
}
