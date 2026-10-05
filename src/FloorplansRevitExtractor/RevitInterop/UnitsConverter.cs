using Autodesk.Revit.DB;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.RevitInterop
{
    /// <summary>
    /// The single point of Revit internal-units conversion (spec section
    /// 4.1) - no `* 0.3048` should ever appear anywhere else in this project.
    /// </summary>
    internal static class UnitsConverter
    {
        internal static double ToMeters(double feet) =>
            UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Meters);

        internal static double ToFeet(double meters) =>
            UnitUtils.ConvertToInternalUnits(meters, UnitTypeId.Meters);

        internal static double SquareFeetToSquareMeters(double squareFeet) =>
            UnitUtils.ConvertFromInternalUnits(squareFeet, UnitTypeId.SquareMeters);

        internal static Point2D ToMetersPoint(XYZ point) =>
            new Point2D(ToMeters(point.X), ToMeters(point.Y));
    }
}
