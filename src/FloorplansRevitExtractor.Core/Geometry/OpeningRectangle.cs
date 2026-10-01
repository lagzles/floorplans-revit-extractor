using System;
using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// Door and window opening in the mask (spec v2, critical rule): exactly
    /// opening width x host wall thickness, centered on the insertion point
    /// and aligned with the wall. Never extend the ends: the vectorizer is
    /// calibrated to long side / true width = 0.999.
    /// </summary>
    public static class OpeningRectangle
    {
        /// <returns>Four corners, counter-clockwise; edge 0-1 runs along the wall.</returns>
        public static IReadOnlyList<Point2D> Build(Point2D center, Vector2D wallDirection, double widthM, double thicknessM)
        {
            if (!center.IsFinite)
            {
                throw new ArgumentException($"Opening center must be finite, got {center}.", nameof(center));
            }

            if (!IsPositiveFinite(widthM) || !IsPositiveFinite(thicknessM))
            {
                throw new ArgumentException($"Opening width and wall thickness must be positive and finite, got {widthM} x {thicknessM}.");
            }

            // Scale down first so squaring a huge component cannot overflow to infinity.
            double scale = Math.Max(Math.Abs(wallDirection.X), Math.Abs(wallDirection.Y));
            if (!wallDirection.IsFinite || scale == 0)
            {
                throw new ArgumentException($"Wall direction must be a finite non-zero vector, got {wallDirection}.", nameof(wallDirection));
            }

            var scaled = new Vector2D(wallDirection.X / scale, wallDirection.Y / scale);
            double ux = scaled.X / scaled.Length;
            double uy = scaled.Y / scaled.Length;

            // Half extents along the wall (u) and across it (the left normal, -uy, ux).
            double ax = ux * widthM / 2;
            double ay = uy * widthM / 2;
            double nx = -uy * thicknessM / 2;
            double ny = ux * thicknessM / 2;

            return new[]
            {
                new Point2D(center.X - ax - nx, center.Y - ay - ny),
                new Point2D(center.X + ax - nx, center.Y + ay - ny),
                new Point2D(center.X + ax + nx, center.Y + ay + ny),
                new Point2D(center.X - ax + nx, center.Y - ay + ny),
            };
        }

        private static bool IsPositiveFinite(double value) => value > 0 && !double.IsInfinity(value);
    }
}
