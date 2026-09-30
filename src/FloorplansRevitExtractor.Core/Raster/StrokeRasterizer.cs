using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Aliased fixed-width stroke for the door swing (arc and leaf): a pixel is
    /// covered when its center is within half the width of any segment, which
    /// gives round caps and gap-free joins without a separate join step.
    /// </summary>
    public static class StrokeRasterizer
    {
        /// <summary>Thinner strokes vanish or break up when rasterized.</summary>
        public const int MinimumWidthPixels = 2;

        /// <summary>
        /// Spec v2: max(2, round(stroke_m x px_per_m)), the generator's formula.
        /// Rounds half to even, like Python's round().
        /// </summary>
        public static int WidthInPixels(double strokeMeters, double pxPerMeter) =>
            Math.Max(MinimumWidthPixels, (int)Math.Round(strokeMeters * pxPerMeter, MidpointRounding.ToEven));

        public static Coverage Stroke(
            int width,
            int height,
            IEnumerable<IReadOnlyList<Point2D>> polylines,
            double strokeWidthPixels)
        {
            var coverage = new Coverage(width, height);
            StrokeSpans(width, height, polylines, strokeWidthPixels, coverage.SetSpan);
            return coverage;
        }

        internal static void StrokeSpans(
            int width,
            int height,
            IEnumerable<IReadOnlyList<Point2D>> polylines,
            double strokeWidthPixels,
            SpanWriter write)
        {
            if (strokeWidthPixels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(strokeWidthPixels), strokeWidthPixels, "Stroke width must be positive.");
            }

            double radius = strokeWidthPixels / 2.0;
            foreach (IReadOnlyList<Point2D> polyline in polylines)
            {
                PixelSpans.RequireFinite(polyline, nameof(polylines));
                if (polyline.Count == 1)
                {
                    StrokeSegment(width, height, polyline[0], polyline[0], radius, write);
                }

                for (int i = 0; i + 1 < polyline.Count; i++)
                {
                    StrokeSegment(width, height, polyline[i], polyline[i + 1], radius, write);
                }
            }
        }

        /// <summary>
        /// A capsule is convex, so the covered pixels of each row are one
        /// contiguous run.
        /// </summary>
        private static void StrokeSegment(int width, int height, Point2D a, Point2D b, double radius, SpanWriter write)
        {
            int firstX = PixelSpans.Clamp(Math.Floor(Math.Min(a.X, b.X) - radius), 0, width - 1);
            int lastX = PixelSpans.Clamp(Math.Ceiling(Math.Max(a.X, b.X) + radius), 0, width - 1);
            int firstY = PixelSpans.Clamp(Math.Floor(Math.Min(a.Y, b.Y) - radius), 0, height - 1);
            int lastY = PixelSpans.Clamp(Math.Ceiling(Math.Max(a.Y, b.Y) + radius), 0, height - 1);
            double radiusSquared = radius * radius;

            for (int y = firstY; y <= lastY; y++)
            {
                int runStart = -1;
                int runEnd = -1;
                for (int x = firstX; x <= lastX; x++)
                {
                    if (DistanceSquaredToSegment(x + 0.5, y + 0.5, a, b) <= radiusSquared)
                    {
                        runStart = runStart < 0 ? x : runStart;
                        runEnd = x;
                    }
                }

                if (runStart >= 0)
                {
                    write(y, runStart, runEnd);
                }
            }
        }

        private static double DistanceSquaredToSegment(double px, double py, Point2D a, Point2D b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double lengthSquared = (dx * dx) + (dy * dy);
            double t = lengthSquared == 0.0
                ? 0.0
                : Math.Max(0.0, Math.Min(1.0, (((px - a.X) * dx) + ((py - a.Y) * dy)) / lengthSquared));
            double ex = px - (a.X + (t * dx));
            double ey = py - (a.Y + (t * dy));
            return (ex * ex) + (ey * ey);
        }
    }
}
