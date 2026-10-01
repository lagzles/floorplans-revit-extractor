using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// One entry of the label's furniture[] (spec v1 section 3.6). The kind
    /// vocabulary governs this list only: the mask paints every furniture
    /// element as class 7, mapped or not (spec v2).
    /// </summary>
    public sealed record FurnitureRecord
    {
        /// <param name="width">Size along the family's local X axis.</param>
        /// <param name="depth">Size along the family's local Y axis.</param>
        public FurnitureRecord(int id, string kind, int? room, Point2D position, double rotation, double width, double depth)
        {
            Id = PlacementGuards.RequireId(id);
            Kind = LabelVocabularies.RequireFurnitureKind(kind);
            Room = room;
            Position = PlacementGuards.RequireFinite(position, "Furniture position");
            Rotation = PlacementGuards.NormalizeDegrees(rotation);
            Width = PlacementGuards.RequirePositive(width, nameof(width));
            Depth = PlacementGuards.RequirePositive(depth, nameof(depth));
        }

        public int Id { get; }

        public string Kind { get; }

        public int? Room { get; }

        public Point2D Position { get; }

        public double Rotation { get; }

        public double Width { get; }

        public double Depth { get; }
    }
}
