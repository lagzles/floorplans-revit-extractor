using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Vocabulary
{
    /// <summary>type -> class_id, spec section 5.1 (18 closed types).</summary>
    public static class RoomTypeCatalog
    {
        private static readonly IReadOnlyDictionary<string, int> ClassIdByType = new Dictionary<string, int>
        {
            ["living_room"] = 1,
            ["dining_room"] = 2,
            ["kitchen"] = 3,
            ["laundry"] = 4,
            ["garage"] = 5,
            ["bedroom"] = 6,
            ["bathroom"] = 7,
            ["powder"] = 8,
            ["closet"] = 9,
            ["pantry"] = 10,
            ["office"] = 11,
            ["corridor"] = 12,
            ["hall"] = 13,
            ["terrace"] = 14,
            ["stairs"] = 15,
            ["void"] = 16,
            ["parking"] = 17,
            ["patio"] = 18,
        };

        public static int GetClassId(string type) => ClassIdByType[type];
    }
}
