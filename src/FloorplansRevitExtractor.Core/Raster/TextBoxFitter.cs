using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>A row or column of the loose box this dark is a line crossing it (dimension line, frame, wall), not a letter.</summary>
        public const double LineShare = 0.8;

        /// <summary>Rows and columns shorter than this (px) are too short to tell a line from a letter.</summary>
        private const int MinLineSamples = 8;

        /// <summary>A search-area fit whose ink spans this share of the area both ways found no letters, only clutter.</summary>
        public const double FilledShare = 0.95;

        /// <param name="center">Center of the loose box, in pixels.</param>
        /// <param name="baseDirection">Reading direction of the text, in pixels (Y down); need not be unit length.</param>
        /// <param name="halfWidth">Half the loose box along the reading direction, in pixels.</param>
        /// <param name="halfHeight">Half the loose box across it, in pixels.</param>
        /// <param name="isSearchArea">
        /// True when the loose box is only an area around where the text should
        /// be (not the text's own box): ink filling it both ways is clutter.
        /// </param>
        /// <returns>The four corners in pixels, or null when the loose box has no letter ink (hidden or cropped text).</returns>
        public static IReadOnlyList<Point2D>? Fit(
            RgbImage image,
            Point2D center,
            Vector2D baseDirection,
            double halfWidth,
            double halfHeight,
            bool isSearchArea = false,
            byte inkThreshold = 160)
        {
            if (!(baseDirection.Length > 0) || !(halfWidth > 0) || !(halfHeight > 0))
            {
                return null;
            }

            var u = new Vector2D(baseDirection.X / baseDirection.Length, baseDirection.Y / baseDirection.Length);
            var v = new Vector2D(-u.Y, u.X);
            List<(double S, double T, bool Dark)> samples = Samples(image, center, u, v, halfWidth, halfHeight, inkThreshold);
            HashSet<int> lineRows = LineBins(samples, p => p.T);
            HashSet<int> lineColumns = LineBins(samples, p => p.S);

            double minS = double.MaxValue, maxS = double.MinValue, minT = double.MaxValue, maxT = double.MinValue;
            foreach ((double s, double t, bool dark) in samples)
            {
                if (!dark || lineRows.Contains(Bin(t)) || lineColumns.Contains(Bin(s)))
                {
                    continue;
                }

                minS = Math.Min(minS, s);
                maxS = Math.Max(maxS, s);
                minT = Math.Min(minT, t);
                maxT = Math.Max(maxT, t);
            }

            if (minS > maxS)
            {
                return null;
            }

            bool fillsArea = maxS - minS >= FilledShare * 2 * (halfWidth - 1) && maxT - minT >= FilledShare * 2 * (halfHeight - 1);
            if (isSearchArea && fillsArea)
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

        /// <summary>Every pixel center inside the loose box, in the text frame (s along, t across), and whether it is ink.</summary>
        private static List<(double S, double T, bool Dark)> Samples(
            RgbImage image, Point2D center, Vector2D u, Vector2D v, double halfWidth, double halfHeight, byte inkThreshold)
        {
            double reachX = (Math.Abs(u.X) * halfWidth) + (Math.Abs(v.X) * halfHeight);
            double reachY = (Math.Abs(u.Y) * halfWidth) + (Math.Abs(v.Y) * halfHeight);
            int x0 = Math.Max(0, (int)Math.Floor(center.X - reachX));
            int x1 = Math.Min(image.Width - 1, (int)Math.Ceiling(center.X + reachX));
            int y0 = Math.Max(0, (int)Math.Floor(center.Y - reachY));
            int y1 = Math.Min(image.Height - 1, (int)Math.Ceiling(center.Y + reachY));

            var samples = new List<(double, double, bool)>();
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var d = new Vector2D(x + 0.5 - center.X, y + 0.5 - center.Y);
                    double s = d.Dot(u);
                    double t = d.Dot(v);
                    if (Math.Abs(s) <= halfWidth && Math.Abs(t) <= halfHeight)
                    {
                        samples.Add((s, t, image.Gray(x, y) < inkThreshold));
                    }
                }
            }

            return samples;
        }

        /// <summary>1 px bins (rows or columns of the text frame) that are mostly ink: lines crossing the box.</summary>
        private static HashSet<int> LineBins(List<(double S, double T, bool Dark)> samples, Func<(double S, double T, bool Dark), double> coordinate)
        {
            var lines = new HashSet<int>();
            foreach (IGrouping<int, (double S, double T, bool Dark)> bin in samples.GroupBy(p => Bin(coordinate(p))))
            {
                int count = bin.Count();
                if (count >= MinLineSamples && bin.Count(p => p.Dark) >= LineShare * count)
                {
                    lines.Add(bin.Key);
                }
            }

            return lines;
        }

        private static int Bin(double coordinate) => (int)Math.Floor(coordinate);
    }
}
