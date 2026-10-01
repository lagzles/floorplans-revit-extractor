using System;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// View frame (meters, Y up) to image pixels (Y down), spec formula:
    /// p_x = (X - x0) * k, p_y = (y1 - Y) * k, with k = px_per_m.
    /// Image and mask must share this exactly: a 3 px shift on a 6 px wall
    /// cuts the overlap to a third and is invisible when looking at the image.
    /// </summary>
    public sealed record PixelTransform
    {
        /// <summary>
        /// k comes from the width alone, so the height has to agree with it:
        /// a larger gap means another aspect or margin, and the mask would
        /// drift vertically with no error. One pixel allows for rounding.
        /// </summary>
        public const double MeasureHeightTolerancePx = 1.0;

        public PixelTransform(double x0, double y1, double pxPerM, int width, int height)
        {
            if (double.IsNaN(x0) || double.IsInfinity(x0) || double.IsNaN(y1) || double.IsInfinity(y1))
            {
                throw new ArgumentException($"Transform origin must be finite, got x0={x0}, y1={y1}.");
            }

            if (!(pxPerM > 0) || double.IsInfinity(pxPerM))
            {
                throw new ArgumentOutOfRangeException(nameof(pxPerM), pxPerM, "px_per_m must be positive and finite.");
            }

            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width), $"Image size must be positive, got {width}x{height}.");
            }

            X0 = x0;
            Y1 = y1;
            PxPerM = pxPerM;
            Width = width;
            Height = height;
        }

        /// <summary>View X (m) of the image's left edge.</summary>
        public double X0 { get; }

        /// <summary>View Y (m) of the image's top edge.</summary>
        public double Y1 { get; }

        public double PxPerM { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// Builds the transform from the region the image export actually
        /// produced, not the requested crop: Revit may add a margin or round
        /// the size, so k is measured on the exported file (width px / width m).
        /// </summary>
        public static PixelTransform Measure(
            double regionMinX,
            double regionMinY,
            double regionMaxX,
            double regionMaxY,
            int widthPx,
            int heightPx)
        {
            double regionWidth = regionMaxX - regionMinX;
            if (!(regionWidth > 0) || !(regionMaxY > regionMinY))
            {
                throw new ArgumentException(
                    $"Exported region is empty: x [{regionMinX}, {regionMaxX}], y [{regionMinY}, {regionMaxY}].");
            }

            double pxPerM = widthPx / regionWidth;
            double expectedHeightPx = (regionMaxY - regionMinY) * pxPerM;
            if (Math.Abs(expectedHeightPx - heightPx) > MeasureHeightTolerancePx)
            {
                throw new ArgumentException(
                    $"Exported height {heightPx} px disagrees with the width scale: the region is {expectedHeightPx:F1} px tall at {pxPerM:F4} px/m.");
            }

            return new PixelTransform(regionMinX, regionMaxY, pxPerM, widthPx, heightPx);
        }

        public Point2D ToPixel(Point2D viewM) => new Point2D((viewM.X - X0) * PxPerM, (Y1 - viewM.Y) * PxPerM);

        public Point2D ToView(Point2D pixel) => new Point2D(X0 + (pixel.X / PxPerM), Y1 - (pixel.Y / PxPerM));
    }
}
