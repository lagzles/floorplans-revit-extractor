using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Vocabulary;

namespace FloorplansRevitExtractor.RevitInterop
{
    /// <summary>
    /// One Room as a label record: vocabulary type, zone, class id, finish-face
    /// polygon with holes, axis rects, area and centroid, all taken through
    /// <c>toFrame</c> (model meters to the coordinates the record is written in).
    /// </summary>
    internal static class RoomRecordBuilder
    {
        private const double AreaMismatchThreshold = 0.02;

        internal static RoomPreviewRecord Build(
            Room room,
            int id,
            Func<Point2D, Point2D> toFrame,
            List<UnmappedRoomName> unmappedNames,
            List<AreaMismatchWarning> areaWarnings)
        {
            (IReadOnlyList<Point2D> outerModel, IReadOnlyList<IReadOnlyList<Point2D>> holesModel) =
                RoomBoundaryExtractor.GetFinishBoundary(room);
            IReadOnlyList<Point2D> outer = Map(outerModel, toFrame);
            IReadOnlyList<IReadOnlyList<Point2D>> holes = holesModel.Select(h => Map(h, toFrame)).ToList();

            string rawName = room.Name;
            string normalized = RoomTypeVocabulary.Normalize(rawName);
            bool mapped = RoomTypeVocabulary.TryMapType(normalized, out string type);
            if (!mapped)
            {
                unmappedNames.Add(new UnmappedRoomName(room.Id.Value, rawName, normalized));
            }

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

            IReadOnlyList<Point2D> centerLoopModel = RoomBoundaryExtractor.GetCenterOuterLoopOrNull(room);
            IReadOnlyList<Point2D> centerLoop = centerLoopModel == null ? null : Map(centerLoopModel, toFrame);

            return new RoomPreviewRecord
            {
                Id = id,
                Type = type,
                Name = rawName,
                Zone = mapped ? RoomZoneTable.GetZone(type) : null,
                ClassId = mapped ? RoomTypeCatalog.GetClassId(type) : (int?)null,
                Polygon = outer,
                Holes = holes,
                AxisRects = RectilinearDecomposer.TryDecompose(centerLoop),
                Area = area,
                // The gate projects it and compares with the pixels' mean, which leaves the holes out.
                Centroid = PolygonGeometry.NetCentroid(outer, holes),
                BBoxM = PolygonGeometry.BBox(outer),
                RevitAreaM2 = revitAreaM2,
            };
        }

        private static IReadOnlyList<Point2D> Map(IReadOnlyList<Point2D> ring, Func<Point2D, Point2D> toFrame) =>
            ring.Select(toFrame).ToArray();
    }
}
