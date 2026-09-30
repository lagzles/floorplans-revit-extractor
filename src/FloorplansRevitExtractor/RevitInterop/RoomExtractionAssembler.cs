using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Vocabulary;

namespace FloorplansRevitExtractor.RevitInterop
{
    internal static class RoomExtractionAssembler
    {
        private const string PluginVersion = "0.1.0";
        private const double AreaMismatchThreshold = 0.02;

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
            List<AreaMismatchWarning> areaWarnings)
        {
            (IReadOnlyList<Point2D> outer, IReadOnlyList<IReadOnlyList<Point2D>> holes) =
                RoomBoundaryExtractor.GetFinishBoundary(room);

            string rawName = room.Name;
            string normalized = RoomTypeVocabulary.Normalize(rawName);
            bool mapped = RoomTypeVocabulary.TryMapType(normalized, out string type);

            if (!mapped)
            {
                unmappedNames.Add(new UnmappedRoomName(room.Id.Value, rawName, normalized));
            }

            string zone = mapped ? RoomZoneTable.GetZone(type) : null;
            int? classId = mapped ? RoomTypeCatalog.GetClassId(type) : (int?)null;

            double area = PolygonGeometry.NetArea(outer, holes);
            double revitAreaM2 = UnitsConverter.SquareFeetToSquareMeters(room.Area);
            if (revitAreaM2 > 0)
            {
                double diffPct = Math.Abs(area - revitAreaM2) / revitAreaM2;
                if (diffPct > AreaMismatchThreshold)
                {
                    areaWarnings.Add(new AreaMismatchWarning(id, area, revitAreaM2, diffPct));
                }
            }

            IReadOnlyList<Point2D> centerLoop = RoomBoundaryExtractor.GetCenterOuterLoopOrNull(room);
            IReadOnlyList<(double X, double Y, double W, double H)> axisRects =
                RectilinearDecomposer.TryDecompose(centerLoop);

            return new RoomPreviewRecord
            {
                Id = id,
                Type = type,
                Name = rawName,
                Zone = zone,
                ClassId = classId,
                Polygon = outer,
                Holes = holes,
                AxisRects = axisRects,
                Area = area,
                Centroid = PolygonGeometry.Centroid(outer),
                BBoxM = PolygonGeometry.BBox(outer),
                RevitAreaM2 = revitAreaM2,
            };
        }

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
