using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>One entry of the label's windows[] (spec v1 section 3.5).</summary>
    public sealed record WindowRecord
    {
        /// <param name="room">The room the window serves, or null.</param>
        /// <param name="kind">"high" when the sill is at 1.50 m or more.</param>
        public WindowRecord(int id, int? room, string kind, Point2D position, double rotation, double width)
        {
            Id = PlacementGuards.RequireId(id);
            Room = room;
            Kind = LabelVocabularies.RequireWindowKind(kind);
            Position = PlacementGuards.RequireFinite(position, "Window position");
            Rotation = PlacementGuards.NormalizeDegrees(rotation);
            Width = PlacementGuards.RequirePositive(width, nameof(width));
        }

        public int Id { get; }

        public int? Room { get; }

        public string Kind { get; }

        public Point2D Position { get; }

        public double Rotation { get; }

        public double Width { get; }
    }
}
