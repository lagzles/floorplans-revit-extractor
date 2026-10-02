using System;

namespace FloorplansRevitExtractor.Core.Geometry
{
    /// <summary>
    /// The view's local frame (spec v2: <c>View.CropBox.Transform</c>), in
    /// meters. Every mask and label coordinate lives in this frame, not in
    /// project or shared coordinates, so walls of a rotated view stay aligned
    /// with the image.
    /// </summary>
    public sealed class ViewFrame
    {
        private const double BasisTolerance = 1e-6;

        /// <param name="originM">Frame origin in model XY, already in meters.</param>
        /// <param name="basisX">Unit X axis of the frame, in model XY.</param>
        /// <param name="basisY">Unit Y axis of the frame, in model XY.</param>
        public ViewFrame(Point2D originM, Vector2D basisX, Vector2D basisY)
        {
            if (!originM.IsFinite)
            {
                throw new ArgumentException($"View origin must be finite, got {originM}.", nameof(originM));
            }

            RequireOrthonormalRightHanded(basisX, basisY);
            OriginM = originM;
            BasisX = basisX;
            BasisY = basisY;
        }

        public Point2D OriginM { get; }

        public Vector2D BasisX { get; }

        public Vector2D BasisY { get; }

        public Point2D ToView(Point2D modelM)
        {
            var offset = new Vector2D(modelM.X - OriginM.X, modelM.Y - OriginM.Y);
            return new Point2D(offset.Dot(BasisX), offset.Dot(BasisY));
        }

        /// <summary>A direction (no origin shift), e.g. a wall's tangent, in the view frame.</summary>
        public Vector2D ToViewDirection(Vector2D modelDirection) =>
            new Vector2D(modelDirection.Dot(BasisX), modelDirection.Dot(BasisY));

        public Point2D ToModel(Point2D viewM) => new Point2D(
            OriginM.X + (viewM.X * BasisX.X) + (viewM.Y * BasisY.X),
            OriginM.Y + (viewM.X * BasisX.Y) + (viewM.Y * BasisY.Y));

        private static void RequireOrthonormalRightHanded(Vector2D basisX, Vector2D basisY)
        {
            if (!basisX.IsFinite || !basisY.IsFinite)
            {
                throw new ArgumentException("View basis has non-finite components.");
            }

            if (Math.Abs(basisX.Length - 1) > BasisTolerance || Math.Abs(basisY.Length - 1) > BasisTolerance)
            {
                throw new ArgumentException($"View basis must be unit length, got |X|={basisX.Length}, |Y|={basisY.Length}.");
            }

            if (Math.Abs(basisX.Dot(basisY)) > BasisTolerance)
            {
                throw new ArgumentException("View basis axes must be perpendicular.");
            }

            // A mirrored frame would flip the plan left-right with no error downstream.
            if (basisX.Cross(basisY) < 0)
            {
                throw new ArgumentException("View basis is mirrored (X x Y points down); plan views must look down with a right-handed frame.");
            }
        }
    }
}
