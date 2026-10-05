using FloorplansRevitExtractor.Core.Label;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Label
{
    public class FixtureRulesTests
    {
        [Theory]
        [InlineData(FurnitureSource.Plumbing, "Toilet", "Toilet-Commercial", false, StructureClass.Fixture)]
        [InlineData(FurnitureSource.Casework, null, "Base Cabinet", false, StructureClass.Fixture)]
        [InlineData(FurnitureSource.Furniture, "Counter", "Counter Top", false, StructureClass.Fixture)]
        [InlineData(FurnitureSource.SpecialtyEquipment, "Fridge", "Refrigerator", false, StructureClass.Furniture)]
        [InlineData(FurnitureSource.SpecialtyEquipment, "Stove", "Range", true, StructureClass.Furniture)]
        [InlineData(FurnitureSource.SpecialtyEquipment, null, "Toilet Partition", false, StructureClass.Fixture)]
        [InlineData(FurnitureSource.SpecialtyEquipment, null, "X-Ray Table", true, StructureClass.Fixture)]
        [InlineData(FurnitureSource.SpecialtyEquipment, null, "Medical Cart", true, StructureClass.Furniture)]
        [InlineData(FurnitureSource.Furniture, "Sofa", "Sofa", false, StructureClass.Furniture)]
        [InlineData(FurnitureSource.GenericModel, null, "Grab Bar", true, StructureClass.Fixture)]
        public void MaskClass_FixedIsFixture_LooseIsFurniture(FurnitureSource source, string? kind, string names, bool hosted, StructureClass expected)
        {
            FixtureRules.MaskClass(source, kind, names, hosted).Should().Be(expected);
        }

        [Fact]
        public void MaskClass_GenericModelWithNoRule_IsNotPainted()
        {
            FixtureRules.MaskClass(FurnitureSource.GenericModel, null, "Planter Box", false).Should().BeNull();
        }
    }
}
