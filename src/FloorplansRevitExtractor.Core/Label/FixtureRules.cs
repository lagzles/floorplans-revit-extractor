using System.Collections.Generic;
using System.Linq;
using FloorplansRevitExtractor.Core.Raster;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>Where a furniture-like element came from in Revit.</summary>
    public enum FurnitureSource
    {
        Furniture,
        Casework,
        Plumbing,
        SpecialtyEquipment,
        GenericModel,
    }

    /// <summary>
    /// Spec v4 section 4.4, after the generator's FURNITURE_MASK_CLASS: fixed
    /// or with a water point is 16 fixture, loose is 7 furniture, including
    /// standing appliances. The kind decides first, then the category, then
    /// the family name; equipment with no rule is fixed when wall-hosted.
    /// </summary>
    public static class FixtureRules
    {
        /// <summary>Kinds that are fixed or plumbed in the generator.</summary>
        private static readonly HashSet<string> FixtureKinds = new HashSet<string>
        {
            "BathSink", "Bathtub", "Counter", "Shower", "Sink", "Tank", "Toilet",
        };

        /// <summary>Standing appliances: furniture in the generator.</summary>
        private static readonly HashSet<string> LooseKinds = new HashSet<string> { "Fridge", "Stove", "Washer" };

        /// <summary>Family name words for fixed things outside the kinds (toilet partitions, grab bars, built-ins).</summary>
        private static readonly string[] FixedWords =
        {
            "PARTITION", "DIVISORIA", "GRAB BAR", "BARRA DE APOIO", "BUILT IN", "EMBUTID", "BANCADA", "COUNTERTOP",
            "LAVATORIO", "VASO SANITARIO", "MICTORIO", "URINAL", "BEBEDOURO", "DRINKING FOUNTAIN",
        };

        /// <summary>Family name words for loose equipment.</summary>
        private static readonly string[] LooseWords =
        {
            "CART", "CARRINHO", "TROLLEY", "FOGAO", "GELADEIRA", "REFRIGERATOR", "LAVADORA", "WASHER", "DRYER", "SECADORA",
        };

        /// <returns>Furniture or Fixture; null for a generic model that is neither (not painted).</returns>
        public static StructureClass? MaskClass(FurnitureSource source, string? kind, string familyAndTypeName, bool wallHosted)
        {
            string names = (familyAndTypeName ?? string.Empty).ToUpperInvariant().Replace('-', ' ').Replace('_', ' ');
            if (kind != null && LooseKinds.Contains(kind))
            {
                return StructureClass.Furniture;
            }

            if ((kind != null && FixtureKinds.Contains(kind)) || source == FurnitureSource.Casework || source == FurnitureSource.Plumbing)
            {
                return StructureClass.Fixture;
            }

            if (FixedWords.Any(names.Contains))
            {
                return StructureClass.Fixture;
            }

            switch (source)
            {
                case FurnitureSource.GenericModel:
                    return null;
                case FurnitureSource.SpecialtyEquipment:
                    return LooseWords.Any(names.Contains) || !wallHosted ? StructureClass.Furniture : StructureClass.Fixture;
                default:
                    return StructureClass.Furniture;
            }
        }
    }
}
