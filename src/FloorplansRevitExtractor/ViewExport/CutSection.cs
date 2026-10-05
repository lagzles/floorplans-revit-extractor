using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// What an element looks like cut by the view's cut plane, in the view
    /// frame (spec v2: walls and pillars enter by their section, not by the
    /// projection of the whole solid, so a pillar that widens above the cut
    /// does not come out larger). Each solid is cut with the half-space below
    /// the plane; the up-facing planar faces left at that height are the
    /// section. Rings are clipped to the view crop, as Revit clips graphics.
    /// </summary>
    internal static class CutSection
    {
        private const double FaceHeightToleranceFeet = 1e-4;
        private const double UpFacingZ = 0.999;
        private const double MinRingAreaM2 = 1e-8;

        /// <returns>Section outlines and holes; both empty when the element is not cut.</returns>
        internal static (List<IReadOnlyList<Point2D>> Outers, List<IReadOnlyList<Point2D>> Holes) Of(
            Element element,
            ViewSetup setup,
            List<GeomWarning> warnings)
        {
            double cutZ = setup.CutZFor(element);
            var cutPlane = Plane.CreateByNormalAndOrigin(XYZ.BasisZ.Negate(), new XYZ(0, 0, cutZ));
            var rings = new List<IReadOnlyList<Point2D>>();
            var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            foreach (Solid solid in Solids(element.get_Geometry(options)))
            {
                Solid below;
                try
                {
                    below = BooleanOperationsUtils.CutWithHalfSpace(solid, cutPlane);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
                {
                    warnings.Add(new GeomWarning(element.Id.Value, $"corte no plano falhou no Revit: {ex.Message}"));
                    continue;
                }

                if (below == null || below.Volume <= 0)
                {
                    continue;
                }

                foreach (PlanarFace face in below.Faces.OfType<PlanarFace>())
                {
                    bool isSection = face.FaceNormal.Z > UpFacingZ
                        && Math.Abs(face.Origin.Z - cutZ) < FaceHeightToleranceFeet;
                    if (isSection)
                    {
                        rings.AddRange(face.GetEdgesAsCurveLoops().Select(loop => Ring(loop, setup)));
                    }
                }
            }

            return SplitOutersAndHoles(Clip(rings, setup.ModelRegion));
        }

        /// <summary>Curves tessellated into the view frame, each curve's last point dropped (it starts the next one).</summary>
        internal static IReadOnlyList<Point2D> Ring(IEnumerable<Curve> loop, ViewSetup setup)
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

        internal static List<IReadOnlyList<Point2D>> Clip(IEnumerable<IReadOnlyList<Point2D>> rings, ViewRegion crop) =>
            rings.Select(r => RectangleClip.Clip(r, crop.MinX, crop.MinY, crop.MaxX, crop.MaxY))
                .Where(r => r.Count >= 3 && PolygonGeometry.Area(r) > MinRingAreaM2)
                .ToList();

        /// <summary>
        /// A face's loops are its outline and its holes; across several faces,
        /// a ring inside an odd number of larger rings is a hole. The Core then
        /// gives each hole to its polygon.
        /// </summary>
        internal static (List<IReadOnlyList<Point2D>> Outers, List<IReadOnlyList<Point2D>> Holes) SplitOutersAndHoles(
            IEnumerable<IReadOnlyList<Point2D>> rings)
        {
            var ordered = rings.OrderByDescending(PolygonGeometry.Area).ToList();
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
    }
}
