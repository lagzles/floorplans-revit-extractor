using System;
using System.Linq;
using FloorplansRevitExtractor.Core.Raster;
using FluentAssertions;
using Xunit;

namespace FloorplansRevitExtractor.Core.Tests.Raster
{
    public class StructureClassNamesTests
    {
        [Fact]
        public void Names_MatchTheFrozenClassesJsonContract()
        {
            Enum.GetValues(typeof(StructureClass)).Cast<StructureClass>().Select(StructureClassNames.ToName)
                .Should().Equal("background", "room", "wall", "door", "window", "pillar", "stair", "furniture", "swing", "void");
        }

        [Fact]
        public void TryParse_RoundTripsEveryName_AndRejectsOthers()
        {
            foreach (StructureClass cls in Enum.GetValues(typeof(StructureClass)))
            {
                StructureClassNames.TryParse(StructureClassNames.ToName(cls), out StructureClass parsed).Should().BeTrue();
                parsed.Should().Be(cls);
            }

            StructureClassNames.TryParse("Wall", out _).Should().BeFalse("names are case-sensitive, as in classes.json");
            StructureClassNames.TryParse("sofa", out _).Should().BeFalse();
        }

        [Theory]
        [InlineData("stairs", StructureClass.Stair)]
        [InlineData("void", StructureClass.Void)]
        [InlineData("bedroom", StructureClass.Room)]
        [InlineData("patio", StructureClass.Room)]
        [InlineData(null, StructureClass.Room)] // unmapped name: still floor in the mask
        public void ForRoomType_OnlyStairsAndVoidLeaveTheRoomClass(string? roomType, StructureClass expected)
        {
            StructureClassNames.ForRoomType(roomType).Should().Be(expected);
        }
    }
}
