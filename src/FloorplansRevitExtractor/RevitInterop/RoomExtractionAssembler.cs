using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.RevitInterop
{
    internal static class RoomExtractionAssembler
    {
        private const string PluginVersion = "0.1.0";

        internal static RoomsPreviewDocument Build(Document doc, UIApplication uiApp, Level level)
        {
            (IList<Room> validRooms, IList<RoomCollector.DiscardedRoom> discarded) =
                RoomCollector.CollectValidRooms(doc, level.Id);

            var unmappedNames = new List<UnmappedRoomName>();
            var areaWarnings = new List<AreaMismatchWarning>();
            var records = new List<RoomPreviewRecord>();

            int nextId = 0;
            foreach (Room room in validRooms)
            {
                RoomPreviewRecord record = BuildRoomRecord(room, nextId, unmappedNames, areaWarnings);
                records.Add(record);
                nextId++;
            }

            SourceFragment source = BuildSource(doc, uiApp, level);
            ParamsFragment parameters = BuildParams(records);

            var debug = new DebugFragment
            {
                DiscardedRooms = discarded
                    .Select(d => new DiscardedRoom(d.ElementId.Value, d.Reason))
                    .ToList(),
                UnmappedNames = unmappedNames,
                AreaMismatchWarnings = areaWarnings,
            };

            return new RoomsPreviewDocument
            {
                Source = source,
                Params = parameters,
                Rooms = records,
                Debug = debug,
            };
        }

        private static RoomPreviewRecord BuildRoomRecord(
            Room room,
            int id,
            List<UnmappedRoomName> unmappedNames,
            List<AreaMismatchWarning> areaWarnings) =>
            RoomRecordBuilder.Build(room, id, p => p, unmappedNames, areaWarnings);

        private static ParamsFragment BuildParams(IReadOnlyList<RoomPreviewRecord> records) => new ParamsFragment
        {
            NBedrooms = records.Count(r => r.Type == "bedroom"),
            NBathrooms = records.Count(r => r.Type == "bathroom"),
            TotalArea = records.Sum(r => r.Area),
            Garage = records.Any(r => r.Type == "garage" || r.Type == "parking"),
        };

        private static SourceFragment BuildSource(Document doc, UIApplication uiApp, Level level)
        {
            ViewPlan associatedView = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .FirstOrDefault(v => !v.IsTemplate && v.GenLevel != null && v.GenLevel.Id == level.Id);

            return new SourceFragment
            {
                DocTitle = doc.Title,
                DocGuid = doc.ProjectInformation.UniqueId,
                RevitVersion = uiApp.Application.VersionNumber,
                Mode = "level",
                LevelId = level.Id.Value,
                LevelName = level.Name,
                LevelElevationM = UnitsConverter.ToMeters(level.Elevation),
                ViewId = associatedView?.Id.Value,
                ViewName = associatedView?.Name,
                ViewScaleDen = associatedView?.Scale,
                SheetNumber = null,
                ExportedAt = DateTimeOffset.Now.ToString("o"),
                PluginVersion = PluginVersion,
            };
        }
    }
}
