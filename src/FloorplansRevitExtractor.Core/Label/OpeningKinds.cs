using System;
using System.Globalization;
using System.Linq;
using System.Text;
using FloorplansRevitExtractor.Core.Geometry;

namespace FloorplansRevitExtractor.Core.Label
{
    /// <summary>
    /// Door and window kinds from the Revit family and type names, by an
    /// explicit keyword table (spec v1 section 3.4): an unmapped door family
    /// falls back to "single" and is reported, never guessed silently.
    /// Names are compared without case or accents, English and Portuguese.
    /// </summary>
    public static class OpeningKinds
    {
        /// <summary>v1: a window whose sill is at 1.50 m or more is "high".</summary>
        public const double HighSillM = 1.50;

        /// <summary>The generator's CURTAIN_WALL_MIN_WIDTH: a window this wide or wider is glass skin (spec v4).</summary>
        public const double CurtainWallMinWidthM = 3.0;

        // First match wins: the more specific kinds come first ("double sliding" is sliding).
        private static readonly (string Kind, string[] Keywords)[] DoorKeywords =
        {
            ("gate", new[] { "OVERHEAD", "GARAGE", "SECTIONAL", "ROLL UP", "ROLLUP", "PORTAO", "BASCULANTE", "ENROLAR" }),
            ("sliding", new[] { "SLIDING", "SLIDER", "POCKET", "BYPASS", "CORRER", "DESLIZANTE" }),
            ("passage", new[] { "OPENING", "CASED", "PASSAGEM", "VAO", "ABERTURA" }),
            ("double", new[] { "DOUBLE", "DUPLA", "DUPLO", "2 FOLHAS", "DUAS FOLHAS", "PAIR" }),
            ("single", new[] { "SINGLE", "SIMPLES", "1 FOLHA", "UMA FOLHA", "GIRO", "FLUSH", "PANEL", "DOOR", "PORTA" }),
        };

        private static readonly (string Kind, string[] Keywords)[] WindowKeywords =
        {
            ("curtain", new[] { "CURTAIN", "STOREFRONT", "PELE DE VIDRO", "FACHADA CORTINA" }),
            ("sliding", new[] { "SLIDING", "SLIDER", "CORRER" }),
            ("double", new[] { "DOUBLE", "DUPLA", "DUPLO", "2 FOLHAS", "DUAS FOLHAS" }),
        };

        public static (string Kind, bool Mapped) DoorKind(string familyName, string typeName)
        {
            string names = Normalize(familyName + " " + typeName);
            foreach ((string kind, string[] keywords) in DoorKeywords)
            {
                if (kind != "single" && keywords.Any(names.Contains))
                {
                    return (kind, true);
                }
            }

            // Plain door families say "single"/"door" in their name; anything else is a guess.
            bool plain = DoorKeywords.Last().Keywords.Any(names.Contains) && !names.Contains("REVOLVING") && !names.Contains("GIRATORIA");
            return ("single", plain);
        }

        public static string WindowKind(string familyName, string typeName, double? sillHeightM)
        {
            string names = Normalize(familyName + " " + typeName);
            string? kind = WindowKeywords.FirstOrDefault(k => k.Keywords.Any(names.Contains)).Kind;
            if (kind == "curtain")
            {
                return kind;
            }

            return sillHeightM >= HighSillM ? "high" : kind ?? "single";
        }

        /// <summary>The generator's is_curtain_wall: kind "curtain", or 3.0 m wide or more.</summary>
        public static bool IsCurtainWall(string windowKind, double widthM) =>
            windowKind == "curtain" || widthM >= CurtainWallMinWidthM;

        /// <summary>
        /// The generator's door_mask_class (spec v4 section 4.2): an opening with
        /// no leaf is 12 passage. A "passage" family is one; a hinged door whose
        /// view geometry draws no leaf or arc is one too. Sliding doors and gates
        /// have a leaf.
        /// </summary>
        public static string DoorKindForMask(string kind, bool drawsLeaf) =>
            kind == "passage" || (LabelVocabularies.HasHinge(kind) && !drawsLeaf) ? "passage" : kind;

        /// <summary>The label's rotation: direction angle in degrees, [0, 360), 0 along +X.</summary>
        public static double RotationDegrees(Vector2D direction)
        {
            double degrees = Math.Atan2(direction.Y, direction.X) * 180.0 / Math.PI;
            return degrees < 0 ? degrees + 360.0 : degrees;
        }

        private static string Normalize(string text)
        {
            string decomposed = (text ?? string.Empty).ToUpperInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (char c in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
                }
            }

            return " " + string.Join(" ", builder.ToString().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) + " ";
        }
    }
}
