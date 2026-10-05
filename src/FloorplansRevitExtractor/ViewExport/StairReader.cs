using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the stairs of a view contribute to the mask.</summary>
    internal sealed record StairReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Stairs as the projection of their runs and landings
    /// (GetFootprintBoundary), painted as one filled area: the treads drawn
    /// stay in the image only (spec v2). A stair that crosses levels keeps
    /// only what this view shows: the components between the bottom of the
    /// view range and its cut plane. Sketched (non-component) stairs have no
    /// run footprints: their upward faces projected to plan stand in. A ramp
    /// is sloped floor with no steps (spec v4, is_ramp): 1 room, by the same
    /// projected faces.
    /// </summary>
    internal static class StairReader
    {
        internal static StairReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();

            IEnumerable<Stairs> stairs = new FilteredElementCollector(doc, setup.View.Id)
                .OfClass(typeof(Stairs))
                .Cast<Stairs>()
                .OrderBy(s => s.Id.Value);

            ElementGuard.Each(stairs, warnings, stair =>
            {
                if (!Stairs.IsByComponent(doc, stair.Id))
                {
                    AddFootprint(elements, StructureClass.Stair, stair, TopFootprint.Of(stair, setup));
                    return;
                }

                IEnumerable<Element> components = stair.GetStairsRuns().Concat(stair.GetStairsLandings()).Select(doc.GetElement).Where(c => c != null);
                List<IReadOnlyList<Point2D>> rings = components
                    .Where(c => Shown(c, setup.BottomZFeet, setup.CutPlaneZFeet))
                    .Select(c => Footprint(c, setup))
                    .Where(r => r != null)
                    .ToList();
                List<IReadOnlyList<Point2D>> clipped = CutSection.Clip(rings, setup.ModelRegion);
                if (clipped.Count > 0)
                {
                    // Overlapping run and landing polygons are united by the painter.
                    elements.Add(GeomElement.Filled(StructureClass.Stair, stair.Id.Value, clipped));
                }
            });

            IEnumerable<Element> ramps = new FilteredElementCollector(doc, setup.View.Id)
                .OfCategory(BuiltInCategory.OST_Ramps)
                .WhereElementIsNotElementType()
                .Where(r => Shown(r, setup.BottomZFeet, setup.CutPlaneZFeet))
                .OrderBy(r => r.Id.Value);
            ElementGuard.Each(ramps, warnings, ramp => AddFootprint(elements, StructureClass.Room, ramp, TopFootprint.Of(ramp, setup)));

            return new StairReading(elements, warnings, new string[0]);
        }

        private static void AddFootprint(List<GeomElement> elements, StructureClass cls, Element element, List<IReadOnlyList<Point2D>> rings)
        {
            if (rings.Count > 0)
            {
                elements.Add(GeomElement.Filled(cls, element.Id.Value, rings));
            }
        }

        private static bool Shown(Element component, double floorZ, double cutZ)
        {
            BoundingBoxXYZ box = component.get_BoundingBox(null);
            return box != null && box.Min.Z <= cutZ && box.Max.Z >= floorZ;
        }

        private static IReadOnlyList<Point2D> Footprint(Element component, ViewSetup setup)
        {
            CurveLoop loop = component switch
            {
                StairsRun run => run.GetFootprintBoundary(),
                StairsLanding landing => landing.GetFootprintBoundary(),
                _ => null,
            };

            return loop == null ? null : CutSection.Ring(loop, setup);
        }
    }
}
