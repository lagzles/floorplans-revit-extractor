using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using FloorplansRevitExtractor.UI;

namespace FloorplansRevitExtractor.RevitInterop
{
    internal static class LevelSelectionService
    {
        internal static IReadOnlyList<(Level Level, int RoomCount)> GetLevelsWithRooms(Document doc)
        {
            IReadOnlyDictionary<ElementId, int> countsByLevelId = RoomCollector.CountRoomsPerLevel(doc);

            var result = new List<(Level Level, int RoomCount)>();
            foreach (KeyValuePair<ElementId, int> entry in countsByLevelId)
            {
                if (doc.GetElement(entry.Key) is Level level)
                {
                    result.Add((level, entry.Value));
                }
            }

            return result.OrderBy(l => l.Level.Elevation).ToList();
        }

        /// <summary>Returns null when there are no rooms at all, or the user cancels the picker.</summary>
        internal static Level Resolve(Document doc)
        {
            IReadOnlyList<(Level Level, int RoomCount)> candidates = GetLevelsWithRooms(doc);

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0].Level;
            }

            var owner = new RevitWindowHandle(Process.GetCurrentProcess().MainWindowHandle);
            using var form = new LevelPickerForm(candidates);
            return form.ShowDialog(owner) == DialogResult.OK ? form.SelectedLevel : null;
        }
    }
}
