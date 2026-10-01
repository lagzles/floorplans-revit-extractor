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
    /// thickness (spec v2): each wall solid is cut with the half-space below
    /// the plane, and the up-facing planar faces left at that height are the
    /// section. A thickness step shows as a step; a multi-layer wall is one
    /// polygon; a wall that does not reach the cut plane is not cut and not
    /// in the mask. Curtain walls are left out until the open question on
    /// them is decided.
    /// </summary>
    internal static class WallSectionReader
    {
        /// <summary>v1: an interior wall thinner than this makes thin_partitions true.</summary>
        private const double ThinPartitionM = 0.10;

        /// <summary>Spec v2: thinner than this in the mask, a wall vanishes when rasterized.</summary>
        private const double MinWallPx = 2.0;

        private const double FaceHeightToleranceFeet = 1e-4;
        private const double UpFacingZ = 0.999;
        private const double MinRingAreaM2 = 1e-8;

        internal static WallReading Read(Document doc, ViewSetup setup, double pxPerM)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            var exterior = new List<double>();
            var interior = new List<double>();
            int curtain = 0;
            int uncut = 0;
            var cutPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ.Negate(), new XYZ(0, 0, setup.CutPlaneZFeet));
            ViewRegion clip = setup.ModelRegion; // Revit clips model graphics at the crop: so does the mask

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

            foreach (Wall wall in walls)
            {
                if (wall.WallType.Kind == WallKind.Curtain)
                {
                    curtain++;
                    continue;
                }

                IReadOnlyList<IReadOnlyList<Point2D>> rings = SectionRings(wall, cutPlane, setup, warnings)
                    .Select(r => RectangleClip.Clip(r, clip.MinX, clip.MinY, clip.MaxX, clip.MaxY))
                    .Where(r => r.Count >= 3)
                    .ToList();
                if (rings.Count == 0)
                {
                    uncut++;
                    continue;
                }

                (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = SplitOutersAndHoles(rings);
                elements.Add(GeomElement.Filled(StructureClass.Wall, wall.Id.Value, outers, holes));

                double widthM = UnitsConverter.ToMeters(wall.Width);
                (wall.WallType.Function == WallFunction.Exterior ? exterior : interior).Add(widthM);
                if (widthM * pxPerM < MinWallPx)
                {
                    warnings.Add(new GeomWarning(wall.Id.Value, $"parede com {widthM * pxPerM:F1} px de espessura: some na rasterização"));
                }
            }

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

        private static IReadOnlyList<IReadOnlyList<Point2D>> SectionRings(
            Wall wall,
            Plane cutPlane,
            ViewSetup setup,
            List<GeomWarning> warnings)
        {
            var rings = new List<IReadOnlyList<Point2D>>();
            var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            foreach (Solid solid in Solids(wall.get_Geometry(options)))
            {
                Solid below;
                try
                {
                    below = BooleanOperationsUtils.CutWithHalfSpace(solid, cutPlane);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    warnings.Add(new GeomWarning(wall.Id.Value, $"corte da parede falhou no Revit: {ex.Message}"));
                    continue;
                }

                if (below == null || below.Volume <= 0)
                {
                    continue;
                }

                foreach (PlanarFace face in below.Faces.OfType<PlanarFace>())
                {
                    bool isSection = face.FaceNormal.Z > UpFacingZ
                        && Math.Abs(face.Origin.Z - setup.CutPlaneZFeet) < FaceHeightToleranceFeet;
                    if (isSection)
                    {
                        rings.AddRange(face.GetEdgesAsCurveLoops().Select(loop => Ring(loop, setup)).Where(r => r.Count >= 3));
                    }
                }
            }

            return rings;
        }

        private static IEnumerable<Solid> Solids(GeometryElement geometry)
        {
            if (geometry == null)
            {
                yield break;
            }

            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 0)
                {
                    yield return solid;
                }
                else if (obj is GeometryInstance instance)
                {
                    foreach (Solid nested in Solids(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }
                }
            }
        }

        /// <summary>Curves tessellated, each curve's last point dropped (it starts the next one).</summary>
        private static IReadOnlyList<Point2D> Ring(CurveLoop loop, ViewSetup setup)
        {
            var points = new List<Point2D>();
            foreach (Curve curve in loop)
            {
                IList<XYZ> tessellated = curve.Tessellate();
                for (int i = 0; i < tessellated.Count - 1; i++)
                {
                    points.Add(setup.ToFrame(tessellated[i]));
                }
            }

            return points;
        }

        /// <summary>
        /// A face's loops are its outline and its holes; with several faces, each
        /// ring that lies inside a larger one is a hole. The Core assigns holes
        /// to their polygon.
        /// </summary>
        private static (List<IReadOnlyList<Point2D>> Outers, List<IReadOnlyList<Point2D>> Holes) SplitOutersAndHoles(
            IReadOnlyList<IReadOnlyList<Point2D>> rings)
        {
            // A zero-area loop (a degenerate sliver of the cut) has no inside to probe.
            var ordered = rings.Where(r => PolygonGeometry.Area(r) > MinRingAreaM2).OrderByDescending(PolygonGeometry.Area).ToList();
            var outers = new List<IReadOnlyList<Point2D>>();
            var holes = new List<IReadOnlyList<Point2D>>();
            foreach (IReadOnlyList<Point2D> ring in ordered)
            {
                Point2D probe = PolygonGeometry.InteriorPoint(ring);
                int depth = outers.Concat(holes).Count(other => PolygonGeometry.Contains(other, probe));
                (depth % 2 == 0 ? outers : holes).Add(ring);
            }

            return (outers, holes);
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
