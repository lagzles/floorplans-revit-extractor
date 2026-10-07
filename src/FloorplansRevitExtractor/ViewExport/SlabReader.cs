using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>Where this plan has a slab under foot: floors and roofs, outlines and their openings.</summary>
    internal sealed record SlabCoverage(IReadOnlyList<IReadOnlyList<Point2D>> Outers, IReadOnlyList<IReadOnlyList<Point2D>> Holes)
    {
        /// <summary>The part of a room with no slab of this plan over it (an opening counts as open).</summary>
        internal IReadOnlyList<PolygonWithHoles> OpenPart(IReadOnlyList<Point2D> outer, IReadOnlyList<IReadOnlyList<Point2D>> holes) =>
            PolygonDifference.Subtract(outer, holes, Outers, Holes);
    }

    /// <summary>
    /// The top faces of the floors and roofs this plan stands on (top between
    /// the view range's bottom and the cut plane, level by level). A room of
    /// the floor below that reaches the cut plane is open to below only where
    /// none of them covers it (a mezzanine covers part of a two-storey room);
    /// an opening in the slab over a double-height space is a hole of its top
    /// face, so it does not count as covered.
    /// </summary>
    internal static class SlabReader
    {
        internal static SlabCoverage Read(Document doc, ViewSetup setup)
        {
            var outers = new List<IReadOnlyList<Point2D>>();
            var holes = new List<IReadOnlyList<Point2D>>();
            IEnumerable<HostObject> slabs = new FilteredElementCollector(doc, setup.View.Id)
                .WherePasses(new ElementMulticategoryFilter(new List<BuiltInCategory> { BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs }))
                .WhereElementIsNotElementType()
                .OfType<HostObject>();

            foreach (HostObject slab in slabs)
            {
                BoundingBoxXYZ box = slab.get_BoundingBox(null);
                if (box == null || box.Max.Z < setup.BottomZFor(slab) || box.Max.Z > setup.CutZFor(slab))
                {
                    continue;
                }

                IList<Reference> tops;
                try
                {
                    tops = HostObjectUtils.GetTopFaces(slab);
                }
                catch (Autodesk.Revit.Exceptions.ArgumentException)
                {
                    continue; // a roof by extrusion or a slab with no top face: it covers nothing here
                }

                foreach (Reference reference in tops)
                {
                    if (slab.GetGeometryObjectFromReference(reference) is Face face)
                    {
                        (List<IReadOnlyList<Point2D>> o, List<IReadOnlyList<Point2D>> h) =
                            CutSection.SplitOutersAndHoles(face.GetEdgesAsCurveLoops().Select(l => CutSection.Ring(l, setup)).ToList());
                        outers.AddRange(o);
                        holes.AddRange(h);
                    }
                }
            }

            return new SlabCoverage(outers, holes);
        }
    }
}
