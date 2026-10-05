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
    /// <summary>What the dimensions of a view contribute to the mask.</summary>
    internal sealed record DimensionReading(IReadOnlyList<GeomElement> Elements, IReadOnlyList<GeomWarning> Warnings, IReadOnlyList<string> Notes);

    /// <summary>
    /// Linear and chain dimensions (spec v3, class 11) as strokes: the
    /// dimension line between its first and last reference, plus the witness
    /// lines and ticks the view draws (Options.View), without the text of the
    /// value. When the view geometry has no such lines (Revit 2024 returns
    /// none for dimensions), they are rebuilt as the spec says: a tick at every
    /// reference, and a witness line from each referenced element to the
    /// dimension line, with the type's gap to element and extension. The
    /// choice is recorded in the report. Angular, radial, diameter, arc-length and spot
    /// dimensions stay in the image only: the generator has no such case.
    /// Spec v4: the value is class 11 too, as the ink box around the text
    /// position (InkBoxes), sized from the type's text size.
    /// </summary>
    internal static class DimensionReader
    {
        /// <summary>Paper size of a drawn tick (2.5 mm) and of the text box excluded around a value (3 mm).</summary>
        private const double TickPaperFeet = 0.0082;
        private const double TextPaperFeet = 0.0098;

        /// <summary>Loose value box, in text heights: half its height, and half the width per character.</summary>
        private const double ValueHalfHeights = 0.9;
        private const double ValueHalfWidthPerChar = 0.45;

        internal static DimensionReading Read(Document doc, ViewSetup setup, InkBoxes ink)
        {
            int values = 0;
            int valuesMissing = 0;
            var valueBoxes = new List<GeomElement>();
            var elements = new List<GeomElement>();
            var warnings = new List<GeomWarning>();
            int fromView = 0;
            int rebuilt = 0;
            int witnessesMissing = 0;
            int otherShapes = 0;
            View view = setup.View;

            IEnumerable<Dimension> dimensions = new FilteredElementCollector(doc, view.Id)
                .OfClass(typeof(Dimension))
                .Cast<Dimension>()
                .Where(d => !(d is SpotDimension))
                .OrderBy(d => d.Id.Value);

            ElementGuard.Each(dimensions, warnings, dimension =>
            {
                if (dimension.DimensionShape != DimensionShape.Linear || !(dimension.Curve is Line axis))
                {
                    otherShapes++;
                    return;
                }

                List<XYZ> marks = ReferenceMarks(dimension, axis.Direction);
                if (marks.Count < 2)
                {
                    return;
                }

                var lines = new List<IReadOnlyList<Point2D>> { new[] { setup.ToFrame(marks.First()), setup.ToFrame(marks.Last()) } };
                List<Line> drawn = ViewLines(dimension, view, TextBoxes(dimension), TextPaperFeet * view.Scale).ToList();
                if (drawn.Count > 0)
                {
                    fromView++;
                    lines.AddRange(drawn.Select(l => (IReadOnlyList<Point2D>)new[] { setup.ToFrame(l.GetEndPoint(0)), setup.ToFrame(l.GetEndPoint(1)) }));
                }
                else
                {
                    rebuilt++;
                    lines.AddRange(Ticks(marks, axis.Direction, TickPaperFeet * view.Scale, setup));
                    (List<IReadOnlyList<Point2D>> witnesses, int missing) = WitnessLines(dimension, axis.Direction, view, setup);
                    lines.AddRange(witnesses);
                    witnessesMissing += missing;
                }

                elements.Add(GeomElement.Stroked(StructureClass.Dimension, dimension.Id.Value, lines, SwingReader.StrokeM));
                foreach ((XYZ position, string text) in ValueTexts(dimension))
                {
                    IReadOnlyList<Point2D> box = ValueBox(dimension, position, text, axis.Direction, setup, ink);
                    if (box == null)
                    {
                        valuesMissing++;
                        continue;
                    }

                    valueBoxes.Add(GeomElement.Filled(StructureClass.Dimension, dimension.Id.Value, new[] { box }).WithLabel(null, text));
                    values++;
                }
            });

            elements.AddRange(valueBoxes);

            var notes = new List<string>();
            if (rebuilt > 0)
            {
                notes.Add($"Cotas: {fromView} com chamadas e tiques da geometria da vista; {rebuilt} reconstruídas (linha de cota, tiques e linhas de chamada pelas referências e pelo afastamento/extensão do tipo)"
                    + (witnessesMissing > 0 ? $"; {witnessesMissing} linha(s) de chamada sem referência legível ficaram fora." : "."));
            }

            if (values + valuesMissing > 0)
            {
                notes.Add($"Valores de cota (11): {values} caixa(s) pela tinta" + (valuesMissing > 0 ? $"; {valuesMissing} sem tinta visível, fora." : "."));
            }

            if (otherShapes > 0)
            {
                notes.Add($"{otherShapes} cota(s) angular/radial/de arco ficaram só na imagem (spec v3: só cota linear entra).");
            }

            return new DimensionReading(elements, warnings, notes);
        }

        /// <summary>Points on the dimension line where its references sit, in order along it.</summary>
        private static List<XYZ> ReferenceMarks(Dimension dimension, XYZ direction)
        {
            var marks = new List<XYZ>();
            if (dimension.NumberOfSegments == 0)
            {
                double half = (dimension.Value ?? 0) / 2;
                marks.Add(dimension.Origin - (direction * half));
                marks.Add(dimension.Origin + (direction * half));
            }
            else
            {
                foreach (DimensionSegment segment in dimension.Segments)
                {
                    double half = (segment.Value ?? 0) / 2;
                    marks.Add(segment.Origin - (direction * half));
                    marks.Add(segment.Origin + (direction * half));
                }
            }

            return marks.OrderBy(m => m.DotProduct(direction)).ToList();
        }

        /// <summary>
        /// One witness line per reference: from the referenced element (minus the
        /// type's gap) across to the dimension line (plus the type's extension),
        /// perpendicular to it. References and marks pair up in order: the nth
        /// segment lies between the nth and n+1st reference.
        /// </summary>
        private static (List<IReadOnlyList<Point2D>> Lines, int Missing) WitnessLines(Dimension dimension, XYZ direction, View view, ViewSetup setup)
        {
            var lines = new List<IReadOnlyList<Point2D>>();
            int missing = 0;
            List<XYZ> marks = MarksByReference(dimension, direction);
            ReferenceArray references = dimension.References;
            double gap = TypeLength(dimension, BuiltInParameter.WITNS_LINE_GAP_TO_ELT) * view.Scale;
            double extension = TypeLength(dimension, BuiltInParameter.WITNS_LINE_EXTENSION) * view.Scale;
            XYZ normal = XYZ.BasisZ.CrossProduct(direction).Normalize();

            for (int i = 0; i < Math.Min(marks.Count, references.Size); i++)
            {
                IReadOnlyList<XYZ> extent = ReferenceExtent(dimension.Document, references.get_Item(i), marks[i], direction, view);
                if (extent == null)
                {
                    missing++;
                    continue;
                }

                // Signed distances of the element from the dimension line; one that
                // crosses the line, or stops within the gap, gets no witness line.
                List<double> offsets = extent.Select(p => new XYZ(p.X - marks[i].X, p.Y - marks[i].Y, 0).DotProduct(normal)).ToList();
                if (offsets.Min() < 0 && offsets.Max() > 0)
                {
                    continue;
                }

                double reach = offsets.OrderBy(Math.Abs).First();
                if (Math.Abs(reach) <= gap)
                {
                    continue;
                }

                double side = Math.Sign(reach);
                XYZ start = marks[i] + (normal * (reach - (side * gap)));
                XYZ end = marks[i] - (normal * side * extension);
                lines.Add(new[] { setup.ToFrame(start), setup.ToFrame(end) });
            }

            return (lines, missing);
        }

        /// <summary>Reference marks in reference order (not sorted along the line).</summary>
        private static List<XYZ> MarksByReference(Dimension dimension, XYZ direction)
        {
            if (dimension.NumberOfSegments == 0)
            {
                double half = (dimension.Value ?? 0) / 2;
                return new List<XYZ> { dimension.Origin - (direction * half), dimension.Origin + (direction * half) };
            }

            List<DimensionSegment> segments = dimension.Segments.Cast<DimensionSegment>().ToList();
            var marks = new List<XYZ> { segments[0].Origin - (direction * ((segments[0].Value ?? 0) / 2)) };
            marks.AddRange(segments.Select(s => s.Origin + (direction * ((s.Value ?? 0) / 2))));
            return marks;
        }

        /// <summary>Along the dimension, a referenced element lies on its mark within this (feet).</summary>
        private const double OnMarkFeet = 0.05;

        /// <summary>
        /// Points spanning the referenced element: its curve, edge or the edges of
        /// its face, or a grid's line in this view. A family instance may return
        /// its symbol's geometry untransformed, so the instance transform is
        /// applied when that is what puts the points on the mark. Null when the
        /// reference has no readable geometry on the mark.
        /// </summary>
        private static IReadOnlyList<XYZ> ReferenceExtent(Document doc, Reference reference, XYZ mark, XYZ direction, View view)
        {
            Element element = doc.GetElement(reference);
            if (element == null)
            {
                return null;
            }

            List<XYZ> points = GeometryPoints(element.GetGeometryObjectFromReference(reference)).ToList();
            if (points.Count == 0)
            {
                points = element is Grid grid
                    ? grid.GetCurvesInView(DatumExtentType.ViewSpecific, view).SelectMany(c => c.Tessellate()).ToList()
                    : LocationPoints(element).ToList();
            }

            var candidates = new List<List<XYZ>> { points };
            if (element is FamilyInstance instance)
            {
                Transform transform = instance.GetTotalTransform();
                candidates.Add(points.Select(transform.OfPoint).ToList());
            }

            return candidates
                .Where(c => c.Count > 0)
                .Select(c => (Points: c, Miss: c.Max(p => Math.Abs((p - mark).DotProduct(direction)))))
                .Where(c => c.Miss <= OnMarkFeet)
                .OrderBy(c => c.Miss)
                .Select(c => c.Points)
                .FirstOrDefault();
        }

        private static IEnumerable<XYZ> GeometryPoints(GeometryObject geometry)
        {
            switch (geometry)
            {
                case Curve curve when curve.IsBound:
                    return curve.Tessellate();
                case Edge edge:
                    return edge.Tessellate();
                case Face face:
                    return face.EdgeLoops.Cast<EdgeArray>().SelectMany(loop => loop.Cast<Edge>()).SelectMany(e => e.Tessellate());
                default:
                    return Enumerable.Empty<XYZ>();
            }
        }

        private static IEnumerable<XYZ> LocationPoints(Element element)
        {
            switch (element.Location)
            {
                case LocationCurve curve:
                    return curve.Curve.Tessellate();
                case LocationPoint point:
                    return new[] { point.Point };
                default:
                    return Enumerable.Empty<XYZ>();
            }
        }

        private static double TypeLength(Dimension dimension, BuiltInParameter parameter)
        {
            Parameter p = dimension.DimensionType?.get_Parameter(parameter);
            return p != null && p.HasValue && p.StorageType == StorageType.Double ? p.AsDouble() : 0;
        }

        /// <summary>Each value text with where it sits; a dimension kind that does not report it gives none.</summary>
        private static IEnumerable<(XYZ Position, string Text)> ValueTexts(Dimension dimension)
        {
            var texts = new List<(XYZ, string)>();
            try
            {
                if (dimension.NumberOfSegments == 0)
                {
                    texts.Add((dimension.TextPosition, dimension.ValueString));
                }
                else
                {
                    texts.AddRange(dimension.Segments.Cast<DimensionSegment>().Select(s => (s.TextPosition, s.ValueString)));
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // No text position for this dimension: its value stays out.
            }

            return texts.Where(t => t.Item1 != null && !string.IsNullOrWhiteSpace(t.Item2));
        }

        /// <summary>The value's ink box: around its text position, along the dimension, sized by the type's text height.</summary>
        private static IReadOnlyList<Point2D> ValueBox(Dimension dimension, XYZ position, string text, XYZ direction, ViewSetup setup, InkBoxes ink)
        {
            Parameter size = dimension.DimensionType?.get_Parameter(BuiltInParameter.TEXT_SIZE);
            double heightFeet = (size != null && size.HasValue ? size.AsDouble() : TickPaperFeet) * setup.View.Scale;
            double heightM = UnitsConverter.ToMeters(heightFeet);
            Vector2D along = setup.Frame.ToViewDirection(new Vector2D(direction.X, direction.Y));
            return ink.Fit(setup.ToFrame(position), along, heightM * ValueHalfWidthPerChar * Math.Max(text.Length, 2), heightM * ValueHalfHeights, isSearchArea: true);
        }

        /// <summary>Where the value texts sit; some dimension kinds do not report it.</summary>
        private static IReadOnlyList<XYZ> TextBoxes(Dimension dimension)
        {
            var positions = new List<XYZ>();
            try
            {
                if (dimension.NumberOfSegments == 0)
                {
                    positions.Add(dimension.TextPosition);
                }
                else
                {
                    positions.AddRange(dimension.Segments.Cast<DimensionSegment>().Select(s => s.TextPosition));
                }
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                // The text position is not applicable to this dimension: nothing to exclude.
            }

            return positions.Where(p => p != null).ToList();
        }

        /// <summary>The straight lines the dimension draws in the view, minus any that lie on its value text.</summary>
        private static IEnumerable<Line> ViewLines(Dimension dimension, View view, IReadOnlyList<XYZ> texts, double textRadiusFeet)
        {
            GeometryElement geometry = dimension.get_Geometry(new Options { View = view });
            if (geometry == null)
            {
                yield break;
            }

            foreach (Line line in Lines(geometry).Where(l => l.IsBound))
            {
                XYZ middle = (line.GetEndPoint(0) + line.GetEndPoint(1)) / 2;
                bool onText = texts.Any(t => new XYZ(middle.X - t.X, middle.Y - t.Y, 0).GetLength() < textRadiusFeet && line.Length < textRadiusFeet * 2);
                if (!onText)
                {
                    yield return line;
                }
            }
        }

        private static IEnumerable<Line> Lines(GeometryElement geometry)
        {
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Line line)
                {
                    yield return line;
                }
                else if (obj is GeometryInstance instance)
                {
                    foreach (Line nested in Lines(instance.GetInstanceGeometry()))
                    {
                        yield return nested;
                    }
                }
            }
        }

        /// <summary>A 45-degree tick at every reference mark, the usual architectural dimension tick.</summary>
        private static IEnumerable<IReadOnlyList<Point2D>> Ticks(IEnumerable<XYZ> marks, XYZ direction, double tickFeet, ViewSetup setup)
        {
            XYZ normal = XYZ.BasisZ.CrossProduct(direction).Normalize();
            XYZ diagonal = ((direction + normal) / Math.Sqrt(2)) * (tickFeet / 2);
            return marks
                .GroupBy(m => (Math.Round(m.X, 6), Math.Round(m.Y, 6)))
                .Select(g => g.First())
                .Select(m => (IReadOnlyList<Point2D>)new[] { setup.ToFrame(m - diagonal), setup.ToFrame(m + diagonal) });
        }
    }
}
