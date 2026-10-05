using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FloorplansRevitExtractor.Core.Vocabulary
{
    /// <summary>
    /// Closed vocabulary mapping (spec section 5.1). A room name that matches
    /// nothing must never fall back to "office" - the caller is responsible
    /// for logging unmapped names instead.
    /// </summary>
    public static class RoomTypeVocabulary
    {
        private static readonly (string Keyword, string Type)[] KeywordsByDescendingLength = BuildKeywordTable()
            .OrderByDescending(entry => entry.Keyword.Length)
            .ToArray();

        public static string Normalize(string? rawName)
        {
            string trimmed = (rawName ?? string.Empty).Trim().ToUpperInvariant();
            string withoutDiacritics = RemoveDiacritics(trimmed);
            string collapsedSpaces = CollapseSpaces(withoutDiacritics);
            string withoutTrailingNumber = Regex.Replace(collapsedSpaces, @"[\s\-\./]*\d+\s*$", string.Empty);
            return CollapseSpaces(withoutTrailingNumber);
        }

        public static bool TryMapType(string normalizedName, out string? type)
        {
            foreach ((string keyword, string mappedType) in KeywordsByDescendingLength)
            {
                if (normalizedName.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    type = mappedType;
                    return true;
                }
            }

            type = null;
            return false;
        }

        private static string CollapseSpaces(string text) => Regex.Replace(text, @"\s+", " ").Trim();

        private static string RemoveDiacritics(string text)
        {
            string decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(c);
                }
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static (string Keyword, string Type)[] BuildKeywordTable() => new[]
        {
            ("QUARTO", "bedroom"),
            ("DORMITORIO", "bedroom"),
            ("SUITE", "bedroom"),
            ("BANHEIRO", "bathroom"),
            ("BWC", "bathroom"),
            ("WC", "bathroom"),
            ("SANITARIO", "bathroom"),
            ("LAVABO", "powder"),
            ("COZINHA", "kitchen"),
            ("COPA", "kitchen"),
            ("AREA DE SERVICO", "laundry"),
            ("LAVANDERIA", "laundry"),
            ("SERVICO", "laundry"),
            ("SALA DE ESTAR", "living_room"),
            ("ESTAR", "living_room"),
            ("LIVING", "living_room"),
            ("SALA", "living_room"),
            ("JANTAR", "dining_room"),
            ("GARAGEM", "garage"),
            ("ESTACIONAMENTO", "parking"),
            ("VAGA", "parking"),
            ("CIRCULACAO", "corridor"),
            ("CORREDOR", "corridor"),
            ("HALL", "hall"),
            ("SAGUAO", "hall"),
            ("RECEPCAO", "hall"),
            ("FOYER", "hall"),
            ("CAIXA DE ESCADA", "stairs"),
            ("ESCADA", "stairs"),
            ("VARANDA", "terrace"),
            ("TERRACO", "terrace"),
            ("SACADA", "terrace"),
            ("PATIO", "patio"),
            ("JARDIM", "patio"),
            ("DECK", "patio"),
            ("DESPENSA", "pantry"),
            ("ADEGA", "pantry"),
            ("CLOSET", "closet"),
            ("ROUPARIA", "closet"),
            ("ESCRITORIO", "office"),
            ("SALA COMERCIAL", "office"),
            ("LOJA", "office"),
            ("CONSULTORIO", "office"),
            ("VAZIO", "void"),
            ("SHAFT", "void"),
            ("POCO", "void"),
            ("DUTO", "void"),
            ("PRUMADA", "void"),

            // English names (Autodesk sample models, foreign offices), same 18 types.
            ("BEDROOM", "bedroom"),
            ("BATHROOM", "bathroom"),
            ("RESTROOM", "bathroom"),
            ("TOILET", "bathroom"),
            ("POWDER", "powder"),
            ("KITCHEN", "kitchen"),
            ("LAUNDRY", "laundry"),
            ("DINING", "dining_room"),
            ("CAFE", "dining_room"),
            ("UTILITY", "laundry"),
            ("STUDIO", "living_room"),
            ("LIVE/WORK", "living_room"),
            ("GARAGE", "garage"),
            ("PARKING", "parking"),
            ("CORRIDOR", "corridor"),
            ("HALLWAY", "corridor"),
            ("LOBBY", "hall"),
            ("STAIR", "stairs"),
            ("OUTDOOR", "terrace"),
            ("BALCONY", "terrace"),
            ("TERRACE", "terrace"),
            ("PORCH", "terrace"),
            ("GARDEN", "patio"),
            ("COURTYARD", "patio"),
            ("PARK", "patio"),
            ("PANTRY", "pantry"),
            ("OFFICE", "office"),
            ("RETAIL", "office"),
            ("ELEVATOR", "void"), // spec v2: elevator shaft is void; the cab is not drawn
            ("LIFT", "void"),
        };
    }
}
