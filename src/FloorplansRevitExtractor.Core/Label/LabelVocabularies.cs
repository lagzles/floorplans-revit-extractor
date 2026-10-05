using System;
using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Closed vocabularies of the label (spec v1 sections 3.4 to 3.6 and 5.4,
    /// checked against the generator's models.py). Case-sensitive, exactly
    /// these values: anything else is rejected, never mapped to a default.
    /// </summary>
    public static class LabelVocabularies
    {
        private static readonly HashSet<string> DoorKindSet = Set("single", "double", "sliding", "passage", "gate");

        /// <summary>The generator gives a hinge to these door kinds only.</summary>
        private static readonly HashSet<string> HingedDoorKindSet = Set("single", "double");

        /// <summary>Spec v4 adds "curtain": a curtain wall panel or storefront.</summary>
        private static readonly HashSet<string> WindowKindSet = Set("single", "double", "sliding", "high", "curtain");

        private static readonly HashSet<string> FurnitureKindSet = Set(
            "Armchair", "BathSink", "Bathtub", "Bed", "Bench", "Chair", "CoffeeTable", "Counter", "Desk",
            "DiningTable", "Fridge", "Nightstand", "RoundTable", "Shelf", "Shower", "Sideboard", "Sink",
            "Sofa", "Stove", "Tank", "TV", "Toilet", "Wardrobe", "Washer");

        /// <summary>params.building, spec v1 section 5.3.</summary>
        private static readonly HashSet<string> BuildingTypeSet = Set(
            "residential", "apartments", "rowhouse", "duplex", "studio", "commercial", "retail", "mall",
            "restaurant", "hotel", "hospital", "school", "assembly", "industrial", "parking", "basement",
            "courtyard", "terminal");

        public static IReadOnlyCollection<string> BuildingTypes => BuildingTypeSet;

        public static IReadOnlyCollection<string> DoorKinds => DoorKindSet;

        public static IReadOnlyCollection<string> WindowKinds => WindowKindSet;

        public static IReadOnlyCollection<string> FurnitureKinds => FurnitureKindSet;

        public static bool HasHinge(string doorKind) => HingedDoorKindSet.Contains(doorKind);

        internal static string RequireBuildingType(string type) => Require(type, BuildingTypeSet, "Building");

        internal static string RequireDoorKind(string kind) => Require(kind, DoorKindSet, "Door");

        internal static string RequireWindowKind(string kind) => Require(kind, WindowKindSet, "Window");

        internal static string RequireFurnitureKind(string kind) => Require(kind, FurnitureKindSet, "Furniture");

        private static string Require(string kind, HashSet<string> vocabulary, string what) =>
            kind != null && vocabulary.Contains(kind)
                ? kind
                : throw new ArgumentException($"{what} kind \"{kind}\" is not in the closed vocabulary: {string.Join(", ", vocabulary)}.");

        private static HashSet<string> Set(params string[] values) => new HashSet<string>(values, StringComparer.Ordinal);
    }
}
