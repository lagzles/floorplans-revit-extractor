using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// One polygon with optional holes, in pixel coordinates (x right, y down).
    /// Rings are open (first point not repeated) and may wind either way.
    /// </summary>
    public sealed record PolygonShape(IReadOnlyList<Point2D> Outer, IReadOnlyList<IReadOnlyList<Point2D>> Holes);
}
