using System.Collections.Generic;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// What the Revit side measures about the building as a whole (spec v1
    /// section 3, section 4.2 for which wall line each one uses). Anything
    /// that cannot be measured is null, never guessed.
    /// </summary>
    public sealed record BuildingMeasures
    {
        /// <summary>Footprint bounding box, measured on the wall axes.</summary>
        public double? Width { get; init; }

        public double? Height { get; init; }

        /// <summary>Footprint area on the wall axes, voids and courtyards removed.</summary>
        public double? BuiltArea { get; init; }

        /// <summary>params.total_area: gross floor area.</summary>
        public required double TotalArea { get; init; }

        /// <summary>Median exterior wall thickness.</summary>
        public double? WallExt { get; init; }

        /// <summary>Median interior wall thickness.</summary>
        public double? WallInt { get; init; }

        public double? WallIntMin { get; init; }

        /// <summary>True when an interior wall is thinner than 0.10 m.</summary>
        public bool? ThinPartitions { get; init; }

        /// <summary>params.building, from the closed vocabulary, or null.</summary>
        public string? BuildingType { get; init; }

        /// <summary>Perimeter on the exterior face, first point not repeated; empty when unknown.</summary>
        public required IReadOnlyList<Point2D> Outline { get; init; }
    }
}
