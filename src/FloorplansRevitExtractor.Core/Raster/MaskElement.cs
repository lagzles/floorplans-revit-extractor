using System;
using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// One element to paint into the semantic mask, in pixel coordinates:
    /// either filled polygons (walls, rooms, openings...) or stroked
    /// polylines (the door swing).
    /// </summary>
    public sealed class MaskElement
    {
        private MaskElement(
            StructureClass cls,
            IReadOnlyList<PolygonShape> polygons,
            IReadOnlyList<IReadOnlyList<Point2D>> polylines,
            double strokeWidthPixels)
        {
            Class = cls;
            Polygons = polygons;
            Polylines = polylines;
            StrokeWidthPixels = strokeWidthPixels;
        }

        public StructureClass Class { get; }

        public IReadOnlyList<PolygonShape> Polygons { get; }

        public IReadOnlyList<IReadOnlyList<Point2D>> Polylines { get; }

        public double StrokeWidthPixels { get; }

        public static MaskElement Filled(StructureClass cls, IReadOnlyList<PolygonShape> polygons) =>
            new MaskElement(cls, polygons, Array.Empty<IReadOnlyList<Point2D>>(), 0.0);

        public static MaskElement Stroked(
            StructureClass cls,
            IReadOnlyList<IReadOnlyList<Point2D>> polylines,
            double strokeWidthPixels)
        {
            if (strokeWidthPixels <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(strokeWidthPixels), strokeWidthPixels, "Stroke width must be positive.");
            }

            return new MaskElement(cls, Array.Empty<PolygonShape>(), polylines, strokeWidthPixels);
        }
    }
}
