using FloorplansRevitExtractor.Core.Label;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class FurnitureKindsTests
    {
        [Theory]
        [InlineData("Bed-Standard", "Queen", "Bed")]
        [InlineData("Cama Casal", "138x188", "Bed")]
        [InlineData("Sofa-Pensi", "2 Seat", "Sofa")]
        [InlineData("Sofá 3 Lugares", "x", "Sofa")]
        [InlineData("Chair-Breuer", "x", "Chair")]
        [InlineData("Armchair-Club", "x", "Armchair")] // ARMCHAIR wins over CHAIR
        [InlineData("Poltrona", "x", "Armchair")]
        [InlineData("Table-Dining Round w Chairs", "x", "RoundTable")]
        [InlineData("Table-Dining", "1800x900", "DiningTable")]
        [InlineData("Mesa de Centro", "x", "CoffeeTable")]
        [InlineData("Coffee Table", "x", "CoffeeTable")]
        [InlineData("Desk", "1525 x 762mm", "Desk")]
        [InlineData("Toilet-Commercial-Wall", "x", "Toilet")]
        [InlineData("Bacia Sanitária", "x", "Toilet")]
        [InlineData("Sink Vanity-Round", "x", "BathSink")]
        [InlineData("Lavatório", "x", "BathSink")]
        [InlineData("Sink-Kitchen-Double", "x", "Sink")]
        [InlineData("Pia Cozinha", "x", "Sink")]
        [InlineData("Tub-Rectangular", "x", "Bathtub")]
        [InlineData("Banheira", "x", "Bathtub")]
        [InlineData("Shower Stall", "x", "Shower")]
        [InlineData("Refrigerator", "x", "Fridge")]
        [InlineData("Geladeira", "x", "Fridge")]
        [InlineData("Range", "30\"", "Stove")]
        [InlineData("Fogão 4 Bocas", "x", "Stove")]
        [InlineData("Washer", "x", "Washer")]
        [InlineData("Máquina de Lavar", "x", "Washer")]
        [InlineData("Wardrobe", "x", "Wardrobe")]
        [InlineData("Guarda-Roupa", "x", "Wardrobe")]
        [InlineData("Night Stand", "x", "Nightstand")]
        [InlineData("Criado-Mudo", "x", "Nightstand")]
        [InlineData("Shelving", "x", "Shelf")]
        [InlineData("Estante", "x", "Shelf")]
        [InlineData("Counter Top", "x", "Counter")]
        [InlineData("Bancada", "x", "Counter")]
        [InlineData("Credenza", "x", "Sideboard")]
        [InlineData("Bench", "x", "Bench")]
        [InlineData("Television", "x", "TV")]
        [InlineData("Water Heater", "x", "Tank")]
        public void Map_FamilyAndTypeNames_ToTheClosedVocabulary(string family, string type, string expected)
        {
            FurnitureKinds.Map(family, type).Should().Be(expected);
        }

        [Theory]
        [InlineData("Piano-Grand", "x")]
        [InlineData("Plant", "Ficus")]
        [InlineData("Treadmill", "x")]
        public void Map_AnythingElse_IsNull_NeverOther(string family, string type)
        {
            // v1: a family outside the 24 kinds is dropped from furniture[] and logged; there is no "Other".
            FurnitureKinds.Map(family, type).Should().BeNull();
        }

        [Fact]
        public void Map_EveryResult_IsInTheClosedVocabulary()
        {
            string[] samples = { "Bed", "Sofa", "Chair", "Armchair", "Desk", "Toilet", "Sink", "Lavatório", "Tub", "Shower", "Refrigerator", "Range", "Washer", "Wardrobe", "Night Stand", "Shelf", "Counter", "Credenza", "Bench", "Television", "Water Heater", "Coffee Table", "Round Table", "Dining Table" };

            foreach (string sample in samples)
            {
                LabelVocabularies.FurnitureKinds.Should().Contain(FurnitureKinds.Map(sample, string.Empty), sample);
            }
        }
    }
}
