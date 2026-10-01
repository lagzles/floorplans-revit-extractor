using System;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Exact squared Euclidean distance from each mask pixel to the nearest
    /// background pixel (Felzenszwalb-Huttenlocher, two 1D passes). Pixels
    /// outside the image count as background, matching the generator's
    /// <c>distance_transform_edt(np.pad(mask, 1))</c>.
    /// </summary>
    public static class DistanceTransform
    {
        public static long[] SquaredDistances(bool[] mask, int width, int height)
        {
            if (width <= 0 || height <= 0 || mask.Length != (long)width * height)
            {
                throw new ArgumentException($"Mask of {mask.Length} pixels does not match {width}x{height}.", nameof(mask));
            }

            int paddedWidth = width + 2;
            int paddedHeight = height + 2;
            long infinity = (long)(paddedWidth + paddedHeight) * (paddedWidth + paddedHeight);
            var grid = new long[paddedWidth * paddedHeight];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (mask[(y * width) + x])
                    {
                        grid[((y + 1) * paddedWidth) + x + 1] = infinity;
                    }
                }
            }

            var envelope = new LowerEnvelope(Math.Max(paddedWidth, paddedHeight));
            for (int x = 0; x < paddedWidth; x++)
            {
                envelope.Transform(grid, x, paddedWidth, paddedHeight);
            }

            for (int y = 0; y < paddedHeight; y++)
            {
                envelope.Transform(grid, y * paddedWidth, 1, paddedWidth);
            }

            var result = new long[width * height];
            for (int y = 0; y < height; y++)
            {
                Array.Copy(grid, ((y + 1) * paddedWidth) + 1, result, y * width, width);
            }

            return result;
        }

        /// <summary>1D squared distance transform over a strided line, reusing its buffers.</summary>
        private sealed class LowerEnvelope
        {
            private readonly long[] _values;
            private readonly int[] _vertices;
            private readonly double[] _boundaries;

            internal LowerEnvelope(int maxLength)
            {
                _values = new long[maxLength];
                _vertices = new int[maxLength];
                _boundaries = new double[maxLength + 1];
            }

            internal void Transform(long[] grid, int start, int stride, int length)
            {
                for (int i = 0; i < length; i++)
                {
                    _values[i] = grid[start + (i * stride)];
                }

                int k = 0;
                _vertices[0] = 0;
                _boundaries[0] = double.NegativeInfinity;
                _boundaries[1] = double.PositiveInfinity;
                for (int q = 1; q < length; q++)
                {
                    double s = Intersection(q, _vertices[k]);
                    while (s <= _boundaries[k])
                    {
                        k--;
                        s = Intersection(q, _vertices[k]);
                    }

                    k++;
                    _vertices[k] = q;
                    _boundaries[k] = s;
                    _boundaries[k + 1] = double.PositiveInfinity;
                }

                k = 0;
                for (int q = 0; q < length; q++)
                {
                    while (_boundaries[k + 1] < q)
                    {
                        k++;
                    }

                    long offset = q - _vertices[k];
                    grid[start + (q * stride)] = (offset * offset) + _values[_vertices[k]];
                }
            }

            private double Intersection(int q, int p) =>
                ((_values[q] + ((long)q * q)) - (_values[p] + ((long)p * p))) / (2.0 * (q - p));
        }
    }
}
