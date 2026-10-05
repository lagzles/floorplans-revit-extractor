using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the shafts and floor openings of a view contribute to the mask.</summary>
    internal sealed record VoidReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Inner voids of the building (spec v2, class 9): shaft openings that
    /// pass through the cut plane and floor openings within the view range,
    /// by their sketched boundary. They are found in the whole document and
    /// filtered by geometry, because view templates often hide the Shaft
    /// Openings category while the shaft is still there. A shaft enclosed by
    /// walls stays void inside with the walls around it (voids are painted
    /// before walls). Holes sketched as inner loops of a floor are not Opening
    /// elements and are not seen; elevator shafts drawn as rooms become void
    /// through the room vocabulary.
    /// </summary>
    internal static class VoidReader
    {
        /// <summary>Sketch curves closer than this (1 mm) are joined into one loop.</summary>
        private const double JoinToleranceM = 0.001;

        internal static VoidReading Read(Document doc, ViewSetup setup)
        {
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int unreadable = 0;

            IEnumerable<Opening> openings = new FilteredElementCollector(doc)
                .OfClass(typeof(Opening))
                .Cast<Opening>()
                .Where(IsVoidCategory)
                .OrderBy(o => o.Id.Value);

            ElementGuard.Each(openings, warnings, opening =>
            {
                BoundingBoxXYZ box = opening.get_BoundingBox(null);
                if (box == null || box.Min.Z > setup.CutZFor(opening) || box.Max.Z < setup.BottomZFor(opening))
                {
                    return;
                }

                List<IReadOnlyList<Point2D>> rings = CutSection.Clip(Loops(opening, setup), setup.ModelRegion);
                if (rings.Count == 0)
                {
                    unreadable++;
                    return;
                }

                (List<IReadOnlyList<Point2D>> outers, List<IReadOnlyList<Point2D>> holes) = CutSection.SplitOutersAndHoles(rings);
                elements.Add(GeomElement.Filled(StructureClass.Void, opening.Id.Value, outers, holes));
            });

            IReadOnlyList<string> notes = unreadable == 0
                ? new string[0]
                : new[] { $"{unreadable} shaft(s)/furo(s) de laje sem contorno fechado ficaram fora da máscara." };
            return new VoidReading(elements, warnings, notes);
        }

        private static bool IsVoidCategory(Opening opening)
        {
            long category = opening.Category?.Id.Value ?? 0;
            return category == (long)BuiltInCategory.OST_ShaftOpening || category == (long)BuiltInCategory.OST_FloorOpening;
        }

        /// <summary>
        /// The sketch as closed rings: a rectangle given by two corners, or the
        /// boundary curves (one flat list holding every loop, in no particular
        /// order) chained end to end.
        /// </summary>
        private static IReadOnlyList<IReadOnlyList<Point2D>> Loops(Opening opening, ViewSetup setup)
        {
            if (opening.IsRectBoundary)
            {
                IList<XYZ> corners = opening.BoundaryRect;
                if (corners == null || corners.Count < 2)
                {
                    return new IReadOnlyList<Point2D>[0];
                }

                XYZ a = corners[0];
                XYZ b = corners[1];
                return new[]
                {
                    new[] { new XYZ(a.X, a.Y, 0), new XYZ(b.X, a.Y, 0), new XYZ(b.X, b.Y, 0), new XYZ(a.X, b.Y, 0) }
                        .Select(setup.ToFrame)
                        .ToArray(),
                };
            }

            CurveArray curves = opening.BoundaryCurves;
            if (curves == null || curves.Size == 0)
            {
                return new IReadOnlyList<Point2D>[0];
            }

            IReadOnlyList<Point2D>[] pieces = curves.Cast<Curve>()
                .Where(c => c.IsBound)
                .Select(c => (IReadOnlyList<Point2D>)c.Tessellate().Select(setup.ToFrame).ToArray())
                .ToArray();
            return PolylineChainer.Chain(pieces, JoinToleranceM);
        }
    }
}
