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
    /// shows as a step; a multi-layer wall is one polygon. Spec v4: a curtain
    /// wall at the cut plane is 15 curtain_wall, its outline (location line by
    /// its thickness, widened to 2 px when thinner: the only widening the spec
    /// allows); a wall whose top is below the cut plane (parapet, low wall) is
    /// 14 low_wall, its plan outline with its real thickness.
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
            int curtainWidened = 0;
            int low = 0;
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
                BoundingBoxXYZ box = wall.get_BoundingBox(null);
                if (wall.WallType.Kind == WallKind.Curtain)
                {
                    if (box == null || box.Min.Z > setup.CutPlaneZFeet || box.Max.Z < setup.CutPlaneZFeet)
                    {
                        uncut++;
                        return;
                    }

                    double thicknessM = Math.Max(UnitsConverter.ToMeters(wall.Width), MinWallPx / pxPerM);
                    curtainWidened += UnitsConverter.ToMeters(wall.Width) * pxPerM < MinWallPx ? 1 : 0;
                    AddOutline(elements, StructureClass.CurtainWall, wall, thicknessM, setup);
                    curtain++;
                    return;
                }

                (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = CutSection.Of(wall, setup, warnings);
                if (outers.Count == 0)
                {
                    bool belowCut = box != null && box.Max.Z < setup.CutPlaneZFeet && box.Max.Z >= setup.BottomZFeet;
                    if (belowCut && AddOutline(elements, StructureClass.LowWall, wall, UnitsConverter.ToMeters(wall.Width), setup))
                    {
                        low++;
                    }
                    else
                    {
                        uncut++;
                    }

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
                notes.Add($"{curtain} parede(s)-cortina como pele de vidro (15)"
                    + (curtainWidened > 0 ? $"; {curtainWidened} engrossada(s) para 2 px." : "."));
            }

            if (low > 0)
            {
                notes.Add($"{low} parede(s) abaixo do plano de corte como mureta (14), pelo contorno em projeção.");
            }

            if (uncut > 0)
            {
                notes.Add($"{uncut} parede(s) visível(is) fora do plano de corte e acima do fundo da vista ficaram fora da máscara.");
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

        /// <summary>
        /// The wall's plan outline: its location line offset to both sides by
        /// half the thickness. Joins and end caps are not drawn, which is close
        /// enough for curtain walls and parapets.
        /// </summary>
        private static bool AddOutline(List<GeomElement> elements, StructureClass cls, Wall wall, double thicknessM, ViewSetup setup)
        {
            if (!(wall.Location is LocationCurve location) || !location.Curve.IsBound)
            {
                return false;
            }

            double half = UnitsConverter.ToFeet(thicknessM) / 2;
            Curve curve = location.Curve;
            IList<XYZ> left = curve.CreateOffset(half, XYZ.BasisZ).Tessellate();
            IList<XYZ> right = curve.CreateOffset(-half, XYZ.BasisZ).Tessellate();
            IReadOnlyList<Point2D> ring = left.Concat(right.Reverse()).Select(setup.ToFrame).ToArray();
            List<IReadOnlyList<Point2D>> clipped = CutSection.Clip(new[] { ring }, setup.ModelRegion);
            if (clipped.Count == 0)
            {
                return false;
            }

            elements.Add(GeomElement.Filled(cls, wall.Id.Value, clipped));
            return true;
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
