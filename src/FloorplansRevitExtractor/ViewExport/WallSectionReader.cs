using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the walls of a view contribute to the sample.</summary>
    internal sealed record WallReading(
        IReadOnlyList<GeomElement> Elements,
        IReadOnlyList<GeomWarning> Warnings,
        IReadOnlyList<string> Notes,
        double? WallExtM,
        double? WallIntM,
        double? WallIntMinM,
        bool? ThinPartitions);

    /// <summary>
    /// Walls enter the mask by their section at the cut plane, with real
    /// thickness (spec v2, see <see cref="CutSection"/>). A thickness step
    /// shows as a step; a multi-layer wall is one polygon; a wall that does
    /// not reach the cut plane is not cut and not in the mask. Curtain walls are left out until the open question on
    /// them is decided.
    /// </summary>
    internal static class WallSectionReader
    {
        /// <summary>v1: an interior wall thinner than this makes thin_partitions true.</summary>
        private const double ThinPartitionM = 0.10;

        /// <summary>Spec v2: thinner than this in the mask, a wall vanishes when rasterized.</summary>
        private const double MinWallPx = 2.0;


        internal static WallReading Read(Document doc, ViewSetup setup, double pxPerM)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            var exterior = new List<double>();
            var interior = new List<double>();
            int curtain = 0;
            int uncut = 0;

            FilteredElementCollector collector = new FilteredElementCollector(doc, setup.View.Id).OfClass(typeof(Wall));
            if (setup.PhaseId != ElementId.InvalidElementId)
            {
                // A "show demolished" phase filter draws demolished walls: they are not walls of this plan.
                collector = collector.WherePasses(new ElementPhaseStatusFilter(
                    setup.PhaseId,
                    new List<ElementOnPhaseStatus> { ElementOnPhaseStatus.New, ElementOnPhaseStatus.Existing }));
            }

            // A stacked wall's members are walls of their own; the parent would count them twice.
            IEnumerable<Wall> walls = collector.Cast<Wall>().Where(w => !w.IsStackedWall).OrderBy(w => w.Id.Value);

            ElementGuard.Each(walls, warnings, wall =>
            {
                if (wall.WallType.Kind == WallKind.Curtain)
                {
                    curtain++;
                    return;
                }

                (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = CutSection.Of(wall, setup, warnings);
                if (outers.Count == 0)
                {
                    uncut++;
                    return;
                }

                elements.Add(GeomElement.Filled(StructureClass.Wall, wall.Id.Value, outers, holes));

                double widthM = UnitsConverter.ToMeters(wall.Width);
                (wall.WallType.Function == WallFunction.Exterior ? exterior : interior).Add(widthM);
                if (widthM * pxPerM < MinWallPx)
                {
                    warnings.Add(new GeomWarning(wall.Id.Value, $"parede com {widthM * pxPerM:F1} px de espessura: some na rasterização"));
                }
            });

            var notes = new List<string>();
            if (curtain > 0)
            {
                notes.Add($"{curtain} parede(s)-cortina deixada(s) fora da máscara (pergunta em aberto do spec).");
            }

            if (uncut > 0)
            {
                notes.Add($"{uncut} parede(s) visível(is) não chega(m) ao plano de corte e ficou(aram) fora da máscara.");
            }

            return new WallReading(
                elements,
                warnings,
                notes,
                Median(exterior),
                Median(interior),
                interior.Count == 0 ? (double?)null : interior.Min(),
                interior.Count == 0 ? (bool?)null : interior.Any(w => w < ThinPartitionM));
        }

        private static double? Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return null;
            }

            double[] sorted = values.OrderBy(v => v).ToArray();
            int mid = sorted.Length / 2;
            return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
        }
    }
}
