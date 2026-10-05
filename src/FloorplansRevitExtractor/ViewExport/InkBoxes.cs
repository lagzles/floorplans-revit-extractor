using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Png;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>
    /// Spec v4 section 4.5: a text box is the loose box Revit reports,
    /// tightened on the exported image to the ink of the letters at the
    /// text's angle, plus 1 px. Works in the view frame (meters) both ways.
    /// </summary>
    internal sealed class InkBoxes
    {
        private readonly RgbImage _image;
        private readonly PixelTransform _transform;

        internal InkBoxes(RgbImage image, PixelTransform transform)
        {
            _image = image;
            _transform = transform;
        }

        /// <param name="centerM">Center of the loose box, view frame (m).</param>
        /// <param name="direction">Reading direction in the view frame (Y up).</param>
        /// <param name="isSearchArea">True when the box is only an area around the text: ink filling it is clutter.</param>
        /// <returns>The tight box in the view frame, or null when the loose box has no letter ink.</returns>
        internal IReadOnlyList<Point2D> Fit(Point2D centerM, Vector2D direction, double halfWidthM, double halfHeightM, bool isSearchArea = false)
        {
            double k = _transform.PxPerM;
            IReadOnlyList<Point2D> pixels = TextBoxFitter.Fit(
                _image,
                _transform.ToPixel(centerM),
                new Vector2D(direction.X, -direction.Y),
                halfWidthM * k,
                halfHeightM * k,
                isSearchArea);
            return pixels?.Select(_transform.ToView).ToArray();
        }

        /// <summary>
        /// The loose box from an annotation's view bounding box, seen along the
        /// text's direction, shrunk by a share of its height on every side (a
        /// tag's frame sits on the bounding box edge and is not text).
        /// </summary>
        internal IReadOnlyList<Point2D> FitBoundingBox(Element element, View view, ViewSetup setup, Vector2D direction, double insetShare)
        {
            BoundingBoxXYZ box = element.get_BoundingBox(view);
            if (box == null)
            {
                return null;
            }

            Point2D[] corners =
            {
                setup.ToFrame(new XYZ(box.Min.X, box.Min.Y, box.Min.Z)),
                setup.ToFrame(new XYZ(box.Max.X, box.Min.Y, box.Min.Z)),
                setup.ToFrame(new XYZ(box.Max.X, box.Max.Y, box.Min.Z)),
                setup.ToFrame(new XYZ(box.Min.X, box.Max.Y, box.Min.Z)),
            };
            var center = new Point2D(corners.Average(c => c.X), corners.Average(c => c.Y));
            double length = direction.Length;
            var u = new Vector2D(direction.X / length, direction.Y / length);
            var v = new Vector2D(-u.Y, u.X);
            double halfWidth = corners.Max(c => new Vector2D(c.X - center.X, c.Y - center.Y).Dot(u));
            double halfHeight = corners.Max(c => new Vector2D(c.X - center.X, c.Y - center.Y).Dot(v));
            double inset = insetShare * 2 * halfHeight;
            return halfWidth - inset <= 0 || halfHeight - inset <= 0
                ? null
                : Fit(center, u, halfWidth - inset, halfHeight - inset);
        }
    }
}
