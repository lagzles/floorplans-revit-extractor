using System;

namespace FloorplansRevitExtractor.Core.Geometry
{
    public readonly record struct Vector2D(double X, double Y)
    {
        public double Length => Math.Sqrt((X * X) + (Y * Y));

        public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);

        public double Dot(Vector2D other) => (X * other.X) + (Y * other.Y);

        /// <summary>Z component of the 3D cross product; positive when <paramref name="other"/> is counter-clockwise from this.</summary>
        public double Cross(Vector2D other) => (X * other.Y) - (Y * other.X);
    }
}
