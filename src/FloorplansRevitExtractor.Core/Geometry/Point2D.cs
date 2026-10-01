namespace FloorplansRevitExtractor.Core.Geometry
{
    public readonly record struct Point2D(double X, double Y)
    {
        public bool IsFinite => !double.IsNaN(X) && !double.IsInfinity(X) && !double.IsNaN(Y) && !double.IsInfinity(Y);
    }
}
