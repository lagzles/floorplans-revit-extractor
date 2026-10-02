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
    /// view range and its cut plane. Legacy sketch-based stairs and ramps have no
    /// components and are noted, not guessed.
    /// </summary>
    internal static class StairReader
    {
        internal static StairReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int legacy = 0;
            int ramps = new FilteredElementCollector(doc, setup.View.Id).OfCategory(BuiltInCategory.OST_Ramps).WhereElementIsNotElementType().GetElementCount();

            IEnumerable<Stairs> stairs = new FilteredElementCollector(doc, setup.View.Id)
                .OfClass(typeof(Stairs))
                .Cast<Stairs>()
                .OrderBy(s => s.Id.Value);

            ElementGuard.Each(stairs, warnings, stair =>
            {
                if (!Stairs.IsByComponent(doc, stair.Id))
                {
                    legacy++;
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

            var notes = new List<string>();
            if (legacy > 0)
            {
                notes.Add($"{legacy} escada(s) por esboço (sem lances/patamares) ficaram fora da máscara.");
            }

            if (ramps > 0)
            {
                notes.Add($"{ramps} rampa(s) visível(is) ainda não entra(m) na máscara (spec: rampa = 6 stair).");
            }

            return new StairReading(elements, warnings, notes);
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
