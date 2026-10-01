using System;
using FloorplansRevitExtractor.Core.Label;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class DistanceTransformTests
    {
        [Fact]
        public void SquaredDistances_SinglePixel_IsOneBecauseOutsideTheImageIsBackground()
        {
            // scipy's distance_transform_edt on the mask padded by 1, as the generator does.
            long[] d = DistanceTransform.SquaredDistances(new[] { true }, 1, 1);

            d.Should().Equal(1);
        }

        [Fact]
        public void SquaredDistances_FilledSquare_GrowsTowardTheMiddle()
        {
            var mask = new bool[5 * 5];
            for (int i = 0; i < mask.Length; i++)
            {
                mask[i] = true;
            }

            long[] d = DistanceTransform.SquaredDistances(mask, 5, 5);

            d[0].Should().Be(1);
            d[(1 * 5) + 1].Should().Be(4);
            d[(2 * 5) + 2].Should().Be(9);
        }

        [Fact]
        public void SquaredDistances_BackgroundPixels_AreZero()
        {
            bool[] mask = { true, false, true };

            DistanceTransform.SquaredDistances(mask, 3, 1).Should().Equal(1, 0, 1);
        }

        [Fact]
        public void SquaredDistances_RandomMasks_MatchBruteForce()
        {
            var random = new Random(7);
            for (int trial = 0; trial < 30; trial++)
            {
                int w = random.Next(1, 14);
                int h = random.Next(1, 14);
                var mask = new bool[w * h];
                for (int i = 0; i < mask.Length; i++)
                {
                    mask[i] = random.NextDouble() < 0.75;
                }

                DistanceTransform.SquaredDistances(mask, w, h).Should().Equal(BruteForce(mask, w, h), $"trial {trial} ({w}x{h})");
            }
        }

        private static long[] BruteForce(bool[] mask, int w, int h)
        {
            var result = new long[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!mask[(y * w) + x])
                    {
                        continue;
                    }

                    long best = long.MaxValue;
                    for (int by = -1; by <= h; by++)
                    {
                        for (int bx = -1; bx <= w; bx++)
                        {
                            bool background = bx < 0 || by < 0 || bx >= w || by >= h || !mask[(by * w) + bx];
                            if (background)
                            {
                                best = Math.Min(best, ((long)(bx - x) * (bx - x)) + ((long)(by - y) * (by - y)));
                            }
                        }
                    }

                    result[(y * w) + x] = best;
                }
            }

            return result;
        }
    }
}
