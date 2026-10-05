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
    /// <summary>What the grids of a view contribute to the mask.</summary>
    internal sealed record GridReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Grids (spec v3, class 10) as strokes: the line as this view shows it
    /// (its view-specific extent, not the 3D model extent) and the circle of
    /// each bubble visible at an end; spec v4: the bubble's letter too, as the
    /// ink box inside the circle, class 10.
    /// A dash-dot grid is painted as one continuous line, as the generator
    /// does. A grid type with no center segment paints only its end segments.
    /// Only grids visible in the exported view are collected.
    /// </summary>
    internal static class GridReader
    {
        /// <summary>The Center Segment parameter of a grid type: the line is drawn only near its ends.</summary>
        private const int CenterSegmentNone = 1;

        private const int CircleSegments = 48;

        /// <summary>The letter is looked for in the square inside the bubble: this share of the radius.</summary>
        private const double LetterShareOfRadius = 0.72;

        internal static GridReading Read(Document doc, ViewSetup setup, InkBoxes ink)
        {
            int lettersMissing = 0;
            var elements = new List<GeomElement>();
            var letters = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int bubblesMissing = 0;
            int bubblesFromHead = 0;
            View view = setup.View;
            var headCircles = new Dictionary<long, Arc>();

            IEnumerable<Grid> grids = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Grid)).Cast<Grid>().OrderBy(g => g.Id.Value);
            ElementGuard.Each(grids, warnings, grid =>
            {
                var polylines = new List<IReadOnlyList<Point2D>>();
                var closed = new List<bool>();
                IList<Curve> curves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                double endLengthFeet = EndSegmentLengthFeet(grid, view);
                foreach (Curve curve in curves.Where(c => c.IsBound))
                {
                    foreach (Curve drawn in DrawnPieces(curve, endLengthFeet))
                    {
                        polylines.Add(drawn.Tessellate().Select(setup.ToFrame).ToArray());
                        closed.Add(false);
                    }
                }

                List<Arc> heads = Circles(grid.get_Geometry(new Options { View = view })).ToList();
                Arc headCircle = heads.Count == 0 ? HeadFamilyCircle(grid, headCircles) : null;
                foreach (DatumEnds end in new[] { DatumEnds.End0, DatumEnds.End1 })
                {
                    if (!grid.IsBubbleVisibleInView(end, view) || curves.Count == 0)
                    {
                        continue;
                    }

                    XYZ tip = curves[0].GetEndPoint(end == DatumEnds.End0 ? 0 : 1);
                    Arc bubble = heads.OrderBy(a => Flat(a.Center).DistanceTo(Flat(tip))).FirstOrDefault(a => Flat(a.Center).DistanceTo(Flat(tip)) <= a.Radius * 3);
                    (XYZ Center, double Radius)? circle = null;
                    if (bubble != null)
                    {
                        circle = (bubble.Center, bubble.Radius);
                    }
                    else if (headCircle != null && curves[0] is Line line)
                    {
                        // The head family is drawn on paper with its origin at the grid end and
                        // its up axis pointing out along the grid: scale and place it there.
                        XYZ outward = end == DatumEnds.End0 ? line.Direction.Negate() : line.Direction;
                        XYZ left = XYZ.BasisZ.CrossProduct(outward);
                        double scale = view.Scale;
                        circle = (tip + (outward * headCircle.Center.Y * scale) + (left * -headCircle.Center.X * scale), headCircle.Radius * scale);
                        bubblesFromHead++;
                    }

                    if (circle == null)
                    {
                        bubblesMissing++;
                        continue;
                    }

                    polylines.Add(Circle(circle.Value.Center, circle.Value.Radius, setup));
                    closed.Add(true);
                    double half = UnitsConverter.ToMeters(circle.Value.Radius * LetterShareOfRadius);
                    IReadOnlyList<Point2D> letter = ink.Fit(setup.ToFrame(circle.Value.Center), new Vector2D(1, 0), half, half);
                    if (letter == null)
                    {
                        lettersMissing++;
                    }
                    else
                    {
                        letters.Add(GeomElement.Filled(StructureClass.Grid, grid.Id.Value, new[] { letter }).WithLabel(null, grid.Name));
                    }
                }

                if (polylines.Count > 0)
                {
                    elements.Add(GeomElement.Stroked(StructureClass.Grid, grid.Id.Value, polylines, SwingReader.StrokeM, closed));
                }
            });

            elements.AddRange(letters);
            var notes = new List<string>();
            if (lettersMissing > 0)
            {
                notes.Add($"{lettersMissing} bolha(s) de eixo sem tinta de letra dentro do círculo: letra fora da máscara.");
            }

            if (bubblesFromHead > 0)
            {
                notes.Add($"{bubblesFromHead} bolha(s) de eixo desenhada(s) pelo círculo da família de cabeça (a vista não devolve a geometria da bolha).");
            }

            if (bubblesMissing > 0)
            {
                notes.Add($"{bubblesMissing} bolha(s) de eixo visível(is) sem círculo legível: só a linha entrou.");
            }

            return new GridReading(elements, warnings, notes);
        }

        /// <summary>The whole line, or only its two end segments when the type hides the center segment.</summary>
        private static IEnumerable<Curve> DrawnPieces(Curve curve, double endLengthFeet)
        {
            if (endLengthFeet <= 0 || !(curve is Line line) || line.Length <= 2 * endLengthFeet)
            {
                return new[] { curve };
            }

            XYZ start = line.GetEndPoint(0);
            XYZ end = line.GetEndPoint(1);
            return new Curve[]
            {
                Line.CreateBound(start, start + (line.Direction * endLengthFeet)),
                Line.CreateBound(end - (line.Direction * endLengthFeet), end),
            };
        }

        /// <summary>The end segment length in model feet when the type has no center segment; 0 otherwise.</summary>
        private static double EndSegmentLengthFeet(Grid grid, View view)
        {
            Element type = grid.Document.GetElement(grid.GetTypeId());
            Parameter style = type?.get_Parameter(BuiltInParameter.GRID_CENTER_SEGMENT_STYLE);
            if (style == null || !style.HasValue || style.AsInteger() != CenterSegmentNone)
            {
                return 0;
            }

            Parameter length = type.get_Parameter(BuiltInParameter.GRID_END_SEGMENTS_LENGTH);
            return length != null && length.HasValue ? length.AsDouble() * view.Scale : 0;
        }

        private static IReadOnlyList<Point2D> Circle(XYZ center, double radius, ViewSetup setup) =>
            Enumerable.Range(0, CircleSegments)
                .Select(i => 2 * Math.PI * i / CircleSegments)
                .Select(a => setup.ToFrame(new XYZ(center.X + (radius * Math.Cos(a)), center.Y + (radius * Math.Sin(a)), center.Z)))
                .ToArray();

        /// <summary>
        /// The largest circle of the grid type's head family (GRID_HEAD_TAG), in
        /// the family's paper units, or null when the family has no readable circle.
        /// An annotation symbol returns no geometry, so the family document is
        /// opened read-only (no transaction may be open) and closed unsaved.
        /// One read per head family.
        /// </summary>
        private static Arc HeadFamilyCircle(Grid grid, IDictionary<long, Arc> cache)
        {
            Document doc = grid.Document;
            Parameter headParameter = doc.GetElement(grid.GetTypeId())?.get_Parameter(BuiltInParameter.GRID_HEAD_TAG);
            if (headParameter == null || !headParameter.HasValue || !(doc.GetElement(headParameter.AsElementId()) is FamilySymbol head))
            {
                return null;
            }

            long familyId = head.Family.Id.Value;
            if (!cache.TryGetValue(familyId, out Arc circle))
            {
                circle = FamilyCircle(doc, head.Family);
                cache[familyId] = circle;
            }

            return circle;
        }

        private static Arc FamilyCircle(Document doc, Family family)
        {
            if (family.IsInPlace || !family.IsEditable)
            {
                return null;
            }

            Document familyDoc = null;
            try
            {
                familyDoc = doc.EditFamily(family);
                return new FilteredElementCollector(familyDoc)
                    .OfClass(typeof(CurveElement))
                    .Cast<CurveElement>()
                    .Select(c => c.GeometryCurve)
                    .OfType<Arc>()
                    .OrderByDescending(a => a.Radius)
                    .FirstOrDefault();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null; // the family cannot be opened here: the bubble is noted as missing
            }
            finally
            {
                familyDoc?.Close(false);
            }
        }

        private static XYZ Flat(XYZ p) => new XYZ(p.X, p.Y, 0);

        private static IEnumerable<Arc> Circles(GeometryElement geometry)
        {
            if (geometry == null)
            {
                yield break;
            }

            foreach (GeometryObject obj in geometry)
            {
                if (obj is Arc arc)
                {
                    yield return arc;
                }
                else if (obj is GeometryInstance instance)
                {
                    foreach (Arc nested in Circles(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }
}
