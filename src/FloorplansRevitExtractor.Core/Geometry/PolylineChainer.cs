using System;
using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// Joins loose pieces (tessellated sketch curves) into closed rings by
    /// matching endpoints, reversing a piece when it was drawn backwards. A
    /// Revit sketch hands its curves as one flat list holding every loop, in
    /// no particular order; concatenating them makes a self-crossing polygon.
    /// Chains that do not close describe no area and are dropped.
    /// </summary>
    public static class PolylineChainer
    {
        public static IReadOnlyList<IReadOnlyList<Point2D>> Chain(IReadOnlyList<IReadOnlyList<Point2D>> pieces, double tolerance)
        {
            var remaining = pieces.Where(p => p.Count >= 2).Select(p => p.ToList()).ToList();
            var rings = new List<IReadOnlyList<Point2D>>();
            while (remaining.Count > 0)
            {
                List<Point2D> chain = remaining[0];
                remaining.RemoveAt(0);
                bool extended = true;
                while (extended && !Near(chain[0], chain[chain.Count - 1], tolerance))
                {
                    extended = false;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        List<Point2D> piece = remaining[i];
                        bool forward = Near(chain[chain.Count - 1], piece[0], tolerance);
                        bool backward = Near(chain[chain.Count - 1], piece[piece.Count - 1], tolerance);
                        if (forward || backward)
                        {
                            IEnumerable<Point2D> next = forward ? piece : Enumerable.Reverse(piece);
                            chain.AddRange(next.Skip(1));
                            remaining.RemoveAt(i);
                            extended = true;
                            break;
                        }
                    }
                }

                if (chain.Count >= 4 && Near(chain[0], chain[chain.Count - 1], tolerance))
                {
                    chain.RemoveAt(chain.Count - 1);
                    rings.Add(chain);
                }
            }

            return rings;
        }

        private static bool Near(Point2D a, Point2D b, double tolerance) =>
            Math.Abs(a.X - b.X) <= tolerance && Math.Abs(a.Y - b.Y) <= tolerance;
    }
}
