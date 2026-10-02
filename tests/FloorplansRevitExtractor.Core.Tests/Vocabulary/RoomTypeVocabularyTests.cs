using FloorplansRevitExtractor.Core.Vocabulary;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Vocabulary
{
    public class RoomTypeVocabularyTests
    {
        [Theory]
        [InlineData("Sanitário 02", "SANITARIO")]
        [InlineData("SALA   DE   ESTAR", "SALA DE ESTAR")]
        [InlineData("Quarto-01", "QUARTO")]
        [InlineData("Circulação", "CIRCULACAO")]
        public void Normalize_UppercasesStripsAccentsAndTrailingNumbering(string raw, string expected)
        {
            RoomTypeVocabulary.Normalize(raw).Should().Be(expected);
        }

        [Theory]
        [InlineData("QUARTO")]
        [InlineData("DORMITORIO")]
        [InlineData("SUITE")]
        public void TryMapType_MapsBedroomSynonyms(string normalized)
        {
            RoomTypeVocabulary.TryMapType(normalized, out string? type).Should().BeTrue();
            type.Should().Be("bedroom");
        }

        [Fact]
        public void TryMapType_PrefersLongerMoreSpecificKeyword()
        {
            RoomTypeVocabulary.TryMapType("SALA COMERCIAL", out string? type).Should().BeTrue();
            type.Should().Be("office");
        }

        [Fact]
        public void TryMapType_DoesNotMatchLoosePrefixOverSpecificPhrase()
        {
            RoomTypeVocabulary.TryMapType("SALA DE ESTAR", out string? type).Should().BeTrue();
            type.Should().Be("living_room");
        }

        // English names (Autodesk sample models, foreign offices) map to the same 18 types.
        [Theory]
        [InlineData("Stair S1", "stairs")]
        [InlineData("Elevator E1", "void")] // spec v2: elevator shaft is void, the cab is not drawn
        [InlineData("Lift 2", "void")]
        [InlineData("Shaft", "void")]
        [InlineData("Residential Lobby 106", "hall")]
        [InlineData("Café Kitchen 102", "kitchen")]
        [InlineData("Commercial/Retail 105", "office")] // as LOJA and SALA COMERCIAL
        [InlineData("Master Bedroom", "bedroom")]
        [InlineData("Restroom 3", "bathroom")]
        [InlineData("Living Room", "living_room")]
        [InlineData("Dining", "dining_room")]
        [InlineData("Laundry", "laundry")]
        [InlineData("Corridor 2", "corridor")]
        [InlineData("Hallway", "corridor")]
        [InlineData("Balcony", "terrace")]
        [InlineData("Pocket Park 104", "patio")]
        [InlineData("Walk-in Closet", "closet")]
        [InlineData("Outdoor Covered Dining 103", "terrace")] // an outdoor covered area, not a dining room
        [InlineData("Parking 2", "parking")] // PARKING wins over PARK
        public void TryMapType_EnglishNames_MapToTheSameTypes(string raw, string expected)
        {
            bool mapped = RoomTypeVocabulary.TryMapType(RoomTypeVocabulary.Normalize(raw), out string? type);

            mapped.Should().BeTrue(raw);
            type.Should().Be(expected);
        }

        [Theory]
        [InlineData("Café 101")]
        [InlineData("Utility 107")]
        public void TryMapType_EnglishNamesWithoutAClearType_StayUnmapped(string raw)
        {
            RoomTypeVocabulary.TryMapType(RoomTypeVocabulary.Normalize(raw), out _).Should().BeFalse(raw);
        }

        [Fact]
        public void TryMapType_UnmappedName_ReturnsFalse_NeverDefaultsToOffice()
        {
            bool mapped = RoomTypeVocabulary.TryMapType("DEPOSITO", out string? type);

            mapped.Should().BeFalse();
            type.Should().BeNull();
        }

        [Fact]
        public void TryMapType_LavaboMapsToPowder_NotBathroom()
        {
            RoomTypeVocabulary.TryMapType("LAVABO", out string? type).Should().BeTrue();
            type.Should().Be("powder");
        }

        [Theory]
        [InlineData("living_room", 1)]
        [InlineData("dining_room", 2)]
        [InlineData("kitchen", 3)]
        [InlineData("laundry", 4)]
        [InlineData("garage", 5)]
        [InlineData("bedroom", 6)]
        [InlineData("bathroom", 7)]
        [InlineData("powder", 8)]
        [InlineData("closet", 9)]
        [InlineData("pantry", 10)]
        [InlineData("office", 11)]
        [InlineData("corridor", 12)]
        [InlineData("hall", 13)]
        [InlineData("terrace", 14)]
        [InlineData("stairs", 15)]
        [InlineData("void", 16)]
        [InlineData("parking", 17)]
        [InlineData("patio", 18)]
        public void GetClassId_MatchesSpecTable(string type, int expectedClassId)
        {
            RoomTypeCatalog.GetClassId(type).Should().Be(expectedClassId);
        }
    }
}
