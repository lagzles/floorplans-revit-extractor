namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// Semantic mask class ids (spec v2, frozen contract): ids 0-8 never
    /// change because trained checkpoints depend on them. The byte value is
    /// the pixel value written to masks_semantic.
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
    }
}
