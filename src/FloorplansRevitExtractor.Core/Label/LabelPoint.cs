using System;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// The room's label anchor (center_px), ported from the generator's
    /// room_point: the pixel centroid when it is inside the room with enough
    /// clearance from the walls, otherwise the innermost pixel. An L-shaped
    /// room's centroid can fall outside it; the innermost pixel never does.
    /// </summary>
    public static class LabelPoint
    {
        /// <summary>Minimum wall clearance, as a fraction of the inscribed radius, to keep the centroid.</summary>
        public const double CenterClearance = 0.25;

        /// <param name="mask">The room's pixels, possibly a crop of the image.</param>
        /// <param name="originX">Image x of the crop's left column.</param>
        /// <param name="originY">Image y of the crop's top row.</param>
        /// <returns>The anchor in the crop's coordinates.</returns>
        /// <remarks>
        /// The centroid is rounded in image coordinates, as the generator does:
        /// half to even depends on the offset's parity (crop mean 3.5 rounds to 4,
        /// image mean 310.5 to 310).
        /// </remarks>
        public static (int X, int Y) Find(bool[] mask, int width, int height, int originX = 0, int originY = 0)
        {
            long[] squared = DistanceTransform.SquaredDistances(mask, width, height);

            long sumX = 0;
            long sumY = 0;
            long count = 0;
            int innermost = -1;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                sumX += i % width;
                sumY += i / width;
                count++;

                // Strictly greater keeps the first maximum in row-major order, like np.argmax.
                if (innermost < 0 || squared[i] > squared[innermost])
                {
                    innermost = i;
                }
            }

            if (count == 0)
            {
                throw new ArgumentException("Mask has no pixels.", nameof(mask));
            }

            int cx = PythonRounding.RoundToInt((double)(sumX + ((long)originX * count)) / count) - originX;
            int cy = PythonRounding.RoundToInt((double)(sumY + ((long)originY * count)) / count) - originY;
            bool centroidInside = cx >= 0 && cx < width && cy >= 0 && cy < height && mask[(cy * width) + cx];
            if (centroidInside
                && Math.Sqrt(squared[(cy * width) + cx]) >= CenterClearance * Math.Sqrt(squared[innermost]))
            {
                return (cx, cy);
            }

            return (innermost % width, innermost / width);
        }
    }
}
