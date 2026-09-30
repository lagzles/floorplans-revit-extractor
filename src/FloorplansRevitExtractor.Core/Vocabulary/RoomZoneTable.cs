using System.Collections.Generic;

namespace FloorplansRevitExtractor.Core.Vocabulary
{
    /// <summary>type -> zone, spec section 5.2.</summary>
    public static class RoomZoneTable
    {
        private static readonly IReadOnlyDictionary<string, string> ZoneByType = new Dictionary<string, string>
        {
            ["living_room"] = "social",
            ["dining_room"] = "social",
            ["hall"] = "social",
            ["office"] = "social",
            ["bedroom"] = "private",
            ["bathroom"] = "private",
            ["powder"] = "private",
            ["closet"] = "private",
            ["kitchen"] = "service",
            ["laundry"] = "service",
            ["pantry"] = "service",
            ["garage"] = "service",
            ["parking"] = "service",
            ["corridor"] = "circulation",
            ["stairs"] = "circulation",
            ["void"] = "circulation",
            ["terrace"] = "outdoor",
            ["patio"] = "outdoor",
        };

        public static string GetZone(string type) => ZoneByType[type];
    }
}
