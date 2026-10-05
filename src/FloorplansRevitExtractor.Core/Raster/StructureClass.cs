namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Semantic mask class ids (spec v4, frozen contract): ids 0-16 never
    /// change because trained checkpoints depend on them; a new class would
    /// be 17. Classes 12-16 each refine an older class (StructureClassFolding),
    /// so a 17-class mask folds exactly to 12 and to 10. The byte value is the
    /// pixel value written to masks_semantic.
    /// </summary>
    public enum StructureClass : byte
    {
        Background = 0,
        Room = 1,
        Wall = 2,
        Door = 3,
        Window = 4,
        Pillar = 5,
        Stair = 6,
        Furniture = 7,
        Swing = 8,
        Void = 9,

        /// <summary>Grid line, bubble circle and (spec v4) the bubble's letter.</summary>
        Grid = 10,

        /// <summary>Linear dimension: line, witness lines, ticks and (spec v4) its value.</summary>
        Dimension = 11,

        /// <summary>Spec v4: an opening with no leaf; folds to door.</summary>
        Passage = 12,

        /// <summary>Spec v4: every letter that is not a dimension value or grid letter; folds to dimension.</summary>
        Text = 13,

        /// <summary>Spec v4: terrace guardrail and walls below the cut plane; folds to wall.</summary>
        LowWall = 14,

        /// <summary>Spec v4: curtain wall, or a window 3.0 m or wider; folds to window.</summary>
        CurtainWall = 15,

        /// <summary>Spec v4: plumbing fixture, casework, fixed equipment; folds to furniture.</summary>
        Fixture = 16,
    }
}
