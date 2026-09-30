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
