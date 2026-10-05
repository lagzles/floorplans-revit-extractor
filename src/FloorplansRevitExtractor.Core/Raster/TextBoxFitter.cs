using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Png;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Spec v4 section 4.5: the generator labels the tight, rotated box of
    /// the drawn glyphs plus 1 px (label_footprint, TEXT_PAD_PX). Revit's text
    /// metrics only give a loose box, so the box is tightened on the exported
    /// image: the smallest box at the text's angle holding the dark pixels
    /// inside the loose one, plus the pad. No font metrics needed.
    /// </summary>
    public static class TextBoxFitter
    {
        /// <summary>The generator's TEXT_PAD_PX.</summary>
        public const double PadPx = 1.0;

        /// <param name="center">Center of the loose box, in pixels.</param>
        /// <param name="baseDirection">Reading direction of the text, in pixels (Y down); need not be unit length.</param>
        /// <param name="halfWidth">Half the loose box along the reading direction, in pixels.</param>
        /// <param name="halfHeight">Half the loose box across it, in pixels.</param>
        /// <returns>The four corners in pixels, or null when the loose box has no ink (hidden or cropped text).</returns>
        public static IReadOnlyList<Point2D>? Fit(
            RgbImage image,
            Point2D center,
            Vector2D baseDirection,
            double halfWidth,
            double halfHeight,
            byte inkThreshold = 160)
        {
            if (!(baseDirection.Length > 0) || !(halfWidth > 0) || !(halfHeight > 0))
            {
                return null;
            }

            var u = new Vector2D(baseDirection.X / baseDirection.Length, baseDirection.Y / baseDirection.Length);
            var v = new Vector2D(-u.Y, u.X);
            double reachX = (Math.Abs(u.X) * halfWidth) + (Math.Abs(v.X) * halfHeight);
            double reachY = (Math.Abs(u.Y) * halfWidth) + (Math.Abs(v.Y) * halfHeight);
            int x0 = Math.Max(0, (int)Math.Floor(center.X - reachX));
            int x1 = Math.Min(image.Width - 1, (int)Math.Ceiling(center.X + reachX));
            int y0 = Math.Max(0, (int)Math.Floor(center.Y - reachY));
            int y1 = Math.Min(image.Height - 1, (int)Math.Ceiling(center.Y + reachY));

            double minS = double.MaxValue, maxS = double.MinValue, minT = double.MaxValue, maxT = double.MinValue;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var d = new Vector2D(x + 0.5 - center.X, y + 0.5 - center.Y);
                    double s = d.Dot(u);
                    double t = d.Dot(v);
                    if (Math.Abs(s) > halfWidth || Math.Abs(t) > halfHeight || image.Gray(x, y) >= inkThreshold)
                    {
                        continue;
                    }

                    minS = Math.Min(minS, s);
                    maxS = Math.Max(maxS, s);
                    minT = Math.Min(minT, t);
                    maxT = Math.Max(maxT, t);
                }
            }

            if (minS > maxS)
            {
                return null;
            }

            // A pixel is a unit square: its half-extent seen along u and along v.
            double padS = (0.5 * (Math.Abs(u.X) + Math.Abs(u.Y))) + PadPx;
            double padT = (0.5 * (Math.Abs(v.X) + Math.Abs(v.Y))) + PadPx;
            Point2D Corner(double s, double t) => new Point2D(center.X + (u.X * s) + (v.X * t), center.Y + (u.Y * s) + (v.Y * t));
            return new[]
            {
                Corner(minS - padS, minT - padT),
                Corner(maxS + padS, minT - padT),
                Corner(maxS + padS, maxT + padT),
                Corner(minS - padS, maxT + padT),
            };
        }
    }
}
