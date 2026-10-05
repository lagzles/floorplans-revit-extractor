using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the exterior floors of a view contribute to the mask.</summary>
    internal sealed record ExteriorFloorReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings);

    /// <summary>
    /// Paved outdoor area (spec v2: deck, sidewalk, patio floor) enters the
    /// mask as 1 room. A floor counts as outdoor when its type's Function is
    /// Exterior, the modeller's own statement, so no building outline has to
    /// be guessed. Its top face is the footprint, holes kept. It has no Room,
    /// so it is floor in the mask but not a room of the label nor an instance.
    /// An uncovered patio with no floor modelled stays background.
    /// </summary>
    internal static class ExteriorFloorReader
    {
        /// <summary>The Function parameter of wall and floor types: 0 interior, 1 exterior.</summary>
        private const int ExteriorFunction = 1;

        internal static ExteriorFloorReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            FilteredElementCollector collector = new FilteredElementCollector(doc, setup.View.Id).OfClass(typeof(Floor));
            if (setup.PhaseId != ElementId.InvalidElementId)
            {
                collector = collector.WherePasses(new ElementPhaseStatusFilter(
                    setup.PhaseId,
                    new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.New, ElementOnPhaseStatus.Existing }));
            }

            IEnumerable<Floor> floors = collector.Cast<Floor>().Where(IsExterior).OrderBy(f => f.Id.Value);
            ElementGuard.Each(floors, warnings, floor =>
            {
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null || box.Max.Z < setup.BottomZFeet || box.Max.Z > setup.CutPlaneZFeet)
                {
                    return;
                }

                foreach (Reference reference in HostObjectUtils.GetTopFaces(floor))
                {
                    if (!(floor.GetGeometryObjectFromReference(reference) is Face face))
                    {
                        continue;
                    }

                    List<IReadOnlyList<Point2D>> rings = CutSection.Clip(face.GetEdgesAsCurveLoops().Select(l => CutSection.Ring(l, setup)), setup.ModelRegion);
                    (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = CutSection.SplitOutersAndHoles(rings);
                    if (outers.Count > 0)
                    {
                        elements.Add(GeomElement.Filled(StructureClass.Room, floor.Id.Value, outers, holes));
                    }
                }
            });

            return new ExteriorFloorReading(elements, warnings);
        }

        private static bool IsExterior(Floor floor)
        {
            Parameter function = floor.FloorType?.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            return function != null && function.HasValue && function.AsInteger() == ExteriorFunction;
        }
    }
}
