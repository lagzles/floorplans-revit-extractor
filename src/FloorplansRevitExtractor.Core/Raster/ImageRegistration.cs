using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Png;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <param name="Dx">Columns to add to the provisional mask to land on the image.</param>
    /// <param name="Dy">Rows to add to the provisional mask to land on the image.</param>
    /// <param name="Score">Share of construction edge pixels on ink (grown by 1 px) at that shift.</param>
    /// <param name="RunnerUpScore">Best share at any shift more than <see cref="ImageRegistration.Exclusion"/> px away.</param>
    public sealed record RegistrationFit(int Dx, int Dy, double Score, double RunnerUpScore)
    {
        public bool IsConfident => Score >= ImageRegistration.MinScore && Score - RunnerUpScore >= ImageRegistration.MinLead;
    }

    /// <summary>
    /// Spec v4 section 7, automatic registration. When the scale of the image
    /// is known but not where the view sits in it (annotation outside the crop
    /// makes Revit's export larger than the crop, on sides that depend on the
    /// drawing), the construction (walls, pillars) is painted at a provisional
    /// place and slid over the possible shifts; at the right one its edges lie
    /// on the drawn lines. Accepted only when that shift clearly beats every other.
    /// </summary>
    public static class ImageRegistration
    {
        public const double MinScore = 0.5;
        public const double MinLead = 0.1;
        public const int Exclusion = 3;

        /// <summary>More edge pixels than this are sampled evenly: the score is a share, and big plans have plenty.</summary>
        private const int MaxEdges = 20000;

        public static RegistrationFit? Find(
            RgbImage image,
            SemanticMask provisional,
            IReadOnlyCollection<StructureClass> construction,
            (int Min, int Max) dxRange,
            (int Min, int Max) dyRange,
            byte inkThreshold = 160)
        {
            if (dxRange.Max < dxRange.Min || dyRange.Max < dyRange.Min)
            {
                return null;
            }

            List<(int X, int Y)> edges = EdgePixels(provisional, construction);
            if (edges.Count == 0)
            {
                return null;
            }

            bool[] exact = Ink(image, inkThreshold);
            bool[] grown = Dilated(exact, image.Width, image.Height);
            int nx = dxRange.Max - dxRange.Min + 1;
            int ny = dyRange.Max - dyRange.Min + 1;
            var exactScores = new double[nx, ny];
            var grownScores = new double[nx, ny];
            for (int i = 0; i < nx; i++)
            {
                for (int j = 0; j < ny; j++)
                {
                    (exactScores[i, j], grownScores[i, j]) = Score(edges, exact, grown, image, dxRange.Min + i, dyRange.Min + j);
                }
            }

            // The exact ink places the shift (grown ink ties the neighbours); the grown
            // ink, tolerant of half-pixel line placement, says how sure it is.
            (int bi, int bj) = Cells(nx, ny).OrderByDescending(c => exactScores[c.I, c.J]).ThenByDescending(c => grownScores[c.I, c.J]).First();
            double runnerUp = Cells(nx, ny)
                .Where(c => Math.Abs(c.I - bi) > Exclusion || Math.Abs(c.J - bj) > Exclusion)
                .Select(c => grownScores[c.I, c.J])
                .DefaultIfEmpty(0)
                .Max();
            return new RegistrationFit(dxRange.Min + bi, dyRange.Min + bj, grownScores[bi, bj], runnerUp);
        }

        private static IEnumerable<(int I, int J)> Cells(int nx, int ny) =>
            Enumerable.Range(0, nx).SelectMany(i => Enumerable.Range(0, ny).Select(j => (i, j)));

        private static (double Exact, double Grown) Score(List<(int X, int Y)> edges, bool[] exact, bool[] grown, RgbImage image, int dx, int dy)
        {
            int inside = 0;
            int onExact = 0;
            int onGrown = 0;
            foreach ((int x, int y) in edges)
            {
                int col = x + dx;
                int row = y + dy;
                if (col < 0 || row < 0 || col >= image.Width || row >= image.Height)
                {
                    continue;
                }

                int k = (row * image.Width) + col;
                inside++;
                onExact += exact[k] ? 1 : 0;
                onGrown += grown[k] ? 1 : 0;
            }

            // Edges pushed out of the image must not win by having nothing left to miss.
            return inside * 2 < edges.Count ? (0, 0) : ((double)onExact / inside, (double)onGrown / inside);
        }

        /// <summary>Pixels of the classes with a 4-neighbour outside them: the outline the drawing shows.</summary>
        private static List<(int X, int Y)> EdgePixels(SemanticMask mask, IReadOnlyCollection<StructureClass> classes)
        {
            var member = new bool[256];
            foreach (StructureClass cls in classes)
            {
                member[(byte)cls] = true;
            }

            byte[] p = mask.Pixels;
            int w = mask.Width;
            int h = mask.Height;
            bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && member[p[(y * w) + x]];
            var edges = new List<(int, int)>();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (In(x, y) && (!In(x - 1, y) || !In(x + 1, y) || !In(x, y - 1) || !In(x, y + 1)))
                    {
                        edges.Add((x, y));
                    }
                }
            }

            if (edges.Count <= MaxEdges)
            {
                return edges;
            }

            int step = (edges.Count / MaxEdges) + 1;
            return edges.Where((_, i) => i % step == 0).ToList();
        }

        private static bool[] Ink(RgbImage image, byte inkThreshold)
        {
            var dark = new bool[image.Width * image.Height];
            for (int i = 0; i < dark.Length; i++)
            {
                dark[i] = image.GrayAt(i) < inkThreshold;
            }

            return dark;
        }

        /// <summary>Ink grown by one pixel: a drawn line sits on a polygon's edge, half a pixel either side.</summary>
        private static bool[] Dilated(bool[] dark, int w, int h)
        {
            var grown = new bool[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool any = false;
                    for (int dy = -1; dy <= 1 && !any; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !any; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            any = nx >= 0 && ny >= 0 && nx < w && ny < h && dark[(ny * w) + nx];
                        }
                    }

                    grown[(y * w) + x] = any;
                }
            }

            return grown;
        }
    }
}
