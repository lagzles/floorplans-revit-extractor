using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Aliased polygon fill: a pixel is covered when its center lies inside.
    /// Each shape uses the even-odd rule over its outer ring and holes;
    /// separate shapes are united, so overlapping polygons never cancel.
    /// Crossings are counted on the half-open interval [start, end), which
    /// makes polygons that share an edge tile without overlap or gap.
    /// </summary>
    public static class ScanlineFiller
    {
        public static Coverage Fill(int width, int height, IEnumerable<PolygonShape> shapes)
        {
            var coverage = new Coverage(width, height);
            foreach (PolygonShape shape in shapes)
            {
                FillSpans(width, height, shape, coverage.SetSpan);
            }

            return coverage;
        }

        internal static void FillSpans(int width, int height, PolygonShape shape, SpanWriter write)
        {
            IReadOnlyList<Point2D>[] rings = new[] { shape.Outer }.Concat(shape.Holes).ToArray();
            List<Point2D> allPoints = rings.SelectMany(r => r).ToList();
            PixelSpans.RequireFinite(allPoints, nameof(shape));
            if (allPoints.Count < 3)
            {
                return;
            }

            int firstRow = PixelSpans.Clamp(Math.Floor(allPoints.Min(p => p.Y)), 0, height - 1);
            int lastRow = PixelSpans.Clamp(Math.Ceiling(allPoints.Max(p => p.Y)), 0, height - 1);
            var crossings = new List<double>();

            for (int row = firstRow; row <= lastRow; row++)
            {
                double centerY = row + 0.5;
                crossings.Clear();
                foreach (IReadOnlyList<Point2D> ring in rings)
                {
                    AddCrossings(ring, centerY, crossings);
                }

                crossings.Sort();
                for (int k = 0; k + 1 < crossings.Count; k += 2)
                {
                    WriteSpan(width, row, crossings[k], crossings[k + 1], write);
                }
            }
        }

        private static void AddCrossings(IReadOnlyList<Point2D> ring, double y, List<double> crossings)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                Point2D a = ring[i];
                Point2D b = ring[(i + 1) % ring.Count];
                bool spansRow = (a.Y <= y && y < b.Y) || (b.Y <= y && y < a.Y);
                if (spansRow)
                {
                    crossings.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
                }
            }
        }

        /// <summary>Covers pixels whose center x satisfies start &lt;= x + 0.5 &lt; end.</summary>
        private static void WriteSpan(int width, int row, double start, double end, SpanWriter write)
        {
            int first = PixelSpans.Clamp(Math.Ceiling(start - 0.5), 0, width);
            int last = PixelSpans.Clamp(Math.Ceiling(end - 0.5) - 1, -1, width - 1);
            if (first <= last)
            {
                write(row, first, last);
            }
        }
    }
}
