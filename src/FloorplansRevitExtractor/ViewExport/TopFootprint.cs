using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// An element's plan footprint as the union of its upward-facing faces
    /// projected to plan: a ramp's slope, a sketched stair's treads and
    /// landings. Every face loop is returned as an outline; the painter
    /// unites them, so overlapping treads never cancel each other.
    /// </summary>
    internal static class TopFootprint
    {
        /// <summary>Faces tilted up by more than about 6 degrees count as seen from above.</summary>
        private const double UpwardZ = 0.1;

        internal static List<IReadOnlyList<Point2D>> Of(Element element, ViewSetup setup)
        {
            var rings = new List<IReadOnlyList<Point2D>>();
            var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            foreach (Solid solid in Solids(element.get_Geometry(options)))
            {
                foreach (Face face in solid.Faces)
                {
                    XYZ normal = face.ComputeNormal(new UV(0.5, 0.5));
                    if (normal.Z > UpwardZ)
                    {
                        rings.AddRange(face.GetEdgesAsCurveLoops().Select(loop => CutSection.Ring(loop, setup)));
                    }
                }
            }

            return CutSection.Clip(rings, setup.ModelRegion);
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
