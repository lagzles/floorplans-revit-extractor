using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Model;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Bridges the geometry contract (meters, view frame) and the mask
    /// painters (pixels): applies the document's transform, sizes the swing
    /// stroke for its px_per_m, and gives each hole to the polygon it is in.
    /// </summary>
    public static class GeomRasterizer
    {
        public static SemanticMask PaintSemantic(GeomDocument document) =>
            SemanticMaskPainter.Paint(document.Transform.Width, document.Transform.Height, ToMaskElements(document));

        public static IReadOnlyList<MaskElement> ToMaskElements(GeomDocument document)
        {
            PixelTransform t = document.Transform;
            return document.Elements.Select(e => ToMaskElement(e, t)).ToList();
        }

        private static MaskElement ToMaskElement(GeomElement element, PixelTransform t)
        {
            if (element.Polylines.Count > 0)
            {
                int widthPx = StrokeRasterizer.WidthInPixels(element.StrokeM!.Value, t.PxPerM);
                return MaskElement.Stroked(
                    element.Class,
                    element.Polylines.Select((line, i) => ToPixels(Closing(line, element.Closed, i), t)).ToList(),
                    widthPx);
            }

            return MaskElement.Filled(element.Class, ToShapes(element, t));
        }

        /// <summary>
        /// Each hole goes to the smallest polygon that is larger than the hole
        /// and contains a point strictly inside it. The size rule skips islands
        /// sitting inside the hole, and the interior point (not the centroid)
        /// keeps U-shaped holes from landing in their own notch. A hole that
        /// fits in none is an error: attached anyway, the even-odd rule would
        /// paint it as solid area.
        /// </summary>
        private static IReadOnlyList<PolygonShape> ToShapes(GeomElement element, PixelTransform t)
        {
            var holesByPolygon = element.Polygons.Select(_ => new List<IReadOnlyList<Point2D>>()).ToList();
            double[] polygonAreas = element.Polygons.Select(PolygonGeometry.Area).ToArray();
            foreach (IReadOnlyList<Point2D> hole in element.Holes)
            {
                Point2D probe = PolygonGeometry.InteriorPoint(hole);
                double holeArea = PolygonGeometry.Area(hole);
                int owner = SmallestIndex(
                    polygonAreas,
                    i => polygonAreas[i] > holeArea && PolygonGeometry.Contains(element.Polygons[i], probe));
                if (owner < 0)
                {
                    throw new ArgumentException(
                        $"Element {element.RevitId} ({StructureClassNames.ToName(element.Class)}) has a hole outside all of its polygons.");
                }

                holesByPolygon[owner].Add(ToPixels(hole, t));
            }

            return element.Polygons
                .Select((polygon, i) => new PolygonShape(ToPixels(polygon, t), holesByPolygon[i]))
                .ToList();
        }

        /// <summary>A closed polyline (a grid bubble) gets its first point again at the end, so the stroke has no gap.</summary>
        private static IReadOnlyList<Point2D> Closing(IReadOnlyList<Point2D> line, IReadOnlyList<bool> closed, int index) =>
            index < closed.Count && closed[index] && line.Count > 2 ? line.Concat(new[] { line[0] }).ToArray() : line;

        private static IReadOnlyList<Point2D> ToPixels(IReadOnlyList<Point2D> ring, PixelTransform t) =>
            ring.Select(t.ToPixel).ToArray();

        /// <summary>Index of the smallest value whose index passes the filter, or -1.</summary>
        private static int SmallestIndex(double[] values, Func<int, bool> filter)
        {
            int best = -1;
            for (int i = 0; i < values.Length; i++)
            {
                if (filter(i) && (best < 0 || values[i] < values[best]))
                {
                    best = i;
                }
            }

            return best;
        }
    }
}
