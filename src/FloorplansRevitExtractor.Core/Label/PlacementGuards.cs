using System;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>Shared checks for things placed in the plan: doors, windows, furniture.</summary>
    internal static class PlacementGuards
    {
        private const double FullTurn = 360.0;

        internal static int RequireId(int id) =>
            id >= 0 ? id : throw new ArgumentOutOfRangeException(nameof(id), id, "Ids are non-negative.");

        internal static Point2D RequireFinite(Point2D point, string what) =>
            point.IsFinite ? point : throw new ArgumentException($"{what} must be finite, got {point}.");

        internal static double RequirePositive(double value, string what) =>
            value > 0 && !double.IsInfinity(value)
                ? value
                : throw new ArgumentOutOfRangeException(what, value, $"{what} must be positive and finite.");

        /// <summary>Degrees in [0, 360).</summary>
        internal static double NormalizeDegrees(double degrees)
        {
            if (double.IsNaN(degrees) || double.IsInfinity(degrees))
            {
                throw new ArgumentException($"Rotation must be finite, got {degrees}.");
            }

            double normalized = degrees % FullTurn;
            if (normalized < 0)
            {
                normalized += FullTurn;
            }

            // A tiny negative angle plus 360 rounds to exactly 360.
            return normalized >= FullTurn ? normalized - FullTurn : normalized;
        }
    }
}
