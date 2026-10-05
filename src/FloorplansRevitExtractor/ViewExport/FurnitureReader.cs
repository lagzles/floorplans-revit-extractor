using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using FloorplansRevitExtractor.Core.Geometry;
using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Model;
using FloorplansRevitExtractor.Core.Raster;
using FloorplansRevitExtractor.RevitInterop;

namespace FloorplansRevitExtractor.ViewExport
{
    /// <summary>What the furniture of a view contributes: mask footprints, label records, notes.</summary>
    internal sealed record FurnitureReading(
        IReadOnlyList<GeomElement> Elements,
        IReadOnlyList<FurnitureRecord> Records,
        IReadOnlyList<GeomWarning> Warnings,
        IReadOnlyList<string> Notes);

    /// <summary>
    /// Furniture and fixed equipment (spec v2 categories) within the view
    /// range. In the mask each one is its family box turned with the instance
    /// (the oriented rectangle the generator uses, accepted in phase 1); for a
    /// wall- or face-hosted fixture, whose local Z is horizontal, the box's 8
    /// corners are projected and their hull taken. Every element is painted;
    /// only those whose family maps to the 24 closed kinds enter furniture[].
    /// </summary>
    internal static class FurnitureReader
    {
        /// <summary>
        /// Families in the furniture categories that are not furniture: clearance
        /// zones drawn for accessibility checks, and elevator doors (part of the
        /// shaft wall, not of the room).
        /// </summary>
        private static readonly string[] NotFurniture = { "CLEAR SPACE", "CLEARANCE", "ZONA LIVRE", "AREA LIVRE", "ELEVATOR DOOR", "PORTA ELEVADOR" };

        /// <summary>Local Z this close to vertical: the family stands upright and its base rectangle is its footprint.</summary>
        private const double UprightZ = 0.999;

        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_FurnitureSystems,
            BuiltInCategory.OST_Casework,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_GenericModel,
        };

        internal static FurnitureReading Read(Document doc, ViewSetup setup, IReadOnlyDictionary<long, int> roomIds)
        {
            var elements = new List<GeomElement>();
            var records = new List<FurnitureRecord>();
            var warnings = new List<GeomWarning>();
            var unmapped = new SortedSet<string>(StringComparer.Ordinal);
            int boxOnly = 0;
            var skipped = new SortedSet<string>(StringComparer.Ordinal);
            var fixtures = new SortedSet<string>(StringComparer.Ordinal);
            Phase phase = doc.GetElement(setup.PhaseId) as Phase;

            ElementGuard.Each(ViewElements.Collect(doc, setup, Categories), warnings, item =>
            {
                BoundingBoxXYZ modelBox = item.get_BoundingBox(null);
                if (modelBox == null || modelBox.Min.Z > setup.CutPlaneZFeet || modelBox.Max.Z < setup.BottomZFeet)
                {
                    return;
                }

                FurnitureSource source = SourceOf(item);
                string kind = FurnitureKinds.Map(item.Symbol.FamilyName, item.Symbol.Name);
                StructureClass? cls = FixtureRules.MaskClass(source, kind, item.Symbol.FamilyName + " " + item.Symbol.Name, item.Host != null);
                if (cls == null)
                {
                    return; // a generic model that is neither furniture nor a fixture
                }

                if (!IsPhysicalFurniture(item))
                {
                    skipped.Add(item.Symbol.FamilyName);
                    return;
                }

                (BoundingBoxXYZ local, Transform transform, bool oriented) = LocalBox(item, modelBox);
                boxOnly += oriented ? 0 : 1;
                IReadOnlyList<Point2D> footprint = Footprint(local, transform, setup);
                List<IReadOnlyList<Point2D>> clipped = CutSection.Clip(new[] { footprint }, setup.ModelRegion);
                if (clipped.Count == 0)
                {
                    return;
                }

                elements.Add(GeomElement.Filled(cls.Value, item.Id.Value, clipped).WithLabel(kind, null));
                if (cls == StructureClass.Fixture)
                {
                    fixtures.Add(item.Symbol.FamilyName);
                }

                if (kind == null)
                {
                    unmapped.Add(item.Symbol.FamilyName);
                    return;
                }

                XYZ widthAxis = Math.Abs(transform.BasisX.Z) < Math.Abs(transform.BasisY.Z) ? transform.BasisX : transform.BasisY;
                Vector2D widthView = setup.Frame.ToViewDirection(new Vector2D(widthAxis.X, widthAxis.Y));
                records.Add(new FurnitureRecord(
                    records.Count,
                    kind,
                    RoomOf(item, phase, roomIds),
                    PolygonGeometry.Centroid(footprint),
                    OpeningKinds.RotationDegrees(widthView),
                    UnitsConverter.ToMeters(local.Max.X - local.Min.X),
                    UnitsConverter.ToMeters(local.Max.Y - local.Min.Y)));
            });

            var notes = new List<string>();
            if (unmapped.Count > 0)
            {
                notes.Add("Famílias de móvel fora do vocabulário (na máscara, fora de furniture[]): " + string.Join("; ", unmapped) + ".");
            }

            if (skipped.Count > 0)
            {
                notes.Add("Famílias sem sólido ou que não são móvel, fora da máscara: " + string.Join("; ", skipped) + ".");
            }

            if (fixtures.Count > 0)
            {
                notes.Add("Famílias como louça/equipamento fixo (16): " + string.Join("; ", fixtures) + ".");
            }

            if (boxOnly > 0)
            {
                notes.Add($"{boxOnly} móvel(is) sem geometria de família: pintado(s) pela caixa alinhada ao modelo, maior que o real se girado(s).");
            }

            return new FurnitureReading(elements, records, warnings, notes);
        }

        private static FurnitureSource SourceOf(FamilyInstance item)
        {
            switch ((BuiltInCategory)item.Category.Id.Value)
            {
                case BuiltInCategory.OST_Casework:
                    return FurnitureSource.Casework;
                case BuiltInCategory.OST_PlumbingFixtures:
                    return FurnitureSource.Plumbing;
                case BuiltInCategory.OST_SpecialityEquipment:
                    return FurnitureSource.SpecialtyEquipment;
                case BuiltInCategory.OST_GenericModel:
                    return FurnitureSource.GenericModel;
                default:
                    return FurnitureSource.Furniture;
            }
        }

        /// <summary>
        /// A 2D-only family (a floor drain symbol) has no visible solid and is a
        /// drawing, not an object; a clearance zone has a solid but is not
        /// furniture. Neither is painted.
        /// </summary>
        private static bool IsPhysicalFurniture(FamilyInstance item)
        {
            string names = (item.Symbol.FamilyName + " " + item.Symbol.Name).ToUpperInvariant().Replace('-', ' ').Replace('_', ' ');
            if (NotFurniture.Any(names.Contains))
            {
                return false;
            }

            var options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            return HasSolid(item.get_Geometry(options));
        }

        private static bool HasSolid(GeometryElement geometry) =>
            geometry != null && geometry.Cast<GeometryObject>().Any(obj =>
                (obj is Solid solid && solid.Volume > 0)
                || (obj is GeometryInstance instance && HasSolid(instance.GetInstanceGeometry())));

        private static IReadOnlyList<Point2D> Footprint(BoundingBoxXYZ local, Transform transform, ViewSetup setup)
        {
            bool upright = Math.Abs(transform.BasisZ.Z) > UprightZ;
            IEnumerable<XYZ> corners = upright
                ? new[]
                {
                    new XYZ(local.Min.X, local.Min.Y, local.Min.Z),
                    new XYZ(local.Max.X, local.Min.Y, local.Min.Z),
                    new XYZ(local.Max.X, local.Max.Y, local.Min.Z),
                    new XYZ(local.Min.X, local.Max.Y, local.Min.Z),
                }
                : new[] { local.Min.Z, local.Max.Z }.SelectMany(z => new[]
                {
                    new XYZ(local.Min.X, local.Min.Y, z),
                    new XYZ(local.Max.X, local.Min.Y, z),
                    new XYZ(local.Max.X, local.Max.Y, z),
                    new XYZ(local.Min.X, local.Max.Y, z),
                });

            IReadOnlyList<Point2D> projected = corners.Select(p => setup.ToFrame(transform.OfPoint(p))).ToArray();
            return upright ? projected : ConvexHull.Of(projected);
        }

        /// <summary>
        /// The family's own box and the transform that places it. Without a
        /// symbol instance (geometry cut, joined or in-place), the model
        /// axis-aligned box stands in.
        /// </summary>
        private static (BoundingBoxXYZ Local, Transform Transform, bool Oriented) LocalBox(FamilyInstance item, BoundingBoxXYZ modelBox)
        {
            GeometryElement geometry = item.get_Geometry(new Options { ComputeReferences = false, DetailLevel = ViewDetailLevel.Fine });
            GeometryInstance instance = geometry?.OfType<GeometryInstance>().FirstOrDefault();
            BoundingBoxXYZ local = instance?.GetSymbolGeometry()?.GetBoundingBox();
            return local != null && local.Max.X > local.Min.X && local.Max.Y > local.Min.Y
                ? (local, instance.Transform, true)
                : (modelBox, Transform.Identity, false);
        }

        private static int? RoomOf(FamilyInstance item, Phase phase, IReadOnlyDictionary<long, int> roomIds)
        {
            Room room = phase == null ? item.Room : item.get_Room(phase);
            return room != null && roomIds.TryGetValue(room.Id.Value, out int id) ? id : (int?)null;
        }
    }
}
