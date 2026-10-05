using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the floors of a view contribute to the mask.</summary>
    internal sealed record FloorReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Every modelled floor of this floor of the building enters the mask as 1
    /// room, by its top face with its holes: paved outdoor area (deck,
    /// sidewalk), and indoor floor with no Room on it (a restroom left without
    /// a room), so the fixtures standing on it are painted. A shaft or floor
    /// opening is a hole in the slab and stays out. A floor has no Room, so it
    /// is floor in the mask but not a room of the label nor an instance. A
    /// slab mostly under an open patio is skipped: the patio stays background
    /// (spec v4).
    /// </summary>
    internal static class FloorReader
    {
        /// <summary>A floor piece this much under an open patio is the patio's paving: left out.</summary>
        private const double MaxPatioShare = 0.5;

        internal static FloorReading Read(Document doc, ViewSetup setup, IReadOnlyList<IReadOnlyList<Point2D>> patios)
        {
            int painted = 0;
            int underPatio = 0;
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            FilteredElementCollector collector = new FilteredElementCollector(doc, setup.View.Id).OfClass(typeof(Floor));
            if (setup.PhaseId != ElementId.InvalidElementId)
            {
                collector = collector.WherePasses(new ElementPhaseStatusFilter(
                    setup.PhaseId,
                    new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.New, ElementOnPhaseStatus.Existing }));
            }

            IEnumerable<Floor> floors = collector.Cast<Floor>().OrderBy(f => f.Id.Value);
            ElementGuard.Each(floors, warnings, floor =>
            {
                BoundingBoxXYZ box = floor.get_BoundingBox(null);
                if (box == null || box.Max.Z < setup.BottomZFor(floor) || box.Max.Z > setup.CutZFor(floor))
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
                    int before = outers.Count;
                    outers = outers.Where(o => PolygonOverlap.SharedFraction(o, patios) <= MaxPatioShare).ToList();
                    underPatio += before - outers.Count;
                    if (outers.Count > 0)
                    {
                        // A hole left without its outer ring would be an error in the rasterizer.
                        holes = holes.Where(h => outers.Any(o => PolygonGeometry.Contains(o, PolygonGeometry.InteriorPoint(h)))).ToList();
                        elements.Add(GeomElement.Filled(StructureClass.Room, floor.Id.Value, outers, holes));
                        painted++;
                    }
                }
            });

            var notes = new List<string> { $"{painted} face(s) de piso modelado pintada(s) como piso (1), com ou sem ambiente em cima." };
            if (underPatio > 0)
            {
                notes.Add($"{underPatio} trecho(s) de piso sob pátio descoberto ficaram fundo.");
            }

            return new FloorReading(elements, warnings, notes);
        }
    }
}
