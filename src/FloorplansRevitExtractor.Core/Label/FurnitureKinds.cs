using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Furniture family and type names to the closed vocabulary of 24 kinds
    /// (spec v1 section 5.4), English and Portuguese, without case or
    /// accents. Keywords are tried in a declared order, the specific before
    /// the generic ("ARMCHAIR" before "CHAIR", "ROUND" before "DINING"). A family that matches nothing returns
    /// null: v1 drops it from furniture[] and logs it; there is no "Other".
    /// The mask paints every furniture element anyway (spec v2).
    /// </summary>
    public static class FurnitureKinds
    {
        /// <summary>First match wins, so the more specific keywords come first.</summary>
        private static readonly (string Keyword, string Kind)[] Keywords =
        {
            // Specific before generic: a vanity sink before "round", a base cabinet before "kitchen".
            ("VANITY", "BathSink"), ("LAVATORY", "BathSink"), ("LAVATORIO", "BathSink"), ("CUBA", "BathSink"),
            ("CASEWORK", "Counter"), ("CABINET", "Counter"), ("COUNTER", "Counter"), ("BANCADA", "Counter"), ("BALCAO", "Counter"),
            ("WATER HEATER", "Tank"), ("AQUECEDOR", "Tank"), ("CAIXA D AGUA", "Tank"), ("RESERVATORIO", "Tank"), ("BOILER", "Tank"), ("TANK", "Tank"),
            ("ARMCHAIR", "Armchair"), ("LOUNGE CHAIR", "Armchair"), ("POLTRONA", "Armchair"),
            ("NIGHT STAND", "Nightstand"), ("NIGHTSTAND", "Nightstand"), ("CRIADO MUDO", "Nightstand"), ("MESA DE CABECEIRA", "Nightstand"),
            ("COFFEE TABLE", "CoffeeTable"), ("MESA DE CENTRO", "CoffeeTable"),
            ("ROUND", "RoundTable"), ("REDONDA", "RoundTable"),
            ("KITCHEN", "Sink"), ("COZINHA", "Sink"), ("SINK", "Sink"), ("PIA", "Sink"), ("TANQUE", "Sink"),
            ("TOILET", "Toilet"), ("WATER CLOSET", "Toilet"), ("WC", "Toilet"), ("BACIA", "Toilet"), ("VASO SANITARIO", "Toilet"), ("URINAL", "Toilet"), ("MICTORIO", "Toilet"),
            ("BATHTUB", "Bathtub"), ("TUB", "Bathtub"), ("BANHEIRA", "Bathtub"),
            ("SHOWER", "Shower"), ("CHUVEIRO", "Shower"), ("BOX", "Shower"),
            ("REFRIGERATOR", "Fridge"), ("FRIDGE", "Fridge"), ("GELADEIRA", "Fridge"), ("REFRIGERADOR", "Fridge"),
            ("RANGE", "Stove"), ("STOVE", "Stove"), ("COOKTOP", "Stove"), ("FOGAO", "Stove"), ("OVEN", "Stove"), ("FORNO", "Stove"),
            ("WASHER", "Washer"), ("WASHING", "Washer"), ("DRYER", "Washer"), ("LAVAR", "Washer"), ("LAVADORA", "Washer"), ("SECADORA", "Washer"),
            ("WARDROBE", "Wardrobe"), ("GUARDA ROUPA", "Wardrobe"), ("ARMARIO", "Wardrobe"), ("CLOSET", "Wardrobe"),
            ("SIDEBOARD", "Sideboard"), ("CREDENZA", "Sideboard"), ("BUFFET", "Sideboard"), ("APARADOR", "Sideboard"), ("DRESSER", "Sideboard"), ("COMODA", "Sideboard"),
            ("SHELVING", "Shelf"), ("SHELF", "Shelf"), ("BOOKCASE", "Shelf"), ("ESTANTE", "Shelf"), ("PRATELEIRA", "Shelf"),
            ("TELEVISION", "TV"), ("TELEVISAO", "TV"), ("TV", "TV"),
            ("DESK", "Desk"), ("ESCRIVANINHA", "Desk"), ("WORKSTATION", "Desk"),
            ("SOFA", "Sofa"), ("COUCH", "Sofa"),
            ("BED", "Bed"), ("CAMA", "Bed"),
            ("BENCH", "Bench"), ("BANCO", "Bench"),
            ("CHAIR", "Chair"), ("CADEIRA", "Chair"), ("STOOL", "Chair"), ("BANQUETA", "Chair"),
            ("DINING", "DiningTable"), ("TABLE", "DiningTable"), ("MESA", "DiningTable"),
        };

        public static string? Map(string familyName, string typeName)
        {
            string names = Normalize(familyName + " " + typeName);
            foreach ((string keyword, string kind) in Keywords)
            {
                if (names.Contains(" " + keyword + " ") || names.Contains(" " + keyword + "S ") || IsPrefixWord(names, keyword))
                {
                    return kind;
                }
            }

            return null;
        }

        /// <summary>Compound words like "Bathtub" or "Nightstand" written as one token still match their keyword.</summary>
        private static bool IsPrefixWord(string names, string keyword) =>
            keyword.Length >= 4 && names.Split(' ').Any(word => word.StartsWith(keyword, StringComparison.Ordinal));

        private static string Normalize(string text)
        {
            string decomposed = (text ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
                }
            }

            return " " + string.Join(" ", builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";
        }
    }
}
