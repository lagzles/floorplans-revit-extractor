using System;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// One entry of the label's doors[] (spec v1 section 3.4), with the
    /// generator's semantics where they differ from the spec text: room_a is
    /// always a room, room_b is null when the door opens to the outside, and
    /// exterior means the door sits in an exterior wall (a door to a patio
    /// room has both rooms and exterior true). Positions are view-frame
    /// meters on the wall axis; rotation is the wall direction in degrees,
    /// normalized to [0, 360).
    /// </summary>
    public sealed record DoorRecord
    {
        /// <param name="roomA">A room the door opens from. When Revit gives only ToRoom, pass it here.</param>
        /// <param name="roomB">The room on the other side, or null when it opens to the outside.</param>
        /// <param name="hinge">Hinge point, single and double doors only.</param>
        /// <param name="swingInto">room_a or room_b, or null when undefined.</param>
        /// <param name="exterior">True when the host wall is an exterior wall.</param>
        public DoorRecord(
            int id,
            int roomA,
            int? roomB,
            string kind,
            Point2D position,
            double rotation,
            double width,
            Point2D? hinge,
            int? swingInto,
            bool exterior)
        {
            Kind = LabelVocabularies.RequireDoorKind(kind);
            if (hinge.HasValue && !LabelVocabularies.HasHinge(kind))
            {
                throw new ArgumentException($"A {kind} door has no hinge; only single and double doors do.", nameof(hinge));
            }

            if (roomB == roomA)
            {
                throw new ArgumentException($"Door {id} has room {roomA} on both sides.", nameof(roomB));
            }

            if (swingInto.HasValue && swingInto != roomA && swingInto != roomB)
            {
                throw new ArgumentException(
                    $"Door {id} swings into room {swingInto}, which is neither of its rooms ({roomA}, {roomB?.ToString() ?? "outside"}).",
                    nameof(swingInto));
            }

            Id = PlacementGuards.RequireId(id);
            RoomA = roomA;
            RoomB = roomB;
            Position = PlacementGuards.RequireFinite(position, "Door position");
            Rotation = PlacementGuards.NormalizeDegrees(rotation);
            Width = PlacementGuards.RequirePositive(width, nameof(width));
            Hinge = hinge.HasValue ? PlacementGuards.RequireFinite(hinge.Value, "Door hinge") : (Point2D?)null;
            SwingInto = swingInto;
            Exterior = exterior;
        }

        public int Id { get; }

        public int RoomA { get; }

        public int? RoomB { get; }

        public string Kind { get; }

        public Point2D Position { get; }

        public double Rotation { get; }

        public double Width { get; }

        public Point2D? Hinge { get; }

        public int? SwingInto { get; }

        public bool Exterior { get; }
    }
}
