using System;
using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Model
{
    /// <summary>
    /// One Revit element's contribution to one mask class: filled polygons
    /// (with optional holes) or stroked polylines, never both. The factories
    /// enforce the same rules as the geom/ reader, so anything built in memory
    /// survives a write and read back; rings are copied, never aliased.
    /// </summary>
    public sealed class GeomElement
    {
        public const int MinPolygonPoints = 3;
        public const int MinPolylinePoints = 1;

        private static readonly IReadOnlyList<IReadOnlyList<Point2D>> NoRings = Array.Empty<IReadOnlyList<Point2D>>();

        private GeomElement(
            StructureClass cls,
            long revitId,
            IReadOnlyList<IReadOnlyList<Point2D>> polygons,
            IReadOnlyList<IReadOnlyList<Point2D>> holes,
            IReadOnlyList<IReadOnlyList<Point2D>> polylines,
            double? strokeM)
        {
            if (cls == StructureClass.Background)
            {
                throw new ArgumentException("Background is what is left unpainted; it has no geometry.", nameof(cls));
            }

            Class = cls;
            RevitId = revitId;
            Polygons = polygons;
            Holes = holes;
            Polylines = polylines;
            StrokeM = strokeM;
        }

        public StructureClass Class { get; }

        public long RevitId { get; }

        public IReadOnlyList<IReadOnlyList<Point2D>> Polygons { get; }

        public IReadOnlyList<IReadOnlyList<Point2D>> Holes { get; }

        public IReadOnlyList<IReadOnlyList<Point2D>> Polylines { get; }

        public double? StrokeM { get; }

        public static GeomElement Filled(
            StructureClass cls,
            long revitId,
            IReadOnlyList<IReadOnlyList<Point2D>> polygons,
            IReadOnlyList<IReadOnlyList<Point2D>>? holes = null)
        {
            IReadOnlyList<IReadOnlyList<Point2D>> polygonCopy = CopyRings(polygons, MinPolygonPoints, nameof(polygons));
            if (polygonCopy.Count == 0)
            {
                throw new ArgumentException("A filled element needs at least one polygon.", nameof(polygons));
            }

            IReadOnlyList<IReadOnlyList<Point2D>> holeCopy = holes == null ? NoRings : CopyRings(holes, MinPolygonPoints, nameof(holes));
            return new GeomElement(cls, revitId, polygonCopy, holeCopy, NoRings, null);
        }

        public static GeomElement Stroked(
            StructureClass cls,
            long revitId,
            IReadOnlyList<IReadOnlyList<Point2D>> polylines,
            double strokeM)
        {
            if (!(strokeM > 0) || double.IsInfinity(strokeM))
            {
                throw new ArgumentOutOfRangeException(nameof(strokeM), strokeM, "stroke_m must be positive and finite.");
            }

            IReadOnlyList<IReadOnlyList<Point2D>> polylineCopy = CopyRings(polylines, MinPolylinePoints, nameof(polylines));
            if (polylineCopy.Count == 0)
            {
                throw new ArgumentException("A stroked element needs at least one polyline.", nameof(polylines));
            }

            return new GeomElement(cls, revitId, NoRings, NoRings, polylineCopy, strokeM);
        }

        private static IReadOnlyList<IReadOnlyList<Point2D>> CopyRings(
            IReadOnlyList<IReadOnlyList<Point2D>> rings,
            int minPoints,
            string paramName)
        {
            if (rings == null)
            {
                throw new ArgumentNullException(paramName);
            }

            return rings.Select((ring, i) =>
            {
                Point2D[] copy = ring.ToArray();
                if (copy.Length < minPoints)
                {
                    throw new ArgumentException($"{paramName}[{i}] has {copy.Length} points, needs at least {minPoints}.", paramName);
                }

                if (copy.Any(p => !p.IsFinite))
                {
                    throw new ArgumentException($"{paramName}[{i}] has a non-finite coordinate.", paramName);
                }

                return (IReadOnlyList<Point2D>)copy;
            }).ToArray();
        }
    }
}
