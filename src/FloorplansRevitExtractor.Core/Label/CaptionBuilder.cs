using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// The label's caption: the generator's template and type order, from the
    /// real room counts. The generator's "de residência {style}" is left out:
    /// a Revit model has no style and need not be a house.
    /// </summary>
    public static class CaptionBuilder
    {
        private static readonly (string Type, string Singular, string Plural)[] CountedTypes =
        {
            ("bedroom", "quarto", "quartos"),
            ("bathroom", "banheiro", "banheiros"),
            ("living_room", "sala", "salas"),
            ("dining_room", "sala de jantar", "salas de jantar"),
            ("kitchen", "cozinha", "cozinhas"),
            ("laundry", "área de serviço", "áreas de serviço"),
            ("garage", "garagem", "garagens"),
            ("office", "escritório", "escritórios"),
            ("terrace", "varanda", "varandas"),
            ("powder", "lavabo", "lavabos"),
        };

        /// <param name="roomTypes">One entry per room; null for an unmapped name.</param>
        /// <param name="builtAreaM2">Footprint area, or null when it was not measured.</param>
        public static string Build(IEnumerable<string?> roomTypes, double? builtAreaM2)
        {
            Dictionary<string, int> counts = roomTypes
                .Where(t => t != null)
                .GroupBy(t => t!)
                .ToDictionary(g => g.Key, g => g.Count());

            string[] parts = CountedTypes
                .Where(c => counts.ContainsKey(c.Type))
                .Select(c => $"{counts[c.Type]} {(counts[c.Type] == 1 ? c.Singular : c.Plural)}")
                .ToArray();

            string rooms = parts.Length == 0 ? string.Empty : " com " + string.Join(", ", parts);
            string area = builtAreaM2.HasValue
                ? $" área construída {PythonRounding.Round(builtAreaM2.Value, 0).ToString("0", CultureInfo.InvariantCulture)} m²,"
                : string.Empty;
            return $"Planta baixa{rooms};{area} um pavimento.";
        }
    }
}
