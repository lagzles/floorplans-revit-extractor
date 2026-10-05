using System.Collections.Generic;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Raster
{
    /// <summary>
    /// The class names used by classes.json and the geom/ contract (cls), and
    /// the room-type rule for which mask class a Room is painted as.
    /// </summary>
    public static class StructureClassNames
    {
        private static readonly IReadOnlyDictionary<StructureClass, string> NameByClass = new Dictionary<StructureClass, string>
        {
            [StructureClass.Background] = "background",
            [StructureClass.Room] = "room",
            [StructureClass.Wall] = "wall",
            [StructureClass.Door] = "door",
            [StructureClass.Window] = "window",
            [StructureClass.Pillar] = "pillar",
            [StructureClass.Stair] = "stair",
            [StructureClass.Furniture] = "furniture",
            [StructureClass.Swing] = "swing",
            [StructureClass.Void] = "void",
            [StructureClass.Grid] = "grid",
            [StructureClass.Dimension] = "dimension",
            [StructureClass.Passage] = "passage",
            [StructureClass.Text] = "text",
            [StructureClass.LowWall] = "low_wall",
            [StructureClass.CurtainWall] = "curtain_wall",
            [StructureClass.Fixture] = "fixture",
        };

        private static readonly IReadOnlyDictionary<string, StructureClass> ClassByName =
            NameByClass.ToDictionary(pair => pair.Value, pair => pair.Key);

        public static string ToName(StructureClass cls) => NameByClass[cls];

        public static bool TryParse(string name, out StructureClass cls) => ClassByName.TryGetValue(name, out cls);

        /// <summary>
        /// Spec v4: every Room is painted 1 room, except type stairs (6), void
        /// (9) and patio (0: open to the sky, the generator leaves it
        /// background, so the room is not painted at all). An unmapped name is
        /// still floor in the mask; the label is what flags it.
        /// </summary>
        public static StructureClass ForRoomType(string? roomType) => roomType switch
        {
            "stairs" => StructureClass.Stair,
            "void" => StructureClass.Void,
            "patio" => StructureClass.Background,
            _ => StructureClass.Room,
        };
    }
}
