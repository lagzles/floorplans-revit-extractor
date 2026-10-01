using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Names the building outline's shape the way the generator does
    /// (cad.py): by corner count alone, 4 rect, 6 L, 8 T, anything else U.
    /// So an 8-corner U is "T" and a 10-corner outline is "U", in the
    /// training data and here. Spec v1's "null when not classifiable" is
    /// kept only for an outline that was not measured.
    /// Corners are counted after cleaning modelling noise the generator's
    /// clean geometry never has: repeated points, edges shorter than
    /// <see cref="MinEdgeM"/> and near-straight bends.
    /// </summary>
    public static class EnvelopeClassifier
    {
        /// <summary>Jogs shorter than this are modelling noise, not corners (5 cm).</summary>
        public const double MinEdgeM = 0.05;

        /// <summary>Bends below this angle are a straight wall (about 0.6 degrees).</summary>
        private const double StraightSine = 0.01;

        public static string? Classify(IReadOnlyList<Point2D> outline)
        {
            if (outline.Count == 0)
            {
                return null;
            }

            switch (CountCorners(outline))
            {
                case 4:
                    return "rect";
                case 6:
                    return "L";
                case 8:
                    return "T";
                default:
                    return "U";
            }
        }

        /// <summary>
        /// Repeatedly drops the vertex that ends the shortest too-short edge, or
        /// sits on a near-straight continuation, until neither is left. A 2 mm
        /// jog becomes two near-straight bends once its short edge is gone.
        /// </summary>
        private static int CountCorners(IReadOnlyList<Point2D> outline)
        {
            var points = new List<Point2D>(outline);
            bool changed = true;
            while (changed && points.Count > 3)
            {
                changed = RemoveShortestShortEdge(points) || RemoveStraightVertex(points);
            }

            return points.Count;
        }

        private static bool RemoveShortestShortEdge(List<Point2D> points)
        {
            int shortest = -1;
            double shortestLength = MinEdgeM;
            for (int i = 0; i < points.Count; i++)
            {
                double length = Edge(points, i).Length;
                if (length < shortestLength)
                {
                    shortest = i;
                    shortestLength = length;
                }
            }

            if (shortest < 0)
            {
                return false;
            }

            points.RemoveAt((shortest + 1) % points.Count);
            return true;
        }

        private static bool RemoveStraightVertex(List<Point2D> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Vector2D inEdge = Edge(points, (i - 1 + points.Count) % points.Count);
                Vector2D outEdge = Edge(points, i);
                bool straight = Math.Abs(inEdge.Cross(outEdge)) <= StraightSine * inEdge.Length * outEdge.Length
                    && inEdge.Dot(outEdge) > 0;
                if (straight)
                {
                    points.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>Edge from point i to the next one.</summary>
        private static Vector2D Edge(List<Point2D> points, int i)
        {
            Point2D a = points[i];
            Point2D b = points[(i + 1) % points.Count];
            return new Vector2D(b.X - a.X, b.Y - a.Y);
        }
    }
}
