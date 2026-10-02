using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>A door's swing: the mask element, and where its leaf turns and opens to (model feet).</summary>
    internal sealed record Swing(GeomElement Element, XYZ Hinge, XYZ ArcMiddle);

    /// <summary>
    /// A door's swing (spec v2, class 8): the arc and the leaf, from the
    /// symbolic curves the door draws in this view (Options.View). Of the
    /// door's plan lines, the swing is each bounded arc plus each straight
    /// line from the arc's center (the hinge) as long as its radius (the open
    /// leaf); frames, sills and the jamb-to-jamb line are left out, and a full
    /// circle (a knob, a revolving drum) has no ends and is skipped. Stroked
    /// 4 cm wide, like the generator; the painter keeps it on room pixels.
    /// </summary>
    internal static class SwingReader
    {
        /// <summary>Spec v2: a 4 cm stroke in the world.</summary>
        internal const double StrokeM = 0.04;

        private const double EndToleranceFeet = 0.02;
        private const double LeafLengthTolerance = 0.15;

        /// <returns>The swing, or null when the door draws no open arc in this view.</returns>
        internal static Swing Read(FamilyInstance door, ViewSetup setup)
        {
            List<Curve> curves = PlanCurves(door.get_Geometry(new Options { View = setup.View })).ToList();
            List<Arc> arcs = curves.OfType<Arc>().Where(a => a.IsBound).ToList();
            if (arcs.Count == 0)
            {
                return null;
            }

            var polylines = new List<IReadOnlyList<Point2D>>();
            foreach (Arc arc in arcs)
            {
                polylines.Add(Polyline(arc, setup));
                polylines.AddRange(curves.OfType<Line>().Where(line => line.IsBound && IsLeafOf(line, arc)).Select(line => Polyline(line, setup)));
            }

            Arc first = arcs[0];
            return new Swing(
                GeomElement.Stroked(StructureClass.Swing, door.Id.Value, polylines, StrokeM),
                first.Center,
                first.Evaluate(0.5, true));
        }

        /// <summary>The leaf runs from the hinge (the arc's center) and is as long as the arc's radius.</summary>
        private static bool IsLeafOf(Line line, Arc arc)
        {
            XYZ hinge = Flat(arc.Center);
            bool fromHinge = hinge.DistanceTo(Flat(line.GetEndPoint(0))) < EndToleranceFeet
                             || hinge.DistanceTo(Flat(line.GetEndPoint(1))) < EndToleranceFeet;
            return fromHinge && Math.Abs(line.Length - arc.Radius) <= arc.Radius * LeafLengthTolerance;
        }

        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

        private static IReadOnlyList<Point2D> Polyline(Curve curve, ViewSetup setup) =>
            curve.Tessellate().Select(setup.ToFrame).ToArray();

        private static IEnumerable<Curve> PlanCurves(GeometryElement geometry)
        {
            if (geometry == null)
            {
                yield break;
            }

            foreach (GeometryObject obj in geometry)
            {
                if (obj is Curve curve)
                {
                    yield return curve;
                }
                else if (obj is GeometryInstance instance)
                {
                    foreach (Curve nested in PlanCurves(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
