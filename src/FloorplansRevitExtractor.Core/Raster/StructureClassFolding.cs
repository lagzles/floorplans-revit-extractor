using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// The generator's LEGACY_PARENT and the two folds of a 17-class mask:
    /// to the 12 classes of spec v3 (each new class becomes its parent) and to
    /// the 10 construction classes (annotation removed). The second fold is
    /// exact only because annotation is painted over background or room alone.
    /// </summary>
    public static class StructureClassFolding
    {
        public static IReadOnlyDictionary<StructureClass, StructureClass> LegacyParent { get; } = new Dictionary<StructureClass, StructureClass>
        {
            [StructureClass.Passage] = StructureClass.Door,
            [StructureClass.Text] = StructureClass.Dimension,
            [StructureClass.LowWall] = StructureClass.Wall,
            [StructureClass.CurtainWall] = StructureClass.Window,
            [StructureClass.Fixture] = StructureClass.Furniture,
        };

        /// <summary>Grid, dimension and text: drawn over the plan, never construction.</summary>
        public static bool IsAnnotation(byte value) =>
            value == (byte)StructureClass.Grid || value == (byte)StructureClass.Dimension || value == (byte)StructureClass.Text;

        /// <summary>to_legacy_structure: 12-16 become their parent; everything else (255 too) is kept.</summary>
        public static byte ToLegacy(byte value) =>
            LegacyParent.TryGetValue((StructureClass)value, out StructureClass parent) ? (byte)parent : value;

        public static byte[] ToLegacy(IReadOnlyList<byte> pixels)
        {
            var folded = new byte[pixels.Count];
            for (int i = 0; i < folded.Length; i++)
            {
                folded[i] = ToLegacy(pixels[i]);
            }

            return folded;
        }

        /// <summary>
        /// strip_annotation on top of the legacy fold: annotation pixels take
        /// the class the construction alone paints there (background or room,
        /// by the paint order), so the result has ids 0-9 only.
        /// </summary>
        public static byte[] StripAnnotation(IReadOnlyList<byte> pixels, IReadOnlyList<byte> constructionOnly)
        {
            var folded = new byte[pixels.Count];
            for (int i = 0; i < folded.Length; i++)
            {
                folded[i] = ToLegacy(IsAnnotation(pixels[i]) ? constructionOnly[i] : pixels[i]);
            }

            return folded;
        }
    }
}
